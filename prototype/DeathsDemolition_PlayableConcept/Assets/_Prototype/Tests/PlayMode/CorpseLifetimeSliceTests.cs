using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
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
    /// WP-076 B2 on real bodies in the Run scene: (1) the killing hit becomes the DeathImpulse the driver reads
    /// (sniper-force bullet → Sniper, head; debug Kill → unknown), (2) the corpse ceiling buries the oldest corpse
    /// and leaves the kill count alone, (3) a corpse that falls off the world is a silent despawn (no out-of-world
    /// removal), (4) a living body removed by the WP-061K net never dies: no EnemyDied, no impulse, body state reported.
    /// </summary>
    [Category("WP076")]
    public sealed class CorpseLifetimeSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private PlayerRig _rig;
        private int _olum;
        private ZombieEnemy.OutOfWorldReason _sebep = (ZombieEnemy.OutOfWorldReason)(-1);
        private ZombieBodyState _durum = (ZombieBodyState)(-1);

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private void OnDied(EnemyDeathInfo _) => _olum++;
        private void OnRemoved(EnemyTier t, Vector3 p, ZombieEnemy.OutOfWorldReason r, ZombieBodyState s) { _sebep = r; _durum = s; }

        private IEnumerator Hazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, KurulumZamanAsimi, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            ZombieEnemy.ResetCorpseStatics();
            ZombieEnemy.ResetOutOfWorldStatics();
            DamageEvents.EnemyDied += OnDied;
            ZombieEnemy.OutOfWorldRemoved += OnRemoved;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            DamageEvents.EnemyDied -= OnDied;
            ZombieEnemy.OutOfWorldRemoved -= OnRemoved;
            ZombieEnemy.ResetCorpseStatics();
            ZombieEnemy.ResetOutOfWorldStatics();
            ZombieEnemy.ResetRisePolicy();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 offset)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 8, NavPathFollower.ClampToNavMesh(_rig.transform.position + offset, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        private static void Tasi(ZombieEnemy z, Vector3 p)
        {
            CharacterController cc = z.GetComponent<CharacterController>();
            bool on = cc != null && cc.enabled;
            if (on) cc.enabled = false;
            z.transform.position = p;
            if (on) cc.enabled = true;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator OlumDarbesi_CesetTavani_CesetDusmesi_KaldirmaOdulsuz()
        {
            yield return Hazirla();

            // (1) the killing hit → DeathImpulse (sniper-force bullet to the head, pushed along +x)
            ZombieEnemy a = Dogur(EnemyTier.Standard, new Vector3(0f, 0f, 8f));
            yield return null;
            var hit = new DamageHit { Amount = 9999f, Region = BodyRegion.Head, Point = a.transform.position + Vector3.up * 1.7f,
                Direction = new Vector3(2f, 0f, 0f), ImpactForce = 3.2f, Source = DamageSourceKind.Bullet };
            a.ApplyHit(in hit);
            Assert.IsTrue(a.IsDying, "öldü");
            DeathImpulse d = a.LastDeathImpulse;
            Assert.IsTrue(d.WeaponKnown); Assert.AreEqual(WeaponClass.Sniper, d.WeaponClass);
            Assert.AreEqual(BodyRegion.Head, d.Region); Assert.AreEqual(1f, d.Direction.x, 1e-4f, "birim yön");
            Assert.AreEqual(3.2f, d.Force, 1e-4f); Assert.AreEqual(DamageSourceKind.Bullet, d.Source);
            Assert.AreEqual(d.Direction, a.Animator.DeathImpulse.Direction, "sürücü aynı darbeyi okur");
            Assert.AreEqual(1, ZombieEnemy.CorpseCount);
            ZombieEnemy b = Dogur(EnemyTier.Standard, new Vector3(2f, 0f, 8f));
            yield return null;
            b.Kill();
            Assert.IsFalse(b.LastDeathImpulse.WeaponKnown, "debug Kill → bilinmez");
            EnemyFactory.ClearAll(); yield return null;
            Assert.AreEqual(0, ZombieEnemy.CorpseCount, "havuz temizliği listeyi boşaltır");

            // (2) corpse ceiling 3: five kills → three corpses, two buried early, five deaths counted
            ZombieEnemy.CorpseCeilingOverride = 3;
            _olum = 0;
            var bodies = new ZombieEnemy[5];
            for (int i = 0; i < 5; i++) bodies[i] = Dogur(EnemyTier.Standard, new Vector3(-4f + i * 2f, 0f, 10f));
            yield return null;
            for (int i = 0; i < 5; i++) bodies[i].Kill();
            yield return null;
            Debug.Log("[DD-OBS] wp076.b2 corpses=" + ZombieEnemy.CorpseCount + " evicted=" + ZombieEnemy.CorpsesEvicted + " deaths=" + _olum + " linger=" + bodies[4].LastCorpseLinger.ToString("0.00"));
            Assert.AreEqual(3, ZombieEnemy.CorpseCount, "tavan");
            Assert.AreEqual(2, ZombieEnemy.CorpsesEvicted, "en eski ikisi gömüldü");
            Assert.IsFalse(bodies[0].IsDying || bodies[1].IsDying, "gömülenler en eskiler");
            Assert.IsTrue(bodies[4].IsDying, "en yeni ceset kalır");
            Assert.AreEqual(5, _olum, "tavan ölüm sayısına dokunmaz");
            ZombieEnemy.CorpseCeilingOverride = 0;
            EnemyFactory.ClearAll(); yield return null;

            // (3) a corpse falling off the world → silent despawn (no out-of-world removal)
            ZombieEnemy c = Dogur(EnemyTier.Standard, new Vector3(0f, 0f, 9f));
            yield return null;
            c.Kill();
            Tasi(c, new Vector3(c.transform.position.x, -60f, c.transform.position.z));
            yield return null; yield return null;
            Assert.IsFalse(c.IsDying, "ceset despawn");
            Assert.AreEqual(1, ZombieEnemy.CorpsesFellOffWorld);
            Assert.AreEqual(0, ZombieEnemy.OutOfWorldRemovals, "ceset düşmesi kaldırma sayılmaz");

            // (4) a living body off the world → WP-061K removal: no death, no impulse, state reported
            _olum = 0;
            ZombieEnemy e = Dogur(EnemyTier.Standard, new Vector3(0f, 0f, 12f));
            yield return null; yield return null;
            Tasi(e, new Vector3(e.transform.position.x, -60f, e.transform.position.z));
            yield return Bekle(() => ZombieEnemy.OutOfWorldRemovals == 1, 3f, "canlı beden kaldırılmadı");
            Assert.AreEqual(0, _olum, "kaldırma ödülsüz, ölüm yok");
            Assert.AreEqual(ZombieEnemy.OutOfWorldReason.Fell, _sebep);
            Assert.AreEqual(ZombieBodyState.Standing, _durum, "olay gövde durumunu taşır");
            Assert.IsFalse(e.IsDying);
            Assert.AreEqual(0, ZombieEnemy.CorpseCount);
        }
    }
}
