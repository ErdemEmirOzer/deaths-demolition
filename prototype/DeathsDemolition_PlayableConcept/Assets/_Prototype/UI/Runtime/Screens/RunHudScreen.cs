using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
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
    /// </summary>
    public sealed class RunHudScreen : UiScreen
    {
        private const float BlockWidth = 420f;
        private const float SmallBlockWidth = 360f;
        private const float GoldBlockWidth = 240f;

        // Block heights are the sum of their own rows plus the panel padding, computed
        // by hand and kept here so the arithmetic is reviewable in one place.
        private const float RoundBlockHeight = 160f;
        private const float ClassicBlockHeight = 164f;
        private const float SpecialBlockHeight = 112f;

        private const float BannerSeconds = 2.4f;
        private const float MarkerEdgeInset = 72f;

        protected override int SortingOrder
        {
            get { return UiTokens.SortingHud; }
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
        private Text _roundValue;
        private Text _enemiesValue;
        private Text _enemiesBreakdown;

        // ---- gold
        private Text _goldValue;

        // ---- health
        private UiBar _healthBar;
        private Text _healthValue;
        private Text _healthState;

        // ---- classic weapon
        private CanvasGroup _classicGroup;
        private Text _classicName;
        private Text _classicBuild;
        private Text _ammoValue;
        private UiBar _reloadBar;
        private Text _reloadCaption;
        private GameObject _suspendedChip;

        // ---- special weapon
        private GameObject _specialBlock;
        private Text _specialPhase;
        private Text _specialTimer;
        private Text _specialCaption;
        private UiBar _specialBar;
        private Image _specialPip;

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
        private Text _bannerText;
        private CanvasGroup _bannerGroup;
        private float _bannerRemaining;

        // ---- world marker
        private RectTransform _marker;
        private RectTransform _markerArrow;
        private Text _markerLabel;
        private Image _markerCore;
        private Image _markerRing;
        private Image _markerHalo;

        private bool _healthBound;

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

            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
            RunEvents.RoundPoolExhausted += OnPoolExhausted;

            WeaponEvents.SpecialWeaponSpawned += OnSpecialSpawned;
            WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds += OnSpecialFinalSeconds;
            WeaponEvents.SpecialWeaponEnded += OnSpecialEnded;
            WeaponEvents.SpecialWeaponGroundExpired += OnSpecialGroundExpired;

            PlayerHealth.AnyHealthChanged += ApplyHealth;
        }

        protected override void Start()
        {
            base.Start();
            BindHealthSnapshot();
        }

        private void OnDestroy()
        {
            RunEvents.RoundStarted -= OnRoundStarted;
            RunEvents.RoundCompleted -= OnRoundCompleted;
            RunEvents.RoundPoolExhausted -= OnPoolExhausted;

            WeaponEvents.SpecialWeaponSpawned -= OnSpecialSpawned;
            WeaponEvents.SpecialWeaponPickedUp -= OnSpecialPickedUp;
            WeaponEvents.SpecialWeaponFinalSeconds -= OnSpecialFinalSeconds;
            WeaponEvents.SpecialWeaponEnded -= OnSpecialEnded;
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

            PlayerHealth health = FindObjectOfType<PlayerHealth>();
            if (health == null)
            {
                return;
            }

            _healthBound = true;
            ApplyHealth(health.Snapshot);
        }

        // ================================================================== blocks

        private void BuildRoundBlock()
        {
            RectTransform block = HudPanel("Round", new Vector2(0f, 1f), SmallBlockWidth, RoundBlockHeight);
            RectTransform host = UiFactory.CardContent(block, UiSpace.L);

            var stack = new UiStack(host);

            Text roundLabel = UiFactory.Label("RoundLabel", host, "ROUND", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)roundLabel.transform, UiFactory.LineHeight(UiType.Label));

            _roundValue = UiFactory.Numeric("RoundValue", host, "—", UiType.NumericL, UiColor.TextPrimary);
            stack.Add((RectTransform)_roundValue.transform, 36f);

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.S);

            Text enemiesLabel = UiFactory.Label(
                "EnemiesLabel", host, "ENEMIES REMAINING", UiType.Label, UiColor.TextMuted);
            RectTransform enemiesLabelRect = stack.Add(
                (RectTransform)enemiesLabel.transform, UiFactory.LineHeight(UiType.Label));

            // The count sits on the same line as its label, right aligned: the two
            // together are one fact, and stacking them would cost a line the HUD does
            // not have.
            _enemiesValue = UiFactory.Numeric(
                "EnemiesValue", host, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleRight);
            RectTransform enemiesValueRect = UiFactory.Fill((RectTransform)_enemiesValue.transform);
            enemiesValueRect.anchorMin = new Vector2(1f, 1f);
            enemiesValueRect.anchorMax = new Vector2(1f, 1f);
            enemiesValueRect.pivot = new Vector2(1f, 1f);
            enemiesValueRect.sizeDelta = new Vector2(120f, 26f);
            enemiesValueRect.anchoredPosition = enemiesLabelRect.anchoredPosition;

            _enemiesBreakdown = UiFactory.Label(
                "EnemiesBreakdown", host, "—", UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)_enemiesBreakdown.transform, UiFactory.LineHeight(UiType.Caption));
        }

        private void BuildGoldBlock()
        {
            RectTransform block = HudPanel("Gold", new Vector2(1f, 1f), GoldBlockWidth, 84f);
            RectTransform host = UiFactory.CardContent(block, UiSpace.L);

            Image coin = UiFactory.NewImage("Coin", host, UiColor.StateGold, UiSprites.Disc);
            var coinRect = (RectTransform)coin.transform;
            coinRect.anchorMin = new Vector2(0f, 0.5f);
            coinRect.anchorMax = new Vector2(0f, 0.5f);
            coinRect.pivot = new Vector2(0f, 0.5f);
            coinRect.sizeDelta = new Vector2(24f, 24f);
            coinRect.anchoredPosition = Vector2.zero;

            Text label = UiFactory.Label("Label", host, "GOLD", UiType.Label, UiColor.TextMuted);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMin = new Vector2(40f, 24f);

            _goldValue = UiFactory.Numeric(
                "Value", host, "0", UiType.NumericM, UiColor.StateGold, TextAnchor.LowerRight);
            RectTransform valueRect = UiFactory.Fill((RectTransform)_goldValue.transform);
            valueRect.offsetMin = new Vector2(40f, 0f);
        }

        private void BuildHealthBlock()
        {
            RectTransform block = HudPanel("Health", new Vector2(0f, 0f), BlockWidth, 116f);
            RectTransform host = UiFactory.CardContent(block, UiSpace.L);

            var stack = new UiStack(host);

            Text label = UiFactory.Label("Label", host, "HEALTH", UiType.Label, UiColor.TextMuted);
            RectTransform labelRect = stack.Add(
                (RectTransform)label.transform, UiFactory.LineHeight(UiType.Label));

            _healthValue = UiFactory.Numeric(
                "Value", host, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleRight);
            RectTransform valueRect = (RectTransform)_healthValue.transform;
            valueRect.anchorMin = new Vector2(1f, 1f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 1f);
            valueRect.sizeDelta = new Vector2(160f, 26f);
            valueRect.anchoredPosition = labelRect.anchoredPosition;

            stack.Gap(UiSpace.S);

            _healthBar = UiFactory.Bar("Bar", host, UiColor.BarHealth, 18f);
            stack.Add((RectTransform)_healthBar.transform, 18f);

            stack.Gap(UiSpace.S);

            _healthState = UiFactory.Label("State", host, string.Empty, UiType.Caption, UiColor.AccentMoss);
            stack.Add((RectTransform)_healthState.transform, UiFactory.LineHeight(UiType.Caption));
        }

        private void BuildWeaponBlocks()
        {
            // ---- Special Weapon, stacked above the classic block --------------
            RectTransform special = HudPanel(
                "SpecialWeapon", new Vector2(1f, 0f), BlockWidth, SpecialBlockHeight,
                ClassicBlockHeight + (int)UiSpace.L);

            _specialBlock = special.gameObject;
            RectTransform specialHost = UiFactory.CardContent(special, UiSpace.L);

            var specialStack = new UiStack(specialHost);

            _specialPhase = UiFactory.Label(
                "Phase", specialHost, "ON THE FIELD", UiType.Label, UiColor.StateGold);
            RectTransform phaseRect = specialStack.Add(
                (RectTransform)_specialPhase.transform, UiFactory.LineHeight(UiType.Label));

            _specialTimer = UiFactory.Numeric(
                "Timer", specialHost, "0.0", UiType.NumericL, UiColor.StateGold, TextAnchor.MiddleRight);
            RectTransform timerRect = (RectTransform)_specialTimer.transform;
            timerRect.anchorMin = new Vector2(1f, 1f);
            timerRect.anchorMax = new Vector2(1f, 1f);
            timerRect.pivot = new Vector2(1f, 1f);
            timerRect.sizeDelta = new Vector2(140f, 40f);
            timerRect.anchoredPosition = phaseRect.anchoredPosition;

            // The blink pip lives exactly where the ground timer's digits used to, so the
            // eye that learned to look there still finds the answer - it is just an answer
            // with no number in it.
            _specialPip = UiFactory.NewImage("GroundPip", specialHost, UiColor.StateGold, UiSprites.Disc);
            RectTransform pipRect = (RectTransform)_specialPip.transform;
            pipRect.anchorMin = new Vector2(1f, 1f);
            pipRect.anchorMax = new Vector2(1f, 1f);
            pipRect.pivot = new Vector2(1f, 1f);
            pipRect.sizeDelta = new Vector2(26f, 26f);
            pipRect.anchoredPosition = new Vector2(
                timerRect.anchoredPosition.x - 10f, timerRect.anchoredPosition.y - 6f);
            _specialPip.gameObject.SetActive(false);

            _specialCaption = UiFactory.Label(
                "Caption", specialHost, string.Empty, UiType.BodyStrong, UiColor.TextPrimary);
            RectTransform captionRect = specialStack.Add(
                (RectTransform)_specialCaption.transform, UiFactory.LineHeight(UiType.BodyStrong));

            // Keeps the caption clear of the timer, which spans both of the rows above.
            captionRect.offsetMax = new Vector2(-150f, captionRect.offsetMax.y);

            specialStack.Gap(UiSpace.S);

            _specialBar = UiFactory.Bar("Bar", specialHost, UiColor.BarSpecial, 12f);
            specialStack.Add((RectTransform)_specialBar.transform, 12f);

            _specialBlock.SetActive(false);

            // ---- classic weapon ------------------------------------------------
            RectTransform classic = HudPanel(
                "ClassicWeapon", new Vector2(1f, 0f), BlockWidth, ClassicBlockHeight);
            RectTransform host = UiFactory.CardContent(classic, UiSpace.L);

            // The CanvasGroup is on the CONTENT, not on the panel: REQ-057 dims the
            // information, never the surface. Keeping the backing at full opacity is
            // what makes the 5.55:1 measurement for dimmed text valid, and it keeps the
            // SUSPENDED pill - which is parented to the panel, outside this group - at
            // full strength while everything it is explaining fades behind it.
            _classicGroup = UiFactory.Group(host.gameObject);

            var stack = new UiStack(host);

            _classicName = UiFactory.Label("Name", host, "—", UiType.Label, UiColor.TextMuted);
            RectTransform nameRect = stack.Add(
                (RectTransform)_classicName.transform, UiFactory.LineHeight(UiType.Label));
            nameRect.offsetMax = new Vector2(-160f, nameRect.offsetMax.y);

            _classicBuild = UiFactory.Label("Build", host, string.Empty, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)_classicBuild.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.XS);

            _ammoValue = UiFactory.Numeric("Ammo", host, "—", UiType.NumericL, UiColor.TextPrimary);
            stack.Add((RectTransform)_ammoValue.transform, 38f);

            stack.Gap(UiSpace.XS);

            _reloadBar = UiFactory.Bar("ReloadBar", host, UiColor.BarReload, 10f);
            stack.Add((RectTransform)_reloadBar.transform, 10f);

            stack.Gap(UiSpace.XS);

            _reloadCaption = UiFactory.Label(
                "ReloadCaption", host, string.Empty, UiType.Caption, UiColor.BarReload);
            stack.Add((RectTransform)_reloadCaption.transform, UiFactory.LineHeight(UiType.Caption));

            // REQ-057 pill. Sits over the block, never instead of it.
            Text chip = UiFactory.Chip(
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

            vignette.gameObject.AddComponent<UiPulse>().Configure(
                new Graphic[] { top, bottom }, 0.10f, 0.30f, UiTokens.PulsePeriod * 0.5f);

            _warningVignette.SetActive(false);

            // Final-seconds banner, directly under the top edge.
            Text warning = UiFactory.Chip(
                "FinalSeconds", ScreenRect, "SPECIAL WEAPON ENDING",
                UiColor.StateDanger, UiColor.SurfaceInset, 420f, 44f);

            _warningBanner = warning.transform.parent.gameObject;
            var warningRect = (RectTransform)_warningBanner.transform;
            warningRect.anchorMin = new Vector2(0.5f, 1f);
            warningRect.anchorMax = new Vector2(0.5f, 1f);
            warningRect.pivot = new Vector2(0.5f, 1f);
            warningRect.anchoredPosition = new Vector2(0f, -(int)UiSpace.Section);
            _warningBanner.SetActive(false);

            // Round / phase banner, just below the warning slot so the two never collide.
            _bannerText = UiFactory.Label(
                "Banner", ScreenRect, string.Empty, UiType.H1, UiColor.TextPrimary, TextAnchor.UpperCenter);
            RectTransform bannerRect = (RectTransform)_bannerText.transform;
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.sizeDelta = new Vector2(900f, 120f);
            bannerRect.anchoredPosition = new Vector2(0f, -((int)UiSpace.Section + 64f));

            _bannerGroup = UiFactory.Group(_bannerText.gameObject);
            _bannerGroup.alpha = 0f;
        }

        private void BuildMarker()
        {
            _marker = UiFactory.NewRect("SpecialWeaponMarker", ScreenRect);
            _marker.sizeDelta = new Vector2(56f, 56f);

            _markerHalo = UiFactory.NewImage("Halo", _marker, UiColor.SurfaceInset, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_markerHalo.transform);
            SetAlpha(_markerHalo, 0.65f);

            _markerRing = UiFactory.NewImage("Ring", _marker, UiColor.StateGold, UiSprites.Ring);
            UiFactory.Fill((RectTransform)_markerRing.transform);

            _markerCore = UiFactory.NewImage("Core", _marker, UiColor.StateGold, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_markerCore.transform, 18f);

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
        /// A HUD panel: near-opaque backing docked to a screen corner. The backing is
        /// opaque on purpose - a translucent HUD cannot guarantee a text contrast ratio,
        /// because the ratio would depend on what the camera happens to be pointing at.
        /// </summary>
        private RectTransform HudPanel(string name, Vector2 corner, float width, float height, float stackOffset = 0f)
        {
            RectTransform panel = UiFactory.Card(name, ScreenRect, UiColor.BorderHairline, UiColor.HudInk);
            UiFactory.Corner(panel, corner, width, height);

            if (stackOffset > 0f)
            {
                Vector2 position = panel.anchoredPosition;
                position.y += Mathf.Approximately(corner.y, 0f) ? stackOffset : -stackOffset;
                panel.anchoredPosition = position;
            }

            return panel;
        }

        // ================================================================== updates

        private void Update()
        {
            if (!_healthBound)
            {
                BindHealthSnapshot();
            }

            UpdateRun();
            UpdateClassicWeapon();
            UpdateSpecialWeapon();
            UpdateBanner();
        }

        private void LateUpdate()
        {
            UpdateMarker();
        }

        private void UpdateRun()
        {
            RunHudState state = RunEvents.Current;

            _roundValue.text = state.Round > 0 ? state.Round.ToString() : "—";
            _goldValue.text = state.Gold.ToString();
            _enemiesValue.text = state.EnemiesRemaining.ToString();

            if (state.Round <= 0)
            {
                // The run has not published a state yet: the HUD says so instead of
                // showing a confident zero.
                _enemiesBreakdown.text = "waiting for the round to start";
            }
            else if (state.PoolExhausted)
            {
                _enemiesBreakdown.text = "pool empty  ·  " + state.AliveEnemies + " still on the map";
            }
            else
            {
                _enemiesBreakdown.text = state.PoolRemaining + " left to arrive  ·  " +
                                         state.AliveEnemies + " on the map";
            }
        }

        private void ApplyHealth(PlayerHealthSnapshot snapshot)
        {
            _healthBound = true;

            float normalized = snapshot.Normalized;

            // One threshold, one constant. The bar, the readout and the damage edge all
            // read UiTokens.LowHealthFraction, so the HUD cannot go red on a different
            // frame from the screen edge that is telling the player why.
            bool low = normalized <= UiTokens.LowHealthFraction;

            _healthBar.SetValue(normalized);
            _healthBar.SetColor(low ? UiColor.BarHealthLow : UiColor.BarHealth);

            _healthValue.text = Mathf.CeilToInt(Mathf.Max(0f, snapshot.Current)) + " / " +
                                Mathf.RoundToInt(snapshot.Max);
            _healthValue.color = UiTokens.Get(low ? UiColor.StateDanger : UiColor.TextPrimary);

            if (snapshot.IsDead)
            {
                _healthState.text = "DOWN";
                _healthState.color = UiTokens.Get(UiColor.StateDanger);
            }
            else if (snapshot.IsRegenerating)
            {
                _healthState.text = "RECOVERING";
                _healthState.color = UiTokens.Get(UiColor.StateSuccess);
            }
            else if (snapshot.SecondsUntilRegen > 0f)
            {
                _healthState.text = "recovery in " + snapshot.SecondsUntilRegen.ToString("0.0") + "s";
                _healthState.color = UiTokens.Get(UiColor.TextSecondary);
            }
            else
            {
                _healthState.text = string.Empty;
            }
        }

        private void UpdateClassicWeapon()
        {
            ClassicWeaponHudState state = WeaponEvents.Classic;

            _classicName.text = string.IsNullOrEmpty(state.DisplayName)
                ? "—"
                : UiFactory.Clip(state.DisplayName.ToUpperInvariant(), 26);

            _classicBuild.text = string.IsNullOrEmpty(state.BuildDescription)
                ? "no upgrades"
                : UiFactory.Clip(state.BuildDescription, 34);

            _ammoValue.text = state.MagazineSize > 0
                ? state.AmmoInMagazine + " / " + state.MagazineSize
                : "—";

            if (state.IsReloading)
            {
                _reloadBar.SetValue(Mathf.Clamp01(state.ReloadProgress01), true);
                _reloadCaption.text = "RELOADING";
                _reloadCaption.color = UiTokens.Get(UiColor.BarReload);
            }
            else if (state.IsEmpty && state.MagazineSize > 0)
            {
                _reloadBar.SetValue(0f, true);
                _reloadCaption.text = "MAGAZINE EMPTY  ·  PRESS R";
                _reloadCaption.color = UiTokens.Get(UiColor.StateDanger);
            }
            else
            {
                _reloadBar.SetValue(1f, true);
                _reloadCaption.text = string.Empty;
            }

            // REQ-057. Dimmed, labelled, and still showing every number.
            bool suspended = state.TemporarilyDisabled;
            _classicGroup.alpha = suspended ? UiTokens.OpacitySuspended : 1f;

            if (_suspendedChip.activeSelf != suspended)
            {
                _suspendedChip.SetActive(suspended);
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
            _specialTimer.text = FormatSeconds(state.ActiveRemaining);
            _specialTimer.color = UiTokens.Get(warning ? UiColor.StateDanger : UiColor.StateGold);
            _specialBar.SetColor(warning ? UiColor.BarHealthLow : UiColor.BarSpecial);
            _specialBar.SetValue(state.ActiveProgress01, true);

            SetWarning(warning);
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

            if (_specialBar != null && _specialBar.gameObject.activeSelf == onGround)
            {
                _specialBar.gameObject.SetActive(!onGround);
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
            _markerLabel.text = Mathf.RoundToInt(distance) + " m";
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
            ShowBanner("ROUND " + round);
        }

        private void OnRoundCompleted(int round)
        {
            ShowBanner("ROUND " + round + " COMPLETE");
        }

        private void OnPoolExhausted(int round)
        {
            ShowBanner("CLEAR THE LAST OF THEM");
        }

        private void OnSpecialSpawned(Vector3 position, float groundLifetime)
        {
            ShowBanner("SPECIAL WEAPON ON THE FIELD");
        }

        private void OnSpecialPickedUp(float activeDuration)
        {
            ShowBanner("HEAVY ROTARY CANNON ONLINE");
        }

        private void OnSpecialFinalSeconds(float remaining)
        {
            SetWarning(true);
        }

        private void OnSpecialEnded()
        {
            SetWarning(false);
            ShowBanner("CLASSIC WEAPON RESTORED");
        }

        private void OnSpecialGroundExpired()
        {
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

        private static string FormatSeconds(float seconds)
        {
            return Mathf.Max(0f, seconds).ToString("0.0");
        }
    }
}
