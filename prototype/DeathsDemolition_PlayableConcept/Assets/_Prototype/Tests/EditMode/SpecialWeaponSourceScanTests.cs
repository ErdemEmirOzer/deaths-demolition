using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-007 · Kaynak taraması sözleşmeleri (DT-10, PB-1, PB-10 · TM-44). Üretim kodunu
    /// değiştirmez; kırmızı yanarsa bulgu üretir. Yorumlar ve metin sabitleri temizlenmiş
    /// kod üzerinde tarar (HandoffIntegrityTests kalıbı).
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponSourceScanTests
    {
        private const string PrototypeRoot = "Assets/_Prototype";

        /// <summary>WP-006 sonunda var olan hasar olay busu; WP-007 yeni paralel olay/yol eklemez (DT-10).</summary>
        private static readonly string[] BilinenHasarOlaylari = { "ArmorBroken", "EnemyDamaged", "EnemyDied", "PlayerAttacked" };

        private static readonly string[] SpecialYasamDongusuDosyalari =
        {
            "Weapons/SpecialWeaponDirector.cs", "Weapons/SpecialWeaponPickup.cs", "Weapons/SpecialWeaponRuntime.cs",
            "Weapons/SpecialWeaponRoundBridge.cs", "Weapons/SpecialWeaponSpawnPoint.cs", "Weapons/SpecialWeaponRandom.cs",
            "Weapons/SpecialDropValidator.cs", "Weapons/SpecialImpacts.cs", "Weapons/SpecialGelEffects.cs",   // WP-069 B4: the chain / gel landing replaces the three field objects
        };

        private static string Kod(string relative)
        {
            string path = Path.Combine(PrototypeRoot, relative);
            Assert.IsTrue(File.Exists(path), "Kaynak yok: " + relative);
            string s = File.ReadAllText(path);
            s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            s = Regex.Replace(s, @"//[^\n]*", " ");
            return Regex.Replace(s, "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");
        }

        /// <summary>[TM-44][DT-10] Damage/ altında yeni statik hasar olayı yok; Special dosyalarında hiç statik hasar olayı yok.</summary>
        [Test]
        public void YeniParalelHasarOlayi_Yok()
        {
            var bulunan = new List<string>();
            foreach (string path in Directory.GetFiles(Path.Combine(PrototypeRoot, "Damage"), "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(Kod(path.Substring(PrototypeRoot.Length + 1)), @"static\s+event\s+[A-Za-z<>,\s]+?\s+(\w+)\s*;"))
                {
                    bulunan.Add(m.Groups[1].Value);
                }
            }

            CollectionAssert.AreEquivalent(BilinenHasarOlaylari, bulunan, "Damage/ altındaki statik olay kümesi WP-006 kümesinden farklı (DT-10).");

            // Hasar taşıyan statik olay yalnız Damage/ altında olabilir. Round köprüsü (round/run
            // bildirimleri, HEAD'de de vardı) hasar olayı değildir ve kapsam dışıdır.
            foreach (string dosya in SpecialYasamDongusuDosyalari)
            {
                if (dosya.EndsWith("SpecialWeaponRoundBridge.cs"))
                {
                    continue;
                }

                Assert.IsFalse(Regex.IsMatch(Kod(dosya), @"static\s+event"), dosya + " statik olay tanımlıyor (yeni paralel olay yolu, DT-10).");
            }
        }

        /// <summary>[PB-1] Special yaşam döngüsü dosyalarının Update yollarında sahne araması yok (yalnız kurulum/debug dosyaları arayabilir).</summary>
        [Test]
        public void UpdateYolunda_SahneAramasi_Yok()
        {
            foreach (string dosya in SpecialYasamDongusuDosyalari)
            {
                string kod = Kod(dosya);
                Assert.IsFalse(Regex.IsMatch(kod, @"\bFindObjectOfType\b|\bFindObjectsOfType\b|\bGameObject\.Find\b|\bFindAnyObjectByType\b|\bFindFirstObjectByType\b"),
                    dosya + " sahne araması içeriyor (PB-1).");
            }
        }

        /// <summary>
        /// [DT-1][DT-4] WP-069 B4: Raiju's chain is ONE transaction — the orb's own (ProjectileSystem.NextId), resumed per hop;
        /// no Begin per target, so a body in the chain is damaged once per shot (the receiver's duplicate guard).
        /// </summary>
        [Test]
        public void CokluHedefSaldirilari_TekTransactionScope()
        {
            string kod = Kod("Weapons/SpecialImpacts.cs");
            Assert.IsTrue(Regex.IsMatch(kod, @"DamageTransaction\s*\.\s*Resume\s*\("), "zincir, merminin işlemini sürdürmüyor (DT-1/3).");
            Assert.IsFalse(Regex.IsMatch(kod, @"DamageTransaction\s*\.\s*Begin\s*\("), "SpecialImpacts kendi transaction'ını açıyor (DT-4).");
        }

        /// <summary>[PB-10] Development-only probe dosya düzeyinde derleme kapısıyla korunur; release'e sembol sızmaz.</summary>
        [Test]
        public void Probe_YalnizDevelopmentBuild()
        {
            string path = Path.Combine(PrototypeRoot, "Diagnostics/SpecialWeaponProbe.cs");
            Assert.IsTrue(File.Exists(path), "SpecialWeaponProbe.cs yok.");
            string ilkSatir = File.ReadLines(path).First().Trim();
            Assert.AreEqual("#if DEVELOPMENT_BUILD || UNITY_EDITOR", ilkSatir, "Probe dosya düzeyinde development kapısıyla başlamıyor (PB-10).");
            Assert.IsTrue(File.ReadAllText(path).TrimEnd().EndsWith("#endif"), "Probe dosyası #endif ile bitmiyor.");
        }
    }
}
