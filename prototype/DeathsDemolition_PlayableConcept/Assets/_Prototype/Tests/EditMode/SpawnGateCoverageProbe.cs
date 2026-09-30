using System.Collections.Generic;
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
    /// WP-076 (C-00 26.09, target ≤ 7 %) · a measurement tool, not a gate: [Explicit, Category("Diagnostics")] (Y6 policy, B9) — runs only when named. On the v6 NavMesh it
    /// takes the same grid / metric as <see cref="SpawnGateCoverageTests"/> (band ≥ 3 gates in 22–45 m, ≥ 8 within 75 m, by path).
    /// <see cref="V6_KapiKapsami_Arama"/> builds a cell × candidate path matrix (each gate's own neighbourhood, 2 m steps; plus a 4 m
    /// grid of "added gate" positions) and searches greedily: the best single moves of inner gates (then edge gates too), and the
    /// best single added gate. <see cref="V6_KapiKapsami_Setler"/> (WP-076 B9, C-00 30.09) measures named gate sets given as moves
    /// over the current spawn-gates-v6.json (DD_WP076_PROBE_SET = json file) on the loaded NavMesh — reachability, share, sectors,
    /// first contact — one table row per set. Both write DD_WP076_PROBE_OUT (and the log, [DD-OBS] wp076.probe / wp076.sets).
    /// B9 replaced the 29.09 "fixed3" mode (pinned scene / NavMesh / gates hashes of a pre-92232235 state and a hard-coded set).
    /// </summary>
    [Category("WP076Probe")]
    public sealed class SpawnGateCoverageProbe
    {
        private const string ScenePath = "Assets/_Art/Scenes/World_V6_Greybox.unity";
        private const string GatesPath = "Assets/_Art/Environment/World/V6/spawn-gates-v6.json";
        private const string NavPath = "Assets/_Art/Environment/World/V6/NavMesh-World_V6_Greybox.asset";
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
        [Explicit("Diagnostics: spawn-gate search (WP-076)")]
        [Category("Diagnostics")]
        public void V6_KapiKapsami_Arama()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GateFile file = JsonUtility.FromJson<GateFile>(File.ReadAllText(GatesPath));
            var outSb = new StringBuilder();
            var t0 = System.Diagnostics.Stopwatch.StartNew();

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
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(g.x, 0.5f, g.z), out NavMeshHit h, 3f, NavMesh.AllAreas), g.id);
                cand.Add(h.position);
                owner.Add(i);
            }
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

        [System.Serializable] private sealed class SetFile { public GateSet[] sets; }
        [System.Serializable] private sealed class GateSet { public string name; public Gate[] moves; }

        /// <summary>
        /// WP-076 B9 (C-00 30.09): each named set = the current json gates with its moves applied (id → x, z; the kind is kept).
        /// Gate sampling, reachability and the per-cell metric are those of <see cref="SpawnGateCoverageTests"/>; a moved gate also
        /// reports its strict sample (≤ 0.9 m, as a builder would place it). Asserts nothing but the inputs (report tool).
        /// </summary>
        [Test]
        [Explicit("Diagnostics: named gate sets (WP-076 B9 M-28)")]
        [Category("Diagnostics")]
        public void V6_KapiKapsami_Setler()
        {
            string setsPath = System.Environment.GetEnvironmentVariable("DD_WP076_PROBE_SET");
            // Diagnostics policy (C-00 30.09 Y6): no Assert.Ignore opt-in — the test is Explicit; run without its input it fails plainly
            Assert.IsFalse(string.IsNullOrEmpty(setsPath), "probe sets: DD_WP076_PROBE_SET=<json> gerekli (Explicit tanı testi)");
            SetFile sets = JsonUtility.FromJson<SetFile>(File.ReadAllText(setsPath));
            Assert.IsTrue(sets != null && sets.sets != null && sets.sets.Length > 0, "set dosyası boş: " + setsPath);

            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);   // M-28: Additive, closed in TearDown (Explicit: runs alone)
            var sb = new StringBuilder();
            sb.AppendLine("[DD-OBS] wp076.sets scene=" + ScenePath + " sceneBlob=" + GitBlob(ScenePath) + " navBlob=" + GitBlob(NavPath)
                          + " gatesBlob=" + GitBlob(GatesPath) + " setsFile=" + setsPath + " triangles=" + NavMesh.CalculateTriangulation().indices.Length / 3);
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0.5f, -3f), out NavMeshHit startHit, 3f, NavMesh.AllAreas), "oyuncu başlangıcı NavMesh'te değil");
            Vector3 start = startHit.position;
            var path = new NavMeshPath();

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
            int C = cells.Count;
            Assert.Greater(C, 500, "v6 NavMesh ızgarası boş");
            sb.AppendLine("| set | kapılar erişilebilir | hücre | ihlal | pay | ≤ %7 | sektör (ihlal/band<3/useful<8) | ilk temas medyanı | kenarsız hücre |");

            foreach (GateSet set in sets.sets)
            {
                GateFile file = JsonUtility.FromJson<GateFile>(File.ReadAllText(GatesPath));
                var moved = new HashSet<string>();
                foreach (Gate m in set.moves ?? new Gate[0])
                {
                    Gate g = System.Array.Find(file.gates, x => x.id == m.id);
                    Assert.IsNotNull(g, set.name + ": bilinmeyen kapı " + m.id);
                    g.x = m.x; g.z = m.z; moved.Add(m.id);
                }

                int G = file.gates.Length, reachable = 0, reserved = 0;
                var dist = new float[G * C];
                var gateLog = new StringBuilder();
                for (int i = 0; i < G; i++)
                {
                    Gate g = file.gates[i];
                    bool onMesh = NavMesh.SamplePosition(new Vector3(g.x, 0.5f, g.z), out NavMeshHit h, 3f, NavMesh.AllAreas);
                    bool ok = onMesh && NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    if (ok) reachable++;
                    if (moved.Contains(g.id) || !ok)
                    {
                        bool strict = TryCandidate(new Vector3(g.x, 0.5f, g.z), start, path, out Vector3 sp);
                        if (DD.Art.Pipeline.EditorTools.GreyboxV6Builder.InReservedZone(new Vector2(g.x, g.z), out string zone)) { reserved++; gateLog.Append(" [dışlama " + zone + "]"); }
                        gateLog.Append(' ').Append(g.id).Append("(").Append(g.x.ToString("0.#")).Append(',').Append(g.z.ToString("0.#")).Append(")")
                               .Append(ok ? " yol=" + (NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) ? Length(path).ToString("0") : "?") + "m" : " X")
                               .Append(onMesh ? " snap=" + Vector2.Distance(new Vector2(h.position.x, h.position.z), new Vector2(g.x, g.z)).ToString("0.0") + "m" : " navmesh-yok")
                               .Append(strict ? " sıkı=ok" : " sıkı=YOK").Append(';');
                    }
                    for (int c = 0; c < C; c++)
                        dist[i * C + c] = onMesh && NavMesh.CalculatePath(cells[c], h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete
                            ? Length(path) : float.PositiveInfinity;
                }

                var all = new int[G]; for (int i = 0; i < G; i++) all[i] = i;
                int v = Violations(all, dist, C, null);
                // 030cf3f4 rolling inner cap (every relaxation pass): a cell with no EDGE gate in [MinSpawnDistance, MaxUseful]
                // of path can only be served by inner gates — once the 24-window is at 50 % inner, the director finds no point
                // there until the player moves (NoPointTicks). Counted per set as the starvation exposure.
                int edgeless = 0;
                var edgelessAt = new StringBuilder();
                for (int c = 0; c < C; c++)
                {
                    bool edge = false;
                    for (int i = 0; i < G && !edge; i++)
                    {
                        if (file.gates[i].kind != "edge") continue;
                        float len = dist[i * C + c];
                        edge = len >= SpawnModeTable.MinSpawnDistance[0] && len <= SpawnWorldProfile.V6MaxUseful;
                    }
                    if (edge) continue;
                    if (edgeless++ < 40) edgelessAt.Append(string.Format(" ({0:0},{1:0})", cells[c].x, cells[c].z));
                }
                float share = (float)v / C;
                var sec = new StringBuilder();
                ReportCells(sec, set.name, all, dist, cells);
                string secLine = sec.ToString().Split('\n')[0];
                secLine = secLine.Substring(secLine.IndexOf(':') + 1).Trim();
                sb.AppendLine("| " + set.name + " | " + reachable + "/" + G + " | " + C + " | " + v + " | " + (share * 100f).ToString("0.00") + " % | "
                              + (share <= 0.07f && reachable == G && reserved == 0 ? "EVET" : reserved > 0 ? "HAYIR (dışlama " + reserved + ")" : "HAYIR") + " | " + secLine + " | " + Contact(all, dist, C) + " | " + edgeless + " |");
                sb.AppendLine("    kapılar:" + gateLog);
                if (edgeless > 0) sb.AppendLine("    kenarsız:" + edgelessAt);
            }

            WriteResult(sb);
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

        private static void WriteResult(StringBuilder sb)
        {
            string text = sb.ToString(); Debug.Log(text);
            string outPath = System.Environment.GetEnvironmentVariable("DD_WP076_PROBE_OUT");
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, text);
        }

        private static bool TryCandidate(Vector3 at, Vector3 start, NavMeshPath path, out Vector3 p)
        {
            p = default;
            if (DD.Art.Pipeline.EditorTools.GreyboxV6Builder.InReservedZone(new Vector2(at.x, at.z), out _)) return false;   // M-28: builder zones
            if (!NavMesh.SamplePosition(at, out NavMeshHit h, 0.9f, NavMesh.AllAreas)) return false;
            Vector3 d = h.position - new Vector3(at.x, h.position.y, at.z);
            if (d.sqrMagnitude > 0.8f) return false;
            if (!NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
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
