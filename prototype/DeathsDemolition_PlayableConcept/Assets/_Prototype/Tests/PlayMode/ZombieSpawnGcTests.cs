using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// B-03 G1 (C-00B GC review, S-03 06.10) · the zombie material path allocates nothing once warm. Real bodies in the Run scene
    /// through the factory's spawn path, every version, the pool warmed (spawn → remove → spawn). On the live bodies the two calls a
    /// spawn makes per version — <see cref="ZombieSkinnedVisual.ApplyMaterial(ZombieModuleLibrary, int, Material)"/> with the library
    /// atlas, then with the WP-036 tint override (ZombieAppearance.ApplyTierTint) — run under the GC.Alloc recorder (positive
    /// control: a 64 KB bait). ASSERTED: 0 GC.Alloc samples on that path, every slot renderer of every body visited.
    /// REPORTED ([DD-OBS] wp084.g1): GC.Alloc samples of one warm EnemyFactory.Spawn and of one re-applied version (look draw +
    /// Assemble + materials + tint) per version, so the rest of the spawn cost is on record.
    /// </summary>
    [TestFixture]
    [Category("WP084")]
    public sealed class ZombieSpawnGcTests
    {
        private const int PerTier = 8;
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        [TearDown]
        public void TearDown()
        {
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Dogus_MalzemeYolu_IsinmisHavuzda0Ayirma()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (!(RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                     && RunDirector.Instance.Phase == RunPhase.Fighting) && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            Assert.IsTrue(RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, "Run sahnesi / Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            CharacterAssetSet set = CharacterAssetSet.Load();
            ZombieModuleLibrary library = set != null ? set.ZombieLibrary : null;
            Assert.IsNotNull(library, "zombie module library");
            ZombieTierLibrary tierLibrary = ZombieTierLibrary.Active;
            bool tinted = tierLibrary != null && tierLibrary.IsReady;
            Assert.IsTrue(tinted, "kaynak yok: tier kütüphanesi hazır değil (tint override yarısı ölçülmezdi)");
            Vector3 origin = PlayerRegistry.Primary.transform.position + new Vector3(0f, 0f, 14f);

            // warm pool: every version's bodies twice through the factory (the second pass reuses pooled bodies)
            var bodies = new List<ZombieEnemy>(PerTier * Tiers.Length);
            var tiers = new List<EnemyTier>(PerTier * Tiers.Length);
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (ZombieEnemy z in bodies) if (z != null && z.IsSpawned) z.Remove();
                bodies.Clear();
                tiers.Clear();
                yield return null;
                for (int t = 0; t < Tiers.Length; t++)
                {
                    for (int i = 0; i < PerTier; i++)
                    {
                        ZombieEnemy z = EnemyFactory.Spawn(Tiers[t], 8, origin + new Vector3((i % 4) * 1.5f - 2f, 0f, t * 2f + (i / 4) * 1f), Quaternion.identity);
                        Assert.IsNotNull(z, Tiers[t] + " spawn " + i);
                        bodies.Add(z);
                        tiers.Add(Tiers[t]);
                    }
                }

                yield return null;
            }

            // reported: one warm spawn per version (the pooled body back through EnemyFactory.Spawn) …
            var spawnAllocs = new int[Tiers.Length];
            for (int t = 0; t < Tiers.Length; t++)
            {
                int k = t * PerTier;
                Vector3 at = bodies[k].transform.position;
                bodies[k].Remove();
                yield return null;
                EnemyTier tier = Tiers[t];
                ZombieEnemy again = null;
                spawnAllocs[t] = Allocs(() => again = EnemyFactory.Spawn(tier, 8, at, Quaternion.identity));
                Assert.IsNotNull(again, tier + " yeniden doğuş");
                bodies[k] = again;
            }

            yield return null;

            // … and one re-applied version on a live body (look draw + Assemble + library materials + tint)
            var versionAllocs = new int[Tiers.Length];
            for (int t = 0; t < Tiers.Length; t++)
            {
                var appearance = bodies[t * PerTier + 1].VersionVisual as ZombieAppearance;
                Assert.IsNotNull(appearance, Tiers[t] + ": ZombieAppearance yok");
                EnemyTier tier = Tiers[t];
                var random = new EnemyRandom(7u);
                versionAllocs[t] = Allocs(() => appearance.ApplyVersion(tier, ref random));
            }

            // asserted: the G1 path — both ApplyMaterial calls of a spawn, every body; the source is in the record first
            int visited = 0;
            foreach (ZombieEnemy z in bodies)
            {
                Assert.IsNotNull(z.Skinned, "skinned visual yok");
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
                        if (z.Skinned.Slot(lod, s) != null) visited++;
            }

            Assert.AreEqual(bodies.Count * ZombieModuleLibrary.LodCount * ZombieSkinnedVisual.SlotCount, visited, "kaynak yok: yuva renderer'ı eksik");
            Action materialPath = () =>
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    int tier = (int)tiers[i];
                    bodies[i].Skinned.ApplyMaterial(library, tier);
                    Material tint = tinted ? tierLibrary.BodyMaterial(tiers[i]) : null;
                    if (tint != null) bodies[i].Skinned.ApplyMaterial(library, tier, tint);
                }
            };
            materialPath();   // warm: the re-applied versions above re-assembled some looks
            byte[] bait = null;
            Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.Greater(Allocs(() => { bait = new byte[64 * 1024]; }), 0, "pozitif kontrol: Allocs yardımcısı ayırmayı görmeli");
            Assert.IsNotNull(bait);
            int materialAllocs = Allocs(materialPath);
            // the measured path really wrote: every body slot ends on its version's tint (the second call), not left early
            int onTint = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                Material tint = tierLibrary.BodyMaterial(tiers[i]);
                if (tint != null && bodies[i].Skinned.Slot(0, ZombieSkinnedVisual.SlotBody).sharedMaterial == tint) onTint++;
            }

            Assert.AreEqual(bodies.Count, onTint, "malzeme yolu gövde yuvasına versiyon tint'ini yazmalı");

            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[DD-OBS] wp084.g1 bodies={0} slots={1} renderers={2} tinted={3} materialPathAllocs={4} spawnAllocs[S/Sp/E/U]={5}/{6}/{7}/{8} versionAllocs[S/Sp/E/U]={9}/{10}/{11}/{12}",
                bodies.Count, ZombieSkinnedVisual.SlotCount, visited, tinted, materialAllocs, spawnAllocs[0], spawnAllocs[1], spawnAllocs[2], spawnAllocs[3],
                versionAllocs[0], versionAllocs[1], versionAllocs[2], versionAllocs[3]));
            Assert.AreEqual(0, materialAllocs, "ısınmış gövdede malzeme yolu GC ayırmamalı (B-03 G1)");

            foreach (ZombieEnemy z in bodies) if (z != null && z.IsSpawned) z.Remove();
            yield return null;
        }

        /// <summary>GC.Alloc samples of one synchronous call on this thread (the AllocatingGCMemory constraint's recorder, as a count).</summary>
        private static int Allocs(Action action)
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }
    }
}
