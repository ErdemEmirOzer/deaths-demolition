using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-076 B4 on real bodies (S-02 §7a contract): the shock holds a Standard for the weapon's 1.2 s (no walk, no
    /// swing; the same transaction once; sw_shock beat). S-02 lands the lethal damage at the end of the tremble → the
    /// death pays then and the fall starts at once as Electrocuted; a lethal chain hit mid-tremble pays when it lands
    /// and holds the fall for the tremble's remainder.
    /// </summary>
    [Category("WP076")]
    public sealed class ShockSliceTests
    {
        private PlayerRig _rig;
        private int _olum;
        private void OnDied(EnemyDeathInfo _) => _olum++;

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator Hazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, 30f, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, 30f, "Koşu Fighting fazına girmedi.");
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            DamageEvents.EnemyDied += OnDied;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            DamageEvents.EnemyDied -= OnDied;
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Dogur(Vector3 offset)
        {
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, NavPathFollower.ClampToNavMesh(_rig.transform.position + offset, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        private static DamageHit Oldur(ZombieEnemy z, Vector3 dir) => new DamageHit { Amount = 9999f, Region = BodyRegion.UpperBody,
            Point = z.transform.position + Vector3.up, Direction = dir, ImpactForce = 0.25f, Source = DamageSourceKind.Chain };

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Sok_Tutar_OlumOdulAninda_DusmeSonra()
        {
            yield return Hazirla();
            Vector3 fwd = Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up).normalized;

            // (1) the hold
            ZombieEnemy z = Dogur(fwd * 7f);
            yield return null; yield return null;
            int beats0 = EnemyCombatEvents.Count(EnemyCombatEventKind.SpecialShock);
            Assert.IsTrue(z.ApplyShock(101, 1.2f));
            Assert.IsFalse(z.ApplyShock(101, 1.2f), "aynı işlem bir kez");
            Assert.AreEqual(beats0 + 1, EnemyCombatEvents.Count(EnemyCombatEventKind.SpecialShock), "sw_shock");
            Assert.IsTrue(z.IsElectrocuted); Assert.AreEqual(ZombieBodyState.Electrocuted, z.BodyState);
            Vector3 p0 = z.transform.position;
            yield return new WaitForSeconds(0.9f);
            float moved = Vector3.ProjectOnPlane(z.transform.position - p0, Vector3.up).magnitude;
            Assert.Less(moved, 0.3f, "şokta yürümez");
            Assert.AreEqual(ZombieAttackPhase.None, z.AttackPhase, "şokta saldırmaz");
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(z.IsElectrocuted, "1,2 s sonra biter"); Assert.AreEqual(ZombieBodyState.Standing, z.BodyState);
            EnemyFactory.ClearAll(); yield return null;

            // (2) S-02's order: tremble 1.2 s, then the lethal damage → pays now, falls now, State = Electrocuted
            ZombieEnemy k = Dogur(fwd * 7f);
            yield return null; yield return null;
            k.BreakAllArmor();   // a plate eats a hit and discards the surplus — the lethal hit must reach health
            yield return null;
            _olum = 0;
            k.ApplyShock(202, 1.2f);
            yield return new WaitForSeconds(1.2f);
            k.ApplyHit(Oldur(k, -fwd));
            Assert.AreEqual(1, _olum, "ödül titreme sonunda, hasar indiğinde");
            Assert.IsTrue(k.Animator.IsDying, "titreme bitti → düşüş hemen");
            Assert.AreEqual(ZombieBodyState.Electrocuted, k.Animator.DeathImpulse.State, "sürücü S2 convulse okur");
            EnemyFactory.ClearAll(); yield return null;

            // (3) a lethal chain hit mid-tremble: pays when it lands, the fall waits for the remainder
            ZombieEnemy m = Dogur(fwd * 7f);
            yield return null; yield return null;
            m.BreakAllArmor();   // a plate eats a hit and discards the surplus — the lethal hit must reach health
            yield return null;
            _olum = 0;
            m.ApplyShock(303, 1.2f);
            yield return new WaitForSeconds(0.4f);
            m.ApplyHit(Oldur(m, -fwd));
            Assert.AreEqual(1, _olum);
            Assert.IsFalse(m.Animator.IsDying, "düşüş titreme kalanını bekler");
            Assert.That(m.ShockDeathHoldRemaining, Is.InRange(0.6f, 0.85f));
            yield return new WaitForSeconds(0.9f);
            Debug.Log("[DD-OBS] wp076.b4 hold=" + m.ShockDeathHoldRemaining.ToString("0.00") + " animDying=" + m.Animator.IsDying + " state=" + m.Animator.DeathImpulse.State);
            Assert.AreEqual(0f, m.ShockDeathHoldRemaining);
            Assert.IsTrue(m.Animator.IsDying);
            Assert.AreEqual(ZombieBodyState.Electrocuted, m.Animator.DeathImpulse.State);
        }
    }
}
