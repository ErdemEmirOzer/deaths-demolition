using System;
using System.IO;
using UnityEngine;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// WP-024: the player-adjustable input preferences (deadzone, rumble, aim assist).
    /// Plain serialisable data, persisted OUTSIDE the profile save in
    /// <c>Application.persistentDataPath/input-settings.json</c>; the S-09 settings
    /// screen edits this through <see cref="InputPreferencesStore"/>. Nothing here is
    /// a balance value: aim assist only bends the aim direction (ADR-024-1), it never
    /// touches damage, range or target count.
    /// </summary>
    [Serializable]
    public sealed class InputPreferences
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;

        [PrototypeOnly(PendingDecisions.None, "Radial stick deadzone (0..0.5). Default 0.15 per the WP-024 request.")]
        [Range(0f, 0.5f)] public float StickDeadzone = 0.15f;

        public bool RumbleEnabled = true;

        [PrototypeOnly(PendingDecisions.None, "Rumble intensity ceiling (0..1). The request caps it at 0.3.")]
        [Range(0f, 1f)] public float RumbleIntensity = 0.3f;

        public bool AimAssistEnabled = true;

        [PrototypeOnly(PendingDecisions.None, "Aim assist cone half-angle in degrees (request: <= 6).")]
        [Range(0f, 6f)] public float AimAssistAngleDegrees = 6f;

        [PrototypeOnly(PendingDecisions.None, "Aim assist maximum target distance in metres (request: <= 15).")]
        [Range(0f, 15f)] public float AimAssistDistance = 15f;

        /// <summary>
        /// WP-059 (S-09 AIM ASSIST Off / Light / Full → WP-024 data): the player-facing level
        /// is the source; <see cref="AimAssistEnabled"/> / <see cref="AimAssistAngleDegrees"/> /
        /// <see cref="AimAssistDistance"/> are derived from it by <see cref="Clamp"/> (see
        /// <see cref="AimAssistLevels"/>). A version-1 file (no level) migrates from those
        /// fields once. Stored as an int so JsonUtility keeps it stable.
        /// </summary>
        public int AimAssistLevel = (int)InputAimAssistLevel.Full;

        /// <summary>Marker of a version-1 document: the level is derived from the legacy fields on the first clamp.</summary>
        public const int AimAssistLevelUnset = -1;

        /// <summary>Hard ceilings from the request (ADR-024-1). Preferences can only narrow the assist, never widen it.</summary>
        public const float MaxAimAssistAngleDegrees = 6f;
        public const float MaxAimAssistDistance = 15f;
        public const float MaxRumbleIntensity = 0.3f;
        public const int MaxRumbleMilliseconds = 120;
        public const float MaxStickDeadzone = 0.5f;

        /// <summary>Clamp every field into its allowed range. Returns this.</summary>
        public InputPreferences Clamp()
        {
            if (AimAssistLevel == AimAssistLevelUnset || Version < 2 || !AimAssistLevels.Matches((InputAimAssistLevel)AimAssistLevel, AimAssistEnabled, AimAssistAngleDegrees, AimAssistDistance))
            {
                // Version 1 (WP-024 / WP-022G) or a direct edit of the legacy fields (tests, imports):
                // the fields say what the player meant, the level follows them; then the fields
                // snap to the level's numbers (a custom 4° cone reads as Light).
                AimAssistLevel = (int)AimAssistLevels.FromLegacy(AimAssistEnabled, AimAssistAngleDegrees);
            }

            Version = CurrentVersion;
            StickDeadzone = Mathf.Clamp(StickDeadzone, 0f, MaxStickDeadzone);
            RumbleIntensity = Mathf.Clamp(RumbleIntensity, 0f, MaxRumbleIntensity);
            AimAssistLevel = Mathf.Clamp(AimAssistLevel, (int)InputAimAssistLevel.Off, (int)InputAimAssistLevel.Full);
            var level = (InputAimAssistLevel)AimAssistLevel;
            AimAssistEnabled = level != InputAimAssistLevel.Off;
            AimAssistAngleDegrees = AimAssistLevels.AngleDegreesOf(level);
            AimAssistDistance = AimAssistLevels.DistanceOf(level);
            return this;
        }

        /// <summary>The level as the enum (always in range after <see cref="Clamp"/>).</summary>
        public InputAimAssistLevel AimAssist
        {
            get { return (InputAimAssistLevel)Mathf.Clamp(AimAssistLevel, (int)InputAimAssistLevel.Off, (int)InputAimAssistLevel.Full); }
            set
            {
                // The level writes its numbers into the legacy fields first, so Clamp sees them agree.
                AimAssistLevel = (int)value;
                AimAssistEnabled = value != InputAimAssistLevel.Off;
                AimAssistAngleDegrees = AimAssistLevels.AngleDegreesOf(value);
                AimAssistDistance = AimAssistLevels.DistanceOf(value);
                Clamp();
            }
        }

        public InputPreferences Copy()
        {
            return (InputPreferences)MemberwiseClone();
        }

        public void CopyFrom(InputPreferences other)
        {
            if (other == null)
            {
                return;
            }

            StickDeadzone = other.StickDeadzone;
            RumbleEnabled = other.RumbleEnabled;
            RumbleIntensity = other.RumbleIntensity;
            AimAssistEnabled = other.AimAssistEnabled;
            AimAssistAngleDegrees = other.AimAssistAngleDegrees;
            AimAssistDistance = other.AimAssistDistance;
            AimAssistLevel = other.AimAssistLevel;
            Version = other.Version;
            Clamp();
        }

        public bool SameAs(InputPreferences other)
        {
            return other != null &&
                   Mathf.Approximately(StickDeadzone, other.StickDeadzone) &&
                   RumbleEnabled == other.RumbleEnabled &&
                   Mathf.Approximately(RumbleIntensity, other.RumbleIntensity) &&
                   AimAssistEnabled == other.AimAssistEnabled &&
                   Mathf.Approximately(AimAssistAngleDegrees, other.AimAssistAngleDegrees) &&
                   Mathf.Approximately(AimAssistDistance, other.AimAssistDistance) &&
                   AimAssistLevel == other.AimAssistLevel;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        /// <summary>Parse; a malformed or empty document yields defaults (never throws).</summary>
        public static InputPreferences FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new InputPreferences();
            }

            try
            {
                InputPreferences p = JsonUtility.FromJson<InputPreferences>(json);
                return (p ?? new InputPreferences()).Clamp();
            }
            catch (Exception)
            {
                return new InputPreferences();
            }
        }
    }

    /// <summary>WP-059: the aim-assist level the CONTROLS pane offers (S-09), stored in the WP-024 data.</summary>
    public enum InputAimAssistLevel
    {
        Off = 0,
        Light = 1,
        Full = 2
    }

    /// <summary>
    /// WP-059: what each <see cref="InputAimAssistLevel"/> means in the ADR-024-1 terms —
    /// Light narrows the cone to half the ceiling at the full distance, Full is the ceiling,
    /// Off keeps the Full numbers on file but disables the pull. Preferences can only
    /// narrow the assist, never widen it.
    /// </summary>
    public static class AimAssistLevels
    {
        public const float LightAngleShare = 0.5f;

        public static float AngleDegreesOf(InputAimAssistLevel level)
        {
            return level == InputAimAssistLevel.Light
                ? InputPreferences.MaxAimAssistAngleDegrees * LightAngleShare
                : InputPreferences.MaxAimAssistAngleDegrees;
        }

        public static float DistanceOf(InputAimAssistLevel level)
        {
            return InputPreferences.MaxAimAssistDistance;
        }

        /// <summary>True when the legacy fields are exactly what <paramref name="level"/> derives (so a direct field edit can be told apart).</summary>
        public static bool Matches(InputAimAssistLevel level, bool enabled, float angleDegrees, float distance)
        {
            return enabled == (level != InputAimAssistLevel.Off) &&
                   Mathf.Abs(angleDegrees - AngleDegreesOf(level)) < 1e-4f &&
                   Mathf.Abs(distance - DistanceOf(level)) < 1e-4f;
        }

        /// <summary>A version-1 file: off when disabled, Light when the angle sat under three quarters of the ceiling, else Full.</summary>
        public static InputAimAssistLevel FromLegacy(bool enabled, float angleDegrees)
        {
            if (!enabled)
            {
                return InputAimAssistLevel.Off;
            }

            return angleDegrees < InputPreferences.MaxAimAssistAngleDegrees * 0.75f ? InputAimAssistLevel.Light : InputAimAssistLevel.Full;
        }

        public static string Label(InputAimAssistLevel level)
        {
            switch (level)
            {
                case InputAimAssistLevel.Off: return "OFF";
                case InputAimAssistLevel.Light: return "LIGHT";
                default: return "FULL";
            }
        }
    }

    /// <summary>
    /// Where the live <see cref="InputPreferences"/> live. One instance for the whole
    /// process; the input adapter, the rumble service and the aim assist read it every
    /// frame, the settings screen writes it and calls <see cref="Save"/>.
    /// File: <c>Application.persistentDataPath/input-settings.json</c> (never the profile).
    /// </summary>
    public static class InputPreferencesStore
    {
        public const string FileName = "input-settings.json";

        private static InputPreferences s_current = new InputPreferences();
        private static string s_pathOverride;
        private static bool s_loaded;

        /// <summary>Raised after a change was committed with <see cref="Apply"/>, <see cref="Load"/> or <see cref="ResetToDefaults"/>.</summary>
        public static event Action<InputPreferences> Changed;

        /// <summary>Never null. Loads the file lazily on first access.</summary>
        public static InputPreferences Current
        {
            get
            {
                if (!s_loaded)
                {
                    Load();
                }

                return s_current;
            }
        }

        /// <summary>Full path of the settings file. Tests point this at a temp file.</summary>
        public static string FilePath
        {
            get { return s_pathOverride ?? Path.Combine(Application.persistentDataPath, FileName); }
        }

        /// <summary>Test seam: redirect persistence (null restores the default path) and drop the loaded state.</summary>
        public static void SetPathOverride(string path)
        {
            s_pathOverride = path;
            s_loaded = false;
        }

        /// <summary>Commit new values (clamped) and persist them.</summary>
        public static void Apply(InputPreferences values)
        {
            if (values == null)
            {
                return;
            }

            Current.CopyFrom(values);
            Save();
            RaiseChanged();
        }

        public static void ResetToDefaults()
        {
            Current.CopyFrom(InputSettingsAsset.LoadDefaults());
            Save();
            RaiseChanged();
        }

        public static void Load()
        {
            s_loaded = true;
            InputPreferences loaded = null;
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    loaded = InputPreferences.FromJson(File.ReadAllText(path));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Input] input-settings.json could not be read; using defaults. " + e.Message);
            }

            s_current.CopyFrom(loaded ?? InputSettingsAsset.LoadDefaults());
            RaiseChanged();
        }

        public static void Save()
        {
            try
            {
                string path = FilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, Current.Clamp().ToJson());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Input] input-settings.json could not be written. " + e.Message);
            }
        }

        private static void RaiseChanged()
        {
            Action<InputPreferences> h = Changed;
            if (h == null)
            {
                return;
            }

            try
            {
                h.Invoke(s_current);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = new InputPreferences();
            s_pathOverride = null;
            s_loaded = false;
            Changed = null;
        }
    }
}
