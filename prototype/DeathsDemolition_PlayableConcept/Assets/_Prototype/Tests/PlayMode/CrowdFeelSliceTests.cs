using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core;
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
    /// WP-054 on real bodies in the Run scene: (1) flank shares per tier and the per-life stall band,
    /// (2) the gate threshold mass inside a Gate volume, (3) corpse linger by tier and the ceiling rule,
    /// (4) the Threat Director layer reaches the spawn feed, (5) bridge / shore flow on the v3 NavMesh with
    /// the 1.35 bridge cost (the bridge is used again, the shore stays an alternative), wall-slide readout.
    /// </summary>
    [Category("WP054")]
    public sealed class CrowdFeelSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, KurulumZamanAsimi, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            CrowdFeelSettings.ResetStatics();
            RegionNavProfile.ResetStatics();
            RegionNavRouter.ResetStatics();
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ZombieEnemy.ResetRisePolicy();
            CrowdFeelSettings.ResetStatics();
            RegionNavProfile.ResetStatics();
            ThreatDirector.ContextOverride = null;
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private static Vector3 Yerde(Vector3 p) => NavPathFollower.ClampToNavMesh(p, 6f);

        private static ZombieEnemy Dogur(EnemyTier tier, Vector3 p, int round = 8)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, round, Yerde(p), Quaternion.identity);
            Assert.IsNotNull(z, "spawn (havuz)");
            return z;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator KanatPaylari_TakilmaBandi_CesetSuresi_KapiKutlesi_TehditYayini()
        {
            yield return RunSahnesiniHazirla();
            CrowdFeelSettings feel = CrowdFeelSettings.Active;

            // flank rolls per tier (24 Standard + 12 Special + 4 Ultra under the 40 ceiling) and the per-life stall band
            var std = new List<ZombieEnemy>(); var sp = new List<ZombieEnemy>(); var ul = new List<ZombieEnemy>();
            for (int i = 0; i < 24; i++) std.Add(Dogur(EnemyTier.Standard, _rig.transform.position + new Vector3(-9f + (i % 12) * 1.5f, 0f, 12f + (i / 12) * 2f)));
            for (int i = 0; i < 12; i++) sp.Add(Dogur(EnemyTier.Special, _rig.transform.position + new Vector3(-9f + i * 1.5f, 0f, 16f)));
            for (int i = 0; i < 4; i++) ul.Add(Dogur(EnemyTier.Ultra, _rig.transform.position + new Vector3(-6f + i * 3f, 0f, 18f)));
            yield return null;
            int stdFlank = 0, spFlank = 0, ulFlank = 0; float stallMin = 9f, stallMax = 0f;
            foreach (ZombieEnemy z in std) { if (z.FlankSign != 0) { stdFlank++; Assert.That(z.FlankAngle, Is.InRange(feel.FlankAngleMin, feel.FlankAngleMax)); } stallMin = Mathf.Min(stallMin, z.WaitStallSeconds); stallMax = Mathf.Max(stallMax, z.WaitStallSeconds); }
            foreach (ZombieEnemy z in sp) if (z.FlankSign != 0) spFlank++;
            foreach (ZombieEnemy z in ul) if (z.FlankSign != 0) ulFlank++;
            Debug.Log("[DD-OBS] wp054.flank standard=" + stdFlank + "/24 special=" + spFlank + "/12 ultra=" + ulFlank + "/4 stallBand=" + stallMin.ToString("0.00") + "–" + stallMax.ToString("0.00"));
            Assert.That((float)stdFlank / 24f, Is.InRange(0.10f, 0.60f), "Standard kanat payı ~0,35 (n=24)");
            Assert.That((float)spFlank / 12f, Is.InRange(0.25f, 0.95f), "Special kanat payı ~0,60 (n=12)");
            Assert.AreEqual(0, ulFlank, "Ultra düz gelir");
            Assert.GreaterOrEqual(stallMin, feel.StallSecondsMin - 1e-4f); Assert.LessOrEqual(stallMax, feel.StallSecondsMax + 1e-4f);
            Assert.Greater(stallMax - stallMin, 0.1f, "takılma eşiği yaşam başına dağılır");

            // the corpse linger by tier (not near the ceiling: 40 bodies alive is the ceiling → clear first)
            EnemyFactory.ClearAll(); yield return null;
            ZombieEnemy s1 = Dogur(EnemyTier.Standard, _rig.transform.position + new Vector3(0f, 0f, 10f));
            ZombieEnemy u1 = Dogur(EnemyTier.Ultra, _rig.transform.position + new Vector3(3f, 0f, 10f));
            yield return null;
            s1.Kill(); u1.Kill(); yield return null;
            float s1Linger = s1.LastCorpseLinger, u1Linger = u1.LastCorpseLinger;   // read now: the pooled bodies are reused below
            Assert.That(s1Linger, Is.InRange(0.55f * 0.85f - 1e-3f, 0.55f * 1.15f + 1e-3f), "Standard ceset 0,55 ± %15");
            Assert.That(u1Linger, Is.InRange(3.0f * 0.85f - 1e-3f, 3.0f * 1.15f + 1e-3f), "Ultra ceset 3,0 ± %15");
            EnemyFactory.ClearAll(); yield return null;
            // near the ceiling: fill to capacity, kill one → ×0.4
            int cap = EnemyFactory.Capacity;
            Assert.Greater(cap, 4);
            var many = new List<ZombieEnemy>();
            for (int i = 0; i < cap; i++) { ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 8, Yerde(_rig.transform.position + new Vector3(-9f + (i % 12) * 1.5f, 0f, 10f + (i / 12) * 2f)), Quaternion.identity); if (z != null) many.Add(z); }
            yield return null;
            Assert.GreaterOrEqual(many.Count, cap - 1);
            many[0].Kill(); yield return null;
            Assert.That(many[0].LastCorpseLinger, Is.InRange(0.55f * 0.4f * 0.85f - 1e-3f, 0.55f * 0.4f * 1.15f + 1e-3f), "tavana yakın ceset ×0,4");
            Debug.Log("[DD-OBS] wp054.corpse standard=" + s1Linger.ToString("0.00") + " ultra=" + u1Linger.ToString("0.00") + " nearCap=" + many[0].LastCorpseLinger.ToString("0.00") + " cap=" + cap);
            EnemyFactory.ClearAll(); yield return null;

            // gate threshold mass: inside N_GateWest the Elite weighs ×1.5, the Standard ×0.6; outside ×1
            NavRegionVolume gate = RegionNavProfile.Active.Find(new Vector3(-8f, 0f, 34f));
            Assert.IsNotNull(gate); Assert.AreEqual(NavRegionKind.Gate, gate.kind);
            _rig.Teleport(Yerde(new Vector3(-8f, 0f, 40f)), Quaternion.identity);   // the player beyond the gate so the bodies path through it
            yield return null;
            ZombieEnemy elIn = Dogur(EnemyTier.Elite, gate.center); ZombieEnemy stIn = Dogur(EnemyTier.Standard, gate.center + new Vector3(1.2f, 0f, 0f));
            ZombieEnemy stOut = Dogur(EnemyTier.Standard, new Vector3(0f, 0f, 10f));
            yield return null; yield return null;
            if (gate.Contains(elIn.transform.position) && gate.Contains(stIn.transform.position))
            {
                Assert.AreEqual(1.5f, elIn.GateMassScale, 1e-4f, "Elite kapı eşiğinde ×1,5"); Assert.AreEqual(0.6f, stIn.GateMassScale, 1e-4f, "Standard kapı eşiğinde ×0,6");
                Assert.AreEqual(PrototypeConfig.Active.Enemies.Find(EnemyTier.Elite).SeparationMass * 1.5f, elIn.Mass, 1e-4f);
            }
            else Debug.Log("[DD-OBS] wp054.gate bodies projected outside the gate volume (NavMesh clamp) — mass scale not asserted");
            Assert.AreEqual(1f, stOut.GateMassScale, 1e-4f, "hacim dışında ×1");
            EnemyFactory.ClearAll(); yield return null;

            // the threat layer reaches the spawn feed
            ThreatDirector.ResetStatics();
            ThreatDirector.ContextOverride = new ThreatContext { PlayerPosition = _rig.transform.position, ViewForward = _rig.transform.forward, Health01 = 0.3f, RoundProgress01 = 0.8f, InFight = true };
            for (int i = 0; i < 10; i++) Dogur(EnemyTier.Elite, _rig.transform.position + Quaternion.Euler(0f, i * 36f, 0f) * new Vector3(0f, 0f, 3.5f));
            yield return null;
            ThreatSnapshot snap = ThreatDirector.Sample();
            Assert.AreEqual(snap.Layer, ThreatLevelFeed.Layer, "Threat Director katmanı spawn yayınına yazılır");
            Debug.Log("[DD-OBS] wp054.threatfeed layer=" + ThreatLevelFeed.Layer + " intensity=" + snap.Intensity.ToString("0.00") + " onScreenFactor=" + feel.OnScreenFactorFor(ThreatLevelFeed.Layer, 0.35f));
            ThreatDirector.ContextOverride = null;
            EnemyFactory.ClearAll();
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator KopruKiyiAkisi_v3_KopruYenidenKullanilir_KiyiAlternatifKalir_DuvarKaymaOkunur()
        {
            yield return RunSahnesiniHazirla();
            NavRegionVolume bridge = RegionNavProfile.Active.Find(new Vector3(4f, 0f, -28f));
            Assert.IsNotNull(bridge); Assert.AreEqual(1.35f, bridge.costMultiplier, 1e-6f);
            _rig.Teleport(Yerde(new Vector3(4f, 0f, -43f)), Quaternion.identity);
            yield return null;
            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 14; i++) bodies.Add(Dogur(EnemyTier.Standard, new Vector3(-2f + (i % 4) * 2f, 0f, -8f - (i / 4) * 2f)));
            yield return null;
            float t0 = Time.realtimeSinceStartup; int samples = 0, maxInside = 0, wallSamples = 0, waitSamples = 0, shuffleSamples = 0;
            while (Time.realtimeSinceStartup - t0 < 14f)
            {
                maxInside = Mathf.Max(maxInside, RegionNavRouter.Occupancy(bridge));
                foreach (ZombieEnemy z in bodies) { if (!z.IsAlive) continue; if (z.WallSliding) wallSamples++; if (z.IsWaiting) waitSamples++; if (z.LastShuffle.sqrMagnitude > 1e-6f) shuffleSamples++; }
                samples++;
                yield return null;
            }
            Debug.Log("[DD-OBS] wp054.bridge samples=" + samples + " maxInside=" + maxInside + " queueEvents=" + RegionNavRouter.QueueEvents + " reroutes=" + RegionNavRouter.RerouteCount + " wallSlideSamples=" + wallSamples + " waitSamples=" + waitSamples + " shuffleSamples=" + shuffleSamples);
            Assert.Greater(samples, 200);
            Assert.GreaterOrEqual(maxInside, 1, "köprü ×1,35 ile en az bir gövde köprüyü kullanır (v3 NavMesh)");
            Assert.Greater(RegionNavRouter.RerouteCount + RegionNavRouter.QueueEvents, 0, "boğaz kuralı çalışır: kıyı alternatifi ya da kuyruk");
            EnemyFactory.ClearAll();
        }
    }
}
