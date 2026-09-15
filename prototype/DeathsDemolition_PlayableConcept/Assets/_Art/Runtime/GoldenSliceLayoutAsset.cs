using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · The AP-001 golden slice placement, already converted to Unity contract
    /// space (x east, y up, z north; see <c>ArtAxis</c> in the editor assembly). One
    /// entry per family instance in the source layout JSON; the 52 non-family unique
    /// meshes are counted but not placed (they are a production hand-off, brief B-3).
    ///
    /// WHY A SCRIPTABLEOBJECT AND NOT THE JSON AT RUNTIME
    /// -------------------------------------------------
    /// The JSON is provenance: nested lists that <c>JsonUtility</c> cannot read and
    /// coordinates in glTF space. Parsing it in a Player would cost a custom parser in
    /// the build plus an axis conversion at every start, and would leave the Player
    /// without a single serialized artefact the asset gate can diff against the JSON.
    /// The editor builder does the conversion once; the EditMode parity test proves the
    /// asset and the JSON agree (823/823); the Player just reads plain fields.
    ///
    /// The two hashes tie this asset to the exact GLB and JSON it was produced from, so
    /// a regenerated export cannot be spawned with a stale layout unnoticed.
    /// </summary>
    public sealed class GoldenSliceLayoutAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            /// <summary>Source node name, e.g. <c>AP001_PalaceWall_0.008_Export</c>.</summary>
            public string name;

            /// <summary>One of <see cref="ArtFamilies.All"/>.</summary>
            public string family;

            /// <summary>Contract-space transform, ready for <c>Transform.localPosition</c> under an identity root.</summary>
            public Vector3 position;

            public Quaternion rotation;

            /// <summary>
            /// Instance scale. For <see cref="uniqueMesh"/> entries this is the scale that
            /// was baked into the source mesh, recovered from its bounds, so the canonical
            /// family prefab lands on the same AABB (brief B-4).
            /// </summary>
            public Vector3 scale;

            /// <summary>Material role from the source material name (<c>AP001_M_&lt;role&gt;_v009</c> → <c>role</c>).</summary>
            public string material;

            public bool occluderCandidate;

            /// <summary>Which LOD the source scene had placed (informational; the prefab's LODGroup decides at runtime).</summary>
            public int lodInSource;

            /// <summary>True when the source instance carried its own mesh instead of the canonical family mesh.</summary>
            public bool uniqueMesh;

            /// <summary>World AABB of the source instance in contract space; the parity test compares against it.</summary>
            public Vector3 aabbMin;

            public Vector3 aabbMax;

            /// <summary>Triangle count of the source instance's mesh (the LOD it was placed with).</summary>
            public int triangles;
        }

        /// <summary>SHA-256 of the GoldenSlice.glb the layout JSON was extracted from.</summary>
        public string sourceGlbSha256;

        /// <summary>SHA-256 of the layout JSON file this asset was built from.</summary>
        public string sourceJsonSha256;

        /// <summary>Every node in the source layout, family instances plus unique meshes.</summary>
        public int instanceCountTotal;

        /// <summary>Family instances, i.e. the expected <see cref="entries"/> count.</summary>
        public int familyInstanceCount;

        /// <summary>Unique meshes outside the 18 families (not placed by the pilot).</summary>
        public int nonFamilyUniqueCount;

        public List<Entry> entries = new List<Entry>();
    }
}
