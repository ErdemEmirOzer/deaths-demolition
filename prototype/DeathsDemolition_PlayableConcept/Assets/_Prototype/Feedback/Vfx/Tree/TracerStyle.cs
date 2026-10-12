using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-104 B4 (S-07) · The run's top-down tracer look: the one hook B4 puts into WP-101's TD block (TracerRibbonRenderer.Tree.cs,
    /// the partial half of <see cref="TracerRibbonRenderer"/>). A round takes the look in effect when it is born and keeps it to its
    /// impact, like its tracer rhythm. Channels and owners (AD-B4 §1, 12.10 — binding over AD-VISUAL §2's tracer columns):
    /// <list type="bullet">
    /// <item>CORE: the realism anchor, no tier channel — the WP-101 class width × the round's own size (S-02 B2
    /// <c>ProjectileSpawnInfo.SizeScale</c>: Big Bore × a merged shot) × a TD record merge (<see cref="MergeFactor"/>), its quad never
    /// past max(<see cref="CoreQuadCapPx"/>, its WP-101 width): it neither grows nor thins with the tree (A5, §5 Q2);</item>
    /// <item>HALO: the WP-101 class halo (core × TdHaloScale, × the same size) + <see cref="TreeVfxLadder.HaloStepPx"/> px at 1080p
    /// (0 / 5 / 10 / 15 / 20) of the max(Shot, Element) tier — no fixed ceiling — and its HDR × <see cref="TreeVfxLadder.HaloHdrV"/>
    /// at V (S1);</item>
    /// <item>LENGTH: the WP-101 motion-blur trail × <see cref="TreeVfxLadder.TrailScale"/> of the Shot tier (1.0 / 1.0 / 1.1 / 1.25 /
    /// 1.45, ≤ 10 m);</item>
    /// <item>COLOUR: S-04's <see cref="TreeStats.DominantElement"/> (K1 "bir mermi = bir renk") paints the core, its tail, the halo,
    /// the body streak and the pellet bars, the full palette at every tier; an element halo covers the ground it veils
    /// (<see cref="ElementHaloCover"/>, AD-B4 M5: its colour unchanged);</item>
    /// <item>CHAOS never paints a trail, except the marked round (K3, <see cref="MarkedLook"/>): violet edge, the V halo whatever its
    /// tier, a sumi core.</item>
    /// </list>
    /// <para>Reduce Flashes (AD-B4 M3): a steady trail is no flash — FlashScale dims the halo's HDR (<see cref="HaloHdr"/>), never its
    /// α; the core keeps WP-101's lerp(0.35, 1) HDR rule. With no tree (<see cref="TreeStats.Neutral"/>, size 1) the hook returns
    /// WP-101's values untouched: look 0, no ceiling, no halo step, the WP-101 trail — Normal byte-identical.</para>
    /// Cached per <see cref="TreeRuntime.Version"/>; 0 B per round. <see cref="SetPreview"/> lets the S-09 diorama birth rounds in
    /// another build's look ("önce → sonra").
    /// </summary>
    public static class TracerStyle
    {
        public const int NeutralLook = 0, FireLook = 1, IceLook = 2, ShockLook = 3, VenomLook = 4, MarkedLook = 5, LookCount = 6;

        /// <summary>AD §2 / A5: the active (bright, thresholded) tracer core ≤ 8 px at 1080p — or the class's own WP-101 core where that
        /// is wider (LMG ≈ 9, SR ≈ 11 px: AD-B4 §5 Q2, the shader's floor).</summary>
        public const float CoreCapPx = 8f;
        /// <summary>DD/VFX Ribbon's cross-section (1 − smoothstep(0.35, 1, |v|)) at the core's 2.4 HDR: the brightest channel
        /// saturates (added light ≥ 1) for |v| ≤ 0.711 of the quad at the head — A5 measures that part, not the empty quad; 0.72 keeps
        /// the 8 px through pixel rounding (Wp104B4VfxPlayTests.A5 measures it).</summary>
        public const float CoreActiveShare = .72f;
        /// <summary>The quad ceiling whose bright part is <see cref="CoreCapPx"/> (≈ 11.1 px at 1080p).</summary>
        public const float CoreQuadCapPx = CoreCapPx / CoreActiveShare;

        /// <summary>
        /// K3: the ProjectileState.TreeFlags bit of the round that carries the chaos core's next hit — S-04's
        /// <see cref="RunTreeHooks.TreeRoundMarkedChaos"/> (16); S-02's B2 sets it from TreeVolleyPlan.Marked on the pull's main
        /// projectile (volley 0, pellet 0) when the nuke is armed or the black hole is off cooldown. Bits 1 / 2 / 4 / 8 are the
        /// retiring capstone tags.
        /// </summary>
        public const int MarkedRoundFlag = RunTreeHooks.TreeRoundMarkedChaos;
        /// <summary>WP-101's core head HDR (TdCoreHead × 2.4) and tail (× 1.1) kept for every palette; the pellet bar's 1.6.</summary>
        public const float CoreHdr = 2.4f, TailHdr = 1.1f, PelletHdr = 1.6f;
        /// <summary>K3: the marked round's violet edge on the additive core layer (every tier, Performance too).</summary>
        public const float MarkedEdgeHdr = 1.6f;
        /// <summary>K3 sumi core: a share of the core width (AD-B4 M4: 0.85 — a dark line that reads, the violet core a thin rim on
        /// both sides; 0.55 was a 2 px hair), α, the 3 px A5 floor.</summary>
        public const float InkWidthShare = .85f, InkAlpha = .9f, InkMinPx = 3f;
        /// <summary>AD-B4 M3: the halo α's Reduce Flashes factor — 1, FlashScale never touches it (a steady trail is no flash: × 0.4
        /// cost the Reduce Flashes streak its form). The AD's floor is 0.70.</summary>
        public const float HaloAlphaFlashFactor = 1f;
        /// <summary>AD-B4 M3: FlashScale dims the halo's HDR down to this (Reduce Flashes' 0.4) — below it a premultiplied halo would
        /// turn into a dark veil.</summary>
        public const float HaloHdrFlashFloor = .4f;
        /// <summary>
        /// AD-B4 M5: an element (and the K3 violet) halo's coverage gain outside its core — DD/VFX Ribbon <c>_TailColor.a</c>: the
        /// premultiplied α that hides the ground is α × (1 + gain × smoothstep(0.15, 0.35, |v|)) while the colour it adds stays
        /// rgb × α. Over the light stone a pale glow (#ADC6FF) over-exposed to near-white (coloured share 0.38); covering the stone
        /// it keeps its hue (CPU model of the AD frames: ≥ 0.65 for the four elements at I and V, the wet slate ±5 %). Any halo's
        /// core lies inside |v| ≤ 0.25 and keeps WP-101's veil; the neutral WP-101 halo takes none (AD accepted).
        /// </summary>
        public const float ElementHaloCover = 2.5f;
        /// <summary>K4: separate tracers per trigger pull; S-02's extra volleys are body streaks, so a pull draws one tracer and
        /// K4 holds without the B9 wedge (the wedge is B4-2).</summary>
        public const int SeparateTracersMax = 5;
        /// <summary>
        /// S-02 B2 (12.10): extra volley rounds (VolleyIndex &gt; 0) hold at most this many of the 48 TD records, so the main rounds
        /// always keep 16; one past it MERGES into its pull's main record (× <see cref="MergeFactor"/>, as S-02 merges shots).
        /// </summary>
        public const int VolleyRecordsMax = 32;

        // linear shader colours per look (index = look); 0 is never read (the WP-101 colour passes through)
        private static readonly Color[] s_head = new Color[LookCount];
        private static readonly Color[] s_tail = new Color[LookCount];
        private static readonly Color[] s_halo = new Color[LookCount];
        private static readonly Color[] s_core = new Color[LookCount];
        private static readonly Color s_ink;

        private static int s_version = int.MinValue;
        private static bool s_preview;
        private static bool s_previewDirty;
        private static TreeStats s_previewStats;
        private static float s_haloPx, s_trailScale = 1f, s_haloHdr = 1f;
        private static int s_look;
        private static bool s_styled;
        private static int s_shotTier, s_haloTier;
        private static TreeElement s_element;

        static TracerStyle()
        {
            Fill(FireLook, TreeVfxLadder.FireCore, TreeVfxLadder.FireGlow);
            Fill(IceLook, TreeVfxLadder.IceCore, TreeVfxLadder.IceGlow);
            Fill(ShockLook, TreeVfxLadder.ShockCore, TreeVfxLadder.ShockGlow);
            Fill(VenomLook, TreeVfxLadder.VenomCore, TreeVfxLadder.VenomGlow);
            // K3: violet edge on the additive core layer, violet halo; the sumi core is its own premultiplied instance (Ink)
            Color edge = TreeVfxLadder.Linear(TreeVfxLadder.ChaosEdge);
            s_head[MarkedLook] = edge * MarkedEdgeHdr;
            s_tail[MarkedLook] = TreeVfxLadder.Linear(TreeVfxLadder.ChaosBase) * TailHdr;
            s_halo[MarkedLook] = edge;
            s_core[MarkedLook] = edge;
            s_ink = TreeVfxLadder.Linear(TreeVfxLadder.ChaosInk);
        }

        private static void Fill(int look, Color coreSrgb, Color glowSrgb)
        {
            Color core = TreeVfxLadder.Linear(coreSrgb), glow = TreeVfxLadder.Linear(glowSrgb);
            s_head[look] = core * CoreHdr;   // the white-hot element core at the bullet (WP-101 head HDR)
            s_tail[look] = glow * TailHdr;   // the streak cools into the element glow (WP-101 tail HDR)
            s_halo[look] = glow;             // premultiplied halo (× TdHaloHdr × HaloHdr, α TdHaloAlpha, covering the ground: M5)
            s_core[look] = core;             // body streak / pellet bar: the pale element core (one bullet, one colour family)
        }

        // ---------------------------------------------------------------- readouts (tests / F1)

        /// <summary>Retired AD-VISUAL §2 channels (AD-B4 §1: the core is no tier channel; the halo keeps its WP-101 metre width and the
        /// tier adds <see cref="HaloPx"/>): always 1 — the AD capture still logs them.</summary>
        public static float CoreScale => 1f;
        public static float HaloScale => 1f;
        /// <summary>The halo's tier step (px at 1080p) a tracer born now takes (max(Shot, Element)).</summary>
        public static float HaloPx { get { Refresh(); return s_haloPx; } }
        /// <summary>The trail factor a tracer born now takes (Shot).</summary>
        public static float TrailScale { get { Refresh(); return s_trailScale; } }
        /// <summary>The halo's HDR boost before FlashScale (S1: <see cref="TreeVfxLadder.HaloHdrV"/> at V, else 1).</summary>
        public static float HaloHdrBoost { get { Refresh(); return s_haloHdr; } }
        public static int Look { get { Refresh(); return s_look; } }
        public static bool Styled { get { Refresh(); return s_styled; } }
        public static int ShotTier { get { Refresh(); return s_shotTier; } }
        public static int HaloTier { get { Refresh(); return s_haloTier; } }
        public static TreeElement DominantElement { get { Refresh(); return s_element; } }
        public static bool PreviewActive => s_preview;

        // ---------------------------------------------------------------- the TD hook

        /// <summary>
        /// WP-101 TdLook's last step: the round's metre widths × <paramref name="size"/> (a tracer's core and halo, a body streak's
        /// width), its palette and core ceiling (<paramref name="capPx"/> 0 = none) and — a tracer only — the tier's halo step
        /// (<paramref name="haloPx"/>, px at 1080p), trail factor and halo HDR boost. A neutral round of size 1 is WP-101 to the byte.
        /// </summary>
        public static void ApplyTd(ref float core, ref float halo, ref int look, ref float capPx, ref float haloPx, ref float trailScale,
            ref float haloHdr, float size, bool tracer)
        {
            Refresh();
            if (size <= 0f) size = 1f;
            core *= size;   // AD-B4 §1: the core is no tier channel - only the round's own size (its quad capped below)
            if (tracer) halo *= size;
            haloPx = tracer ? s_haloPx : 0f;
            trailScale = tracer ? s_trailScale : 1f;
            haloHdr = tracer ? s_haloHdr : 1f;
            look = s_look;
            capPx = s_styled || Mathf.Abs(size - 1f) > 1e-4f ? CoreQuadCapPx : 0f;
        }

        /// <summary>K3: the marked chaos round — a full tracer in the violet palette from the frame its tag is seen, with the V halo
        /// (base + 20 px, AD-B4 M4) whatever its tier and its own HDR (V's bloom boost belongs to the tier, not to the mark).</summary>
        public static void MarkTd(ref int look, ref float capPx, ref float haloPx, ref float haloHdr)
        {
            look = MarkedLook;
            capPx = CoreQuadCapPx;
            haloPx = TreeVfxLadder.HaloStepPx(TreeVfxLadder.TierMax);
            haloHdr = 1f;
        }

        public static bool IsMarkedRound(int treeFlags) => (treeFlags & MarkedRoundFlag) != 0;

        /// <summary>A TD record that took <paramref name="merged"/> extra volley rounds widens like S-02's merged shot: √(1 + n),
        /// at most <see cref="TreeHooks.MergedSizeMax"/> (×1.25).</summary>
        public static float MergeFactor(int merged) => merged <= 0 ? 1f : Mathf.Min(TreeHooks.MergedSizeMax, Mathf.Sqrt(1f + merged));

        public static Color CoreHead(int look, Color neutral) => look > NeutralLook && look < LookCount ? s_head[look] : neutral;

        public static Color CoreTail(int look, Color neutral) => look > NeutralLook && look < LookCount ? s_tail[look] : neutral;

        /// <summary>The heat halo's colour (TracerRibbonRenderer multiplies it by TdHaloHdr × <see cref="HaloHdr"/>, as WP-101's).</summary>
        public static Color Halo(int look, Color neutral) => look > NeutralLook && look < LookCount ? s_halo[look] : neutral;

        /// <summary>AD-B4 M3: the halo's α factor under FlashScale — <see cref="HaloAlphaFlashFactor"/> (1) for every look: FlashScale
        /// dims the halo's HDR (<see cref="HaloHdr"/>), never its α. Look and flash stay in the signature (the AD capture's M3 probe
        /// reads it).</summary>
        public static float HaloFlash(int look, float flash) => HaloAlphaFlashFactor;

        /// <summary>
        /// AD-B4 M3 / S1: the halo's HDR factor (× TdHaloHdr) — <paramref name="boost"/> (V's ×1.5) at FlashScale 1, gone by Reduce
        /// Flashes' 0.4, then × FlashScale down to <see cref="HaloHdrFlashFloor"/>. Under Reduce Flashes the WP-101 halo's 2.2 drops to
        /// 0.88: no bloom, and its centre no longer clips white over the core, so the streak's peak falls while its α keeps the form.
        /// </summary>
        public static float HaloHdr(float boost, float flash)
        {
            float f = Mathf.Clamp01(flash);
            float b = boost > 1f ? Mathf.Lerp(1f, boost, Mathf.InverseLerp(HaloHdrFlashFloor, 1f, f)) : 1f;
            return b * Mathf.Max(f, HaloHdrFlashFloor);
        }

        /// <summary>The halo's coverage gain (DD/VFX Ribbon <c>_TailColor.a</c>): <see cref="ElementHaloCover"/> for an element or
        /// the marked violet halo, 0 for the neutral WP-101 halo.</summary>
        public static float HaloCover(int look) => look > NeutralLook && look < LookCount ? ElementHaloCover : 0f;

        /// <summary>A body streak (premultiplied): the element's pale core at the WP-101 colour's own intensity.</summary>
        public static Color Body(int look, Color neutral) => Pale(look, neutral);

        /// <summary>A pellet bar (added): the element's pale core at the WP-101 bar's intensity.</summary>
        public static Color Pellet(int look, Color neutral) => Pale(look, neutral);

        private static Color Pale(int look, Color neutral)
        {
            if (look <= NeutralLook || look >= LookCount) return neutral;
            float gain = Mathf.Max(neutral.r, Mathf.Max(neutral.g, neutral.b));
            Color c = s_core[look] * gain;
            c.a = neutral.a;
            return c;
        }

        /// <summary>K3: the sumi ink core's linear colour (#12100F).</summary>
        public static Color Ink => s_ink;

        /// <summary>A tracer's streak length (m): the WP-101 one-exposure trail × the Shot tier's factor, never past
        /// <see cref="TreeVfxLadder.TrailMaxMetres"/>; a factor ≤ 1 (WP-101, an unstyled record) returns the trail itself.</summary>
        public static float ScaledTrail(float wp101Trail, float trailScale) =>
            trailScale > 1f ? Mathf.Min(wp101Trail * trailScale, TreeVfxLadder.TrailMaxMetres) : wp101Trail;

        /// <summary>
        /// DD/VFX Ribbon <c>_Extra.z</c> of a TD instance: 0 = no ceiling (a neutral round, every tracer halo — AD-B4 §1 drops the fixed
        /// 64 px cap —, the sniper vapour, a plain pellet bar); else −<paramref name="capPx"/> (max px at 1080p) for a core / body
        /// streak. The shader's first-person flag is &gt; 0.5, so a negative value never collides with it.
        /// </summary>
        public static float ShaderCap(int layer, bool tracer, bool plainPellet, float capPx)
        {
            if (capPx <= 0f || layer == 1 || plainPellet || (layer == 2 && tracer)) return 0f;
            return -capPx;
        }

        /// <summary>
        /// DD/VFX Ribbon <c>_Extra.w</c> of a TD instance that has a ceiling: −(the instance's WP-101 width, m) — the ceiling's floor
        /// (the shader clamps to max(ceiling, floor), so a styled round is never narrower than WP-101 drew it); 0 without a ceiling.
        /// The head-dot flag is w &gt; 0.5, so a negative value never collides with it. <paramref name="baseWidth"/> is the round's
        /// WP-101 core / body width (m).
        /// </summary>
        public static float ShaderBase(int layer, bool tracer, bool plainPellet, float capPx, float baseWidth)
        {
            if (ShaderCap(layer, tracer, plainPellet, capPx) == 0f || baseWidth <= 0f) return 0f;
            return -baseWidth;
        }

        /// <summary>DD/VFX Ribbon <c>_Extra.y</c> of a TD instance: −(a tracer halo's tier step, px at 1080p) — the shader adds it to
        /// the metre width, resolution-proportional and exact at any camera distance; 0 elsewhere (the head-dot size is ≥ 0, so a
        /// negative value never collides with it).</summary>
        public static float ShaderStep(int layer, bool tracer, float haloPx) => layer == 2 && tracer && haloPx > 0f ? -haloPx : 0f;

        /// <summary>The shader's clamp in pixels (tests / F1): min(width, max(ceiling, floor)) — what DD/VFX Ribbon draws at 1080p for
        /// a quad of <paramref name="widthPx"/> under <c>_Extra.z</c> / <c>_Extra.w</c> converted at <paramref name="pxPerMetre"/>.</summary>
        public static float ClampedPx(float widthPx, float shaderCap, float shaderBase, float pxPerMetre)
        {
            if (shaderCap >= 0f) return widthPx;
            return Mathf.Min(widthPx, Mathf.Max(-shaderCap, -Mathf.Min(shaderBase, 0f) * pxPerMetre));
        }

        /// <summary>The shader's halo width in pixels (tests / F1): its metre width at <paramref name="pxPerMetre"/> + the tier step
        /// (−<c>_Extra.y</c>) scaled from 1080p to <paramref name="screenHeight"/>.</summary>
        public static float HaloWidthPx(float haloMetres, float shaderStep, float pxPerMetre, float screenHeight = 1080f) =>
            haloMetres * pxPerMetre + Mathf.Max(-shaderStep, 0f) * screenHeight / 1080f;

        // ---------------------------------------------------------------- AP-041 X5 fan wedge (B4-2 helper)

        /// <summary>
        /// The B9 wedge source (15 / 30 / 60°) for a fan of <paramref name="arcDegrees"/>: the largest source not wider than the arc
        /// (at least 15°), its quad height stretched by tan(arc/2) / tan(source/2) — GD sh_fan 40° draws the 30° source × tan20 / tan15.
        /// </summary>
        public static int FanWedgeSource(float arcDegrees, out float worldYScale)
        {
            float arc = Mathf.Clamp(arcDegrees, 1f, 170f);
            int source = arc >= 60f ? 60 : arc >= 30f ? 30 : 15;
            worldYScale = Mathf.Tan(arc * .5f * Mathf.Deg2Rad) / Mathf.Tan(source * .5f * Mathf.Deg2Rad);
            return source;
        }

        // ---------------------------------------------------------------- preview (S-09 diorama)

        /// <summary>Rounds born from now on take <paramref name="stats"/>'s look (the diorama's "before" / "after" halves).</summary>
        public static void SetPreview(in TreeStats stats)
        {
            s_previewStats = stats;
            s_preview = true;
            s_previewDirty = true;
        }

        public static void ClearPreview()
        {
            if (!s_preview) return;
            s_preview = false;
            s_version = int.MinValue;   // back to the run's stats on the next read
        }

        // ---------------------------------------------------------------- cache

        private static void Refresh()
        {
            if (s_preview)
            {
                if (!s_previewDirty) return;
                s_previewDirty = false;
                Compute(in s_previewStats);
                return;
            }

            int v = TreeRuntime.Version;
            if (v == s_version) return;
            s_version = v;
            Compute(in TreeRuntime.Current);
        }

        private static void Compute(in TreeStats s)
        {
            s_shotTier = TreeVfxLadder.BranchTier(in s, TreeBranch.Shot);
            s_element = TreeVfxLadder.DominantElement(in s);
            int elementTier = s_element == TreeElement.None ? 0 : Mathf.Max(1, TreeVfxLadder.BranchTier(in s, TreeBranch.Element));
            s_haloTier = Mathf.Max(s_shotTier, elementTier);
            s_haloPx = TreeVfxLadder.HaloStepPx(s_haloTier);            // AD-B4 §1: owner max(Shot, Element)
            s_trailScale = TreeVfxLadder.TrailScale(s_shotTier);         // owner Shot only
            s_haloHdr = s_haloTier >= TreeVfxLadder.TierMax ? TreeVfxLadder.HaloHdrV : 1f;   // S1
            s_look = LookOf(s_element);
            s_styled = s_look != NeutralLook || s_haloPx > 0f || s_trailScale > 1f || s_haloHdr > 1f;
        }

        public static int LookOf(TreeElement e)
        {
            switch (e)
            {
                case TreeElement.Fire: return FireLook;
                case TreeElement.Ice: return IceLook;
                case TreeElement.Shock: return ShockLook;
                case TreeElement.Venom: return VenomLook;
                default: return NeutralLook;
            }
        }

        /// <summary>Tests / domain reload: forget the cache and any preview.</summary>
        public static void ResetForTests()
        {
            s_preview = false;
            s_previewDirty = false;
            s_version = int.MinValue;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ResetForTests();
    }
}
