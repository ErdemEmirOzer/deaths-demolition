using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · Family name → prefab, serialized. The spawner and the scene builders
    /// resolve prefabs through this asset rather than by path so the Player never
    /// depends on <c>Resources</c> or on a folder layout, and so the set of prefabs a
    /// scene can spawn is one reviewable list of eighteen references. Rebuilt by
    /// <c>ArtFamilyPrefabBuilder.BuildLibrary</c> after every prefab build.
    /// </summary>
    public sealed class ArtFamilyLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Item
        {
            public string family;
            public GameObject prefab;
        }

        public List<Item> items = new List<Item>();

        /// <summary>The prefab for a family, or null when the family is unknown or its prefab is missing.</summary>
        public GameObject Find(string family)
        {
            if (items == null || family == null)
            {
                return null;
            }

            // Eighteen entries: a linear scan is cheaper than keeping a dictionary in
            // sync with a serialized list the editor rewrites.
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item != null && string.Equals(item.family, family, StringComparison.Ordinal))
                {
                    return item.prefab;
                }
            }

            return null;
        }
    }
}
