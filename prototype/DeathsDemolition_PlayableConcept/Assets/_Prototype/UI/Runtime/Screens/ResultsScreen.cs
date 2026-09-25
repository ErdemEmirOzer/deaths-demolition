using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Profile;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Run Results v3 (WP-068 R1-D, direction E26). One screen, no scrolling, no Claim button.
    ///
    /// LAYOUT (1920 × 1080 reference, the 1408 page rule at Mac):
    ///   hero band      - kicker, the ending as the H1 ("DEFEATED IN ROUND 7"), the loadout line; NEW BEST as a sakura
    ///                    ribbon at the band's right, the last and largest beat
    ///   hero tiles     - Round · Kills · Gold · Time, four NumericXL counters that roll up in turn
    ///   RUN REPORT     - 7 columns: the weapon row (identity glyph, name, build), COMBAT and RUN rows side by side, the
    ///                    kill-XP note at the foot
    ///   REWARDS        - 5 columns: EXPERIENCE (+XP, breakdown, level line, the XP bar revealed before → after, LEVEL UP
    ///                    stamp), PATH +Ash, then the unlocks and achievements that fit - measured, the rest summarised
    ///   footer         - the save note and MAIN MENU, the one Primary
    ///
    /// CHOREOGRAPHY (≤ 2.2 s after the curtain opens, E26): the three blocks stagger in, the tiles count up 120 ms apart,
    /// the XP bar reveals, LEVEL UP stamps, each unlock / achievement stamps 120 ms apart, NEW BEST last. Every beat
    /// raises its UiEvent cue. The first Submit or click skips to the end state; MAIN MENU takes the focus at the end, so
    /// the second press leaves. Reduced Motion shows the end state at once.
    ///
    /// EVERYTHING IS A SNAPSHOT: the screen reads <see cref="ProfileService.LastReport"/> once through
    /// <see cref="ResultsViewModel"/>; the profile was written before this scene existed, so nothing here can write it,
    /// and closing the screen early changes nothing that was earned.
    /// </summary>
    public sealed class ResultsScreen : UiScreen
    {
        private const float RowHeight = 40f;
        private const float RewardEntryHeight = 92f; // AD v004 MUST-5: two-line detail, no truncation (24 title + 44 detail + 2·12)
        private const float RewardTitleBand = 24f;
        private const float RewardDetailBand = 44f;
        private const float FooterHeight = 88f;
        private const float ReportShare = 0.58f;     // 7 of 12 columns (+ half a gutter)
        private const float WeaponRowHeight = 52f;

        /// <summary>Spacing between choreography beats (E26: 120 ms).</summary>
        public const float CelebrationBeatSeconds = 0.12f;

        private ResultsViewModel _model;
        private UiButton _continueButton;
        private readonly UiCounter[] _tiles = new UiCounter[4];
        private TMP_Text _timeValue;
        private UiBar _xpBar;
        private RectTransform _levelUp;
        private CanvasGroup _levelUpGroup;
        private RectTransform _newBest;
        private CanvasGroup _newBestGroup;
        private readonly List<RectTransform> _rewardCards = new List<RectTransform>(6);
        private readonly List<CanvasGroup> _rewardGroups = new List<CanvasGroup>(6);
        private readonly List<bool> _rewardIsAchievement = new List<bool>(6);
        private Coroutine _choreography;
        private bool _playing;
        private bool _newBestRound;

        /// <summary>What the screen drew. Exposed for tests; it is never rebuilt.</summary>
        public ResultsViewModel Model => _model;

        /// <summary>True while the choreography plays (tests, captures).</summary>
        public bool Playing => _playing;

        /// <summary>The hero tile counters: Round, Kills, Gold, Time (tests).</summary>
        public UiCounter Tile(int i) => _tiles[i];

        protected override UiContext BaseContext => UiContext.RunResults;

        protected override void Build()
        {
            _model = ResultsViewModel.From(ProfileService.LastReport);
            // WP-022B: the WP-023 results record adds what the receipt cannot know - a new
            // best round, what killed the player, achievement rarity (medallion ring).
            _model.ApplyResultsData(ProfileService.ResultsSource != null ? ProfileService.ResultsSource.Current : null);

            if (!_model.HasRun)
            {
                BuildEmptyState();
                return;
            }

            // The ending is the title; "RUN RESULTS" (and the name plate) the kicker; the loadout the line under it.
            string ending = string.IsNullOrEmpty(_model.KilledBy)
                ? _model.Eyebrow
                : _model.Eyebrow + "  ·  " + UiFactory.Clip(_model.KilledBy, 32).ToUpperInvariant();
            string plate = CosmeticsViewModel.NamePlate(ProfileService.CosmeticsSource);
            string eyebrow = string.IsNullOrEmpty(plate) ? ResultsViewModel.Title : ResultsViewModel.Title + "  ·  " + plate.ToUpperInvariant();
            float headerHeight = BuildHeader(eyebrow, ending, _model.Loadout);
            BuildNewBest();

            float top = headerHeight + (int)UiSpace.XL;
            RectTransform tiles = BuildTiles(top, out float tilesHeight);
            top += tilesHeight + (int)UiSpace.XL;

            RectTransform report = Column("Report", 0f, ReportShare, top, FooterHeight);
            RectTransform rewards = Column("Rewards", ReportShare + 0.02f, 1f, top, FooterHeight);
            // E26: the blocks take their content's height (one height for both, capped by the room) - no empty card. The
            // reward cards are fitted against the same capped room, so none is ever clipped by the cap.
            float room = Content.rect.height - top - FooterHeight - (int)UiSpace.XL;
            float reportHeight = BuildReport(report);
            float rewardsHeight = BuildRewards(rewards, room);

            float blocks = Mathf.Min(room, Mathf.Max(reportHeight, rewardsHeight));
            TopBand(report, top, blocks);
            TopBand(rewards, top, blocks);

            // The three blocks land in reading order (60 ms apart) after the screen fade; reduced motion → at rest.
            gameObject.AddComponent<UiStaggerIn>().Configure(new[]
            {
                UiFactory.Group(tiles.gameObject),
                UiFactory.Group(report.gameObject),
                UiFactory.Group(rewards.gameObject)
            });

            BuildFooter(false);
        }

        protected override void Start()
        {
            base.Start();
            if (_model != null && _model.HasRun)
            {
                _choreography = StartCoroutine(Choreography());
            }
        }

        // ============================================================ hero tiles

        private RectTransform BuildTiles(float top, out float height)
        {
            RectTransform row = UiFactory.NewRect("Tiles", Content);
            string[] keys = { "results.tile.round", "results.tile.kills", "results.tile.gold", "results.tile.time" };
            string[] words = { "ROUND", "KILLS", "GOLD", "TIME" };
            float tileHeight = UiHeroTiles.Height;
            for (int i = 0; i < _tiles.Length; i++)
            {
                _tiles[i] = UiHeroTiles.Create("Tile." + words[i], row, UiStrings.Get(keys[i], words[i]), 0);
                var tile = (RectTransform)_tiles[i].transform.parent.parent;
                tile.anchorMin = new Vector2(i / 4f, 0f);
                tile.anchorMax = new Vector2((i + 1) / 4f, 1f);
                tile.pivot = new Vector2(0.5f, 0.5f);
                tile.offsetMin = new Vector2(i == 0 ? 0f : (int)UiSpace.M * 0.5f, 0f);
                tile.offsetMax = new Vector2(i == 3 ? 0f : -(int)UiSpace.M * 0.5f, 0f);

                // Measured (the R1-B lesson): the numeral and its caption keep their lines at TextScale 1.25.
                TMP_Text caption = tile.GetComponentsInChildren<TMP_Text>(true)[tile.GetComponentsInChildren<TMP_Text>(true).Length - 1];
                float need = LineOf(_tiles[i].ValueText) + LineOf(caption) + 2f * (int)UiSpace.L + (int)UiSpace.XS;
                tileHeight = Mathf.Max(tileHeight, Mathf.Ceil(need));
            }

            // Time reads m:ss: the counter shows the clock text itself (a timer does not roll).
            _timeValue = _tiles[3].ValueText;
            _timeValue.text = "0:00";

            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0f, -(top + tileHeight));
            row.offsetMax = new Vector2(0f, -top);
            height = tileHeight;
            return row;
        }

        /// <summary>NEW BEST (E26): a sakura ribbon at the hero band's right, hidden until the last beat.</summary>
        private void BuildNewBest()
        {
            if (string.IsNullOrEmpty(_model.BestRoundChip))
            {
                return;
            }

            _newBestRound = true;
            string word = UiStrings.Get("results.new_best", "NEW BEST · ROUND {value}").Replace("{value}", _model.HeroRound.ToString());
            TMP_Text text = UiChips.Create("NewBest", Content, UiChipKind.New, word, UiChipSize.Card);
            _newBest = (RectTransform)text.transform.parent;
            _newBest.anchorMin = new Vector2(1f, 1f);
            _newBest.anchorMax = new Vector2(1f, 1f);
            _newBest.pivot = new Vector2(1f, 1f);
            _newBest.anchoredPosition = new Vector2(0f, -(LineOf(text) + (int)UiSpace.S));
            _newBestGroup = UiFactory.Group(_newBest.gameObject);
            _newBestGroup.alpha = 0f;
        }

        // ============================================================ run report

        private float BuildReport(RectTransform column)
        {
            RectTransform card = UiFactory.FramedCard("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            UiFactory.Fill(card);
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XL));   // AD R1-D SHOULD: one padding, one header baseline with REWARDS
            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading(UiStrings.Get("results.report", "RUN REPORT"), host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.L);

            // The weapon the run was fought with: its identity glyph, its name, its build.
            RectTransform weapon = UiFactory.NewRect("Weapon", host);
            stack.Add(weapon, UiFactory.ScaledBand(WeaponRowHeight));
            Image glyph = UiFactory.NewImage("Glyph", weapon, UiGlyphTints.Weapon(_model.Weapon), UiSprites.Disc);
            Sprite icon = UiArt.WeaponIcon(_model.Weapon);
            if (icon != null)
            {
                UiArt.SetGlyph(glyph, icon);
                if (UiArt.IsGlyph(glyph.sprite))
                {
                    UiArt.ApplyGlyph(glyph, new Vector3(0.25f, 0f, 0f));
                }
            }

            glyph.preserveAspect = true;
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.anchorMin = new Vector2(0f, 0.5f);
            glyphRect.anchorMax = new Vector2(0f, 0.5f);
            glyphRect.pivot = new Vector2(0f, 0.5f);
            glyphRect.sizeDelta = new Vector2(WeaponRowHeight, WeaponRowHeight);
            glyphRect.anchoredPosition = Vector2.zero;
            TMP_Text name = UiFactory.Label("Name", weapon, _model.WeaponName.ToUpperInvariant(), UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.LowerLeft);
            RectTransform nameRect = UiFactory.Fill((RectTransform)name.transform);
            nameRect.offsetMin = new Vector2(WeaponRowHeight + (int)UiSpace.L, UiFactory.ScaledBand(WeaponRowHeight) * 0.5f);
            TMP_Text build = UiFactory.Label("Build", weapon, _model.BuildLine, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform buildRect = UiFactory.Fill((RectTransform)build.transform);
            buildRect.offsetMin = new Vector2(WeaponRowHeight + (int)UiSpace.L, 0f);
            buildRect.offsetMax = new Vector2(0f, -UiFactory.ScaledBand(WeaponRowHeight) * 0.5f);

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            // COMBAT and RUN side by side: the report is wide, so the rows sit in two columns rather than one tall list.
            RectTransform rows = UiFactory.NewRect("Rows", host);
            int lines = Mathf.Max(_model.Combat.Count, _model.Economy.Count);
            float rowsHeight = UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS + lines * UiFactory.ScaledBand(RowHeight);
            stack.Add(rows, rowsHeight);
            BuildRowColumn(rows, 0f, 0.48f, UiStrings.Get("results.combat", "COMBAT"), _model.Combat, null);
            BuildRowColumn(rows, 0.52f, 1f, UiStrings.Get("results.run", "RUN"), _model.Economy, UiType.Body);
            stack.Gap(UiSpace.M);

            TMP_Text note = UiFactory.Label("Note", host, _model.CombatNote, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption)) * 2f);
            return stack.Cursor + 2f * (int)UiSpace.XL;
        }

        private static void BuildRowColumn(RectTransform parent, float from, float to, string label, List<UiStatLine> lines, UiType? type)
        {
            RectTransform column = UiFactory.NewRect(label, parent);
            column.anchorMin = new Vector2(from, 0f);
            column.anchorMax = new Vector2(to, 1f);
            column.offsetMin = Vector2.zero;
            column.offsetMax = Vector2.zero;
            var stack = new UiStack(column);
            TMP_Text heading = UiFactory.Label("Label", column, label, UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)heading.transform, UiFactory.LineHeight(UiType.Label));
            stack.Gap(UiSpace.XS);
            for (int i = 0; i < lines.Count; i++)
            {
                AddRow(ref stack, column, lines[i], type);
            }
        }

        // ============================================================== rewards

        private float BuildRewards(RectTransform column, float blockRoom)
        {
            RectTransform card = UiFactory.FramedCard("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            UiFactory.Fill(card);
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XL));
            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading(UiStrings.Get("results.rewards", "REWARDS"), host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.M);

            // EXPERIENCE: the label left, the total (sakura, NumericL) right, the breakdown under it.
            RectTransform xpRow = UiFactory.NewRect("Xp", host);
            float xpLine = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.NumericL));
            stack.Add(xpRow, xpLine);
            TMP_Text xpLabel = UiFactory.Label("Label", xpRow, UiStrings.Get("results.xp", "EXPERIENCE"), UiType.Label, UiColor.TextMuted, TextAnchor.MiddleLeft);
            UiFactory.Fill((RectTransform)xpLabel.transform);
            // AD R1-D MUST-1 (§2.1): sakura is NEW / NEW BEST only - the total prints cream, its unit quieter.
            TMP_Text xpUnit = UiFactory.Label("Unit", xpRow, "XP", UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
            RectTransform unitRect = UiFactory.Fill((RectTransform)xpUnit.transform);
            float unitWidth = Mathf.Ceil(xpUnit.GetPreferredValues("XP").x);
            unitRect.offsetMin = new Vector2(-unitWidth, 0f);
            unitRect.anchorMin = new Vector2(1f, 0f);
            TMP_Text xpValue = UiFactory.Numeric("Total", xpRow, _model.TotalXp, UiType.NumericL, UiColor.TextPrimary, TextAnchor.MiddleRight, false);
            RectTransform valueRect = UiFactory.Fill((RectTransform)xpValue.transform);
            valueRect.offsetMax = new Vector2(-(unitWidth + (int)UiSpace.S), 0f);
            for (int i = 0; i < _model.XpBreakdown.Count; i++)
            {
                TMP_Text value = UiFactory.StatRow(_model.XpBreakdown[i].Label, _model.XpBreakdown[i].Value, host, _model.XpBreakdown[i].Color, UiType.Body, 120f);
                stack.Add((RectTransform)value.transform.parent, UiFactory.ScaledBand(32f));
            }

            stack.Gap(UiSpace.M);

            // The level line, LEVEL UP at its right (stamped in its beat), the bar revealed before → after.
            RectTransform levelRow = UiFactory.NewRect("Level", host);
            stack.Add(levelRow, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.H3)));
            TMP_Text level = UiFactory.Numeric("Line", levelRow, _model.LevelLine, UiType.H3, UiColor.TextPrimary, TextAnchor.MiddleLeft, false);
            UiFactory.Fill((RectTransform)level.transform);
            if (_model.LeveledUp)
            {
                // LEVEL UP stamps in cream / ink, like PURCHASED (AD R1-D MUST-1).
                TMP_Text chip = UiFactory.Chip("LevelUp", levelRow, _model.LevelUpChip, UiColor.TextPrimary, UiColor.SurfaceBase, 180f, 30f);
                _levelUp = (RectTransform)chip.transform.parent;
                _levelUp.anchorMin = new Vector2(1f, 0.5f);
                _levelUp.anchorMax = new Vector2(1f, 0.5f);
                _levelUp.pivot = new Vector2(1f, 0.5f);
                _levelUp.anchoredPosition = Vector2.zero;
                _levelUpGroup = UiFactory.Group(_levelUp.gameObject);
                _levelUpGroup.alpha = 0f;
            }

            stack.Gap(UiSpace.S);
            // AD R1-D MUST-1: BarXp is Sakura400 - on Results sakura belongs to NEW / NEW BEST, so the bar fills cream.
            _xpBar = UiFactory.Bar("XpBar", host, UiColor.TextPrimary, 14f);
            _xpBar.EnableGhost();
            _xpBar.SetValue(_model.ProgressBefore01 >= 0f ? _model.ProgressBefore01 : 0f, true);
            stack.Add((RectTransform)_xpBar.transform, 14f);
            stack.Gap(UiSpace.XS);
            TMP_Text barCaption = UiFactory.Label("BarCaption", host, _model.ProgressCaption, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)barCaption.transform, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption)));
            if (!string.IsNullOrEmpty(_model.NextUnlockLine))
            {
                // E26's forward look (12b, AD R1-D SHOULD-1): what the next level brings, in the empty state too.
                TMP_Text next = UiFactory.Label("NextUnlock", host, _model.NextUnlockLine, UiType.Caption, UiColor.TextSecondary);
                stack.Add((RectTransform)next.transform, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption)));
            }

            if (_model.AshEarned > 0)
            {
                stack.Gap(UiSpace.S);
                TMP_Text ash = UiFactory.Label("Path", host,
                    UiStrings.Get("path.results.delta", "PATH +{value} Ash").Replace("{value}", _model.AshEarned.ToString()), UiType.Label, UiColor.TextPrimary);
                stack.Add((RectTransform)ash.transform, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Label)));
            }

            stack.Gap(UiSpace.M);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            // Unlocks first, then achievements, as many cards as the column holds (measured); the rest summarised.
            float entry = UiFactory.ScaledBand(RewardEntryHeight);
            float more = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption));
            float room = blockRoom - 2f * (int)UiSpace.XL - stack.Cursor;
            int total = _model.NewUnlocks.Count + _model.NewAchievements.Count;
            if (total == 0)
            {
                TMP_Text empty = UiFactory.Label("Empty", host, UiStrings.Get("results.empty", "No new unlocks this run."), UiType.Body, UiColor.TextMuted);
                stack.Add((RectTransform)empty.transform, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Body)));
                return stack.Cursor + 2f * (int)UiSpace.XL;
            }

            int fits = Mathf.Max(0, Mathf.FloorToInt((room - more) / (entry + (int)UiSpace.S)));
            int shown = 0;
            for (int i = 0; i < _model.NewUnlocks.Count && shown < fits; i++, shown++)
            {
                AddReward(ref stack, host, _model.NewUnlocks[i], UiColor.AccentSakura, false, entry);
            }

            for (int i = 0; i < _model.NewAchievements.Count && shown < fits; i++, shown++)
            {
                AddReward(ref stack, host, _model.NewAchievements[i], UiColor.StateSuccess, true, entry);
            }

            if (total > shown)
            {
                // No card fits (a short screen at Mac): one line names them instead of a card cut by the block.
                string line = shown == 0
                    ? UiStrings.Get("results.unlocked_count", "{value} new, already added to your profile").Replace("{value}", total.ToString())
                    : ResultsViewModel.Overflow(total - shown + ResultsViewModel.MaxRewardEntries);
                TMP_Text rest = UiFactory.Label("More", host, line, UiType.Caption, UiColor.TextSecondary);
                stack.Add((RectTransform)rest.transform, more);
            }

            return stack.Cursor + 2f * (int)UiSpace.XL;
        }

        private void AddReward(ref UiStack stack, RectTransform host, ProfileUnlockEntry entry, UiColor accent, bool achievement, float height)
        {
            RectTransform card = BuildRewardEntry(host, entry, accent);
            stack.Add(card, height);
            stack.Gap(UiSpace.S);
            CanvasGroup group = UiFactory.Group(card.gameObject);
            group.alpha = 0f;
            _rewardCards.Add(card);
            _rewardGroups.Add(group);
            _rewardIsAchievement.Add(achievement);
        }

        /// <summary>
        /// A new unlock or achievement. Raised surface, accent border, the medallion or unlock glyph and a pulsing NEW
        /// pill - the strongest treatment on the screen.
        /// </summary>
        private RectTransform BuildRewardEntry(RectTransform host, ProfileUnlockEntry entry, UiColor accent)
        {
            RectTransform card = UiFactory.Card("Reward", host, accent, UiColor.SurfaceRaised);
            RectTransform content = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.M));

            float markSize = RewardEntryHeight - (int)UiSpace.M * 2;
            string typeLabel = string.Empty;
            Sprite markSprite = accent == UiColor.StateSuccess
                ? UiArt.Medal(_model.MedalFor(entry.Title))
                : _model.UnlockSpriteFor(entry.Title, out typeLabel);
            Image mark = UiFactory.NewImage("Mark", content, accent, markSprite);
            mark.preserveAspect = true;
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0f, 0.5f);
            markRect.sizeDelta = new Vector2(markSize, markSize);
            markRect.anchoredPosition = Vector2.zero;

            float textLeft = markSize + (int)UiSpace.M;
            string titleText = entry.Title;
            if (!string.IsNullOrEmpty(typeLabel))
            {
                int colon = titleText.IndexOf(": ", System.StringComparison.Ordinal);
                if (colon > 0 && colon < 12)
                {
                    titleText = titleText.Substring(colon + 2);
                }
            }

            TMP_Text title = UiFactory.Label("Title", content, UiFactory.Clip(titleText, 32), UiType.BodyStrong, accent, TextAnchor.LowerLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMin = new Vector2(textLeft, UiFactory.ScaledBand(RewardDetailBand));
            titleRect.offsetMax = new Vector2(-64f, 0f);

            string detailText = string.IsNullOrEmpty(typeLabel) ? entry.Detail : typeLabel + "  ·  " + entry.Detail;
            TMP_Text detail = UiFactory.Label("Detail", content, UiFactory.Clip(detailText, 120), UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
            detailRect.offsetMin = new Vector2(textLeft, 0f);
            detailRect.offsetMax = new Vector2(-64f, -UiFactory.ScaledBand(RewardTitleBand));

            // AD v004 MUST-1: NEW is one token everywhere - ink on sakura - whatever the card's accent.
            TMP_Text badge = UiFactory.Chip("New", content, "NEW", UiColor.AccentSakura, UiColor.SurfaceBase, 56f, 24f);
            var badgeRect = (RectTransform)badge.transform.parent;
            badgeRect.anchorMin = new Vector2(1f, 0.5f);
            badgeRect.anchorMax = new Vector2(1f, 0.5f);
            badgeRect.pivot = new Vector2(1f, 0.5f);
            badgeRect.anchoredPosition = Vector2.zero;
            badgeRect.gameObject.AddComponent<UiPulse>().Configure(
                new Graphic[] { badgeRect.GetComponent<Graphic>() }, 0.80f, 1f, UiTokens.PulsePeriod);
            return card;
        }

        // ========================================================= choreography

        /// <summary>
        /// E26: the beats in reading order, each raising its cue; ≤ 2.2 s. The first Submit / click skips (Update), the
        /// end state is the same either way.
        /// </summary>
        private IEnumerator Choreography()
        {
            _playing = true;
            yield return UiCurtain.WaitRevealed(); // the beats play on screen, not behind the curtain
            if (UiMotion.ReducedMotion)
            {
                Finish();
                RaiseCues();
                yield break;
            }

            var beat = new WaitForSecondsRealtime(CelebrationBeatSeconds);
            UiSound.Play(UiSoundKind.Reveal);
            int[] values = { _model.HeroRound, _model.HeroKills, _model.HeroGold };
            for (int i = 0; i < 3; i++)
            {
                _tiles[i].RollTo(values[i]);
                yield return beat;
            }

            _timeValue.text = ResultsViewModel.FormatDuration(_model.HeroSeconds);
            UiPresets.StampIn((RectTransform)_timeValue.transform);
            yield return beat;

            _xpBar.SetValue(_model.ProgressAfter01);
            if (_levelUp != null)
            {
                yield return beat;
                _levelUpGroup.alpha = 1f;
                UiPresets.StampIn(_levelUp);
                UiEvents.Raise(UiEvent.LevelUp);
            }

            yield return new WaitForSecondsRealtime(0.3f);
            bool unlockCue = false;
            bool achievementCue = false;
            for (int i = 0; i < _rewardCards.Count; i++)
            {
                _rewardGroups[i].alpha = 1f;
                UiPresets.StampIn(_rewardCards[i]);
                if (_rewardIsAchievement[i] && !achievementCue)
                {
                    achievementCue = true;
                    UiEvents.Raise(UiEvent.AchievementRevealed);
                }
                else if (!_rewardIsAchievement[i] && !unlockCue)
                {
                    unlockCue = true;
                    UiEvents.Raise(UiEvent.UnlockRevealed);
                }

                yield return beat;
            }

            if (_newBest != null)
            {
                yield return new WaitForSecondsRealtime(0.25f);   // UiNewBest ≥ 250 ms after the last stamp
                _newBestGroup.alpha = 1f;
                UiPresets.StampIn(_newBest);
                UiEvents.Raise(UiEvent.NewBestRound);
            }

            Finish();
        }

        /// <summary>The end state: every value final, every stamp shown, MAIN MENU focused (the second press leaves).</summary>
        private void Finish()
        {
            _playing = false;
            _tiles[0].SetNow(_model.HeroRound);
            _tiles[1].SetNow(_model.HeroKills);
            _tiles[2].SetNow(_model.HeroGold);
            _timeValue.text = ResultsViewModel.FormatDuration(_model.HeroSeconds);
            _xpBar.SetValue(_model.ProgressAfter01, UiMotion.ReducedMotion);
            if (_levelUpGroup != null)
            {
                _levelUpGroup.alpha = 1f;
            }

            for (int i = 0; i < _rewardGroups.Count; i++)
            {
                _rewardGroups[i].alpha = 1f;
            }

            if (_newBestGroup != null)
            {
                _newBestGroup.alpha = 1f;
            }

            if (_continueButton != null)
            {
                UiInput.Focus(_continueButton);
            }
        }

        /// <summary>Reduced Motion: the cues still name what happened, in reading order, without the timing.</summary>
        private void RaiseCues()
        {
            if (_model.LeveledUp)
            {
                UiEvents.Raise(UiEvent.LevelUp);
            }

            if (_model.NewUnlocks.Count > 0)
            {
                UiEvents.Raise(UiEvent.UnlockRevealed);
            }

            if (_model.NewAchievements.Count > 0)
            {
                UiEvents.Raise(UiEvent.AchievementRevealed);
            }

            if (_newBestRound)
            {
                UiEvents.Raise(UiEvent.NewBestRound);
            }
        }

        /// <summary>Skips the choreography to its end state (the first Submit / click; tests).</summary>
        public void Skip()
        {
            if (!_playing)
            {
                return;
            }

            if (_choreography != null)
            {
                StopCoroutine(_choreography);
                _choreography = null;
            }

            Finish();
        }

        // =================================================================== chrome

        private void BuildFooter(bool focusNow)
        {
            RectTransform footer = UiFactory.NewRect("Footer", Content);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, UiTokens.TargetPreferred);
            footer.anchoredPosition = Vector2.zero;

            TMP_Text note = UiFactory.Label(
                "FooterNote", footer, _model.FooterNote, UiType.Caption,
                _model.SaveFailed ? UiColor.StateWarning : UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform noteRect = UiFactory.Fill((RectTransform)note.transform);
            noteRect.offsetMax = new Vector2(-360f, 0f);

            UiButton continueButton = UiFactory.Button(
                "MainMenu", footer, ResultsViewModel.ContinueLabel, UiButtonVariant.Primary,
                PrototypeUiFlow.GoToMainMenu);
            RectTransform rect = (RectTransform)continueButton.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(320f, UiTokens.TargetPreferred);
            rect.anchoredPosition = Vector2.zero;

            UiInput.Prompt(continueButton, UiPromptAction.Submit, true);
            if (focusNow)
            {
                SetInitialFocus(continueButton);
            }

            _continueButton = continueButton;
        }

        /// <summary>
        /// Cancel = the one action on this screen (MAIN MENU). While the choreography plays, the first Submit / click
        /// skips it (MAIN MENU is not focused yet, so that press cannot leave); after it, focus is reclaimed for the pad.
        /// </summary>
        private void Update()
        {
            if (UiInput.CancelPressed())
            {
                PrototypeUiFlow.GoToMainMenu();
                return;
            }

            if (_playing)
            {
                if (UiInput.SubmitPressed() || Input.GetMouseButtonDown(0))
                {
                    Skip();
                }

                return;
            }

            UiInput.ReclaimFocus(_continueButton);
        }

        /// <summary>
        /// Shown when the screen is reached without a run behind it - opening the scene directly in the editor, or a build
        /// settings order that skipped the run.
        /// </summary>
        private void BuildEmptyState()
        {
            BuildHeader(_model.Eyebrow, ResultsViewModel.Title);

            RectTransform card = UiFactory.FramedCard("EmptyCard", Content, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(720f, 240f);
            card.anchoredPosition = Vector2.zero;

            RectTransform host = UiFactory.CardContent(card, UiSpace.XXL);
            var stack = new UiStack(host);
            TMP_Text title = UiFactory.Label("Title", host, _model.EmptyTitle, UiType.H3, UiColor.TextPrimary);
            stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H3));
            stack.Gap(UiSpace.M);
            TMP_Text body = UiFactory.Label("Body", host, _model.EmptyBody, UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)body.transform, UiFactory.LineHeight(UiType.Body) * 2f);

            BuildFooter(true);
        }

        // =================================================================== helpers

        private static void TopBand(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(rect.anchorMin.x, 1f);
            rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(rect.offsetMin.x, -(top + height));
            rect.offsetMax = new Vector2(rect.offsetMax.x, -top);
        }

        private static float LineOf(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag").y);
        }

        private static void AddRow(ref UiStack stack, RectTransform host, UiStatLine line, UiType? type = null)
        {
            UiType valueType = type ?? (line.Emphasis ? UiType.H3 : UiType.BodyStrong);
            TMP_Text valueText = UiFactory.StatRow(line.Label, line.Value, host, line.Color, valueType, 140f);
            stack.Add((RectTransform)valueText.transform.parent, UiFactory.ScaledBand(RowHeight));
        }
    }
}
