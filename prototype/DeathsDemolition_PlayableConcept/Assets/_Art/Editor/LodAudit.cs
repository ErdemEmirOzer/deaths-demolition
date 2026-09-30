using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B7 · Static LOD / culling inventory of World_V6_Greybox (edit mode; no PlayMode, no Player; the scene is never saved).
    /// One row per LODGroup (source prefab, position, world size, per-LOD screen-relative transition height and triangles, layer,
    /// static flags, instancing) and one per renderer outside any LODGroup; a header with the scene's occlusion data size.
    /// The switch distances per quality tier × camera mode are computed offline from these rows (lod-audit.py). Output: env DD_B7_OUT.
    /// </summary>
    public static class LodAudit
    {
        public static void Run()
        {
            string outPath = Environment.GetEnvironmentVariable("DD_B7_OUT");
            if (string.IsNullOrEmpty(outPath)) throw new InvalidOperationException("[B7] set DD_B7_OUT");
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);

            var sb = new StringBuilder();
            sb.Append("scene\t").Append(GreyboxV6Builder.ScenePath).Append("\tumbraBytes\t").Append(StaticOcclusionCulling.umbraDataSize.ToString(CultureInfo.InvariantCulture)).Append('\n');
            var inGroup = new HashSet<Renderer>();
            int groups = 0, loose = 0;
            foreach (LODGroup g in UnityEngine.Object.FindObjectsOfType<LODGroup>(true))
            {
                groups++;
                LOD[] lods = g.GetLODs();
                Vector3 s = g.transform.lossyScale;
                float worldSize = g.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                var cells = new List<string>();
                Renderer first = null;
                foreach (LOD lod in lods)
                {
                    int tris = 0;
                    foreach (Renderer r in lod.renderers)
                    {
                        if (r == null) continue;
                        inGroup.Add(r);
                        first ??= r;
                        tris += Triangles(r);
                    }

                    cells.Add(lod.screenRelativeTransitionHeight.ToString("0.#####", CultureInfo.InvariantCulture) + ":" + tris);
                }

                sb.Append("lod\t").Append(Source(g.gameObject)).Append('\t').Append(Where(g.transform)).Append('\t').Append(Vec(g.transform.position))
                  .Append('\t').Append(worldSize.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t').Append(g.fadeMode)
                  .Append('\t').Append(first != null ? LayerMask.LayerToName(first.gameObject.layer) : "-")
                  .Append('\t').Append(first != null ? Flags(first.gameObject) : "-")
                  .Append('\t').Append(first != null ? Instancing(first) : "-")
                  .Append('\t').Append(string.Join(" ", cells)).Append('\n');
            }

            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
            {
                if (inGroup.Contains(r) || !(r is MeshRenderer)) continue;
                loose++;
                Bounds b = r.bounds;
                sb.Append("mesh\t").Append(Source(r.gameObject)).Append('\t').Append(Where(r.transform)).Append('\t').Append(Vec(b.center))
                  .Append('\t').Append(Mathf.Max(b.size.x, b.size.y, b.size.z).ToString("0.###", CultureInfo.InvariantCulture)).Append("\t-")
                  .Append('\t').Append(LayerMask.LayerToName(r.gameObject.layer)).Append('\t').Append(Flags(r.gameObject)).Append('\t').Append(Instancing(r))
                  .Append('\t').Append("1:").Append(Triangles(r)).Append('\n');
            }

            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[B7] " + groups + " LOD groups, " + loose + " loose mesh renderers, umbra " + StaticOcclusionCulling.umbraDataSize + " B -> " + outPath);
        }

        private static int Triangles(Renderer r)
        {
            Mesh m = r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : r is SkinnedMeshRenderer sk ? sk.sharedMesh : null;
            if (m == null) return 0;
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
            return (int)n;
        }

        private static string Source(GameObject go)
        {
            GameObject src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            return src != null ? src.name : go.name;
        }

        private static string Where(Transform t)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null && parts.Count < 3; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }

        private static string Vec(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.##}\t{1:0.##}\t{2:0.##}", v.x, v.y, v.z);

        private static string Flags(GameObject go)
        {
            StaticEditorFlags f = GameObjectUtility.GetStaticEditorFlags(go);
            return ((f & StaticEditorFlags.OccluderStatic) != 0 ? "O" : "-") + ((f & StaticEditorFlags.OccludeeStatic) != 0 ? "o" : "-") +
                   ((f & StaticEditorFlags.BatchingStatic) != 0 ? "B" : "-") + ((f & StaticEditorFlags.ContributeGI) != 0 ? "G" : "-");
        }

        private static string Instancing(Renderer r)
        {
            int on = 0, all = 0;
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;
                all++;
                if (m.enableInstancing) on++;
            }

            return on + "/" + all;
        }
    }
}
