using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// Creates or refreshes the one <see cref="UiTheme"/> asset from the AP-012 atlases in
    /// Resources (WP-022). Menu item for a person, <see cref="Build"/> for batchmode:
    /// <c>-executeMethod DD.Prototype.UI.EditorTools.UiThemeBuilder.Build</c>.
    /// </summary>
    public static class UiThemeBuilder
    {
        public const string AssetPath = "Assets/_Prototype/UI/Art/Resources/UI/UiTheme.asset";

        [MenuItem("DD/UI/Build UI Theme From AP-012 Atlases")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(UiArtImporter.ArtRoot.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>(AssetPath);
            bool created = theme == null;
            if (created)
            {
                theme = ScriptableObject.CreateInstance<UiTheme>();
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                AssetDatabase.CreateAsset(theme, AssetPath);
            }

            theme.FillFromResources();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();

            Debug.Log("[UI] UiTheme " + (created ? "created" : "updated") + ": icons=" + theme.Icons.Count +
                      " frames=" + theme.Frames.Count + " skins=" + theme.SkinPreviews.Count +
                      " plate=" + (theme.MenuPlate != null) + " marker=" + (theme.HudMarker != null));
        }
    }
}
