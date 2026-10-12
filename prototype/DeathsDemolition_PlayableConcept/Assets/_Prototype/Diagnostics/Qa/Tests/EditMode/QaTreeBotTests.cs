using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Qa.Tests.EditMode
{
    /// <summary>
    /// WP-104 B5b · QA bot'un özellik ağacı politikası: arketip hatlarındaki her düğüm katalogda, Shot hattı ilk round'da kendi
    /// başından alır, karşılanamayan hat düğümü için biriktirir, yalnız canlı düğüm alır, rapor satırı düşer.
    /// </summary>
    [TestFixture]
    public sealed class QaTreeBotTests
    {
        [TearDown]
        public void TearDown()
        {
            QaTreeBot.Reset();
            TreeLiveV2.TreatAllAsLive = false;
            TreeRuntime.Reset();
            TreeEvents.ResetForTests();
        }

        [Test]
        public void Lines_UseCatalogNodesOnly()
        {
            foreach (string arch in new[] { "Shot", "Element", "Barrage", "Tank", "Hoarder" })
            {
                for (int seed = 1; seed <= 6; seed++)
                {
                    foreach (string id in QaTreeBot.BuildLine(arch, seed)) Assert.GreaterOrEqual(TreeCatalogV2.IndexOf(id), 0, arch + ": " + id);
                }
            }
        }

        [Test]
        public void Shot_BuysItsLine_SavesForTheNext_OnlyLiveNodes()
        {
            QaTreeBot.Reset("Shot");
            TreeLiveV2.TreatAllAsLive = true;
            var shop = new RoundUpgradeShop();
            var wallet = new GoldWallet();
            wallet.InitializeForRun(1);
            wallet.Credit(100, GoldSource.EnemyPickup, wallet.NextTransactionId());
            var weapon = new TreeWeaponProfile { Id = ClassicWeaponId.AssaultRifle, Key = "ar_1", FireMode = WeaponFireMode.Auto, BaseRpm = 600f, BaseDamage = 12f, Pellets = 1, Magazine = 30 };
            shop.TreeV2.Begin(1, TreeMode.Normal, wallet, weapon, TreeAwakening.None);
            shop.TreeV2.Open(1);

            string logged = null;
            Assert.IsTrue(QaTreeBot.BuyRound(shop, 1, 26, l => logged = l));
            Assert.IsTrue(shop.TreeV2.Owns("sh_heavy"), "line start");
            Assert.IsTrue(shop.TreeV2.Owns("sp_rate1"), "second on the line, still affordable");
            Assert.IsFalse(shop.TreeV2.Owns("mg_ext"), "R1: sh_double is fogged, the line skips to mg_ext only after it saves gold");
            StringAssert.Contains("[TreeBot] arch=Shot", logged);

            TreeLiveV2.TreatAllAsLive = false;
            QaTreeBot.Reset("Shot");
            var shop2 = new RoundUpgradeShop();
            var wallet2 = new GoldWallet();
            wallet2.InitializeForRun(2);
            wallet2.Credit(500, GoldSource.EnemyPickup, wallet2.NextTransactionId());
            shop2.TreeV2.Begin(2, TreeMode.Normal, wallet2, weapon, TreeAwakening.None);
            shop2.TreeV2.Open(1);
            QaTreeBot.BuyRound(shop2, 1, 26, null);
            // WP-104 B6: every catalog node is live, so without TreatAllAsLive the bot follows the same line.
            Assert.IsTrue(shop2.TreeV2.Owns("sh_heavy"), "live (B2): the line start is bought");
            Assert.IsTrue(shop2.TreeV2.Owns("sp_rate1"), "live (B2): the second on the line too");
        }

        /// <summary>C-00 12.10: saving for one line node more than SaveRoundsBeforeFill round ends buys one fill node a round; the
        /// saved-for node is still bought first once affordable.</summary>
        [Test]
        public void Saving_PastTwoRoundEnds_BuysOneFill_ThenTheSavedNode()
        {
            QaTreeBot.Reset("Shot");
            TreeLiveV2.TreatAllAsLive = true;
            try
            {
                var shop = new RoundUpgradeShop();
                var wallet = new GoldWallet();
                wallet.InitializeForRun(3);
                wallet.Credit(99, GoldSource.EnemyPickup, wallet.NextTransactionId());   // < sh_double 160, < Hearth 2 × 50
                var weapon = new TreeWeaponProfile { Id = ClassicWeaponId.AssaultRifle, Key = "ar_1", FireMode = WeaponFireMode.Auto, BaseRpm = 600f, BaseDamage = 12f, Pellets = 1, Magazine = 30 };
                shop.TreeV2.Begin(3, TreeMode.Normal, wallet, weapon, TreeAwakening.None);
                shop.TreeV2.GrantFree("sh_heavy");
                shop.TreeV2.GrantFree("sp_rate1");
                shop.TreeV2.Open(3);   // tier II open: the line waits for sh_double

                int owned = shop.TreeV2.NodesOwned;
                for (int r = 1; r <= QaTreeBot.SaveRoundsBeforeFill; r++)
                {
                    QaTreeBot.BuyRound(shop, r, 26, null);
                    Assert.AreEqual(owned, shop.TreeV2.NodesOwned, "round end " + r + ": still only saving");
                }

                QaTreeBot.BuyRound(shop, 3, 26, null);
                Assert.AreEqual(owned + 1, shop.TreeV2.NodesOwned, "past two round ends: one fill node");
                Assert.IsFalse(shop.TreeV2.Owns("sh_double"));

                wallet.Credit(300, GoldSource.EnemyPickup, wallet.NextTransactionId());
                QaTreeBot.BuyRound(shop, 4, 26, null);
                Assert.IsTrue(shop.TreeV2.Owns("sh_double"), "the saved-for node first once affordable");
            }
            finally
            {
                TreeLiveV2.TreatAllAsLive = false;
                QaTreeBot.Reset(null);
            }
        }

        [Test]
        public void Inactive_WithoutTheFlag()
        {
            QaTreeBot.Reset(string.Empty);
            Assert.IsFalse(QaTreeBot.Active);
            Assert.IsFalse(QaTreeBot.BuyRound(new RoundUpgradeShop(), 1, 1, null));
        }
    }
}
