using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-075 · Look v3 imports. C2: the cloud cookie — AP-023 sky_v2 <c>T_cloud_cookie_1024.png</c> v002
    /// (sha256 5c635f88…, K7-clean: no sun or shadow patch under 14 m), copied from the AP export to
    /// <see cref="CloudCookiePath"/> (the source lives outside the repo); its importer rules are
    /// <see cref="ArtImportContract.ApplyLightCookie"/> (applied by the art postprocessor on every import).
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.LookV3Builder.ImportCloudCookieCli</c>.
    /// </summary>
    public static class LookV3Builder
    {
        /// <summary>Resources path of <see cref="DD.Prototype.World.WorldAtmosphere.CloudShadow.CookieResourceName"/>.</summary>
        public const string CloudCookiePath = ArtImportContract.CloudCookiePath;

        public static void ImportCloudCookieCli()
        {
            Debug.Log("[LookV3] " + ConfigureCloudCookie());
        }

        public static string ConfigureCloudCookie()
        {
            if (!(AssetImporter.GetAtPath(CloudCookiePath) is TextureImporter)) throw new FileNotFoundException("cloud cookie not imported", CloudCookiePath);
            AssetDatabase.ImportAsset(CloudCookiePath, ImportAssetOptions.ForceUpdate);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CloudCookiePath);
            return CloudCookiePath + " " + tex.width + "x" + tex.height + " " + tex.graphicsFormat + " mips " + tex.mipmapCount + " wrap " + tex.wrapMode;
        }
    }
}
