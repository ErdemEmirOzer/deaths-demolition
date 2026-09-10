using System.Collections.Generic;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Run Results. One screen, twelve fields, no scrolling, no Claim button.
    ///
    /// THE TWELVE FIELDS AND WHERE THEY ARE
    ///   PERFORMANCE column   1 Reached Round (hero) · 2 Total Kills
    ///                        3 Standard · 4 Special · 5 Elite · 6 Ultra
    ///   EXPERIENCE column    7 Completed Round XP · 8 Kill XP
    ///                        9 Total XP Earned (hero) · 10 Profile Level Progress
    ///   REWARDS column      11 Newly Unlocked Content · 12 Achievements Earned
    ///
    /// WHY IT CANNOT SCROLL
    /// Three fixed columns on fractional anchors, a canvas scaler that matches on
    /// height, and a hard cap on the only two lists that can grow. Nothing on this
    /// screen is sized by its content, so there is no arrangement of results that can
    /// make it taller than the viewport - which is what "one tidy screen" has to mean
    /// if it is going to survive a run that earns two unlocks and three achievements at
    /// once. The reward lists show at most <see cref="MaxRewardEntries"/> entries and
    /// summarise the rest on one line.
    ///
    /// WHY THERE IS NO CLAIM BUTTON
    /// The profile was already written to disk by <c>PrototypeUiFlow</c> before this
    /// scene loaded. This screen is a report, not a transaction: closing it, quitting
    /// during it, or never reading it at all makes no difference to what was earned.
    ///
    /// FIVE STATES. Empty: no run behind the screen - it says so and offers the way
    /// back, instead of printing twelve zeros. Loading: none, and deliberately - the
    /// report is complete before the scene exists. Error: a profile that could not be
    /// saved, stated in the footer rather than hidden. Normal: below. Dense: the reward
    /// cap above.
    ///
    /// KNOWN AND INTENTIONAL: "Newly Unlocked Content" is usually empty from the second
    /// run of a session onwards, because the unlock ladder is exhausted at profile
    /// level 4 and one round-8 run earns roughly that much. The numbers are frozen
    /// (decision S6). The consequence is that the EMPTY state of this column is the
    /// state the player will see most often, so it is written as a finished sentence
    /// and given the same weight as a filled list - not left as a blank rectangle.
    /// </summary>
    public sealed class ResultsScreen : UiScreen
    {
        private const float RowHeight = 40f;
        private const float HeroValueHeight = 56f;
        private const float RewardEntryHeight = 72f;

        /// <summary>Title band inside a reward entry. 24 px clears an 18 px bold line.</summary>
        private const float RewardTitleBand = 24f;

        private const float FooterHeight = 88f;

        /// <summary>Hard cap. The overflow is summarised, never scrolled.</summary>
        private const int MaxRewardEntries = 3;

        private RunRewardReport _report;
        private bool _celebrate;

        protected override void Build()
        {
            _report = ProfileService.LastReport ?? RunRewardReport.Empty();

            if (!_report.HasRun)
            {
                BuildEmptyState();
                return;
            }

            RunSummary summary = _report.Summary;

            float headerHeight = BuildHeader(
                DescribeEnding(summary),
                "RUN RESULTS",
                DescribeLoadout(summary));

            float top = headerHeight + (int)UiSpace.XXL;

            BuildPerformanceColumn(Column("Performance", 0f, 0.32f, top, FooterHeight), summary);
            BuildExperienceColumn(Column("Experience", 0.34f, 0.66f, top, FooterHeight));
            BuildRewardsColumn(Column("Rewards", 0.68f, 1f, top, FooterHeight));

            BuildFooter();
        }

        protected override void Start()
        {
            base.Start();

            if (_celebrate)
            {
                UiSound.Play(UiSoundKind.Reveal);
            }
        }

        // ============================================================== performance

        private void BuildPerformanceColumn(RectTransform column, RunSummary summary)
        {
            RectTransform card = UiFactory.Card("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel);
            UiFactory.Fill(card);
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XXL));

            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading("PERFORMANCE", host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XL);

            AddHero(ref stack, host, "REACHED ROUND", summary.ReachedRound.ToString(), UiColor.TextPrimary);

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            AddRow(ref stack, host, "Total Kills", summary.TotalKills.ToString(), UiColor.TextPrimary, UiType.H3);

            stack.Gap(UiSpace.S);

            AddRow(ref stack, host, "Standard Kills", summary.StandardKills.ToString());
            AddRow(ref stack, host, "Special Kills", summary.SpecialKills.ToString(), UiColor.AccentSakura);
            AddRow(ref stack, host, "Elite Kills", summary.EliteKills.ToString(), UiColor.AccentWisteria);
            AddRow(ref stack, host, "Ultra Kills", summary.UltraKills.ToString(), UiColor.StateGold);

            stack.Gap(UiSpace.L);
            stack.Add((RectTransform)UiFactory.Divider("D2", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            AddRow(ref stack, host, "Rounds Completed", summary.CompletedRounds.ToString(),
                UiColor.TextSecondary, UiType.Body);
            AddRow(ref stack, host, "Gold Collected", summary.GoldEarned.ToString(),
                UiColor.StateGold, UiType.Body);

            stack.Gap(UiSpace.M);

            Text note = UiFactory.Label(
                "Note", host,
                "Kills from the round you died in count. That round grants no round XP, and gold grants none at all.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 3f);
        }

        // =============================================================== experience

        private void BuildExperienceColumn(RectTransform column)
        {
            RectTransform card = UiFactory.Card("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel);
            UiFactory.Fill(card);
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XXL));

            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading("EXPERIENCE", host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.XL);

            AddRow(ref stack, host, "Completed Round XP", "+" + _report.CompletedRoundXp);
            AddRow(ref stack, host, "Kill XP", "+" + _report.KillXp);

            stack.Gap(UiSpace.S);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            AddHero(ref stack, host, "TOTAL XP EARNED", "+" + _report.TotalXpEarned, UiColor.AccentSakura);

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D2", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            Text progressLabel = UiFactory.Label(
                "ProgressLabel", host, "PROFILE LEVEL PROGRESS", UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)progressLabel.transform, UiFactory.LineHeight(UiType.Label));

            stack.Gap(UiSpace.M);

            string levelLine = _report.LeveledUp
                ? "LEVEL " + _report.LevelBefore + "   ->   LEVEL " + _report.LevelAfter
                : "LEVEL " + _report.LevelAfter;

            Text level = UiFactory.Numeric(
                "Level", host, levelLine, UiType.H2,
                _report.LeveledUp ? UiColor.StateSuccess : UiColor.TextPrimary);
            stack.Add((RectTransform)level.transform, 32f);

            stack.Gap(UiSpace.M);

            UiBar bar = UiFactory.Bar("XpBar", host, UiColor.BarXp, 16f);
            bar.SetValue(0f, true);
            bar.SetValue(_report.ProgressAfter01);
            stack.Add((RectTransform)bar.transform, 16f);

            stack.Gap(UiSpace.S);

            Text barCaption = UiFactory.Label(
                "BarCaption", host,
                _report.XpIntoLevelAfter + " / " + _report.LevelRequirementAfter +
                " XP towards level " + (_report.LevelAfter + 1),
                UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)barCaption.transform, UiFactory.LineHeight(UiType.Caption));

            if (_report.LeveledUp)
            {
                _celebrate = true;

                stack.Gap(UiSpace.L);

                string text = _report.LevelsGained > 1
                    ? "LEVEL UP  x" + _report.LevelsGained
                    : "LEVEL UP";

                // StateSuccess, not AccentMoss: moss is dark enough that dark-on-moss
                // measures 3.52:1 and light-on-moss 4.66:1, i.e. one of the two always
                // sits near the floor. Jade clears both comfortably.
                Text chip = UiFactory.Chip(
                    "LevelUp", host, text, UiColor.StateSuccess, UiColor.SurfaceInset, 200f, 34f);

                var chipRect = (RectTransform)chip.transform.parent;
                stack.AddLeft(chipRect, 200f, 34f);

                // The pulse moves the PILL only. Fading the label as well would drop the
                // composite text ratio to 4.49:1 at the bottom of the swing.
                chipRect.gameObject.AddComponent<UiPulse>().Configure(
                    new Graphic[] { chipRect.GetComponent<Graphic>() },
                    0.80f, 1f, UiTokens.PulsePeriod);
            }
            else
            {
                stack.Gap(UiSpace.L);

                Text note = UiFactory.Label(
                    "Note", host,
                    "Profile progress unlocks content only. It never raises your starting damage, health or gold.",
                    UiType.Caption, UiColor.TextMuted);
                stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 3f);
            }
        }

        // ================================================================== rewards

        private void BuildRewardsColumn(RectTransform column)
        {
            RectTransform card = UiFactory.Card("Card", column, UiColor.BorderHairline, UiColor.SurfacePanel);
            UiFactory.Fill(card);
            // The rewards column carries the two lists that can grow, so it gets the
            // tighter padding: it is the column with the least vertical slack.
            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.L));

            var stack = new UiStack(host);

            stack.Add(UiFactory.Heading("NEWLY UNLOCKED", host, UiType.H3), UiFactory.LineHeight(UiType.H3));
            stack.Gap(UiSpace.M);
            BuildRewardList(ref stack, host, _report.NewUnlocks,
                "No new content this run. Everything the ladder offers is already unlocked.",
                UiColor.AccentSakura);

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.L);

            stack.Add(UiFactory.Heading("ACHIEVEMENTS EARNED", host, UiType.H3), UiFactory.LineHeight(UiType.H3));
            stack.Gap(UiSpace.M);
            BuildRewardList(ref stack, host, _report.NewAchievements,
                "No new achievements this run.",
                UiColor.StateSuccess);

            stack.Gap(UiSpace.L);

            Text note = UiFactory.Label(
                "Note", host,
                "Everything above is already saved to your profile. There is nothing to claim.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
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
                Text empty = UiFactory.Label("Empty", host, emptyMessage, UiType.Body, UiColor.TextMuted);
                stack.Add((RectTransform)empty.transform, UiFactory.LineHeight(UiType.Body) * 2f);
                return;
            }

            _celebrate = true;

            int shown = Mathf.Min(entries.Count, MaxRewardEntries);
            for (int i = 0; i < shown; i++)
            {
                stack.Add(BuildRewardEntry(host, entries[i], accent), RewardEntryHeight);
                stack.Gap(UiSpace.S);
            }

            if (entries.Count > shown)
            {
                Text more = UiFactory.Label(
                    "More", host,
                    "and " + (entries.Count - shown) + " more, already added to your profile",
                    UiType.Caption, UiColor.TextSecondary);
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

            Image mark = UiFactory.NewImage("Mark", content, accent, UiSprites.Petal);
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0f, 0.5f);
            markRect.sizeDelta = new Vector2(24f, 24f);
            markRect.anchoredPosition = Vector2.zero;

            Text title = UiFactory.Label(
                "Title", content, UiFactory.Clip(entry.Title, 32), UiType.BodyStrong, accent,
                TextAnchor.LowerLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMin = new Vector2(24f + (int)UiSpace.M, RewardTitleBand);
            titleRect.offsetMax = new Vector2(-64f, 0f);

            Text detail = UiFactory.Label(
                "Detail", content, UiFactory.Clip(entry.Detail, 60), UiType.Caption, UiColor.TextSecondary,
                TextAnchor.UpperLeft);
            RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
            detailRect.offsetMin = new Vector2(24f + (int)UiSpace.M, 0f);
            detailRect.offsetMax = new Vector2(-64f, -RewardTitleBand);

            Text badge = UiFactory.Chip("New", content, "NEW", accent, UiColor.SurfaceInset, 56f, 24f);
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

            string message = _report.SaveFailed
                ? "This run could not be written to the profile file. Progress from it will be lost on exit."
                : "Rewards are granted automatically. There is nothing to claim.";

            Text note = UiFactory.Label(
                "FooterNote", footer, message, UiType.Caption,
                _report.SaveFailed ? UiColor.StateWarning : UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform noteRect = UiFactory.Fill((RectTransform)note.transform);
            noteRect.offsetMax = new Vector2(-360f, 0f);

            UiButton continueButton = UiFactory.Button(
                "Continue", footer, "CONTINUE", UiButtonVariant.Primary, PrototypeUiFlow.GoToMainMenu);
            RectTransform rect = (RectTransform)continueButton.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(320f, UiTokens.TargetPreferred);
            rect.anchoredPosition = Vector2.zero;

            SetInitialFocus(continueButton);
        }

        /// <summary>
        /// Shown when the screen is reached without a run behind it - opening the scene
        /// directly in the editor, or a build settings order that skipped the run.
        /// </summary>
        private void BuildEmptyState()
        {
            BuildHeader("NO RUN RECORDED", "RUN RESULTS");

            RectTransform card = UiFactory.Card("EmptyCard", Content, UiColor.BorderHairline, UiColor.SurfacePanel);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(720f, 240f);
            card.anchoredPosition = Vector2.zero;

            RectTransform host = UiFactory.CardContent(card, UiSpace.XXL);
            var stack = new UiStack(host);

            Text title = UiFactory.Label(
                "Title", host, "There is no finished run to report.", UiType.H3, UiColor.TextPrimary);
            stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H3));

            stack.Gap(UiSpace.M);

            Text body = UiFactory.Label(
                "Body", host,
                "This screen is filled in the moment a run ends. Start one from the main menu and it will " +
                "come back here by itself.",
                UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)body.transform, UiFactory.LineHeight(UiType.Body) * 2f);

            BuildFooter();
        }

        // =================================================================== helpers

        private static void AddHero(
            ref UiStack stack, RectTransform host, string label, string value, UiColor valueColor)
        {
            Text labelText = UiFactory.Label("HeroLabel", host, label, UiType.Label, UiColor.TextMuted);
            stack.Add((RectTransform)labelText.transform, UiFactory.LineHeight(UiType.Label));

            stack.Gap(UiSpace.XS);

            Text valueText = UiFactory.Numeric("HeroValue", host, value, UiType.NumericXL, valueColor);
            stack.Add((RectTransform)valueText.transform, HeroValueHeight);
        }

        private static void AddRow(
            ref UiStack stack,
            RectTransform host,
            string key,
            string value,
            UiColor color = UiColor.TextPrimary,
            UiType type = UiType.BodyStrong)
        {
            Text valueText = UiFactory.StatRow(key, value, host, color, type, 140f);
            stack.Add((RectTransform)valueText.transform.parent, RowHeight);
        }

        private static string DescribeEnding(RunSummary summary)
        {
            switch (summary.EndReason)
            {
                case RunEndReason.Aborted:
                    return "RUN ABANDONED IN ROUND " + summary.ReachedRound;

                case RunEndReason.Debug:
                    return "RUN ENDED FROM THE DEBUG PANEL";

                default:
                    return "DEFEATED IN ROUND " + summary.ReachedRound;
            }
        }

        private static string DescribeLoadout(RunSummary summary)
        {
            string weapon = ProfileService.WeaponDisplayName(summary.Weapon);
            string build = string.IsNullOrEmpty(summary.BuildDescription)
                ? "no upgrades"
                : UiFactory.Clip(summary.BuildDescription, 40);

            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(summary.RunDuration));
            string duration = (totalSeconds / 60) + ":" + (totalSeconds % 60).ToString("00");

            return weapon + "   ·   " + build + "   ·   " + duration + " played";
        }
    }
}
