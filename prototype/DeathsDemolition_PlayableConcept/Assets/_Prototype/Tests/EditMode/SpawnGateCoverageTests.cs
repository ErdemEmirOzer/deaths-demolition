using System.Collections.Generic;
using System.IO;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B6c (2/2) · §5.4 the v6 spawn coverage on the real v6 NavMesh (World_V6_Greybox, S-05 WP-074 B1): the gates of
    /// spawn-gates-v6.json, a 2 m grid over the walkable ±54 m, only cells connected to the player start.
    /// ASSERTED (C-00 decision C + D, 25.09): 22 gates, all reachable from the start; ≥ 3 gates within 22–45 m and ≥ 8
    /// within 75 m of path, violations ≤ 7 % of cells; first contact median (nearest eligible gate ≥ 12 m, ≤ 75 m; the
    /// Executioner ≤ 55 m) Standard ≤ 20 s, Runner ≤ 12 s, Executioner ≤ 35 s at the config walk speeds.
    /// REPORTED: the 3 % figure outside the corner belt (|x| and |z| ≤ 40 m), the straight-line variant and the 3 × 3
    /// sector breakdown ([DD-OBS] wp076.b6c). The 25.09 measurement: 18 gates / band ≥ 4 → 62 %; C + D → 9.1 %.
    /// </summary>
    [Category("WP076")]
    public sealed class SpawnGateCoverageTests
    {
        private const string ScenePath = "Assets/_Art/Scenes/World_V6_Greybox.unity";
        private const string GatesPath = "Assets/_Art/Environment/World/V6/spawn-gates-v6.json";
        private const float Half = 54f, Step = 2f;
        // C-00 decision (25.09, option C + D): 22 gates (+ I_CloisterEast, I_ShoreWest, I_ServiceYardEast, I_PlazaSouthWest),
        // band ≥ 3 gates in 22–45 m and ≥ 8 within 75 m; asserted target ≤ 7 % of cells by path; the 3 % figure stays an
        // information line for the cells outside the corner belt (|x| or |z| > 40 m).
        private const int GateCount = 22, BandMin = 3, UsefulMin = 8;
        private const float MaxViolationShare = 0.07f, InfoShareOutsideCorners = 0.03f, CornerBelt = 40f;

        [System.Serializable] private sealed class GateFile { public Gate[] gates; }
        [System.Serializable] private sealed class Gate { public string id; public string kind; public float x; public float z; }

        private Scene _scene;

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void V6_KapiKapsami_IlkTemas()
        {
            Assert.IsTrue(File.Exists(ScenePath) && File.Exists(GatesPath), "v6 sahnesi / kapı verisi yok");
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GateFile file = JsonUtility.FromJson<GateFile>(File.ReadAllText(GatesPath));
            Assert.AreEqual(GateCount, file.gates.Length, "22 kapı (12 kenar + 10 orta; C-00 C + D) — S-05 spawn-gates-v6.json v007-B");

            var gates = new List<Vector3>(18);
            foreach (Gate g in file.gates)
            {
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(g.x, 0.5f, g.z), out NavMeshHit h, 3f, NavMesh.AllAreas), g.id + " NavMesh üstünde değil");
                gates.Add(h.position);
            }

            PrototypeConfigCache speeds = PrototypeConfigCache.Load();
            var path = new NavMeshPath();

            // the playable graph = the NavMesh component the player starts on (islands / vista pockets are not play space)
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0.5f, -3f), out NavMeshHit startHit, 3f, NavMesh.AllAreas), "oyuncu başlangıcı NavMesh'te değil");
            Vector3 start = startHit.position;
            var gateLog = new System.Text.StringBuilder();
            int gatesReachable = 0;
            for (int i = 0; i < gates.Count; i++)
            {
                bool ok = NavMesh.CalculatePath(start, gates[i], NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (ok) gatesReachable++;
                gateLog.Append(file.gates[i].id).Append('=').Append(ok ? Length(path).ToString("0") : "X").Append(' ');
            }
            Debug.Log("[DD-OBS] wp076.b6c gatesFromStart " + gatesReachable + "/18 " + gateLog);
            int bandFail = 0, usefulFail = 0, unreachableCells = 0, flatBandFail = 0, flatUsefulFail = 0, innerCells = 0, innerViolations = 0;
            // S-05 request (25.09): the breakdown by 3 × 3 sectors (36 m each; x W/C/E, z S/C/N) so gate additions can be aimed
            var secCells = new int[9]; var secBandFail = new int[9]; var secUsefulFail = new int[9]; var secBand = new int[9]; var secUseful = new int[9];
            var firstStd = new List<float>(); var firstRun = new List<float>(); var firstExe = new List<float>();
            int cells = 0, violations = 0, minBand = int.MaxValue, minUseful = int.MaxValue;
            for (float x = -Half; x <= Half; x += Step)
            for (float z = -Half; z <= Half; z += Step)
            {
                if (!NavMesh.SamplePosition(new Vector3(x, 0.5f, z), out NavMeshHit c, 0.9f, NavMesh.AllAreas)) continue;
                Vector3 d = c.position - new Vector3(x, c.position.y, z);
                if (d.sqrMagnitude > 0.8f) continue;
                if (!NavMesh.CalculatePath(c.position, start, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { unreachableCells++; continue; }
                cells++;
                int band = 0, useful = 0; float nearest = float.MaxValue, nearestHeavy = float.MaxValue;
                int flatBand = 0, flatUseful = 0;   // straight-line variant (for comparison with S-05's B4/B5 measure)
                for (int i = 0; i < gates.Count; i++)
                {
                    Vector3 fd = gates[i] - c.position; fd.y = 0f; float flat = fd.magnitude;
                    if (flat >= SpawnWorldProfile.V6PreferredMin && flat <= SpawnWorldProfile.V6PreferredMax) flatBand++;
                    if (flat <= SpawnWorldProfile.V6MaxUseful) flatUseful++;
                    if (!NavMesh.CalculatePath(c.position, gates[i], NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    float len = Length(path);
                    if (len >= SpawnWorldProfile.V6PreferredMin && len <= SpawnWorldProfile.V6PreferredMax) band++;
                    if (len <= SpawnWorldProfile.V6MaxUseful) useful++;
                    if (len >= SpawnModeTable.MinSpawnDistance[0] && len <= SpawnWorldProfile.V6MaxUseful) nearest = Mathf.Min(nearest, len);
                    if (len >= SpawnModeTable.MinSpawnDistance[0] && len <= SpawnModeTable.HeavyTierMaxPath) nearestHeavy = Mathf.Min(nearestHeavy, len);
                }
                bool violated = band < BandMin || useful < UsefulMin;
                if (violated) violations++;
                if (Mathf.Abs(c.position.x) <= CornerBelt && Mathf.Abs(c.position.z) <= CornerBelt) { innerCells++; if (violated) innerViolations++; }
                int sec = Sector(c.position.x) + 3 * Sector(c.position.z);
                secCells[sec]++; secBand[sec] += band; secUseful[sec] += useful;
                if (band < BandMin) secBandFail[sec]++;
                if (useful < UsefulMin) secUsefulFail[sec]++;
                if (band < BandMin) bandFail++;
                if (flatBand < BandMin) flatBandFail++;
                if (flatUseful < UsefulMin) flatUsefulFail++;
                if (useful < UsefulMin) usefulFail++;
                minBand = Mathf.Min(minBand, band); minUseful = Mathf.Min(minUseful, useful);
                if (nearest < float.MaxValue) { firstStd.Add(nearest / speeds.Standard); firstRun.Add(nearest / speeds.Special); }
                if (nearestHeavy < float.MaxValue) firstExe.Add(nearestHeavy / speeds.Ultra);
            }

            float share = cells > 0 ? (float)violations / cells : 1f;
            string[] secName = { "SW", "S", "SE", "W", "C", "E", "NW", "N", "NE" };
            var secLog = new System.Text.StringBuilder("[DD-OBS] wp076.b6c sectors (cells / band<4 / useful<8 / mean band / mean useful):");
            for (int k = 0; k < 9; k++)
            {
                if (secCells[k] == 0) continue;
                secLog.Append(' ').Append(secName[k]).Append('=').Append(secCells[k]).Append('/').Append(secBandFail[k]).Append('/').Append(secUsefulFail[k])
                      .Append('/').Append(((float)secBand[k] / secCells[k]).ToString("0.0")).Append('/').Append(((float)secUseful[k] / secCells[k]).ToString("0.0"));
            }
            Debug.Log(secLog.ToString());
            float mStd = Median(firstStd), mRun = Median(firstRun), mExe = Median(firstExe);
            Debug.Log("[DD-OBS] wp076.b6c cells=" + cells + " unreachableCells=" + unreachableCells + " bandFail=" + bandFail + " usefulFail=" + usefulFail + " flatBandFail=" + flatBandFail + " flatUsefulFail=" + flatUsefulFail + " violations=" + violations + " share=" + share.ToString("0.000") + " minBand=" + minBand + " minUseful=" + minUseful
                      + " firstContactMedian std=" + mStd.ToString("0.0") + "s runner=" + mRun.ToString("0.0") + "s exec=" + mExe.ToString("0.0") + "s");
            Assert.Greater(cells, 500, "v6 NavMesh ızgarası boş (sahne NavMesh'i yüklenmedi mi?)");
            Assert.AreEqual(GateCount, gatesReachable, "her kapı oyuncu başlangıcından yürünebilir");
            float innerShare = innerCells > 0 ? (float)innerViolations / innerCells : 0f;
            Debug.Log("[DD-OBS] wp076.b6c outsideCornerBelt cells=" + innerCells + " violations=" + innerViolations + " share=" + innerShare.ToString("0.000")
                      + (innerShare > InfoShareOutsideCorners ? " (> 3 % information line)" : " (≤ 3 %)"));
            Assert.LessOrEqual(share, MaxViolationShare, "C-00 C + D: yol mesafesiyle ihlal ≤ %10 (band ≥ 3 kapı 22–45 m, ≥ 8 kapı ≤ 75 m)");
            Assert.LessOrEqual(mStd, 20f, "Standard ilk temas medyanı ≤ 20 s");
            Assert.LessOrEqual(mRun, 12f, "Runner ilk temas medyanı ≤ 12 s");
            Assert.LessOrEqual(mExe, 35f, "Executioner ilk temas medyanı ≤ 35 s");
        }

        /// <summary>0 / 1 / 2 for −54…−18 / −18…18 / 18…54 (x: W / C / E, z: S / C / N).</summary>
        private static int Sector(float v) => v < -18f ? 0 : v > 18f ? 2 : 1;

        private static float Length(NavMeshPath p)
        {
            Vector3[] c = p.corners; float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        private static float Median(List<float> v)
        {
            if (v.Count == 0) return float.MaxValue;
            v.Sort();
            return v[v.Count / 2];
        }

        /// <summary>Walk speeds from the shipped config (EnemyTierSettings.MoveSpeed).</summary>
        private sealed class PrototypeConfigCache
        {
            public float Standard = 2.7f, Special = 4.6f, Ultra = 1.7f;
            public static PrototypeConfigCache Load()
            {
                var c = new PrototypeConfigCache();
                var cfg = DD.Prototype.Core.Config.PrototypeConfig.Active;
                if (cfg == null) return c;
                var s = cfg.Enemies.Find(DD.Prototype.Core.Config.EnemyTier.Standard); if (s != null) c.Standard = s.MoveSpeed;
                var r = cfg.Enemies.Find(DD.Prototype.Core.Config.EnemyTier.Special); if (r != null) c.Special = r.MoveSpeed;
                var u = cfg.Enemies.Find(DD.Prototype.Core.Config.EnemyTier.Ultra); if (u != null) c.Ultra = u.MoveSpeed;
                return c;
            }
        }
    }
}
