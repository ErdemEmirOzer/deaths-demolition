using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-028 (ADR-028-1) · The B/B/B version layer: shipped profile defaults, the armour
    /// tendency on top of the untouched DEC-021 odds, the snapshot / timer scales, the forced
    /// fit, the simulator before / after report and the attack-window parity with the Ultra
    /// placeholder clip.
    /// </summary>
    [Category("WP028")]
    public sealed class TierBehaviourProfileTests
    {
        private PrototypeConfig _config;
        private TierBehaviourProfileSet _defaults;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _defaults = TierBehaviourProfileSet.CreateDefault();
            TierBehaviourProfileSet.SetActive(_defaults);
        }

        [TearDown]
        public void TearDown()
        {
            TierBehaviourProfileSet.ResetStatics();
            if (_defaults != null) Object.DestroyImmediate(_defaults);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void Varsayilanlar_BBB_Sekli()
        {
            TierBehaviourProfile std = _defaults.For(EnemyTier.Standard), sp = _defaults.For(EnemyTier.Special), el = _defaults.For(EnemyTier.Elite), ul = _defaults.For(EnemyTier.Ultra);
            Assert.IsTrue(std.IsIdentity, "Standard: katman yok");
            // Special B: kısa hazırlık / uzun toparlanma + 1 siluet parçası; zırh eğilimi yok; fit yok
            Assert.Less(sp.TelegraphScale, 1f); Assert.Greater(sp.RecoveryScale, 1f); Assert.AreEqual(1f, sp.SpeedScale);
            Assert.IsTrue(sp.HasSilhouette); Assert.IsFalse(sp.ForcesFit); Assert.AreEqual(0f, sp.BodyChanceFloor); Assert.IsFalse(sp.HasAttackPlaceholder);
            // Elite B: gövde zırhı garanti eğilimi + siluet parçası; zamanlayıcı değişmez
            Assert.GreaterOrEqual(el.BodyChanceFloor, 0.85f); Assert.AreEqual(1f, el.TelegraphScale); Assert.AreEqual(1f, el.RecoveryScale); Assert.IsTrue(el.HasSilhouette); Assert.IsFalse(el.ForcesFit);
            // Ultra B: fit C + üç bölge zırh eğilimi + siluet parçası + sweep placeholder
            Assert.AreEqual("C", ul.ForcedFit); Assert.GreaterOrEqual(ul.HelmetChanceFloor, 0.9f); Assert.GreaterOrEqual(ul.BodyChanceFloor, 0.9f); Assert.GreaterOrEqual(ul.LegChanceFloor, 0.9f);
            Assert.IsTrue(ul.HasSilhouette); Assert.AreEqual("AP009_zombie_attack_sweep", ul.AttackClipPlaceholder);
            Assert.AreEqual(1f, ul.TelegraphScale); Assert.AreEqual(1f, ul.RecoveryScale);
            // her tier'ın kendi anchor'ı; tint'ler birbirinden farklı
            Assert.AreNotEqual(sp.SilhouetteAnchor, el.SilhouetteAnchor); Assert.AreNotEqual(el.SilhouetteAnchor, ul.SilhouetteAnchor);
            Assert.AreNotEqual(sp.Tint, el.Tint); Assert.AreNotEqual(el.Tint, ul.Tint);
            // profili olmayan tier → kimlik (aktif set olmadan da)
            var empty = ScriptableObject.CreateInstance<TierBehaviourProfileSet>();
            Assert.IsTrue(empty.For(EnemyTier.Ultra).IsIdentity);
            Object.DestroyImmediate(empty);
        }

        [Test]
        public void DEC021_TabloDegerleri_Degismez_StandardKimlik_EgilimUstKatman()
        {
            var slots = new[] { ArmorSlot.Helmet, ArmorSlot.Body, ArmorSlot.Leg };
            for (int round = 1; round <= 12; round++)
            {
                for (int t = 0; t < 4; t++)
                {
                    var tier = (EnemyTier)t;
                    foreach (ArmorSlot slot in slots)
                    {
                        float before = _config.ArmorChance(tier, slot, round);
                        float applied = EnemyVersionApplier.ArmourChance(_config, _defaults.For(tier), tier, slot, round);
                        float after = _config.ArmorChance(tier, slot, round);
                        Assert.AreEqual(before, after, 0f, "DEC-021 değeri okunur, yazılmaz: " + tier + " " + slot + " R" + round);
                        Assert.GreaterOrEqual(applied, before - 1e-6f, "eğilim tabanın altına inmez: " + tier + " " + slot + " R" + round);
                        Assert.LessOrEqual(applied, 1f);
                        if (tier == EnemyTier.Standard) Assert.AreEqual(before, applied, 1e-6f, "Standard kimlik");
                    }

                    Assert.AreEqual(_config.ArmorChance(tier, ArmorSlot.Body, round), EnemyVersionApplier.ArmourChance(_config, null, tier, ArmorSlot.Body, round), 1e-6f, "profil yok = taban");
                }
            }

            Assert.AreEqual(0, _config.RoundDifficultyOverrides.Count, "override tablosu boş kaldı");
            // Elite gövde ≥ 0.85, Ultra üç bölge ≥ 0.97 her round; Elite miğfer/bacak taban (çarpan 1, floor 0)
            for (int round = 1; round <= 12; round++)
            {
                Assert.GreaterOrEqual(EnemyVersionApplier.ArmourChance(_config, _defaults.For(EnemyTier.Elite), EnemyTier.Elite, ArmorSlot.Body, round), 0.85f, "Elite dō R" + round);
                Assert.AreEqual(_config.ArmorChance(EnemyTier.Elite, ArmorSlot.Helmet, round), EnemyVersionApplier.ArmourChance(_config, _defaults.For(EnemyTier.Elite), EnemyTier.Elite, ArmorSlot.Helmet, round), 1e-6f, "Elite miğfer taban");
                foreach (ArmorSlot slot in slots) Assert.GreaterOrEqual(EnemyVersionApplier.ArmourChance(_config, _defaults.For(EnemyTier.Ultra), EnemyTier.Ultra, slot, round), 0.97f, "Ultra " + slot + " R" + round);
            }

            // sınır: taban 0 + floor 0 → 0; taban 0 + floor → floor; çarpan 1'in üstü clamp
            var p = new TierBehaviourProfile { Tier = EnemyTier.Special, BodyChanceMultiplier = 10f, LegChanceFloor = 0.4f };
            Assert.AreEqual(0f, p.ArmourChance(ArmorSlot.Helmet, 0f));
            Assert.AreEqual(0.4f, p.ArmourChance(ArmorSlot.Leg, 0f), 1e-6f);
            Assert.AreEqual(1f, p.ArmourChance(ArmorSlot.Body, 0.3f), 1e-6f);
            Assert.AreEqual(0.3f, p.ArmourChance(ArmorSlot.Helmet, 0.3f), 1e-6f);
        }

        [Test]
        public void Snapshot_ProfilOlcekleri_YalnizTelegraphRecoverySpeed()
        {
            EnemyStatSnapshot temel = EnemyStatSnapshot.Resolve(_config, EnemyTier.Special, 5);
            EnemyStatSnapshot bos = EnemyStatSnapshot.Resolve(_config, EnemyTier.Special, 5, null);
            Assert.IsTrue(temel.SameProfileAs(bos), "profil yok = WP-010 kaydı");
            EnemyStatSnapshot bbb = EnemyStatSnapshot.Resolve(_config, EnemyTier.Special, 5, _defaults.For(EnemyTier.Special));
            EnemyTierSettings s = _config.Enemies.Find(EnemyTier.Special);
            Assert.AreEqual(s.TelegraphTime * 0.85f, bbb.TelegraphTime, 1e-5f);
            Assert.AreEqual(s.AttackRecoveryTime * 1.25f, bbb.AttackRecoveryTime, 1e-5f);
            Assert.AreEqual(temel.AttackActiveTime, bbb.AttackActiveTime, 0f, "aktif pencere (DEC-029) ölçeklenmez");
            Assert.AreEqual(temel.MaxHealth, bbb.MaxHealth, 0f); Assert.AreEqual(temel.AttackDamage, bbb.AttackDamage, 0f); Assert.AreEqual(temel.MoveSpeed, bbb.MoveSpeed, 0f);
            // Standard / Elite / Ultra: zamanlayıcı ölçekleri 1 → kayıt bit-eşit
            foreach (EnemyTier t in new[] { EnemyTier.Standard, EnemyTier.Elite, EnemyTier.Ultra })
            {
                Assert.IsTrue(EnemyStatSnapshot.Resolve(_config, t, 8).SameProfileAs(EnemyStatSnapshot.Resolve(_config, t, 8, _defaults.For(t))), t + " kayıt değişmez");
            }
        }

        [Test]
        public void AttackState_VersionTiming_TekrarlananCagriBirikmez_AktifDegismez()
        {
            var go = new GameObject("rig");
            try
            {
                var attack = new EnemyAttackState(new ZombieRig { Visual = go.transform });
                attack.ApplyConfig(_config, EnemyTier.Special, 5);
                float t0 = attack.TelegraphSeconds, a0 = attack.ActiveSeconds, r0 = attack.RecoverySeconds;
                attack.ApplyVersionTiming(0.85f, 1.25f);
                Assert.AreEqual(t0 * 0.85f, attack.TelegraphSeconds, 1e-5f); Assert.AreEqual(r0 * 1.25f, attack.RecoverySeconds, 1e-5f); Assert.AreEqual(a0, attack.ActiveSeconds, 0f);
                attack.ApplyVersionTiming(0.85f, 1.25f);   // aynı ölçek yeniden → birikmez
                Assert.AreEqual(t0 * 0.85f, attack.TelegraphSeconds, 1e-5f); Assert.AreEqual(r0 * 1.25f, attack.RecoverySeconds, 1e-5f);
                attack.ApplyConfig(_config, EnemyTier.Special, 5);   // config yeniden okununca taban
                Assert.AreEqual(t0, attack.TelegraphSeconds, 1e-6f); Assert.AreEqual(r0, attack.RecoverySeconds, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Simulator_OnceSonra_EliteDo_UltraUcBolge_FitC()
        {
            var once = new SpawnMixSimulator.Options { config = _config, seed = 28u, spawnsPerRound = 6000, useTierProfiles = false };
            var sonra = new SpawnMixSimulator.Options { config = _config, seed = 28u, spawnsPerRound = 6000, useTierProfiles = true, profiles = _defaults };
            string dir = Path.Combine(Application.dataPath, "..", "Temp", "wp028");
            SpawnMixSimulator.Report a = SpawnMixSimulator.RunAndWrite(once, Path.Combine(dir, "dd-spawn-mix-before.json"));
            SpawnMixSimulator.Report b = SpawnMixSimulator.RunAndWrite(sonra, Path.Combine(dir, "dd-spawn-mix-after.json"));
            Assert.IsFalse(a.tierProfilesApplied); Assert.IsTrue(b.tierProfilesApplied);
            Assert.IsEmpty(b.violations, string.Join("\n", b.violations));
            Debug.Log("[DD-TEST] wp028.spawnmix BEFORE\n" + SpawnMixSimulator.Summarize(a) + "AFTER\n" + SpawnMixSimulator.Summarize(b));
            for (int i = 0; i < b.rounds.Length; i++)
            {
                SpawnMixSimulator.RoundResult ra = a.rounds[i], rb = b.rounds[i];
                // tier dağılımı katmandan etkilenmez (aynı seed, aynı tier çekilişi)
                CollectionAssert.AreEqual(ra.tierCounts, rb.tierCounts, "R" + rb.round + " tier sayıları");
                if (rb.tierCounts[2] >= 60)
                {
                    float eliteBody = (float)rb.armour[1].equippedByTier[2] / rb.tierCounts[2];
                    Assert.GreaterOrEqual(eliteBody, 0.80f, "R" + rb.round + " Elite dō payı (hedef ≥ 0.85, n=" + rb.tierCounts[2] + ")");
                    Assert.GreaterOrEqual(eliteBody, (float)ra.armour[1].equippedByTier[2] / ra.tierCounts[2] - 1e-6f, "R" + rb.round + " Elite dō payı düşmez");
                }

                if (rb.tierCounts[3] >= 60)
                {
                    float ultra3 = (float)rb.threeRegionByTier[3] / rb.tierCounts[3];
                    Assert.GreaterOrEqual(ultra3, 0.85f, "R" + rb.round + " Ultra üç bölge payı (hedef ≥ 0.90 teknik varsayılan, n=" + rb.tierCounts[3] + ")");
                    if (b.lookSimulated) Assert.AreEqual(rb.tierCounts[3], rb.forcedFitByTier[3], "R" + rb.round + " Ultra fit C");
                }

                // Standard / Special zırh sayıları önce = sonra (katman bu tier'larda zırha dokunmaz)
                for (int s = 0; s < 3; s++) { Assert.AreEqual(ra.armour[s].equippedByTier[0], rb.armour[s].equippedByTier[0], "R" + rb.round + " Standard zırh"); Assert.AreEqual(ra.armour[s].equippedByTier[1], rb.armour[s].equippedByTier[1], "R" + rb.round + " Special zırh"); }
            }
        }

        [Test]
        public void SaldiriPenceresi_UltraPlaceholderSweep_FazOranlari_Sapma0_SpecialOranli()
        {
            string fixture = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype", "Tests", "EditMode", "Fixtures", "AP009-attack-clips.json"));
            List<AttackWindowParityCheck.Row> rows = AttackWindowParityCheck.Compare(_config, fixture, _defaults);
            Debug.Log("[DD-TEST] wp028.attackwindow\n" + AttackWindowParityCheck.Table(rows));
            AttackWindowParityCheck.Row ultra = rows[3];
            Assert.IsTrue(ultra.HasClip && ultra.placeholder && ultra.rateScaled, "Ultra sweep placeholder");
            Assert.AreEqual("AP009_zombie_attack_sweep", ultra.clip);
            EnemyTierSettings u = _config.Enemies.Find(EnemyTier.Ultra);
            Assert.AreEqual(0.8f / u.TelegraphTime, ultra.phaseRateTelegraph, 1e-4f);
            Assert.AreEqual(0.2f / u.AttackActiveTime, ultra.phaseRateActive, 1e-4f);
            Assert.AreEqual(0.7f / u.AttackRecoveryTime, ultra.phaseRateRecovery, 1e-4f);
            Assert.AreEqual(0f, ultra.DevTelegraph, 1e-4f); Assert.AreEqual(0f, ultra.DevActive, 1e-4f); Assert.AreEqual(0f, ultra.DevRecovery, 1e-4f);
            Assert.AreEqual(u.TelegraphTime, ultra.clipTelegraph, 1e-4f); Assert.AreEqual(u.AttackActiveTime, ultra.clipActive, 1e-4f); Assert.AreEqual(u.AttackRecoveryTime, ultra.clipRecovery, 1e-4f);
            // Special: zamanlayıcı ölçekli → klip faz oranlarıyla eşlenir (sapma 0); ham klip sapması bilgi
            AttackWindowParityCheck.Row sp = rows[1];
            Assert.IsTrue(sp.rateScaled && !sp.placeholder);
            EnemyTierSettings s = _config.Enemies.Find(EnemyTier.Special);
            Assert.AreEqual(s.TelegraphTime * 0.85f, sp.timerTelegraph, 1e-5f); Assert.AreEqual(s.AttackRecoveryTime * 1.25f, sp.timerRecovery, 1e-5f);
            Assert.AreEqual(0f, sp.DevRecovery, 1e-4f);
            Assert.AreEqual(0.333f, sp.nativeTelegraph, 0.001f);
            // Standard / Elite: ölçek yok → WP-027 ham satırları, eşik altında
            Assert.IsFalse(rows[0].rateScaled); Assert.IsFalse(rows[2].rateScaled);
            foreach (AttackWindowParityCheck.Row r in rows) Assert.IsFalse(r.OverThreshold, r.tier.ToString());
        }
    }
}
