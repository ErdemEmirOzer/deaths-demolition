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
        /// <summary>The three libraries this package imports, in production order.</summary>
        public static readonly string[] Packages = { "AP-002", "AP-003", "AP-003B" };

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
