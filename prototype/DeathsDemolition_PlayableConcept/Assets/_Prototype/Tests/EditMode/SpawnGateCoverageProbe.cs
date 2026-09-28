using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.IO;
using System.Text;
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
    /// WP-076 (C-00 26.09, target ≤ 7 %) · a measurement tool, not a gate: runs only with DD_WP076_PROBE=1. On the v6 NavMesh it
    /// takes the same grid / metric as <see cref="SpawnGateCoverageTests"/> (band ≥ 3 gates in 22–45 m, ≥ 8 within 75 m, by path),
    /// builds a cell × candidate path matrix (each gate's own neighbourhood, 2 m steps; plus a 4 m grid of "added gate" positions)
    /// and searches greedily: the best single moves of inner gates (then edge gates too), and the best single added gate. It writes
    /// the violating cells by sector and each step's share to DD_WP076_PROBE_OUT (and the log, [DD-OBS] wp076.probe).
    /// </summary>
    [Category("WP076Probe")]
    public sealed class SpawnGateCoverageProbe
    {
        private const string ScenePath = "Assets/_Art/Scenes/World_V6_Greybox.unity";
        private const string GatesPath = "Assets/_Art/Environment/World/V6/spawn-gates-v6.json";
        private const string NavPath = "Assets/_Art/Environment/World/V6/NavMesh-World_V6_Greybox.asset";
        private const string SceneGuid = "2afaebde573d0428c96966afda288bb6", NavGuid = "0bf676a94f68d4cf18e4725d5df7f9fb";
        private const string SceneBlob = "81d5066fb37c570223d99d20c96225278ffd4f9d", NavBlob = "d6e9c211b33d0ab1687d4baea633ea9944ed5185";
        private const string GatesBlob = "1884da54ead6d59e5ef8df2b4896792be582c224";
        private const float Half = 54f, Step = 2f;
        private const int BandMin = 3, UsefulMin = 8;

        [System.Serializable] private sealed class GateFile { public Gate[] gates; }
        [System.Serializable] private sealed class Gate { public string id; public string kind; public float x; public float z; }

        private Scene _scene;

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void V6_KapiKapsami_Arama()
        {
            if (System.Environment.GetEnvironmentVariable("DD_WP076_PROBE") != "1") Assert.Ignore("probe: DD_WP076_PROBE=1");
            bool fixed3 = System.Environment.GetEnvironmentVariable("DD_WP076_PROBE_FIXED3") == "1";
            _scene = EditorSceneManager.OpenScene(ScenePath, fixed3 ? OpenSceneMode.Single : OpenSceneMode.Additive);
            if (fixed3) ValidateFixedWorld();
            GateFile file = JsonUtility.FromJson<GateFile>(File.ReadAllText(GatesPath));
            var outSb = new StringBuilder();
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            GateFile original = fixed3 ? JsonUtility.FromJson<GateFile>(File.ReadAllText(GatesPath)) : null;
            if (fixed3) ApplyFixedThree(file, original, outSb);

            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0.5f, -3f), out NavMeshHit startHit, 3f, NavMesh.AllAreas));
            Vector3 start = startHit.position;
            var path = new NavMeshPath();

            // cells (same as the gate test)
            var cells = new List<Vector3>();
            for (float x = -Half; x <= Half; x += Step)
            for (float z = -Half; z <= Half; z += Step)
            {
                if (!NavMesh.SamplePosition(new Vector3(x, 0.5f, z), out NavMeshHit c, 0.9f, NavMesh.AllAreas)) continue;
                Vector3 d = c.position - new Vector3(x, c.position.y, z);
                if (d.sqrMagnitude > 0.8f) continue;
                if (!NavMesh.CalculatePath(c.position, start, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                cells.Add(c.position);
            }

            // candidates: [0, G) the gates as they are; then per gate its neighbourhood; then the added-gate grid
            int G = file.gates.Length;
            var cand = new List<Vector3>(); var owner = new List<int>();   // owner −1 = added gate
            for (int i = 0; i < G; i++)
            {
                Gate g = file.gates[i];
                Vector3 at = new Vector3(g.x, 0.5f, g.z);
                if (fixed3 && IsFixedGate(g.id))
                {
                    bool valid = TryCandidate(at, start, path, out Vector3 sample, outSb, g.id);
                    if (!valid) WriteResult(outSb.AppendLine("[DD-OBS] wp076.fixed3 candidate FAIL/OPEN " + g.id));
                    Assert.IsTrue(valid, "fixed3 candidate FAIL/OPEN: " + g.id);
                    cand.Add(sample);
                }
                else
                {
                    Assert.IsTrue(NavMesh.SamplePosition(at, out NavMeshHit h, 3f, NavMesh.AllAreas), g.id);
                    cand.Add(h.position);
                    if (fixed3)
                    {
                        Gate source = original.gates[i];
                        Assert.AreEqual(source.id, g.id); Assert.AreEqual(source.kind, g.kind);
                        Assert.AreEqual(source.x, g.x); Assert.AreEqual(source.z, g.z);
                        outSb.AppendLine("[DD-OBS] wp076.fixed3 unchanged " + g.id + " kind=" + g.kind +
                            " source=" + at.ToString("F4") + " sample=" + h.position.ToString("F4") +
                            " deviationSquared=" + new Vector2(h.position.x - g.x, h.position.z - g.z).sqrMagnitude.ToString("R"));
                    }
                }
                owner.Add(i);
            }
            if (!fixed3)
            {
                for (int i = 0; i < G; i++)
                {
                    Gate g = file.gates[i];
                    float r = g.kind == "inner" ? 10f : 6f;
                    for (float dx = -r; dx <= r; dx += Step)
                    for (float dz = -r; dz <= r; dz += Step)
                    {
                        if ((dx == 0f && dz == 0f) || dx * dx + dz * dz > r * r) continue;
                        if (TryCandidate(new Vector3(g.x + dx, 0.5f, g.z + dz), start, path, out Vector3 p)) { cand.Add(p); owner.Add(i); }
                    }
                }
                for (float x = -52f; x <= 52f; x += 4f)
                for (float z = -52f; z <= 52f; z += 4f)
                    if (TryCandidate(new Vector3(x, 0.5f, z), start, path, out Vector3 p)) { cand.Add(p); owner.Add(-1); }
            }

            // the matrix: cell → candidate path length (∞ when incomplete)
            int C = cells.Count, K = cand.Count;
            var dist = new float[K * C];
            for (int k = 0; k < K; k++)
            for (int c = 0; c < C; c++)
                dist[k * C + c] = NavMesh.CalculatePath(cells[c], cand[k], NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? Length(path) : float.PositiveInfinity;
            outSb.AppendLine("[DD-OBS] wp076.probe cells=" + C + " candidates=" + K + " matrix " + t0.ElapsedMilliseconds + " ms");

            // current set
            var set = new int[G]; for (int i = 0; i < G; i++) set[i] = i;
            int v0 = Violations(set, dist, C, null);
            outSb.AppendLine("[DD-OBS] wp076.probe now violations=" + v0 + " share=" + ((float)v0 / C).ToString("0.0000"));
            ReportCells(outSb, "now", set, dist, cells);

            if (fixed3)
            {
                Assert.Greater(C, 0, "fixed3 has no connected grid cells");
                float share = (float)v0 / C;
                outSb.AppendLine("[DD-OBS] wp076.fixed3 coverage gates=22 edge=12 inner=10 changed=3 unchanged=19 cells=" + C +
                    " violations=" + v0 + " share=" + share.ToString("R") + " target07=" + (share <= 0.07f ? "MET" : "OPEN") +
                    " elapsedMs=" + t0.ElapsedMilliseconds);
                WriteResult(outSb);
                return;
            }

            // greedy moves: inner gates only, then inner + edge
            foreach (bool edgeToo in new[] { false, true })
            {
                var s = (int[])set.Clone();
                int v = v0;
                outSb.AppendLine("[DD-OBS] wp076.probe greedy " + (edgeToo ? "inner+edge" : "inner-only"));
                for (int step = 0; step < 6; step++)
                {
                    int bestK = -1, bestV = v;
                    for (int k = G; k < K; k++)
                    {
                        int i = owner[k];
                        if (i < 0 || (!edgeToo && file.gates[i].kind != "inner")) continue;
                        int keep = s[i]; s[i] = k;
                        int vv = Violations(s, dist, C, null);
                        s[i] = keep;
                        if (vv < bestV) { bestV = vv; bestK = k; }
                    }
                    if (bestK < 0) break;
                    int gi = owner[bestK];
                    Vector3 from = cand[s[gi]], to = cand[bestK];
                    s[gi] = bestK; v = bestV;
                    outSb.AppendLine(string.Format("  move {0} ({1:0.0},{2:0.0}) → ({3:0.0},{4:0.0}) [{5:0.0} m]  violations={6} share={7:0.0000} {8}",
                        file.gates[gi].id, from.x, from.z, to.x, to.z, Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z)), v, (float)v / C, Contact(s, dist, C)));
                }
                ReportCells(outSb, edgeToo ? "inner+edge" : "inner-only", s, dist, cells);
            }

            // best single added gate (23 gates) on the current set
            {
                var s = new int[G + 1]; for (int i = 0; i < G; i++) s[i] = i;
                var ranked = new List<(int v, int k)>();
                for (int k = G; k < K; k++)
                {
                    if (owner[k] >= 0) continue;
                    s[G] = k;
                    ranked.Add((Violations(s, dist, C, null), k));
                }
                ranked.Sort((a, b) => a.v.CompareTo(b.v));
                outSb.AppendLine("[DD-OBS] wp076.probe added gate (top 8):");
                for (int r = 0; r < 8 && r < ranked.Count; r++)
                {
                    s[G] = ranked[r].k;
                    outSb.AppendLine(string.Format("  + ({0:0.0},{1:0.0}) violations={2} share={3:0.0000} {4}", cand[ranked[r].k].x, cand[ranked[r].k].z, ranked[r].v, (float)ranked[r].v / C, Contact(s, dist, C)));
                }
            }

            outSb.AppendLine("[DD-OBS] wp076.probe total " + t0.ElapsedMilliseconds + " ms");
            WriteResult(outSb);
        }

        private void ValidateFixedWorld()
        {
            Assert.AreEqual(1, SceneManager.sceneCount, "fixed3 requires an isolated scene");
            Assert.AreEqual(ScenePath, _scene.path);
            Assert.AreEqual(SceneGuid, AssetDatabase.AssetPathToGUID(_scene.path));
            Assert.AreEqual(SceneBlob, GitBlob(_scene.path));
            Assert.AreEqual(GatesBlob, GitBlob(GatesPath));
            Assert.AreEqual(NavGuid, AssetDatabase.AssetPathToGUID(NavPath));
            Assert.AreEqual(NavBlob, GitBlob(NavPath));
            var expected = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavPath);
            Assert.IsNotNull(expected);
            MonoBehaviour surface = null;
            foreach (GameObject root in _scene.GetRootGameObjects())
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().FullName != "Unity.AI.Navigation.NavMeshSurface") continue;
                Assert.IsNull(surface, "fixed3 requires one NavMeshSurface");
                surface = component;
            }
            Assert.IsNotNull(surface);
            Assert.IsTrue(surface.isActiveAndEnabled);
            var data = new SerializedObject(surface).FindProperty("m_NavMeshData");
            Assert.IsNotNull(data);
            Assert.AreSame(expected, data.objectReferenceValue);
            Assert.AreEqual(NavPath, AssetDatabase.GetAssetPath(data.objectReferenceValue));
            var type = surface.GetType();
            var instanceProperty = type.GetProperty("navMeshDataInstance", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(instanceProperty, "cannot verify the active NavMesh instance");
            var instance = (NavMeshDataInstance)instanceProperty.GetValue(surface);
            Assert.IsTrue(instance.valid, "resolved NavMesh asset is not active");
            Assert.AreSame(surface, instance.owner);
            var activeProperty = type.GetProperty("activeSurfaces", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(activeProperty);
            var active = (IList)activeProperty.GetValue(null);
            Assert.AreEqual(1, active.Count, "unrelated active NavMeshSurface");
            Assert.AreSame(surface, active[0]);
            Debug.Log("[DD-OBS] wp076.fixed3 binding scene=" + _scene.path + " sceneGuid=" + SceneGuid + " sceneBlob=" + GitBlob(_scene.path) +
                " loadedScenes=" + SceneManager.sceneCount + " navPath=" + AssetDatabase.GetAssetPath(data.objectReferenceValue) +
                " navGuid=" + AssetDatabase.AssetPathToGUID(NavPath) + " navBlob=" + GitBlob(NavPath) +
                " instanceValid=" + instance.valid + " instanceOwner=" + surface.name + " activeSurfaces=" + active.Count +
                " gatesBlob=" + GitBlob(GatesPath) + " probeBlob=" + GitBlob("Assets/_Prototype/Tests/EditMode/SpawnGateCoverageProbe.cs"));
        }

        private static string GitBlob(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] header = Encoding.UTF8.GetBytes("blob " + bytes.Length + "\0");
            using (var sha = SHA1.Create())
            {
                sha.TransformBlock(header, 0, header.Length, header, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return System.BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static bool IsFixedGate(string id) => id == "I_CloisterWest" || id == "I_GreatSakuraSouth" || id == "I_ShoreEast";

        private static void ApplyFixedThree(GateFile file, GateFile original, StringBuilder sb)
        {
            Assert.AreEqual(22, file.gates.Length);
            var ids = new HashSet<string>();
            int edges = 0, inners = 0, changed = 0, unchanged = 0;
            for (int i = 0; i < file.gates.Length; i++)
            {
                Gate gate = file.gates[i], source = original.gates[i];
                Assert.IsTrue(ids.Add(gate.id), "duplicate gate id: " + gate.id);
                if (gate.kind == "edge") edges++; else { Assert.AreEqual("inner", gate.kind); inners++; }
                if (IsFixedGate(gate.id))
                {
                    Assert.AreEqual("inner", gate.kind);
                    Vector2 at = gate.id == "I_CloisterWest" ? new Vector2(-22f, 14f) :
                        gate.id == "I_GreatSakuraSouth" ? new Vector2(24f, 1f) : new Vector2(32f, -24f);
                    gate.x = at.x; gate.z = at.y; changed++;
                    Assert.IsTrue(source.x != gate.x || source.z != gate.z);
                }
                else { Assert.AreEqual(source.id, gate.id); Assert.AreEqual(source.kind, gate.kind); Assert.AreEqual(source.x, gate.x); Assert.AreEqual(source.z, gate.z); unchanged++; }
            }
            Assert.AreEqual(12, edges); Assert.AreEqual(10, inners); Assert.AreEqual(3, changed); Assert.AreEqual(19, unchanged);
            sb.AppendLine("[DD-OBS] wp076.fixed3 inputs gates=22 edge=12 inner=10 changed=3 unchanged=19 gatesBlob=" + GitBlob(GatesPath));
        }

        private static void WriteResult(StringBuilder sb)
        {
            string text = sb.ToString(); Debug.Log(text);
            string outPath = System.Environment.GetEnvironmentVariable("DD_WP076_PROBE_OUT");
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, text);
        }

        private static bool TryCandidate(Vector3 at, Vector3 start, NavMeshPath path, out Vector3 p, StringBuilder evidence = null, string id = null)
        {
            p = default;
            bool sampled = NavMesh.SamplePosition(at, out NavMeshHit h, 0.9f, NavMesh.AllAreas);
            evidence?.AppendLine("[DD-OBS] wp076.fixed3 sample " + id + " requested=" + at.ToString("F4") + " success=" + sampled + " actual=" + h.position.ToString("F4"));
            if (!sampled) return false;
            Vector3 d = h.position - new Vector3(at.x, h.position.y, at.z);
            evidence?.AppendLine("[DD-OBS] wp076.fixed3 deviation " + id + " squared=" + d.sqrMagnitude.ToString("R"));
            if (d.sqrMagnitude > 0.8f) return false;
            bool calculated = NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path);
            evidence?.AppendLine("[DD-OBS] wp076.fixed3 path " + id + " calculated=" + calculated + " status=" + path.status + " metres=" + Length(path).ToString("R"));
            if (!calculated || path.status != NavMeshPathStatus.PathComplete) return false;
            p = h.position;
            return true;
        }

        private static bool Violated(int[] set, float[] dist, int C, int c, out int band, out int useful)
        {
            band = 0; useful = 0;
            foreach (int k in set)
            {
                float len = dist[k * C + c];
                if (len >= SpawnWorldProfile.V6PreferredMin && len <= SpawnWorldProfile.V6PreferredMax) band++;
                if (len <= SpawnWorldProfile.V6MaxUseful) useful++;
            }
            return band < BandMin || useful < UsefulMin;
        }

        private static int Violations(int[] set, float[] dist, int C, List<int> which)
        {
            int v = 0;
            for (int c = 0; c < C; c++)
                if (Violated(set, dist, C, c, out _, out _)) { v++; which?.Add(c); }
            return v;
        }

        /// <summary>First-contact median path (m) — nearest gate ≥ 12 m and ≤ 75 m; the heavy one ≤ 55 m.</summary>
        private static string Contact(int[] set, float[] dist, int C)
        {
            var near = new List<float>(); var heavy = new List<float>();
            for (int c = 0; c < C; c++)
            {
                float n = float.MaxValue, h = float.MaxValue;
                foreach (int k in set)
                {
                    float len = dist[k * C + c];
                    if (len >= SpawnModeTable.MinSpawnDistance[0] && len <= SpawnWorldProfile.V6MaxUseful) n = Mathf.Min(n, len);
                    if (len >= SpawnModeTable.MinSpawnDistance[0] && len <= SpawnModeTable.HeavyTierMaxPath) h = Mathf.Min(h, len);
                }
                if (n < float.MaxValue) near.Add(n);
                if (h < float.MaxValue) heavy.Add(h);
            }
            near.Sort(); heavy.Sort();
            return "contactMedian=" + (near.Count > 0 ? near[near.Count / 2].ToString("0.0") : "-") + "m heavy=" + (heavy.Count > 0 ? heavy[heavy.Count / 2].ToString("0.0") : "-") + "m";
        }

        private static void ReportCells(StringBuilder sb, string label, int[] set, float[] dist, List<Vector3> cells)
        {
            int C = cells.Count;
            var secV = new int[9]; var secBand = new int[9]; var secUse = new int[9];
            var list = new StringBuilder();
            int shown = 0;
            for (int c = 0; c < C; c++)
            {
                if (!Violated(set, dist, C, c, out int band, out int useful)) continue;
                int sec = Sector(cells[c].x) + 3 * Sector(cells[c].z);
                secV[sec]++; if (band < BandMin) secBand[sec]++; if (useful < UsefulMin) secUse[sec]++;
                if (shown++ < 400) list.Append(string.Format("({0:0},{1:0}:{2}/{3}) ", cells[c].x, cells[c].z, band, useful));
            }
            string[] n = { "SW", "S", "SE", "W", "C", "E", "NW", "N", "NE" };
            sb.Append("[DD-OBS] wp076.probe cells " + label + " by sector (violations/band<3/useful<8):");
            for (int k = 0; k < 9; k++) if (secV[k] > 0) sb.Append(' ').Append(n[k]).Append('=').Append(secV[k]).Append('/').Append(secBand[k]).Append('/').Append(secUse[k]);
            sb.AppendLine();
            sb.AppendLine("  " + list);
        }

        private static int Sector(float v) => v < -18f ? 0 : v > 18f ? 2 : 1;

        private static float Length(NavMeshPath p)
        {
            Vector3[] c = p.corners; float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }
    }
}
