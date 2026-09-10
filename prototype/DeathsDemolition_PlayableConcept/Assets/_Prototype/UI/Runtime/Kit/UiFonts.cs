using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Font resolution for the prototype UI.
    ///
    /// WHY uGUI Text AND NOT TextMeshPro
    /// ---------------------------------
    /// The TextMeshPro package is in the manifest, but its "Essential Resources" have
    /// never been imported into this project - there is no Assets/TextMesh Pro folder and
    /// therefore no TMP Settings asset and no default font asset. The first TMP_Text that
    /// wakes up in that state logs to the console and, in a build, renders nothing.
    /// Acceptance criterion 27 forbids console errors during normal use, and importing
    /// the essentials would drop ~30 assets into the project root, which is outside this
    /// task's file ownership.
    ///
    /// So the whole UI is built on UnityEngine.UI.Text against Unity's built-in font,
    /// which is guaranteed present, needs no import step and works offline. Every text
    /// object is created through <see cref="UiFactory"/>, so switching the module to TMP
    /// later is a change to one factory method, not to twenty screens.
    /// </summary>
    public static class UiFonts
    {
        private static Font s_font;
        private static bool s_warned;

        /// <summary>
        /// The one typeface in the prototype UI. Never null: falls back to an OS font, and
        /// warns exactly once if even that fails, rather than letting a null Font reach a
        /// Text component.
        /// </summary>
        public static Font Primary
        {
            get
            {
                if (s_font != null)
                {
                    return s_font;
                }

                // Unity 2022+ renamed the built-in font resource. Only this name is asked
                // for: requesting the old "Arial.ttf" would itself log an error.
                s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                if (s_font == null)
                {
                    s_font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Helvetica Neue", "Helvetica", "Arial", "Segoe UI", "Liberation Sans" },
                        UiTokens.FontSize(UiType.Body));
                }

                if (s_font == null && !s_warned)
                {
                    s_warned = true;
                    Debug.LogWarning(
                        "[UI] No usable font found. Text will not render. " +
                        "This is a prototype environment problem, not a gameplay failure.");
                }

                return s_font;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_font = null;
            s_warned = false;
        }
    }
}
