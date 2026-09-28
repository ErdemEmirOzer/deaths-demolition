using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
            Assert.AreEqual("Hareket Yukarı", MainMenuScreen.RemapRowName(entry));
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
    }
}
