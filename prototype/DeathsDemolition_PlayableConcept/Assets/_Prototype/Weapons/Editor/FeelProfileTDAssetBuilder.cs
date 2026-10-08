using DD.Prototype.Feedback;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Weapons.EditorTools
{
    /// <summary>
    /// WP-095 (R3) · writes the top-down feel profile asset (Resources/FeelProfile_TD) from FeelProfileTD's code defaults
    /// (his sözleşmesi §3 / §4.0) — once; an existing asset is never overwritten (its values are the tuned ones from then on).
    /// Batch: -executeMethod DD.Prototype.Weapons.EditorTools.FeelProfileTDAssetBuilder.Build
    /// </summary>
    public static class FeelProfileTDAssetBuilder
    {
        public const string Folder = "Assets/_Prototype/Core/Resources";
        public const string AssetPath = Folder + "/" + FeelProfileTD.ResourceName + ".asset";

        [MenuItem("Deaths Demolition/Feel/Build FeelProfile_TD (code defaults, once)")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<FeelProfileTD>(AssetPath) != null)
            {
                Debug.Log("[FeelProfileTD] " + AssetPath + " exists — kept (tuned values are never overwritten)");
                return;
            }

            FeelProfileTD profile = ScriptableObject.CreateInstance<FeelProfileTD>();
            profile.ResetToDefaults();
            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FeelProfileTD] built " + AssetPath + " weapons=" + profile.weapons.Length + " events=" + profile.events.Length);
        }
    }
}
