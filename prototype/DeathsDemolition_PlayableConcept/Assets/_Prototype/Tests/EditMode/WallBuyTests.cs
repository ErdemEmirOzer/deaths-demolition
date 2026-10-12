using System;
using System.Collections.Generic;
using System.Linq;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-107 (R4, kullanıcı 11.10 not 8): seviyeyle silah açılması kalkar (roketatar ve Special hariç hepsi baştan açık) +
    /// CoD Zombies tarzı wall buy. Kurallar (satın alma / yarı fiyata cephane / dolu cephane / Special / yetersiz altın /
    /// donmuş koşu), sevk edilen katalog (her klasik silahın fiyatı ve istasyonu, roketatar en pahalı ve en uzakta),
    /// açılış verisi (rl_1 yalnız duvardan, profil kapısı, meta yolda Kül'lü silah düğümü yok), cüzdan kaynağı.
    /// </summary>
    [TestFixture]
    public sealed class WallBuyTests
    {
        private static readonly Vector3 V6PlayerStart = new Vector3(0f, 0.1f, -3f);

        [TearDown]
        public void TearDown()
        {
            WallBuyCatalog.SetForTests(null);
            WallBuyEvents.ClearState();
        }

        // ------------------------------------------------------------------ rules

        [TestCase(250, 125)]
        [TestCase(2500, 1250)]
        [TestCase(155, 78)]
        [TestCase(1, 1)]
        [TestCase(0, 0)]
        public void YenilemeFiyati_YarisiYukariYuvarlanir(int price, int refill)
        {
            Assert.AreEqual(refill, WallBuyRules.RefillCost(price, 0.5f));
        }

        [Test]
        public void BaskaSilah_TamFiyatlaSatinAlinir_AltinYetmezseReddedilir()
        {
            WallBuyQuote q = WallBuyRules.Quote(ClassicWeaponId.AutoSmg, 250, 0.5f, true, false, true, ClassicWeaponId.AssaultRifle, true, 300);
            Assert.AreEqual(WallBuyAction.Buy, q.Action);
            Assert.AreEqual(250, q.Cost);
            Assert.AreEqual(WallBuyBlock.None, q.Block, "elde başka silah varken cephanenin dolu olması satın almayı engellemez");

            q = WallBuyRules.Quote(ClassicWeaponId.AutoSmg, 250, 0.5f, true, false, true, ClassicWeaponId.AssaultRifle, false, 249);
            Assert.AreEqual(WallBuyBlock.NotEnoughGold, q.Block);
            Assert.AreEqual(250, q.Cost, "istem eksik kalınan fiyatı gösterir");
        }

        [Test]
        public void AyniSilah_YariFiyatlaCephane_DoluysaUcretsizRed()
        {
            WallBuyQuote q = WallBuyRules.Quote(ClassicWeaponId.AssaultRifle, 250, 0.5f, true, false, true, ClassicWeaponId.AssaultRifle, false, 125);
            Assert.AreEqual(WallBuyAction.Refill, q.Action);
            Assert.AreEqual(125, q.Cost);
            Assert.IsTrue(q.CanBuy);

            q = WallBuyRules.Quote(ClassicWeaponId.AssaultRifle, 250, 0.5f, true, false, true, ClassicWeaponId.AssaultRifle, true, 9999);
            Assert.AreEqual(WallBuyBlock.AmmoFull, q.Block, "dolu cephaneye altın alınmaz");

            q = WallBuyRules.Quote(ClassicWeaponId.AssaultRifle, 250, 0.5f, true, false, true, ClassicWeaponId.AssaultRifle, false, 124);
            Assert.AreEqual(WallBuyBlock.NotEnoughGold, q.Block);
        }

        [Test]
        public void SpecialElde_VeDonmusKosu_Engeller()
        {
            WallBuyQuote q = WallBuyRules.Quote(ClassicWeaponId.AutoSmg, 250, 0.5f, true, true, true, ClassicWeaponId.AssaultRifle, false, 9999);
            Assert.AreEqual(WallBuyBlock.SpecialActive, q.Block);
            q = WallBuyRules.Quote(ClassicWeaponId.AutoSmg, 250, 0.5f, false, false, true, ClassicWeaponId.AssaultRifle, false, 9999);
            Assert.AreEqual(WallBuyBlock.Unavailable, q.Block);
            q = WallBuyRules.Quote(ClassicWeaponId.AutoSmg, 0, 0.5f, true, false, true, ClassicWeaponId.AssaultRifle, false, 9999);
            Assert.AreEqual(WallBuyBlock.Unavailable, q.Block, "fiyatı olmayan silah satılmaz");

            Assert.IsTrue(WallBuyRules.PhaseAllows(RunPhase.Fighting));
            Assert.IsTrue(WallBuyRules.PhaseAllows(RunPhase.Countdown));
            Assert.IsFalse(WallBuyRules.PhaseAllows(RunPhase.RoundComplete));
            Assert.IsFalse(WallBuyRules.PhaseAllows(RunPhase.Upgrading));
            Assert.IsFalse(WallBuyRules.PhaseAllows(RunPhase.Dead));
            Assert.IsFalse(WallBuyRules.PhaseAllows(RunPhase.None));
        }

        [Test]
        public void Istem_YalnizDegisinceYayinlanir()
        {
            int heard = 0;
            Action<WallBuyPrompt> listener = _ => heard++;
            WallBuyEvents.PromptChanged += listener;
            try
            {
                var p = new WallBuyPrompt("WB_X", ClassicWeaponId.AutoSmg, "smg_1", new WallBuyQuote(WallBuyAction.Buy, 250, WallBuyBlock.None), Vector3.zero);
                WallBuyEvents.PublishPrompt(p);
                WallBuyEvents.PublishPrompt(p);
                Assert.AreEqual(1, heard);
                WallBuyEvents.PublishPrompt(new WallBuyPrompt("WB_X", ClassicWeaponId.AutoSmg, "smg_1", new WallBuyQuote(WallBuyAction.Buy, 250, WallBuyBlock.NotEnoughGold), Vector3.zero));
                Assert.AreEqual(2, heard);
                WallBuyEvents.PublishPrompt(default);
                Assert.AreEqual(3, heard);
                Assert.IsFalse(WallBuyEvents.CurrentPrompt.Visible);
            }
            finally
            {
                WallBuyEvents.PromptChanged -= listener;
            }
        }

        [Test]
        public void HudIstemi_HerRedIkonlu_SatinAlmaEtiketteKoban()
        {
            // WP-107B (AD 12.10 MUST 4): a refusal is never colour alone.
            foreach (WallBuyAction action in new[] { WallBuyAction.Buy, WallBuyAction.Refill })
            {
                Assert.IsNotNull(DD.Prototype.UI.HudWallBuyPrompt.IconFileFor(WallBuyBlock.NotEnoughGold, action), "yetersiz altın " + action);
                Assert.IsNotNull(DD.Prototype.UI.HudWallBuyPrompt.IconFileFor(WallBuyBlock.SpecialActive, action), "Special " + action);
                Assert.IsNotNull(DD.Prototype.UI.HudWallBuyPrompt.IconFileFor(WallBuyBlock.AmmoFull, action), "dolu cephane " + action);
                Assert.IsNull(DD.Prototype.UI.HudWallBuyPrompt.IconFileFor(WallBuyBlock.Unavailable, action), "donmuş koşuda istem yok");
            }

            Assert.AreEqual(DD.Prototype.UI.HudWallBuyPrompt.IconAmmo, DD.Prototype.UI.HudWallBuyPrompt.IconFileFor(WallBuyBlock.None, WallBuyAction.Refill));
            Assert.IsNull(DD.Prototype.UI.HudWallBuyPrompt.IconFileFor(WallBuyBlock.None, WallBuyAction.Buy), "satın almada koban fiyat etiketinde");
            foreach (string file in new[] { DD.Prototype.UI.HudWallBuyPrompt.IconAmmo, DD.Prototype.UI.HudWallBuyPrompt.IconRefused })
            {
                Assert.IsNotNull(Resources.Load<Sprite>(DD.Prototype.UI.HudArt.Folder + file), file + " Resources'ta");
            }
        }

        // ------------------------------------------------------------------ shipped catalog

        private static List<ClassicWeaponId> SoldWeapons()
        {
            var ids = new List<ClassicWeaponId>();
            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                WeaponDefinition d = Arsenal.Get(id);
                if (d != null && d.enabled) ids.Add(id);
            }

            return ids;
        }

        [Test]
        public void Katalog_HerKlasikSilahinFiyatiVeIstasyonuVar_SpecialYok()
        {
            WallBuyCatalog c = WallBuyCatalog.Current;
            Assert.IsNotNull(c, "Resources/" + WallBuyCatalog.ResourcePath);
            Assert.AreEqual("dd-wallbuy-v1", c.Schema);
            Assert.AreEqual(0.5f, c.RefillShare, 1e-6f, "aynı silah = yarı fiyata cephane");
            Assert.That(c.InteractRadius, Is.InRange(1.5f, 3f));

            List<ClassicWeaponId> sold = SoldWeapons();
            Assert.AreEqual(10, sold.Count, "ar_1 … rl_1");
            foreach (ClassicWeaponId id in sold)
            {
                Assert.Greater(c.PriceOf(id), 0, id + " fiyatı");
                Assert.IsTrue(c.Stations.Any(s => WallBuyCatalog.TryWeapon(s.weapon, out ClassicWeaponId w) && w == id), id + " istasyonu");
            }

            Assert.IsFalse(WallBuyCatalog.TryWeapon("sw_arc", out _), "Special duvardan satılmaz");
            Assert.IsFalse(WallBuyCatalog.TryWeapon("sw_gel", out _));
            Assert.AreEqual(c.Stations.Count, c.Stations.Select(s => s.id).Distinct().Count(), "istasyon kimlikleri tekil");
            foreach (WallBuyCatalog.StationRow s in c.Stations)
            {
                Assert.That(Mathf.Abs(s.x), Is.LessThanOrEqualTo(55f), s.id);
                Assert.That(Mathf.Abs(s.z), Is.LessThanOrEqualTo(55f), s.id);
            }
        }

        [Test]
        public void Roketatar_EnPahali_VeEnUzakIstasyon()
        {
            WallBuyCatalog c = WallBuyCatalog.Current;
            int rocket = c.PriceOf(ClassicWeaponId.Rl1);
            int nextDearest = SoldWeapons().Where(id => id != ClassicWeaponId.Rl1).Max(id => c.PriceOf(id));
            Assert.GreaterOrEqual(rocket, nextDearest * 3, "kullanıcı: roketatar çok pahalı");

            float Far(WallBuyCatalog.StationRow s) => new Vector2(s.x - V6PlayerStart.x, s.z - V6PlayerStart.z).magnitude;
            WallBuyCatalog.StationRow farthest = c.Stations.OrderByDescending(Far).First();
            Assert.IsTrue(WallBuyCatalog.TryWeapon(farthest.weapon, out ClassicWeaponId w) && w == ClassicWeaponId.Rl1,
                "kullanıcı: roketatara ulaşmak zor - başlangıçtan en uzak istasyon (" + farthest.id + ")");
            Assert.AreEqual(1, c.Stations.Count(s => s.weapon == "rl_1"), "tek roketatar istasyonu");
        }

        [Test]
        public void Parse_BozukMetin_Null_BilinmeyenSilahAtlanir()
        {
            Assert.IsNull(WallBuyCatalog.Parse(""));
            WallBuyCatalog c = WallBuyCatalog.Parse("{\"schema\":\"dd-wallbuy-v1\",\"prices\":[{\"weapon\":\"ar_1\",\"price\":100},{\"weapon\":\"zz_9\",\"price\":5}]," +
                                                    "\"stations\":[{\"id\":\"A\",\"weapon\":\"ar_1\",\"x\":1,\"z\":2,\"yaw\":0},{\"id\":\"B\",\"weapon\":\"sw_arc\",\"x\":0,\"z\":0,\"yaw\":0}]}");
            Assert.IsNotNull(c);
            Assert.AreEqual(100, c.PriceOf(ClassicWeaponId.AssaultRifle));
            Assert.AreEqual(50, c.RefillPriceOf(ClassicWeaponId.AssaultRifle));
            Assert.AreEqual(0, c.PriceOf(ClassicWeaponId.AutoSmg));
            Assert.AreEqual(1, c.Stations.Count, "Special istasyonu atlanır");
        }

        // ------------------------------------------------------------------ unlock data

        [Test]
        public void Acilis_RoketatarHaricHepsiBastanAcik_RoketatarYalnizDuvardan()
        {
            foreach (ClassicWeaponId id in SoldWeapons())
            {
                WeaponDefinition d = Arsenal.Get(id);
                if (id == ClassicWeaponId.Rl1)
                {
                    Assert.AreEqual(WeaponUnlockV3.WallBuy, d.unlock.kind);
                    Assert.IsTrue(d.IsWallBuyOnly);
                    Assert.IsTrue(ProfileService.IsWallBuyOnly(id));
                    Assert.AreEqual(ClassicWeaponSettings.NotOnProfileLadder, d.CreateLegacySettings().UnlockProfileLevel, "merdivende yok");
                    Assert.AreEqual(MetaLockReason.WallBuyOnly, ProfileService.WeaponLock(id).LockReason);
                    Assert.IsFalse(ProfileService.IsWeaponUnlocked(id), "başlangıç silahı olamaz");
                }
                else
                {
                    Assert.AreEqual(WeaponUnlockV3.Default, d.unlock.kind, id.ToString());
                    Assert.IsFalse(ProfileService.IsWallBuyOnly(id), id.ToString());
                    Assert.AreEqual(1, ProfileService.UnlockLevelFor(id), id + " seviye 1");
                }
            }
        }

        [Test]
        public void Profil_SeviyeBirde_DokuzSilahVerilir_RoketatarListedeOlsaBileKapali()
        {
            var p = new ProfileData { ProfileLevel = 1 };
            ProfileProgression.GrantUnlocksForLevel(p, new RealLadder(), null);
            foreach (ClassicWeaponId id in SoldWeapons())
            {
                bool want = id != ClassicWeaponId.Rl1;
                Assert.AreEqual(want, ProfileProgression.IsWeaponEntitled(p, id), id.ToString());
            }

            // A profile that bought Bohiya on the old meta path keeps the entry, but the gate never opens it as a start weapon.
            p.UnlockedWeapons.Add("rl_1");
            Assert.IsTrue(ProfileProgression.IsWeaponEntitled(p, ClassicWeaponId.Rl1));
            Assert.IsTrue(ProfileService.IsWallBuyOnly(ClassicWeaponId.Rl1));
        }

        private sealed class RealLadder : IUnlockLadder
        {
            public int UnlockLevelFor(ClassicWeaponId id) => ProfileService.UnlockLevelFor(id);
            public int UnlockLevelFor(UpgradeBranch branch) => ProfileService.UnlockLevelFor(branch);
            public string WeaponDisplayName(ClassicWeaponId id) => id.ToString();
            public string BranchDisplayName(UpgradeBranch branch) => branch.ToString();
        }

        [Test]
        public void MetaYol_KulluSilahDugumuYok_RoketatarDugumuKalkti()
        {
            Assert.IsNull(MetaPathCatalog.Find("w_rl_1"), "roketatar meta yoldan alınmaz");
            int weapons = 0;
            foreach (MetaPathNode n in MetaPathCatalog.Nodes)
            {
                if (n.Kind != MetaNodeKind.Weapon) continue;
                weapons++;
                Assert.AreEqual(0, n.AshCost, n.Id);
                Assert.AreEqual(1, n.LevelGate, n.Id);
            }

            Assert.AreEqual(0, weapons, "WP-104 B1a: the weapons page is retired (Awakening replaces it); WP-107 opens every weapon but rl_1 without the meta path");
        }

        // ------------------------------------------------------------------ wallet

        [Test]
        public void Cuzdan_WallBuyHarcamasi_GoldSpentIcindeAyriTutulur()
        {
            var w = new GoldWallet();
            w.InitializeForRun(7);
            Assert.AreEqual(GoldTransactionResult.Applied, w.Credit(1000, GoldSource.EnemyPickup, w.NextTransactionId()));
            Assert.AreEqual(GoldTransactionResult.Applied, w.TrySpend(250, GoldSource.WallBuy, w.NextTransactionId()));
            Assert.AreEqual(GoldTransactionResult.Applied, w.TrySpend(125, GoldSource.WallBuy, w.NextTransactionId()));
            Assert.AreEqual(GoldTransactionResult.InsufficientBalance, w.TrySpend(700, GoldSource.WallBuy, w.NextTransactionId()));
            Assert.AreEqual(625, w.Balance);
            Assert.AreEqual(375, w.SpentOnWallBuys);
            Assert.AreEqual(375, w.TotalSpent);
            Assert.AreEqual(0, w.SpentOnUpgrades);
        }
    }
}
