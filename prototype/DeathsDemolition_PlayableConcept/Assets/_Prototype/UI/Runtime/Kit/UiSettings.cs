using System;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Player-facing settings the Settings pane edits (WP-022): UI scale, master volume
    /// and graphics quality. Stored in PlayerPrefs, applied immediately, and exposed as
    /// events so the audio (S-08) and rendering (S-01) owners can bind their own systems
    /// without the UI module knowing them. This is the data interface the package
    /// brief asked for; the UI only owns the knobs.
    ///
    /// Nothing here touches the profile file: settings are device preferences, not
    /// progress, and must not enter the profile transaction (WP-009).
    /// </summary>
    public static class UiSettings
    {
        public const string UiScaleKey = "DD.UI.Scale";
        public const string MasterVolumeKey = "DD.Audio.MasterVolume";
        public const string QualityKey = "DD.Graphics.Quality";

        /// <summary>Raised after any value changes and has been applied. Menus rebuild from it.</summary>
        public static event Action Changed;

        /// <summary>Raised with the new master volume (0..1). The audio owner applies it to its mixer.</summary>
        public static event Action<float> MasterVolumeChanged;

        /// <summary>Raised with the new quality level index. The rendering owner may react beyond QualitySettings.</summary>
        public static event Action<int> QualityChanged;

        private static bool s_loaded;
        private static float s_uiScale = 1f;
        private static float s_masterVolume = 1f;
        private static int s_quality;

        /// <summary>UI scale multiplier, 0.9..1.2. 1 = the 1920x1080 reference layout.</summary>
        public static float UiScale
        {
            get
            {
                EnsureLoaded();
                return s_uiScale;
            }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp(value, UiTheme.UiScaleMin, UiTheme.UiScaleMax);
                if (Mathf.Approximately(clamped, s_uiScale))
                {
                    return;
                }

                s_uiScale = clamped;
                PlayerPrefs.SetFloat(UiScaleKey, clamped);
                RaiseChanged();
            }
        }

        public static float MasterVolume
        {
            get
            {
                EnsureLoaded();
                return s_masterVolume;
            }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, s_masterVolume))
                {
                    return;
                }

                s_masterVolume = clamped;
                PlayerPrefs.SetFloat(MasterVolumeKey, clamped);
                ApplyVolume();
                MasterVolumeChanged?.Invoke(clamped);
                RaiseChanged();
            }
        }

        /// <summary>Index into <see cref="QualitySettings.names"/>.</summary>
        public static int Quality
        {
            get
            {
                EnsureLoaded();
                return s_quality;
            }
            set
            {
                EnsureLoaded();
                int count = Mathf.Max(1, QualitySettings.names.Length);
                int clamped = Mathf.Clamp(value, 0, count - 1);
                if (clamped == s_quality)
                {
                    return;
                }

                s_quality = clamped;
                PlayerPrefs.SetInt(QualityKey, clamped);
                ApplyQuality();
                QualityChanged?.Invoke(clamped);
                RaiseChanged();
            }
        }

        public static string QualityName(int index)
        {
            string[] names = QualitySettings.names;
            return names != null && index >= 0 && index < names.Length ? names[index] : "Default";
        }

        /// <summary>Reads PlayerPrefs once and applies volume/quality. Idempotent.</summary>
        public static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            float defaultScale = UiTheme.Active != null ? UiTheme.Active.DefaultUiScale : 1f;
            s_uiScale = Mathf.Clamp(PlayerPrefs.GetFloat(UiScaleKey, defaultScale), UiTheme.UiScaleMin, UiTheme.UiScaleMax);
            s_masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
            int count = Mathf.Max(1, QualitySettings.names.Length);
            s_quality = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()), 0, count - 1);
            ApplyVolume();
            ApplyQuality();
        }

        private static void ApplyVolume()
        {
            AudioListener.volume = s_masterVolume;
        }

        private static void ApplyQuality()
        {
            if (QualitySettings.GetQualityLevel() != s_quality)
            {
                QualitySettings.SetQualityLevel(s_quality, false);
            }
        }

        private static void RaiseChanged()
        {
            PlayerPrefs.Save();
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_loaded = false;
            s_uiScale = 1f;
            s_masterVolume = 1f;
            s_quality = 0;
            Changed = null;
            MasterVolumeChanged = null;
            QualityChanged = null;
        }
    }
}
