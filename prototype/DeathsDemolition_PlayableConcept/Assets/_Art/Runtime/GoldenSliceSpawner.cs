using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · Places every family instance of the golden slice under one root.
    ///
    /// Runtime, not editor: the pilot scene file stays as empty as Run.unity (one
    /// bootstrap object), the 823 instances are produced from the layout asset on
    /// Awake, and the same code path runs in the Player the perf probe measures. An
    /// instance is a plain <c>Object.Instantiate</c> of the family prefab: LODGroup,
    /// renderers, colliders and layer come from the prefab, only the transform, the
    /// name and (for the purple trees) the material are set here.
    ///
    /// Deterministic: entries are walked in layout order and named after their source
    /// node, so two builds produce the same hierarchy and a screenshot diff means a
    /// content change, not an ordering one. Idempotent: an existing root of the same
    /// name under the parent is removed first.
    /// </summary>
    public static class GoldenSliceSpawner
    {
        public struct Stats
        {
            /// <summary>Instances actually created.</summary>
            public int Instances;

            /// <summary>Entries skipped because the library has no prefab for their family.</summary>
            public int Missing;

            public int Renderers;
            public int LodGroups;
            public int Colliders;

            /// <summary>Sum of triangles over every MeshFilter placed (all LOD levels, not just the visible one).</summary>
            public int Triangles;
        }

        public const string RootName = "[GoldenSlice]";

        /// <summary>Per-prefab counts, taken once per family instead of once per instance.</summary>
        private struct PrefabCounts
        {
            public int Renderers;
            public int LodGroups;
            public int Colliders;
            public int Triangles;
        }

        public static Stats Spawn(GoldenSliceLayoutAsset layout, ArtFamilyLibrary library, Transform parent,
            Material sakuraPurple)
        {
            var stats = new Stats();

            Clear(parent);

            var rootGo = new GameObject(RootName);
            Transform root = rootGo.transform;
            root.SetParent(parent, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            if (layout == null || layout.entries == null)
            {
                Debug.LogError("[Pilot] No layout asset: nothing to spawn.");
                return stats;
            }

            if (library == null)
            {
                Debug.LogError("[Pilot] No family library: every layout entry is missing its prefab.");
                stats.Missing = layout.entries.Count;
                return stats;
            }

            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var counts = new Dictionary<string, PrefabCounts>(StringComparer.Ordinal);
            var warned = new HashSet<string>(StringComparer.Ordinal);

            List<GoldenSliceLayoutAsset.Entry> entries = layout.entries;
            for (int i = 0; i < entries.Count; i++)
            {
                GoldenSliceLayoutAsset.Entry entry = entries[i];
                if (entry == null)
                {
                    stats.Missing++;
                    continue;
                }

                string family = entry.family ?? string.Empty;
                if (!prefabs.TryGetValue(family, out GameObject prefab))
                {
                    prefab = library.Find(family);
                    prefabs[family] = prefab;
                    if (prefab != null)
                    {
                        counts[family] = Count(prefab);
                    }
                }

                if (prefab == null)
                {
                    stats.Missing++;

                    // One line per family, not one per instance: a missing groundcover
                    // prefab would otherwise print 431 identical warnings.
                    if (warned.Add(family))
                    {
                        Debug.LogWarning("[Pilot] No prefab for family '" + family +
                                         "' in the library; its instances are skipped.");
                    }

                    continue;
                }

                GameObject go = UnityEngine.Object.Instantiate(prefab, root);
                go.name = entry.name;

                Transform t = go.transform;
                t.localPosition = entry.position;
                t.localRotation = entry.rotation;
                t.localScale = entry.scale;

                if (sakuraPurple != null &&
                    string.Equals(entry.material, ArtFamilies.SakuraPurpleRole, StringComparison.Ordinal))
                {
                    ApplyMaterial(go, sakuraPurple);
                }

                PrefabCounts c = counts[family];
                stats.Instances++;
                stats.Renderers += c.Renderers;
                stats.LodGroups += c.LodGroups;
                stats.Colliders += c.Colliders;
                stats.Triangles += c.Triangles;
            }

            return stats;
        }

        /// <summary>Removes any earlier slice root under the parent so a rebuild never stacks two.</summary>
        public static void Clear(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (!string.Equals(child.name, RootName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    // Destroy is deferred to the end of the frame; deactivating first keeps
                    // the old instances out of every FindObjectOfType that runs in between.
                    child.SetActive(false);
                    UnityEngine.Object.Destroy(child);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child);
                }
            }
        }

        /// <summary>
        /// The purple trees share the pink family prefab and differ only in material,
        /// so the swap happens per instance on every LOD renderer. sharedMaterial, not
        /// material: an instantiated material per tree would break the SRP batch and
        /// leak on scene unload.
        /// </summary>
        private static void ApplyMaterial(GameObject instance, Material material)
        {
            MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }
        }

        private static PrefabCounts Count(GameObject prefab)
        {
            var c = new PrefabCounts
            {
                Renderers = prefab.GetComponentsInChildren<MeshRenderer>(true).Length,
                LodGroups = prefab.GetComponentsInChildren<LODGroup>(true).Length,
                Colliders = prefab.GetComponentsInChildren<Collider>(true).Length
            };

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null && mesh.subMeshCount > 0)
                {
                    // GetIndexCount, not .triangles: the latter copies the index buffer
                    // into a managed array and needs a readable mesh, which these are not.
                    c.Triangles += (int)(mesh.GetIndexCount(0) / 3);
                }
            }

            return c;
        }
    }
}
