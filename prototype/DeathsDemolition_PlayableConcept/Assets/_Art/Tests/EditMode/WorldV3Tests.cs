using System;
using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-020 · World v3 (AP-005C delta on the v002 chain): the three west-corridor V03 panels take the
    /// plaster world material on their <c>panel</c> part only (<c>material_override_parts</c>), the
    /// named NavMesh areas exist (Bridge 3 / Corridor 4 / Gate 5) and the scene carries one modifier
    /// volume per bridge segment / corridor module / gate transition, the baked NavMesh crosses the
    /// AP-005C north gate (5.20 m clear opening, GATE-N) and prices it as Gate, the prop materials bind
    /// the ORM v002 occlusion, and the AP-005C records reference existing prefabs.
    /// </summary>
    [TestFixture]
    [Category("WP020")]
    public sealed class WorldV3Tests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // Leave no World_AP005 (and no NavMeshSurface data) behind for fixtures that assume an empty scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            NavMesh.RemoveAllNavMeshData();
        }

        private static Scene OpenWorld()
        {
            Assert.IsTrue(File.Exists(ArtLibraryPaths.Ap005WorldScene), "Dünya sahnesi yok: " + ArtLibraryPaths.Ap005WorldScene);
            return EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
        }

        private static Transform Root(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == Ap005WorldBootstrap.RootName) return root.transform;
            Assert.Fail("dünya kökü yok");
            return null;
        }

        [Test]
        public void ParcaOverride_UcKoridorPaneli_YalnizPanelPlasterWorld_DigerParcalarKendiMateryali()
        {
            var p = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            Assert.IsNotNull(p);
            var withParts = new List<WorldPlacementAsset.Instance>();
            foreach (WorldPlacementAsset.Instance i in p.instances) if (i.materialOverrideParts != null && i.materialOverrideParts.Length > 0) withParts.Add(i);
            Assert.AreEqual(3, withParts.Count, "AP-005C F1: üç batı koridoru V03 paneli");
            Transform instances = Root(OpenWorld()).Find(Ap005WorldBootstrap.InstancesName);
            foreach (WorldPlacementAsset.Instance i in withParts)
            {
                Assert.AreEqual("v003", i.deltaVersion, i.id);
                Assert.AreEqual(1, i.materialOverrideParts.Length); Assert.AreEqual("panel", i.materialOverrideParts[0].part); Assert.AreEqual("AP005_M_plaster_world", i.materialOverrideParts[0].material);
                Transform t = instances.Find(i.id);
                Assert.IsNotNull(t, i.id + " sahnede yok");
                int panelWorld = 0, panelPlain = 0, otherWorld = 0;
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                {
                    bool panel = r.name.StartsWith("panel_LOD", StringComparison.Ordinal) || r.name == "panel";
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        bool world = m.name.EndsWith("_world", StringComparison.Ordinal);
                        bool plaster = m.name.StartsWith("M_plaster", StringComparison.Ordinal);
                        if (panel) { if (world) panelWorld++; else if (plaster) panelPlain++; }   // the panel's wood frame slot stays M_wood
                        else if (world) otherWorld++;
                    }
                }

                Assert.Greater(panelWorld, 0, i.id + ": panelde plaster_world slotu yok");
                Assert.AreEqual(0, panelPlain, i.id + ": panelde AP-002 plaster kalmamalı (#DFDFD4 → #CDCBC1)");
                Assert.AreEqual(0, otherWorld, i.id + ": floor/frame/roof parçaları kendi materyalinde kalır");
            }
        }

        [Test]
        public void NavMeshAlanlari_ProjeAlanlari_16Hacim_BridgeCorridorGate()
        {
            Assert.AreEqual(3, NavMesh.GetAreaFromName(PlacementImporter.AreaBridge), "Bridge alanı");
            Assert.AreEqual(4, NavMesh.GetAreaFromName(PlacementImporter.AreaCorridor), "Corridor alanı");
            Assert.AreEqual(5, NavMesh.GetAreaFromName(PlacementImporter.AreaGate), "Gate alanı");
            Transform areas = Root(OpenWorld()).Find(PlacementImporter.AreaVolumesName);
            Assert.IsNotNull(areas, "alan hacimleri kökü yok");
            var counts = new Dictionary<int, int>();
            foreach (NavMeshModifierVolume v in areas.GetComponentsInChildren<NavMeshModifierVolume>(true))
            {
                counts[v.area] = (counts.TryGetValue(v.area, out int n) ? n : 0) + 1;
                Assert.AreEqual(PlacementImporter.AreaVolumeHeight, v.size.y, 1e-3f, v.name);
                Assert.Greater(v.size.x, 1f, v.name); Assert.Greater(v.size.z, 1f, v.name);
            }

            Assert.AreEqual(5, counts.TryGetValue(3, out int bridge) ? bridge : 0, "5 köprü segmenti");
            Assert.AreEqual(6, counts.TryGetValue(4, out int corridor) ? corridor : 0, "6 batı koridoru modülü");
            Assert.AreEqual(5, counts.TryGetValue(5, out int gate) ? gate : 0, "4 kapı + AP-005C kuzey kapısı");
            Debug.Log("[DD-TEST] wp020.areas bridge=" + bridge + " corridor=" + corridor + " gate=" + gate);
        }

        [Test]
        public void KuzeyKapisi_GATEN_520mAcikliktanNavMeshGecer_GateAlani()
        {
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(ArtLibraryPaths.Ap005NavMeshData);
            Assert.IsNotNull(data, "NavMesh verisi yok");
            OpenWorld();
            NavMesh.RemoveAllNavMeshData();
            NavMeshDataInstance handle = NavMesh.AddNavMeshData(data);
            try
            {
                // Blender (0, 17) = Unity (x 0, z 17): posts at x ±2.79 → 5.20 m clear; the walk crosses it on the stone way.
                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(new Vector3(0f, 0f, 11f), new Vector3(0f, 0f, 23f), NavMesh.AllAreas, path), "yol hesabı");
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "kuzey kapısından geçen tam yol");
                float maxAbsX = 0f;
                foreach (Vector3 c in path.corners) if (Mathf.Abs(c.z - 17f) < 2.5f) maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(c.x));
                Assert.LessOrEqual(maxAbsX, 2.6f, "yol açıklık içinde kalır (|x| ≤ 2,6)");

                int gateArea = NavMesh.GetAreaFromName(PlacementImporter.AreaGate);
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0f, 17f), out NavMeshHit hit, 1.0f, NavMesh.AllAreas), "açıklıkta NavMesh var");
                Assert.AreEqual(1 << gateArea, hit.mask, "açıklık Gate alanında");
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(4f, 0f, -28.75f), out NavMeshHit bridge, 2.0f, NavMesh.AllAreas), "köprü güvertesinde NavMesh var");
                Assert.AreEqual(1 << NavMesh.GetAreaFromName(PlacementImporter.AreaBridge), bridge.mask, "köprü Bridge alanında");
                Debug.Log("[DD-TEST] wp020.gate corners=" + path.corners.Length + " maxAbsX=" + maxAbsX.ToString("F2") + " gateMask=" + hit.mask + " bridgeMask=" + bridge.mask);
            }
            finally
            {
                NavMesh.RemoveNavMeshData(handle);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // drops the scene's NavMeshSurface data too (other fixtures assume no NavMesh)
                NavMesh.RemoveAllNavMeshData();
            }
        }

        [Test]
        public void KaldirimDerzi_ADMust1_QuietPavingAltindaTasRenkliDolguQuad()
        {
            var p = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            int plates = 0;
            foreach (WorldPlacementAsset.Instance i in p.instances) if (i.family == "quiet_paving") plates++;
            Assert.AreEqual(1, plates, "AP-005 tek quiet_paving plakası");
            Transform root = Root(OpenWorld());
            Transform grout = root.Find(PlacementImporter.PavingGroutName);
            Assert.IsNotNull(grout, "derz dolgusu kökü yok");
            MeshRenderer[] quads = grout.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(plates, quads.Length, "plaka başına bir quad");
            Transform plate = root.Find(Ap005WorldBootstrap.InstancesName).Find("AP005_i0051");
            Assert.IsNotNull(plate);
            Bounds pb = plate.GetComponentInChildren<Renderer>(true).bounds;
            foreach (MeshRenderer q in quads)
            {
                Assert.AreEqual("M_stone", q.sharedMaterial.name, q.name + ": plakanın taş materyali");
                Assert.AreEqual(pb.max.y - PlacementImporter.PavingGroutDepth, q.bounds.max.y, 1e-3f, q.name + ": plaka üstünün 2 mm altında");
                Assert.Greater(q.bounds.size.x, 50f); Assert.Greater(q.bounds.size.z, 45f);
                Assert.IsNull(q.GetComponent<Collider>(), q.name + ": collider yok (NavMesh plakada kalır)");
                Assert.AreEqual(0, (int)(GameObjectUtility.GetStaticEditorFlags(q.gameObject) & StaticEditorFlags.ContributeGI), q.name + ": GI'ye katkı yok");
            }
        }

        [Test]
        public void PropMateryalleri_OrmV002_OcclusionBagli_TuretilmisMaskeGKanaliAO()
        {
            int bound = 0, windRoles = 0, ap023 = 0, ap023Wind = 0;
            foreach (string role in ArtLibraryMaterialRoles.PackedTextureRoles())
            {
                if (!ArtTextureRepack.CarriesOcclusion(role)) continue;
                bool kit = role.StartsWith("ap023_", StringComparison.Ordinal);   // WP-074 B5: the AP-023 kit's ORM carries AO too
                var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(role));
                Assert.IsNotNull(m, role);
                if (ArtLibraryMaterialRoles.Get(role).Shader == ArtLibraryMaterialRoles.ShaderKind.FoliageWind)
                {
                    // WP-038L: the AP-017 furin hangs on DD/Foliage Wind (no occlusion slot) — the derived mask still binds as MetallicSmoothness.
                    Assert.IsNotNull(m.GetTexture("_MetallicGlossMap"), role + ": _MetallicGlossMap");
                    if (kit) ap023Wind++; else windRoles++;
                    continue;
                }
                Assert.IsNotNull(m.GetTexture("_OcclusionMap"), role + ": _OcclusionMap");
                Assert.AreEqual(m.GetTexture("_MetallicGlossMap"), m.GetTexture("_OcclusionMap"), role + ": AO aynı maskenin G kanalı");
                Assert.IsTrue(m.IsKeywordEnabled("_OCCLUSIONMAP"), role);
                string prov = File.ReadAllText(ArtPipelinePaths.DerivedMaskProvenance(role));
                Assert.IsTrue(prov.Contains("MetallicSmoothnessAO v2"), role + ": provenance v2");
                if (kit) ap023++; else bound++;
            }

            Assert.AreEqual(14, bound, "6 AP-005B prop rolü + 4 AP-017 Faz 2 prop rolü (WP-038I) + 4 Faz 2b Lit prop rolü (WP-038L)");
            Assert.AreEqual(1, windRoles, "WP-038L: furin rüzgâr gölgelendiricisinde");
            Assert.AreEqual(21, ap023, "WP-074 B5: AP-023 20 Lit karo rolü (ORM AO'lu; B5b-2 v005 + gravel); v007 + trim = 21");
            Assert.AreEqual(1, ap023Wind, "WP-074 B5: AP-023 bamboo_culm rüzgâr gölgelendiricisinde");
        }
    }
}
