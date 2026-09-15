using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-014 · dd-placement-v1 → Unity (ADR-014-1). Reads <see cref="ArtLibraryPaths.Ap005PlacementJson"/>,
    /// validates the schema, converts every record to Unity space into
    /// <see cref="WorldPlacementAsset"/>, and writes <see cref="ArtLibraryPaths.Ap005WorldScene"/>:
    ///
    ///   [AP005 World]            Ap005WorldBootstrap (placement, overcast profile)
    ///     Instances              one prefab INSTANCE per placement record, in file order, named by id
    ///       AP005_i0001 …        TRS from the record; RegionTag; layer by occluder/fade flag;
    ///                            static for structure/ground/dressing; NavMeshModifier(ignore) for
    ///                            navmesh:ignore records that carry colliders
    ///     NavMesh Surface        the WorldNavigation settings, baked here; data saved as an asset
    ///
    /// Coordinates: Blender (x, y, z) → Unity (x, z, y); yaw about +Z (CCW) → −yaw about +Y;
    /// scale baked (1). Assembly parts are one prefab (the WP-012 multi-part prefabs).
    /// Layer policy (ADR-014-2): the placement flag decides per instance — occluder_candidate
    /// or fade_mesh → the whole instance on CameraOccluder (fades for the camera AND blocks
    /// bullets/aim, exactly like the procedural walls); otherwise the whole instance on
    /// Environment. Markers and the atmosphere are runtime (bootstrap); routes are data only.
    ///
    /// Idempotent in content: a re-run yields the same instance digest (<see cref="Digest"/>);
    /// Unity assigns fresh local file ids, so the scene TEXT is not byte-stable by design.
    /// </summary>
    public static class PlacementImporter
    {
        private const string LogPrefix = "[Placement] ";

        public const string RegionSuffixNone = "";

        private static readonly Dictionary<string, string> PrefixToPackage = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "AP002_", "AP-002" },
            { "AP003_", "AP-003" },
            { "AP003B_", "AP-003B" },
            { "AP005_", "AP-005" }
        };

        public sealed class ImportReport
        {
            public int Instances;
            public int Routes;
            public int Markers;
            public int SpawnPoints;
            public int WeaponPoints;
            public int Regions;
            public int Assemblies;
            public int Occluders;
            public int StaticInstances;
            public int IgnoreModifiers;
            public int NavMeshTriangles;
            public List<string> Unmatched = new List<string>();
            public string Digest;
        }

        // ------------------------------------------------------------------ pipeline

        /// <summary>Full import: asset, scene, NavMesh, build settings, world-source asset (source unchanged if it exists).</summary>
        public static ImportReport ImportAll()
        {
            WorldPlacementAsset placement = BuildAsset();
            ImportReport report = BuildScene(placement);
            RegisterInBuildSettings();
            EnsureWorldSourceAsset(null);
            return report;
        }

        // --------------------------------------------------------------------- asset

        public static WorldPlacementAsset BuildAsset()
        {
            string jsonPath = ArtLibraryPaths.Ap005PlacementJson;
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException(LogPrefix + "placement JSON missing", jsonPath);
            }

            byte[] bytes = File.ReadAllBytes(jsonPath);
            string json = Encoding.UTF8.GetString(bytes);
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(json), "placement");
            Validate(doc);

            string assetPath = ArtLibraryPaths.Ap005PlacementAsset;
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var asset = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(assetPath);
            bool created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<WorldPlacementAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.schema = ArtJson.AsString(doc["schema"]);
            asset.space = ArtJson.AsString(doc["space"]);
            asset.package = ArtJson.AsString(doc["package"]);
            asset.version = ArtJson.AsString(doc["version"]);
            asset.created = ArtJson.AsString(doc["created"]);
            asset.sourceSha256 = Sha256(bytes);

            asset.instances = new List<WorldPlacementAsset.Instance>();
            foreach (object o in ArtJson.AsList(doc["instances"], "instances"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "instance");
                string lodGroup = ArtJson.AsString(r["lod_group"]);
                Split(lodGroup, out string ap, out string family, out string variation);
                List<object> pos = ArtJson.AsList(r["position"], "position");
                List<object> rot = ArtJson.AsList(r["rotation_euler_deg"], "rotation");
                List<object> scl = ArtJson.AsList(r["scale"], "scale");
                float rx = ArtJson.AsFloat(rot[0]), ry = ArtJson.AsFloat(rot[1]), rz = ArtJson.AsFloat(rot[2]);
                if (Mathf.Abs(rx) > 1e-4f || Mathf.Abs(ry) > 1e-4f)
                {
                    throw new FormatException(LogPrefix + ArtJson.AsString(r["id"]) + ": rotation about X/Y is not part of dd-placement-v1 (yaw only).");
                }

                var assembly = new List<string>();
                foreach (object a in ArtJson.AsList(r["assembly"], "assembly")) assembly.Add(ArtJson.AsString(a));

                asset.instances.Add(new WorldPlacementAsset.Instance
                {
                    id = ArtJson.AsString(r["id"]),
                    ap = ap,
                    family = family,
                    variation = variation,
                    lodGroup = lodGroup,
                    assembly = assembly.ToArray(),
                    position = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(pos[0]), ArtJson.AsFloat(pos[1]), ArtJson.AsFloat(pos[2])),
                    yawDegrees = rz,
                    scale = new Vector3(ArtJson.AsFloat(scl[0]), ArtJson.AsFloat(scl[2]), ArtJson.AsFloat(scl[1])),
                    region = ArtJson.AsString(r["region"]),
                    role = ArtJson.AsString(r["role"]),
                    navMesh = ArtJson.AsString(r["navmesh"]),
                    occluder = r.ContainsKey("occluder_candidate") && ArtJson.AsBool(r["occluder_candidate"]),
                    fade = r.ContainsKey("fade_mesh") && ArtJson.AsBool(r["fade_mesh"])
                });
            }

            asset.routes = new List<WorldPlacementAsset.Route>();
            foreach (object o in ArtJson.AsList(doc["routes"], "routes"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "route");
                List<object> a = ArtJson.AsList(r["a"], "a");
                List<object> b = ArtJson.AsList(r["b"], "b");
                asset.routes.Add(new WorldPlacementAsset.Route
                {
                    index = ArtJson.AsInt(r["index"]),
                    a = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(a[0]), ArtJson.AsFloat(a[1]), 0f),
                    b = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(b[0]), ArtJson.AsFloat(b[1]), 0f),
                    halfWidth = ArtJson.AsFloat(r["half_width_m"]),
                    surface = ArtJson.AsString(r["surface"])
                });
            }

            asset.markers = new List<WorldPlacementAsset.Marker>();
            foreach (object o in ArtJson.AsList(doc["markers"], "markers"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "marker");
                List<object> p = ArtJson.AsList(r["position"], "position");
                asset.markers.Add(new WorldPlacementAsset.Marker
                {
                    id = ArtJson.AsString(r["id"]),
                    role = ArtJson.AsString(r["role"]),
                    position = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(p[0]), ArtJson.AsFloat(p[1]), ArtJson.AsFloat(p[2]))
                });
            }

            asset.spawnPoints = Points(doc["spawn_points"], "spawn_points");
            asset.weaponPoints = Points(doc["weapon_points"], "weapon_points");
            List<object> ps = ArtJson.AsList(doc["player_start"], "player_start");
            asset.playerStart = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(ps[0]), ArtJson.AsFloat(ps[1]), ArtJson.AsFloat(ps[2]));

            asset.regions = new List<WorldPlacementAsset.Region>();
            foreach (object o in ArtJson.AsList(doc["regions"], "regions"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "region");
                var poly = new List<float>();
                foreach (object pt in ArtJson.AsList(r["polygon"], "polygon"))
                {
                    List<object> xy = ArtJson.AsList(pt, "polygon point");
                    poly.Add(ArtJson.AsFloat(xy[0]));
                    poly.Add(ArtJson.AsFloat(xy[1])); // Blender y = Unity z
                }

                asset.regions.Add(new WorldPlacementAsset.Region
                {
                    id = ArtJson.AsString(r["id"]),
                    label = r.ContainsKey("label") ? ArtJson.AsString(r["label"]) : string.Empty,
                    identity = r.ContainsKey("identity") ? ArtJson.AsString(r["identity"]) : string.Empty,
                    sightlineClass = r.ContainsKey("sightline_class") ? ArtJson.AsString(r["sightline_class"]) : string.Empty,
                    polygonXZ = poly.ToArray()
                });
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "Placement asset " + (created ? "created" : "updated") + ": " + assetPath + " (" + asset.instances.Count +
                      " instances, " + asset.routes.Count + " routes, " + asset.markers.Count + " markers, " + asset.spawnPoints.Count + "+" +
                      asset.weaponPoints.Count + " points, " + asset.regions.Count + " regions; source sha256 " + asset.sourceSha256.Substring(0, 12) + "…)");
            return asset;
        }

        /// <summary>The dd-placement-v1 contract checks: schema/space ids, required arrays, per-record fields.</summary>
        public static void Validate(Dictionary<string, object> doc)
        {
            string schema = ArtJson.AsString(ArtJson.Require(doc, "schema"), "schema");
            if (schema != WorldPlacementAsset.Schema)
            {
                throw new FormatException(LogPrefix + "schema '" + schema + "' is not " + WorldPlacementAsset.Schema);
            }

            string space = ArtJson.AsString(ArtJson.Require(doc, "space"), "space");
            if (space != "AP001-contract-metres")
            {
                throw new FormatException(LogPrefix + "space '" + space + "' is not AP001-contract-metres");
            }

            foreach (string key in new[] { "instances", "routes", "markers", "spawn_points", "weapon_points", "player_start", "regions" })
            {
                ArtJson.AsList(ArtJson.Require(doc, key), key);
            }

            string[] instanceKeys = { "id", "family", "variation", "lod_group", "assembly", "position", "rotation_euler_deg", "scale", "region", "role", "navmesh" };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(doc["instances"], "instances"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "instance");
                for (int i = 0; i < instanceKeys.Length; i++) ArtJson.Require(r, instanceKeys[i]);
                string id = ArtJson.AsString(r["id"]);
                if (!ids.Add(id)) throw new FormatException(LogPrefix + "duplicate instance id " + id);
                string nav = ArtJson.AsString(r["navmesh"]);
                if (nav != "obstacle" && nav != "walkable" && nav != "ignore") throw new FormatException(LogPrefix + id + ": navmesh '" + nav + "'");
                Split(ArtJson.AsString(r["lod_group"]), out _, out _, out _);
            }
        }

        /// <summary><c>AP003B_bamboo_stalk_V01</c> → (AP-003B, bamboo_stalk, V01).</summary>
        public static void Split(string lodGroup, out string ap, out string family, out string variation)
        {
            foreach (KeyValuePair<string, string> kv in PrefixToPackage)
            {
                if (!lodGroup.StartsWith(kv.Key, StringComparison.Ordinal))
                {
                    continue;
                }

                string rest = lodGroup.Substring(kv.Key.Length);
                int cut = rest.LastIndexOf("_V", StringComparison.Ordinal);
                if (cut <= 0 || rest.Length - cut != 4)
                {
                    throw new FormatException(LogPrefix + "lod_group '" + lodGroup + "' has no _V<nn> suffix");
                }

                ap = kv.Value;
                family = rest.Substring(0, cut);
                variation = rest.Substring(cut + 1);
                return;
            }

            throw new FormatException(LogPrefix + "lod_group '" + lodGroup + "' has no known package prefix (AP002_/AP003_/AP003B_/AP005_)");
        }

        // --------------------------------------------------------------------- scene

        public static ImportReport BuildScene(WorldPlacementAsset placement)
        {
            if (placement == null)
            {
                throw new ArgumentNullException(nameof(placement));
            }

            PilotSceneBuilder.GuardEditMode("Import AP-005 world");
            PilotSceneBuilder.ConfirmSceneChange();

            var indices = new Dictionary<string, ArtLibraryAsset>(StringComparer.Ordinal);
            foreach (string ap in ArtLibraryPaths.Packages)
            {
                var index = AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(ArtLibraryPaths.IndexAsset(ap));
                if (index == null)
                {
                    throw new InvalidOperationException(LogPrefix + "library index missing for " + ap + " (run the library pipeline first)");
                }

                indices[ap] = index;
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            if (profile == null)
            {
                throw new InvalidOperationException(LogPrefix + "overcast profile missing: " + ArtLibraryPaths.OvercastVolumeProfile);
            }

            // Resolve every prefab BEFORE touching the scene: an unmatched record is a
            // production/contract mismatch and aborts with the full list, not a half world.
            var report = new ImportReport();
            var prefabs = new GameObject[placement.instances.Count];
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                GameObject prefab = indices.TryGetValue(inst.ap, out ArtLibraryAsset index) ? index.Find(inst.family, inst.variation) : null;
                if (prefab == null)
                {
                    report.Unmatched.Add(inst.id + " " + inst.lodGroup);
                }

                prefabs[i] = prefab;
            }

            if (report.Unmatched.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + report.Unmatched.Count + " placement records have no prefab:\n" + string.Join("\n", report.Unmatched));
            }

            int envLayer = PrototypeLayers.Environment;
            int occLayer = PrototypeLayers.CameraOccluder;
            if (envLayer < 0 || occLayer < 0)
            {
                throw new InvalidOperationException(LogPrefix + "Environment / CameraOccluder layers are missing.");
            }

            PilotSceneBuilder.EnsureFolder(PilotSceneBuilder.AssetFolderOf(ArtLibraryPaths.Ap005WorldScene));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject(Ap005WorldBootstrap.RootName);
            var bootstrap = root.AddComponent<Ap005WorldBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("_placement").objectReferenceValue = placement;
            so.FindProperty("_volumeProfile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();

            var instances = new GameObject(Ap005WorldBootstrap.InstancesName);
            instances.transform.SetParent(root.transform, false);

            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                go.name = inst.id;
                go.transform.SetParent(instances.transform, false);
                go.transform.SetPositionAndRotation(inst.position, WorldPlacementAsset.ToUnityYaw(inst.yawDegrees));
                go.transform.localScale = inst.scale;

                go.AddComponent<RegionTag>().Set(inst.id, inst.region, inst.role, inst.navMesh);

                bool occluder = inst.occluder || inst.fade;
                SetLayerRecursively(go, occluder ? occLayer : envLayer);
                if (occluder) report.Occluders++;

                // Static for what never moves and never swaps state: structures, ground and
                // dressing are batched; vegetation keeps its LOD cross-fade (GPU instancing on
                // the materials) and breakables swap to their broken child at runtime.
                if (inst.role == "structure" || inst.role == "ground" || inst.role == "dressing")
                {
                    SetStaticRecursively(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                    report.StaticInstances++;
                }

                if (inst.navMesh == "ignore" && go.GetComponentInChildren<Collider>(true) != null)
                {
                    NavMeshModifier modifier = go.AddComponent<NavMeshModifier>();
                    modifier.ignoreFromBuild = true;
                    report.IgnoreModifiers++;
                }

                if (inst.assembly != null && inst.assembly.Length > 0) report.Assemblies++;
            }

            report.Instances = placement.instances.Count;
            report.Routes = placement.routes.Count;
            report.Markers = placement.markers.Count;
            report.SpawnPoints = placement.spawnPoints.Count;
            report.WeaponPoints = placement.weaponPoints.Count;
            report.Regions = placement.regions.Count;

            // Ground: AP-005 places only the golden-slice plate (58×56 m, NE quadrant); the
            // rest of the 90 m map has no floor in the placement. A stand-in soil ground
            // (M_earth, 4 m tiles, PilotGround's recipe) carries the NavMesh and the walk
            // everywhere else, with holes under the koi basins so the water planes stay
            // visible. Reported to production (WP-014 handover).
            BuildStandInGround(root.transform, placement);

            // NavMesh: the WorldNavigation settings (agent 0, physics colliders in
            // Environment|CameraOccluder, ±48 m volume), baked HERE and saved as an asset so
            // the Player never bakes; the bootstrap only verifies.
            report.NavMeshTriangles = BakeNavMesh(root.transform);

            PilotSceneBuilder.Save(scene, ArtLibraryPaths.Ap005WorldScene);
            report.Digest = Digest(instances.transform);

            Debug.Log(LogPrefix + "Wrote " + ArtLibraryPaths.Ap005WorldScene + ": " + report.Instances + " instances (" + report.Assemblies +
                      " assemblies, " + report.Occluders + " on CameraOccluder, " + report.StaticInstances + " static, " + report.IgnoreModifiers +
                      " navmesh-ignore modifiers), NavMesh " + report.NavMeshTriangles + " triangles, digest " + report.Digest.Substring(0, 12) + "…");
            return report;
        }

        public const float NavMeshVoxelSize = 0.1f;
        public const float NavMeshMinRegionArea = 1f;

        public const string GroundName = "Ground (stand-in)";
        /// <summary>±80 m: the 19.9 m / −49° rig at the arena edge (±45) still sees ground, not sky (art review, west/east framings).</summary>
        public const float GroundSize = 160f;
        public const float GroundY = -0.05f;
        /// <summary>A soil bed under each basin, seen through the ground hole below the water plane (−0.15).</summary>
        public const float PondBedY = -0.85f;
        private const float GroundTile = 4f;
        private const float GroundFineTile = 0.25f;
        private const float HoleMargin = 0.1f;

        private static void BuildStandInGround(Transform root, WorldPlacementAsset placement)
        {
            // Basin water ellipses (Unity XZ): centre + semi-axes from the water-plane bounds
            // of the basin's own variation (pond_water_surface V01/V02 ↔ koi_pond_basin V01/V02/V03).
            var holes = new List<(Vector2 centre, Vector2 semi, float yaw)>();
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                if (inst.family != "koi_pond_basin") continue;
                Vector2 semi = (inst.variation == "V01" ? new Vector2(2.8f, 1.8f) : new Vector2(5.8f, 3.8f)) + Vector2.one * HoleMargin;
                holes.Add((new Vector2(inst.position.x, inst.position.z), semi, inst.yawDegrees));
            }

            // Pond beds: one quad per basin under its water plane, so the hole shows soil, not sky.
            var bed = new MeshBuilder(64);
            foreach ((Vector2 centre, Vector2 semi, float yaw) hole in holes)
            {
                float r = Mathf.Max(hole.semi.x, hole.semi.y) + 0.5f;
                bed.AddQuad(new Vector3(hole.centre.x - r, PondBedY - GroundY, hole.centre.y - r), new Vector3(hole.centre.x - r, PondBedY - GroundY, hole.centre.y + r),
                            new Vector3(hole.centre.x + r, PondBedY - GroundY, hole.centre.y + r), new Vector3(hole.centre.x + r, PondBedY - GroundY, hole.centre.y - r), 1f / PilotGround.TileMetres);
            }

            var builder = new MeshBuilder(4096);
            float h = GroundSize * 0.5f;
            int coarse = Mathf.RoundToInt(GroundSize / GroundTile);
            for (int ix = 0; ix < coarse; ix++)
            for (int iz = 0; iz < coarse; iz++)
            {
                float x0 = -h + ix * GroundTile, z0 = -h + iz * GroundTile;
                bool nearHole = false;
                foreach ((Vector2 centre, Vector2 semi, float yaw) hole in holes)
                {
                    float reach = Mathf.Max(hole.semi.x, hole.semi.y) + GroundTile;
                    if (x0 + GroundTile >= hole.centre.x - reach && x0 <= hole.centre.x + reach && z0 + GroundTile >= hole.centre.y - reach && z0 <= hole.centre.y + reach)
                    {
                        nearHole = true;
                        break;
                    }
                }

                // Only the tiles the hole boundary crosses are subdivided; tiles wholly outside stay
                // one quad and tiles wholly inside are dropped (keeps the mesh asset small).
                if (nearHole)
                {
                    int inside = 0;
                    for (int sx = 0; sx <= 2; sx++)
                    for (int sz = 0; sz <= 2; sz++)
                    {
                        if (InsideHole(holes, x0 + sx * 0.5f * GroundTile, z0 + sz * 0.5f * GroundTile)) inside++;
                    }

                    if (inside == 0) nearHole = false;
                    else if (inside == 9) continue;
                }

                if (!nearHole)
                {
                    AddGroundQuad(builder, x0, z0, GroundTile);
                    continue;
                }

                int fine = Mathf.RoundToInt(GroundTile / GroundFineTile);
                for (int fx = 0; fx < fine; fx++)
                for (int fz = 0; fz < fine; fz++)
                {
                    float cx = x0 + (fx + 0.5f) * GroundFineTile, cz = z0 + (fz + 0.5f) * GroundFineTile;
                    if (!InsideHole(holes, cx, cz))
                    {
                        AddGroundQuad(builder, x0 + fx * GroundFineTile, z0 + fz * GroundFineTile, GroundFineTile);
                    }
                }
            }

            // The beds join the ground mesh (same material, one draw, one collider).
            Mesh bedMesh = bed.Build("beds");
            AppendMesh(builder, bedMesh);
            UnityEngine.Object.DestroyImmediate(bedMesh);
            Mesh mesh = builder.Build("AP005_GroundStandIn");
            string meshPath = ArtLibraryPaths.Ap005GroundMesh;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            AssetDatabase.SaveAssets();

            var earth = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("earth"));
            var go = new GameObject(GroundName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, GroundY, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = earth;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            go.layer = PrototypeLayers.Environment;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        private static void AppendMesh(MeshBuilder builder, Mesh source)
        {
            Vector3[] v = source.vertices;
            int[] t = source.triangles;
            Vector3[] n = source.normals;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 normal = n != null && n.Length == v.Length ? n[t[i]] : Vector3.up;
                builder.AddTriangle(v[t[i]], v[t[i + 1]], v[t[i + 2]], normal);
            }
        }

        private static void AddGroundQuad(MeshBuilder builder, float x0, float z0, float size)
        {
            builder.AddQuad(
                new Vector3(x0, 0f, z0),
                new Vector3(x0, 0f, z0 + size),
                new Vector3(x0 + size, 0f, z0 + size),
                new Vector3(x0 + size, 0f, z0),
                1f / PilotGround.TileMetres);
        }

        private static bool InsideHole(List<(Vector2 centre, Vector2 semi, float yaw)> holes, float x, float z)
        {
            foreach ((Vector2 centre, Vector2 semi, float yaw) hole in holes)
            {
                // Undo the instance yaw (Blender CCW about +Z = −yaw about Unity +Y) to test in the basin's own frame.
                float a = hole.yaw * Mathf.Deg2Rad;
                float dx = x - hole.centre.x, dz = z - hole.centre.y;
                float lx = dx * Mathf.Cos(a) + dz * Mathf.Sin(a);
                float lz = -dx * Mathf.Sin(a) + dz * Mathf.Cos(a);
                float e = (lx * lx) / (hole.semi.x * hole.semi.x) + (lz * lz) / (hole.semi.y * hole.semi.y);
                if (e <= 1f) return true;
            }

            return false;
        }

        private static int BakeNavMesh(Transform root)
        {
            var go = new GameObject("NavMesh Surface");
            go.transform.SetParent(root, false);
            int env = PrototypeLayers.Environment;
            int occ = PrototypeLayers.CameraOccluder;

            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, 26f * 0.5f - 4f, 0f);
            surface.size = new Vector3(WorldLayout.MapSize + 6f, 26f, WorldLayout.MapSize + 6f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = (1 << env) | (1 << occ);
            surface.defaultArea = 0;
            // The AP-003B bridge deck is 1.2 m clear between its railings: with the 0.5 m agent
            // it erodes to a ~0.2 m centre strip that the procedural 4 m² island prune would
            // drop. The authored world keeps the single-file choke (its design) by baking at a
            // finer voxel and pruning only islands under 1 m² (ADR-014-1).
            surface.overrideVoxelSize = true;
            surface.voxelSize = NavMeshVoxelSize;
            surface.minRegionArea = NavMeshMinRegionArea;
            surface.buildHeightMesh = false;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;

            Physics.SyncTransforms();
            surface.BuildNavMesh();

            NavMeshData data = surface.navMeshData;
            if (data == null)
            {
                throw new InvalidOperationException(LogPrefix + "NavMesh bake produced no data.");
            }

            string dataPath = ArtLibraryPaths.Ap005NavMeshData;
            PilotSceneBuilder.EnsureFolder(Path.GetDirectoryName(dataPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(dataPath);
            if (existing != null)
            {
                // Keep the GUID: overwrite the asset's serialised content with the fresh bake.
                EditorUtility.CopySerialized(data, existing);
                existing.name = Path.GetFileNameWithoutExtension(dataPath);
                surface.navMeshData = existing;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                data.name = Path.GetFileNameWithoutExtension(dataPath);
                AssetDatabase.CreateAsset(data, dataPath);
            }

            AssetDatabase.SaveAssets();

            // The count comes from the data just baked (the scene is the only NavMesh loaded now).
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            return tri.indices != null ? tri.indices.Length / 3 : 0;
        }

        /// <summary>SHA-256 over the instance list (name, prefab GUID, TRS, layer, static flags, region/role/navmesh) — the idempotence contract.</summary>
        public static string Digest(Transform instances)
        {
            var sb = new StringBuilder(instances.childCount * 96);
            for (int i = 0; i < instances.childCount; i++)
            {
                Transform t = instances.GetChild(i);
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                string guid = source != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)) : "none";
                var tag = t.GetComponent<RegionTag>();
                Vector3 p = t.position;
                Vector3 e = t.rotation.eulerAngles;
                sb.Append(t.name).Append('|').Append(guid).Append('|')
                  .Append(p.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(p.y.ToString("F4", CultureInfo.InvariantCulture)).Append(',').Append(p.z.ToString("F4", CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.y.ToString("F3", CultureInfo.InvariantCulture)).Append('|')
                  .Append(t.gameObject.layer).Append('|').Append((int)GameObjectUtility.GetStaticEditorFlags(t.gameObject)).Append('|')
                  .Append(tag != null ? tag.Region + "/" + tag.Role + "/" + tag.NavMesh : "-").Append('\n');
            }

            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        // ---------------------------------------------------------------- settings

        public static void RegisterInBuildSettings()
        {
            string path = ArtLibraryPaths.Ap005WorldScene;
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != path) continue;
                if (!scenes[i].enabled)
                {
                    scenes[i] = new EditorBuildSettingsScene(path, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                }

                Debug.Log(LogPrefix + "World scene registered at build index " + i + ".");
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log(LogPrefix + "World scene appended to the build settings at index " + (scenes.Count - 1) + ".");
        }

        /// <summary>Creates the Resources world-source asset; sets the source when one is given, otherwise keeps the current value (Procedural on creation).</summary>
        public static WorldSourceSettings EnsureWorldSourceAsset(WorldSource? source)
        {
            string path = ArtLibraryPaths.WorldSourceAsset;
            var settings = AssetDatabase.LoadAssetAtPath<WorldSourceSettings>(path);
            bool created = settings == null;
            if (created)
            {
                settings = ScriptableObject.CreateInstance<WorldSourceSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }

            var so = new SerializedObject(settings);
            so.FindProperty("_ap005SceneName").stringValue = ArtLibraryPaths.Ap005WorldSceneName;
            if (source.HasValue)
            {
                so.FindProperty("_source").enumValueIndex = (int)source.Value;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "World source asset " + (created ? "created" : "updated") + ": " + path + " = " + settings.Source);
            return settings;
        }

        // ---------------------------------------------------------------- internals

        private static List<WorldPlacementAsset.Point> Points(object value, string what)
        {
            var list = new List<WorldPlacementAsset.Point>();
            foreach (object o in ArtJson.AsList(value, what))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, what);
                List<object> p = ArtJson.AsList(r["position"], "position");
                list.Add(new WorldPlacementAsset.Point
                {
                    id = ArtJson.AsString(r["id"]),
                    position = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(p[0]), ArtJson.AsFloat(p[1]), ArtJson.AsFloat(p[2])),
                    weight = r.ContainsKey("weight") ? ArtJson.AsFloat(r["weight"]) : 1f
                });
            }

            return list;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursively(t.GetChild(i).gameObject, layer);
        }

        private static void SetStaticRecursively(GameObject go, StaticEditorFlags flags)
        {
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetStaticRecursively(t.GetChild(i).gameObject, flags);
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void EnsureFolder(string assetFolder)
        {
            PilotSceneBuilder.EnsureFolder(assetFolder);
        }
    }
}
