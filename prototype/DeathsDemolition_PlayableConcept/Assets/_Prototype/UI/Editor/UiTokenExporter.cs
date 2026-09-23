using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// WP-068 token v3: writes the derived token artefacts from UiTokens.cs. Menu item for a
    /// person, <see cref="Export"/> for batchmode:
    /// <c>-executeMethod DD.Prototype.UI.EditorTools.UiTokenExporter.Export</c>.
    /// Run it after any token change; the EditMode freshness test fails until you do.
    /// </summary>
    public static class UiTokenExporter
    {
        [MenuItem("DD/UI/Export Tokens")]
        public static void Export()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            Write(Path.Combine(project, UiTokenExport.HlslPath), UiTokenExport.BuildHlsl());
            Write(Path.Combine(project, UiTokenExport.JsonPath), UiTokenExport.BuildJson());

            string guidePath = Path.Combine(project, UiTokenExport.StyleGuidePath);
            string guide = File.ReadAllText(guidePath);
            Write(guidePath, UiTokenExport.MergeIntoStyleGuide(guide));

            AssetDatabase.Refresh();
            Debug.Log("[UI] tokens exported: " + UiTokenExport.HlslPath + ", " + UiTokenExport.JsonPath + ", " + UiTokenExport.StyleGuidePath + " §2.2 UI table");
        }

        private static void Write(string path, string text)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // LF, no BOM: the freshness test compares text exactly.
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
        }
    }
}
