using System;
using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Feedback;
using DD.Prototype.World;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-038 · World v4 (ADR-038-1): the AP-015P surfaces replace the stand-in ground — 71 plates (9 ground, 49 route,
    /// 13 paving) with walkable colliders and 27 render-only kerbs, 3 water meshes on DD/Water v2, one DD/GroundBlend
    /// material, surface tags per plate, the v1 pond planes hidden; the NavMesh walks on the plates (route tops 10–16 mm,
    /// ground −5 mm) and still crosses GATE-N; the footprint rules of Contract v1.3 (environment mesh compression Medium,
    /// derived masks ≤ 1024, plates and water without lightmap UVs).
    /// </summary>
    [TestFixture]
    [Category("WP038")]
    public sealed class WorldV4Tests
    {
        private static Scene OpenWorld()
        {
            Assert.IsTrue(File.Exists(ArtLibraryPaths.Ap005WorldScene), "Dünya sahnesi yok");
            return EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
        }

        private static Transform Root(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == Ap005WorldBootstrap.RootName) return root.transform;
            Assert.Fail("dünya kökü yok");
            return null;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            NavMesh.RemoveAllNavMeshData();
        }

        [Test]
        public void Yuzeyler_71Plaka_3Su_27Kerb_StandInYok_MateryallerVeEtiketler()
        {
            Assert.IsTrue(ArtSurfaceImporter.Available, "AP-015P manifest");
            Transform root = Root(OpenWorld());
            Assert.IsNull(root.Find(PlacementImporter.GroundName), "stand-in zemin kalktı (DEC-078 kapanışı)");
            Transform surfaces = root.Find(ArtSurfaceImporter.RootName);
            Assert.IsNotNull(surfaces, "Surfaces kökü yok");
            var ground = AssetDatabase.LoadAssetAtPath<Material>(ArtSurfaceImporter.GroundMaterialPath);
            var water = AssetDatabase.LoadAssetAtPath<Material>(ArtSurfaceImporter.WaterMaterialPath);
            Assert.IsNotNull(ground); Assert.IsNotNull(water);
            Assert.AreEqual(ArtSurfaceImporter.GroundShaderName, ground.shader.name);
            Assert.AreEqual(ArtSurfaceImporter.WaterShaderName, water.shader.name);
            Assert.AreEqual(ArtSurfaceImporter.GravelTint, ground.GetFloat("_GravelTint"), 1e-4f, "_GravelTint 0,72 (P-05 0,60 → AD MUST-3: earth < gravel)");
            Material waterMat = AssetDatabase.LoadAssetAtPath<Material>(ArtSurfaceImporter.WaterMaterialPath);
            Assert.IsNotNull(waterMat, "M_water_v2");
            Assert.AreEqual(ArtSurfaceImporter.SparkleCap, waterMat.GetFloat("_SparkleCap"), 1e-4f, "_SparkleCap 0,18 (AD MUST-2)");
            Assert.AreEqual(ArtSurfaceImporter.CausticIntensity, waterMat.GetFloat("_CausticIntensity"), 1e-4f, "_CausticIntensity 0,08 (AD MUST-1)");
            Material kerbMat = AssetDatabase.LoadAssetAtPath<Material>(ArtSurfaceImporter.KerbMaterialPath);
            Assert.IsNotNull(kerbMat, "M_kerb_stone");
            Assert.AreEqual(ArtSurfaceImporter.KerbAlbedoLift, kerbMat.GetColor("_BaseColor").r, 1e-4f, "kerb albedo × 1,2 (AP-015P M4 / AD MUST-4)");
            Assert.LessOrEqual(ground.GetFloat("_WetSmoothness"), 0.60f, "ıslak smoothness ≤ 0,60 (DEC-076 ayna yok)");
            // WP-038E · AP-018 wet path: tiles / masks / wet packed → derived masks bound, tints from role_stats, puddle exception 0,70, legacy darkening off.
            foreach (string prop in new[] { "_DampMask", "_PuddleMask", "_ShoreMask", "_WetStoneMap", "_WetStoneMask", "_WetEarthMask", "_WetGravelMask" })
                Assert.IsNotNull(ground.GetTexture(prop), prop + " (AP-018) bağlı değil");
            Assert.Less(((Vector4)(ground.GetColor("_WetTintEarth") - ArtSurfaceImporter.WetTintEarth)).magnitude, 1e-3f, "_WetTintEarth (AP-018 role_stats)");
            Assert.Less(((Vector4)(ground.GetColor("_WetTintGravel") - ArtSurfaceImporter.WetTintGravel)).magnitude, 1e-3f, "_WetTintGravel (AP-018 role_stats)");
            Assert.AreEqual(ArtSurfaceImporter.PuddleSmoothness, ground.GetFloat("_PuddleSmoothness"), 1e-4f);
            Assert.AreEqual(0f, ground.GetFloat("_WetDarken"), 1e-4f, "AP-018 tint/ıslak karo koyulaştırmayı taşır");
            Material puddleMat = AssetDatabase.LoadAssetAtPath<Material>(ArtSurfaceImporter.PuddleDecalMaterialPath);
            Assert.IsNotNull(puddleMat, "M_decal_puddle"); Assert.IsNotNull(puddleMat.GetTexture("Base_Map"), "puddle decal sheet");
            var rendererData = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(ArtLightingBake.UrpRendererPath);
            bool decalFeature = false;
            foreach (UnityEngine.Rendering.Universal.ScriptableRendererFeature f in rendererData.rendererFeatures) if (f != null && f.GetType().Name == "DecalRendererFeature") decalFeature = true;
            Assert.IsTrue(decalFeature, "DecalRendererFeature (AP-018 su birikintileri) renderer'da yok");
            Transform puddles = surfaces.Find(ArtSurfaceImporter.PuddleDecalsName);
            Assert.IsNotNull(puddles, "PuddleDecals kökü");
            int projectors = puddles.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true).Length;
            Assert.IsTrue(projectors >= 20 && projectors <= ArtSurfaceImporter.PuddleDecalCap, "su birikintisi projektörleri 20..160: " + projectors);
            // WP-038E (2) · AP-019: three unlit horizon cylinders (scaled 0.55 to the 260 m far clip) + the sky band lat-long on the panoramic skybox.
            Transform sky = surfaces.Find(ArtSurfaceImporter.SkyRootName);
            Assert.IsNotNull(sky, "Sky (AP-019) kökü");
            MeshRenderer[] horizon = sky.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(3, horizon.Length, "3 ufuk plakası");
            foreach (MeshRenderer hr in horizon)
            {
                Assert.AreEqual(ArtSurfaceImporter.SkyPlateShaderName, hr.sharedMaterial.shader.name, hr.name + ": DD/SkyPlate");
                Assert.AreEqual(ShadowCastingMode.Off, hr.shadowCastingMode, hr.name + ": gölge yok");
                Bounds hb = hr.bounds;
                Assert.Greater(Mathf.Max(hb.extents.x, hb.extents.z), 250f, hr.name + ": AP-019 sözleşme yarıçapı (260/330/420 m; derinlik shader'da far plane'e sabitlenir)");
            }

            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(ArtLibraryPaths.SkyMaterial);
            var skyTex = skyMat.GetTexture("_MainTex") as Texture2D;
            Assert.IsNotNull(skyTex, "gök dokusu");
            Assert.GreaterOrEqual(skyTex.width, 1024, "AP-019 gök bandı lat-long ≥ 1024 (4×256 gradyan değil)");
            Assert.AreEqual(0.6f, water.GetFloat("_DepthFade"), 1e-4f);
            Assert.AreEqual(ArtSurfaceImporter.WaterSpecularScale, water.GetFloat("_SpecularScale"), 1e-4f, "_SpecularScale 0,40 (WP-038D, AD SHOULD-5)");
            Assert.AreEqual(ArtSurfaceImporter.WaterSmoothness, water.GetFloat("_Smoothness"), 1e-4f, "_Smoothness 0,88 (P-05; AD SHOULD-5 0,82 denendi, lob genişledi → geri alındı)");

            int plates = 0, waters = 0, kerbs = 0, colliders = 0, tags = 0, lodGroups = 0, otherMaterial = 0;
            var kinds = new Dictionary<SurfaceKind, int>();
            for (int i = 0; i < surfaces.childCount; i++)
            {
                Transform t = surfaces.GetChild(i);
                if (t.name == "PondNavHoles" || t.name == ArtSurfaceImporter.PuddleDecalsName || t.name == ArtSurfaceImporter.SkyRootName) continue;   // NavMesh basin rows / AP-018 decal projectors / AP-019 horizon (no collider, own materials)
                bool isWater = t.name.StartsWith("AP015_water_", StringComparison.Ordinal);
                if (isWater) waters++; else plates++;
                var tag = t.GetComponent<SurfaceTag>();
                if (tag != null) { tags++; kinds[tag.Surface] = (kinds.TryGetValue(tag.Surface, out int n) ? n : 0) + 1; }
                if (t.GetComponent<LODGroup>() != null) lodGroups++;
                foreach (MeshRenderer mr in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool kerb = mr.name.Contains("_kerb_");
                    if (kerb) { kerbs++; Assert.IsNotNull(mr.GetComponent<NavMeshModifier>(), mr.name + ": kerb NavMesh ignore"); Assert.AreEqual("M_kerb_stone", mr.sharedMaterial.name, mr.name + ": kerb material"); continue; }
                    if (mr.sharedMaterial != (isWater ? water : ground)) otherMaterial++;
                    Assert.AreEqual(LightProbeUsage.BlendProbes, mr.lightProbeUsage, mr.name + ": probe ile aydınlanır");
                }

                foreach (MeshCollider mc in t.GetComponentsInChildren<MeshCollider>(true))
                {
                    colliders++;
                    Assert.IsFalse(isWater, t.name + ": su collider taşımaz");
                    Assert.AreEqual(DD.Prototype.Core.PrototypeLayers.Environment, mc.gameObject.layer, mc.name + ": Environment katmanı");
                }

                if (!isWater) Assert.AreNotEqual(0, (int)(GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.ContributeGI), t.name + ": bake'e katkı (occluder/bounce)");
                // WP-038G (M3 closure K1): plates are static-batched (one material) and cast no shadow; the kerbs keep their shadow.
                if (!isWater) Assert.AreNotEqual(0, (int)(GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.BatchingStatic), t.name + ": statik birleştirme");
                foreach (MeshRenderer pr in t.GetComponentsInChildren<MeshRenderer>(true))
                    if (!pr.name.Contains("_kerb_")) Assert.AreEqual(ShadowCastingMode.Off, pr.shadowCastingMode, pr.name + ": plaka gölge düşürmez (WP-038G)");
                if (isWater) Assert.AreEqual(0, (int)(GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.ContributeGI), t.name + ": su bake'e girmez");
            }

            Assert.AreEqual(71, plates, "9 zemin + 49 rota + 13 kaldırım");
            Assert.AreEqual(3, waters);
            Assert.AreEqual(27, kerbs);
            Assert.AreEqual(71, colliders, "her plaka bir COL");
            Assert.AreEqual(74, tags);
            Assert.AreEqual(0, otherMaterial, "plakalar tek M_ground_blend, su M_water_v2");
            Assert.AreEqual(5, lodGroups, "5 bölge zemin plakası LOD0/LOD1 (dış bant tek LOD)");
            Assert.AreEqual(3, kinds[SurfaceKind.Water]);
            Assert.GreaterOrEqual(kinds[SurfaceKind.Stone], 20 + 13, "taş rotalar + kaldırımlar");
            Assert.GreaterOrEqual(kinds[SurfaceKind.Dirt], 29 + 9, "toprak rotalar + zemin plakaları");

            // The v1 pond planes stay as instances (counts) but no longer render.
            Transform instances = root.Find(Ap005WorldBootstrap.InstancesName);
            int pondOn = 0, pond = 0;
            for (int i = 0; i < instances.childCount; i++)
            {
                GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(instances.GetChild(i).gameObject);
                if (src == null || !src.name.Contains("pond_water_surface")) continue;
                pond++;
                foreach (Renderer r in instances.GetChild(i).GetComponentsInChildren<Renderer>(true)) if (r.enabled) pondOn++;
            }

            Assert.Greater(pond, 0); Assert.AreEqual(0, pondOn, "v1 su plakaları görünmez (DD_Water_v1 devre dışı)");
            Debug.Log("[DD-TEST] wp038.surfaces plates=" + plates + " water=" + waters + " kerbs=" + kerbs + " colliders=" + colliders + " tags=" + tags + " stone=" + kinds[SurfaceKind.Stone] + " dirt=" + kinds[SurfaceKind.Dirt]);
        }

        [Test]
        public void NavMesh_PlakalarUstunde_RotaVeZeminYuksekligi_GATENGecisi()
        {
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(ArtLibraryPaths.Ap005NavMeshData);
            Assert.IsNotNull(data);
            OpenWorld();
            NavMesh.RemoveAllNavMeshData();
            NavMeshDataInstance handle = NavMesh.AddNavMeshData(data);
            try
            {
                // Stone way north (route plate top ≈ 0.010–0.016) and the west earth field (ground plate −0.005): the walkable
                // surface is the plate, not the vanished stand-in (−0.02).
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0f, 25f), out NavMeshHit north, 1f, NavMesh.AllAreas), "kuzey taş yolu NavMesh'te");
                Assert.Greater(north.position.y, -0.02f + 0.001f, "yol plakası üstünde (stand-in −0,02 değil): " + north.position.y);
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(-24f, 0f, 0f), out NavMeshHit west, 1f, NavMesh.AllAreas), "batı koridoru doğusundaki toprak alan NavMesh'te");
                Assert.Greater(west.position.y, -0.02f + 0.001f, "zemin plakası üstünde: " + west.position.y);
                var path = new NavMeshPath();
                Assert.IsTrue(NavMesh.CalculatePath(new Vector3(0f, 0f, 11f), new Vector3(0f, 0f, 23f), NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "GATE-N geçişi plakalar üstünde");
                Assert.IsTrue(NavMesh.CalculatePath(new Vector3(-36f, 0f, -10f), new Vector3(30f, 0f, 30f), NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "batı koridoru → kuzeydoğu tam yol");

                // South ponds (WP-030 choke): the water footprint is not walkable — neither the basin centres nor the 0.75 m
                // ground isthmus between ponds 02 / 03 beside the raised shore module — and the crossing from the centre to
                // the south bank climbs onto the shore module / bridge deck (≥ 0.25 m), as with the WP-014 stand-in holes.
                Assert.IsFalse(NavMesh.SamplePosition(new Vector3(2.5f, 0f, -29.3f), out _, 0.6f, NavMesh.AllAreas), "havuz 03 merkezi yürünemez");
                Assert.IsFalse(NavMesh.SamplePosition(new Vector3(-8.4f, 0f, -29f), out _, 0.6f, NavMesh.AllAreas), "havuz 02 merkezi yürünemez");
                bool isthmus = NavMesh.SamplePosition(new Vector3(-4.2f, 0f, -27.5f), out NavMeshHit strip, 0.3f, NavMesh.AllAreas);
                Assert.IsTrue(!isthmus || strip.position.y > 0.2f, "havuz 02/03 arası zemin şeridi yürünemez (kıyı modülü üstü hariç): " + (isthmus ? strip.position.ToString("F2") : "yok"));
                Assert.IsTrue(NavMesh.CalculatePath(new Vector3(0f, 0f, -10f), new Vector3(4f, 0f, -43f), NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "merkez → güney kıyı tam yol");
                float crest = float.MinValue;
                foreach (Vector3 c in path.corners) crest = Mathf.Max(crest, c.y);
                Assert.Greater(crest, 0.25f, "güney geçişi köprü üstünden (WP-030 boğazı), zemin şeridinden değil: tepe " + crest.ToString("F2"));
                // The deck itself is the crossing: north approach → south bank straight over the five segments (rail posts
                // out of the bake), not a 30 m detour around pond 01.
                Assert.IsTrue(NavMesh.CalculatePath(new Vector3(4f, 0f, -22.5f), new Vector3(4f, 0f, -36f), NavMesh.AllAreas, path));
                Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "köprü kuzey ↔ güney");
                float deck = 0f;
                for (int c = 1; c < path.corners.Length; c++) deck += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                Assert.Less(deck, 16f, "köprü geçişi güverte üstünden (13,5 m), dolanma değil: " + deck.ToString("F1"));
                int waterVolumes = 0;
                foreach (NavMeshModifierVolume v in UnityEngine.Object.FindObjectsOfType<NavMeshModifierVolume>(true))
                    if (v.area == ArtSurfaceImporter.NavAreaNotWalkable && v.name.StartsWith("NavNotWalkable_", StringComparison.Ordinal)) waterVolumes++;
                Assert.GreaterOrEqual(waterVolumes, 30, "havuz elipsi NavMesh hacimleri (3 havuz × 0,5 m satır)");
                Debug.Log("[DD-TEST] wp038.navmesh north.y=" + north.position.y.ToString("F4") + " west.y=" + west.position.y.ToString("F4") + " southCrest=" + crest.ToString("F2") + " deckPath=" + deck.ToString("F1") + " basinRows=" + waterVolumes);
            }
            finally
            {
                NavMesh.RemoveNavMeshData(handle);
            }
        }

        [Test]
        public void SozlesmeV13_CevreMeshSikistirmaMedium_TuretilmisMaske1024_PlakalarUV2siz()
        {
            var hatalar = new List<string>();
            int env = 0, plates = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtImportContract.ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ArtImportContract.IsContractModel(path) || ArtCharacterContract.IsCharacterModel(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                if (importer.meshCompression != ArtImportContract.MeshCompressionOf(path)) hatalar.Add(path + ": meshCompression " + importer.meshCompression);
                if (path.StartsWith(ArtImportContract.EnvironmentRoot + "/", StringComparison.Ordinal))
                {
                    env++;
                    if (importer.meshCompression != ModelImporterMeshCompression.Medium) hatalar.Add(path + ": çevre modeli Medium değil");
                }

                if (ArtImportContract.IsSurfaceModel(path))
                {
                    plates++;
                    if (importer.generateSecondaryUV) hatalar.Add(path + ": plaka lightmap UV üretmemeli (probe-lit)");
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtImportContract.ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ArtImportContract.IsContractTexture(path)) continue;
                ArtImportContract.TextureRole role = ArtImportContract.RoleOf(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (ArtImportContract.IsDerivedMaskRole(role) && importer.maxTextureSize > ArtImportContract.DerivedMaskMaxSize) hatalar.Add(path + ": türetilmiş maske " + importer.maxTextureSize + " > 1024");
            }

            var character = AssetImporter.GetAtPath(ArtCharacterContract.PlayerAvatarSource) as ModelImporter;
            Assert.IsNotNull(character);
            Assert.AreEqual(ModelImporterMeshCompression.Off, character.meshCompression, "skinned karakterler Off (deforme yüzeyde normal/tangent hassasiyeti)");
            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            Assert.Greater(env, 200); Assert.AreEqual(74, plates);
            Assert.AreEqual(1024, ArtImportContract.MaxSizeOf(ArtImportContract.TextureClass.Character, ArtImportContract.TextureRole.MetallicSmoothness));
            Assert.AreEqual(2048, ArtImportContract.MaxSizeOf(ArtImportContract.TextureClass.Character, ArtImportContract.TextureRole.BaseColor));
            Debug.Log("[DD-TEST] wp038.contract env=" + env + " plates=" + plates);
        }
    }
}
