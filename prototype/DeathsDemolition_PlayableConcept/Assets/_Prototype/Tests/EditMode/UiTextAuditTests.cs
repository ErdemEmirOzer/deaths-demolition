using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · kullanıcıya görünen UI metni denetimi (kaynak taraması).
    ///
    /// UI modülünün runtime kaynaklarındaki her string literal taranır: Türkçe'ye özgü
    /// karakter yok (oyun tamamen İngilizce); "claim" eylemi yok; karara bağlanmamış
    /// DEC-048/DEC-050 davranışlarını kural gibi ilan eden ifade yok ("reroll", "buy as
    /// many", "buy any number", "one purchase per round"); ASCII "->" oku yok (glyph
    /// tutarlılığı). Yorumlar taranmaz — yalnız literal'ler.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class UiTextAuditTests
    {
        private static readonly Regex Literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex Turkish = new Regex("[çğıöşüÇĞİÖŞÜ]", RegexOptions.Compiled);

        private static readonly string[] YasakIfadeler =
        {
            "reroll", "re-roll", "buy as many", "buy any number", "one purchase per round", "claim"
        };

        private static IEnumerable<string> UiKaynaklari()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        }

        private static string YorumlariSoy(string kaynak)
        {
            kaynak = Regex.Replace(kaynak, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            kaynak = Regex.Replace(kaynak, @"^\s*///?.*$", string.Empty, RegexOptions.Multiline);
            return kaynak;
        }

        [Test]
        public void UiLiteralleri_TurkceKarakterIcermez()
        {
            int dosya = 0;
            int literal = 0;
            var ihlaller = new List<string>();

            foreach (string yol in UiKaynaklari())
            {
                dosya++;
                string kaynak = YorumlariSoy(File.ReadAllText(yol));
                foreach (Match m in Literal.Matches(kaynak))
                {
                    literal++;
                    if (Turkish.IsMatch(m.Groups[1].Value))
                    {
                        ihlaller.Add(Path.GetFileName(yol) + ": " + m.Groups[1].Value);
                    }
                }
            }

            Debug.Log("[DD-TEST] wp005.textAudit files=" + dosya + " literals=" + literal + " turkish=" + ihlaller.Count);
            Assert.Greater(dosya, 10, "UI runtime kaynakları bulunamadı");
            Assert.Greater(literal, 100, "literal taraması boş — regex kırık");
            Assert.IsEmpty(ihlaller, "Türkçe karakterli UI literal'i:\n" + string.Join("\n", ihlaller));
        }

        [Test]
        public void UiLiteralleri_ClaimVeKararaBagliIfadeIcermez()
        {
            var ihlaller = new List<string>();

            foreach (string yol in UiKaynaklari())
            {
                string kaynak = YorumlariSoy(File.ReadAllText(yol));
                foreach (Match m in Literal.Matches(kaynak))
                {
                    string deger = m.Groups[1].Value.ToLowerInvariant();
                    for (int i = 0; i < YasakIfadeler.Length; i++)
                    {
                        if (!deger.Contains(YasakIfadeler[i]))
                        {
                            continue;
                        }

                        // "nothing to claim" / "there is nothing to claim": claim'in yokluğunu söyleyen
                        // cümleler serbest; bir claim EYLEMİ ("claim reward", "CLAIM") yasak.
                        if (YasakIfadeler[i] == "claim" && deger.Contains("nothing to claim"))
                        {
                            continue;
                        }

                        ihlaller.Add(Path.GetFileName(yol) + " [" + YasakIfadeler[i] + "]: " + m.Groups[1].Value);
                    }
                }
            }

            Assert.IsEmpty(ihlaller, "karara bağlı/claim ifadesi:\n" + string.Join("\n", ihlaller));
        }

        [Test]
        public void UiLiteralleri_AsciiOkKullanmaz()
        {
            var ihlaller = new List<string>();
            foreach (string yol in UiKaynaklari())
            {
                string kaynak = YorumlariSoy(File.ReadAllText(yol));
                foreach (Match m in Literal.Matches(kaynak))
                {
                    if (m.Groups[1].Value.Contains("->"))
                    {
                        ihlaller.Add(Path.GetFileName(yol) + ": " + m.Groups[1].Value);
                    }
                }
            }

            Assert.IsEmpty(ihlaller, "ASCII ok yerine → kullanılmalı:\n" + string.Join("\n", ihlaller));
        }

        [Test]
        public void SunumModelleri_SabitMetinlerIngilizce()
        {
            string[] metinler =
            {
                ResultsViewModel.Title, ResultsViewModel.ContinueLabel, ProfileViewModel.Title,
                ProfileViewModel.RuleNote, StatisticsViewModel.Title, StatisticsViewModel.Note,
                AchievementsViewModel.Title, AchievementsViewModel.EmptyTitle, AchievementsViewModel.EmptyBody,
                AchievementsViewModel.AutoNote, RunHudViewModel.Placeholder
            };

            for (int i = 0; i < metinler.Length; i++)
            {
                Assert.IsFalse(Turkish.IsMatch(metinler[i]), metinler[i]);
                Assert.IsFalse(string.IsNullOrWhiteSpace(metinler[i]));
            }
        }

        [Test]
        public void UpgradeIkonlari_HerDalinMonogramiVar_VarsayilanSpriteYok()
        {
            Assert.AreEqual(0, UpgradeIcons.LoadFromResources(), "M1: Resources/" + UpgradeIcons.ResourcesFolder + " altında ikon yok (final ikon M3)");

            var goruldu = new HashSet<string>();
            foreach (UpgradeBranch dal in System.Enum.GetValues(typeof(UpgradeBranch)))
            {
                string mono = UpgradeIcons.Monogram(dal);
                Assert.IsTrue(mono.Length >= 1 && mono.Length <= 2, dal + " monogramı 1–2 harf olmalı: " + mono);
                Assert.AreEqual(mono.ToUpperInvariant(), mono, "büyük harf");
                Assert.IsTrue(goruldu.Add(mono), dal + " monogramı benzersiz değil: " + mono);
                Assert.IsFalse(UpgradeIcons.Has(dal), "final ikon üretilmedi: " + dal + " için sprite kayıtlı olmamalı");
            }

            // Kayıt sözleşmesi: sprite verildiğinde bulunur, null ile silinir.
            var doku = new Texture2D(4, 4);
            Sprite sprite = Sprite.Create(doku, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            try
            {
                UpgradeIcons.Register(UpgradeBranch.Piercing, sprite);
                Assert.IsTrue(UpgradeIcons.TryGet(UpgradeBranch.Piercing, out Sprite bulunan));
                Assert.AreSame(sprite, bulunan);
            }
            finally
            {
                UpgradeIcons.Register(UpgradeBranch.Piercing, null);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(doku);
            }

            Assert.IsFalse(UpgradeIcons.Has(UpgradeBranch.Piercing));
        }
    }
}
