using DD.Prototype.Core.InputAbstraction;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-070 B4 · ONE-WAY bridge: the persisted look preferences (input-settings.json,
    /// <see cref="InputPreferences"/> v3: LookSensitivity / LookInvertY / StickLookDegreesPerSecond)
    /// feed the live <see cref="CameraModeService.Settings"/>. The camera never writes the file:
    /// the settings UI (S-09) edits <see cref="InputPreferencesStore"/> and saves there, like the
    /// dead zone. Tests and bots therefore cannot touch the player's input file through the camera.
    /// </summary>
    public static class CameraInputPreferencesBridge
    {
        private static bool s_hooked;

        /// <summary>Subscribes once and applies the current preferences. Called by the camera rig.</summary>
        public static void Ensure()
        {
            if (s_hooked)
            {
                return;
            }

            s_hooked = true;
            InputPreferencesStore.Changed -= Apply;
            InputPreferencesStore.Changed += Apply;
            Apply(InputPreferencesStore.Current);
        }

        /// <summary>Copies the look fields into the live camera settings (no mode change) when they differ.</summary>
        public static void Apply(InputPreferences preferences)
        {
            if (preferences == null)
            {
                return;
            }

            CameraModeSettings next = CameraModeService.Settings.Clone();
            CopyLook(preferences, next);
            next.Sanitize();
            CameraModeSettings live = CameraModeService.Settings;
            if (Mathf.Approximately(next.LookSensitivity, live.LookSensitivity)
                && next.InvertY == live.InvertY
                && Mathf.Approximately(next.StickLookDegreesPerSecond, live.StickLookDegreesPerSecond))
            {
                return;
            }

            CameraModeService.ApplySettings(next, applyMode: false);
        }

        /// <summary>Pure mapping (tests): InputPreferences look fields → CameraModeSettings.</summary>
        public static void CopyLook(InputPreferences preferences, CameraModeSettings settings)
        {
            settings.LookSensitivity = preferences.LookSensitivity;
            settings.InvertY = preferences.LookInvertY;
            settings.StickLookDegreesPerSecond = preferences.StickLookDegreesPerSecond;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_hooked)
            {
                InputPreferencesStore.Changed -= Apply;
            }

            s_hooked = false;
        }
    }
}
