namespace DD.Art
{
    /// <summary>
    /// WP-012 · Asset paths of the imported production libraries (ADR-012-2). The
    /// AP-001 golden-slice families stay where WP-011 put them
    /// (<see cref="ArtPipelinePaths.FamiliesRoot"/>/&lt;family&gt;); every later library
    /// lives one level down under its package id so two packages that ship the same
    /// family name (AP-002 re-exports the twelve AP-001 architecture families) never
    /// collide:
    ///
    ///   Assets/_Art/Environment/Families/&lt;AP&gt;/&lt;AP&gt;_Library.json     manifest (generated)
    ///   Assets/_Art/Environment/Families/&lt;AP&gt;/&lt;AP&gt;_Library.asset    prefab index (generated)
    ///   Assets/_Art/Environment/Families/&lt;AP&gt;/&lt;family&gt;/*.fbx          contract FBX (embed off)
    ///   Assets/_Art/Environment/Families/&lt;AP&gt;/&lt;family&gt;/SM_&lt;family&gt;_V&lt;nn&gt;.prefab
    ///
    /// Textures and materials share the WP-011 folders and naming
    /// (<c>T_&lt;role&gt;_&lt;Map&gt;.png</c>, <c>M_&lt;role&gt;.mat</c>); the roles are listed in
    /// <see cref="ArtLibraryMaterialRoles"/>.
    /// </summary>
    public static class ArtLibraryPaths
    {
        /// <summary>The imported libraries, in production order (WP-012: AP-002/AP-003/AP-003B; WP-014: AP-005 modules).</summary>
        public static readonly string[] Packages = { "AP-002", "AP-003", "AP-003B", "AP-005", "AP-005B" };

        // ------------------------------------------------------------ WP-014 world assembly

        public const string WorldRoot = ArtPipelinePaths.ArtRoot + "/Environment/World";
        /// <summary>The AP-005 placement source (dd-placement-v1), byte-identical copy of exports/placement.json.</summary>
        public const string Ap005PlacementJson = WorldRoot + "/AP005_placement.json";
        /// <summary>WP-015 · Unity-side per-instance overrides (dd-placement-overrides-v1) applied on top of the AP-005 source at import; the source file is never edited.</summary>
        public const string Ap005PlacementOverridesJson = WorldRoot + "/AP005_placement-overrides.json";
        /// <summary>WP-017 · AP-005B delta (dd-placement-delta-v1) applied on top of the v001 source at import.</summary>
        public const string Ap005PlacementDeltaJson = WorldRoot + "/AP005_placement-delta-v002.json";
        /// <summary>The imported placement (Unity space) the importer and the world bootstrap read.</summary>
        public const string Ap005PlacementAsset = WorldRoot + "/AP005_Placement.asset";
        /// <summary>The stand-in soil ground under the whole 90 m map (AP-005 ships only the NE golden-slice ground plate); holes at the koi basins.</summary>
        public const string Ap005GroundMesh = WorldRoot + "/AP005_GroundStandIn.asset";
        public const string Ap005WorldScene = ArtPipelinePaths.ArtRoot + "/Scenes/World_AP005.unity";
        public const string Ap005WorldSceneName = "World_AP005";
        public const string Ap005NavMeshData = ArtPipelinePaths.ArtRoot + "/Scenes/World_AP005/NavMesh-World_AP005.asset";
        public const string WorldSourceAsset = ArtPipelinePaths.ArtRoot + "/Resources/DD_WorldSource.asset";

        public const string ShowcaseScene = ArtPipelinePaths.ArtRoot + "/Scenes/AP_LibraryShowcase.unity";
        public const string ShowcaseSceneName = "AP_LibraryShowcase";

        public const string LightingFolder = ArtPipelinePaths.ArtRoot + "/Lighting";
        public const string OvercastVolumeProfile = LightingFolder + "/DD_Overcast_v1.asset";

        /// <summary>
        /// The shared overcast sky material. Lives under Resources so the Run scene's
        /// atmosphere code (DD.Prototype, which must not reference DD.Art) can pick it up
        /// by name at runtime; the pilot and the showcase reference the asset directly.
        /// </summary>
        public const string SkyResourceName = "DD_Sky_Overcast_v1";
        public const string SkyMaterial = ArtPipelinePaths.ArtRoot + "/Resources/" + SkyResourceName + ".mat";

        public static string LibraryRoot(string ap)
        {
            return ArtPipelinePaths.FamiliesRoot + "/" + ap;
        }

        /// <summary><c>AP002_Library.json</c> (package id without the dash).</summary>
        public static string ManifestJson(string ap)
        {
            return LibraryRoot(ap) + "/" + Tag(ap) + "_Library.json";
        }

        public static string IndexAsset(string ap)
        {
            return LibraryRoot(ap) + "/" + Tag(ap) + "_Library.asset";
        }

        public static string FamilyFolder(string ap, string family)
        {
            return LibraryRoot(ap) + "/" + family;
        }

        public static string Fbx(string ap, string family, string fileName)
        {
            return FamilyFolder(ap, family) + "/" + fileName;
        }

        public static string Prefab(string ap, string family, string prefabName)
        {
            return FamilyFolder(ap, family) + "/" + prefabName + ".prefab";
        }

        public static string Tag(string ap)
        {
            return ap.Replace("-", string.Empty);
        }
    }
}
