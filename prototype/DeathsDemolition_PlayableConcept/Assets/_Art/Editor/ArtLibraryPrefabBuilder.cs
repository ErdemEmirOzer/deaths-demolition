using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-012 · Builds one prefab per (family, variation) of an imported library from its
    /// generated manifest (ADR-012-2 §Prefab). Same contract as the AP-001 builder
    /// (<see cref="ArtFamilyPrefabBuilder"/>): Geometry child rotated by the exact
    /// <see cref="ArtImportContract.GeometryRotation"/>, LODGroup with the ADR-012-1
    /// thresholds, primitive colliders, layer policy, disabled <see cref="OccluderFadeState"/>
    /// on occluder roots — extended for what the production libraries add:
    ///
    ///   SM_&lt;family&gt;_V&lt;nn&gt;             LODGroup (CrossFade), layer, [OccluderFadeState]
    ///     Geometry                       Rx(+90°)
    ///       [&lt;part&gt;_]LOD0 / LOD1 / LOD2  one renderer per part per level; sub-mesh i → M_&lt;materials[i]&gt;
    ///       COL_&lt;i&gt;                      one primitive per proxy SHELL (box; capsule when prism-like)
    ///     Roof                           Rx(+90°), own LODGroup, CameraOccluder layer, OccluderFadeState
    ///       LOD0 / LOD1 / LOD2 / COL_i   (REQ-111: the corridor roof fades on its own)
    ///     Broken                         Rx(+90°), INACTIVE, own LODGroup (REQ-113 broken state)
    ///       LOD0 / LOD1 / LOD2 / COL_i
    ///
    /// WHY SHELL SPLITTING. AP-003B proxies are unions of axis-aligned boxes (up to 35 per
    /// mesh) and AP-003 proxies are single convex hulls; a MeshCollider would need a
    /// readable mesh at runtime for the NavMesh bake (the pilot bakes from physics
    /// colliders in the Player), which the Import Contract forbids. The generator records
    /// every loose part of the proxy (by the FBX's own vertex indices, the same "shell" the
    /// production receipt counts) with its bounds; the builder emits a BoxCollider per shell
    /// (a CapsuleCollider when the shell is a prism 2.5× longer than wide — culms, lantern
    /// shafts, trunks). Nothing at runtime reads vertices; the position-weld split is only
    /// the fallback for a manifest without shell boxes.
    ///
    /// Shadows: LOD2 never casts (WP-011 handover; the far band is inside the fog and the
    /// shadow distance is 45 m, so the cost bought nothing).
    /// </summary>
    public static class ArtLibraryPrefabBuilder
    {
        public const string GeometryName = ArtFamilyPrefabBuilder.GeometryName;
        public const string RoofName = "Roof";
        public const string BrokenName = "Broken";

        /// <summary>A proxy shell whose longest extent is this many times its middle extent becomes a capsule.</summary>
        public const float CapsuleAspect = 2.5f;

        /// <summary>A prism shell whose minor cross-section extent is at least this fraction of its middle extent counts as round (capsule).</summary>
        public const float CapsuleRoundness = 0.75f;

        /// <summary>Vertex positions closer than this are one position when splitting shells (welded exports; 0.1 mm).</summary>
        public const float ShellWeld = 0.0001f;

        public static void BuildAll()
        {
            string[] packages = ArtLibraryPaths.Packages;
            for (int i = 0; i < packages.Length; i++)
            {
                BuildLibrary(packages[i]);
            }
        }

        public static ArtLibraryManifest LoadManifest(string ap)
        {
            string path = ArtLibraryPaths.ManifestJson(ap);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("[ArtLibrary] Manifest missing: " + path, path);
            }

            return ArtLibraryManifest.FromJson(File.ReadAllText(path));
        }

        /// <summary>Builds every prefab of one library and rewrites its index asset. Throws on the first failure.</summary>
        public static ArtLibraryAsset BuildLibrary(string ap)
        {
            ArtLibraryManifest manifest = LoadManifest(ap);
            var items = new List<ArtLibraryAsset.Item>(manifest.VariationCount);
            int prefabs = 0;
            for (int f = 0; f < manifest.families.Count; f++)
            {
                ArtLibraryManifest.Family family = manifest.families[f];
                for (int v = 0; v < family.variations.Count; v++)
                {
                    ArtLibraryManifest.Variation variation = family.variations[v];
                    GameObject prefab = Build(ap, family, variation);
                    items.Add(new ArtLibraryAsset.Item { family = family.family, variation = variation.id, prefab = prefab });
                    prefabs++;
                }
            }

            string indexPath = ArtLibraryPaths.IndexAsset(ap);
            var index = AssetDatabase.LoadAssetAtPath<ArtLibraryAsset>(indexPath);
            bool created = index == null;
            if (created)
            {
                index = ScriptableObject.CreateInstance<ArtLibraryAsset>();
                AssetDatabase.CreateAsset(index, indexPath);
            }

            index.ap = ap;
            index.items = items;
            EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
            Debug.Log("[ArtLibrary] " + ap + ": " + prefabs + " prefabs, index " + (created ? "created" : "updated") + " " + indexPath);
            return index;
        }

        /// <summary>Builds one variation prefab in memory and saves it over the contract path (GUID kept).</summary>
        public static GameObject Build(string ap, ArtLibraryManifest.Family family, ArtLibraryManifest.Variation variation)
        {
            int envLayer = PrototypeLayers.Environment;
            int occLayer = PrototypeLayers.CameraOccluder;
            if (envLayer < 0 || occLayer < 0)
            {
                throw new InvalidOperationException("[ArtLibrary] Layers '" + PrototypeLayers.EnvironmentName + "' / '" +
                                                    PrototypeLayers.CameraOccluderName + "' do not exist in this project.");
            }

            var root = new GameObject(variation.prefab);
            try
            {
                Transform geometry = MakeRotatedChild(root.transform, GeometryName);
                var mainLods = new List<Renderer>[3];
                Transform roof = null;
                Transform broken = null;
                var roofLods = new List<Renderer>[3];
                var brokenLods = new List<Renderer>[3];
                for (int i = 0; i < 3; i++)
                {
                    mainLods[i] = new List<Renderer>();
                    roofLods[i] = new List<Renderer>();
                    brokenLods[i] = new List<Renderer>();
                }

                for (int p = 0; p < variation.parts.Count; p++)
                {
                    ArtLibraryManifest.Part part = variation.parts[p];
                    if (part.lods == null || part.lods.Count != 3)
                    {
                        throw new InvalidOperationException("[ArtLibrary] " + variation.prefab + " part '" + part.name + "' must have 3 LODs.");
                    }

                    Transform parent;
                    List<Renderer>[] sink;
                    if (part.IsRoof)
                    {
                        roof = roof ?? MakeRotatedChild(root.transform, RoofName);
                        parent = roof;
                        sink = roofLods;
                    }
                    else if (part.IsBroken)
                    {
                        broken = broken ?? MakeRotatedChild(root.transform, BrokenName);
                        parent = broken;
                        sink = brokenLods;
                    }
                    else
                    {
                        parent = geometry;
                        sink = mainLods;
                    }

                    for (int lod = 0; lod < 3; lod++)
                    {
                        ArtLibraryManifest.Lod entry = part.lods[lod];
                        Mesh mesh = LoadMesh(ap, family.family, entry.fbx, entry.@object);
                        Material[] materials = ResolveMaterials(mesh, entry, variation.prefab);
                        string name = (string.IsNullOrEmpty(part.name) ? string.Empty : part.name + "_") + "LOD" + lod;
                        Renderer built = BuildLod(parent, name, mesh, materials, lod);
                        // WP-038G (M3 closure K1): AP-017 derivative litter / piles cast no shadow (flat, ≤ 0.4 m; ~450 instances × 2 cascades).
                        if (family.IsDerivative) built.shadowCastingMode = ShadowCastingMode.Off;
                        sink[lod].Add(built);
                    }
                }

                int colliderIndex = 0;
                int roofColliders = 0;
                int brokenColliders = 0;
                for (int c = 0; c < variation.colliders.Count; c++)
                {
                    ArtLibraryManifest.Collider col = variation.colliders[c];
                    if (col.IsNone) continue;   // WP-038B: AP-017 litter footprint, no collision
                    Mesh mesh = LoadMesh(ap, family.family, col.fbx, col.@object);
                    Transform parent;
                    if (col.broken)
                    {
                        broken = broken ?? MakeRotatedChild(root.transform, BrokenName);
                        parent = broken;
                    }
                    else if (col.part == "roof" && roof != null)
                    {
                        parent = roof;
                    }
                    else
                    {
                        parent = geometry;
                    }

                    List<Shell> shells = ManifestShells(col) ?? SplitShells(mesh);
                    for (int s = 0; s < shells.Count; s++)
                    {
                        int index = parent == geometry ? colliderIndex++ : parent == roof ? roofColliders++ : brokenColliders++;
                        BuildCollider(parent, index, shells[s], col.IsTrigger);
                    }
                }

                // Main LODGroup (root). Every part that is not roof/broken switches together.
                // WP-038G: AP-017 families cull at 40 m (intended) instead of the 138 m contract distance — the gameplay camera sees ≤ 30 m of ground.
                AddLodGroup(root, mainLods, root.transform, family.IsDerivative || family.IsBreakable ? ArtFamilyPrefabBuilder.PropCullDistance : 0f);
                bool occluder = family.occluder;
                if (occluder)
                {
                    OccluderFadeState fade = root.AddComponent<OccluderFadeState>();
                    fade.enabled = false;
                }

                SetLayerRecursively(root, occluder ? occLayer : envLayer);

                if (roof != null)
                {
                    AddLodGroup(roof.gameObject, roofLods, root.transform);
                    OccluderFadeState fade = roof.gameObject.AddComponent<OccluderFadeState>();
                    fade.enabled = false;
                    SetLayerRecursively(roof.gameObject, occLayer);
                }

                if (broken != null)
                {
                    AddLodGroup(broken.gameObject, brokenLods, root.transform, family.IsBreakable ? ArtFamilyPrefabBuilder.PropCullDistance : 0f);
                    SetLayerRecursively(broken.gameObject, envLayer);
                    broken.gameObject.SetActive(false);
                    // WP-013 (REQ-113): the intact/broken switch lives on the prefab root; WP-038B: AP-017 event id / residue cell.
                    ArtVfxBuilder.Bind(root, variation.breakEventId, variation.residueCell);
                }

                if (variation.HasSwim)
                {
                    // WP-038L · AP-017 koi shoal: the swim loop on the root; the cards sit under the pond surface and cast nothing.
                    ArtSwimLoop swim = root.AddComponent<ArtSwimLoop>();
                    swim.Configure(variation.swim.radiusM[0], variation.swim.radiusM[1], variation.swim.speedMps);
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                }

                string path = ArtLibraryPaths.Prefab(ap, family.family, variation.prefab);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException("[ArtLibrary] SaveAsPrefabAsset failed for " + path);
                }

                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- internals

        private static Transform MakeRotatedChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = ArtImportContract.GeometryRotation;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static Renderer BuildLod(Transform parent, string name, Mesh mesh, Material[] materials, int lod)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = lod == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return renderer;
        }

        private static void AddLodGroup(GameObject owner, List<Renderer>[] lods, Transform prefabRoot, float cullDistanceOverride = 0f)
        {
            if (lods[0].Count == 0)
            {
                return;
            }

            var group = owner.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;

            Bounds bounds = PrefabSpaceBounds(lods[0], prefabRoot);
            float[] heights = cullDistanceOverride > 0f ? ArtFamilyPrefabBuilder.TransitionHeights(bounds, cullDistanceOverride) : ArtFamilyPrefabBuilder.TransitionHeights(bounds);
            group.SetLODs(new[]
            {
                new LOD(heights[0], lods[0].ToArray()),
                new LOD(heights[1], lods[1].ToArray()),
                new LOD(heights[2], lods[2].ToArray())
            });
            group.RecalculateBounds();
        }

        /// <summary>Union of the renderers' mesh bounds in the prefab root's space (the AP world contract space).</summary>
        public static Bounds PrefabSpaceBounds(List<Renderer> renderers, Transform prefabRoot)
        {
            Bounds result = default;
            bool first = true;
            for (int i = 0; i < renderers.Count; i++)
            {
                var filter = renderers[i].GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Matrix4x4 m = prefabRoot.worldToLocalMatrix * renderers[i].transform.localToWorldMatrix;
                Bounds b = TransformBounds(filter.sharedMesh.bounds, m);
                if (first)
                {
                    result = b;
                    first = false;
                }
                else
                {
                    result.Encapsulate(b);
                }
            }

            return result;
        }

        private static Material[] ResolveMaterials(Mesh mesh, ArtLibraryManifest.Lod entry, string prefab)
        {
            string[] roles = entry.materials ?? Array.Empty<string>();
            int sub = mesh.subMeshCount;
            string[] chosen;
            if (roles.Length == sub)
            {
                chosen = roles;
            }
            else
            {
                // The importer drops a slot that no face uses (bamboo_fence: wood slot, 0 faces).
                var used = new List<string>();
                for (int i = 0; i < roles.Length; i++)
                {
                    if (entry.facesPerSlot == null || i >= entry.facesPerSlot.Length || entry.facesPerSlot[i] > 0)
                    {
                        used.Add(roles[i]);
                    }
                }

                if (used.Count != sub)
                {
                    throw new InvalidOperationException("[ArtLibrary] " + prefab + " / " + entry.@object + ": mesh has " + sub +
                                                        " sub-meshes but the manifest lists " + roles.Length + " slots (" +
                                                        used.Count + " with faces): " + string.Join(", ", roles));
                }

                chosen = used.ToArray();
            }

            var materials = new Material[chosen.Length];
            for (int i = 0; i < chosen.Length; i++)
            {
                string path = ArtPipelinePaths.Material(chosen[i]);
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (materials[i] == null)
                {
                    throw new InvalidOperationException("[ArtLibrary] Material missing for " + prefab + ": " + path +
                                                        " (run the material builders first)");
                }
            }

            return materials;
        }

        private static Mesh LoadMesh(string ap, string family, string fbx, string objectName)
        {
            string path = ArtLibraryPaths.Fbx(ap, family, fbx);
            UnityEngine.Object[] reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            if (reps == null || reps.Length == 0)
            {
                throw new InvalidOperationException("[ArtLibrary] No sub-assets in " + path + " (is the FBX imported?)");
            }

            Mesh prefix = null;
            for (int i = 0; i < reps.Length; i++)
            {
                if (!(reps[i] is Mesh mesh))
                {
                    continue;
                }

                if (string.Equals(mesh.name, objectName, StringComparison.Ordinal))
                {
                    return mesh;
                }

                if (prefix == null && mesh.name.StartsWith(objectName, StringComparison.Ordinal))
                {
                    prefix = mesh;
                }
            }

            if (prefix != null)
            {
                return prefix;
            }

            throw new InvalidOperationException("[ArtLibrary] Mesh '" + objectName + "' not found in " + path);
        }

        /// <summary>One connected component of a proxy mesh: its mesh-space bounds and how many distinct positions it has (8 = box).</summary>
        public struct Shell
        {
            public Bounds Bounds;
            public int Positions;
            public bool IsBox => Positions <= 8;
        }

        /// <summary>A hull shell at least this many times taller than wide, and no wider than <see cref="LeaningColumnMaxWidth"/>, is a culm: a capsule along its base→tip axis.</summary>
        public const float LeaningColumnAspect = 2.5f;
        public const float LeaningColumnMaxWidth = 0.6f;
        public const float LeaningColumnMinRadius = 0.06f;
        public const float LeaningColumnMaxRadius = 0.15f;

        private static void BuildCollider(Transform parent, int index, Shell shellInfo, bool trigger)
        {
            Bounds shell = shellInfo.Bounds;
            if (TryBuildLeaningColumn(parent, index, shellInfo, trigger))
            {
                return;
            }

            var go = new GameObject("COL_" + index);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Vector3 size = shell.size;
            int axis = size.z >= size.x && size.z >= size.y ? 2 : size.y >= size.x ? 1 : 0;
            float longest = size[axis];
            float middle = 0f;
            float minor = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (i != axis)
                {
                    middle = Mathf.Max(middle, size[i]);
                    minor = Mathf.Min(minor, size[i]);
                }
            }

            // A capsule only for a STANDING prism that is long and round in cross-section (culm,
            // lantern shaft, trunk). Mesh-space z is Blender's up axis under the Import
            // Contract. A roof envelope or a beam is long but lies down, and its ends must
            // stay square: box.
            bool round = minor >= CapsuleRoundness * middle;
            bool standing = axis == 2;
            if (shell.size.magnitude > 0f && longest >= CapsuleAspect * Mathf.Max(middle, 0.0001f) && !shellInfo.IsBox && round && standing)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.direction = axis;
                capsule.center = shell.center;
                capsule.radius = middle * 0.5f;
                capsule.height = longest;
                capsule.isTrigger = trigger;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = shell.center;
                box.size = shell.size;
                box.isTrigger = trigger;
            }
        }

        /// <summary>
        /// WP-014 · A leaning culm (bamboo stalk/cluster hulls) is not a box: its hull bounds are
        /// a thin column whose footprint is the lean, and a box of those bounds blocks bullets,
        /// aim and the readability rays across the whole lean (route 42 read 4/5 blocked by a
        /// stalk 4 m away). The capsule runs from the base (the bounds corner nearest the
        /// origin at ground level) to the tip corner, with the culm's own radius.
        /// Mesh space: Blender up is mesh −Z (Import Contract).
        /// </summary>
        private static bool TryBuildLeaningColumn(Transform parent, int index, Shell shellInfo, bool trigger)
        {
            if (shellInfo.IsBox)
            {
                return false;
            }

            Bounds b = shellInfo.Bounds;
            float height = b.size.z;
            float width = Mathf.Max(b.size.x, b.size.y);
            if (height < LeaningColumnAspect * Mathf.Max(width, 0.0001f) || width > LeaningColumnMaxWidth || height < 1.5f)
            {
                return false;
            }

            // Base: the ground-level end (mesh z nearest 0, i.e. max.z ≤ 0 side); tip: the far corner.
            bool baseAtMax = Mathf.Abs(b.max.z) < Mathf.Abs(b.min.z);
            float baseZ = baseAtMax ? b.max.z : b.min.z;
            float tipZ = baseAtMax ? b.min.z : b.max.z;
            float tipX = Mathf.Abs(b.min.x) > Mathf.Abs(b.max.x) ? b.min.x : b.max.x;
            float tipY = Mathf.Abs(b.min.y) > Mathf.Abs(b.max.y) ? b.min.y : b.max.y;
            float baseX = Mathf.Abs(b.min.x) > Mathf.Abs(b.max.x) ? b.max.x : b.min.x;
            float baseY = Mathf.Abs(b.min.y) > Mathf.Abs(b.max.y) ? b.max.y : b.min.y;
            var basePoint = new Vector3(baseX, baseY, baseZ);
            var tipPoint = new Vector3(tipX, tipY, tipZ);
            Vector3 axis = tipPoint - basePoint;
            float length = axis.magnitude;
            if (length < 0.5f)
            {
                return false;
            }

            float radius = Mathf.Clamp(0.5f * Mathf.Min(b.size.x, b.size.y), LeaningColumnMinRadius, LeaningColumnMaxRadius);

            var go = new GameObject("COL_" + index);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = (basePoint + tipPoint) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axis / length);
            go.transform.localScale = Vector3.one;

            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.center = Vector3.zero;
            capsule.radius = radius;
            capsule.height = length + 2f * radius;
            capsule.isTrigger = trigger;
            return true;
        }

        /// <summary>
        /// The shells the manifest recorded from the FBX itself (loose parts by Blender vertex
        /// index), converted to mesh space; null when the manifest carries none. Preferred over
        /// <see cref="SplitShells"/>: touching boxes (a path slab and its curb) share positions
        /// after Unity's weld and would otherwise merge into one oversized box.
        /// </summary>
        public static List<Shell> ManifestShells(ArtLibraryManifest.Collider collider)
        {
            if (collider.shellBoxes == null || collider.shellBoxes.Count == 0)
            {
                return null;
            }

            var result = new List<Shell>(collider.shellBoxes.Count);
            for (int i = 0; i < collider.shellBoxes.Count; i++)
            {
                ArtLibraryManifest.ShellBox box = collider.shellBoxes[i];
                result.Add(new Shell { Bounds = box.MeshSpaceBounds(), Positions = box.positions });
            }

            return result;
        }

        /// <summary>
        /// Connected components of the mesh (by welded vertex position), each as its
        /// mesh-space bounds — the fallback for a manifest without shell boxes. Falls back
        /// to the whole mesh bounds when the vertex data is not readable in this context.
        /// </summary>
        public static List<Shell> SplitShells(Mesh mesh)
        {
            var result = new List<Shell>();
            Vector3[] vertices;
            int[] triangles;
            try
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ArtLibrary] " + mesh.name + ": vertex data not readable (" + e.Message + "); one box from mesh bounds.");
                result.Add(new Shell { Bounds = mesh.bounds, Positions = 8 });
                return result;
            }

            if (vertices.Length == 0 || triangles.Length == 0)
            {
                result.Add(new Shell { Bounds = mesh.bounds, Positions = 8 });
                return result;
            }

            // Weld by quantised position so split normals/UVs do not break a box into faces.
            var keyOf = new int[vertices.Length];
            var keys = new Dictionary<Vector3Int, int>();
            var uniquePositions = new List<Vector3>();
            float inv = 1f / ShellWeld;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                var q = new Vector3Int(Mathf.RoundToInt(v.x * inv), Mathf.RoundToInt(v.y * inv), Mathf.RoundToInt(v.z * inv));
                if (!keys.TryGetValue(q, out int id))
                {
                    id = uniquePositions.Count;
                    keys.Add(q, id);
                    uniquePositions.Add(v);
                }

                keyOf[i] = id;
            }

            var parent = new int[uniquePositions.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = keyOf[triangles[t]];
                int b = keyOf[triangles[t + 1]];
                int c = keyOf[triangles[t + 2]];
                Union(parent, a, b);
                Union(parent, a, c);
            }

            var boundsOf = new Dictionary<int, Bounds>();
            var countOf = new Dictionary<int, int>();
            for (int i = 0; i < uniquePositions.Count; i++)
            {
                int r = Find(parent, i);
                if (boundsOf.TryGetValue(r, out Bounds b))
                {
                    b.Encapsulate(uniquePositions[i]);
                    boundsOf[r] = b;
                    countOf[r] = countOf[r] + 1;
                }
                else
                {
                    boundsOf[r] = new Bounds(uniquePositions[i], Vector3.zero);
                    countOf[r] = 1;
                }
            }

            // Deterministic order: by minimum corner, so COL_i indices are stable between builds.
            var roots = new List<int>(boundsOf.Keys);
            roots.Sort((x, y) =>
            {
                Vector3 mx = boundsOf[x].min;
                Vector3 my = boundsOf[y].min;
                int byX = mx.x.CompareTo(my.x);
                if (byX != 0) return byX;
                int byY = mx.y.CompareTo(my.y);
                if (byY != 0) return byY;
                return mx.z.CompareTo(my.z);
            });

            for (int i = 0; i < roots.Count; i++)
            {
                result.Add(new Shell { Bounds = boundsOf[roots[i]], Positions = countOf[roots[i]] });
            }

            return result;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 min = local.min;
            Vector3 max = local.max;
            var result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) != 0 ? max.x : min.x,
                    (i & 2) != 0 ? max.y : min.y,
                    (i & 4) != 0 ? max.z : min.z);
                result.Encapsulate(matrix.MultiplyPoint3x4(corner));
            }

            return result;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
            }
        }
    }
}
