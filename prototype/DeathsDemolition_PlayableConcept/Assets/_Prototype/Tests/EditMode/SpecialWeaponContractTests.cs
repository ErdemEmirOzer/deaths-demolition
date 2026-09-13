using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-007 · Special Weapon spawn uygunluğu / zamanlama sözleşmesi — CONFIG-RELATIVE.
    /// DEC-024 / DEC-025 / DEC-026 · REQ-052 · FLOW-005 (AP-1, AP-2 · TM-1, TM-5, TM-6, TM-11).
    ///
    /// Hiçbir assert sayı literal'i taşımaz: eşikler <see cref="SpecialWeaponConfigSection"/>
    /// alanlarından türetilir (K4). Sayılar PrototypeOnly'dir ve ürün kararı değildir; onları
    /// karakterize eden fixture ayrıdır (<c>SpecialWeaponPrototypeCharacterizationTests</c>).
    /// Tek "sabit tazelik nöbetçisi" sevkiyat asset'i ile kod varsayılanının aynı kaldığını
    /// sınar (K3/K5 istisnası): sapınca "türetmeyi yenile" der, değeri kilitlemez.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponContractTests
    {
        /// <summary>Test parametresi (örneklem genişliği), ürün sayısı değil.</summary>
        private const int Orneklem = 5;

        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        // ------------------------------------------------------------ AP-1 · DEC-025 · TM-1

        /// <summary>[TM-1] Başlangıç roundundan önce ihtimal sıfırdır (config'ten okunan sınır).</summary>
        [Test]
        public void IlkUygunTurdanOnce_IhtimalSifir()
        {
            SpecialWeaponConfigSection s = _config.Weapons.Special;
            for (int r = s.FirstEligibleRound - Orneklem; r < s.FirstEligibleRound; r++)
            {
                Assert.AreEqual(0f, _config.SpecialWeaponSpawnChance(r), 0.0001f, "R" + r + " ihtimali sıfır değil.");
            }
        }

        /// <summary>[TM-2 öncülü] Başlangıç roundundan itibaren ihtimal pozitiftir ve garanti (1) değildir.</summary>
        [Test]
        public void IlkUygunTurdanItibaren_IhtimalPozitif_GarantiDegil()
        {
            SpecialWeaponConfigSection s = _config.Weapons.Special;
            Assume.That(s.BaseSpawnChance, Is.GreaterThan(0f), "Türetme öncülü: taban ihtimal 0.");
            for (int r = s.FirstEligibleRound; r < s.FirstEligibleRound + Orneklem; r++)
            {
                float p = _config.SpecialWeaponSpawnChance(r);
                Assert.Greater(p, 0f, "R" + r + " ihtimali pozitif değil.");
                Assert.Less(p, 1f, "R" + r + " ihtimali 1: spawn garanti olurdu (DEC-025 'ihtimal').");
            }
        }

        /// <summary>Eğri tabandan başlar, monoton artar, tavanda sabitlenir (formül config'ten).</summary>
        [Test]
        public void Egri_TabandanBaslar_MonotonArtar_TavandaSabitlenir()
        {
            SpecialWeaponConfigSection s = _config.Weapons.Special;
            Assume.That(s.SpawnChanceGrowthPerRound, Is.GreaterThan(0f), "Türetme öncülü: büyüme 0 → tavan roundu tanımsız.");
            Assume.That(s.MaxSpawnChance, Is.GreaterThanOrEqualTo(s.BaseSpawnChance), "Türetme öncülü: tavan < taban.");

            int rTavan = s.FirstEligibleRound + Mathf.CeilToInt((s.MaxSpawnChance - s.BaseSpawnChance) / s.SpawnChanceGrowthPerRound);

            Assert.AreEqual(s.BaseSpawnChance, _config.SpecialWeaponSpawnChance(s.FirstEligibleRound), 0.0001f, "İlk uygun round tabandan başlamadı.");

            for (int r = s.FirstEligibleRound + 1; r <= rTavan + Orneklem; r++)
            {
                float p = _config.SpecialWeaponSpawnChance(r);
                float q = _config.SpecialWeaponSpawnChance(r - 1);
                Assert.GreaterOrEqual(p, q, "R" + r + " ihtimali azaldı.");
                Assert.LessOrEqual(p, s.MaxSpawnChance + 0.0001f, "R" + r + " tavanı aştı.");
            }

            Assert.AreEqual(s.MaxSpawnChance, _config.SpecialWeaponSpawnChance(rTavan), 0.0001f, "Tavan hesaplanan roundda bağlamadı.");
            Assert.AreEqual(s.MaxSpawnChance, _config.SpecialWeaponSpawnChance(rTavan + Orneklem), 0.0001f, "Tavan sonrası sabit değil.");
        }

        // ------------------------------------------------------------ AP-2 · DEC-024 · TM-5 / TM-6

        /// <summary>[TM-5] Spawn penceresi roundun içinde açık bir aralıktır: ne başlangıç (0) ne bitiş (1).</summary>
        [Test]
        public void SpawnPenceresi_RoundIcinde_BaslangicVeBitisDisinda()
        {
            SpecialWeaponConfigSection s = _config.Weapons.Special;
            Assert.Greater(s.SpawnWindowMin, 0f, "Pencere roundun hemen başını içeriyor (DEC-024).");
            Assert.Less(s.SpawnWindowMax, 1f, "Pencere roundun bitişini içeriyor (DEC-024).");
            Assert.Less(s.SpawnWindowMin, s.SpawnWindowMax, "Pencere ters ya da boş.");
        }

        /// <summary>[TM-6] Geç-sınır (LatestSpawnProgress) pencere üstünün üzerinde ve roundun bitişinin altındadır.</summary>
        [Test]
        public void GecSinir_PencereUstundenBuyuk_RoundBitisindenKucuk()
        {
            SpecialWeaponConfigSection s = _config.Weapons.Special;
            float gecSinir = DirectorSabiti<float>("LatestSpawnProgress");
            Assert.GreaterOrEqual(gecSinir, s.SpawnWindowMax, "Geç-sınır pencere üstünün altında: pencerenin bir kısmı hiç kullanılamaz.");
            Assert.Less(gecSinir, 1f, "Geç-sınır roundun bitişi: 'bitmeye çok yakın' engeli yok (DEC-024).");
            Assert.GreaterOrEqual(DirectorSabiti<float>("SecondSpawnProgressOffset"), 0f, "İkinci spawn ilerleme payı negatif.");
        }

        /// <summary>Round ilerlemesi köprüsü authoritative sağlayıcıyı 0..1'e kırpar; sağlayıcı yokken 0 ve bağlı değil.</summary>
        [Test]
        public void RoundKoprusu_IlerlemeyiKirpar_SaglayiciYokkenBagliDegil()
        {
            System.Func<int> eskiRound = SpecialWeaponRoundBridge.CurrentRoundProvider;
            System.Func<float> eskiIlerleme = SpecialWeaponRoundBridge.RoundProgressProvider;
            try
            {
                SpecialWeaponRoundBridge.CurrentRoundProvider = null;
                SpecialWeaponRoundBridge.RoundProgressProvider = null;
                Assert.IsFalse(SpecialWeaponRoundBridge.IsBound);
                Assert.AreEqual(0f, SpecialWeaponRoundBridge.RoundProgress01);

                float deger = 1f + Orneklem;
                SpecialWeaponRoundBridge.CurrentRoundProvider = () => Orneklem;
                SpecialWeaponRoundBridge.RoundProgressProvider = () => deger;
                Assert.IsTrue(SpecialWeaponRoundBridge.IsBound);
                Assert.AreEqual(1f, SpecialWeaponRoundBridge.RoundProgress01, 0.0001f, "İlerleme 1'e kırpılmadı.");
                deger = -deger;
                Assert.AreEqual(0f, SpecialWeaponRoundBridge.RoundProgress01, 0.0001f, "İlerleme 0'a kırpılmadı.");
                Assert.AreEqual(Orneklem, SpecialWeaponRoundBridge.CurrentRound);
            }
            finally
            {
                SpecialWeaponRoundBridge.CurrentRoundProvider = eskiRound;
                SpecialWeaponRoundBridge.RoundProgressProvider = eskiIlerleme;
            }
        }

        // ------------------------------------------------------------ AP-3 · DEC-026 · TM-11

        /// <summary>[TM-11] İkinci spawn ihtimali ve başlangıç roundu config'ten okunur; ihtimal 0..1 aralığındadır.</summary>
        [Test]
        public void IkinciSpawn_ConfigtenOkunur_IhtimalAralikta()
        {
            SpecialWeaponConfigSection s = _config.Weapons.Special;
            Assert.GreaterOrEqual(s.SecondSpawnChance, 0f);
            Assert.LessOrEqual(s.SecondSpawnChance, 1f);
            Assert.GreaterOrEqual(s.SecondSpawnFromRound, 1, "İkinci spawn başlangıç roundu geçersiz.");
        }

        // ------------------------------------------------------------ K3 · sabit tazelik nöbetçisi

        /// <summary>
        /// Sevkiyat asset'i (Resources/PrototypeConfig) ile kod varsayılanı aynı mı? Sapınca
        /// "türetmeyi yenile" der; sayıyı kilitlemez (K5 istisnası: asset↔kod paritesi).
        /// </summary>
        [Test]
        public void SabitTazelik_SpecialWeaponConfig_SevkiyatVarsayilanlaAyni()
        {
            var sevkiyat = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(sevkiyat, "Resources/PrototypeConfig yok.");
            NobetciDogrula(sevkiyat.Weapons.Special);
        }

        /// <summary>Negatif kontrol: nöbetçi gerçekten kırılabiliyor (ölçüm canlı).</summary>
        [Test]
        public void Nobetci_KasitliSapmayi_Yakalar()
        {
            var kopya = new SpecialWeaponConfigSection();
            kopya.GroundLifetime *= 2f;   // çarpan = test girdisi, ürün sayısı değil
            Assert.Throws<AssertionException>(() => NobetciDogrula(kopya), "Nöbetçi sapmayı görmedi — ölçüm kırık.");
        }

        private static void NobetciDogrula(SpecialWeaponConfigSection asset)
        {
            var kod = new SpecialWeaponConfigSection();
            const string not = " — asset, kod varsayılanından saptı; türetmeyi yenile (PrototypeOnly, ürün kararı değil).";
            Assert.AreEqual(kod.ActiveDuration, asset.ActiveDuration, 0.0001f, "ActiveDuration" + not);
            Assert.AreEqual(kod.GroundLifetime, asset.GroundLifetime, 0.0001f, "GroundLifetime" + not);
            Assert.AreEqual(kod.FirstEligibleRound, asset.FirstEligibleRound, "FirstEligibleRound" + not);
            Assert.AreEqual(kod.BaseSpawnChance, asset.BaseSpawnChance, 0.0001f, "BaseSpawnChance" + not);
            Assert.AreEqual(kod.SpawnChanceGrowthPerRound, asset.SpawnChanceGrowthPerRound, 0.0001f, "SpawnChanceGrowthPerRound" + not);
            Assert.AreEqual(kod.MaxSpawnChance, asset.MaxSpawnChance, 0.0001f, "MaxSpawnChance" + not);
            Assert.AreEqual(kod.SpawnWindowMin, asset.SpawnWindowMin, 0.0001f, "SpawnWindowMin" + not);
            Assert.AreEqual(kod.SpawnWindowMax, asset.SpawnWindowMax, 0.0001f, "SpawnWindowMax" + not);
            Assert.AreEqual(kod.SecondSpawnFromRound, asset.SecondSpawnFromRound, "SecondSpawnFromRound" + not);
            Assert.AreEqual(kod.SecondSpawnChance, asset.SecondSpawnChance, 0.0001f, "SecondSpawnChance" + not);
            Assert.AreEqual(kod.FinalSecondsWarning, asset.FinalSecondsWarning, 0.0001f, "FinalSecondsWarning" + not);
            Assert.AreEqual(kod.MinDistanceFromPlayer, asset.MinDistanceFromPlayer, 0.0001f, "MinDistanceFromPlayer" + not);

            // Runtime'ın config'siz ikizi (Begin(null)) aynı süreyi kullanır; alt klamp yalnız yön (literal yok).
            var rt = new SpecialWeaponRuntime();
            rt.Begin(null, SpecialWeaponId.KaguraBell);
            Assert.AreEqual(kod.ActiveDuration, rt.Duration, 0.0001f, "Runtime.Begin(null) fallback süresi" + not);
            rt.End();
            var kisa = new SpecialWeaponConfigSection { ActiveDuration = 0f };
            rt.Begin(kisa, SpecialWeaponId.KaguraBell);
            Assert.Greater(rt.Duration, 0f, "Runtime alt klampı pozitif değil.");
            rt.End();
        }

        /// <summary>Director'ın private PrototypeOnly sabitleri: alan yoksa FAIL (yalancı yeşil olamaz).</summary>
        private static T DirectorSabiti<T>(string ad)
        {
            FieldInfo f = typeof(SpecialWeaponDirector).GetField(ad, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(f, "SpecialWeaponDirector." + ad + " bulunamadı — ölçüm kırık.");
            return (T)f.GetRawConstantValue();
        }
    }
}
