using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Navigation;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-074 B1 · The World v5 greybox (GreyboxV6Builder, WP-074 direction §0 K1–K5, Ek B §2 / §6): a ±55 m ring with its
    /// runtime WorldBounds and the NavMesh 0.5 m inside it, one NavMesh piece where bodies go (no body-reachable unlinked island),
    /// the twelve spawn gates, twelve weapon points and every room reaching the start, spawn gates ≥ 30 m of path from it, every
    /// room with ≥ 2 exits, roofs out of the bake and tagged Roof, and the stakeholder data (22 gates, 5 regions, acoustic spaces).
    /// </summary>
    public sealed class GreyboxV6Tests
    {
        private static WorldPlacementAsset Open()
        {
            // WP-092 A: the world as the builder made it — World_FPTPOnly's units folded back in place (WorldFptpSplit.OpenMerged)
            WorldFptpSplit.OpenMerged();
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
        public void Greybox_KopruVeGecit_NavMeshKesintisiz_CitKesmez()
        {
            // WP-075 (C-00 28.09): the zombies held at the C1 slab joint (z −23.2) and the C8 plank ends — the pond NotWalkable strips
            // carved the 0.15–0.16 m decks, links bridged the holes and a pond rim fenced the causeway. The decks bake whole now.
            // WP-074 B9 (M-39): the walks, the rim probe (the full deck width) and the dress colliders all come from GreyboxV6Builder.Crossings.
            Open();
            foreach ((int index, float top, float inset, string name) in new[]
            {
                (GreyboxV6Builder.C1Causeway, 0.2f, 0.4f, "C1 geçidi"),
                (GreyboxV6Builder.C8Bridge, 0.35f, 0.3f, "C8 köprüsü"),
            })
            {
                GreyboxV6Builder.Crossing crossing = GreyboxV6Builder.Crossings[index];
                var a = new Vector3(crossing.Centre.x, top, crossing.Min.y + inset);
                var b = new Vector3(crossing.Centre.x, top, crossing.Max.y - inset);
                Assert.IsTrue(NavMesh.SamplePosition(a, out NavMeshHit ha, 0.6f, NavMesh.AllAreas), name + " başı NavMesh dışı");
                Assert.IsTrue(NavMesh.SamplePosition(b, out NavMeshHit hb, 0.6f, NavMesh.AllAreas), name + " sonu NavMesh dışı");
                bool blocked = NavMesh.Raycast(ha.position, hb.position, out NavMeshHit edge, NavMesh.AllAreas);
                Assert.IsFalse(blocked, name + " NavMesh'i kesik: " + edge.position.ToString("F2"));
                Collider[] rims = Physics.OverlapBox((a + b) * 0.5f + Vector3.up * 0.4f, new Vector3(crossing.Width * 0.5f, 0.3f, Vector3.Distance(a, b) * 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                foreach (Collider c in rims)
                    Assert.IsFalse(c.transform.parent != null && c.transform.parent.name == WorldBoundaryBuilder.PondRimsName, name + " üstünde havuz çiti: " + c.name);
            }

            foreach (NavMeshModifierVolume v in Object.FindObjectsOfType<NavMeshModifierVolume>())
                if (v.name.Contains("_NotWalkable_"))
                    Assert.IsFalse(GreyboxV6Builder.InPondStripExclusion(new Vector2(v.transform.position.x, v.transform.position.z)), "güverte altında NotWalkable: " + v.name);

            // The dress's kit crossings: no pond NotWalkable strip lies under any walkable collider of theirs (the strips skip the
            // Crossings footprints — one source; the class is the AP-023 manifest's, COL_i in manifest shell order). The kit landings
            // overhang the 3 m deck by 0.2 m a side on land (x 2.29…5.71 at C8, the S-03 C8 funnel, M-40) and the rails stand 0.1 m
            // outside it: what must hold is that nothing walked on is carved, not that every collider fits the deck rectangle.
            Transform bridges = GameObject.Find(WorldV5Dress.RootName).transform.Find(WorldV5Dress.BridgeGroupName);
            Assert.IsNotNull(bridges, "Bridges grubu");
            var strips = new List<NavMeshModifierVolume>();
            foreach (NavMeshModifierVolume v in Object.FindObjectsOfType<NavMeshModifierVolume>())
                if (v.name.Contains("_NotWalkable_")) strips.Add(v);
            Assert.Greater(strips.Count, 0, "havuz şeridi yok; tarama yalanlanmıştır");
            int pieces = 0, walkable = 0;
            foreach (Transform piece in bridges)
            {
                pieces++;
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(piece.gameObject);
                HashSet<int> walk = WalkableColliders(source != null ? source.name : piece.name);
                foreach (Collider c in piece.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger || !c.name.StartsWith("COL_") || !int.TryParse(c.name.Substring(4), out int index) || !walk.Contains(index)) continue;
                    walkable++;
                    Bounds cb = c.bounds;
                    foreach (NavMeshModifierVolume v in strips)
                        Assert.IsFalse(StripOverlaps(v, cb), piece.name + "/" + c.name + " altında havuz şeridi: " + v.name);
                }
            }

            Assert.AreEqual(WorldV5Dress.BridgePieceCount, pieces, "C8 iki kalas açıklığı + C1 " + WorldV5Dress.C1SlabCount + " taş");
            Assert.Greater(walkable, 0, "yürünür çarpıştırıcı yok; tarama yalanlanmıştır");
        }

        /// <summary>The COL_i indices of an AP-023 prefab (SM_&lt;family&gt;_V&lt;nn&gt;) whose manifest collider class is walkable — one
        /// COL per shell, in manifest order (ArtLibraryPrefabBuilder).</summary>
        private static HashSet<int> WalkableColliders(string prefab)
        {
            ArtLibraryManifest manifest = ArtLibraryManifest.FromJson(File.ReadAllText(ArtLibraryPaths.ManifestJson("AP-023")));
            var set = new HashSet<int>();
            foreach (ArtLibraryManifest.Family f in manifest.families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                if (v.prefab != prefab) continue;
                int index = 0;
                foreach (ArtLibraryManifest.Collider c in v.colliders)
                {
                    int shells = c.shellBoxes != null && c.shellBoxes.Count > 0 ? c.shellBoxes.Count : c.shells;
                    for (int s = 0; s < shells; s++, index++)
                        if (c.@class == "walkable") set.Add(index);
                }
            }

            return set;
        }

        /// <summary>2D (XZ) separating-axis test of a NotWalkable strip (its yawed box) against a collider's world AABB.</summary>
        private static bool StripOverlaps(NavMeshModifierVolume v, Bounds b)
        {
            Vector3 c = v.transform.TransformPoint(v.center);
            float yaw = v.transform.eulerAngles.y * Mathf.Deg2Rad;
            var ax = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));   // local x in world XZ
            var az = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));    // local z in world XZ
            Vector2 hs = new Vector2(v.size.x * v.transform.lossyScale.x, v.size.z * v.transform.lossyScale.z) * 0.5f;
            var centre = new Vector2(c.x, c.z);
            var boxCentre = new Vector2(b.center.x, b.center.z);
            var boxHalf = new Vector2(b.extents.x, b.extents.z);
            foreach (Vector2 axis in new[] { Vector2.right, Vector2.up, ax, az })
            {
                float stripR = hs.x * Mathf.Abs(Vector2.Dot(ax, axis)) + hs.y * Mathf.Abs(Vector2.Dot(az, axis));
                float boxR = boxHalf.x * Mathf.Abs(axis.x) + boxHalf.y * Mathf.Abs(axis.y);
                if (Mathf.Abs(Vector2.Dot(centre - boxCentre, axis)) > stripR + boxR) return false;
            }

            return true;
        }

        /// <summary>
        /// WP-074 B9 (Y7, C-00 30.09 decision B) · Builder ↔ scene parity: the dress's garden trees are exactly what the builder puts on
        /// its garden spots (<see cref="GreyboxV6Builder.GardenTreeSpots"/> + the great sakura) — one to one, and per tree the family,
        /// variation, position (±1 cm), yaw (≤ 0.5°) and scale (±0.01) of <see cref="WorldV5Dress.GardenTreeAt"/>: the accepted pins, the
        /// position hash on an unpinned spot. No hand-edited tree left, missing or re-dressed.
        /// </summary>
        [Test]
        public void Greybox_BahceAgaclari_BuilderNoktalarinaBireBir()
        {
            Open();
            Transform garden = GameObject.Find(WorldV5Dress.RootName).transform.Find(WorldV5Dress.GardenGroupName);
            Assert.IsNotNull(garden, "GardenTrees grubu");
            var spots = new List<(Vector2 position, bool great)> { (GreyboxV6Builder.GreatSakura, true) };
            foreach (var (_, position, _) in GreyboxV6Builder.GardenTreeSpots()) spots.Add((position, false));
            var trees = new List<Transform>();
            foreach (Transform tree in garden) trees.Add(tree);
            Assert.AreEqual(spots.Count, trees.Count, "ağaç sayısı builder noktalarıyla aynı olmalı");
            var errors = new List<string>();
            int pinned = 0;
            foreach ((Vector2 spot, bool great) in spots)
            {
                int hit = trees.FindIndex(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), spot) <= 0.01f);
                if (hit < 0) { errors.Add("builder noktasında ağaç yok: " + spot.ToString("F2")); continue; }
                Transform tree = trees[hit];
                trees.RemoveAt(hit);
                var expected = WorldV5Dress.GardenTreeAt(new Vector3(spot.x, 0f, spot.y), great);
                if (System.Array.Exists(WorldV5Dress.GardenPins, g => Vector2.Distance(new Vector2(g.Position.x, g.Position.z), spot) < 0.01f)) pinned++;
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(tree.gameObject);
                string name = "SM_" + expected.family + "_V" + expected.variation.ToString("00");
                if (source == null || source.name != name) errors.Add(spot.ToString("F2") + ": " + (source != null ? source.name : tree.name) + " (beklenen " + name + ")");
                if (Vector3.Distance(tree.position, expected.position) > 0.01f) errors.Add(spot.ToString("F2") + ": konum " + tree.position.ToString("F3") + " (beklenen " + expected.position.ToString("F3") + ")");
                if (Quaternion.Angle(tree.rotation, expected.rotation) > 0.5f) errors.Add(spot.ToString("F2") + ": yaw farkı " + Quaternion.Angle(tree.rotation, expected.rotation).ToString("F2") + "°");
                if ((tree.localScale - expected.scale).magnitude > 0.01f) errors.Add(spot.ToString("F2") + ": ölçek " + tree.localScale.ToString("F3") + " (beklenen " + expected.scale.ToString("F3") + ")");
            }

            foreach (Transform t in trees) errors.Add("builder'da olmayan ağaç: " + t.position.ToString("F2"));
            Assert.AreEqual(WorldV5Dress.GardenPins.Length, pinned, "her sabit (pin) bir builder noktasında olmalı");
            Assert.IsEmpty(errors, string.Join("\n", errors));
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

            foreach (GreyboxV6Builder.Room room in GreyboxV6Builder.AllRooms())
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
            Assert.AreEqual(10, expected.Count, "6 + S-03 senaryo C'nin 4 orta kapısı (C-00 25.09)");
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
        public void Greybox_BolgeHacimleri_13Oda_33Cikis_3Darbogaz_S03Degerleri()
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

            // WP-092: the layout plan's buildings join the rooms (R1–R6 + R7 had 7 rooms and 20 exits before it)
            List<GreyboxV6Builder.Room> all = GreyboxV6Builder.AllRooms();
            int doors = 0;
            foreach (GreyboxV6Builder.Room r in all) doors += r.Doors != null ? r.Doors.Length : 0;
            Assert.AreEqual(all.Count + 1, rooms.Count, "R1–R6 + R7 + WP-092 yapıları");
            Assert.AreEqual(doors + 4 + GreyboxV6Builder.CorridorGates.Length, gates.Count, "oda çıkışı");
            Assert.AreEqual(13, rooms.Count, "S-03 08.10 M1: 13 oda");
            Assert.AreEqual(33, gates.Count, "S-03 08.10 M1: 33 çıkış (K1_W0 ile)");
            Assert.AreEqual(3, other.Count, "C8 köprü + C1 geçit + K1");
            var fails = new List<string>();
            var noAlt = new List<string>();
            var exits = new Dictionary<string, int>();
            foreach (KeyValuePair<string, NavRegionVolume> r in rooms)
            {
                GreyboxV6Builder.Room room = all.Find(x => x.Id == r.Key);
                int want = r.Key == GreyboxV6Builder.R7Id ? GreyboxV6Builder.RoomCapacity[r.Key] : room != null ? GreyboxV6Builder.CapacityOf(room) : -1;
                if (r.Value.capacity != want || Mathf.Abs(r.Value.costMultiplier - 1f) > 1e-4f) fails.Add(r.Key + " kapasite/maliyet");
            }
            foreach (NavRegionVolume g in gates)
            {
                bool corridorGate = System.Array.Exists(GreyboxV6Builder.CorridorGates, c => c.id == g.id) && other.Exists(v => v.id == g.roomId);
                if (!rooms.ContainsKey(g.roomId) && !corridorGate) { fails.Add(g.id + " roomId '" + g.roomId + "'"); continue; }
                exits[g.roomId] = (exits.TryGetValue(g.roomId, out int e) ? e : 0) + 1;
                float width = Mathf.Max(g.size.x, g.size.z);
                bool choke = width <= GreyboxV6Builder.ChokepointWidth + 1e-3f;
                int cap = choke ? (width <= 2.2f + 1e-3f ? 2 : 3) : 0;
                if (g.capacity != cap || Mathf.Abs(g.costMultiplier - (choke ? 1.15f : 1f)) > 1e-4f) fails.Add(g.id + " " + width + " m: kapasite " + g.capacity + " maliyet " + g.costMultiplier);
                if (choke && !g.hasAlternative) noAlt.Add(g.id);
                if (!NavMesh.SamplePosition(g.center - Vector3.up * 1.2f, out _, 1.5f, NavMesh.AllAreas)) fails.Add(g.id + " NavMesh dışı");
            }

            foreach (GreyboxV6Builder.Room r in all)
                if (r.Walls && (!exits.TryGetValue(r.Id, out int e) || e < 2)) fails.Add(r.Id + " < 2 çıkış hacmi");
            NavRegionVolume bridge = other.Find(v => v.kind == NavRegionKind.Bridge);
            Assert.IsNotNull(bridge);
            Assert.AreEqual(2, bridge.capacity); Assert.AreEqual(1.35f, bridge.costMultiplier, 1e-4f);
            Assert.AreEqual(6, other.Find(v => v.id == "K1_WestKairo").capacity);
            Assert.AreEqual(1, other.Find(v => v.id == "C1_SteppingCauseway").capacity);
            // WP-088 r4 (C-00 06.10, option A): C1 stays 1.3 — on the live V6 router the deck already takes 10/24 of a north-bank
            // crowd and the shore 14/24 (V6CrossingShareTests), so the deck cost is pinned rather than lowered
            Assert.AreEqual(1.3f, other.Find(v => v.id == "C1_SteppingCauseway").costMultiplier, 1e-4f, "WP-088 r4: C1 deck cost 1.3");
            Debug.Log("[DD-TEST] wp074.b1c.regions rooms 7 gates " + gates.Count + " chokepoints without a ≥ 3.5 m alternative within 15 m: " + (noAlt.Count == 0 ? "none" : string.Join(", ", noAlt)));
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>
        /// WP-088 (R2) · The pond crossings join shore to shore, and each carries a shore alternative S-03's router can send a crowd
        /// along: both deck ends land on the NavMesh past the rim and the direct path goes over the deck (C1 ended in the water at
        /// z −33 before); the marker's alternative is on the NavMesh and north end → alternative → south end stays off both decks.
        /// </summary>
        [Test]
        public void Greybox_Gecitler_KiyidanKiyiya_KiyiAlternatifiDestenGecmez()
        {
            Open();
            var volumes = new Dictionary<string, NavRegionVolume>();
            foreach (NavRegionMarker m in Object.FindObjectsOfType<NavRegionMarker>())
            {
                m.Register();
                volumes[m.Volume.id] = m.Volume;
                m.Unregister();
            }

            var decks = new List<GreyboxV6Builder.Crossing>(GreyboxV6Builder.Crossings);
            var fails = new List<string>();
            foreach (int index in new[] { GreyboxV6Builder.C8Bridge, GreyboxV6Builder.C1Causeway })
            {
                GreyboxV6Builder.Crossing c = GreyboxV6Builder.Crossings[index];
                if (!NavMesh.SamplePosition(new Vector3(c.Centre.x, 0.2f, c.Max.y + 1.5f), out NavMeshHit north, 0.6f, NavMesh.AllAreas)) { fails.Add(c.Id + ": kuzey ucu karada değil"); continue; }
                if (!NavMesh.SamplePosition(new Vector3(c.Centre.x, 0.2f, c.Min.y - 1.5f), out NavMeshHit south, 0.6f, NavMesh.AllAreas)) { fails.Add(c.Id + ": güney ucu karada değil"); continue; }
                float direct = PathLength(north.position, south.position);
                if (direct < 0f || direct > c.Length + 6f) fails.Add(c.Id + ": doğrudan yol güverteden geçmiyor (" + direct.ToString("F1") + " m, güverte " + c.Length + " m)");
                if (!volumes.TryGetValue(c.Id, out NavRegionVolume v) || !v.hasAlternative) { fails.Add(c.Id + ": kıyı alternatifi yok"); continue; }
                if (!NavMesh.SamplePosition(v.alternative, out NavMeshHit alt, 1f, NavMesh.AllAreas)) { fails.Add(c.Id + ": alternatif NavMesh dışı " + v.alternative); continue; }
                float a = PathLengthOffDecks(north.position, alt.position, decks), b = PathLengthOffDecks(alt.position, south.position, decks);
                if (a < 0f || b < 0f) fails.Add(c.Id + ": alternatif yolu bir güverteden geçiyor ya da kopuk");
                else Debug.Log("[DD-TEST] wp088." + c.Id + " direct " + direct.ToString("F1") + " m, shore " + (a + b).ToString("F1") + " m via " + v.alternative);

                // WP-088 r2 (V6 Waterfall stall): the router offers this marker's detour only while it can help — bank to bank yes;
                // the goal or the body on the deck, or a body 1 m past the waypoint, no (RegionNavRouter.AlternativeAhead).
                float margin = RegionNavProfile.CreateDefault().AlternativeAheadMargin;
                Vector3 deck = new Vector3(c.Centre.x, 0.2f, c.Centre.y), pastVia = v.alternative + (deck - v.alternative).normalized;
                if (!RegionNavRouter.AlternativeAhead(v, north.position, south.position, margin)) fails.Add(c.Id + ": kıyıdan kıyıya kıyı rotası sunulmuyor");
                if (RegionNavRouter.AlternativeAhead(v, north.position, deck, margin)) fails.Add(c.Id + ": hedef güvertedeyken dolambaç sunuluyor");
                if (RegionNavRouter.AlternativeAhead(v, deck, south.position, margin)) fails.Add(c.Id + ": gövde güvertedeyken dolambaç sunuluyor");
                if (RegionNavRouter.AlternativeAhead(v, pastVia, south.position, margin)) fails.Add(c.Id + ": via'ya varmış gövdeye yeniden via sunuluyor");
            }

            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        private static float PathLength(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete ? PathLength(path) : -1f;
        }

        private static float PathLengthOffDecks(Vector3 from, Vector3 to, List<GreyboxV6Builder.Crossing> decks)
        {
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
            {
                for (int s = 0; s <= 20; s++)
                {
                    Vector3 m = Vector3.Lerp(path.corners[i - 1], path.corners[i], s / 20f);
                    foreach (GreyboxV6Builder.Crossing d in decks)
                        if (m.x >= d.Min.x && m.x <= d.Max.x && m.z >= d.Min.y && m.z <= d.Max.y) return -1f;
                }

                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            return length;
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
            Assert.AreEqual(22, CountOf(gates, "\"kind\":"), "22 kapı (S-03 senaryo C, C-00 25.09)");
            Assert.AreEqual(10, CountOf(gates, "\"kind\": \"inner\""), "10 orta kapı");
            Assert.AreEqual(5, CountOf(regions, "\"polygonXZ\":"), "5 bölge");
            Assert.AreEqual(GreyboxV6Builder.AllRooms().Count + GreyboxV6Builder.Corridors.Length + GreyboxV6Builder.AcousticExtras.Length, CountOf(acoustic, "\"enclosure\":"), "akustik alan");
            StringAssert.Contains("\"enclosure\": \"Hall\"", acoustic);
        }

        /// <summary>
        /// WP-097 / WP-092 (S-08, C-00 07.10) · The acoustic spaces: every room's enclosure is the builder's rule (a closed room of 20–90 m²
        /// under ≤ 4 m is a Room — the pavilion, the kura and the tea house among them), and every top-down roof inside the ring sits over an
        /// acoustic space (S-08's coverage gate: a new roof cannot reach main without its sound space).
        /// </summary>
        [Test]
        public void Greybox_Akustik_OdaSinifi_HerCatiBirAlanin_Ustunde()
        {
            Open();
            Dictionary<string, object> json = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(GreyboxV6Builder.AcousticJson)), "acoustic");
            var polygons = new List<List<Vector2>>();
            var enclosure = new Dictionary<string, string>();
            foreach (object o in ArtJson.AsList(ArtJson.Require(json, "spaces"), "spaces"))
            {
                Dictionary<string, object> space = ArtJson.AsObject(o, "space");
                enclosure[ArtJson.AsString(ArtJson.Require(space, "id"))] = ArtJson.AsString(ArtJson.Require(space, "enclosure"));
                object flat = ArtJson.Get(space, "polygonXZ");
                if (flat != null) polygons.Add(Points(ArtJson.AsList(flat)));
                object segments = ArtJson.Get(space, "segmentsXZ");
                if (segments != null) foreach (object quad in ArtJson.AsList(segments)) polygons.Add(Points(ArtJson.AsList(quad)));
            }

            var fails = new List<string>();
            foreach (GreyboxV6Builder.Room room in GreyboxV6Builder.AllRooms())
                if (!enclosure.TryGetValue(room.Id, out string e) || e != GreyboxV6Builder.AcousticClassOf(room)) fails.Add(room.Id + " enclosure " + e);
            foreach (string id in new[] { "R2_EastPavilion", "R3_Kura", "R4_TeaHouse" })
                if (!enclosure.TryGetValue(id, out string e) || e != "Room") fails.Add(id + " Room değil: " + e);
            int roofs = 0;
            foreach (CameraOccluderClass c in Object.FindObjectsOfType<CameraOccluderClass>(true))
            {
                if (c.Kind != CameraOccluderKind.Roof) continue;
                Vector3 centre = c.GetComponent<Collider>().bounds.center;
                if (Mathf.Abs(centre.x) > GreyboxV6Builder.HalfExtent || Mathf.Abs(centre.z) > GreyboxV6Builder.HalfExtent) continue;
                roofs++;
                var p = new Vector2(centre.x, centre.z);
                if (!polygons.Exists(poly => Inside(poly, p))) fails.Add(c.transform.root.name + "/…/" + c.transform.parent.name + "/" + c.name + " (" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ") hiçbir akustik alanda değil");
            }

            Debug.Log("[DD-TEST] wp092.acoustic spaces " + enclosure.Count + " roofs " + roofs);
            Assert.Greater(roofs, 10, "çatı");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        private static List<Vector2> Points(List<object> flat)
        {
            var list = new List<Vector2>();
            for (int i = 0; i + 1 < flat.Count; i += 2) list.Add(new Vector2(ArtJson.AsFloat(flat[i]), ArtJson.AsFloat(flat[i + 1])));
            return list;
        }

        private static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        /// <summary>
        /// WP-092 B · The layout plan (WorldV5Layout, layout-v6.json) in the world: every building's shell fades as one (one fade state over its
        /// bays, roofs and camera-side wall); no collider top of a plan piece in the banned 0.31–0.49 m band; every lit lamp carries a baked
        /// point light in the AD's 2400–2700 K range at its socket; every piece of the plan is in the project (none skipped).
        /// </summary>
        [Test]
        public void Greybox_Yerlesim_YapilarSoner_YasakTepeYok_LambalarPisirilir()
        {
            Open();
            WorldV5Layout.Plan plan = WorldV5Layout.Load();
            Assert.IsNotNull(plan, WorldV5Layout.LayoutJson);
            GameObject root = GameObject.Find(WorldV5Layout.RootName);
            Assert.IsNotNull(root, WorldV5Layout.RootName);
            var fails = new List<string>();
            foreach (WorldV5Layout.Building b in plan.Buildings)
            {
                if (b.IsExisting) continue;
                Transform shell = root.transform.Find(WorldV5Layout.BuildingGroupName + "/" + b.Id + WorldV5Layout.ShellSuffix);
                if (shell == null) { fails.Add(b.Id + " kabuk yok"); continue; }
                if (shell.GetComponent<OccluderFadeState>() == null) fails.Add(b.Id + " kabukta solma durumu yok");
                foreach (OccluderFadeState inner in shell.GetComponentsInChildren<OccluderFadeState>(true))
                    if (inner.transform != shell) fails.Add(b.Id + " iç solma durumu " + inner.name);
                if (b.SouthWall == "fade" && shell.Find(WorldV5Layout.WallOccluderName) == null) fails.Add(b.Id + " güney cephe solmuyor");
            }

            foreach (WorldV5Layout.Item item in plan.Items)
            {
                string prefab = ArtLibraryPaths.Prefab(item.Source, item.Family, "SM_" + item.Family + "_V" + item.Variation.ToString("00"));
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null) fails.Add(item.Id + " " + prefab + " yok");
            }

            foreach (WorldV5Layout.Lamp lamp in plan.Lamps)
            {
                string prefab = ArtLibraryPaths.Prefab(lamp.Source, lamp.Family, "SM_" + lamp.Family + "_V" + lamp.Variation.ToString("00"));
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null) fails.Add(lamp.Id + " " + prefab + " yok");
            }

            foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
            {
                if (c.gameObject.layer == PrototypeLayers.CameraOccluder) continue;
                GameObject owner = PrefabUtility.GetOutermostPrefabInstanceRoot(c.gameObject);
                if (owner == null) continue;
                float rise = c.bounds.max.y - owner.transform.position.y;
                if (rise > WorldV5Layout.BannedTopMin && rise < WorldV5Layout.BannedTopMax && c.bounds.size.y > 0.05f) fails.Add(owner.name + "/" + c.name + " tepe " + rise.ToString("0.00") + " m");
            }

            int lit = 0;
            Color warm = Mathf.CorrelatedColorTemperatureToRGB(WorldV5Layout.MaxKelvin), hot = Mathf.CorrelatedColorTemperatureToRGB(WorldV5Layout.MinKelvin);   // AD AP-030: 2400–2700 K
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                lit++;
                if (light.lightmapBakeType != LightmapBakeType.Baked) fails.Add(light.transform.parent.name + " pişirilmiyor");
                if (light.color.b > warm.b + 0.02f || light.color.b < hot.b - 0.02f) fails.Add(light.transform.parent.name + " renk sıcaklığı dışında");
            }

            // S-07 08.10: what LanternFlames moves (hanging lamps, the floating tōrō) is not static-batched.
            var swinging = new HashSet<string>();
            foreach (WorldV5Layout.Lamp lamp in plan.Lamps) if (WorldV5Layout.Swings(lamp)) swinging.Add(WorldV5Layout.LampPrefix + lamp.Id + "_");
            int moving = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                bool lamp = false;
                foreach (string prefix in swinging) if (t.name.StartsWith(prefix, System.StringComparison.Ordinal)) lamp = true;
                bool toro = t.parent != null && System.Array.IndexOf(WorldV5Layout.MovingGroups, t.parent.name) >= 0;
                if (!lamp && !toro) continue;
                moving++;
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                    if ((GameObjectUtility.GetStaticEditorFlags(c.gameObject) & StaticEditorFlags.BatchingStatic) != 0) { fails.Add(t.name + " statik batch'te (sallanamaz)"); break; }
            }

            Assert.Greater(moving, 20, "sallanan lambalar + tōrō");

            int wantLit = 0;
            foreach (WorldV5Layout.Lamp lamp in plan.Lamps) if (lamp.Light != "emissive" && lamp.Lit) wantLit++;
            // S-03 08.10 (b)4: every kit door (the nagaya doors, K1 west) has no collider at its lintel — the Executioner (2.66 m) walks under.
            int doorsChecked = 0;
            foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
            {
                GameObject src = PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) ? PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) : null;
                if (src == null || !src.name.StartsWith("SM_" + WorldV5Dress.KairoDoorFamily + "_V", System.StringComparison.Ordinal)) continue;
                doorsChecked++;
                foreach (Collider c in t.GetComponentsInChildren<Collider>(false))
                {
                    float bottom = c.bounds.min.y - t.position.y;
                    if (c.enabled && bottom > WorldV5Dress.LintelBottomMin && bottom < WorldV5Dress.LintelBottomMax) fails.Add(t.name + " lento kollideri açık (" + bottom.ToString("0.00") + " m)");
                }
            }

            Assert.GreaterOrEqual(doorsChecked, 7, "kit kapıları (B1–B6 + K1 batı)");
            Debug.Log("[DD-TEST] wp092.layout buildings " + plan.Buildings.Count + " items " + plan.Items.Count + " lamps " + plan.Lamps.Count + " lit " + lit);
            Assert.AreEqual(wantLit, lit, "pişirilen lamba ışığı");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>
        /// WP-092 · The baked probes reach the world: every world renderer but the ground, the water and the camera/sky sheets blends the light
        /// and reflection probes (the AP-023 kit prefabs came with both off, so the bake never lit the dress); the ground and water stay off.
        /// </summary>
        [Test]
        public void Greybox_ProbeKarisimi_DunyaBlend_ZeminSuKapali()
        {
            Open();
            GameObject world = GameObject.Find(Ap005WorldBootstrap.RootName);
            Assert.IsNotNull(world);
            int blend = 0;
            var fails = new List<string>();
            foreach (MeshRenderer r in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (WorldV5Lighting.IsProbeOff(r.name))
                {
                    if (r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off) fails.Add(r.name + " zemin/su probe kullanıyor");
                    continue;
                }

                if (r.sharedMaterial == null || System.Array.IndexOf(WorldV5Lighting.ProbeIgnoredShaders, r.sharedMaterial.shader.name) >= 0) continue;
                if (r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.BlendProbes) fails.Add(r.transform.parent.name + "/" + r.name + " " + r.lightProbeUsage);
                else blend++;
            }

            Debug.Log("[DD-TEST] wp092.probes blending " + blend + " wrong " + fails.Count);
            Assert.Greater(blend, 1000, "probe karışan renderer");
            Assert.IsEmpty(fails, fails.Count + " renderer:\n" + string.Join("\n", fails.GetRange(0, Mathf.Min(fails.Count, 40))));
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
            // maps and the AD values, a kerb module per metre of open edge on the wet kerb material, the four v005 planters in place of the boxes.
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
                foreach ((string name, float value) in WorldV5Ground.MaterialValues)
                    Assert.AreEqual(value, mats[i].GetFloat(name), 1e-4f, mats[i].name + " " + name);
            }

            Transform kerbs = dress.transform.Find(WorldV5Ground.KerbGroupName);
            Assert.AreEqual(WorldV5Ground.KerbCount(), kerbs.childCount, "bordür");
            foreach (Renderer r in kerbs.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    Assert.AreNotEqual(WorldV5Ground.KerbSourceMaterial, m.name, r.name + ": bordür ıslak granitte");
            Assert.AreEqual(WorldV5Ground.ProbeRects.Length, dress.transform.Find(WorldV5Ground.ProbeGroupName).GetComponentsInChildren<ReflectionProbe>().Length, "meydan yansıma probu");
            int planters = 0;
            foreach (Transform t in dress.transform.Find("CentrePlanting"))
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject).EndsWith("SM_planter_V07.prefab", System.StringComparison.Ordinal)) planters++;
            Assert.AreEqual(4, planters, "v005 saksı");

            // B5b-3 · the vista: > 900 trees, every base in the 58–84 m band (outside the ring), out of the bake; the AP-019 horizon rings.
            Transform vista = dress.transform.Find(WorldV5Dress.VistaGroupName);
            Assert.IsNotNull(vista, "vista");
            Assert.AreEqual(PrototypeLayers.Environment, vista.gameObject.layer, "modifier katmanı bake maskesinde");
            Assert.IsTrue(vista.GetComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild);
            int trees = 0;
            foreach (Transform t in vista)
            {
                float band = Mathf.Max(Mathf.Abs(t.position.x), Mathf.Abs(t.position.z));
                if (band < WorldV5Dress.VistaInner - 1e-3f || band > WorldV5Dress.VistaOuter + 1e-3f) fails.Add("vista bandı dışı: " + t.name + " " + t.position);
                trees++;
            }

            Assert.Greater(trees, 900, "vista ağaç sayısı (3,5 m ızgara)");
            Assert.IsNotNull(dress.transform.Find(ArtSurfaceImporter.SkyRootName), "ufuk halkaları");
            Debug.Log("[DD-TEST] wp074.b5b3.vista trees " + trees);

            // B5b-4 · kit kairō (K3 W / E, K6) rails off, each bay's roof fading; v007-B: kairo_angle corners on K2 / K4, R6 the kit chūmon.
            Transform kairo = dress.transform.Find(WorldV5Dress.KairoGroupName);
            Assert.IsNotNull(kairo, "kairō");
            int bays = 0, expectedBays = WorldV5Dress.BayCount();
            foreach (Transform bay in kairo)
            {
                bays++;
                foreach (Renderer r in bay.GetComponentsInChildren<Renderer>(false))
                    if (r.name.StartsWith("rail_LOD", System.StringComparison.Ordinal)) fails.Add("koran açık: " + bay.name);
                Transform occluder = bay.Find(ArtLibraryPrefabBuilder.RoofName + "/" + WorldV5Dress.RoofOccluderName);
                if (occluder == null || occluder.GetComponent<CameraOccluderClass>() == null) fails.Add("çatı soldurma yok: " + bay.name);
            }

            Assert.AreEqual(expectedBays, bays, "kairō bölmesi");
            int corners = 0;
            foreach (Transform t in kairo) if (t.name.Contains("_Corner_")) corners++;
            Assert.AreEqual(4, corners, "v007-B: kairo_angle köşeleri (K2 2 × 45°, K4 2 × 60°)");
            Assert.IsNotNull(dress.transform.Find(WorldV5Dress.ShoreGroupName + "/" + WorldV5Dress.WaterfallName), "v007-B: şelale (en büyük havuz, kavisli kıyı)");
            Assert.IsNotNull(dress.transform.Find(WorldV5Dress.ChumonName), "chūmon (v007-B: kit chūmon, × 1,2 geniş)");

            // B5b-5 · R2–R5 as kit buildings (roofs fading), the garden trunks as v005 trees; no greybox tree / room / corridor left.
            Transform buildings = dress.transform.Find(WorldV5Dress.BuildingGroupName);
            Assert.AreEqual(WorldV5Dress.KitBuildings.Length, buildings.childCount, "kit yapıları");
            foreach (Transform b in buildings)
                if (b.Find(ArtLibraryPrefabBuilder.RoofName + "/" + WorldV5Dress.RoofOccluderName) == null) fails.Add("çatı soldurma yok: " + b.name);
            Assert.Greater(dress.transform.Find(WorldV5Dress.GardenGroupName).childCount, 20, "bahçe ağaçları");

            // B5b-5 (2) · water v2 on the ponds, their shores, the kit bridges, the grove dressed over its (hidden) blocks, fences, rock garden.
            foreach (Transform t in GameObject.Find(GreyboxV6Builder.GreyboxRootName).transform)
            {
                if (t.name.StartsWith("Pond", System.StringComparison.Ordinal) && t.name.EndsWith("_Water", System.StringComparison.Ordinal))
                    Assert.AreEqual(WorldV5Dress.WaterMaterialPath, AssetDatabase.GetAssetPath(t.GetComponent<MeshRenderer>().sharedMaterial), t.name);
                if (t.name.StartsWith("Bamboo_", System.StringComparison.Ordinal))
                    Assert.IsFalse(t.GetComponent<MeshRenderer>().enabled, t.name + ": blok yalnız çarpışma");
            }

            Assert.Greater(dress.transform.Find(WorldV5Dress.ShoreGroupName).childCount, 20, "havuz kıyıları");
            Assert.AreEqual(WorldV5Dress.BridgePieceCount, dress.transform.Find(WorldV5Dress.BridgeGroupName).childCount, "2 kalas + " + WorldV5Dress.C1SlabCount + " levha köprü");
            Assert.Greater(dress.transform.Find(WorldV5Dress.GroveGroupName).childCount, 40, "bambu kümeleri + çit");
            // DEC-102 AD E: NE / SE side doors — 4 doors × (void + 3 frame pieces), visual only, on the tsuiji's inner face.
            Transform doors = dress.transform.Find(WorldV5Dress.SideDoorGroupName);
            Assert.IsNotNull(doors, "köşe yan kapıları");
            Assert.AreEqual(16, doors.childCount, "4 kapı × (boşluk + 3 çerçeve)");
            Assert.AreEqual(0, doors.GetComponentsInChildren<Collider>(true).Length, "yan kapılar yalnız görsel");
            foreach (var spot in WorldV5Dress.SideDoorSpots())
                Assert.AreEqual(WorldV5Dress.RingCentreline - 0.30f, Mathf.Max(Mathf.Abs(spot.at.x), Mathf.Abs(spot.at.y)), 1e-4f, "iç yüzde");
            Assert.Greater(dress.transform.Find(WorldV5Dress.FenceGroupName).childCount, 40, "kenninji çitleri");
            Assert.IsNotNull(dress.transform.Find(WorldV5Dress.KaresansuiGroupName + "/Karesansui_Gravel"), "karesansui çakılı");

            // B8 · the plaza's planar reflection at its surface; the mirror maps y → 2h − y; tiers off / 256 / 768 (r3), characters at Cinematic only.
            PlanarReflection planar = plaza.GetComponent<PlanarReflection>();
            Assert.IsNotNull(planar, "planar yansıma");
            Assert.AreEqual(WorldV5Ground.Lift, planar.PlaneHeight, 1e-5f);
            Matrix4x4 mirror = PlanarReflection.Reflection(new Vector4(0f, 1f, 0f, -planar.PlaneHeight));
            Vector3 p = mirror.MultiplyPoint(new Vector3(3f, 5f, -2f));
            Assert.AreEqual(3f, p.x, 1e-4f); Assert.AreEqual(2f * planar.PlaneHeight - 5f, p.y, 1e-4f); Assert.AreEqual(-2f, p.z, 1e-4f);
            Assert.AreEqual(0, PlanarReflection.HeightOf(QualityTier.Performans));
            Assert.AreEqual(256, PlanarReflection.HeightOf(QualityTier.Yuksek));
            Assert.AreEqual(768, PlanarReflection.HeightOf(QualityTier.Sinematik));
            Assert.IsTrue(PlanarReflection.CharactersIn(QualityTier.Sinematik) && !PlanarReflection.CharactersIn(QualityTier.Yuksek));

            // WP-075 C3 · the v6 layout's skybox (DEC-102 r2: sky_v3's panorama), the rings on the v2 sheets in queue order, the landmarks.
            Assert.AreEqual(DD.Prototype.World.WorldAtmosphere.SkyV3ResourceName, DD.Prototype.World.WorldAtmosphere.SkyResourceFor(DD.Prototype.World.WorldLayoutKind.V6));   // DEC-102 r2: the sky_v3 panorama
            Assert.AreEqual(DD.Prototype.World.WorldAtmosphere.SharedSkyResourceName, DD.Prototype.World.WorldAtmosphere.SkyResourceFor(DD.Prototype.World.WorldLayoutKind.Ap005));
            Assert.IsNotNull(Resources.Load<Material>(DD.Prototype.World.WorldAtmosphere.SkyV3ResourceName), "DD_Sky_v3");
            Transform rings = dress.transform.Find(ArtSurfaceImporter.SkyRootName);
            int lastQueue = 0;
            foreach ((string layer, int queue) in WorldV5Sky.PlateQueues)
            {
                Material ring = rings.Find("Horizon_" + layer).GetComponent<MeshRenderer>().sharedMaterial;
                StringAssert.Contains("horizon_" + layer + "_v2", AssetDatabase.GetAssetPath(ring.GetTexture("_BaseMap")));
                Assert.Greater(ring.renderQueue, lastQueue, layer + " sırası");
                // DEC-108 / M-44: only the near ring's fog footer reveals the panorama (its bottom edge AA with it), at the builder's height.
                float reveal = "horizon_" + layer + "_v2" == WorldV5Sky.PanoramaRevealRole ? 1f : 0f;
                Assert.AreEqual(reveal, ring.GetFloat("_BottomPanoramaReveal"), layer + " panorama açılımı (DEC-108: yalnız near)");
                Assert.AreEqual(reveal, ring.GetFloat("_BottomEdgeAA"), layer + " alt kenar AA");
                Assert.AreEqual(WorldV5Sky.PanoramaRevealHeight, ring.GetFloat("_RevealHeight"), 1e-6f, layer + " açılım yüksekliği (M-44: builder'dan)");
                lastQueue = ring.renderQueue;
            }

            Transform skyV2 = dress.transform.Find(WorldV5Sky.RootName);
            // DEC-102 r2: the cloud planes are retired (the sky_v3 panorama carries the clouds); the landmark cards and the sun disc stay.
            Assert.AreEqual(WorldV5Sky.Cards.Length + 1, skyV2.childCount, "nirengi kartı + güneş diski (DEC-102 r2: bulut düzlemi yok)");

            // WP-075 C4 / C5 · shafts (20; none within 10 m of the centre) and fog sheets (one per pond + 7), tier-cut, visual only.
            Transform shafts = dress.transform.Find(WorldV5Atmosphere.ShaftGroupName), fogSheets = dress.transform.Find(WorldV5Atmosphere.FogGroupName);
            Assert.AreEqual(WorldV5Atmosphere.Shafts.Length, shafts.childCount, "huzme kartı");
            Assert.AreEqual(3 + WorldV5Atmosphere.FogSheets.Length, fogSheets.childCount, "sis levhası (3 havuz + 7)");
            AtmosphereTierGroup shaftTier = shafts.GetComponent<AtmosphereTierGroup>(), fogTier = fogSheets.GetComponent<AtmosphereTierGroup>();
            // DEC-102: the volumetric light carries the shafts on High / Cinematic; the cards are Performance's stand-in.
            Assert.AreEqual(new[] { 6, 0, 0 }, new[] { shaftTier.CountOf(QualityTier.Performans), shaftTier.CountOf(QualityTier.Yuksek), shaftTier.CountOf(QualityTier.Sinematik) });
            Assert.AreEqual(new[] { 4, 7, 10 }, new[] { fogTier.CountOf(QualityTier.Performans), fogTier.CountOf(QualityTier.Yuksek), fogTier.CountOf(QualityTier.Sinematik) });
            foreach (Transform t in shafts)
            {
                if (new Vector2(t.position.x, t.position.z).magnitude < WorldV5Atmosphere.CentreClear) fails.Add("merkezde huzme: " + t.name);
                Assert.AreEqual("DD/ShaftCard", t.GetComponent<MeshRenderer>().sharedMaterial.shader.name);
            }

            foreach (Transform t in fogSheets)
            {
                Assert.IsNull(t.GetComponent<Collider>(), t.name + " çarpışmasız");
                Assert.LessOrEqual(t.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_PeakAlpha"), 0.12f + 1e-4f, t.name + " ≤ %12");
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>
        /// WP-074 B8 · Lighting v5 baked with the scene: the report is OK and within budget, and the scene carries its light probes and
        /// ≥ 18 baked 512 reflection probes; vegetation stays out of the lightmaps (probe-lit).
        /// </summary>
        [Test]
        public void Greybox_B8_IsikV5_BakeRaporu_ButceIcinde_ProbelarSahnede()
        {
            Open();
            Assert.IsTrue(File.Exists(WorldV5Lighting.ReportPath), "bake raporu yok (GreyboxV6Builder -ddBake)");
            var report = JsonUtility.FromJson<WorldV5Lighting.Report>(File.ReadAllText(WorldV5Lighting.ReportPath));
            Assert.IsTrue(report.ok, "bake");
            Assert.IsTrue(report.withinBudget, "bütçe (içe aktarılmış) " + report.shippedBytes / 1048576 + " MB > " + report.budgetMb);
            Assert.Greater(report.shippedBytes, 0L, "içe aktarılmış boyut ölçülmedi");
            Assert.Greater(report.lightmapCount, 0);
            Assert.IsNotNull(Object.FindObjectOfType<LightProbeGroup>(), "ışık probları");
            int baked = 0;
            foreach (ReflectionProbe probe in Object.FindObjectsOfType<ReflectionProbe>())
                if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Baked && probe.resolution == WorldV5Lighting.ReflectionResolution) baked++;
            Assert.GreaterOrEqual(baked, 18, "pişmiş 512 yansıma probu");
            foreach (Transform t in GameObject.Find(WorldV5Dress.VistaGroupName).transform)
                if (!t.name.StartsWith("SM_yagura", System.StringComparison.Ordinal))   // the distant keep is a structure (lightmapped)
                    Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.ContributeGI, t.name + " ışık haritasında");
            Debug.Log("[DD-TEST] wp074.b8.bake " + report.lightmapCount + " lightmaps " + report.shippedBytes / 1048576 + " MB imported (" + report.bytes / 1048576 + " MB EXR), probes " + report.probes + ", reflection " + report.reflectionProbes + ", " + report.bakeSeconds.ToString("F0") + " s");
        }

        /// <summary>
        /// R3 #1 (C-00 07.10 decision B) · The bake writes its probes into the root's <see cref="WorldBakedProbes"/> (the runtime binds
        /// them: the world is loaded additively and Unity never does): the same count the bake laid out, from the scene's own lighting
        /// data (or the standalone copy). AD 07.10 M1: the ground, plaza slate, rock-garden gravel and every pond's water stay off the
        /// probes (the runtime ambient, the approved Look B tone); the bamboo grove stays probe-lit.
        /// </summary>
        [Test]
        [Category("R3")]
        public void Greybox_R3_PisimisProblar_KokteReferans_ZeminVeSuProbDisi()
        {
            Open();
            var report = JsonUtility.FromJson<WorldV5Lighting.Report>(File.ReadAllText(WorldV5Lighting.ReportPath));
            var holder = GameObject.Find(Ap005WorldBootstrap.RootName).GetComponent<WorldBakedProbes>();
            Assert.IsNotNull(holder, "kökte WorldBakedProbes yok (bake sonrası WriteProbeReference)");
            Assert.IsNotNull(holder.Probes, "pişmiş prob referansı boş");
            Assert.AreEqual(report.probes, holder.Probes.count, "referanstaki prob sayısı bake'in yerleştirdiği değil");
            Assert.AreEqual(holder.Probes.count, holder.BakedCount, "BakedCount");
            Assert.AreEqual(report.probeRefCount, holder.Probes.count, "rapordaki probeRefCount");
            Assert.AreEqual(report.probeSource, holder.Source, "rapordaki probeSource");
            string path = AssetDatabase.GetAssetPath(holder.Probes);
            if (holder.Source == "lightingData")
                StringAssert.EndsWith("/LightingData.asset", path, "kaynak sahnenin kendi ışık verisi olmalı");
            else
                Assert.AreEqual(WorldV5Lighting.StandaloneProbesPath, path, "bağımsız kopya yolu");

            int off = 0, ponds = 0;
            foreach (MeshRenderer r in GameObject.Find(Ap005WorldBootstrap.RootName).GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!WorldV5Lighting.IsProbeOff(r.name)) continue;
                Assert.AreEqual(UnityEngine.Rendering.LightProbeUsage.Off, r.lightProbeUsage, r.name + ": zemin/su prob dışı kalmalı (AD M1)");
                off++;
                if (r.name.StartsWith("Pond", System.StringComparison.Ordinal)) ponds++;
            }

            Assert.GreaterOrEqual(ponds, 3, "havuz suları");
            Assert.GreaterOrEqual(off, 6, "Ground + Plaza_Slate + Karesansui_Gravel + havuzlar");
            int groveProbe = 0;
            foreach (MeshRenderer r in GameObject.Find(WorldV5Dress.GroveGroupName).GetComponentsInChildren<MeshRenderer>(true))
                if (r.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.BlendProbes) groveProbe++;
            Assert.Greater(groveProbe, 0, "bambu korusu probla aydınlanmalı");
            Debug.Log("[DD-TEST] r3.probes.ref " + holder.Probes.count + " (" + holder.Source + ") off=" + off + " groveProbe=" + groveProbe);
        }

        /// <summary>
        /// N3 (WP-081 B, menu-n2-n4 RAPOR §N3) · Each pond's water is the builder's ellipse disc (<see cref="GreyboxV6Builder.PondWaterSegments"/>
        /// rim vertices, depth ramp in R, foam band in G) instead of the 20-sided Cylinder, and the waterfall pond keeps its shore: at
        /// least five strips outside the falls' clearing, the strips and curves covering ≥ 60 % of its rim, the two curved pieces on the
        /// rim (not out on the grass).
        /// </summary>
        [Test]
        [Category("R3")]
        public void Greybox_N3_HavuzSuyu_ElipsAg_SelaleHavuzununKiyisi()
        {
            Open();
            Transform greybox = GameObject.Find(GreyboxV6Builder.GreyboxRootName).transform;
            Transform falls = null;
            var waters = new List<Transform>();
            foreach (Transform w in greybox)
            {
                if (!w.name.StartsWith("Pond", System.StringComparison.Ordinal) || !w.name.EndsWith("_Water", System.StringComparison.Ordinal)) continue;
                waters.Add(w);
                Mesh mesh = w.GetComponent<MeshFilter>().sharedMesh;
                int index = int.Parse(w.name.Substring(4, w.name.Length - 4 - "_Water".Length));
                Assert.AreEqual(GreyboxV6Builder.PondWaterMeshPath(index), AssetDatabase.GetAssetPath(mesh), w.name + ": su ağı builder'ın elipsi değil");
                int seg = GreyboxV6Builder.PondWaterSegments;
                Assert.GreaterOrEqual(seg, 96, "kenar");
                Assert.AreEqual(seg * GreyboxV6Builder.PondRingInsets.Length + 1, mesh.vertexCount, w.name + ": köşe sayısı");
                Color[] colors = mesh.colors;
                Assert.AreEqual(mesh.vertexCount, colors.Length, w.name + ": vertex rengi yok");
                for (int i = 0; i < seg; i++)
                {
                    Assert.AreEqual(0f, colors[i].r, 1e-4f, w.name + ": kıyıda derinlik 0");
                    Assert.AreEqual(1f, colors[i].g, 1e-4f, w.name + ": kıyıda köpük bandı");
                }

                Assert.AreEqual(1f, colors[mesh.vertexCount - 1].r, 1e-4f, w.name + ": merkez derin");
                Assert.IsNull(w.GetComponent<Collider>(), w.name + ": su ağının çarpıştırıcısı yok");
                if (falls == null || w.localScale.x * w.localScale.z > falls.localScale.x * falls.localScale.z) falls = w;
            }

            Assert.GreaterOrEqual(waters.Count, 3, "havuzlar");
            Transform shores = GameObject.Find(WorldV5Dress.ShoreGroupName).transform;
            int strips = 0, curves = 0;
            float covered = 0f;
            foreach (Transform t in shores)
            {
                bool strip = t.name.StartsWith("SM_pond_shore_moss", System.StringComparison.Ordinal) || t.name.StartsWith("SM_pond_shore_pebble", System.StringComparison.Ordinal);
                bool curve = t.name.StartsWith("SM_pond_shore_curve", System.StringComparison.Ordinal);
                if (!strip && !curve) continue;
                // each piece belongs to the pond whose rim it sits closest to (Pond1's east rim lies inside Pond0's 0.8–1.2 band)
                Transform owner = null;
                float best = float.MaxValue;
                foreach (Transform w in waters)
                {
                    Vector3 local = w.InverseTransformPoint(t.position);
                    float off = Mathf.Abs(2f * new Vector2(local.x, local.z).magnitude - 1f);   // 0 = on the rim of the unit disc (radius 0.5)
                    if (off < best) { best = off; owner = w; }
                }

                if (owner != falls || best > 0.2f) continue;
                if (strip) strips++; else curves++;
                Bounds b = new Bounds(t.position, Vector3.zero);
                foreach (Renderer rr in t.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(rr.bounds);
                covered += Mathf.Max(b.size.x, b.size.z);
            }

            float perimeter = Mathf.PI * (falls.localScale.x + falls.localScale.z) * 0.5f;
            Debug.Log("[DD-TEST] n3.shore " + falls.name + " strips=" + strips + " curves=" + curves + " covered=" + covered.ToString("F1") + " / perimeter " + perimeter.ToString("F1"));
            // WP-092 (first rebuild of the N3 builder, 08.10): Pond0's 10 rim points less the falls' 5.5 m clearing and the crossing deck
            // leave 5 strips; with the two curves they cover 112 % of the rim — the shore is whole, the count was an estimate.
            Assert.GreaterOrEqual(strips, 5, falls.name + ": şelale havuzunda kıyı şeridi");
            Assert.AreEqual(2, curves, falls.name + ": iki kavisli parça kıyıda (elipsin 0,8–1,2 bandında)");
            Assert.GreaterOrEqual(covered / perimeter, 0.6f, falls.name + ": kıyı modülleri / çevre");
        }

        /// <summary>WP-075 C6 · The v6 look: the bootstrap's profile is DD_Look_v5 with the colour lookup on LUT v2 at full contribution; the recipe's ΔE gate passed.</summary>
        [Test]
        public void Greybox_C6_LutV2_ProfilBagli_DeltaEKapisi()
        {
            Open();
            var bootstrap = Object.FindObjectOfType<Ap005WorldBootstrap>();
            var profile = new SerializedObject(bootstrap).FindProperty("_volumeProfile").objectReferenceValue as UnityEngine.Rendering.VolumeProfile;
            Assert.AreEqual(WorldV5Look.ProfilePath, AssetDatabase.GetAssetPath(profile));
            Assert.IsTrue(profile.TryGet(out UnityEngine.Rendering.Universal.ColorLookup lookup) && lookup.active);
            Assert.AreEqual(WorldV5Look.LutPath, AssetDatabase.GetAssetPath(lookup.texture.value));
            Assert.AreEqual(1f, lookup.contribution.value, 1e-4f);
            StringAssert.Contains("\"pass\": true", File.ReadAllText(WorldV5Look.RecipePath), "ΔE kapısı");
            var importer = (TextureImporter)AssetImporter.GetAtPath(WorldV5Look.LutPath);
            Assert.IsFalse(importer.sRGBTexture, "LUT sRGB kapalı (ApplyLut)");
            Assert.IsFalse(importer.mipmapEnabled, "LUT mip yok");
        }

        /// <summary>The look modes' screen atmosphere: shafts 20 / 0.22 High, 48 / 0.40 Cinematic, height fog in both, none in
        /// Performance; Ek A §5.2's fog from eye height.</summary>
        [Test]
        [Category("FPTP")]
        public void Greybox_C4B_C5_EkranAtmosferi_TPFP()
        {
            ScreenAtmosphereFeature.Rules cin = ScreenAtmosphereFeature.RulesFor(QualityTier.Sinematik, CameraMode.ThirdPerson);
            Assert.IsTrue(cin.HeightFog);
            Assert.AreEqual(48, cin.ShaftSamples);
            Assert.AreEqual(0.40f, cin.ShaftPeak, 1e-4f);
            ScreenAtmosphereFeature.Rules high = ScreenAtmosphereFeature.RulesFor(QualityTier.Yuksek, CameraMode.FirstPerson);
            Assert.AreEqual(20, high.ShaftSamples);
            Assert.AreEqual(0.22f, high.ShaftPeak, 1e-4f);
            Assert.AreEqual(0, ScreenAtmosphereFeature.RulesFor(QualityTier.Performans, CameraMode.ThirdPerson).ShaftSamples, "Performans kapalı");

            // Ek A §5.2: ≈ 9 % at 30 m, ≈ 12 % (cap) at 45 m from eye height; never over 12 %.
            float at30 = ScreenAtmosphereFeature.HeightFogOpacity(1.7f, Vector3.forward, 30f), at45 = ScreenAtmosphereFeature.HeightFogOpacity(1.7f, Vector3.forward, 45f);
            Assert.That(at30, Is.InRange(0.07f, 0.10f), "30 m");
            Assert.That(at45, Is.InRange(0.10f, 0.12f), "45 m");
            Assert.AreEqual(0.12f, ScreenAtmosphereFeature.HeightFogOpacity(1.7f, Vector3.forward, 400f), 1e-4f, "tavan");
            Assert.Less(ScreenAtmosphereFeature.HeightFogOpacity(20f, Vector3.forward, 30f), at30, "yükseldikçe incelir");
        }

        [Test]
        public void Greybox_C7_PostKurallari_KademeVeKameraModu()
        {
            // Ek A §7 table: grain by tier, vignette by mode, DoF / camera blur only in Cinematic TP / FP — the Top Down rows
            // (user 06.10); the TP / FP rows are Greybox_C7_PostKurallari_TPFP.
            LookV3Post.Rules td = LookV3Post.RulesFor(QualityTier.Sinematik, CameraMode.TopDown);
            Assert.AreEqual(0.18f, td.Grain, 1e-4f);
            Assert.AreEqual(0.24f, td.VignetteIntensity, 1e-4f);
            Assert.AreEqual(0.40f, td.VignetteSmoothness, 1e-4f);
            Assert.AreEqual(LookV3Post.DepthMode.Off, td.Depth);
            Assert.AreEqual(0f, td.MotionBlur);
            LookV3Post.Rules high = LookV3Post.RulesFor(QualityTier.Yuksek, CameraMode.TopDown);
            Assert.AreEqual(0.10f, high.Grain, 1e-4f);
            Assert.AreEqual(LookV3Post.DepthMode.Off, high.Depth);
            Assert.AreEqual(0f, high.MotionBlur);
            Assert.AreEqual(0f, LookV3Post.RulesFor(QualityTier.Performans, CameraMode.TopDown).Grain);

            Open();
            Assert.IsNotNull(GameObject.Find(WorldV5Dress.RootName).GetComponent<LookV3Post>(), "dress kökü LookV3Post taşır");
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(WorldV5Look.ProfilePath);
            Assert.IsTrue(profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom) && bloom.active);
            Assert.AreEqual(1.10f, bloom.threshold.value, 1e-4f);
            Assert.AreEqual(0.55f, bloom.intensity.value, 1e-4f);
            Assert.AreEqual(0.68f, bloom.scatter.value, 1e-4f);
            Assert.IsFalse(bloom.highQualityFiltering.value);
            Assert.IsTrue(profile.TryGet(out UnityEngine.Rendering.Universal.FilmGrain grain) && grain.active);
            Assert.AreEqual(0.18f, grain.intensity.value, 1e-4f);
            Assert.IsTrue(profile.TryGet(out UnityEngine.Rendering.Universal.DepthOfField dof) && !dof.active, "taban tepeden: DoF kapalı");
            foreach (UnityEngine.Rendering.VolumeComponent c in profile.components)
                Assert.IsTrue(AssetDatabase.Contains(c), c.GetType().Name + " profil alt varlığı");
        }

        /// <summary>The Ek A §7 rows of the look modes: DoF / camera blur only in Cinematic TP / FP, grain by tier there too.</summary>
        [Test]
        [Category("FPTP")]
        public void Greybox_C7_PostKurallari_TPFP()
        {
            LookV3Post.Rules tp = LookV3Post.RulesFor(QualityTier.Sinematik, CameraMode.ThirdPerson);
            Assert.AreEqual(LookV3Post.DepthMode.GaussianFar, tp.Depth);
            Assert.AreEqual(0.22f, tp.VignetteIntensity, 1e-4f);
            Assert.AreEqual(0.12f, tp.MotionBlur, 1e-4f);
            Assert.AreEqual(LookV3Post.DepthMode.BokehAim, LookV3Post.RulesFor(QualityTier.Sinematik, CameraMode.FirstPerson).Depth);
            LookV3Post.Rules high = LookV3Post.RulesFor(QualityTier.Yuksek, CameraMode.ThirdPerson);
            Assert.AreEqual(0.10f, high.Grain, 1e-4f);
            Assert.AreEqual(LookV3Post.DepthMode.Off, high.Depth);
            Assert.AreEqual(0f, high.MotionBlur);
            Assert.AreEqual(0f, LookV3Post.RulesFor(QualityTier.Performans, CameraMode.FirstPerson).Grain);
        }

        [Test]
        public void Greybox_C7b_YosunZemin_DunyaUzayi_YolBantlari()
        {
            // WP-092 B5 replaces the WP-075 C7b box material: the box stays the walkable collider (NavMesh source) and draws nothing;
            // the ground is WorldV5Terrain's chunks on DD/GroundTerrain with the world masks (the full contract: WorldV5TerrainTests).
            Open();
            var ground = GameObject.Find(GreyboxV6Builder.GreyboxRootName).transform.Find("Ground")?.gameObject;
            Assert.IsNotNull(ground, "greybox Ground");
            Assert.IsNotNull(ground.GetComponent<BoxCollider>(), "zemin çarpıştırıcısı");
            Assert.IsNull(ground.GetComponent<MeshRenderer>(), "kutu çizilmez (zemin WorldV5Terrain)");
            Transform terrain = GameObject.Find(WorldV5Dress.RootName).transform.Find(WorldV5Terrain.RootName);
            Assert.IsNotNull(terrain, "zemin parçaları");
            Material m = terrain.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreEqual(WorldV5Terrain.ShaderName, m.shader.name);
            foreach (string slot in new[] { "_MaskA", "_MaskB", "_MossMap", "_SoilMap", "_GravelMap" }) Assert.IsNotNull(m.GetTexture(slot), slot);
            Assert.LessOrEqual(m.GetFloat("_MossSaturation"), 0.9f, "yosun doygunluğu (AD MUST-1)");
        }

        [Test]
        public void Greybox_C4B_C5_EkranAtmosferi_KademeModu_YukseklikSisi()
        {
            // QUALITY-TIERS-V2 r2 §4.2: top-down gets neither shafts nor height fog at any tier — the Top Down rows (user 06.10);
            // the TP / FP rows and the eye-height fog curve are Greybox_C4B_C5_EkranAtmosferi_TPFP.
            foreach (QualityTier tier in new[] { QualityTier.Performans, QualityTier.Yuksek, QualityTier.Sinematik })
            {
                ScreenAtmosphereFeature.Rules td = ScreenAtmosphereFeature.RulesFor(tier, CameraMode.TopDown);
                Assert.IsFalse(td.HeightFog, tier + " tepeden: sis kapalı");
                Assert.AreEqual(0, td.ShaftSamples, tier + " tepeden: ışık huzmesi kapalı");
            }

            // On the High and Cinematic renderers (shader bound for builds), not on Performance's.
            foreach (string path in ScreenAtmosphereSetup.RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
                ScreenAtmosphereFeature feature = null;
                foreach (var f in data.rendererFeatures) if (f is ScreenAtmosphereFeature s) feature = s;
                Assert.IsNotNull(feature, path);
                Assert.IsTrue(feature.isActive, path);
                Assert.AreEqual(ScreenAtmosphereFeature.ShaderName, feature.Shader.name, path);
                Assert.IsTrue(AssetDatabase.Contains(feature), path + ": alt varlık");
            }

            var performance = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/DD_URP_Renderer_Performance.asset");
            foreach (var f in performance.rendererFeatures) Assert.IsFalse(f is ScreenAtmosphereFeature, "Performans renderer'ı");
        }

        [Test]
        public void Greybox_DEC102_ParcaliBulut_YuksekGunes_HacimselIsik()
        {
            // DEC-102 (user 25.09): the v6 sky is broken cloud under a high, strong sun; the shafts are the raymarched volumetric light.
            WorldLayoutKind previous = WorldLayout.Current;
            var go = new GameObject("DEC102 sun");
            try
            {
                WorldLayout.SetCurrent(WorldLayoutKind.V6);
                Light light = go.AddComponent<Light>();
                WorldAtmosphere.ConfigureSun(light);
                Assert.AreEqual(WorldAtmosphere.SunIntensity * WorldAtmosphere.V6Sky.SunIntensityScale, light.intensity, 1e-4f);
                Assert.AreEqual(WorldAtmosphere.V6Sky.SunShadowStrength, light.shadowStrength, 1e-4f);
                Assert.That(WorldAtmosphere.SunEulerFor(WorldLayoutKind.V6).x, Is.InRange(45f, 55f), "güneş yüksekliği (DEC-102 r2: 45–55°)");
                Assert.AreEqual(WorldAtmosphere.CloudShadow.Sky3CookieResourceName, WorldAtmosphere.CloudShadow.CookieResourceFor(WorldLayoutKind.V6));
                Assert.AreEqual(0f, WorldAtmosphere.CloudShadow.SpeedFor(WorldLayoutKind.V6), "r2: gök duruk, gölgesi de");
                Assert.AreEqual(WorldAtmosphere.SkyV3ResourceName, WorldAtmosphere.SkyResourceFor(WorldLayoutKind.V6));
                WorldLayout.SetCurrent(WorldLayoutKind.Ap005);
                WorldAtmosphere.ConfigureSun(light);
                Assert.AreEqual(WorldAtmosphere.SunIntensity, light.intensity, 1e-4f, "World_AP005 v3 anahtarı değişmez");
                Assert.AreEqual(WorldAtmosphere.SunEuler, WorldAtmosphere.SunEulerFor(WorldLayoutKind.Ap005));
            }
            finally
            {
                Object.DestroyImmediate(go);
                WorldLayout.SetCurrent(previous);
            }

            // The v6 cookie (r2: cast from the sky_v3 panorama): 40–60 % sun (≥ 0.8), 0.40 under a cloud, imported by the cookie rules.
            var png = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(png.LoadImage(File.ReadAllBytes(ArtImportContract.CloudCookieSky3Path)), "sky3 cookie PNG");
                Color32[] px = png.GetPixels32();
                int sun = 0, low = 255;
                foreach (Color32 c in px) { if (c.r >= 204) sun++; low = Mathf.Min(low, c.r); }
                Assert.That(sun / (float)px.Length, Is.InRange(0.40f, 0.60f), "açıklık payı");
                Assert.AreEqual(102, low, 1, "bulut altı 0,40");
            }
            finally
            {
                Object.DestroyImmediate(png);
            }

            Assert.IsTrue(ArtImportContract.IsLightCookieTexture(ArtImportContract.CloudCookieSky3Path));
            Assert.IsNotNull(Resources.Load<Texture2D>(WorldAtmosphere.CloudShadow.Sky3CookieResourceName));
            var skyV3 = Resources.Load<Material>(WorldAtmosphere.SkyV3ResourceName);
            Assert.IsNotNull(skyV3, "r2: gök panoraması malzemesi");
            Assert.AreEqual(ArtSkyBuilder.SkyDec102SourcePath, AssetDatabase.GetAssetPath(skyV3.GetTexture("_MainTex")), "r2: sky_v3 panoraması");

            // Tiers: Cinematic ⅓ × 20 (r3, FP p99), High ¼ × 12, Performance off; the feature on the High and Cinematic renderers.
            VolumetricLightFeature.Rules cin = VolumetricLightFeature.RulesFor(QualityTier.Sinematik), high = VolumetricLightFeature.RulesFor(QualityTier.Yuksek);
            Assert.IsTrue(cin.On && cin.Downsample == 3 && cin.Steps == 20);   // r3: ⅓ × 20 (release FP p99)
            Assert.IsTrue(high.On && high.Downsample == 4 && high.Steps == 12);
            Assert.IsFalse(VolumetricLightFeature.RulesFor(QualityTier.Performans).On);
            foreach (string path in ScreenAtmosphereSetup.RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
                VolumetricLightFeature feature = null;
                foreach (var f in data.rendererFeatures) if (f is VolumetricLightFeature v) feature = v;
                Assert.IsNotNull(feature, path);
                Assert.AreEqual(VolumetricLightFeature.ShaderName, feature.Shader.name, path);
            }

            // The dressed scene: the field (regions, the plaza centre clear), the sun disc, sunlit broken clouds, hazed plates.
            Open();
            var dress = GameObject.Find(WorldV5Dress.RootName);
            var field = dress.GetComponent<VolumetricLight>();
            Assert.IsNotNull(field, "VolumetricLight");
            Assert.AreEqual(WorldV5Atmosphere.VolumeRegions.Length, field.Regions.Length);
            Assert.Less(field.DensityAt(new Vector3(0f, 1.5f, 0f)), 0.004f, "meydan merkezi açık (K10)");
            Assert.Greater(field.DensityAt(new Vector3(-45f, 3f, 14f)), 0.02f, "bambu korusu sisli");
            // r3 (AD MUST-B1/C1 25.09): the eye-level base haze 0.0035 → 0.0010 (it veiled the roofs and the treeline); the lifted band
            // (0.0015 at 18 ± 9 m, unchanged) carries the top-down columns, so the haze still reaches 30 m.
            Assert.Greater(field.DensityAt(new Vector3(10f, 30f, -10f)), 0.00045f, "r2/r3: pus 30 m'ye uzanır (bulut açıklığından sütunlar tepeden okunur)");
            Assert.Less(field.BaseDensity, 0.0015f, "r3: göz hizası perdesi ince (AD MUST-B1/C1)");
            Assert.LessOrEqual(field.AmbientScatter, 0.1f, "r2: sütun aralarında gök saçılması düşük (sütunlar gölgeden ayrılır)");
            // S-07 sözleşmesi (DD_ShaftMask, C-00 25.09): tepe yoğunluğuna göre 0..1 — meydan huzme dışı, koru huzmenin içinde.
            Assert.Greater(field.PeakDensity(), 0f, "tepe yoğunluğu");
            Assert.Less(field.ShaftMaskAt(new Vector3(0f, 1.5f, 0f)), 0.2f, "meydan: huzme maskesi düşük");
            Assert.Greater(field.ShaftMaskAt(new Vector3(-45f, 3f, 14f)), 0.8f, "koru: huzme maskesi yüksek");
            Assert.IsNotNull(dress.transform.Find(WorldV5Sky.RootName + "/" + WorldV5Sky.SunDiscName).GetComponent<SunDisc>(), "güneş diski");
            Assert.IsNull(dress.transform.Find(WorldV5Sky.RootName + "/Clouds_Near"), "r2: döşeme bulut düzlemleri yok (panorama)");
            Assert.Greater(field.DensityAt(new Vector3(0f, 18f, 0f)), 0.0015f, "r2: yükseltilmiş pus bandı (tepeden sütunlar)");
            Assert.AreEqual(WorldV5Atmosphere.VistaMist.Length, GameObject.Find(WorldV5Atmosphere.VistaMistGroupName).transform.childCount, "vista sisi");
            // DEC-108 AD MUST 2: the understory skirt closes the vista band's square (±88 m, y −1…+5) — every tier, unlit, no shadows,
            // just past the vista trees (≤ 84 m + jitter), so no line of sight below +5 m leaves the band to the sky.
            Transform skirt = dress.transform.Find(WorldV5Atmosphere.SkirtGroupName);
            Assert.AreEqual(4, skirt.childCount, "alt-bitki eteği: 4 duvar");
            Assert.IsNull(skirt.GetComponent<AtmosphereTierGroup>(), "etek her kademede");
            foreach (Transform wall in skirt)
            {
                var r = wall.GetComponent<MeshRenderer>();
                Assert.AreEqual("DD/UnderstorySkirt", r.sharedMaterial.shader.name, wall.name);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, wall.name + " gölgesiz");
                Assert.AreEqual(WorldV5Atmosphere.SkirtHalf, Mathf.Max(Mathf.Abs(r.bounds.center.x), Mathf.Abs(r.bounds.center.z)), 0.01f, wall.name + " ±88 m");
                Assert.AreEqual(WorldV5Atmosphere.SkirtHalf, Mathf.Max(r.bounds.extents.x, r.bounds.extents.z), 0.01f, wall.name + " köşeden köşeye");
                Assert.AreEqual(WorldV5Atmosphere.SkirtBottom, r.bounds.min.y, 0.01f, wall.name + " alt");
                Assert.AreEqual(WorldV5Atmosphere.SkirtTop, r.bounds.max.y, 0.01f, wall.name + " üst");
            }

            foreach (Renderer tree in dress.transform.Find(WorldV5Dress.VistaGroupName).GetComponentsInChildren<Renderer>())
            {
                Vector3 c = tree.transform.position;
                Assert.Less(Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.z)), WorldV5Atmosphere.SkirtHalf, tree.name + " eteğin içinde");
            }
            var far = dress.transform.Find(ArtSurfaceImporter.SkyRootName + "/Horizon_far").GetComponent<MeshRenderer>().sharedMaterial;
            Assert.Greater(far.GetFloat("_Haze"), 0.3f, "uzak katman sisli");
        }

        private static int CountOf(string s, string token)
        {
            int n = 0;
            for (int i = s.IndexOf(token, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(token, i + token.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
