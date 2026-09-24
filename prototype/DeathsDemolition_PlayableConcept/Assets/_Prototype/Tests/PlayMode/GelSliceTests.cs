using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-076 B5 on real bodies: 8 Standards walk at the player through a gel puddle → they slide (no avoidance), fall
    /// after 0.9 s and S-02's outcome (a test stand-in that kills, as §7b does for the Standard) pays through the normal
    /// death with State Downed; no out-of-world removal. An Ultra survivor lies Downed, gets up and is made immune; a
    /// direct-hit knockdown holds a Standard, then it walks again.
    /// </summary>
    [Category("WP076")]
    public sealed class GelSliceTests
    {
        private PlayerRig _rig;
        private int _olum;
        private System.Action<ZombieEnemy, int> _savedOutcome;   // S-02's permanent ApplySlipOutcome — restored, never cleared
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
            GelPuddleRegistry.ResetStatics();
            ZombieEnemy.ResetGelStatics();
            ZombieEnemy.ResetOutOfWorldStatics();
            DamageEvents.EnemyDied += OnDied;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            DamageEvents.EnemyDied -= OnDied;
            ZombieEnemy.SlipOutcomeHandler = _savedOutcome;
            GelPuddleRegistry.ResetStatics();
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 at)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, NavPathFollower.ClampToNavMesh(at, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        // the §7b outcome for a Standard: dies (S-02 applies it; this stand-in kills through the normal damage path)
        private static void Olduren(ZombieEnemy z, int puddle)
        {
            if (z.Tier == EnemyTier.Ultra) { z.SetGelImmunity(3f); return; }
            z.BreakAllArmor();
            var hit = new DamageHit { Amount = 9999f, Region = BodyRegion.UpperBody, Point = z.transform.position + Vector3.up, Direction = z.transform.forward, ImpactForce = 0.1f, Source = DamageSourceKind.Bullet };
            z.ApplyHit(in hit);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Jel_KayarDuserOlur_Kaldirma0_UltraKalkar_Devrilme()
        {
            yield return Hazirla();
            _savedOutcome = ZombieEnemy.SlipOutcomeHandler;
            ZombieEnemy.SlipOutcomeHandler = Olduren;
            Vector3 p = _rig.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(_rig.transform.forward, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);

            // the puddle on the path, 6 m ahead of the player, on the NavMesh surface
            Vector3 c = NavPathFollower.ClampToNavMesh(p + fwd * 6f, 6f);
            GelPuddleRegistry.Register(c, 4.0f, c.y, 25f, 1);

            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 8; i++) bodies.Add(Dogur(EnemyTier.Standard, p + fwd * (12f + (i / 4) * 2f) + side * (-3f + (i % 4) * 2f)));
            yield return Bekle(() => ZombieEnemy.SlipFalls >= 6, 20f, "8 Standard'ın en az 6'sı kayıp düşmeli (kaçınma yok)");
            yield return new WaitForSeconds(0.2f);
            Debug.Log("[DD-OBS] wp076.b5 slides=" + ZombieEnemy.SlidesStarted + " falls=" + ZombieEnemy.SlipFalls + " deaths=" + _olum + " removals=" + ZombieEnemy.OutOfWorldRemovals);
            Assert.GreaterOrEqual(_olum, 6, "kayma ölümü normal ölüm yolundan (ödül)");
            Assert.AreEqual(0, ZombieEnemy.OutOfWorldRemovals, "kaldırma 0");
            int downedDeaths = 0;
            foreach (ZombieEnemy z in bodies) if (z.IsDying && z.LastDeathImpulse.State == ZombieBodyState.Downed) downedDeaths++;
            Assert.GreaterOrEqual(downedDeaths, 6, "DeathImpulse.State = Downed (S-06 gel_slip)");
            EnemyFactory.ClearAll(); GelPuddleRegistry.ResetStatics(); yield return null;

            // the Executioner survives (§7b: 20 % — here none), lies Downed, gets up immune
            ZombieEnemy u = Dogur(EnemyTier.Ultra, p + fwd * 12f);
            yield return null;
            c = NavPathFollower.ClampToNavMesh(u.transform.position - fwd * 2.5f, 6f);
            GelPuddleRegistry.Register(c, 3.0f, c.y, 25f, 2);
            yield return Bekle(() => u.Gel == GelState.Downed, 15f, "Ultra kayıp yere düşmeli");
            Assert.IsFalse(u.IsDying);
            yield return Bekle(() => u.Gel == GelState.None, 8f, "Ultra kalkmalı");
            Assert.IsTrue(u.IsGelImmune, "kalkınca bağışık (S-02 SetGelImmunity)");
            EnemyFactory.ClearAll(); GelPuddleRegistry.ResetStatics(); yield return null;

            // the direct-hit knockdown: 1.5 s down, then walks again
            ZombieEnemy k = Dogur(EnemyTier.Standard, p + fwd * 9f);
            yield return null; yield return null;
            Assert.IsTrue(k.ApplyKnockdown(7, 1.5f));
            Assert.IsFalse(k.ApplyKnockdown(7, 1.5f), "aynı işlem bir kez");
            Vector3 k0 = k.transform.position;
            yield return new WaitForSeconds(1.2f);
            Assert.AreNotEqual(GelState.None, k.Gel);
            Assert.Less(Vector3.ProjectOnPlane(k.transform.position - k0, Vector3.up).magnitude, 0.3f, "devrilmişken yürümez");
            Assert.AreEqual(ZombieBodyState.Downed, k.BodyState);
            Assert.AreEqual(DD.Prototype.Characters.ZombieSpecialVisual.Slipping, k.SpecialVisual, "S-06 Slipping görseli (düşüş/yerde)");
            yield return Bekle(() => k.Gel == GelState.None, 3f, "kalkış 1,5 + 0,9 s sonra biter");
        }
    }
}
