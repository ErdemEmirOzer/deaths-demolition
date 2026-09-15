using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-012 · Generated index of one imported library (<c>&lt;AP&gt;_Library.asset</c>):
    /// every (family, variation) → prefab, in manifest order. The placement package
    /// (WP-014 / AP-005) reads this instead of searching folders; the asset gate verifies
    /// it against the manifest.
    /// </summary>
    public sealed class ArtLibraryAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Item
        {
            public string family;
            public string variation;
            public GameObject prefab;
        }

        public string ap;
        public List<Item> items = new List<Item>();

        public GameObject Find(string family, string variation)
        {
            if (items == null)
            {
                return null;
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item != null && string.Equals(item.family, family, StringComparison.Ordinal) &&
                    string.Equals(item.variation, variation, StringComparison.Ordinal))
                {
                    return item.prefab;
                }
            }

            return null;
        }

        /// <summary>All variations of a family, in manifest (V01, V02, …) order.</summary>
        public List<GameObject> Variations(string family)
        {
            var list = new List<GameObject>();
            if (items == null)
            {
                return list;
            }

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item != null && string.Equals(item.family, family, StringComparison.Ordinal) && item.prefab != null)
                {
                    list.Add(item.prefab);
                }
            }

            return list;
        }
    }
}
