using System;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>WP-104 B3 · what a tree area zone does.</summary>
    public enum TreeAreaKind : byte
    {
        /// <summary>Burning ground (Wildfire, Wildfire Rounds): damage per tick to every body inside.</summary>
        Fire = 0,
        /// <summary>Plague cloud: venom stacks per tick to every body inside (the DoT is the status system's).</summary>
        Venom = 1
    }

    /// <summary>WP-104 B3 · one zone as the pool publishes it (S-07 VFX / S-08 audio). Readonly, no allocation.</summary>
    public readonly struct TreeAreaInfo
    {
        public readonly int Id;
        public readonly TreeAreaKind Kind;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float DamagePerSecond;
        public readonly float Seconds;
        public readonly float SecondsLeft;

        public TreeAreaInfo(int id, TreeAreaKind kind, Vector3 center, float radius, float damagePerSecond, float seconds, float secondsLeft)
        {
            Id = id; Kind = kind; Center = center; Radius = radius; DamagePerSecond = damagePerSecond; Seconds = seconds; SecondsLeft = secondsLeft;
        }
    }

    /// <summary>
    /// WP-104 B3 (GDEV §2 #14 #17) · the generalisation of <see cref="RunTreeGroundFire"/> for the feature tree: one fixed pool
    /// of <see cref="TreeGuardrails.AreaZones"/> (16) zones shared by fire grounds (≤ <see cref="TreeGuardrails.FireGroundsActive"/>)
    /// and venom clouds (≤ <see cref="TreeGuardrails.VenomCloudsActive"/>). Over a kind's cap (or the shared cap) the OLDEST
    /// zone goes out (GD §8.3 "en eskisi söner"). Each zone ticks every <see cref="TreeSystemsTuning.AreaTickSeconds"/>; the
    /// first tick lands one interval after ignition (the event that lit it already hit), so a 3 s zone ticks 6 times.
    /// A tick is one area query from the frame budget (<see cref="TreeTargetQuery"/>); a refused tick stays due and a late
    /// zone catches up in one query (damage × ticks due), so the total is kept. A zone lit during the pool's own tick (a body
    /// it killed lights another) waits in a small pending list until the tick ends (no swap under the iterator).
    /// No allocation; <see cref="TreeMarkers.AreaZones"/>. WP-077's <see cref="RunTreeGroundFire"/> stays as is for the old tree.
    /// </summary>
    public static class TreeAreaPool
    {
        private const int Capacity = TreeGuardrails.AreaZones;
        private const float Eps = 1e-4f;

        private struct Zone
        {
            public int Id;
            public TreeAreaKind Kind;
            public Vector3 Center;
            public float Radius, DamagePerSecond, Seconds, Elapsed, NextTick;
            public int Depth;
        }

        private static readonly Zone[] s_zones = new Zone[Capacity];
        private static readonly Zone[] s_pending = new Zone[Capacity];
        private static readonly ITreeStatusReceiver[] s_targets = new ITreeStatusReceiver[TreeGuardrails.StatusSlots];
        private static int s_count;
        private static int s_pendingCount;
        private static int s_nextId;
        private static bool s_ticking;

        /// <summary>A zone started (S-07 / S-08: kind, centre, radius, seconds).</summary>
        public static event Action<TreeAreaInfo> Ignited;

        /// <summary>A zone went out (expired, replaced or cleared): the id of <see cref="Ignited"/>.</summary>
        public static event Action<int> Extinguished;

        public static int Count => s_count;
        public static int PendingCount => s_pendingCount;
        public static int Replaced { get; private set; }
        public static int Dropped { get; private set; }
        public static int Ticks { get; private set; }
        public static int BodiesTouched { get; private set; }
        public static float DamageDealt { get; private set; }

        public static int CountOf(TreeAreaKind kind)
        {
            int n = 0;
            for (int i = 0; i < s_count; i++)
            {
                if (s_zones[i].Kind == kind) n++;
            }

            return n;
        }

        public static TreeAreaInfo Get(int i)
        {
            if (i < 0 || i >= s_count) return default;
            Zone z = s_zones[i];
            return new TreeAreaInfo(z.Id, z.Kind, z.Center, z.Radius, z.DamagePerSecond, z.Seconds, Mathf.Max(0f, z.Seconds - z.Elapsed));
        }

        /// <summary>True when a live zone carries <paramref name="id"/>.</summary>
        public static bool IsLive(int id)
        {
            for (int i = 0; i < s_count; i++)
            {
                if (s_zones[i].Id == id) return true;
            }

            return false;
        }

        /// <summary>
        /// Starts a zone; returns its id (0 = nothing: non-positive radius / time, a fire zone without damage, or a full
        /// pending list mid-tick). <paramref name="depth"/> = chain generation of the death that lit it (a kill by the zone
        /// is of this depth).
        /// </summary>
        public static int Ignite(TreeAreaKind kind, Vector3 center, float radius, float seconds, float damagePerSecond, int depth = 0)
        {
            if (radius <= 0f || seconds <= 0f) return 0;
            if (kind == TreeAreaKind.Fire && damagePerSecond <= 0f) return 0;

            var zone = new Zone
            {
                Id = ++s_nextId, Kind = kind, Center = center, Radius = radius, DamagePerSecond = Mathf.Max(0f, damagePerSecond),
                Seconds = seconds, Elapsed = 0f, NextTick = TreeSystemsTuning.AreaTickSeconds, Depth = depth
            };

            if (s_ticking)
            {
                if (s_pendingCount >= Capacity)
                {
                    Dropped++;
                    return 0;
                }

                s_pending[s_pendingCount++] = zone;
                return zone.Id;
            }

            Insert(in zone);
            return zone.Id;
        }

        /// <summary>Advances every zone; due ticks hit (one query each), spent zones go out. Called by <see cref="TreeSystemsRunner.Tick"/>.</summary>
        internal static void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || (s_count == 0 && s_pendingCount == 0)) return;

            using (TreeMarkers.AreaZones.Auto())
            {
                float tick = TreeSystemsTuning.AreaTickSeconds;
                s_ticking = true;
                try
                {
                    for (int i = s_count - 1; i >= 0; i--)
                    {
                        if (i >= s_count) continue;
                        s_zones[i].Elapsed += deltaTime;

                        int due = 0;
                        while (s_zones[i].NextTick <= s_zones[i].Elapsed + Eps && s_zones[i].NextTick <= s_zones[i].Seconds + Eps)
                        {
                            due++;
                            s_zones[i].NextTick += tick;
                        }

                        if (due > 0)
                        {
                            if (TreeTargetQuery.TryUse())
                            {
                                Zone z = s_zones[i];
                                Hit(in z, due);
                            }
                            else
                            {
                                s_zones[i].NextTick -= due * tick;   // stays due: next frame
                            }
                        }

                        // out only when spent AND every tick it owed has landed
                        if (i < s_count && s_zones[i].Elapsed >= s_zones[i].Seconds - Eps && s_zones[i].NextTick > s_zones[i].Seconds + Eps)
                        {
                            RemoveAt(i);
                        }
                    }
                }
                finally
                {
                    s_ticking = false;
                }

                for (int p = 0; p < s_pendingCount; p++)
                {
                    Insert(in s_pending[p]);
                    s_pending[p] = default;
                }

                s_pendingCount = 0;
            }
        }

        /// <summary>Puts every zone out (run start / end, scene change, tests).</summary>
        internal static void Reset()
        {
            int n = s_count;
            s_count = 0;
            for (int i = 0; i < n; i++)
            {
                int id = s_zones[i].Id;
                s_zones[i] = default;
                RaiseOut(id);
            }

            for (int i = 0; i < Capacity; i++) s_pending[i] = default;
            for (int i = 0; i < s_targets.Length; i++) s_targets[i] = null;
            s_pendingCount = 0;
            s_ticking = false;
            Replaced = 0;
            Dropped = 0;
            Ticks = 0;
            BodiesTouched = 0;
            DamageDealt = 0f;
        }

        /// <summary>Domain reload / tests: forget listeners too.</summary>
        internal static void ResetForTests()
        {
            Reset();
            s_nextId = 0;
            Ignited = null;
            Extinguished = null;
        }

        // ------------------------------------------------------------------ internals

        private static void Hit(in Zone z, int ticksDue)
        {
            Ticks += ticksDue;
            float tick = TreeSystemsTuning.AreaTickSeconds;
            int n = TreeStatusSystem.Query(z.Center, z.Radius, s_targets);
            bool owned = TreeDamageContext.BeginTransaction(0);   // one transaction per zone tick (each body once)
            TreeStatusSystem.BeginBatch();   // a death inside frees its slot after the loop (12.10 (a))
            try
            {
                if (z.Kind == TreeAreaKind.Fire)
                {
                    float amount = z.DamagePerSecond * tick * ticksDue;
                    for (int k = 0; k < n; k++)
                    {
                        ITreeStatusReceiver body = s_targets[k];
                        if (body == null || !body.IsAlive) continue;
                        BodiesTouched++;
                        DamageDealt += amount;
                        TreeDamageContext.Status(body, amount, TreeElement.Fire, z.Depth, TreeDamageKind.Area, TreeProcKind.FireGround, z.Center);
                    }
                }
                else
                {
                    int stacks = Mathf.Max(1, Mathf.RoundToInt(TreeSystemsTuning.VenomCloudStacksPerSecond * tick * ticksDue));
                    for (int k = 0; k < n; k++)
                    {
                        ITreeStatusReceiver body = s_targets[k];
                        if (body == null || !body.IsAlive) continue;
                        BodiesTouched++;
                        TreeStatusSystem.AddVenomStacks(body, stacks);
                    }
                }
            }
            finally
            {
                TreeStatusSystem.EndBatch();
                TreeDamageContext.EndTransaction(owned);
                for (int k = 0; k < n; k++) s_targets[k] = null;
            }
        }

        private static void Insert(in Zone zone)
        {
            int cap = zone.Kind == TreeAreaKind.Fire ? TreeGuardrails.FireGroundsActive : TreeGuardrails.VenomCloudsActive;
            int index;
            if (CountOf(zone.Kind) >= cap)
            {
                index = OldestOf(zone.Kind, true);
                Replace(index);
            }
            else if (s_count >= Capacity)
            {
                index = OldestOf(zone.Kind, false);
                Replace(index);
            }
            else
            {
                index = s_count++;
            }

            s_zones[index] = zone;

            if (Ignited != null)
            {
                try { Ignited(new TreeAreaInfo(zone.Id, zone.Kind, zone.Center, zone.Radius, zone.DamagePerSecond, zone.Seconds, zone.Seconds)); }
                catch (Exception e) { Debug.LogException(e); }
            }

            TreeSystemsRunner.Raise(zone.Kind == TreeAreaKind.Fire ? TreeProcKind.FireGround : TreeProcKind.VenomCloud, zone.Center, zone.Radius,
                zone.DamagePerSecond, zone.Kind == TreeAreaKind.Fire ? TreeElement.Fire : TreeElement.Venom, TreeBranch.Element, TreeSynergy.None, null,
                zone.Depth, zone.Seconds);   // S-07 / S-08: Damage = per second, Seconds = the zone's life
        }

        private static void Replace(int index)
        {
            Replaced++;
            RaiseOut(s_zones[index].Id);
        }

        /// <summary>Oldest zone (lowest id); of <paramref name="kind"/> only when <paramref name="sameKind"/>.</summary>
        private static int OldestOf(TreeAreaKind kind, bool sameKind)
        {
            int best = -1;
            for (int i = 0; i < s_count; i++)
            {
                if (sameKind && s_zones[i].Kind != kind) continue;
                if (best < 0 || s_zones[i].Id < s_zones[best].Id) best = i;
            }

            return best < 0 ? 0 : best;
        }

        private static void RemoveAt(int index)
        {
            if (index < 0 || index >= s_count) return;
            int id = s_zones[index].Id;
            s_zones[index] = s_zones[--s_count];
            s_zones[s_count] = default;
            RaiseOut(id);
        }

        private static void RaiseOut(int id)
        {
            if (Extinguished == null) return;
            try { Extinguished(id); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
