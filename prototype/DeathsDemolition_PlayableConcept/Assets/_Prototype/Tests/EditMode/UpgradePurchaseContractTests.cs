using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-004 · round sonu satın alma işleminin atomikliği — sahnesiz, saf.
    /// Dükkân kendi cüzdanı ve enjekte edilmiş rank otoritesiyle (WeaponModifierState)
    /// açılır; WeaponRegistry'de silah YOKTUR, bu yüzden "silahsız" dalı da gerçektir.
    ///
    /// ÇÜRÜTME KOŞULU: her sonuç (Purchased/RED nedeni) beklentiyle; bakiye, TotalSpent,
    /// tier ve kart durumu her dalda ayrı ayrı doğrulanır. Kartların hangi dallar olduğu
    /// rastgeledir (ağırlıklı çekiliş); testler indeks 0 üzerinden çalışır ve dalı karttan
    /// okur — hiçbir dal/maliyet sayısı kilitlenmez.
    /// </summary>
    [TestFixture]
    public sealed class UpgradePurchaseContractTests
    {
        private PrototypeConfig _config;
        private GoldWallet _cuzdan;
        private WeaponModifierState _rank;
        private RoundUpgradeShop _dukkan;
        private List<UpgradeCardOffer> _satinAlinanlar;
        private int _teklifDegisti;
        private int _reddedildi;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _config.Economy.PurchasesPerRound = 1;   // WP-049C (DEC-087 sevk 2): bu fixture limit = 1 sözleşmesini belgeler; sevk limiti UpgradePurchaseLimitTests
            _cuzdan = new GoldWallet();
            _cuzdan.InitializeForRun(1);
            _rank = new WeaponModifierState();
            // WP-008: unlock yüklemi enjekte (profil dosyası okunmaz), RNG tohumlu (teklif tekrar üretilebilir).
            _dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(404) };
            _satinAlinanlar = new List<UpgradeCardOffer>();
            _teklifDegisti = 0;
            _reddedildi = 0;
            _dukkan.Purchased += c => _satinAlinanlar.Add(c);
            _dukkan.OfferChanged += () => _teklifDegisti++;
            _dukkan.PurchaseRefused += i => _reddedildi++;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private void Ac(int round = 1, int gold = 1000)
        {
            if (gold > 0)
            {
                _cuzdan.Credit(gold, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            }

            _dukkan.Open(round, _config, _cuzdan, _rank, runId: 1);
            Assert.IsTrue(_dukkan.IsOpen);
            Assert.GreaterOrEqual(_dukkan.Cards.Count, 1, "Teklif boş: en az bir kart beklenir (hiç dal maxlanmadı).");
        }

        private int TierToplam()
        {
            return _rank.TotalTiers;
        }

        // ------------------------------------------------------------------ success path

        [Test]
        public void GecerliSatinAlma_GoldBirKezDuser_RankBirKezArtar_EtkiBirKez()
        {
            Ac();
            UpgradeCardOffer kart = _dukkan.Cards[0];
            int once = _cuzdan.Balance;
            int onceTier = _rank.GetTier(kart.Branch);
            int onceVersiyon = _rank.Version;

            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.Purchased, _dukkan.LastResult);

            Assert.AreEqual(once - kart.Cost, _cuzdan.Balance, "Maliyet tam bir kez düştü.");
            Assert.AreEqual(kart.Cost, _cuzdan.TotalSpent);
            Assert.AreEqual(onceTier + 1, _rank.GetTier(kart.Branch), "Rank tam bir kez arttı.");
            Assert.AreEqual(onceVersiyon + 1, _rank.Version, "Etki (state değişimi) tam bir kez uygulandı.");
            Assert.AreEqual(1, _satinAlinanlar.Count);
            Assert.IsTrue(_dukkan.Cards[0].Purchased);
            Assert.AreEqual(kart.Branch, _satinAlinanlar[0].Branch);
            Assert.Greater(_dukkan.Cards[0].TransactionId, 0, "Kart satın alma işleminin kimliğini taşır.");
            Assert.AreEqual(1, _dukkan.PurchasesThisRound);
            Assert.AreEqual(kart.Cost, _dukkan.GoldSpentThisRound);
        }

        [Test]
        public void CiftTiklama_DuplicateSatinAlmaUretmez()
        {
            Ac();
            UpgradeCardOffer kart = _dukkan.Cards[0];
            int once = _cuzdan.Balance;

            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            // WP-008 (DEC-048): hak kontrolü kart kontrolünden önce gelir; ikinci tıklama
            // "round'un tek satın alması kullanıldı" ile reddedilir (AlreadyPurchased değil).
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);

            Assert.AreEqual(once - kart.Cost, _cuzdan.Balance, "İkinci tıklama gold düşürmedi.");
            Assert.AreEqual(1, _rank.GetTier(kart.Branch), "İkinci tıklama rank artırmadı.");
            Assert.AreEqual(1, _satinAlinanlar.Count);
        }

        // ------------------------------------------------------------------ refusals

        [Test]
        public void YetersizGold_UpgradeUygulamaz_StateDegismez()
        {
            Ac(gold: 0);
            _cuzdan.Credit(1, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            UpgradeCardOffer kart = _dukkan.Cards[0];
            Assert.Greater(kart.Cost, 1, "Test öncülü: kart 1 gold'dan pahalı (15-45 bandı).");
            Assert.IsFalse(_dukkan.CanAfford(0));

            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.InsufficientGold, _dukkan.LastResult);
            Assert.AreEqual(1, _cuzdan.Balance);
            Assert.AreEqual(0, _cuzdan.TotalSpent);
            Assert.AreEqual(0, _rank.GetTier(kart.Branch));
            Assert.IsFalse(_dukkan.Cards[0].Purchased);
            Assert.AreEqual(1, _reddedildi, "Oyuncunun canlı butonla aldığı red bir kez bildirilir.");
        }

        [Test]
        public void GecersizUpgradeId_StateDegistirmez()
        {
            Ac();
            int once = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(-1, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.InvalidCard, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.TryPurchase(99, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.InvalidCard, _dukkan.LastResult);
            Assert.AreEqual(once, _cuzdan.Balance);
            Assert.AreEqual(0, TierToplam());
            Assert.AreEqual(0, _reddedildi, "Geçersiz indeks sessizce düşer (canlı buton değil).");
        }

        [Test]
        public void EskiRoundTeklifi_SatinAlamaz()
        {
            Ac(round: 1);
            int eskiOturum = _dukkan.SessionId;
            _dukkan.Close();
            Assert.IsFalse(_dukkan.TryPurchase(0, eskiOturum), "Kapalı dükkân satmaz.");
            Assert.AreEqual(UpgradePurchaseResult.ShopClosed, _dukkan.LastResult);

            _dukkan.Open(2, _config, _cuzdan, _rank, runId: 1);
            Assert.AreNotEqual(eskiOturum, _dukkan.SessionId);
            int once = _cuzdan.Balance;

            Assert.IsFalse(_dukkan.TryPurchase(0, eskiOturum), "Önceki round'un butonu yeni teklife satın alamaz.");
            Assert.AreEqual(UpgradePurchaseResult.SessionMismatch, _dukkan.LastResult);
            Assert.AreEqual(once, _cuzdan.Balance);
            Assert.AreEqual(0, TierToplam());
            Assert.IsFalse(_dukkan.Cards[0].Purchased);

            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId), "Güncel oturum satın alır.");
        }

        [Test]
        public void MaxRank_MevcutPrototipKuralinaGoreKorunur()
        {
            Ac();
            UpgradeCardOffer kart = _dukkan.Cards[0];
            UpgradeBranchSettings ayar = _config.Economy.Find(kart.Branch);
            Assert.IsNotNull(ayar);
            int max = ayar.MaxTier;
            Assert.GreaterOrEqual(max, 2, "Test öncülü: dalın MaxTier'ı ≥ 2 (mevcut prototip 3–5).");

            // Dış etki (F1) dalı tavanın bir altına çeker: kart bayatlar → RED + yenileme, harcama yok.
            _rank.SetTier(kart.Branch, max - 1, _config);
            int once = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.StaleOffer, _dukkan.LastResult);
            Assert.AreEqual(once, _cuzdan.Balance, "Bayat teklif harcama yapmaz.");
            Assert.AreEqual(max - 1, _dukkan.Cards[0].OwnedTier, "Kart canlı tier'a yenilendi.");
            Assert.AreEqual(max, _dukkan.Cards[0].NextTier);

            // Son tier satın alınır…
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(max, _rank.GetTier(kart.Branch));
            Assert.IsTrue(_rank.IsMaxed(kart.Branch, _config));

            // …ve tavan korunur: aynı kart tekrar alınamaz, tier asla tavanı aşmaz.
            once = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(once, _cuzdan.Balance);
            Assert.AreEqual(max, _rank.GetTier(kart.Branch));

            // Yeni round teklifi maxlanmış dalı hiç sunmaz.
            _dukkan.Close();
            _dukkan.Open(2, _config, _cuzdan, _rank, runId: 1);
            for (int i = 0; i < _dukkan.Cards.Count; i++)
            {
                Assert.AreNotEqual(kart.Branch, _dukkan.Cards[i].Branch, "Maxlanmış dal teklif edilmemeli.");
            }
        }

        [Test]
        public void BranchMaxed_DogrudanRed_HarcamaYok()
        {
            Ac();
            UpgradeCardOffer kart = _dukkan.Cards[0];
            int max = _config.Economy.Find(kart.Branch).MaxTier;

            // Ekran açıkken dal dışarıdan TAVANA çekilir (F1): ilk deneme bayat teklif, ikinci deneme tavan reddi.
            _rank.SetTier(kart.Branch, max, _config);
            int once = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.StaleOffer, _dukkan.LastResult);
            Assert.IsTrue(_dukkan.Cards[0].IsMaxed, "Yenilenen kart tavanda.");
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.BranchMaxed, _dukkan.LastResult);
            Assert.AreEqual(once, _cuzdan.Balance, "Tavan reddi harcama yapmaz.");
            Assert.AreEqual(max, _rank.GetTier(kart.Branch), "Tavan aşılmaz.");
            Assert.IsFalse(_dukkan.Cards[0].Purchased);
        }

        [Test]
        public void KapaliCuzdan_SatinAlmaYok()
        {
            Ac();
            // Ekran açıkken cüzdan koşu dışına düşer (ölüm sırası): bakiye 0 okunur, kart alınmaz, tier değişmez.
            _cuzdan.ResetForRunEnd();
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.InsufficientGold, _dukkan.LastResult, "Kapalı cüzdan 0 bakiye okutur → yetersiz gold.");
            Assert.AreEqual(0, TierToplam());
            Assert.IsFalse(_dukkan.Cards[0].Purchased);
            Assert.AreEqual(0, _cuzdan.TotalSpent);
        }

        /// <summary>
        /// Prerequisite: mevcut prototipte bir prerequisite kavramı YOKTUR (UpgradeBranchSettings'te
        /// böyle bir alan yok). Bu test o gerçeği kilitler: alan ortaya çıkarsa satın alma
        /// yolunun doğrulama adımı eklenmeden test kırmızı olur.
        /// </summary>
        [Test]
        public void Prerequisite_PrototipteYok_ModelSessizceGenislemez()
        {
            FieldInfo[] alanlar = typeof(UpgradeBranchSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo f in alanlar)
            {
                Assert.IsFalse(f.Name.ToLowerInvariant().Contains("prereq"),
                    "UpgradeBranchSettings'e prerequisite alanı eklendi (" + f.Name + "); RoundUpgradeShop.PurchaseCore'a doğrulama adımı ekleyin.");
            }
        }

        [Test]
        public void SilahsizSatinAlma_HarcamaYapmaz_YarimDurumBirakmaz()
        {
            _cuzdan.Credit(1000, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            Assert.IsNull(WeaponRegistry.ActiveController, "Test öncülü: EditMode'da silah yok.");

            // Enjekte otorite YOK → dükkân WeaponRegistry'ye bakar → null → NoWeapon.
            _dukkan.Open(1, _config, _cuzdan, null, runId: 1);
            Assert.Greater(_dukkan.Cards.Count, 0, "Teklif silahsız da kurulur (tier 0 varsayılır).");

            int once = _cuzdan.Balance;
            int onceTx = _cuzdan.AppliedTransactions;

            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.NoWeapon, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.Cards[0].Purchased);

            Assert.AreEqual(once, _cuzdan.Balance, "Silahsız satın alma gold düşürmez (eski refund dalı yok).");
            Assert.AreEqual(0, _cuzdan.TotalSpent);
            Assert.AreEqual(onceTx, _cuzdan.AppliedTransactions, "Hiçbir işlem (kredi/harcama) uygulanmadı.");
            Assert.AreEqual(1000, _cuzdan.TotalEarned, "Refund 'kazanç' olarak kaydedilmedi.");
        }

        [Test]
        public void UIYenidenKurulunca_EtkiTekrarUygulanmaz()
        {
            Ac();
            UpgradeCardOffer kart = _dukkan.Cards[0];
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            int versiyon = _rank.Version;
            int bakiye = _cuzdan.Balance;
            int olay = _teklifDegisti;

            Assert.GreaterOrEqual(_dukkan.Cards.Count, 2, "Test öncülü: en az iki kart.");

            // Ekranın yeniden kurulumu = OfferChanged sonrası teklifi yeniden okumak; UI
            // hiçbir 'uygula' çağrısı yapmaz. Dükkânın kendi yeniden okuma yolu (aynı round'un
            // yeniden açılışı: RefreshCards + OfferChanged) da rank'a dokunmaz: dış etkiyle
            // değişen başka bir dal yeniden okunurken satın alınan kartın etkisi ikinci kez
            // uygulanmamış olmalı. (WP-008: satın alma teklifi kilitler; ikinci kart denemesi
            // hak kontrolünde durur ve ekranı yeniden çizmez — bu yüzden yeniden okuma yolu
            // olarak aynı round'un Close/Open'ı kullanılır.)
            _rank.SetTier(_dukkan.Cards[1].Branch, 1, _config);        // başka bir dal dışarıdan değişti (F1)
            int versiyonDis = _rank.Version;
            _dukkan.Close();
            _dukkan.Open(1, _config, _cuzdan, _rank, runId: 1);         // aynı round: teklif korunur, kartlar yeniden okunur
            Assert.Greater(_teklifDegisti, olay, "Ekran yeniden çizim olayı yükseldi.");
            Assert.AreEqual(1, _dukkan.Cards[1].OwnedTier, "Dış değişim yeniden okundu.");
            Assert.IsFalse(_dukkan.TryPurchase(1, _dukkan.SessionId));   // kilitli teklif: hak yok
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);

            Assert.AreEqual(versiyonDis, _rank.Version, "Yeniden çizim/yenileme rank state'ine dokunmadı.");
            Assert.AreEqual(1, _rank.GetTier(kart.Branch), "Satın alınan kartın etkisi tek kaldı.");
            Assert.AreEqual(bakiye, _cuzdan.Balance);
            Assert.IsTrue(_dukkan.Cards[0].Purchased, "Yenileme satın alınmış kartı sıfırlamadı.");
            Assert.AreEqual(versiyon + 1, versiyonDis, "Dış değişim tam bir sürüm.");
        }

        // ------------------------------------------------------------------ run boundary

        [Test]
        public void RoundDegisimi_BuildiKorur_OlumSifirlar_YeniRunSifirlar()
        {
            Ac(round: 1);
            UpgradeCardOffer kart = _dukkan.Cards[0];
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            _dukkan.Close();

            // Round geçişi: dükkân kapanır/açılır; rank ve cüzdan dokunulmaz.
            int bakiye = _cuzdan.Balance;
            _dukkan.Open(2, _config, _cuzdan, _rank, runId: 1);
            Assert.AreEqual(1, _rank.GetTier(kart.Branch), "Round değişimi build'i korur.");
            Assert.AreEqual(bakiye, _cuzdan.Balance, "Round değişimi gold'u korur.");
            _dukkan.Close();

            // Ölüm: director sırası — cüzdan ResetForRunEnd, build EndRun; sonraki StartRun
            // rank state'ini sıfırlar.
            _cuzdan.ResetForRunEnd();
            _rank.ResetForNewRun();
            Assert.AreEqual(0, TierToplam(), "Ölüm build'i sıfırlar.");
            Assert.AreEqual(0, _cuzdan.Balance, "Ölüm gold'u sıfırlar.");

            // Yeni koşu temiz başlar; önceki dükkân oturumu geçersizdir.
            _cuzdan.InitializeForRun(2);
            _dukkan.Open(1, _config, _cuzdan, _rank, runId: 2);
            Assert.AreEqual(0, TierToplam(), "Yeni run build'i sıfırdır.");
            Assert.AreEqual(2, _dukkan.RunId);
            for (int i = 0; i < _dukkan.Cards.Count; i++)
            {
                Assert.AreEqual(0, _dukkan.Cards[i].OwnedTier, "Yeni koşunun kartları sıfır tier'dan başlar.");
            }
        }

        [Test]
        public void SatinAlma_ProfilXpUretmez()
        {
            var stats = new RunStatsTracker();
            stats.RecordKill(EnemyTier.Standard, 1);
            int killXp = stats.KillXp;
            int roundXp = stats.CompletedRoundXp;

            Ac();
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            stats.RecordGoldSpent(_satinAlinanlar[0].Cost);
            stats.RecordGoldEarned(1000);

            Assert.AreEqual(killXp, stats.KillXp, "Gold harcamak/kazanmak kill XP'sine dokunmaz.");
            Assert.AreEqual(roundXp, stats.CompletedRoundXp, "Gold round XP'sine dokunmaz.");
            Assert.AreEqual(killXp + roundXp, stats.TotalXp, "Toplam XP yalnız kill + round.");
            Assert.AreEqual(1, stats.UpgradesPurchased);
        }

        /// <summary>
        /// REQ-059: Special Weapon upgrade ağacından etkilenmez. SpecialWeaponRuntime ve
        /// SpecialWeaponDirector'ın hiçbir alanı/özelliği/metot parametresi WeaponModifierState
        /// tipinde değildir — etki yolu yapısal olarak yoktur.
        /// </summary>
        [Test]
        public void SpecialWeapon_IcinEtkiYoluYok()
        {
            var tipler = new[] { typeof(SpecialWeaponRuntime), typeof(SpecialWeaponDirector) };
            const BindingFlags hepsi = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            foreach (var tip in tipler)
            {
                foreach (FieldInfo f in tip.GetFields(hepsi))
                {
                    Assert.AreNotEqual(typeof(WeaponModifierState), f.FieldType, tip.Name + "." + f.Name + " upgrade state'ini görüyor.");
                }

                foreach (PropertyInfo p in tip.GetProperties(hepsi))
                {
                    Assert.AreNotEqual(typeof(WeaponModifierState), p.PropertyType, tip.Name + "." + p.Name + " upgrade state'ini görüyor.");
                }

                foreach (MethodInfo m in tip.GetMethods(hepsi))
                {
                    foreach (ParameterInfo prm in m.GetParameters())
                    {
                        Assert.AreNotEqual(typeof(WeaponModifierState), prm.ParameterType, tip.Name + "." + m.Name + " upgrade state'ini alıyor.");
                    }
                }
            }
        }

        [Test]
        public void OfferChanged_AcilisVeSatinAlmadaYukselir_UIPollingGerektirmez()
        {
            Ac();
            Assert.AreEqual(1, _teklifDegisti, "Open bir OfferChanged.");
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId));
            Assert.AreEqual(2, _teklifDegisti, "Satın alma bir OfferChanged.");
            _dukkan.Close();
            Assert.AreEqual(3, _teklifDegisti, "Close bir OfferChanged.");
        }
    }
}
