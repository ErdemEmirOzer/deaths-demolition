using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>WP-076 B5: one live gel puddle as the registry publishes it (S-07 / S-08 visuals and sound).</summary>
    public readonly struct GelPuddleInfo
    {
        public readonly int Id;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float SurfaceY;
        public readonly float ExpiresAt;
        public readonly int SourceTransaction;

        public GelPuddleInfo(int id, Vector3 center, float radius, float surfaceY, float expiresAt, int sourceTransaction)
        {
            Id = id; Center = center; Radius = radius; SurfaceY = surfaceY; ExpiresAt = expiresAt; SourceTransaction = sourceTransaction;
        }
    }

    /// <summary>
    /// WP-076 B5 · the gel Special's puddles, agreed with S-02 (WP-069 DIRECTION §7b, 24.09). S-02 registers them (the
    /// impact puddle, the death puddle) and configures the rules from its data; the AI (every body, every 2nd frame) and
    /// S-02's PlayerMotor query them with <see cref="TryFind"/>. The cap is a game rule (<see cref="MaxPuddles"/> 12,
    /// tier-independent — a quality tier thins only the VFX): over it the oldest fades out within
    /// <see cref="FadeSeconds"/>. Touching puddles are NOT merged; their remaining lifetimes are equalised to the longest.
    /// "Inside" = XZ distance² ≤ R² and |y − SurfaceY| ≤ <see cref="InsideHeight"/>. Fixed 24-slot storage, 0 allocation.
    /// </summary>
    public static class GelPuddleRegistry
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 B5: vertical band (m) of 'inside a puddle'; slot storage.", true)]
        public const float InsideHeight = 0.6f;
        public const int Capacity = 24;

        public static int MaxPuddles { get; private set; } = 12;
        public static float FadeSeconds { get; private set; } = 0.5f;
        public static float SlipFallSeconds { get; private set; } = 0.9f;

        public static event Action<GelPuddleInfo> PuddleCreated;
        public static event Action<int> PuddleExpired;

        private struct Slot
        {
            public int Id;
            public Vector3 Center;
            public float Radius, SurfaceY, CreatedAt, ExpiresAt;
            public int Source;
            public bool Fading;
        }

        private static readonly Slot[] s_slots = new Slot[Capacity];
        private static int s_count;
        private static int s_nextId;

        /// <summary>Live puddles (fading ones included until they expire).</summary>
        public static int Count { get { Tick(); return s_count; } }

        /// <summary>S-02 calls this once from its weapon data (the defaults are §7b's values).</summary>
        public static void Configure(int maxPuddles = 12, float fadeSeconds = 0.5f, float slipFallSeconds = 0.9f)
        {
            MaxPuddles = Mathf.Clamp(maxPuddles, 1, Capacity);
            FadeSeconds = Mathf.Max(0f, fadeSeconds);
            SlipFallSeconds = Mathf.Max(0.05f, slipFallSeconds);
        }

        public static int Register(Vector3 center, float radius, float surfaceY, float lifetime, int sourceTransaction)
        {
            Tick();
            float now = Time.time;

            // over the cap: the oldest not-yet-fading puddle fades out
            int live = 0;
            for (int i = 0; i < s_count; i++) if (!s_slots[i].Fading) live++;
            while (live >= MaxPuddles)
            {
                int oldest = -1;
                for (int i = 0; i < s_count; i++)
                    if (!s_slots[i].Fading && (oldest < 0 || s_slots[i].Id < s_slots[oldest].Id)) oldest = i;   // ids are monotonic
                if (oldest < 0) break;
                s_slots[oldest].Fading = true;
                s_slots[oldest].ExpiresAt = Mathf.Min(s_slots[oldest].ExpiresAt, now + FadeSeconds);
                live--;
            }
            if (s_count >= Capacity) ExpireAt(IndexOfSoonest());

            var slot = new Slot
            {
                Id = ++s_nextId, Center = center, Radius = Mathf.Max(0.05f, radius), SurfaceY = surfaceY,
                CreatedAt = now, ExpiresAt = now + Mathf.Max(0f, lifetime), Source = sourceTransaction
            };

            // touching puddles are not merged — their remaining lifetimes are equalised to the longest
            float longest = slot.ExpiresAt;
            for (int i = 0; i < s_count; i++)
                if (!s_slots[i].Fading && Touching(in s_slots[i], in slot)) longest = Mathf.Max(longest, s_slots[i].ExpiresAt);
            slot.ExpiresAt = longest;
            for (int i = 0; i < s_count; i++)
                if (!s_slots[i].Fading && Touching(in s_slots[i], in slot)) s_slots[i].ExpiresAt = longest;

            s_slots[s_count++] = slot;
            try { PuddleCreated?.Invoke(new GelPuddleInfo(slot.Id, slot.Center, slot.Radius, slot.SurfaceY, slot.ExpiresAt, slot.Source)); }
            catch (Exception e) { Debug.LogException(e); }
            return slot.Id;
        }

        public static void Expire(int id)
        {
            for (int i = 0; i < s_count; i++) if (s_slots[i].Id == id) { ExpireAt(i); return; }
        }

        /// <summary>The puddle <paramref name="p"/> stands in (the most recent one when several overlap).</summary>
        public static bool TryFind(Vector3 p, out int id)
        {
            Tick();
            id = 0;
            for (int i = 0; i < s_count; i++)
            {
                ref Slot s = ref s_slots[i];
                float dx = p.x - s.Center.x, dz = p.z - s.Center.z;
                if (dx * dx + dz * dz > s.Radius * s.Radius || Mathf.Abs(p.y - s.SurfaceY) > InsideHeight) continue;
                if (s.Id > id) id = s.Id;   // the most recent (ids are monotonic)
            }
            return id != 0;
        }

        /// <summary>Expires the puddles whose time is up (raises <see cref="PuddleExpired"/>). Queries call it; S-07 may too.</summary>
        public static void Tick()
        {
            float now = Time.time;
            for (int i = s_count - 1; i >= 0; i--) if (s_slots[i].ExpiresAt <= now) ExpireAt(i);
        }

        public static bool TryGet(int id, out GelPuddleInfo info)
        {
            for (int i = 0; i < s_count; i++)
                if (s_slots[i].Id == id) { Slot s = s_slots[i]; info = new GelPuddleInfo(s.Id, s.Center, s.Radius, s.SurfaceY, s.ExpiresAt, s.Source); return true; }
            info = default;
            return false;
        }

        private static bool Touching(in Slot a, in Slot b)
        {
            float dx = a.Center.x - b.Center.x, dz = a.Center.z - b.Center.z, r = a.Radius + b.Radius;
            return dx * dx + dz * dz <= r * r;
        }

        private static int IndexOfSoonest()
        {
            int best = 0;
            for (int i = 1; i < s_count; i++) if (s_slots[i].ExpiresAt < s_slots[best].ExpiresAt) best = i;
            return best;
        }

        private static void ExpireAt(int index)
        {
            if (index < 0 || index >= s_count) return;
            int id = s_slots[index].Id;
            s_slots[index] = s_slots[--s_count];
            s_slots[s_count] = default;
            try { PuddleExpired?.Invoke(id); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Tests: drop every puddle (no events) and restore the §7b defaults. Subscribers are kept.</summary>
        public static void ResetStatics()
        {
            for (int i = 0; i < s_count; i++) s_slots[i] = default;
            s_count = 0;
            Configure();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() { ResetStatics(); s_nextId = 0; PuddleCreated = null; PuddleExpired = null; }
    }
}
