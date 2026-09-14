using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-005 · Profile / Statistics / Achievements sunum modelleri (saf; config gerekmez).
    /// Profil seviyesi güç gibi sunulmaz; 0 ile "unknown" karışmaz; görülmemiş tier vurgusuz;
    /// achievement yoksa empty state; unlock satırları prototip merdiveninden.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class ProfilePresentationTests
    {
        /// <summary>Sahte lookup: 100 + 75(L-1) eğrisi ve istek merdiveni (SMG L2, Shotgun L3, Explosion L2, Chain L3).</summary>
        private sealed class SahteLookup : IProfileLookup
        {
            public List<AchievementSettings> Tanimlar = new List<AchievementSettings>();
            public bool Bozuk;

            public int LevelRequirement(int level) => 100 + 75 * (Mathf.Max(1, level) - 1);
            public string WeaponDisplayName(ClassicWeaponId id) => "W-" + id;
            public int UnlockLevelFor(ClassicWeaponId id) => id == ClassicWeaponId.AutoSmg ? 2 : id == ClassicWeaponId.CombatShotgun ? 3 : 1;
            public string BranchDisplayName(UpgradeBranch branch) => "B-" + branch;
            public int UnlockLevelFor(UpgradeBranch branch) => branch == UpgradeBranch.Explosion ? 2 : branch == UpgradeBranch.Chain ? 3 : 1;
            public IReadOnlyList<AchievementSettings> Achievements => Tanimlar;
            public bool RecoveredFromCorruptSave => Bozuk;
            public string Bildirim = string.Empty;
            public string PersistenceNotice => Bildirim;
        }

        private static AchievementSettings Tanim(string id, string ad)
        {
            return new AchievementSettings { Id = id, DisplayName = ad, Description = ad + " desc" };
        }

        private static ProfileData Profil()
        {
            var p = new ProfileData
            {
                ProfileLevel = 2, XpIntoLevel = 40, TotalXpEarned = 140, HighestRound = 4, RunsPlayed = 3,
                RoundsCompleted = 6, TotalKills = 31, StandardKills = 31, ArmorPiecesBroken = 2, BestRunXp = 80
            };
            p.Achievements.Add("first_blood");

            // WP-009: unlock satırları KAZANILMIŞ entitlement listesini okur (seviye karşılaştırması değil);
            // L2 profilin merdivenden kazanmış olacağı içerik listede.
            p.UnlockedWeapons.Add("AssaultRifle");
            p.UnlockedWeapons.Add("AutoSmg");
            p.UnlockedBranches.Add("Explosion");
            return p;
        }

        [Test]
        public void Profil_SeviyeIlerlemeVeBasliklar()
        {
            var lookup = new SahteLookup();
            lookup.Tanimlar.Add(Tanim("first_blood", "First Blood"));
            lookup.Tanimlar.Add(Tanim("reach_round_5", "Reach Round 5"));

            ProfileViewModel vm = ProfileViewModel.From(Profil(), lookup);

            Assert.AreEqual("LEVEL 2", vm.LevelText);
            Assert.AreEqual(40f / 175f, vm.Progress01, 0.0001f);
            Assert.AreEqual("40 / 175 XP towards level 3", vm.ProgressCaption);

            Assert.AreEqual("Highest Round", vm.Headline[0].Label);
            Assert.AreEqual("4", vm.Headline[0].Value);
            Assert.AreEqual("Rounds Completed", vm.Headline[1].Label);
            Assert.AreEqual("6", vm.Headline[1].Value);
            Assert.AreEqual("Total Kills", vm.Headline[2].Label);
            Assert.AreEqual("31", vm.Headline[2].Value);

            Assert.AreEqual("1 of 2 achievements earned", vm.AchievementSummary);
            Assert.AreEqual(string.Empty, vm.Notice);
        }

        [Test]
        public void Profil_UnlockSatirlari_MerdivendenVeSeviyeden()
        {
            ProfileViewModel vm = ProfileViewModel.From(Profil(), new SahteLookup());

            // 3 silah + 2 kilitli dal = 5 satır; L2: AssaultRifle ve AutoSmg açık, Shotgun L3 kilitli.
            Assert.AreEqual(5, vm.Unlocks.Count);
            Assert.IsTrue(vm.Unlocks[0].Unlocked, "AssaultRifle L1");
            Assert.IsTrue(vm.Unlocks[1].Unlocked, "AutoSmg L2");
            Assert.IsFalse(vm.Unlocks[2].Unlocked, "CombatShotgun L3 kilitli");
            Assert.AreEqual(3, vm.Unlocks[2].RequiredLevel);
            Assert.AreEqual("W-CombatShotgun", vm.Unlocks[2].Title);
            Assert.IsTrue(vm.Unlocks[3].Unlocked, "Explosion L2");
            Assert.IsFalse(vm.Unlocks[4].Unlocked, "Chain L3");
            StringAssert.EndsWith(" branch", vm.Unlocks[4].Title);
        }

        [Test]
        public void Profil_UnlockSatirlari_EntitlementListesiOtoritedir_SeviyeDegil()
        {
            // WP-009: seviye yükselmiş ama liste kazanımı içermiyorsa satır KİLİTLİ; liste içeriyorsa
            // seviye eşiğin altına düşse (config yükselse) bile AÇIK kalır.
            ProfileData p = Profil();
            p.ProfileLevel = 9;
            ProfileViewModel vm = ProfileViewModel.From(p, new SahteLookup());
            Assert.IsFalse(vm.Unlocks[2].Unlocked, "CombatShotgun listede yok → seviye 9 olsa da kilitli görünür");
            Assert.IsFalse(vm.Unlocks[4].Unlocked, "Chain listede yok → kilitli");

            p.ProfileLevel = 1;
            p.UnlockedWeapons.Add("CombatShotgun");
            p.UnlockedBranches.Add("Chain");
            vm = ProfileViewModel.From(p, new SahteLookup());
            Assert.IsTrue(vm.Unlocks[1].Unlocked, "AutoSmg listede → L1'de bile açık (kazanılmış entitlement geri alınmaz)");
            Assert.IsTrue(vm.Unlocks[2].Unlocked, "CombatShotgun listede → açık");
            Assert.IsTrue(vm.Unlocks[4].Unlocked, "Chain listede → açık");
            Assert.AreEqual(3, vm.Unlocks[2].RequiredLevel, "merdiven değeri bilgi olarak kalır");
        }

        [Test]
        public void Profil_KalicilikBildirimi_LookupTanOkunur()
        {
            var lookup = new SahteLookup { Bildirim = "Another copy of the game is already using this profile." };
            ProfileViewModel vm = ProfileViewModel.From(Profil(), lookup);
            Assert.AreEqual(lookup.Bildirim, vm.Notice);

            lookup = new SahteLookup { Bozuk = true };
            vm = ProfileViewModel.From(Profil(), lookup);
            StringAssert.Contains("could not be read", vm.Notice, "bildirim yokken bozuk-kayıt metni korunur");
        }

        [Test]
        public void Istatistik_AtanmamisKill_YalnizSifirdanBuyukseSatir()
        {
            ProfileData p = Profil();
            StatisticsViewModel vm = StatisticsViewModel.From(p);
            for (int i = 0; i < vm.Kills.Count; i++)
            {
                Assert.AreNotEqual("Recorded Before Tier Tracking", vm.Kills[i].Label, "0 iken satır yok");
            }

            p.TotalKills = 40; // legacy: toplam > tier toplamı → UnattributedKills 9
            p.Sanitize();
            Assert.AreEqual(9, p.UnattributedKills);
            Assert.AreEqual(40, p.TotalKills);
            vm = StatisticsViewModel.From(p);
            bool bulundu = false;
            for (int i = 0; i < vm.Kills.Count; i++)
            {
                if (vm.Kills[i].Label == "Recorded Before Tier Tracking")
                {
                    bulundu = true;
                    Assert.AreEqual("9", vm.Kills[i].Value);
                }
            }

            Assert.IsTrue(bulundu, "atanmamış kill satırı gösterilir, gizlenmez");
        }

        [Test]
        public void Profil_GucGostergesiTasimaz()
        {
            ProfileViewModel vm = ProfileViewModel.From(Profil(), new SahteLookup());
            string[] yasak = { "damage", "health bonus", "starting gold", "boost" };

            var metinler = new List<string> { vm.LevelText, vm.ProgressCaption, vm.AchievementSummary, ProfileViewModel.RuleNote };
            for (int i = 0; i < vm.Headline.Count; i++)
            {
                metinler.Add(vm.Headline[i].Label);
            }

            for (int i = 0; i < metinler.Count; i++)
            {
                string m = metinler[i].ToLowerInvariant();
                for (int j = 0; j < yasak.Length; j++)
                {
                    // Kural cümlesi "never grants starting damage, health or gold" der: yasağı söylemek serbest.
                    if (metinler[i] == ProfileViewModel.RuleNote)
                    {
                        continue;
                    }

                    Assert.IsFalse(m.Contains(yasak[j]), "'" + metinler[i] + "' güç göstergesi içeriyor: " + yasak[j]);
                }
            }

            StringAssert.Contains("never grants starting damage, health or gold", ProfileViewModel.RuleNote);
        }

        [Test]
        public void Profil_TazeProfil_TireVeSifir()
        {
            ProfileViewModel vm = ProfileViewModel.From(new ProfileData(), new SahteLookup());
            Assert.AreEqual("—", vm.Headline[0].Value, "hiç koşu yok → en yüksek round bilinmiyor");
            Assert.AreEqual("0", vm.Headline[2].Value, "kill sayısı gerçek sıfır");
            Assert.AreEqual("No achievements are defined in this build yet.", vm.AchievementSummary);
        }

        [Test]
        public void Profil_BozukKayitBildirimi()
        {
            ProfileViewModel vm = ProfileViewModel.From(new ProfileData(), new SahteLookup { Bozuk = true });
            StringAssert.Contains("could not be read", vm.Notice);
        }

        [Test]
        public void Istatistik_GruplarVeGorulmemisTierVurgusuz()
        {
            ProfileData p = Profil();
            p.EliteKills = 1;
            p.TotalKills = 32;

            StatisticsViewModel vm = StatisticsViewModel.From(p);

            Assert.AreEqual(string.Empty, vm.EmptyNotice);
            Assert.AreEqual("Runs Played", vm.Runs[0].Label);
            Assert.AreEqual("3", vm.Runs[0].Value);
            Assert.AreEqual("Highest Round", vm.Runs[1].Label);
            Assert.AreEqual("Rounds Completed", vm.Runs[2].Label);

            Assert.AreEqual("Total Kills", vm.Kills[0].Label);
            Assert.AreEqual("32", vm.Kills[0].Value);
            Assert.AreEqual("Standard", vm.Kills[1].Label);
            Assert.AreEqual("31", vm.Kills[1].Value);

            Assert.AreEqual("Special", vm.Kills[2].Label);
            Assert.AreEqual("0", vm.Kills[2].Value, "kayıtlı sıfır: sıfır yazılır, 'unknown' değil");
            Assert.AreEqual(UiColor.TextMuted, vm.Kills[2].Color, "görülmemiş tier vurgusuz");

            Assert.AreEqual("Elite", vm.Kills[3].Label);
            Assert.AreEqual("1", vm.Kills[3].Value);
            Assert.AreNotEqual(UiColor.TextMuted, vm.Kills[3].Color, "görülen tier normal/vurgulu");

            Assert.AreEqual("Ultra", vm.Kills[4].Label);
            Assert.AreEqual(UiColor.TextMuted, vm.Kills[4].Color);

            Assert.AreEqual("Armour Pieces Broken", vm.Kills[5].Label);
            Assert.AreEqual("2", vm.Kills[5].Value);

            Assert.IsNotEmpty(vm.TierCaption);
            Assert.AreEqual("Profile Level", vm.Progress[0].Label);
            Assert.AreEqual("Lifetime XP", vm.Progress[1].Label);
            Assert.AreEqual("140", vm.Progress[1].Value);
            Assert.AreEqual("Best Single Run XP", vm.Progress[2].Label);
        }

        [Test]
        public void Istatistik_KosuYok_EmptyNotice()
        {
            StatisticsViewModel vm = StatisticsViewModel.From(new ProfileData());
            Assert.IsNotEmpty(vm.EmptyNotice);
            Assert.AreEqual("0", vm.Runs[0].Value);
            Assert.AreEqual(6, vm.Kills.Count, "şema satırları yine mevcut");
        }

        [Test]
        public void Basarimlar_ListeVeIlerleme()
        {
            var tanimlar = new List<AchievementSettings>
            {
                Tanim("first_blood", "First Blood"), Tanim("armor_breaker", "Armor Breaker"), null,
                new AchievementSettings { Id = string.Empty, DisplayName = "Kimliksiz" }
            };

            AchievementsViewModel vm = AchievementsViewModel.From(Profil(), tanimlar);
            Assert.AreEqual(2, vm.Defined, "null ve kimliksiz tanım atlanır");
            Assert.AreEqual(1, vm.Earned);
            Assert.AreEqual("1 of 2 earned", vm.Progress);
            Assert.IsFalse(vm.IsEmpty);
            Assert.IsTrue(vm.Lines[0].Earned);
            Assert.AreEqual("First Blood", vm.Lines[0].Title);
            Assert.IsFalse(vm.Lines[1].Earned);
        }

        [Test]
        public void Basarimlar_TanimYok_EmptyState_HataYok()
        {
            AchievementsViewModel vm = AchievementsViewModel.From(Profil(), new List<AchievementSettings>());
            Assert.IsTrue(vm.IsEmpty);
            Assert.AreEqual(0, vm.Lines.Count);
            Assert.AreEqual(string.Empty, vm.Progress);
            Assert.IsNotEmpty(AchievementsViewModel.EmptyTitle);
            Assert.IsNotEmpty(AchievementsViewModel.EmptyBody);

            vm = AchievementsViewModel.From(null, null);
            Assert.IsTrue(vm.IsEmpty, "null girdiler de güvenli");
            StringAssert.Contains("nothing to claim", AchievementsViewModel.AutoNote);
        }
    }
}
