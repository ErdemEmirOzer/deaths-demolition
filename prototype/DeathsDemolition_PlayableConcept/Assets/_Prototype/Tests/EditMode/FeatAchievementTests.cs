using DD.Prototype.Achievements;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 A10 · R1 içerik paketi §6: on iki silah / Special başarımı Feat koşuluyla. Koşu içi kayıt (max = tek olayın
    /// en iyisi, toplam = koşu toplamı; koşu yokken yok sayılır), yaşam boyu birleştirme (toplamlar eklenir, max'lar en iyisi),
    /// motor (tek koşu / yaşam boyu), katalog doğrulaması (bilinmeyen feat düşer, ArsenalRound10 yalnız yaşam boyu) ve
    /// "her silahla 10. round" — profilin ilk-R10 kayıtları + bu koşunun silahı, eşik yapıdaki klasik silah sayısıyla sınırlı.
    /// </summary>
    [TestFixture]
    public sealed class FeatAchievementTests
    {
        [TearDown]
        public void TearDown() => AchievementRunTracker.Clear();

        private static AchievementDefinition Tanim(string id)
        {
            foreach (AchievementDefinition d in AchievementCatalog.All) if (d.Id == id) return d;
            return null;
        }

        [Test]
        public void Katalog_OnIkiYeni_FeatKosulu()
        {
            string[] idler =
            {
                "raiju_full_chain", "raiju_kills_100", "raiju_grounded", "namekuji_puddles", "namekuji_executioner", "namekuji_skater",
                "tomi_line", "bohiya_one_rocket", "nito_double", "kurogane_plates", "tetsui_runners", "full_arsenal"
            };
            foreach (string id in idler)
            {
                AchievementDefinition d = Tanim(id);
                Assert.IsNotNull(d, id);
                Assert.AreEqual(AchievementCondition.Feat, d.Condition, id);
            }

            Assert.AreEqual(FeatKind.RaijuChainMax, Tanim("raiju_full_chain").Feat);
            Assert.AreEqual(FeatKind.ArsenalRound10, Tanim("full_arsenal").Feat);
        }

        [Test]
        public void Kayit_MaxEnIyiOlay_ToplamEklenir_KosuYokkenYok()
        {
            AchievementRunTracker.RecordFeatMax(FeatKind.RaijuChainMax, 5);
            Assert.AreEqual(0, AchievementRunTracker.Current.Feat(FeatKind.RaijuChainMax), "koşu yok → yok sayılır");

            AchievementRunTracker.Begin("run-a");
            AchievementRunTracker.RecordFeatMax(FeatKind.RaijuChainMax, 5);
            AchievementRunTracker.RecordFeatMax(FeatKind.RaijuChainMax, 3);
            AchievementRunTracker.AddFeat(FeatKind.TetsuiRunnerKills);
            AchievementRunTracker.AddFeat(FeatKind.TetsuiRunnerKills, 2);
            RunFeatCounters c = AchievementRunTracker.CountersFor("run-a");
            Assert.AreEqual(5, c.Feat(FeatKind.RaijuChainMax), "max = en iyi tek olay");
            Assert.AreEqual(3, c.Feat(FeatKind.TetsuiRunnerKills), "toplam");
        }

        [Test]
        public void YasamBoyu_ToplamlarEklenir_MaxlarEnIyisi()
        {
            var p = new ProfileData();
            p.Sanitize();
            var f = new RunFeatCounters { FeatValues = new int[FeatKinds.Count] };
            f.FeatValues[(int)FeatKind.RaijuKills] = 40;
            f.FeatValues[(int)FeatKind.PuddlesMax] = 9;
            ProfileProgression.ApplyFeats(p, f);
            f.FeatValues[(int)FeatKind.RaijuKills] = 70;
            f.FeatValues[(int)FeatKind.PuddlesMax] = 6;
            ProfileProgression.ApplyFeats(p, f);
            Assert.AreEqual(110, p.FeatTotal(FeatKind.RaijuKills));
            Assert.AreEqual(9, p.FeatTotal(FeatKind.PuddlesMax));
        }

        [Test]
        public void Motor_TekKosuVeYasamBoyu()
        {
            var p = new ProfileData();
            p.Sanitize();
            var f = new RunFeatCounters { FeatValues = new int[FeatKinds.Count] };
            f.FeatValues[(int)FeatKind.TomiKillsPerShotMax] = 4;
            var input = new RunRewardInput(new RunSummary { ReachedRound = 3, CompletedRounds = 2 });
            Assert.IsTrue(AchievementEngine.IsSatisfied(p, input, f, Tanim("tomi_line")));
            f.FeatValues[(int)FeatKind.TomiKillsPerShotMax] = 3;
            Assert.IsFalse(AchievementEngine.IsSatisfied(p, input, f, Tanim("tomi_line")));

            p.FeatLifetime[(int)FeatKind.RaijuKills] = 99;
            Assert.IsFalse(AchievementEngine.IsSatisfied(p, input, f, Tanim("raiju_kills_100")));
            p.FeatLifetime[(int)FeatKind.RaijuKills] = 100;
            Assert.IsTrue(AchievementEngine.IsSatisfied(p, input, f, Tanim("raiju_kills_100")));
        }

        [Test]
        public void Arsenal_IlkR10Kayitlari_BuKosununSilahi_EsikYapidakiSilahSayisi()
        {
            var p = new ProfileData();
            p.Sanitize();
            int yapida = DD.Prototype.Core.Weapons.WeaponRoster.Enabled.Count;
            string[] anahtarlar = { "ar_1", "smg_1", "sg_1", "ar_2", "smg_2", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1" };
            for (int i = 0; i < yapida - 1 && i < anahtarlar.Length; i++) p.Grant(p.AshMilestones, "r10:" + anahtarlar[i]);
            Assert.AreEqual(yapida - 1, AchievementEngine.CountRound10Weapons(p));

            var ulasmadi = new RunRewardInput(new RunSummary { ReachedRound = 9, CompletedRounds = 8, Weapon = DD.Prototype.Core.Config.ClassicWeaponId.Sr1 });
            Assert.IsFalse(AchievementEngine.IsSatisfied(p, ulasmadi, RunFeatCounters.None, Tanim("full_arsenal")));

            // the last weapon reaches round 10 in THIS run: its first-R10 record is written after the rules, so the rule counts it now.
            ProfileData tek = p.Clone();
            tek.AshMilestones.Clear();
            for (int i = 0; i < yapida - 1; i++) tek.Grant(tek.AshMilestones, "r10:x" + i);
            var ulasti = new RunRewardInput(new RunSummary { ReachedRound = 10, CompletedRounds = 9, Weapon = DD.Prototype.Core.Config.ClassicWeaponId.Sr1 });
            Assert.IsTrue(AchievementEngine.IsSatisfied(tek, ulasti, RunFeatCounters.None, Tanim("full_arsenal")), "eşik " + yapida + " silah");
        }

        [Test]
        public void Katalog_BilinmeyenFeatDuser_ArsenalYalnizYasamBoyu()
        {
            string Json(string feat, string scope) =>
                "{\"Version\":1,\"Definitions\":[{\"Id\":\"x\",\"Condition\":\"Feat\",\"Threshold\":1,\"Scope\":\"" + scope + "\",\"Feat\":\"" + feat + "\"}]}";
            Assert.AreEqual(0, AchievementCatalog.Parse(Json("Nope", "SingleRun"), out _).Count);
            Assert.AreEqual(0, AchievementCatalog.Parse(Json("ArsenalRound10", "SingleRun"), out _).Count);
            Assert.AreEqual(1, AchievementCatalog.Parse(Json("ArsenalRound10", "Lifetime"), out _).Count);
            Assert.AreEqual(1, AchievementCatalog.Parse(Json("RaijuChainMax", "SingleRun"), out _).Count);
        }

        [Test]
        public void Izleyici_TomiAtisBasinaOldurme_UltraJelDusus()
        {
            FeatTracker.ResetState();
            AchievementRunTracker.Begin("run-b");
            DD.Prototype.Weapons.ProjectileImpactInfo Vurus(int id, bool oldu) => new DD.Prototype.Weapons.ProjectileImpactInfo(id, 0, DD.Prototype.Core.Config.ClassicWeaponId.Sr1,
                default, default, UnityEngine.Vector3.zero, UnityEngine.Vector3.up, UnityEngine.Vector3.forward, default, 100f, true, false, oldu, false, 1);
            for (int i = 0; i < 3; i++) FeatTracker.TestSeam.Impact(Vurus(7, true));
            FeatTracker.TestSeam.Impact(Vurus(7, false));
            FeatTracker.TestSeam.Impact(Vurus(8, true));
            Assert.AreEqual(3, AchievementRunTracker.CountersFor("run-b").Feat(FeatKind.TomiKillsPerShotMax), "tek merminin 3 öldürmesi");

            var dusus = new DD.Prototype.Enemies.EnemyCombatEventInfo(DD.Prototype.Enemies.EnemyCombatEventKind.SpecialFall, null,
                DD.Prototype.Core.Config.EnemyTier.Ultra, 5, UnityEngine.Vector3.zero, UnityEngine.Vector3.forward, default, false, 1.5f);
            FeatTracker.TestSeam.CombatEvent(dusus);
            Assert.AreEqual(1, AchievementRunTracker.CountersFor("run-b").Feat(FeatKind.GelKnockdownUltra));
            FeatTracker.ResetState();
        }
    }
}
