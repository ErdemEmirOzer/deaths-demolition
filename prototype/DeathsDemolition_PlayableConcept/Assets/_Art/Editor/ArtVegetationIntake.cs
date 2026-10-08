using System.Collections.Generic;
using System.IO;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-093 · AP-031 v001 intake (P-05 delivery 08.10-1, verified against its SHA256SUMS on copy): 19 FBX under Meshes/,
    /// 18 PNG under Textures/, the delivery manifest and the two atlas sidecars beside them, M_VEG_Grass / M_VEG_Flora under
    /// Materials/, the binding Resources/DD_VegetationKit.asset. The manifest is the table the asset gates read
    /// (ArtAssetGateTests FBX / PNG sets): every file it lists, at its intake path. Import rules: ArtImportContract
    /// (IsVegetationAsset).
    /// </summary>
    public static class ArtVegetationIntake
    {
        public const string Root = ArtImportContract.VegetationRoot + "/AP-031/v001";
        public const string MeshFolder = Root + "/Meshes";
        public const string TextureFolder = Root + "/Textures";
        public const string MaterialFolder = Root + "/Materials";
        public const string ManifestPath = Root + "/AP031-manifest-v001.json";
        public const string FloraAtlasJson = Root + "/AP031-atlas-flora-v001.json";
        public const string KitAsset = ArtImportContract.ArtRoot + "/Resources/DD_VegetationKit.asset";
        public const int ExpectedFbx = 19, ExpectedTextures = 18;

        public static bool Active => File.Exists(ManifestPath);

        private static Dictionary<string, object> Manifest()
        {
            return ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ManifestPath)), "AP031 manifest");
        }

        /// <summary>Intake paths of the manifest's FBX (exports/v001/fbx/X.fbx → Meshes/X.fbx).</summary>
        public static List<string> ModelFiles()
        {
            var list = new List<string>(ExpectedFbx);
            if (!Active) return list;
            foreach (object o in ArtJson.AsList(Manifest()["fbx"], "fbx"))
            {
                string file = ArtJson.AsString(ArtJson.AsObject(o, "fbx")["file"]);
                list.Add(MeshFolder + "/" + Path.GetFileName(file));
            }

            return list;
        }

        /// <summary>Intake paths of the manifest's textures (exports/v001/textures/X.png → Textures/X.png).</summary>
        public static List<string> TextureFiles()
        {
            var list = new List<string>(ExpectedTextures);
            if (!Active) return list;
            Dictionary<string, object> sets = ArtJson.AsObject(Manifest()["sets"], "sets");
            foreach (object o in ArtJson.AsList(ArtJson.AsObject(sets["textures"], "textures")["files"], "files"))
            {
                string file = ArtJson.AsString(ArtJson.AsObject(o, "texture")["file"]);
                list.Add(TextureFolder + "/" + Path.GetFileName(file));
            }

            return list;
        }

        /// <summary>The manifest's per-object record (tris, vc1 height) for a FBX family and LOD, or null.</summary>
        public static Dictionary<string, object> ObjectRecord(string family, int lod)
        {
            if (!Active) return null;
            foreach (object o in ArtJson.AsList(Manifest()["fbx"], "fbx"))
            {
                Dictionary<string, object> f = ArtJson.AsObject(o, "fbx");
                if (ArtJson.AsString(f["family"]) != family) continue;
                foreach (object ob in ArtJson.AsList(f["objects"], "objects"))
                {
                    Dictionary<string, object> r = ArtJson.AsObject(ob, "object");
                    if (ArtJson.AsString(r["name"]) == family + "_LOD" + lod) return r;
                }
            }

            return null;
        }
    }
}
