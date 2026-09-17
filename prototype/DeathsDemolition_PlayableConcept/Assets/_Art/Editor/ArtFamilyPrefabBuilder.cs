using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Builds <c>SM_&lt;family&gt;.prefab</c> for the eighteen families and the
    /// library that indexes them (ADR-011-1 §Prefab).
    ///
    /// Hierarchy:
    ///   SM_&lt;family&gt;            LODGroup (CrossFade), layer, [OccluderFadeState, disabled]
    ///     Geometry               localRotation = ArtImportContract.GeometryRotation (+90° about X)
    ///       LOD0 / LOD1 / LOD2   MeshFilter (FBX Mesh sub-asset) + MeshRenderer (M_&lt;role&gt;)
    ///       COL_0 … COL_n        BoxCollider (CapsuleCollider for sakura_trunk) from the part mesh bounds
    ///
    /// WHY THE GEOMETRY CHILD. Under the Import Contract the Mesh sub-asset holds
    /// (bx, by, −bz) of the Blender vertex and the importer's own node carries +90° about
    /// X (measured, study2); the AP-001 contract space is (bx, bz, by) = Rx(+90°)·mesh.
    /// Applying that exact rotation on one child (rather than trusting the node's 89.98°
    /// float drift) means the prefab ROOT is in contract space, so the layout's TRS goes
    /// on the root untouched and the AABB parity test compares like with like.
    ///
    /// WHY PRIMITIVE COLLIDERS. The AP export marks each collider part as a box (one
    /// cylinder), and a BoxCollider needs no readable mesh, no cooking and no NavMesh
    /// voxelisation of triangles. The bounds of the imported part mesh ARE the box, in
    /// mesh space, which is exactly the space of the Geometry child they sit under.
    ///
    /// WHY LOD THRESHOLDS FROM CAMERA GEOMETRY. See <see cref="TransitionHeights"/>.
    ///
    /// The occluder families also get <see cref="OccluderFadeState"/> on the root,
    /// disabled. CameraOcclusionFader resolves the component from the hit collider by
    /// walking UP; with the colliders and the renderers as siblings it finds neither a
    /// renderer above the collider nor one below it and would never fade the piece.
    /// Pre-placing the state on the root gives the fader its owner and costs nothing
    /// while disabled (the fader enables it on first occlusion).
    /// </summary>
    public static class ArtFamilyPrefabBuilder
    {
        public const string GeometryName = "Geometry";

        // -------------------------------------------------- LOD distance contract (ADR-012-1)

        /// <summary>
        /// The Quality level the game ships with (Ultra) applies <c>lodBias</c> 2, which
        /// doubles every LODGroup distance. WP-011 measured exactly that (LOD0 held to 64 m
        /// for a 32 m threshold, evidence iter5). ADR-012-1 therefore authors the thresholds
        /// for the INTENDED distances divided by the shipped bias, so the switches happen
        /// where the camera/fog contract says: 32 m (LOD0→1), 52 m (LOD1→2, fog start),
        /// 138 m (cull, fog end). The bias itself stays 2 (QualitySettings untouched).
        /// </summary>
        public const float ShippedLodBias = 2f;

        /// <summary>Intended metres at which LOD0 hands over to LOD1 (at the shipped lodBias).</summary>
        public const float IntendedLod0MaxDistance = 32f;

        /// <summary>Intended metres at which LOD1 hands over to LOD2 (fog start).</summary>
        public const float IntendedLod1MaxDistance = 52f;

        /// <summary>Intended metres at which the object is culled (fog end).</summary>
        public const float IntendedCullDistance = 138f;

        /// <summary>Authored (lodBias 1) metres at which LOD0 hands over to LOD1: 16.</summary>
        public const float Lod0MaxDistance = IntendedLod0MaxDistance / ShippedLodBias;

        /// <summary>Authored metres at which LOD1 hands over to LOD2: 26.</summary>
        public const float Lod1MaxDistance = IntendedLod1MaxDistance / ShippedLodBias;

        /// <summary>Authored metres at which the object is culled: 69.</summary>
        public const float CullDistance = IntendedCullDistance / ShippedLodBias;

        /// <summary>
        /// Visible world height per metre of distance for the prototype camera:
        /// 2·tan(42° / 2) = 0.768 (vertical FOV 42, PrototypeCameraRig).
        /// </summary>
        public const float VisibleHeightPerMetre = 0.768f;

        /// <summary>Objects flatter than this are treated as this tall so ground-hugging pieces still get sane thresholds.</summary>
        public const float MinObjectHeight = 0.5f;

        public const float MinTransition = 0.001f;
        public const float MaxTransition = 0.95f;

        public static void BuildAll()
        {
            string[] families = ArtFamilies.All;
            for (int i = 0; i < families.Length; i++)
            {
                Build(families[i]);
            }

            BuildLibrary();
            Debug.Log("[ArtPipeline] Family prefabs: " + families.Length + " under " + ArtPipelinePaths.FamiliesRoot);
        }

        /// <summary>
        /// Builds the prefab for one family in memory, saves it over any existing asset
        /// at the contract path (GUID kept) and returns the saved prefab asset. Throws
        /// when an FBX mesh, the material or a project layer is missing.
        /// </summary>
        public static GameObject Build(string family)
        {
            if (!ArtFamilies.IsKnown(family))
            {
                throw new ArgumentException("[ArtPipeline] Unknown family '" + family + "'", nameof(family));
            }

            string role = ArtFamilies.MaterialRole(family);
            string materialPath = ArtPipelinePaths.Material(role);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                throw new InvalidOperationException("[ArtPipeline] Material missing for '" + family + "': " +
                                                    materialPath + " (run ArtMaterialBuilder first)");
            }

            var lods = new Mesh[3];
            for (int lod = 0; lod < 3; lod++)
            {
                lods[lod] = LoadSingleMesh(ArtPipelinePaths.FamilyLod(family, lod));
            }

            Mesh[] colliderMeshes = ArtFamilies.HasCollider(family)
                ? LoadColliderMeshes(family)
                : Array.Empty<Mesh>();

            bool occluder = ArtFamilies.IsOccluderCandidate(family);
            int layer = occluder ? PrototypeLayers.CameraOccluder : PrototypeLayers.Environment;
            if (layer < 0)
            {
                throw new InvalidOperationException("[ArtPipeline] Layer '" +
                                                    (occluder ? PrototypeLayers.CameraOccluderName : PrototypeLayers.EnvironmentName) +
                                                    "' does not exist in this project.");
            }

            var root = new GameObject("SM_" + family);
            try
            {
                var geometry = new GameObject(GeometryName);
                geometry.transform.SetParent(root.transform, false);
                geometry.transform.localPosition = Vector3.zero;
                geometry.transform.localRotation = ArtImportContract.GeometryRotation;
                geometry.transform.localScale = Vector3.one;

                var renderers = new Renderer[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    renderers[lod] = BuildLod(geometry.transform, lod, lods[lod], material);
                }

                for (int i = 0; i < colliderMeshes.Length; i++)
                {
                    BuildCollider(geometry.transform, i, colliderMeshes[i], family == "sakura_trunk");
                }

                var group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = true;

                Bounds prefabBounds = TransformBounds(lods[0].bounds, renderers[0].transform.localToWorldMatrix);
                float[] heights = TransitionHeights(prefabBounds);
                group.SetLODs(new[]
                {
                    new LOD(heights[0], new[] { renderers[0] }),
                    new LOD(heights[1], new[] { renderers[1] }),
                    new LOD(heights[2], new[] { renderers[2] })
                });
                group.RecalculateBounds();

                if (occluder)
                {
                    OccluderFadeState fade = root.AddComponent<OccluderFadeState>();
                    fade.enabled = false;
                }

                SetLayerRecursively(root, layer);

                string path = ArtPipelinePaths.FamilyPrefab(family);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException("[ArtPipeline] SaveAsPrefabAsset failed for " + path);
                }

                Debug.Log("[ArtPipeline] Prefab " + path + ": layer " + LayerMask.LayerToName(layer) +
                          ", height " + prefabBounds.size.y.ToString("0.###") + " m, LOD " +
                          heights[0].ToString("0.####") + " / " + heights[1].ToString("0.####") + " / " +
                          heights[2].ToString("0.####") + ", colliders " + colliderMeshes.Length);
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// screenRelativeTransitionHeight for LOD0, LOD1 and the cull, from the prefab's
        /// canonical bounds and the camera geometry of the prototype rig.
        ///
        /// The camera sees 0.768·d metres of height at distance d (vertical FOV 42°), so
        /// an object h metres tall covers h / (0.768·d) of the screen there. The intended
        /// distances are 32 m (LOD0 → LOD1), 52 m (LOD1 → LOD2, fog start) and 138 m
        /// (cull, fog end); the authored distances are those divided by the shipped
        /// lodBias (ADR-012-1). Each threshold is clamped to [0.001, 0.95] and the three
        /// are forced strictly decreasing, which LODGroup requires.
        ///
        /// Measured caveat for the ADR: Unity evaluates a LODGroup against its largest
        /// bounds extent (<c>LODGroup.size</c>), not its height. For families wider than
        /// they are tall the effective switch distance is therefore d · (size / h); the
        /// probe's LOD counter reports what actually happens. Quality lodBias (2 on
        /// Ultra) doubles every distance on top of that.
        /// </summary>
        /// <summary>WP-038G · AP-017 litter / piles / breakables: cull at 40 m (intended; / lodBias like the contract distances) — small ground dressing the −49° camera never sees past ~30 m.</summary>
        public const float IntendedPropCullDistance = 40f;
        public const float PropCullDistance = IntendedPropCullDistance / ShippedLodBias;

        /// <summary>Contract thresholds with a family-specific cull distance (LOD0 / LOD1 distances stay; the cull moves in, never out).</summary>
        public static float[] TransitionHeights(Bounds prefabBounds, float cullDistance)
        {
            float h = Mathf.Max(MinObjectHeight, Mathf.Max(prefabBounds.size.x, Mathf.Max(prefabBounds.size.y, prefabBounds.size.z)));
            float[] distances = { Mathf.Min(Lod0MaxDistance, cullDistance * 0.5f), Mathf.Min(Lod1MaxDistance, cullDistance * 0.75f), Mathf.Min(CullDistance, cullDistance) };
            var result = new float[3];
            for (int i = 0; i < 3; i++) result[i] = Mathf.Clamp(h / (VisibleHeightPerMetre * distances[i]), MinTransition, MaxTransition);
            for (int i = 1; i < 3; i++) if (result[i] >= result[i - 1]) result[i] = result[i - 1] * 0.5f;
            return result;
        }

        public static float[] TransitionHeights(Bounds prefabBounds)
        {
            // Unity evaluates a LODGroup against LODGroup.size, which is the LARGEST extent of
            // its bounds, not the height. Using the same measure here is what makes the
            // contract distances (32 / 52 / 138 m) the distances the group actually switches
            // at; with the height alone a 7 m wide, 8 cm tall slab would have switched 14x
            // further out (PRA K-10).
            float h = Mathf.Max(MinObjectHeight, Mathf.Max(prefabBounds.size.x, Mathf.Max(prefabBounds.size.y, prefabBounds.size.z)));
            float[] distances = { Lod0MaxDistance, Lod1MaxDistance, CullDistance };

            var result = new float[3];
            for (int i = 0; i < 3; i++)
            {
                result[i] = Mathf.Clamp(h / (VisibleHeightPerMetre * distances[i]), MinTransition, MaxTransition);
            }

            // Only reachable when the 0.95 ceiling flattens two thresholds (an object
            // taller than ~38 m); halving keeps the order without inventing a distance.
            for (int i = 1; i < 3; i++)
            {
                if (result[i] >= result[i - 1])
                {
                    result[i] = result[i - 1] * 0.5f;
                }
            }

            return result;
        }

        /// <summary>
        /// Rewrites <see cref="ArtPipelinePaths.FamilyLibraryAsset"/> with the eighteen
        /// prefabs, in <see cref="ArtFamilies.All"/> order. Throws if any prefab is
        /// missing: an incomplete library would spawn a slice with silent holes.
        /// </summary>
        public static ArtFamilyLibrary BuildLibrary()
        {
            string path = ArtPipelinePaths.FamilyLibraryAsset;
            var library = AssetDatabase.LoadAssetAtPath<ArtFamilyLibrary>(path);
            bool created = library == null;
            if (created)
            {
                library = ScriptableObject.CreateInstance<ArtFamilyLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }

            var items = new List<ArtFamilyLibrary.Item>(ArtFamilies.All.Length);
            var missing = new List<string>();
            for (int i = 0; i < ArtFamilies.All.Length; i++)
            {
                string family = ArtFamilies.All[i];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPipelinePaths.FamilyPrefab(family));
                if (prefab == null)
                {
                    missing.Add(family);
                }

                items.Add(new ArtFamilyLibrary.Item { family = family, prefab = prefab });
            }

            library.items = items;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException("[ArtPipeline] Family library is incomplete; no prefab for: " +
                                                    string.Join(", ", missing) + " (run ArtFamilyPrefabBuilder.BuildAll)");
            }

            Debug.Log("[ArtPipeline] Family library " + (created ? "created" : "updated") + ": " + path + " (" +
                      items.Count + " items)");
            return library;
        }

        // ---------------------------------------------------------------- internals

        private static Renderer BuildLod(Transform geometry, int lod, Mesh mesh, Material material)
        {
            var go = new GameObject("LOD" + lod);
            go.transform.SetParent(geometry, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // Same renderer contract as WorldPiece so the pilot and the blockout are
            // measured under identical renderer settings. LOD0/1 cast; LOD2 does not
            // (ADR-012-1): it lives past the 45 m shadow distance once the thresholds are
            // authored for the shipped lodBias, so its shadow pass only cost draw calls.
            renderer.shadowCastingMode = lod == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            return renderer;
        }

        /// <summary>
        /// One primitive per collider part, from the part mesh's bounds. The part
        /// meshes are axis-aligned boxes in mesh space (the trunk a cylinder), so centre
        /// and size ARE the collider. The trunk's axis is Blender Z, which the importer
        /// delivers as mesh −Z: the capsule takes the longest bounds axis as its
        /// direction (Z here, measured 0..−3.4 m) rather than assuming Y, so the capsule
        /// stands up regardless of which axis the import lands the cylinder on. Radius
        /// 0.35 and height 3.4 as in the AP-001 contract.
        /// </summary>
        private static void BuildCollider(Transform geometry, int index, Mesh mesh, bool capsule)
        {
            var go = new GameObject("COL_" + index);
            go.transform.SetParent(geometry, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Bounds b = mesh.bounds;
            if (capsule)
            {
                Vector3 size = b.size;
                int axis = size.z >= size.x && size.z >= size.y ? 2 : size.y >= size.x ? 1 : 0;
                float height = size[axis];
                float radius = 0f;
                for (int i = 0; i < 3; i++)
                {
                    if (i != axis)
                    {
                        radius = Mathf.Max(radius, size[i] * 0.5f);
                    }
                }

                var c = go.AddComponent<CapsuleCollider>();
                c.direction = axis;
                c.center = b.center;
                c.radius = radius;
                c.height = height;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
            }
        }

        private static Mesh LoadSingleMesh(string fbxPath)
        {
            Mesh[] meshes = LoadMeshes(fbxPath);
            if (meshes.Length == 0)
            {
                throw new InvalidOperationException("[ArtPipeline] No Mesh sub-asset in " + fbxPath +
                                                    " (is the FBX imported?)");
            }

            if (meshes.Length > 1)
            {
                Debug.LogWarning("[ArtPipeline] " + fbxPath + " has " + meshes.Length +
                                 " meshes; the contract expects one per LOD file. Using '" + meshes[0].name + "'.");
            }

            return meshes[0];
        }

        /// <summary>
        /// The collider parts in export order. Sub-asset order from the AssetDatabase is
        /// not a contract, so the index in <c>AP001_COL_&lt;family&gt;_&lt;i&gt;_v009_Export</c>
        /// decides; names that do not match fall back to ordinal order after the rest.
        /// </summary>
        private static Mesh[] LoadColliderMeshes(string family)
        {
            string path = ArtPipelinePaths.FamilyColliders(family);
            Mesh[] meshes = LoadMeshes(path);
            if (meshes.Length == 0)
            {
                throw new InvalidOperationException("[ArtPipeline] No collider Mesh sub-assets in " + path);
            }

            var pattern = new Regex("^AP001_COL_" + Regex.Escape(family) + @"_(\d+)_v\d+", RegexOptions.CultureInvariant);
            var keyed = new List<KeyValuePair<int, Mesh>>(meshes.Length);
            for (int i = 0; i < meshes.Length; i++)
            {
                Match m = pattern.Match(meshes[i].name ?? string.Empty);
                int index = m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
                keyed.Add(new KeyValuePair<int, Mesh>(index, meshes[i]));
            }

            keyed.Sort((a, b) =>
            {
                int byIndex = a.Key.CompareTo(b.Key);
                return byIndex != 0 ? byIndex : string.CompareOrdinal(a.Value.name, b.Value.name);
            });

            var sorted = new Mesh[keyed.Count];
            for (int i = 0; i < keyed.Count; i++)
            {
                sorted[i] = keyed[i].Value;
            }

            return sorted;
        }

        private static Mesh[] LoadMeshes(string fbxPath)
        {
            var list = new List<Mesh>();
            UnityEngine.Object[] representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath);
            if (representations != null)
            {
                for (int i = 0; i < representations.Length; i++)
                {
                    if (representations[i] is Mesh mesh)
                    {
                        list.Add(mesh);
                    }
                }
            }

            return list.ToArray();
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
