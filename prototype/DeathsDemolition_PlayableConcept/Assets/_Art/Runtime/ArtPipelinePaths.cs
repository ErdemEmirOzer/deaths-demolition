namespace DD.Art
{
    /// <summary>
    /// WP-011 · Every asset path the art pipeline reads or writes, in one place
    /// (ADR-011-2 folder layout). Builders, the bootstrap, the report and the asset
    /// gate all go through these so a renamed folder is a one-line change and a typo
    /// cannot put a prefab and its library entry in two different places.
    ///
    /// All paths are Unity asset paths (forward slashes, rooted at the project folder);
    /// they are valid for <c>AssetDatabase</c> and, in the Editor, for <c>System.IO</c>
    /// because the Editor's working directory is the project root.
    /// </summary>
    public static class ArtPipelinePaths
    {
        public const string ArtRoot = "Assets/_Art";
        public const string FamiliesRoot = ArtRoot + "/Environment/Families";
        public const string GoldenSliceRoot = ArtRoot + "/Environment/GoldenSlice";
        public const string LayoutJson = GoldenSliceRoot + "/AP001_GoldenSliceLayout.json";
        public const string LayoutAsset = GoldenSliceRoot + "/AP001_GoldenSliceLayout.asset";
        public const string GroundColliderFbx = GoldenSliceRoot + "/GoldenSlice_Ground_COLLIDER.fbx";
        public const string FamilyLibraryAsset = FamiliesRoot + "/ArtFamilyLibrary.asset";
        public const string TexturesSource = ArtRoot + "/Textures/Source";
        public const string TexturesDerived = ArtRoot + "/Textures/Derived";
        public const string Materials = ArtRoot + "/Materials";
        public const string PilotScene = ArtRoot + "/Scenes/Pilot/AP001_GoldenSlice.unity";
        public const string KitShelfScene = ArtRoot + "/Scenes/Pilot/AP001_KitShelf.unity";
        public const string PilotSceneName = "AP001_GoldenSlice";

        /// <summary>Suffix of the derived URP mask (R metallic, A smoothness).</summary>
        public const string MaskMapName = "MetallicSmoothness";

        public static string FamilyFolder(string family)
        {
            return FamiliesRoot + "/" + family;
        }

        /// <summary><c>SM_&lt;family&gt;.prefab</c> next to the family's FBX files.</summary>
        public static string FamilyPrefab(string family)
        {
            return FamilyFolder(family) + "/SM_" + family + ".prefab";
        }

        /// <summary><c>&lt;family&gt;_LOD&lt;n&gt;.fbx</c> (the AP export label is kept verbatim).</summary>
        public static string FamilyLod(string family, int lod)
        {
            return FamilyFolder(family) + "/" + family + "_LOD" + lod + ".fbx";
        }

        /// <summary><c>&lt;family&gt;_COLLIDERS.fbx</c>; only families with <see cref="ArtFamilies.HasCollider"/> ship one.</summary>
        public static string FamilyColliders(string family)
        {
            return FamilyFolder(family) + "/" + family + "_COLLIDERS.fbx";
        }

        /// <summary><c>M_&lt;role&gt;.mat</c>.</summary>
        public static string Material(string role)
        {
            return Materials + "/M_" + role + ".mat";
        }

        /// <summary><c>T_&lt;role&gt;_&lt;map&gt;.png</c> in the byte-identical AP-001 source set (BaseColor, Normal, Packed).</summary>
        public static string SourceTexture(string role, string map)
        {
            return TexturesSource + "/T_" + role + "_" + map + ".png";
        }

        /// <summary><c>T_&lt;role&gt;_MetallicSmoothness.png</c>, generated from the packed map by the repack tool.</summary>
        public static string DerivedMask(string role)
        {
            return TexturesDerived + "/T_" + role + "_" + MaskMapName + ".png";
        }

        /// <summary>WP-017 · A derived (generated) texture of any map name, e.g. the roof tile-course detail normal.</summary>
        public static string DerivedTexture(string role, string map)
        {
            return TexturesDerived + "/T_" + role + "_" + map + ".png";
        }

        public static string DerivedTextureProvenance(string role, string map)
        {
            return TexturesDerived + "/T_" + role + "_" + map + ".provenance.json";
        }

        /// <summary>The provenance sidecar written next to <see cref="DerivedMask"/> (source hash, algorithm).</summary>
        public static string DerivedMaskProvenance(string role)
        {
            return TexturesDerived + "/T_" + role + "_" + MaskMapName + ".provenance.json";
        }
    }
}
