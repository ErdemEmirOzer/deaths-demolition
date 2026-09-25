using DD.Prototype.CameraSystem;
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
    public sealed partial class RunHudScreen : UiScreen
    {
        private const float SpecialBlockWidth = 220f;
        private const float SpecialBlockHeight = 260f;   // AD R1-B MUST-2b: the usage row has its own line under the time strip

        /// <summary>
        /// WP-068 8-R: the top-centre stack, from the screen top - the seal line (24 + 56), the calm breakdown (to 106),
        /// the threat chips (110 + 30); the final-seconds line and the banner sit under it.
        /// </summary>
        private const float ThreatRowTop = 110f;
        private const float WarningTop = 150f;
        private const float BannerTop = 196f;

        private const float BannerSeconds = HudBanner.Seconds;
        private const float MarkerEdgeInset = 72f;
        private const float LowRingAlpha = 0.6f;

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
        private TMP_Text _ammoValue;
        private UiBar _reloadBar;
        private TMP_Text _reloadCaption;

        // WP-068 slice 8a · HUD v3 panels (direction §5 E10–E13).
        private UiCounter _goldCounter;
        private TMP_Text _goldDelta;
        private Vector2 _goldDeltaHome;
        private UiFx _goldLeaf;
        private float _goldGlintAt = -10f;
        private int _goldSeen = int.MinValue;
        private TMP_Text _healthLowWord;
        private UiCounter _ammoCounter;
        private Image _ladderBack;
        private Image _ladderFill;
        private float _ladderWidth;
        private int _ladderMagazine = -1;
        private int _ladderPellets = -1;
        private int _ladderShown = -1;
        private UiRing _reloadRing;
        private UiPrompt _reloadPrompt;
        private CanvasGroup _reloadPromptGroup;   // WP-068 8c · E23: α + 4 px Fast in, Exit Fast out
        private Vector2 _reloadPromptHome;
        private bool _reloadPromptShown;
        private RunHudReloadState _shownReload = RunHudReloadState.Ready;
        private bool _ammoLowShown;
        private int _roundShown = int.MinValue;

        /// <summary>
        /// E12 (AD 8a MUST-1): the gold panel's only leaf - the A6 kinpaku flake tile in StateGold at this alpha, never a
        /// flat fill (the panel rests on HudInk like its siblings); the S-07 Leaf glint lays the same alpha only while it
        /// sweeps. UiArtThemeTests.HudKontrasti measures the numeral over it.
        /// </summary>
        private const float GoldGlintMinInterval = 1f;
        private const float GoldGlintSeconds = 0.6f;
        private const float HealthBarHeight = 20f;
        private const float LadderHeight = 12f;
        private const float LadderPitchMax = 8f;
        private const float ReloadRingSize = 100f;   // AD 8a SHOULD-1: the ring encloses the glyph - Ø 100, 3 px, glyph 80 inside
        private const float WeaponGlyphInRing = 80f;
        private const float ReloadPromptSize = 22f;
        private GameObject _suspendedChip;

        // ---- special weapon
        private GameObject _specialBlock;
        private TMP_Text _specialPhase;
        private TMP_Text _specialTimer;

        // WP-007 (PB-2): the active timer is formatted only when its displayed tenth
        // changes (at most ten times a second), the marker distance only when the displayed
        // metre changes (UiWorldMarker) - never once per frame.
        private int _specialMagShown = int.MinValue;
        private int _specialReserveShown = int.MinValue;

        // WP-069 B4 / DEC-097 · E14 v3: the Special is a magazine weapon with a time cap. The ring is the magazine (one
        // segment a round; the reload refills it in BarReload), the centre reads the rounds over the reserve, a thin cream
        // strip under the ring is the time cap (coral in the final seconds), and an inner arc is Raiju's 0.25 s charge.
        private TMP_Text _specialReserve;
        private Image _specialCharge;
        private Image _specialCapFill;
        private GameObject _specialCap;
        public const float SpecialCapWidth = 132f;   // AD R1-B SHOULD-1: the ring's baseline (= the ring's width)
        public const float SpecialCapHeight = 4f;

        /// <summary>
        /// WP-068 12b (AD R1-B SHOULD-1, the remainder): the time strip's track is Stone (TextMuted, Stone350) at this alpha
        /// under the cream fill, so the remaining fraction reads against a denominator - the ammo ladder's spent-round
        /// language - instead of a second cream. Both are the flat Solid sprite (square ends, not a brush).
        /// </summary>
        public const float SpecialCapTrackAlpha = 0.25f;
        public const float SpecialChargeSize = 108f;
        public const float SpecialGlyphSize = 64f;       // N19 §1.5: the E14 ring glyph
        public const float SpecialGlyphY = 22f;          // its centre above the ring's centre (spans -10 .. +54 inside r 58)
        public const float SpecialRoundsY = -30f;        // the rounds / reserve row's centre below it
        private TMP_Text _specialCaption;
        private Image _specialRing;
        private Image _specialPip;

        // WP-068 slice 8c · E14 the Special ring: A4 Ring.Progress132, a tick a second while active, the pickup fill,
        // one kinpaku glint on the fill's own shape, the ready sparks.
        public const float SpecialRingGhost = 0.25f;   // 8-R: no panel behind the ring - the ghost carries the track
        public const float SpecialPickupFillSeconds = 0.32f;
        private const float SpecialTickLength = 12f;
        private const float SpecialTickWidth = 1f;          // AD 8c SHOULD-1: 1 px a second, every 5th 2 px (the health strong-tick rule)
        private const float SpecialTickStrongWidth = 2f;
        private const float SparkStartRadius = 66f;   // the Special ring's stroke (Ring.Progress132)
        private const float SparkSpeed = 100f;        // px/s at the fastest spark: 66 → 96 over the 0.3 s life
        private const int SpecialTickCapacity = 30;
        private RectTransform _specialTicks;
        private readonly Image[] _specialTickMarks = new Image[SpecialTickCapacity];
        private int _specialTickCount = -1;
        private const float SpecialHitPop = 1.06f;
        private const float SpecialHitPopSeconds = 0.12f;
        private UiFx _specialLeaf;
        private UiFxParticles _specialSparks;
        private float _specialPickupAt = -10f;

        /// <summary>E14: how many one-second ticks the ring carries (the active duration, rounded); test readout.</summary>
        public int SpecialTickCount => _specialTickCount;
        private Image _weaponIcon;

        // WP-022H · Special usage v2 (WP-056 SpecialWeaponUsageEvents): a field / shots line
        // under the ring and the AP-014 glyph next to the phase text. State is integers
        // and one expiry time; the line's text is rebuilt only when a displayed integer
        // changes (ADR-037-1 - no per-frame string work).
        private TMP_Text _specialUsage;

        /// <summary>WP-022J: the HUD dims with the S-07 death overlay (DamageVignetteLayer.DeathOverlayT), so the frozen numbers recede while the world desaturates; Results carries the same figures.</summary>
        private CanvasGroup _deathDim;
        private DD.Prototype.Feedback.Vfx.DamageVignetteLayer _deathLayer;
        private const float DeathDimFloor = 0.35f;

        /// <summary>
        /// WP-068 8c · E22: the learning moment. For the first 0.6 s of the dim, the health block stays at α 1 and says
        /// "DOWN · killed by …", then it follows the rest down.
        /// </summary>
        public const float DeathLineHoldSeconds = 0.6f;
        private RectTransform _healthPanel;
        private CanvasGroup _healthGroup;
        private string _killedBy;
        private float _deathAt = -1f;
        private bool _deathLineDimmed;

        /// <summary>The health block's own group during the death beat (test readout; null before the first death).</summary>
        public CanvasGroup HealthGroup => _healthGroup;

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
        private Image _healthIcon;
        private bool _lowHealthIconShown;
        private HudReticle _reticle;   // WP-068 8b · E15 the honest reticle
        private readonly System.Collections.Generic.List<RectTransform> _clusters =
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
        private HudBanner _banner;            // WP-068 8c · E13 banner v3
        private TMP_Text _bannerText;         // the banner's words (SpecialWeaponTestKit.HudBanner reads both by name)
        private float _bannerRemaining;

        // ---- world marker (WP-068 8c · E14 / E21)
        private UiWorldMarker _worldMarker;

        private bool _healthBound;
        private bool _lowHealthShown;

        // ---- threat warnings (WP-022B, ThreatHudFeed / WP-033)
        private GameObject _threatRow;
        private readonly ThreatSlot[] _threatSlots = new ThreatSlot[3];
        private int _threatWindupShown;
        private bool _threatUltra;
        private HudPlayerArcs _playerArcs;   // WP-068 8b · E13 rear arc, E17 player arc (top-down)
        private HudRearEdge _rearEdge;       // M4-R1 N2: the rear threat at the screen edge (third / first person)
        private Image _staticDot;            // M4-R1 N2 (WP-070 §3.2): the motion-sickness centre dot
        private bool _staticDotShown;

        /// <summary>The rear-edge arc (tests, captures).</summary>
        public HudRearEdge RearEdge
        {
            get { return _rearEdge; }
        }

        /// <summary>The reticle (tests, captures).</summary>
        public HudReticle Reticle
        {
            get { return _reticle; }
        }

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

            // WP-068 8-R (user decision 24.09, concept A "Mühür ve Fırça"): no panels - each readout is one ink object
            // at its corner (RunHudScreen.Ink.cs).
            BuildInkClusters();
            BuildCentreChannel();
            BuildMarker();
            BuildReticle();
            BuildInkThreatChips();

            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
            RunEvents.RoundPoolExhausted += OnPoolExhausted;
            RunEvents.GoldChanged += OnGoldChanged;

            WeaponEvents.SpecialWeaponSpawned += OnSpecialSpawned;
            WeaponEvents.DryFire += OnDryFire;
            WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds += OnSpecialFinalSeconds;
            WeaponEvents.SpecialWeaponEnded += OnSpecialEnded;
            SpecialWeaponUsageEvents.Usage += OnSpecialUsage;
            WeaponEvents.SpecialWeaponGroundExpired += OnSpecialGroundExpired;

            PlayerHealth.AnyHealthChanged += ApplyHealth;
            PlayerHealth.AnyDamaged += OnPlayerDamaged;
            PlayerHealth.AnyPlayerDied += OnPlayerDied;
        }

        protected override void Start()
        {
            base.Start();
            BindHealthSnapshot();
        }

        protected override void OnDestroy()
        {
            BottomCentreTop = 0f;
            base.OnDestroy();
            UiCursor.SetContextVisible(true);
            RunEvents.RunStarted -= OnRunStarted;
            RunEvents.RoundStarted -= OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted;
            RunEvents.RoundPoolExhausted -= OnPoolExhausted;
            RunEvents.GoldChanged -= OnGoldChanged;

            WeaponEvents.SpecialWeaponSpawned -= OnSpecialSpawned;
            WeaponEvents.DryFire -= OnDryFire;
            WeaponEvents.SpecialWeaponPickedUp -= OnSpecialPickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds -= OnSpecialFinalSeconds;
            WeaponEvents.SpecialWeaponEnded -= OnSpecialEnded;
            SpecialWeaponUsageEvents.Usage -= OnSpecialUsage;
            WeaponEvents.SpecialWeaponGroundExpired -= OnSpecialGroundExpired;

            PlayerHealth.AnyHealthChanged -= ApplyHealth;
            PlayerHealth.AnyDamaged -= OnPlayerDamaged;
            PlayerHealth.AnyPlayerDied -= OnPlayerDied;
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
        private const float SpecialRingSize = 132f;

        /// <summary>E12: a gain - "+N" rises 16 px and fades; the leaf glints (≤ 1 / s, never under Reduce Flashes).</summary>
        private void ShowGoldGain(int delta)
        {
            RectTransform deltaRect = _goldDelta.rectTransform;
            UiMotion.KillTarget(deltaRect, false);
            UiMotion.KillTarget(_goldDelta, false);
            _goldDelta.SetText("+{0}", delta);
            deltaRect.anchoredPosition = _goldDeltaHome;
            _goldDelta.canvasRenderer.SetAlpha(1f);
            UiMotion.Move(deltaRect, _goldDeltaHome + new Vector2(0f, 16f), UiDuration.Emphasis, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_goldDelta, 0f, UiDuration.Base, UiEase.Exit, UiMotionClass.Data, UiMotionTokens.Seconds(UiDuration.Screen));

            float now = Time.unscaledTime;
            if (!UiSettings.ReduceFlashes && now - _goldGlintAt >= GoldGlintMinInterval)
            {
                // The glint layer takes the leaf alpha for the sweep only, then goes back to nothing.
                _goldGlintAt = now;
                if (_goldLeaf != null)
                {
                    _goldLeaf.Play();
                }
            }
        }

        /// <summary>
        /// The Special block's readout inside <paramref name="specialHost"/> - phase line, ring (track + fill), ground pip,
        /// timer, name, usage line, glyph. <paramref name="ink"/> (8-R concepts): no panel behind it, so the track is the A4
        /// ring's cream ghost and every word carries the HUD ink.
        /// </summary>
        private void BuildSpecialContent(RectTransform specialHost, bool ink)
        {
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

            // E14: the A4 Ring.Progress132 (8 px stroke on r 58–66) - a cream ghost track and the gold fill (Data, Linear).
            Sprite ringArt = UiArt.Kit("Ring.Progress132");
            Image track = UiFactory.NewImage("Track", ringRoot, UiColor.TextPrimary, ringArt != null ? ringArt : UiSprites.Ring);
            UiFactory.Fill((RectTransform)track.transform);
            Color ghost = track.color;
            ghost.a = SpecialRingGhost;
            track.color = ghost;

            _specialRing = UiFactory.NewImage("Fill", ringRoot, UiColor.BarSpecial, ringArt != null ? ringArt : UiSprites.Ring);
            UiFactory.Fill((RectTransform)_specialRing.transform);
            _specialRing.type = Image.Type.Filled;
            _specialRing.fillMethod = Image.FillMethod.Radial360;
            _specialRing.fillOrigin = (int)Image.Origin360.Top;
            _specialRing.fillClockwise = false;
            _specialRing.fillAmount = 1f;
            // The pickup's one kinpaku glint runs over the fill's own shape (S-07 Leaf; Flash class, never under Reduce Flashes).
            _specialLeaf = _specialRing.gameObject.AddComponent<UiFx>();
            _specialLeaf.Configure(UiFxMode.Leaf, GoldGlintSeconds, new Vector3(1f, 0f, 0f));

            // A tick a second while active: ink notches across the stroke, so "one more fight?" reads as the number of
            // segments left. Laid on pickup from the active duration (≤ 30).
            _specialTicks = UiFactory.NewRect("Ticks", ringRoot);
            _specialTicks.sizeDelta = Vector2.zero;
            for (int i = 0; i < SpecialTickCapacity; i++)
            {
                Image tick = UiFactory.NewImage("Tick." + i, _specialTicks, UiColor.HudInk, UiSprites.Solid);
                ((RectTransform)tick.transform).sizeDelta = new Vector2(SpecialTickWidth, SpecialTickLength);
                tick.enabled = false;
                _specialTickMarks[i] = tick;
            }

            LaySpecialTicks(11f);
            _specialTicks.gameObject.SetActive(false);

            // The ready sparks: 12 gold quads leave the ring's stroke outward, r 66 → 96 in 0.3 s (AD 8c SHOULD-2: the timer
            // numeral stays clean), speed 100 px/s at the fastest (the shader spreads 0.55–1.0), a light fall; Flash × FlashScale,
            // none under reduced motion.
            RectTransform sparkRect = UiFactory.NewRect("Sparks", ringRoot);
            UiFactory.Fill(sparkRect);
            _specialSparks = sparkRect.gameObject.AddComponent<UiFxParticles>();
            _specialSparks.color = UiTokens.Get(UiColor.BarSpecial);
            _specialSparks.Configure(12, 5f, SparkSpeed, 120f, 0.3f, 0f, null, SparkStartRadius);

            // The pip lives in the ring's centre while the weapon is on the ground: the
            // eye that learned to look there for the timer still finds the answer - an
            // answer with no number in it.
            _specialPip = UiArt.HasIcon("Hud.Special")
                ? UiArt.IconImage("GroundPip", ringRoot, "Hud.Special", UiColor.StateGold, 40f)
                : UiFactory.NewImage("GroundPip", ringRoot, UiColor.StateGold, UiSprites.Disc);
            RectTransform pipRect = (RectTransform)_specialPip.transform;
            pipRect.sizeDelta = new Vector2(40f, 40f);
            pipRect.anchoredPosition = Vector2.zero;
            // Runtime blink tint is applied through CanvasRenderer. Keep the Graphic's
            // vertex colour neutral so the renderer tint is the exact palette colour,
            // rather than palette × palette.
            _specialPip.color = Color.white;
            _specialPip.gameObject.SetActive(false);

            // E14 v3: the rounds in the magazine (the old timer's numeral and name, so its tests and captures find it) over
            // the reserve.
            // AD R1-B MUST-1: the ring's interior carries the Special's glyph at 64 px in its upper half (N19 §1.5) and the
            // rounds over the reserve in its lower half, side by side on one baseline: "3" NumericL then "/ 9".
            _specialTimer = UiFactory.Numeric(
                "Timer", ringRoot, "0", UiType.NumericL, UiColor.StateGold, TextAnchor.MiddleRight, false);
            RectTransform timerRect = (RectTransform)_specialTimer.transform;
            timerRect.anchorMin = new Vector2(0.5f, 0.5f);
            timerRect.anchorMax = new Vector2(0.5f, 0.5f);
            timerRect.pivot = new Vector2(1f, 0.5f);
            timerRect.sizeDelta = new Vector2(48f, UiFactory.LineHeight(UiType.NumericL));
            timerRect.anchoredPosition = new Vector2(2f, SpecialRoundsY);
            _specialReserve = UiFactory.Label("Reserve", ringRoot, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            RectTransform reserveRect = (RectTransform)_specialReserve.transform;
            reserveRect.anchorMin = new Vector2(0.5f, 0.5f);
            reserveRect.anchorMax = new Vector2(0.5f, 0.5f);
            reserveRect.pivot = new Vector2(0f, 0.5f);
            reserveRect.sizeDelta = new Vector2(44f, UiFactory.LineHeight(UiType.NumericL));
            reserveRect.anchoredPosition = new Vector2(6f, SpecialRoundsY - 3f);
            _specialReserve.enableWordWrapping = false;

            // Raiju's charge: an inner arc (the same A4 ring at 108 px, inside the magazine stroke) filling over the 0.25 s
            // the trigger is held; hidden otherwise.
            _specialCharge = UiFactory.NewImage("Charge", ringRoot, UiColor.TextPrimary, ringArt != null ? ringArt : UiSprites.Ring);
            RectTransform chargeRect = (RectTransform)_specialCharge.transform;
            chargeRect.anchorMin = new Vector2(0.5f, 0.5f);
            chargeRect.anchorMax = new Vector2(0.5f, 0.5f);
            chargeRect.sizeDelta = new Vector2(SpecialChargeSize, SpecialChargeSize);
            _specialCharge.type = Image.Type.Filled;
            _specialCharge.fillMethod = Image.FillMethod.Radial360;
            _specialCharge.fillOrigin = (int)Image.Origin360.Top;
            _specialCharge.fillClockwise = true;
            _specialCharge.fillAmount = 0f;
            _specialCharge.enabled = false;

            // The time cap (40 s / 45 s): secondary, so a thin strip under the ring - a Stone track (12b, AD R1-B SHOULD-1) and a
            // cream fill that drains, flat-ended; the final seconds turn the fill coral with the ENDING word (colour never alone).
            RectTransform cap = UiFactory.NewRect("TimeCap", ringRoot);
            cap.anchorMin = new Vector2(0.5f, 0f);
            cap.anchorMax = new Vector2(0.5f, 0f);
            cap.pivot = new Vector2(0.5f, 1f);
            cap.sizeDelta = new Vector2(SpecialCapWidth, SpecialCapHeight);
            cap.anchoredPosition = new Vector2(0f, -(int)UiSpace.S);
            Image capTrack = UiFactory.NewImage("Track", cap, UiColor.TextMuted, UiSprites.Solid);
            UiFactory.Fill((RectTransform)capTrack.transform);
            Color capGhost = capTrack.color;
            capGhost.a = SpecialCapTrackAlpha;
            capTrack.color = capGhost;
            _specialCapFill = UiFactory.NewImage("Fill", cap, UiColor.TextPrimary, UiSprites.Solid);
            UiFactory.Fill((RectTransform)_specialCapFill.transform);
            _specialCapFill.type = Image.Type.Filled;
            _specialCapFill.fillMethod = Image.FillMethod.Horizontal;
            _specialCapFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _specialCapFill.fillAmount = 1f;
            _specialCap = cap.gameObject;

            // AD R1-B MUST-2b: under the ring, top-down - the time strip (ring bottom + 8), the usage row ("SHOTS 2 · HITS 5",
            // gold, strip + 8) and the name (usage + 4). Placed from the top, so a larger TextScale grows the block downward
            // into its own 260 px and never back over the strip.
            float captionBand = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
            float usageTop = UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS + SpecialRingSize + (int)UiSpace.S + SpecialCapHeight + (int)UiSpace.S;
            _specialUsage = UiFactory.Label(
                "Usage", specialHost, string.Empty, UiType.Caption, UiColor.StateGold, TextAnchor.MiddleCenter);
            TopBand((RectTransform)_specialUsage.transform, usageTop, captionBand);
            _specialUsage.fontStyle = TMPro.FontStyles.Bold;
            _specialUsage.enableWordWrapping = false;

            _specialCaption = UiFactory.Label(
                "Caption", specialHost, string.Empty, UiType.Caption, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            TopBand((RectTransform)_specialCaption.transform, usageTop + captionBand + (int)UiSpace.XS, captionBand);
            _specialCaption.enableWordWrapping = false;

            // N19 §1.5 / AD R1-B MUST-1: the Special's glyph, 64 px, in the ring's upper half, in its identity tint.
            _specialGlyph = UiFactory.NewImage("Glyph", ringRoot, UiColor.StateGold, UiSprites.Disc);
            _specialGlyph.preserveAspect = true;
            RectTransform glyphRect = (RectTransform)_specialGlyph.transform;
            glyphRect.anchorMin = new Vector2(0.5f, 0.5f);
            glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
            glyphRect.pivot = new Vector2(0.5f, 0.5f);
            glyphRect.sizeDelta = new Vector2(SpecialGlyphSize, SpecialGlyphSize);
            glyphRect.anchoredPosition = new Vector2(0f, SpecialGlyphY);
            _specialGlyph.enabled = false;
            if (ink)
            {
                UiHudInk.Apply(_specialPhase);
                UiHudInk.Apply(_specialTimer);
                UiHudInk.Apply(_specialReserve);
                UiHudInk.Apply(_specialCaption);
                UiHudInk.Apply(_specialUsage);
            }
        }

        /// <summary>
        /// Directional damage edges. Built here and not installed by the player module,
        /// because the HUD owns the canvas and the draw order, and the draw order is the
        /// part that has to be right.
        /// </summary>
        private Image LadderImage(string name, RectTransform ladder, UiColor colour)
        {
            Image image = UiFactory.NewImage(name, ladder, colour, UiSprites.AmmoTick);
            image.type = Image.Type.Tiled;
            var rect = (RectTransform)image.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            return image;
        }

        /// <summary>E11: lay the ladder for a magazine (a weapon change) - pitch ≤ 8 px so up to LadderMaxRounds fit the column.</summary>
        private void LayLadder(int magazine, int pellets)
        {
            _ladderMagazine = magazine;
            _ladderPellets = pellets;
            _ladderShown = -1;
            bool show = magazine > 0 && magazine <= RunHudViewModel.LadderMaxRounds;
            _ladderBack.enabled = show;
            _ladderFill.enabled = show;
            if (!show)
            {
                return;
            }

            Sprite unit = pellets > 1 ? UiSprites.AmmoPip : UiSprites.AmmoTick;
            float pitch = Mathf.Min(LadderPitchMax * (pellets > 1 ? 1.5f : 1f), Mathf.Floor(_ladderWidth / magazine));
            float multiplier = unit.rect.width / Mathf.Max(1f, pitch);   // one tile per round at this pitch
            _ladderBack.sprite = unit;
            _ladderBack.pixelsPerUnitMultiplier = multiplier;
            _ladderFill.sprite = unit;
            _ladderFill.pixelsPerUnitMultiplier = multiplier;

            Color back = UiTokens.Get(UiColor.TextMuted);
            back.a = 0.35f; // AD 8a SHOULD-2: the spent rounds recede, the remaining count reads at a glance
            _ladderBack.color = back;
            ((RectTransform)_ladderBack.transform).sizeDelta = new Vector2(pitch * magazine, 0f);
        }

        /// <summary>E23: the reload prompt rises 4 px into place as it fades in (Fast, Settle) and fades out (Fast, Exit).</summary>
        private void ShowReloadPrompt(bool show)
        {
            if (show == _reloadPromptShown)
            {
                return;
            }

            _reloadPromptShown = show;
            var rect = (RectTransform)_reloadPrompt.transform;
            UiMotion.KillTarget(rect, false);
            UiMotion.KillTarget(_reloadPromptGroup, false);
            if (show)
            {
                _reloadPrompt.gameObject.SetActive(true);
                _reloadPromptGroup.alpha = 0f;
                rect.anchoredPosition = _reloadPromptHome + new Vector2(0f, -PromptRise);
                UiMotion.Alpha(_reloadPromptGroup, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.Move(rect, _reloadPromptHome, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                return;
            }

            rect.anchoredPosition = _reloadPromptHome;
            UiMotion.Alpha(_reloadPromptGroup, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
        }

        /// <summary>E23: how far a prompt rises as it enters.</summary>
        public const float PromptRise = 4f;

        /// <summary>
        /// E11: the trigger on an empty magazine - the ladder kicks 4 px outward and settles (Decor: none under reduced motion).
        /// WP-068 12b: it settles on its laid-out home (the 8-R ladder is not at x 0 - it used to land there, past the cluster's
        /// edge), and the kick mirrors with the first-person layout.
        /// </summary>
        private void OnDryFire()
        {
            RectTransform ladder = _ladderBack != null ? (RectTransform)_ladderBack.transform.parent : null;
            if (ladder == null || UiMotion.ReducedMotion)
            {
                return;
            }

            UiMotion.KillTarget(ladder, false);
            ladder.anchoredPosition = _ladderHome + new Vector2(WeaponClusterMirrored ? -4f : 4f, 0f);
            UiMotion.Move(ladder, _ladderHome, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
        }

        private void FillLadder(int rounds)
        {
            if (!_ladderFill.enabled || rounds == _ladderShown)
            {
                return;
            }

            _ladderShown = rounds;
            float pitch = ((RectTransform)_ladderBack.transform).sizeDelta.x / Mathf.Max(1, _ladderMagazine);
            ((RectTransform)_ladderFill.transform).sizeDelta = new Vector2(pitch * Mathf.Clamp(rounds, 0, _ladderMagazine), 0f);
        }

        /// <summary>Capture seam (-ddUiCapture hudstates): holds the rear arc on towards this world direction (null = live data).</summary>
        public static Vector3? CaptureRearDirection;

        /// <summary>Capture seam: the reticle draws this weapon state instead of the live one (staged spread / empty / pellets).</summary>
        public static ClassicWeaponHudState? CaptureWeapon;

        /// <summary>Capture seam: the reticle draws a live Special (Gold400) regardless of the Special's phase.</summary>
        public static bool CaptureSpecialActive;

        /// <summary>Capture seam (8-R): the SURROUNDED and BEHIND YOU chips shown as if the director raised them.</summary>
        public static bool CaptureThreats;

        /// <summary>Capture seam (8-R, AD SHOULD-5): hold the back planes at this parallax drift (canvas px) - the depth pair.</summary>
        public static Vector2? CaptureParallax;

        /// <summary>Capture seam: a hit from <paramref name="towardsAttackerWorld"/> as the damage arc shows it.</summary>
        public void CaptureHit(Vector3 towardsAttackerWorld)
        {
            if (_playerArcs != null)
            {
                _playerArcs.Flash(towardsAttackerWorld);
            }
        }

        /// <summary>
        /// Capture seam (12c, AD 12b SHOULD-2): raises a banner now and returns its box on the canvas (px, centre origin) -
        /// the band a status stamp sorts under.
        /// </summary>
        public Rect CaptureBanner(string message)
        {
            ShowBanner(message);
            return CanvasRectOf(_banner.Root);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaptureSeams()
        {
            CaptureRearDirection = null;
            CaptureWeapon = null;
            CaptureSpecialActive = false;
            CaptureThreats = false;
            CaptureParallax = null;
        }

        /// <summary>E17: the player-bound arc faces the attacker (HitDirection runs from the attacker towards the player).</summary>
        private void OnPlayerDamaged(PlayerDamageInfo info)
        {
            _lastHitAt = Time.unscaledTime;   // 8-R posture: a hit keeps the HUD forward for 3 s
            if (_playerArcs != null)
            {
                _playerArcs.Flash(-info.HitDirection);
            }
        }

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

            // E15: ticks at the real spread (HudReticle); the root moves with the aim, parked off-screen when hidden.
            _reticle = new HudReticle(layer);
            _reticle.Root.anchoredPosition = OffScreen;
            _playerArcs = new HudPlayerArcs(layer);
            _playerArcs.Place(OffScreen);

            // M4-R1 N2: third / first person - the rear threat at the edge, the optional static centre dot.
            _rearEdge = new HudRearEdge(layer);
            _staticDot = UiFactory.NewImage("CentreDot", layer, UiColor.TextPrimary, UiArt.Icon("Pip.Full") ?? UiSprites.Disc);
            if (UiArt.IsGlyph(_staticDot.sprite))
            {
                UiArt.ApplyGlyph(_staticDot, new Vector3(0.3f, 0f, 0f));
            }

            _staticDot.rectTransform.sizeDelta = new Vector2(HudReticle.DotSize, HudReticle.DotSize);
            _staticDot.rectTransform.anchoredPosition = OffScreen;
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

            // Final-seconds line, under the threat row: the words in coral over a brush, no chip (8-R).
            _warningBanner = BuildInkWarning(ScreenRect, WarningTop);
            _warningBanner.SetActive(false);

            // Round / phase banner, just below the warning slot so the two never collide (E13 v3: wipe, brush, exit).
            _banner = new HudBanner(ScreenRect, BannerTop);
            _bannerText = _banner.Text;
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
            // WP-068 12b: they keep clear of the ink clusters (AD 8c, new for 12b) and of the player figure (AD R1-B SHOULD-4).
            RectTransform statusHost = UiFactory.NewRect("EnemyStatus", layer);
            UiFactory.Fill(statusHost);
            _enemyStatus = statusHost.gameObject.AddComponent<UiEnemyStatusMarkers>();
            _enemyStatus.Configure(ScreenRect, StatusStampAvoidRects, PlayerFigureRect);

            // E14 / E21: the AP-010 diamond in view (56 → 40 px with distance), the EdgeMarker at the edge.
            _worldMarker = new UiWorldMarker(layer);

            // WP-068 12b (AD R1-B SHOULD-4): the stamps sort under the banner. SHOCK was drawn over "SPECIAL WEAPON ON THE
            // FIELD" because the marker canvas came after the banner; the banner now follows it (the reticle layer and the
            // threat chips are built after, so they stay on top).
            _banner.Root.SetAsLastSibling();
        }

        /// <summary>The Special's world marker (test readout).</summary>
        public UiWorldMarker WorldMarker => _worldMarker;

        /// <summary>
        /// The play area no HUD panel may cover (REQ-104/105, DEC-077): the centre 62%
        /// of the width and the band from 25% to 80% of the height - the capsule, the
        /// enemies closing on it and the aim plane at the gameplay camera distance. The
        /// top strip holds the round chip, the bottom strip the three lower panels.
        /// Fractions of the canvas rect; UiArtSliceTests measures every panel against it.
        /// </summary>
        public static readonly Rect HudSafeZone = new Rect(0.19f, 0.25f, 0.62f, 0.55f);

        // ============================================================ threat chips
        //
        // WP-068 slice 8b · E13 threat chips v3, boxless since 8-R: the outer-only glyph and the word in their own HUD ink
        // over a brush underline in the state colour (RunHudScreen.Ink.cs). Three fixed slots sharing one width (SURROUNDED ·
        // BEHIND YOU · ELITE / ULTRA NEARBY), shown ≥ 800 ms and gone only after 300 ms without the threat (no flicker),
        // entering y −6 → 0 with α (Fast, Settle), leaving by α (Fast, Exit).

        private const float ThreatMinShownSeconds = 0.8f;
        private const float ThreatQuietSeconds = 0.3f;
        private const float ThreatEntryDrop = 6f;

        /// <summary>Every text a threat chip can carry (sizes the shared slot width).</summary>
        private static readonly string[] ThreatLabels = { "SURROUNDED", "BEHIND YOU", "ELITE NEARBY", "ULTRA NEARBY" };

        private sealed class ThreatSlot
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TMP_Text Label;
            public Image Glyph;
            public Vector2 Home;
            public bool Shown;
            public float ShownAt;
            public float WantAt;
            public float HideAt = -1f;
        }

        private void UpdateThreat()
        {
            if (_threatRow == null)
            {
                return;
            }

            bool fighting = RunEvents.Current.Phase == RunPhase.Fighting && UiNavigation.GameplayInputAllowed;
            ThreatSnapshot threat = ThreatDirector.Current;
            float now = Time.unscaledTime;

            DriveThreat(_threatSlots[0], fighting && (threat.Encircled || CaptureThreats), now, UiEvent.HudThreatEncircled);
            DriveThreat(_threatSlots[1], fighting && (threat.RearAttack || CaptureThreats), now, UiEvent.HudThreatRearAttack);

            bool strong = fighting && threat.StrongVersionPresent;
            bool ultra = strong && threat.StrongestTier == EnemyTier.Ultra;
            if (strong && ultra != _threatUltra)
            {
                // The strong slot names the worst tier near: its word and its outer-only tier glyph change together.
                _threatUltra = ultra;
                _threatSlots[2].Label.text = ultra ? "ULTRA NEARBY" : "ELITE NEARBY";
                Sprite tier = UiArt.Icon(ultra ? "Threat.UltraOuter" : "Threat.EliteOuter");
                if (tier != null && _threatSlots[2].Glyph != null)
                {
                    UiArt.SetGlyph(_threatSlots[2].Glyph, tier);
                }
            }

            DriveThreat(_threatSlots[2], strong, now, UiEvent.HudThreatStrongVersion);

            // E13 rear arc: for the BEHIND YOU chip's lifetime, turned to the attackers (S-03 data).
            if (_playerArcs != null)
            {
                bool staged = CaptureRearDirection.HasValue;
                bool rear = _threatSlots[1].Shown || staged;
                Vector3 towards = staged ? CaptureRearDirection.Value : threat.RearDirection;
                bool look = CameraModeService.IsLookMode;
                _playerArcs.SetRear(rear && !look, towards);
                int avoid = rear && look ? RearEdgeAvoidRects(_rearAvoid) : 0;
                _rearEdge.Tick(rear && look, towards, Camera.main, CameraModeService.Current == CameraMode.FirstPerson, ScreenRect.rect.size, _rearAvoid, avoid);
            }

            int windup = fighting ? threat.WindingUp : 0;
            if (windup > _threatWindupShown)
            {
                UiEvents.Raise(UiEvent.HudThreatWindup);
            }

            _threatWindupShown = windup;
        }

        /// <summary>One slot's hysteresis: in at once, out only after ≥ 800 ms shown and 300 ms without the threat.</summary>
        private static void DriveThreat(ThreatSlot slot, bool want, float now, UiEvent entered)
        {
            if (want)
            {
                slot.WantAt = now;
            }

            if (want && !slot.Shown)
            {
                slot.Shown = true;
                slot.ShownAt = now;
                slot.HideAt = -1f;
                slot.Rect.gameObject.SetActive(true);
                UiMotion.KillTarget(slot.Rect, false);
                UiMotion.KillTarget(slot.Group, false);
                slot.Rect.anchoredPosition = slot.Home + new Vector2(0f, -ThreatEntryDrop);
                slot.Group.alpha = 0f;
                UiMotion.Move(slot.Rect, slot.Home, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.Alpha(slot.Group, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                UiEvents.Raise(entered);
                return;
            }

            if (!want && slot.Shown && now - slot.ShownAt >= ThreatMinShownSeconds && now - slot.WantAt >= ThreatQuietSeconds)
            {
                slot.Shown = false;
                slot.HideAt = now + UiMotionTokens.Seconds(UiDuration.Fast);
                UiMotion.Alpha(slot.Group, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
                return;
            }

            if (!slot.Shown && slot.HideAt > 0f && now >= slot.HideAt)
            {
                slot.HideAt = -1f;
                slot.Rect.gameObject.SetActive(false);
            }
        }

        /// <summary>The threat chips, for tests (inactive unless the director says so).</summary>
        public bool ThreatChipVisible(int index)
        {
            ThreatSlot slot = index >= 0 && index < _threatSlots.Length ? _threatSlots[index] : null;
            return slot != null && slot.Rect != null && slot.Rect.gameObject.activeSelf;
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
            UpdateSlick();
            UpdateBanner();
            UpdateThreat();
            UpdateInkPosture();   // 8-R: calm / danger
            ApplyHudFrame();      // 12c · B12: the 16:9 frame follows the setting and the window
        }

        private void LateUpdate()
        {
            UpdateMarker();
            UpdateReticle();
            UpdateInkParallax();   // 8-R: the back planes drift against the camera
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

            // M4-R1 N2 (WP-070 §4.5): third / first person aim down the screen centre (the centre ray); the reticle
            // hides while the camera blends between modes (fire is swallowed then, D-14).
            bool look = CameraModeService.IsLookMode;
            _reticle.SetMode(CameraModeService.Current);
            ApplyModeLayout();
            bool reticleShown = show && !CameraModeService.IsTransitioning;

            Vector2 local = OffScreen;
            if (reticleShown && look)
            {
                local = Vector2.zero;
            }
            else if (reticleShown)
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
            UiCursor.SetContextVisible(!show);   // WP-068: UiCursor owns the pointer (a pad hides it anywhere)

            // Hidden = parked off-screen, never SetActive: toggling a graphic's active state
            // re-registers it with the canvas and rebuilds the whole HUD mesh each time, which
            // is exactly the per-frame allocation the HUD budget (0 B idle) forbids.
            if (local != _reticleLocal)
            {
                _reticleLocal = local;
                _reticle.Root.anchoredPosition = local;
            }

            // E13 / E17: the player-bound arcs follow the figure's screen point while the run is live (top-down only:
            // in third / first person the screen-edge hit layer and HudRearEdge carry direction).
            Vector2 playerLocal = OffScreen;
            if (show && !look)
            {
                Vector3 playerViewport = camera.WorldToViewportPoint(rig.transform.position + Vector3.up * 0.9f);
                if (playerViewport.z >= 0f)
                {
                    Vector2 size = ScreenRect.rect.size;
                    playerLocal = new Vector2((playerViewport.x - 0.5f) * size.x, (playerViewport.y - 0.5f) * size.y);
                }
            }

            _playerArcs.Place(playerLocal);

            bool dot = show && look && UiSettings.CentreDot;
            if (dot != _staticDotShown)
            {
                _staticDotShown = dot;
                _staticDot.rectTransform.anchoredPosition = dot ? Vector2.zero : OffScreen;
            }

            if (reticleShown)
            {
                // E15: the spread the weapon really has, projected at the aim distance (from the chest, where the shot leaves).
                _reticle.Tick(camera, rig.Aim.AimWorldPoint, rig.transform.position + Vector3.up * 1.2f,
                    ScreenRect.rect.size, CaptureWeapon ?? WeaponEvents.Classic,
                    CaptureSpecialActive || WeaponEvents.Special.Phase == SpecialWeaponPhase.Active);
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

            UiArt.SetGlyph(_weaponIcon, sprite);   // WP-068 G1: keeps the SDF draw path on a swap
            _weaponIcon.enabled = sprite != null;
            TintWeaponIcon();
        }

        /// <summary>
        /// M4-R1 N19 §2: the glyph wears its lineage's identity tint (Iwa terracotta / Nami celadon, UiGlyphTints). At LOW
        /// or EMPTY it steps back to alpha 0.70 so the amber word reads first. The tint never becomes a state colour.
        /// </summary>
        private void TintWeaponIcon()
        {
            if (_weaponIcon == null)
            {
                return;
            }

            Color tint = UiTokens.Get(UiGlyphTints.Weapon(WeaponEvents.Classic.Id));
            bool low = _model.AmmoLow || _model.ReloadState == RunHudReloadState.MagazineEmpty;
            tint.a = low ? UiGlyphTints.LowAlpha : 1f;
            _weaponIcon.color = tint;
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
                UpdateDeathDim();
                return;
            }

            ApplyRunFields(_model.ApplyRun(state));
        }

        /// <summary>
        /// WP-022J · while the player is dead the HUD canvas fades to DeathDimFloor in step
        /// with the S-07 death overlay (same unscaled clock, same 2 s), reduced motion →
        /// the floor at once. One CanvasGroup alpha per frame, no layout.
        /// </summary>
        private void UpdateDeathDim()
        {
            if (_deathDim == null)
            {
                _deathDim = UiFactory.Group(Canvas.gameObject);
            }

            if (_deathLayer == null)
            {
                _deathLayer = FindObjectOfType<DD.Prototype.Feedback.Vfx.DamageVignetteLayer>();
            }

            float t = UiSettings.ReducedMotion ? 1f : (_deathLayer != null ? _deathLayer.DeathOverlayT : 1f);
            float alpha = Mathf.Lerp(1f, DeathDimFloor, UiMotionCurve.EaseOut(t));
            if (!Mathf.Approximately(_deathDim.alpha, alpha))
            {
                _deathDim.alpha = alpha;
            }

            if (_deathAt < 0f)
            {
                BeginDeathLine();
            }
            else if (!_deathLineDimmed && Time.unscaledTime - _deathAt >= DeathLineHoldSeconds)
            {
                // Held long enough to read; now it follows the rest (Decor: a cut to the floor under reduced motion).
                _deathLineDimmed = true;
                UiMotion.Alpha(_healthGroup, DeathDimFloor, UiDuration.Emphasis, UiEase.Exit, UiMotionClass.Decor);
            }
        }

        /// <summary>E22 t0: the health block leaves the dim for 0.6 s and names what ended the run.</summary>
        private void BeginDeathLine()
        {
            _deathAt = Time.unscaledTime;
            _deathLineDimmed = false;
            if (_healthPanel == null)
            {
                return;
            }

            if (_healthGroup == null)
            {
                _healthGroup = UiFactory.Group(_healthPanel.gameObject);
            }

            UiMotion.KillTarget(_healthGroup, false);
            _healthGroup.ignoreParentGroups = true;
            _healthGroup.alpha = 1f;
            _healthLowWord.gameObject.SetActive(false);   // AD 8c SHOULD-4: one word per state - DOWN, not LOW
            _healthState.text = RunHudViewModel.DeathLine(_killedBy);
            _healthState.color = UiTokens.Get(UiColor.StateDanger);
            _healthState.fontStyle = FontStyles.Bold;
        }

        private void OnPlayerDied(PlayerDeathInfo info)
        {
            _killedBy = info.KilledBy;
        }

        /// <summary>A new run after a death on the same HUD: the health block rejoins the canvas.</summary>
        private void ResetDeathLine()
        {
            _deathAt = -1f;
            _deathLineDimmed = false;
            _killedBy = null;
            if (_healthGroup != null)
            {
                UiMotion.KillTarget(_healthGroup, false);
                _healthGroup.ignoreParentGroups = false;
                _healthGroup.alpha = 1f;
                _healthState.fontStyle = FontStyles.Normal;
            }
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
            _healthMaxShown = int.MinValue;
            ResetDeathLine();
            _goldSeen = int.MinValue;
            _roundShown = int.MinValue;
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
                // E13: the new round's digit stamps (Stamp 1.12 → 1, not on the first round of a run).
                int round = RunEvents.Current.Round;
                if (_roundShown != int.MinValue && round > _roundShown)
                {
                    UiPresets.StampIn(_roundValue.rectTransform);
                }

                _roundShown = round;
            }

            if ((changed & RunHudField.Gold) != 0)
            {
                // E12: roll to the balance; a gain rises "+N" and glints the leaf. The first value of a run lands at once.
                int gold = _model.Gold;
                if (_goldSeen == int.MinValue)
                {
                    _goldCounter.SetNow(gold);
                }
                else
                {
                    if (gold > _goldSeen)
                    {
                        ShowGoldGain(gold - _goldSeen);
                    }

                    _goldCounter.RollTo(gold);
                }

                _goldSeen = gold;
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
                    // E10: LOW word + coral frame + the cracked heart stamps once; no pulse.
                    _healthLowWord.gameObject.SetActive(_lowHealthShown);
                }

                _healthBar.SetValue(_model.HealthNormalized);
                _healthBar.SetColor(_model.HealthLow ? UiColor.BarHealthLow : UiColor.BarHealth);
                _healthValue.color = UiTokens.Get(_model.HealthLow ? UiColor.StateDanger : UiColor.TextPrimary);
                if (_healthIcon != null)
                {
                    _healthIcon.color = UiTokens.Get(_model.HealthLow ? UiColor.BarHealthLow : UiColor.BarHealth);
                    // WP-022L (AP-012 v003): cracked heart while low; the whole heart otherwise.
                    // Sprite swap only when the low state flips (HealthBar refreshes every tick).
                    if (_model.HealthLow != _lowHealthIconShown)
                    {
                        _lowHealthIconShown = _model.HealthLow;
                        Sprite heartSprite = UiArt.Icon(_model.HealthLow ? "Hud.LowHealth" : "Hud.Health");
                        if (heartSprite != null)
                        {
                            UiArt.SetGlyph(_healthIcon, heartSprite);
                        }

                        if (_model.HealthLow)
                        {
                            UiPresets.StampIn(_healthIcon.rectTransform);
                        }
                    }
                }
            }

            if ((changed & RunHudField.Health) != 0)
            {
                if (_healthMaxText != null)
                {
                    // 8-R concepts: the value large, "/ max" small beside it - integers through SetText, no string.
                    _healthValue.SetText("{0}", _model.HealthCurrent);
                    if (_model.HealthMax != _healthMaxShown)
                    {
                        _healthMaxShown = _model.HealthMax;
                        _healthMaxText.SetText("/ {0}", _healthMaxShown);
                    }
                }
                else
                {
                    _healthValue.text = _model.HealthText;
                }
            }

            if ((changed & RunHudField.HealthState) != 0 && _deathAt < 0f)   // E22: the death line keeps the row
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
                ApplyWeaponIcon();
            }

            if ((changed & (RunHudField.WeaponName | RunHudField.Build)) != 0)
            {
                // 8-R (AD SHOULD-A1): the weapon and its build on one calm-only line; built on a change, never per frame.
                _classicName.text = _model.WeaponName + "  ·  " + _model.BuildText;
            }

            if ((changed & RunHudField.Ammo) != 0)
            {
                // E11: set, never rolled (a Data readout read mid-fight).
                if (_model.Magazine != _ladderMagazine || _model.PelletCount != _ladderPellets)
                {
                    _ammoCounter.SetMax(_model.Magazine);
                    LayLadder(_model.Magazine, _model.PelletCount);
                }

                _ammoCounter.SetNow(_model.Ammo);
                FillLadder(_model.Ammo);
            }

            bool low = _model.AmmoLow;
            if ((changed & RunHudField.Reload) != 0 || low != _ammoLowShown)
            {
                RunHudReloadState reload = _model.ReloadState;
                // E11: a low magazine turns the reload marks StateWarning, so Focus400 and #F7C766 never mean two things in one panel.
                UiColor mark = low ? UiColor.StateWarning : UiColor.BarReload;
                _reloadRing.SetColour(mark);
                _reloadBar.SetColor(mark);
                // AD 8-R SHOULD-1: at LOW the ring's track and the ladder's rounds say it too (the fill is only the reload).
                _reloadRing.SetGhost(low ? UiColor.StateWarning : UiColor.TextPrimary, low ? LowRingAlpha : 0.25f);
                _ladderFill.color = UiTokens.Get(low ? UiColor.StateWarning : UiColor.TextPrimary);
                if (reload != RunHudReloadState.Reloading)
                {
                    _reloadRing.Set(0f); // the ghost stays, the fill is the reload
                }
                ShowReloadPrompt(reload == RunHudReloadState.MagazineEmpty);
                switch (reload)
                {
                    case RunHudReloadState.Reloading:
                        _reloadBar.SetValue(_model.ReloadProgress01, true);
                        _reloadRing.Set(_model.ReloadProgress01);
                        _reloadCaption.text = string.Empty;   // AD 8-R SHOULD-2: the Focus400 ring fill says it
                        _reloadCaption.color = UiTokens.Get(mark);
                        break;

                    case RunHudReloadState.MagazineEmpty:
                        if (_shownReload != RunHudReloadState.MagazineEmpty)
                        {
                            UiEvents.Raise(UiEvent.HudMagazineEmpty);
                        }

                        _reloadBar.SetValue(0f, true);
                        // E11: EMPTY + the reload control's live glyph (UiPrompt knows the device in hand).
                        _reloadCaption.text = "EMPTY";
                        _reloadCaption.color = UiTokens.Get(UiColor.StateWarning);
                        break;

                    default:
                        _reloadBar.SetValue(1f, true);
                        _reloadCaption.text = low ? _ammoLowWord : string.Empty;   // 12b: "LOW AMMO" (AD FP cluster SHOULD-2)
                        _reloadCaption.color = UiTokens.Get(UiColor.StateWarning);
                        if (_shownReload == RunHudReloadState.Reloading)
                        {
                            UiPresets.StampIn(_ammoValue.rectTransform); // E11: the reload lands - the number stamps
                        }

                        break;
                }

                _shownReload = reload;
                _ammoLowShown = low;
                TintWeaponIcon();
            }

            if ((changed & RunHudField.Suspended) != 0)
            {
                // REQ-057. Dimmed, labelled, and still showing every number.
                bool suspended = _model.Suspended;
                // E14: dims and returns at Base (Decor: at once under reduced motion); the numbers stay the whole time.
                UiMotion.Alpha(_classicGroup, suspended ? UiTokens.OpacitySuspended : 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);

                if (_suspendedChip.activeSelf != suspended)
                {
                    _suspendedChip.SetActive(suspended);
                }

                if (_reloadCaptionGroup != null)
                {
                    _reloadCaptionGroup.alpha = suspended ? 0f : 1f;   // 8-R: SUSPENDED owns the caption slot
                }
            }
        }

        /// <summary>
        /// WP-068 slice 0 (D8): the top edge (canvas units, from the bottom) of what the HUD
        /// currently occupies at bottom centre - the Special block while it is shown, 0
        /// otherwise. The round-end "NEXT ROUND IN" strip (UpgradeScreen, its own canvas,
        /// same reference resolution) docks above it instead of drawing over it: a round
        /// can end with the Special still on the field (baseline bot-special-ground vs
        /// bot-upgrade put both in the same place).
        /// </summary>
        public static float BottomCentreTop { get; private set; }

        private static void TopBand(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
        }

        private void UpdateSpecialWeapon()
        {
            SpecialWeaponHudState state = WeaponEvents.Special;
            bool visible = state.Phase != SpecialWeaponPhase.None;

            if (_specialBlock.activeSelf != visible)
            {
                _specialBlock.SetActive(visible);
            }

            BottomCentreTop = visible ? ((RectTransform)_specialBlock.transform).offsetMax.y : 0f;

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
            RetitleSpecialBanner(state);

            bool warning = state.FinalSecondsWarning;
            bool reloading = state.IsReloading;

            // E14 v3 words: RELOADING while a cell / canister changes, ENDING in the time cap's final seconds.
            _specialPhase.text = warning ? "ENDING" : reloading ? "RELOADING" : "ACTIVE";
            _specialPhase.color = UiTokens.Get(warning ? UiColor.StateDanger : UiColor.StateGold);
            _specialCaption.text = name;
            _specialCaption.color = UiTokens.Get(UiColor.TextPrimary);

            // The centre: rounds in the magazine over the reserve; strings only when an integer changes.
            if (state.AmmoInMagazine != _specialMagShown)
            {
                _specialMagShown = state.AmmoInMagazine;
                _specialTimer.text = state.AmmoInMagazine.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (state.ReserveAmmo != _specialReserveShown)
            {
                _specialReserveShown = state.ReserveAmmo;
                _specialReserve.text = "/ " + state.ReserveAmmo.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            _specialTimer.color = UiTokens.Get(state.AmmoInMagazine > 0 ? UiColor.StateGold : UiColor.StateWarning);

            // The ring: one segment a round; the reload refills it in the reload colour (E11's language).
            LaySpecialTicks(state.MagazineSize);
            float magazine = state.MagazineSize > 0 ? Mathf.Clamp01((float)state.AmmoInMagazine / state.MagazineSize) : state.ActiveProgress01;
            _specialRing.color = UiTokens.Get(reloading ? UiColor.BarReload : UiColor.BarSpecial);
            _specialRing.fillAmount = SpecialRingFill(reloading ? state.ReloadProgress01 : magazine);

            // The time cap strip, and Raiju's charge arc.
            _specialCapFill.fillAmount = state.ActiveProgress01;
            _specialCapFill.color = UiTokens.Get(warning ? UiColor.BarHealthLow : UiColor.TextPrimary);   // E14: final 3 s Coral400
            bool charging = state.Charge01 > 0f;
            if (_specialCharge.enabled != charging)
            {
                _specialCharge.enabled = charging;
            }

            if (charging)
            {
                _specialCharge.fillAmount = state.Charge01;
            }

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

            // DEC-094 v3: the activation's shots and the bodies they reached (Raiju's hops, Namekuji's direct hits).
            int seconds = 0;
            int hits = _swFieldHits;
            int mode = _swShots > 0 ? 3 : 0;

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
                case 3: _specialUsage.text = "SHOTS " + _swShots + (hits > 0 ? "  ·  HITS " + hits : string.Empty); break;
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
                    _specialMagShown = int.MinValue;
                    _specialReserveShown = int.MinValue;
                    if (_specialGlyph != null)
                    {
                        Sprite glyph = UiArt.SpecialIcon(e.Id);
                        UiArt.SetGlyph(_specialGlyph, glyph);
                        if (UiArt.IsGlyph(glyph))
                        {
                            UiArt.ApplyGlyph(_specialGlyph, GlyphInk);
                        }

                        _specialGlyph.enabled = glyph != null;
                        // N19: the Special's glyph wears its identity tint (Arc / Gel); the ring stays gold (DEC-097 reserve).
                        _specialGlyph.color = UiTokens.Get(SpecialTint(e.Id));
                        if (glyph != null)
                        {
                            UiPresets.StampIn(_specialGlyph.rectTransform);   // E14: the weapon's glyph stamps with the banner
                        }
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
                    if (_specialUsage != null)
                    {
                        // E14: the HIT count pops 1.06 → 1 over 120 ms (text-bearing, so ≤ 1.06).
                        UiMotion.ScaleSeconds(_specialUsage.rectTransform, SpecialHitPop, 1f, SpecialHitPopSeconds, UiEase.Settle, UiMotionClass.Decor);
                    }

                    break;
                case SpecialUsageKind.ReloadEnded:
                    if (_specialTimer != null)
                    {
                        UiPresets.StampIn(_specialTimer.rectTransform);   // E14 v3: the refilled count stamps (E11's reload landing)
                    }

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

        /// <summary>E14 v3: one notch per round of the magazine (Raiju 3, Namekuji 8), evenly round the ring from 12 o'clock.</summary>
        private void LaySpecialTicks(float activeSeconds)
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(activeSeconds), 0, SpecialTickCapacity);
            if (count == _specialTickCount)
            {
                return;
            }

            _specialTickCount = count;
            const float radius = 62f;   // the stroke's centre line (r 58–66)
            for (int i = 0; i < SpecialTickCapacity; i++)
            {
                Image tick = _specialTickMarks[i];
                tick.enabled = i < count && count > 1;
                if (!tick.enabled)
                {
                    continue;
                }

                ((RectTransform)tick.transform).sizeDelta = new Vector2(i % 5 == 0 ? SpecialTickStrongWidth : SpecialTickWidth, SpecialTickLength);

                float degrees = 360f * i / count;
                float radians = degrees * Mathf.Deg2Rad;
                var rect = (RectTransform)tick.transform;
                rect.anchoredPosition = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * radius;
                rect.localRotation = Quaternion.Euler(0f, 0f, -degrees);
            }
        }

        /// <summary>E14: the fill shown this frame - the real progress, grown from 0 over the first 320 ms after a pickup.</summary>
        private float SpecialRingFill(float progress01)
        {
            float t = (Time.unscaledTime - _specialPickupAt) / SpecialPickupFillSeconds;
            if (t >= 1f || t < 0f || UiMotion.ReducedMotion)
            {
                return progress01;
            }

            return progress01 * UiEaseCurve.Evaluate(UiEase.Settle, t);
        }

        /// <summary>
        /// Swap the Special Weapon block between its two readouts: a blinking pip while
        /// the weapon is on the ground, digits and a bar while it is in hand.
        /// </summary>
        private void SetGroundReadout(bool onGround)
        {
            if (_specialTicks != null && _specialTicks.gameObject.activeSelf == onGround)
            {
                _specialTicks.gameObject.SetActive(!onGround);
            }

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

            if (_specialReserve != null && _specialReserve.gameObject.activeSelf == onGround)
            {
                _specialReserve.gameObject.SetActive(!onGround);
                _specialCap.SetActive(!onGround);
            }

            if (onGround && _specialCharge != null)
            {
                _specialCharge.enabled = false;
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
            _specialPip.canvasRenderer.SetColor(colour);

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
                _worldMarker.SetVisible(false);
                return;
            }

            _worldMarker.SetVisible(true);
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

            float angle = 0f;
            if (offScreen)
            {
                // AD 8c MUST-2: the edge marker rides the inset edge on its true bearing and slides clear of the ink
                // clusters; the chevron keeps the true bearing.
                Vector2 direction = local.sqrMagnitude > 0.0001f ? local.normalized : Vector2.up;
                angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                local = EdgeMarkerFor(direction);
            }
            else
            {
                local.x = Mathf.Clamp(local.x, -halfWidth, halfWidth);
                local.y = Mathf.Clamp(local.y, -halfHeight, halfHeight);
            }

            // G-21. The SAME phase the world beacon is pulsing on, taken straight off the
            // snapshot rather than recomputed here. Two clocks at the same rate still
            // drift in phase, and a player reads that drift as two separate things
            // happening. One clock, two surfaces.
            float blink = GroundBlinkClock.Intensity01(state.GroundBlinkPhase);
            float urgency = state.GroundProgress01 < GroundUrgencyStart
                ? 1f - state.GroundProgress01 / GroundUrgencyStart
                : 0f;

            // "How far" - a different question from "how long", so the distance stays (G-21). E14: measured from the
            // player, whose walk it is; the camera stands in when there is no player.
            PlayerRig rig = PlayerRegistry.Primary;
            Vector3 from = rig != null ? rig.transform.position : camera.transform.position;
            float metres = Vector3.Distance(from, state.WorldPosition);
            _worldMarker.Place(local, offScreen, angle, metres, blink, urgency);
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

            // WP-022J r2 (AP-020 §2.4 S-8): the runtime raises PickedUp BEFORE the controller
            // publishes the new state, so WeaponEvents.Special.DisplayName is still the
            // previous weapon's on a consecutive pickup. Show the generic line now and let
            // the first Active snapshot with a name retitle the banner (same banner timer).
            ShowBanner("SPECIAL WEAPON ONLINE");
            _bannerAwaitsSpecialName = true;
            PlaySpecialPickup(activeDuration);
        }

        /// <summary>
        /// E14 · the pickup: the ring grows 0 → 1 over 320 ms (Settle, then the real progress), one kinpaku glint sweeps
        /// the fill (never under Reduce Flashes) and 12 sparks burst from the centre (≤ 0.3 s; none under reduced motion).
        /// </summary>
        private void PlaySpecialPickup(float activeDuration)
        {
            _specialPickupAt = Time.unscaledTime;

            if (_specialLeaf != null && !UiSettings.ReduceFlashes)
            {
                _specialLeaf.Play();
            }

            if (_specialSparks != null && !UiMotion.ReducedMotion)
            {
                _specialSparks.Play();
            }
        }

        /// <summary>Capture seam (-ddUiCapture hudstates): the pickup moment as a live pickup plays it.</summary>
        public void CaptureSpecialPickup(float activeDuration)
        {
            PlaySpecialPickup(activeDuration);
        }

        /// <summary>True between a Special pickup and the first Active snapshot that names the weapon.</summary>
        private bool _bannerAwaitsSpecialName;

        private void RetitleSpecialBanner(in SpecialWeaponHudState state)
        {
            if (!_bannerAwaitsSpecialName)
            {
                return;
            }

            if (state.Phase != SpecialWeaponPhase.Active)
            {
                return;
            }

            _bannerAwaitsSpecialName = false;
            if (!string.IsNullOrEmpty(state.DisplayName) && _bannerRemaining > 0f)
            {
                // WP-068 12b (AD R1-B SHOULD-2): the line carries the Special's glyph, 48 px in its identity tint, stamping in
                // with the name, left of the words (HudBanner.GlyphGap).
                _banner.Retitle(UiFactory.Clip(state.DisplayName.ToUpperInvariant(), 30) + " ONLINE",
                    UiArt.SpecialIcon(state.WeaponId), UiTokens.Get(SpecialTint(state.WeaponId)));
            }
        }

        /// <summary>N19: a Special's identity tint - Arc for Raiju, Gel for Namekuji (UiGlyphTints; the ring stays gold).</summary>
        private static UiColor SpecialTint(SpecialWeaponId id)
        {
            return id == SpecialWeaponId.Raiju ? UiGlyphTints.ChainLightning : UiGlyphTints.Gel;
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
            _banner.Show(message, Time.unscaledTime);
            _bannerRemaining = BannerSeconds;
        }

        /// <summary>E13: the envelope runs on presets; this only starts the exit at its time and reports what is left.</summary>
        private void UpdateBanner()
        {
            float now = Time.unscaledTime;
            if (UiFrozenFrame.IsHeld && _banner.Live(now))
            {
                _banner.Cut();   // a panel is over the HUD (shop / Pause): the banner never reads through its band (12b)
            }

            _bannerRemaining = _banner.Tick(now);
        }
    }
}
