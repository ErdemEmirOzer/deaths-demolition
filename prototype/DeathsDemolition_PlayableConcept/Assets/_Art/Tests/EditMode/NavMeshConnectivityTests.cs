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
                Bounds b = box.bounds;
                // Sample the footprint every 0.25 m along its long axis (skirts of a side are merged into one box, WP-038Q).
                bool alongZ = b.size.z >= b.size.x;
                for (float u = 0.1f; u <= (alongZ ? b.size.z : b.size.x) - 0.1f; u += 0.25f)
                {
                    Vector3 q = alongZ ? new Vector3(b.center.x, 0.2f, b.min.z + u) : new Vector3(b.min.x + u, 0.2f, b.center.z);
                    if (NavMesh.SamplePosition(q, out NavMeshHit under, 0.1f, NavMesh.AllAreas) && under.position.x >= b.min.x && under.position.x <= b.max.x && under.position.z >= b.min.z && under.position.z <= b.max.z)
                    { navUnderSkirt.Add(box.name + " " + under.position.ToString("F2")); break; }
                }
            }

            Assert.GreaterOrEqual(skirtCount, 2, "köprü korkuluk etekleri");
            Assert.IsEmpty(navUnderSkirt, "etek altında yürünebilir NavMesh: " + string.Join(", ", navUnderSkirt));
            Assert.Greater(points.Count, 30);
            Assert.IsEmpty(unreachable, "Başlangıca tam yolu olmayan işaretli nokta (" + unreachable.Count + "):\n" + string.Join("\n", unreachable));
            Assert.IsEmpty(traps, "Bedenin ana NavMesh'ten yürüyerek geçebildiği (kapsül r " + (NavMeshIslandLinker.BodyRadius + NavMeshIslandLinker.BodySkin) + ", " + NavMeshIslandLinker.StepHeight + " m basamak) kopuk adacık (" + traps.Count + "):\n" + string.Join("\n", traps));
        }

        /// <summary>
        /// WP-038Q · The bridge deck is a corridor a body walks, not a slot it must thread: between the two skirts of the bridge a
        /// body capsule (r 0.48 with skin, standing on the deck) fits across a band of at least 0.18 m at every 5 cm along it, and
        /// no pond fence segment reaches into it (run 25: Pond0_48 / 51 left 2 cm and held the player 230 s).
        /// </summary>
        [Test]
        public void KopruGuvertesi_BedenKoridoruAcik_CitSokulmez_EtekBirlesik()
        {
            OpenWorld();
            Physics.SyncTransforms();
            Transform root = GameObject.Find(Ap005WorldBootstrap.RootName).transform;
            BoxCollider[] skirtBoxes = root.Find(PlacementImporter.BridgeRailSkirtRootName).GetComponentsInChildren<BoxCollider>();
            var skirts = new List<Bounds>();
            foreach (BoxCollider box in skirtBoxes) skirts.Add(box.bounds);
            // The deck's two sides: long skirts along z with overlapping spans, one west of the other.
            skirts.Sort((a, b) => a.center.x.CompareTo(b.center.x));
            Assert.AreEqual(2, skirts.Count, "köprünün her yanı tek (birleşik) etek: " + skirts.Count);
            Bounds west = skirts[0], east = skirts[1];
            float z0 = Mathf.Max(west.min.z, east.min.z) + 0.5f, z1 = Mathf.Min(west.max.z, east.max.z) - 0.5f;
            Assert.Greater(z1 - z0, 10f, "güverte boyu");
            int mask = NavMeshIslandLinker.BodyMask();
            float r = NavMeshIslandLinker.BodyRadius + NavMeshIslandLinker.BodySkin, narrowest = float.MaxValue, narrowestZ = 0f;
            for (float z = z0; z <= z1; z += 0.05f)
            {
                float top = Physics.Raycast(new Vector3((west.max.x + east.min.x) * 0.5f, 3f, z), Vector3.down, out RaycastHit h, 6f, (1 << 14) | (1 << 15), QueryTriggerInteraction.Ignore) ? h.point.y : 0f;
                float first = float.NaN, last = float.NaN;
                for (float x = west.max.x; x <= east.min.x; x += 0.01f)
                {
                    bool blocked = Physics.CheckCapsule(new Vector3(x, top + 0.35f + r, z), new Vector3(x, top + 1.8f - 0.45f, z), r, mask, QueryTriggerInteraction.Ignore);
                    if (blocked) continue;
                    if (float.IsNaN(first)) first = x;
                    last = x;
                }

                float band = float.IsNaN(first) ? 0f : last - first;
                if (band < narrowest) { narrowest = band; narrowestZ = z; }
            }

            Debug.Log("[DD-TEST] wp038q.deck corridor " + west.max.x.ToString("F2") + ".." + east.min.x.ToString("F2") + " narrowest body band " + narrowest.ToString("F2") + " m at z " + narrowestZ.ToString("F2"));
            Assert.GreaterOrEqual(narrowest, 0.18f, "güvertede bedenin sığdığı bant çok dar (z " + narrowestZ.ToString("F2") + ")");
            var intruders = new List<string>();
            foreach (BoxCollider seg in root.Find(WorldBoundaryBuilder.ObjectName).Find(WorldBoundaryBuilder.PondRimsName).GetComponentsInChildren<BoxCollider>())
                foreach (BoxCollider sk in skirtBoxes)
                    if (Physics.ComputePenetration(seg, seg.transform.position, seg.transform.rotation, sk, sk.transform.position, sk.transform.rotation, out _, out _))
                        intruders.Add(seg.name + "↔" + sk.name);
            Assert.IsEmpty(intruders, "havuz çiti köprü eteğine giriyor: " + string.Join(", ", intruders));
        }
    }
}
