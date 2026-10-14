using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    /// WP-017: full XYZ euler through <see cref="WorldPlacementAsset.ToUnityRotation"/> (the AP-005B
    /// gable panels tilt about Y); scale baked (1). Assembly parts are one prefab (the WP-012 multi-part prefabs).
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
            { "AP005_", "AP-005" },
            { "AP005B_", "AP-005B" },  // WP-017 · AP-005B prop families
            { "AP017_", "AP-017" },    // WP-038B · AP-017 derivatives + world breakables
            { "AP023_", "AP-023" }     // WP-074 B5 · AP-023 world v5 kit (architecture, trees, pond, lanterns, props)
        };

        /// <summary>
        /// WP-017 · AP-005B <c>material_override</c> → Unity library material roles (ADR-017-2).
        /// The world master carries three materials that exist only there: the plaster copies
        /// (linear albedo × 0.82 → mean #CDCBC1, art verdict F1), the roof copy with tile-course
        /// lines (F5) and the AP-002 stone on the gate kerbs (F2). Each entry lists which slot
        /// roles it replaces (null = every slot) and the role that replaces them; the Unity
        /// materials are library roles (<c>ArtLibraryMaterialRoles</c>: plaster_hf003_world,
        /// plaster_world, roof_world; stone is the WP-011 role).
        /// </summary>
        public sealed class MaterialOverride
        {
            public string Name;
            /// <summary>Slot role → replacement role; null key = any role.</summary>
            public Dictionary<string, string> Map;
        }

        public static readonly Dictionary<string, MaterialOverride> MaterialOverrides = new Dictionary<string, MaterialOverride>(StringComparer.Ordinal)
        {
            { "AP005_M_plaster_world", new MaterialOverride { Name = "AP005_M_plaster_world", Map = new Dictionary<string, string> { { "plaster_hf003", "plaster_hf003_world" }, { "plaster", "plaster_world" } } } },
            { "AP005_M_plaster_world_ap001", new MaterialOverride { Name = "AP005_M_plaster_world_ap001", Map = new Dictionary<string, string> { { "plaster_hf003", "plaster_hf003_world" }, { "plaster", "plaster_world" } } } },
            { "AP005_M_roof_world", new MaterialOverride { Name = "AP005_M_roof_world", Map = new Dictionary<string, string> { { "roof", "roof_world" } } } },
            { "AP002_M_stone_v009_build_v003", new MaterialOverride { Name = "AP002_M_stone_v009_build_v003", Map = new Dictionary<string, string> { { "*", "stone" } } } }
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
            /// <summary>WP-038G · instances whose renderers stopped casting shadows (low vegetation / small dressing).</summary>
            public int ShadowsOff;
            /// <summary>WP-038 · bridge rail / post colliders kept out of the NavMesh bake (the deck keeps its width).</summary>
            public int BridgeRailIgnores;
            public int NavMeshTriangles;
            public int MaterialOverrides;
            public int MaterialSlotsReplaced;
            public int HeavyUnbatched;
            /// <summary>WP-020 · NavMeshModifierVolumes written for the named areas (Bridge / Corridor / Gate).</summary>
            public int AreaVolumes;
            /// <summary>WP-038N · Not-walkable volumes along the bridge rails / posts (outer side + ends).</summary>
            public int BridgeRailNavVolumes;
            /// <summary>WP-038P · NavMeshLinks joining body-reachable islands to the main mesh.</summary>
            public int IslandLinks;
            /// <summary>WP-038 · AP-015P plates (ground / route / paving) and water meshes placed; 0 = stand-in ground.</summary>
            public int SurfacePlates;
            public int WaterMeshes;
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
                asset.instances.Add(ParseInstance(ArtJson.AsObject(o, "instance"), string.Empty));
            }

            ApplyDelta(asset);

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

            ApplyOverrides(asset);

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
                      asset.weaponPoints.Count + " points, " + asset.regions.Count + " regions; source sha256 " + asset.sourceSha256.Substring(0, 12) + "…; overrides " +
                      asset.overriddenIds.Count + ")");
            return asset;
        }

        /// <summary>One dd-placement-v1 instance record → Unity space (shared by the source and the delta).</summary>
        public static WorldPlacementAsset.Instance ParseInstance(Dictionary<string, object> r, string deltaVersion)
        {
            string lodGroup = ArtJson.AsString(r["lod_group"]);
            Split(lodGroup, out string ap, out string family, out string variation);
            List<object> pos = ArtJson.AsList(r["position"], "position");
            List<object> rot = ArtJson.AsList(r["rotation_euler_deg"], "rotation");
            List<object> scl = ArtJson.AsList(r["scale"], "scale");
            // WP-017: full XYZ euler (AP-005B tilts its gable panels about Y); v001 records are yaw-only and unaffected.
            float rx = ArtJson.AsFloat(rot[0]), ry = ArtJson.AsFloat(rot[1]), rz = ArtJson.AsFloat(rot[2]);

            var assembly = new List<string>();
            foreach (object a in ArtJson.AsList(r["assembly"], "assembly")) assembly.Add(ArtJson.AsString(a));

            string materialOverride = r.ContainsKey("material_override") ? ArtJson.AsString(r["material_override"]) : string.Empty;
            if (!string.IsNullOrEmpty(materialOverride) && !MaterialOverrides.ContainsKey(materialOverride))
            {
                throw new FormatException(LogPrefix + ArtJson.AsString(r["id"]) + ": material_override '" + materialOverride + "' has no Unity mapping (PlacementImporter.MaterialOverrides)");
            }

            // WP-020 · AP-005C: per-part overrides ({ part: material }); the part must be in the assembly and the material mapped.
            var parts = new List<WorldPlacementAsset.PartOverride>();
            if (r.ContainsKey("material_override_parts"))
            {
                foreach (KeyValuePair<string, object> kv in ArtJson.AsObject(r["material_override_parts"], "material_override_parts"))
                {
                    string material = ArtJson.AsString(kv.Value, kv.Key);
                    if (!assembly.Contains(kv.Key)) throw new FormatException(LogPrefix + ArtJson.AsString(r["id"]) + ": material_override_parts names part '" + kv.Key + "' outside the assembly [" + string.Join(", ", assembly) + "]");
                    if (!MaterialOverrides.ContainsKey(material)) throw new FormatException(LogPrefix + ArtJson.AsString(r["id"]) + ": material_override_parts '" + kv.Key + "' → '" + material + "' has no Unity mapping (PlacementImporter.MaterialOverrides)");
                    parts.Add(new WorldPlacementAsset.PartOverride { part = kv.Key, material = material });
                }

                parts.Sort((a, b) => string.CompareOrdinal(a.part, b.part));
            }

            return new WorldPlacementAsset.Instance
            {
                id = ArtJson.AsString(r["id"]),
                ap = ap,
                family = family,
                variation = variation,
                lodGroup = lodGroup,
                assembly = assembly.ToArray(),
                position = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(pos[0]), ArtJson.AsFloat(pos[1]), ArtJson.AsFloat(pos[2])),
                yawDegrees = rz,
                rotationEuler = new Vector3(rx, ry, rz),
                scale = new Vector3(ArtJson.AsFloat(scl[0]), ArtJson.AsFloat(scl[2]), ArtJson.AsFloat(scl[1])),
                region = ArtJson.AsString(r["region"]),
                role = ArtJson.AsString(r["role"]),
                navMesh = ArtJson.AsString(r["navmesh"]),
                occluder = r.ContainsKey("occluder_candidate") && ArtJson.AsBool(r["occluder_candidate"]),
                fade = r.ContainsKey("fade_mesh") && ArtJson.AsBool(r["fade_mesh"]),
                materialOverride = materialOverride,
                materialOverrideParts = parts.ToArray(),
                deltaVersion = deltaVersion ?? string.Empty
            };
        }

        // --------------------------------------------------------------------- delta

        /// <summary>
        /// WP-017 / WP-020 · The delta chain (<see cref="ArtLibraryPaths.Ap005PlacementDeltaJsons"/>: AP-005B v002,
        /// then AP-005C v003; schema dd-placement-delta-v1) on top of the parsed v001 source. Each file, in order:
        /// <c>removed_ids</c> (every id must exist), <c>changed</c> (ids kept, full record replaces the previous one),
        /// <c>added</c> (ids must be new); its <c>base</c> must describe the instance count the previous step produced.
        /// The v001 file stays the source (WP-014 staged it); re-running yields the same list (idempotent), and the
        /// asset records every file's sha256 / target / counts (<see cref="WorldPlacementAsset.deltas"/>) plus the
        /// totals. A missing file ends the chain (no file = v001 world). Returns the number of records touched.
        /// </summary>
        public static int ApplyDelta(WorldPlacementAsset asset)
        {
            asset.deltaSha256 = string.Empty;
            asset.deltaTarget = string.Empty;
            asset.deltaAdded = asset.deltaChanged = asset.deltaRemoved = 0;
            asset.deltaRemovedIds = new List<string>();
            asset.deltas = new List<WorldPlacementAsset.DeltaRecord>();
            int touched = 0;
            foreach (string path in ArtLibraryPaths.Ap005PlacementDeltaJsons)
            {
                if (!File.Exists(path)) break;
                touched += ApplyDeltaFile(asset, path);
            }

            asset.deltaRemovedIds.Sort(StringComparer.Ordinal);
            return touched;
        }

        /// <summary>Version tag of a delta file from its name (<c>…-delta-v003.json</c> → "v003").</summary>
        public static string DeltaVersionOf(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int at = name.LastIndexOf("-v", StringComparison.Ordinal);
            return at >= 0 ? name.Substring(at + 1) : WorldPlacementAsset.DeltaVersionTag;
        }

        private static int ApplyDeltaFile(WorldPlacementAsset asset, string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "delta");
            string schema = ArtJson.AsString(ArtJson.Require(doc, "schema"), "schema");
            if (schema != WorldPlacementAsset.DeltaSchema)
            {
                throw new FormatException(LogPrefix + "delta schema '" + schema + "' != " + WorldPlacementAsset.DeltaSchema + " (" + path + ")");
            }

            string basePath = ArtJson.AsString(ArtJson.Require(doc, "base"), "base");
            if (!basePath.Contains(asset.instances.Count.ToString(CultureInfo.InvariantCulture) + " instances"))
            {
                throw new FormatException(LogPrefix + "delta base '" + basePath + "' does not describe the parsed source (" + asset.instances.Count + " instances) — " + path);
            }

            string version = DeltaVersionOf(path);
            var byId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < asset.instances.Count; i++) byId[asset.instances[i].id] = i;

            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "removed_ids"), "removed_ids"))
            {
                string id = ArtJson.AsString(o, "removed id");
                if (!byId.ContainsKey(id)) throw new FormatException(LogPrefix + "delta removes unknown instance '" + id + "' (" + path + ")");
                if (!removed.Add(id)) throw new FormatException(LogPrefix + "delta removes '" + id + "' twice (" + path + ")");
            }

            var changed = new Dictionary<string, WorldPlacementAsset.Instance>(StringComparer.Ordinal);
            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "changed"), "changed"))
            {
                WorldPlacementAsset.Instance inst = ParseInstance(ArtJson.AsObject(o, "changed"), version);
                if (!byId.ContainsKey(inst.id)) throw new FormatException(LogPrefix + "delta changes unknown instance '" + inst.id + "' (" + path + ")");
                if (removed.Contains(inst.id)) throw new FormatException(LogPrefix + "delta both removes and changes '" + inst.id + "' (" + path + ")");
                changed[inst.id] = inst;
            }

            var added = new List<WorldPlacementAsset.Instance>();
            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "added"), "added"))
            {
                WorldPlacementAsset.Instance inst = ParseInstance(ArtJson.AsObject(o, "added"), version);
                if (byId.ContainsKey(inst.id)) throw new FormatException(LogPrefix + "delta adds existing instance '" + inst.id + "' (" + path + ")");
                added.Add(inst);
            }

            var result = new List<WorldPlacementAsset.Instance>(asset.instances.Count - removed.Count + added.Count);
            for (int i = 0; i < asset.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = asset.instances[i];
                if (removed.Contains(inst.id)) continue;
                result.Add(changed.TryGetValue(inst.id, out WorldPlacementAsset.Instance c) ? c : inst);
            }

            result.AddRange(added);
            asset.instances = result;
            var record = new WorldPlacementAsset.DeltaRecord
            {
                file = Path.GetFileName(path),
                version = version,
                sha256 = Sha256(bytes),
                target = ArtJson.AsString(ArtJson.Require(doc, "target"), "target"),
                added = added.Count,
                changed = changed.Count,
                removed = removed.Count,
                removedIds = new List<string>(removed)
            };
            record.removedIds.Sort(StringComparer.Ordinal);
            asset.deltas.Add(record);
            asset.deltaSha256 = record.sha256;
            asset.deltaTarget = record.target;
            asset.deltaAdded += added.Count;
            asset.deltaChanged += changed.Count;
            asset.deltaRemoved += removed.Count;
            asset.deltaRemovedIds.AddRange(record.removedIds);
            Debug.Log(LogPrefix + "Applied delta " + path + " (" + version + "): +" + added.Count + " / ~" + changed.Count + " / -" + removed.Count + " → " + result.Count + " instances (sha256 " + record.sha256.Substring(0, 12) + "…)");
            return added.Count + changed.Count + removed.Count;
        }

        // ----------------------------------------------------------------- overrides

        /// <summary>
        /// WP-015 · Unity-side placement overrides (<see cref="ArtLibraryPaths.Ap005PlacementOverridesJson"/>,
        /// schema dd-placement-overrides-v1): <c>{ schema, source, overrides[ { id, position_delta [bx,by,bz] (m, Blender
        /// space), reason } ] }</c>. Each entry moves one instance of the already-parsed source by
        /// its delta; the AP-005 source file is not touched (WP-014 handover: three bamboo culms
        /// on the camera plane of route samples, reported to production). Missing file = no
        /// overrides. Unknown ids and deltas over <see cref="MaxOverrideDeltaMetres"/> are errors.
        /// </summary>
        public static int ApplyOverrides(WorldPlacementAsset asset)
        {
            asset.overridesSha256 = string.Empty;
            asset.overriddenIds = new List<string>();
            string path = ArtLibraryPaths.Ap005PlacementOverridesJson;
            if (!File.Exists(path))
            {
                return 0;
            }

            byte[] bytes = File.ReadAllBytes(path);
            Dictionary<string, object> doc = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "overrides");
            string schema = ArtJson.AsString(ArtJson.Require(doc, "schema"), "schema");
            if (schema != WorldPlacementAsset.OverridesSchema)
            {
                throw new FormatException(LogPrefix + "overrides schema '" + schema + "' != " + WorldPlacementAsset.OverridesSchema);
            }

            string source = ArtJson.AsString(ArtJson.Require(doc, "source"), "source");
            if (source != Path.GetFileName(ArtLibraryPaths.Ap005PlacementJson))
            {
                throw new FormatException(LogPrefix + "overrides target '" + source + "' is not the AP-005 source");
            }

            var byId = new Dictionary<string, WorldPlacementAsset.Instance>(StringComparer.Ordinal);
            foreach (WorldPlacementAsset.Instance inst in asset.instances) byId[inst.id] = inst;

            int applied = 0;
            foreach (object o in ArtJson.AsList(ArtJson.Require(doc, "overrides"), "overrides"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "override");
                string id = ArtJson.AsString(ArtJson.Require(r, "id"), "id");
                if (!byId.TryGetValue(id, out WorldPlacementAsset.Instance inst))
                {
                    throw new FormatException(LogPrefix + "override for unknown instance '" + id + "'");
                }

                if (asset.overriddenIds.Contains(id))
                {
                    throw new FormatException(LogPrefix + "instance '" + id + "' is overridden twice");
                }

                List<object> d = ArtJson.AsList(ArtJson.Require(r, "position_delta"), "position_delta");
                Vector3 delta = WorldPlacementAsset.ToUnity(ArtJson.AsFloat(d[0]), ArtJson.AsFloat(d[1]), ArtJson.AsFloat(d[2]));
                if (delta.magnitude > MaxOverrideDeltaMetres)
                {
                    throw new FormatException(LogPrefix + "override for '" + id + "' moves " + delta.magnitude.ToString("F2") + " m (> " + MaxOverrideDeltaMetres + " m): that is a production change, not a Unity override");
                }

                ArtJson.AsString(ArtJson.Require(r, "reason"), "reason");
                inst.position += delta;
                asset.overriddenIds.Add(id);
                applied++;
            }

            asset.overridesSha256 = Sha256(bytes);
            Debug.Log(LogPrefix + "Applied " + applied + " placement override(s) from " + path + " (sha256 " + asset.overridesSha256.Substring(0, 12) + "…)");
            return applied;
        }

        /// <summary>An override is a nudge; anything further belongs to the AP-005 source.</summary>
        public const float MaxOverrideDeltaMetres = 1f;

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
            var areaInstances = new List<KeyValuePair<GameObject, string>>();

            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                go.name = inst.id;
                if (AreaFamilies.TryGetValue(inst.family, out string areaName)) areaInstances.Add(new KeyValuePair<GameObject, string>(go, areaName));
                go.transform.SetParent(instances.transform, false);
                go.transform.SetPositionAndRotation(inst.position, inst.Rotation);
                go.transform.localScale = inst.scale;

                go.AddComponent<RegionTag>().Set(inst.id, inst.region, inst.role, inst.navMesh);

                bool occluder = inst.occluder || inst.fade;
                SetLayerRecursively(go, occluder ? occLayer : envLayer);
                if (occluder) report.Occluders++;

                // Static for what never moves and never swaps state: structures, ground and
                // dressing are batched; vegetation keeps its LOD cross-fade (GPU instancing on
                // the materials) and breakables swap to their broken child at runtime.
                // WP-017: static batching copies every LOD's vertices into the scene file — the
                // 12 palace roof bays (21 k / 17 k / 12 k tris) and 9 roof slabs alone made
                // World_AP005 142 MB in the build (release 373 → 507 MB). Heavy meshes stay
                // individual renderers (SRP-batched); the flag is kept for everything else.
                // WP-038L: a swimming prop (AP-017 koi) moves at runtime — never static (no batching, no occludee / probe static).
                bool moves = prefabs[i] != null && prefabs[i].GetComponent<ArtSwimLoop>() != null;
                if (!moves && (inst.role == "structure" || inst.role == "ground" || inst.role == "dressing"))
                {
                    bool heavy = IsHeavyForStaticBatching(prefabs[i]);
                    // WP-029 (ADR-029-2): structures and ground contribute to the lightmap bake (lightmap UVs from
                    // Contract v1.2); dressing stays static but probe-lit (small charts overlapped at bake resolution).
                    StaticEditorFlags flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                    if (inst.role != "dressing") flags |= StaticEditorFlags.ContributeGI;
                    if (!heavy) flags |= StaticEditorFlags.BatchingStatic; else report.HeavyUnbatched++;   // WP-038I: + M_prop_* families (scene-file size)
                    SetStaticRecursively(go, flags);
                    ArtLightingBake.ApplyLightmapLodPolicy(go, inst.role != "dressing");   // LOD0 charts only (ADR-029-2)
                    report.StaticInstances++;
                }

                if (inst.navMesh == "ignore" && go.GetComponentInChildren<Collider>(true) != null)
                {
                    NavMeshModifier modifier = go.AddComponent<NavMeshModifier>();
                    modifier.ignoreFromBuild = true;
                    report.IgnoreModifiers++;
                }

                // WP-038G (M3 closure K1): low vegetation and small dressing stop casting shadows. 1 750 groundcover / greenery /
                // shrub instances re-drew into every cascade (the largest share of the ~3 400 draws); under the 0.5-strength
                // overcast sun a ≤ 1.5 m bush's shadow is invisible at the 19.9 m camera. Structures, breakables, tall vegetation
                // (bamboo, sakura) and the AP-017 breakables keep theirs.
                if (LowCasterRole(inst.role) && ShadowCasterHeight(go) < LowCasterMaxHeight)
                {
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                    report.ShadowsOff++;
                }

                if (inst.family == BridgeFamily) report.BridgeRailIgnores += IgnoreBridgeRailColliders(go);

                if (!string.IsNullOrEmpty(inst.materialOverride))
                {
                    report.MaterialSlotsReplaced += ApplyMaterialOverride(go, inst);
                    report.MaterialOverrides++;
                }

                if (inst.materialOverrideParts != null && inst.materialOverrideParts.Length > 0)
                {
                    report.MaterialSlotsReplaced += ApplyPartOverrides(go, inst);
                    report.MaterialOverrides++;
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
            // WP-038 · With the AP-015P surfaces staged, the real ground / route / paving plates and water meshes
            // replace the stand-in ground (+ pond beds); without them the WP-014 stand-in stays (procedural fallback).
            if (ArtSurfaceImporter.Available)
            {
                Transform standIn = root.transform.Find(GroundName);
                if (standIn != null) UnityEngine.Object.DestroyImmediate(standIn.gameObject);
                ArtSurfaceImporter.Report surfaces = ArtSurfaceImporter.Build(root.transform, instances.transform, BasinHoles(placement));
                report.SurfacePlates = surfaces.Plates;
                report.WaterMeshes = surfaces.Water;
            }
            else
            {
                BuildStandInGround(root.transform, placement);
            }

            // WP-020 (AD MUST-1): the quiet_paving plate's slab seams let the AP-005 earth plate 3 mm below
            // read as thin brown lines at the gameplay camera; a stone-coloured grout quad inside the
            // plate's thickness hides it (production overlap of the slabs stays an AP-015 / P-05 item).
            BuildPavingGrout(root.transform, instances.transform, placement);

            // WP-020 · Named NavMesh areas (S-03 / WP-030 request): the bridge deck, the west corridor
            // modules and the gate openings carry their own area so RegionNavProfile.ApplyAreaCosts can
            // price them on the NavMesh itself (Bridge ×1.6, Corridor ×1.3, Gate ×1 / ×1.15).
            report.AreaVolumes = BuildAreaVolumes(root.transform, areaInstances);
            report.BridgeRailNavVolumes = BuildBridgeRailNavVolumes(root.transform, instances.transform, placement);
            WorldBoundaryBuilder.BuildRingNavVolumes(root.transform);   // WP-038R: no walkable NavMesh at or beyond the ring

            // NavMesh: the WorldNavigation settings (agent 0, physics colliders in
            // Environment|CameraOccluder, ±48 m volume), baked HERE and saved as an asset so
            // the Player never bakes; the bootstrap only verifies.
            report.NavMeshTriangles = BakeNavMesh(root.transform);
            // WP-038M: the playable-area ring (±45 m, invisible, Ignore Raycast), the pond rims + the fall guard — after the bake, never in it.
            WorldBoundaryBuilder.Build(root.transform, BasinHoles(placement));
            Physics.SyncTransforms();
            // WP-038P: NavMesh pieces a body walks into but no path reaches get a link along the body's swept line.
            // WP-038R: after the boundary — the body walks meet the ring / rims / plugs.
            report.IslandLinks = NavMeshIslandLinker.LinkIslands(root.transform, SeedFor(placement));

            PilotSceneBuilder.Save(scene, ArtLibraryPaths.Ap005WorldScene);
            report.Digest = Digest(instances.transform);

            Debug.Log(LogPrefix + "Wrote " + ArtLibraryPaths.Ap005WorldScene + ": " + report.Instances + " instances (surfaces " + report.SurfacePlates + " plates + " + report.WaterMeshes + " water; " + report.Assemblies +
                      " assemblies, " + report.Occluders + " on CameraOccluder, " + report.StaticInstances + " static, " + report.IgnoreModifiers +
                      " navmesh-ignore modifiers (+ " + report.BridgeRailIgnores + " bridge rail colliders), " + report.ShadowsOff + " low casters shadow-off, " + report.MaterialOverrides + " material overrides / " + report.MaterialSlotsReplaced + " slots, " + report.HeavyUnbatched + " heavy unbatched), NavMesh " + report.NavMeshTriangles + " triangles, digest " + report.Digest.Substring(0, 12) + "…");
            return report;
        }

        public const float NavMeshVoxelSize = 0.1f;
        public const float NavMeshMinRegionArea = 1f;

        public const string GroundName = "Ground (stand-in)";
        /// <summary>±80 m: the 19.9 m / −49° rig at the arena edge (±45) still sees ground, not sky (art review, west/east framings).</summary>
        public const float GroundSize = 160f;
        /// <summary>
        /// WP-015: −0.02 (was −0.05). The AP-005 ground plate (58×56 m, 3 cm slab at z −0.037)
        /// tops out at −0.007 and hero_earth at −0.010; 1.3 cm below the plate keeps the ledge
        /// along its edge to a pixel at the gameplay camera while staying clear of z-fighting.
        /// Material and mapping already equal the plate's: M_earth (the hero_earth role) with
        /// planar UV = world / 4 m (the plate's UVs fit u = 0.25·x − 0.005, v = 0.25·z, residual
        /// ≤ 0.22 — WP-015 uvfit), so the seam is only the height step.
        /// </summary>
        public const float GroundY = -0.02f;
        /// <summary>A soil bed under each basin, seen through the ground hole below the water plane (−0.15).</summary>
        public const float PondBedY = -0.85f;
        private const float GroundTile = 4f;
        private const float GroundFineTile = 0.25f;
        private const float HoleMargin = 0.1f;

        private static void BuildStandInGround(Transform root, WorldPlacementAsset placement)
        {
            // Basin water ellipses (Unity XZ): centre + semi-axes from the water-plane bounds
            // of the basin's own variation (pond_water_surface V01/V02 ↔ koi_pond_basin V01/V02/V03).
            List<(Vector2 centre, Vector2 semi, float yaw)> holes = BasinHoles(placement);

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

        public const string PavingGroutName = "Paving grout (WP-020)";
        public const float PavingGroutDepth = 0.002f;   // m under the plate's top: above the earth plate (−0.003), below the slabs

        /// <summary>
        /// One flat quad per <c>quiet_paving</c> instance, the plate's renderer footprint in XZ at
        /// (top − <see cref="PavingGroutDepth"/>), with the plate's own <c>stone</c> material (untextured
        /// tint would differ from the slabs; the same map keeps the seams at the slab value). No collider
        /// (the NavMesh stays on the plate), no GI contribution, probe-lit static.
        /// </summary>
        private static void BuildPavingGrout(Transform root, Transform instances, WorldPlacementAsset placement)
        {
            Transform old = root.Find(PavingGroutName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject(PavingGroutName);
            parent.transform.SetParent(root, false);
            var stone = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("stone"));
            if (stone == null) throw new InvalidOperationException(LogPrefix + "stone material missing for the paving grout");
            int count = 0;
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                if (inst.family != "quiet_paving") continue;
                Transform t = instances.Find(inst.id);
                if (t == null) continue;
                Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds;
                for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                var builder = new MeshBuilder();
                float y = b.max.y - PavingGroutDepth;
                builder.AddQuad(new Vector3(b.min.x, y, b.min.z), new Vector3(b.min.x, y, b.max.z), new Vector3(b.max.x, y, b.max.z), new Vector3(b.max.x, y, b.min.z), 1f / GroundTile);
                Mesh mesh = builder.Build("PavingGrout_" + inst.id);
                var go = new GameObject("grout " + inst.id);
                go.transform.SetParent(parent.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = stone;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                go.layer = PrototypeLayers.Environment;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                string meshPath = Path.GetDirectoryName(ArtLibraryPaths.Ap005GroundMesh).Replace('\\', '/') + "/AP005_PavingGrout_" + inst.id + ".asset";
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

                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "paving grout: " + count + " quad(s) under the quiet_paving plate(s)");
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

        /// <summary>
        /// Basin water ellipses (Unity XZ): centre + semi-axes from the water-plane bounds of the basin's own variation
        /// (pond_water_surface V01/V02 ↔ koi_pond_basin V01/V02/V03) + <see cref="HoleMargin"/>. WP-014 cut them out of
        /// the stand-in ground; WP-038 keeps them as the NavMesh pond footprint on top of the AP-015P plates (the real
        /// basins leave a 0.75 m ground strip between the south ponds 02 / 03 that would bypass the WP-030 choke).
        /// </summary>
        public static List<(Vector2 centre, Vector2 semi, float yaw)> BasinHoles(WorldPlacementAsset placement)
        {
            var holes = new List<(Vector2 centre, Vector2 semi, float yaw)>();
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                if (inst.family != "koi_pond_basin") continue;
                Vector2 semi = (inst.variation == "V01" ? new Vector2(2.8f, 1.8f) : new Vector2(5.8f, 3.8f)) + Vector2.one * HoleMargin;
                holes.Add((new Vector2(inst.position.x, inst.position.z), semi, inst.yawDegrees));
            }

            return holes;
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

        /// <summary>WP-017 · A prefab whose largest mesh exceeds this many triangles is not static-batched (ADR-017-2 note).</summary>
        public const int StaticBatchTriangleLimit = 5000;

        /// <summary>Material-name prefix of the prop families (AP-005B, AP-017 Phase 2): one unique material per family.</summary>
        public const string PropMaterialPrefix = "M_prop_";

        public static bool IsHeavyForStaticBatching(GameObject prefab)
        {
            if (prefab == null) return false;
            // WP-038I: the prop families stay individual renderers as well — static batching copies every LOD's
            // (seam-split) vertices into the scene file: the twelve Phase 2 dressing props alone grew World_AP005's level
            // file by 5.5 MB in the release build, for a handful of merged draws the SRP Batcher already makes cheap.
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
            {
                if (m != null && m.name.StartsWith(PropMaterialPrefix, StringComparison.Ordinal)) return true;
            }

            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                long tris = 0;
                for (int i = 0; i < m.subMeshCount; i++) tris += m.GetIndexCount(i) / 3;
                if (tris > StaticBatchTriangleLimit) return true;
            }

            return false;
        }

        /// <summary>
        /// WP-017 · Replace the instance's material slots per <see cref="MaterialOverrides"/>
        /// (prefab-instance overrides on the renderers' shared material arrays — no material is
        /// instantiated; the library material of the target role is shared by every override).
        /// Returns the number of slots replaced; zero is a contract mismatch and throws.
        /// </summary>
        public static int ApplyMaterialOverride(GameObject instance, WorldPlacementAsset.Instance inst)
        {
            return ApplyMaterialOverride(instance, inst.id, inst.lodGroup, inst.materialOverride, null);
        }

        /// <summary>
        /// WP-020 · AP-005C <c>material_override_parts</c>: each named assembly part (its renderers are
        /// <c>&lt;part&gt;_LOD&lt;n&gt;</c> or <c>&lt;part&gt;</c>) takes its own world-master material; the
        /// other parts keep the prefab's materials. A part that matches no renderer or no slot is an error.
        /// </summary>
        public static int ApplyPartOverrides(GameObject instance, WorldPlacementAsset.Instance inst)
        {
            int replaced = 0;
            foreach (WorldPlacementAsset.PartOverride po in inst.materialOverrideParts)
            {
                string part = po.part;
                replaced += ApplyMaterialOverride(instance, inst.id, inst.lodGroup + "/" + part, po.material,
                    r => r.name == part || r.name.StartsWith(part + "_LOD", StringComparison.Ordinal));
            }

            return replaced;
        }

        private static int ApplyMaterialOverride(GameObject instance, string id, string what, string overrideName, Func<Renderer, bool> rendererFilter)
        {
            if (!MaterialOverrides.TryGetValue(overrideName, out MaterialOverride over))
            {
                throw new InvalidOperationException(LogPrefix + id + ": material_override '" + overrideName + "' has no Unity mapping");
            }

            int replaced = 0;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (rendererFilter != null && !rendererFilter(renderer)) continue;
                Material[] slots = renderer.sharedMaterials;
                bool touched = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    string role = slots[i].name.StartsWith("M_", StringComparison.Ordinal) ? slots[i].name.Substring(2) : slots[i].name;
                    string target;
                    if (!over.Map.TryGetValue(role, out target) && !over.Map.TryGetValue("*", out target)) continue;
                    if (role == target) continue;
                    var material = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(target));
                    if (material == null)
                    {
                        throw new InvalidOperationException(LogPrefix + id + ": override target material missing: " + ArtPipelinePaths.Material(target) + " (build the library materials first)");
                    }

                    slots[i] = material;
                    touched = true;
                    replaced++;
                }

                if (touched) renderer.sharedMaterials = slots;
            }

            if (replaced == 0)
            {
                throw new InvalidOperationException(LogPrefix + id + " (" + what + "): material_override '" + overrideName + "' matched no material slot");
            }

            return replaced;
        }

        public const string AreaVolumesName = "NavMesh Areas (WP-020)";
        public const string AreaBridge = "Bridge", AreaCorridor = "Corridor", AreaGate = "Gate";
        /// <summary>Family stem → NavMesh area name; the volume is the instance's renderer bounds (XZ) from the floor to <see cref="AreaVolumeHeight"/>.</summary>
        /// <summary>WP-038G · shadow-caster policy: vegetation / dressing instances lower than this (renderer bounds height, LOD0) cast no shadow.</summary>
        public const float LowCasterMaxHeight = 1.5f;

        public static bool LowCasterRole(string role) { return role == "vegetation" || role == "dressing"; }

        public static float ShadowCasterHeight(GameObject go)
        {
            bool any = false; float min = float.MaxValue, max = float.MinValue;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                Bounds b = r.bounds; any = true;
                min = Mathf.Min(min, b.min.y); max = Mathf.Max(max, b.max.y);
            }

            return any ? max - min : 0f;
        }

        public const string BridgeFamily = "bridge_segment";
        /// <summary>A bridge child collider thinner than this in both horizontal axes and taller than <see cref="BridgeRailMinHeight"/> is a post / rail.</summary>
        public const float BridgeRailMaxThickness = 0.2f, BridgeRailMinHeight = 0.5f;

        /// <summary>
        /// WP-038 · The AP-003B bridge deck is 1.45 m wide, but its rail posts (0.12–0.15 m boxes, 0.7–1.35 m tall) stand
        /// inside the deck's edge: eroded by the 0.5 m agent radius they left a ~0.1 m NavMesh sliver that broke at the
        /// segment joints, so the WP-030 "bridge" crossing was really the basin rim stones plus a one-voxel thread of
        /// ground between ponds 02 / 03 — gone with the AP-015P plates. The posts / rails (thin in both horizontal
        /// axes, tall) are excluded from the bake (NavMeshModifier ignore on the collider object; the deck, the end
        /// steps and the piers stay), which gives the deck its 0.45 m walkable corridor — the choke WP-030 designed.
        /// </summary>
        private static int IgnoreBridgeRailColliders(GameObject go)
        {
            int count = 0;
            foreach (BoxCollider box in go.GetComponentsInChildren<BoxCollider>(true))
            {
                Vector3 size = box.size;
                // Height is whichever local axis points along world up (the FBX child carries the axis rotation).
                Vector3 up = box.transform.InverseTransformDirection(Vector3.up);
                float ax = Mathf.Abs(up.x), ay = Mathf.Abs(up.y), az = Mathf.Abs(up.z);
                float height, thinA, thinB;
                if (ax >= ay && ax >= az) { height = size.x; thinA = size.y; thinB = size.z; }
                else if (ay >= az) { height = size.y; thinA = size.x; thinB = size.z; }
                else { height = size.z; thinA = size.x; thinB = size.y; }
                if (height < BridgeRailMinHeight || Mathf.Min(thinA, thinB) >= BridgeRailMaxThickness) continue;
                NavMeshModifier m = box.GetComponent<NavMeshModifier>() ?? box.gameObject.AddComponent<NavMeshModifier>();
                m.ignoreFromBuild = true;
                count++;
            }

            return count;
        }

        public const string BridgeRailNavRootName = "Bridge Rail NavMesh (WP-038N)";
        /// <summary>WP-038N · Body radius (player / zombie CharacterController): how far the rail volumes reach outward and past the ends.</summary>
        public const float BodyRadius = 0.45f;
        public const float BridgeRailNavBottom = -0.6f, BridgeRailNavTop = 0.45f;
        public const string BridgeRailSkirtRootName = "Bridge Rail Skirts (WP-038P)";
        /// <summary>WP-038P · Skirt top: over any step a body takes (0.35 m) and the 0.30 m deck.</summary>
        public const float BridgeRailSkirtTop = 1.4f;
        /// <summary>
        /// WP-038Q · A skirt comes only from rail parts above the deck (a post foot under the deck never met a body — the WP-038P
        /// skirts took it and stood 3 cm inside the rails), its inner face 1 cm inside the rail line (the body slides on one
        /// skirt face, never on the per-segment rail boxes and their joints).
        /// </summary>
        public const float BridgeRailSkirtAboveDeck = 0.35f, BridgeRailSkirtInset = 0.01f;

        /// <summary>
        /// WP-038N · The rails / posts are out of the bake (the deck keeps its corridor), so the NavMesh joined the ground beside a
        /// segment to the 0.30 m deck sideways, through the rails: paths entered the deck diagonally at its ends and the bodies slid
        /// down the outside of a rail (centre → south bank stalled at the bridge's north end). One Not-Walkable volume per rail /
        /// post (a volume does not erode): its box in the segment's frame, grown by <see cref="BodyRadius"/> outward (away from the
        /// deck's centre line) and past both ends along the deck, never inward — the deck corridor stays, the side link is gone,
        /// the path meets the deck head-on. Environment layer (the surface's mask). Rebuilt in place.
        /// WP-038P: each volume also gets a skirt — an invisible box on the same footprint (built-in Ignore Raycast layer, out of
        /// the bake like the WP-038M ring) up to <see cref="BridgeRailSkirtTop"/>: where the NavMesh says "not walkable" beside a
        /// rail, a body cannot stand either. Without it the crowd pushed a zombie into the pocket beside the south-end post, off the
        /// NavMesh, where it jittered against the post for the rest of the round (S-10 soak seed 61, run 14).
        /// WP-038Q: the skirts of one side of a bridge whose segments are axis-aligned merge into one box: 38 boxes meeting at
        /// every segment joint gave the CharacterController seams to snag on (with the pond fence pinch, run 25 held the player
        /// on the deck for 230 s).
        /// </summary>
        public static int BuildBridgeRailNavVolumes(Transform root, Transform instances, WorldPlacementAsset placement)
        {
            Transform old = root.Find(BridgeRailNavRootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            Transform oldSkirts = root.Find(BridgeRailSkirtRootName);
            if (oldSkirts != null) UnityEngine.Object.DestroyImmediate(oldSkirts.gameObject);
            var parent = new GameObject(BridgeRailNavRootName);
            parent.transform.SetParent(root, false);
            var skirts = new GameObject(BridgeRailSkirtRootName) { layer = WorldBoundaryBuilder.Layer };
            skirts.transform.SetParent(root, false);
            skirts.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            var skirtBoxes = new List<(Transform bridge, Vector3 min, Vector3 max)>();
            int count = 0;
            foreach (WorldPlacementAsset.Instance inst in placement.instances)
            {
                if (inst.family != BridgeFamily) continue;
                Transform bridge = instances.Find(inst.id);
                if (bridge == null) throw new InvalidOperationException(LogPrefix + "bridge instance missing in scene: " + inst.id);
                // Deck axis = the segment's longer horizontal extent (bridge-local), lateral = the other.
                var all = new Bounds();
                bool any = false;
                foreach (BoxCollider box in bridge.GetComponentsInChildren<BoxCollider>(true))
                {
                    Bounds b = LocalBounds(bridge, box);
                    if (!any) { all = b; any = true; } else all.Encapsulate(b);
                }

                if (!any) continue;
                bool deckAlongX = all.size.x >= all.size.z;
                float deckCentreLateral = deckAlongX ? all.center.z : all.center.x;
                foreach (BoxCollider box in bridge.GetComponentsInChildren<BoxCollider>(true))
                {
                    NavMeshModifier m = box.GetComponent<NavMeshModifier>();
                    if (m == null || !m.ignoreFromBuild) continue;
                    Bounds b = LocalBounds(bridge, box);
                    Vector3 min = b.min, max = b.max;
                    if (deckAlongX)
                    {
                        min.x -= BodyRadius; max.x += BodyRadius;
                        if (b.center.z >= deckCentreLateral) max.z += BodyRadius; else min.z -= BodyRadius;
                    }
                    else
                    {
                        min.z -= BodyRadius; max.z += BodyRadius;
                        if (b.center.x >= deckCentreLateral) max.x += BodyRadius; else min.x -= BodyRadius;
                    }

                    min.y = BridgeRailNavBottom - bridge.position.y;
                    max.y = BridgeRailNavTop - bridge.position.y;
                    var vol = new GameObject("RailNav_" + inst.id + "_" + count.ToString("00"));
                    vol.transform.SetParent(parent.transform, false);
                    vol.layer = PrototypeLayers.Environment;
                    vol.transform.SetPositionAndRotation(bridge.TransformPoint((min + max) * 0.5f), bridge.rotation);
                    var v = vol.AddComponent<NavMeshModifierVolume>();
                    v.center = Vector3.zero;
                    v.size = max - min;
                    v.area = ArtSurfaceImporter.NavAreaNotWalkable;
                    // Skirt: rail parts above the deck only, inner face inset past the rail line, outward / past the ends as the volume.
                    Bounds wb = box.bounds;
                    if (wb.max.y > bridge.position.y + BridgeRailSkirtAboveDeck)
                    {
                        Vector3 smin = min, smax = max;
                        if (deckAlongX) { if (b.center.z >= deckCentreLateral) smin.z = b.min.z - BridgeRailSkirtInset; else smax.z = b.max.z + BridgeRailSkirtInset; }
                        else { if (b.center.x >= deckCentreLateral) smin.x = b.min.x - BridgeRailSkirtInset; else smax.x = b.max.x + BridgeRailSkirtInset; }
                        smax.y = BridgeRailSkirtTop - bridge.position.y;
                        skirtBoxes.Add((bridge, smin, smax));
                    }

                    count++;
                }
            }

            BuildRailSkirts(skirts.transform, skirtBoxes);
            return count;
        }

        /// <summary>
        /// WP-038Q · Skirt colliders: boxes of axis-aligned segments become world boxes merged along each side (same lateral
        /// extents within 5 cm, overlapping or touching along the deck), so a side is one face with no joints; a segment at any
        /// other yaw keeps its own oriented box.
        /// </summary>
        private static void BuildRailSkirts(Transform parent, List<(Transform bridge, Vector3 min, Vector3 max)> boxes)
        {
            var merged = new List<Bounds>();
            int n = 0;
            foreach ((Transform bridge, Vector3 min, Vector3 max) in boxes)
            {
                float yaw = bridge.eulerAngles.y;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Round(yaw / 90f) * 90f)) > 0.5f)
                {
                    var skirt = new GameObject("RailSkirt_" + (n++).ToString("00")) { layer = WorldBoundaryBuilder.Layer };
                    skirt.transform.SetParent(parent, false);
                    skirt.transform.SetPositionAndRotation(bridge.TransformPoint((min + max) * 0.5f), bridge.rotation);
                    BoxCollider sc = skirt.AddComponent<BoxCollider>();
                    sc.size = max - min;
                    continue;
                }

                var wb = new Bounds(bridge.TransformPoint(min), Vector3.zero);
                for (int i = 1; i < 8; i++) wb.Encapsulate(bridge.TransformPoint(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z)));
                merged.Add(wb);
            }

            // Merge until stable: two boxes join when they share the lateral extents (one axis within 5 cm on both faces) and
            // meet along the other (overlap or a gap ≤ 5 cm).
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < merged.Count && !changed; i++)
                for (int j = i + 1; j < merged.Count && !changed; j++)
                {
                    Bounds a = merged[i], c = merged[j];
                    bool sameX = Mathf.Abs(a.min.x - c.min.x) < 0.05f && Mathf.Abs(a.max.x - c.max.x) < 0.05f;
                    bool sameZ = Mathf.Abs(a.min.z - c.min.z) < 0.05f && Mathf.Abs(a.max.z - c.max.z) < 0.05f;
                    bool meetZ = a.min.z <= c.max.z + 0.05f && c.min.z <= a.max.z + 0.05f;
                    bool meetX = a.min.x <= c.max.x + 0.05f && c.min.x <= a.max.x + 0.05f;
                    if (!((sameX && meetZ) || (sameZ && meetX))) continue;
                    // The merged face keeps the side's innermost-safe line: lateral bounds are the union's (≤ 5 cm apart).
                    a.Encapsulate(c);
                    merged[i] = a;
                    merged.RemoveAt(j);
                    changed = true;
                }
            }

            foreach (Bounds wb in merged)
            {
                var skirt = new GameObject("RailSkirt_" + (n++).ToString("00")) { layer = WorldBoundaryBuilder.Layer };
                skirt.transform.SetParent(parent, false);
                skirt.transform.position = wb.center;
                BoxCollider sc = skirt.AddComponent<BoxCollider>();
                sc.size = wb.size;
            }
        }

        /// <summary>A box collider's bounds in <paramref name="frame"/>'s local space (unscaled frame: placement scale 1).</summary>
        private static Bounds LocalBounds(Transform frame, BoxCollider box)
        {
            var b = new Bounds();
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                Vector3 local = frame.InverseTransformPoint(box.transform.TransformPoint(box.center + Vector3.Scale(corner, box.size)));
                if (i == 0) b = new Bounds(local, Vector3.zero); else b.Encapsulate(local);
            }

            return b;
        }

        public static readonly Dictionary<string, string> AreaFamilies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "bridge_segment", AreaBridge },
            { "west_corridor_module", AreaCorridor },
            { "gate_transition", AreaGate }
        };
        public const float AreaVolumeHeight = 3f;
        public const float AreaVolumeMargin = 0.25f;

        /// <summary>
        /// WP-020 · One <see cref="NavMeshModifierVolume"/> per instance of an area family under
        /// <see cref="AreaVolumesName"/>: the bridge deck segments (5), the west corridor modules (6) and
        /// the gate transitions (5 incl. the AP-005C north gate). The volume spans the instance's
        /// renderer bounds in XZ (+ margin) from the ground to 3 m, so the whole opening / deck / floor
        /// gets the area. Areas must exist in ProjectSettings/NavMeshAreas (3 Bridge, 4 Corridor, 5 Gate);
        /// a missing area name is an error, not a silent Walkable.
        /// </summary>
        public static int BuildAreaVolumes(Transform root, List<KeyValuePair<GameObject, string>> areaInstances)
        {
            Transform old = root.Find(AreaVolumesName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject(AreaVolumesName);
            parent.transform.SetParent(root, false);
            int count = 0;
            foreach (KeyValuePair<GameObject, string> pair in areaInstances)
            {
                string area = pair.Value;
                int areaIndex = NavMesh.GetAreaFromName(area);
                if (areaIndex < 0) throw new InvalidOperationException(LogPrefix + "NavMesh area '" + area + "' is not defined in ProjectSettings/NavMeshAreas.asset");
                Renderer[] renderers = pair.Key.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds b = renderers[0].bounds;
                for (int r = 1; r < renderers.Length; r++) b.Encapsulate(renderers[r].bounds);
                var go = new GameObject(area + " " + pair.Key.name);
                go.layer = PrototypeLayers.Environment;   // NavMeshSurface collects modifier volumes through its layer mask
                go.transform.SetParent(parent.transform, false);
                go.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
                var vol = go.AddComponent<NavMeshModifierVolume>();
                vol.area = areaIndex;
                vol.center = new Vector3(0f, AreaVolumeHeight * 0.5f, 0f);
                vol.size = new Vector3(b.size.x + AreaVolumeMargin * 2f, AreaVolumeHeight, b.size.z + AreaVolumeMargin * 2f);
                count++;
            }

            return count;
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
            BuildAtBodyClimb(surface);
            return SaveNavMeshAsset(surface);
        }

        /// <summary>
        /// WP-038N · Step height the authored world is baked at. Agent type 0 climbs 0.75 m (project setting, shared with the
        /// procedural world's runtime bake), but every body that walks here is a CharacterController with a 0.35 m step (player
        /// PlayerMotor, zombies EnemyFactory / ZombieEnemy). Baked at 0.75 the NavMesh put walkable tops on props 0.35–0.75 m tall
        /// and linked them to the ground: the path from the centre to the east gate ran over an AP-017 stone pile (0.35 m) and the
        /// 0.50 m pile beside it — PathComplete, but the S-10 bot stood on the first pile for 300 s and the last zombie never came
        /// (soak seed 61, before and after WP-038M). 0.30 keeps a margin under the 0.35 step.
        /// </summary>
        public const float NavMeshBodyClimb = 0.3f;

        /// <summary>
        /// NavMeshSurface.BuildNavMesh with the climb lowered to <see cref="NavMeshBodyClimb"/>: the same sources (the surface's own
        /// collection — modifiers, modifier volumes, layer mask, volume bounds) and settings otherwise.
        /// </summary>
        public static void BuildAtBodyClimb(NavMeshSurface surface)
        {
            MethodInfo collect = typeof(NavMeshSurface).GetMethod("CollectSources", BindingFlags.Instance | BindingFlags.NonPublic);
            if (collect == null) throw new InvalidOperationException(LogPrefix + "NavMeshSurface.CollectSources not found (AI Navigation package changed?)");
            var sources = (List<NavMeshBuildSource>)collect.Invoke(surface, null);
            NavMeshBuildSettings settings = surface.GetBuildSettings();
            settings.agentClimb = NavMeshBodyClimb;
            var bounds = new Bounds(surface.center, new Vector3(Mathf.Abs(surface.size.x), Mathf.Abs(surface.size.y), Mathf.Abs(surface.size.z)));
            NavMeshData built = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, surface.transform.position, surface.transform.rotation);
            if (built == null) throw new InvalidOperationException(LogPrefix + "NavMesh bake produced no data.");
            built.name = surface.gameObject.name;
            surface.RemoveData();
            surface.navMeshData = built;
            if (surface.isActiveAndEnabled) surface.AddData();
        }

        /// <summary>
        /// WP-038N batch entry: re-bake World_AP005's NavMesh at <see cref="NavMeshBodyClimb"/> without a re-import (same surface,
        /// same sources), save the asset, then rebuild the WP-038M boundary — its pond-rim crossings are read off the NavMesh.
        /// </summary>
        public static void RebakeNavMeshCli()
        {
            Scene scene = EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            GameObject root = GameObject.Find(Ap005WorldBootstrap.RootName);
            if (root == null) throw new InvalidOperationException(LogPrefix + "world root missing");
            NavMeshSurface[] surfaces = root.GetComponentsInChildren<NavMeshSurface>(true);
            if (surfaces.Length != 1) throw new InvalidOperationException(LogPrefix + "expected one NavMeshSurface, found " + surfaces.Length);
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            if (placement == null) throw new InvalidOperationException(LogPrefix + "placement asset missing");
            Transform instances = root.transform.Find(Ap005WorldBootstrap.InstancesName);
            if (instances == null) throw new InvalidOperationException(LogPrefix + "instances container missing");
            int railVolumes = BuildBridgeRailNavVolumes(root.transform, instances, placement);
            WorldBoundaryBuilder.BuildRingNavVolumes(root.transform);   // WP-038R
            Physics.SyncTransforms();
            BuildAtBodyClimb(surfaces[0]);
            int triangles = SaveNavMeshAsset(surfaces[0]);
            WorldBoundaryBuilder.Build(root.transform, BasinHoles(placement));
            Physics.SyncTransforms();
            int links = NavMeshIslandLinker.LinkIslands(root.transform, SeedFor(placement));   // WP-038R: after the boundary
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(LogPrefix + "NavMesh re-baked at climb " + NavMeshBodyClimb + " m (+ " + railVolumes + " bridge rail volumes / skirts): " + triangles + " triangles, " + links + " island links; boundary rebuilt");
        }

        /// <summary>WP-038P · The connectivity seed: the player start on the NavMesh.</summary>
        public static Vector3 SeedFor(WorldPlacementAsset placement)
        {
            return NavMesh.SamplePosition(placement.playerStart, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : placement.playerStart;
        }

        private static int SaveNavMeshAsset(NavMeshSurface surface)
        {
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
                  .Append(e.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(e.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(e.z.ToString("F3", CultureInfo.InvariantCulture)).Append('|')
                  .Append(t.gameObject.layer).Append('|').Append((int)GameObjectUtility.GetStaticEditorFlags(t.gameObject)).Append('|')
                  .Append(tag != null ? tag.Region + "/" + tag.Role + "/" + tag.NavMesh : "-").Append('|')
                  .Append(FirstMaterialName(t)).Append('\n');
            }

            return Sha256(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static string FirstMaterialName(Transform t)
        {
            var r = t.GetComponentInChildren<Renderer>(true);
            Material m = r != null ? r.sharedMaterial : null;
            return m != null ? m.name : "-";
        }

        // ---------------------------------------------------------------- settings

        /// <summary>
        /// The world scene is a RELEASE scene (enabled). WP-015 (ADR-015-2): disabled entries are
        /// dev-only scenes (pilot, showcase) and keep their place; a missing world scene is
        /// inserted after the last enabled entry so the release list stays contiguous.
        /// </summary>
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

            int insertAt = 0;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].enabled) insertAt = i + 1;
            }

            scenes.Insert(insertAt, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log(LogPrefix + "World scene inserted into the build settings at index " + insertAt + " (after the last release scene).");
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

