using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-042B (AP-013): the classic weapon models as data — one entry per runtime weapon with
    /// the imported FBX root (LOD0–2 objects <c>AP013_&lt;key&gt;_LOD&lt;n&gt;</c>, the ADR-042-1
    /// sockets as children) and the shared 1K atlas material. Built by
    /// <c>ArtWeaponBuilder</c> into <c>Resources/DD_ClassicWeapons.asset</c>; the controller
    /// instantiates the entry on equip (not a hot path) and falls back to the AP-009C blockout
    /// (<c>ClassicWeaponDefinition.ModelResource</c>) for a weapon without an entry.
    /// </summary>
    public sealed class ClassicWeaponVisualLibrary : ScriptableObject
    {
        public const string Schema = "dd-classic-weapon-visuals-v1";
        public const string ResourcePath = "DD_ClassicWeapons";
        public const int LodCount = 3;

        [Serializable]
        public sealed class Entry
        {
            public ClassicWeaponId id;
            public string rosterKey;
            /// <summary>The imported FBX root (prefab asset). Instantiate, never mutate.</summary>
            public GameObject model;
            public int[] lodTriangles = new int[LodCount];

            /// <summary>
            /// WP-069 B4d: an AP-021 (P-07) model with its own materials, part_* pieces and COL_* hulls — instantiated
            /// through <see cref="AuthoredWeaponModel"/> (no atlas override, one LODGroup, hulls dropped).
            /// </summary>
            public bool ownMaterials;
            public string modelPackage = "";

            public bool HasModel => model != null;
        }

        public string schema = Schema;
        public string package = "AP-013";
        public Material material;
        public Entry[] entries = Array.Empty<Entry>();

        private static ClassicWeaponVisualLibrary s_current;
        private static bool s_looked;

        /// <summary>The library in Resources, loaded once (null when the package is not imported).</summary>
        public static ClassicWeaponVisualLibrary Current
        {
            get
            {
                if (!s_looked)
                {
                    s_looked = true;
                    s_current = Resources.Load<ClassicWeaponVisualLibrary>(ResourcePath);
                }

                return s_current;
            }
        }

        /// <summary>Tests: forget the cached lookup.</summary>
        public static void ResetLookup()
        {
            s_looked = false;
            s_current = null;
        }

        public Entry Find(ClassicWeaponId id)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].id == id) return entries[i];
            }

            return null;
        }

        /// <summary>
        /// Instantiate an entry's model for the rig: the atlas material on every renderer, the
        /// import-generated LODGroup re-thresholded for the raised camera (a weapon is a small
        /// screen object; Unity's import defaults would cull it at gameplay distance).
        /// </summary>
        public static GameObject Instantiate(Entry entry, Material material, float[] screenHeights)
        {
            if (entry == null || entry.model == null) return null;
            if (entry.ownMaterials)
            {
                return AuthoredWeaponModel.Instantiate(entry.model, null, screenHeights, Vector3.zero);
            }

            GameObject go = UnityEngine.Object.Instantiate(entry.model);
            go.name = "Model";
            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (material != null) renderers[i].sharedMaterial = material;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            var lodGroup = go.GetComponentInChildren<LODGroup>();
            if (lodGroup != null && screenHeights != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                for (int i = 0; i < lods.Length && i < screenHeights.Length; i++)
                {
                    lods[i].screenRelativeTransitionHeight = screenHeights[i];
                }

                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }

            return go;
        }
    }
}
