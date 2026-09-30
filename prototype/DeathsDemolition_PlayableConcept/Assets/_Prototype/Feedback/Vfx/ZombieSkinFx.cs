using DD.Prototype.Characters;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B6 · The skin reactions the two Specials leave on a zombie (direction §2.1 "Çarpılma", §2.2 "Zombide jel"):
    /// the electrocution rim — cyan #7FD3FF × 1.2 (the direction's × 3 washed the texture out, see ShockIntensity), a 20 Hz square-wave flicker for the first 0.4 s then 8 Hz (flash / seizure
    /// standard, A4.6: > 12 Hz only ≤ 0.4 s; Reduce flashes: no flicker, a steady 0.75), full for min(0.5 s, 42 % of the
    /// hold) and fading to 0 at its end (§4.2: ≤ 0.5 s at full intensity) — and the gel film, magenta #FF5FD2 × 0.8 fading
    /// to 0 over 6 s. Both are one MaterialPropertyBlock <c>_EmissionColor</c> on the body and bottom slots of every LOD
    /// (§4.2 readability: the head and top slots carry the WP-036 tier band, so they are never lit); the block is removed
    /// (SetPropertyBlock(null)) when the reaction ends, so a body is off the SRP batcher only while it glows.
    ///
    /// DEPENDENCY (coordinator decision, see b6-plan.md): the AP-007 zombie materials are URP Lit without the
    /// <c>_EMISSION</c> keyword, and a property block cannot turn a keyword on. A body whose material has emission off is
    /// counted in <see cref="SkippedNoEmission"/> and left alone (the arcs, sparks, smoke and burn mark still play), so this
    /// class is safe to ship before the material change. 16 slots (8 shocked + 8 gelled bodies at most), fixed arrays,
    /// 0 B per frame. Ticked once per frame by whichever of ArcSystem / GelSystem runs first.
    /// </summary>
    public static class ZombieSkinFx
    {
        public const int Capacity = 16;
        public const float GelFilmSeconds = 6f;
        public const float ShockFullMaxSeconds = 0.5f;
        public const float FastFlickerSeconds = 0.4f;
        public const float FastFlickerHz = 20f, SlowFlickerHz = 8f;

        // ×1.2, not the direction's ×3 (C-00 25.09): URP Lit emission is flat, not a rim — at ×3 the first 0.5 s painted the
        // whole body pale cyan and the texture vanished on the real AP-007 zombies (wp073-b6-captures21 sheet-skin.jpg).
        public const float ShockIntensity = 1.2f;
        private static readonly Vector4 ShockHdr = SpecialVfxAssets.Hdr(0x7FD3FF, ShockIntensity);
        private static readonly Vector4 GelHdr = SpecialVfxAssets.Hdr(0xFF5FD2, 0.8f);

        // DEC-112 (user 30.09, S-04 CapstoneProc): the capstone tone — a cold steel pulse #9FB8D0 on the struck body, so the
        // stagger moment reads without a new VFX. Lightning Fang (smg_2 bonus on a hit-staggered body) 0.25 s, Black Iron
        // (lmg_2 plate break → stagger) 0.5 s; full for the first 30 %, then an ease-out; Reduce flashes: × 0.6.
        [DD.Prototype.Core.Config.PrototypeOnly(DD.Prototype.Core.Config.PendingDecisions.None, "DEC-112 capstone tone: #9FB8D0 × 0.9, 0.25 s (Lightning Fang) / 0.5 s (Black Iron); S-07 value, AD round pending.", true)]
        public const float SteelIntensity = 0.9f;
        public const float SteelFangSeconds = 0.25f;
        public const float SteelIronSeconds = 0.5f;
        public const float SteelReduced = 0.6f;
        private static readonly Vector4 SteelHdr = SpecialVfxAssets.Hdr(0x9FB8D0, SteelIntensity);
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private struct Slot
        {
            public ZombieSkinnedVisual Visual;
            public float ShockBorn, ShockSeconds;
            public float GelBorn;
            public float SteelBorn, SteelSeconds;
            public bool Emissive;
            public bool Applied;
        }

        private static readonly Slot[] s_slots = new Slot[Capacity];
        private static MaterialPropertyBlock s_block;
        private static int s_frame = -1;

        /// <summary>Bodies with a live reaction (tests / probes).</summary>
        public static int ActiveCount { get; private set; }

        /// <summary>Reactions refused because the body's material has no _EMISSION keyword (the decision above).</summary>
        public static int SkippedNoEmission { get; private set; }

        /// <summary>Renderer blocks written since start-up (tests).</summary>
        public static int BlocksWritten { get; private set; }

        /// <summary>A chain hop shocked the body for <paramref name="seconds"/> (a new hop extends, never stacks).</summary>
        public static bool Shock(ZombieSkinnedVisual visual, float seconds)
        {
            int i = Acquire(visual);
            if (i < 0) return false;
            ref Slot s = ref s_slots[i];
            float now = Time.time;
            float left = s.ShockSeconds > 0f ? s.ShockBorn + s.ShockSeconds - now : 0f;
            if (left < seconds)
            {
                s.ShockBorn = now;
                s.ShockSeconds = seconds;
            }

            return s.Emissive;
        }

        /// <summary>Pulses (DEC-112 capstone tone) started since start-up (tests).</summary>
        public static int SteelPulses { get; private set; }

        /// <summary>DEC-112: a capstone struck the body — a cold steel pulse for <paramref name="seconds"/> (a new proc restarts it).</summary>
        public static bool Steel(ZombieSkinnedVisual visual, float seconds)
        {
            int i = Acquire(visual);
            if (i < 0) return false;
            s_slots[i].SteelBorn = Time.time;
            s_slots[i].SteelSeconds = seconds;
            SteelPulses++;
            return s_slots[i].Emissive;
        }

        /// <summary>A gel hit / slide filmed the body (restarts the 6 s film).</summary>
        public static bool Gel(ZombieSkinnedVisual visual)
        {
            int i = Acquire(visual);
            if (i < 0) return false;
            s_slots[i].GelBorn = Time.time;
            return s_slots[i].Emissive;
        }

        private static int Acquire(ZombieSkinnedVisual visual)
        {
            if (visual == null) return -1;
            int free = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_slots[i].Visual == visual) return i;
                if (free < 0 && s_slots[i].Visual == null) free = i;
            }

            if (free < 0) return -1;   // 16 glowing bodies is past both caps (rim ≤ 8, gel ≤ 8): the arcs still play
            bool emissive = HasEmission(visual);
            if (!emissive) SkippedNoEmission++;
            s_slots[free] = new Slot { Visual = visual, ShockBorn = -1f, GelBorn = -1f, SteelBorn = -1f, Emissive = emissive };
            return free;
        }

        private static bool HasEmission(ZombieSkinnedVisual visual)
        {
            SkinnedMeshRenderer r = visual.Slot(0, ZombieSkinnedVisual.SlotBody);
            Material m = r != null ? r.sharedMaterial : null;
            return m != null && m.IsKeywordEnabled("_EMISSION");
        }

        /// <summary>Once per frame (the first caller wins): envelope → block on the lit slots; a finished body is released.</summary>
        public static void Tick(float now)
        {
            if (s_frame == Time.frameCount) return;
            s_frame = Time.frameCount;
            bool reduce = GameFeelSettings.FlashScale < 0.99f;
            int active = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ref Slot s = ref s_slots[i];
                if (s.Visual == null)
                {
                    if (!ReferenceEquals(s.Visual, null)) s = default;   // destroyed body: drop the slot (nothing to clear)
                    continue;
                }

                Vector4 c = Vector4.zero;
                bool live = false;
                if (s.ShockBorn >= 0f)
                {
                    float t = now - s.ShockBorn;
                    if (t < s.ShockSeconds) { c += ShockColour(t, s.ShockSeconds, reduce); live = true; }
                    else s.ShockBorn = -1f;
                }

                if (s.GelBorn >= 0f)
                {
                    float t = now - s.GelBorn;
                    if (t < GelFilmSeconds) { c += GelColour(t); live = true; }
                    else s.GelBorn = -1f;
                }

                if (s.SteelBorn >= 0f)
                {
                    float t = now - s.SteelBorn;
                    if (t < s.SteelSeconds) { c += SteelColour(t, s.SteelSeconds, reduce); live = true; }
                    else s.SteelBorn = -1f;
                }

                if (!live)
                {
                    if (s.Applied) Write(s.Visual, Vector4.zero, true);
                    s = default;
                    continue;
                }

                active++;
                if (!s.Emissive) continue;
                Write(s.Visual, c, false);
                s.Applied = true;
            }

            ActiveCount = active;
        }

        /// <summary>The electrocution rim at <paramref name="t"/> s of a <paramref name="seconds"/> hold (linear HDR).</summary>
        public static Vector4 ShockColour(float t, float seconds, bool reduceFlashes)
        {
            if (t < 0f || t >= seconds) return Vector4.zero;
            float full = Mathf.Min(ShockFullMaxSeconds, seconds * 0.42f);
            float envelope = t <= full ? 1f : 1f - (t - full) / Mathf.Max(0.01f, seconds - full);
            float flicker;
            if (reduceFlashes)
            {
                flicker = 0.75f;
            }
            else
            {
                float hz = t < FastFlickerSeconds ? FastFlickerHz : SlowFlickerHz;
                flicker = (t * hz) % 1f < 0.5f ? 1f : 0.35f;
            }

            return ShockHdr * (envelope * flicker);
        }

        /// <summary>The DEC-112 steel pulse at <paramref name="t"/> s of <paramref name="seconds"/> (linear HDR).</summary>
        public static Vector4 SteelColour(float t, float seconds, bool reduceFlashes)
        {
            if (t < 0f || t >= seconds) return Vector4.zero;
            float full = seconds * 0.3f;
            float k = t <= full ? 1f : 1f - (t - full) / Mathf.Max(0.01f, seconds - full);
            return SteelHdr * (k * k * (reduceFlashes ? SteelReduced : 1f));
        }

        /// <summary>The gel film at <paramref name="t"/> s (linear HDR): 0.8 → 0 over 6 s, ease-out.</summary>
        public static Vector4 GelColour(float t)
        {
            if (t < 0f || t >= GelFilmSeconds) return Vector4.zero;
            float k = 1f - t / GelFilmSeconds;
            return GelHdr * (k * k);
        }

        private static void Write(ZombieSkinnedVisual visual, Vector4 colour, bool clear)
        {
            if (s_block == null) s_block = new MaterialPropertyBlock();
            for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
            {
                WriteOne(visual.Slot(lod, ZombieSkinnedVisual.SlotBody), colour, clear);
                WriteOne(visual.Slot(lod, ZombieSkinnedVisual.SlotBottom), colour, clear);
            }
        }

        private static void WriteOne(Renderer r, Vector4 colour, bool clear)
        {
            if (r == null) return;
            if (clear)
            {
                r.SetPropertyBlock(null);   // back on the SRP batcher (the zombie renderers carry no other block)
                return;
            }

            s_block.Clear();
            s_block.SetVector(EmissionId, colour);
            r.SetPropertyBlock(s_block);
            BlocksWritten++;
        }

        /// <summary>Tests: the block value a renderer carries now (Vector4.zero when none).</summary>
        public static Vector4 BlockColourForTests(Renderer r)
        {
            if (r == null || !r.HasPropertyBlock()) return Vector4.zero;
            if (s_block == null) s_block = new MaterialPropertyBlock();
            r.GetPropertyBlock(s_block);
            return s_block.GetVector(EmissionId);
        }

        /// <summary>Scene change / run end / tests: every body back to its plain material.</summary>
        public static void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (s_slots[i].Visual != null && s_slots[i].Applied) Write(s_slots[i].Visual, Vector4.zero, true);
                s_slots[i] = default;
            }

            ActiveCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < Capacity; i++) s_slots[i] = default;
            s_frame = -1;
            ActiveCount = 0;
            SkippedNoEmission = 0;
            BlocksWritten = 0;
        }
    }
}
