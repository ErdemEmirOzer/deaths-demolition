using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4 (S-07) · The art direction of the run tree as pure tables: the tier ladder (AD-VISUAL §2), the layer palettes
    /// (§1.2), the dominant element (§1.3 K1, S-04's TreeStats.DominantElement), the global aura (§2, AP-041 B11) and the synergy ensō tints
    /// (AP-041 synergy-map.json). Nothing here touches the scene; <see cref="TracerStyle"/>, <see cref="TreeVfxBinder"/> and
    /// <see cref="TreeAuraVfx"/> read it, Wp104B4VfxTests pins every number to the AD tables. Colours are sRGB as the AD writes
    /// them (<see cref="Linear"/> for shader vectors; a MaterialPropertyBlock.SetColor converts by itself).
    /// Reads take <c>in TreeStats</c> and touch fields only: a method call on a readonly struct would copy the whole struct.
    /// </summary>
    public static class TreeVfxLadder
    {
        public const int TierMin = 1, TierMax = 5;
        /// <summary>The TD look's URP bloom threshold (AD §1.4: an emissive above it reads as a bloom spot).</summary>
        public const float BloomThreshold = 1.25f;
        /// <summary>The TD gameplay camera's scale at 1080p (≈ 95 px/m: WP-101 Wp101TdIzZeminTests, 1080 / (2 · 20 m · tan 15.85°)) —
        /// screen-size estimates (FlashGate area, A4 ring step, the tracer ceilings' WP-101 floor).</summary>
        public const float TdPixelsPerMetre = 95f;
        /// <summary>WCAG 2.3.1 / WP-068 FlashGate: a flash of this many pixels or more (1080p) is a "large" onset (≤ 3 / s).</summary>
        public const float LargeFlashPixels = 43000f;
        /// <summary>K1: a secondary element shows only on the hit, at this scale.</summary>
        public const float SecondaryElementScale = .6f;
        /// <summary>K1: at most this many element visuals per hit.</summary>
        public const int ElementVisualsPerHit = 2;
        /// <summary>K2: the element rim of a chaos event, share of its area (shader mask pending — B4-2).</summary>
        public const float ChaosElementRimShare = .25f;

        // ---------------------------------------------------------------- AD §2 tier ladder (I … V)
        private static readonly float[] s_emissive = { 0f, .3f, .6f, 1f, 1.5f };
        // AD-B4 §1 (12.10, binding over AD-VISUAL §2's tracer columns): the core is the realism anchor, not a tier channel
        private static readonly float[] s_haloStepPx = { 0f, 5f, 10f, 15f, 20f };
        private static readonly float[] s_trail = { 1f, 1f, 1.1f, 1.25f, 1.45f };
        private static readonly float[] s_impact = { .6f, .75f, .9f, 1f, 1.25f };

        /// <summary>0 (no node in the branch) reads as tier I: the realistic WP-101 look.</summary>
        public static int ClampTier(int tier) => tier < TierMin ? TierMin : tier > TierMax ? TierMax : tier;

        /// <summary>
        /// AD-B4 §1 "Hale": a tracer's halo is its WP-101 class halo (core × TdHaloScale, m) + this many px at 1080p (resolution-
        /// proportional, DD/VFX Ribbon <c>_Extra.y</c>) — 0 / 5 / 10 / 15 / 20: one equal step per tier, saturating at V = base + 20
        /// (AR 64, SR ≈ 84 px; no fixed ceiling). Owner max(Shot, Element); the K3 marked round takes V whatever its tier.
        /// </summary>
        public static float HaloStepPx(int tier) => s_haloStepPx[ClampTier(tier) - 1];

        /// <summary>AD-B4 §1 "İz boyu": × the WP-101 motion-blur trail — 1.0 / 1.0 / 1.1 / 1.25 / 1.45, at most
        /// <see cref="TrailMaxMetres"/>. Owner Shot only (the shape's owner): I–II keep the realistic length.</summary>
        public static float TrailScale(int tier) => s_trail[ClampTier(tier) - 1];

        /// <summary>AD-B4 §1: a styled tracer streak is never longer than this (the sniper's own WP-101 ceiling).</summary>
        public const float TrailMaxMetres = 10f;

        /// <summary>AD-B4 S1: the halo's HDR × this at V (owner max(Shot, Element)) — a bloom boost, so Reduce Flashes takes it out
        /// before it dims the rest (<see cref="TracerStyle.HaloHdr"/>).</summary>
        public const float HaloHdrV = 1.5f;

        /// <summary>AD §2 "İsabet olayı ölçeği" ×0.6 / 0.75 / 0.9 / 1.0 / 1.25.</summary>
        public static float ImpactScale(int tier) => s_impact[ClampTier(tier) - 1];

        /// <summary>AD §2 "Emissive" 0 / 0.3 / 0.6 / 1.0 / 1.5 HDR — weapon add-ons (E4 / E5 / E6, S-06) and the B12 muzzle overlay.
        /// The tracer core keeps its WP-101 HDR: "çekirdeği hep gerçekçi kalır" (AD §0).</summary>
        public static float Emissive(int tier) => s_emissive[ClampTier(tier) - 1];

        /// <summary>AD §2 Reduce Flashes: emissive peaks × FlashScale — V's 1.5 HDR drops under the bloom threshold at 0.4.</summary>
        public static float EmissiveFor(int tier, float flashScale) => Emissive(tier) * Mathf.Clamp01(flashScale);

        /// <summary>Which S-06 add-on a glow value is for (AD-B4 M9 / §5 Q4).</summary>
        public enum AddOnGlow
        {
            /// <summary>E1, E2, E3 (read by their silhouette): the §2 ladder 0 / 0.3 / 0.6 / 1.0 / 1.5.</summary>
            Silhouette = 0,
            /// <summary>E4 side capsule (≈ 5 px, read by its bloom spot, §1.4): I 0 · II 0.3 · III 1.4 · IV 1.6 · V 2.0.</summary>
            Side = 4,
            /// <summary>E5 muzzle ring: I 0 · II 0.3 · III 0.6 · IV 1.4 · V 2.0 (the ring mounts from IV; full auto / endless kills below).</summary>
            Ring = 5
        }

        // AD-B4 M9: E4 III 1.4 · IV 1.6 · V 2.0, E5 IV 1.4 · V 2.0 — over the 1.25 bloom threshold; the rungs M9 leaves open keep §2's.
        private static readonly float[] s_sideGlow = { 0f, .3f, 1.4f, 1.6f, 2f };
        private static readonly float[] s_ringGlow = { 0f, .3f, .6f, 1.4f, 2f };

        /// <summary>The add-on's HDR emission at <paramref name="tier"/> before FlashScale (no tier (0) reads as I: no glow).</summary>
        public static float AddOnEmissive(AddOnGlow kind, int tier)
        {
            int i = ClampTier(tier) - 1;
            return kind == AddOnGlow.Side ? s_sideGlow[i] : kind == AddOnGlow.Ring ? s_ringGlow[i] : s_emissive[i];
        }

        /// <summary>
        /// S-06's add-on glow (AP-040 slot 1 <c>M_AP040_Glow</c>, URP Lit with <c>_EMISSION</c>): the <c>_EmissionColor</c> a renderer
        /// takes through its slot-1 MaterialPropertyBlock (SetColor: the gamma value the material inspector and URP's bloom threshold
        /// read) — white × <see cref="AddOnEmissive"/> × FlashScale; the glow material's emission map carries the hue (its own default
        /// 1.5 white shows only on a slot nobody painted). Black while the add-on is not shown (TD only).
        /// </summary>
        public static Color AddOnEmission(AddOnGlow kind, int tier, float flashScale, bool shown)
        {
            float e = shown ? AddOnEmissive(kind, tier) * Mathf.Clamp01(flashScale) : 0f;
            return new Color(e, e, e, 1f);
        }

        /// <summary>The §2 ladder (E1–E3, <see cref="AddOnGlow.Silhouette"/>); E4 / E5 take the M9 overload.</summary>
        public static Color AddOnEmission(int tier, float flashScale, bool shown) => AddOnEmission(AddOnGlow.Silhouette, tier, flashScale, shown);

        /// <summary>The ink layer an impact event of this tier carries (AD §2 "Sumi katmanı"; AP-041 TIER rows: one per event).</summary>
        public enum Sumi
        {
            None = 0,
            /// <summary>III: thin ink rim on the shock ring (G_T3_ink_shock).</summary>
            InkShock = 3,
            /// <summary>IV: ensō attraction ring (G_T4_enso_attraction) — gravity events; a blast at IV keeps the ink shock.</summary>
            Enso = 4,
            /// <summary>V: ink spatter + burnt-paper rim (G_T5_ink_spatter).</summary>
            Spatter = 5
        }

        public static Sumi SumiFor(int tier, bool gravity)
        {
            if (tier >= 5) return Sumi.Spatter;
            if (tier == 4) return gravity ? Sumi.Enso : Sumi.InkShock;
            return tier == 3 ? Sumi.InkShock : Sumi.None;
        }

        // ---------------------------------------------------------------- tree reads (fields only)

        /// <summary><see cref="TreeStats.BranchTier"/> without the defensive copy a method call on <c>in TreeStats</c> makes.</summary>
        public static int BranchTier(in TreeStats s, TreeBranch branch) => (s.BranchTiers >> ((int)branch * 3)) & 7;

        /// <summary>
        /// K1 "bir mermi = bir renk": the element that paints every round is S-04's <see cref="TreeStats.DominantElement"/> (B1b, S-04
        /// 12.10 §1: the element of the highest-tier owned element node, a tie to the later purchase, re-resolved on every purchase
        /// and equip). One owned element bit or None — never a fixed order.
        /// </summary>
        public static TreeElement DominantElement(in TreeStats s)
        {
            TreeElement d = Single(s.DominantElement);
            return d != TreeElement.None && (s.Elements & d) == d ? d : TreeElement.None;   // an owned flag only (S-06's E4 capsule rule)
        }

        /// <summary><paramref name="e"/> when it is exactly one of the four elements, else None.</summary>
        public static TreeElement Single(TreeElement e) =>
            e == TreeElement.Fire || e == TreeElement.Ice || e == TreeElement.Shock || e == TreeElement.Venom ? e : TreeElement.None;

        /// <summary>
        /// The one element a proc's visual shows (S-04 12.10 §4: an element moment carries its own element; a blast fused in
        /// S-04's queue ORs them; Element Nova carries every owned one): a single bit as it is, else the dominant element when the
        /// mask holds it, else the mask's first bit. None stays None.
        /// </summary>
        public static TreeElement PickElement(TreeElement mask, TreeElement dominant)
        {
            TreeElement one = Single(mask);
            if (one != TreeElement.None || mask == TreeElement.None) return one;
            if (dominant != TreeElement.None && (mask & dominant) == dominant) return dominant;
            return FirstElement(mask);
        }

        // ---------------------------------------------------------------- palettes (AD §1.2; sRGB)

        public static Color Hex(int rgb) => new Color32((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255);

        /// <summary>sRGB → linear (a shader vector; <c>SetVectorArray</c> does not convert).</summary>
        public static Color Linear(Color srgb) => srgb.linear;

        public static readonly Color FireGlow = Hex(0xFF5A1F), FireCore = Hex(0xFFE2B0);
        public static readonly Color IceGlow = Hex(0x8CE6FF), IceCore = Hex(0xF0FCFF);
        public static readonly Color ShockGlow = Hex(0xADC6FF), ShockCore = Hex(0xFFFFFF);
        public static readonly Color VenomGlow = Hex(0xA8E04A), VenomCore = Hex(0xE8FFB0);
        /// <summary>K3 marked chaos round: violet edge, sumi core; the chaos base pigment.</summary>
        public static readonly Color ChaosEdge = Hex(0x9B5CFF), ChaosInk = Hex(0x12100F), ChaosBase = Hex(0x5E3A8C);
        /// <summary>The gold orb (never recoloured, K8) — A2 keeps every element ΔE ≥ 35 from it.</summary>
        public static readonly Color OrbGold = Hex(0xFFC74D);
        /// <summary>Speed's white-hot (s ≤ 0.15) and the global level-up ginpaku.</summary>
        public static readonly Color WhiteHot = Hex(0xFFF4E0);

        public static Color ElementGlow(TreeElement e)
        {
            switch (e)
            {
                case TreeElement.Fire: return FireGlow;
                case TreeElement.Ice: return IceGlow;
                case TreeElement.Shock: return ShockGlow;
                case TreeElement.Venom: return VenomGlow;
                default: return Color.white;
            }
        }

        public static Color ElementCore(TreeElement e)
        {
            switch (e)
            {
                case TreeElement.Fire: return FireCore;
                case TreeElement.Ice: return IceCore;
                case TreeElement.Shock: return ShockCore;
                case TreeElement.Venom: return VenomCore;
                default: return Color.white;
            }
        }

        /// <summary>A single element of a (possibly multi-bit) element mask: the first set bit, None when empty.</summary>
        public static TreeElement FirstElement(TreeElement mask)
        {
            for (int i = 0; i < 4; i++)
            {
                var e = (TreeElement)(1 << i);
                if ((mask & e) != 0) return e;
            }

            return TreeElement.None;
        }

        // ---------------------------------------------------------------- aura (AD §2 B11, global)

        public const int AuraLevels = 3;
        private static readonly Color[] s_auraTint = { Hex(0x5C8DFF), Hex(0xFFE8B8), Hex(0xE8302A) };
        private static readonly float[] s_auraRadius = { 1f, 1.3f, 1.6f };
        private static readonly float[] s_auraAlpha = { .45f, .55f, .60f };
        private static readonly int[] s_auraSparksPerSecond = { 6, 12, 20 };
        private static readonly int[] s_auraSparksLive = { 12, 18, 24 };
        /// <summary>Aura III's sumi smoke stays under the body (AP-041 G_B11_sumi_smoke_tier3 α ≤ 0.25).</summary>
        public const float AuraSmokeAlpha = .25f;
        /// <summary>K7 / AD §2 RF: sparks at half while the shield is up or flashes are reduced.</summary>
        public const float AuraSparkDamp = .5f;

        public static int ClampAura(int level) => level < 0 ? 0 : level > AuraLevels ? AuraLevels : level;
        public static Color AuraTint(int level) => level < 1 ? Color.clear : s_auraTint[ClampAura(level) - 1];
        public static float AuraRadius(int level) => level < 1 ? 0f : s_auraRadius[ClampAura(level) - 1];
        public static float AuraAlpha(int level) => level < 1 ? 0f : s_auraAlpha[ClampAura(level) - 1];
        public static int AuraSparksPerSecond(int level) => level < 1 ? 0 : s_auraSparksPerSecond[ClampAura(level) - 1];
        public static int AuraSparksLive(int level) => level < 1 ? 0 : s_auraSparksLive[ClampAura(level) - 1];

        // ---------------------------------------------------------------- light-stone underdraws (AD-B4 M6 / M7 / M8)

        /// <summary>
        /// AD-B4 M6: the aura ring drawn once more UNDER itself with its own sheet and frame — sumi #12100F, α ≤ 0.35, ×1.04, alpha-
        /// blended (the sheets are premultiplied: a near-black tint keeps the ring's coverage as an ink darkening). The pale rings moved
        /// next to no light-stone pixel (aura II: 15 px); the ink rim does, and on wet slate it all but vanishes.
        /// <see cref="AuraUnderCoverage"/> scales the sheet's coverage before the cap (DD/VFX Flipbook: fade = tint α × _Alpha), so
        /// the stroke's soft shoulder takes the 0.35 too — the bare stroke (3–4 texels at ≥ 0.28) left aura II at ≈ 0.48 × its slate
        /// pixels in a CPU model of frames 22–24, the AD's floor is 0.5.
        /// </summary>
        public static readonly Color AuraUnderTint = Hex(0x12100F);
        public const float AuraUnderAlpha = .35f, AuraUnderScale = 1.04f, AuraUnderCoverage = 2f;

        /// <summary>
        /// AD-B4 M7: the leech thread's jade underdraw — the same B15 ribbon once more under itself, #4F8A63 (× the sheet's own white-
        /// jade fibre), α 0.5, alpha-blended, <see cref="LeechUnderThickness"/> × the thread's band so the jade edges the pale wisp on
        /// both sides; <see cref="LeechUnderCoverage"/> lifts the wisp's coverage (mean α ≈ 0.3) to the cap.
        /// </summary>
        public static readonly Color LeechUnderTint = Hex(0x4F8A63);
        public const float LeechUnderAlpha = .5f, LeechUnderThickness = 1.6f, LeechUnderCoverage = 4f;

        /// <summary>
        /// AD-B4 M8: the tree fire ground's ember underdraw — WP-073's char disc (its texture, size and turn) once more in #E0702A at
        /// α ≤ 0.30 (this × the zone's ramp / dying / out intensity), alpha-blended between the char and the additive ember glow: on
        /// light stone the char-grey disc warms under the glow, on wet slate the glow already carries it. Tree zones only
        /// (GroundFireSystem.OnTreeIgnited); RunTreeGroundFire's own zones draw as WP-073 left them.
        /// </summary>
        public static readonly Color FireUnderTint = Hex(0xE0702A);
        public const float FireUnderAlpha = .21f;   // b4-r3: .28 lifted wet slate satMean +13 % (AD ±10 %); light stone .41 → ≈ .37 (≥ .35)

        // ---------------------------------------------------------------- B8 chain blast scale (AD-B4 M10)

        /// <summary>A later chain generation's B8 card × this per generation (AD §3 "sonra ×0,85ⁿ").</summary>
        public const float ChainGenerationScale = .85f;
        /// <summary>The B8 card's ceiling in authored sizes (1.8 m → 7.2 m): V at R 2.5 m (6.25 m) still shows its whole area.</summary>
        public const float ChainCardMaxShare = 4f;

        /// <summary>
        /// AD-B4 M10: a chain blast shows its damage area as B5 does — S-04's ChainBlast Radius is the sphere its queue damages
        /// (TreeBlastQueue → TreeStatusSystem.Query(Point, Radius)) — × the tier's event scale: the first generation's card is
        /// 2R × <see cref="ImpactScale"/>, and the AP-041 B8 sheet's ring fills ≈ 0.9 of its card from frame 2 to 12, so R 3 m at
        /// III draws a 5.4 m card and a ≈ 4.9 m ring (AD: 0.8–1.0 × 2R × 0.9 = 4.3–5.4 m; the old authored 1.8 m card drew 1.1 →
        /// 2.3 m). A later generation × <see cref="ChainGenerationScale"/>ⁿ; no radius: the authored size × the ladder, as before.
        /// </summary>
        public static float ChainCardSize(float radius, int tier, int generation, float authoredMetres)
        {
            float a = Mathf.Max(.05f, authoredMetres);
            float size = radius > 0f ? Mathf.Clamp(2f * radius * ImpactScale(tier), a * .5f, a * ChainCardMaxShare) : a * ImpactScale(tier);
            return generation > 0 ? size * Mathf.Pow(ChainGenerationScale, generation) : size;
        }

        // ---------------------------------------------------------------- synergy ensō (AP-041 synergy-map.json; UI pigments)

        /// <summary>The two halves' pigments of a synergy's first-trigger ensō (left = first node's branch, right = second's).</summary>
        public static void SynergyTints(TreeSynergy synergy, out Color left, out Color right)
        {
            switch (synergy)
            {
                case TreeSynergy.Barrage: left = Hex(0xC4362B); right = Hex(0x7F8C99); return;
                case TreeSynergy.FrozenGravity: left = Hex(0x3E9AAE); right = Hex(0x5E3A8C); return;
                case TreeSynergy.ThunderCascade: left = Hex(0x5B7FD1); right = Hex(0x5E3A8C); return;
                case TreeSynergy.PinballStorm: left = Hex(0xC4362B); right = Hex(0xC4362B); return;
                case TreeSynergy.UnbrokenLine: left = Hex(0x46567C); right = Hex(0x7F8C99); return;
                case TreeSynergy.WildfireRounds: left = Hex(0xE0702A); right = Hex(0xC4362B); return;
                case TreeSynergy.ToxicRail: left = Hex(0x6E9A2E); right = Hex(0xC4362B); return;
                case TreeSynergy.CriticalMeltdown: left = Hex(0x5E3A8C); right = Hex(0x5E3A8C); return;
                case TreeSynergy.JudgmentLine: left = Hex(0xC4362B); right = Hex(0xC4362B); return;
                case TreeSynergy.GoldenGun: left = Hex(0xFFC74D); right = Hex(0x5E3A8C); return;
                default: left = right = Color.white; return;
            }
        }

        /// <summary>The single synergy bit of a mask (first set bit), None when empty.</summary>
        public static TreeSynergy FirstSynergy(TreeSynergy mask)
        {
            for (int i = 0; i < 16; i++)
            {
                var s = (TreeSynergy)(1 << i);
                if ((mask & s) != 0) return s;
            }

            return TreeSynergy.None;
        }

        /// <summary>Screen area (px, 1080p TD) of a square world quad — FlashGate's "large" test.</summary>
        public static float TdAreaPixels(float worldSize) => worldSize * TdPixelsPerMetre * worldSize * TdPixelsPerMetre;
    }
}
