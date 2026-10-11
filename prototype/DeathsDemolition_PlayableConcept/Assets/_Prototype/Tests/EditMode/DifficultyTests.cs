using System;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Difficulty;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-102 (R4, C-00 11.10 approved design S-03-11.10-1): the three difficulty modes. Normal is r3-final byte for byte (no copy,
    /// the shipped config instance and its serialization unchanged); Hard / Demolition run on a mode-applied copy and leave the
    /// shipped asset untouched; the asset carries the code defaults; every speed a mode sets is honest on its gait pool for the
    /// tier's body size at the jitter corners (a speed outside the band slides the feet); the pursuit / cut / open-side math.
    /// </summary>
    public sealed class DifficultyTests
    {
        private const string ZombieSetPath = "Assets/_Art/Characters/Zombie/CharacterAnimationSet_Zombie.asset";

        [SetUp]
        public void SetUp()
        {
            DifficultyService.ResetForTests();
            DifficultyProfile.SetActiveForTests(null);
            PursuitPlanner.ResetForTests();
        }

        [TearDown]
        public void TearDown() => DifficultyService.ResetForTests();

        [Test]
        public void Profil_VarlikYuklenir_UcMod_KodVarsayilaniylaAyni_NormalNotr()
        {
            var asset = Resources.Load<DifficultyProfile>(DifficultyProfile.ResourcesPath);
            Assert.IsNotNull(asset, "Resources/" + DifficultyProfile.ResourcesPath + " yok");
            foreach (DifficultyMode m in Enum.GetValues(typeof(DifficultyMode)))
            {
                DifficultyModeSettings s = asset.For(m);
                Assert.IsNotNull(s, m + " satırı yok");
                Assert.AreEqual(JsonUtility.ToJson(DifficultyProfile.Defaults(m)), JsonUtility.ToJson(s), m + ": varlık kod varsayılanından farklı");
            }

            Assert.IsTrue(asset.For(DifficultyMode.Normal).IsNeutral, "Normal nötr değil");
        }

        [Test]
        public void Normal_KopyaYok_AktifKonfigBaytEsit_AiNotr()
        {
            PrototypeConfig shipped = PrototypeConfig.Active;
            Assert.IsNotNull(shipped);
            string before = JsonUtility.ToJson(shipped);
            DifficultyService.Selected = DifficultyMode.Normal;
            DifficultyService.BeginRun();
            Assert.AreSame(shipped, PrototypeConfig.Active, "Normal bir kopya etkinleştirdi");
            Assert.AreEqual(before, JsonUtility.ToJson(PrototypeConfig.Active), "Normal'de yapılandırma değişti");
            Assert.IsNull(DifficultyService.Current);
            Assert.AreEqual(DifficultyMode.Normal, DifficultyService.RunMode);
            Assert.AreEqual(0, DifficultyService.AttackSlotBonus);
            Assert.AreEqual(1f, DifficultyService.QueueSecondsScale);
            for (int t = 0; t < 4; t++) Assert.IsNull(DifficultyService.GaitPoolFor(t), "Normal'de kademe " + t + " yürüyüş havuzu değişti");
            DifficultyService.EndRun();
            Assert.AreSame(shipped, PrototypeConfig.Active);
        }

        [TestCase(DifficultyMode.Hard)]
        [TestCase(DifficultyMode.Demolition)]
        public void ZorDemolition_KopyayaUygulanir_SevkVarligiDokunulmaz(DifficultyMode m)
        {
            PrototypeConfig shipped = PrototypeConfig.Active;
            string before = JsonUtility.ToJson(shipped);
            DifficultyModeSettings s = DifficultyProfile.Active.For(m);
            DifficultyService.Selected = m;
            DifficultyService.BeginRun();
            PrototypeConfig run = PrototypeConfig.Active;
            try
            {
                Assert.AreNotSame(shipped, run, m + ": kopya etkin değil");
                Assert.AreSame(s, DifficultyService.Current);
                Assert.AreEqual(m, DifficultyService.RunMode);
                foreach (EnemyTierSettings a in shipped.Enemies.Tiers)
                {
                    EnemyTierSettings b = run.Enemies.Tiers.Find(x => x.Tier == a.Tier);
                    int k = (int)a.Tier;
                    string tag = m + " " + a.Tier;
                    Assert.AreEqual(s.MoveSpeed[k] > 0f ? s.MoveSpeed[k] : a.MoveSpeed, b.MoveSpeed, 1e-5f, tag + " hız");
                    Assert.AreEqual(s.IntroductionRound[k] > 0 ? s.IntroductionRound[k] : a.IntroductionRound, b.IntroductionRound, tag + " giriş round'u");
                    Assert.AreEqual(a.AttackDamage * s.DamageScale, b.AttackDamage, 1e-4f, tag + " hasar");
                    Assert.AreEqual(a.HealthMultiplier * s.HealthScale, b.HealthMultiplier, 1e-4f, tag + " can");
                    Assert.Less(b.AttackCooldown, a.AttackCooldown, tag + " bekleme");
                    Assert.GreaterOrEqual(b.TelegraphTime, Mathf.Min(a.TelegraphTime, s.MinTelegraphSeconds) - 1e-5f, tag + " telegraf tabanı");
                    Assert.LessOrEqual(b.ArmorChances.MaxChance, 1f, tag + " zırh tavanı");
                }

                Assert.LessOrEqual(run.Rounds.ActiveCap, run.Rounds.AbsoluteSafetyCeiling, m + ": aktif tavan güvenlik tavanını aştı");
                Assert.AreEqual(shipped.Rounds.AbsoluteSafetyCeiling, run.Rounds.AbsoluteSafetyCeiling, m + ": güvenlik tavanı değişti");
                for (int r = 1; r <= 10; r++)
                {
                    Assert.Greater(run.TotalEnemyPool(r), shipped.TotalEnemyPool(r), m + " havuz R" + r);
                    Assert.GreaterOrEqual(run.TargetActiveEnemies(r), shipped.TargetActiveEnemies(r), m + " aktif R" + r);
                    Assert.Greater(run.EnemyHealthMultiplier(r), shipped.EnemyHealthMultiplier(r) - 1e-5f, m + " can R" + r);
                }

                Assert.Greater(run.Spawn.AheadOfPlayerBonus, shipped.Spawn.AheadOfPlayerBonus, m + " oyuncunun önü bonusu");
                Assert.Greater(DifficultyService.AttackSlotBonus, 0, m + " saldırı yuvası");
                Assert.Less(DifficultyService.QueueSecondsScale, 1f, m + " kuyruk");
            }
            finally
            {
                DifficultyService.EndRun();
            }

            Assert.AreSame(shipped, PrototypeConfig.Active, m + ": koşu sonunda sevk varlığı geri gelmedi");
            Assert.AreEqual(before, JsonUtility.ToJson(shipped), m + ": sevk varlığı yazıldı");
        }

        [TestCase(DifficultyMode.Hard)]
        [TestCase(DifficultyMode.Demolition)]
        public void ModHizlari_YuruyusHavuzununKadansBandinda(DifficultyMode m)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ZombieSetPath);
            Assert.IsNotNull(set, ZombieSetPath);
            DifficultyService.Selected = m;
            DifficultyService.BeginRun();
            try
            {
                DifficultyModeSettings s = DifficultyService.Current;
                for (int t = 0; t < 4; t++)
                {
                    float v = s.MoveSpeed[t];
                    if (v <= 0f) continue;
                    int[] pool = DifficultyService.GaitPoolFor(t) ?? CharacterAnimatorContract.ZombieGaitPools[t];
                    // the game's honest rate is speed / (clip speed × body scale) (ZombieSkinnedVisual.SetLocomotion): the corners are the
                    // slowest spawn on the largest body and the fastest spawn on the smallest (gt MUST-1 12.10; the WP-096 stomp test's rule)
                    ZombieAppearance.BodyScaleRange((EnemyTier)t, out float bodyMin, out float bodyMax);
                    float slow = v * (1f - ZombieLocomotionMath.SpeedSpread), fast = v * (1f + ZombieLocomotionMath.SpeedSpread);
                    int carried = 0;
                    foreach (int g in pool)
                    {
                        string clip = CharacterAnimatorContract.ZombieGaitClips[g];
                        if (set.Find(clip) == null) continue;
                        carried++;
                        float c = set.SpeedOf(clip);
                        float low = slow / (c * bodyMax), high = fast / (c * bodyMin);
                        string tag = m + " kademe " + t + " " + v.ToString("F2", CultureInfo.InvariantCulture) + " m/s, " + clip + " "
                                     + c.ToString("F2", CultureInfo.InvariantCulture) + " m/s, gövde " + bodyMin.ToString("F2", CultureInfo.InvariantCulture)
                                     + "-" + bodyMax.ToString("F2", CultureInfo.InvariantCulture) + ": dürüst oran " + low.ToString("F3", CultureInfo.InvariantCulture)
                                     + "-" + high.ToString("F3", CultureInfo.InvariantCulture);
                        Debug.Log("[DD-TEST] wp102.cadence " + tag);
                        Assert.GreaterOrEqual(low, set.MinRateOf(clip) - 1e-3f, tag + ": ayak gövdeyi geçer (kadans tabanı)");
                        Assert.LessOrEqual(high, set.MaxRateOf(clip) + 1e-3f, tag + ": ayak kayar (kadans tavanı)");
                    }

                    Assert.Greater(carried, 0, m + " kademe " + t + ": havuzun hiçbir klibi sette yok");
                }
            }
            finally
            {
                DifficultyService.EndRun();
            }
        }

        [Test]
        public void Takip_Kesme_AcikYan_Matematigi()
        {
            // intercept lead = velocity × min(distance / own speed, cap)
            Assert.AreEqual(7f, PursuitPlanner.InterceptOffset(10f, new Vector3(5f, 0f, 0f), 4f, 1.4f).x, 1e-4f);
            Assert.AreEqual(2.5f, PursuitPlanner.InterceptOffset(2f, new Vector3(5f, 0f, 0f), 4f, 1.4f).x, 1e-4f);
            Assert.AreEqual(Vector3.zero, PursuitPlanner.InterceptOffset(10f, new Vector3(5f, 0f, 0f), 4f, 0f));
            // a cut is taken only when the body gets there the margin before the player
            Assert.IsTrue(PursuitPlanner.CutReachable(2f, 4.3f, 5f, 6.2f, 0.3f));    // 0.47 + 0.3 ≤ 0.81
            Assert.IsFalse(PursuitPlanner.CutReachable(4f, 4.3f, 5f, 6.2f, 0.3f));   // 0.93 + 0.3 > 0.81
            Assert.IsTrue(PursuitPlanner.CutReachable(15f, 4.3f, 5f, 0f, 0.3f));    // standing counts as 1 m/s: 3.49 + 0.3 ≤ 5
            // slices: 0 = +Z, clockwise from above
            Assert.AreEqual(0, PursuitPlanner.SliceOf(Vector3.forward));
            Assert.AreEqual(2, PursuitPlanner.SliceOf(Vector3.right));
            Assert.AreEqual(4, PursuitPlanner.SliceOf(Vector3.back));
            Assert.AreEqual(6, PursuitPlanner.SliceOf(Vector3.left));
            Assert.Less(Vector3.Distance(PursuitPlanner.OpenSide(new[] { 3, 2, 2, 2, 0, 2, 2, 2 }, Vector3.forward), Vector3.back), 1e-3f, "tek açık yan arka");
            Assert.Less(Vector3.Distance(PursuitPlanner.OpenSide(new[] { 0, 1, 1, 1, 0, 1, 1, 1 }, Vector3.forward), Vector3.forward), 1e-3f, "iki açık yan: oyuncunun baktığı");
        }

        /// <summary>C-00 KARAR-WP104 §11.3 / §11.5: R21+ health × 1.15^(R−20) and a late Elite / Ultra share (every mode); the mode
        /// gold scale exact over a run (the fraction carries); Demolition's opening gold.</summary>
        [Test]
        public void GecOyunCanEgrisi_GecElitUltraPayi_ModAltini()
        {
            PrototypeConfig c = PrototypeConfig.Active;
            for (int r = 1; r <= 20; r++)
                Assert.AreEqual(1f + c.Rounds.HealthMultiplierPerRound * (r - 1), c.EnemyHealthMultiplier(r), 1e-4f, "R" + r + " doğrusal kalır");
            Assert.AreEqual((1f + 0.12f * 20f) * 1.15f, c.EnemyHealthMultiplier(21), 1e-3f, "R21 × 1,15");
            Assert.AreEqual((1f + 0.12f * 27f) * Mathf.Pow(1.15f, 8f), c.EnemyHealthMultiplier(28), 1e-2f, "R28 × 1,15^8");
            Assert.IsFalse(float.IsInfinity(c.EnemyHealthMultiplier(int.MaxValue)), "uç round sonlu");
            EnemyTierSettings elite = c.Enemies.Find(EnemyTier.Elite), ultra = c.Enemies.Find(EnemyTier.Ultra), std = c.Enemies.Find(EnemyTier.Standard);
            Assert.AreEqual(elite.SpawnWeightMax, elite.ResolveSpawnWeight(20), 1e-4f, "Elite R20 tavanda");
            Assert.Greater(elite.ResolveSpawnWeight(21), elite.SpawnWeightMax, "Elite R21 geç payı");
            Assert.Greater(ultra.ResolveSpawnWeight(25), ultra.SpawnWeightMax, "Ultra R25 geç payı");
            Assert.AreEqual(std.ResolveSpawnWeight(20), std.ResolveSpawnWeight(30), 1e-4f, "Standard'ın geç payı yok");

            DifficultyService.Selected = DifficultyMode.Normal;
            DifficultyService.BeginRun();
            Assert.AreEqual(7, DifficultyService.ScaleGold(7), "Normal altını değişmez");
            Assert.AreEqual(0, DifficultyService.StartingGold);
            DifficultyService.EndRun();

            DifficultyService.Selected = DifficultyMode.Hard;
            DifficultyService.BeginRun();
            int sum = 0;
            for (int i = 0; i < 8; i++) sum += DifficultyService.ScaleGold(2);
            Assert.AreEqual(20, sum, "Zor ×1,25: 8 × 2 → 20 (kesir taşınır)");
            // a refused ledger claim does not eat the fraction: scaling without the commit leaves the carry where it was
            int a = DifficultyService.ScaleGold(2, out float carryA);
            int b = DifficultyService.ScaleGold(2, out float carryB);
            Assert.AreEqual(a, b, "kesir taahhüt edilmeden ilerledi (reddedilen talep kesri yer)");
            Assert.AreEqual(carryA, carryB, 1e-6f);
            DifficultyService.CommitGoldCarry(carryA);
            Assert.AreEqual(3, DifficultyService.ScaleGold(2), "taahhüt edilen kesir sonraki öldürmeye taşınır");
            Assert.AreEqual(0, DifficultyService.StartingGold);
            DifficultyService.EndRun();

            DifficultyService.Selected = DifficultyMode.Demolition;
            DifficultyService.BeginRun();
            Assert.AreEqual(150, DifficultyService.StartingGold, "Demolition açılış altını");
            sum = 0;
            for (int i = 0; i < 4; i++) sum += DifficultyService.ScaleGold(5);
            Assert.AreEqual(50, sum, "Demolition ×2,5: 4 × 5 → 50");
            DifficultyService.EndRun();
        }

        [Test]
        public void ModAyrisma_AdlarVeKomutSatiri()
        {
            Assert.IsTrue(DifficultyService.TryParse("Demolition", out DifficultyMode d) && d == DifficultyMode.Demolition);
            Assert.IsTrue(DifficultyService.TryParse("zor", out DifficultyMode h) && h == DifficultyMode.Hard);
            Assert.IsTrue(DifficultyService.TryParse(" normal ", out DifficultyMode n) && n == DifficultyMode.Normal);
            Assert.IsFalse(DifficultyService.TryParse("insane", out _));
            // the stable ids (UI keys, command line, log line) round-trip
            Assert.AreEqual("normal", DifficultyService.Id(DifficultyMode.Normal));
            Assert.AreEqual("hard", DifficultyService.Id(DifficultyMode.Hard));
            Assert.AreEqual("demolition", DifficultyService.Id(DifficultyMode.Demolition));
            foreach (DifficultyMode m in Enum.GetValues(typeof(DifficultyMode)))
                Assert.IsTrue(DifficultyService.TryParse(DifficultyService.Id(m), out DifficultyMode back) && back == m, m + " id round-trip");
            Assert.AreEqual(0, (int)DifficultyMode.Normal);
            Assert.AreEqual(1, (int)DifficultyMode.Hard);
            Assert.AreEqual(2, (int)DifficultyMode.Demolition);

            // the launch override wins over the menu: a bot / FINAL probe started with -ddDifficulty plays that mode; the menu's
            // pick raises SelectedChanged once per change (S-08 mode-select sound, S-09 badge), never under the override
            int changes = 0;
            Action<DifficultyMode> onChanged = _ => changes++;
            DifficultyService.SelectedChanged += onChanged;
            try
            {
                DifficultyService.SetLaunchOverrideForTests(DifficultyMode.Demolition);
                DifficultyService.Selected = DifficultyMode.Normal;
                Assert.AreEqual(DifficultyMode.Demolition, DifficultyService.Selected, "menü komut satırını ezdi");
                Assert.IsTrue(DifficultyService.LaunchOverride);
                Assert.AreEqual(0, changes, "override altında seçim olayı yayınlandı");
                DifficultyService.ResetForTests();
                DifficultyService.Selected = DifficultyMode.Hard;
                Assert.AreEqual(DifficultyMode.Hard, DifficultyService.Selected, "override yokken menü seçemedi");
                DifficultyService.Selected = DifficultyMode.Hard;
                Assert.AreEqual(1, changes, "SelectedChanged yalnız değişimde, bir kez");
            }
            finally
            {
                DifficultyService.SelectedChanged -= onChanged;
            }
        }
    }
}
