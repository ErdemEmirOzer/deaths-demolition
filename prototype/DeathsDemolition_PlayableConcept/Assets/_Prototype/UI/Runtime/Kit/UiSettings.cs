using System;
using System.IO;
using DD.Prototype.Audio;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>The serialised shape of the UI settings file. Plain data; clamped on load.</summary>
    [Serializable]
    public sealed class UiSettingsData
    {
        /// <summary>2 (M4-R1): the quality index counts the three tiers of QUALITY-TIERS-V2 (was six levels).</summary>
        public const int CurrentVersion = 2;

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

        // ---- M4-R1 N2 camera (WP-070 §3, §5: the Pause "Camera" selector and the Settings camera rows write these;
        // UiCameraSettingsBridge feeds CameraModeService). Older files lack the keys: the initialisers stand.
        /// <summary>CameraMode: 0 top-down, 1 third person, 2 first person.</summary>
        public int CameraMode;
        /// <summary>First-person FOV, horizontal degrees at 16:9 (CameraModeSettings range).</summary>
        public float FirstPersonFov = 90f;
        /// <summary>Third-person FOV, vertical degrees.</summary>
        public float ThirdPersonFov = 60f;
        /// <summary>CameraShoulder: 0 right, 1 left.</summary>
        public int Shoulder;
        /// <summary>Head bob amplitude, 0..1 (0 under reduced motion anyway).</summary>
        public float HeadBobScale = 1f;
        /// <summary>WP-070 §3.2: a small static dot at the screen centre in TP/FP (motion-sickness aid).</summary>
        public bool CentreDot;

        public UiSettingsData Clamp()
        {
            CameraMode = Mathf.Clamp(CameraMode, 0, 2);
            FirstPersonFov = Mathf.Clamp(FirstPersonFov, CameraModeSettings.MinFirstPersonFov, CameraModeSettings.MaxFirstPersonFov);
            ThirdPersonFov = Mathf.Clamp(ThirdPersonFov, CameraModeSettings.MinThirdPersonFov, CameraModeSettings.MaxThirdPersonFov);
            Shoulder = Shoulder == 1 ? 1 : 0;
            HeadBobScale = Mathf.Clamp01(HeadBobScale);
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

        /// <summary>M4-R1 N2: a camera row (mode, FOV, shoulder, head bob, centre dot) or reduced motion changed.</summary>
        public static event Action CameraChanged;

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
                RaiseCamera();   // WP-070 D-10: the one switch also stills the camera
            }
        }

        // ---- M4-R1 N2 camera

        /// <summary>The saved camera mode (0 top-down, 1 third person, 2 first person).</summary>
        public static int CameraMode
        {
            get { EnsureLoaded(); return s_data.CameraMode; }
            set { SetCamera(ref s_data.CameraMode, Mathf.Clamp(value, 0, 2)); }
        }

        public static float FirstPersonFov
        {
            get { EnsureLoaded(); return s_data.FirstPersonFov; }
            set { SetCamera(ref s_data.FirstPersonFov, Mathf.Clamp(value, CameraModeSettings.MinFirstPersonFov, CameraModeSettings.MaxFirstPersonFov)); }
        }

        public static float ThirdPersonFov
        {
            get { EnsureLoaded(); return s_data.ThirdPersonFov; }
            set { SetCamera(ref s_data.ThirdPersonFov, Mathf.Clamp(value, CameraModeSettings.MinThirdPersonFov, CameraModeSettings.MaxThirdPersonFov)); }
        }

        /// <summary>0 right shoulder, 1 left.</summary>
        public static int Shoulder
        {
            get { EnsureLoaded(); return s_data.Shoulder; }
            set { SetCamera(ref s_data.Shoulder, value == 1 ? 1 : 0); }
        }

        public static float HeadBobScale
        {
            get { EnsureLoaded(); return s_data.HeadBobScale; }
            set { SetCamera(ref s_data.HeadBobScale, Mathf.Clamp01(value)); }
        }

        public static bool CentreDot
        {
            get { EnsureLoaded(); return s_data.CentreDot; }
            set
            {
                EnsureLoaded();
                if (value == s_data.CentreDot)
                {
                    return;
                }

                s_data.CentreDot = value;
                Persist();
                RaiseCamera();
            }
        }

        private static void SetCamera(ref int field, int value)
        {
            EnsureLoaded();
            if (field == value)
            {
                return;
            }

            field = value;
            Persist();
            RaiseCamera();
        }

        private static void SetCamera(ref float field, float value)
        {
            EnsureLoaded();
            if (Mathf.Approximately(field, value))
            {
                return;
            }

            field = value;
            Persist();
            RaiseCamera();
        }

        private static void RaiseCamera()
        {
            try
            {
                CameraChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
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

        /// <summary>
        /// S-05 QUALITY-TIERS-V2 §2 rule 3: a file written before the three tiers (version 1) holds an index into the old six
        /// levels - 0–1 → Performance, 2–3 → High, 4–5 → Cinematic. Newer files and other layouts pass through.
        /// </summary>
        public static int MigrateQuality(int savedQuality, int savedVersion, int levelCount)
        {
            if (savedVersion < 2 && levelCount == 3 && savedQuality >= 0)
            {
                return Mathf.Clamp(savedQuality / 2, 0, 2);
            }

            return savedQuality;
        }

        /// <summary>
        /// DEC-100: the Settings label of a quality level - the level's name with its frame target (Cinematic "30 FPS", High
        /// "60 FPS"; Performance carries none), upper case for the stepper.
        /// </summary>
        public static string QualityLabel(int index)
        {
            string[] names = QualitySettings.names;
            if (names == null || names.Length == 0)
            {
                return "DEFAULT";
            }

            string name = QualityName(index).ToUpperInvariant();
            QualityTier tier = QualityTiers.TierOfLevel(index, names.Length);
            return tier == QualityTier.Sinematik ? name + " · 30 FPS" : tier == QualityTier.Yuksek ? name + " · 60 FPS" : name;
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
            var defaults = new UiSettingsData();
            s_data.CameraMode = loaded != null ? loaded.CameraMode : defaults.CameraMode;
            s_data.FirstPersonFov = loaded != null ? loaded.FirstPersonFov : defaults.FirstPersonFov;
            s_data.ThirdPersonFov = loaded != null ? loaded.ThirdPersonFov : defaults.ThirdPersonFov;
            s_data.Shoulder = loaded != null ? loaded.Shoulder : defaults.Shoulder;
            s_data.HeadBobScale = loaded != null ? loaded.HeadBobScale : defaults.HeadBobScale;
            s_data.CentreDot = loaded != null && loaded.CentreDot;
            int count = Mathf.Max(1, QualitySettings.names.Length);
            // DEC-100 (user, 25.09): with no saved choice the game opens on Cinematic (QualityTiers.DefaultTier); a saved
            // choice is kept as it is (no migration).
            int quality = loaded != null && loaded.Quality >= 0
                ? MigrateQuality(loaded.Quality, loaded.Version, QualitySettings.names.Length)
                : QualityTiers.LevelOfTier(QualityTiers.DefaultTier, QualitySettings.names.Length);
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
            // WP-078: a -ddQualityTier run keeps its tier for the process; the saved choice stays in s_data / on disk.
            int level = QualityTiers.LevelOverride ?? s_data.Quality;
            if (QualitySettings.GetQualityLevel() != level)
            {
                QualitySettings.SetQualityLevel(level, false);
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
            var defaults = new UiSettingsData();
            s_data.CameraMode = defaults.CameraMode;
            s_data.FirstPersonFov = defaults.FirstPersonFov;
            s_data.ThirdPersonFov = defaults.ThirdPersonFov;
            s_data.Shoulder = defaults.Shoulder;
            s_data.HeadBobScale = defaults.HeadBobScale;
            s_data.CentreDot = defaults.CentreDot;
            Changed = null;
            CameraChanged = null;
            MasterVolumeChanged = null;
            UiVolumeChanged = null;
            QualityChanged = null;
        }
    }
}
