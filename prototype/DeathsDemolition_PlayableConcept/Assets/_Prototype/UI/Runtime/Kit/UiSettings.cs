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
        /// <summary>WP-068 U8: UI sounds (Interface bus) on their own slider, 0..1.</summary>
        public float UiVolume = 1f;
        public int Quality = -1;
        public bool ReducedMotion;

        /// <summary>WP-022E: local run telemetry (WP-045) on/off; default on, no identity data either way.</summary>
        public bool Telemetry = true;

        // ---- WP-022G accessibility
        /// <summary>0 = default palette, 1 = colour-blind safe (greens → blue family; danger keeps vermilion; blood is not touched).</summary>
        public int ColorPalette;

        /// <summary>Reading-text scale (Body/Label/Caption), 1.0–1.25; applies to the next screen built.</summary>
        public float TextScale = 1f;

        /// <summary>Halves damage / final-seconds flashes (HUD edges, WP-019 vignette flash) - never the blood.</summary>
        public bool ReduceFlashes;

        /// <summary>WP-022M: -1 = follow the launch / OS state, 0 = windowed, 1 = fullscreen (Settings DISPLAY).</summary>
        public int DisplayMode = -1;

        /// <summary>Last windowed size in Screen units (0 = let PrototypeWindowFit / 1280x720 decide).</summary>
        public int WindowWidth;
        public int WindowHeight;

        /// <summary>WP-068 slice 10b (U9): brief hints during the first three runs (in play and on loading screens). Off leaves the rules.</summary>
        public bool Hints = true;

        public UiSettingsData Clamp()
        {
            DisplayMode = Mathf.Clamp(DisplayMode, -1, 1);
            WindowWidth = Mathf.Max(0, WindowWidth);
            WindowHeight = Mathf.Max(0, WindowHeight);
            ColorPalette = ColorPalette == 1 ? 1 : 0;
            TextScale = Mathf.Clamp(TextScale, UiSettings.TextScaleMin, UiSettings.TextScaleMax);
            Version = CurrentVersion;
            UiScale = Mathf.Clamp(UiScale, UiTheme.UiScaleMin, UiTheme.UiScaleMax);
            MasterVolume = Mathf.Clamp01(MasterVolume);
            UiVolume = Mathf.Clamp01(UiVolume);
            return this;
        }
    }

    /// <summary>
    /// Player-facing settings the Settings pane edits (WP-022 / WP-022B): UI scale,
    /// master volume, graphics quality, reduced motion. Persisted in
    /// <c>ui-settings.json</c> under <see cref="UiDataPaths.Root"/> - the WP-024
    /// <c>InputPreferencesStore</c> pattern - NEVER in PlayerPrefs (a PlayerPrefs write
    /// lands in the Player's plist and broke every other session's user-data restore
    /// check) and never in the profile file (settings are device preferences, not
    /// progress; WP-009 transaction boundary). The file is written only when the player
    /// changes a setting: what the game measures or remembers on its own (load times,
    /// HINTs shown) lives in <see cref="UiLoadTiming"/> and <see cref="UiHintState"/>.
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

        /// <summary>WP-068 U8: raised with the new UI-sounds volume (0..1); the audio module applies it to the Interface bus.</summary>
        public static event Action<float> UiVolumeChanged;

        /// <summary>Raised with the new quality level index. S-01 may react beyond QualitySettings.</summary>
        public static event Action<int> QualityChanged;

        private static readonly UiSettingsData s_data = new UiSettingsData();
        private static bool s_loaded;
        private static string s_pathOverride;

        public static string FilePath
        {
            get { return s_pathOverride ?? Path.Combine(UiDataPaths.Root, FileName); }
        }

        /// <summary>Test seam: redirect the file and forget the loaded state (null = the UiDataPaths root).</summary>
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

        /// <summary>WP-068 slice 10b (U9): the HINTS setting (Settings pane).</summary>
        public static bool Hints
        {
            get
            {
                EnsureLoaded();
                return s_data.Hints;
            }
            set
            {
                EnsureLoaded();
                if (value == s_data.Hints)
                {
                    return;
                }

                s_data.Hints = value;
                Persist();
            }
        }

        /// <summary>WP-068 U8: the UI SOUNDS slider (Interface bus), persisted with the device preferences.</summary>
        public static float UiVolume
        {
            get
            {
                EnsureLoaded();
                return s_data.UiVolume;
            }
            set
            {
                EnsureLoaded();
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, s_data.UiVolume))
                {
                    return;
                }

                s_data.UiVolume = clamped;
                PrototypeAudio.UiVolume = clamped;
                UiVolumeChanged?.Invoke(clamped);
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
                UiMotion.ReducedMotion = value; // WP-068: the motion core applies RM by class
                Persist();
            }
        }

        public const float TextScaleMin = 1f;
        public const float TextScaleMax = 1.25f;

        /// <summary>WP-022G: 0 default, 1 colour-blind safe palette (UiTokens swaps the green family for blue).</summary>
        public static int ColorPalette
        {
            get { EnsureLoaded(); return s_data.ColorPalette; }
            set
            {
                EnsureLoaded();
                int v = value == 1 ? 1 : 0;
                if (v == s_data.ColorPalette) return;
                s_data.ColorPalette = v;
                UiTokens.Palette = v == 1 ? UiPalette.ColorBlindSafe : UiPalette.Default;
                Persist();
                Action handlers = PaletteChanged;
                if (handlers != null) handlers();
            }
        }

        /// <summary>Raised after the palette flips (screens rebuild their colours on the next build; the HUD re-reads tokens).</summary>
        public static event Action PaletteChanged;

        /// <summary>WP-022G: reading-text scale; UiTokens.FontSize multiplies Body / Label / Caption by it.</summary>
        public static float TextScale
        {
            get { EnsureLoaded(); return s_data.TextScale; }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp(value, TextScaleMin, TextScaleMax);
                if (Mathf.Approximately(v, s_data.TextScale)) return;
                s_data.TextScale = v;
                UiTokens.TextScale = v;
                Persist();
            }
        }

        /// <summary>WP-022G: flash reduction (HUD damage edges and the WP-019 vignette flash at 0.4×; blood untouched).</summary>
        public static bool ReduceFlashes
        {
            get { EnsureLoaded(); return s_data.ReduceFlashes; }
            set
            {
                EnsureLoaded();
                if (value == s_data.ReduceFlashes) return;
                s_data.ReduceFlashes = value;
                ApplyFlashes();
                Persist();
            }
        }

        /// <summary>The multiplier the flash consumers read (HUD, WP-019 GameFeelSettings.FlashScale).</summary>
        public const float ReducedFlashScale = 0.4f;

        private static void ApplyFlashes()
        {
            float scale = s_data.ReduceFlashes ? ReducedFlashScale : 1f;
            UiTokens.FlashScale = scale;
            DD.Prototype.Feedback.Vfx.GameFeelSettings.FlashScale = scale;
        }

        /// <summary>WP-022E: whether WP-045 run telemetry records runs (Settings toggle).</summary>
        public static bool Telemetry
        {
            get
            {
                EnsureLoaded();
                return s_data.Telemetry;
            }
            set
            {
                EnsureLoaded();
                if (value == s_data.Telemetry)
                {
                    return;
                }

                s_data.Telemetry = value;
                ApplyTelemetry();
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
            // Keys older files carry (LoadScenes / LoadSeconds / HintsSeen) are not fields any
            // more: JsonUtility skips them, and they drop out the next time the player saves.
            UiSettingsData loaded = UiDataPaths.Read<UiSettingsData>(FilePath, FileName);

            float defaultScale = UiTheme.Active != null ? UiTheme.Active.DefaultUiScale : 1f;
            s_data.UiScale = loaded != null ? loaded.UiScale : defaultScale;
            s_data.MasterVolume = loaded != null ? loaded.MasterVolume : 1f;
            s_data.UiVolume = loaded != null ? Mathf.Clamp01(loaded.UiVolume) : 1f;   // older files: the field initialiser (1) stands
            s_data.ReducedMotion = loaded != null && loaded.ReducedMotion;
            UiMotion.ReducedMotion = s_data.ReducedMotion;
            s_data.Telemetry = loaded == null || loaded.Telemetry;
            s_data.Hints = loaded == null || loaded.Hints;
            s_data.ColorPalette = loaded != null ? loaded.ColorPalette : 0;
            s_data.TextScale = loaded != null && loaded.TextScale > 0f ? loaded.TextScale : 1f;
            s_data.ReduceFlashes = loaded != null && loaded.ReduceFlashes;
            s_data.DisplayMode = loaded != null ? loaded.DisplayMode : -1;
            s_data.WindowWidth = loaded != null ? loaded.WindowWidth : 0;
            s_data.WindowHeight = loaded != null ? loaded.WindowHeight : 0;
            int count = Mathf.Max(1, QualitySettings.names.Length);
            int quality = loaded != null && loaded.Quality >= 0 ? loaded.Quality : QualitySettings.GetQualityLevel();
            s_data.Quality = Mathf.Clamp(quality, 0, count - 1);
            s_data.Clamp();
            ApplyVolume();
            ApplyQuality();
            ApplyTelemetry();
            UiTokens.Palette = s_data.ColorPalette == 1 ? UiPalette.ColorBlindSafe : UiPalette.Default;
            UiTokens.TextScale = s_data.TextScale;
            ApplyFlashes();
            ApplyStoredDisplayMode();
        }

        // ---- WP-022M display mode

        /// <summary>True when the game is (or is switching to) fullscreen. Settings DISPLAY row.</summary>
        public static bool Fullscreen
        {
            get
            {
                EnsureLoaded();
                return UiDisplayMode.Pending.HasValue ? UiDisplayMode.Pending.Value : Screen.fullScreenMode != FullScreenMode.Windowed;
            }
            set
            {
                EnsureLoaded();
                if (value && Screen.fullScreenMode == FullScreenMode.Windowed)
                {
                    RememberWindow(); // the size to come back to
                }

                s_data.DisplayMode = value ? 1 : 0;
                Persist();
                UiDisplayMode.Request(value, s_data.WindowWidth, s_data.WindowHeight);
            }
        }

        private static void RememberWindow()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width > 0 && Screen.height > 0)
            {
                s_data.WindowWidth = Screen.width;
                s_data.WindowHeight = Screen.height;
            }
        }

        private static void ApplyStoredDisplayMode()
        {
#if !UNITY_EDITOR
            // The command line (-screen-fullscreen, captures and QA gates) outranks the saved choice.
            if (s_data.DisplayMode < 0 || HasArg("-screen-fullscreen"))
            {
                return;
            }

            bool wantFull = s_data.DisplayMode == 1;
            if (wantFull != (Screen.fullScreenMode != FullScreenMode.Windowed))
            {
                UiDisplayMode.Request(wantFull, s_data.WindowWidth, s_data.WindowHeight);
            }
#endif
        }

        private static bool HasArg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyTelemetry()
        {
            // The command line (-ddTelemetry 0, bots/tests) is parsed by RunTelemetry at
            // install and is not overridden by a saved "on".
            if (s_data.Telemetry && !DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled && CommandLineDisablesTelemetry())
            {
                return;
            }

            DD.Prototype.Diagnostics.Telemetry.RunTelemetry.SetEnabled(s_data.Telemetry);
        }

        private static bool CommandLineDisablesTelemetry()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-ddTelemetry" && (args[i + 1] == "0" || string.Equals(args[i + 1], "off", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i + 1], "false", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyVolume()
        {
            // Single owner of AudioListener.volume is the audio module (S-08).
            PrototypeAudio.MasterVolume = s_data.MasterVolume;
            PrototypeAudio.UiVolume = s_data.UiVolume;
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
            UiDataPaths.Write(FilePath, FileName, s_data);
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
            s_data.UiVolume = 1f;
            s_data.Quality = -1;
            s_data.ReducedMotion = false;
            UiMotion.ReducedMotion = false;
            Changed = null;
            MasterVolumeChanged = null;
            UiVolumeChanged = null;
            QualityChanged = null;
        }
    }
}
