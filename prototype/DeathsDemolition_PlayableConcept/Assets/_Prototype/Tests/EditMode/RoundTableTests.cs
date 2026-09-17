using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-050 · Ö-2 armour curve + Ö-5 DEC-021 rows (user approval 2026-09-17, DEC-085 by S-04):
    /// (1) the authored source (RoundTableAuthoring) validates and the SHIPPED asset carries exactly
    /// it — rows and the Ö-2 tier values — so a hand edit of the YAML or a stale asset fails here;
    /// (2) the ArmorTiers scope (ADR-010-3 addendum): a Standard/Runner jump never lowers Guard /
    /// Executioner, an empty scope keeps the WP-010 all-tier behaviour; (3) Executioner share:
    /// weight and forced minimum per row; (4) the spawn-mix simulator on the shipped asset shows the
    /// Ö-2 targets (Standard armoured ≈ 50 % R10 / ≈ 65 % R15) and the R8 / R11 / R14 jumps.
    /// </summary>
    [Category("WP050")]
    public sealed class RoundTableTests
    {
        private PrototypeConfig _fresh;

        [SetUp]
        public void SetUp() { _fresh = ScriptableObject.CreateInstance<PrototypeConfig>(); }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_fresh); }

        private static PrototypeConfig Shipped()
        {
            var asset = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(asset, "sevk edilen PrototypeConfig (Resources) yok");
            return asset;
        }

        [Test]
        public void SevkAsset_YazarKaynaklaBirebir_SatirlarVeO2Egrisi()
        {
            PrototypeConfig asset = Shipped();
            List<RoundDifficultyOverride> rows = RoundTableAuthoring.Rows();
            Assert.AreEqual(rows.Count, asset.RoundDifficultyOverrides.Count, "sevk satır sayısı (R8/R11/R14) — RoundTableAuthoring.Apply koşulmamış?");
            for (int i = 0; i < rows.Count; i++)
            {
                RoundDifficultyOverride a = rows[i], b = asset.RoundDifficultyOverrides[i];
                Assert.AreEqual(a.Round, b.Round, "satır " + i + " round");
                Assert.AreEqual(a.OverrideArmorChances, b.OverrideArmorChances);
                Assert.AreEqual(a.ArmorChances.HelmetChance, b.ArmorChances.HelmetChance, 1e-6f, "R" + a.Round + " kask");
                Assert.AreEqual(a.ArmorChances.BodyChance, b.ArmorChances.BodyChance, 1e-6f, "R" + a.Round + " gövde");
                Assert.AreEqual(a.ArmorChances.LegChance, b.ArmorChances.LegChance, 1e-6f, "R" + a.Round + " bacak");
                Assert.AreEqual(0f, b.ArmorChances.ChanceGrowthPerRound, 1e-6f); Assert.AreEqual(0.55f, b.ArmorChances.MaxChance, 1e-6f, "satır tavanı = Ö-2 tavanı");
                Assert.LessOrEqual(Mathf.Max(b.ArmorChances.HelmetChance, Mathf.Max(b.ArmorChances.BodyChance, b.ArmorChances.LegChance)), 0.55f, "R" + a.Round + " satır değeri Ö-2 tavanını aşmaz (S-04)");
                CollectionAssert.AreEqual(a.ArmorTiers, b.ArmorTiers, "R" + a.Round + " zırh kapsamı Standard + Runner");
                Assert.AreEqual(a.OverrideTierWeights, b.OverrideTierWeights);
                Assert.AreEqual(1, b.TierWeights.Count); Assert.AreEqual(EnemyTier.Ultra, b.TierWeights[0].Tier); Assert.AreEqual(a.TierWeights[0].Weight, b.TierWeights[0].Weight, 1e-6f, "R" + a.Round + " Ultra ağırlığı");
                Assert.AreEqual(a.OverrideForcedCounts, b.OverrideForcedCounts);
                Assert.AreEqual(a.ForcedTierCounts.Count, b.ForcedTierCounts.Count);
                if (a.ForcedTierCounts.Count > 0) Assert.AreEqual(a.ForcedTierCounts[0].Count, b.ForcedTierCounts[0].Count, "R" + a.Round + " Ultra zorunlu");
            }
            Assert.IsEmpty(RoundDifficultyOverrideRules.Validate(asset), "sevk tablosu geçerli");

            // Ö-2 on the asset and on the code defaults (parity)
            foreach (PrototypeConfig c in new[] { asset, _fresh })
            {
                string k = c == asset ? "asset" : "kod";
                Assert.AreEqual(0.012f, c.Enemies.Find(EnemyTier.Standard).ArmorChances.ChanceGrowthPerRound, 1e-6f, k + " Standard büyüme");
                Assert.AreEqual(0.55f, c.Enemies.Find(EnemyTier.Standard).ArmorChances.MaxChance, 1e-6f, k + " Standard tavan");
                Assert.AreEqual(0.012f, c.Enemies.Find(EnemyTier.Special).ArmorChances.ChanceGrowthPerRound, 1e-6f, k + " Runner büyüme");
                Assert.AreEqual(0.55f, c.Enemies.Find(EnemyTier.Special).ArmorChances.MaxChance, 1e-6f, k + " Runner tavan");
                Assert.AreEqual(0.015f, c.Enemies.Find(EnemyTier.Elite).ArmorChances.ChanceGrowthPerRound, 1e-6f, k + " Guard büyüme");
                Assert.AreEqual(0.45f, c.Enemies.Find(EnemyTier.Elite).ArmorChances.HelmetChance, 1e-6f, k + " Guard giriş kask korunur");
                Assert.AreEqual(0.9f, c.Enemies.Find(EnemyTier.Elite).ArmorChances.MaxChance, 1e-6f);
                Assert.AreEqual(0.02f, c.Enemies.Find(EnemyTier.Ultra).ArmorChances.ChanceGrowthPerRound, 1e-6f, k + " Executioner değişmez");
                Assert.AreEqual(0.7f, c.Enemies.Find(EnemyTier.Ultra).ArmorChances.BodyChance, 1e-6f);
            }
            Assert.AreEqual(0, _fresh.RoundDifficultyOverrides.Count, "kod varsayılanı boş kalır (formül testleri tabanı)");
        }

        [Test]
        public void ZirhKapsami_StandardRunnerSicramasi_GuardExecutionerDokunulmaz_BosKapsamTumTierlar()
        {
            Assert.IsTrue(_fresh.ReplaceRoundDifficultyOverrides(RoundTableAuthoring.Rows(), out List<string> f), string.Join("\n", f));
            // R8 row: Standard / Runner read the row, Guard / Executioner keep their own DEC-021 odds
            Assert.AreEqual(0.40f, _fresh.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, 8), 1e-6f);
            Assert.AreEqual(0.36f, _fresh.ArmorChance(EnemyTier.Special, ArmorSlot.Body, 8), 1e-6f);
            Assert.AreEqual(_fresh.Enemies.Find(EnemyTier.Elite).ArmorChances.Resolve(ArmorSlot.Helmet, 8), _fresh.ArmorChance(EnemyTier.Elite, ArmorSlot.Helmet, 8), 1e-6f, "Guard kendi eğrisi (0,45 + 0,015·7)");
            Assert.AreEqual(0.555f, _fresh.ArmorChance(EnemyTier.Elite, ArmorSlot.Helmet, 8), 1e-4f);
            Assert.AreEqual(_fresh.Enemies.Find(EnemyTier.Ultra).ArmorChances.Resolve(ArmorSlot.Leg, 8), _fresh.ArmorChance(EnemyTier.Ultra, ArmorSlot.Leg, 8), 1e-6f, "Executioner kendi eğrisi");
            // neighbours are formula: the jump is visible
            float r7 = _fresh.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, 7), r9 = _fresh.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, 9);
            Assert.AreEqual(0.15f + 0.012f * 6, r7, 1e-5f); Assert.AreEqual(0.15f + 0.012f * 8, r9, 1e-5f);
            Assert.Greater(0.40f, r7 + 0.10f, "R8 sıçraması komşularından ≥ 0,10 yukarıda");
            // R11 / R14
            Assert.AreEqual(0.48f, _fresh.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, 11), 1e-6f);
            Assert.AreEqual(0.45f, _fresh.ArmorChance(EnemyTier.Special, ArmorSlot.Leg, 14), 1e-6f);
            // Ö-2 curve outside the rows: R10 ≈ 50 % armoured, R15 ≈ 65 % (any of three plates)
            float any10 = AnyArmoured(_fresh, EnemyTier.Standard, 10), any15 = AnyArmoured(_fresh, EnemyTier.Standard, 15);
            Assert.That(any10, Is.InRange(0.45f, 0.60f), "R10 Standard zırhlı pay hedefi ~%50 (analitik " + any10.ToString("0.000") + ")");
            Assert.That(any15, Is.InRange(0.58f, 0.72f), "R15 Standard zırhlı pay hedefi ~%65 (analitik " + any15.ToString("0.000") + ")");
            Assert.LessOrEqual(_fresh.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, 40), 0.55f, "tavan 0,55");

            // empty scope = every tier (WP-010 behaviour kept)
            var all = new RoundDifficultyOverride { Round = 9, OverrideArmorChances = true, ArmorChances = new ArmorChanceSettings { HelmetChance = 0.9f, BodyChance = 0.9f, LegChance = 0.9f, ChanceGrowthPerRound = 0f, MaxChance = 1f } };
            Assert.IsTrue(all.ArmorAppliesTo(EnemyTier.Ultra) && all.ArmorAppliesTo(EnemyTier.Standard));
            Assert.IsTrue(_fresh.ReplaceRoundDifficultyOverrides(new List<RoundDifficultyOverride> { all }, out f), string.Join("\n", f));
            Assert.AreEqual(0.9f, _fresh.ArmorChance(EnemyTier.Ultra, ArmorSlot.Helmet, 9), 1e-6f, "boş kapsam: Executioner da satırı okur");
            Assert.IsFalse(new RoundDifficultyOverride { Round = 9, OverrideArmorChances = false }.ArmorAppliesTo(EnemyTier.Standard), "kapalı override hiç uygulanmaz");
        }

        private static float AnyArmoured(PrototypeConfig c, EnemyTier tier, int round)
        {
            float h = c.ArmorChance(tier, ArmorSlot.Helmet, round), b = c.ArmorChance(tier, ArmorSlot.Body, round), l = c.ArmorChance(tier, ArmorSlot.Leg, round);
            return 1f - (1f - h) * (1f - b) * (1f - l);
        }

        [Test]
        public void ExecutionerPayi_AgirlikVeZorunlu_SatirlardaFormulUstu()
        {
            Assert.IsTrue(_fresh.ReplaceRoundDifficultyOverrides(RoundTableAuthoring.Rows(), out List<string> f), string.Join("\n", f));
            EnemyTierSettings u = _fresh.Enemies.Find(EnemyTier.Ultra);
            Assert.AreEqual(6f, _fresh.TierSpawnWeight(EnemyTier.Ultra, 8), 1e-6f); Assert.Greater(6f, u.ResolveSpawnWeight(8));
            Assert.AreEqual(9f, _fresh.TierSpawnWeight(EnemyTier.Ultra, 11), 1e-6f); Assert.Greater(9f, u.ResolveSpawnWeight(11));
            Assert.AreEqual(12f, _fresh.TierSpawnWeight(EnemyTier.Ultra, 14), 1e-6f); Assert.GreaterOrEqual(12f, u.ResolveSpawnWeight(14));
            Assert.AreEqual(u.ResolveSpawnWeight(10), _fresh.TierSpawnWeight(EnemyTier.Ultra, 10), 1e-6f, "satırsız round formül");
            Assert.AreEqual(_fresh.Enemies.Find(EnemyTier.Elite).ResolveSpawnWeight(11), _fresh.TierSpawnWeight(EnemyTier.Elite, 11), 1e-6f, "listelenmeyen tier formülde kalır");
            Assert.AreEqual(0, _fresh.ForcedTierCount(EnemyTier.Ultra, 8), "R8 tanıtım garantisi (REQ-071) zaten bir Executioner getirir");
            Assert.AreEqual(2, _fresh.ForcedTierCount(EnemyTier.Ultra, 11));
            Assert.AreEqual(3, _fresh.ForcedTierCount(EnemyTier.Ultra, 14));
            Assert.AreEqual(0, _fresh.ForcedTierCount(EnemyTier.Elite, 14));
        }

        [Test]
        public void SpawnMixSimulatoru_SevkAsset_R1_R15_HedeflerVeSicramalar()
        {
            PrototypeConfig asset = Shipped();
            var o = new SpawnMixSimulator.Options { config = asset, firstRound = 1, lastRound = 15, spawnsPerRound = 4000, seed = 50u, useTierProfiles = true };
            string dir = Path.Combine(Application.dataPath, "..", "Temp", "wp050");
            SpawnMixSimulator.Report r = SpawnMixSimulator.RunAndWrite(o, Path.Combine(dir, "dd-spawn-mix-wp050.json"));
            Assert.IsEmpty(r.violations, string.Join("\n", r.violations));
            Debug.Log("[DD-TEST] wp050.spawnmix R1–R15 (Ö-2 + Ö-5)\n" + SpawnMixSimulator.Summarize(r));

            float Helmet(int round) { SpawnMixSimulator.RoundResult rr = r.rounds[round - 1]; return rr.tierCounts[0] > 0 ? (float)rr.armour[0].equippedByTier[0] / rr.tierCounts[0] : 0f; }
            float UltraShare(int round) { SpawnMixSimulator.RoundResult rr = r.rounds[round - 1]; return rr.spawns > 0 ? (float)rr.tierCounts[3] / rr.spawns : 0f; }
            Assert.AreEqual(0.258f, Helmet(10), 0.035f, "R10 Standard kask payı (Ö-2 eğrisi)");
            Assert.AreEqual(0.318f, Helmet(15), 0.035f, "R15 Standard kask payı");
            Assert.AreEqual(0.40f, Helmet(8), 0.035f, "R8 sıçrama (satır)"); Assert.Greater(Helmet(8), Helmet(7) + 0.08f);
            Assert.AreEqual(0.48f, Helmet(11), 0.035f, "R11 sıçrama"); Assert.Greater(Helmet(11), Helmet(10) + 0.08f);
            Assert.AreEqual(0.55f, Helmet(14), 0.035f, "R14 sıçrama (tavan)"); Assert.Greater(Helmet(14), Helmet(13) + 0.08f);
            Assert.Greater(UltraShare(11), UltraShare(10), "R11 Executioner payı komşusundan yüksek");
            Assert.Greater(UltraShare(14), UltraShare(13), "R14 Executioner payı komşusundan yüksek");
            for (int round = 8; round <= 15; round++)
            {
                SpawnMixSimulator.RoundResult rr = r.rounds[round - 1];
                if (rr.tierCounts[2] > 0) Assert.AreEqual(rr.tierCounts[2], rr.armour[1].equippedByTier[2], "R" + round + " Guard dō garantisi (WP-040) satırlardan etkilenmez");
                if (rr.tierCounts[3] > 0) Assert.AreEqual(rr.tierCounts[3], rr.threeRegionByTier[3], "R" + round + " Executioner üç bölge");
            }
            Debug.Log("[DD-OBS] wp050.armour standardHelmet R7=" + Helmet(7).ToString("0.000") + " R8=" + Helmet(8).ToString("0.000") + " R10=" + Helmet(10).ToString("0.000") + " R11=" + Helmet(11).ToString("0.000") + " R14=" + Helmet(14).ToString("0.000") + " R15=" + Helmet(15).ToString("0.000") +
                      " ultraShare R10=" + UltraShare(10).ToString("0.000") + " R11=" + UltraShare(11).ToString("0.000") + " R13=" + UltraShare(13).ToString("0.000") + " R14=" + UltraShare(14).ToString("0.000"));
        }
    }
}
