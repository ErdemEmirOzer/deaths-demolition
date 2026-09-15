using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · One text block describing what the art pipeline has produced: derived
    /// textures, materials, family prefabs (LOD levels, colliders, triangles), the family
    /// library, the layout asset, the two scenes and the build settings.
    ///
    /// It is printed at the end of <see cref="ArtPipelineMenu.RunAll"/> so the Editor.log
    /// of a batch run carries the inventory, and it is what a reviewer reads instead of
    /// clicking through eighteen prefabs. It never throws: a missing asset is a line that
    /// says MISSING, because the report is most useful precisely when something failed.
    /// </summary>
    public static class ArtPipelineReport
    {
        private const string LogPrefix = "[ArtPipeline] ";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Describe()
        {
            var sb = new StringBuilder(8192);
            sb.Append(LogPrefix).Append("Report ").Append(DateTime.UtcNow.ToString("o", Inv))
              .Append(" · Unity ").Append(Application.unityVersion).Append('\n');

            DescribeDerivedTextures(sb);
            DescribeMaterials(sb);
            DescribePrefabs(sb);
            DescribeLibrary(sb);
            DescribeLayout(sb);
            DescribeScenes(sb);
            DescribeBuildSettings(sb);

            return sb.ToString();
        }

        // ---------------------------------------------------------------- textures

        private static void DescribeDerivedTextures(StringBuilder sb)
        {
            string[] guids = AssetDatabase.IsValidFolder(ArtPipelinePaths.TexturesDerived)
                ? AssetDatabase.FindAssets("t:Texture2D", new[] { ArtPipelinePaths.TexturesDerived })
                : new string[0];

            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            paths.Sort(StringComparer.Ordinal);

            int sidecars = Directory.Exists(ArtPipelinePaths.TexturesDerived)
                ? Directory.GetFiles(ArtPipelinePaths.TexturesDerived, "*.json", SearchOption.TopDirectoryOnly).Length
                : 0;

            sb.Append("Derived textures (").Append(ArtPipelinePaths.TexturesDerived).Append("): ")
              .Append(paths.Count).Append(" textures, ").Append(sidecars).Append(" provenance sidecars\n");

            for (int i = 0; i < paths.Count; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                var importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;

                sb.Append("  - ").Append(Path.GetFileName(paths[i]));
                if (texture != null)
                {
                    sb.Append(' ').Append(texture.width).Append('x').Append(texture.height)
                      .Append(' ').Append(texture.format).Append(" mips ").Append(texture.mipmapCount);
                }
                else
                {
                    sb.Append(" (not imported)");
                }

                if (importer != null)
                {
                    sb.Append(importer.sRGBTexture ? " sRGB" : " linear");
                }

                sb.Append('\n');
            }
        }

        // --------------------------------------------------------------- materials

        private static void DescribeMaterials(StringBuilder sb)
        {
            string[] guids = AssetDatabase.IsValidFolder(ArtPipelinePaths.Materials)
                ? AssetDatabase.FindAssets("t:Material", new[] { ArtPipelinePaths.Materials })
                : new string[0];

            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            paths.Sort(StringComparer.Ordinal);

            sb.Append("Materials (").Append(ArtPipelinePaths.Materials).Append("): ").Append(paths.Count).Append('\n');

            for (int i = 0; i < paths.Count; i++)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                sb.Append("  - ").Append(Path.GetFileNameWithoutExtension(paths[i]));
                if (material == null)
                {
                    sb.Append(" (not loadable)\n");
                    continue;
                }

                sb.Append(": shader '").Append(material.shader != null ? material.shader.name : "null").Append('\'');
                AppendTextureSlot(sb, material, "_BaseMap", "base");
                AppendTextureSlot(sb, material, "_BumpMap", "normal");
                AppendTextureSlot(sb, material, "_MetallicGlossMap", "mask");
                AppendFloat(sb, material, "_Metallic", "metallic");
                AppendFloat(sb, material, "_Smoothness", "smoothness");
                sb.Append(material.enableInstancing ? " instancing" : " no-instancing");
                sb.Append(" keywords[").Append(string.Join(" ", material.shaderKeywords)).Append(']');
                sb.Append('\n');
            }
        }

        private static void AppendTextureSlot(StringBuilder sb, Material material, string property, string label)
        {
            if (!material.HasProperty(property))
            {
                return;
            }

            Texture texture = material.GetTexture(property);
            sb.Append(' ').Append(label).Append('=').Append(texture != null ? texture.name : "-");
        }

        private static void AppendFloat(StringBuilder sb, Material material, string property, string label)
        {
            if (!material.HasProperty(property))
            {
                return;
            }

            sb.Append(' ').Append(label).Append('=').Append(material.GetFloat(property).ToString("0.###", Inv));
        }

        // ----------------------------------------------------------------- prefabs

        private static void DescribePrefabs(StringBuilder sb)
        {
            string[] families = ArtFamilies.All;
            int present = 0;
            var lines = new StringBuilder();

            for (int i = 0; i < families.Length; i++)
            {
                string path = ArtPipelinePaths.FamilyPrefab(families[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                lines.Append("  - ").Append(Path.GetFileNameWithoutExtension(path));
                if (prefab == null)
                {
                    lines.Append(": MISSING (").Append(path).Append(")\n");
                    continue;
                }

                present++;
                DescribePrefab(lines, prefab);
                lines.Append('\n');
            }

            sb.Append("Family prefabs: ").Append(present).Append('/').Append(families.Length).Append('\n');
            sb.Append(lines.ToString());
        }

        private static void DescribePrefab(StringBuilder sb, GameObject prefab)
        {
            sb.Append(": layer ").Append(LayerMask.LayerToName(prefab.layer));

            var lodGroup = prefab.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                sb.Append(", LODs ").Append(lods.Length).Append(" [");
                for (int i = 0; i < lods.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(lods[i].screenRelativeTransitionHeight.ToString("0.000", Inv));
                }

                sb.Append("] fade ").Append(lodGroup.fadeMode);

                // Triangles per level, so a reviewer sees LOD0 > LOD1 > LOD2 without opening
                // the FBX; this is the number the AP-001 manifest drift test compares.
                sb.Append(", tris [");
                for (int i = 0; i < lods.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(Triangles(lods[i].renderers));
                }

                sb.Append(']');
            }
            else
            {
                sb.Append(", NO LODGroup");
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            sb.Append(", renderers ").Append(renderers.Length);

            Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
            int box = 0, capsule = 0, mesh = 0, other = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] is BoxCollider)
                {
                    box++;
                }
                else if (colliders[i] is CapsuleCollider)
                {
                    capsule++;
                }
                else if (colliders[i] is MeshCollider)
                {
                    mesh++;
                }
                else
                {
                    other++;
                }
            }

            sb.Append(", colliders ").Append(colliders.Length);
            if (colliders.Length > 0)
            {
                sb.Append(" (box ").Append(box).Append(" capsule ").Append(capsule);
                if (mesh > 0 || other > 0)
                {
                    sb.Append(" mesh ").Append(mesh).Append(" other ").Append(other);
                }

                sb.Append(')');
            }

            var materials = new HashSet<string>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] shared = renderers[i].sharedMaterials;
                for (int m = 0; m < shared.Length; m++)
                {
                    materials.Add(shared[m] != null ? shared[m].name : "null");
                }
            }

            var names = new List<string>(materials);
            names.Sort(StringComparer.Ordinal);
            sb.Append(", materials [").Append(string.Join(" ", names)).Append(']');
        }

        private static int Triangles(Renderer[] renderers)
        {
            int total = 0;
            if (renderers == null)
            {
                return 0;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                var filter = renderers[i].GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    total += (int)(mesh.GetIndexCount(s) / 3);
                }
            }

            return total;
        }

        // ----------------------------------------------------------------- library

        private static void DescribeLibrary(StringBuilder sb)
        {
            var library = AssetDatabase.LoadAssetAtPath<ArtFamilyLibrary>(ArtPipelinePaths.FamilyLibraryAsset);
            sb.Append("Family library (").Append(ArtPipelinePaths.FamilyLibraryAsset).Append("): ");
            if (library == null)
            {
                sb.Append("MISSING\n");
                return;
            }

            int items = library.items != null ? library.items.Count : 0;
            int resolved = 0;
            var unresolved = new List<string>();
            for (int i = 0; i < ArtFamilies.All.Length; i++)
            {
                if (library.Find(ArtFamilies.All[i]) != null)
                {
                    resolved++;
                }
                else
                {
                    unresolved.Add(ArtFamilies.All[i]);
                }
            }

            sb.Append(items).Append(" items, ").Append(resolved).Append('/').Append(ArtFamilies.All.Length)
              .Append(" families resolve");
            if (unresolved.Count > 0)
            {
                sb.Append(" (unresolved: ").Append(string.Join(" ", unresolved)).Append(')');
            }

            sb.Append('\n');
        }

        // ------------------------------------------------------------------ layout

        private static void DescribeLayout(StringBuilder sb)
        {
            var layout = AssetDatabase.LoadAssetAtPath<GoldenSliceLayoutAsset>(ArtPipelinePaths.LayoutAsset);
            sb.Append("Layout asset (").Append(ArtPipelinePaths.LayoutAsset).Append("): ");
            if (layout == null)
            {
                sb.Append("MISSING\n");
                return;
            }

            int entries = layout.entries != null ? layout.entries.Count : 0;
            sb.Append(entries).Append(" entries; instanceCountTotal ").Append(layout.instanceCountTotal)
              .Append(", familyInstanceCount ").Append(layout.familyInstanceCount)
              .Append(", nonFamilyUniqueCount ").Append(layout.nonFamilyUniqueCount)
              .Append("; glb sha ").Append(Short(layout.sourceGlbSha256))
              .Append(", json sha ").Append(Short(layout.sourceJsonSha256)).Append('\n');

            if (entries == 0)
            {
                return;
            }

            // Per-family instance counts: the quickest way to see that the 823 are the
            // same 823 the AP-001 manifest lists (and that courtyard_floor/ring_transition
            // are 0, as measured in brief B-3).
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int uniqueMeshes = 0;
            int occluders = 0;
            int purple = 0;
            long triangles = 0;
            for (int i = 0; i < layout.entries.Count; i++)
            {
                GoldenSliceLayoutAsset.Entry entry = layout.entries[i];
                string family = entry.family ?? "(null)";
                counts.TryGetValue(family, out int n);
                counts[family] = n + 1;

                if (entry.uniqueMesh)
                {
                    uniqueMeshes++;
                }

                if (entry.occluderCandidate)
                {
                    occluders++;
                }

                if (entry.material == "sakura_purple")
                {
                    purple++;
                }

                triangles += entry.triangles;
            }

            sb.Append("  uniqueMesh ").Append(uniqueMeshes).Append(", occluderCandidate ").Append(occluders)
              .Append(", sakura_purple ").Append(purple).Append(", source triangles ").Append(triangles).Append('\n');

            foreach (KeyValuePair<string, int> pair in counts)
            {
                sb.Append("  - ").Append(pair.Key).Append(": ").Append(pair.Value).Append('\n');
            }
        }

        private static string Short(string sha)
        {
            if (string.IsNullOrEmpty(sha))
            {
                return "-";
            }

            return sha.Length > 12 ? sha.Substring(0, 12) + "..." : sha;
        }

        // ------------------------------------------------------------------ scenes

        private static void DescribeScenes(StringBuilder sb)
        {
            sb.Append("Scenes:\n");
            DescribeScene(sb, "pilot", ArtPipelinePaths.PilotScene);
            DescribeScene(sb, "kit shelf", ArtPipelinePaths.KitShelfScene);
        }

        private static void DescribeScene(StringBuilder sb, string label, string path)
        {
            sb.Append("  - ").Append(label).Append(' ').Append(path).Append(": ");
            if (!File.Exists(path))
            {
                sb.Append("MISSING\n");
                return;
            }

            long bytes = new FileInfo(path).Length;
            sb.Append((bytes / 1024.0).ToString("0.0", Inv)).Append(" KB");

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            sb.Append(asset != null ? ", imported" : ", NOT imported");
            sb.Append('\n');
        }

        // ---------------------------------------------------------- build settings

        private static void DescribeBuildSettings(StringBuilder sb)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int pilotIndex = -1;
            int shelfIndex = -1;

            sb.Append("Build settings: ").Append(scenes.Length).Append(" scenes\n");
            for (int i = 0; i < scenes.Length; i++)
            {
                sb.Append("  ").Append(i).Append(' ').Append(scenes[i].enabled ? "[x] " : "[ ] ")
                  .Append(Path.GetFileNameWithoutExtension(scenes[i].path)).Append(" (").Append(scenes[i].path).Append(")\n");

                if (scenes[i].path == ArtPipelinePaths.PilotScene)
                {
                    pilotIndex = i;
                }

                if (scenes[i].path == ArtPipelinePaths.KitShelfScene)
                {
                    shelfIndex = i;
                }
            }

            sb.Append("  pilot scene: ").Append(pilotIndex >= 0 ? "registered at index " + pilotIndex : "NOT registered");
            if (pilotIndex >= 0 && !scenes[pilotIndex].enabled)
            {
                sb.Append(" (DISABLED)");
            }

            sb.Append('\n');
            sb.Append("  kit shelf: ").Append(shelfIndex >= 0 ? "registered at index " + shelfIndex + " (SHOULD NOT BE)" : "not registered (correct)")
              .Append('\n');
        }
    }
}
