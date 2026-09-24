using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
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

        private static int CountOf(string s, string token)
        {
            int n = 0;
            for (int i = s.IndexOf(token, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(token, i + token.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
