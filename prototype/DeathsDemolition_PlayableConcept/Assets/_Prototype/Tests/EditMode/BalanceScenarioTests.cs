using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-049 · DEC-085 Ö-1 sevk verisi (CostGrowthPerTier 2,0), simülatör senaryo modu (Ö-2..Ö-5 klon config
    /// üzerinde, sevk varlığı dokunulmaz), Threat Director sütunu ve roster/merdiven/katalog/Results tutarlılığı.
    /// </summary>
    [Category("WP049")]
    public sealed class BalanceScenarioTests
    {
        private static EconomySimulator.Options Secenekler(EconomySimulator.Scenario sc = null, int last = 12)
        {
            Assert.IsNotNull(PrototypeConfig.Active);
            return new EconomySimulator.Options { config = PrototypeConfig.Active, seed = 1u, firstRound = 1, lastRound = last, scenario = sc };
        }

        [Test]
        public void O1_SevkVerisi_CostGrowth2_TumDallar_AcilisBandiKorunur()
        {
            PrototypeConfig c = PrototypeConfig.Active;
            foreach (UpgradeBranch b in (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch)))
            {
                UpgradeBranchSettings s = c.Economy.Find(b);
                Assert.IsNotNull(s, b.ToString());
                Assert.AreEqual(2f, s.CostGrowthPerTier, 1e-5f, "DEC-085 Ö-1: " + b);
                Assert.AreEqual(s.BaseCost, s.CostForTier(0), "açılış fiyatı BaseCost (DEC-070 bandı değişmedi)");
                Assert.AreEqual(Mathf.RoundToInt(s.BaseCost * 2f), s.CostForTier(1));
                Assert.AreEqual(Mathf.RoundToInt(s.BaseCost * 4f), s.CostForTier(2));
            }

            // Sınıf varsayılanları da 2,0 (CreateInstance ile üretilen testler).
            var fresh = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                foreach (UpgradeBranchSettings s in fresh.Economy.Branches) Assert.AreEqual(2f, s.CostGrowthPerTier, 1e-5f, s.Branch.ToString());
            }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }

            // Simülatör kanıtı: harcama/gelir oranı 1,5–1,6 dönemine göre yükselir (WP-041: ~%12).
            EconomySimulator.Report r = EconomySimulator.Run(Secenekler(null, 15));
            Assert.AreEqual(2f, r.costGrowthPerTierPiercing, 1e-5f);
            EconomySimulator.WeaponTrack a = r.weapons[0];
            int income = 0, spent = 0; foreach (EconomySimulator.RoundRow row in a.rounds) { income += row.goldIncome; spent += row.goldSpent; }
            Assert.Greater(spent / (float)income, 0.18f, "Ö-1 ile harcama payı %18'in üstüne çıkar (WP-041 %12): " + spent + "/" + income);
            Debug.Log("[DD-TEST] wp049.o1 income=" + income + " spent=" + spent + " share=" + (spent / (float)income).ToString("0.00"));
        }

        [Test]
        public void Senaryo_KlonUzerinde_SevkVarligiDokunulmaz_O2ZirhDusurur_O4DpsArtar_O3Seviye()
        {
            PrototypeConfig c = PrototypeConfig.Active;
            float growthOnce = c.Enemies.Find(EnemyTier.Standard).ArmorChances.ChanceGrowthPerRound;
            float maxOnce = c.Enemies.Find(EnemyTier.Standard).ArmorChances.MaxChance;
            float dmgOnce = c.Weapons.Find(ClassicWeaponId.AutoSmg).Damage;
            int lvlOnce = c.Weapons.Find(ClassicWeaponId.AutoSmg).UnlockProfileLevel;
            int rowsOnce = c.RoundDifficultyOverrides != null ? c.RoundDifficultyOverrides.Count : 0;

            EconomySimulator.Report shipped = EconomySimulator.Run(Secenekler(null));
            EconomySimulator.Report o2 = EconomySimulator.Run(Secenekler(EconomySimulator.Scenario.Preset("o2")));
            EconomySimulator.Report o34 = EconomySimulator.Run(Secenekler(EconomySimulator.Scenario.Preset("o34")));
            EconomySimulator.Report o5 = EconomySimulator.Run(Secenekler(EconomySimulator.Scenario.Preset("o5")));

            Assert.AreEqual(growthOnce, c.Enemies.Find(EnemyTier.Standard).ArmorChances.ChanceGrowthPerRound, 1e-6f, "sevk varlığı dokunulmadı");
            Assert.AreEqual(maxOnce, c.Enemies.Find(EnemyTier.Standard).ArmorChances.MaxChance, 1e-6f);
            Assert.AreEqual(dmgOnce, c.Weapons.Find(ClassicWeaponId.AutoSmg).Damage, 1e-6f);
            Assert.AreEqual(lvlOnce, c.Weapons.Find(ClassicWeaponId.AutoSmg).UnlockProfileLevel);
            Assert.AreEqual(rowsOnce, c.RoundDifficultyOverrides != null ? c.RoundDifficultyOverrides.Count : 0, "override satırları klonda kaldı");

            Assert.AreEqual("shipped", shipped.scenario); Assert.AreEqual("o2", o2.scenario);
            // Ö-2: R8'de Standard zırhlı payı düşer.
            float sArm = shipped.weapons[0].rounds[7].armouredShare[0], o2Arm = o2.weapons[0].rounds[7].armouredShare[0];
            Assert.Less(o2Arm, sArm, "Ö-2 zırh doygunluğunu düşürür: " + sArm + " → " + o2Arm);
            // Ö-4: B tek-hedef DPS artar (hasar 7 → 8); Ö-3: B/C açılış seviyeleri raporda 3/5.
            EconomySimulator.WeaponTrack bS = shipped.weapons[1], b34 = o34.weapons[1];
            Assert.Greater(b34.rounds[0].sustainedDps, bS.rounds[0].sustainedDps, "Ö-4 B DPS");
            Assert.AreEqual(3, b34.unlockLevel); Assert.AreEqual(5, o34.weapons[2].unlockLevel);
            Assert.AreEqual(lvlOnce, bS.unlockLevel, "sevk raporu sevk seviyesini taşır");
            Assert.AreEqual(3, lvlOnce, "DEC-085 Ö-3 sevk verisi: B seviye 3"); Assert.AreEqual(5, c.Weapons.Find(ClassicWeaponId.CombatShotgun).UnlockProfileLevel, "C seviye 5");
            // Ö-5: R8 override satırı Ultra ağırlığını/zırh tablosunu değiştirir → R8 bileşimi sevkten farklı.
            EconomySimulator.RoundRow s8 = shipped.weapons[0].rounds[7], o58 = o5.weapons[0].rounds[7];
            bool farkli = s8.tierCounts[3] != o58.tierCounts[3] || Mathf.Abs(s8.armouredShare[0] - o58.armouredShare[0]) > 0.01f;
            Assert.IsTrue(farkli, "Ö-5 override satırı R8'i değiştirir");
            Assert.IsTrue(Array.Exists(o5.notes, n => n.Contains("overrideRows=3")));
            Assert.IsFalse(Array.Exists(o5.notes, n => n.Contains("rejected")), "override satırları doğrulamayı geçer");
        }

        [Test]
        public void ThreatSutunu_Araliklar_VeGecKalabalikYuksek()
        {
            EconomySimulator.Report r = EconomySimulator.Run(Secenekler(null, 12));
            EconomySimulator.WeaponTrack a = r.weapons[0];
            foreach (EconomySimulator.RoundRow row in a.rounds)
            {
                Assert.GreaterOrEqual(row.threatIntensity, 0f); Assert.LessOrEqual(row.threatIntensity, 1f);
                Assert.GreaterOrEqual(row.threatLayer, 0); Assert.LessOrEqual(row.threatLayer, 3);
            }

            Assert.GreaterOrEqual(a.rounds[11].threatIntensity, a.rounds[0].threatIntensity, "geç round kalabalığı/tier'ı tehdidi düşürmez");
            EconomySimulator.Report kapali = EconomySimulator.Run(new EconomySimulator.Options { config = PrototypeConfig.Active, lastRound = 3, threatColumn = false });
            Assert.AreEqual(0f, kapali.weapons[0].rounds[0].threatIntensity);
        }

        // ------------------------------------------------------------ WP-042 tutarlılık (roster ↔ merdiven ↔ katalog ↔ Results)

        private static readonly string TestPath = Path.Combine(Application.temporaryCachePath, "dd-editmode-wp049", "prototype-profile.json");

        [Test]
        public void SilahAcilisi_RosterMerdivenKatalogResults_Tutarli()
        {
            List<string> faults = WeaponRoster.Validate();
            Assert.AreEqual(0, faults.Count, string.Join("; ", faults));
            PrototypeConfig config = PrototypeConfig.Active;
            string realHash = HashOf(Path.Combine(Application.persistentDataPath, ProfileStore.FileName));
            Directory.CreateDirectory(Path.GetDirectoryName(TestPath));
            ProfileStore.SetPathOverride(TestPath);
            try
            {
                typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                ProfileService.ResetProfile();
                UnlockCatalog.TestSeam.Override(null);
                foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
                {
                    if (!d.HasRuntimeId || d.UnlockKind != WeaponUnlockKind.ProfileLevel) continue;
                    int level = d.UnlockLevel(config);
                    Assert.AreEqual(level, ProfileService.UnlockLevelFor(d.Id), "merdiven = roster: " + d.Id);
                    UnlockDefinition u = UnlockCatalog.Find(d.Id.ToString());
                    Assert.IsNotNull(u, "katalog girdisi: " + d.Id); Assert.AreEqual(level, u.NumericKey, "katalog = roster: " + d.Id);
                    Assert.IsFalse(ProfileService.IsWeaponUnlocked(d.Id), "taze profilde kapalı: " + d.Id);
                }

                // Tam seviyeye çıkan bir koşu: her roster silahı Results'ta Weapon türünde, roster adıyla, bir kez.
                int hedef = 1; foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled) if (d.HasRuntimeId) hedef = Math.Max(hedef, d.UnlockLevel(config));
                int xp = 0; for (int l = 1; l < hedef; l++) xp += ProfileService.LevelRequirement(l);
                RunRewardReport r = ProfileService.ApplyRun(new RunSummary { CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(), ReachedRound = 2, CompletedRounds = 1, CompletedRoundXp = xp, TotalXpEarned = xp, Weapon = ClassicWeaponId.AssaultRifle, EndReason = RunEndReason.PlayerDeath });
                Assert.IsTrue(r.Applied); Assert.AreEqual(hedef, r.LevelAfter);
                RunResultsData d2 = ProfileService.LastResults;
                foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
                {
                    if (!d.HasRuntimeId || d.UnlockKind != WeaponUnlockKind.ProfileLevel) continue;
                    Assert.IsTrue(ProfileService.IsWeaponUnlocked(d.Id), d.Id.ToString());
                    string ad = ProfileService.WeaponDisplayName(d.Id);
                    int adet = 0; foreach (RunResultsUnlock u in d2.NewUnlocks) if (u.Type == UnlockContentType.Weapon && u.Title == ad) adet++;
                    Assert.AreEqual(1, adet, "Results'ta tam bir Weapon girdisi: " + ad);
                }

                foreach (RunResultsUnlock u in d2.NewUnlocks)
                {
                    if (u.Type == UnlockContentType.Branch) Assert.IsTrue(Enum.IsDefined(typeof(UpgradeBranch), u.Id), "dal girdisi ancak tanımlı dal adıyla: " + u.Id);
                    if (u.Type == UnlockContentType.Weapon) Assert.IsTrue(Enum.IsDefined(typeof(ClassicWeaponId), u.Id), "silah girdisi enum adıyla: " + u.Id);
                }
                // Devre dışı roster silahı (D) hiçbir yerde açılmaz.
                foreach (ClassicWeaponDefinition d in WeaponRoster.All)
                {
                    if (d == null || d.Enabled || !d.HasRuntimeId) continue;
                    Assert.IsNull(UnlockCatalog.Find(d.Id.ToString()), "devre dışı: " + d.RosterKey);
                }
            }
            finally
            {
                typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                typeof(ProfileService).GetField("s_lastResults", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
                ProfileStore.SetPathOverride(null);
                try { Directory.Delete(Path.GetDirectoryName(TestPath), true); } catch (Exception) { }
                Assert.AreEqual(realHash, HashOf(Path.Combine(Application.persistentDataPath, ProfileStore.FileName)), "gerçek profil değişmedi");
            }
        }

        private static string HashOf(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
    }
}
