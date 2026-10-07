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
    /// <summary>WP-068 token v3 (direction §2.2): three OFL faces by role.</summary>
    public enum UiFontRole
    {
        /// <summary>Shippori Mincho B1 700 - Display / Banner / H1 / H2 only (≥ 24 px). The slot stays EMPTY until the user picks the face (U1, AD MUST 6); labels then use the Text face.</summary>
        Display = 0,

        /// <summary>Source Sans 3 400/600 - H3 → Caption.</summary>
        Text = 1,

        /// <summary>Oswald 500/600 with equalised digit advances - the numeric types.</summary>
        Numeric = 2
    }

    public static class UiFonts
    {
        /// <summary>WP-068 slice 3: role font assets (static SDF, Resources/UI/Fonts/). Absent → the chain falls back.</summary>
        public const string DisplayFontResource = "UI/Fonts/Display SDF";
        public const string TextFontResource = "UI/Fonts/Text SDF";
        public const string NumericFontResource = "UI/Fonts/Numeric SDF";

        private static readonly TMP_FontAsset[] s_roles = new TMP_FontAsset[3];
        private static bool s_rolesResolved;

        public static UiFontRole RoleOf(UiType type)
        {
            switch (type)
            {
                case UiType.Display:
                case UiType.Banner:
                case UiType.H1:
                case UiType.H2:
                    return UiFontRole.Display;
                case UiType.NumericXL:
                case UiType.NumericL:
                case UiType.NumericM:
                case UiType.NumericS:
                case UiType.NumericHero:   // WP-098A: the C21 hero number
                    return UiFontRole.Numeric;
                default:
                    return UiFontRole.Text;
            }
        }

        /// <summary>The asset placed for a role, or null (not placed yet).</summary>
        public static TMP_FontAsset Role(UiFontRole role)
        {
            if (!s_rolesResolved)
            {
                s_rolesResolved = true;
                s_roles[(int)UiFontRole.Display] = Resources.Load<TMP_FontAsset>(DisplayFontResource);
                s_roles[(int)UiFontRole.Text] = Resources.Load<TMP_FontAsset>(TextFontResource);
                s_roles[(int)UiFontRole.Numeric] = Resources.Load<TMP_FontAsset>(NumericFontResource);
            }

            return s_roles[(int)role];
        }

        /// <summary>
        /// The face a label of this type draws with: its role's asset → the Text face →
        /// <see cref="Primary"/> (today's face). With no v3 assets placed this is exactly
        /// the WP-022B behaviour.
        /// </summary>
        public static TMP_FontAsset For(UiType type)
        {
            // DEC-109 (user 30.09, M-64 closed): Turkish headings draw in the Display face too - its DD-TR derivative has
            // Ğ İ Ş, so the Codex 19cb757a switch to the Text SDF is gone. Lower case in a heading still falls back to Text,
            // as it does in English.
            UiFontRole role = RoleOf(type);
            TMP_FontAsset font = Role(role);
            if (font == null)
            {
                font = Role(UiFontRole.Text);
            }

            return font != null ? font : Primary;
        }

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
            s_rolesResolved = false;
            s_roles[0] = null;
            s_roles[1] = null;
            s_roles[2] = null;
        }
    }
}
