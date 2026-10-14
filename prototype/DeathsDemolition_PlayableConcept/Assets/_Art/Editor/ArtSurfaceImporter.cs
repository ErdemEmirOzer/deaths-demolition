using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.Feedback;
using DD.Prototype.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-038 · AP-015P world surfaces (ADR-038-1): the ground / route / paving plates and the three water
    /// meshes of <c>AP015-surfaces-manifest.json</c> (dd-surfaces-v1) placed at identity under
    /// <see cref="RootName"/> in World_AP005. Every FBX is already in world space (AP-001 metres, Unity
    /// bakeAxisConversion), so a plate is instantiated as-is: LOD0 / LOD1 → LODGroup (switch at the plan's
    /// 45 m, never culled), <c>_COL</c> → MeshCollider on the Environment layer (the NavMesh walks on it),
    /// kerbs render-only (NavMesh ignore, stone material), water → <c>M_water_v2</c> (no collider). One
    /// <c>M_ground_blend</c> (DD/GroundBlend, vertex mask) for every plate. The stand-in ground and its
    /// pond beds (WP-014) and the route-line rendering are gone; the golden-slice plates stay.
    /// Plates contribute to the bake as occluders / bounce but stay probe-lit (no lightmap UVs, contract).
    /// SurfaceTag (WP-021C) per plate from the manifest surface: Dirt / Stone / Water.
    /// </summary>
    public static class ArtSurfaceImporter
    {
        private const string LogPrefix = "[ArtSurfaces] ";
        public const string RootName = "Surfaces (AP-015P)";
        public const string ManifestPath = ArtImportContract.SurfacesRoot + "/AP015-surfaces-manifest.json";
        public const string GroundMaterialPath = ArtPipelinePaths.Materials + "/M_ground_blend.mat";
        public const string WaterMaterialPath = ArtPipelinePaths.Materials + "/M_water_v2.mat";
        /// <summary>Kerb runs: the stone material with the AP-015P M4 albedo lift (× 1.20) so the rim reads above the gravel field (WP-038 AD MUST-4).</summary>
        public const string KerbMaterialPath = ArtPipelinePaths.Materials + "/M_kerb_stone.mat";

        // ------------------------------------------------------------------ WP-038E · AP-018 Look B wet surfaces
        /// <summary>AP-018 roles whose wet packed map gets a derived MetallicSmoothness (the dry tiles' repack), bound by DD/GroundBlend.</summary>
        public static readonly string[] WetPackedRoles = { "wet_stone", "wet_earth", "wet_gravel" };
        /// <summary>AP-018 role_stats.wet_tint_linear_rgb (v001): the wet albedo of the roles that ship no wet tile.</summary>
        public static readonly Color WetTintEarth = new Color(0.522f, 0.539f, 0.567f, 1f), WetTintGravel = new Color(0.545f, 0.568f, 0.609f, 1f);
        /// <summary>Puddle score = vertex wetness (≤ ~0.55 on the AP-015P plates) × damp tile (≈ 0.82) × puddle tile: threshold 0.30, full at 0.42.</summary>
        public const float PuddleThreshold = 0.30f, PuddleRamp = 0.12f, PuddleSmoothness = 0.70f, ShoreFloor = 0.5f;
        public static readonly Vector4 ShoreBounds = new Vector4(-16f, -46f, 16f, -14f);   // AP018_shore_wetness_pond world bounds (top = north)
        public const string PuddleDecalMaterialPath = ArtPipelinePaths.Materials + "/M_decal_puddle.mat";
        public const string DecalShaderName = "Shader Graphs/Decal";
        public const string DecalFeatureTypeName = "UnityEngine.Rendering.Universal.DecalRendererFeature";
        public const string PuddleDecalsName = "PuddleDecals (AP-018)";

        // ------------------------------------------------------------------ WP-038E (2) · AP-019 sky / horizon plates
        public const string SkyRootName = "Sky (AP-019)";
        public const string SkyPlateShaderName = "DD/SkyPlate";
        public const string SkyMeshFolder = ArtImportContract.EnvironmentRoot + "/Sky/AP019";
        /// <summary>
        /// AP-019 placement contract (manifest files[]): cylinders centred on the map, base −1 m, unlit / fog-excluded, drawn
        /// right after the skybox. The rings sit beyond the gameplay camera's 260 m far clip (PrototypeCameraRig, S-02): the
        /// DD/SkyPlate vertex shader pins their depth just inside the far plane (skybox-style), so the contract radii are used as
        /// delivered and nothing is clipped; a 0.55 scale (tried first) put the treeline 53 m from the 90 m presentation camera.
        /// </summary>
        public const float HorizonScale = 1.0f;
        public static readonly (string layer, float radius, float height, int repeats, float azimuthOffset)[] HorizonLayers =
        {
            ("near", 260f, 51.1f, 8, 0f),
            ("mid", 330f, 86.4f, 6, 15f),
            ("far", 420f, 178.2f, 4, 22.5f)
        };
        public const float HorizonBaseY = -1f;
        public const int HorizonSegmentsPerRepeat = 12;

        /// <summary>Horizon materials (DD/SkyPlate, one per layer) — built with the surface materials.</summary>
        public static string HorizonMaterialPath(string layer) { return ArtPipelinePaths.Materials + "/M_horizon_" + layer + ".mat"; }

        /// <summary>
        /// Three inward-facing cylinder strips around the map origin, u = repeats around 360° (offset per layer so seams never
        /// stack), v = 0 at the base → 1 at the top. Meshes are saved under Environment/Sky/AP019 (regenerated, byte-stable).
        /// </summary>
        public static int BuildHorizonPlates(GameObject parent)
        {
            var root = new GameObject(SkyRootName);
            root.transform.SetParent(parent.transform, false);
            EnsureFolder(SkyMeshFolder);
            int built = 0;
            foreach ((string layer, float radius, float height, int repeats, float azimuthOffset) l in HorizonLayers)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(HorizonMaterialPath(l.layer));
                if (material == null) throw new InvalidOperationException(LogPrefix + "build the surface materials first (" + HorizonMaterialPath(l.layer) + ")");
                float r = l.radius * HorizonScale, h = l.height * HorizonScale, baseY = HorizonBaseY * HorizonScale;
                int segments = l.repeats * HorizonSegmentsPerRepeat;
                var verts = new Vector3[(segments + 1) * 2];
                var uvs = new Vector2[verts.Length];
                var tris = new int[segments * 6];
                for (int i = 0; i <= segments; i++)
                {
                    float t = (float)i / segments;
                    float ang = (l.azimuthOffset + t * 360f) * Mathf.Deg2Rad;
                    float x = Mathf.Sin(ang) * r, z = Mathf.Cos(ang) * r;
                    verts[i * 2] = new Vector3(x, baseY, z);
                    verts[i * 2 + 1] = new Vector3(x, baseY + h, z);
                    uvs[i * 2] = new Vector2(t * l.repeats, 0f);
                    uvs[i * 2 + 1] = new Vector2(t * l.repeats, 1f);
                }

                for (int i = 0; i < segments; i++)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                    // Wound to face the map centre (inside of the cylinder); the shader culls neither side.
                    tris[i * 6] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                    tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
                }

                string meshPath = SkyMeshFolder + "/SM_horizon_" + l.layer + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                bool created = mesh == null;
                if (created) mesh = new Mesh();
                mesh.name = "SM_horizon_" + l.layer;
                mesh.Clear();
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                if (created) AssetDatabase.CreateAsset(mesh, meshPath); else EditorUtility.SetDirty(mesh);

                var go = new GameObject("Horizon_" + l.layer);
                go.transform.SetParent(root.transform, false);
                go.layer = PrototypeLayers.Environment;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                built++;
            }

            return built;
        }
        /// <summary>Puddle decal cells: 4×2 sheet, 1.2 m world size, seeded where the plate wetness × puddle tile peaks; ≥ 1.8 m apart, capped.</summary>
        public const int PuddleDecalCap = 160;
        public const float PuddleDecalSpacing = 1.8f, PuddleDecalSize = 1.2f, PuddleDecalDepth = 0.4f, PuddleDecalDrawDistance = 45f;

        /// <summary>Derived MetallicSmoothness for the AP-018 wet packed maps (same repack as the dry Ground tiles).</summary>
        public static void BuildWetTextures()
        {
            for (int i = 0; i < WetPackedRoles.Length; i++) ArtTextureRepack.BuildMask(WetPackedRoles[i]);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// URP Decal Renderer Feature on the WP-029 renderer (screen space, low normal blend, 60 m): the AP-018 puddle projectors.
        /// Created by type name like the WP-029 SSAO feature (the URP 14 class is internal); idempotent.
        /// </summary>
        public static void ConfigureDecalFeature()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ArtLightingBake.UrpRendererPath);
            if (renderer == null) throw new FileNotFoundException(LogPrefix + "renderer data missing", ArtLightingBake.UrpRendererPath);
            Type type = typeof(UniversalRendererData).Assembly.GetType(DecalFeatureTypeName);
            if (type == null) throw new InvalidOperationException(LogPrefix + "decal feature type not found: " + DecalFeatureTypeName);
            ScriptableRendererFeature feature = null;
            foreach (ScriptableRendererFeature f in renderer.rendererFeatures) if (f != null && f.GetType() == type) feature = f;
            var so = new SerializedObject(renderer);
            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = "DecalRendererFeature";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                SerializedProperty list = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var fso = new SerializedObject(feature);
            SerializedProperty settings = fso.FindProperty("m_Settings");
            SerializedProperty technique = settings.FindPropertyRelative("technique");
            if (technique != null) technique.enumValueIndex = 2;   // ScreenSpace (URP 14: Automatic 0, DBuffer 1, ScreenSpace 2) — DepthNormals already on for SSAO
            SerializedProperty distance = settings.FindPropertyRelative("maxDrawDistance");
            if (distance != null) distance.floatValue = 60f;
            SerializedProperty blend = settings.FindPropertyRelative("screenSpaceSettings.normalBlend");
            if (blend != null) blend.enumValueIndex = 0;          // Low
            fso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "decal feature: screen space, 60 m, normal blend low → " + ArtLightingBake.UrpRendererPath);
        }
        public const float KerbAlbedoLift = 1.2f;
        /// <summary>
        /// WP-038 art-direction round (KOŞULLU KABUL 85 %): P-05's 0.60 gravel tint left earth ≈ gravel at the
        /// gameplay distance → 0.72 (earth &lt; gravel ΔY ≥ 0.02 linear); the water sparkle cap 0.30 → 0.18 and the
        /// caustic sheet 0.18 → 0.08 so the south ponds stop being the brightest element of the frame under the
        /// overcast Look B (pond hot spot ≤ 2.5× the gravel, no readable Voronoi net at 19.9 m).
        /// </summary>
        public const float GravelTint = 0.72f, SparkleCap = 0.18f, CausticIntensity = 0.08f;
        /// <summary>
        /// P-05 contract 0.88. AD SHOULD-5 (0.82 when the pond hot spot stays > 0.55 sRGB) was tried and reverted: a lower
        /// smoothness widens the GGX lobe, so the south pond's lit area grew (pond mean 0.234 → 0.270, p99 0.66 → 0.74 sRGB)
        /// instead of shrinking — the right tool is a direct-specular scale, not smoothness (WP-038B handover).
        /// </summary>
        public const float WaterSmoothness = 0.88f;
        /// <summary>
        /// WP-038D (AD SHOULD-5, second try — the smoothness route widened the lobe): share of the direct main-light GGX specular kept
        /// on the water. 0.40 → south pond p99 0.67 → 0.53 sRGB at 19.9 m (AD ceiling 0.55); the residual 3.75× over the gravel is
        /// sky / probe reflection, not the sun hot spot.
        /// </summary>
        public const float WaterSpecularScale = 0.40f;
        public const string GroundShaderName = "DD/GroundBlend";
        public const string WaterShaderName = "DD/Water v2";
        public const float LodSwitchMetres = 45f;
        public const string Schema = "dd-surfaces-v1";

        public static bool Available => File.Exists(ManifestPath);

        /// <summary>Every FBX the manifest ships (asset gate), in manifest order.</summary>
        public static List<string> ManifestFbxPaths()
        {
            var result = new List<string>();
            if (!Available) return result;
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ManifestPath)), "surfaces");
            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "assets"), "assets"))
            {
                Dictionary<string, object> a = ArtJson.AsObject(o, "asset");
                bool isWater = ArtJson.AsString(a["kind"]) == "water_surface";
                string f = ArtJson.AsString(ArtJson.AsObject(a["fbx"], "fbx")["file"]);
                result.Add((isWater ? ArtImportContract.WaterRoot : ArtImportContract.SurfacesRoot) + "/" + Path.GetFileName(f));
            }

            return result;
        }

        /// <summary>The seven water v2 input textures (contract names under Water/AP015/Textures).</summary>
        public static readonly string[] WaterTextures =
        {
            ArtImportContract.WaterRoot + "/Textures/T_water_large_Normal.png", ArtImportContract.WaterRoot + "/Textures/T_water_small_Normal.png",
            ArtImportContract.WaterRoot + "/Textures/T_water_flow_Mask.png", ArtImportContract.WaterRoot + "/Textures/T_water_foam_Mask.png",
            ArtImportContract.WaterRoot + "/Textures/T_water_sparkle_Mask.png", ArtImportContract.WaterRoot + "/Textures/T_water_caustic_Mask.png",
            ArtImportContract.WaterRoot + "/Textures/T_water_rings_Sheet.png"
        };

        public sealed class Report
        {
            public int Plates, Water, Colliders, Kerbs, Lods, Tags, WaterNavVolumes, PuddleDecals, HorizonPlates;
            public string ManifestSha256;
        }

        // ================================================================== materials

        /// <summary>M_ground_blend (earth / gravel / stone tiles, P-05 parameters) and M_water_v2 (the seven AP-015P inputs, contract values).</summary>
        public static void BuildMaterials()
        {
            Shader ground = Shader.Find(GroundShaderName);
            Shader water = Shader.Find(WaterShaderName);
            if (ground == null || water == null) throw new InvalidOperationException(LogPrefix + "shaders missing: " + GroundShaderName + " / " + WaterShaderName + " (Assets/_Art/Shaders)");

            Material g = LoadOrCreate(GroundMaterialPath, ground, "M_ground_blend");
            g.SetTexture("_EarthMap", Load(ArtPipelinePaths.SourceTexture("earth", "BaseColor")));
            g.SetTexture("_EarthNormal", Load(ArtPipelinePaths.SourceTexture("earth", "Normal")));
            g.SetTexture("_EarthMask", Load(ArtPipelinePaths.DerivedMask("earth")));
            g.SetTexture("_GravelMap", Load(ArtPipelinePaths.SourceTexture("gravel", "BaseColor")));
            g.SetTexture("_StoneMap", Load(ArtPipelinePaths.SourceTexture("stone", "BaseColor")));
            g.SetTexture("_StoneNormal", Load(ArtPipelinePaths.SourceTexture("stone", "Normal")));
            g.SetTexture("_StoneMask", Load(ArtPipelinePaths.DerivedMask("stone")));
            g.SetFloat("_Tile", 4f);
            // WP-038E · AP-018 wet surfaces (Look B): tiles, wet packed → derived masks, tints, puddles, shore map.
            g.SetTexture("_DampMask", Load(ArtPipelinePaths.SourceTexture("wet_damp", "Mask")));
            g.SetTexture("_PuddleMask", Load(ArtPipelinePaths.SourceTexture("wet_puddle", "Mask")));
            g.SetTexture("_ShoreMask", Load(ArtPipelinePaths.SourceTexture("wet_shore", "Mask")));
            g.SetVector("_ShoreBounds", ShoreBounds);
            g.SetFloat("_ShoreFloor", ShoreFloor);
            g.SetTexture("_WetStoneMap", Load(ArtPipelinePaths.SourceTexture("wet_stone", "BaseColor")));
            g.SetTexture("_WetStoneMask", Load(ArtPipelinePaths.DerivedMask("wet_stone")));
            g.SetTexture("_WetEarthMask", Load(ArtPipelinePaths.DerivedMask("wet_earth")));
            g.SetTexture("_WetGravelMask", Load(ArtPipelinePaths.DerivedMask("wet_gravel")));
            g.SetColor("_WetTintEarth", WetTintEarth);
            g.SetColor("_WetTintGravel", WetTintGravel);
            g.SetFloat("_PuddleThreshold", PuddleThreshold);
            g.SetFloat("_PuddleRamp", PuddleRamp);
            g.SetFloat("_PuddleSmoothness", PuddleSmoothness);
            g.SetFloat("_GravelTint", GravelTint);
            g.SetFloat("_GravelSmoothness", 0.15f);
            g.SetFloat("_EarthSmoothness", 0.08f);
            g.SetFloat("_StoneSmoothness", 0.43f);
            g.SetFloat("_UseMasks", 1f);
            g.SetFloat("_WetDarken", 0f);   // WP-038E: the AP-018 tints / wet tile carry the darkening
            g.SetFloat("_WetSmoothness", 0.60f);
            g.SetFloat("_NormalStrength", 1f);
            g.enableInstancing = false;
            g.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(g);

            string wt = ArtImportContract.WaterRoot + "/Textures";
            Material w = LoadOrCreate(WaterMaterialPath, water, "M_water_v2");
            w.SetColor("_BaseColor", new Color(0.180f, 0.294f, 0.310f, 0.92f));
            w.SetColor("_ShallowColor", new Color(0.240f, 0.330f, 0.330f, 0.45f));
            w.SetFloat("_Smoothness", WaterSmoothness);
            w.SetFloat("_SpecularScale", WaterSpecularScale);
            w.SetFloat("_FresnelPower", 4f);
            w.SetFloat("_DepthFade", 0.6f);
            w.SetTexture("_NormalLarge", Load(wt + "/T_water_large_Normal.png"));
            w.SetTexture("_NormalSmall", Load(wt + "/T_water_small_Normal.png"));
            w.SetFloat("_NormalLargeTile", 4f); w.SetFloat("_NormalSmallTile", 1f);
            w.SetFloat("_NormalLargeSpeed", 0.03f); w.SetFloat("_NormalSmallSpeed", 0.06f);
            w.SetFloat("_NormalLargeStrength", 0.35f); w.SetFloat("_NormalSmallStrength", 0.25f);
            w.SetTexture("_FlowMap", Load(wt + "/T_water_flow_Mask.png"));
            w.SetVector("_FlowBounds", new Vector4(-16f, -46f, 16f, -14f));
            w.SetFloat("_FlowSpeed", 0.04f); w.SetFloat("_FlowPeriod", 3f);
            w.SetTexture("_FoamMap", Load(wt + "/T_water_foam_Mask.png")); w.SetFloat("_FoamTile", 2f); w.SetFloat("_FoamIntensity", 0.20f);
            w.SetTexture("_SparkleMap", Load(wt + "/T_water_sparkle_Mask.png")); w.SetFloat("_SparkleTile", 1.5f); w.SetFloat("_SparkleDensity", 0.15f); w.SetFloat("_SparkleCap", SparkleCap);
            w.SetTexture("_CausticMap", Load(wt + "/T_water_caustic_Mask.png")); w.SetFloat("_CausticTile", 2f); w.SetFloat("_CausticFps", 8f); w.SetFloat("_CausticIntensity", CausticIntensity); w.SetFloat("_CausticMinDepth", 0.15f);
            w.SetFloat("_Surface", 1f); w.SetFloat("_Blend", 0f); w.SetFloat("_Cull", 2f);
            w.SetFloat("_SrcBlend", 5f); w.SetFloat("_DstBlend", 10f); w.SetFloat("_ZWrite", 0f);
            w.renderQueue = (int)RenderQueue.Transparent;
            w.enableInstancing = false;
            EditorUtility.SetDirty(w);

            var stone = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("stone"));
            if (stone == null) throw new InvalidOperationException(LogPrefix + "M_stone missing (build the library materials first)");
            Material k = LoadOrCreate(KerbMaterialPath, stone.shader, "M_kerb_stone");
            k.CopyPropertiesFromMaterial(stone);
            k.SetColor("_BaseColor", new Color(KerbAlbedoLift, KerbAlbedoLift, KerbAlbedoLift, 1f));
            k.enableInstancing = stone.enableInstancing;
            EditorUtility.SetDirty(k);

            // WP-038E (2) · AP-019 horizon plates: one DD/SkyPlate material per layer.
            Shader skyPlate = Shader.Find(SkyPlateShaderName);
            if (skyPlate == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + SkyPlateShaderName);
            foreach ((string layer, float radius, float height, int repeats, float azimuthOffset) l in HorizonLayers)
            {
                Material hm = LoadOrCreate(HorizonMaterialPath(l.layer), skyPlate, "M_horizon_" + l.layer);
                hm.SetTexture("_BaseMap", Load(ArtPipelinePaths.SourceTexture("horizon_" + l.layer, "Sheet")));
                hm.SetColor("_BaseColor", Color.white);
                hm.enableInstancing = false;
                EditorUtility.SetDirty(hm);
            }

            // WP-038E · puddle decal (URP Decal shader graph: Base_Map RGBA sheet 4×2 + Normal_Map; cells picked per projector by uvScale / uvBias).
            Shader decal = Shader.Find(DecalShaderName);
            if (decal == null) throw new InvalidOperationException(LogPrefix + "decal shader missing: " + DecalShaderName);
            Material d = LoadOrCreate(PuddleDecalMaterialPath, decal, "M_decal_puddle");
            d.SetTexture("Base_Map", Load(ArtPipelinePaths.SourceTexture("decal_puddle", "Sheet")));
            d.SetTexture("Normal_Map", Load(ArtPipelinePaths.SourceTexture("decal_puddle", "Normal")));
            d.SetFloat("Normal_Blend", 1f);
            d.enableInstancing = true;
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "materials: M_ground_blend (DD/GroundBlend) + M_water_v2 (DD/Water v2)");
        }

        // ================================================================== scene

        /// <summary>Builds the surfaces under <paramref name="root"/> (replacing an earlier build) and returns the counts.</summary>
        public static Report Build(Transform root, Transform instances, List<(Vector2 centre, Vector2 semi, float yaw)> basinHoles)
        {
            if (!Available) throw new FileNotFoundException(LogPrefix + "manifest missing", ManifestPath);
            byte[] bytes = File.ReadAllBytes(ManifestPath);
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "surfaces");
            string schema = ArtJson.AsString(ArtJson.Require(doc, "schema"), "schema");
            if (schema != Schema) throw new FormatException(LogPrefix + "schema '" + schema + "' != " + Schema);

            var ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            var water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            var stone = AssetDatabase.LoadAssetAtPath<Material>(KerbMaterialPath);
            if (ground == null || water == null || stone == null) throw new InvalidOperationException(LogPrefix + "build the surface materials first (M_ground_blend / M_water_v2 / M_kerb_stone)");

            Transform old = root.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject(RootName);
            parent.transform.SetParent(root, false);
            var report = new Report { ManifestSha256 = Sha256(bytes) };
            int env = PrototypeLayers.Environment;

            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "assets"), "assets"))
            {
                Dictionary<string, object> a = ArtJson.AsObject(o, "asset");
                string id = ArtJson.AsString(a["id"]);
                string kind = ArtJson.AsString(a["kind"]);
                bool isWater = kind == "water_surface";
                string fbxFile = ArtJson.AsString(ArtJson.AsObject(a["fbx"], "fbx")["file"]);
                string fbxPath = (isWater ? ArtImportContract.WaterRoot : ArtImportContract.SurfacesRoot) + "/" + Path.GetFileName(fbxFile);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (model == null) throw new FileNotFoundException(LogPrefix + id + ": FBX missing", fbxPath);

                var lods = new List<string>();
                foreach (object l in ArtJson.AsList(a["lods"], "lods")) lods.Add(ArtJson.AsString(l));
                string colliderMesh = a.ContainsKey("collider_mesh") && a["collider_mesh"] != null ? ArtJson.AsString(a["collider_mesh"]) : null;
                var kerbs = new List<string>();
                if (a.ContainsKey("kerbs")) foreach (object k in ArtJson.AsList(a["kerbs"], "kerbs")) kerbs.Add(ArtJson.AsString(k));

                var go = new GameObject(id);
                go.transform.SetParent(parent.transform, false);
                go.layer = env;
                SurfaceKind surface = SurfaceOf(a, kind);

                // LOD renderers.
                var lodRenderers = new List<Renderer>[lods.Count];
                for (int i = 0; i < lods.Count; i++)
                {
                    MeshFilter src = FindMesh(model, lods[i]);
                    if (src == null) throw new InvalidOperationException(LogPrefix + id + ": object '" + lods[i] + "' missing in " + fbxPath);
                    var part = new GameObject(lods[i]);
                    part.transform.SetParent(go.transform, false);
                    CopyLocalTransform(src.transform, part.transform);
                    part.layer = env;
                    part.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
                    var mr = part.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = isWater ? water : ground;
                    // WP-038G (M3 closure K1): flat plates never cast a useful shadow — Off saves a draw per cascade per plate; the kerbs keep theirs.
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = true;
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.reflectionProbeUsage = isWater ? ReflectionProbeUsage.BlendProbes : ReflectionProbeUsage.Off;
                    mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    lodRenderers[i] = new List<Renderer> { mr };
                    report.Lods++;
                }

                // Kerbs: render-only stone runs on the route's LOD0 level (small, never culled with the plate).
                foreach (string k in kerbs)
                {
                    MeshFilter src = FindMesh(model, k);
                    if (src == null) throw new InvalidOperationException(LogPrefix + id + ": kerb '" + k + "' missing in " + fbxPath);
                    var part = new GameObject(k);
                    part.transform.SetParent(go.transform, false);
                    CopyLocalTransform(src.transform, part.transform);
                    part.layer = env;
                    part.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
                    var mr = part.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = stone;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    lodRenderers[0].Add(mr);
                    report.Kerbs++;
                }

                if (lods.Count > 1)
                {
                    var group = go.AddComponent<LODGroup>();
                    group.fadeMode = LODFadeMode.None;
                    // Screen-relative heights from the 45 m switch: the plate's own bounds decide the ratio.
                    Bounds b = lodRenderers[0][0].bounds;
                    float size = Mathf.Max(b.size.x, b.size.z, 1f);
                    float h45 = Mathf.Clamp(size / (2f * LodSwitchMetres * Mathf.Tan(0.5f * 42f * Mathf.Deg2Rad)), 0.02f, 0.9f);
                    group.SetLODs(new[] { new LOD(h45, lodRenderers[0].ToArray()), new LOD(0.0f, lodRenderers[1].ToArray()) });   // LOD1 to zero: never culled
                    group.RecalculateBounds();
                }

                // Collider: the COL mesh (walkable) on the Environment layer; the NavMesh bakes on it.
                if (!string.IsNullOrEmpty(colliderMesh) && !isWater)
                {
                    MeshFilter src = FindMesh(model, colliderMesh);
                    if (src == null) throw new InvalidOperationException(LogPrefix + id + ": collider '" + colliderMesh + "' missing in " + fbxPath);
                    var col = new GameObject(colliderMesh);
                    col.transform.SetParent(go.transform, false);
                    CopyLocalTransform(src.transform, col.transform);
                    col.layer = env;
                    var mc = col.AddComponent<MeshCollider>();
                    mc.sharedMesh = src.sharedMesh;
                    mc.convex = false;
                    report.Colliders++;
                }

                // Static flags: occludee + reflection + ContributeGI (occluder / bounce in the bake) — receive from probes (no lightmap UVs).
                var flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                if (!isWater) flags |= StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic;   // WP-038G: 71 plates + 27 kerbs on one material → a few static batches (was ~100 renderers × passes)
                foreach (Transform c in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(c.gameObject, flags);
                foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var so = new SerializedObject(mr);
                    SerializedProperty rgi = so.FindProperty("m_ReceiveGI");
                    if (rgi != null) { rgi.intValue = (int)ReceiveGI.LightProbes; so.ApplyModifiedPropertiesWithoutUndo(); }
                }

                // Kerbs never enter the NavMesh (render-only runs beside the walkable corridor).
                if (kerbs.Count > 0)
                {
                    foreach (string k in kerbs)
                    {
                        Transform kt = go.transform.Find(k);
                        var mod = kt.gameObject.AddComponent<NavMeshModifier>();
                        mod.ignoreFromBuild = true;
                    }
                }

                go.AddComponent<SurfaceTag>().Surface = surface;
                report.Tags++;
                if (isWater) report.Water++; else report.Plates++;
            }

            // NavMesh pond footprint. The AP-015P plates follow the real basins and leave a 0.75 m ground strip between the
            // south ponds 02 / 03 beside the raised shore module; the crowd took it at ground level and bypassed the WP-030
            // bridge / shore-module choke (RegionNavSliceTests bridge test). The WP-014 basin ellipses (placement data:
            // basin variation → half-axes, still the hole definition WP-030 was tuned on) become Not-Walkable volumes at
            // ground level only (0.5 m rows, −0.6..+0.12 m) — the shore modules and the bridge deck (≈ 0.3 m) keep their
            // NavMesh, the ellipses overlap and close the isthmus. Volumes do not erode their surroundings (unlike a hole).
            var holesRoot = new GameObject("PondNavHoles");
            holesRoot.transform.SetParent(parent.transform, false);
            foreach ((Vector2 centre, Vector2 semi, float yaw) hole in basinHoles) report.WaterNavVolumes += BuildBasinNavVolumes(holesRoot, hole, env);

            // WP-038E · AP-018 puddle decals seeded from the plates' own wetness (readable LOD0 vertex colour) × the puddle tile.
            report.PuddleDecals = BuildPuddleDecals(parent, basinHoles);
            // WP-038E (2) · AP-019 horizon plates (three unlit cylinders; the sky band itself is the panoramic skybox, ArtSkyBuilder).
            report.HorizonPlates = BuildHorizonPlates(parent);

            // The v1 pond planes (AP-003B pond_water_surface placement instances) are replaced by the water meshes: keep the
            // instances (counts, NavMesh ignore) but stop rendering them.
            int hidden = 0;
            if (instances != null)
            {
                for (int i = 0; i < instances.childCount; i++)
                {
                    Transform t = instances.GetChild(i);
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (source == null || !source.name.Contains("pond_water_surface")) continue;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) { if (r.enabled) { r.enabled = false; hidden++; } }
                }
            }

            Debug.Log(LogPrefix + "built " + report.Plates + " plates (" + report.Lods + " LOD renderers, " + report.Kerbs + " kerbs, " + report.Colliders + " colliders) + " + report.Water + " water meshes (" + report.WaterNavVolumes + " not-walkable basin NavMesh rows, " + report.PuddleDecals + " puddle decals, " + report.HorizonPlates + " horizon plates), " + report.Tags + " surface tags; v1 pond renderers hidden " + hidden + "; manifest " + report.ManifestSha256.Substring(0, 12) + "…");
            return report;
        }

        /// <summary>
        /// Diagnostic: NavMesh coverage on a grid, the south-pond topology (pond centres / 02–03 isthmus not walkable at
        /// ground level, bridge deck kept, centre → south-bank crossing over the shore module / deck — the WP-030 choke)
        /// and the plate colliders' normal direction (batch: -executeMethod …NavMeshAuditCli).
        /// </summary>
        public static void NavMeshAuditCli()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(ArtLibraryPaths.Ap005NavMeshData);
            UnityEngine.AI.NavMesh.RemoveAllNavMeshData();
            UnityEngine.AI.NavMesh.AddNavMeshData(data);
            var sb = new StringBuilder();
            int hits = 0, total = 0;
            for (int z = -44; z <= 44; z += 8)
            {
                for (int x = -44; x <= 44; x += 8)
                {
                    total++;
                    bool ok = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(x, 0f, z), out UnityEngine.AI.NavMeshHit hit, 1f, UnityEngine.AI.NavMesh.AllAreas);
                    if (ok) hits++;
                    sb.Append(ok ? (hit.position.y > -0.019f ? 'P' : 's') : '.');
                }

                sb.Append('\n');
            }

            // The deck's own NavMesh strip is ~0.1 m wide (rail posts vs the 0.5 m agent radius — unchanged since WP-014), so
            // the crossing runs over the AP-003B shore modules beside it; the isthmus between ponds 02 / 03 must stay closed
            // at ground level (water NavMesh rows), or the crowd bypasses the choke.
            Vector3[] probes = { new Vector3(4f, 0f, -22.5f), new Vector3(4f, 0.3f, -24.6f), new Vector3(4f, 0.3f, -30.1f), new Vector3(4f, 0.3f, -34.6f), new Vector3(4f, 0f, -36f),
                                 new Vector3(2.5f, 0f, -29.3f), new Vector3(-8.4f, 0f, -29f), new Vector3(10.2f, 0f, -36.2f), new Vector3(-4.2f, 0f, -27.5f), new Vector3(-3.5f, 0.3f, -28f) };
            foreach (Vector3 pr in probes)
            {
                bool ok = UnityEngine.AI.NavMesh.SamplePosition(pr, out UnityEngine.AI.NavMeshHit hit, 0.3f, UnityEngine.AI.NavMesh.AllAreas);
                sb.Append("probe ").Append(pr.ToString("F1")).Append(ok ? " -> " + hit.position.ToString("F3") + " mask " + hit.mask : " -> none").Append('\n');
            }

            var path = new UnityEngine.AI.NavMeshPath();
            Vector3[] from = { new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, -10f), new Vector3(4f, 0f, -22.5f) };
            Vector3[] to = { new Vector3(4f, 0f, -43f), new Vector3(4f, 0f, -30.1f), new Vector3(4f, 0f, -36f) };
            for (int i = 0; i < from.Length; i++)
            {
                UnityEngine.AI.NavMesh.SamplePosition(from[i], out UnityEngine.AI.NavMeshHit a, 2f, UnityEngine.AI.NavMesh.AllAreas);
                UnityEngine.AI.NavMesh.SamplePosition(to[i], out UnityEngine.AI.NavMeshHit b, 2f, UnityEngine.AI.NavMesh.AllAreas);
                UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, path);
                float len = 0f, crest = float.MinValue;
                for (int c = 0; c < path.corners.Length; c++)
                {
                    if (c > 0) len += Vector3.Distance(path.corners[c - 1], path.corners[c]);
                    crest = Mathf.Max(crest, path.corners[c].y);
                }

                sb.Append("path ").Append(from[i].ToString("F0")).Append(" -> ").Append(to[i].ToString("F0")).Append(": ").Append(path.status).Append(" corners ").Append(path.corners.Length)
                  .Append(" length ").Append(len.ToString("F1")).Append(" crest ").Append(crest.ToString("F2")).Append(" [");
                foreach (Vector3 c in path.corners) sb.Append(c.ToString("F1")).Append(' ');
                sb.Append("]\n");
            }

            UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            var root = GameObject.Find(RootName);
            int up = 0, down = 0;
            if (root != null)
            {
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh m = mf.sharedMesh; if (m == null) continue;
                    float f = GeometricUpFraction(m);
                    if (f >= 0.5f) up++; else down++;
                }

                foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    Mesh m = mc.sharedMesh; if (m == null) continue;
                    float f = GeometricUpFraction(m);
                    Bounds b = mc.bounds;
                    sb.Append(mc.name).Append(": cross-up ").Append(f.ToString("F2")).Append(" verts ").Append(m.vertexCount).Append(" tris ").Append(m.triangles.Length / 3)
                      .Append(" bounds ").Append(b.min.ToString("F2")).Append("..").Append(b.max.ToString("F2")).Append(" readable ").Append(m.isReadable).Append('\n');
                    if (f >= 0.5f) up++; else down++;
                }
            }

            Debug.Log(LogPrefix + "navmesh audit: grid hits " + hits + "/" + total + " (P = plate height, s = below), triangles " + tri.indices.Length / 3 + ", colliders up " + up + " / down " + down + "\n" + sb);
        }

        /// <summary>Diagnostic: the imported FBX hierarchy of one plate (transforms + mesh bounds) — orientation triage.</summary>
        public static void FbxAuditCli()
        {
            foreach (string f in new[] { ArtImportContract.SurfacesRoot + "/AP015_ground_east_01.fbx", ArtImportContract.SurfacesRoot + "/AP015_route_11.fbx", ArtImportContract.WaterRoot + "/AP015_water_pond_01.fbx", ArtLibraryPaths.Fbx("AP-005", "ground", "AP005_ground_V01.fbx") })
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(f);
                var sb = new StringBuilder(f).Append('\n');
                if (model == null) { sb.Append("  (missing)\n"); Debug.Log(LogPrefix + sb); continue; }
                var importer = AssetImporter.GetAtPath(f) as ModelImporter;
                sb.Append("  bakeAxisConversion ").Append(importer != null && importer.bakeAxisConversion).Append(" useFileUnits ").Append(importer != null && importer.useFileUnits).Append('\n');
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    var mf = t.GetComponent<MeshFilter>();
                    sb.Append("  ").Append(t.name).Append(" pos ").Append(t.localPosition.ToString("F3")).Append(" rot ").Append(t.localRotation.eulerAngles.ToString("F1")).Append(" scale ").Append(t.localScale.ToString("F2"));
                    if (mf != null && mf.sharedMesh != null) sb.Append(" mesh bounds ").Append(mf.sharedMesh.bounds.min.ToString("F2")).Append("..").Append(mf.sharedMesh.bounds.max.ToString("F2")).Append(" cross-up ").Append(GeometricUpFraction(mf.sharedMesh).ToString("F2"));
                    sb.Append('\n');
                }

                Debug.Log(LogPrefix + sb);
            }
        }

        /// <summary>Share of the mesh's triangle area whose geometric (winding) normal points up — the plates are horizontal, so anything under 0.5 is an inverted export.</summary>
        public static float GeometricUpFraction(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            double up = 0, total = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                double a = n.magnitude;
                total += a;
                if (n.y > 0) up += a;
            }

            return total > 1e-9 ? (float)(up / total) : 1f;
        }

        private static SurfaceKind SurfaceOf(Dictionary<string, object> a, string kind)
        {
            switch (kind)
            {
                case "water_surface": return SurfaceKind.Water;
                case "paving_plate": return SurfaceKind.Stone;
                case "route_plate": return a.ContainsKey("surface") && ArtJson.AsString(a["surface"]) == "Stone" ? SurfaceKind.Stone : SurfaceKind.Dirt;
                default: return SurfaceKind.Dirt;   // ground plates: earth / gravel field (routes carry the stone)
            }
        }

        /// <summary>The FBX child carries the Blender → Unity axis rotation (90° about X) while the vertices stay in Blender XY: the part copies that local transform.</summary>
        /// <summary>Not-walkable NavMesh area (Unity built-in index 1).</summary>
        public const int NavAreaNotWalkable = 1;
        public const float PondNavRowMetres = 0.5f, PondNavVolumeBottom = -0.6f, PondNavVolumeTop = 0.06f;

        /// <summary>
        /// One <see cref="NavMeshModifierVolume"/> (Not Walkable) per 0.5 m row of a basin ellipse (WP-014 hole: centre,
        /// half-axes, instance yaw as <c>PlacementImporter.InsideHole</c>, grown by the agent radius — the erosion a real
        /// hole would have): the row's x extent from the sampled boundary,
        /// −0.6 m to +0.06 m, so only ground-level spans inside the pond footprint are removed — the basin rim stones (≥ 0.15 m)
        /// and the bridge deck (≈ 0.3 m) above them keep their NavMesh. Environment layer (the NavMeshSurface mask).
        /// </summary>
        private static int BuildBasinNavVolumes(GameObject parent, (Vector2 centre, Vector2 semi, float yaw) hole, int layer)
        {
            var rows = new SortedDictionary<int, Vector2>();
            float a = hole.yaw * Mathf.Deg2Rad;
            // A real hole erodes the NavMesh around it by the agent radius; a modifier volume does not, so the ellipse is
            // grown by that radius (the 1.1 m ground strip between ponds 02 / 03 closed the same way in WP-014).
            float erosion = UnityEngine.AI.NavMesh.GetSettingsByID(0).agentRadius;
            if (!(erosion > 0f)) erosion = 0.5f;
            for (int i = 0; i < 360; i++)
            {
                float t = i * Mathf.Deg2Rad;
                float lx = (hole.semi.x + erosion) * Mathf.Cos(t), lz = (hole.semi.y + erosion) * Mathf.Sin(t);
                // Inverse of InsideHole's frame change: basin frame → world.
                float x = hole.centre.x + lx * Mathf.Cos(a) - lz * Mathf.Sin(a);
                float z = hole.centre.y + lx * Mathf.Sin(a) + lz * Mathf.Cos(a);
                int row = Mathf.FloorToInt(z / PondNavRowMetres);
                if (!rows.TryGetValue(row, out Vector2 range)) rows[row] = new Vector2(x, x);
                else rows[row] = new Vector2(Mathf.Min(range.x, x), Mathf.Max(range.y, x));
            }

            int count = 0;
            foreach (KeyValuePair<int, Vector2> r in rows)
            {
                float width = r.Value.y - r.Value.x;
                if (width < 0.25f) continue;
                var vol = new GameObject("NavNotWalkable_" + hole.centre.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "_" + hole.centre.y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "_" + count.ToString("00"));
                vol.transform.SetParent(parent.transform, false);
                vol.layer = layer;
                float z0 = r.Key * PondNavRowMetres;
                vol.transform.position = new Vector3(0.5f * (r.Value.x + r.Value.y), 0.5f * (PondNavVolumeBottom + PondNavVolumeTop), z0 + 0.5f * PondNavRowMetres);
                var m = vol.AddComponent<NavMeshModifierVolume>();
                m.size = new Vector3(width, PondNavVolumeTop - PondNavVolumeBottom, PondNavRowMetres);
                m.center = Vector3.zero;
                m.area = NavAreaNotWalkable;
                count++;
            }

            return count;
        }

        /// <summary>
        /// AP-018 puddle projectors. For every ground plate (readable LOD0: AP015_Mask.A = wetness per vertex) a vertex is a
        /// candidate when wetness × damp tile × puddle tile (both 4 m, sampled from the PNGs) clears <see cref="PuddleThreshold"/>
        /// and it lies outside the basin ellipses; candidates are taken strongest-first with a <see cref="PuddleDecalSpacing"/>
        /// exclusion up to <see cref="PuddleDecalCap"/>. Each projector is 1.2 × 1.2 m, points down, random yaw and sheet cell
        /// (uvScale 0.25 × 0.5 + bias), screen-space decal (albedo + normal); the shader's puddle path lifts the smoothness there.
        /// </summary>
        private static int BuildPuddleDecals(GameObject parent, List<(Vector2 centre, Vector2 semi, float yaw)> basinHoles)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(PuddleDecalMaterialPath);
            if (material == null) throw new InvalidOperationException(LogPrefix + "build the surface materials first (M_decal_puddle)");
            Texture2D damp = ReadPng(ArtPipelinePaths.SourceTexture("wet_damp", "Mask"));
            Texture2D puddle = ReadPng(ArtPipelinePaths.SourceTexture("wet_puddle", "Mask"));
            Texture2D shore = ReadPng(ArtPipelinePaths.SourceTexture("wet_shore", "Mask"));
            var candidates = new List<(float score, Vector3 pos)>();
            int platesSeen = 0, platesReadable = 0, vertsSeen = 0, vertsColoured = 0, a20 = 0, a35 = 0, a45 = 0; float maxA = 0f, maxScore = 0f;
            try
            {
                foreach (MeshFilter mf in parent.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.name.StartsWith("AP015_ground_", StringComparison.Ordinal) || !mf.name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
                    platesSeen++;
                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;
                    platesReadable++;
                    Vector3[] verts = mesh.vertices;
                    Color[] colours = mesh.colors;
                    vertsSeen += verts.Length;
                    if (colours == null || colours.Length != verts.Length) continue;
                    vertsColoured += colours.Length;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        float a = colours[i].a;
                        maxA = Mathf.Max(maxA, a);
                        if (a >= 0.20f) a20++; if (a >= 0.35f) a35++; if (a >= 0.45f) a45++;
                        if (a < 0.20f) continue;
                        Vector3 w = mf.transform.TransformPoint(verts[i]);
                        float wet = a * Tile(damp, w.x / 4f, w.z / 4f);
                        if (w.x >= ShoreBounds.x && w.x <= ShoreBounds.z && w.z >= ShoreBounds.y && w.z <= ShoreBounds.w)
                        {
                            float sh = shore.GetPixelBilinear((w.x - ShoreBounds.x) / (ShoreBounds.z - ShoreBounds.x), (w.z - ShoreBounds.y) / (ShoreBounds.w - ShoreBounds.y)).r;
                            wet *= Mathf.Max(sh, ShoreFloor);
                        }

                        // The vertex grid (0.5 m) rarely lands on a puddle-tile peak: probe a 3×3 ring (±0.4 m) and seed at the best sample.
                        float best = -1f; Vector3 at = w;
                        for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            Vector3 q = w + new Vector3(ox * 0.4f, 0f, oz * 0.4f);
                            float pv = Tile(puddle, q.x / 4f, q.z / 4f);
                            if (pv > best) { best = pv; at = q; }
                        }

                        float score = wet * best;
                        maxScore = Mathf.Max(maxScore, score);
                        if (score < PuddleThreshold) continue;
                        w = at;
                        bool inBasin = false;
                        foreach ((Vector2 centre, Vector2 semi, float yaw) hole in basinHoles)
                        {
                            float ang = hole.yaw * Mathf.Deg2Rad, dx = w.x - hole.centre.x, dz = w.z - hole.centre.y;
                            float lx = dx * Mathf.Cos(ang) + dz * Mathf.Sin(ang), lz = -dx * Mathf.Sin(ang) + dz * Mathf.Cos(ang);
                            float sx = hole.semi.x + PuddleDecalSize, sz = hole.semi.y + PuddleDecalSize;
                            if ((lx * lx) / (sx * sx) + (lz * lz) / (sz * sz) <= 1f) { inBasin = true; break; }
                        }

                        if (!inBasin) candidates.Add((score, w));
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(damp); UnityEngine.Object.DestroyImmediate(puddle); UnityEngine.Object.DestroyImmediate(shore);
            }

            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            Debug.Log(LogPrefix + "puddle seeds: plates " + platesSeen + " readable " + platesReadable + " vertices " + vertsSeen + " coloured " + vertsColoured + " A≥0.2/0.35/0.45 " + a20 + "/" + a35 + "/" + a45 + " maxA " + maxA.ToString("F2") + " maxScore " + maxScore.ToString("F2") + " candidates " + candidates.Count);
            var chosen = new List<Vector3>();
            foreach ((float score, Vector3 pos) c in candidates)
            {
                if (chosen.Count >= PuddleDecalCap) break;
                bool far = true;
                for (int i = 0; i < chosen.Count; i++)
                {
                    float dx = chosen[i].x - c.pos.x, dz = chosen[i].z - c.pos.z;
                    if (dx * dx + dz * dz < PuddleDecalSpacing * PuddleDecalSpacing) { far = false; break; }
                }

                if (far) chosen.Add(c.pos);
            }

            var root = new GameObject(PuddleDecalsName);
            root.transform.SetParent(parent.transform, false);
            var rng = new System.Random(18);   // AP-018 seed
            for (int i = 0; i < chosen.Count; i++)
            {
                var go = new GameObject("Puddle_" + i.ToString("000"));
                go.transform.SetParent(root.transform, false);
                go.transform.position = chosen[i] + Vector3.up * (PuddleDecalDepth * 0.5f);
                go.transform.rotation = Quaternion.Euler(90f, (float)(rng.NextDouble() * 360.0), 0f);   // projects along local +Z = world down
                var projector = go.AddComponent<DecalProjector>();
                projector.material = material;
                projector.size = new Vector3(PuddleDecalSize, PuddleDecalSize, PuddleDecalDepth);
                projector.pivot = new Vector3(0f, 0f, PuddleDecalDepth * 0.5f);
                projector.drawDistance = PuddleDecalDrawDistance;
                projector.fadeScale = 0.6f;
                int cell = rng.Next(8);
                projector.uvScale = new Vector2(0.25f, 0.5f);
                projector.uvBias = new Vector2((cell % 4) * 0.25f, (cell / 4) * 0.5f);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
            }

            return chosen.Count;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        private static Texture2D ReadPng(string assetPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(assetPath), false)) throw new InvalidOperationException(LogPrefix + "cannot decode " + assetPath);
            return tex;
        }

        private static float Tile(Texture2D tex, float u, float v)
        {
            return tex.GetPixelBilinear(u - Mathf.Floor(u), v - Mathf.Floor(v)).r;
        }

        private static void CopyLocalTransform(Transform src, Transform dst)
        {
            dst.localPosition = src.localPosition;
            dst.localRotation = src.localRotation;
            dst.localScale = src.localScale;
        }

        private static MeshFilter FindMesh(GameObject model, string objectName)
        {
            MeshFilter[] all = model.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter mf in all) if (mf.name == objectName) return mf;
            // A single-object FBX (the water meshes) imports with its mesh on the root, named after the file.
            if (all.Length == 1 && all[0].sharedMesh != null && (all[0].sharedMesh.name == objectName || objectName.StartsWith(model.name, StringComparison.Ordinal))) return all[0];
            return null;
        }

        private static Material LoadOrCreate(string path, Shader shader, string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            return m;
        }

        private static Texture2D Load(string path)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) throw new FileNotFoundException(LogPrefix + "texture missing", path);
            return t;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(bytes)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
