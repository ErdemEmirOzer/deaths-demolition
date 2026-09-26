using System.Collections.Generic;
using System.Linq;
using System.Text;
using DD.Art;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-073 (C-00 26.09) · the V6 world's impact surfaces after S-05's material slot-order fix: every collider of the default
    /// world is resolved the way a bullet resolves it (Ap005SurfaceTags.Apply → SurfaceResolver.Resolve, the plaza refinement for
    /// the ground) and grouped by what it is — wet slate plaza, timber (buildings, fences, bridges), roof tiles, bamboo,
    /// plaster / stone walls, foliage, water — so a class that lands on the wrong impact kind shows up by name.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073V6SurfaceAuditTests
    {
        private static string ClassOf(Collider c)
        {
            string path = c.name;
            for (Transform t = c.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            string p = path.ToLowerInvariant();
            if (p.Contains("roof")) return "roof (tile)";
            if (p.Contains("bamboo")) return "bamboo";
            if (p.Contains("pond") && p.Contains("water") || p.EndsWith("water")) return "water";
            if (c.name == "Ground" || p.Contains("ground (")) return "ground";
            string fam = null;
            for (Transform t = c.transform; t != null && fam == null; t = t.parent) fam = Ap005SurfaceTags.V6Family(t.name);
            if (fam != null) return "kit:" + fam;
            if (p.Contains("wall")) return "wall";
            if (p.Contains("floor") || p.Contains("deck") || p.Contains("stair")) return "floor/deck";
            return "other";
        }

        private static string PathOf(Collider c)
        {
            string path = c.name;
            Transform t = c.transform.parent;
            for (int i = 0; i < 3 && t != null; i++, t = t.parent) path = t.name + "/" + path;
            return path + " [" + LayerMask.LayerToName(c.gameObject.layer) + "]";
        }

        private static readonly Dictionary<string, SurfaceKind> Expected = new Dictionary<string, SurfaceKind>
        {
            { "roof (tile)", SurfaceKind.Stone }, { "bamboo", SurfaceKind.Wood }, { "water", SurfaceKind.Water }, { "wall", SurfaceKind.Stone },
            { "floor/deck", SurfaceKind.Wood },
        };

        [Test]
        public void V6_YuzeyEtiketleri_SinifBasina()
        {
            EditorSceneManager.OpenScene(ArtLibraryPaths.GreyboxV6Scene, OpenSceneMode.Single);
            var bootstrap = Object.FindObjectOfType<Ap005WorldBootstrap>();
            Assert.IsNotNull(bootstrap, "V6 sahnesinde Ap005WorldBootstrap yok");
            Ap005SurfaceTags.Apply(bootstrap);

            var table = new SortedDictionary<string, Dictionary<SurfaceKind, int>>();
            var wrong = new List<string>();
            foreach (Collider c in Object.FindObjectsOfType<Collider>())
            {
                // Only what a bullet can hit (the boundary's pond rims / invisible walls sit on Ignore Raycast, untagged on purpose).
                if (c.isTrigger || (DD.Prototype.Core.PrototypeLayers.BulletHitMask & (1 << c.gameObject.layer)) == 0) continue;
                string cls = ClassOf(c);
                SurfaceKind kind = SurfaceResolver.Resolve(c);
                if (cls == "ground")
                {
                    // The plaza refinement at the hit point: a slate point inside the rects reads Stone (wet slate), the rest Dirt.
                    Assert.AreEqual(SurfaceKind.Stone, Ap005SurfaceTags.RefineV6(new Vector3(0f, 0f, 0f), kind), "plaza merkezi arduvaz (Stone) değil");
                    Assert.AreEqual(SurfaceKind.Dirt, Ap005SurfaceTags.RefineV6(new Vector3(0f, 0f, -60f), kind), "plaza dışı toprak (Dirt) değil");
                }

                if (!table.TryGetValue(cls, out Dictionary<SurfaceKind, int> row)) table[cls] = row = new Dictionary<SurfaceKind, int>();
                row.TryGetValue(kind, out int n);
                row[kind] = n + 1;
                if (Expected.TryGetValue(cls, out SurfaceKind want) && kind != want && wrong.Count < 60) wrong.Add(cls + " → " + kind + " : " + PathOf(c));
                if (kind == SurfaceKind.Default && wrong.Count < 60) wrong.Add(cls + " → Default : " + PathOf(c));
            }

            var sb = new StringBuilder();
            foreach (var kv in table)
                sb.AppendLine(string.Format("{0,-24} {1}", kv.Key, string.Join(", ", kv.Value.OrderByDescending(x => x.Value).Select(x => x.Key + " " + x.Value))));
            Debug.Log("[DD-TEST] wp073.v6surfaces\n" + sb + (wrong.Count > 0 ? "WRONG:\n  " + string.Join("\n  ", wrong) : "WRONG: none"));
            Assert.IsEmpty(wrong, "yanlış yüzey sınıfları:\n" + string.Join("\n", wrong) + "\n" + sb);
        }
    }
}
