using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Achievements;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using TMPro;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 TR · the Turkish language pack in the UI: the pack is whole in both columns, every literal key the UI asks for
    /// exists, Settings › LANGUAGE persists and sets <see cref="UiStrings.Language"/>, and upper-casing follows the language.
    /// </summary>
    public sealed class UiLanguageTests
    {
        private static readonly Regex LiteralKey = new Regex("UiStrings\\.(?:Get|Format)\\(\\s*\"([^\"]+)\"\\s*[,)]", RegexOptions.Compiled);

        private string _dir;

        private static string RepoRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        [SetUp]
        public void SetUp()
        {
            UiStrings.ResetForTests();
            UiStrings.Language = UiStrings.English;
            _dir = Path.Combine(Path.GetTempPath(), "dd-ui-language-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.SetPathOverride(null);
            UiStrings.Language = UiStrings.English;
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void Paket_IkiKopyaBaytBayt_HerAnahtarEnVeTrDolu()
        {
            string planning = Path.Combine(RepoRoot, "planning", "content", "r1-strings.json");
            string shipped = Path.Combine(Application.dataPath, "_Prototype", "UI", "Resources", "Strings", "r1-strings.json");
            CollectionAssert.AreEqual(File.ReadAllBytes(planning), File.ReadAllBytes(shipped), "the two pack files are byte-equal");

            Dictionary<string, Dictionary<string, string>> table = UiStrings.Parse(File.ReadAllText(planning));
            Assert.Greater(table.Count, 800);
            var eksik = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, string>> row in table)
            {
                string en, tr;
                if (!row.Value.TryGetValue("en", out en) || string.IsNullOrEmpty(en)) eksik.Add(row.Key + " (en)");
                if (!row.Value.TryGetValue("tr", out tr) || string.IsNullOrEmpty(tr)) eksik.Add(row.Key + " (tr)");
            }

            Assert.IsEmpty(eksik, "every key has en and tr:\n" + string.Join("\n", eksik));
        }

        [Test]
        public void KaynakTaramasi_HerSabitAnahtarPakette()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            var eksik = new List<string>();
            int seen = 0;
            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match m in LiteralKey.Matches(File.ReadAllText(path)))
                {
                    seen++;
                    if (!UiStrings.Has(m.Groups[1].Value))
                    {
                        eksik.Add(Path.GetFileName(path) + ": " + m.Groups[1].Value);
                    }
                }
            }

            Assert.Greater(seen, 300, "the scan found the UI's pack calls");
            Assert.IsEmpty(eksik, "UiStrings keys missing from the pack:\n" + string.Join("\n", eksik));
        }

        [Test]
        public void DilAyari_EskiDosyaIngilizce_TrKalici_UiStringsDiliniKurar()
        {
            string file = Path.Combine(_dir, UiSettings.FileName);
            File.WriteAllText(file, "{\"Version\":2,\"UiScale\":1.0,\"MasterVolume\":0.5}");
            UiSettings.SetPathOverride(file);
            Assert.AreEqual(UiStrings.English, UiSettings.Language, "an older file without the key reads English");
            Assert.AreEqual(UiStrings.English, UiStrings.Language);
            Assert.AreEqual("LANGUAGE", UiStrings.Get("settings.row.language", "LANGUAGE"));

            UiSettings.Language = UiStrings.Turkish;
            Assert.AreEqual(UiStrings.Turkish, UiStrings.Language, "the setting sets the column the UI reads");
            Assert.AreEqual("DİL", UiStrings.Get("settings.row.language", "LANGUAGE"));
            StringAssert.Contains("\"Language\": \"tr\"", File.ReadAllText(file).Replace("\":\"", "\": \""));

            UiStrings.Language = UiStrings.English;   // what a fresh session starts with
            UiSettings.SetPathOverride(file);          // reload from disk
            Assert.AreEqual(UiStrings.Turkish, UiSettings.Language, "tr survives a reload");
            Assert.AreEqual(UiStrings.Turkish, UiStrings.Language, "loading the settings applies the saved language");

            UiSettings.Language = "de";
            Assert.AreEqual(UiStrings.English, UiSettings.Language, "only en / tr");
            Assert.AreEqual(UiStrings.English, UiStrings.Language);

            File.WriteAllText(file, "{\"Version\":2,\"Language\":\"xx\"}");
            UiSettings.SetPathOverride(file);
            Assert.AreEqual(UiStrings.English, UiSettings.Language, "an unknown code on disk reads English");
        }

        [Test]
        public void Upper_TurkceIleNoktaliI_IngilizceIleDuz()
        {
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("İSTATİSTİK", UiStrings.Upper("istatistik"));
            Assert.AreEqual("KILIÇ", UiStrings.Upper("kılıç"));
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("ISTATISTIK", UiStrings.Upper("istatistik"));
            Assert.AreEqual(string.Empty, UiStrings.Upper(null));
        }

        [Test]
        public void Display_TR_TamSatirTextSdf_ENDisplay_MetrikCizilenYuzden()
        {
            TMP_FontAsset text = UiFonts.Role(UiFontRole.Text);
            TMP_FontAsset display = UiFonts.Role(UiFontRole.Display);
            foreach (UiType type in new[] { UiType.Display, UiType.Banner, UiType.H1, UiType.H2 })
            {
                UiStrings.Language = UiStrings.Turkish;
                Assert.AreSame(text, UiFonts.For(type), "TR heading has one complete face, not mixed glyph fallback");
                Assert.AreEqual(text.faceInfo.lineHeight / text.faceInfo.pointSize, UiFactory.FaceLineRatio(type), 0.001f);
                foreach (char glyph in "KOŞU RAPORU PROFİL SEVİYE ĞğİıŞş")
                {
                    Assert.IsTrue(text.characterLookupTable.ContainsKey(glyph), "the drawing face owns " + glyph);
                }

                UiStrings.Language = UiStrings.English;
                Assert.AreSame(display, UiFonts.For(type));
                Assert.AreEqual(display.faceInfo.lineHeight / display.faceInfo.pointSize, UiFactory.FaceLineRatio(type), 0.001f);
            }
        }

        [Test]
        public void Agac_AdVeOnkosul_AyniStringKey_TR_EN_DetayCanliArguman()
        {
            var node = new RunTreeNodeView { StringKey = "tree.run.key.piercing", DisplayName = "Clean Line", ShortName = "Line" };
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("Hat", UiRunTreePanel.NameOf(node));
            var fit = typeof(UiRunTreePanel).GetMethod("Fit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            string needs = (string)fit.Invoke(null, new object[] { null, UiStrings.Get("tree.state.prereq_missing"), node, false });
            StringAssert.Contains("HAT", needs);
            StringAssert.DoesNotContain("Clean Line", needs);
            node.StringKey = "tree.run.branch.piercing";
            Assert.AreEqual("Delme", UiRunTreePanel.NameOf(node));
            node.StringKey = "tree.run.fork.sr_1.x";
            node.ArgNames = new[] { "seconds", "value" };
            node.ArgValues = new[] { 2.5f, 2f };
            Assert.AreEqual("Atıştan önce 2.5 sn bekle, ×2 hasar.", UiRunTreePanel.DescriptionOf(node));
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("Hold Breath", UiRunTreePanel.NameOf(node));
            Assert.AreEqual("Wait 2.5 s before the shot for ×2 damage.", UiRunTreePanel.DescriptionOf(node));
            node.StringKey = "unknown.node";
            Assert.AreEqual("Clean Line", UiRunTreePanel.NameOf(node), "an unknown mapping keeps its real catalog name");
        }

        [Test]
        public void Agac_TR_TierDetayi_CanliTuningVeMaxKademeyiIzler()
        {
            WeaponUpgradeTuning tuning = WeaponUpgradeTuning.Active;
            float old = tuning.FireRateBonusPerTier, oldMax = tuning.MaximumFireRateMultiplier;
            try
            {
                tuning.FireRateBonusPerTier = 0.17f;
                tuning.MaximumFireRateMultiplier = 2.2f;
                var node = new RunTreeNodeView { Kind = RunTreeNodeKind.Tier, HasBranch = true,
                    Branch = UpgradeBranch.FireRate, OwnedTier = 1, MaxTier = 2, StringKey = "tree.run.branch.fire_rate" };
                UiStrings.Language = UiStrings.Turkish;
                Assert.AreEqual("Atış hızı +%17 (toplam 1.34x, üst sınır 2.20x).", UiRunTreePanel.DescriptionOf(node));
                node.OwnedTier = 2;
                Assert.AreEqual("Atış hızı +%17 (toplam 1.34x, üst sınır 2.20x).", UiRunTreePanel.DescriptionOf(node), "MAX describes the current tier, not a nonexistent third tier");
                foreach (UpgradeBranch branch in System.Enum.GetValues(typeof(UpgradeBranch)))
                {
                    string detail = UiUpgradeCopy.TierDescription(branch, 1, "");
                    Assert.IsNotEmpty(detail, branch.ToString());
                    StringAssert.DoesNotContain("{", detail, branch.ToString());
                }
                string blast = UiUpgradeCopy.TierDescription(UpgradeBranch.Explosion, 1, "tree.run.branch.blast");
                Assert.AreEqual("Patlama yarıçapı +1 m, patlama hasarı +%24.", blast, "Blast reads live rocket constants, not Explosion chance wording");
            }
            finally
            {
                tuning.FireRateBonusPerTier = old;
                tuning.MaximumFireRateMultiplier = oldMax;
            }
        }

        [Test]
        public void Shop_TR_EN_SayilarKilitVeKalanHak_DoluYerTutucular()
        {
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("Henüz bir şey alınmadı. 123 altının tümü sonraki rounda geçer.", UiUpgradeCopy.Footer(10, 123, 0, false));
            Assert.AreEqual("Hemen uygulandı. 2 seçimden 1 kullanıldı, 1 kaldı. 37 altın sonraki rounda geçer.", UiUpgradeCopy.Footer(2, 37, 1, false));
            StringAssert.Contains("16777217 altın", UiUpgradeCopy.Footer(2, 16777217, 1, false), "gold remains an exact integer above float precision");
            StringAssert.Contains("Tüm seçimler kullanıldı", UiUpgradeCopy.Footer(3, 99, 3, true));
            StringAssert.Contains("Round başına tek geliştirme", UiUpgradeCopy.Footer(1, 20, 1, true));
            foreach (string language in new[] { UiStrings.Turkish, UiStrings.English })
            {
                UiStrings.Language = language;
                foreach (int limit in new[] { 1, 2, 3, 10 })
                {
                    foreach (string line in new[] { UiUpgradeCopy.Intro(limit), UiUpgradeCopy.Choose(limit), UiUpgradeCopy.PurchaseLimitReached(limit), UiUpgradeCopy.Footer(limit, 37, 1, false) })
                    {
                        Assert.IsNotEmpty(line);
                        StringAssert.DoesNotContain("{", line);
                    }
                }
            }
            Assert.AreEqual("Buy up to two upgrades this round, or reroll the offer once before the first. Unspent gold carries into the next round.", UiUpgradeCopy.Intro(2));
            Assert.AreEqual("Applied immediately. 1 of 2 picks used, 1 left. 37 gold carries into the next round.", UiUpgradeCopy.Footer(2, 37, 1, false));
        }

        [Test]
        public void Controls_TR_EN_EylemVeParca_Cevrilir_KeycapHamKalir()
        {
            var entry = new InputRemapEntry("binding-id", "Gameplay", "Move", InputSchemeKind.KeyboardMouse, "up", "<Keyboard>/w", "<Keyboard>/w", "W", false);
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("Yukarı Hareket", MainMenuScreen.RemapRowName(entry));   // M-69: part first
            Assert.AreEqual("W", entry.DisplayName);
            Assert.AreEqual("HAFİF", MainMenuScreen.AimAssistLabel(InputAimAssistLevel.Light));
            Assert.AreEqual("KAPALI", MainMenuScreen.AimAssistLabel(InputAimAssistLevel.Off));
            Assert.AreEqual("TAM", MainMenuScreen.AimAssistLabel(InputAimAssistLevel.Full));
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("Move Up", MainMenuScreen.RemapRowName(entry));
            Assert.AreEqual("LIGHT", MainMenuScreen.AimAssistLabel(InputAimAssistLevel.Light));
        }

        [Test]
        public void Olum_TR_KatmanCevrilir_BilinmeyenKaynakUydurulmaz_ENKorunur()
        {
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("STANDART ZOMBİ", ResultsViewModel.KillerName("Zombie.Standard.Overhead"));
            Assert.AreEqual("ÖZEL ZOMBİ", ResultsViewModel.KillerName("Zombie.Special.Lunge"));
            Assert.AreEqual("ELİT ZOMBİ", ResultsViewModel.KillerName("Zombie.Elite.Slam"));
            StringAssert.Contains("STANDART ZOMBİ", RunHudViewModel.DeathLine("Zombie.Standard.Swipe"));
            StringAssert.DoesNotContain("killed by", RunHudViewModel.DeathLine("Zombie.Standard.Swipe"));
            StringAssert.Contains("HAZARD FİRE", RunHudViewModel.DeathLine("Hazard.Fire"), "unknown source remains readable and honest");
            Assert.AreEqual(UiStrings.Get("hud.status.down"), RunHudViewModel.DeathLine(null));
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("DOWN  ·  killed by a Standard's swipe", RunHudViewModel.DeathLine("Zombie.Standard.Swipe"));
        }

        // ------------------------------------------------------------ WP-068 R1-E E2: profile copy by identity

        private static object Copy(string method, object argument)
        {
            System.Type copy = typeof(ProfileViewModel).Assembly.GetType("DD.Prototype.UI.ProfileContentCopy");
            Assert.IsNotNull(copy, "ProfileContentCopy");
            MethodInfo info = copy.GetMethod(method, BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(info, method);
            return info.Invoke(null, new[] { argument });
        }

        [Test]
        public void ProfilOdulu_TR_KimlikleCevrilir_IngilizceMetinEslesmesiYok_ENVeriKalir()
        {
            const string reworded = "Reworded English line";
            UiStrings.Language = UiStrings.Turkish;
            // The identity decides, not the English copy: a reworded English line still gets its key ...
            Assert.AreEqual(UiStrings.Get("results.reward.weapon_selectable"), Copy("RewardDetail", new ProfileUnlockEntry("Kodo", reworded, ProfileUnlockKind.Weapon, "ar_1")));
            Assert.AreEqual(UiStrings.Get("results.reward.branch_offered"), Copy("RewardDetail", new ProfileUnlockEntry("Chain", reworded, ProfileUnlockKind.Branch, "Chain")));
            Assert.AreEqual(UiStrings.Get("tree.run.branch.chain.name"), Copy("RewardTitle", new ProfileUnlockEntry("Chain", reworded, ProfileUnlockKind.Branch, "Chain")));
            Assert.AreEqual("Kodo", Copy("RewardTitle", new ProfileUnlockEntry("Kodo", reworded, ProfileUnlockKind.Weapon, "ar_1")), "weapon names are identity");
            Assert.AreEqual("Mystery", Copy("RewardTitle", new ProfileUnlockEntry("Mystery", reworded, ProfileUnlockKind.Branch, "NotABranch")), "an unknown branch id keeps its name");
            // ... and the old English line without an identity is no longer recognised: it shows as is.
            Assert.AreEqual("Classic weapon - selectable before your next run",
                Copy("RewardDetail", new ProfileUnlockEntry("Kodo", "Classic weapon - selectable before your next run")));
            Assert.AreEqual(reworded, Copy("RewardDetail", new ProfileUnlockEntry("First Blood", reworded, ProfileUnlockKind.Achievement, "no_such_achievement")));

            int roster = 0;
            foreach (UnlockDefinition d in UnlockCatalog.Entries)
            {
                string tr = (string)Copy("RewardDetail", new ProfileUnlockEntry(d.DisplayName, reworded, ProfileUnlockKind.Content, d.Id));
                Assert.AreNotEqual(reworded, tr, d.Id + " has its key");
                StringAssert.DoesNotContain("{", tr, d.Id);
                if (d.Type == UnlockContentType.Weapon && d.Condition == UnlockCondition.Level)
                {
                    roster++;
                    Assert.AreEqual(UiStrings.Format("results.reward.weapon_roster", "level", d.NumericKey.ToString()), tr, d.Id);
                }
            }

            if (DD.Prototype.Core.Weapons.WeaponRoster.Enabled.Count > 0)
            {
                Assert.Greater(roster, 0, "roster weapons are read by type + condition");
            }

            foreach (AchievementDefinition d in AchievementCatalog.Definitions)
            {
                string tr = (string)Copy("RewardDetail", new ProfileUnlockEntry(d.DisplayName, reworded, ProfileUnlockKind.Achievement, d.Id));
                Assert.AreNotEqual(reworded, tr, d.Id + " has its key");
                StringAssert.DoesNotContain("{", tr, d.Id);
            }

            UiStrings.Language = UiStrings.English;
            Assert.AreEqual(reworded, Copy("RewardDetail", new ProfileUnlockEntry("Chain", reworded, ProfileUnlockKind.Branch, "Chain")), "English shows the data");
            Assert.AreEqual("Chain", Copy("RewardTitle", new ProfileUnlockEntry("Chain", reworded, ProfileUnlockKind.Branch, "Chain")));
            Assert.AreEqual(reworded, Copy("RewardDetail", new ProfileUnlockEntry("First Blood", reworded, ProfileUnlockKind.Achievement, "first_blood")));
        }

        [Test]
        public void Kozmetik_TR_TurAnahtariyla_AdKimlikKalir_ENVeriKalir()
        {
            var title = new CosmeticEntry("title_first_blood", CosmeticKind.Title, "Title: Blooded", "d", true, false, false);
            var badge = new CosmeticEntry("badge_ghost", CosmeticKind.Badge, "Badge: Ghost", "d", true, false, false);
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual(UiStrings.Format("profile.cosmetic.title_format", "name", "Blooded"), Copy("CosmeticTitle", title));
            Assert.AreEqual(UiStrings.Format("profile.cosmetic.badge_format", "name", "Ghost"), Copy("CosmeticTitle", badge));
            Assert.AreEqual("Blooded", Copy("CosmeticTitle", new CosmeticEntry("x", CosmeticKind.Title, "Blooded", "d", true, false, false)), "no kind label: the name as is");
            Assert.AreEqual("Ivory: Crimson", Copy("CosmeticTitle", new CosmeticEntry("s", CosmeticKind.Skin, "Ivory: Crimson", "d", true, false, false)), "skins untouched");
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("Title: Blooded", Copy("CosmeticTitle", title));
        }

        [Test]
        public void ProfilOdulu_Uretici_TurVeKimlikTasir()
        {
            var ladder = new AllAtOneLadder();
            var profile = new ProfileData { ProfileLineageId = ProfileData.NewLineageId(), ProfileLevel = 1 };
            var granted = new List<ProfileUnlockEntry>();
            ProfileProgression.GrantUnlocksForLevel(profile, ladder, granted);
            Assert.Greater(granted.Count, 0);
            foreach (ProfileUnlockEntry e in granted)
            {
                Assert.That(e.Kind, Is.EqualTo(ProfileUnlockKind.Weapon).Or.EqualTo(ProfileUnlockKind.Branch), e.Title);
                Assert.IsNotEmpty(e.Id, e.Title);
                if (e.Kind == ProfileUnlockKind.Branch)
                    Assert.IsTrue(System.Enum.IsDefined(typeof(UpgradeBranch), System.Enum.Parse(typeof(UpgradeBranch), e.Id)), e.Id);
                else
                    Assert.IsTrue(profile.UnlockedWeapons.Contains(e.Id), "the weapon id is its saved key: " + e.Id);
            }

            // Source census: the copy never compares English lines (the brittle 3c0875e1 form).
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Presentation", "ProfileViewModels.cs"));
            string copy = source.Substring(source.IndexOf("internal static class ProfileContentCopy", System.StringComparison.Ordinal));
            foreach (string english in new[] { "\"Classic weapon", "\"Upgrade branch", "\"Title: ", "\"Badge: ", "Detail ==", "DisplayName ==", "Description ==" })
                StringAssert.DoesNotContain(english, copy);
        }

        // ------------------------------------------------------------ slice 9 · E3 R2: binding conflicts

        [Test]
        public void Controls_TusCakismasi_AyniSemadaIkiYuva_UyariCumlesi_TR_EN()
        {
            var kb = InputSchemeKind.KeyboardMouse;
            var entries = new List<InputRemapEntry>
            {
                new InputRemapEntry("fire", "Gameplay", "Fire", kb, null, "<Mouse>/leftButton", "<Keyboard>/r", "R", true),
                new InputRemapEntry("reload", "Gameplay", "Reload", kb, null, "<Keyboard>/r", "<Keyboard>/r", "R", false),
                new InputRemapEntry("up", "Gameplay", "Move", kb, "up", "<Keyboard>/w", "<Keyboard>/w", "W", false),
                new InputRemapEntry("pad-reload", "Gameplay", "Reload", InputSchemeKind.Gamepad, null, "<Gamepad>/buttonWest", "<Keyboard>/r", "R", false),
                new InputRemapEntry("aim", "Gameplay", "Aim", kb, null, "<Keyboard>/r", "<Keyboard>/r", "R", false),
                new InputRemapEntry("empty-a", "Gameplay", "Pause", kb, null, "", "", "", false),
                new InputRemapEntry("empty-b", "Gameplay", "Special", kb, null, "", "", "", false),
            };

            UiStrings.Language = UiStrings.Turkish;
            List<string> tr = MainMenuScreen.RemapConflicts(entries, kb, out HashSet<string> paths);
            Assert.AreEqual(1, tr.Count, "one control answered by two keyboard slots; the pad scheme, the Aim axis and unbound slots never count");
            CollectionAssert.AreEquivalent(new[] { "<Keyboard>/r" }, paths);
            StringAssert.Contains(UiStrings.Get("controls.action.Fire"), tr[0]);
            StringAssert.Contains(UiStrings.Get("controls.action.Reload"), tr[0]);
            StringAssert.StartsWith("R ", tr[0]);
            StringAssert.DoesNotContain("{", tr[0]);
            Assert.AreEqual(0, MainMenuScreen.RemapConflicts(entries, InputSchemeKind.Gamepad, out _).Count, "the gamepad scheme has no conflict");

            UiStrings.Language = UiStrings.English;
            List<string> en = MainMenuScreen.RemapConflicts(entries, kb, out _);
            Assert.AreEqual("R is bound to more than one action: Fire, Reload. Rebind one of them.", en[0]);
        }

        // ------------------------------------------------------------ slice 9 · E3 T1: a locked title / badge says what opens it

        [Test]
        public void Kozmetik_Kilitli_KosulSatirda_TR_EN_EskiGelistiriciMetniYok()
        {
            int checkedRows = 0;
            foreach (UnlockDefinition d in UnlockCatalog.Entries)
            {
                if (d.Type != UnlockContentType.Title && d.Type != UnlockContentType.Badge) continue;
                StringAssert.DoesNotContain("supports titles", d.Detail, d.Id + ": a condition, not a developer note");
                foreach (string language in new[] { UiStrings.Turkish, UiStrings.English })
                {
                    UiStrings.Language = language;
                    string reason = (string)Copy("LockReason", d.Id);
                    StringAssert.StartsWith(UiStrings.Get("tree.state.locked"), reason, d.Id + " " + language);
                    StringAssert.DoesNotContain("{", reason, d.Id);
                    Assert.Greater(reason.Length, UiStrings.Get("tree.state.locked").Length + 3, d.Id + " names its condition");
                    if (d.Condition != UnlockCondition.Achievement)
                        StringAssert.Contains(d.NumericKey.ToString(), reason, d.Id + " carries its number");
                    string detail = (string)typeof(ProfileViewModel).Assembly.GetType("DD.Prototype.UI.ProfileContentCopy")
                        .GetMethod("UnlockDetail", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { d.Id, d.Detail });
                    StringAssert.DoesNotContain("{", detail, d.Id);
                }

                checkedRows++;
            }

            Assert.Greater(checkedRows, 0, "the catalog has titles and badges");
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual(UiStrings.Get("tree.state.locked"), Copy("LockReason", "no_such_unlock"), "an unknown id reads plain LOCKED");
        }

        private sealed class AllAtOneLadder : IUnlockLadder
        {
            public int UnlockLevelFor(ClassicWeaponId id) { return 1; }
            public int UnlockLevelFor(UpgradeBranch branch) { return 1; }
            public string WeaponDisplayName(ClassicWeaponId id) { return id.ToString(); }
            public string BranchDisplayName(UpgradeBranch branch) { return branch.ToString(); }
        }
    }
}
