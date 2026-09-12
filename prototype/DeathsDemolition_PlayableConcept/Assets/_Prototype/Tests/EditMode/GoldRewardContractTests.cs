using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-004 · düşman ölümü → gold hak edişi ve değer sözleşmesi — sahnesiz, saf.
    /// "Her düşman tam bir paket", "gold yalnız kill'den", "tekrar yok", "önceki koşu
    /// event'i yeni koşuda ödemez", "negatif değer yok", "variant/armor katkısı iki kez
    /// eklenemez", "M1'de olmayan variant/armor için değer uydurulmaz".
    ///
    /// ÇÜRÜTME KOŞULU: her karar (Granted/RED) testin kendi beklentisiyle karşılaştırılır;
    /// ledger sayaçları ancak beyan olarak okunur ve olay sayısıyla çapraz kontrol edilir.
    /// Despawn/cleanup/abort/pool/initialize-başarısızlığı yolları ölüm olayı ÜRETMEZ; bu
    /// sınıf o yollardan hiç çağrılmaz — DamageEvents düzeyinde de kanıtlanır.
    /// </summary>
    [TestFixture]
    public sealed class GoldRewardContractTests
    {
        private GoldRewardLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            _ledger = new GoldRewardLedger();
        }

        // ------------------------------------------------------------------ eligibility

        [Test]
        public void GecerliStandardOlum_BirRewardUretir()
        {
            _ledger.BeginRun(1, 0);
            Assert.AreEqual(GoldRewardVerdict.Granted, _ledger.TryClaim(lifeId: 1, value: 2));
            Assert.AreEqual(1, _ledger.Granted);
            Assert.AreEqual(0, _ledger.Rejected);
            Assert.IsTrue(_ledger.IsClaimed(1));
        }

        [Test]
        public void HerDusman_TamBirPaket_FarkliYasamlarAyriAyriOdenir()
        {
            _ledger.BeginRun(1, 0);
            int granted = 0;
            for (int life = 1; life <= 40; life++)
            {
                if (_ledger.TryClaim(life, 2) == GoldRewardVerdict.Granted) granted++;
            }

            Assert.AreEqual(40, granted, "40 farklı yaşam → 40 hak ediş.");
            Assert.AreEqual(40, _ledger.Granted);
        }

        [Test]
        public void DuplicateDeathCallback_IkinciPaketUretmez()
        {
            _ledger.BeginRun(1, 0);
            Assert.AreEqual(GoldRewardVerdict.Granted, _ledger.TryClaim(7, 2));
            Assert.AreEqual(GoldRewardVerdict.Duplicate, _ledger.TryClaim(7, 2));
            Assert.AreEqual(GoldRewardVerdict.Duplicate, _ledger.TryClaim(7, 99), "Farklı değerle de tekrar reddedilir.");
            Assert.AreEqual(1, _ledger.Granted);
            Assert.AreEqual(2, _ledger.RejectedDuplicate);
        }

        [Test]
        public void KosuCanliDegilken_HicbirOlumOdemez()
        {
            Assert.IsFalse(_ledger.IsRunLive);
            Assert.AreEqual(GoldRewardVerdict.RunNotLive, _ledger.TryClaim(1, 2));

            _ledger.BeginRun(1, 0);
            _ledger.EndRun();
            Assert.AreEqual(GoldRewardVerdict.RunNotLive, _ledger.TryClaim(2, 2), "Abort/ölüm sonrası gecikmiş ölüm ödemez.");
            Assert.AreEqual(0, _ledger.Granted);
        }

        [Test]
        public void OncekiKosununEventi_YeniKosudaGoldUretmez()
        {
            // Koşu 1: yaşamlar 1..10 doğdu, 3'ü öldü.
            _ledger.BeginRun(1, 0);
            _ledger.TryClaim(3, 2);

            // Koşu 2 açılırken en son verilen yaşam kimliği 10 → 1..10 bayat.
            _ledger.BeginRun(2, 10);
            Assert.AreEqual(GoldRewardVerdict.StaleLife, _ledger.TryClaim(5, 2), "Koşu 1'den kalan gövde koşu 2'de ödemez.");
            Assert.AreEqual(GoldRewardVerdict.StaleLife, _ledger.TryClaim(10, 2));
            Assert.AreEqual(GoldRewardVerdict.Granted, _ledger.TryClaim(11, 2), "Koşu 2'nin ilk yaşamı öder.");
            Assert.AreEqual(1, _ledger.Granted, "Sayaçlar koşu başına sıfırlanır.");
            Assert.AreEqual(2, _ledger.RejectedStale);
            Assert.IsFalse(_ledger.IsClaimed(3), "Koşu 1'in hak edişleri koşu 2'ye taşınmaz.");
        }

        [Test]
        public void YasamKimligiOlmayanUretici_Odemez()
        {
            _ledger.BeginRun(1, 0);
            Assert.AreEqual(GoldRewardVerdict.InvalidLife, _ledger.TryClaim(0, 2));
            Assert.AreEqual(GoldRewardVerdict.InvalidLife, _ledger.TryClaim(-4, 2));
            Assert.AreEqual(0, _ledger.Granted);
        }

        [Test]
        public void DegersizOlum_HakEdisTuketmez()
        {
            _ledger.BeginRun(1, 0);
            Assert.AreEqual(GoldRewardVerdict.NoValue, _ledger.TryClaim(1, 0));
            Assert.IsFalse(_ledger.IsClaimed(1), "Değersiz istek yaşamı tüketmemeli.");
            Assert.AreEqual(GoldRewardVerdict.Granted, _ledger.TryClaim(1, 2));
        }

        /// <summary>
        /// Gold yalnız kill'den: ölüm olayı dışındaki hiçbir combat olayı EnemyDied'ı
        /// tetiklemez. Hasar olayı yükseltilir; EnemyDied dinleyicisi hiç çağrılmaz.
        /// GoldService yalnız EnemyDied'a abonedir (kaynak düzeyinde tek abonelik satırı).
        /// </summary>
        [Test]
        public void HasarVerme_OlumOlayiUretmez_DolayisiylaGoldUretmez()
        {
            int olum = 0;
            System.Action<EnemyDeathInfo> dinle = i => olum++;
            DamageEvents.EnemyDied += dinle;
            try
            {
                var go = new GameObject("dummy");
                try
                {
                    var hit = new DamageResult();
                    DamageEvents.RaiseEnemyDamaged(new EnemyDamageInfo(go.transform, EnemyTier.Standard, in hit,
                        Vector3.zero, Vector3.forward, DamageSourceKind.Bullet, 1f, 0.5f, false));
                    DamageEvents.RaiseArmorBroken(new ArmorBreakInfo(go.transform, EnemyTier.Standard, ArmorSlot.Helmet,
                        BodyRegion.Head, Vector3.zero, Vector3.forward, DamageSourceKind.Bullet, 0, 0f));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }

                Assert.AreEqual(0, olum, "Hasar/zırh olayları ölüm olayı değildir.");
            }
            finally
            {
                DamageEvents.EnemyDied -= dinle;
            }
        }

        /// <summary>
        /// Round tamamlanması ayrıca gold üretmez: GoldRewardLedger, GoldWallet ve GoldService
        /// tiplerinde "round" sözcüğünü taşıyan hiçbir kamu üyesi kredi/hak ediş ÜRETMEZ —
        /// tek üretici yol ölüm olayıdır (EnemyDied → TryClaim). Reflection ile kilitlenir:
        /// ledger'ın kamu API'sinde yalnız BeginRun/EndRun/TryClaim/IsClaimed vardır; round
        /// tamamlanmasına tepki veren bir giriş eklenirse bu test kırmızı olur.
        /// </summary>
        [Test]
        public void RoundTamamlanmasi_AyricaGoldUretmez_LedgerinRoundGirisiYok()
        {
            var izinli = new System.Collections.Generic.HashSet<string> { "BeginRun", "EndRun", "TryClaim", "IsClaimed" };
            foreach (System.Reflection.MethodInfo m in typeof(GoldRewardLedger).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;   // property getters
                Assert.IsTrue(izinli.Contains(m.Name), "GoldRewardLedger'a beklenmeyen kamu girişi: " + m.Name + " (round tamamlanması gold üretmemeli).");
            }

            // Kanıt: bir round'un tüm ölümleri hak edildikten sonra sweep, cüzdana yalnız
            // ölüm değerlerinin toplamını yazar (RoundClearOrderingTests bunu miktarla ölçer).
            _ledger.BeginRun(1, 0);
            for (int i = 1; i <= 12; i++) _ledger.TryClaim(i, 2);
            Assert.AreEqual(12, _ledger.Granted);
        }

        /// <summary>Gold olayı XP olayı üretmez: aynı ölüm olayının KillXp'si 0 ya da 100 olsun gold değeri değişmez; Resolve KillXp'yi hiç görmez.</summary>
        [Test]
        public void GoldDegeri_KillXpDenBagimsiz()
        {
            var go = new GameObject("dummy");
            try
            {
                var az = new EnemyDeathInfo(go.transform, EnemyTier.Standard, Vector3.zero, Vector3.forward,
                    BodyRegion.UpperBody, DamageSourceKind.Bullet, false, false, 1, 2, 0, 0f, 0f, 1);
                var cok = new EnemyDeathInfo(go.transform, EnemyTier.Standard, Vector3.zero, Vector3.forward,
                    BodyRegion.UpperBody, DamageSourceKind.Bullet, false, false, 1, 2, 100, 0f, 0f, 2);
                Assert.AreEqual(GoldRewardValue.Resolve(az.GoldReward, az.Tier, null).Total,
                    GoldRewardValue.Resolve(cok.GoldReward, cok.Tier, null).Total, "KillXp gold değerini etkilemez.");
                Assert.AreEqual(2, GoldRewardValue.Resolve(cok.GoldReward, cok.Tier, null).Total);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------ value

        [Test]
        public void StandardZirhsiz_MevcutTemelPrototypeOnlyDegeriUretir()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                int temel = config.GoldReward(EnemyTier.Standard);
                Assert.AreEqual(2, temel, "Sevkiyat config'inin Standard temel değeri (PrototypeOnly) saptı; türetmeyi yenile.");

                GoldRewardValue v = GoldRewardValue.Resolve(temel, EnemyTier.Standard, config);
                Assert.AreEqual(temel, v.Base);
                Assert.AreEqual(0, v.VariantContribution, "M1: versiyon katkısı yok.");
                Assert.AreEqual(0, v.ArmorContribution, "M1: zırh katkısı yok.");
                Assert.AreEqual(temel, v.Total);

                // Event 0 taşırsa config'ten aynı temel değer okunur; iki kez türetilmez.
                Assert.AreEqual(temel, GoldRewardValue.Resolve(0, EnemyTier.Standard, config).Total);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void NegatifDeger_KabulEdilmez()
        {
            var v = new GoldRewardValue(-5, -3, -1);
            Assert.AreEqual(0, v.Base);
            Assert.AreEqual(0, v.VariantContribution);
            Assert.AreEqual(0, v.ArmorContribution);
            Assert.AreEqual(0, v.Total);
            Assert.IsFalse(v.HasValue);
            Assert.AreEqual(0, GoldRewardValue.Resolve(-9, EnemyTier.Standard, null).Total);
        }

        [Test]
        public void VariantVeArmorKatkisi_AyniDegeriIkiKezEkleyemez()
        {
            var v = new GoldRewardValue(2);
            GoldRewardValue birKez = v.WithVariant(3).WithArmor(4);
            GoldRewardValue ikiKez = v.WithVariant(3).WithVariant(3).WithArmor(4).WithArmor(4);

            Assert.AreEqual(9, birKez.Total);
            Assert.AreEqual(birKez.Total, ikiKez.Total, "Yuva ATANIR, toplanmaz: tekrar çağrı değeri değiştirmez.");
            Assert.AreEqual(3, ikiKez.VariantContribution);
            Assert.AreEqual(4, ikiKez.ArmorContribution);
            Assert.AreEqual(2, ikiKez.Base, "Katkılar temel değeri değiştirmez.");
        }

        /// <summary>
        /// M1'de bulunmayan variant/armor için ürün değeri uydurulmaz: Resolve'un M1 yolu
        /// her tier için katkı yuvalarını 0 bırakır; yalnız config'in mevcut temel değeri
        /// okunur (onlar da PrototypeOnly). Yeni bir sayı hiçbir yerde üretilmez.
        /// </summary>
        [Test]
        public void M1deOlmayanVariantArmor_IcinDegerUydurulmaz()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                foreach (EnemyTier tier in System.Enum.GetValues(typeof(EnemyTier)))
                {
                    GoldRewardValue v = GoldRewardValue.Resolve(0, tier, config);
                    Assert.AreEqual(0, v.VariantContribution, tier + ": versiyon katkısı uydurulmuş.");
                    Assert.AreEqual(0, v.ArmorContribution, tier + ": zırh katkısı uydurulmuş.");
                    Assert.AreEqual(config.GoldReward(tier), v.Total, tier + ": yalnız mevcut temel değer.");
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>Yaşam kimliği vericisi: 0 asla verilmez, oturum içinde tekrar etmez, artar.</summary>
        [Test]
        public void EnemyLifeIds_SifirVermez_TekrarEtmez()
        {
            int a = EnemyLifeIds.Next();
            int b = EnemyLifeIds.Next();
            Assert.Greater(a, 0);
            Assert.Greater(b, a);
            Assert.AreEqual(b, EnemyLifeIds.Last);
        }

        /// <summary>Ölüm olayı yaşam kimliğini taşır; eski 13-parametreli kurucu 0 verir (yalnız yabancı üretici).</summary>
        [Test]
        public void EnemyDeathInfo_LifeIdTasir_EskiKurucuSifir()
        {
            var go = new GameObject("dummy");
            try
            {
                var yeni = new EnemyDeathInfo(go.transform, EnemyTier.Standard, Vector3.zero, Vector3.forward,
                    BodyRegion.UpperBody, DamageSourceKind.Bullet, false, false, 1, 2, 1, 0f, 0f, 77);
                var eski = new EnemyDeathInfo(go.transform, EnemyTier.Standard, Vector3.zero, Vector3.forward,
                    BodyRegion.UpperBody, DamageSourceKind.Bullet, false, false, 1, 2, 1, 0f, 0f);
                Assert.AreEqual(77, yeni.LifeId);
                Assert.AreEqual(0, eski.LifeId);

                _ledger.BeginRun(1, 0);
                Assert.AreEqual(GoldRewardVerdict.Granted, _ledger.TryClaim(yeni.LifeId, yeni.GoldReward));
                Assert.AreEqual(GoldRewardVerdict.InvalidLife, _ledger.TryClaim(eski.LifeId, eski.GoldReward));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
