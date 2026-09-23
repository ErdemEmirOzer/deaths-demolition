using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-038M · The playable area of World_AP005 is closed: the invisible ring stands on the ±45 m region line, blocks the Player and
    /// Enemy layers and nothing that aims or shoots, a capsule swept outward every 0.5 m around the whole perimeter is stopped at the
    /// line (a gap = FAIL); the only holes in the ground (1 m down-ray grid) are the koi ponds, and a capsule swept into every pond
    /// from outside is stopped at its rim (fence or raised crossing).
    /// </summary>
    public sealed class WorldBoundaryTests
    {
        private static Transform OpenRing()
        {
            EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            GameObject root = GameObject.Find(Ap005WorldBootstrap.RootName);
            Assert.IsNotNull(root, "dünya kökü");
            Transform ring = root.transform.Find(WorldBoundaryBuilder.ObjectName);
            Assert.IsNotNull(ring, "WP-038M sınır halkası sahnede yok");
            Physics.SyncTransforms();
            return ring;
        }

        [Test]
        public void SinirHalkasi_DortDuvar_IgnoreRaycast_OyuncuVeDusmaniDurdurur_AtisVeKamerayiEtkilemez()
        {
            Transform ring = OpenRing();
            var walls = new List<BoxCollider>();
            foreach (string side in new[] { "North", "South", "East", "West" })
            {
                Transform w = ring.Find(side);
                Assert.IsNotNull(w, "duvar " + side);
                walls.Add(w.GetComponent<BoxCollider>());
            }

            foreach (BoxCollider seg in ring.Find(WorldBoundaryBuilder.PondRimsName).GetComponentsInChildren<BoxCollider>(true))
            {
                Assert.AreEqual(WorldBoundaryBuilder.Layer, seg.gameObject.layer, seg.name + " katman");
                Assert.IsFalse(seg.isTrigger, seg.name);
                Assert.IsNull(seg.GetComponent<Renderer>(), seg.name + ": görünmez");
            }

            foreach (BoxCollider b in walls)
            {
                Assert.AreEqual(WorldBoundaryBuilder.Layer, b.gameObject.layer, b.name + " katman");
                Assert.IsFalse(b.isTrigger, b.name);
                Assert.IsNull(b.GetComponent<Renderer>(), b.name + ": görünmez");
                float inner = Mathf.Min(Mathf.Abs(b.bounds.min.x), Mathf.Abs(b.bounds.max.x), Mathf.Abs(b.bounds.min.z), Mathf.Abs(b.bounds.max.z));
                Assert.AreEqual(WorldBoundaryBuilder.HalfExtent, inner, 0.01f, b.name + ": iç yüz ±45 m çizgisinde");
                Assert.LessOrEqual(b.bounds.min.y, -1f); Assert.GreaterOrEqual(b.bounds.max.y, 8f);
            }

            Assert.IsNotNull(ring.GetComponent<WorldFallGuard>(), "düşme güvenlik ağı");
            int layer = WorldBoundaryBuilder.Layer;
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PrototypeLayers.Player, layer), "oyuncu halkaya çarpar");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(PrototypeLayers.Enemy, layer), "zombi halkaya çarpar");
            Assert.AreEqual(0, PrototypeLayers.BulletHitMask & (1 << layer), "mermi halkayı görmez");
            Assert.AreEqual(0, PrototypeLayers.AimBlockMask & (1 << layer), "nişan / kamera halkayı görmez");
            Assert.AreEqual(0, Physics.DefaultRaycastLayers & (1 << layer), "maskesiz ışın da görmez (Ignore Raycast)");
        }

        [Test]
        public void SinirHalkasi_CevreBoyuncaKapsulTaramasi_BoslukYok()
        {
            OpenRing();
            // The player's own collision mask: every layer the Player layer is not told to ignore.
            int mask = 0;
            for (int l = 0; l < 32; l++) if (!Physics.GetIgnoreLayerCollision(PrototypeLayers.Player, l)) mask |= 1 << l;
            float h = WorldBoundaryBuilder.HalfExtent;
            const float radius = 0.35f, step = 0.5f, start = 1.5f;
            var gaps = new List<string>();
            int sweeps = 0;
            foreach ((Vector3 outward, Vector3 along) in new[] { (Vector3.forward, Vector3.right), (Vector3.back, Vector3.right), (Vector3.right, Vector3.forward), (Vector3.left, Vector3.forward) })
            {
                for (float t = -h + 0.25f; t <= h - 0.25f; t += step)
                {
                    Vector3 basePoint = outward * (h - start) + along * t;
                    Vector3 p1 = basePoint + Vector3.up * 0.4f, p2 = basePoint + Vector3.up * 1.4f;
                    sweeps++;
                    // RaycastAll-style: the nearest hit that is NOT a collider the capsule already overlaps at the start.
                    bool blocked = Physics.CapsuleCast(p1, p2, radius, outward, out RaycastHit hit, start + 1f, mask, QueryTriggerInteraction.Ignore)
                                   && hit.distance <= start + 0.05f;
                    if (!blocked) gaps.Add(outward + " @ " + t.ToString("F2"));
                }
            }

            Assert.Greater(sweeps, 700);
            Assert.IsEmpty(gaps, "Sınırda oyuncu kapsülünün geçtiği boşluk (" + gaps.Count + "):\n" + string.Join("\n", gaps.GetRange(0, Mathf.Min(40, gaps.Count))));
        }

        private static List<(Vector2 centre, Vector2 semi, float yaw)> Basins()
        {
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            Assert.IsNotNull(placement, "yerleşim varlığı");
            return PlacementImporter.BasinHoles(placement);
        }

        private static int PlayerMask()
        {
            int mask = 0;
            for (int l = 0; l < 32; l++) if (!Physics.GetIgnoreLayerCollision(PrototypeLayers.Player, l)) mask |= 1 << l;
            return mask;
        }

        [Test]
        public void OynanabilirAlan_ZeminDelikleriYalnizHavuzlarda()
        {
            OpenRing();
            List<(Vector2 centre, Vector2 semi, float yaw)> basins = Basins();
            // Ground = the plates / props plus the WP-038M plugs (Ignore Raycast); a hole ≥ the 0.7 m capsule holds a 0.5 m grid point.
            int ground = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder) | (1 << WorldBoundaryBuilder.Layer);
            float h = WorldBoundaryBuilder.HalfExtent - 0.25f;
            var stray = new List<string>();
            int samples = 0, pond = 0;
            for (float x = -h; x <= h; x += 0.5f)
            for (float z = -h; z <= h; z += 0.5f)
            {
                samples++;
                if (Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, 8f, ground, QueryTriggerInteraction.Ignore)) continue;
                bool inPond = WorldBoundaryBuilder.InsidePond(basins, x, z);
                if (inPond) pond++;
                else stray.Add("(" + x + ", " + z + ")");
            }

            Debug.Log("[DD-TEST] wp038m.ground samples=" + samples + " pondHoles=" + pond + " stray=" + stray.Count + (stray.Count > 0 ? " first=" + string.Join(" ", stray.GetRange(0, Mathf.Min(60, stray.Count))) : ""));
            Assert.Greater(basins.Count, 0, "havuz yok?");
            Assert.IsEmpty(stray, "Havuz dışında zemin deliği (" + stray.Count + "): " + string.Join(" ", stray.GetRange(0, Mathf.Min(60, stray.Count))));
        }

        [Test]
        public void Havuzlar_KenardanIceriKapsulTaramasi_CitVeyaGecitteDurur()
        {
            OpenRing();
            List<(Vector2 centre, Vector2 semi, float yaw)> basins = Basins();
            int mask = PlayerMask();
            const float radius = 0.35f, start = 1.2f;
            var gaps = new List<string>();
            int sweeps = 0, crossings = 0;
            for (int bi = 0; bi < basins.Count; bi++)
            {
                int n = WorldBoundaryBuilder.PondSegments(basins[bi]);
                for (int i = 0; i < n; i++)
                {
                    Vector2 m = (WorldBoundaryBuilder.PondPoint(basins[bi], 2f * Mathf.PI * i / n) + WorldBoundaryBuilder.PondPoint(basins[bi], 2f * Mathf.PI * (i + 1) / n)) * 0.5f;
                    Vector2 outward2 = (m - basins[bi].centre).normalized;
                    var outward = new Vector3(outward2.x, 0f, outward2.y);
                    Vector3 basePoint = new Vector3(m.x, 0f, m.y) + outward * start;
                    if (WorldBoundaryBuilder.IsCrossing(m)) crossings++;
                    sweeps++;
                    bool blocked = Physics.CapsuleCast(basePoint + Vector3.up * 0.4f, basePoint + Vector3.up * 1.4f, radius, -outward, out RaycastHit hit, start + 1f, mask, QueryTriggerInteraction.Ignore)
                                   && hit.distance <= start + 0.1f;
                    if (!blocked) gaps.Add("havuz" + bi + " #" + i + " @(" + m.x.ToString("F1") + ", " + m.y.ToString("F1") + ")");
                }
            }

            Debug.Log("[DD-TEST] wp038m.ponds basins=" + basins.Count + " sweeps=" + sweeps + " crossings=" + crossings + " gaps=" + gaps.Count);
            Assert.IsEmpty(gaps, "Havuz kenarında oyuncu kapsülünün geçtiği boşluk (" + gaps.Count + "):\n" + string.Join("\n", gaps.GetRange(0, Mathf.Min(40, gaps.Count))));
        }
    }
}
