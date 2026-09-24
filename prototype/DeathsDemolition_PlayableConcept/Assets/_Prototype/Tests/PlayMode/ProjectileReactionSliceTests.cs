using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-076 B3 on real bodies (ApplyHit directly; the WP-069 projectile reaches the same ApplyHit): a shotgun shell
    /// staggers and shoves a Standard ≈ 0.6 m over 0.25 s (KnockbackState readable), the Ultra is never moved, a sniper
    /// hit staggers, sustained LMG fire suppresses (×0.70, push ≤ 1.2 m/s; Elite ×0.45).
    /// </summary>
    [Category("WP076")]
    public sealed class ProjectileReactionSliceTests
    {
        private PlayerRig _rig;

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
            StaggerGovernor.Clear();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            ZombieEnemy.ResetRisePolicy();
            StaggerGovernor.Clear();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 offset)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 8, NavPathFollower.ClampToNavMesh(_rig.transform.position + offset, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        private static void Vur(ZombieEnemy z, float impact, DamageSourceKind source, Vector3 dir, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                var hit = new DamageHit { Amount = 0.01f, Region = BodyRegion.UpperBody, Point = z.transform.position + Vector3.up,
                    Direction = dir, ImpactForce = impact, UngovernedImpactForce = impact, Source = source };
                z.ApplyHit(in hit);
            }
        }

        private static float Boyunca(Vector3 from, Vector3 to, Vector3 dir) { Vector3 d = to - from; d.y = 0f; return Vector3.Dot(d, dir); }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Pompali_Itme_UltraSabit_Sniper_LmgBastirma()
        {
            yield return Hazirla();
            Vector3 away = Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up).normalized;   // push away from the player

            // shotgun shell on a Standard (away from the player, so locomotion never helps)
            ZombieEnemy s = Dogur(EnemyTier.Standard, away * 7f);
            ZombieEnemy u = Dogur(EnemyTier.Ultra, away * 7f + Vector3.Cross(Vector3.up, away) * 3f);
            yield return null; yield return null;
            Vector3 s0 = s.transform.position, u0 = u.transform.position;
            Vur(s, 0.85f, DamageSourceKind.Pellet, away, 8);
            Vur(u, 0.85f, DamageSourceKind.Pellet, away, 8);
            Assert.IsTrue(s.Knockback.Active, "itme başladı (S-06 okuma yüzeyi)");
            Assert.IsFalse(u.Knockback.Active, "Ultra itilmez");
            Assert.IsTrue(s.Animator.IsStaggering, "pompalı sendeletir");
            yield return new WaitForSeconds(0.35f);
            float sd = Boyunca(s0, s.transform.position, away), ud = Boyunca(u0, u.transform.position, away);
            Debug.Log("[DD-OBS] wp076.b3 shotgunPush=" + sd.ToString("0.00") + " ultraPush=" + ud.ToString("0.00") + " progress=" + s.Knockback.Progress01.ToString("0.00"));
            Assert.That(sd, Is.InRange(0.40f, 0.80f), "≈ 0,6 m ease-out");
            Assert.Less(ud, 0.15f, "Ultra yerinde (yürüyüş oyuncuya doğru)");
            Assert.IsFalse(s.Knockback.Active); Assert.AreEqual(1f, s.Knockback.Progress01, 1e-4f);
            EnemyFactory.ClearAll(); yield return null;

            // sniper on a Standard → stagger
            ZombieEnemy n = Dogur(EnemyTier.Standard, away * 8f);
            yield return null;
            Vur(n, 3.2f, DamageSourceKind.Bullet, away);
            Assert.IsTrue(n.Animator.IsStaggering, "sniper → her zaman sendeleme");
            Assert.IsFalse(n.Knockback.Active, "3,2 < 4,5 → itme yok");
            EnemyFactory.ClearAll(); yield return null;

            // LMG stream: 0.8 @ 750 rpm for 0.6 s on a Standard and an Elite
            ZombieEnemy l = Dogur(EnemyTier.Standard, away * 9f);
            ZombieEnemy e = Dogur(EnemyTier.Elite, away * 9f + Vector3.Cross(Vector3.up, away) * 3f);
            yield return null;
            float peakL = 0f, peakE = 0f; bool suppressed = false;
            float t0 = Time.time, next = 0f;
            while (Time.time - t0 < 0.6f)
            {
                float t = Time.time - t0;
                while (next <= t) { Vur(l, 0.8f, DamageSourceKind.Bullet, away); Vur(e, 0.8f, DamageSourceKind.Bullet, away); next += 0.08f; }
                suppressed |= l.IsSuppressed;
                peakL = Mathf.Max(peakL, l.SuppressionPushSpeed); peakE = Mathf.Max(peakE, e.SuppressionPushSpeed);
                yield return null;
            }
            Debug.Log("[DD-OBS] wp076.b3 lmgPush std=" + peakL.ToString("0.00") + " elite=" + peakE.ToString("0.00") + " suppressed=" + suppressed + " stagger=" + l.Animator.IsStaggering);
            Assert.IsTrue(suppressed, "≥ 4 isabet → bastırma");
            Assert.AreEqual(HitReactionProfiles.SuppressionSpeedFactor, l.SuppressionFactor, 1e-6f);
            Assert.LessOrEqual(peakL, 1.2f + 1e-3f); Assert.Greater(peakL, 0.5f);
            Assert.LessOrEqual(peakE, 1.2f * 0.45f + 1e-3f, "Elite ×0,45");
            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(l.IsSuppressed, "son vuruştan 0,30 s sonra biter");
        }
    }
}
