using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>WP-033 · The threat director over one real round: the signal rises with the crowd, pulls back after the round (DEC-065), events fire on layer/flag changes, cost ≤ 0.05 ms, no per-frame allocation.</summary>
    [Category("WP033")]
    public sealed class ThreatSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private RunDirector _run;
        private PlayerRig _rig;

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ThreatDirector.ResetStatics();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator BirRound_SinyalYukselirDuser_OlaylarKatmanda_Maliyet_Ayirma()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi");
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null && RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "sahne kurulmadı");
            _run = RunDirector.Instance; _director = EnemySpawnDirector.Instance; _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ThreatDirector.ResetStatics();
            _director.StopRound(); _director.ClearAllEnemies(); EnemyFactory.ClearAll();
            yield return null;

            int changes = 0, maxLayer = 0; ThreatDirector.Changed += s => { changes++; maxLayer = Mathf.Max(maxLayer, s.Layer); };
            var trace = new List<float>(4096);
            float peak = 0f; int encircledFrames = 0, near = 0;

            // 24 bodies around the player (real crowd, no killing): the signal must climb and flag encirclement
            for (int i = 0; i < 24; i++) { float a = i * Mathf.PI * 2f / 24f; EnemyFactory.Spawn(i % 5 == 0 ? EnemyTier.Elite : EnemyTier.Standard, 6, NavPathFollowerPos(_rig.transform.position + new Vector3(Mathf.Cos(a) * 9f, 0f, Mathf.Sin(a) * 9f)), Quaternion.identity); }
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f)
            {
                ThreatSnapshot s = ThreatDirector.Current;
                trace.Add(s.Intensity); peak = Mathf.Max(peak, s.Intensity); near = Mathf.Max(near, s.NearCount);
                if (s.Encircled) encircledFrames++;
                yield return null;
            }

            Assert.Greater(peak, 0.35f, "kalabalık tehdidi yükseltmeli (tepe " + peak + ")");
            Assert.Greater(near, 4, "yakın sayımı");
            Assert.Greater(encircledFrames, 30, "halka → kuşatılma bayrağı");
            Assert.IsTrue(ThreatDirector.Current.StrongVersionPresent, "Elite sahada → güçlü versiyon bayrağı");
            Assert.Greater(maxLayer, 0, "katman olayı");
            Assert.Greater(changes, 0);
            int changesAtPeak = changes;

            // allocation + cost while the crowd is live
            // WP-051C: the process-wide heap counters (HeapWindow: GC.GetTotalMemory / mono used size) also see other Editor
            // threads' allocations; the test framework's GC.Alloc recorder is filtered to the calling thread only, and a
            // positive control proves it is not blind.
            yield return null;
            byte[] yem = null;
            Assert.That(() => { yem = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
            Assert.That(() => { for (int i = 0; i < 300; i++) ThreatDirector.Sample(); }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(),
                "örnekleme ayırma yapmaz (bu iş parçacığında GC.Alloc örneği görüldü; yem " + (yem != null ? yem.Length : 0) + " B)");
            Assert.LessOrEqual(ThreatDirector.AverageSampleMilliseconds, 0.05, "ortalama örnekleme ≤ 0,05 ms (" + ThreatDirector.AverageSampleMilliseconds.ToString("0.0000") + ")");

            // pull-back: clear the crowd → intensity releases slowly (FallRate) and reaches ≤ 0.15 in a few seconds; between rounds the ceiling clamps at once
            EnemyFactory.ClearAll(); yield return null;
            float t1 = Time.realtimeSinceStartup; float first = ThreatDirector.Current.Intensity;
            yield return Bekle(() => ThreatDirector.Current.Intensity <= 0.15f, 8f, "sinyal düşmedi");
            float release = Time.realtimeSinceStartup - t1;
            Assert.Greater(release, 0.5f, "salıverme anında değil (düşüş yavaş): " + release + " s from " + first);
            _director.StopRound();
            ThreatDirector.ContextOverride = new ThreatContext { PlayerPosition = _rig.transform.position, ViewForward = Vector3.forward, Health01 = 1f, RoundProgress01 = 1f, InFight = false };
            Assert.LessOrEqual(ThreatDirector.Sample().Intensity, 0.15f, "round arası tavan (DEC-065)");
            ThreatDirector.ContextOverride = null;
            Assert.Greater(changes, changesAtPeak, "katman düşüşü olay üretir");
            Debug.Log("[DD-TEST] wp033.slice frames=" + trace.Count + " peak=" + peak.ToString("0.00") + " near=" + near + " encircledFrames=" + encircledFrames + " maxLayer=" + maxLayer + " changes=" + changes + " avgMs=" + ThreatDirector.AverageSampleMilliseconds.ToString("0.0000") + " release=" + release.ToString("0.0") + " s threadAlloc=none");
        }

        private static Vector3 NavPathFollowerPos(Vector3 p) => NavPathFollower.ClampToNavMesh(p, 6f);
    }
}
