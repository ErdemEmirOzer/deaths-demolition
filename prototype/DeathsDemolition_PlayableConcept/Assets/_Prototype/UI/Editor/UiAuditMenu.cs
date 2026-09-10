using System.Text;
using DD.Prototype.Profile;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// Editor entry points for the two things about this UI that are claims rather than
    /// opinions: its contrast ratios and its geometry constants.
    ///
    /// Both reports are generated from the same code the screens are built from, so
    /// neither can drift away from what is on screen. A palette edit that breaks a
    /// threshold is caught by running the audit, not by somebody noticing on a
    /// screenshot two weeks later.
    /// </summary>
    public static class UiAuditMenu
    {
        private const string MenuRoot = "Tools/Deaths Demolition/UI/";

        [MenuItem(MenuRoot + "Audit Accessibility", false, 200)]
        public static void AuditAccessibility()
        {
            string report = UiContrast.BuildReport();
            int failures = UiContrast.CountFailures();

            if (failures > 0)
            {
                Debug.LogWarning(report);
            }
            else
            {
                Debug.Log(report);
            }
        }

        [MenuItem(MenuRoot + "Print Layout Constants", false, 201)]
        public static void PrintLayoutConstants()
        {
            var sb = new StringBuilder(1024);
            sb.AppendLine("UI layout constants - the geometry half of the accessibility contract");
            sb.AppendLine("---------------------------------------------------------------------");
            sb.AppendLine("Reference resolution      " + UiTokens.ReferenceResolution.x + " x " +
                          UiTokens.ReferenceResolution.y + " (canvas scaler matches on HEIGHT)");
            sb.AppendLine("Minimum touch target      " + UiTokens.TargetMin + " px   (WCAG 2.2 AA floor)");
            sb.AppendLine("Standard button height    " + UiTokens.TargetPreferred + " px");
            sb.AppendLine("Compact square control    " + UiTokens.TargetCompact + " px");
            sb.AppendLine("Minimum gap between two targets  " + (int)UiTokens.TargetGap + " px");
            sb.AppendLine("Focus ring / halo         " + UiTokens.StrokeFocusRing + " px + " +
                          UiTokens.StrokeFocusHalo + " px, drawn OUTSIDE the control rect");
            sb.AppendLine("Suspended-weapon opacity  " + UiTokens.OpacitySuspended +
                          " (REQ-057: dimmed, never hidden)");
            sb.AppendLine("Maximum paragraph measure " + UiTokens.MaxMeasure + " px");
            sb.AppendLine();
            sb.AppendLine("Spacing scale (8pt base, one 4pt half-step):");

            foreach (UiSpace space in System.Enum.GetValues(typeof(UiSpace)))
            {
                sb.AppendLine("  " + space.ToString().PadRight(10) + (int)space + " px");
            }

            sb.AppendLine();
            sb.AppendLine("Type scale:");

            foreach (UiType type in System.Enum.GetValues(typeof(UiType)))
            {
                sb.AppendLine("  " + type.ToString().PadRight(12) +
                              UiTokens.FontSize(type) + " px / " +
                              UiTokens.Weight(type) + " / line " + UiFactory.LineHeight(type) + " px");
            }

            Debug.Log(sb.ToString());
        }

        [MenuItem(MenuRoot + "Print Profile Save", false, 220)]
        public static void PrintProfileSave()
        {
            ProfileData data = ProfileService.Current;

            Debug.Log(
                "[Profile] " + ProfileStore.Path + "\n" +
                JsonUtility.ToJson(data, true) +
                (ProfileStore.LastLoadFellBack
                    ? "\nNOTE: the previous file was unreadable - " + ProfileStore.LastLoadMessage
                    : string.Empty));
        }

        [MenuItem(MenuRoot + "Delete Profile Save", false, 221)]
        public static void DeleteProfileSave()
        {
            if (!EditorUtility.DisplayDialog(
                    "Delete the local profile?",
                    "This removes " + ProfileStore.Path +
                    " and starts from a level 1 profile. It cannot be undone.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            ProfileService.ResetProfile();
            Debug.Log("[Profile] Local profile deleted and reset to defaults.");
        }
    }
}
