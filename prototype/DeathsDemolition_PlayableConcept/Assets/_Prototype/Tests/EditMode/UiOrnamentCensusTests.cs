using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 12b · E5 (direction §14 AD MUST-4, the ornament rules of §1 into T6): per screen, vermilion ornaments ≤ 2
    /// (the heading tick, and the seal only at boot / logo), brush underline ≤ 1, ceremony elements ≤ 1 (seal stamp /
    /// leaf glint / ink bleed). No per-screen runtime count existed (the WP-038J count code is a render-cost audit, not
    /// an ornament census), so this is a source census in the manner of T6's drift lock (UiMotionCoreTests): every
    /// screen's files, comments stripped, construction sites counted -
    ///   vermilion  one heading tick per screen when it calls <c>UiFactory.Heading(</c> (panes replace each other), plus
    ///              every <c>UiColor.AccentPrimary</c> on an image / frame / tint line (a Primary button or the BUY pill
    ///              is a call to action, not an ornament: its colour comes from the variant or the chip's state);
    ///   brush      <c>UiInkChannel.Underline</c> / <c>Ink.BrushUnderline</c> sites;
    ///   ceremony   the kinds present: seal stamp (<c>StampIn(</c> on a seal), leaf glint (<c>UiFxMode.Leaf</c>),
    ///              ink bleed (<c>UiInkChannel.Blot</c>).
    /// Two screens hold a counted exception, locked at today's count (they may only go down) and listed for the AD:
    /// the HUD's brush sites (the 8-R ink HUD draws its banner ribbon, cluster strokes and status stamps with the
    /// brush mask - L0 material, not an H1 underline) and the curtain file's two ceremony kinds (boot: blot + seal
    /// stamp as one sequence, 10b-2; the loading ink bleed is a different screen in the same file).
    /// WP-094 HUD v4 (R3, S-09 decisions (a) and (b), C-00 07.10; the AD confirms at delivery): the HUD holds two NAMED
    /// exceptions - ONE vermilion seal (the round's seal accent, "WP-094 user note 3: kırmızı mühür aksanı"; SealLock 1,
    /// any second vermilion seal fails) and TWO ceremony kinds (the leaf glint on the gold and the Special, and the round
    /// seal's stamp at the round change; CeremonyLock 2 - an ink bleed on the HUD would still fail).
    /// A new screen file must be placed in a group, or the census fails.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiOrnamentCensusTests
    {
        public const int MaxVermilion = 2;
        public const int MaxBrush = 1;
        public const int MaxCeremony = 1;

        private sealed class Group
        {
            public string Name;
            public string[] Files;          // relative to UI/Runtime
            public int BrushLock = -1;      // ≥ 0: a counted exception locked at this count
            public int CeremonyLock = -1;
            public bool SealAllowed;
            public int SealLock = -1;       // ≥ 0: vermilion seals allowed outside boot / logo - a named exception at this count
        }

        private static readonly Group[] Groups =
        {
            // WP-081 B (main-ci d7ecac1b): the cinematic world behind the menu and its look profile are the menu screen's own
            // files - counted with it, so an ornament added there spends the menu's budget.
            new Group { Name = "MainMenu", Files = new[] { "Screens/MainMenuScreen.cs", "Screens/MenuCinematic.cs", "Screens/MenuLook.cs" } },
            new Group { Name = "Selection", Files = new[] { "Screens/SelectionScreen.cs" } },
            new Group { Name = "Results", Files = new[] { "Screens/ResultsScreen.cs" } },
            new Group { Name = "Pause", Files = new[] { "Screens/PauseScreen.cs" } },
            new Group { Name = "Shop", Files = new[] { "Screens/UpgradeScreen.cs", "Screens/UpgradeTree.cs", "Screens/UiTreeEdges.cs" } },
            new Group
            {
                Name = "HUD",
                Files = new[]
                {
                    "Screens/RunHudScreen.cs", "Screens/RunHudScreen.Ink.cs", "Screens/HudBanner.cs", "Screens/HudReticle.cs",
                    "Screens/HudPlayerArcs.cs", "Screens/HudRearEdge.cs", "Kit/UiEnemyStatusMarkers.cs", "Kit/UiWorldMarker.cs",
                    // WP-094 HUD v4 (R3): the round numeral and the gold target are the HUD's own files - counted with it.
                    "Hud/HudRoundNumeral.cs", "Hud/HudGoldTarget.cs", "Hud/HudGoldReadout.cs", "Hud/RomanNumerals.cs",
                    // WP-094 juice: the HUD's juice partial, its rules and its feel-event seam.
                    "Screens/RunHudScreen.Juice.cs", "Hud/HudJuiceRules.cs", "Hud/HudFeelSource.cs"
                },
                BrushLock = 3,
                // WP-094 (S-09 decision (b)): the leaf glint (gold / Special) + the round seal's stamp (R3) - 1 → 2.
                CeremonyLock = 2,
                // WP-094 user note 3: kırmızı mühür aksanı - the round's one vermilion seal (S-09 decision (a)).
                SealLock = 1,
            },
            new Group { Name = "Curtain (boot / loading / ink)", Files = new[] { "Flow/UiCurtain.cs" }, CeremonyLock = 2, SealAllowed = true },
        };

        private static readonly Regex Heading = new Regex(@"UiFactory\.Heading\(");
        private static readonly Regex VermilionImage = new Regex(@"^.*(NewImage\(|FramedCard\(|\.SetColor\(|Tint\().*UiColor\.AccentPrimary\b.*$", RegexOptions.Multiline);
        private static readonly Regex Seal = new Regex(@"seal", RegexOptions.IgnoreCase);
        private static readonly Regex Brush = new Regex(@"UiInkChannel\.Underline|Ink\.BrushUnderline");
        private static readonly Regex SealStamp = new Regex(@"StampIn\([^;]*[Ss]eal");
        private static readonly Regex LeafGlint = new Regex(@"UiFxMode\.Leaf\b");
        private static readonly Regex InkBleed = new Regex(@"UiInkChannel\.Blot\b");

        private static string Root
        {
            get { return Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime"); }
        }

        /// <summary>The source without // and /* */ comments (string and char literals kept, verbatim strings too).</summary>
        public static string StripComments(string source)
        {
            var sb = new StringBuilder(source.Length);
            bool inString = false, verbatim = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (inString)
                {
                    sb.Append(c);
                    if (verbatim && c == '"' && i + 1 < source.Length && source[i + 1] == '"')
                    {
                        sb.Append(source[++i]);
                    }
                    else if (!verbatim && c == '\\' && i + 1 < source.Length)
                    {
                        sb.Append(source[++i]);
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '\'')
                {
                    // A char literal: copied whole, so '"' never opens a string.
                    sb.Append(c);
                    while (++i < source.Length)
                    {
                        sb.Append(source[i]);
                        if (source[i] == '\\' && i + 1 < source.Length) sb.Append(source[++i]);
                        else if (source[i] == '\'') break;
                    }
                }
                else if (c == '"')
                {
                    inString = true;
                    verbatim = i > 0 && source[i - 1] == '@';
                    sb.Append(c);
                }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    sb.Append('\n');
                }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        [Test]
        public void YorumAyiklama_KodKalir()
        {
            string code = StripComments("a(); // UiFactory.Heading(\n/* UiFxMode.Leaf */ b(\"// kept\");");
            StringAssert.DoesNotContain("Heading", code);
            StringAssert.DoesNotContain("Leaf", code);
            StringAssert.Contains("a();", code);
            StringAssert.Contains("\"// kept\"", code);
        }

        [Test]
        public void HerEkranDosyasiBirGrupta()
        {
            var placed = new HashSet<string>(StringComparer.Ordinal);
            foreach (Group group in Groups)
            {
                foreach (string file in group.Files)
                {
                    Assert.IsTrue(File.Exists(Path.Combine(Root, file)), group.Name + ": " + file + " yok (grup listesi eskidi)");
                    placed.Add(Path.GetFileName(file));
                }
            }

            var missing = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(Root, "Screens"), "*.cs"))
            {
                if (!placed.Contains(Path.GetFileName(file)))
                {
                    missing.Add(Path.GetFileName(file));
                }
            }

            Assert.IsEmpty(missing, "ornament sayımında grubu olmayan ekran dosyası (E5):\n" + string.Join("\n", missing));
        }

        [Test]
        public void OrnamentSayimi_EkranBasina_Vermilyon2_Firca1_Toren1()
        {
            var failures = new List<string>();
            var table = new StringBuilder("[DD-TEST] wp068.ornamentCensus (vermilion / brush / ceremony kinds)");
            foreach (Group group in Groups)
            {
                int vermilion = 0, brush = 0, seals = 0;
                bool stamp = false, leaf = false, bleed = false;
                bool tick = false;
                foreach (string file in group.Files)
                {
                    string code = StripComments(File.ReadAllText(Path.Combine(Root, file)));
                    tick |= Heading.IsMatch(code);
                    foreach (Match m in VermilionImage.Matches(code))
                    {
                        vermilion++;
                        if (Seal.IsMatch(m.Value)) seals++;
                    }

                    brush += Brush.Matches(code).Count;
                    stamp |= SealStamp.IsMatch(code);
                    leaf |= LeafGlint.IsMatch(code);
                    bleed |= InkBleed.IsMatch(code);
                }

                if (tick) vermilion++;
                int ceremony = (stamp ? 1 : 0) + (leaf ? 1 : 0) + (bleed ? 1 : 0);
                table.Append('\n').Append(group.Name).Append(": ").Append(vermilion).Append(" / ").Append(brush).Append(" / ").Append(ceremony)
                     .Append(tick ? " (tick)" : string.Empty).Append(stamp ? " (seal stamp)" : string.Empty)
                     .Append(leaf ? " (leaf glint)" : string.Empty).Append(bleed ? " (ink bleed)" : string.Empty);

                if (vermilion > MaxVermilion) failures.Add(group.Name + ": vermilion ornaments " + vermilion + " > " + MaxVermilion);
                int sealLimit = group.SealAllowed ? int.MaxValue : Math.Max(0, group.SealLock);
                if (seals > sealLimit) failures.Add(group.Name + ": a vermilion seal outside boot / logo (" + seals + " > " + sealLimit + " named)");

                int brushLimit = group.BrushLock >= 0 ? group.BrushLock : MaxBrush;
                if (brush > brushLimit) failures.Add(group.Name + ": brush underline sites " + brush + " > " + brushLimit);

                int ceremonyLimit = group.CeremonyLock >= 0 ? group.CeremonyLock : MaxCeremony;
                if (ceremony > ceremonyLimit) failures.Add(group.Name + ": ceremony kinds " + ceremony + " > " + ceremonyLimit);
            }

            Debug.Log(table.ToString());
            Assert.IsEmpty(failures, "ornament sayımı (AD §14 MUST-4):\n" + string.Join("\n", failures));
        }
    }
}
