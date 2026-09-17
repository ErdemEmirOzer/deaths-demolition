using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The in-run HUD. All ten readouts the request lists, in four corner blocks plus a
    /// centre channel.
    ///
    /// WHERE EACH OF THE TEN LIVES, AND WHY THERE
    ///   top left      Round, Enemies Remaining      - pressure. Read between fights.
    ///   top right     Gold                          - a number that only ever grows.
    ///   bottom left   Health                        - read under fire, so it is on the
    ///                                                 same side as the movement hand
    ///                                                 and it is the largest bar.
    ///   bottom right  Classic Weapon, Ammo/Reload   - read under fire, aim-hand side.
    ///   bottom right  Special Weapon ground BLINK and active timer, above the weapon
    ///                 block so the two weapon states are adjacent and their
    ///                 relationship is visible.
    ///   centre top    final-seconds warning, round banners
    ///   world space   Special Weapon marker, clamped to the screen edge when off view
    ///   all four edges directional player-damage flash, UNDER every block above
    ///
    /// The four blocks sit in the corners and the middle of the screen is left empty,
    /// because the camera keeps the player near the centre and the request is explicit
    /// that the HUD must not cover the play area.
    ///
    /// REQ-057 - THE SUSPENDED CLASSIC WEAPON
    /// When a Special Weapon is active the classic weapon is NOT gone. This HUD dims the
    /// whole classic block to <see cref="UiTokens.OpacitySuspended"/> and adds a
    /// SUSPENDED pill. It never hides it, never blanks the ammo count, and never
    /// replaces the numbers with dashes: the player has to be able to see what they are
    /// going back to. 0.62 is not a taste value - it is the lowest alpha at which the
    /// dimmed text still measures 5.55:1 over the HUD backing.
    ///
    /// G-21 - THE GROUND TIMER IS A RHYTHM, THE ACTIVE TIMER IS STILL A NUMBER
    /// The two timers are NOT the same readout wearing different clothes, and that is why
    /// only one of them changed.
    ///
    ///   GROUND  answers "is it worth crossing the map for this?". That is a one-off
    ///           commitment decision made while running and being chased, and a two-digit
    ///           number can only be read standing still - which is exactly when the
    ///           player is not. So the number is gone: the drop BLINKS, faster and faster
    ///           (<see cref="GroundBlinkClock"/>), on the world beacon and on the HUD
    ///           marker at the same time and on the same beat.
    ///
    ///   ACTIVE  answers "do I have time for one more engagement?". That is a fine
    ///           decision made several times per activation, and 3 s versus 6 s changes
    ///           what the player does. No blink rate distinguishes 3 from 6. It also
    ///           already has a discrete alarm on it - the REQ-058 final-seconds vignette
    ///           and banner - and stacking a second, continuously accelerating rhythm on
    ///           top of an alarm is how two signals become one blur. So it keeps its
    ///           number, unchanged.
    ///
    /// The ground progress BAR went with the number, for the same reason: an analogue
    /// depletion bar is a precise duration readout, and leaving it in would have let the
    /// player ignore the rhythm entirely and read the bar instead.
    ///
    /// READING STRATEGY
    /// Snapshots are polled in Update (RunEvents.Current, WeaponEvents.Classic /
    /// .Special); events are used only for the moments worth animating. That is the
    /// split both contracts ask for, and it is why this class raises nothing per frame.
    /// Health is the exception and uses the static event, because PlayerHealth publishes
    /// its first snapshot in OnEnable - possibly before this HUD exists - so the binding
    /// reads <c>Snapshot</c> once rather than waiting for an event that already fired.
    ///
    /// PRESENTER (WP-005). Every polled snapshot goes through a <see cref="RunHudViewModel"/>
    /// which formats a readout only on the frame its value changes and tells the view
    /// which labels to touch. A quiet frame therefore writes no string and allocates
    /// nothing; the old per-frame ToString() on round, gold, enemies and ammo is gone.
    /// Gold additionally lands through RunEvents.GoldChanged, so the counter moves on
    /// the frame the orb was taken rather than on the director's next publish. On
    /// RunStarted the model is reset, so a HUD that survived into a second run cannot
    /// show the first run's last numbers. The player is found through PlayerRegistry,
    /// never by searching the scene.
    /// </summary>
    public sealed class RunHudScreen : UiScreen
    {
        private const float BlockWidth = 420f;
        private const float SmallBlockWidth = 360f;
        private const float GoldBlockWidth = 220f;
        private const float SpecialBlockWidth = 220f;

        // Block heights are the sum of their own rows plus the panel padding, computed
        // by hand and kept here so the arithmetic is reviewable in one place.
        private const float RoundBlockHeight = 150f;
        private const float ClassicBlockHeight = 150f;
        private const float SpecialBlockHeight = 216f;

        private const float BannerSeconds = 2.4f;
        private const float MarkerEdgeInset = 72f;

        protected override int SortingOrder
        {
            get { return UiTokens.SortingHud; }
        }

        protected override UiContext BaseContext
        {
            get { return UiContext.GameplayHud; }
        }

        protected override bool WantsUiCamera
        {
            get { return false; }
        }

        protected override bool WantsBackdrop
        {
            get { return false; }
        }

        protected override bool WantsPageGrid
        {
            get { return false; }
        }

        // ---- round / enemies
        private TMP_Text _roundValue;
        private TMP_Text _enemiesValue;
        private TMP_Text _enemiesBreakdown;

        // ---- gold
        private TMP_Text _goldValue;

        // ---- health
        private UiBar _healthBar;
        private TMP_Text _healthValue;
        private TMP_Text _healthState;

        // ---- classic weapon
        private CanvasGroup _classicGroup;
        private TMP_Text _classicName;
        private TMP_Text _classicBuild;
        private TMP_Text _ammoValue;
        private UiBar _reloadBar;
        private TMP_Text _reloadCaption;
        private GameObject _suspendedChip;

        // ---- special weapon
        private GameObject _specialBlock;
        private TMP_Text _specialPhase;
        private TMP_Text _specialTimer;

        // WP-007 (PB-2): the active timer is formatted only when its displayed tenth
        // changes (at most ten times a second), the marker distance only when the displayed
        // metre changes - never once per frame.
        private int _specialTimerTenths = int.MinValue;
        private int _markerMetres = int.MinValue;
        private TMP_Text _specialCaption;
        private Image _specialRing;
        private Image _specialPip;
        private Image _weaponIcon;
        private TMP_Text _shotModel;

        // WP-022H · Special usage v2 (WP-056 SpecialWeaponUsageEvents): a field / shots line
        // under the ring and the AP-014 glyph next to the phase text. State is integers
        // and one expiry time; the line's text is rebuilt only when a displayed integer
        // changes (ADR-037-1 - no per-frame string work).
        private TMP_Text _specialUsage;

        /// <summary>WP-022I enemy status markers (test readout).</summary>
        public UiEnemyStatusMarkers EnemyStatus => _enemyStatus;
        private UiEnemyStatusMarkers _enemyStatus;
        private Image _specialGlyph;
        private int _swShots;
        private int _swFieldHits;
        private float _swFieldEndsAt = -1f;
        private float _swFieldEndedAt = -1f;
        private int _swFieldEndedCount;
        private int _swShownSeconds = -1, _swShownHits = -1, _swShownShots = -1, _swShownMode = -1;
        private const float ShotModelWidth = 150f;
        private Image _healthIcon;
        private Image _reticle;
        private readonly System.Collections.Generic.List<RectTransform> _panels =
            new System.Collections.Generic.List<RectTransform>(6);

        // ---- player damage direction (REQ-045)
        private UiHitDirectionLayer _damageEdges;

        /// <summary>
        /// The directional damage edges this HUD owns. Exposed so the regression test can
        /// prove the HUD actually built them, rather than finding a loose component in the
        /// scene and assuming somebody meant it.
        /// </summary>
        public UiHitDirectionLayer DamageEdges
        {
            get { return _damageEdges; }
        }

        // ---- warnings and banners
        private GameObject _warningVignette;
        private GameObject _warningBanner;
        private TMP_Text _bannerText;
        private CanvasGroup _bannerGroup;
        private float _bannerRemaining;

        // ---- world marker
        private RectTransform _marker;
        private RectTransform _markerArrow;
        private TMP_Text _markerLabel;
        private Image _markerCore;
        private Image _markerRing;
        private Image _markerHalo;

        private bool _healthBound;
        private bool _lowHealthShown;

        // ---- threat warnings (WP-022B, ThreatHudFeed / WP-033)
        private GameObject _threatRow;
        private GameObject _threatSurrounded;
        private GameObject _threatRear;
        private GameObject _threatStrong;
        private TMP_Text _threatStrongText;
        private bool _threatSurroundedShown;
        private bool _threatRearShown;
        private bool _threatStrongShown;
        private int _threatWindupShown;

        private readonly RunHudViewModel _model = new RunHudViewModel();

        /// <summary>The presenter this HUD draws from. Exposed so a test can read what is shown.</summary>
        public RunHudViewModel Model
        {
            get { return _model; }
        }

        protected override void Build()
        {
            // FIRST, and the order is the whole point. uGUI draws siblings back to front,
            // so anything built here ends up UNDER every readout that follows. A damage
            // edge at 0.92 alpha drawn over the health block would blank the one number
            // the player needs while they are being hit - the readability requirement and
            // the damage requirement pulling in opposite directions, settled by depth
            // rather than by softening the flash until it stops working.
            BuildDamageEdges();

            BuildRoundBlock();
            BuildGoldBlock();
            BuildHealthBlock();
            BuildWeaponBlocks();
            BuildCentreChannel();
            BuildMarker();
            BuildReticle();
            BuildThreatChips();

            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
            RunEvents.RoundPoolExhausted += OnPoolExhausted;
            RunEvents.GoldChanged += OnGoldChanged;

            WeaponEvents.SpecialWeaponSpawned += OnSpecialSpawned;
            WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds += OnSpecialFinalSeconds;
            WeaponEvents.SpecialWeaponEnded += OnSpecialEnded;
            SpecialWeaponUsageEvents.Usage += OnSpecialUsage;
            WeaponEvents.SpecialWeaponGroundExpired += OnSpecialGroundExpired;

            PlayerHealth.AnyHealthChanged += ApplyHealth;
        }

        protected override void Start()
        {
            base.Start();
            BindHealthSnapshot();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Cursor.visible = true;
            RunEvents.RunStarted -= OnRunStarted;
            RunEvents.RoundStarted -= OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted;
            RunEvents.RoundPoolExhausted -= OnPoolExhausted;
            RunEvents.GoldChanged -= OnGoldChanged;

            WeaponEvents.SpecialWeaponSpawned -= OnSpecialSpawned;
            WeaponEvents.SpecialWeaponPickedUp -= OnSpecialPickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds -= OnSpecialFinalSeconds;
            WeaponEvents.SpecialWeaponEnded -= OnSpecialEnded;
            SpecialWeaponUsageEvents.Usage -= OnSpecialUsage;
            WeaponEvents.SpecialWeaponGroundExpired -= OnSpecialGroundExpired;

            PlayerHealth.AnyHealthChanged -= ApplyHealth;
        }

        /// <summary>
        /// Reads the player's health ONCE at bind time instead of waiting for an event.
        /// PlayerHealth raises its first HealthChanged in OnEnable, which can happen
        /// before this HUD exists; a HUD that only listened would sit on an empty bar
        /// until the player first took damage.
        /// </summary>
        private void BindHealthSnapshot()
        {
            if (_healthBound)
            {
                return;
            }

            // Through the registry, not a scene search: PlayerRegistry.Primary is a list
            // read, and it is the same hand-off point every other system uses.
            PlayerRig rig = PlayerRegistry.Primary;
            PlayerHealth health = rig != null ? rig.Health : null;
            if (health == null)
            {
                return;
            }

            _healthBound = true;
            ApplyHealth(health.Snapshot);
        }

        // ================================================================== blocks
        //
        // WP-022 HUD v2 layout (AP-012 composite, 1920x1080 reference, SafeInset 32):
        //
        //   [GOLD]                 [ROUND · ENEMIES]                          .
        //   .                                                                  .
        //   .                          (reticle)                               .
        //   .                                                                  .
        //   [HEALTH]              [SPECIAL WEAPON]              [CLASSIC WEAPON]
        //
        // The centre of the screen is the play area (HudSafeZone): nothing here may
        // enter it, and the test HudSafeZone_PanellerOyunAlaniniOrtmez proves it.

        /// <summary>Icon size inside a HUD panel. Reads at 28 px; the atlas glyphs are 128 px.</summary>
        private const float PanelIconSize = 28f;
        private const float WeaponIconSize = 64f;
        /// <summary>
        /// AD MUST-1: the weapon glyph anchors the panel. The atlas cell is square (the
        /// rifle fills ~90 % of its width), so the slot is a 96 px square: the silhouette
        /// comes out ~86 px wide, about 60 % of the panel height, as in the composite.
        /// </summary>
        private const float WeaponIconWidth = 96f;
        private const float SpecialRingSize = 132f;
        private const float ReticleSize = 40f;

        private void BuildRoundBlock()
        {
            RectTransform block = HudPanel("Round", new Vector2(0.5f, 1f), SmallBlockWidth, RoundBlockHeight);
            RectTransform host = UiFactory.CardContent(block, UiSpace.L);

            var stack = new UiStack(host);

            TMP_Text roundLabel = UiFactory.Label(
                "RoundLabel", host, "ROUND", UiType.Label, UiColor.TextMuted, TextAnchor.UpperCenter);
            stack.Add((RectTransform)roundLabel.transform, UiFactory.LineHeight(UiType.Label));

            _roundValue = UiFactory.Numeric(
                "RoundValue", host, "—", UiType.NumericL, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            stack.Add((RectTransform)_roundValue.transform, 36f);

            stack.Gap(UiSpace.XS);
            stack.Add((RectTransform)UiFactory.Divider("D", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.S);

            // Waves glyph + count on one line, centred: "18 REMAINING" is one fact.
            RectTransform enemiesRow = UiFactory.NewRect("EnemiesRow", host);
            stack.Add(enemiesRow, 26f);
            Image waves = UiArt.IconImage("Icon", enemiesRow, "Hud.Round", UiColor.TextSecondary, 22f);
            RectTransform wavesRect = (RectTransform)waves.transform;
            wavesRect.anchorMin = new Vector2(0.5f, 0.5f);
            wavesRect.anchorMax = new Vector2(0.5f, 0.5f);
            wavesRect.pivot = new Vector2(1f, 0.5f);
            wavesRect.anchoredPosition = new Vector2(-44f, 0f);

            _enemiesValue = UiFactory.Numeric(
                "EnemiesValue", enemiesRow, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform enemiesValueRect = UiFactory.Fill((RectTransform)_enemiesValue.transform);
            enemiesValueRect.offsetMin = new Vector2(SmallBlockWidth * 0.5f - (int)UiSpace.L - 36f, 0f);

            TMP_Text enemiesLabel = UiFactory.Label(
                "EnemiesLabel", enemiesRow, "REMAINING", UiType.Label, UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform enemiesLabelRect = UiFactory.Fill((RectTransform)enemiesLabel.transform);
            enemiesLabelRect.offsetMin = new Vector2(SmallBlockWidth * 0.5f - (int)UiSpace.L + 24f, 0f);

            _enemiesBreakdown = UiFactory.Label(
                "EnemiesBreakdown", host, "—", UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperCenter);
            stack.Add((RectTransform)_enemiesBreakdown.transform, UiFactory.LineHeight(UiType.Caption));
        }

        private void BuildGoldBlock()
        {
            RectTransform block = HudPanel("Gold", new Vector2(0f, 1f), GoldBlockWidth, 72f);
            RectTransform host = UiFactory.CardContent(block, UiSpace.L);

            // AP-012 old-coin glyph; the disc is the no-art fallback.
            Image coin = UiArt.HasIcon("Hud.Gold")
                ? UiArt.IconImage("Coin", host, "Hud.Gold", UiColor.StateGold, PanelIconSize)
                : UiFactory.NewImage("Coin", host, UiColor.StateGold, UiSprites.Disc);
            var coinRect = (RectTransform)coin.transform;
            coinRect.anchorMin = new Vector2(0f, 0.5f);
            coinRect.anchorMax = new Vector2(0f, 0.5f);
            coinRect.pivot = new Vector2(0f, 0.5f);
            coinRect.sizeDelta = new Vector2(PanelIconSize, PanelIconSize);
            coinRect.anchoredPosition = Vector2.zero;

            _goldValue = UiFactory.Numeric(
                "Value", host, "0", UiType.NumericL, UiColor.StateGold, TextAnchor.MiddleLeft);
            RectTransform valueRect = UiFactory.Fill((RectTransform)_goldValue.transform);
            valueRect.offsetMin = new Vector2(PanelIconSize + (int)UiSpace.M, 0f);
        }

        private void BuildHealthBlock()
        {
            RectTransform block = HudPanel("Health", new Vector2(0f, 0f), BlockWidth, 104f);
            RectTransform host = UiFactory.CardContent(block, UiSpace.L);

            Image heart = UiArt.IconImage("Heart", host, "Hud.Health", UiColor.BarHealth, PanelIconSize);
            var heartRect = (RectTransform)heart.transform;
            heartRect.anchorMin = new Vector2(0f, 1f);
            heartRect.anchorMax = new Vector2(0f, 1f);
            heartRect.pivot = new Vector2(0f, 1f);
            heartRect.sizeDelta = new Vector2(PanelIconSize, PanelIconSize);
            heartRect.anchoredPosition = Vector2.zero;
            _healthIcon = heart;

            float textLeft = heart.enabled ? PanelIconSize + (int)UiSpace.M : 0f;

            TMP_Text label = UiFactory.Label("Label", host, "HEALTH", UiType.Label, UiColor.TextMuted);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Label));
            labelRect.offsetMin = new Vector2(textLeft, labelRect.offsetMin.y);
            labelRect.anchoredPosition = new Vector2(textLeft, -4f);

            _healthValue = UiFactory.Numeric(
                "Value", host, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleRight);
            RectTransform valueRect = (RectTransform)_healthValue.transform;
            valueRect.anchorMin = new Vector2(1f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 1f);
            valueRect.sizeDelta = new Vector2(160f, 26f);
            valueRect.anchoredPosition = Vector2.zero;

            _healthBar = UiFactory.Bar("Bar", host, UiColor.BarHealth, 18f);
            RectTransform barRect = (RectTransform)_healthBar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0f, 0f);
            barRect.sizeDelta = new Vector2(0f, 18f);
            barRect.anchoredPosition = new Vector2(0f, UiFactory.LineHeight(UiType.Caption) + (int)UiSpace.XS);

            _healthState = UiFactory.Label("State", host, string.Empty, UiType.Caption, UiColor.AccentMoss);
            RectTransform stateRect = (RectTransform)_healthState.transform;
            stateRect.anchorMin = new Vector2(0f, 0f);
            stateRect.anchorMax = new Vector2(1f, 0f);
            stateRect.pivot = new Vector2(0f, 0f);
            stateRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Caption));
            stateRect.anchoredPosition = Vector2.zero;
        }

        private void BuildWeaponBlocks()
        {
            // ---- Special Weapon: radial timer, bottom centre -------------------
            // Its own nested Canvas: the ring fill and the digits change many times a
            // second while a Special is active, and a nested canvas confines that
            // rebuild to a handful of vertices instead of the whole HUD (WP-007 hand-off).
            RectTransform special = HudPanel(
                "SpecialWeapon", new Vector2(0.5f, 0f), SpecialBlockWidth, SpecialBlockHeight);
            special.gameObject.AddComponent<Canvas>();
            _specialBlock = special.gameObject;
            RectTransform specialHost = UiFactory.CardContent(special, UiSpace.L);

            _specialPhase = UiFactory.Label(
                "Phase", specialHost, "ON THE FIELD", UiType.Label, UiColor.StateGold, TextAnchor.UpperCenter);
            RectTransform phaseRect = UiFactory.Fill((RectTransform)_specialPhase.transform);
            phaseRect.anchorMin = new Vector2(0f, 1f);
            phaseRect.pivot = new Vector2(0.5f, 1f);
            phaseRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Label));
            phaseRect.anchoredPosition = Vector2.zero;

            // Ring track + fill, centred under the phase line.
            RectTransform ringRoot = UiFactory.NewRect("Ring", specialHost);
            ringRoot.anchorMin = new Vector2(0.5f, 1f);
            ringRoot.anchorMax = new Vector2(0.5f, 1f);
            ringRoot.pivot = new Vector2(0.5f, 1f);
            ringRoot.sizeDelta = new Vector2(SpecialRingSize, SpecialRingSize);
            ringRoot.anchoredPosition = new Vector2(0f, -(UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS));

            Image track = UiFactory.NewImage("Track", ringRoot, UiColor.SurfaceInset, UiSprites.Ring);
            UiFactory.Fill((RectTransform)track.transform);

            _specialRing = UiFactory.NewImage("Fill", ringRoot, UiColor.BarSpecial, UiSprites.Ring);
            UiFactory.Fill((RectTransform)_specialRing.transform);
            _specialRing.type = Image.Type.Filled;
            _specialRing.fillMethod = Image.FillMethod.Radial360;
            _specialRing.fillOrigin = (int)Image.Origin360.Top;
            _specialRing.fillClockwise = false;
            _specialRing.fillAmount = 1f;

            // The pip lives in the ring's centre while the weapon is on the ground: the
            // eye that learned to look there for the timer still finds the answer - an
            // answer with no number in it.
            _specialPip = UiArt.HasIcon("Hud.Special")
                ? UiArt.IconImage("GroundPip", ringRoot, "Hud.Special", UiColor.StateGold, 40f)
                : UiFactory.NewImage("GroundPip", ringRoot, UiColor.StateGold, UiSprites.Disc);
            RectTransform pipRect = (RectTransform)_specialPip.transform;
            pipRect.sizeDelta = new Vector2(40f, 40f);
            pipRect.anchoredPosition = Vector2.zero;
            _specialPip.gameObject.SetActive(false);

            _specialTimer = UiFactory.Numeric(
                "Timer", ringRoot, "0.0", UiType.NumericM, UiColor.StateGold, TextAnchor.MiddleCenter);
            RectTransform timerRect = UiFactory.Fill((RectTransform)_specialTimer.transform, 20f);

            _specialCaption = UiFactory.Label(
                "Caption", specialHost, string.Empty, UiType.Caption, UiColor.TextPrimary, TextAnchor.LowerCenter);
            RectTransform captionRect = UiFactory.Fill((RectTransform)_specialCaption.transform);
            captionRect.anchorMax = new Vector2(1f, 0f);
            captionRect.pivot = new Vector2(0.5f, 0f);
            captionRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Caption));
            captionRect.anchoredPosition = Vector2.zero;

            // WP-022H: usage line between the ring and the name - "FIELD 4s · 3 HIT" while a
            // field lives, "SHOTS 2" between fields, "FIELD ENDED · 5 HIT" for two seconds.
            _specialUsage = UiFactory.Label(
                "Usage", specialHost, string.Empty, UiType.Caption, UiColor.StateGold, TextAnchor.LowerCenter);
            RectTransform usageRect = UiFactory.Fill((RectTransform)_specialUsage.transform);
            usageRect.anchorMax = new Vector2(1f, 0f);
            usageRect.pivot = new Vector2(0.5f, 0f);
            usageRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.Caption));
            usageRect.anchoredPosition = new Vector2(0f, UiFactory.LineHeight(UiType.Caption));
            _specialUsage.fontStyle = TMPro.FontStyles.Bold;

            // AP-014 glyph (Sw.<id>, AP-012 Special.<id> as the fallback) at the block's top-left.
            _specialGlyph = UiFactory.NewImage("Glyph", specialHost, UiColor.StateGold, UiSprites.Disc);
            _specialGlyph.preserveAspect = true;
            RectTransform glyphRect = (RectTransform)_specialGlyph.transform;
            glyphRect.anchorMin = new Vector2(0f, 1f);
            glyphRect.anchorMax = new Vector2(0f, 1f);
            glyphRect.pivot = new Vector2(0f, 1f);
            glyphRect.sizeDelta = new Vector2(28f, 28f);
            glyphRect.anchoredPosition = Vector2.zero;
            _specialGlyph.enabled = false;

            _specialBlock.SetActive(false);

            // ---- classic weapon, bottom right ---------------------------------
            RectTransform classic = HudPanel(
                "ClassicWeapon", new Vector2(1f, 0f), BlockWidth, ClassicBlockHeight);
            RectTransform host = UiFactory.CardContent(classic, UiSpace.L);

            // The CanvasGroup is on the CONTENT, not on the panel: REQ-057 dims the
            // information, never the surface. Keeping the backing at full opacity is
            // what makes the 5.55:1 measurement for dimmed text valid, and it keeps the
            // SUSPENDED pill - which is parented to the panel, outside this group - at
            // full strength while everything it is explaining fades behind it.
            _classicGroup = UiFactory.Group(host.gameObject);

            _weaponIcon = UiArt.IconImage("WeaponIcon", host, "Weapon.AssaultRifle", UiColor.TextPrimary, WeaponIconSize);
            var iconRect = (RectTransform)_weaponIcon.transform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(WeaponIconWidth, WeaponIconWidth);
            iconRect.anchoredPosition = Vector2.zero;
            float textLeft = WeaponIconWidth + (int)UiSpace.M;

            RectTransform textColumn = UiFactory.NewRect("Text", host);
            UiFactory.Fill(textColumn);
            textColumn.offsetMin = new Vector2(textLeft, 0f);

            var stack = new UiStack(textColumn);

            _classicName = UiFactory.Label("Name", textColumn, "—", UiType.Label, UiColor.TextMuted);
            RectTransform nameRect = stack.Add(
                (RectTransform)_classicName.transform, UiFactory.LineHeight(UiType.Label));

            _classicBuild = UiFactory.Label("Build", textColumn, string.Empty, UiType.Caption, UiColor.TextSecondary);
            RectTransform buildRect = stack.Add((RectTransform)_classicBuild.transform, UiFactory.LineHeight(UiType.Caption));
            buildRect.offsetMax = new Vector2(-ShotModelWidth - (int)UiSpace.S, buildRect.offsetMax.y);

            // WP-022C: roster type / shot model ("SHOTGUN · PELLET x8") on the build row's
            // right, set once per weapon change - the pellet contract made visible. The
            // name row keeps its full width: "ASSAULT RIFLE" at Label size already fills
            // it (the 1080p pause capture showed the two overlapping on one row).
            _shotModel = UiFactory.Label("ShotModel", textColumn, string.Empty, UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleRight);
            RectTransform modelRect = (RectTransform)_shotModel.transform;
            modelRect.anchorMin = new Vector2(1f, 1f);
            modelRect.anchorMax = new Vector2(1f, 1f);
            modelRect.pivot = new Vector2(1f, 1f);
            modelRect.sizeDelta = new Vector2(ShotModelWidth, UiFactory.LineHeight(UiType.Caption));
            modelRect.anchoredPosition = buildRect.anchoredPosition;

            stack.Gap(UiSpace.XS);

            _ammoValue = UiFactory.Numeric("Ammo", textColumn, "—", UiType.NumericL, UiColor.TextPrimary);
            stack.Add((RectTransform)_ammoValue.transform, 38f);

            stack.Gap(UiSpace.XS);

            _reloadBar = UiFactory.Bar("ReloadBar", textColumn, UiColor.BarReload, 8f);
            stack.Add((RectTransform)_reloadBar.transform, 8f);

            stack.Gap(UiSpace.XS);

            _reloadCaption = UiFactory.Label(
                "ReloadCaption", textColumn, string.Empty, UiType.Caption, UiColor.BarReload);
            stack.Add((RectTransform)_reloadCaption.transform, UiFactory.LineHeight(UiType.Caption));

            // REQ-057 pill. Sits over the block, never instead of it.
            TMP_Text chip = UiFactory.Chip(
                "Suspended", classic, "SUSPENDED", UiColor.StateWarning, UiColor.SurfaceInset, 148f, 26f);
            _suspendedChip = chip.transform.parent.gameObject;
            var chipRect = (RectTransform)_suspendedChip.transform;
            chipRect.anchorMin = new Vector2(1f, 1f);
            chipRect.anchorMax = new Vector2(1f, 1f);
            chipRect.pivot = new Vector2(1f, 1f);
            chipRect.anchoredPosition = new Vector2(-(int)UiSpace.L, -(int)UiSpace.L);
            _suspendedChip.SetActive(false);
        }

        /// <summary>
        /// Directional damage edges. Built here and not installed by the player module,
        /// because the HUD owns the canvas and the draw order, and the draw order is the
        /// part that has to be right.
        /// </summary>
        private void BuildDamageEdges()
        {
            RectTransform layer = UiFactory.NewRect("DamageEdges", ScreenRect);
            UiFactory.Fill(layer);
            layer.SetAsFirstSibling();

            _damageEdges = layer.gameObject.AddComponent<UiHitDirectionLayer>();
        }

        /// <summary>
        /// The aim reticle (WP-022): AP-012 tick reticle drawn at the projected aim point,
        /// so pointer and stick aim share one mark. Its own nested canvas - it moves every
        /// frame and must not rebuild the HUD mesh while doing so. Never a raycast target.
        /// </summary>
        private void BuildReticle()
        {
            RectTransform layer = UiFactory.NewRect("ReticleLayer", ScreenRect);
            UiFactory.Fill(layer);
            layer.gameObject.AddComponent<Canvas>();

            _reticle = UiFactory.NewImage("Reticle", layer, UiColor.TextPrimary, UiArt.Reticle(false));
            var rect = (RectTransform)_reticle.transform;
            rect.sizeDelta = new Vector2(ReticleSize, ReticleSize);
            rect.anchoredPosition = Vector2.zero;
            _reticle.preserveAspect = true;
            SetAlpha(_reticle, 0.9f);
            rect.anchoredPosition = OffScreen;
        }

        private void BuildCentreChannel()
        {
            // Edge vignette for the final-seconds warning. Two ramps, so the centre of
            // the screen - where the player is aiming - stays completely clear.
            RectTransform vignette = UiFactory.NewRect("Warning", ScreenRect);
            UiFactory.Fill(vignette);
            _warningVignette = vignette.gameObject;

            Image top = UiFactory.NewImage("Top", vignette, UiColor.StateDanger, UiSprites.FadeDown);
            RectTransform topRect = (RectTransform)top.transform;
            topRect.anchorMin = new Vector2(0f, 0.82f);
            topRect.anchorMax = new Vector2(1f, 1f);
            topRect.offsetMin = Vector2.zero;
            topRect.offsetMax = Vector2.zero;

            Image bottom = UiFactory.NewImage("Bottom", vignette, UiColor.StateDanger, UiSprites.FadeUp);
            RectTransform bottomRect = (RectTransform)bottom.transform;
            bottomRect.anchorMin = new Vector2(0f, 0f);
            bottomRect.anchorMax = new Vector2(1f, 0.18f);
            bottomRect.offsetMin = Vector2.zero;
            bottomRect.offsetMax = Vector2.zero;

            // WP-022G: the final-seconds pulse band scales with the flash-reduction setting
            // (built per screen; the setting applies to the next run's HUD).
            vignette.gameObject.AddComponent<UiPulse>().Configure(
                new Graphic[] { top, bottom }, 0.10f * UiTokens.FlashScale, 0.30f * UiTokens.FlashScale, UiTokens.PulsePeriod * 0.5f);

            _warningVignette.SetActive(false);

            // Final-seconds banner, directly under the round block.
            TMP_Text warning = UiFactory.Chip(
                "FinalSeconds", ScreenRect, "SPECIAL WEAPON ENDING",
                UiColor.StateDanger, UiColor.SurfaceInset, 420f, 44f);

            _warningBanner = warning.transform.parent.gameObject;
            var warningRect = (RectTransform)_warningBanner.transform;
            warningRect.anchorMin = new Vector2(0.5f, 1f);
            warningRect.anchorMax = new Vector2(0.5f, 1f);
            warningRect.pivot = new Vector2(0.5f, 1f);
            warningRect.anchoredPosition = new Vector2(0f, -((int)UiTokens.SafeInset + RoundBlockHeight + (int)UiSpace.M));
            _warningBanner.SetActive(false);

            // Round / phase banner, just below the warning slot so the two never collide.
            _bannerText = UiFactory.Label(
                "Banner", ScreenRect, string.Empty, UiType.H1, UiColor.TextPrimary, TextAnchor.UpperCenter);
            RectTransform bannerRect = (RectTransform)_bannerText.transform;
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.sizeDelta = new Vector2(900f, 120f);
            bannerRect.anchoredPosition = new Vector2(0f, -((int)UiTokens.SafeInset + RoundBlockHeight + (int)UiSpace.M + 48f));

            _bannerGroup = UiFactory.Group(_bannerText.gameObject);
            _bannerGroup.alpha = 0f;
        }

        private void BuildMarker()
        {
            // Own nested canvas: the marker moves and blinks every frame of the ground
            // phase (WP-007 hand-off: ~62 KB/frame of HUD rebuild). Confined here, the
            // rebuild is a few quads.
            RectTransform layer = UiFactory.NewRect("MarkerLayer", ScreenRect);
            UiFactory.Fill(layer);
            layer.gameObject.AddComponent<Canvas>();

            // WP-022I: enemy status markers (NET / DAZED) share the marker canvas.
            RectTransform statusHost = UiFactory.NewRect("EnemyStatus", layer);
            UiFactory.Fill(statusHost);
            _enemyStatus = statusHost.gameObject.AddComponent<UiEnemyStatusMarkers>();
            _enemyStatus.Configure(ScreenRect);

            _marker = UiFactory.NewRect("SpecialWeaponMarker", layer);
            _marker.sizeDelta = new Vector2(56f, 56f);

            _markerHalo = UiFactory.NewImage("Halo", _marker, UiColor.SurfaceInset, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_markerHalo.transform);
            SetAlpha(_markerHalo, 0.65f);

            // AP-010 beacon marker (greyscale diamond, DEC-053): tinted with the beacon
            // reserve colour at runtime. The procedural ring/disc pair is the fallback.
            bool artMarker = UiTheme.Active != null && UiTheme.Active.HudMarker != null;
            _markerRing = UiFactory.NewImage("Ring", _marker, UiColor.StateGold, artMarker ? UiArt.HudMarker() : UiSprites.Ring);
            UiFactory.Fill((RectTransform)_markerRing.transform);
            _markerRing.preserveAspect = true;

            _markerCore = UiFactory.NewImage("Core", _marker, UiColor.StateGold, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_markerCore.transform, artMarker ? 22f : 18f);

            _markerArrow = (RectTransform)UiFactory.NewImage(
                "Arrow", _marker, UiColor.StateGold, UiSprites.Chevron).transform;
            _markerArrow.anchorMin = new Vector2(0.5f, 0.5f);
            _markerArrow.anchorMax = new Vector2(0.5f, 0.5f);
            _markerArrow.pivot = new Vector2(-0.4f, 0.5f);
            _markerArrow.sizeDelta = new Vector2(22f, 22f);
            _markerArrow.anchoredPosition = Vector2.zero;
            _markerArrow.gameObject.SetActive(false);

            _markerLabel = UiFactory.Label(
                "Distance", _marker, string.Empty, UiType.Label, UiColor.StateGold, TextAnchor.UpperCenter);
            RectTransform labelRect = (RectTransform)_markerLabel.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(160f, 24f);
            labelRect.anchoredPosition = new Vector2(0f, -(int)UiSpace.XS);

            _marker.gameObject.SetActive(false);
        }

        /// <summary>
        /// A HUD panel: near-opaque backing docked to a screen edge or corner, wearing the
        /// AP-012 ink frame when the atlas is present. The backing is opaque on purpose -
        /// a translucent HUD cannot guarantee a text contrast ratio, because the ratio
        /// would depend on what the camera happens to be pointing at.
        /// </summary>
        private RectTransform HudPanel(string name, Vector2 anchor, float width, float height, float stackOffset = 0f)
        {
            RectTransform panel = UiFactory.FramedCard(
                name, ScreenRect, UiColor.BorderHairline, UiColor.HudInk, UiFrame.HudPanel, UiColor.TextSecondary);
            UiFactory.Corner(panel, anchor, width, height);

            if (stackOffset > 0f)
            {
                Vector2 position = panel.anchoredPosition;
                position.y += Mathf.Approximately(anchor.y, 0f) ? stackOffset : -stackOffset;
                panel.anchoredPosition = position;
            }

            _panels.Add(panel);
            return panel;
        }

        /// <summary>
        /// The HUD panels this screen built, for the safe-zone test. Nothing else reads it.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<RectTransform> Panels
        {
            get { return _panels; }
        }

        /// <summary>
        /// The play area no HUD panel may cover (REQ-104/105, DEC-077): the centre 62%
        /// of the width and the band from 25% to 80% of the height - the capsule, the
        /// enemies closing on it and the aim plane at the gameplay camera distance. The
        /// top strip holds the round chip, the bottom strip the three lower panels.
        /// Fractions of the canvas rect; UiArtSliceTests measures every panel against it.
        /// </summary>
        public static readonly Rect HudSafeZone = new Rect(0.19f, 0.25f, 0.62f, 0.55f);

        // ============================================================ threat chips

        private const float ThreatChipWidth = 200f;
        private const float ThreatChipHeight = 40f; // AD v005 MUST-1: 20 px Body bold, 20 px glyph inside, 40 px tall
        private const float ThreatGlyphSize = 20f;

        /// <summary>
        /// Combat warnings from the threat director (WP-033 <see cref="ThreatHudFeed"/>):
        /// three chips under the round block - SURROUNDED, BEHIND YOU, ELITE/ULTRA NEARBY.
        /// Each carries a glyph AND a word (REQ-105 shape + colour; colour-blind safe),
        /// sits in the top strip outside <see cref="HudSafeZone"/>, and is toggled only
        /// on a state change (no per-frame rebuild). Low health already has its own
        /// readout in the health panel; windup count is an event for the audio owner.
        /// </summary>
        private void BuildThreatChips()
        {
            RectTransform row = UiFactory.NewRect("ThreatRow", ScreenRect);
            row.anchorMin = new Vector2(0.5f, 1f);
            row.anchorMax = new Vector2(0.5f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(ThreatChipWidth * 3f + (int)UiSpace.S * 2, ThreatChipHeight);
            // 4 px under the round block (was 8): the taller chip still ends >= 6 px above
            // the safe-zone top (32 + 150 + 4 + 40 = 226 < 270 at 1080p).
            row.anchoredPosition = new Vector2(0f, -((int)UiTokens.SafeInset + RoundBlockHeight + (int)UiSpace.XS));
            _threatRow = row.gameObject;

            _threatSurrounded = ThreatChip(row, 0, "SURROUNDED", "Hud.Enemies", UiColor.StateDanger, out _);
            _threatRear = ThreatChip(row, 1, "BEHIND YOU", "Menu.Back", UiColor.StateDanger, out _);
            _threatStrong = ThreatChip(row, 2, "ELITE NEARBY", "Hud.Special", UiColor.StateWarning, out _threatStrongText);
        }

        private static GameObject ThreatChip(RectTransform row, int index, string text, string icon, UiColor color, out TMP_Text label)
        {
            label = UiFactory.Chip("Threat." + index, row, text, UiColor.SurfaceInset, color, ThreatChipWidth, ThreatChipHeight);
            var chip = (RectTransform)label.transform.parent;
            chip.anchorMin = new Vector2(0f, 0.5f);
            chip.anchorMax = new Vector2(0f, 0.5f);
            chip.pivot = new Vector2(0f, 0.5f);
            chip.anchoredPosition = new Vector2(index * (ThreatChipWidth + (int)UiSpace.S), 0f);

            // AD v005 MUST-1: Body 20 bold, and a 1 px outline in the state colour (0.6 α)
            // behind the fill so the chip separates from bright water and dark foliage.
            label.fontSize = UiTokens.FontSize(UiType.Body);
            label.fontStyle = TMPro.FontStyles.Bold;
            label.characterSpacing = 4f;
            // The chip root IS the fill image, so the outline is a uGUI Outline effect on
            // it (a child would draw over the fill): 1 px in the state colour at 0.6 α.
            Color outlineColor = UiTokens.Get(color);
            outlineColor.a = 0.6f;
            var outline = chip.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(1f, 1f);
            outline.useGraphicAlpha = true;

            Image glyph = UiArt.IconImage("Icon", chip, icon, color, ThreatGlyphSize);
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.anchorMin = new Vector2(0f, 0.5f);
            glyphRect.anchorMax = new Vector2(0f, 0.5f);
            glyphRect.pivot = new Vector2(0f, 0.5f);
            glyphRect.anchoredPosition = new Vector2(20f, 0f); // 20 px padding, glyph inside the chip
            if (index == 1)
            {
                // The back chevron points the way the danger is: behind.
                glyphRect.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.offsetMin = new Vector2(20f + ThreatGlyphSize + (int)UiSpace.S, 0f);
            labelRect.offsetMax = new Vector2(-20f, 0f);
            label.alignment = TMPro.TextAlignmentOptions.Left;

            chip.gameObject.SetActive(false);
            return chip.gameObject;
        }

        private void UpdateThreat()
        {
            if (_threatRow == null)
            {
                return;
            }

            bool fighting = RunEvents.Current.Phase == RunPhase.Fighting && UiNavigation.GameplayInputAllowed;
            ThreatSnapshot threat = ThreatDirector.Current;
            bool surrounded = fighting && threat.Encircled;
            bool rear = fighting && threat.RearAttack;
            bool strong = fighting && threat.StrongVersionPresent;
            int windup = fighting ? threat.WindingUp : 0;

            if (surrounded != _threatSurroundedShown)
            {
                _threatSurroundedShown = surrounded;
                _threatSurrounded.SetActive(surrounded);
                if (surrounded)
                {
                    UiEvents.Raise(UiEvent.HudThreatEncircled);
                }
            }

            if (rear != _threatRearShown)
            {
                _threatRearShown = rear;
                _threatRear.SetActive(rear);
                if (rear)
                {
                    UiEvents.Raise(UiEvent.HudThreatRearAttack);
                }
            }

            if (strong != _threatStrongShown)
            {
                _threatStrongShown = strong;
                if (strong)
                {
                    _threatStrongText.text = threat.StrongestTier == EnemyTier.Ultra ? "ULTRA NEARBY" : "ELITE NEARBY";
                    UiEvents.Raise(UiEvent.HudThreatStrongVersion);
                }

                _threatStrong.SetActive(strong);
            }

            if (windup > _threatWindupShown)
            {
                UiEvents.Raise(UiEvent.HudThreatWindup);
            }

            _threatWindupShown = windup;
        }

        /// <summary>The threat chips, for tests (inactive unless the director says so).</summary>
        public bool ThreatChipVisible(int index)
        {
            GameObject chip = index == 0 ? _threatSurrounded : index == 1 ? _threatRear : _threatStrong;
            return chip != null && chip.activeSelf;
        }

        // ================================================================== updates

        private void Update()
        {
            if (!_healthBound)
            {
                BindHealthSnapshot();
            }
            else
            {
                // The regen countdown ("recovery in 2.4s") moves without a health event:
                // PlayerHealth only publishes when the value changes. Polling the snapshot
                // costs nothing on a quiet frame - the presenter compares tenths and
                // integers before it formats anything.
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    ApplyHealth(rig.Health.Snapshot);
                }
            }

            UpdateRun();
            UpdateClassicWeapon();
            UpdateSpecialWeapon();
            UpdateBanner();
            UpdateThreat();
        }

        private void LateUpdate()
        {
            UpdateMarker();
            UpdateReticle();
        }

        /// <summary>
        /// Puts the reticle on the projected aim point. Pointer aim projects the cursor
        /// onto the aim plane and stick aim is a direction, so drawing the mark at the
        /// aim point rather than at the cursor gives both devices the same answer to
        /// "where will this shot go".
        /// </summary>
        private void UpdateReticle()
        {
            if (_reticle == null)
            {
                return;
            }

            PlayerRig rig = PlayerRegistry.Primary;
            Camera camera = Camera.main;
            // Only while the player can actually shoot: dead, paused and round-end shop
            // frames park the reticle, so a shop or pause screen never has a mark drifting
            // under it (and never pays for its rebuild).
            bool show = rig != null && rig.Aim != null && rig.Aim.HasAimPoint && camera != null &&
                        RunEvents.Current.Phase == RunPhase.Fighting && UiNavigation.GameplayInputAllowed;

            Vector2 local = OffScreen;
            if (show)
            {
                Vector3 viewport = camera.WorldToViewportPoint(rig.Aim.AimWorldPoint);
                if (viewport.z >= 0f)
                {
                    // Snapped to whole reference pixels: sub-pixel drift from camera sway
                    // would otherwise move (and re-batch) the reticle every frame while the
                    // aim stands still.
                    Vector2 canvasSize = ScreenRect.rect.size;
                    local = new Vector2(
                        Mathf.Round((Mathf.Clamp01(viewport.x) - 0.5f) * canvasSize.x),
                        Mathf.Round((Mathf.Clamp01(viewport.y) - 0.5f) * canvasSize.y));
                }
            }

            // The OS pointer hides while the reticle is the pointer (WP-022B); it returns
            // for the shop, the pause screen and the results.
            if (Cursor.visible == show)
            {
                Cursor.visible = !show;
            }

            // Hidden = parked off-screen, never SetActive: toggling a graphic's active state
            // re-registers it with the canvas and rebuilds the whole HUD mesh each time, which
            // is exactly the per-frame allocation the HUD budget (0 B idle) forbids.
            if (local != _reticleLocal)
            {
                _reticleLocal = local;
                _reticle.rectTransform.anchoredPosition = local;
            }
        }

        /// <summary>Parking spot for the reticle when there is no aim point (outside any canvas).</summary>
        private static readonly Vector2 OffScreen = new Vector2(-100000f, -100000f);

        private Vector2 _reticleLocal = new Vector2(float.NaN, float.NaN);

        /// <summary>
        /// The classic weapon glyph follows the weapon in hand: the WP-034 roster's
        /// HudIconId through the placeholder contract, the enum glyph as fallback, hidden
        /// when neither exists. The shot-model caption is refreshed at the same time.
        /// </summary>
        private void ApplyWeaponIcon()
        {
            if (_weaponIcon == null)
            {
                return;
            }

            ClassicWeaponId id = WeaponEvents.Classic.Id;
            ClassicWeaponDefinition def = WeaponRoster.IsLoaded ? WeaponRoster.Find(id) : null;
            Sprite sprite = def != null ? UiArt.RosterGlyph(def.HudIconId) : null;
            if (sprite == null)
            {
                sprite = UiArt.WeaponIcon(id);
            }

            _weaponIcon.sprite = sprite;
            _weaponIcon.enabled = sprite != null;

            if (_shotModel != null)
            {
                string model = string.Empty;
                if (def != null)
                {
                    PrototypeConfig config = PrototypeConfig.Active;
                    ClassicWeaponSettings settings = config != null ? def.Settings(config) : null;
                    model = def.ShotModel == WeaponShotModel.Pellet
                        ? (settings != null && settings.PelletCount > 1 ? "PELLET x" + settings.PelletCount : "PELLET")
                        : "HITSCAN";
                    model = def.TypeClass.ToString().ToUpperInvariant() + " · " + model;
                }

                if (_shotModel.text != model)
                {
                    _shotModel.text = model;
                }
            }
        }

        private void UpdateRun()
        {
            RunHudState state = RunEvents.Current;

            // The run is over: the director wipes the wallet right after it publishes
            // the summary, and the snapshot reads 0 gold for the death beat. The HUD
            // keeps the last live numbers instead - the Results screen quotes the same
            // summary the player was just looking at.
            if (state.Phase == RunPhase.Dead)
            {
                return;
            }

            ApplyRunFields(_model.ApplyRun(state));
        }

        /// <summary>
        /// Gold arrives through the economy's own event as well as through the polled
        /// snapshot, so a collected orb and the counter move in the same frame.
        /// </summary>
        private void OnGoldChanged(int balance)
        {
            if (RunEvents.Current.Phase == RunPhase.Dead)
            {
                // The wallet wipe at run end. See UpdateRun.
                return;
            }

            ApplyRunFields(_model.ApplyGold(balance));
        }

        /// <summary>
        /// A new run. The presenter forgets the previous run's numbers so the first
        /// frame of this one shows placeholders, never last run's gold or build.
        /// </summary>
        private void OnRunStarted(int startRound)
        {
            _model.Reset();
            _healthBound = false;
            ApplyRunFields(_model.ApplyRun(RunEvents.Current));
            ApplyWeaponFields(_model.ApplyWeapon(WeaponEvents.Classic));
            ApplyWeaponFields(_model.ApplyBuild(CurrentBuildDescription()));
            BindHealthSnapshot();
        }

        private void ApplyRunFields(RunHudField changed)
        {
            if ((changed & RunHudField.Round) != 0)
            {
                _roundValue.text = _model.RoundText;
            }

            if ((changed & RunHudField.Gold) != 0)
            {
                _goldValue.text = _model.GoldText;
            }

            if ((changed & RunHudField.Enemies) != 0)
            {
                _enemiesValue.text = _model.EnemiesText;
                _enemiesBreakdown.text = _model.EnemiesBreakdown;
            }
        }

        private void ApplyHealth(PlayerHealthSnapshot snapshot)
        {
            _healthBound = true;

            // One threshold, one constant. The bar, the readout and the damage edge all
            // read UiTokens.LowHealthFraction, so the HUD cannot go red on a different
            // frame from the screen edge that is telling the player why.
            RunHudField changed = _model.ApplyHealth(snapshot, UiTokens.LowHealthFraction);

            if ((changed & RunHudField.HealthBar) != 0)
            {
                if (_model.HealthLow != _lowHealthShown)
                {
                    _lowHealthShown = _model.HealthLow;
                    UiEvents.Raise(_lowHealthShown ? UiEvent.HudLowHealthEntered : UiEvent.HudLowHealthLeft);
                }

                _healthBar.SetValue(_model.HealthNormalized);
                _healthBar.SetColor(_model.HealthLow ? UiColor.BarHealthLow : UiColor.BarHealth);
                _healthValue.color = UiTokens.Get(_model.HealthLow ? UiColor.StateDanger : UiColor.TextPrimary);
                if (_healthIcon != null)
                {
                    _healthIcon.color = UiTokens.Get(_model.HealthLow ? UiColor.BarHealthLow : UiColor.BarHealth);
                }
            }

            if ((changed & RunHudField.Health) != 0)
            {
                _healthValue.text = _model.HealthText;
            }

            if ((changed & RunHudField.HealthState) != 0)
            {
                _healthState.text = _model.HealthStateText;
                _healthState.color = UiTokens.Get(ToneColor(_model.HealthStateTone));
            }
        }

        private static UiColor ToneColor(RunHudTone tone)
        {
            switch (tone)
            {
                case RunHudTone.Danger: return UiColor.StateDanger;
                case RunHudTone.Positive: return UiColor.StateSuccess;
                default: return UiColor.TextSecondary;
            }
        }

        private void UpdateClassicWeapon()
        {
            ApplyWeaponFields(_model.ApplyWeapon(WeaponEvents.Classic));
            ApplyWeaponFields(_model.ApplyBuild(CurrentBuildDescription()));
        }

        /// <summary>
        /// The build line comes from the director's RunBuildState - the producer the run
        /// summary and Results quote - so the HUD shows every branch, not only the six
        /// weapon branches the weapon's own describer knows. Cached by the producer.
        /// </summary>
        private static string CurrentBuildDescription()
        {
            RunDirector director = RunDirector.Instance;
            if (director != null && director.Build != null && director.Build.IsRunLive)
            {
                return director.Build.DescribeBuild();
            }

            return WeaponEvents.Classic.BuildDescription;
        }

        private void ApplyWeaponFields(RunHudField changed)
        {
            if ((changed & RunHudField.WeaponName) != 0)
            {
                _classicName.text = _model.WeaponName;
                ApplyWeaponIcon();
            }

            if ((changed & RunHudField.Build) != 0)
            {
                _classicBuild.text = _model.BuildText;
            }

            if ((changed & RunHudField.Ammo) != 0)
            {
                _ammoValue.text = _model.AmmoText;
            }

            if ((changed & RunHudField.Reload) != 0)
            {
                switch (_model.ReloadState)
                {
                    case RunHudReloadState.Reloading:
                        _reloadBar.SetValue(_model.ReloadProgress01, true);
                        _reloadCaption.text = "RELOADING";
                        _reloadCaption.color = UiTokens.Get(UiColor.BarReload);
                        break;

                    case RunHudReloadState.MagazineEmpty:
                        UiEvents.Raise(UiEvent.HudMagazineEmpty);
                        _reloadBar.SetValue(0f, true);
                        // No key name: the reload binding differs between keyboard and
                        // gamepad, and the HUD does not know which one is in hand.
                        _reloadCaption.text = "MAGAZINE EMPTY  ·  RELOAD";
                        _reloadCaption.color = UiTokens.Get(UiColor.StateDanger);
                        break;

                    default:
                        _reloadBar.SetValue(1f, true);
                        _reloadCaption.text = string.Empty;
                        break;
                }
            }

            if ((changed & RunHudField.Suspended) != 0)
            {
                // REQ-057. Dimmed, labelled, and still showing every number.
                bool suspended = _model.Suspended;
                _classicGroup.alpha = suspended ? UiTokens.OpacitySuspended : 1f;

                if (_suspendedChip.activeSelf != suspended)
                {
                    _suspendedChip.SetActive(suspended);
                }
            }
        }

        private void UpdateSpecialWeapon()
        {
            SpecialWeaponHudState state = WeaponEvents.Special;
            bool visible = state.Phase != SpecialWeaponPhase.None;

            if (_specialBlock.activeSelf != visible)
            {
                _specialBlock.SetActive(visible);
            }

            if (!visible)
            {
                SetGroundReadout(false);
                SetWarning(false);
                return;
            }

            string name = string.IsNullOrEmpty(state.DisplayName)
                ? "Special Weapon"
                : UiFactory.Clip(state.DisplayName, 30);

            if (state.Phase == SpecialWeaponPhase.OnGround)
            {
                _specialPhase.text = "ON THE FIELD";
                _specialPhase.color = UiTokens.Get(UiColor.StateGold);
                _specialCaption.text = name;
                _specialCaption.color = UiTokens.Get(UiColor.TextPrimary);

                // No number and no bar. Both are precise duration readouts, and both
                // would let the player ignore the rhythm that replaced them.
                SetGroundReadout(true);

                float blink = GroundBlinkClock.Intensity01(state.GroundBlinkPhase);
                ApplyPipBlink(blink, state.GroundProgress01);

                SetWarning(false);
                return;
            }

            SetGroundReadout(false);

            bool warning = state.FinalSecondsWarning;

            _specialPhase.text = warning ? "ENDING" : "ACTIVE";
            _specialPhase.color = UiTokens.Get(warning ? UiColor.StateDanger : UiColor.StateGold);
            _specialCaption.text = name;
            _specialCaption.color = UiTokens.Get(UiColor.TextPrimary);
            int tenths = Mathf.RoundToInt(Mathf.Max(0f, state.ActiveRemaining) * 10f);
            if (tenths != _specialTimerTenths)
            {
                _specialTimerTenths = tenths;
                _specialTimer.text = FormatSeconds(state.ActiveRemaining);
            }

            _specialTimer.color = UiTokens.Get(warning ? UiColor.StateDanger : UiColor.StateGold);
            _specialRing.color = UiTokens.Get(warning ? UiColor.BarHealthLow : UiColor.BarSpecial);
            _specialRing.fillAmount = state.ActiveProgress01;

            RefreshSpecialUsage();
            SetWarning(warning);
        }

        /// <summary>The usage line; string work only when a displayed integer changes.</summary>
        private void RefreshSpecialUsage()
        {
            if (_specialUsage == null)
            {
                return;
            }

            int mode;
            int seconds = 0;
            int hits = _swFieldHits;
            if (_swFieldEndsAt > 0f && Time.time < _swFieldEndsAt)
            {
                mode = 1;
                seconds = Mathf.CeilToInt(_swFieldEndsAt - Time.time);
            }
            else if (_swFieldEndedAt > 0f && Time.time - _swFieldEndedAt < 2f)
            {
                mode = 2;
                hits = _swFieldEndedCount;
            }
            else
            {
                mode = _swShots > 0 ? 3 : 0;
            }

            if (mode == _swShownMode && seconds == _swShownSeconds && hits == _swShownHits && _swShots == _swShownShots)
            {
                return;
            }

            _swShownMode = mode;
            _swShownSeconds = seconds;
            _swShownHits = hits;
            _swShownShots = _swShots;
            switch (mode)
            {
                case 1: _specialUsage.text = "FIELD " + seconds + "s" + (hits > 0 ? "  ·  " + hits + " HIT" : string.Empty); break;
                case 2: _specialUsage.text = "FIELD ENDED  ·  " + hits + " HIT"; break;
                case 3: _specialUsage.text = "SHOTS " + _swShots; break;
                default: _specialUsage.text = string.Empty; break;
            }
        }

        private void OnSpecialUsage(SpecialUsageEvent e)
        {
            switch (e.Kind)
            {
                case SpecialUsageKind.Activated:
                    _swShots = 0;
                    _swFieldHits = 0;
                    _swFieldEndsAt = -1f;
                    _swFieldEndedAt = -1f;
                    if (_specialGlyph != null)
                    {
                        Sprite glyph = UiArt.SpecialIcon(e.Id);
                        _specialGlyph.sprite = glyph;
                        _specialGlyph.enabled = glyph != null;
                    }

                    break;
                case SpecialUsageKind.Fired:
                    _swShots++;
                    break;
                case SpecialUsageKind.FieldSpawned:
                    _swFieldHits = 0;
                    _swFieldEndsAt = Time.time + Mathf.Max(0.5f, e.Seconds);
                    _swFieldEndedAt = -1f;
                    break;
                case SpecialUsageKind.Hit:
                    _swFieldHits++;
                    break;
                case SpecialUsageKind.FieldEnded:
                    _swFieldEndedCount = Mathf.Max(_swFieldHits, e.Count);
                    _swFieldEndsAt = -1f;
                    _swFieldEndedAt = Time.time;
                    break;
                case SpecialUsageKind.Deactivated:
                    _swFieldEndsAt = -1f;
                    _swFieldEndedAt = -1f;
                    if (_specialGlyph != null)
                    {
                        _specialGlyph.enabled = false;
                    }

                    if (_specialUsage != null)
                    {
                        _specialUsage.text = string.Empty;
                        _swShownMode = -1;
                    }

                    break;
            }
        }

        /// <summary>
        /// Swap the Special Weapon block between its two readouts: a blinking pip while
        /// the weapon is on the ground, digits and a bar while it is in hand.
        /// </summary>
        private void SetGroundReadout(bool onGround)
        {
            if (_specialPip != null && _specialPip.gameObject.activeSelf != onGround)
            {
                _specialPip.gameObject.SetActive(onGround);
            }

            if (_specialTimer != null && _specialTimer.gameObject.activeSelf == onGround)
            {
                _specialTimer.gameObject.SetActive(!onGround);
            }

            if (_specialRing != null && _specialRing.gameObject.activeSelf == onGround)
            {
                _specialRing.gameObject.SetActive(!onGround);
            }
        }

        /// <summary>
        /// The pip's blink. Alpha AND size move together, because alpha alone loses
        /// against a bright frame and size alone loses at a distance - and the pip is
        /// small enough that it has to survive both.
        ///
        /// Colour walks gold -> ember on the SAME urgency curve the world beacon uses, so
        /// the screen and the map agree about more than just the beat.
        /// </summary>
        private void ApplyPipBlink(float blink, float remaining01)
        {
            if (_specialPip == null)
            {
                return;
            }

            float urgency = remaining01 < GroundUrgencyStart
                ? 1f - remaining01 / GroundUrgencyStart
                : 0f;

            Color colour = Color.Lerp(
                UiTokens.Get(UiColor.StateGold), UiTokens.Get(UiColor.StateDanger), Mathf.Clamp01(urgency));
            colour.a = blink;
            _specialPip.color = colour;

            float scale = Mathf.Lerp(0.72f, 1.15f, blink);
            _specialPip.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// PROTOTYPE-ONLY: fraction of the ground timer at which the blink turns from gold
        /// to ember. Deliberately the same 0.35 the world beacon uses - two surfaces
        /// changing colour at two different moments would read as two different events.
        /// </summary>
        private const float GroundUrgencyStart = SpecialWeaponPickup.UrgencyStart;

        private void SetWarning(bool active)
        {
            if (_warningVignette.activeSelf != active)
            {
                _warningVignette.SetActive(active);
            }

            if (_warningBanner.activeSelf != active)
            {
                _warningBanner.SetActive(active);
            }
        }

        /// <summary>
        /// Projects the Special Weapon's world position onto the canvas and clamps it to
        /// the screen edge when it is behind the camera or outside the view, with a
        /// chevron pointing the way. Without the clamp the one thing the request wants
        /// the player to chase would be invisible exactly when they need to find it.
        /// </summary>
        private void UpdateMarker()
        {
            SpecialWeaponHudState state = WeaponEvents.Special;
            bool show = state.Phase == SpecialWeaponPhase.OnGround;

            Camera camera = Camera.main;
            if (!show || camera == null)
            {
                if (_marker.gameObject.activeSelf)
                {
                    _marker.gameObject.SetActive(false);
                }

                return;
            }

            if (!_marker.gameObject.activeSelf)
            {
                _marker.gameObject.SetActive(true);
            }

            Vector3 viewport = camera.WorldToViewportPoint(state.WorldPosition);
            bool behind = viewport.z < 0f;

            if (behind)
            {
                viewport.x = 1f - viewport.x;
                viewport.y = 1f - viewport.y;
            }

            bool offScreen = behind || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f;

            Vector2 canvasSize = ScreenRect.rect.size;
            var local = new Vector2(
                (viewport.x - 0.5f) * canvasSize.x,
                (viewport.y - 0.5f) * canvasSize.y);

            float halfWidth = canvasSize.x * 0.5f - MarkerEdgeInset;
            float halfHeight = canvasSize.y * 0.5f - MarkerEdgeInset;

            if (offScreen)
            {
                Vector2 direction = local.sqrMagnitude > 0.0001f ? local.normalized : Vector2.up;
                local.x = Mathf.Clamp(local.x, -halfWidth, halfWidth);
                local.y = Mathf.Clamp(local.y, -halfHeight, halfHeight);

                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                _markerArrow.localRotation = Quaternion.Euler(0f, 0f, angle);

                if (!_markerArrow.gameObject.activeSelf)
                {
                    _markerArrow.gameObject.SetActive(true);
                }
            }
            else
            {
                local.x = Mathf.Clamp(local.x, -halfWidth, halfWidth);
                local.y = Mathf.Clamp(local.y, -halfHeight, halfHeight);

                if (_markerArrow.gameObject.activeSelf)
                {
                    _markerArrow.gameObject.SetActive(false);
                }
            }

            _marker.anchoredPosition = local;

            // G-21. The SAME phase the world beacon is pulsing on, taken straight off the
            // snapshot rather than recomputed here. Two clocks at the same rate still
            // drift in phase, and a player reads that drift as two separate things
            // happening. One clock, two surfaces.
            float blink = GroundBlinkClock.Intensity01(state.GroundBlinkPhase);
            float urgency = state.GroundProgress01 < GroundUrgencyStart
                ? 1f - state.GroundProgress01 / GroundUrgencyStart
                : 0f;

            Color markerColour = Color.Lerp(
                UiTokens.Get(UiColor.StateGold), UiTokens.Get(UiColor.StateDanger), Mathf.Clamp01(urgency));

            ApplyMarkerBlink(_markerCore, markerColour, blink, 1f);
            ApplyMarkerBlink(_markerRing, markerColour, blink, 0.85f);

            if (_markerHalo != null)
            {
                // The halo is the marker's backing, not part of the blink: it is what
                // keeps the pip legible against a bright frame at the dark end of the
                // cycle. It breathes a little so the whole marker still feels alive.
                SetAlpha(_markerHalo, Mathf.Lerp(0.45f, 0.75f, blink));
            }

            float markerScale = Mathf.Lerp(0.86f, 1.14f, blink);
            _marker.localScale = new Vector3(markerScale, markerScale, 1f);

            // The distance label stays. It is the answer to a DIFFERENT question - "how
            // far", not "how long" - and the request only removed the duration.
            float distance = Vector3.Distance(camera.transform.position, state.WorldPosition);
            int metres = Mathf.RoundToInt(distance);
            if (metres != _markerMetres)
            {
                _markerMetres = metres;
                _markerLabel.text = metres + " m";
            }

            _markerLabel.color = markerColour;
        }

        private static void ApplyMarkerBlink(Image image, Color colour, float blink, float weight)
        {
            if (image == null)
            {
                return;
            }

            Color c = colour;
            c.a = Mathf.Lerp(1f - weight, 1f, blink);
            image.color = c;
        }

        // ================================================================== banners

        private void OnRoundStarted(int round)
        {
            UiEvents.Raise(UiEvent.HudRoundStarted);
            ShowBanner("ROUND " + round);
        }

        private void OnRoundCompleted(int round)
        {
            UiEvents.Raise(UiEvent.HudRoundCompleted);
            ShowBanner("ROUND " + round + " COMPLETE");
        }

        private void OnPoolExhausted(int round)
        {
            UiEvents.Raise(UiEvent.HudPoolExhausted);
            ShowBanner("CLEAR THE LAST OF THEM");
        }

        private void OnSpecialSpawned(Vector3 position, float groundLifetime)
        {
            UiEvents.Raise(UiEvent.HudSpecialSpawned);
            ShowBanner("SPECIAL WEAPON ON THE FIELD");
        }

        private void OnSpecialPickedUp(float activeDuration)
        {
            UiEvents.Raise(UiEvent.HudSpecialPickedUp);
            string name = WeaponEvents.Special.DisplayName;
            ShowBanner(string.IsNullOrEmpty(name)
                ? "SPECIAL WEAPON ONLINE"
                : UiFactory.Clip(name.ToUpperInvariant(), 30) + " ONLINE");
        }

        private void OnSpecialFinalSeconds(float remaining)
        {
            UiEvents.Raise(UiEvent.HudSpecialFinalSeconds);
            SetWarning(true);
        }

        private void OnSpecialEnded()
        {
            UiEvents.Raise(UiEvent.HudSpecialEnded);
            SetWarning(false);

            // The death beat: the run director cancels the Special Weapon the moment the
            // run ends (WP-007, AP-10), which publishes Ended while the HUD is still up.
            // The HUD is frozen for the dead player (see UpdateRun) - there is no classic
            // weapon coming back for anyone to read about.
            if (RunEvents.Current.Phase == RunPhase.Dead)
            {
                return;
            }

            ShowBanner("CLASSIC WEAPON RESTORED");
        }

        private void OnSpecialGroundExpired()
        {
            UiEvents.Raise(UiEvent.HudSpecialExpired);
            SetWarning(false);
        }

        private void ShowBanner(string message)
        {
            _bannerText.text = message;
            _bannerRemaining = BannerSeconds;
        }

        private void UpdateBanner()
        {
            if (_bannerRemaining <= 0f)
            {
                if (_bannerGroup.alpha > 0f)
                {
                    _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, 0f, Time.unscaledDeltaTime * 3f);
                }

                return;
            }

            _bannerRemaining -= Time.unscaledDeltaTime;
            _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, 1f, Time.unscaledDeltaTime * 6f);
        }

        /// <summary>Formats to a tenth. Callers cache by the displayed tenth so this never runs once per frame (WP-007, PB-2).</summary>
        private static string FormatSeconds(float seconds)
        {
            return Mathf.Max(0f, seconds).ToString("0.0");
        }
    }
}
