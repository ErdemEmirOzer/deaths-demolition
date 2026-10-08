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
    ///   ground         - WP-098B (AP-028 §3.9; brush kit KILAVUZ §3 "Sonuç"): the world plate (Look B far / mid / near) under
    ///                    the kit's Window scrim (<see cref="UiBrushPane"/>, linear 0.95 - text stands on it directly); no veils,
    ///                    no petals, no AP-012 lozenge, no card frame on any block
    ///   title          - the kit's C5 title without a glyph: the ending ("DEFEATED IN ROUND 7"), its rule and title brush
    ///   reason line    - what killed the player · the loadout · the name plate (MenuBody); NEW BEST at its right (AP-028 C28):
    ///                    the words with the kit's C8 selection trace under them, the last and largest beat
    ///   hero numbers   - Round · Kills · Gold · Time: four kit C21 counters (caption, NumericHero number, hairline); the gold
    ///                    number is the kit's Currency style (the gold reserve lives in the kit)
    ///   RUN REPORT     - 7 columns: the weapon row (identity glyph, name, build), COMBAT and RUN rows side by side, the
    ///                    kill-XP note at the foot
    ///   REWARDS        - 5 columns: EXPERIENCE (+XP, breakdown, level line, the XP bar revealed before → after, LEVEL UP
    ///                    stamp), PATH +Ash, then the unlocks and achievements that fit - measured, the rest summarised
    ///   footer         - the save note and MAIN MENU (the kit's C11, Menu.Home)
    /// Trace budget (KILAVUZ §1): title brush 1 + C8 1 = 2; vermilion 1 (the C8 core).
    ///
    /// CHOREOGRAPHY (≤ 2.2 s after the curtain opens, E26): the three blocks stagger in; the four numbers count up from 0
    /// 120 ms apart over 400 ms each on one motion timeline (all four landed by 0.76 s, WP-098B ≤ 1.5 s), each landing
    /// with a short punch and a counter tok (UiEvent.CounterRolling once, CounterSettled per landing - S-08's map); the
    /// XP bar reveals, LEVEL UP stamps, each unlock / achievement stamps 120 ms apart, NEW BEST last (its C8 inks in under
    /// the words). The first Submit or click skips to the end state; MAIN MENU takes the focus at the end, so the second
    /// press leaves. Reduced Motion shows the end state at once (the counters at their final value, no punch, the C8 drawn).
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
        private const float FooterHeight = 88f;      // the kit's 64 high C11 + XL
        private const float ReportShare = 0.58f;     // 7 of 12 columns (+ half a gutter)
        private const float WeaponRowHeight = 52f;

        /// <summary>WP-098B: the blocks have no card - no frame, no inner padding; their words align with the counters.</summary>
        private const UiSpace BlockPadding = UiSpace.None;

        /// <summary>Spacing between choreography beats (E26: 120 ms).</summary>
        public const float CelebrationBeatSeconds = 0.12f;

        /// <summary>The window's kit pane (its root under the page grid; its C1 on the screen root).</summary>
        public const string PaneName = "ResultsPane";

        /// <summary>
        /// WP-098B: the Results' carrier and count constants in one place. Every scrim, size and stroke is the brush kit's
        /// (UiBrushTokens); what is left here is the screen's own choice of level, plates and count timing.
        /// </summary>
        public static class Look
        {
            /// <summary>
            /// KILAVUZ §3 "Sonuç": the Window scrim - linear 0.95, AP-028's α 205 in sRGB (§0 T1). Text stands on it directly:
            /// TextSecondary 5.10:1 on the Cream100 world, 4.75:1 on pure white (KILAVUZ §4.4); TextMuted is not used.
            /// </summary>
            public const UiBrushScrim Scrim = UiBrushScrim.Window;

            /// <summary>The least ink under every word (no wash on a Window): ≥ <see cref="UiBrushTokens.TextGroundMin"/>.</summary>
            public static float TextGroundAlpha
            {
                get { return UiBrushTokens.Combined(UiBrushTokens.Scrim(Scrim), 0f); }
            }

            /// <summary>The world plate's bands, far → near (S-05's Look B captures; static here - no parallax, no rain).</summary>
            public static readonly string[] Plates = { "MenuPlates/plate_far", "MenuPlates/plate_mid", "MenuPlates/plate_near" };

            /// <summary>One number's roll (Emphasis 400 ms, Settle). Four rolls 120 ms apart land by 0.76 s.</summary>
            public const UiDuration CountDuration = UiDuration.Emphasis;

            public const float CountStagger = CelebrationBeatSeconds;

            /// <summary>The landing punch: text-bearing, so ≤ TextPopMaxScale (1.06), back to 1 in 120 ms.</summary>
            public const float PunchScale = UiMotionTokens.TextPopMaxScale;

            public const float PunchSeconds = 0.12f;

            /// <summary>AP-028 §3.9: four counters on a 440 step, each hairline 392 long - 48 between two counters.</summary>
            public const float CounterGap = 48f;

            /// <summary>Seconds from the first roll to the last landing (3 staggers + one roll).</summary>
            public static float CountPhaseSeconds
            {
                get { return 3f * CountStagger + UiMotionTokens.Seconds(CountDuration); }
            }
        }

        private ResultsViewModel _model;
        private UiBrushPane _pane;
        private RectTransform _page;   // the pane's clipped content: every block of the screen lives here
        private UiButton _continueButton;
        private readonly UiCounter[] _tiles = new UiCounter[4];
        private UiTimeline _count;
        private Image _clockRoll;      // disabled; its fill is the time roll's progress, driven by UiMotion
        private bool _clockRolling;
        private int _clockShown = -1;
        private string _finalTime;
        private TMP_Text _timeValue;
        private UiBar _xpBar;
        private RectTransform _levelUp;
        private CanvasGroup _levelUpGroup;
        private RectTransform _newBest;
        private CanvasGroup _newBestGroup;
        private TMP_Text _newBestText;
        private UiBrushMark _newBestMark;
        private bool _newBestInked;
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

        /// <summary>The hero counters (the kit's C21): Round, Kills, Gold, Time (tests).</summary>
        public UiCounter Tile(int i) => _tiles[i];

        /// <summary>The kit's carrier: C1 Window scrim, the clipped content (tests read its level and its text ground).</summary>
        public UiBrushPane Pane => _pane;

        /// <summary>NEW BEST's C8 (null when the run set no best) - tests.</summary>
        public UiBrushMark NewBestMark => _newBestMark;

        protected override UiContext BaseContext => UiContext.RunResults;

        /// <summary>WP-098B: the v2 backdrop (veils, petals, foot rule) gives way to the world plate - <see cref="BuildWorldPlate"/>.</summary>
        protected override bool WantsBackdrop => false;

        protected override void Build()
        {
            BuildWorldPlate(ScreenRect);
            _pane = UiBrushKit.Pane(ScreenRect, Content, PaneName, Look.Scrim, UiBrushWash.None);
            _page = _pane.Content;
            _model = ResultsViewModel.From(ProfileService.LastReport);
            // WP-022B: the WP-023 results record adds what the receipt cannot know - a new
            // best round, what killed the player, achievement rarity (medallion ring).
            _model.ApplyResultsData(ProfileService.ResultsSource != null ? ProfileService.ResultsSource.Current : null);

            if (!_model.HasRun)
            {
                BuildEmptyState();
                return;
            }

            // The ending is the title; the killing blow, the loadout and the name plate the reason line under it (AP-028
            // §3.9). 12b: the killing blow in words ("STANDARD ZOMBIE"), never the raw source id ("ZOMBIE.STANDARD.OVERHEAD").
            float headerHeight = BuildHeader(_model.Eyebrow, ReasonLine());

            float top = headerHeight + (int)UiSpace.XL;
            RectTransform tiles = BuildTiles(top, out float tilesHeight);
            top += tilesHeight + (int)UiSpace.XL;

            RectTransform report = PageColumn("Report", 0f, ReportShare, top, FooterHeight);
            RectTransform rewards = PageColumn("Rewards", ReportShare + 0.02f, 1f, top, FooterHeight);
            // E26: the blocks take their content's height (one height for both, capped by the room) - no empty block. The
            // reward entries are fitted against the same capped room, so none is ever clipped by the cap.
            // AD 12c MUST-4: the room runs to the footer's own band (the kit's control height) + L.
            float room = _page.rect.height - top - UiBrushTokens.ControlHeight - (int)UiSpace.L;
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

            // The window opens with the screen: the input is live at once, but nothing is focused - MAIN MENU takes the focus
            // only when the beats end (Finish), so the first press skips them instead of leaving.
            _pane.Open(null);
        }

        protected override void Start()
        {
            base.Start();
            if (_model != null && _model.HasRun)
            {
                _choreography = StartCoroutine(Choreography());
            }
        }

        /// <summary>The reason line: the killing blow · the loadout · the name plate (each only when it has words).</summary>
        private string ReasonLine()
        {
            string line = ResultsViewModel.KillerName(_model.KilledBy);
            line = Join(line, _model.Loadout);
            return Join(line, UiStrings.Upper(CosmeticsViewModel.NamePlate(ProfileService.CosmeticsSource)));
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(b))
            {
                return a ?? string.Empty;
            }

            return string.IsNullOrEmpty(a) ? b : a + "  ·  " + b;
        }

        // ================================================================== title

        /// <summary>
        /// KILAVUZ §3 "Sonuç" (C5, no glyph): the kit's title at the page's top - its words, the 1 px rule and the short title
        /// brush the pane inks in on Open. The one title site of this file (the empty state passes its own words).
        /// </summary>
        private UiBrushTitle BuildTitle(string titleKey, string titleFallback)
        {
            return UiBrushKit.Title(_page, titleKey, titleFallback);
        }

        /// <summary>
        /// The title, then the reason line (MenuBody, TextSecondary; it wraps rather than cut a long killer id) with NEW BEST
        /// at its right. Returns the height the header took from the page's top.
        /// </summary>
        private float BuildHeader(string ending, string reason)
        {
            UiBrushTitle title = BuildTitle(null, ending);   // null key: the ending is the model's sentence, already in words
            float top = title.Height + (int)UiSpace.M;
            float width = _page.rect.width;

            float newBestWidth = BuildNewBest(top);
            float room = Mathf.Max(1f, width - (newBestWidth > 0f ? newBestWidth + (int)UiSpace.XL : 0f));
            TMP_Text line = UiFactory.Label("Reason", _page, reason, UiType.MenuBody, UiColor.TextSecondary, TextAnchor.UpperLeft);
            float height = Mathf.Max(LineOf(line), string.IsNullOrEmpty(reason) ? 0f : Mathf.Ceil(line.GetPreferredValues(reason, room, 0f).y));
            UiFactory.Place(line.rectTransform, 0f, top, room, height);
            float newBestHeight = _newBest != null ? _newBest.sizeDelta.y : 0f;
            return top + Mathf.Max(height, newBestHeight);
        }

        /// <summary>
        /// WP-098B (AP-028 C28 + the kit's C8): NEW BEST is its words - no chip, no ribbon, no gold (G15: gold is money) - and
        /// the kit's selection trace under them, inked in on the last beat. Right-aligned on the reason line; hidden until
        /// then. Returns its width (0 when the run set no best).
        /// </summary>
        private float BuildNewBest(float top)
        {
            if (string.IsNullOrEmpty(_model.BestRoundChip))
            {
                return 0f;
            }

            _newBestRound = true;
            string word = UiStrings.Get("results.new_best", "NEW BEST · ROUND {value}").Replace("{value}", _model.HeroRound.ToString());
            _newBest = UiFactory.NewRect("NewBest", _page);
            _newBestText = UiFactory.Label("Text", _newBest, word, UiType.MenuBody, UiColor.TextPrimary, TextAnchor.MiddleRight);
            _newBestText.enableWordWrapping = false;
            float width = Mathf.Ceil(_newBestText.GetPreferredValues(word).x);
            float line = LineOf(_newBestText);
            TopStrip(_newBestText.rectTransform, 0f, line);
            PivotRight(_newBestText.rectTransform);   // the punch grows the words away from the page's edge

            // C8: 16 in from each end of the words, 8 under them, its bed 3 around a 3 px core (placed by Under on the beat).
            _newBestMark = UiBrushKit.SelectionMark(_newBest);
            float markBand = UiBrushTokens.SelectionGap + UiBrushTokens.SelectionCore + UiBrushTokens.SelectionBed;

            _newBest.anchorMin = new Vector2(1f, 1f);
            _newBest.anchorMax = new Vector2(1f, 1f);
            _newBest.pivot = new Vector2(1f, 1f);
            _newBest.sizeDelta = new Vector2(width, line + markBand);
            _newBest.anchoredPosition = new Vector2(0f, -top);
            _newBestGroup = UiFactory.Group(_newBest.gameObject);
            _newBestGroup.alpha = 0f;
            return width;
        }

        /// <summary>NEW BEST lands: its words show, its C8 inks in under them (a cut on a skip or under Reduced Motion).</summary>
        private void InkNewBest(bool animate)
        {
            if (_newBest == null)
            {
                return;
            }

            _newBestGroup.alpha = 1f;
            if (_newBestMark != null && !_newBestInked)
            {
                _newBestInked = true;
                _newBestMark.Under(_newBestText.rectTransform, animate);
            }
        }

        // ============================================================ hero numbers

        /// <summary>
        /// WP-098B (AP-028 §3.9, kit C21): four <see cref="UiBrushKit.BigCounter"/>s - a MenuSmall caption, a NumericHero
        /// number, the 1 px line under it - on four quarter columns (<see cref="Look.CounterGap"/> between them). The root of
        /// each is "Tile.&lt;WORD&gt;" (the tests and the stagger find it); GOLD is the kit's Currency counter (its colour is
        /// the kit's). Every number starts at 0; its pivot sits at its left middle, so the landing punch grows it away from
        /// its caption.
        /// </summary>
        private RectTransform BuildTiles(float top, out float height)
        {
            RectTransform row = UiFactory.NewRect("Tiles", _page);
            string[] keys = { "results.tile.round", "results.tile.kills", "results.tile.gold", "results.tile.time" };
            string[] words = { "ROUND", "KILLS", "GOLD", "TIME" };
            float tileHeight = 0f;
            for (int i = 0; i < _tiles.Length; i++)
            {
                UiCounterStyle style = i == 2 ? UiCounterStyle.Currency : UiCounterStyle.Plain;
                _tiles[i] = UiBrushKit.BigCounter(row, "Tile." + words[i], keys[i], words[i], style, 0);
                var tile = (RectTransform)_tiles[i].transform;
                float band = tile.sizeDelta.y;   // the kit's measured band (caption + number + line), read before re-anchoring
                tileHeight = Mathf.Max(tileHeight, band);
                tile.anchorMin = new Vector2(i / 4f, 1f);
                tile.anchorMax = new Vector2((i + 1) / 4f, 1f);
                tile.pivot = new Vector2(0.5f, 1f);
                tile.offsetMin = new Vector2(i == 0 ? 0f : Look.CounterGap * 0.5f, -band);
                tile.offsetMax = new Vector2(i == 3 ? 0f : -Look.CounterGap * 0.5f, 0f);
                PivotLeft(_tiles[i].ValueText.rectTransform);
            }

            // Time reads m:ss: its counter's text is written as a clock while a hidden fill (the motion core's progress)
            // rolls 0 → 1 - see TickClock.
            _timeValue = _tiles[3].ValueText;
            _timeValue.text = "0:00";
            _finalTime = ResultsViewModel.FormatDuration(_model.HeroSeconds);
            _clockRoll = UiFactory.NewImage("ClockRoll", row, UiColor.TextPrimary);
            _clockRoll.enabled = false;
            _clockRoll.fillAmount = 0f;

            tileHeight = Mathf.Ceil(tileHeight);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0f, -(top + tileHeight));
            row.offsetMax = new Vector2(0f, -top);
            height = tileHeight;
            return row;
        }

        /// <summary>A full-width strip <paramref name="height"/> tall, <paramref name="top"/> below its parent's top.</summary>
        private static void TopStrip(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
        }

        /// <summary>The rect's pivot to its left middle without moving it (its offsets are put back).</summary>
        private static void PivotLeft(RectTransform rect)
        {
            SetPivot(rect, new Vector2(0f, 0.5f));
        }

        /// <summary>The rect's pivot to its right middle without moving it.</summary>
        private static void PivotRight(RectTransform rect)
        {
            SetPivot(rect, new Vector2(1f, 0.5f));
        }

        private static void SetPivot(RectTransform rect, Vector2 pivot)
        {
            Vector2 min = rect.offsetMin;
            Vector2 max = rect.offsetMax;
            rect.pivot = pivot;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        /// <summary>A column of the pane's page, on fractional anchors (UiScreen.Column's rule, inside the kit's content).</summary>
        private RectTransform PageColumn(string name, float from, float to, float top, float bottom)
        {
            RectTransform column = UiFactory.NewRect(name, _page);
            column.anchorMin = new Vector2(from, 0f);
            column.anchorMax = new Vector2(to, 1f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.offsetMin = new Vector2(0f, bottom);
            column.offsetMax = new Vector2(0f, -top);
            return column;
        }

        // ============================================================ ground

        /// <summary>
        /// WP-098B (AP-028 §3.1 / §3.9): the world plate instead of the v2 backdrop - a base fill first (a build without the
        /// plates keeps a plain ink ground), then the three Look B bands cover-fit. The scrim over it is the kit's (the
        /// pane's C1); there is no hand-set alpha here. Static - no parallax, no rain, no per-frame work. Shared with
        /// Selection (KILAVUZ §3: "v2 backdrop yerine plakalar + C1").
        /// </summary>
        internal static RectTransform BuildWorldPlate(RectTransform screen)
        {
            RectTransform ground = UiFactory.NewRect("Backdrop", screen);
            UiFactory.Fill(ground);
            ground.SetAsFirstSibling();

            Image baseFill = UiFactory.NewImage("Base", ground, UiColor.SurfaceBase);
            UiFactory.Fill((RectTransform)baseFill.transform);

            for (int i = 0; i < Look.Plates.Length; i++)
            {
                Texture2D plate = Resources.Load<Texture2D>(Look.Plates[i]);
                if (plate == null)
                {
                    continue;
                }

                var band = new GameObject("Plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(UiBackdropArt));
                var bandRect = (RectTransform)band.transform;
                bandRect.SetParent(ground, false);
                UiFactory.Fill(bandRect);
                RawImage image = band.GetComponent<RawImage>();
                image.texture = plate;
                image.raycastTarget = false;
                band.GetComponent<UiBackdropArt>().Configure(1f);
            }

            return ground;
        }

        // ============================================================ run report

        private float BuildReport(RectTransform column)
        {
            RectTransform host = BlockHost(column);   // AD R1-D SHOULD: one padding, one header baseline with REWARDS
            var stack = new UiStack(host);

            stack.Add(SectionHeading(UiStrings.Get("results.report", "RUN REPORT"), host), UiFactory.LineHeight(UiType.H2));
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
            TMP_Text name = UiFactory.Label("Name", weapon, UiStrings.Upper(_model.WeaponName), UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.LowerLeft);
            RectTransform nameRect = UiFactory.Fill((RectTransform)name.transform);
            nameRect.offsetMin = new Vector2(WeaponRowHeight + (int)UiSpace.L, UiFactory.ScaledBand(WeaponRowHeight) * 0.5f);
            TMP_Text build = UiFactory.Label("Build", weapon, _model.BuildLine, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform buildRect = UiFactory.Fill((RectTransform)build.transform);
            buildRect.offsetMin = new Vector2(WeaponRowHeight + (int)UiSpace.L, 0f);
            buildRect.offsetMax = new Vector2(0f, -UiFactory.ScaledBand(WeaponRowHeight) * 0.5f);

            stack.Gap(UiSpace.L);
            stack.Add(UiBrushKit.Hairline(host, 1f).rectTransform, UiTokens.StrokeHairline);   // C6 row rule (the stack stretches it)
            stack.Gap(UiSpace.L);

            // COMBAT and RUN side by side: the report is wide, so the rows sit in two columns rather than one tall list.
            RectTransform rows = UiFactory.NewRect("Rows", host);
            int lines = Mathf.Max(_model.Combat.Count, _model.Economy.Count);
            float rowsHeight = UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS + lines * UiFactory.ScaledBand(RowHeight);
            stack.Add(rows, rowsHeight);
            BuildRowColumn(rows, 0f, 0.48f, UiStrings.Get("results.combat", "COMBAT"), _model.Combat, null);
            BuildRowColumn(rows, 0.52f, 1f, UiStrings.Get("results.run", "RUN"), _model.Economy, UiType.Body);
            stack.Gap(UiSpace.M);

            TMP_Text note = UiFactory.Label("Note", host, _model.CombatNote, UiType.Caption, UiColor.TextSecondary);
            stack.Add((RectTransform)note.transform, UiFactory.LineHeight(UiType.Caption) * 2f);
            return stack.Cursor + BlockPad;
        }

        /// <summary>Top + bottom padding of a block (WP-098B: none - a block is its words, not a card).</summary>
        private static float BlockPad
        {
            get { return 2f * (int)BlockPadding; }
        }

        /// <summary>
        /// WP-098B (KILAVUZ §1 "iz, yüzey değil"): a block is an open column on the Window scrim - no frame, no panel fill,
        /// no inner padding (the kit's Window ink is the text's ground). "Card" is kept as the clipped host's name.
        /// </summary>
        private static RectTransform BlockHost(RectTransform column)
        {
            RectTransform card = UiFactory.NewRect("Card", column);
            UiFactory.Fill(card);
            return UiFactory.ClipContent(UiFactory.CardContent(card, BlockPadding));
        }

        /// <summary>A block's heading: its words in TextPrimary - no vermilion heading tick in a brush window (KILAVUZ §1).</summary>
        private static RectTransform SectionHeading(string words, RectTransform host)
        {
            TMP_Text heading = UiFactory.Label("Heading", host, words, UiType.H2, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            heading.enableWordWrapping = false;
            return heading.rectTransform;
        }

        private static void BuildRowColumn(RectTransform parent, float from, float to, string label, List<UiStatLine> lines, UiType? type)
        {
            RectTransform column = UiFactory.NewRect(label, parent);
            column.anchorMin = new Vector2(from, 0f);
            column.anchorMax = new Vector2(to, 1f);
            column.offsetMin = Vector2.zero;
            column.offsetMax = Vector2.zero;
            var stack = new UiStack(column);
            TMP_Text heading = UiFactory.Label("Label", column, label, UiType.Label, UiColor.TextSecondary);
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
            RectTransform host = BlockHost(column);
            var stack = new UiStack(host);

            stack.Add(SectionHeading(UiStrings.Get("results.rewards", "REWARDS"), host), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.M);

            // EXPERIENCE: the label left, the total (sakura, NumericL) right, the breakdown under it.
            RectTransform xpRow = UiFactory.NewRect("Xp", host);
            float xpLine = UiFactory.LineHeight(UiType.NumericL);
            stack.Add(xpRow, xpLine);
            TMP_Text xpLabel = UiFactory.Label("Label", xpRow, UiStrings.Get("results.xp", "EXPERIENCE"), UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
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
            // What must follow the breakdown (AD 12b MUST-1 / 12c MUST-4: the first card, its "and N more", the forward
            // line - never traded for the breakdown): measured first, so the rows take what is left.
            float rowBand = UiFactory.ScaledBand(32f);
            // A caption's band here is its line box: LineHeight(Caption) is already text-scaled (FontSize × TextScale), and
            // ScaledBand over it scaled the text scale in twice (45 px for a 36 px line at 1.25) - 12c MUST-4's missing 13 px.
            float captionBand = UiFactory.LineHeight(UiType.Caption);
            float nextLine = captionBand;
            float after = (_model.AshEarned > 0 ? (int)UiSpace.S + UiFactory.LineHeight(UiType.Label) : 0f)
                          + 2f * (int)UiSpace.M + UiTokens.StrokeHairline;
            float entry = UiFactory.ScaledBand(RewardEntryHeight);
            float more = captionBand;
            int listed = _model.NewUnlocks.Count + _model.NewAchievements.Count;
            float listFirst = listed == 0
                ? UiFactory.LineHeight(UiType.Body)
                : entry + (int)UiSpace.S + (listed > 1 ? more : 0f);
            float levelBlock = (int)UiSpace.M + UiFactory.LineHeight(UiType.H3) + (int)UiSpace.S + 14f
                               + (int)UiSpace.XS + captionBand;
            float forward = string.IsNullOrEmpty(_model.NextUnlockLine) ? 0f : nextLine;
            float tail = levelBlock + forward + after + listFirst + BlockPad;
            int rowsFit = Mathf.Max(0, Mathf.FloorToInt((blockRoom - stack.Cursor - tail) / rowBand));
            for (int i = 0; i < _model.XpBreakdown.Count; i++)
            {
                if (!ShowsBreakdownRow(i, rowsFit))
                {
                    continue;
                }

                TMP_Text value = UiFactory.StatRow(_model.XpBreakdown[i].Label, _model.XpBreakdown[i].Value, host, _model.XpBreakdown[i].Color, UiType.Body, 120f);
                stack.Add((RectTransform)value.transform.parent, rowBand);
            }

            stack.Gap(UiSpace.M);

            // The level line, LEVEL UP at its right (stamped in its beat), the bar revealed before → after.
            RectTransform levelRow = UiFactory.NewRect("Level", host);
            stack.Add(levelRow, UiFactory.LineHeight(UiType.H3));
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
            stack.Add((RectTransform)barCaption.transform, captionBand);
            // E26's forward look (12b, AD R1-D SHOULD-1): what the next level brings - only while the list below still gets its
            // first card and its "and N more" (or its one line: the empty state) inside the block's room (AD 12b MUST-1).
            if (!string.IsNullOrEmpty(_model.NextUnlockLine)
                && stack.Cursor + nextLine + after + listFirst + BlockPad <= blockRoom)
            {
                TMP_Text next = UiFactory.Label("NextUnlock", host, _model.NextUnlockLine, UiType.Caption, UiColor.TextSecondary);
                stack.Add((RectTransform)next.transform, nextLine);
            }

            if (_model.AshEarned > 0)
            {
                stack.Gap(UiSpace.S);
                TMP_Text ash = UiFactory.Label("Path", host,
                    UiStrings.Get("path.results.delta", "PATH +{value} Ash").Replace("{value}", _model.AshEarned.ToString()), UiType.Label, UiColor.TextPrimary);
                stack.Add((RectTransform)ash.transform, UiFactory.LineHeight(UiType.Label));
            }

            stack.Gap(UiSpace.M);
            stack.Add(UiBrushKit.Hairline(host, 1f).rectTransform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.M);

            // Unlocks first, then achievements, as many cards as the column holds (measured); the rest summarised.
            float room = blockRoom - BlockPad - stack.Cursor;
            int total = _model.NewUnlocks.Count + _model.NewAchievements.Count;
            if (total == 0)
            {
                TMP_Text empty = UiFactory.Label("Empty", host, UiStrings.Get("results.empty", "No new unlocks this run."), UiType.Body, UiColor.TextSecondary);
                stack.Add((RectTransform)empty.transform, UiFactory.LineHeight(UiType.Body));
                return stack.Cursor + BlockPad;
            }

            // "and N more" is reserved only when not every entry fits (a single unlock gets its card, not "1 new").
            int fits = Mathf.Max(0, Mathf.FloorToInt(room / (entry + (int)UiSpace.S)));
            if (fits < total)
            {
                fits = Mathf.Max(0, Mathf.FloorToInt((room - more) / (entry + (int)UiSpace.S)));
            }
            int shown = 0;
            for (int i = 0; i < _model.NewUnlocks.Count && shown < fits; i++, shown++)
            {
                AddReward(ref stack, host, _model.NewUnlocks[i], UiColor.TextSecondary, false, entry);   // 12b MUST-2: cream frame
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

            return stack.Cursor + BlockPad;
        }

        /// <summary>
        /// AD 12c MUST-4: which XP breakdown rows print when only <paramref name="fit"/> of them fit - the rows that add
        /// nothing ("+0") yield first, then the last rows; the total over them always prints.
        /// </summary>
        private bool ShowsBreakdownRow(int index, int fit)
        {
            List<UiStatLine> rows = _model.XpBreakdown;
            if (fit >= rows.Count)
            {
                return true;
            }

            int kept = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Value == "+0")
                {
                    continue;
                }

                if (i == index)
                {
                    return kept < fit;
                }

                kept++;
            }

            if (rows[index].Value != "+0")
            {
                return false;
            }

            // Zero rows only in what is left after every row with a value.
            int valued = kept;
            int zeroBefore = 0;
            for (int i = 0; i < index; i++)
            {
                zeroBefore += rows[i].Value == "+0" ? 1 : 0;
            }

            return valued + zeroBefore < fit;
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
        /// A new unlock or achievement: the medallion or unlock glyph, its name and detail, the NEW chip. WP-098B (KILAVUZ §1
        /// "iz, yüzey değil"): no card under it any more - no raised fill, no accent frame - and the NEW chip holds still
        /// (no motion behind words); the achievement's state tint stays on its name and medallion.
        /// </summary>
        private RectTransform BuildRewardEntry(RectTransform host, ProfileUnlockEntry entry, UiColor accent)
        {
            // AD 12b MUST-2 (§2.1): an unlock reads a TextPrimary name and its glyph in the content's own tint (a weapon's
            // identity tint); sakura is the NEW chip's alone. Achievements keep their state tint.
            bool achievement = accent == UiColor.StateSuccess;
            UiColor nameColour = achievement ? accent : UiColor.TextPrimary;
            RectTransform card = UiFactory.NewRect("Reward", host);
            RectTransform content = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.M));

            float markSize = RewardEntryHeight - (int)UiSpace.M * 2;
            string typeLabel = string.Empty;
            Sprite markSprite = achievement
                ? UiArt.Medal(_model.MedalFor(entry.Title))
                : _model.UnlockSpriteFor(entry.Title, out typeLabel);
            Image mark = UiFactory.NewImage("Mark", content, achievement ? accent : _model.UnlockTintFor(entry.Title), markSprite);
            mark.preserveAspect = true;
            var markRect = (RectTransform)mark.transform;
            markRect.anchorMin = new Vector2(0f, 0.5f);
            markRect.anchorMax = new Vector2(0f, 0.5f);
            markRect.pivot = new Vector2(0f, 0.5f);
            markRect.sizeDelta = new Vector2(markSize, markSize);
            markRect.anchoredPosition = Vector2.zero;

            float textLeft = markSize + (int)UiSpace.M;
            string titleText = ProfileContentCopy.RewardTitle(entry);
            if (!string.IsNullOrEmpty(typeLabel))
            {
                titleText = ProfileContentCopy.CosmeticName(titleText);
            }

            TMP_Text title = UiFactory.Label("Title", content, UiFactory.Clip(titleText, 32), UiType.BodyStrong, nameColour, TextAnchor.LowerLeft);
            RectTransform titleRect = UiFactory.Fill((RectTransform)title.transform);
            titleRect.offsetMin = new Vector2(textLeft, UiFactory.ScaledBand(RewardDetailBand));
            titleRect.offsetMax = new Vector2(-64f, 0f);

            string localizedDetail = ProfileContentCopy.RewardDetail(entry);
            string detailText = string.IsNullOrEmpty(typeLabel) ? localizedDetail : typeLabel + "  ·  " + localizedDetail;
            TMP_Text detail = UiFactory.Label("Detail", content, UiFactory.Clip(detailText, 120), UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            RectTransform detailRect = UiFactory.Fill((RectTransform)detail.transform);
            detailRect.offsetMin = new Vector2(textLeft, 0f);
            detailRect.offsetMax = new Vector2(-64f, -UiFactory.ScaledBand(RewardTitleBand));

            // AD v004 MUST-1: NEW is one token everywhere - ink on sakura - whatever the card's accent.
            TMP_Text badge = UiFactory.Chip("New", content, UiStrings.Get("tree.flag.new", "NEW"), UiColor.AccentSakura, UiColor.SurfaceBase, 56f, 24f);
            var badgeRect = (RectTransform)badge.transform.parent;
            badgeRect.anchorMin = new Vector2(1f, 0.5f);
            badgeRect.anchorMax = new Vector2(1f, 0.5f);
            badgeRect.pivot = new Vector2(1f, 0.5f);
            badgeRect.anchoredPosition = Vector2.zero;
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
            StartCount();
            yield return new WaitForSecondsRealtime(Look.CountPhaseSeconds);

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
                InkNewBest(true);   // the words show and the kit's C8 inks in under them (Reselect 400)
                Punch(_newBestText.rectTransform, 0f);
                UiEvents.Raise(UiEvent.NewBestRound);
            }

            Finish();
        }

        /// <summary>
        /// WP-098B: the four numbers on one motion timeline - each counts 0 → its value over <see cref="Look.CountDuration"/>
        /// (Delta: Reduced Motion lands it at once), <see cref="Look.CountStagger"/> apart in reading order, and lands with a
        /// punch and a tok. One roll cue for the group, a settle cue per landing (S-08: UiEvent.CounterRolling /
        /// CounterSettled, already mapped). Every step and cue is in the timeline, so a skip (Complete) lands them all and
        /// collapses the cues to the last settle - the roll voice stops with the numbers.
        /// </summary>
        private void StartCount()
        {
            int owner = GetInstanceID();
            _count = UiMotion.Timeline(owner);
            _count.Cue(UiEvent.CounterRolling, 0f);
            float roll = UiMotionTokens.Seconds(Look.CountDuration);
            int[] values = { _model.HeroRound, _model.HeroKills, _model.HeroGold };
            for (int i = 0; i < 3; i++)
            {
                float at = i * Look.CountStagger;
                _count.Add(UiMotion.Count(_tiles[i].ValueText, 0, values[i], Look.CountDuration, UiEase.Settle, UiMotionClass.Delta, at, UiClock.Unscaled, owner));
                if (values[i] > 0)
                {
                    Land(i, at + roll);
                }
            }

            // The clock rolls on the same curve; TickClock writes the m:ss it has reached.
            float clockAt = 3f * Look.CountStagger;
            _clockRoll.fillAmount = 0f;
            _clockShown = -1;
            _clockRolling = true;
            _count.Add(UiMotion.Fill(_clockRoll, 1f, Look.CountDuration, UiEase.Settle, UiMotionClass.Delta, clockAt, UiClock.Unscaled, owner));
            Land(3, clockAt + roll);
        }

        /// <summary>A landing: the number (not its caption or its line) punches, and the counter toks.</summary>
        private void Land(int tile, float at)
        {
            _count.Add(Punch(_tiles[tile].ValueText.rectTransform, at));
            _count.Cue(UiEvent.CounterSettled, at);
        }

        /// <summary>The landing punch: <see cref="Look.PunchScale"/> → 1 over <see cref="Look.PunchSeconds"/> (Decor: none under Reduced Motion).</summary>
        private UiTweenHandle Punch(RectTransform rect, float delay)
        {
            return UiMotion.ScaleSeconds(rect, Look.PunchScale, 1f, Look.PunchSeconds, UiEase.Settle, UiMotionClass.Decor, delay, GetInstanceID());
        }

        /// <summary>The time number while its roll runs: the fill's progress × the run's seconds, written as m:ss without a string.</summary>
        private void TickClock()
        {
            float k = _clockRoll.fillAmount;
            int seconds = k >= 1f ? _model.HeroSeconds : Mathf.RoundToInt(k * _model.HeroSeconds);
            if (seconds != _clockShown)
            {
                _clockShown = seconds;
                int minutes = seconds / 60;
                int rest = seconds % 60;
                _timeValue.SetText("{0}:{1}{2}", minutes, rest / 10, rest % 10);   // = ResultsViewModel.FormatDuration
            }

            if (k >= 1f)
            {
                _clockRolling = false;
            }
        }

        /// <summary>The end state: every value final, every stamp shown, MAIN MENU focused (the second press leaves).</summary>
        private void Finish()
        {
            _playing = false;
            if (_count.IsAlive)
            {
                _count.Complete();   // a skip: the rolls and punches land, the cues collapse to the last settle
            }

            _tiles[0].SetNow(_model.HeroRound);
            _tiles[1].SetNow(_model.HeroKills);
            _tiles[2].SetNow(_model.HeroGold);
            _clockRolling = false;
            _timeValue.text = _finalTime;
            _xpBar.SetValue(_model.ProgressAfter01, UiMotion.ReducedMotion);
            if (_levelUpGroup != null)
            {
                _levelUpGroup.alpha = 1f;
            }

            for (int i = 0; i < _rewardGroups.Count; i++)
            {
                _rewardGroups[i].alpha = 1f;
            }

            InkNewBest(false);   // a skip / Reduced Motion: the C8 lands drawn (a cut), never an ink-in after the skip

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

        /// <summary>
        /// The save note and MAIN MENU - the kit's C11 (Menu.Home glyph + the word, unfilled, 64 high, as wide as its word);
        /// focus on it is the kit's cream brackets. The note takes the row's width up to the button.
        /// </summary>
        private void BuildFooter(bool focusNow)
        {
            RectTransform footer = UiFactory.NewRect("Footer", _page);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, UiBrushTokens.ControlHeight);
            footer.anchoredPosition = Vector2.zero;

            UiButton continueButton = UiBrushKit.Button(footer, "MainMenu", "common.main_menu", "MAIN MENU", "Menu.Home", PrototypeUiFlow.GoToMainMenu);
            RectTransform rect = (RectTransform)continueButton.transform;
            float width = rect.sizeDelta.x;   // the kit's measured width
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            TMP_Text note = UiFactory.Label(
                "FooterNote", footer, _model.FooterNote, UiType.Caption,
                _model.SaveFailed ? UiColor.StateWarning : UiColor.TextSecondary, TextAnchor.MiddleLeft);
            RectTransform noteRect = UiFactory.Fill((RectTransform)note.transform);
            noteRect.offsetMax = new Vector2(-(width + (int)UiSpace.XL), 0f);

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

            if (_clockRolling)
            {
                TickClock();
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
            BuildHeader(ResultsViewModel.Title, _model.Eyebrow);

            // WP-098B: the notice is its words on the Window ink - no card frame (the name is kept).
            RectTransform card = UiFactory.NewRect("EmptyCard", _page);
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
            _pane.Open(_continueButton);   // nothing plays here: MAIN MENU is the window's focus at once
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
