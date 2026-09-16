using TMPro;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Font resolution for the prototype UI (WP-022B: TextMesh Pro).
    ///
    /// WP-005 built the UI on uGUI Text because the TMP Essential Resources had never
    /// been imported. WP-022B imported them (<c>Assets/TextMesh Pro/</c>, via
    /// <c>UiTmpSetup</c>), so every label is a TextMeshProUGUI on one font asset:
    ///
    ///   1. <c>Resources/UI/Fonts/UiPrimary SDF</c> - the project face, when art places one
    ///      under <c>Assets/_Prototype/UI/Art/Resources/UI/Fonts/</c> (OFL licence file
    ///      beside it; the display face is an open user decision, see planning/04);
    ///   2. TMP's default font asset (LiberationSans SDF, OFL, from the essentials);
    ///   3. null - TMP then falls back to its own default and warns once.
    ///
    /// Every text object is created through <see cref="UiFactory"/>, so a face change
    /// is a change to this one lookup, not to twenty screens.
    /// </summary>
    public static class UiFonts
    {
        /// <summary>Resources path (without extension) of the project face, if any.</summary>
        public const string ProjectFontResource = "UI/Fonts/UiPrimary SDF";

        private static TMP_FontAsset s_font;
        private static bool s_resolved;

        /// <summary>The one typeface in the prototype UI. Null only when TMP has no default at all.</summary>
        public static TMP_FontAsset Primary
        {
            get
            {
                if (s_resolved)
                {
                    return s_font;
                }

                s_resolved = true;
                s_font = Resources.Load<TMP_FontAsset>(ProjectFontResource);
                if (s_font == null)
                {
                    s_font = TMP_Settings.defaultFontAsset;
                }

                if (s_font == null)
                {
                    Debug.LogWarning(
                        "[UI] No TMP font asset found (project face or TMP default). Text will use TMP's own fallback. " +
                        "Import the TMP essentials (DD/UI/Import TextMesh Pro Essentials).");
                }

                return s_font;
            }
        }

        /// <summary>True when the UI draws with a project-placed face rather than the TMP default.</summary>
        public static bool UsesProjectFace
        {
            get { return Primary != null && Primary.name.StartsWith("UiPrimary", System.StringComparison.Ordinal); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_font = null;
            s_resolved = false;
        }
    }
}
