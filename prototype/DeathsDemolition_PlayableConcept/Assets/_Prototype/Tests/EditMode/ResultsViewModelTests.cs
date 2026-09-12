using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · Results sunum modeli: değişmez rapordan üretilir, raporu değiştirmez,
    /// Reached/Completed/Total/Standard doğru, olmayan varyant satırı uydurulmaz,
    /// gold/build özeti RunSummary ile eşleşir, boş rapor için empty state, ödül kapağı.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class ResultsViewModelTests
    {
        private static string SilahAdi(ClassicWeaponId id)
        {
            return "Weapon:" + id;
        }

        private static RunRewardReport Rapor(RunSummary ozet, int roundXp, int killXp, int levelBefore = 1,
            int levelAfter = 1)
        {
            var rapor = new RunRewardReport
            {
                HasRun = true,
                Summary = ozet,
                CompletedRoundXp = roundXp,
                KillXp = killXp,
                TotalXpEarned = roundXp + killXp,
                LevelBefore = levelBefore,
                LevelAfter = levelAfter,
                XpIntoLevelAfter = 40,
                LevelRequirementAfter = 175
            };
            return rapor;
        }

        private static RunSummary Ozet()
        {
            return new RunSummary
            {
                ReachedRound = 3,
                CompletedRounds = 2,
                TotalKills = 17,
                StandardKills = 17,
                CompletedRoundXp = 45,
                KillXp = 17,
                TotalXpEarned = 62,
                Weapon = ClassicWeaponId.AssaultRifle,
                BuildDescription = "P1 / FR2 / MS1",
                GoldEarned = 34,
                GoldSpent = 25,
                GoldUnspent = 9,
                UpgradesPurchased = 1,
                RunDuration = 155.7f,
                EndReason = RunEndReason.PlayerDeath
            };
        }

        [Test]
        public void ReachedCompletedTotalStandard_Dogru()
        {
            ResultsViewModel vm = ResultsViewModel.From(Rapor(Ozet(), 45, 17), SilahAdi);

            Assert.IsTrue(vm.HasRun);
            Assert.AreEqual("DEFEATED IN ROUND 3", vm.Eyebrow);
            Assert.AreEqual("3", vm.ReachedRound);
            Assert.AreEqual("2 rounds completed", vm.RoundsCompletedCaption);

            Assert.AreEqual("Total Kills", vm.Combat[0].Label);
            Assert.AreEqual("17", vm.Combat[0].Value);
            Assert.IsTrue(vm.Combat[0].Emphasis);
            Assert.AreEqual("Standard", vm.Combat[1].Label);
            Assert.AreEqual("17", vm.Combat[1].Value);
            Assert.AreEqual(2, vm.Combat.Count, "yalnız Total + Standard: sıfır varyant satırı yok");

            Assert.AreEqual("MAIN MENU", ResultsViewModel.ContinueLabel);
            Debug.Log("[DD-TEST] wp005.results eyebrow='" + vm.Eyebrow + "' loadout='" + vm.Loadout + "'");
        }

        [Test]
        public void BilinmeyenVaryant_Uydurulmaz_GorulenVaryant_Listelenir()
        {
            RunSummary ozet = Ozet();
            ozet.SpecialKills = 0;
            ozet.EliteKills = 2;
            ozet.UltraKills = 0;
            ozet.TotalKills = 19;

            ResultsViewModel vm = ResultsViewModel.From(Rapor(ozet, 45, 17), SilahAdi);

            Assert.AreEqual(3, vm.Combat.Count, "Total, Standard, Elite — Special/Ultra sıfır → satır yok");
            Assert.AreEqual("Elite", vm.Combat[2].Label);
            Assert.AreEqual("2", vm.Combat[2].Value);

            for (int i = 0; i < vm.Combat.Count; i++)
            {
                Assert.AreNotEqual("Special", vm.Combat[i].Label);
                Assert.AreNotEqual("Ultra", vm.Combat[i].Label);
            }
        }

        [Test]
        public void GoldVeBuild_OzetIleEslesir()
        {
            RunSummary ozet = Ozet();
            ResultsViewModel vm = ResultsViewModel.From(Rapor(ozet, 45, 17), SilahAdi);

            Assert.AreEqual("Gold Collected", vm.Economy[0].Label);
            Assert.AreEqual(ozet.GoldEarned.ToString(), vm.Economy[0].Value);
            Assert.AreEqual("Gold Spent", vm.Economy[1].Label);
            Assert.AreEqual(ozet.GoldSpent.ToString(), vm.Economy[1].Value);
            Assert.AreEqual("Upgrades Bought", vm.Economy[2].Label);
            Assert.AreEqual(ozet.UpgradesPurchased.ToString(), vm.Economy[2].Value);

            StringAssert.Contains("Weapon:AssaultRifle", vm.Loadout);
            StringAssert.Contains(ozet.BuildDescription, vm.Loadout);
            StringAssert.Contains("2:35 played", vm.Loadout, "155.7 s → 2:35");

            // Gold hiçbir XP satırında görünmez; "+0" satırı gold/upgrade/süre için açıkça yazılır.
            Assert.AreEqual("+45", vm.XpBreakdown[0].Value);
            Assert.AreEqual("+17", vm.XpBreakdown[1].Value);
            Assert.AreEqual("+0", vm.XpBreakdown[2].Value);
            StringAssert.Contains("Gold", vm.XpBreakdown[2].Label);
            Assert.AreEqual("+62", vm.TotalXp);
        }

        [Test]
        public void StockBuild_NoUpgrades_UzunBuildKirpilir()
        {
            RunSummary ozet = Ozet();
            ozet.BuildDescription = "Stock";
            ResultsViewModel vm = ResultsViewModel.From(Rapor(ozet, 0, 0), SilahAdi);
            StringAssert.Contains("no upgrades", vm.Loadout);

            ozet.BuildDescription = "P3 / R3 / X3 / C3 / FR3 / RL3 / MS3 / HP3 / RD3 / RR3 / FAZLA / FAZLA";
            vm = ResultsViewModel.From(Rapor(ozet, 0, 0), SilahAdi);
            StringAssert.Contains("…", vm.Loadout, "uzun build kırpılır, satır taşmaz");
        }

        [Test]
        public void ViewModel_RaporuDegistirmez()
        {
            RunRewardReport rapor = Rapor(Ozet(), 45, 17, 1, 2);
            rapor.NewUnlocks.Add(new ProfileUnlockEntry("Auto SMG", "Classic weapon"));
            rapor.NewAchievements.Add(new ProfileUnlockEntry("First Blood", "Defeat your first enemy."));

            string once = Parmakizi(rapor);
            ResultsViewModel.From(rapor, SilahAdi);
            ResultsViewModel.From(rapor, SilahAdi);
            ResultsViewModel.From(rapor, SilahAdi);
            string sonra = Parmakizi(rapor);

            Assert.AreEqual(once, sonra, "üç kez model üretmek raporun hiçbir alanını değiştirmemeli");
        }

        [Test]
        public void RunSummary_DegerTipi_KopyaOrijinaliDegistirmez()
        {
            RunRewardReport rapor = Rapor(Ozet(), 45, 17);
            RunSummary kopya = rapor.Summary;
            kopya.TotalKills = 999;
            Assert.AreEqual(17, rapor.Summary.TotalKills, "Summary bir kopya verir; yerinde değişiklik imkânsız");
        }

        [Test]
        public void SeviyeAtlama_ChipVeSatir()
        {
            ResultsViewModel vm = ResultsViewModel.From(Rapor(Ozet(), 45, 17, 1, 3), SilahAdi);
            Assert.IsTrue(vm.LeveledUp);
            Assert.AreEqual(2, vm.LevelsGained);
            Assert.AreEqual("LEVEL 1  →  LEVEL 3", vm.LevelLine);
            Assert.AreEqual("LEVEL UP  x2", vm.LevelUpChip);
            Assert.IsTrue(vm.Celebrate);
            Assert.AreEqual("40 / 175 XP towards level 4", vm.ProgressCaption);

            vm = ResultsViewModel.From(Rapor(Ozet(), 45, 17, 2, 2), SilahAdi);
            Assert.IsFalse(vm.LeveledUp);
            Assert.AreEqual("LEVEL 2", vm.LevelLine);
            Assert.AreEqual(string.Empty, vm.LevelUpChip);
            Assert.IsFalse(vm.Celebrate, "ne seviye ne ödül → kutlama yok");
        }

        [Test]
        public void BosRapor_EmptyState()
        {
            ResultsViewModel vm = ResultsViewModel.From(RunRewardReport.Empty(), SilahAdi);
            Assert.IsFalse(vm.HasRun);
            Assert.IsNotEmpty(vm.EmptyTitle);
            Assert.IsNotEmpty(vm.EmptyBody);
            Assert.AreEqual(0, vm.Combat.Count);
            Assert.AreEqual(0, vm.NewUnlocks.Count);

            vm = ResultsViewModel.From(null, SilahAdi);
            Assert.IsFalse(vm.HasRun, "null rapor da güvenli boş durum");
        }

        [Test]
        public void OdulListesi_KapakVeOzet()
        {
            RunRewardReport rapor = Rapor(Ozet(), 45, 17);
            for (int i = 0; i < 5; i++)
            {
                rapor.NewUnlocks.Add(new ProfileUnlockEntry("Unlock " + i, "detail"));
            }

            ResultsViewModel vm = ResultsViewModel.From(rapor, SilahAdi);
            Assert.AreEqual(5, vm.NewUnlocks.Count, "model tam listeyi taşır; kapak görünüm kuralıdır");
            Assert.AreEqual("and 2 more, already added to your profile", ResultsViewModel.Overflow(5));
            Assert.AreEqual(string.Empty, ResultsViewModel.Overflow(3));
            Assert.IsTrue(vm.Celebrate);
            Assert.AreEqual("No new achievements this run.", vm.AchievementsEmpty);
        }

        [Test]
        public void SonNotlar_ClaimYok_KaydedildiDenir()
        {
            ResultsViewModel vm = ResultsViewModel.From(Rapor(Ozet(), 45, 17), SilahAdi);
            Assert.IsFalse(vm.SaveFailed);
            StringAssert.Contains("already saved", vm.FooterNote);

            RunRewardReport basarisiz = Rapor(Ozet(), 45, 17);
            basarisiz.SaveFailed = true;
            vm = ResultsViewModel.From(basarisiz, SilahAdi);
            Assert.IsTrue(vm.SaveFailed);
            StringAssert.Contains("could not be written", vm.FooterNote);

            // Tek eylem: ana menü. Model bir "claim" eylemi taşımaz.
            FieldInfo[] alanlar = typeof(ResultsViewModel).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < alanlar.Length; i++)
            {
                Assert.IsFalse(alanlar[i].Name.ToLowerInvariant().Contains("claim"), alanlar[i].Name);
            }
        }

        [Test]
        public void Bitis_Aciklamalari()
        {
            RunSummary ozet = Ozet();
            ozet.EndReason = RunEndReason.Aborted;
            Assert.AreEqual("RUN ABANDONED IN ROUND 3", ResultsViewModel.DescribeEnding(ozet));
            ozet.EndReason = RunEndReason.Debug;
            Assert.AreEqual("RUN ENDED IN ROUND 3", ResultsViewModel.DescribeEnding(ozet));
            Assert.AreEqual("0:00", ResultsViewModel.FormatDuration(-3f));
            Assert.AreEqual("10:05", ResultsViewModel.FormatDuration(605.9f));
        }

        private static string Parmakizi(RunRewardReport rapor)
        {
            var sb = new System.Text.StringBuilder();
            foreach (PropertyInfo p in typeof(RunRewardReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = p.GetValue(rapor);
                sb.Append(p.Name).Append('=')
                    .Append(v is RunSummary ? JsonUtility.ToJson(v) : (v != null ? v.ToString() : "null"))
                    .Append(';');
            }

            foreach (FieldInfo f in typeof(RunRewardReport).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(rapor);
                if (v is System.Collections.ICollection c)
                {
                    sb.Append(f.Name).Append("#").Append(c.Count).Append(';');
                    foreach (object o in c)
                    {
                        sb.Append(JsonUtility.ToJson(o)).Append(';');
                    }
                }
                else
                {
                    sb.Append(f.Name).Append('=').Append(v).Append(';');
                }
            }

            return sb.ToString();
        }
    }
}
