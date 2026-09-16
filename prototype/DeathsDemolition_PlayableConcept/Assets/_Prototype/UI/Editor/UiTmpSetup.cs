using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// WP-022B: imports the TextMesh Pro Essential Resources (TMP Settings, the OFL
    /// LiberationSans SDF font, shaders) into <c>Assets/TextMesh Pro/</c> - the step the
    /// WP-005 UI deliberately skipped (UiFonts note). Idempotent: skipped when the
    /// settings asset already exists. Batchmode:
    /// <c>-executeMethod DD.Prototype.UI.EditorTools.UiTmpSetup.ImportEssentials</c>.
    /// </summary>
    public static class UiTmpSetup
    {
        public const string PackagePath = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
        public const string SettingsAssetPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        [MenuItem("DD/UI/Import TextMesh Pro Essentials")]
        public static void ImportEssentials()
        {
            if (File.Exists(SettingsAssetPath))
            {
                Debug.Log("[UI] TMP essentials already present: " + SettingsAssetPath);
                return;
            }

            string full = Path.GetFullPath(PackagePath);
            if (!File.Exists(full))
            {
                Debug.LogError("[UI] TMP essentials package not found: " + full);
                return;
            }

            AssetDatabase.ImportPackage(full, false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[UI] TMP essentials imported: settings=" + File.Exists(SettingsAssetPath));
        }
    }
}
