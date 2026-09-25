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
    /// - Gold (top-left): the coin on a small kinpaku disc, the numeral, "+N".
    /// - Round (top-centre): a hanko seal holding the round number, then the enemies glyph and count. The breakdown shows
    ///   in calm; the threat chips sit under it.
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
            BuildSealRound();
            BuildBrushHealth();
            BuildRingWeapon();
            BuildInkSpecial();
            BuildInkSlick();
        }

        // ---- top-left gold: kinpaku disc, coin, numeral, "+N"

        private void BuildInkGold()
        {
            RectTransform root = Cluster("Gold", new Vector2(0f, 1f), new Vector2(InkMargin, -InkMargin), new Vector2(170f, 36f), false);
            RectTransform back = BackPlane(root);

            // The one leaf the HUD carries (E12): a small kinpaku disc under the coin; S-07's Leaf glint sweeps it on a gain.
            Image disc = UiFactory.NewImage("Leaf", back, UiColor.StateGold, UiSprites.Disc);
            Place(disc.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(36f, 36f));
            Fade(disc, GoldDiscAlpha);
            _goldLeaf = disc.gameObject.AddComponent<UiFx>();
            _goldLeaf.Configure(UiFxMode.Leaf, GoldGlintSeconds, new Vector3(1f, 0f, 0f));

            Image coin = InkGlyph("Coin", root, "Hud.Gold", UiColor.StateGold, 24f);
            Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(24f, 24f));

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

        // ---- top-centre: the hanko seal with the round, the enemies count, the calm breakdown

        private void BuildSealRound()
        {
            const float seal = 56f;
            RectTransform root = Cluster("Round", new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(240f, seal), false);
            RectTransform back = BackPlane(root, SealDrift);
            Sprite hanko = UiFxGlobals.InkMask(UiInkChannel.Hanko);
            Image stamp = UiFactory.NewImage("Seal", back, UiColor.HudInk, hanko != null ? hanko : UiSprites.Disc);
            Place(stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-36f, 0f), new Vector2(seal, seal));
            Depth(stamp);

            // The digit is part of the impression: it rides the seal's plane (AD 8-R · A MUST-1), unlike the count beside it.
            _roundValue = InkText("RoundValue", back, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            Place(_roundValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-36f, 0f), new Vector2(seal, seal));

            Image enemies = InkGlyph("Enemies", root, "Hud.Enemies", UiColor.TextSecondary, 20f);
            Place(enemies.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
            _enemiesValue = InkText("EnemiesValue", root, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            Place(_enemiesValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(72f, 28f));

            RectTransform calm = InkRoot("RoundCalm", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f - seal - 6f), new Vector2(420f, 20f));
            _enemiesBreakdown = InkText("EnemiesBreakdown", calm, "—", UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
            UiFactory.Fill(_enemiesBreakdown.rectTransform);
            _calmOnly.Add(UiFactory.Group(calm.gameObject));
        }

        // ---- bottom-left health: heart, value, the brush stroke

        private void BuildBrushHealth()
        {
            // Bands from the metrics at the TextScale (D6): the value row sits clear of the leaning stroke's highest point.
            const float strokeLength = 320f, strokeHeight = 26f, strokeCentre = 32f;
            float strokeTop = strokeCentre + strokeHeight * 0.5f + strokeLength * Mathf.Tan(HealthLean * Mathf.Deg2Rad);
            float band = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.NumericL));
            float row = strokeTop + 2f + band * 0.5f;
            RectTransform root = Cluster("Health", Vector2.zero, new Vector2(InkMargin, InkMargin), new Vector2(380f, row + band * 0.5f + 4f), true);
            _healthPanel = root;   // 8c · E22: the cluster that holds α 1 through the death dim
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
            _healthLowWord = InkText("Low", root, "LOW", UiType.Label, UiColor.StateDanger, TextAnchor.MiddleLeft);
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

            float edge = -(WeaponRing + 16f);
            BuildInkAmmo(root, host, back, edge);

            // T2 (calm only): the weapon and its build on one line (AD 8-R SHOULD-A1).
            RectTransform calm = UiFactory.NewRect("WeaponCalm", root);
            Place(calm, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(edge, 118f), new Vector2(340f, 18f));
            _classicName = InkText("Name", calm, "—", UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
            UiFactory.Fill(_classicName.rectTransform);
            _calmOnly.Add(UiFactory.Group(calm.gameObject));
        }

        /// <summary>
        /// The count ("24" NumericXL over "/ 30"), the ladder on an ink brush, the LOW / EMPTY word with the reload prompt, and
        /// SUSPENDED in the same slot (one slot, one word - AD 8-R MUST-1). <paramref name="edge"/> is where the count ends.
        /// </summary>
        private void BuildInkAmmo(RectTransform root, RectTransform host, RectTransform back, float edge)
        {
            const float countWidth = 170f;
            const float ladderWidth = 230f;
            var anchor = new Vector2(1f, 0f);
            float ladderLeft = edge - ladderWidth;
            float captionLeft = edge - countWidth;

            _ammoCounter = UiCounter.Create("Ammo", host, UiCounterStyle.Fraction, 0, UiType.NumericXL, UiColor.TextPrimary, 0);
            Place((RectTransform)_ammoCounter.transform, anchor, new Vector2(1f, 0f), new Vector2(edge, 30f), new Vector2(countWidth, 48f));
            _ammoValue = _ammoCounter.ValueText;
            InkAll(_ammoCounter.gameObject);

            RectTransform under = UiFactory.NewRect("Underline", back);
            Place(under, anchor, new Vector2(0f, 0.5f), new Vector2(ladderLeft - 8f, 20f), new Vector2(ladderWidth + 16f, 14f));
            under.localRotation = Quaternion.Euler(0f, 0f, -StrokeLean);
            Image brush = UiFactory.NewImage("Brush", under, UiColor.HudInk, BrushSprite());
            UiFactory.Fill(brush.rectTransform);
            Depth(brush);

            RectTransform ladder = UiFactory.NewRect("Ladder", host);
            Place(ladder, anchor, new Vector2(0f, 0f), new Vector2(ladderLeft, 14f), new Vector2(ladderWidth, LadderHeight));
            ladder.localRotation = Quaternion.Euler(0f, 0f, -StrokeLean);
            _ladderBack = LadderImage("Back", ladder, UiColor.TextMuted);
            _ladderFill = LadderImage("Fill", ladder, UiColor.TextPrimary);
            _ladderWidth = ladderWidth;

            _reloadCaption = InkText("ReloadCaption", host, string.Empty, UiType.Label, UiColor.BarReload, TextAnchor.MiddleLeft);
            _reloadCaption.fontStyle = FontStyles.Bold;
            RectTransform captionRow = _reloadCaption.rectTransform;
            Place(captionRow, anchor, new Vector2(0f, 0.5f), new Vector2(captionLeft, 92f), new Vector2(220f, ReloadPromptSize + 6f));
            _reloadPrompt = UiInputGlyphs.Build("ReloadPrompt", captionRow, UiPromptAction.Reload, UiColor.TextPrimary, UiColor.SurfaceInset, ReloadPromptSize);
            _reloadPromptHome = new Vector2(Mathf.Ceil(_reloadCaption.GetPreferredValues("EMPTY").x) + (int)UiSpace.S, 0f);
            Place((RectTransform)_reloadPrompt.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), _reloadPromptHome, ((RectTransform)_reloadPrompt.transform).sizeDelta);
            _reloadPromptGroup = UiFactory.Group(_reloadPrompt.gameObject);   // 8c · E23: α + 4 px Fast in, Exit Fast out
            _reloadPrompt.gameObject.SetActive(false);
            _reloadCaptionGroup = UiFactory.Group(captionRow.gameObject);

            TMP_Text suspended = InkText("Suspended", root, "SUSPENDED", UiType.Label, UiColor.StateWarning, TextAnchor.MiddleLeft);
            suspended.fontStyle = FontStyles.Bold;
            Place(suspended.rectTransform, anchor, new Vector2(0f, 0.5f), new Vector2(captionLeft, 92f), new Vector2(220f, ReloadPromptSize + 6f));
            _suspendedChip = suspended.gameObject;
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
            TMP_Text words = InkText("Text", row, "SPECIAL WEAPON ENDING", UiType.Label, UiColor.StateDanger, TextAnchor.MiddleCenter);
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

            _threatSlots[0] = InkThreatChip(row, "SURROUNDED", "Threat.Surrounded", UiColor.StateDanger);
            _threatSlots[1] = InkThreatChip(row, "BEHIND YOU", "Threat.BehindOuter", UiColor.StateDanger);
            _threatSlots[2] = InkThreatChip(row, "ELITE NEARBY", "Threat.EliteOuter", UiColor.StateWarning);

            float words = 0f;
            for (int i = 0; i < ThreatLabels.Length; i++)
            {
                words = Mathf.Max(words, Mathf.Ceil(_threatSlots[2].Label.GetPreferredValues(ThreatLabels[i]).x));
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
            bool danger = ThreatChipVisible(0) || ThreatChipVisible(1) || ThreatChipVisible(2) || _model.HealthLow || _model.AmmoLow ||
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
        /// The cluster's rect for a camera mode, canvas px with the origin top-left (1920 × 1080: x 1488–1888, y 908–1048;
        /// first person x 32–432, y 820–960). S-02's FP viewmodel test measures its 0 px overlap against it.
        /// </summary>
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

        private void ApplyModeLayout()
        {
            if (_weaponCluster == null)
            {
                return;
            }

            int mode = (int)DD.Prototype.CameraSystem.CameraModeService.Current;
            Vector2 size = ScreenRect.rect.size;
            if (mode == _layoutMode && size == _layoutSize)
            {
                return;
            }

            bool first = _layoutMode >= 0;
            _layoutMode = mode;
            _layoutSize = size;
            Vector2 at = WeaponClusterPosition(_weaponHome, mode == (int)DD.Prototype.CameraSystem.CameraMode.FirstPerson, size);
            if (first)
            {
                UiMotion.Move(_weaponCluster, at, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            }
            else
            {
                _weaponCluster.anchoredPosition = at;
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
            RectTransform root = InkRoot(name, anchor, anchor, position, size);
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
            Sprite brush = UiFxGlobals.InkMask(UiInkChannel.Underline);
            return brush != null ? brush : UiSprites.Solid;
        }
    }
}
