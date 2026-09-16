using System;
using System.IO;
using DD.Prototype.Audio;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>The serialised shape of the UI settings file. Plain data; clamped on load.</summary>
    [Serializable]
    public sealed class UiSettingsData
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public float UiScale = 1f;
        public float MasterVolume = 1f;
        public int Quality = -1;
        public bool ReducedMotion;

        public UiSettingsData Clamp()
        {
            Version = CurrentVersion;
            UiScale = Mathf.Clamp(UiScale, UiTheme.UiScaleMin, UiTheme.UiScaleMax);
            MasterVolume = Mathf.Clamp01(MasterVolume);
            return this;
        }
    }

    /// <summary>
    /// Player-facing settings the Settings pane edits (WP-022 / WP-022B): UI scale,
    /// master volume, graphics quality, reduced motion. Persisted in
    /// <c>Application.persistentDataPath/ui-settings.json</c> - the WP-024
    /// <c>InputPreferencesStore</c> pattern - NEVER in PlayerPrefs (a PlayerPrefs write
    /// lands in the Player's plist and broke every other session's user-data restore
    /// check) and never in the profile file (settings are device preferences, not
    /// progress; WP-009 transaction boundary).
    ///
    /// Values are applied immediately and announced through events so the audio owner
    /// (S-08: <see cref="PrototypeAudio.MasterVolume"/> is set here, the mixer's
    /// "MasterVolume" parameter follows <see cref="MasterVolumeChanged"/>) and the
    /// rendering owner (S-01) can bind their own systems without the UI knowing them.
    /// </summary>
    public static class UiSettings
    {
        public const string FileName = "ui-settings.json";

        /// <summary>Raised after any value changed and was applied. Menus redraw readouts from it.</summary>
        public static event Action Changed;

        /// <summary>Raised with the new master volume (0..1). S-08 mirrors it onto the mixer's "MasterVolume".</summary>
        public static event Action<float> MasterVolumeChanged;

        /// <summary>Raised with the new quality level index. S-01 may react beyond QualitySettings.</summary>
        public static event Action<int> QualityChanged;

        private static readonly UiSettingsData s_data = new UiSettingsData();
        private static bool s_loaded;
        private static string s_pathOverride;

        public static string FilePath
        {
            get { return s_pathOverride ?? Path.Combine(Application.persistentDataPath, FileName); }
        }

        /// <summary>Test seam: redirect the file and forget the loaded state.</summary>
        public static void SetPathOverride(string path)
        {
            s_pathOverride = path;
            s_loaded = false;
        }

        /// <summary>UI scale multiplier, 0.9..1.2. 1 = the 1920x1080 reference layout.</summary>
        public static float UiScale
        {
            get
            {
                EnsureLoaded();
                return s_data.UiScale;
            }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp(value, UiTheme.UiScaleMin, UiTheme.UiScaleMax);
                if (Mathf.Approximately(clamped, s_data.UiScale))
                {
                    return;
                }

                s_data.UiScale = clamped;
                Persist();
            }
        }

        public static float MasterVolume
        {
            get
            {
                EnsureLoaded();
                return s_data.MasterVolume;
            }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, s_data.MasterVolume))
                {
                    return;
                }

                s_data.MasterVolume = clamped;
                ApplyVolume();
                MasterVolumeChanged?.Invoke(clamped);
                Persist();
            }
        }

        /// <summary>Index into <see cref="QualitySettings.names"/>.</summary>
        public static int Quality
        {
            get
            {
                EnsureLoaded();
                return s_data.Quality;
            }
            set
            {
                EnsureLoaded();
                int count = Mathf.Max(1, QualitySettings.names.Length);
                int clamped = Mathf.Clamp(value, 0, count - 1);
                if (clamped == s_data.Quality)
                {
                    return;
                }

                s_data.Quality = clamped;
                ApplyQuality();
                QualityChanged?.Invoke(clamped);
                Persist();
            }
        }

        /// <summary>
        /// Accessibility: pulses hold at full alpha, screen fades are instant, drifting
        /// petals stand still (the prefers-reduced-motion equivalent). Read by UiMotion.
        /// </summary>
        public static bool ReducedMotion
        {
            get
            {
                EnsureLoaded();
                return s_data.ReducedMotion;
            }
            set
            {
                EnsureLoaded();
                if (value == s_data.ReducedMotion)
                {
                    return;
                }

                s_data.ReducedMotion = value;
                Persist();
            }
        }

        public static string QualityName(int index)
        {
            string[] names = QualitySettings.names;
            return names != null && index >= 0 && index < names.Length ? names[index] : "Default";
        }

        /// <summary>Reads the file once and applies volume/quality. Idempotent.</summary>
        public static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            UiSettingsData loaded = null;
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    loaded = JsonUtility.FromJson<UiSettingsData>(File.ReadAllText(path));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] " + FileName + " could not be read; using defaults. " + e.Message);
            }

            float defaultScale = UiTheme.Active != null ? UiTheme.Active.DefaultUiScale : 1f;
            s_data.UiScale = loaded != null ? loaded.UiScale : defaultScale;
            s_data.MasterVolume = loaded != null ? loaded.MasterVolume : 1f;
            s_data.ReducedMotion = loaded != null && loaded.ReducedMotion;
            int count = Mathf.Max(1, QualitySettings.names.Length);
            int quality = loaded != null && loaded.Quality >= 0 ? loaded.Quality : QualitySettings.GetQualityLevel();
            s_data.Quality = Mathf.Clamp(quality, 0, count - 1);
            s_data.Clamp();
            ApplyVolume();
            ApplyQuality();
        }

        private static void ApplyVolume()
        {
            // Single owner of AudioListener.volume is the audio module (S-08).
            PrototypeAudio.MasterVolume = s_data.MasterVolume;
        }

        private static void ApplyQuality()
        {
            if (QualitySettings.GetQualityLevel() != s_data.Quality)
            {
                QualitySettings.SetQualityLevel(s_data.Quality, false);
            }
        }

        private static void Persist()
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, JsonUtility.ToJson(s_data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] " + FileName + " could not be written: " + e.Message);
            }

            UiEvents.Raise(UiEvent.SettingsChanged);
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
            s_pathOverride = null;
            s_data.UiScale = 1f;
            s_data.MasterVolume = 1f;
            s_data.Quality = -1;
            s_data.ReducedMotion = false;
            Changed = null;
            MasterVolumeChanged = null;
            QualityChanged = null;
        }
    }
}
