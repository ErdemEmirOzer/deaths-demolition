using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-004 · koşu build state sözleşmesi — sahnesiz, saf. Rank otoritesi
    /// WeaponModifierState'te kalır; RunBuildState onu gözler, satın alma kaydını ve
    /// önbellekli açıklamayı taşır. "DescribeBuild her kare string üretmez" burada
    /// referans eşitliğiyle kanıtlanır.
    /// </summary>
    [TestFixture]
    public sealed class RunBuildStateTests
    {
        private WeaponModifierState _rank;
        private RunBuildState _build;
        private List<RunBuildSnapshot> _degisimler;

        [SetUp]
        public void SetUp()
        {
            _rank = new WeaponModifierState();
            _build = new RunBuildState();
            _degisimler = new List<RunBuildSnapshot>();
            _build.Changed += s => _degisimler.Add(s);
        }

        [Test]
        public void RankOtoritesi_TekYerde_BuildStateKopyaTutmaz()
        {
            _build.Attach(_rank);
            _build.BeginRun(1, ClassicWeaponId.AssaultRifle);

            _rank.AddTier(UpgradeBranch.Piercing, 1, null);
            Assert.AreEqual(1, _build.Tier(UpgradeBranch.Piercing), "Build state rank'ı otoriteden okur.");

            _rank.SetTier(UpgradeBranch.Piercing, 3, null);
            Assert.AreEqual(3, _build.Tier(UpgradeBranch.Piercing), "Otorite değişince build state anında aynı sayıyı görür (kopya yok).");
        }

        [Test]
        public void DescribeBuild_DegismedikceAyniStringNesnesi_DegisinceYenilenir()
        {
            _build.Attach(_rank);
            _build.BeginRun(1, ClassicWeaponId.AssaultRifle);

            string a = _build.DescribeBuild();
            string b = _build.DescribeBuild();
            Assert.AreSame(a, b, "Değişiklik yokken aynı string nesnesi (0 B/kare).");
            Assert.AreEqual("Stock", a);

            _rank.AddTier(UpgradeBranch.FireRate, 1, null);
            string c = _build.DescribeBuild();
            Assert.AreNotSame(a, c);
            Assert.AreEqual("FR1", c);
            Assert.AreSame(c, _build.DescribeBuild());

            _rank.AddTier(UpgradeBranch.MovementSpeed, 1, null);
            _rank.AddTier(UpgradeBranch.Piercing, 2, null);
            Assert.AreEqual("P2 / FR1 / MS1", _build.DescribeBuild(), "Silah yarısı + oyuncu yarısı, sabit sırada.");

            // Silah tarafındaki (HUD'a her kare yayınlanan) açıklama da önbelleklidir.
            string w1 = _rank.DescribeBuild();
            Assert.AreSame(w1, _rank.DescribeBuild(), "WeaponModifierState.DescribeBuild değişmedikçe aynı nesne.");
            Assert.AreEqual("P2 / FR1", w1);
        }

        [Test]
        public void Snapshot_YalnizDegisimdeYenilenir_VersiyonArtar()
        {
            _build.Attach(_rank);
            _build.BeginRun(3, ClassicWeaponId.AutoSmg);

            RunBuildSnapshot s1 = _build.Snapshot;
            RunBuildSnapshot s1b = _build.Snapshot;
            Assert.AreEqual(s1.Version, s1b.Version);
            Assert.AreSame(s1.BuildDescription, s1b.BuildDescription, "Snapshot okumak string üretmez.");
            Assert.AreEqual(3, s1.RunId);
            Assert.AreEqual(ClassicWeaponId.AutoSmg, s1.Weapon);
            Assert.IsTrue(s1.IsStock);

            _rank.AddTier(UpgradeBranch.ReloadSpeed, 1, null);
            _build.RecordPurchase(UpgradeBranch.ReloadSpeed, 15, round: 2);

            RunBuildSnapshot s2 = _build.Snapshot;
            Assert.Greater(s2.Version, s1.Version);
            Assert.AreEqual(1, s2.PurchaseCount);
            Assert.AreEqual(15, s2.TotalGoldSpent);
            Assert.AreEqual(2, s2.LastUpdatedRound);
            Assert.AreEqual(1, s2.TotalTiers);
            Assert.AreEqual("RL1", s2.BuildDescription);
            Assert.IsFalse(s2.IsStock);
        }

        [Test]
        public void RoundlarArasiKorunur_OlumdeSifirlanir_YeniRunTemiz()
        {
            _build.Attach(_rank);
            _build.BeginRun(1, ClassicWeaponId.AssaultRifle);
            _rank.AddTier(UpgradeBranch.FireRate, 1, null);
            _build.RecordPurchase(UpgradeBranch.FireRate, 18, 1);

            // Round geçişi: build state'e hiçbir çağrı yapılmaz → korunur.
            Assert.AreEqual(1, _build.Tier(UpgradeBranch.FireRate));
            Assert.AreEqual(1, _build.PurchaseCount);

            // Ölüm: EndRun; sonraki StartRun rank'ı sıfırlar.
            _build.EndRun();
            Assert.IsFalse(_build.IsRunLive);
            Assert.AreEqual(0, _build.PurchaseCount);
            Assert.AreEqual(0, _build.TotalGoldSpent);
            _rank.ResetForNewRun();
            Assert.AreEqual("Stock", _build.DescribeBuild(), "Ölüm sonrası build sıfır.");

            _build.BeginRun(2, ClassicWeaponId.AssaultRifle);
            Assert.AreEqual(2, _build.RunId);
            Assert.AreEqual(0, _build.TotalTiers, "Yeni koşu temiz build.");
            Assert.AreEqual(0, _build.LastUpdatedRound);
        }

        [Test]
        public void RecordPurchase_KosuKapaliykenYokSayilir()
        {
            _build.Attach(_rank);
            _build.RecordPurchase(UpgradeBranch.Chain, 40, 1);
            Assert.AreEqual(0, _build.PurchaseCount, "Koşu açık değilken satın alma kaydı tutulmaz.");
        }

        [Test]
        public void Attach_EskiStateAboneligiBirakir_YeniStateIzler()
        {
            var eski = new WeaponModifierState();
            var yeni = new WeaponModifierState();
            _build.Attach(eski);
            _build.BeginRun(1, ClassicWeaponId.AssaultRifle);
            int n = _degisimler.Count;

            _build.Attach(yeni);
            eski.AddTier(UpgradeBranch.Piercing, 1, null);
            Assert.AreEqual(n + 1, _degisimler.Count, "Yalnız Attach bir değişim; eski state'in değişimi izlenmez.");
            Assert.AreEqual(0, _build.Tier(UpgradeBranch.Piercing));

            yeni.AddTier(UpgradeBranch.Piercing, 1, null);
            Assert.AreEqual(1, _build.Tier(UpgradeBranch.Piercing));
            Assert.AreEqual(n + 2, _degisimler.Count);

            _build.Detach();
            Assert.IsFalse(_build.HasState);
            Assert.AreEqual("Stock", _build.DescribeBuild(), "Bağlı state yokken build stock okunur.");
        }
    }
}
