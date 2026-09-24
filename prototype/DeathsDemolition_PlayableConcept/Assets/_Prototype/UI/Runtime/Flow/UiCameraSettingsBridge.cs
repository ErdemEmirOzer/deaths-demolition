using DD.Prototype.CameraSystem;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// M4-R1 N2 · ONE-WAY bridge from the persisted camera rows (ui-settings.json, <see cref="UiSettings"/>: mode, FOVs,
    /// shoulder, head bob, reduced motion) to the live <see cref="CameraModeService.Settings"/>. It mirrors S-01's
    /// <c>CameraInputPreferencesBridge</c>: it copies the live settings and overwrites only its own fields, so the look
    /// fields that bridge owns are never clobbered.
    ///
    /// At boot the settings apply without a mode change: the rig starts a run in <see cref="CameraModeService.ResolveStartMode"/>,
    /// where <c>-ddCameraMode</c> outranks the saved mode. A mode chosen in Pause is requested through
    /// <see cref="RequestMode"/> (WP-070 §5.1: the blend starts behind the pause, on unscaled time).
    /// </summary>
    public static class UiCameraSettingsBridge
    {
        private static bool s_installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            UiSettings.CameraChanged += OnCameraChanged;
            Apply(applyMode: false);
        }

        /// <summary>Pause "Camera" selector: saves the mode and requests it (also when only the command line differed).</summary>
        public static void RequestMode(CameraMode mode)
        {
            UiSettings.CameraMode = (int)mode;
            Apply(applyMode: false);
            CameraModeService.Request(mode);
        }

        /// <summary>Pure mapping (tests): the saved camera rows → a CameraModeSettings copy.</summary>
        public static void CopyInto(CameraModeSettings settings)
        {
            settings.Mode = (CameraMode)UiSettings.CameraMode;
            settings.FirstPersonFov = UiSettings.FirstPersonFov;
            settings.ThirdPersonFov = UiSettings.ThirdPersonFov;
            settings.Shoulder = UiSettings.Shoulder == 1 ? CameraShoulder.Left : CameraShoulder.Right;
            settings.HeadBobScale = UiSettings.HeadBobScale;
            settings.ReducedMotion = UiSettings.ReducedMotion;
        }

        private static void OnCameraChanged()
        {
            Apply(applyMode: false);
        }

        private static void Apply(bool applyMode)
        {
            CameraModeSettings next = CameraModeService.Settings.Clone();
            CopyInto(next);
            CameraModeService.ApplySettings(next, applyMode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;   // UiSettings clears its own CameraChanged at the same point
        }
    }
}
