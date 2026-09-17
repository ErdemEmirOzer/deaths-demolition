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
        public const float KerbAlbedoLift = 1.2f;
        /// <summary>
        /// WP-038 art-director round (Fable, KOŞULLU KABUL 85 %): P-05's 0.60 gravel tint left earth ≈ gravel at the
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
            public int Plates, Water, Colliders, Kerbs, Lods, Tags, WaterNavVolumes;
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
            g.SetFloat("_GravelTint", GravelTint);
            g.SetFloat("_GravelSmoothness", 0.15f);
            g.SetFloat("_EarthSmoothness", 0.08f);
            g.SetFloat("_StoneSmoothness", 0.43f);
            g.SetFloat("_UseMasks", 1f);
            g.SetFloat("_WetDarken", 0.45f);
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
                    mr.shadowCastingMode = isWater ? ShadowCastingMode.Off : ShadowCastingMode.On;
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
                if (!isWater) flags |= StaticEditorFlags.ContributeGI;
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

            Debug.Log(LogPrefix + "built " + report.Plates + " plates (" + report.Lods + " LOD renderers, " + report.Kerbs + " kerbs, " + report.Colliders + " colliders) + " + report.Water + " water meshes (" + report.WaterNavVolumes + " not-walkable basin NavMesh rows), " + report.Tags + " surface tags; v1 pond renderers hidden " + hidden + "; manifest " + report.ManifestSha256.Substring(0, 12) + "…");
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
