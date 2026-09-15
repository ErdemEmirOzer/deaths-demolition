using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Layout JSON → <see cref="GoldenSliceLayoutAsset"/>.
    ///
    /// Reads <c>AP001_GoldenSliceLayout.json</c> (823 family instances in glTF space,
    /// extracted from GoldenSlice.glb by the WP-011 manifest tooling), converts every
    /// placement through <see cref="ArtAxis"/> and writes the ScriptableObject the
    /// spawner reads. The asset is updated in place so its GUID - referenced by the
    /// pilot scene - never changes.
    ///
    /// Scale rule (brief B-4): 26 instances have their scale baked into a unique mesh
    /// in the source; for those the JSON carries <c>derived_scale_from_bounds</c>, the
    /// scale that puts the CANONICAL family mesh on the same AABB. The asset stores
    /// that, so the prefab (canonical) plus the entry scale reproduces the source
    /// footprint. The AABB itself is stored too, converted, for the parity test.
    ///
    /// Both hashes are recorded: the GLB hash the JSON declares as its source and the
    /// hash of the JSON bytes actually read, so the gate can tell "stale asset" from
    /// "stale JSON".
    /// </summary>
    public static class GoldenSliceLayoutBuilder
    {
        /// <summary><c>AP001_M_&lt;role&gt;_v009[.001]</c> → <c>role</c>.</summary>
        private static readonly Regex MaterialRole = new Regex(
            @"^(?:AP001_M_)?(?<role>.+?)(?:_v\d+)?(?:\.\d+)?$", RegexOptions.CultureInvariant);

        public static GoldenSliceLayoutAsset Build()
        {
            string jsonPath = ArtPipelinePaths.LayoutJson;
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException("[ArtPipeline] Layout JSON is missing", jsonPath);
            }

            byte[] bytes = File.ReadAllBytes(jsonPath);
            string jsonSha = ArtTextureRepack.Sha256Hex(bytes);
            Dictionary<string, object> root = ArtJson.AsObject(ArtJson.Parse(Encoding.UTF8.GetString(bytes)), "layout");

            string assetPath = ArtPipelinePaths.LayoutAsset;
            var asset = AssetDatabase.LoadAssetAtPath<GoldenSliceLayoutAsset>(assetPath);
            bool created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<GoldenSliceLayoutAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            Dictionary<string, object> sourceGlb = ArtJson.AsObject(ArtJson.Require(root, "source_glb"), "source_glb");
            asset.sourceGlbSha256 = ArtJson.AsString(ArtJson.Require(sourceGlb, "sha256"), "source_glb.sha256");
            asset.sourceJsonSha256 = jsonSha;
            asset.instanceCountTotal = ArtJson.AsInt(ArtJson.Require(root, "instance_count_total"), "instance_count_total");
            asset.familyInstanceCount = ArtJson.AsInt(ArtJson.Require(root, "family_instance_count"), "family_instance_count");
            asset.nonFamilyUniqueCount = ArtJson.AsInt(ArtJson.Require(root, "non_family_unique_count"), "non_family_unique_count");

            List<object> instances = ArtJson.AsList(ArtJson.Require(root, "family_instances"), "family_instances");
            var entries = new List<GoldenSliceLayoutAsset.Entry>(instances.Count);
            var unknownFamilies = new SortedSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < instances.Count; i++)
            {
                string what = "family_instances[" + i + "]";
                Dictionary<string, object> o = ArtJson.AsObject(instances[i], what);
                GoldenSliceLayoutAsset.Entry entry = ReadEntry(o, what);
                if (!ArtFamilies.IsKnown(entry.family))
                {
                    unknownFamilies.Add(entry.family);
                }

                entries.Add(entry);
            }

            asset.entries = entries;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            if (entries.Count != asset.familyInstanceCount)
            {
                Debug.LogWarning("[ArtPipeline] Layout declares " + asset.familyInstanceCount +
                                 " family instances but lists " + entries.Count + ".");
            }

            if (unknownFamilies.Count > 0)
            {
                Debug.LogWarning("[ArtPipeline] Layout references families outside ArtFamilies.All: " +
                                 string.Join(", ", unknownFamilies) + ". Their instances will be reported missing.");
            }

            Debug.Log("[ArtPipeline] Layout asset " + (created ? "created" : "updated") + ": " + assetPath + " (" +
                      entries.Count + " entries, json sha256 " + jsonSha.Substring(0, 12) + "…, glb sha256 " +
                      asset.sourceGlbSha256.Substring(0, Math.Min(12, asset.sourceGlbSha256.Length)) + "…)");
            return asset;
        }

        /// <summary>The material role in a source material name, e.g. <c>AP001_M_sakura_purple_v009</c> → <c>sakura_purple</c>.</summary>
        public static string RoleFromMaterialName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return string.Empty;
            }

            Match m = MaterialRole.Match(materialName);
            return m.Success ? m.Groups["role"].Value : materialName;
        }

        private static GoldenSliceLayoutAsset.Entry ReadEntry(Dictionary<string, object> o, string what)
        {
            var entry = new GoldenSliceLayoutAsset.Entry
            {
                name = ArtJson.AsString(ArtJson.Require(o, "name"), what + ".name"),
                family = ArtJson.AsString(ArtJson.Require(o, "family"), what + ".family"),
                lodInSource = ArtJson.AsInt(ArtJson.Require(o, "lod_in_source"), what + ".lod_in_source"),
                uniqueMesh = ArtJson.AsBool(ArtJson.Require(o, "unique_mesh"), what + ".unique_mesh"),
                triangles = ArtJson.AsInt(ArtJson.Require(o, "triangles"), what + ".triangles")
            };

            List<object> materials = ArtJson.AsList(ArtJson.Require(o, "materials"), what + ".materials");
            entry.material = materials.Count > 0
                ? RoleFromMaterialName(ArtJson.AsString(materials[0], what + ".materials[0]"))
                : string.Empty;

            entry.position = ArtAxis.GltfToContractPosition(
                ArtJson.ToVector3(ArtJson.Require(o, "gltf_translation"), what + ".gltf_translation"));

            Quaternion rotation = ArtAxis.GltfToContractRotation(
                ArtJson.ToQuaternion(ArtJson.Require(o, "gltf_rotation"), what + ".gltf_rotation"));

            // Six-decimal JSON components drift from unit length in the last digit;
            // normalising keeps Transform.localRotation from renormalising differently
            // per platform.
            entry.rotation = Quaternion.Normalize(rotation);

            object derived = ArtJson.Get(o, "derived_scale_from_bounds");
            object scaleSource = entry.uniqueMesh && derived != null ? derived : ArtJson.Require(o, "gltf_scale");
            entry.scale = ArtAxis.GltfToContractScale(ArtJson.ToVector3(scaleSource, what + ".scale"));

            // null in the JSON for families the AP export did not classify; the family
            // table is the authority there, and for the classified ones the two agree.
            object occluder = ArtJson.Get(o, "occluder_candidate");
            entry.occluderCandidate = occluder is bool flag ? flag : ArtFamilies.IsOccluderCandidate(entry.family);

            Bounds aabb = ArtAxis.GltfAabbToContract(
                ArtJson.ToVector3(ArtJson.Require(o, "world_aabb_gltf_min"), what + ".world_aabb_gltf_min"),
                ArtJson.ToVector3(ArtJson.Require(o, "world_aabb_gltf_max"), what + ".world_aabb_gltf_max"));
            entry.aabbMin = aabb.min;
            entry.aabbMax = aabb.max;

            return entry;
        }
    }
}
