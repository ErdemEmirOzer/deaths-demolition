using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-003 · spawn noktası seçimi sözleşmesi — sahnesiz, NavMesh'siz, kamerasız.
    /// REQ-065 (yalnız kayıtlı noktalar), REQ-066 (çok uzak nokta sıfır ağırlık; oyuncu
    /// konumuyla ağırlık değişir), REQ-070 (devre dışı nokta seçilmez), boş liste /
    /// sıfır ağırlık güvenli düşüşü.
    ///
    /// SAYILAR PROTOTYPE-ONLY — TEST SABİTİ OLARAK YAZILDI
    /// -----------------------------------------------------
    /// MaxUsefulDistance 62 m, tercih bandı 18–35 m, MinSpawnDistance 12 m, SameRegionBonus
    /// 1.15, DistantRegionFactor 0.9 (SpawnConfigSection / SpawnWeightTuning varsayılanları).
    /// Sabit tazelik nöbetçisi sapmada "türetmeyi yenile" der; hiçbir sayı ürün kararı değildir.
    /// Seçim RNG'si enjekte edilir: aynı tohum aynı seçimi verir (deterministik).
    /// </summary>
    [TestFixture]
    public sealed class SpawnSelectionContractTests
    {
        private const float MaxUseful = 62f;
        private const float TercihMin = 18f;
        private const float TercihMax = 35f;
        private const float MinMesafe = 12f;
        private const float AyniBolgeBonusu = 1.15f;
        private const float UzakBolgeFaktoru = 0.9f;

        private SpawnConfigSection _config;
        private SpawnWeightTuning _tuning;
        private SpawnPointSelector _secici;

        [SetUp]
        public void SetUp()
        {
            _config = new SpawnConfigSection();
            _tuning = new SpawnWeightTuning();
            _tuning.Sanitize();
            _secici = new SpawnPointSelector();
        }

        [Test]
        public void SabitTazelik_ConfigVarsayilanlariTestSabitleriyleAyni()
        {
            Assert.AreEqual(MaxUseful, _config.MaxUsefulDistance, 0.0001f, "MaxUsefulDistance sabiti saptı; türetmeyi yenile.");
            Assert.AreEqual(TercihMin, _config.PreferredDistanceMin, 0.0001f);
            Assert.AreEqual(TercihMax, _config.PreferredDistanceMax, 0.0001f);
            Assert.AreEqual(MinMesafe, _config.MinSpawnDistance, 0.0001f);
            Assert.AreEqual(AyniBolgeBonusu, _config.SameRegionBonus, 0.0001f);
            Assert.AreEqual(UzakBolgeFaktoru, _tuning.DistantRegionFactor, 0.0001f);
            Assert.IsTrue(_config.RequireReachablePath);
        }

        // ------------------------------------------------------------ yardımcılar

        private static SpawnPointRuntime Nokta(int index, string id, RingRegion bolge, Vector3 konum,
            float yolMesafesi, bool etkin = true, float tabanAgirlik = 1f)
        {
            var tanim = new SpawnPointDefinition
            {
                Id = id,
                Region = bolge,
                Position = konum,
                BaseWeight = tabanAgirlik,
                Enabled = etkin,
                FaceMapCenter = true
            };

            var p = new SpawnPointRuntime(index, tanim)
            {
                PathDistance = yolMesafesi,
                StraightDistance = yolMesafesi,
                PathStatus = NavPathStatus.Complete,
                LastUsedTime = -999f
            };

            return p;
        }

        private static SpawnWeightInputs Girdi(Vector3 oyuncuKonumu)
        {
            return new SpawnWeightInputs
            {
                PlayerPosition = oyuncuKonumu,
                PlayerRegion = RingRegionMap.Classify(oyuncuKonumu, Vector3.zero, 14f),
                PlayerVelocity = Vector3.zero,
                Now = 100f,
                NavDegraded = false
            };
        }

        /// <summary>Deterministik [0,1) dizisi: LCG, tohumdan üretilir.</summary>
        private static System.Func<float> Rng(uint tohum)
        {
            uint durum = tohum;
            return () =>
            {
                durum = unchecked(durum * 1664525u + 1013904223u);
                return (durum >> 8) / 16777216f;
            };
        }

        private List<SpawnPointRuntime> DortNokta(float mesafe = 25f)
        {
            return new List<SpawnPointRuntime>
            {
                Nokta(0, "N", RingRegion.North, new Vector3(0f, 0f, 34f), mesafe),
                Nokta(1, "E", RingRegion.East, new Vector3(34f, 0f, 0f), mesafe),
                Nokta(2, "S", RingRegion.South, new Vector3(0f, 0f, -34f), mesafe),
                Nokta(3, "W", RingRegion.West, new Vector3(-34f, 0f, 0f), mesafe)
            };
        }

        // ------------------------------------------------------------ sözleşmeler

        [Test]
        public void BosSpawnPointListesi_SonsuzDonguUretmezNullDoner()
        {
            var bos = new List<SpawnPointRuntime>();
            SpawnWeightInputs g = Girdi(Vector3.zero);

            SpawnPointRuntime secim = _secici.SelectWithLadder(bos, in g, _config, _tuning, Rng(1), out SpawnRelaxation gevseme);

            Assert.IsNull(secim, "Boş listeden nokta üretildi.");
            Assert.AreEqual(SpawnRelaxation.None, gevseme);
            Assert.AreEqual(0f, _secici.LastTotalWeight);
            Assert.AreEqual(0, _secici.LastEligibleCount);

            Assert.IsNull(_secici.Select(null, in g, _config, _tuning, SpawnRelaxation.None, Rng(1)), "null liste güvenli olmalı.");
        }

        [Test]
        public void DevreDisiNokta_HicSecilmez()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();
            noktalar[2].Enabled = false;
            SpawnWeightInputs g = Girdi(Vector3.zero);

            System.Func<float> rng = Rng(7);
            for (int i = 0; i < 500; i++)
            {
                SpawnPointRuntime secim = _secici.SelectWithLadder(noktalar, in g, _config, _tuning, rng, out _);
                Assert.IsNotNull(secim, "Üç etkin nokta varken seçim null döndü.");
                Assert.AreNotSame(noktalar[2], secim, "Devre dışı nokta seçildi (deneme " + i + ").");
            }

            Assert.AreEqual(0f, noktalar[2].LastWeight);
            Assert.AreEqual("disabled", noktalar[2].LastRejectReason);
            Assert.AreEqual(3, _secici.LastEligibleCount);
        }

        [Test]
        public void YalnizKayitliNoktalar_SecilirKoordinatUretilmez()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();
            SpawnWeightInputs g = Girdi(Vector3.zero);
            var gorulen = new HashSet<SpawnPointRuntime>();

            System.Func<float> rng = Rng(42);
            for (int i = 0; i < 400; i++)
            {
                SpawnPointRuntime secim = _secici.SelectWithLadder(noktalar, in g, _config, _tuning, rng, out _);
                Assert.IsNotNull(secim);
                Assert.IsTrue(noktalar.Contains(secim), "Seçici listede olmayan bir nokta döndürdü — kayıtlı olmayan koordinat.");
                gorulen.Add(secim);
            }

            Assert.AreEqual(4, gorulen.Count, "Dört eşit nokta 400 çekilişte hepsi görülmeli (ağırlıklı seçim çalışmıyor).");
        }

        [Test]
        public void CokUzakNokta_SifirAgirlikAlir()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();
            noktalar[2].PathDistance = MaxUseful + 0.5f;   // "too far"
            noktalar[2].StraightDistance = MaxUseful + 0.5f;
            SpawnWeightInputs g = Girdi(Vector3.zero);

            _secici.Evaluate(noktalar, in g, _config, _tuning, SpawnRelaxation.None);

            Assert.AreEqual(0f, noktalar[2].LastWeight, "Çok uzak nokta sıfır ağırlık almalı (REQ-066).");
            Assert.AreEqual("too far", noktalar[2].LastRejectReason);
            Assert.Greater(noktalar[0].LastWeight, 0f);
            Assert.AreEqual(3, _secici.LastEligibleCount);

            // Gevşetme merdiveni uzaklığı asla gevşetmez: tam gevşetmede bile sıfır.
            _secici.Evaluate(noktalar, in g, _config, _tuning,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.Reachability);
            Assert.AreEqual(0f, noktalar[2].LastWeight, "Uzaklık kapısı gevşetilebilir olmamalı.");

            System.Func<float> rng = Rng(3);
            for (int i = 0; i < 300; i++)
            {
                Assert.AreNotSame(noktalar[2], _secici.SelectWithLadder(noktalar, in g, _config, _tuning, rng, out _));
            }
        }

        [Test]
        public void CokYakinNokta_SifirAgirlikAlir()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();
            noktalar[0].PathDistance = MinMesafe - 0.5f;
            noktalar[0].StraightDistance = MinMesafe - 0.5f;
            SpawnWeightInputs g = Girdi(Vector3.zero);

            _secici.Evaluate(noktalar, in g, _config, _tuning, SpawnRelaxation.None);
            Assert.AreEqual(0f, noktalar[0].LastWeight, "Adil tepki mesafesi altındaki nokta sıfır olmalı (REQ-069).");
            Assert.AreEqual("too close", noktalar[0].LastRejectReason);
        }

        [Test]
        public void OyuncuKonumuDegisince_AgirlikSonucuDegisir()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();

            // Oyuncu kuzeyde: kuzey noktası aynı bölge bonusu (1.15), güney noktası uzak
            // bölge faktörü (0.9) alır. Oyuncu güneyde tam tersi.
            SpawnWeightInputs kuzey = Girdi(new Vector3(0f, 0f, 22f));
            SpawnWeightInputs guney = Girdi(new Vector3(0f, 0f, -22f));
            Assert.AreEqual(RingRegion.North, kuzey.PlayerRegion);
            Assert.AreEqual(RingRegion.South, guney.PlayerRegion);

            _secici.Evaluate(noktalar, in kuzey, _config, _tuning, SpawnRelaxation.None);
            float kuzeyIkenN = noktalar[0].LastWeight;
            float kuzeyIkenS = noktalar[2].LastWeight;

            _secici.Evaluate(noktalar, in guney, _config, _tuning, SpawnRelaxation.None);
            float guneyIkenN = noktalar[0].LastWeight;
            float guneyIkenS = noktalar[2].LastWeight;

            Assert.Greater(kuzeyIkenN, kuzeyIkenS, "Oyuncu kuzeydeyken kuzey noktası daha ağır olmalı.");
            Assert.Greater(guneyIkenS, guneyIkenN, "Oyuncu güneydeyken güney noktası daha ağır olmalı.");
            Assert.AreNotEqual(kuzeyIkenN, guneyIkenN, "Konum değişince ağırlık değişmedi.");

            // Oran formülden: aynı bölge / uzak bölge = 1.15 / 0.9 (yol mesafesi ve diğer girdiler eşit).
            float beklenenOran = AyniBolgeBonusu / UzakBolgeFaktoru;
            Assert.AreEqual(beklenenOran, kuzeyIkenN / kuzeyIkenS, 0.001f, "Bölge oranı formülden ayrıldı.");
        }

        [Test]
        public void ButunAgirliklarSifir_GuvenliFallbackNullVeIstisnasiz()
        {
            List<SpawnPointRuntime> noktalar = DortNokta(MaxUseful + 10f);   // hepsi çok uzak
            SpawnWeightInputs g = Girdi(Vector3.zero);

            SpawnPointRuntime secim = null;
            SpawnRelaxation gevseme = SpawnRelaxation.None;
            Assert.DoesNotThrow(() => secim = _secici.SelectWithLadder(noktalar, in g, _config, _tuning, Rng(9), out gevseme));

            Assert.IsNull(secim, "Hiç uygun nokta yokken seçici bir nokta uydurdu.");
            Assert.AreEqual(0f, _secici.LastTotalWeight);
            Assert.AreEqual(0, _secici.LastEligibleCount);

            for (int i = 0; i < noktalar.Count; i++)
            {
                Assert.AreEqual(0f, noktalar[i].LastWeight);
                Assert.AreEqual("too far", noktalar[i].LastRejectReason);
            }
        }

        [Test]
        public void YolOlmayanNokta_StriktGecisteReddedilirMerdivenSonundaGevsetilir()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();
            for (int i = 0; i < noktalar.Count; i++)
            {
                noktalar[i].PathStatus = NavPathStatus.Partial;
            }

            SpawnWeightInputs g = Girdi(Vector3.zero);
            Assert.IsNull(_secici.Select(noktalar, in g, _config, _tuning, SpawnRelaxation.None, Rng(1)),
                "Kısmi yol REQ-068 gereği strikt geçişte kabul edilmemeli.");
            Assert.AreEqual("partial path", noktalar[0].LastRejectReason);

            // Merdivenin son basamağı (mevcut prototip davranışı, sayılır ve uyarılır).
            SpawnPointRuntime secim = _secici.SelectWithLadder(noktalar, in g, _config, _tuning, Rng(1), out SpawnRelaxation gevseme);
            Assert.IsNotNull(secim);
            Assert.IsTrue((gevseme & SpawnRelaxation.Reachability) != 0, "Gevşetme seviyesi raporlanmalı.");
        }

        [Test]
        public void Cooldown_StriktGecisteReddedilirIkinciBasamaktaGevsetilir()
        {
            List<SpawnPointRuntime> noktalar = DortNokta();
            for (int i = 0; i < noktalar.Count; i++)
            {
                noktalar[i].LastUsedTime = 99f;   // Now = 100 → 1 s önce kullanıldı, cooldown 4 s
            }

            SpawnWeightInputs g = Girdi(Vector3.zero);
            Assert.IsNull(_secici.Select(noktalar, in g, _config, _tuning, SpawnRelaxation.None, Rng(1)));
            Assert.AreEqual("cooldown", noktalar[0].LastRejectReason);

            SpawnPointRuntime secim = _secici.SelectWithLadder(noktalar, in g, _config, _tuning, Rng(1), out SpawnRelaxation gevseme);
            Assert.IsNotNull(secim);
            Assert.AreEqual(SpawnRelaxation.Screen | SpawnRelaxation.Cooldown, gevseme);
        }

        [Test]
        public void AgirlikliSecim_DeterministikVeAgirligaOrantili()
        {
            // İki nokta: biri taban 3.0, biri 1.0 → seçim payı ~%75 / %25.
            var noktalar = new List<SpawnPointRuntime>
            {
                Nokta(0, "A", RingRegion.North, new Vector3(0f, 0f, 30f), 25f, true, 3f),
                Nokta(1, "B", RingRegion.North, new Vector3(5f, 0f, 30f), 25f, true, 1f)
            };
            SpawnWeightInputs g = Girdi(Vector3.zero);

            int a1 = 0;
            System.Func<float> rng = Rng(2024);
            for (int i = 0; i < 4000; i++)
            {
                if (ReferenceEquals(_secici.Select(noktalar, in g, _config, _tuning, SpawnRelaxation.None, rng), noktalar[0])) a1++;
            }

            int a2 = 0;
            rng = Rng(2024);
            for (int i = 0; i < 4000; i++)
            {
                if (ReferenceEquals(_secici.Select(noktalar, in g, _config, _tuning, SpawnRelaxation.None, rng), noktalar[0])) a2++;
            }

            Assert.AreEqual(a1, a2, "Aynı tohum aynı seçim dizisini vermeli (deterministik).");
            Assert.That(a1 / 4000f, Is.InRange(0.70f, 0.80f), "3:1 ağırlıkta A payı ~%75 olmalı, ölçülen " + (a1 / 4000f));

            // Roll tam sınırda (1.0'a yakın) bile kayıtlı bir nokta döner.
            Assert.IsTrue(noktalar.Contains(_secici.Select(noktalar, in g, _config, _tuning, SpawnRelaxation.None, () => 0.999999f)));
            Assert.IsTrue(noktalar.Contains(_secici.Select(noktalar, in g, _config, _tuning, SpawnRelaxation.None, () => 0f)));
        }

        [Test]
        public void BuyukNoktaListesi_TamponBuyurVeSecimCalisir()
        {
            var noktalar = new List<SpawnPointRuntime>();
            for (int i = 0; i < 64; i++)
            {
                float aci = i / 64f * Mathf.PI * 2f;
                noktalar.Add(Nokta(i, "P" + i, RingRegion.North, new Vector3(Mathf.Sin(aci) * 30f, 0f, Mathf.Cos(aci) * 30f), 25f));
            }

            SpawnWeightInputs g = Girdi(Vector3.zero);
            SpawnPointRuntime secim = _secici.SelectWithLadder(noktalar, in g, _config, _tuning, Rng(5), out _);
            Assert.IsNotNull(secim);
            Assert.IsTrue(noktalar.Contains(secim));
            Assert.AreEqual(64, _secici.LastEligibleCount);
            Assert.GreaterOrEqual(_secici.LastWeights.Count, 64);
        }

        // ------------------------------------------------------------ PrototypeOnly işareti

        /// <summary>
        /// Paketin PrototypeOnly politikası: round/spawn sayısal alanlarının her biri
        /// [PrototypeOnly] taşır. Etiketsiz bir sayısal alan eklenirse kırmızı yanar.
        /// </summary>
        [TestCase(typeof(RoundConfigSection))]
        [TestCase(typeof(SpawnConfigSection))]
        [TestCase(typeof(SpawnWeightTuning))]
        public void PrototypeOnlyRoundDegerleri_Isaretli(System.Type tip)
        {
            FieldInfo[] alanlar = tip.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var eksik = new List<string>();
            int sayisal = 0;

            for (int i = 0; i < alanlar.Length; i++)
            {
                System.Type t = alanlar[i].FieldType;
                if (t != typeof(float) && t != typeof(int) && t != typeof(bool))
                {
                    continue;
                }

                sayisal++;
                if (alanlar[i].GetCustomAttribute<PrototypeOnlyAttribute>() == null)
                {
                    eksik.Add(alanlar[i].Name);
                }
            }

            Assert.Greater(sayisal, 0, tip.Name + " hiç sayısal alan taşımıyor; test kapsamı boş.");
            Assert.IsEmpty(eksik, tip.Name + " içinde [PrototypeOnly] etiketi olmayan alanlar: " + string.Join(", ", eksik));
        }
    }
}
