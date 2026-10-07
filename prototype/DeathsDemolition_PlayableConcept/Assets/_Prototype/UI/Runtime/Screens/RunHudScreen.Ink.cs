using System.Collections.Generic;
using DD.Prototype.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8-R · the HUD as ink objects: concept A "Mühür ve Fırça", chosen by the user on 24.09
    /// (council/8r, ad-slice8r). The user's verdict on the panels was "too big, all rectangles, amateur, covers the
    /// screen → minimalist, with depth", so no panel, box or label word is left. The corners keep their posts:
    /// - Gold (WP-094: bottom-left, directly above the health cluster - the coins fly to it): the coin on a small kinpaku
    ///   disc, the numeral, "+N"; the coin's flight target rect beside it (HudGoldTarget).
    /// - Round (WP-094: top-right): the round as a Roman numeral (HudRoundNumeral) over an ink smear, a small vermilion
    ///   seal at the corner. The enemies count and its calm breakdown are gone from the player's view (the user 07.10);
    ///   the threat chips moved up into the freed top-centre slot at InkMargin (S-09 decision (d)).
    /// - Health (bottom-left): heart, value, "/ max" in calm, LOW, and a segmented brush stroke that leans toward the
    ///   centre; "recovery in" under it.
    /// - Weapon (bottom-right): a lacquer ring (Ø 60) round the weapon glyph, the count ("24 / 30"), and the ladder on an
    ///   ink brush that leans toward the centre. LOW / EMPTY with the live reload prompt (or SUSPENDED) sits above; the
    ///   weapon and build line shows in calm.
    /// - Special (bottom-centre): the A4 132 ring, no panel.
    /// Numbers and words carry their own ink (UiHudInk: HudInk outline α 0.98 + a soft underlay); glyphs an SDF ink
    /// edge. Measured persistent coverage ≈ 1 % of 1080p, against 10.48 % for the panels (HudCoverageTests caps it).
    ///
    /// DEPTH (the user's "derinlik"; AD 8-R ElevInk):
    /// - Every form (seal, stroke, ring, brush, disc) sits on its cluster's back plane: a contact shadow in two layers
    ///   (HudInk 0.35 at (2, −3), 0.15 at (4, −6)) and a 1 px cream rim light on its upper-left.
    /// - Parallax from camera motion: the back planes drift up to 3 px against the camera (Follow 0.12), while numbers,
    ///   glyphs and ticks stay still, so the two planes separate. None under reduced motion.
    /// - Strokes lean 3° toward the centre; digits stay level.
    /// POSTURE (ID 8-R): calm shows the T2 lines and lets the health and weapon clusters recede to 0.94. Danger (a chip,
    /// low health, low or empty magazine, a hit in the last 3 s) hides T2 and brings them forward. Text alpha never
    /// changes.
    /// </summary>
    public sealed partial class RunHudScreen
    {
        private const float InkMargin = 32f;
        private const float StrokeLean = 3f;
        private const float HealthLean = 2.5f;       // the long stroke: 320 px at 2.5° already rises 14 px
        private const float CalmScale = 0.94f;
        private const float DangerHoldSeconds = 0.8f;
        private const float HitDangerSeconds = 3f;
        private const float WeaponRing = 60f;          // AD 8-R SHOULD-A1: 72 → 60, glyph 32
        private const float WeaponGlyph = 32f;
        /// <summary>E12 / 8-R: the kinpaku disc under the coin (UiArtThemeTests.HudKontrasti measures the coin over it).</summary>
        public const float GoldDiscAlpha = 0.16f;
        private const float SealDrift = 2f / 3f;      // AD 8-R · A MUST-1: a stamped object drifts as one, ≤ 2 px
        private const float ParallaxReach = 3f;       // px, the back plane's furthest drift
        private const float ParallaxPerMetre = 0.5f;  // px per m/s of camera motion
        private const float ParallaxHalfLife = 0.12f;
        private const float ShadowNearAlpha = 0.35f;
        private const float ShadowFarAlpha = 0.15f;
        private const float RimAlpha = 0.10f;
        private static readonly Vector2 ShadowNear = new Vector2(2f, -3f);
        private static readonly Vector2 ShadowFar = new Vector2(4f, -6f);
        private static readonly Vector2 RimOffset = new Vector2(-1f, 1f);
        private static readonly Vector3 GlyphInk = new Vector3(0.25f, 0f, 0f);

        private TMP_Text _healthMaxText;
        private int _healthMaxShown = int.MinValue;
        private CanvasGroup _reloadCaptionGroup;
        private readonly List<CanvasGroup> _calmOnly = new List<CanvasGroup>(4);
        private readonly List<RectTransform> _postureClusters = new List<RectTransform>(2);
        private readonly List<RectTransform> _backPlanes = new List<RectTransform>(4);
        private readonly List<float> _backPlaneDepth = new List<float>(4);
        private int _posture = -1;           // 0 calm, 1 danger
        private float _dangerSeenAt = -10f;
        private float _lastHitAt = -10f;
        private Vector2 _parallax;
        private Vector2 _parallaxShown = new Vector2(float.NaN, float.NaN);
        private Vector3 _parallaxCameraAt;
        private float _parallaxTime = -1f;

        /// <summary>The HUD's corner clusters (the safe-zone and coverage tests; nothing else reads it).</summary>
        public IReadOnlyList<RectTransform> Clusters => _clusters;

        /// <summary>The back planes' current parallax drift in canvas px (test readout).</summary>
        public Vector2 Parallax => _parallax;

        // ================================================================ build

        private void BuildInkClusters()
        {
            BuildInkGold();
            BuildInkRound();
            BuildBrushHealth();
            BuildRingWeapon();
            BuildInkSpecial();
            BuildInkSlick();
        }

        // ---- WP-094 HUD v4 (R3, C-00 07.10): the gold over the health stroke, the round at the top right

        public const float GoldClusterWidth = 170f;
        public const float GoldClusterHeight = 36f;

        /// <summary>
        /// WP-094 AP-032: the koban's body in ref px. The spec proposes 48 at centre (56, ≈ 165); S-09 08.10 keeps the
        /// HudGoldTarget point where it is (the coin's centre at x 18 in the cluster, its vertical middle), and 40 is the
        /// largest body whose ink stays inside the 32 px margin there and within 2 px of the cluster's height. The §1 layer:
        /// 40 px → the 64 canvas (body 48) at 1080, the 128 one on the Mac.
        /// </summary>
        public const float GoldCoinBody = 40f;

        /// <summary>The round cluster's least height (the old seal line's); the numeral's line sets it when taller.</summary>
        public const float RoundClusterHeight = 56f;

        /// <summary>
        /// The round's seal accent at the corner. The user asked for "ince altın ya da kırmızı mühür aksanı"; S-09 decision (a)
        /// (C-00 07.10, the AD confirms at delivery): VERMILION (#C4362B, AccentPrimary), as AP-032 §3.2 tints its seal cell -
        /// gold stays money's and the Special's (HAREKET-HUD: no kinpaku on the round). The HUD's one vermilion seal is a named
        /// exception in UiOrnamentCensusTests and this file is on AccentPrimary's reserve list (UiTokensV3Tests). The AP-032
        /// seal cell (theme key <see cref="RoundSealKey"/>) replaces the hanko mask when it lands.
        /// </summary>
        public const float RoundSealSize = 32f;
        public const float RoundSealAlpha = 0.9f;

        /// <summary>The AP-032 seal cell's theme key (§3.3); the kit's hanko mask until it resolves.</summary>
        public const string RoundSealKey = "Roman.Seal";

        /// <summary>HAREKET-HUD R4: at a round's end the seal "dries" to this share of its alpha; the next stamp wets it again.</summary>
        public const float RoundSealDryAlpha = 0.7f;

        /// <summary>The numeral clip's margin round the letters: the HUD ink's outline and underlay never meet the mask's edge.</summary>
        private const float RoundClipPad = 8f;

        /// <summary>AP-032's smear plate takes the HUD ink at this alpha (§3.3 "kodda ink, α ≤ 0,90"), level (it is laid out, not leaned).</summary>
        public const float RoundSmearAuthoredAlpha = 0.9f;

        /// <summary>The one brush channel the HUD's strokes draw with (the brush census counts this site once).</summary>
        private const UiInkChannel BrushChannel = UiInkChannel.Underline;

        /// <summary>The S-08 cue at the seal's contact frame (HAREKET-HUD R3): UiEvent.StampLanded with this detail - no new UiEvent.</summary>
        public const string RoundSealCueDetail = "round";

        /// <summary>The ink smear under the numeral: HudInk at this alpha, this tall, reaching this far past the first letter (and to the seal's edge on the right).</summary>
        public const float RoundSmearAlpha = 0.6f;
        public const float RoundSmearHeight = 30f;
        public const float RoundSmearOverhang = 16f;

        /// <summary>
        /// The AP-032 letters' base cap height on the glyph path (layout px). 08.10 (S-09: "cap 44→40" lever, the 1.5 % coverage
        /// cap): 40 - below the spec's 44–56 (AP-032 §3.2; the 512 atlas's cap 80 is then 2× down, AD note 5), the plate 9 %
        /// smaller with it; a long numeral fits the plate (HudArt.FitCap, floor 32).
        /// </summary>
        public const float RoundGlyphHeight = 40f;

        /// <summary>The fallback numeral's type: the display face (H1, 34) - not text-scaled, like every HUD numeral.</summary>
        private const UiType RoundNumeralType = UiType.H1;

        private const float HealthStrokeLength = 320f;
        private const float HealthStrokeHeight = 26f;
        private const float HealthStrokeCentre = 32f;

        private RectTransform _goldCluster;
        private RectTransform _roundCluster;
        private Vector2 _roundClusterHome;
        private Image _roundSmear;
        private UiFx _roundSmearFx;
        private RectTransform _roundSeal;
        private Image _roundSealImage;
        private UiFx _roundSealFx;
        private RectTransform _roundClip;
        private CanvasGroup _roundGroup;
        private RectTransform _roundBox;
        private bool _roundSmearAuthored;
        private bool _roundSealAuthored;
        private RectTransform _roundSealHost;
        private float _roundRunRight;   // the numeral's end, from the cluster's right edge (the seal and its gap, or the plate's tail too)
        private HudRoundNumeral _roundNumeral;
        private float _roundSealHitAt = -1f;
        private UiTimeline _roundTimeline;
        private int _roundPending;
        private Image _goldFlare;
        private Image _goldCatch;
        private UiFx _goldCatchFx;

        /// <summary>G1 under reduced motion: the flare twin's alpha - over the 0.16 disc it composites to 0.35 (1 − 0.84 × 0.774).</summary>
        public const float GoldFlareAlpha = 0.226f;

        /// <summary>G5: the catch ring's ink (≤ 0.35) and its growth (Ø 36 → 52).</summary>
        public const float GoldCatchAlpha = 0.35f;
        public const float GoldCatchFrom = 36f;
        public const float GoldCatchTo = 52f;

        /// <summary>The round numeral (tests, captures).</summary>
        public HudRoundNumeral RoundNumeral => _roundNumeral;

        /// <summary>The round's seal accent (tests).</summary>
        public RectTransform RoundSeal => _roundSeal;

        /// <summary>Unscaled time the round seal's hit is due (the reveal's contact frame; −1 before the first reveal). Tests, captures.</summary>
        public float RoundSealHitAt => _roundSealHitAt;

        /// <summary>WP-094 AP-032: the round stands on AP-032's smear plate (the cluster is the plate; tests, captures).</summary>
        public bool RoundPlateAuthored => _roundSmearAuthored;

        /// <summary>The numeral's cap height now: the base, or smaller where the plate fit shrank it (tests, captures).</summary>
        public float RoundCap => _roundNumeral != null ? _roundNumeral.Height : RoundGlyphHeight;

        /// <summary>The round transition (R1 → R3) is running (tests, captures).</summary>
        public bool RoundTransitionLive => _roundTimeline.IsAlive;

        /// <summary>The numeral's reveal clip (tests: its width opens 0 → full in R2).</summary>
        public RectTransform RoundClip => _roundClip;

        /// <summary>
        /// The health cluster's height (layout px) at the current text scale: the leaning stroke's highest point, the value row
        /// centred a NumericL band above it, half a band and 4 px over the row. BuildBrushHealth lays out from the same numbers.
        /// </summary>
        public static float HealthClusterHeight()
        {
            float strokeTop = HealthStrokeCentre + HealthStrokeHeight * 0.5f + HealthStrokeLength * Mathf.Tan(HealthLean * Mathf.Deg2Rad);
            float band = UiFactory.LineHeight(UiType.NumericL);
            return strokeTop + 2f + band + 4f;
        }

        /// <summary>
        /// WP-094: the gold cluster's bottom edge, canvas px from the bottom - directly above the health cluster (top-down and
        /// third person) or above the first-person weapon cluster that stands there, one UiSpace.S clear, on the 8 px grid.
        /// The gap is measured from the health cluster's LAID-OUT top (<see cref="HealthClusterHeight"/>, the danger posture's
        /// scale 1): UiSpace.S plus the grid's remainder, so 8 ≤ gap &lt; 16 px at every text scale. The calm posture draws the
        /// health cluster at CalmScale (0.94) about its bottom-left corner, so its DRAWN top sits up to 6 % of its height lower
        /// (≈ 6 px at 1080) - the gold does not follow it: it is the coins' flight target and holds one place on the grid
        /// (HudGoldTarget.LayoutVersion moves only on a real layout change, never with the posture). r3-w1: HudV4Tests had
        /// measured the calm-drawn top against the layout bound (17.09 &gt; 16 px); it now measures the laid-out top.
        /// </summary>
        public static float GoldClusterBottom(bool firstPerson)
        {
            float under = firstPerson ? FpWeaponClusterBottom + WeaponClusterHeight : InkMargin + HealthClusterHeight();
            return Mathf.Ceil((under + (int)UiSpace.S) / 8f) * 8f;
        }

        // ---- bottom-left gold, above the health: kinpaku disc, coin, numeral, "+N", the flight target

        private void BuildInkGold()
        {
            RectTransform root = Cluster("Gold", Vector2.zero, new Vector2(InkMargin, GoldClusterBottom(false)), new Vector2(GoldClusterWidth, GoldClusterHeight), false);
            _goldCluster = root;
            // WP-094 (HAREKET-HUD "ada kuralı"): the counter rolls and the coin punches frame by frame - its own nested canvas
            // re-batches only the gold, never the main HUD canvas.
            root.gameObject.AddComponent<Canvas>();
            RectTransform back = BackPlane(root);

            // The one leaf the HUD carries (E12): a small kinpaku disc under the coin; S-07's Leaf glint sweeps it on a gain.
            // WP-094 AP-032: the full-colour koban carries its own metal - the disc gives way and the glint sweeps the coin.
            Sprite koban = HudArt.GoldCoin(GoldCoinBody * HudArt.CanvasScale);
            Image disc = UiFactory.NewImage("Leaf", back, UiColor.StateGold, UiSprites.Disc);
            Place(disc.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(36f, 36f));
            Fade(disc, GoldDiscAlpha);
            if (koban == null)
            {
                _goldLeaf = disc.gameObject.AddComponent<UiFx>();
                _goldLeaf.Configure(UiFxMode.Leaf, GoldGlintSeconds, new Vector3(1f, 0f, 0f));
            }
            else
            {
                disc.enabled = false;
            }

            // WP-094 (HAREKET-HUD G1 under reduced motion: "ölçeksiz parlama"): a twin of the disc that flares the leaf 0.16 → 0.35
            // → 0.16 instead of the punch's scale. Resting at alpha 0, culled.
            _goldFlare = UiFactory.NewImage("LeafFlare", back, UiColor.StateGold, UiSprites.Disc);
            Place(_goldFlare.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(36f, 36f));
            Fade(_goldFlare, GoldFlareAlpha);
            Rest(_goldFlare);

            // WP-094 (G5): the catch ring under the disc - the hanko mark inking centre-out as it grows Ø 36 → 52 (HudInk ≤ 0.35).
            Sprite hankoMark = UiFxGlobals.InkMask(UiInkChannel.Hanko);
            _goldCatch = UiFactory.NewImage("Catch", back, UiColor.HudInk, hankoMark != null ? hankoMark : UiSprites.Ring);
            Place(_goldCatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(GoldCatchFrom, GoldCatchFrom));
            Fade(_goldCatch, GoldCatchAlpha);
            _goldCatch.transform.SetAsFirstSibling();
            Rest(_goldCatch);
            _goldCatchFx = _goldCatch.gameObject.AddComponent<UiFx>();
            _goldCatchFx.Configure(UiFxMode.InkReveal, UiDuration.Screen, Vector3.zero, UiInkChannel.Hanko);

            Image coin;
            if (koban != null)
            {
                // AP-032 v001 (§3.1, AD ACCEPT 08.10): the koban - its light, rim and sumi edge baked, the 20° tilt baked (the
                // rect never turns). The §1 layer for its body on this screen (HudArt.GoldCoin); the G1 Hud.Gold glyph stays the
                // single-colour symbol elsewhere. The Leaf band sweeps the coin itself (HAREKET-HUD G4: over the RGBA coin).
                coin = UiFactory.NewImage("Coin", root, UiColor.TextPrimary, koban);
                coin.color = Color.white;   // full-colour art: no tint
                coin.preserveAspect = true;
                float canvas = GoldCoinBody / HudArt.GoldBodyShare;
                Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(canvas, canvas));
                _goldLeaf = coin.gameObject.AddComponent<UiFx>();
                _goldLeaf.Configure(UiFxMode.Leaf, GoldGlintSeconds, new Vector3(1f, 0f, 0f));
            }
            else
            {
                coin = InkGlyph("Coin", root, "Hud.Gold", UiColor.StateGold, 24f);
                Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(24f, 24f));
            }

            _goldCoin = coin.rectTransform;

            // WP-094: where the coins land - the coin's own rect as a bare sibling, so the arrival punch (which scales the
            // coin) never moves the point HudGoldTarget reports.
            _goldTarget = Place(UiFactory.NewRect("FlightTarget", root), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(24f, 24f));

            _goldCounter = UiCounter.Create("Value", root, UiCounterStyle.Currency, 0, UiType.NumericM, UiColor.StateGold);
            RectTransform valueRect = UiFactory.Fill((RectTransform)_goldCounter.transform);
            valueRect.offsetMin = new Vector2(44f, 0f);
            _goldValue = _goldCounter.ValueText;
            InkAll(_goldCounter.gameObject);

            _goldDelta = InkText("Delta", root, string.Empty, UiType.Label, UiColor.StateGold, TextAnchor.MiddleLeft);   // AD 8-R · A SHOULD-1: the 14 px floor
            _goldDelta.fontStyle = FontStyles.Bold;
            UiHudInk.Apply(_goldDelta);
            _goldDeltaHome = new Vector2(112f, 0f);
            Place(_goldDelta.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), _goldDeltaHome, new Vector2(72f, UiFactory.LineHeight(UiType.Label)));
            _goldDelta.canvasRenderer.SetAlpha(0f);
        }

        // ---- top-right: the round as a Roman numeral over an ink smear, the seal accent at the corner

        /// <summary>
        /// WP-094 (the user 07.10: "round sayısı üst ortada değil sağ üstte ... fırça darbesi tasarım diliyle romen
        /// rakamlarıyla"): anchored top-right, InkMargin in from both edges, growing leftward with the numeral. Right to left:
        /// the vermilion seal (the hanko mask until AP-032's cell, S-09 decision (a)), UiSpace.S, the numeral (HudRoundNumeral:
        /// AP-032 brush letters when the set is in, the display-face line today) in its reveal clip over an HudInk smear from the
        /// seal's edge to RoundSmearOverhang past the first letter, leaning toward the centre like every HUD stroke. Smear,
        /// letters and seal are one impression: they ride one back plane at the stamped drift (AD 8-R · A MUST-1). The seal is
        /// drawn last, over the smear's tail. WP-094 juice: the round change is the R1 → R3 transition (PlayRoundTransition).
        /// </summary>
        private void BuildInkRound()
        {
            float height = Mathf.Max(RoundClusterHeight, UiFactory.LineHeight(RoundNumeralType));
            float letters = RoundSealSize + (int)UiSpace.S;   // the numeral's right edge, in from the cluster's
            RectTransform root = Cluster("Round", Vector2.one, new Vector2(-InkMargin, -InkMargin), new Vector2(letters + RoundSmearOverhang, height), false);
            _roundCluster = root;
            _roundClusterHome = root.anchoredPosition;
            // WP-094 (HAREKET-HUD "ada kuralı": the round seal): the transition moves the whole impression - its own canvas.
            root.gameObject.AddComponent<Canvas>();
            RectTransform back = BackPlane(root, SealDrift);

            // WP-094 AP-032 (§3.3): the 3-slice smear plate, centre Tiled at its period, when it is in; the AP-012 brush before.
            float scale = HudArt.CanvasScale;
            float plateNative;
            Sprite plate = HudArt.Smear(RoundGlyphHeight / HudArt.SmearCapRatio * scale, out plateNative);
            _roundSmearAuthored = plate != null;
            float sealCentre = RoundSealSize * 0.5f, sealSize = RoundSealSize;
            _roundRunRight = letters;
            if (_roundSmearAuthored)
            {
                float plateWidth, plateHeight, sealDiameter;
                HudArt.LaySmear(RoundGlyphHeight, 0f, 0f, out plateWidth, out plateHeight, out _roundRunRight, out sealCentre, out sealDiameter);
                _roundSmear = UiFactory.NewImage("Smear", back, UiColor.HudInk, plate);
                _roundSmear.type = Image.Type.Tiled;
                _roundSmear.pixelsPerUnitMultiplier = plateNative / plateHeight;   // the plate's height maps to its native rows
                Place(_roundSmear.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -HudArt.SmearCapOffset(plateHeight)), new Vector2(plateWidth, plateHeight));
                Fade(_roundSmear, RoundSmearAuthoredAlpha);
                // R2: the plate inks in left to right over its own alpha (wipe), Emphasis.
                _roundSmearFx = _roundSmear.gameObject.AddComponent<UiFx>();
                _roundSmearFx.Configure(UiFxMode.InkReveal, UiDuration.Emphasis, new Vector3(0f, 1f, 0f), BrushChannel);
                sealSize = UiArt.Icon(RoundSealKey) != null ? sealDiameter / HudArt.SealRingShareOfCell : sealDiameter;
            }
            else
            {
                _roundSmear = UiFactory.NewImage("Smear", back, UiColor.HudInk, BrushSprite());
                // The smear runs from the seal's edge (under the gap) to RoundSmearOverhang past the first letter.
                Place(_roundSmear.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-RoundSealSize, -2f), new Vector2(RoundSmearOverhang + (int)UiSpace.S, RoundSmearHeight));
                _roundSmear.rectTransform.localRotation = Quaternion.Euler(0f, 0f, StrokeLean);
                Fade(_roundSmear, RoundSmearAlpha);
                // R2: the smear inks in behind the letters (InkReveal over the same brush's time map, Emphasis).
                _roundSmearFx = _roundSmear.gameObject.AddComponent<UiFx>();
                _roundSmearFx.Configure(UiFxMode.InkReveal, UiDuration.Emphasis, Vector3.zero, BrushChannel);
            }

            // R1 / R2: the numeral sits in a left-pivoted RectMask2D the brush reveal opens left to right (RomanReveal), its
            // group carrying the old numeral's exit. The clip is RoundClipPad wider than the letters on every side.
            _roundClip = UiFactory.NewRect("NumeralClip", back);
            Place(_roundClip, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-letters, 0f), new Vector2(0f, height + 2f * RoundClipPad));
            UiFactory.ClipContent(_roundClip);
            _roundGroup = UiFactory.Group(_roundClip.gameObject);
            _roundBox = UiFactory.NewRect("Numeral", _roundClip);
            Place(_roundBox, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(RoundClipPad, 0f), new Vector2(0f, height));
            _roundValue = InkText("RoundValue", _roundBox, RunHudViewModel.Placeholder, RoundNumeralType, UiColor.TextPrimary, TextAnchor.MiddleRight);
            UiFactory.Fill(_roundValue.rectTransform);
            _roundNumeral = new HudRoundNumeral(_roundBox, _roundValue, RoundGlyphHeight, UiColor.TextPrimary);
            _roundNumeral.SetGlyphAlternates(RoundGlyphAlternates(RoundGlyphHeight * scale));

            // The seal (S-09 (a): vermilion): the AP-032 cell when the theme has it (a white ring, the whole cell - the ring is
            // 72 / 256 of it), the kit's hanko mask until then. With the plate it sits 0.55 diameters inside the dense zone's
            // right end, at the cap's middle (§3.3, AD note 3); LayRound re-lays it when the plate fit changes the cap.
            Sprite authoredSeal = UiArt.Icon(RoundSealKey);
            _roundSealAuthored = authoredSeal != null;
            Sprite hanko = authoredSeal != null ? authoredSeal : UiFxGlobals.InkMask(UiInkChannel.Hanko);
            // The seal and its depth twins stand in one host the plate fit moves and sizes (08.10: the cap may shrink).
            RectTransform sealHost = Place(UiFactory.NewRect("SealHost", back), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-sealCentre, 0f), new Vector2(sealSize, sealSize));
            Image seal = UiFactory.NewImage("Seal", sealHost, UiColor.AccentPrimary, hanko != null ? hanko : UiSprites.Disc);
            UiFactory.Fill(seal.rectTransform);
            Fade(seal, RoundSealAlpha);
            Depth(seal);
            _roundSealHost = sealHost;
            _roundSeal = seal.rectTransform;
            _roundSealImage = seal;
            // R3: the ink spreads centre-out through the seal as it stamps (Hanko time map, Screen) - over the kit mask's own
            // mark, or (the AP-032 ring, whose shape is its alpha) as a wipe.
            _roundSealFx = seal.gameObject.AddComponent<UiFx>();
            _roundSealFx.Configure(UiFxMode.InkReveal, UiDuration.Screen, authoredSeal != null ? new Vector3(0f, 1f, 0f) : Vector3.zero, UiInkChannel.Hanko);

            LayRound(0);
        }

        /// <summary>
        /// The AP-032 letters with their alternates (HudArt, the §1 atlas for a cap of <paramref name="capPhysicalPx"/>), else the
        /// theme's one sprite per key (HudRoundNumeral.GlyphKeys); missing ones stay null and keep the text path.
        /// </summary>
        private static Sprite[][] RoundGlyphAlternates(float capPhysicalPx)
        {
            var alternates = new Sprite[HudRoundNumeral.GlyphKeys.Length][];
            for (int i = 0; i < alternates.Length; i++)
            {
                alternates[i] = HudArt.RomanAlternates(i, capPhysicalPx, null);
                if (alternates[i] == null)
                {
                    Sprite one = UiArt.Icon(HudRoundNumeral.GlyphKeys[i]);
                    alternates[i] = one != null ? new[] { one } : null;
                }
            }

            return alternates;
        }

        /// <summary>
        /// Shows <paramref name="round"/> (≤ 0: the placeholder). <paramref name="reveal"/>: the round CHANGED in a run - the
        /// R1 → R3 transition plays (<see cref="PlayRoundTransition"/>); otherwise (a run's first round, a rebuilt HUD) it is laid
        /// at once, landing any transition still running first.
        /// </summary>
        private void SetRound(int round, bool reveal)
        {
            if (_roundNumeral == null)
            {
                return;
            }

            if (reveal)
            {
                PlayRoundTransition(round);
                return;
            }

            if (_roundTimeline.IsAlive)
            {
                _roundTimeline.Complete();
            }

            _roundPending = round;
            LayRound(round);
            UiMotion.KillTarget(_roundSealImage, false);
            _roundSealImage.canvasRenderer.SetAlpha(1f);   // a new run's seal is wet again (R4 dried the last one)
        }

        /// <summary>
        /// Lays <paramref name="round"/>: the numeral (HudRoundNumeral), the clip round it (the letters' width + its pad, its left
        /// edge where the numeral starts), the smear under it and the cluster's width. Once per round change, never per frame.
        /// </summary>
        private float LayRound(int round)
        {
            if (_roundSmearAuthored && _roundNumeral.DrawsGlyphsFor(round))
            {
                // 08.10 (S-09): a long numeral's cap shrinks so its plate fits (HudArt.FitCap; floor 32).
                float lsb;
                float em = HudRoundNumeral.EmWidth(round, out lsb);
                _roundNumeral.SetHeight(HudArt.FitCap(RoundGlyphHeight, em, lsb));
            }

            float width = _roundNumeral.Set(round);
            if (_roundSmearAuthored)
            {
                // AP-032 §3.3: the plate's width rounds to whole periods (the W' rule, spare to the head), its tail's tip on
                // the margin (AD note 4) - the cluster is the plate; the seal and the letters' end follow the cap.
                float cap = _roundNumeral.Height;
                float plateWidth, plateHeight, sealCentre, sealDiameter;
                HudArt.LaySmear(cap, width, _roundNumeral.FirstBearingEm, out plateWidth, out plateHeight, out _roundRunRight, out sealCentre, out sealDiameter);
                _roundSmear.rectTransform.sizeDelta = new Vector2(plateWidth, plateHeight);
                _roundSmear.rectTransform.anchoredPosition = new Vector2(0f, -HudArt.SmearCapOffset(plateHeight));
                _roundCluster.sizeDelta = new Vector2(plateWidth, _roundCluster.sizeDelta.y);
                float sealSize = _roundSealAuthored ? sealDiameter / HudArt.SealRingShareOfCell : sealDiameter;
                _roundSealHost.anchoredPosition = new Vector2(-sealCentre, 0f);
                _roundSealHost.sizeDelta = new Vector2(sealSize, sealSize);
            }
            else
            {
                _roundSmear.rectTransform.sizeDelta = new Vector2(width + RoundSmearOverhang + (int)UiSpace.S, RoundSmearHeight);
                _roundCluster.sizeDelta = new Vector2(_roundRunRight + width + RoundSmearOverhang, _roundCluster.sizeDelta.y);
            }

            _roundClip.anchoredPosition = new Vector2(-(_roundRunRight + width + RoundClipPad), 0f);
            _roundClip.sizeDelta = new Vector2(width + 2f * RoundClipPad, _roundClip.sizeDelta.y);
            return width;
        }

        /// <summary>
        /// WP-094 · the round transition (HAREKET-HUD R1 → R3; S-09 decision (c)), one UiMotion timeline with cues:
        /// - R1 (t 0): the old numeral leaves - its group fades and drops 4 px (Fast, Exit);
        /// - R2 (t 0.4 s, SealHandoffDelay: the banner reads first): the new numeral is laid and the clip opens left to right
        ///   (RomanReveal: I / IV / IX 220 ms, XIV 280, XXIII 400, ≥ 7 letters 600; Brush) while the smear inks in (Emphasis);
        /// - R3 (the reveal's end): the vermilion seal stamps (1.12 → 1, 90 ms) with its ink spreading (Hanko, Screen), the
        ///   impression dips 2 px on the contact (not in the danger posture), and on the contact frame StampLanded "round"
        ///   rises for S-08 (an existing event and a detail - no new UiEvent).
        /// A newer change, or a non-reveal set, lands the running one (Complete: visuals at their end, the cue still raised -
        /// the last cue lays the numeral). Reduced motion: the swap at once and the cue at once - the sound still plays.
        /// </summary>
        private void PlayRoundTransition(int round)
        {
            if (_roundTimeline.IsAlive)
            {
                _roundTimeline.Complete();
            }

            _roundPending = round;
            float now = JuiceNow;
            int glyphs = Mathf.Max(1, RomanNumerals.Length(round));
            int owner = GetInstanceID();
            _roundTimeline = UiMotion.ReducedMotion ? default(UiTimeline) : UiMotion.Timeline(owner, this);
            if (!_roundTimeline.IsValid)
            {
                FinishRound();
                _roundSealHitAt = now;
                UiEvents.Raise(UiEvent.StampLanded, RoundSealCueDetail);
                return;
            }

            _roundSealHitAt = now + HudRoundTransition.ContactSeconds(glyphs);
            Vector2 clip = _roundClip.anchoredPosition;
            _roundTimeline.Add(UiMotion.Alpha(_roundGroup, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner));
            _roundTimeline.Add(UiMotion.Move(_roundClip, new Vector2(clip.x, -HudRoundTransition.OldOutDropPx), UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, owner));
            _roundTimeline.Call(HudRoundTransition.HandoffSeconds, HudRoundTransition.CueSwap)
                .Call(HudRoundTransition.SealSeconds(glyphs), HudRoundTransition.CueSeal)
                .Cue(UiEvent.StampLanded, RoundSealCueDetail, HudRoundTransition.ContactSeconds(glyphs), HudRoundTransition.CueContact);
        }

        /// <summary>The transition's cues (IUiMotionListener): the swap and reveal, the seal's stamp, the contact that lands it all.</summary>
        void IUiMotionListener.OnCue(int cueId)
        {
            switch (cueId)
            {
                case HudRoundTransition.CueSwap:
                    RevealRound();
                    break;
                case HudRoundTransition.CueSeal:
                    StampRoundSeal();
                    break;
                case HudRoundTransition.CueContact:
                    FinishRound();
                    break;
            }
        }

        /// <summary>R2: the new numeral, laid and opened by the brush; the smear inks in under it.</summary>
        private void RevealRound()
        {
            int owner = GetInstanceID();
            LayRound(_roundPending);
            UiMotion.KillTarget(_roundGroup, false);
            UiMotion.KillTarget(_roundClip, false);
            _roundGroup.alpha = 1f;
            _roundClip.anchoredPosition = new Vector2(_roundClip.anchoredPosition.x, 0f);
            _roundTimeline.Add(UiPresets.RomanReveal(_roundClip, _roundClip.sizeDelta.x, RomanNumerals.Length(_roundPending), owner));
            _roundSmearFx.Play();
        }

        /// <summary>R3: the seal stamps wet (the R4 dry alpha lifts), its ink spreads; the impression dips on the contact.</summary>
        private void StampRoundSeal()
        {
            int owner = GetInstanceID();
            float now = JuiceNow;
            UiMotion.KillTarget(_roundSealImage, false);
            _roundSealImage.canvasRenderer.SetAlpha(1f);
            _roundTimeline.Add(UiPresets.StampIn(_roundSeal, 0f, owner));
            _roundSealFx.Play();
            if (!InDangerPosture)
            {
                _roundTimeline.Add(UiMotion.MoveSeconds(_roundCluster, _roundClusterHome - new Vector2(0f, HudRoundTransition.ClusterKickPx), _roundClusterHome,
                    UiMotionTokens.Seconds(UiDuration.Fast), UiEase.Settle, UiMotionClass.Decor, UiMotionTokens.StampInSeconds, owner));
            }

            _feelBudget.Note(now, UiMotionTokens.StampInSeconds + UiMotionTokens.StampHoldSeconds);
        }

        /// <summary>The contact (or a skip): the new numeral stands laid, open and inked, whatever was cut short.</summary>
        private void FinishRound()
        {
            if (_roundNumeral.Value != _roundPending || _roundClip.sizeDelta.x <= 0f)
            {
                LayRound(_roundPending);
            }

            _roundGroup.alpha = 1f;
            _roundClip.anchoredPosition = new Vector2(_roundClip.anchoredPosition.x, 0f);
            _roundSeal.localScale = Vector3.one;
            _roundSealImage.canvasRenderer.SetAlpha(1f);
        }

        /// <summary>HAREKET-HUD R4: the round is won - the seal dries (α 1 → 0.7 over Emphasis, Brush; Decor: at once under RM).</summary>
        private void DryRoundSeal()
        {
            if (_roundSealImage != null && !_roundTimeline.IsAlive)
            {
                UiMotion.TintAlpha(_roundSealImage, RoundSealDryAlpha, UiDuration.Emphasis, UiEase.Brush, UiMotionClass.Decor);
            }
        }

        /// <summary>Capture seam (-ddUiCapture hudstates / hudv4, WP-094 acceptance frames I / IV / IX / XIV / XXIII): shows a round through its transition.</summary>
        public void CaptureRound(int round)
        {
            SetRound(round, true);
        }

        // ---- bottom-left health: heart, value, the brush stroke

        private void BuildBrushHealth()
        {
            // Bands from the metrics at the TextScale (D6): the value row sits clear of the leaning stroke's highest point.
            const float strokeLength = HealthStrokeLength, strokeHeight = HealthStrokeHeight, strokeCentre = HealthStrokeCentre;
            float strokeTop = strokeCentre + strokeHeight * 0.5f + strokeLength * Mathf.Tan(HealthLean * Mathf.Deg2Rad);
            float band = UiFactory.LineHeight(UiType.NumericL);
            float row = strokeTop + 2f + band * 0.5f;
            RectTransform root = Cluster("Health", Vector2.zero, new Vector2(InkMargin, InkMargin), new Vector2(380f, HealthClusterHeight()), true);
            _healthPanel = root;   // 8c · E22: the cluster that holds α 1 through the death dim
            // WP-094 (HAREKET-HUD "ada kuralı": the health front plane): the value, the beat and the death line re-batch only it.
            root.gameObject.AddComponent<Canvas>();
            RectTransform back = BackPlane(root);

            _healthIcon = InkGlyph("Heart", root, "Hud.Health", UiColor.BarHealth, 28f);
            Place(_healthIcon.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0f, row), new Vector2(28f, 28f));
            _healthValue = InkText("Value", root, "—", UiType.NumericL, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            float valueWidth = Mathf.Ceil(_healthValue.GetPreferredValues("100").x) + 4f;
            Place(_healthValue.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(38f, row), new Vector2(valueWidth, band));
            float after = 38f + valueWidth + 6f;

            RectTransform maxCalm = UiFactory.NewRect("MaxCalm", root);
            Place(maxCalm, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(after, row - 4f), new Vector2(72f, band * 0.6f));
            _healthMaxText = InkText("Max", maxCalm, string.Empty, UiType.NumericS, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Fill(_healthMaxText.rectTransform);
            _calmOnly.Add(UiFactory.Group(maxCalm.gameObject));

            // LOW takes the "/ max" place (T2 hides whenever health is low - low is danger).
            _healthLowWord = InkText("Low", root, UiStrings.Get("hud.health.low", "LOW"), UiType.Label, UiColor.StateDanger, TextAnchor.MiddleLeft);
            _healthLowWord.fontStyle = FontStyles.Bold;
            Place(_healthLowWord.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(after, row), new Vector2(72f, band * 0.6f));
            _healthLowWord.gameObject.SetActive(false);

            // The stroke on the back plane: ink rim, cream ghost track, lost-value ghost, fill; leaning toward the centre.
            RectTransform stroke = UiFactory.NewRect("Stroke", back);
            Place(stroke, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0f, strokeCentre), new Vector2(strokeLength, strokeHeight));   // the brush tapers: 26 px gives it body
            stroke.localRotation = Quaternion.Euler(0f, 0f, HealthLean);
            Sprite brush = BrushSprite();
            Image rim = UiFactory.NewImage("Rim", stroke, UiColor.HudInk, brush);
            UiFactory.Outset(rim.rectTransform, 1.5f);
            Depth(rim);
            Image track = UiFactory.NewImage("Track", stroke, UiColor.TextPrimary, brush);
            UiFactory.Fill(track.rectTransform);
            Fade(track, 0.25f);
            Image fill = UiFactory.NewImage("Fill", stroke, UiColor.BarHealth, brush);
            UiFactory.Fill(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            _healthBar = stroke.gameObject.AddComponent<UiBar>();
            _healthBar.Configure(fill, UiColor.BarHealth);
            _healthBar.EnableGhost();
            _healthBar.EnableSegments(10);
            BuildHealthJuice(stroke, fill, rim, strokeLength, strokeHeight);   // WP-094: H1 chunks, H4 glow / wet band, H5 rim

            _healthState = InkText("State", root, string.Empty, UiType.Caption, UiColor.AccentMoss, TextAnchor.MiddleLeft);
            Place(_healthState.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(320f, 18f));
        }

        // ---- bottom-right weapon: the lacquer ring, the count, the ladder on its brush

        private void BuildRingWeapon()
        {
            RectTransform root = Cluster("ClassicWeapon", new Vector2(1f, 0f), new Vector2(-InkMargin, InkMargin), new Vector2(WeaponClusterWidth, WeaponClusterHeight), true);
            _weaponCluster = root;
            _weaponHome = root.anchoredPosition;
            RectTransform back = BackPlane(root);
            RectTransform host = UiFactory.NewRect("Content", root);
            UiFactory.Fill(host);
            // WP-094 (HAREKET-HUD A1 / the motion lead's main-branch finding: every shot re-batched the main HUD canvas): the
            // count, the ladder and the glyph change per shot - their own nested canvas, as the gold's.
            host.gameObject.AddComponent<Canvas>();
            _classicGroup = UiFactory.Group(host.gameObject);

            Vector2 ringCentre = new Vector2(-WeaponRing * 0.5f, WeaponRing * 0.5f + 8f);
            _reloadRing = UiRing.Create("ReloadRing", back, WeaponRing, UiColor.BarReload, 0.25f);
            Place((RectTransform)_reloadRing.transform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), ringCentre, new Vector2(WeaponRing, WeaponRing));
            Transform ghost = _reloadRing.transform.Find("Ghost");
            if (ghost != null)
            {
                Depth(ghost.GetComponent<Image>());
            }

            _weaponIcon = InkGlyph("WeaponIcon", host, "Weapon.AssaultRifle", UiColor.TextPrimary, WeaponGlyph);
            Place(_weaponIcon.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), ringCentre, new Vector2(WeaponGlyph, WeaponGlyph));

            float edge = -(WeaponRing + RingGap);
            BuildInkAmmo(root, host, back, edge);

            // T2 (calm only): the weapon and its build on one line (AD 8-R SHOULD-A1).
            RectTransform calm = UiFactory.NewRect("WeaponCalm", root);
            Place(calm, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(edge, 118f), new Vector2(340f, UiFactory.LineHeight(UiType.Caption)));   // WP-081 B4 (U2): the caption's line, not 18 (LayWeaponCluster too)
            _classicName = InkText("Name", calm, "—", UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
            UiFactory.Fill(_classicName.rectTransform);
            _calmOnly.Add(UiFactory.Group(calm.gameObject));
            _weaponCalm = calm;
            LayWeaponCluster(false);
        }

        /// <summary>
        /// The count ("24" NumericXL over "/ 30"), the ladder on an ink brush, the LOW AMMO / EMPTY word with the reload prompt, and
        /// SUSPENDED in the same slot (one slot, one word - AD 8-R MUST-1). <paramref name="edge"/> is where the count ends.
        /// </summary>
        private void BuildInkAmmo(RectTransform root, RectTransform host, RectTransform back, float edge)
        {
            const float countWidth = CountWidth;
            const float ladderWidth = LadderWidth;
            var anchor = new Vector2(1f, 0f);
            float ladderLeft = edge - ladderWidth;
            float captionLeft = edge - countWidth;

            _ammoCounter = UiCounter.Create("Ammo", host, UiCounterStyle.Fraction, 0, UiType.NumericXL, UiColor.TextPrimary, 0);
            Place((RectTransform)_ammoCounter.transform, anchor, new Vector2(1f, 0f), new Vector2(edge, 30f), new Vector2(countWidth, 48f));

            // WP-068 12b (AD FP cluster SHOULD-2): the ammo word carries its noun in every mode - in first person it shares a
            // column with the health LOW. Read once from the R1 pack (hud.ammo.low).
            _ammoLowWord = UiStrings.Get("hud.ammo.low", "LOW AMMO");
            _ammoValue = _ammoCounter.ValueText;
            InkAll(_ammoCounter.gameObject);

            RectTransform under = UiFactory.NewRect("Underline", back);
            Place(under, anchor, new Vector2(0f, 0.5f), new Vector2(ladderLeft - 8f, 20f), new Vector2(ladderWidth + 16f, 14f));
            under.localRotation = Quaternion.Euler(0f, 0f, -StrokeLean);
            Image brush = UiFactory.NewImage("Brush", under, UiColor.HudInk, BrushSprite());
            UiFactory.Fill(brush.rectTransform);
            Depth(brush);
            _ladderBrush = under;

            RectTransform ladder = UiFactory.NewRect("Ladder", host);
            Place(ladder, anchor, new Vector2(0f, 0f), new Vector2(ladderLeft, 14f), new Vector2(ladderWidth, LadderHeight));
            ladder.localRotation = Quaternion.Euler(0f, 0f, -StrokeLean);
            _ladderHome = ladder.anchoredPosition;
            _ladderBack = LadderImage("Back", ladder, UiColor.TextMuted);
            _ladderFill = LadderImage("Fill", ladder, UiColor.TextPrimary);
            _ladderWidth = ladderWidth;
            BuildAmmoJuice(ladder, back);   // WP-094: A3 preview, A4 group, A1 spark, A2 casings, A6 ring kick

            _reloadCaption = InkText("ReloadCaption", host, string.Empty, UiType.Label, UiColor.BarReload, TextAnchor.MiddleLeft);
            _reloadCaption.fontStyle = FontStyles.Bold;
            RectTransform captionRow = _reloadCaption.rectTransform;
            Place(captionRow, anchor, new Vector2(0f, 0.5f), new Vector2(captionLeft, 92f), new Vector2(220f, ReloadPromptSize + 6f));
            _reloadPrompt = UiInputGlyphs.Build("ReloadPrompt", captionRow, UiPromptAction.Reload, UiColor.TextPrimary, UiColor.SurfaceInset, ReloadPromptSize);
            // WP-094 (feel contract §4.8): the caption reads EMPTY, or RELOAD / DOLDUR after an empty pull - the key sits past the wider.
            float captionWords = Mathf.Max(_reloadCaption.GetPreferredValues(UiStrings.Get("hud.ammo.empty", "EMPTY")).x,
                _reloadCaption.GetPreferredValues(UiStrings.Get("hud.ammo.reload_hint", "RELOAD")).x);
            _reloadPromptHome = new Vector2(Mathf.Ceil(captionWords) + (int)UiSpace.S, 0f);
            Place((RectTransform)_reloadPrompt.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), _reloadPromptHome, ((RectTransform)_reloadPrompt.transform).sizeDelta);
            _reloadPromptGroup = UiFactory.Group(_reloadPrompt.gameObject);   // 8c · E23: α + 4 px Fast in, Exit Fast out
            _reloadPrompt.gameObject.SetActive(false);
            _reloadCaptionGroup = UiFactory.Group(captionRow.gameObject);

            TMP_Text suspended = InkText("Suspended", root, UiStrings.Get("hud.special.suspended", "SUSPENDED"), UiType.Label, UiColor.StateWarning, TextAnchor.MiddleLeft);
            suspended.fontStyle = FontStyles.Bold;
            Place(suspended.rectTransform, anchor, new Vector2(0f, 0.5f), new Vector2(captionLeft, 92f), new Vector2(220f, ReloadPromptSize + 6f));
            _suspendedChip = suspended.gameObject;
            _suspendedRect = suspended.rectTransform;
            _suspendedChip.SetActive(false);

            // The ring carries the reload; the linear bar stays for the presenter's contract, unseen.
            _reloadBar = UiFactory.Bar("ReloadBar", host, UiColor.BarReload, 8f);
            _reloadBar.gameObject.SetActive(false);
        }

        // ---- bottom-centre: the Special, boxless

        private void BuildInkSpecial()
        {
            RectTransform special = InkRoot("SpecialWeapon", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(SpecialBlockWidth, SpecialBlockHeight));
            special.gameObject.AddComponent<Canvas>();
            _specialBlock = special.gameObject;
            BuildSpecialContent(special, true);
            _specialBlock.SetActive(false);
        }

        // ---- bottom-centre: the player's SLICK hint (N19 §2 · E23 prompt slot)

        private RectTransform _slick;
        private CanvasGroup _slickGroup;
        private Vector2 _slickHome;
        private bool _slickShown;
        private float _slickAt;

        /// <summary>The SLICK hint is up (tests, captures).</summary>
        public bool SlickShown => _slickShown;

        /// <summary>Capture seam: show the hint without a puddle under the player (-ddUiCapture hudstates).</summary>
        public static bool CaptureSlick;

        /// <summary>
        /// While the player stands in Namekuji's gel (PlayerMotor.InGel, WP-069 B4): Status.Sliding at 24 px in the gel's
        /// identity tint + the pack's hud.slick word in cream with the HUD ink, in the E23 prompt slot at bottom centre
        /// (above the Special block while it shows). No box, no duration (the puddle has none to show); the player arcs and
        /// the reticle are untouched.
        /// </summary>
        private void BuildInkSlick()
        {
            _slick = InkRoot("Slick", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), HudRearEdge.PromptSlot);
            _slickHome = _slick.anchoredPosition;
            TMP_Text word = InkText("Text", _slick, UiStrings.Get("hud.slick", "SLICK"), UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            word.fontStyle = FontStyles.Bold;
            UiHudInk.Apply(word);
            UiFactory.Fill(word.rectTransform);
            float half = Mathf.Ceil(word.GetPreferredValues(word.text).x) * 0.5f;
            Sprite sprite = UiArt.Icon("Status.SlidingOuter") ?? UiArt.Icon("Status.Sliding");
            if (sprite != null)
            {
                Image glyph = UiFactory.NewImage("Glyph", _slick, UiColor.TextPrimary, sprite);
                glyph.preserveAspect = true;
                if (UiArt.IsGlyph(sprite))
                {
                    UiArt.ApplyGlyph(glyph, GlyphInk);
                }

                glyph.color = UiTokens.Get(UiGlyphTints.Gel);
                Place(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(half + (int)UiSpace.S), 0f), new Vector2(24f, 24f));
            }

            _slickGroup = UiFactory.Group(_slick.gameObject);
            _slickGroup.alpha = 0f;
            _slick.gameObject.SetActive(false);
        }

        private void UpdateSlick()
        {
            if (_slick == null)
            {
                return;
            }

            DD.Prototype.PlayerSystem.PlayerRig rig = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
            bool inGel = CaptureSlick || (rig != null && rig.Motor != null && rig.Motor.InGel);
            bool show = inGel && DD.Prototype.Rounds.RunEvents.Current.Phase == DD.Prototype.Rounds.RunPhase.Fighting && UiNavigation.GameplayInputAllowed;
            Vector2 home = new Vector2(_slickHome.x, BottomCentreTop > 0f ? BottomCentreTop + (int)UiSpace.S : _slickHome.y);
            if (show == _slickShown)
            {
                if (show && !Mathf.Approximately(home.y, _slickAt))
                {
                    _slickAt = home.y;   // the Special block came or went under it
                    UiMotion.Move(_slick, home, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                }

                return;
            }

            _slickAt = home.y;

            // E23: rises 4 px into place as it fades in (Fast, Settle); fades out (Fast, Exit).
            _slickShown = show;
            UiMotion.KillTarget(_slick, false);
            UiMotion.KillTarget(_slickGroup, false);
            if (show)
            {
                _slick.gameObject.SetActive(true);
                _slickGroup.alpha = 0f;
                _slick.anchoredPosition = home + new Vector2(0f, -PromptRise);
                UiMotion.Alpha(_slickGroup, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.Move(_slick, home, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                return;
            }

            UiMotion.Alpha(_slickGroup, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
        }

        /// <summary>The final-seconds line: the words in coral with their ink over a brush underline - no chip (8-R).</summary>
        private GameObject BuildInkWarning(RectTransform parent, float top)
        {
            RectTransform row = InkRootOn(parent, "FinalSeconds", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(420f, 34f));
            Image underline = UiFactory.NewImage("Underline", row, UiColor.StateDanger, BrushSprite());
            Place(underline.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(300f, 7f));
            Fade(underline, 0.8f);
            TMP_Text words = InkText("Text", row, UiStrings.Get("hud.special.ending_banner", "SPECIAL WEAPON ENDING"), UiType.Label, UiColor.StateDanger, TextAnchor.MiddleCenter);
            words.fontStyle = FontStyles.Bold;
            UiHudInk.Apply(words);
            UiFactory.Fill(words.rectTransform);
            return row.gameObject;
        }

        // ---- threat chips without a box: outer glyph + word in their own ink, a brush underline in the state colour

        private void BuildInkThreatChips()
        {
            RectTransform row = InkRoot("ThreatRow", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -ThreatRowTop), new Vector2(600f, 30f));
            _threatRow = row.gameObject;

            _threatSlots[0] = InkThreatChip(row, UiStrings.Get("hud.threat.surrounded", "SURROUNDED"), "Threat.Surrounded", UiColor.StateDanger);
            _threatSlots[1] = InkThreatChip(row, UiStrings.Get("hud.threat.behind", "BEHIND YOU"), "Threat.BehindOuter", UiColor.StateDanger);
            _threatSlots[2] = InkThreatChip(row, UiStrings.Get("hud.threat.elite", "ELITE NEARBY"), "Threat.EliteOuter", UiColor.StateWarning);

            float words = 0f;
            for (int i = 0; i < ThreatLabels.Length; i++)
            {
                words = Mathf.Max(words, Mathf.Ceil(_threatSlots[2].Label.GetPreferredValues(UiStrings.Get(ThreatLabelKeys[i], ThreatLabels[i])).x));
            }

            float width = 30f + words;
            for (int i = 0; i < _threatSlots.Length; i++)
            {
                ThreatSlot slot = _threatSlots[i];
                slot.Rect.sizeDelta = new Vector2(width, 30f);
                slot.Home = new Vector2(i * (width + (int)UiSpace.L), 0f);
                slot.Rect.anchoredPosition = slot.Home;
                Transform underline = slot.Rect.Find("Underline");
                if (underline != null)
                {
                    ((RectTransform)underline).sizeDelta = new Vector2(words, 5f);
                }

                slot.Rect.gameObject.SetActive(false);
            }

            row.sizeDelta = new Vector2(width * 3f + (int)UiSpace.L * 2f, 30f);
        }

        private ThreatSlot InkThreatChip(RectTransform row, string text, string glyph, UiColor colour)
        {
            RectTransform chip = UiFactory.NewRect("Threat." + text, row);
            chip.anchorMin = new Vector2(0f, 0.5f);
            chip.anchorMax = new Vector2(0f, 0.5f);
            chip.pivot = new Vector2(0f, 0.5f);

            Image underline = UiFactory.NewImage("Underline", chip, colour, BrushSprite());
            Place(underline.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(30f, 2f), new Vector2(120f, 5f));
            Fade(underline, 0.8f);
            Image icon = InkGlyph("Glyph", chip, glyph, colour, 24f);
            Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            TMP_Text label = InkText("Text", chip, text, UiType.Label, colour, TextAnchor.MiddleLeft);
            label.fontStyle = FontStyles.Bold;
            UiHudInk.Apply(label);
            RectTransform labelRect = UiFactory.Fill(label.rectTransform);
            labelRect.offsetMin = new Vector2(30f, 0f);
            return new ThreatSlot { Rect = chip, Group = UiFactory.Group(chip.gameObject), Label = label, Glyph = icon };
        }

        // ================================================================ posture and parallax

        /// <summary>Calm ↔ danger (ID 8-R): the T2 lines and the T1 clusters' scale, only on a change (≤ 1 per 0.8 s out).</summary>
        private void UpdateInkPosture()
        {
            float now = Time.unscaledTime;
            bool danger = ThreatChipVisible(0) || ThreatChipVisible(1) || ThreatChipVisible(2) || _feelLow || _model.AmmoLow ||
                          _model.ReloadState == RunHudReloadState.MagazineEmpty || now - _lastHitAt < HitDangerSeconds ||
                          WeaponEvents.Special.FinalSecondsWarning;   // AD 8-R · A SHOULD-3
            if (danger)
            {
                _dangerSeenAt = now;
            }

            int posture = danger || now - _dangerSeenAt < DangerHoldSeconds ? 1 : 0;
            if (posture == _posture)
            {
                return;
            }

            _posture = posture;
            for (int i = 0; i < _calmOnly.Count; i++)
            {
                UiMotion.Alpha(_calmOnly[i], posture == 1 ? 0f : 1f, posture == 1 ? UiDuration.Fast : UiDuration.Base,
                    posture == 1 ? UiEase.Exit : UiEase.Settle, UiMotionClass.Decor);
            }

            for (int i = 0; i < _postureClusters.Count; i++)
            {
                UiMotion.Scale(_postureClusters[i], posture == 1 ? 1f : CalmScale, posture == 1 ? UiDuration.Base : UiDuration.Emphasis,
                    posture == 1 ? UiEase.Settle : UiEase.SettleLong, UiMotionClass.Decor);
            }
        }

        /// <summary>
        /// The back planes drift against the camera's motion: up to <see cref="ParallaxReach"/> px, following with a 0.12 s
        /// half-life, 0 under reduced motion. Driven by the camera, never by the aim; written only on a visible change.
        /// </summary>
        private void UpdateInkParallax()
        {
            Camera camera = Camera.main;
            float now = Time.unscaledTime;
            float dt = _parallaxTime < 0f ? 0f : Mathf.Min(0.1f, now - _parallaxTime);
            _parallaxTime = now;
            Vector2 target = Vector2.zero;
            if (camera != null)
            {
                Vector3 at = camera.transform.position;
                // M4-R1 N2: third / first person turn the camera with the look - the drift would shake the ink; it rests.
                if (dt > 0f && !UiMotion.ReducedMotion && !DD.Prototype.CameraSystem.CameraModeService.IsLookMode)
                {
                    Vector3 velocity = (at - _parallaxCameraAt) / dt;
                    Vector3 up = Vector3.ProjectOnPlane(camera.transform.up, Vector3.up).normalized;
                    target = new Vector2(
                        Mathf.Clamp(-Vector3.Dot(velocity, camera.transform.right) * ParallaxPerMetre, -ParallaxReach, ParallaxReach),
                        Mathf.Clamp(-Vector3.Dot(velocity, up) * ParallaxPerMetre, -ParallaxReach, ParallaxReach));
                }

                _parallaxCameraAt = at;
            }

            _parallax = UiMotion.ReducedMotion
                ? Vector2.zero
                : new Vector2(UiEaseCurve.Follow(_parallax.x, target.x, ParallaxHalfLife, dt), UiEaseCurve.Follow(_parallax.y, target.y, ParallaxHalfLife, dt));
            if (CaptureParallax.HasValue)
            {
                _parallax = CaptureParallax.Value;
            }

            if (float.IsNaN(_parallaxShown.x) || (_parallax - _parallaxShown).sqrMagnitude > 0.0025f)
            {
                _parallaxShown = _parallax;
                for (int i = 0; i < _backPlanes.Count; i++)
                {
                    _backPlanes[i].anchoredPosition = _parallax * _backPlaneDepth[i];
                }
            }
        }

        // ================================================================ the edge marker's clear path (AD 8c MUST-2)

        private readonly Rect[] _markerAvoid = new Rect[8];
        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>
        /// Where the edge marker sits for a bearing <paramref name="direction"/> (canvas px, centre origin): on the inset
        /// edge, slid clear of every ink cluster - the four corners, the seal line with its chips (and the banner while it
        /// lives), the Special while it shows - each grown by 12 px and the marker's own extents. Public for the sweep test.
        /// </summary>
        public Vector2 EdgeMarkerFor(Vector2 direction)
        {
            Vector2 canvas = ScreenRect.rect.size;
            var half = new Vector2(canvas.x * 0.5f - MarkerEdgeInset, canvas.y * 0.5f - MarkerEdgeInset);
            int count = 0;
            for (int i = 0; i < _clusters.Count && count < _markerAvoid.Length; i++)
            {
                _markerAvoid[count++] = Grown(CanvasRectOf(_clusters[i]));
            }

            if (_threatRow != null && count < _markerAvoid.Length)
            {
                _markerAvoid[count++] = Grown(CanvasRectOf((RectTransform)_threatRow.transform));
            }

            if (_specialBlock != null && _specialBlock.activeInHierarchy && count < _markerAvoid.Length)
            {
                _markerAvoid[count++] = Grown(CanvasRectOf((RectTransform)_specialBlock.transform));
            }

            if (_banner != null && _bannerRemaining > 0f && count < _markerAvoid.Length)
            {
                _markerAvoid[count++] = Grown(CanvasRectOf(_banner.Clip));
            }

            return UiWorldMarker.AvoidOnEdge(UiWorldMarker.EdgePoint(direction, half), half, _markerAvoid, count);
        }

        // ---- M4-R1 N2 per-mode layout and the rear-edge arc's clearances (AD R1-A MUST-1 / MUST-2)

        // AD R1-A MUST-2 closed by S-01 WP-070V (5825f1c8), then C-00 (25.09): S-02's FP viewmodel v2 grows into the bottom
        // right (the ten weapons' union at 1080: x 1074–1918, y 502–1078 from the top). In first person the weapon cluster
        // stands above the health block at the bottom left (x 32–432, y 820–960); top-down and third person keep the corner.

        private RectTransform _weaponCluster;
        private Vector2 _weaponHome;
        private int _layoutMode = -1;
        private Vector2 _layoutSize = new Vector2(-1f, -1f);

        public const float WeaponClusterWidth = 400f;
        public const float WeaponClusterHeight = 140f;

        /// <summary>First person: the cluster's bottom edge above the health block (x 32, bottom 32, ≈ 80 tall), 8 px clear.</summary>
        public const float FpWeaponClusterBottom = 120f;
        private readonly Rect[] _rearAvoid = new Rect[12];

        /// <summary>
        /// Where the weapon cluster sits (anchor and pivot bottom-right, canvas px): its corner in top-down and third person,
        /// above the health block at the bottom left in first person (the FP viewmodel owns the bottom right).
        /// </summary>
        public static Vector2 WeaponClusterPosition(Vector2 home, bool firstPerson, Vector2 canvasSize)
        {
            return firstPerson ? new Vector2(-(canvasSize.x - InkMargin - WeaponClusterWidth), FpWeaponClusterBottom) : home;
        }

        /// <summary>The height-only form callers used before C-00 25.09: a 16:9 canvas is assumed; prefer the size overload.</summary>
        public static Vector2 WeaponClusterPosition(Vector2 home, bool firstPerson, float canvasHeight)
        {
            return WeaponClusterPosition(home, firstPerson, new Vector2(canvasHeight * 16f / 9f, canvasHeight));
        }

        /// <summary>
        /// The cluster's rect for a camera mode, in canvas px with the origin at the TOP-LEFT and y pointing DOWN
        /// (1920 × 1080: top-down / third person x 1488–1888, y 908–1048; first person x 32–432, y 820–960). Static: tests
        /// call it without a scene. S-01 / S-02's FP viewmodel tests measure their 0 px overlap against it.
        /// </summary>
        public static Rect WeaponClusterRect(DD.Prototype.CameraSystem.CameraMode mode, Vector2 canvasSize)
        {
            return WeaponClusterRect(mode == DD.Prototype.CameraSystem.CameraMode.FirstPerson, canvasSize);
        }

        /// <summary>The same on the 1920 × 1080 reference canvas (UI scale 1, 16:9): static, no scene or HUD instance needed.</summary>
        public static Rect WeaponClusterRect(DD.Prototype.CameraSystem.CameraMode mode)
        {
            return WeaponClusterRect(mode, ReferenceCanvas);
        }

        /// <summary>The reference canvas the HUD is authored on (UI scale 1, 16:9).</summary>
        public static readonly Vector2 ReferenceCanvas = new Vector2(1920f, 1080f);

        /// <summary>As above for "first person or not".</summary>
        public static Rect WeaponClusterRect(bool firstPerson, Vector2 canvasSize)
        {
            float left = firstPerson ? InkMargin : canvasSize.x - InkMargin - WeaponClusterWidth;
            float bottom = firstPerson ? FpWeaponClusterBottom : InkMargin;
            return new Rect(left, canvasSize.y - bottom - WeaponClusterHeight, WeaponClusterWidth, WeaponClusterHeight);
        }

        public RectTransform WeaponCluster
        {
            get { return _weaponCluster; }
        }

        // ---- WP-068 12b (AD FP cluster SHOULD-1): the weapon cluster's internals, right-anchored or mirrored

        private const float CountWidth = 170f;
        private const float LadderWidth = 230f;
        private const float RingGap = 16f;

        /// <summary>First person: the count (and the words above it) start this far in from the box's left - x 100 at 1080.</summary>
        public const float FpCountInset = 68f;

        /// <summary>First person: the ladder runs x 100–295 and ends with the health brush under it (55–305).</summary>
        public const float FpLadderWidth = 195f;

        private RectTransform _ladderBrush;
        private RectTransform _suspendedRect;
        private RectTransform _weaponCalm;
        private Vector2 _ladderHome;
        private string _ammoLowWord;
        private int _clusterMirrored = -1;

        /// <summary>The weapon cluster's LOW word (12b: "LOW AMMO", the pack's hud.ammo.low; tests).</summary>
        public string AmmoLowWord => _ammoLowWord;

        /// <summary>The weapon cluster's internals are mirrored (first person; tests).</summary>
        public bool WeaponClusterMirrored => _clusterMirrored == 1;

        /// <summary>
        /// The weapon cluster's internals for its place. Top-down and third person keep the corner layout: the ring at the
        /// screen's edge, the count, ladder and words running inward from it, right-anchored. First person (the cluster above
        /// the health block at the bottom left) mirrors it so the column has one left rail, as AD FP cluster SHOULD-1 laid out:
        /// - the ring at the box's left end (x 32–92 at 1080) over the heart, the glyph in it;
        /// - the count 8 px right of the ring (x 100), the ladder under it ending with the health brush (x 100–295 over 55–305),
        ///   both strokes leaning toward the centre - the TD lean mirrored, the inner end held;
        /// - the state word (LOW AMMO / EMPTY + prompt, or SUSPENDED) and the calm weapon line above, left-aligned with the count.
        /// The box, its rect (WeaponClusterRect) and everything tested against it stay as they are. Runs only on a change.
        /// </summary>
        private void LayWeaponCluster(bool mirrored)
        {
            int want = mirrored ? 1 : 0;
            if (_reloadRing == null || want == _clusterMirrored)
            {
                return;
            }

            _clusterMirrored = want;
            Vector2 anchor = mirrored ? Vector2.zero : new Vector2(1f, 0f);
            float edge = -(WeaponRing + RingGap);   // top-down / third person: where the count ends
            var ringCentre = new Vector2(mirrored ? WeaponRing * 0.5f : -WeaponRing * 0.5f, WeaponRing * 0.5f + 8f);
            Place((RectTransform)_reloadRing.transform, anchor, new Vector2(0.5f, 0.5f), ringCentre, new Vector2(WeaponRing, WeaponRing));
            Place(_weaponIcon.rectTransform, anchor, new Vector2(0.5f, 0.5f), ringCentre, new Vector2(WeaponGlyph, WeaponGlyph));
            LayAmmoJuice(anchor, ringCentre);   // WP-094 (A6): the shake's homes, the ring kick on the ring

            float ladderWidth = mirrored ? FpLadderWidth : LadderWidth;
            float wordsLeft = mirrored ? FpCountInset : edge - CountWidth;
            Place((RectTransform)_ammoCounter.transform, anchor, mirrored ? Vector2.zero : new Vector2(1f, 0f),
                new Vector2(mirrored ? FpCountInset : edge, 30f), new Vector2(CountWidth, 48f));

            // The ladder and its brush turn about their inner end (toward the screen's centre), which holds its height.
            float lean = mirrored ? StrokeLean : -StrokeLean;
            float inner = mirrored ? FpCountInset + ladderWidth : edge - LadderWidth;
            Place(_ladderBrush, anchor, new Vector2(mirrored ? 1f : 0f, 0.5f), new Vector2(inner + (mirrored ? 8f : -8f), 20f), new Vector2(ladderWidth + 16f, 14f));
            _ladderBrush.localRotation = Quaternion.Euler(0f, 0f, lean);
            RectTransform ladder = (RectTransform)_ladderBack.transform.parent;
            _ladderHome = new Vector2(inner, 14f);
            Place(ladder, anchor, new Vector2(mirrored ? 1f : 0f, 0f), _ladderHome, new Vector2(ladderWidth, LadderHeight));
            ladder.localRotation = Quaternion.Euler(0f, 0f, lean);
            if (!Mathf.Approximately(ladderWidth, _ladderWidth))
            {
                _ladderWidth = ladderWidth;
                if (_ladderMagazine > 0)
                {
                    int shown = _ladderShown;
                    LayLadder(_ladderMagazine, _ladderPellets);   // the pitch follows the new width
                    FillLadder(shown);
                }
            }

            Vector2 row = new Vector2(220f, ReloadPromptSize + 6f);
            Place(_reloadCaption.rectTransform, anchor, new Vector2(0f, 0.5f), new Vector2(wordsLeft, 92f), row);
            Place(_suspendedRect, anchor, new Vector2(0f, 0.5f), new Vector2(wordsLeft, 92f), row);
            Place(_weaponCalm, anchor, mirrored ? Vector2.zero : new Vector2(1f, 0f), new Vector2(mirrored ? FpCountInset : edge, 118f), new Vector2(340f, UiFactory.LineHeight(UiType.Caption)));
            _classicName.alignment = UiFactory.Align(mirrored ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
        }

        private void ApplyModeLayout()
        {
            if (_weaponCluster == null)
            {
                return;
            }

            int mode = (int)DD.Prototype.CameraSystem.CameraModeService.Current;
            // AD 12c MUST-1: the cluster's own parent - the 16:9 frame when B12 is on - not the canvas (FP measured its left
            // rail from the canvas's right edge and stood 320 px off the screen at 21:9 with the frame on).
            Vector2 size = HudFrame.rect.size;
            if (mode == _layoutMode && size == _layoutSize)
            {
                return;
            }

            bool first = _layoutMode >= 0;
            _layoutMode = mode;
            _layoutSize = size;
            bool firstPerson = mode == (int)DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            LayWeaponCluster(firstPerson);   // 12b: FP mirrors the internals
            Vector2 at = WeaponClusterPosition(_weaponHome, firstPerson, size);
            // WP-094: the gold keeps to the top of the bottom-left column - over the health, or over the FP weapon cluster.
            var goldAt = new Vector2(InkMargin, GoldClusterBottom(firstPerson));
            if (first)
            {
                UiMotion.Move(_weaponCluster, at, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
                if (_goldCluster != null && _goldCluster.anchoredPosition != goldAt)
                {
                    UiMotion.Move(_goldCluster, goldAt, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
                }
            }
            else
            {
                _weaponCluster.anchoredPosition = at;
                if (_goldCluster != null)
                {
                    _goldCluster.anchoredPosition = goldAt;
                }
            }
        }

        /// <summary>
        /// The rects the rear-edge arc keeps clear of, in canvas px (centre origin): the ink clusters (live
        /// rects, the weapon cluster included), the chip row, the Special block and the banner while they show, and the prompt /
        /// countdown slot at the bottom centre (reserved).
        /// </summary>
        public int RearEdgeAvoidRects(Rect[] into)
        {
            int n = 0;
            for (int i = 0; i < _clusters.Count && n < into.Length; i++)
            {
                into[n++] = CanvasRectOf(_clusters[i]);
            }

            if (_threatRow != null && n < into.Length)
            {
                into[n++] = CanvasRectOf((RectTransform)_threatRow.transform);
            }

            if (_specialBlock != null && _specialBlock.activeInHierarchy && n < into.Length)
            {
                into[n++] = CanvasRectOf((RectTransform)_specialBlock.transform);
            }

            if (_banner != null && _bannerRemaining > 0f && n < into.Length)
            {
                into[n++] = CanvasRectOf(_banner.Clip);
            }

            if (n < into.Length)
            {
                float bottom = -ScreenRect.rect.height * 0.5f;
                Vector2 slot = HudRearEdge.PromptSlot;
                into[n++] = new Rect(-slot.x * 0.5f, bottom + 24f, slot.x, slot.y);
            }

            // AD R1-B MUST-3: the prompt row where it sits now - SLICK closes onto the Special block when both show, so the
            // arc lifted above the block must also clear it (Place re-checks every rect after a lift).
            if (_slick != null && _slickShown && n < into.Length)
            {
                into[n++] = CanvasRectOf(_slick);
            }

            return n;
        }

        // ---- WP-068 12b · the status stamps' clear path (AD 8c new for 12b, AD R1-B SHOULD-4)

        /// <summary>The player figure's height and half width, metres: the capsule as the camera sees it, cap and hair included.</summary>
        public const float FigureHeight = 1.9f;
        public const float FigureHalfWidth = 0.45f;
        private static readonly Vector3[] s_figure = new Vector3[4];

        /// <summary>
        /// The rects a status stamp keeps clear of, in canvas px (centre origin): the ink clusters (live rects, the weapon
        /// cluster in its mode's place) and the Special block while it shows. The banner's band has its own rule
        /// (<see cref="StatusStampBand"/>). UiEnemyStatusMarkers reads it once a frame, only while a stamp is placed.
        /// </summary>
        public int StatusStampAvoidRects(Rect[] into)
        {
            int n = 0;
            for (int i = 0; i < _clusters.Count && n < into.Length; i++)
            {
                into[n++] = CanvasRectOf(_clusters[i]);
            }

            if (_specialBlock != null && _specialBlock.activeInHierarchy && n < into.Length)
            {
                into[n++] = CanvasRectOf((RectTransform)_specialBlock.transform);
            }

            return n;
        }

        /// <summary>
        /// AD 12c MUST-3: the banner's band (round and Special banners alike) while one shows, canvas px (centre origin) - a
        /// stamp in it lifts over it (UiEnemyStatusMarkers.BandClearance) instead of sorting under the ribbon.
        /// </summary>
        public bool StatusStampBand(out Rect band)
        {
            band = default(Rect);
            if (_banner == null || _bannerRemaining <= 0f)
            {
                return false;
            }

            band = CanvasRectOf(_banner.Root);
            return true;
        }

        /// <summary>
        /// The player figure on the canvas (px, centre origin): the box round the feet and the head, each ± the figure's half
        /// width along the camera's right. False in first person (no figure on screen) or without a player or camera.
        /// </summary>
        public bool PlayerFigureRect(out Rect figure)
        {
            figure = default(Rect);
            DD.Prototype.PlayerSystem.PlayerRig rig = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
            Camera camera = Camera.main;
            if (rig == null || camera == null || DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson)
            {
                return false;
            }

            Vector3 feet = rig.transform.position;
            Vector3 side = camera.transform.right * FigureHalfWidth;
            Vector3 head = feet + Vector3.up * FigureHeight;
            s_figure[0] = feet - side;
            s_figure[1] = feet + side;
            s_figure[2] = head - side;
            s_figure[3] = head + side;
            Vector2 size = ScreenRect.rect.size;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < s_figure.Length; i++)
            {
                Vector3 v = camera.WorldToViewportPoint(s_figure[i]);
                if (v.z <= 0f)
                {
                    return false;
                }

                float x = (v.x - 0.5f) * size.x, y = (v.y - 0.5f) * size.y;
                xMin = Mathf.Min(xMin, x);
                xMax = Mathf.Max(xMax, x);
                yMin = Mathf.Min(yMin, y);
                yMax = Mathf.Max(yMax, y);
            }

            figure = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        /// <summary>The marker-avoid rects in canvas px (test readout: the sweep checks the marker never meets one).</summary>
        public int MarkerAvoidRects(Rect[] into)
        {
            EdgeMarkerFor(Vector2.up);
            int n = 0;
            for (int i = 0; i < _clusters.Count && n < into.Length; i++)
            {
                into[n++] = CanvasRectOf(_clusters[i]);
            }

            return n;
        }

        private Rect CanvasRectOf(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            Vector3 a = ScreenRect.InverseTransformPoint(s_corners[0]);
            Vector3 b = ScreenRect.InverseTransformPoint(s_corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static Rect Grown(Rect r)
        {
            Vector2 grow = UiWorldMarker.EdgeExtents + new Vector2(UiWorldMarker.ClusterClearance, UiWorldMarker.ClusterClearance);
            return Rect.MinMaxRect(r.xMin - grow.x, r.yMin - grow.y, r.xMax + grow.x, r.yMax + grow.y);
        }

        // ================================================================ helpers

        /// <summary>A corner cluster: a root at the anchor (pivot at the same corner), listed for the tests.</summary>
        private RectTransform Cluster(string name, Vector2 anchor, Vector2 position, Vector2 size, bool posture)
        {
            RectTransform root = InkRootOn(HudFrame, name, anchor, anchor, position, size);
            _clusters.Add(root);
            if (posture)
            {
                _postureClusters.Add(root);
            }

            return root;
        }

        /// <summary>The cluster's back plane (forms and their shadows): its own nested canvas, so the parallax drift re-batches only it.</summary>
        /// <param name="depth">The share of the parallax this plane takes (1 = the full reach; a stamped object less).</param>
        private RectTransform BackPlane(RectTransform cluster, float depth = 1f)
        {
            RectTransform back = UiFactory.NewRect("Back", cluster);
            UiFactory.Fill(back);
            back.gameObject.AddComponent<Canvas>();
            back.SetAsFirstSibling();
            _backPlanes.Add(back);
            _backPlaneDepth.Add(depth);
            return back;
        }

        /// <summary>
        /// WP-068 12c · B12: the corner clusters' parent - the whole canvas, or with <see cref="UiSettings.HudFrame169"/> on a
        /// screen wider than 16:9 a centred 16:9 frame (the reticle, markers, stamps, damage edges and the centred rows stay on
        /// the canvas). Built on first use, so it sits where the clusters' draw order puts it.
        /// </summary>
        private RectTransform HudFrame
        {
            get
            {
                if (_hudFrame == null)
                {
                    _hudFrame = UiFactory.NewRect("HudFrame", ScreenRect);
                    UiFactory.Fill(_hudFrame);
                    ApplyHudFrame();
                }

                return _hudFrame;
            }
        }

        private RectTransform _hudFrame;
        private float _hudFrameWidth = -1f;
        private bool _hudFrameOn;

        /// <summary>The frame's inset from each side, canvas px: half the width past 16:9 when on, else 0.</summary>
        public static float HudFrameInset(Vector2 canvas, bool on)
        {
            if (!on || canvas.y <= 0f)
            {
                return 0f;
            }

            return Mathf.Max(0f, (canvas.x - canvas.y * 16f / 9f) * 0.5f);
        }

        /// <summary>The frame's current inset (tests, captures).</summary>
        public float HudFrameInsetNow
        {
            get { return _hudFrame != null ? _hudFrame.offsetMin.x : 0f; }
        }

        /// <summary>Re-insets the frame when the setting or the canvas width changed (one compare a frame otherwise).</summary>
        private void ApplyHudFrame()
        {
            if (_hudFrame == null)
            {
                return;
            }

            Vector2 canvas = ScreenRect.rect.size;
            bool on = UiSettings.HudFrame169;
            if (Mathf.Approximately(canvas.x, _hudFrameWidth) && on == _hudFrameOn)
            {
                return;
            }

            _hudFrameWidth = canvas.x;
            _hudFrameOn = on;
            float inset = HudFrameInset(canvas, on);
            _hudFrame.offsetMin = new Vector2(inset, 0f);
            _hudFrame.offsetMax = new Vector2(-inset, 0f);
        }

        private RectTransform InkRoot(string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            return InkRootOn(ScreenRect, name, anchor, pivot, position, size);
        }

        private static RectTransform InkRootOn(RectTransform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = UiFactory.NewRect(name, parent);
            return Place(rect, anchor, pivot, position, size);
        }

        private static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static TMP_Text InkText(string name, Transform parent, string text, UiType type, UiColor colour, TextAnchor align)
        {
            bool numeric = type == UiType.NumericXL || type == UiType.NumericL || type == UiType.NumericM || type == UiType.NumericS;
            TMP_Text label = numeric
                ? UiFactory.Numeric(name, parent, text, type, colour, align, false)
                : UiFactory.Label(name, parent, text, type, colour, align);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            UiHudInk.Apply(label);
            return label;
        }

        private static void InkAll(GameObject root)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                UiHudInk.Apply(text);
            }
        }

        private static Image InkGlyph(string name, Transform parent, string key, UiColor colour, float size)
        {
            Image image = UiArt.IconImage(name, parent, key, colour, size);
            if (UiArt.IsGlyph(image.sprite))
            {
                UiArt.ApplyGlyph(image, GlyphInk);
            }

            return image;
        }

        /// <summary>A form's depth: two contact-shadow layers under it and a 1 px cream rim light on its upper-left (ElevInk).</summary>
        private static void Depth(Image form)
        {
            if (form == null)
            {
                return;
            }

            Twin(form, "ShadowFar", UiColor.HudInk, ShadowFar, ShadowFarAlpha);
            Twin(form, "ShadowNear", UiColor.HudInk, ShadowNear, ShadowNearAlpha);
            Twin(form, "RimLight", UiColor.TextPrimary, RimOffset, RimAlpha);
        }

        /// <summary>The same sprite, offset and tinted, drawn just before <paramref name="of"/>.</summary>
        private static Image Twin(Image of, string suffix, UiColor colour, Vector2 offset, float alpha)
        {
            Image twin = UiFactory.NewImage(of.name + "." + suffix, of.transform.parent, colour, of.sprite);
            twin.type = of.type;
            twin.preserveAspect = of.preserveAspect;
            twin.raycastTarget = false;
            RectTransform src = of.rectTransform, dst = twin.rectTransform;
            dst.anchorMin = src.anchorMin;
            dst.anchorMax = src.anchorMax;
            dst.pivot = src.pivot;
            dst.offsetMin = src.offsetMin + offset;
            dst.offsetMax = src.offsetMax + offset;
            dst.localRotation = src.localRotation;
            Fade(twin, alpha);
            twin.transform.SetSiblingIndex(of.transform.GetSiblingIndex());
            return twin;
        }

        private static Image Fade(Image image, float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
            return image;
        }

        private static Sprite BrushSprite()
        {
            Sprite brush = UiFxGlobals.InkMask(BrushChannel);
            return brush != null ? brush : UiSprites.Solid;
        }
    }
}
