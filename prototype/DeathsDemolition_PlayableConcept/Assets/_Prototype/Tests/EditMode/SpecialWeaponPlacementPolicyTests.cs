using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-007 · Konum seçim politikası — saf, enjekte RNG ile (AP-4, FLOW-006 ilkesi · TM-18).
    /// <see cref="SpecialWeaponDirector.PickWeighted"/> ve <see cref="SpecialWeaponDirector.EffectiveWeight"/>
    /// kare döngüsü olmadan sürülür. Ağırlık çarpanı config/sabitten okunur; yalnız YÖN sınanır
    /// (off-route ≥ on-route), sayı kilitlenmez.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponPlacementPolicyTests
    {
        private const int TohumA = 77;
        private const int Orneklem = 32;

        private readonly List<GameObject> _nesneler = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _nesneler.Count; i++)
            {
                if (_nesneler[i] != null)
                {
                    Object.DestroyImmediate(_nesneler[i]);
                }
            }

            _nesneler.Clear();
        }

        /// <summary>EditMode'da OnEnable koşmaz: nokta statik listeye girmez (izole), alanlar yansımayla kurulur.</summary>
        private SpecialWeaponSpawnPoint Nokta(Vector3 konum, float agirlik, bool rotaDisi)
        {
            var go = new GameObject("TestPoint");
            go.transform.position = konum;
            _nesneler.Add(go);
            var p = go.AddComponent<SpecialWeaponSpawnPoint>();
            FieldInfo w = typeof(SpecialWeaponSpawnPoint).GetField("_weight", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo o = typeof(SpecialWeaponSpawnPoint).GetField("_offRoute", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(w, "SpecialWeaponSpawnPoint._weight bulunamadı — ölçüm kırık.");
            Assert.IsNotNull(o, "SpecialWeaponSpawnPoint._offRoute bulunamadı — ölçüm kırık.");
            w.SetValue(p, agirlik);
            o.SetValue(p, rotaDisi);
            return p;
        }

        /// <summary>[TM-18] Aynı seed + aynı aday listesi aynı noktayı seçer; farklı seed'ler en az bir kez farklı seçer (pozitif kontrol).</summary>
        [Test]
        public void AyniSeedAyniAdaylar_AyniNokta()
        {
            var adaylar = new List<SpecialWeaponSpawnPoint>
            {
                Nokta(new Vector3(10f, 0f, 0f), 1f, false),
                Nokta(new Vector3(0f, 0f, 10f), 1f, false),
                Nokta(new Vector3(-10f, 0f, 0f), 1f, true),
            };

            Vector3 a = SpecialWeaponDirector.PickWeighted(adaylar, new SeededSpecialWeaponRandom(TohumA));
            Vector3 b = SpecialWeaponDirector.PickWeighted(adaylar, new SeededSpecialWeaponRandom(TohumA));
            Assert.AreEqual(a, b, "Aynı seed farklı nokta seçti.");

            bool farkli = false;
            for (int tohum = TohumA + 1; tohum < TohumA + 1 + Orneklem && !farkli; tohum++)
            {
                farkli = SpecialWeaponDirector.PickWeighted(adaylar, new SeededSpecialWeaponRandom(tohum)) != a;
            }

            Assert.IsTrue(farkli, "Pozitif kontrol: hiçbir seed farklı nokta seçmedi — ölçüm kırık.");
        }

        /// <summary>Ağırlığı 0 olan aday hiç seçilmez; seçim her zaman aday listesinden bir DropPosition'dır.</summary>
        [Test]
        public void SifirAgirlik_HicSecilmez_SecimListeden()
        {
            SpecialWeaponSpawnPoint sifir = Nokta(new Vector3(5f, 0f, 5f), 0f, true);
            SpecialWeaponSpawnPoint bir = Nokta(new Vector3(-5f, 0f, -5f), 1f, false);
            var adaylar = new List<SpecialWeaponSpawnPoint> { sifir, bir };

            var rng = new SeededSpecialWeaponRandom(TohumA);
            for (int i = 0; i < Orneklem; i++)
            {
                Vector3 secim = SpecialWeaponDirector.PickWeighted(adaylar, rng);
                Assert.AreEqual(bir.DropPosition, secim, "Ağırlığı 0 olan aday seçildi ya da seçim listede yok.");
            }
        }

        /// <summary>[REQ-054 yönü] Rota dışı nokta, aynı taban ağırlıkla rota içi noktadan daha ağırdır (çarpan sabitten; yalnız yön).</summary>
        [Test]
        public void RotaDisi_AyniTabanAgirlikla_DahaAgir()
        {
            SpecialWeaponSpawnPoint ici = Nokta(Vector3.zero, 1f, false);
            SpecialWeaponSpawnPoint disi = Nokta(Vector3.one, 1f, true);
            Assert.Greater(SpecialWeaponDirector.EffectiveWeight(disi), SpecialWeaponDirector.EffectiveWeight(ici), "Rota dışı nokta daha ağır değil (REQ-054 teşviki yok).");
            Assert.AreEqual(ici.Weight * SpecialWeaponDirector.OffRouteWeightMultiplier, SpecialWeaponDirector.EffectiveWeight(disi), 0.0001f, "Çarpan sabitten okunmuyor.");
        }

        /// <summary>Toplam ağırlık 0 ise (hepsi 0) seçim eşit olasılıkla listeden yapılır, çökmez.</summary>
        [Test]
        public void ToplamAgirlikSifir_EsitSecim_Cokmez()
        {
            var adaylar = new List<SpecialWeaponSpawnPoint>
            {
                Nokta(new Vector3(1f, 0f, 0f), 0f, false),
                Nokta(new Vector3(2f, 0f, 0f), 0f, false),
            };
            var rng = new SeededSpecialWeaponRandom(TohumA);
            var gorulen = new HashSet<Vector3>();
            for (int i = 0; i < Orneklem; i++)
            {
                gorulen.Add(SpecialWeaponDirector.PickWeighted(adaylar, rng));
            }

            Assert.AreEqual(2, gorulen.Count, "Eşit seçim iki adayı da görmedi.");
        }

        /// <summary>Sevkiyat doğrulayıcısı ve rastgele kaynağı tekil örneklerdir (allocation-free varsayılan).</summary>
        [Test]
        public void SevkiyatVarsayilanlari_TekilOrnek()
        {
            Assert.AreSame(NavMeshDropValidator.Instance, NavMeshDropValidator.Instance);
            Assert.AreSame(UnityRandomSource.Instance, UnityRandomSource.Instance);
            float v = UnityRandomSource.Instance.Value();
            Assert.GreaterOrEqual(v, 0f);
            Assert.LessOrEqual(v, 1f);
        }
    }
}
