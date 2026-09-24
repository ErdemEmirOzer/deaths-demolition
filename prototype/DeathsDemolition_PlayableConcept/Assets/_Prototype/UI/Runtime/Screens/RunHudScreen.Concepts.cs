using System.Collections.Generic;
using DD.Prototype.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8-R · the two HUD compositions put to the user (council/8r: AD, UI/UX, ID, GD; user feedback 24.09 "too
    /// big, all rectangles, amateur, covers the screen → minimalist, with depth"). Neither has a panel or a box.
    ///
    /// A "Seal &amp; Brush" (Mühür ve Fırça) - the corners keep their posts; each panel becomes one ink object.
    ///   - Gold: glyph and numeral, top-left.
    ///   - Round: a hanko seal holding the round number, with the enemies glyph and count, top-centre.
    ///   - Health: heart, value and a segmented brush stroke, bottom-left.
    ///   - Weapon: a lacquer ring round the weapon glyph, with the count and its ladder on a brush underline, bottom-right.
    /// B "Ensō" - the HUD is where the eyes are.
    ///   - The shapes live at the two fixation points: the health ensō on the ground around the ronin (HudEnso) and the
    ///     ammo arc on the reticle.
    ///   - The numbers live on one self-line under the figure: health left, Special centre, ammo right.
    ///   - Round, enemies and gold share one ledger line, top-centre. The corners stay empty.
    /// Shared depth:
    ///   - Numbers and words carry their own ink (UiHudInk: HudInk outline + underlay), glyphs an SDF ink edge.
    ///   - Strokes and rings cast a contact shadow and lean 3° toward the centre; digits stay level.
    ///   - Posture: calm shows the T2 lines (breakdown, weapon name and build, "/ max") with the T1 clusters receding to
    ///     0.94. Danger (a threat chip, low health, low or empty magazine, a hit in the last 3 s) hides the T2 lines and
    ///     brings the clusters forward. Text alpha never changes (ID 8-R).
    /// </summary>
    public sealed partial class RunHudScreen
    {
        private const float InkMargin = 32f;
        private const float StrokeLean = 3f;
        private const float ContactShadowAlpha = 0.35f;
        private const float CalmScale = 0.94f;
        private const float DangerHoldSeconds = 0.8f;
        private const float HitDangerSeconds = 3f;
        private const float AmmoArcAlpha = 0.7f;
        private static readonly Vector2 ContactShadowOffset = new Vector2(2f, -3f);
        private static readonly Vector3 GlyphInk = new Vector3(0.25f, 0f, 0f);

        private TMP_Text _healthMaxText;
        private int _healthMaxShown = int.MinValue;
        private readonly List<CanvasGroup> _conceptT2 = new List<CanvasGroup>(4);
        private readonly List<RectTransform> _conceptT1 = new List<RectTransform>(4);
        private int _posture = -1;           // 0 calm, 1 danger
        private float _dangerSeenAt = -10f;
        private float _lastHitAt = -10f;
        private HudEnso _enso;
        private Image _ammoArc;
        private Image _ammoArcTrack;
        private float _ammoArcFill = -1f;
        private float _ammoArcSize = -1f;
        private UiColor _ammoArcColour = UiColor.Transparent;

        /// <summary>Concept B's ensō (test / capture readout; null otherwise).</summary>
        public HudEnso Enso => _enso;

        // ================================================================ build

        private void BuildConcept(HudConcept concept)
        {
            if (concept == HudConcept.A)
            {
                BuildCornerGold();
                BuildSealRound();
                BuildBrushHealth();
                BuildRingWeapon();
            }
            else
            {
                BuildLedger();
                BuildSelfLineHealth();
                BuildSelfLineWeapon();
            }

            BuildInkSpecial();
        }

        // ---- A · top-left gold

        private void BuildCornerGold()
        {
            RectTransform root = InkRoot("Gold", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InkMargin, -InkMargin), new Vector2(160f, 32f));
            Image coin = InkGlyph("Coin", root, "Hud.Gold", UiColor.StateGold, 24f);
            Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            BuildInkGold(root, 32f, UiType.NumericM);
        }

        private void BuildInkGold(RectTransform root, float left, UiType type)
        {
            _goldCounter = UiCounter.Create("Value", root, UiCounterStyle.Currency, 0, type, UiColor.StateGold);
            RectTransform valueRect = UiFactory.Fill((RectTransform)_goldCounter.transform);
            valueRect.offsetMin = new Vector2(left, 0f);
            _goldValue = _goldCounter.ValueText;
            InkAll(_goldCounter.gameObject);

            _goldDelta = InkText("Delta", root, string.Empty, UiType.Caption, UiColor.StateGold, TextAnchor.MiddleLeft);
            _goldDelta.fontStyle = FontStyles.Bold;
            UiHudInk.Apply(_goldDelta);
            _goldDeltaHome = new Vector2(left + 64f, 0f);
            Place(_goldDelta.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), _goldDeltaHome, new Vector2(72f, UiFactory.LineHeight(UiType.Caption)));
            _goldDelta.canvasRenderer.SetAlpha(0f);
        }

        // ---- A · top-centre seal

        private void BuildSealRound()
        {
            const float seal = 56f;
            RectTransform root = InkRoot("Round", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(240f, seal));
            Sprite hanko = UiFxGlobals.InkMask(UiInkChannel.Hanko);
            Image stamp = UiFactory.NewImage("Seal", root, UiColor.HudInk, hanko != null ? hanko : UiSprites.Disc);
            Place(stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-36f, 0f), new Vector2(seal, seal));
            ContactShadow(stamp);

            _roundValue = InkText("RoundValue", stamp.rectTransform, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            UiFactory.Fill(_roundValue.rectTransform);

            Image enemies = InkGlyph("Enemies", root, "Hud.Enemies", UiColor.TextSecondary, 20f);
            Place(enemies.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(20f, 20f));
            _enemiesValue = InkText("EnemiesValue", root, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            Place(_enemiesValue.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(72f, 28f));

            RectTransform calm = InkRoot("RoundCalm", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f - seal - 6f), new Vector2(420f, 20f));
            _enemiesBreakdown = InkText("EnemiesBreakdown", calm, "—", UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
            UiFactory.Fill(_enemiesBreakdown.rectTransform);
            _conceptT2.Add(UiFactory.Group(calm.gameObject));
        }

        // ---- A · bottom-left health: heart, value, the brush stroke

        private void BuildBrushHealth()
        {
            RectTransform root = InkRoot("Health", Vector2.zero, Vector2.zero, new Vector2(InkMargin, InkMargin), new Vector2(380f, 96f));
            _conceptT1.Add(root);

            _healthIcon = InkGlyph("Heart", root, "Hud.Health", UiColor.BarHealth, 28f);
            Place(_healthIcon.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0f, 66f), new Vector2(28f, 28f));
            _healthValue = InkText("Value", root, "—", UiType.NumericL, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            Place(_healthValue.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(38f, 66f), new Vector2(72f, 40f));

            RectTransform maxCalm = UiFactory.NewRect("MaxCalm", root);
            Place(maxCalm, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(104f, 62f), new Vector2(64f, 24f));
            _healthMaxText = InkText("Max", maxCalm, string.Empty, UiType.NumericS, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Fill(_healthMaxText.rectTransform);
            _conceptT2.Add(UiFactory.Group(maxCalm.gameObject));

            _healthLowWord = InkText("Low", root, "LOW", UiType.Label, UiColor.StateDanger, TextAnchor.MiddleLeft);
            _healthLowWord.fontStyle = FontStyles.Bold;
            Place(_healthLowWord.rectTransform, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(170f, 66f), new Vector2(60f, 24f));
            _healthLowWord.gameObject.SetActive(false);

            // The stroke: ink rim, cream ghost track, lost-value ghost, fill; leaning toward the centre.
            RectTransform stroke = UiFactory.NewRect("Stroke", root);
            Place(stroke, Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0f, 32f), new Vector2(320f, 26f));   // the brush tapers: 26 px gives it body
            stroke.localRotation = Quaternion.Euler(0f, 0f, StrokeLean);
            Sprite brush = BrushSprite();
            Image rim = UiFactory.NewImage("Rim", stroke, UiColor.HudInk, brush);
            UiFactory.Outset(rim.rectTransform, 1.5f);
            ContactShadow(rim);
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

        // ---- A · bottom-right weapon: the lacquer ring, the count, the ladder on its brush

        private void BuildRingWeapon()
        {
            const float ring = 72f;
            RectTransform root = InkRoot("ClassicWeapon", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-InkMargin, InkMargin), new Vector2(420f, 150f));
            _conceptT1.Add(root);
            RectTransform host = UiFactory.NewRect("Content", root);
            UiFactory.Fill(host);
            _classicGroup = UiFactory.Group(host.gameObject);

            Vector2 ringCentre = new Vector2(-ring * 0.5f, ring * 0.5f + 6f);
            _reloadRing = UiRing.Create("ReloadRing", host, ring, UiColor.BarReload, 0.25f);
            Place((RectTransform)_reloadRing.transform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), ringCentre, new Vector2(ring, ring));
            Transform ghost = _reloadRing.transform.Find("Ghost");
            if (ghost != null)
            {
                ContactShadow(ghost.GetComponent<Image>());
            }

            _weaponIcon = InkGlyph("WeaponIcon", host, "Weapon.AssaultRifle", UiColor.TextPrimary, 44f);
            Place(_weaponIcon.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), ringCentre, new Vector2(44f, 44f));

            BuildInkAmmo(host, new Vector2(1f, 0f), -(ring + 16f), true, -StrokeLean);
            BuildInkWeaponCalm(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-(ring + 16f), 116f), TextAnchor.MiddleRight);
            BuildInkSuspended(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, ring + 18f), TextAnchor.MiddleRight);
        }

        /// <summary>
        /// The count ("24" NumericXL over "/ 30"), the ladder on an ink brush underline, the LOW / EMPTY word and the reload
        /// prompt. <paramref name="edgeX"/> is where the count ends (right-anchored) or starts (left-anchored).
        /// </summary>
        private void BuildInkAmmo(RectTransform host, Vector2 anchor, float edgeX, bool rightAligned, float lean)
        {
            const float countWidth = 170f;
            const float ladderWidth = 230f;
            Vector2 pivot = rightAligned ? new Vector2(1f, 0f) : new Vector2(0f, 0f);
            float ladderLeft = rightAligned ? edgeX - ladderWidth : edgeX;

            _ammoCounter = UiCounter.Create("Ammo", host, UiCounterStyle.Fraction, 0, UiType.NumericXL, UiColor.TextPrimary, 0);
            Place((RectTransform)_ammoCounter.transform, anchor, pivot, new Vector2(edgeX, 30f), new Vector2(countWidth, 48f));
            _ammoValue = _ammoCounter.ValueText;
            InkAll(_ammoCounter.gameObject);

            RectTransform under = UiFactory.NewRect("Underline", host);
            Place(under, anchor, new Vector2(0f, 0.5f), new Vector2(ladderLeft - 8f, 20f), new Vector2(ladderWidth + 16f, 14f));
            under.localRotation = Quaternion.Euler(0f, 0f, lean);
            Image brush = UiFactory.NewImage("Brush", under, UiColor.HudInk, BrushSprite());
            UiFactory.Fill(brush.rectTransform);
            ContactShadow(brush);

            RectTransform ladder = UiFactory.NewRect("Ladder", host);
            Place(ladder, anchor, new Vector2(0f, 0f), new Vector2(ladderLeft, 14f), new Vector2(ladderWidth, LadderHeight));
            ladder.localRotation = Quaternion.Euler(0f, 0f, lean);
            _ladderBack = LadderImage("Back", ladder, UiColor.TextMuted);
            _ladderFill = LadderImage("Fill", ladder, UiColor.TextPrimary);
            _ladderWidth = ladderWidth;

            float captionLeft = rightAligned ? edgeX - countWidth : edgeX;
            _reloadCaption = InkText("ReloadCaption", host, string.Empty, UiType.Label, UiColor.BarReload, TextAnchor.MiddleLeft);
            _reloadCaption.fontStyle = FontStyles.Bold;
            RectTransform captionRow = _reloadCaption.rectTransform;
            Place(captionRow, anchor, new Vector2(0f, 0.5f), new Vector2(captionLeft, 92f), new Vector2(220f, ReloadPromptSize + 6f));
            _reloadPrompt = UiInputGlyphs.Build("ReloadPrompt", captionRow, UiPromptAction.Reload, UiColor.TextPrimary, UiColor.SurfaceInset, ReloadPromptSize);
            Place((RectTransform)_reloadPrompt.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(Mathf.Ceil(_reloadCaption.GetPreferredValues("EMPTY").x) + (int)UiSpace.S, 0f), ((RectTransform)_reloadPrompt.transform).sizeDelta);
            _reloadPrompt.gameObject.SetActive(false);

            // The ring (or the reticle arc in B) carries the reload; the linear bar stays for the presenter, unseen.
            _reloadBar = UiFactory.Bar("ReloadBar", host, UiColor.BarReload, 8f);
            _reloadBar.gameObject.SetActive(false);
        }

        private void BuildInkWeaponCalm(RectTransform root, Vector2 anchor, Vector2 pivot, Vector2 position, TextAnchor align)
        {
            RectTransform calm = UiFactory.NewRect("WeaponCalm", root);
            Place(calm, anchor, pivot, position, new Vector2(320f, 34f));
            _classicName = InkText("Name", calm, "—", UiType.Label, UiColor.TextSecondary, align);
            Place(_classicName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(320f, 18f));
            _classicBuild = InkText("Build", calm, string.Empty, UiType.Caption, UiColor.TextMuted, align);
            Place(_classicBuild.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(320f, 16f));
            _conceptT2.Add(UiFactory.Group(calm.gameObject));
        }

        private void BuildInkSuspended(RectTransform root, Vector2 anchor, Vector2 pivot, Vector2 position, TextAnchor align)
        {
            TMP_Text suspended = InkText("Suspended", root, "SUSPENDED", UiType.Label, UiColor.StateWarning, align);
            suspended.fontStyle = FontStyles.Bold;
            Place(suspended.rectTransform, anchor, pivot, position, new Vector2(170f, 22f));
            _suspendedChip = suspended.gameObject;
            _suspendedChip.SetActive(false);
        }

        // ---- B · the ledger line, top-centre

        private void BuildLedger()
        {
            RectTransform root = InkRoot("Ledger", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(330f, 30f));
            _roundValue = LedgerSlot(root, -165f, "Hud.Round", UiColor.TextSecondary);
            _enemiesValue = LedgerSlot(root, -55f, "Hud.Enemies", UiColor.TextSecondary);
            Image coin = InkGlyph("Coin", root, "Hud.Gold", UiColor.StateGold, 20f);
            Place(coin.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(55f, 0f), new Vector2(20f, 20f));
            RectTransform gold = UiFactory.NewRect("GoldSlot", root);
            Place(gold, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(55f, 0f), new Vector2(110f, 30f));
            BuildInkGold(gold, 26f, UiType.NumericM);

            RectTransform calm = InkRoot("LedgerCalm", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(420f, 20f));
            _enemiesBreakdown = InkText("EnemiesBreakdown", calm, "—", UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
            UiFactory.Fill(_enemiesBreakdown.rectTransform);
            _conceptT2.Add(UiFactory.Group(calm.gameObject));
        }

        private TMP_Text LedgerSlot(RectTransform root, float x, string glyph, UiColor glyphColour)
        {
            Image icon = InkGlyph(glyph, root, glyph, glyphColour, 20f);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(20f, 20f));
            TMP_Text value = InkText(glyph + ".Value", root, "—", UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            Place(value.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x + 26f, 0f), new Vector2(76f, 30f));
            return value;
        }

        // ---- B · the self-line: health left of the Special, ammo right

        private void BuildSelfLineHealth()
        {
            RectTransform root = InkRoot("Health", new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-124f, 36f), new Vector2(280f, 72f));
            _conceptT1.Add(root);

            RectTransform maxCalm = UiFactory.NewRect("MaxCalm", root);
            Place(maxCalm, new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 38f), new Vector2(56f, 24f));
            _healthMaxText = InkText("Max", maxCalm, string.Empty, UiType.NumericS, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Fill(_healthMaxText.rectTransform);
            _conceptT2.Add(UiFactory.Group(maxCalm.gameObject));

            _healthValue = InkText("Value", root, "—", UiType.NumericL, UiColor.TextPrimary, TextAnchor.MiddleRight);
            Place(_healthValue.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-60f, 42f), new Vector2(80f, 40f));
            _healthIcon = InkGlyph("Heart", root, "Hud.Health", UiColor.BarHealth, 28f);
            Place(_healthIcon.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-146f, 42f), new Vector2(28f, 28f));
            _healthLowWord = InkText("Low", root, "LOW", UiType.Label, UiColor.StateDanger, TextAnchor.MiddleRight);
            _healthLowWord.fontStyle = FontStyles.Bold;
            Place(_healthLowWord.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(-182f, 42f), new Vector2(60f, 24f));
            _healthLowWord.gameObject.SetActive(false);

            _healthState = InkText("State", root, string.Empty, UiType.Caption, UiColor.AccentMoss, TextAnchor.MiddleRight);
            Place(_healthState.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(280f, 18f));

            // The fraction is the ensō's (BuildConceptPlayerLayer); this bar keeps the presenter's contract, unseen.
            _healthBar = UiFactory.Bar("PresenterBar", root, UiColor.BarHealth, 4f);
            _healthBar.gameObject.SetActive(false);
        }

        private void BuildSelfLineWeapon()
        {
            const float ring = 56f;
            RectTransform root = InkRoot("ClassicWeapon", new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(124f, 36f), new Vector2(420f, 150f));
            _conceptT1.Add(root);
            RectTransform host = UiFactory.NewRect("Content", root);
            UiFactory.Fill(host);
            _classicGroup = UiFactory.Group(host.gameObject);

            Vector2 ringCentre = new Vector2(ring * 0.5f, ring * 0.5f + 10f);
            _reloadRing = UiRing.Create("ReloadRing", host, ring, UiColor.BarReload, 0.25f);
            Place((RectTransform)_reloadRing.transform, Vector2.zero, new Vector2(0.5f, 0.5f), ringCentre, new Vector2(ring, ring));
            Transform ghost = _reloadRing.transform.Find("Ghost");
            if (ghost != null)
            {
                ContactShadow(ghost.GetComponent<Image>());
            }

            _weaponIcon = InkGlyph("WeaponIcon", host, "Weapon.AssaultRifle", UiColor.TextPrimary, 34f);
            Place(_weaponIcon.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), ringCentre, new Vector2(34f, 34f));

            BuildInkAmmo(host, Vector2.zero, ring + 14f, false, -StrokeLean);
            BuildInkWeaponCalm(root, Vector2.zero, Vector2.zero, new Vector2(ring + 14f, 116f), TextAnchor.MiddleLeft);
            BuildInkSuspended(root, Vector2.zero, Vector2.zero, new Vector2(0f, ring + 22f), TextAnchor.MiddleLeft);
        }

        // ---- both · the Special, boxless at bottom centre

        private void BuildInkSpecial()
        {
            RectTransform special = InkRoot("SpecialWeapon", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(SpecialBlockWidth, SpecialBlockHeight));
            special.gameObject.AddComponent<Canvas>();
            _specialBlock = special.gameObject;
            BuildSpecialContent(special, true);
            _specialBlock.SetActive(false);
        }

        // ---- both · threat chips without a box: outer glyph + word in their own ink, a brush underline in the state colour

        private void BuildInkThreatChips()
        {
            float top = Concept == HudConcept.A ? 110f : 96f;
            RectTransform row = InkRoot("ThreatRow", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -top), new Vector2(600f, 30f));
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

        // ---- B · the ensō and the reticle's ammo arc

        private void BuildConceptPlayerLayer(HudConcept concept)
        {
            if (concept != HudConcept.B || _reticle == null)
            {
                return;
            }

            var layer = (RectTransform)_reticle.Root.parent;
            _enso = new HudEnso(layer);
            _enso.Root.SetAsFirstSibling();   // under the reticle and the 8b arcs
            _enso.Place(OffScreen);

            _ammoArcTrack = UiFactory.NewImage("AmmoTrack", _reticle.Root, UiColor.TextPrimary, UiSprites.ReticleArc270);
            _ammoArcTrack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            Fade(_ammoArcTrack, 0.2f);
            _ammoArc = UiFactory.NewImage("AmmoArc", _reticle.Root, UiColor.TextPrimary, UiSprites.ReticleArc270);
            _ammoArc.canvasRenderer.SetAlpha(AmmoArcAlpha);   // GD 8-R: 2 px at 0.7 - on the target, never over it
            _ammoArc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            _ammoArc.type = Image.Type.Filled;
            _ammoArc.fillMethod = Image.FillMethod.Radial360;
            _ammoArc.fillOrigin = (int)Image.Origin360.Bottom;
            _ammoArc.fillClockwise = true;
        }

        /// <summary>B: the magazine as a 270° arc riding outside the honest ring (Data, direct write); the reload fills it Focus400.</summary>
        private void UpdateAmmoArc(in ClassicWeaponHudState weapon)
        {
            if (_ammoArc == null)
            {
                return;
            }

            float share;
            UiColor colour;
            if (weapon.IsReloading)
            {
                share = weapon.ReloadProgress01;
                colour = UiColor.BarReload;
            }
            else
            {
                share = weapon.MagazineSize > 0 ? (float)weapon.AmmoInMagazine / weapon.MagazineSize : 0f;
                colour = _model.AmmoLow || (weapon.MagazineSize > 0 && weapon.AmmoInMagazine <= 0) ? UiColor.StateWarning : UiColor.TextPrimary;
            }

            float fill = (45f + 270f * Mathf.Clamp01(share)) / 360f;
            if (Mathf.Abs(fill - _ammoArcFill) >= 1f / 512f)
            {
                _ammoArcFill = fill;
                _ammoArc.fillAmount = fill;
            }

            if (colour != _ammoArcColour)
            {
                _ammoArcColour = colour;
                _ammoArc.color = UiTokens.Get(colour);
            }

            float size = Mathf.Round(Mathf.Max(68f, (_reticle.Radius + HudReticle.TickLength + 12f) * 2f));
            if (!Mathf.Approximately(size, _ammoArcSize))
            {
                _ammoArcSize = size;
                _ammoArc.rectTransform.sizeDelta = new Vector2(size, size);
                _ammoArcTrack.rectTransform.sizeDelta = new Vector2(size, size);
            }
        }

        // ================================================================ posture

        /// <summary>Calm ↔ danger (ID 8-R): the T2 lines and the T1 clusters' scale, only on a change (≤ 1 per 0.8 s out).</summary>
        private void UpdateConceptPosture()
        {
            if (_conceptT1.Count == 0 && _conceptT2.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool danger = ThreatChipVisible(0) || ThreatChipVisible(1) || ThreatChipVisible(2) || _model.HealthLow || _model.AmmoLow ||
                          _model.ReloadState == RunHudReloadState.MagazineEmpty || now - _lastHitAt < HitDangerSeconds;
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
            for (int i = 0; i < _conceptT2.Count; i++)
            {
                UiMotion.Alpha(_conceptT2[i], posture == 1 ? 0f : 1f, posture == 1 ? UiDuration.Fast : UiDuration.Base,
                    posture == 1 ? UiEase.Exit : UiEase.Settle, UiMotionClass.Decor);
            }

            for (int i = 0; i < _conceptT1.Count; i++)
            {
                UiMotion.Scale(_conceptT1[i], posture == 1 ? 1f : CalmScale, posture == 1 ? UiDuration.Base : UiDuration.Emphasis,
                    posture == 1 ? UiEase.Settle : UiEase.SettleLong, UiMotionClass.Decor);
            }
        }

        // ================================================================ helpers

        private RectTransform InkRoot(string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = UiFactory.NewRect(name, ScreenRect);
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

        /// <summary>A contact shadow: the same sprite in HudInk at 0.35, 2 px right and 3 px down, drawn just before it.</summary>
        private static Image ContactShadow(Image of)
        {
            if (of == null)
            {
                return null;
            }

            Image shadow = UiFactory.NewImage(of.name + ".Shadow", of.transform.parent, UiColor.HudInk, of.sprite);
            shadow.type = of.type;
            shadow.preserveAspect = of.preserveAspect;
            RectTransform src = of.rectTransform, dst = shadow.rectTransform;
            dst.anchorMin = src.anchorMin;
            dst.anchorMax = src.anchorMax;
            dst.pivot = src.pivot;
            dst.offsetMin = src.offsetMin + ContactShadowOffset;
            dst.offsetMax = src.offsetMax + ContactShadowOffset;
            dst.localRotation = src.localRotation;
            Fade(shadow, ContactShadowAlpha);
            shadow.transform.SetSiblingIndex(of.transform.GetSiblingIndex());
            return shadow;
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
