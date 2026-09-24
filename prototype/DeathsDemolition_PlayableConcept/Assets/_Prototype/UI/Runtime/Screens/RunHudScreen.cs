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
        private const float SpecialBlockHeight = 216f;

        /// <summary>
        /// WP-068 8-R: the top-centre stack, from the screen top - the seal line (24 + 56), the calm breakdown (to 106),
        /// the threat chips (110 + 30); the final-seconds line and the banner sit under it.
        /// </summary>
        private const float ThreatRowTop = 110f;
        private const float WarningTop = 150f;
        private const float BannerTop = 196f;

        private const float BannerSeconds = 2.4f;
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
        private RunHudReloadState _shownReload = RunHudReloadState.Ready;
        private bool _ammoLowShown;
        private int _roundShown = int.MinValue;

        /// <summary>
        /// E12 (AD 8a MUST-1): the gold panel's only leaf - the A6 kinpaku flake tile in StateGold at this alpha, never a
        /// flat fill (the panel rests on HudInk like its siblings); the S-07 Leaf glint lays the same alpha only while it
        /// sweeps. UiArtThemeTests.HudKontrasti measures the numeral over it.
        /// </summary>
        public const float GoldLeafAlpha = 0.03f;   // AD 8a MUST-1's "≤ 0.10" is a perceptual (sRGB) figure; UI blends in linear light, where 0.03 looks like it
        private const float GoldLeafTilePixels = 96f;
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
        // metre changes - never once per frame.
        private int _specialTimerTenths = int.MinValue;
        private int _markerMetres = int.MinValue;
        private TMP_Text _specialCaption;
        private Image _specialRing;
        private Image _specialPip;
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
        private readonly ThreatSlot[] _threatSlots = new ThreatSlot[3];
        private int _threatWindupShown;
        private bool _threatUltra;
        private HudPlayerArcs _playerArcs;   // WP-068 8b · E13 rear arc, E17 player arc

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

            Sprite ringArt = ink ? UiArt.Kit("Ring.Progress132") : null;
            Image track = UiFactory.NewImage("Track", ringRoot, ink ? UiColor.TextPrimary : UiColor.SurfaceInset, ringArt != null ? ringArt : UiSprites.Ring);
            UiFactory.Fill((RectTransform)track.transform);
            if (ink)
            {
                Color ghost = track.color;
                ghost.a = 0.25f;
                track.color = ghost;
            }

            _specialRing = UiFactory.NewImage("Fill", ringRoot, UiColor.BarSpecial, ringArt != null ? ringArt : UiSprites.Ring);
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
            // Runtime blink tint is applied through CanvasRenderer. Keep the Graphic's
            // vertex colour neutral so the renderer tint is the exact palette colour,
            // rather than palette × palette.
            _specialPip.color = Color.white;
            _specialPip.gameObject.SetActive(false);

            _specialTimer = UiFactory.Numeric(
                "Timer", ringRoot, "0.0", UiType.NumericM, UiColor.StateGold, TextAnchor.MiddleCenter, false);
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
            if (ink)
            {
                UiHudInk.Apply(_specialPhase);
                UiHudInk.Apply(_specialTimer);
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

        /// <summary>E11: the trigger on an empty magazine - the ladder kicks 4 px and settles (Decor: none under reduced motion).</summary>
        private void OnDryFire()
        {
            RectTransform ladder = _ladderBack != null ? (RectTransform)_ladderBack.transform.parent : null;
            if (ladder == null || UiMotion.ReducedMotion)
            {
                return;
            }

            UiMotion.KillTarget(ladder, false);
            Vector2 home = new Vector2(0f, ladder.anchoredPosition.y);
            ladder.anchoredPosition = home + new Vector2(4f, 0f);
            UiMotion.Move(ladder, home, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
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

            // Round / phase banner, just below the warning slot so the two never collide.
            _bannerText = UiFactory.Label(
                "Banner", ScreenRect, string.Empty, UiType.H1, UiColor.TextPrimary, TextAnchor.UpperCenter);
            RectTransform bannerRect = (RectTransform)_bannerText.transform;
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.sizeDelta = new Vector2(900f, 120f);
            bannerRect.anchoredPosition = new Vector2(0f, -BannerTop);
            UiHudInk.Apply(_bannerText);   // 8-R: no panel under the words - they carry their own ink

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
            _markerHalo.color = Color.white;

            // AP-010 beacon marker (greyscale diamond, DEC-053): tinted with the beacon
            // reserve colour at runtime. The procedural ring/disc pair is the fallback.
            bool artMarker = UiTheme.Active != null && UiTheme.Active.HudMarker != null;
            _markerRing = UiFactory.NewImage("Ring", _marker, UiColor.StateGold, artMarker ? UiArt.HudMarker() : UiSprites.Ring);
            UiFactory.Fill((RectTransform)_markerRing.transform);
            _markerRing.preserveAspect = true;
            _markerRing.color = Color.white;

            _markerCore = UiFactory.NewImage("Core", _marker, UiColor.StateGold, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_markerCore.transform, artMarker ? 22f : 18f);
            _markerCore.color = Color.white;

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
            _markerLabel.color = Color.white;
            RectTransform labelRect = (RectTransform)_markerLabel.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0f);
            labelRect.anchorMax = new Vector2(0.5f, 0f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(160f, 24f);
            labelRect.anchoredPosition = new Vector2(0f, -(int)UiSpace.XS);

            _marker.gameObject.SetActive(false);
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
                _playerArcs.SetRear(_threatSlots[1].Shown || staged, staged ? CaptureRearDirection.Value : threat.RearDirection);
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
            UpdateBanner();
            UpdateThreat();
            UpdateInkPosture();   // 8-R: calm / danger
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
            UiCursor.SetContextVisible(!show);   // WP-068: UiCursor owns the pointer (a pad hides it anywhere)

            // Hidden = parked off-screen, never SetActive: toggling a graphic's active state
            // re-registers it with the canvas and rebuilds the whole HUD mesh each time, which
            // is exactly the per-frame allocation the HUD budget (0 B idle) forbids.
            if (local != _reticleLocal)
            {
                _reticleLocal = local;
                _reticle.Root.anchoredPosition = local;
            }

            // E13 / E17: the player-bound arcs follow the figure's screen point while the run is live.
            Vector2 playerLocal = OffScreen;
            if (show)
            {
                Vector3 playerViewport = camera.WorldToViewportPoint(rig.transform.position + Vector3.up * 0.9f);
                if (playerViewport.z >= 0f)
                {
                    Vector2 size = ScreenRect.rect.size;
                    playerLocal = new Vector2((playerViewport.x - 0.5f) * size.x, (playerViewport.y - 0.5f) * size.y);
                }
            }

            _playerArcs.Place(playerLocal);

            if (show)
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
                _reloadPrompt.gameObject.SetActive(reload == RunHudReloadState.MagazineEmpty);
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
                        _reloadCaption.text = low ? "LOW" : string.Empty;
                        _reloadCaption.color = UiTokens.Get(UiColor.StateWarning);
                        if (_shownReload == RunHudReloadState.Reloading)
                        {
                            UiPresets.StampIn(_ammoValue.rectTransform); // E11: the reload lands - the number stamps
                        }

                        break;
                }

                _shownReload = reload;
                _ammoLowShown = low;
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
                        UiArt.SetGlyph(_specialGlyph, glyph);
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
                Color haloColour = UiTokens.Get(UiColor.SurfaceInset);
                haloColour.a = Mathf.Lerp(0.45f, 0.75f, blink);
                _markerHalo.canvasRenderer.SetColor(haloColour);
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

            _markerLabel.canvasRenderer.SetColor(markerColour);
        }

        private static void ApplyMarkerBlink(Image image, Color colour, float blink, float weight)
        {
            if (image == null)
            {
                return;
            }

            Color c = colour;
            c.a = Mathf.Lerp(1f - weight, 1f, blink);
            image.canvasRenderer.SetColor(c);
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
                _bannerText.text = UiFactory.Clip(state.DisplayName.ToUpperInvariant(), 30) + " ONLINE";
            }
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
