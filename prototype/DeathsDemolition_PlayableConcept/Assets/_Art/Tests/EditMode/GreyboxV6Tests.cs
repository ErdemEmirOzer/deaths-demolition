using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-074 B1 · The World v5 greybox (GreyboxV6Builder, WP-074 direction §0 K1–K5, Ek B §2 / §6): a ±55 m ring with its
    /// runtime WorldBounds and the NavMesh 0.5 m inside it, one NavMesh piece where bodies go (no body-reachable unlinked island),
    /// the twelve spawn gates, twelve weapon points and every room reaching the start, spawn gates ≥ 30 m of path from it, every
    /// room with ≥ 2 exits, roofs out of the bake and tagged Roof, and the stakeholder data (18 gates, 5 regions, acoustic spaces).
    /// </summary>
    public sealed class GreyboxV6Tests
    {
        private static WorldPlacementAsset Open()
        {
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(GreyboxV6Builder.PlacementPath);
            Assert.IsNotNull(placement, "greybox placement");
            return placement;
        }

        private static Vector3 Seed(WorldPlacementAsset placement)
        {
            Assert.IsTrue(NavMesh.SamplePosition(placement.playerStart, out NavMeshHit hit, 3f, NavMesh.AllAreas), "başlangıç NavMesh dışı");
            return hit.position;
        }

        private static float PathLength(NavMeshPath path)
        {
            float len = 0f;
            for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            return len;
        }

        [Test]
        public void Greybox_Halka55_WorldBounds_NavMeshHalkaIcinde_TekParca()
        {
            WorldPlacementAsset placement = Open();
            WorldBounds bounds = Object.FindObjectOfType<WorldBounds>();
            Assert.IsNotNull(bounds, "WorldBounds yok (S-10 bot halka iç yüzünü okuyamaz)");
            Assert.AreEqual(55f, bounds.HalfExtent, 1e-3f);
            Assert.AreEqual(0.5f, bounds.NavMeshInset, 1e-3f);
            float limit = 55f - NavMeshIslandLinker.BodyRadius;
            foreach (Vector3 v in NavMesh.CalculateTriangulation().vertices)
                Assert.IsTrue(Mathf.Abs(v.x) <= limit && Mathf.Abs(v.z) <= limit, "halkaya beden yarıçapından yakın NavMesh: " + v);

            List<NavMeshIslandLinker.Island> islands = NavMeshIslandLinker.Islands(Seed(placement), out int mainTris, out float mainArea);
            var traps = new List<string>();
            foreach (NavMeshIslandLinker.Island i in islands) if (i.BodyCrosses) traps.Add(i.Centroid.ToString("F1") + " " + i.Area.ToString("F1") + " m²");
            Debug.Log("[DD-TEST] wp074.b1.navmesh main " + mainTris + " tris " + mainArea.ToString("F0") + " m², islands " + islands.Count);
            Assert.Greater(mainArea, 5000f, "oynanır NavMesh alanı");
            Assert.IsEmpty(traps, "beden erişimli bağlantısız adacık: " + string.Join(", ", traps));
        }

        [Test]
        public void Greybox_SpawnSilahOdalar_BaslangicaUlasir_Spawn30mUstu_Oda2Cikis()
        {
            WorldPlacementAsset placement = Open();
            Vector3 seed = Seed(placement);
            var path = new NavMeshPath();
            var fails = new List<string>();
            var log = new List<string>();
            Assert.AreEqual(12, placement.spawnPoints.Count);
            Assert.AreEqual(12, placement.weaponPoints.Count);
            foreach (WorldPlacementAsset.Point p in placement.spawnPoints)
            {
                if (!NavMesh.SamplePosition(p.position, out NavMeshHit h, 1.5f, NavMesh.AllAreas)) { fails.Add("spawn " + p.id + " NavMesh dışı"); continue; }
                if (!NavMesh.CalculatePath(h.position, seed, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) { fails.Add("spawn " + p.id + " " + path.status); continue; }
                float len = PathLength(path);
                log.Add(p.id + " " + len.ToString("F0") + " m");
                if (len < 30f) fails.Add("spawn " + p.id + " başlangıca " + len.ToString("F1") + " m (< 30)");
            }

            foreach (WorldPlacementAsset.Point p in placement.weaponPoints)
                if (!NavMesh.SamplePosition(p.position + Vector3.up * 1.3f, out NavMeshHit h, 2f, NavMesh.AllAreas) || !NavMesh.CalculatePath(h.position, seed, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    fails.Add("silah " + p.id + " " + p.position);

            foreach (GreyboxV6Builder.Room room in GreyboxV6Builder.Rooms)
            {
                if (room.Walls && (room.Doors == null || room.Doors.Length < 2)) fails.Add(room.Id + " < 2 çıkış");
                var inside = new Vector3((room.X0 + room.X1) * 0.5f + 1.5f, room.Floor + 0.2f, (room.Z0 + room.Z1) * 0.5f - 1.5f);
                if (!NavMesh.SamplePosition(inside, out NavMeshHit h, 2f, NavMesh.AllAreas) || !NavMesh.CalculatePath(h.position, seed, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    fails.Add(room.Id + " içi başlangıca bağlı değil");
            }

            Debug.Log("[DD-TEST] wp074.b1.spawns " + string.Join(" | ", log));
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>WP-074 B1b · the v6 layout signal and the six inner gates (S-03 WP-076 B6): on the deck, on the NavMesh, a full path to the start.</summary>
        [Test]
        public void Greybox_V6Katman_OrtaKapilar_AltiInner_ZemindeVeBaslangicaUlasir()
        {
            WorldPlacementAsset placement = Open();
            Assert.AreEqual("v6", placement.layout);
            Assert.AreEqual(DD.Prototype.World.WorldLayoutKind.V6, Ap005WorldBootstrap.LayoutOf(placement));
            Assert.AreEqual(DD.Prototype.World.WorldLayoutKind.Ap005, Ap005WorldBootstrap.LayoutOf(AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset)), "AP005 ±45 düzeni");
            var expected = new List<string>();
            foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates) if (g.Kind == "inner") expected.Add(g.Id);
            Assert.AreEqual(6, expected.Count);
            Assert.AreEqual(expected, placement.innerSpawnPoints.ConvertAll(p => p.id), "orta kapı id'leri spawn-gates-v6.json ile aynı");
            Vector3 seed = Seed(placement);
            var path = new NavMeshPath();
            var fails = new List<string>();
            foreach (WorldPlacementAsset.Point p in placement.innerSpawnPoints)
            {
                if (p.position.y < -0.05f || p.position.y > 0.4f) fails.Add(p.id + " zemin yüksekliği " + p.position.y);
                if (!NavMesh.SamplePosition(p.position, out NavMeshHit h, 1f, NavMesh.AllAreas)) { fails.Add(p.id + " NavMesh dışı"); continue; }
                if (!NavMesh.CalculatePath(h.position, seed, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) fails.Add(p.id + " " + path.status);
            }

            Assert.IsEmpty(fails, string.Join("\n", fails));
            StringAssert.Contains("\"id\": \"I_HallRear\"", File.ReadAllText(GreyboxV6Builder.SpawnGatesJson));
        }

        /// <summary>WP-074 B1c · S-03's region volumes (WP-076 B6 §5.2): 7 rooms, 20 room exits, the three chokepoint runs, S-03's values.</summary>
        [Test]
        public void Greybox_BolgeHacimleri_7Oda_20Cikis_3Darbogaz_S03Degerleri()
        {
            Open();
            var rooms = new Dictionary<string, NavRegionVolume>();
            var gates = new List<NavRegionVolume>();
            var other = new List<NavRegionVolume>();
            foreach (NavRegionMarker m in Object.FindObjectsOfType<NavRegionMarker>())
            {
                m.Register();   // edit time: build the volume from the serialized fields (S-03 note), read, drop
                NavRegionVolume v = m.Volume;
                m.Unregister();
                if (v.kind == NavRegionKind.Room) rooms[v.id] = v;
                else if (v.kind == NavRegionKind.Gate) gates.Add(v);
                else other.Add(v);
            }

            Assert.AreEqual(7, rooms.Count, "R1–R6 + R7");
            Assert.AreEqual(20, gates.Count, "oda çıkışı");
            Assert.AreEqual(3, other.Count, "C8 köprü + C1 geçit + K1");
            var fails = new List<string>();
            var noAlt = new List<string>();
            var exits = new Dictionary<string, int>();
            foreach (KeyValuePair<string, NavRegionVolume> r in rooms)
                if (r.Value.capacity != GreyboxV6Builder.RoomCapacity[r.Key] || Mathf.Abs(r.Value.costMultiplier - 1f) > 1e-4f) fails.Add(r.Key + " kapasite/maliyet");
            foreach (NavRegionVolume g in gates)
            {
                if (!rooms.ContainsKey(g.roomId)) { fails.Add(g.id + " roomId '" + g.roomId + "'"); continue; }
                exits[g.roomId] = (exits.TryGetValue(g.roomId, out int e) ? e : 0) + 1;
                float width = Mathf.Max(g.size.x, g.size.z);
                bool choke = width <= GreyboxV6Builder.ChokepointWidth + 1e-3f;
                int cap = choke ? (width <= 2.2f + 1e-3f ? 2 : 3) : 0;
                if (g.capacity != cap || Mathf.Abs(g.costMultiplier - (choke ? 1.15f : 1f)) > 1e-4f) fails.Add(g.id + " " + width + " m: kapasite " + g.capacity + " maliyet " + g.costMultiplier);
                if (choke && !g.hasAlternative) noAlt.Add(g.id);
                if (!NavMesh.SamplePosition(g.center - Vector3.up * 1.2f, out _, 1.5f, NavMesh.AllAreas)) fails.Add(g.id + " NavMesh dışı");
            }

            foreach (GreyboxV6Builder.Room r in GreyboxV6Builder.Rooms)
                if (r.Walls && (!exits.TryGetValue(r.Id, out int e) || e < 2)) fails.Add(r.Id + " < 2 çıkış hacmi");
            NavRegionVolume bridge = other.Find(v => v.kind == NavRegionKind.Bridge);
            Assert.IsNotNull(bridge);
            Assert.AreEqual(2, bridge.capacity); Assert.AreEqual(1.35f, bridge.costMultiplier, 1e-4f);
            Assert.AreEqual(6, other.Find(v => v.id == "K1_WestKairo").capacity);
            Assert.AreEqual(1, other.Find(v => v.id == "C1_SteppingCauseway").capacity);
            Debug.Log("[DD-TEST] wp074.b1c.regions rooms 7 gates " + gates.Count + " chokepoints without a ≥ 3.5 m alternative within 15 m: " + (noAlt.Count == 0 ? "none" : string.Join(", ", noAlt)));
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        [Test]
        public void Greybox_Catilar_BakeDisi_RoofEtiketli_Veri18Kapi5Bolge()
        {
            Open();
            int roofs = 0;
            foreach (CameraOccluderClass c in Object.FindObjectsOfType<CameraOccluderClass>())
            {
                if (c.Kind != CameraOccluderKind.Roof) continue;
                roofs++;
                Assert.AreEqual(PrototypeLayers.CameraOccluder, c.gameObject.layer, c.name);
                Unity.AI.Navigation.NavMeshModifier m = c.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
                Assert.IsTrue(m != null && m.ignoreFromBuild, c.name + " bake içinde");
                Bounds b = c.GetComponent<Collider>().bounds;
                Assert.IsFalse(NavMesh.SamplePosition(new Vector3(b.center.x, b.max.y, b.center.z), out _, 0.3f, NavMesh.AllAreas), c.name + " üstünde NavMesh");
            }

            Assert.GreaterOrEqual(roofs, 10, "çatı sayısı");
            string gates = File.ReadAllText(GreyboxV6Builder.SpawnGatesJson), regions = File.ReadAllText(GreyboxV6Builder.RegionsJson), acoustic = File.ReadAllText(GreyboxV6Builder.AcousticJson);
            Assert.AreEqual(18, CountOf(gates, "\"kind\":"), "18 kapı");
            Assert.AreEqual(6, CountOf(gates, "\"kind\": \"inner\""), "6 orta kapı");
            Assert.AreEqual(5, CountOf(regions, "\"polygonXZ\":"), "5 bölge");
            Assert.AreEqual(GreyboxV6Builder.Rooms.Length + GreyboxV6Builder.Corridors.Length, CountOf(acoustic, "\"enclosure\":"), "akustik alan");
            StringAssert.Contains("\"enclosure\": \"Hall\"", acoustic);
        }

        /// <summary>
        /// WP-074 B5b-1 · The AP-023 architecture shell (WorldV5Dress). Every dress piece is an AP-023 library prefab. The hall
        /// carries the whole kit recipe with five open bays (3 front + E + W, on R1's doors). The ring has 4 corners + 4 × 27 tsuiji.
        /// No greybox box is left where the kit stands. Every R1 door is a passage on the NavMesh: engawa → inside in under 4 m.
        /// The hall roof fades as one (collider on CameraOccluder, the upper roof and mokoshi under it).
        /// </summary>
        [Test]
        public void Greybox_V5Giydirme_AP023Kabugu_KutuKalmadi_KapilarGecit()
        {
            Open();
            var fails = new List<string>();
            GameObject dress = GameObject.Find(WorldV5Dress.RootName);
            Assert.IsNotNull(dress, "giydirme kökü");
            int instances = 0;
            foreach (Transform t in dress.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                instances++;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if (path == null || !path.Contains("/AP-023/")) fails.Add("AP-023 dışı parça: " + t.name + " " + path);
            }

            int open = 0;
            List<WorldV5Dress.KitPlacement> hall = WorldV5Dress.HallLayout((side, s, half) =>
            {
                bool o = WorldV5Dress.R1DoorOpens(side, s, half);
                if (o) open++;
                return o;
            });
            Assert.AreEqual(5, open, "açık bölme: 3 ön + D + B");
            Transform hallRoot = dress.transform.Find("Hall_R1");
            Assert.IsNotNull(hallRoot);
            int hallInstances = 0;
            foreach (Transform t in hallRoot.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) hallInstances++;
            Assert.AreEqual(hall.Count, hallInstances, "salon tarifi");
            Assert.AreEqual(4 + 4 * 27, dress.transform.Find("RingWall").childCount, "halka tsuiji");
            Debug.Log("[DD-TEST] wp074.b5b1.dress instances " + instances + " hall " + hallInstances + " open " + open);

            foreach (Transform t in GameObject.Find(GreyboxV6Builder.GreyboxRootName).transform)
                foreach (string prefix in WorldV5Dress.ReplacedPrefixes)
                    if (t.name.StartsWith(prefix, System.StringComparison.Ordinal)) fails.Add("kutu kaldı: " + t.name);

            GreyboxV6Builder.Room r1 = GreyboxV6Builder.Rooms[0];
            var navPath = new NavMeshPath();
            foreach (GreyboxV6Builder.Door d in r1.Doors)
            {
                Vector3 wall = d.Side == 'S' ? new Vector3(d.At, 0f, r1.Z0) : d.Side == 'E' ? new Vector3(r1.X1, 0f, d.At) : new Vector3(r1.X0, 0f, d.At);
                Vector3 inward = d.Side == 'S' ? Vector3.forward : d.Side == 'E' ? Vector3.left : Vector3.right;
                Vector3 outside = wall - inward * 0.8f + Vector3.up * 1.4f, inside = wall + inward * 1.0f + Vector3.up * 1.4f;
                if (!NavMesh.SamplePosition(outside, out NavMeshHit a, 0.5f, NavMesh.AllAreas) || !NavMesh.SamplePosition(inside, out NavMeshHit b, 0.5f, NavMesh.AllAreas))
                {
                    fails.Add("R1 kapı " + d.Side + " " + d.At + ": iki yanında NavMesh yok");
                    continue;
                }

                if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, navPath) || navPath.status != NavMeshPathStatus.PathComplete || PathLength(navPath) > 4f)
                    fails.Add("R1 kapı " + d.Side + " " + d.At + ": geçit değil (" + navPath.status + ", " + PathLength(navPath).ToString("F1") + " m)");
            }

            Transform roof = hallRoot.Find(WorldV5Dress.HallRoofName);
            Assert.IsNotNull(roof);
            Assert.AreEqual(PrototypeLayers.CameraOccluder, roof.gameObject.layer);
            Assert.AreEqual(CameraOccluderKind.Roof, roof.GetComponent<CameraOccluderClass>().Kind);
            Assert.Greater(roof.GetComponentsInChildren<Renderer>(true).Length, 60, "üst çatı + mokoshi parçaları çatı tutucusunda");

            // B5b-2 · the slate plaza: every variant and every quarter turn in use, each sub-mesh on DD/GroundTiles with its set's three
            // maps, 120 kerbs on the open edges (32 + 28 + 28 + 12 + 12 + 4 + 4 m), the four v005 planters in place of the boxes.
            Transform plaza = dress.transform.Find(WorldV5Ground.PlazaName);
            Assert.IsNotNull(plaza, "meydan");
            Mesh mesh = plaza.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(WorldV5Ground.SlateSets.Length, mesh.subMeshCount);
            var variants = new HashSet<int>();
            var quarters = new HashSet<int>();
            foreach (Vector4 r in WorldV5Ground.PlazaRects)
                for (int cx = Mathf.FloorToInt(r.x / WorldV5Ground.Cell); cx * WorldV5Ground.Cell < r.z; cx++)
                for (int cz = Mathf.FloorToInt(r.y / WorldV5Ground.Cell); cz * WorldV5Ground.Cell < r.w; cz++)
                {
                    (int v, int q) = WorldV5Ground.CellPick(cx, cz, WorldV5Ground.SlateSets.Length);
                    variants.Add(v);
                    quarters.Add(q);
                }

            Assert.AreEqual(3, variants.Count, "üç varyant");
            Assert.AreEqual(4, quarters.Count, "dört çeyrek dönüş");
            Material[] mats = plaza.GetComponent<MeshRenderer>().sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Assert.Greater(mesh.GetTriangles(i).Length, 0, "varyant " + i + " hücresiz");
                Assert.AreEqual(WorldV5Ground.ShaderName, mats[i].shader.name, mats[i].name);
                foreach (string prop in new[] { "_BaseMap", "_NormalMap", "_MaskMap" })
                    StringAssert.Contains(WorldV5Ground.SlateSets[i], AssetDatabase.GetAssetPath(mats[i].GetTexture(prop)), mats[i].name + " " + prop);
            }

            Assert.AreEqual(120, dress.transform.Find(WorldV5Ground.KerbGroupName).childCount, "bordür");
            Assert.AreEqual(WorldV5Ground.PlazaRects.Length, dress.transform.Find(WorldV5Ground.ProbeGroupName).GetComponentsInChildren<ReflectionProbe>().Length, "meydan yansıma probu");
            int planters = 0;
            foreach (Transform t in dress.transform.Find("CentrePlanting"))
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject).EndsWith("SM_planter_V07.prefab", System.StringComparison.Ordinal)) planters++;
            Assert.AreEqual(4, planters, "v005 saksı");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        private static int CountOf(string s, string token)
        {
            int n = 0;
            for (int i = s.IndexOf(token, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(token, i + token.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
