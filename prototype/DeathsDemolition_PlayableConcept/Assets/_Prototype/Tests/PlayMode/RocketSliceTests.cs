using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-069 B3 · rl_1 Bohiya on the live scene (DIRECTION §5, DEC-097 / DEC-099): the rocket boosts to 46 m/s and bursts
    /// in the air at 70 m; a direct hit lands its 110 and the blast after it in a SEPARATE transaction (the struck body takes
    /// both — D1: armour counts through the region split); a body beyond 5 m is untouched; inside 4 m it is a dud (30, no
    /// blast); the player takes 45 % of the blast at their distance (source "rl_1"); Cluster drops its bomblets; the controller
    /// fires a Rocket from the equipped rl_1.
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class RocketSliceTests
    {
        private SpecialWeaponSahne _s;
        private readonly List<RocketBlastInfo> _blasts = new List<RocketBlastInfo>(16);

        // C-00 30.09 (S-09 root cause, K/wp-068e/evidence r1e-cipin + r1e-pindiag): TrySetClassicWeapon writes the process-wide
        // WeaponRegistry.SelectedWeapon, and ResetStatics runs once per PlayMode run — the RunTree* tier gate later in the CI
        // order saw rl_1. The selection is saved before each test and given back as the teardown's last step.
        private ClassicWeaponId _seciliOnce;

        [SetUp]
        public void SeciliSilahiSakla() => _seciliOnce = WeaponRegistry.SelectedWeapon;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            RocketImpacts.Detonated -= OnBlast;
            RocketImpacts.PlayerOverride = null;
            _blasts.Clear();
            ProjectileSystem.Clear();
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
            WeaponRegistry.SelectedWeapon = _seciliOnce;
        }

        private IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            _blasts.Clear();
            RocketImpacts.Detonated += OnBlast;
        }

        private void OnBlast(RocketBlastInfo b) => _blasts.Add(b);

        private ZombieEnemy Spawn(EnemyTier tier, Vector3 at)
        {
            Vector3 d = _s.Rig.transform.position - at;
            d.y = 0f;
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, at, Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward, Vector3.up));
            Assert.IsNotNull(z, tier + " doğmadı");
            return z;
        }

        private static Vector3 Torso(ZombieEnemy z)
        {
            Collider c = z.HitboxFor(BodyRegion.UpperBody);
            return c != null ? c.bounds.center : z.transform.position + Vector3.up * 1.2f;
        }

        private Vector3 Chest => _s.Rig.transform.position + Vector3.up * 1.39f;

        /// <summary>One rocket from <paramref name="from"/> at <paramref name="at"/> with rl_1's numbers (no aiming, no controller).</summary>
        private static void Launch(Vector3 from, Vector3 at, int bomblets = 0)
        {
            WeaponDefinition def = Arsenal.Get(ClassicWeaponId.Rl1);
            var shot = new ShotRequest
            {
                Origin = from, Direction = (at - from).normalized, Damage = def.fire.damage, Range = def.projectile.maxRange,
                ImpactForce = def.projectile.impulse, Source = DamageSourceKind.Bullet, Power = WeaponPower.Heavy, PierceDamageFalloff = 1f
            };
            ProjectileLaunch launch = ProjectileLaunch.From(def, ClassicWeaponId.Rl1, def.projectile.maxRange);
            launch.ClusterBomblets = bomblets;
            ProjectileSystem.Spawn(shot, launch);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Ucus_Itki_VeHavadaPatlama_70m()
        {
            yield return Kur();
            int bursts = RocketImpacts.AirBursts;
            Vector3 from = Chest;
            float t0 = Time.time;
            Launch(from, from + new Vector3(0f, 1.2f, 1f) * 10f);   // steeply up and away: nothing to meet
            yield return SpecialWeaponSahne.Bekle(() => RocketImpacts.AirBursts > bursts, 4f, "roket havada patlamadı");
            float seconds = Time.time - t0;
            float metres = Vector3.Distance(from, RocketImpacts.LastBlast.Origin);
            Debug.Log("[DD-TEST] wp069.b3.flight burst after " + seconds.ToString("0.00") + " s at " + metres.ToString("0.0") + " m");
            Assert.AreEqual(RocketBlastKind.AirBurst, RocketImpacts.LastBlast.Kind);
            Assert.AreEqual(70f, metres, 2f, "70 m menzil sonunda patlar");
            Assert.AreEqual(1.61f, seconds, 0.15f, "24 → 46 m/s (0,37 s'de tavan) ile 70 m ≈ 1,6 s");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator DogrudanIsabet_VePatlama_AyriIslemde_HedefIkisiniDeAlir()
        {
            yield return Kur();
            ZombieEnemy target = Spawn(EnemyTier.Ultra, _s.Zemin(0f, 10f));
            ZombieEnemy far = Spawn(EnemyTier.Ultra, _s.Zemin(7f, 10f));
            target.FitArmorComposition(false, false, false);
            yield return new WaitForSeconds(1.5f);

            var targetTx = new HashSet<int>();
            int farHits = 0;
            System.Action<EnemyDamageInfo> onDamaged = i =>
            {
                if (i.Owner == target.transform) targetTx.Add(i.Meta.TransactionId);
                if (i.Owner == far.transform) farHits++;
            };
            DamageEvents.EnemyDamaged += onDamaged;
            int detonations = RocketImpacts.Detonations;
            float hp0 = target.Health;
            try
            {
                Launch(Chest, Torso(target));
                yield return SpecialWeaponSahne.Bekle(() => RocketImpacts.Detonations > detonations, 3f, "roket patlamadı");
                yield return null;
            }
            finally
            {
                DamageEvents.EnemyDamaged -= onDamaged;
            }

            Debug.Log("[DD-TEST] wp069.b3.direct hp " + hp0.ToString("0") + " → " + target.Health.ToString("0") + " tx=" + targetTx.Count + " farHits=" + farHits);
            Assert.AreEqual(RocketBlastKind.Impact, RocketImpacts.LastBlast.Kind);
            Assert.AreEqual(target.GetInstanceID(), RocketImpacts.LastBlast.DirectTargetId, "patlama doğrudan hedefi biliyor");
            Assert.GreaterOrEqual(targetTx.Count, 2, "doğrudan isabet ve patlama ayrı işlemlerde (hedef ikisini de alır)");
            Assert.IsFalse(target.IsAlive, "110 doğrudan + 160 patlama > Executioner R1 (≈ 170)");
            Assert.AreEqual(0, farHits, "5 m dışındaki gövde etkilenmez");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KurmaMesafesiIcinde_Dud_30Hasar_PatlamaYok()
        {
            yield return Kur();
            ZombieEnemy near = Spawn(EnemyTier.Ultra, _s.Zemin(0f, 2.6f));
            near.FitArmorComposition(false, false, false);
            yield return new WaitForSeconds(1.2f);
            int duds = RocketImpacts.Duds, detonations = RocketImpacts.Detonations;
            float hp0 = near.Health;
            var dudSeen = new List<RocketDudInfo>();
            System.Action<RocketDudInfo> onDud = d => dudSeen.Add(d);
            int applications = 0;
            System.Action<EnemyDamageInfo> onDamaged = i => { if (i.Owner == near.transform) applications++; };
            RocketImpacts.Dud += onDud;
            DamageEvents.EnemyDamaged += onDamaged;
            try
            {
                Launch(Chest, Torso(near));
                yield return SpecialWeaponSahne.Bekle(() => RocketImpacts.Duds > duds, 3f, "dud yok");
                yield return null;
            }
            finally
            {
                RocketImpacts.Dud -= onDud;
                DamageEvents.EnemyDamaged -= onDamaged;
            }

            Debug.Log("[DD-TEST] wp069.b3.dud hp " + hp0.ToString("0") + " → " + near.Health.ToString("0") + " applications=" + applications);
            Assert.AreEqual(detonations, RocketImpacts.Detonations, "4 m içinde patlama yok");
            Assert.AreEqual(1, dudSeen.Count);
            Assert.AreEqual(1f, dudSeen[0].FizzleSeconds, 1e-4f, "1 s sonra söner");
            Assert.AreEqual(1, applications, "yalnız doğrudan isabet (patlama yok)");
            // 30 × the struck region's multiplier (the torso sweep may clip the head: ×2).
            Assert.That(hp0 - near.Health, Is.InRange(29f, 61f), "dud 30 (bölge çarpanıyla)");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Oyuncu_KendiPatlamasindan_Yuzde45_Alir()
        {
            yield return Kur();
            var health = _s.Rig.GetComponentInChildren<DD.Prototype.PlayerSystem.PlayerHealth>();
            Assert.IsNotNull(health);
            float taken = 0f;
            System.Action<DD.Prototype.PlayerSystem.PlayerDamageInfo> onDamaged = i => { if (i.SourceId == "rl_1") taken += i.AppliedAmount; };
            int selfHits = RocketImpacts.SelfHits;
            health.DiagnosisInvulnerable = false;
            health.Damaged += onDamaged;
            try
            {
                // The ground 4.5 m ahead: armed (≈ 4.7 m of flight), the blast centre ≈ 4.6 m from the player's centre.
                Vector3 ground = _s.Zemin(0f, 4.5f);
                Launch(Chest, ground);
                yield return SpecialWeaponSahne.Bekle(() => RocketImpacts.SelfHits > selfHits, 3f, "oyuncu patlamadan pay almadı");
                yield return null;
            }
            finally
            {
                health.Damaged -= onDamaged;
                health.DiagnosisInvulnerable = true;
            }

            WeaponExplosiveBlock e = Arsenal.Get(ClassicWeaponId.Rl1).explosive;
            float distance = Vector3.Distance(RocketImpacts.LastBlast.Origin, _s.Rig.transform.position + Vector3.up * RocketImpacts.PlayerCentreHeight);
            float expected = RocketImpacts.SelfDamageAt(distance, e.blastDamage, e.innerRadius, e.outerRadius, e.selfDamageShare, e.selfDamageMax);
            Debug.Log("[DD-TEST] wp069.b3.self distance " + distance.ToString("0.00") + " m expected " + expected.ToString("0.0") + " taken " + taken.ToString("0.0"));
            Assert.Greater(expected, 0f);
            Assert.AreEqual(expected, taken, 0.6f, "%45 × düşüş, zırh yok");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Cluster_Bombaciklari_PatlamaEtrafinda()
        {
            yield return Kur();
            int bomblets = RocketImpacts.Bomblets;
            Launch(Chest, _s.Zemin(0f, 9f), 3);
            yield return SpecialWeaponSahne.Bekle(() => RocketImpacts.Bomblets >= bomblets + 3, 3f, "bombacık yok");
            int n = 0;
            RocketBlastInfo main = default;
            foreach (RocketBlastInfo b in _blasts)
            {
                if (b.Kind == RocketBlastKind.Bomblet) n++;
                else main = b;
            }

            Assert.AreEqual(3, n, "Cluster 2 → 3 bombacık");
            foreach (RocketBlastInfo b in _blasts)
            {
                if (b.Kind != RocketBlastKind.Bomblet) continue;
                Assert.AreEqual(RocketImpacts.BombletDamage, b.Damage, 1e-4f);
                Assert.AreEqual(RocketImpacts.BombletRing, Vector3.Distance(main.Origin, b.Origin), 0.05f, "halka üzerinde");
            }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Kontrolcu_Rl1_RoketAtar()
        {
            yield return Kur();
            Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.Rl1));
            yield return null;
            ProjectileSpawnInfo spawned = default;
            bool seen = false;
            System.Action<ProjectileSpawnInfo> onSpawn = i => { if (i.WeaponId == ClassicWeaponId.Rl1) { spawned = i; seen = true; } };
            ProjectileEvents.Spawned += onSpawn;
            try
            {
                _s.Girdi.AtesBasili = true;
                _s.Girdi.AtesBasti = true;
                yield return SpecialWeaponSahne.Bekle(() => seen, 2f, "rl_1 ateşlemedi");
            }
            finally
            {
                _s.Girdi.AtesBasili = false;
                _s.Girdi.AtesBasti = false;
                ProjectileEvents.Spawned -= onSpawn;
            }

            Assert.AreEqual(ProjectileKind.Rocket, spawned.Kind);
            Assert.AreEqual(24f, spawned.Speed, 1e-3f, "ilk hız 24 m/s");
            Assert.AreEqual(ClassicWeaponId.Rl1, WeaponEvents.Classic.Id);
        }
    }
}
