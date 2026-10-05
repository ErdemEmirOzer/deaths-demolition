using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Enemies;
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
    /// WP-088 (R2, from the WP-076 triage, C-00 01.10) · the zombie roster. A full PlayMode session showed a "phantom" in
    /// ZombieEnemy.Live (a body destroyed without despawning) poisoning every later suite (spawn null, "BeginRound did not
    /// clear", "no zombie may remain").
    /// (1) The player's scene flow: mid-fight (living, dying and corpse bodies out) Run → MainMenu → Run, Run → Run (restart),
    /// Run → MainMenu. ASSERTED: after each leave no life from the left Run is on the roster, no Unity-null entry stays and the
    /// pool's checked-out count equals the roster (a reloaded Run spawns its own round at once, so "empty" is only true for the
    /// menu); back in a fresh Run the factory fills to its ceiling. REPORTED ([DD-OBS] wp088.roster): per leg live / alive / null /
    /// pool spawned / pool total / ceiling.
    /// (2) A body destroyed behind the pool's back (the A1 mechanism): ASSERTED: it leaves the roster at once, no Unity-null
    /// entry stays, and the factory still fills to its ceiling (the pool gives the dead slot back).
    /// </summary>
    [TestFixture]
    [Category("WP088")]
    public sealed class ZombieRosterSceneCycleTests
    {
        private const float KurulumZamanAsimi = 40f;

        [TearDown]
        public void TearDown()
        {
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator RunMenuRun_OrtaSavasta_HayaletGovdeYok_TavanTam()
        {
            LogAssert.ignoreFailingMessages = true;
            string[] cikis = { PrototypeScenes.MainMenu, PrototypeScenes.Run, PrototypeScenes.MainMenu };
            for (int c = 0; c < cikis.Length; c++)
            {
                yield return RunaGir();
                yield return OrtaSavas();
                Rapor("run" + c + ".before-leave");
                var oncekiHayatlar = new HashSet<int>();
                for (int i = 0; i < ZombieEnemy.Live.Count; i++) if (ZombieEnemy.Live[i] != null) oncekiHayatlar.Add(ZombieEnemy.Live[i].LifeId);

                SceneManager.LoadScene(cikis[c], LoadSceneMode.Single);
                yield return null;
                yield return null;
                Rapor("run" + c + ".after-" + cikis[c]);
                Assert.AreEqual(0, NullGirdi(), "leg " + c + ": Unity-null roster entry (destroyed, never despawned)");
                int kalan = 0;
                for (int i = 0; i < ZombieEnemy.Live.Count; i++) if (ZombieEnemy.Live[i] != null && oncekiHayatlar.Contains(ZombieEnemy.Live[i].LifeId)) kalan++;
                Assert.AreEqual(0, kalan, "leg " + c + ": a body from the left Run is still on the roster");
                Assert.AreEqual(ZombieEnemy.Live.Count, HavuzDisarida(), "leg " + c + ": pool checked-out count differs from the roster");
                if (cikis[c] == PrototypeScenes.MainMenu) Assert.AreEqual(0, ZombieEnemy.Live.Count, "leg " + c + ": roster not empty in the menu");
            }

            // Back in a fresh Run: the full ceiling is still available (a phantom would eat one slot per body).
            yield return RunaGir();
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
            int havuzTavani = PoolService.TryGet(EnemyFactory.TemplatePrefab, out PrototypePool<PooledBehaviour> havuz) ? havuz.Capacity : 0;
            int tavan = Math.Min(EnemyFactory.Capacity, havuzTavani), dogan = 0;
            Vector3 p = PlayerRegistry.Primary.transform.position + new Vector3(0f, 0f, 12f);
            for (int i = 0; i < tavan + 2; i++)
            {
                if (EnemyFactory.Spawn(EnemyTier.Standard, 1, p + new Vector3((i % 8) * 1.2f, 0f, (i / 8) * 1.2f), Quaternion.identity) == null) break;
                dogan++;
            }
            Rapor("final.filled spawned=" + dogan + " factoryCeiling=" + EnemyFactory.Capacity + " poolCapacity=" + havuzTavani);
            EnemyFactory.ClearAll();
            Assert.Greater(tavan, 0, "factory ceiling unset");
            Assert.AreEqual(tavan, dogan, "fresh Run does not fill to the factory ceiling");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HavuzunArkasindanYokEdilenGovde_RosterdanCikar_TavanTam()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunaGir();
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            Vector3 p = PlayerRegistry.Primary.transform.position + new Vector3(0f, 0f, 12f);
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p, Quaternion.identity);
            Assert.IsNotNull(z, "spawn");
            Assert.AreEqual(1, ZombieEnemy.Live.Count);
            UnityEngine.Object.Destroy(z.gameObject);   // behind the pool's back: no Despawn, no OnDespawned
            yield return null;
            Rapor("destroyed");
            Assert.AreEqual(0, NullGirdi(), "a Unity-null entry stayed in the roster");
            Assert.AreEqual(0, ZombieEnemy.Live.Count, "the destroyed body is still on the roster");

            int havuzTavani = PoolService.TryGet(EnemyFactory.TemplatePrefab, out PrototypePool<PooledBehaviour> havuz) ? havuz.Capacity : 0;
            int tavan = Math.Min(EnemyFactory.Capacity, havuzTavani), dogan = 0;
            for (int i = 0; i < tavan + 2; i++)
            {
                if (EnemyFactory.Spawn(EnemyTier.Standard, 1, p + new Vector3((i % 8) * 1.2f, 0f, (i / 8) * 1.2f), Quaternion.identity) == null) break;
                dogan++;
            }
            Rapor("destroyed.filled spawned=" + dogan + " factoryCeiling=" + EnemyFactory.Capacity + " poolCapacity=" + havuzTavani);
            EnemyFactory.ClearAll();
            Assert.Greater(tavan, 0, "factory ceiling unset");
            Assert.AreEqual(tavan, dogan, "the destroyed body still holds a pool slot");
        }

        private static IEnumerator RunaGir()
        {
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                                     && RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Run sahnesi / Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
        }

        /// <summary>The director fills the round; three bodies are killed so dying bodies and corpses are out at the leave.</summary>
        private static IEnumerator OrtaSavas()
        {
            EnemySpawnDirector director = EnemySpawnDirector.Instance;
            director.ForceActiveTarget(12, true);
            yield return Bekle(() => Canli() >= 8, 20f, "the director did not fill the round");
            int olduruldu = 0;
            for (int i = ZombieEnemy.Live.Count - 1; i >= 0 && olduruldu < 3; i--)
            {
                ZombieEnemy z = ZombieEnemy.Live[i];
                if (z != null && z.IsAlive) { z.Kill(); olduruldu++; }
            }
            yield return null;
        }

        private static int Canli()
        {
            int n = 0;
            for (int i = 0; i < ZombieEnemy.Live.Count; i++) if (ZombieEnemy.Live[i] != null && ZombieEnemy.Live[i].IsAlive) n++;
            return n;
        }

        private static int NullGirdi()
        {
            int n = 0;
            for (int i = 0; i < ZombieEnemy.Live.Count; i++) if (ZombieEnemy.Live[i] == null) n++;
            return n;
        }

        private static int HavuzDisarida() =>
            PoolService.TryGet(EnemyFactory.TemplatePrefab, out PrototypePool<PooledBehaviour> pool) ? pool.CountSpawned : 0;

        private static void Rapor(string leg)
        {
            int spawned = -1, total = -1;
            if (PoolService.TryGet(EnemyFactory.TemplatePrefab, out PrototypePool<PooledBehaviour> pool)) { spawned = pool.CountSpawned; total = pool.CountTotal; }
            Debug.Log("[DD-OBS] wp088.roster " + leg + " scene=" + SceneManager.GetActiveScene().name + " live=" + ZombieEnemy.Live.Count
                      + " alive=" + Canli() + " null=" + NullGirdi() + " poolSpawned=" + spawned + " poolTotal=" + total
                      + " ceiling=" + EnemyFactory.Capacity + " atCeiling=" + EnemyFactory.IsAtCeiling);
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t < saniye) yield return null;
            Assert.IsTrue(kosul(), aciklama + " (" + saniye + " s)");
        }
    }
}
