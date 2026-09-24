using System.Collections.Generic;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-038P · The World_AP005 NavMesh is one piece where bodies go (NavMeshIslandLinker): no island a body can walk to
    /// stays unlinked — a zombie pushed there would stand on a NavMesh piece no path leaves (S-10 soak seed 61, run 14) —
    /// no NavMesh under the bridge rail skirts, and the player start, the twelve spawns, the twelve weapon points, the five
    /// region centres, both bridge ends and the soak's stuck-zombie spot reach the start with a complete path.
    /// </summary>
    public sealed class NavMeshConnectivityTests
    {
        /// <summary>Highest surface (any collider a body meets) under the crossing segment, sampled every 5 cm, relative to its lower end.</summary>
        public static float MaxTop(Vector3 a, Vector3 b)
        {
            float top = float.MinValue, baseY = Mathf.Min(a.y, b.y);
            for (float u = 0f; u <= 1f; u += 0.05f / Mathf.Max(0.05f, Vector3.Distance(a, b)))
            {
                Vector3 q = Vector3.Lerp(a, b, u);
                if (Physics.Raycast(new Vector3(q.x, baseY + 2f, q.z), Vector3.down, out RaycastHit hit, 3f, NavMeshIslandLinker.BodyMask(), QueryTriggerInteraction.Ignore)) top = Mathf.Max(top, hit.point.y);
            }

            return top - baseY;
        }

        private static Vector3 Seed(WorldPlacementAsset placement)
        {
            Assert.IsTrue(NavMesh.SamplePosition(placement.playerStart, out NavMeshHit hit, 3f, NavMesh.AllAreas), "oyuncu başlangıcı NavMesh dışı");
            return hit.position;
        }

        private static WorldPlacementAsset OpenWorld()
        {
            EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            Assert.IsNotNull(placement);
            return placement;
        }

        [Test]
        public void NavMesh_TekParca_BedenErisilebilirAdacikYok_IsaretliNoktalarBaslangicaUlasir()
        {
            WorldPlacementAsset placement = OpenWorld();
            Vector3 seed = Seed(placement);
            List<NavMeshIslandLinker.Island> islands = NavMeshIslandLinker.Islands(seed, out int mainTris, out float mainArea);
            var log = new StringBuilder("[DD-TEST] wp038p.navmesh main " + mainTris + " tris " + mainArea.ToString("F0") + " m²; islands " + islands.Count + "\n");
            var traps = new List<string>();
            foreach (NavMeshIslandLinker.Island i in islands)
            {
                bool trap = i.BodyCrosses;
                string row = "  " + (trap ? "TRAP " : "     ") + i.Centroid.ToString("F2") + " area " + i.Area.ToString("F2") + " m² y " + i.MinY.ToString("F2") + ".." + i.MaxY.ToString("F2") + " gap " + (i.GapToMain < 1e6f ? i.GapToMain.ToString("F2") : "∞") + " tris " + i.Triangles
                             + (trap ? " cross " + i.CrossFrom.ToString("F2") + "→" + i.CrossTo.ToString("F2") + " maxTop " + MaxTop(i.CrossFrom, i.CrossTo).ToString("F2") : "");
                log.Append(row).Append('\n');
                if (trap) traps.Add(row.Trim());
            }

            Debug.Log(log.ToString());

            // Marked points: player start, spawns, weapon points, region centres, bridge ends — all joined to the start.
            var points = new List<(string name, Vector3 p)> { ("start", placement.playerStart) };
            foreach (WorldPlacementAsset.Point p in placement.spawnPoints) points.Add(("spawn " + p.id, p.position));
            foreach (WorldPlacementAsset.Point p in placement.weaponPoints) points.Add(("weapon " + p.id, p.position));
            points.Add(("centre", new Vector3(0f, 0f, 0f))); points.Add(("north", new Vector3(0f, 0f, 30f))); points.Add(("east", new Vector3(30f, 0f, 0f)));
            points.Add(("south", new Vector3(20f, 0f, -30f))); points.Add(("west", new Vector3(-30f, 0f, 0f)));
            points.Add(("bridge N", new Vector3(4f, 0f, -22.5f))); points.Add(("bridge S", new Vector3(4f, 0f, -37f))); points.Add(("south bank", new Vector3(4f, 0f, -40f)));
            points.Add(("soak61-14 zombie", new Vector3(3.2f, 0.2f, -36.1f)));
            var unreachable = new List<string>();
            var path = new NavMeshPath();
            foreach ((string name, Vector3 p) in points)
            {
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) { unreachable.Add(name + " NavMesh dışı " + p); continue; }
                if (!NavMesh.CalculatePath(hit.position, seed, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    unreachable.Add(name + " " + p + " → " + hit.position.ToString("F2") + " " + path.status);
            }

            // Where the NavMesh says "not walkable" beside a bridge rail, a body cannot stand either (rail skirts, WP-038P).
            Transform root = GameObject.Find(Ap005WorldBootstrap.RootName).transform;
            Transform skirtRoot = root.Find(PlacementImporter.BridgeRailSkirtRootName);
            Assert.IsNotNull(skirtRoot, "köprü korkuluk etekleri yok");
            int skirtCount = 0;
            var navUnderSkirt = new List<string>();
            foreach (BoxCollider box in skirtRoot.GetComponentsInChildren<BoxCollider>())
            {
                skirtCount++;
                Vector3 c = box.transform.TransformPoint(box.center);
                if (NavMesh.SamplePosition(new Vector3(c.x, 0.2f, c.z), out NavMeshHit under, 0.12f, NavMesh.AllAreas)) navUnderSkirt.Add(box.name + " " + under.position.ToString("F2"));
            }

            Assert.AreEqual(38, skirtCount, "38 korkuluk hacmi = 38 etek");
            Assert.IsEmpty(navUnderSkirt, "etek altında yürünebilir NavMesh: " + string.Join(", ", navUnderSkirt));
            Assert.Greater(points.Count, 30);
            Assert.IsEmpty(unreachable, "Başlangıca tam yolu olmayan işaretli nokta (" + unreachable.Count + "):\n" + string.Join("\n", unreachable));
            Assert.IsEmpty(traps, "Bedenin ana NavMesh'ten yürüyerek geçebildiği (kapsül r " + (NavMeshIslandLinker.BodyRadius + NavMeshIslandLinker.BodySkin) + ", " + NavMeshIslandLinker.StepHeight + " m basamak) kopuk adacık (" + traps.Count + "):\n" + string.Join("\n", traps));
        }
    }
}
