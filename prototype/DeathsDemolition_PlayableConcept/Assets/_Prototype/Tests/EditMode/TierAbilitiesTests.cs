using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-040 · Tier abilities v1, the beat surface and the rise policy — the pure parts:
    /// (1) B·B·B bands resolve from DEC-021 + the profile layer (Special Lunge band, Elite dō
    /// guaranteed + corridor closing, Ultra three regions 1.0 + own clip name), (2) the attack
    /// state's cooldown floor only lengthens, (3) DEC-081 rise truth table, (4) EnemyCombatEvents
    /// row names / counters / handler isolation, (5) the follower's chokepoint hold, (6) the
    /// spawn-mix simulator's R3 / R5 / R8 introduction runs under the completed layer.
    /// </summary>
    [Category("WP040")]
    public sealed class TierAbilitiesTests
    {
        private PrototypeConfig _config;
        private TierBehaviourProfileSet _defaults;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _defaults = TierBehaviourProfileSet.CreateDefault();
            EnemyCombatEvents.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_defaults);
            TierBehaviourProfileSet.ResetStatics();
            EnemyCombatEvents.ResetStatics();
            ZombieEnemy.ResetRisePolicy();
        }

        // ------------------------------------------------------------------ bands

        [Test]
        public void SpecialB_LungeBandi_Menzil_Cooldown_Can()
        {
            EnemyTierSettings s = _config.Enemies.Find(EnemyTier.Special);
            TierBehaviourProfile p = _defaults.For(EnemyTier.Special);
            Assert.AreEqual(2.6f, s.AttackRange, 1e-5f, "DEC-021 Lunge menzili 2.6 (tablo değişmez)");
            Assert.AreEqual(0.85f, s.HealthMultiplier, 1e-5f, "DEC-021 Special HP ×0.85");
            Assert.GreaterOrEqual(p.Cooldown(s.AttackCooldown), 1.9f, "cooldown ≥ 1.9 bandı (profil tabanı)");
            Assert.AreEqual(1.9f, p.CooldownFloor, 1e-5f);
            Assert.AreEqual(2.5f, p.Cooldown(2.5f), 1e-5f, "taban yalnız uzatır");
            Assert.AreEqual(1.9f, p.Cooldown(1.2f), 1e-5f, "tablo altında kalsa taban kazanır");
            Assert.IsFalse(p.HoldsChokepoints);
            Assert.IsFalse(p.HasOwnAttackClip);

            var go = new GameObject("rig");
            try
            {
                var attack = new EnemyAttackState(new ZombieRig { Visual = go.transform });
                attack.ApplyConfig(_config, EnemyTier.Special, 3);
                float c0 = attack.CooldownSeconds;
                attack.ApplyVersionCooldownFloor(p.CooldownFloor);
                Assert.AreEqual(Mathf.Max(c0, 1.9f), attack.CooldownSeconds, 1e-5f, "saldırı makinesi tabanı uygular");
                attack.ApplyVersionCooldownFloor(0.5f);
                Assert.AreEqual(Mathf.Max(c0, 1.9f), attack.CooldownSeconds, 1e-5f, "düşük taban kısaltmaz");
                attack.ApplyConfig(_config, EnemyTier.Special, 3);
                Assert.AreEqual(c0, attack.CooldownSeconds, 1e-5f, "ApplyConfig tabloya döner (WP-028 ölçek sözleşmesiyle aynı)");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void EliteB_DoGaranti_Kutle_Stagger_KoridorKapatma()
        {
            EnemyTierSettings s = _config.Enemies.Find(EnemyTier.Elite);
            TierBehaviourProfile p = _defaults.For(EnemyTier.Elite);
            Assert.AreEqual(2.2f, s.SeparationMass, 1e-5f, "DEC-021 Elite SeparationMass 2.2");
            Assert.AreEqual(0.45f, s.StaggerSusceptibility, 1e-5f, "DEC-021 Elite stagger 0.45");
            Assert.AreEqual(1f, p.BodyChanceFloor, 1e-6f, "gövde zırhı garanti (taban 1.0)");
            Assert.IsTrue(p.HoldsChokepoints, "koridor kapatma");
            for (int round = 5; round <= 12; round++)
            {
                Assert.AreEqual(1f, EnemyVersionApplier.ArmourChance(_config, p, EnemyTier.Elite, ArmorSlot.Body, round), 1e-6f, "R" + round + " dō şansı 1.0");
                Assert.AreEqual(_config.ArmorChance(EnemyTier.Elite, ArmorSlot.Helmet, round), EnemyVersionApplier.ArmourChance(_config, p, EnemyTier.Elite, ArmorSlot.Helmet, round), 1e-6f, "kask DEC-021 değeri (eğilim yok)");
            }
        }

        [Test]
        public void UltraB_UcBolge1_Telegraph_Kutle_Stagger_KendiKlibi()
        {
            EnemyTierSettings s = _config.Enemies.Find(EnemyTier.Ultra);
            TierBehaviourProfile p = _defaults.For(EnemyTier.Ultra);
            Assert.AreEqual(1.15f, s.TelegraphTime, 1e-5f, "DEC-021 Ultra telegraph 1.15");
            Assert.AreEqual(5f, s.SeparationMass, 1e-5f);
            Assert.AreEqual(0.12f, s.StaggerSusceptibility, 1e-5f);
            Assert.AreEqual(1f, p.HelmetChanceFloor, 1e-6f); Assert.AreEqual(1f, p.BodyChanceFloor, 1e-6f); Assert.AreEqual(1f, p.LegChanceFloor, 1e-6f);
            Assert.AreEqual("C", p.ForcedFit);
            Assert.AreEqual("AP009_zombie_attack_overhead", p.AttackClip, "AP-009B overhead = versiyonun kendi klibi");
            Assert.AreEqual("AP009_zombie_attack_sweep", p.AttackClipPlaceholder, "yer tutucu kalır");

            // data switch-over: the clip name resolves by what the animation set carries
            Assert.AreEqual("AP009_zombie_attack_sweep", p.ResolvedAttackClip(name => name == "AP009_zombie_attack_sweep"), "v001 seti → yer tutucu");
            Assert.AreEqual("AP009_zombie_attack_overhead", p.ResolvedAttackClip(name => true), "v002 seti (WP-032) → overhead");
            Assert.AreEqual("AP009_zombie_attack_sweep", p.ResolvedAttackClip(null), "sorgusuz → yer tutucu");
            Assert.AreEqual("", _defaults.For(EnemyTier.Standard).ResolvedAttackClip(name => true), "Standard: stilin varsayılan klibi");
            Assert.IsTrue(_defaults.For(EnemyTier.Standard).IsIdentity, "Standard profil kimlik kalır (yeni alanlar boş)");
        }

        // ------------------------------------------------------------------ rise policy (DEC-081)

        [Test]
        public void RisePolitikasi_Melez_DogrulukTablosu()
        {
            Assert.AreEqual(ZombieEnemy.RisePolicy.OffScreenOnly, ZombieEnemy.Rise, "sevk varsayılanı DEC-081 melez");
            Assert.IsTrue(ZombieEnemy.ShouldRise(ZombieEnemy.RisePolicy.OffScreenOnly, SpawnVisibility.OffScreen));
            Assert.IsFalse(ZombieEnemy.ShouldRise(ZombieEnemy.RisePolicy.OffScreenOnly, SpawnVisibility.OnScreen));
            Assert.IsFalse(ZombieEnemy.ShouldRise(ZombieEnemy.RisePolicy.OffScreenOnly, SpawnVisibility.Unknown), "kamera yok / debug spawn → kalkış yok");
            foreach (SpawnVisibility v in new[] { SpawnVisibility.Unknown, SpawnVisibility.OnScreen, SpawnVisibility.OffScreen })
            {
                Assert.IsTrue(ZombieEnemy.ShouldRise(ZombieEnemy.RisePolicy.Always, v));
                Assert.IsFalse(ZombieEnemy.ShouldRise(ZombieEnemy.RisePolicy.Off, v));
            }

            // the WP-018 switch stays a view on the policy
            ZombieEnemy.RiseOnSpawn = true; Assert.AreEqual(ZombieEnemy.RisePolicy.Always, ZombieEnemy.Rise); Assert.IsTrue(ZombieEnemy.RiseOnSpawn);
            ZombieEnemy.RiseOnSpawn = false; Assert.AreEqual(ZombieEnemy.RisePolicy.Off, ZombieEnemy.Rise); Assert.IsFalse(ZombieEnemy.RiseOnSpawn);
            ZombieEnemy.ResetRisePolicy(); Assert.AreEqual(ZombieEnemy.RisePolicy.OffScreenOnly, ZombieEnemy.Rise); Assert.IsFalse(ZombieEnemy.RiseOnSpawn);
        }

        // ------------------------------------------------------------------ beat surface

        [Test]
        public void OlayYuzeyi_SatirAdlari_Sayaclar_Yalitim()
        {
            Assert.AreEqual(EnemyCombatEvents.KindCount, EnemyCombatEvents.RowRoots.Length);
            Assert.AreEqual(EnemyCombatEvents.KindCount, System.Enum.GetValues(typeof(EnemyCombatEventKind)).Length);
            Assert.AreEqual("armor_break_torso", EnemyCombatEvents.RowName(EnemyCombatEventKind.ArmorBreak, ArmorSlot.Body, EnemyTier.Elite));
            Assert.AreEqual("armor_break_head", EnemyCombatEvents.RowName(EnemyCombatEventKind.ArmorBreak, ArmorSlot.Helmet, EnemyTier.Elite));
            Assert.AreEqual("armor_break_leg", EnemyCombatEvents.RowName(EnemyCombatEventKind.ArmorBreak, ArmorSlot.Leg, EnemyTier.Elite));
            Assert.AreEqual("death_ultra", EnemyCombatEvents.RowName(EnemyCombatEventKind.Death, ArmorSlot.None, EnemyTier.Ultra));
            Assert.AreEqual("death_special", EnemyCombatEvents.RowName(EnemyCombatEventKind.Death, ArmorSlot.None, EnemyTier.Special));
            Assert.AreEqual("telegraph_start", EnemyCombatEvents.RowName(EnemyCombatEventKind.TelegraphStart, ArmorSlot.None, EnemyTier.Standard));
            Assert.AreEqual("spawn_rise", EnemyCombatEvents.RowName(EnemyCombatEventKind.SpawnRise, ArmorSlot.Body, EnemyTier.Ultra), "son ek yalnız kırılma/ölümde");

            int got = 0;
            EnemyCombatEventInfo seen = default;
            System.Action<EnemyCombatEventInfo> bad = _ => throw new System.InvalidOperationException("kötü dinleyici");
            System.Action<EnemyCombatEventInfo> good = i => { got++; seen = i; };
            EnemyCombatEvents.Raised += bad;
            EnemyCombatEvents.Raised += good;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("kötü dinleyici"));
                var info = new EnemyCombatEventInfo(EnemyCombatEventKind.AttackCommit, null, EnemyTier.Elite, 7, new Vector3(1f, 2f, 3f), Vector3.forward, ArmorSlot.None, true, 0.2f);
                EnemyCombatEvents.Raise(in info);
                // Unity's invocation list runs in subscription order and the whole invoke is contained: the throwing
                // FIRST listener stops the chain for this beat (DamageEvents contract, documented) but never the raise site.
                Assert.AreEqual(1, EnemyCombatEvents.Count(EnemyCombatEventKind.AttackCommit), "sayaç yükseltildi");
                Assert.AreEqual(1, EnemyCombatEvents.Total);
                Assert.AreEqual(EnemyCombatEventKind.AttackCommit, EnemyCombatEvents.Last.Kind);
                Assert.AreEqual("attack_commit", EnemyCombatEvents.Last.RowName);
                Assert.AreEqual(7, EnemyCombatEvents.Last.LifeId);
            }
            finally
            {
                EnemyCombatEvents.Raised -= bad;
                EnemyCombatEvents.Raised -= good;
            }

            // the healthy listener alone gets every field by value
            EnemyCombatEvents.Raised += good;
            try
            {
                var info = new EnemyCombatEventInfo(EnemyCombatEventKind.Death, null, EnemyTier.Ultra, 9, Vector3.one, Vector3.back, ArmorSlot.None, false, 0f);
                EnemyCombatEvents.Raise(in info);
                Assert.AreEqual(EnemyTier.Ultra, seen.Tier); Assert.AreEqual(9, seen.LifeId); Assert.AreEqual("death_ultra", seen.RowName); Assert.IsFalse(seen.AnimationDriven);
                Assert.AreEqual(1, EnemyCombatEvents.Count(EnemyCombatEventKind.Death));
            }
            finally { EnemyCombatEvents.Raised -= good; }

            EnemyCombatEvents.ResetStatics();
            Assert.AreEqual(0, EnemyCombatEvents.Total);
        }

        // ------------------------------------------------------------------ chokepoint hold

        [Test]
        public void KoridorKapatan_Kuyruga_Girmez_AlternatifRotaAlmaz()
        {
            var f = new NavPathFollower();
            Assert.IsFalse(f.HoldsChokepoints, "varsayılan: WP-030 davranışı");
            f.HoldsChokepoints = true;
            Assert.IsTrue(f.HoldsChokepoints);
            Assert.IsFalse(f.IsQueued); Assert.AreEqual(1f, f.SpeedFactor, 1e-6f); Assert.IsFalse(f.IsRerouted);
            // the profile → follower binding (ZombieCrowdBinding.ApplyNavProfileForLife) is exercised in PlayMode (TierAbilitiesSliceTests)
        }

        // ------------------------------------------------------------------ introduction runs (R3 / R5 / R8)

        [Test]
        public void TanitimKosulari_R3_R5_R8_TamamlanmisKatman()
        {
            var o = new SpawnMixSimulator.Options { config = _config, firstRound = 3, lastRound = 8, spawnsPerRound = 4000, seed = 40u, useTierProfiles = true, profiles = _defaults };
            string dir = Path.Combine(Application.dataPath, "..", "Temp", "wp040");
            SpawnMixSimulator.Report r = SpawnMixSimulator.RunAndWrite(o, Path.Combine(dir, "dd-spawn-mix-wp040.json"));
            Assert.IsTrue(r.tierProfilesApplied);
            Assert.IsEmpty(r.violations, string.Join("\n", r.violations));
            Debug.Log("[DD-TEST] wp040.spawnmix R3–R8\n" + SpawnMixSimulator.Summarize(r));

            foreach (SpawnMixSimulator.RoundResult rr in r.rounds)
            {
                bool intro = rr.round == 3 || rr.round == 5 || rr.round == 8;
                if (rr.round == 3) Assert.Greater(rr.tierCounts[1], 0, "R3 Special tanıtımı");
                if (rr.round == 5) Assert.Greater(rr.tierCounts[2], 0, "R5 Elite tanıtımı");
                if (rr.round == 8) Assert.Greater(rr.tierCounts[3], 0, "R8 Ultra tanıtımı");
                if (rr.tierCounts[2] > 0) Assert.AreEqual(rr.tierCounts[2], rr.armour[1].equippedByTier[2], "R" + rr.round + " her Elite dō taşır (garanti)");
                if (rr.tierCounts[3] > 0) Assert.AreEqual(rr.tierCounts[3], rr.threeRegionByTier[3], "R" + rr.round + " her Ultra üç bölge (eğilim 1.0)");
                if (intro) Assert.AreEqual(0, rr.repetitionsWithWrongGuaranteeCount, "R" + rr.round + " tanıtım garantisi");
            }
        }
    }
}
