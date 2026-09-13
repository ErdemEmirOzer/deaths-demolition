using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-007 · Uygunluk ve zamanlama politikası — saf, enjekte RNG ile (AP-1, AP-2 · TM-2, TM-3,
    /// TM-4, TM-5, TM-6). <see cref="SpecialWeaponDirector.RollEligibility"/> ve
    /// <see cref="SpecialWeaponDirector.TryComputeSpawnMark"/> kare döngüsü olmadan sürülür.
    /// Pencere ve eşikler config'ten; seed'ler test parametresidir. Sayılar ürün kararı değil.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponSchedulingPolicyTests
    {
        /// <summary>Test parametreleri: tohumlar ve örneklem; ürün sayısı değil.</summary>
        private const int TohumA = 1234;
        private const int TohumB = 4321;
        private const int Orneklem = 64;

        /// <summary>Sabit değer döndüren kaynak: roll'u doğrudan sürmek için.</summary>
        private sealed class SabitRandom : ISpecialWeaponRandom
        {
            private readonly float _deger;
            public SabitRandom(float deger) { _deger = deger; }
            public float Value() => _deger;
            public float Range(float min, float max) => min + (max - min) * _deger;
            public int Range(int min, int maxExclusive) => min;
        }

        // ------------------------------------------------------------ TM-2 / TM-3 · AP-1

        /// <summary>[TM-2] Schedule kararı yalnız enjekte edilen roll sonucuna bağlıdır: roll ≤ ihtimal → planlanır, roll &gt; ihtimal → planlanmaz.</summary>
        [Test]
        public void Uygunluk_YalnizRollSonucunaBagli()
        {
            var s = new SpecialWeaponConfigSection();
            float ihtimal = s.BaseSpawnChance;
            Assume.That(ihtimal, Is.GreaterThan(0f).And.LessThan(1f), "Türetme öncülü: taban ihtimal (0,1) dışında.");

            Assert.IsTrue(SpecialWeaponDirector.RollEligibility(ihtimal, new SabitRandom(ihtimal * 0.5f)), "İhtimalin altındaki roll planlamadı.");
            Assert.IsTrue(SpecialWeaponDirector.RollEligibility(ihtimal, new SabitRandom(ihtimal)), "İhtimale eşit roll planlamadı (kapsayıcı).");
            Assert.IsFalse(SpecialWeaponDirector.RollEligibility(ihtimal, new SabitRandom(ihtimal + (1f - ihtimal) * 0.5f)), "İhtimalin üstündeki roll planladı.");
            Assert.IsFalse(SpecialWeaponDirector.RollEligibility(0f, new SabitRandom(0f)), "İhtimal 0 iken planlandı (uygun olmayan round).");
            Assert.IsTrue(SpecialWeaponDirector.RollEligibility(1f, new SabitRandom(1f)), "İhtimal 1 iken planlanmadı.");
        }

        /// <summary>[TM-3] Başarısız roll durumu kilitlemez: aynı kaynaktan sonraki roll planlayabilir.</summary>
        [Test]
        public void BasarisizRoll_SonrakiRolluKilitlemez()
        {
            var s = new SpecialWeaponConfigSection();
            float ihtimal = s.BaseSpawnChance;
            var rng = new SeededSpecialWeaponRandom(TohumA);

            bool birinciBasarisiz = false;
            bool sonraBasarili = false;
            for (int i = 0; i < Orneklem; i++)
            {
                bool sonuc = SpecialWeaponDirector.RollEligibility(ihtimal, rng);
                if (!sonuc)
                {
                    birinciBasarisiz = true;
                }
                else if (birinciBasarisiz)
                {
                    sonraBasarili = true;
                    break;
                }
            }

            Assert.IsTrue(birinciBasarisiz, "Öncül: örneklemde hiç başarısız roll yok (ihtimal 1'e çok yakın?).");
            Assert.IsTrue(sonraBasarili, "Başarısız roll'dan sonra hiç başarılı roll gelmedi: kilit ya da ölçüm kırık.");
        }

        // ------------------------------------------------------------ TM-4 · AP-2f determinizm

        /// <summary>[TM-4] Aynı seed + aynı girdiler aynı mark'ı üretir; farklı seed farklı mark (pozitif kontrol).</summary>
        [Test]
        public void AyniSeed_AyniSchedule_FarkliSeedFarkli()
        {
            var s = new SpecialWeaponConfigSection();
            Assume.That(s.SpawnWindowMin, Is.LessThan(s.SpawnWindowMax), "Türetme öncülü: pencere tek nokta — ayırt gücü yok.");

            bool okA1 = SpecialWeaponDirector.TryComputeSpawnMark(s, false, 0f, new SeededSpecialWeaponRandom(TohumA), out float markA1);
            bool okA2 = SpecialWeaponDirector.TryComputeSpawnMark(s, false, 0f, new SeededSpecialWeaponRandom(TohumA), out float markA2);
            Assert.AreEqual(okA1, okA2);
            Assert.AreEqual(markA1, markA2, "Aynı seed farklı mark üretti (determinizm yok).");

            bool farkli = false;
            for (int tohum = TohumB; tohum < TohumB + Orneklem && !farkli; tohum++)
            {
                SpecialWeaponDirector.TryComputeSpawnMark(s, false, 0f, new SeededSpecialWeaponRandom(tohum), out float markB);
                farkli = !Mathf.Approximately(markA1, markB);
            }

            Assert.IsTrue(farkli, "Pozitif kontrol: hiçbir farklı seed farklı mark üretmedi — ölçüm kırık.");
        }

        // ------------------------------------------------------------ TM-5 / TM-6 · AP-2

        /// <summary>[TM-5] Mark her zaman config penceresinin içindedir (örneklem üzerinden).</summary>
        [Test]
        public void Mark_HerZamanPencereIcinde()
        {
            var s = new SpecialWeaponConfigSection();
            var rng = new SeededSpecialWeaponRandom(TohumA);
            float min = Mathf.Min(s.SpawnWindowMin, s.SpawnWindowMax);
            float max = Mathf.Max(s.SpawnWindowMin, s.SpawnWindowMax);
            int planlanan = 0;
            for (int i = 0; i < Orneklem; i++)
            {
                if (!SpecialWeaponDirector.TryComputeSpawnMark(s, false, 0f, rng, out float mark))
                {
                    continue;
                }

                planlanan++;
                Assert.GreaterOrEqual(mark, min - 0.0001f, "Mark pencerenin altında.");
                Assert.LessOrEqual(mark, max + 0.0001f, "Mark pencerenin üstünde.");
                Assert.LessOrEqual(mark, SpecialWeaponDirector.LatestSpawnProgress + 0.0001f, "Mark geç-sınırı aştı.");
            }

            Assert.Greater(planlanan, 0, "Öncül: hiç mark üretilmedi.");
        }

        /// <summary>[TM-6] Roundun bitişine çok yakın bir mark üretilmez: geç-sınırın üstünde kalan aday reddedilir; alt sınır 0 olamaz.</summary>
        [Test]
        public void GecSinir_UstundekiMarkReddedilir_SinirlarIhlalEdilmez()
        {
            var s = new SpecialWeaponConfigSection();
            Assume.That(SpecialWeaponDirector.LatestSpawnProgress, Is.LessThan(1f));

            // Pencereyi geç-sınırın üstüne taşı (test girdisi): her aday reddedilmeli.
            var gec = new SpecialWeaponConfigSection
            {
                SpawnWindowMin = Mathf.Min(1f, SpecialWeaponDirector.LatestSpawnProgress + (1f - SpecialWeaponDirector.LatestSpawnProgress) * 0.5f),
                SpawnWindowMax = 1f
            };
            Assert.IsFalse(SpecialWeaponDirector.TryComputeSpawnMark(gec, false, 0f, new SabitRandom(0f), out _), "Geç-sınır üstündeki pencere mark üretti (DEC-024 'bitmeye çok yakın').");

            // İkinci spawn: ilerleme payı adayı geç-sınırın üstüne taşırsa reddedilir; altındaysa mark ≥ ilerleme + pay.
            float ilerleme = SpecialWeaponDirector.LatestSpawnProgress - SpecialWeaponDirector.SecondSpawnProgressOffset * 0.5f;
            Assert.IsFalse(SpecialWeaponDirector.TryComputeSpawnMark(s, true, ilerleme, new SabitRandom(0f), out _), "İkinci mark geç-sınırı aştığı hâlde kabul edildi.");
            float erken = Mathf.Min(s.SpawnWindowMin, s.SpawnWindowMax);
            Assert.IsTrue(SpecialWeaponDirector.TryComputeSpawnMark(s, true, erken, new SabitRandom(0f), out float ikinciMark));
            Assert.GreaterOrEqual(ikinciMark, erken + SpecialWeaponDirector.SecondSpawnProgressOffset - 0.0001f, "İkinci mark ilerleme + payın gerisinde.");

            // Roundun başı (0) hiçbir zaman mark olamaz: pencere alt sınırı pozitif.
            Assert.IsTrue(SpecialWeaponDirector.TryComputeSpawnMark(s, false, 0f, new SabitRandom(0f), out float enErken));
            Assert.Greater(enErken, 0f, "Mark roundun hemen başına düştü (DEC-024).");
        }

        /// <summary>Politika girdisi yoksa (config ya da kaynak null) planlama yapılmaz — güvenli.</summary>
        [Test]
        public void EksikGirdi_Planlamaz()
        {
            Assert.IsFalse(SpecialWeaponDirector.TryComputeSpawnMark(null, false, 0f, new SabitRandom(0f), out float mark));
            Assert.AreEqual(-1f, mark);
            Assert.IsFalse(SpecialWeaponDirector.TryComputeSpawnMark(new SpecialWeaponConfigSection(), false, 0f, null, out _));
            Assert.IsFalse(SpecialWeaponDirector.RollEligibility(1f, null));
        }
    }
}
