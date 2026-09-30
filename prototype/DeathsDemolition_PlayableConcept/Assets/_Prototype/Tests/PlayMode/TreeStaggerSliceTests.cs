using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// DEC-112 (user 30.09, S-04 WP-077 Kurogane capstone) · the zombie side on real bodies: ZombieEnemy.ApplyTreeStagger starts
    /// the REQ-124 stagger for the given seconds (Ultra half), adds no damage, is marked as a tree stagger (IsStaggering true,
    /// IsHitStaggering false — stagger-keyed damage bonuses do not see it), and is refused while staggering, inside the REQ-124
    /// lock and inside the body's 1.5 s lock.
    /// </summary>
    [Category("WP076")]
    public sealed class TreeStaggerSliceTests
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
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 offset)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, NavPathFollower.ClampToNavMesh(_rig.transform.position + offset, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AgacSendelemesi_HasarYok_EtiketliVeKilitli_UltraYariSure()
        {
            yield return Hazirla();
            Vector3 fwd = Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up).normalized;

            ZombieEnemy z = Dogur(EnemyTier.Standard, fwd * 8f);
            yield return null; yield return null;
            float hp0 = z.Health;
            int n0 = ZombieEnemy.TreeStaggers;
            Assert.IsTrue(z.ApplyTreeStagger(0.35f, fwd), "sendeleme başlar");
            Assert.AreEqual(n0 + 1, ZombieEnemy.TreeStaggers);
            Assert.IsTrue(z.Animator.IsStaggering, "gövde sendeliyor (hareket/saldırı yolu)");
            Assert.IsFalse(z.Animator.IsHitStaggering, "ağaç sendelemesi: hasar bonusları görmez (DEC-112)");
            Assert.AreEqual(hp0, z.Health, 1e-4f, "hasar eklemez");
            Assert.IsFalse(z.ApplyTreeStagger(0.35f, fwd), "sendelerken yeniden başlamaz");
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(z.Animator.IsStaggering, "0,35 s sonra biter");
            Assert.IsFalse(z.ApplyTreeStagger(0.35f, fwd), "REQ-124 / 1,5 s kilidi içinde reddedilir");
            yield return new WaitForSeconds(1.1f);
            Assert.IsTrue(z.ApplyTreeStagger(0.35f, fwd), "1,5 s sonra yeniden sendeler");
            EnemyFactory.ClearAll(); yield return null;

            ZombieEnemy u = Dogur(EnemyTier.Ultra, fwd * 9f);
            yield return null; yield return null;
            Assert.IsTrue(u.ApplyTreeStagger(0.35f, fwd), "Ultra da sendeler");
            yield return new WaitForSeconds(0.25f);
            Assert.IsFalse(u.Animator.IsStaggering, "Ultra yarı süre (0,175 s)");
            EnemyFactory.ClearAll(); yield return null;
        }
    }
}
