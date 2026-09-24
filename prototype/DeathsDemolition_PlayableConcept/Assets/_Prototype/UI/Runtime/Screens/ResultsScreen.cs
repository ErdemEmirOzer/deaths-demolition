using System.Collections.Generic;
using DD.Prototype.Profile;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Run Results. One screen, no scrolling, no Claim button.
    ///
    /// HIERARCHY (WP-005), top to bottom and left to right:
    ///   1 the run's result     - the title IS the ending ("DEFEATED IN ROUND 7"), and
    ///                            REACHED ROUND is the only extra-large number
    ///   2 combat summary       - kills, by tier where a tier was actually fought
    ///   3 XP and profile       - the itemised breakdown, the total, level progress
    ///   4 unlocks/achievements - what the run did to the profile
    ///   5 secondary detail     - gold, upgrades, time; run-only, no XP
    ///   6 the way out          - one primary action: MAIN MENU
    ///
    /// EVERYTHING IS A SNAPSHOT
    /// The screen builds a <see cref="ResultsViewModel"/> ONCE, from the
    /// <see cref="RunRewardReport"/> the profile produced when the run ended - before
    /// this scene existed and after the run's own state was reset. Nothing here reads
    /// the director, the wallet, the weapon or <c>RunEvents.Current</c>, so nothing here
    /// can change after the fact, and re-opening the screen can never write the profile
    /// a second time: the profile was applied by <c>PrototypeUiFlow</c> exactly once, on
    /// the run-ended event, and this class only reads its report.
    ///
    /// WHY IT CANNOT SCROLL
    /// Three fixed columns on fractional anchors, a canvas scaler that matches on
    /// height, and a hard cap on the only two lists that can grow. Nothing on this
    /// screen is sized by its content, so there is no arrangement of results that can
    /// make it taller than the viewport. The reward lists show at most
    /// <see cref="ResultsViewModel.MaxRewardEntries"/> entries and summarise the rest.
    ///
    /// WHY THERE IS NO CLAIM BUTTON
    /// The profile was already written to disk before this scene loaded. This screen
    /// is a report, not a transaction: closing it, quitting during it, or never reading
    /// it at all makes no difference to what was earned.
    ///
    /// FIVE STATES. Empty: no run behind the screen - it says so and offers the way
    /// back. Loading: none, and deliberately - the report is complete before the scene
    /// exists. Error: a profile that could not be saved, stated in the footer. Normal:
    /// below. Dense: the reward cap above, and tier rows that appear only when earned.
    /// </summary>
    public sealed class ResultsScreen : UiScreen
    {
        private const float RowHeight = 40f;
        private const float HeroValueHeight = 56f;
        private const float RewardEntryHeight = 92f; // AD v004 MUST-5: two-line detail, no truncation (24 title + 44 detail + 2·12)

        /// <summary>Title band inside a reward entry. 24 px clears an 18 px bold line.</summary>
        private const float RewardTitleBand = 24f;

        /// <summary>Two Caption lines under the title (AD v004 MUST-5); the card content is 92 − 2·12 = 68 px, so title = 24, detail = 44 (2 × 22 with TMP's leading).</summary>
        private const float RewardDetailBand = 44f;

        private const float FooterHeight = 88f;

        private ResultsViewModel _model;

        /// <summary>What the screen drew. Exposed for tests; it is never rebuilt.</summary>
        public ResultsViewModel Model
        {
            get { return _model; }
        }

        protected override UiContext BaseContext
        {
            get { return UiContext.RunResults; }
        }

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

            // The ending is the title: it is the first thing the player reads, and the
            // largest text on the screen. "RUN RESULTS" becomes the eyebrow.
            // WP-023 data: the ending line names what ended the run when it is known.
            string ending = string.IsNullOrEmpty(_model.KilledBy)
                ? _model.Eyebrow
                : _model.Eyebrow + "  ·  " + UiFactory.Clip(_model.KilledBy, 32).ToUpperInvariant(); // a caps heading (the Display face is caps-only)
            // WP-022E name plate: "RUN RESULTS  ·  BLOODED" when a title is chosen.
            string plate = CosmeticsViewModel.NamePlate(ProfileService.CosmeticsSource);
            string eyebrow = string.IsNullOrEmpty(plate) ? ResultsViewModel.Title : ResultsViewModel.Title + "  ·  " + plate.ToUpperInvariant();
            float headerHeight = BuildHeader(eyebrow, ending, _model.Loadout);

            float top = headerHeight + (int)UiSpace.XXL;

            RectTransform performance = Column("Performance", 0f, 0.32f, top, FooterHeight);
            RectTransform experience = Column("Experience", 0.34f, 0.66f, top, FooterHeight);
            RectTransform rewards = Column("Rewards", 0.68f, 1f, top, FooterHeight);
            BuildPerformanceColumn(performance);
            BuildExperienceColumn(experience);
            BuildRewardsColumn(rewards);

            // WP-022F: the three columns land left to right (60 ms apart) after the screen
            // fade - the one motion the Results screen has; reduced motion → at rest.
            gameObject.AddComponent<UiStaggerIn>().Configure(new[]
            {
                UiFactory.Group(performance.gameObject),
                UiFactory.Group(experience.gameObject),
                UiFactory.Group(rewards.gameObject)
            });

            BuildFooter();
        }

        protected override void Start()
        {
            base.Start();

            if (_model != null && _model.Celebrate)
            {
                StartCoroutine(CelebrationBeats());
            }
            else if (_newBestRound)
            {
                UiEvents.Raise(UiEvent.NewBestRound);
            }
        }

        /// <summary>
        /// Spacing between Results celebration beats. WP-068 slice 0 (D4): Reveal, new
        /// best, level-up, unlock and achievement used to be raised in the same frame
        /// (four or five cues stacked into the limiter); each is now its own beat, in
        /// reading order, only the beats that happened taking a slot. Sound is tied to
        /// the logical event, so Reduced Motion keeps the same timing.
        /// </summary>
        public const float CelebrationBeatSeconds = 0.12f;

        private System.Collections.IEnumerator CelebrationBeats()
        {
            var beat = new WaitForSecondsRealtime(CelebrationBeatSeconds);
            yield return UiCurtain.WaitRevealed(); // WP-068 slice 10: the beats play on screen, not behind the curtain
            UiSound.Play(UiSoundKind.Reveal);

            if (_newBestRound)
            {
                yield return beat;
                UiEvents.Raise(UiEvent.NewBestRound);
            }

            if (_model.LeveledUp)
            {
                yield return beat;
                UiEvents.Raise(UiEvent.LevelUp);
            }

            if (_model.NewUnlocks.Count > 0)
            {
                yield return beat;
                UiEvents.Raise(UiEvent.UnlockRevealed);
            }

            if (_model.NewAchievements.Count > 0)
            {
                yield return beat;
                UiEvents.Raise(UiEvent.AchievementRevealed);
            }
        }

        private bool _newBestRound;

        // ============================================================== performance

        private void BuildPerformanceColumn(RectTransform column)
        {
            RectTransform card = UiFactory.FramedCard("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            UiFactory.Fill(card);
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XXL));

            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading("PERFORMANCE", host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.L);

            AddHero(ref stack, host, _model.ReachedRoundLabel, _model.ReachedRound, UiColor.TextPrimary, UiType.NumericXL);

            if (!string.IsNullOrEmpty(_model.BestRoundChip))
            {
                // WP-023 data: the profile's highest round moved. Pulsing pill, StateSuccess.
                TMP_Text best = UiFactory.Chip("BestRound", host, _model.BestRoundChip, UiColor.StateSuccess, UiColor.SurfaceInset, 200f, 30f);
                RectTransform bestRect = stack.Add((RectTransform)best.transform.parent, 30f);
                bestRect.anchorMax = new Vector2(0f, 1f);
                bestRect.sizeDelta = new Vector2(200f, 30f);
                bestRect.gameObject.AddComponent<UiPulse>().Configure(
                    new Graphic[] { bestRect.GetComponent<Graphic>() }, 0.80f, 1f, UiTokens.PulsePeriod);
                _newBestRound = true; // raised as a Results beat (Start), not during the build
                stack.Gap(UiSpace.XS);
            }

            stack.Gap(UiSpace.XS);

            TMP_Text rounds = UiFactory.Label(
                "RoundsCompleted", host, _model.RoundsCompletedCaption, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)rounds.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            for (int i = 0; i < _model.Combat.Count; i++)
            {
                AddRow(ref stack, host, _model.Combat[i]);
            }

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D2", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            // Secondary detail, deliberately last and in the quieter type: gold and time
            // are facts about the run that grant nothing.
            for (int i = 0; i < _model.Economy.Count; i++)
            {
                AddRow(ref stack, host, _model.Economy[i], UiType.Body);
            }

            stack.Gap(UiSpace.M);

            TMP_Text note = UiFactory.Label("Note", host, _model.CombatNote, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
        }

        // =============================================================== experience

        private void BuildExperienceColumn(RectTransform column)
        {
            RectTransform card = UiFactory.FramedCard("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            UiFactory.Fill(card);
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XXL));

            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading("EXPERIENCE", host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XL);

            for (int i = 0; i < _model.XpBreakdown.Count; i++)
            {
                AddRow(ref stack, host, _model.XpBreakdown[i]);
            }

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            // NumericL, one step under the round hero: the screen has one extra-large
            // number, and it is the round.
            AddHero(ref stack, host, "TOTAL XP EARNED", _model.TotalXp, UiColor.AccentSakura, UiType.NumericL);

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D2", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            TMP_Text progressLabel = UiFactory.Label(
                "ProgressLabel", host, "PROFILE LEVEL PROGRESS", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)progressLabel.transform, UiFactory.LineHeight(UiType.Label));

            stack.Gap(UiSpace.M);

            TMP_Text level = UiFactory.Numeric(
                "Level", host, _model.LevelLine, UiType.H2,
                _model.LeveledUp ? UiColor.StateSuccess : UiColor.TextPrimary);
            stack.Add((RectTransform)level.transform, 32f);

            stack.Gap(UiSpace.M);

            UiBar bar = UiFactory.Bar("XpBar", host, UiColor.BarXp, 16f);
            bar.SetValue(0f, true);
            bar.SetValue(_model.ProgressAfter01);
            stack.Add((RectTransform)bar.transform, 16f);

            stack.Gap(UiSpace.S);

            TMP_Text barCaption = UiFactory.Label(
                "BarCaption", host, _model.ProgressCaption, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)barCaption.transform, UiFactory.LineHeight(UiType.Caption));

            stack.Gap(UiSpace.L);

            if (_model.LeveledUp)
            {
                // StateSuccess, not AccentMoss: moss is dark enough that dark-on-moss
                // measures 3.52:1 and light-on-moss 4.66:1, i.e. one of the two always
                // sits near the floor. Jade clears both comfortably.
                TMP_Text chip = UiFactory.Chip(
                    "LevelUp", host, _model.LevelUpChip, UiColor.StateSuccess, UiColor.SurfaceInset, 200f, 34f);

                var chipRect = (RectTransform)chip.transform.parent;
                stack.AddLeft(chipRect, 200f, 34f);

                // The pulse moves the PILL only. Fading the label as well would drop the
                // composite text ratio to 4.49:1 at the bottom of the swing.
                chipRect.gameObject.AddComponent<UiPulse>().Configure(
                    new Graphic[] { chipRect.GetComponent<Graphic>() },
                    0.80f, 1f, UiTokens.PulsePeriod);

                stack.Gap(UiSpace.M);
            }

            TMP_Text note = UiFactory.Label("Note", host, _model.XpNote, UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 3f);
        }

        // ================================================================== rewards

        private void BuildRewardsColumn(RectTransform column)
        {
            RectTransform card = UiFactory.FramedCard("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            UiFactory.Fill(card);
            // The rewards column carries the two lists that can grow, so it gets the
            // tighter padding: it is the column with the least vertical slack.
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.L));

            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading("NEWLY UNLOCKED", host, UiType.H3), UiFactory.LineHeight(UiType.H3));
            stack.Gap(UiSpace.M);
            BuildRewardList(ref stack, host, _model.NewUnlocks, _model.UnlocksEmpty, UiColor.AccentSakura);

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            stack.Add(UiFactory.Heading("ACHIEVEMENTS EARNED", host, UiType.H3), UiFactory.LineHeight(UiType.H3));
            stack.Gap(UiSpace.M);
            BuildRewardList(ref stack, host, _model.NewAchievements, _model.AchievementsEmpty, UiColor.StateSuccess);
        }

        private void BuildRewardList(
            ref UiStack stack,
            RectTransform host,
            List<ProfileUnlockEntry> entries,
            string emptyMessage,
            UiColor accent)
        {
            if (entries == null || entries.Count == 0)
            {
                TMP_Text empty = UiFactory.Label("Empty", host, emptyMessage, UiType.Body, UiColor.TextMuted);
                stack.Add((RectTransform)empty.transform, UiFactory.LineHeight(UiType.Body) * 2f);
                return;
            }

            int shown = Mathf.Min(entries.Count, ResultsViewModel.MaxRewardEntries);
            for (int i = 0; i < shown; i++)
            {
                stack.Add(BuildRewardEntry(host, entries[i], accent), UiFactory.ScaledBand(RewardEntryHeight));
                stack.Gap(UiSpace.S);
            }

            if (entries.Count > shown)
            {
                TMP_Text more = UiFactory.Label(
                    "More", host, ResultsViewModel.Overflow(entries.Count), UiType.Caption, UiColor.TextSecondary);
                stack.Add((RectTransform)more.transform, UiFactory.LineHeight(UiType.Caption));
            }
        }

        /// <summary>
        /// A new unlock. Raised surface, accent border, petal mark and a pulsing NEW
        /// pill - the strongest treatment on the screen, which is what "prominently
        /// highlighted" has to buy the player who was not looking for it.
        /// </summary>
        private RectTransform BuildRewardEntry(RectTransform host, ProfileUnlockEntry entry, UiColor accent)
        {
            RectTransform card = UiFactory.Card("Reward", host, accent, UiColor.SurfaceRaised);
            RectTransform content = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.M));

            // WP-022: an AP-012 medallion carries the entry (unlocks: rare wreath,
            // achievements: ring by WP-023 rarity); the petal mark stays the no-art fallback.
            float markSize = RewardEntryHeight - (int)UiSpace.M * 2; // the mark keeps its 1.0 size; the text bands grow (D6)
            string typeLabel = string.Empty;
            Sprite markSprite = accent == UiColor.StateSuccess
                ? UiArt.Medal(_model.MedalFor(entry.Title))
                : _model.UnlockSpriteFor(entry.Title, out typeLabel); // WP-022D: glyph by unlock type
            Image mark = UiFactory.NewImage("Mark", content, accent, markSprite);
            mark.preserveAspect = true;
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0f, 0.5f);
            markRect.sizeDelta = new Vector2(markSize, markSize);
            markRect.anchoredPosition = Vector2.zero;

            float textLeft = markSize + (int)UiSpace.M;
            // AD v004 MUST-2: the type is said once, on the detail line - "Blooded", not
            // "Title: Blooded" (the data keeps its prefix; only the headline drops it).
            string titleText = entry.Title;
            if (!string.IsNullOrEmpty(typeLabel))
            {
                int colon = titleText.IndexOf(": ", System.StringComparison.Ordinal);
                if (colon > 0 && colon < 12)
                {
                    titleText = titleText.Substring(colon + 2);
                }
            }

            TMP_Text title = UiFactory.Label(
                "Title", content, UiFactory.Clip(titleText, 32), UiType.BodyStrong, accent,
                TextAnchor.LowerLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMin = new Vector2(textLeft, UiFactory.ScaledBand(RewardDetailBand)); // title sits above the two-line detail band
            titleRect.offsetMax = new Vector2(-64f, 0f);

            // WP-022D: the type in front of the detail ("TITLE · shown next to your name"),
            // so a title, a badge and a weapon read as three different things.
            string detailText = string.IsNullOrEmpty(typeLabel) ? entry.Detail : typeLabel + "  ·  " + entry.Detail;
            TMP_Text detail = UiFactory.Label(
                "Detail", content, UiFactory.Clip(detailText, 120), UiType.Caption, UiColor.TextSecondary,
                TextAnchor.UpperLeft); // wraps to the second line (Label truncates at the box, AD v004 MUST-5)
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

            // Pill only, never the label. See the LEVEL UP chip for the measurement.
            badgeRect.gameObject.AddComponent<UiPulse>().Configure(
                new Graphic[] { badgeRect.GetComponent<Graphic>() }, 0.80f, 1f, UiTokens.PulsePeriod);

            return card;
        }

        // =================================================================== chrome

        private void BuildFooter()
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
            SetInitialFocus(continueButton);
            _continueButton = continueButton;
        }

        private UiButton _continueButton;

        /// <summary>Cancel = the one action on this screen (MAIN MENU); focus is reclaimed for the pad.</summary>
        private void Update()
        {
            if (UiInput.CancelPressed())
            {
                PrototypeUiFlow.GoToMainMenu();
                return;
            }

            UiInput.ReclaimFocus(_continueButton);
        }

        /// <summary>
        /// Shown when the screen is reached without a run behind it - opening the scene
        /// directly in the editor, or a build settings order that skipped the run.
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

            BuildFooter();
        }

        // =================================================================== helpers

        private static void AddHero(
            ref UiStack stack, RectTransform host, string label, string value, UiColor valueColor, UiType type)
        {
            TMP_Text labelText = UiFactory.Label("HeroLabel", host, label, UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)labelText.transform, UiFactory.LineHeight(UiType.Label));

            stack.Gap(UiSpace.XS);

            TMP_Text valueText = UiFactory.Numeric("HeroValue", host, value, type, valueColor);
            stack.Add((RectTransform)valueText.transform, type == UiType.NumericXL ? HeroValueHeight : 40f);
        }

        private static void AddRow(ref UiStack stack, RectTransform host, UiStatLine line, UiType? type = null)
        {
            UiType valueType = type ?? (line.Emphasis ? UiType.H3 : UiType.BodyStrong);
            TMP_Text valueText = UiFactory.StatRow(line.Label, line.Value, host, line.Color, valueType, 140f);
            stack.Add((RectTransform)valueText.transform.parent, RowHeight);
        }
    }
}
