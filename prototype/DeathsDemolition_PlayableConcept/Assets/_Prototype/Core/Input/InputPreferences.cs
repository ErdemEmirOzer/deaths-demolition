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
        public const int CurrentVersion = 1;

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

        /// <summary>Hard ceilings from the request (ADR-024-1). Preferences can only narrow the assist, never widen it.</summary>
        public const float MaxAimAssistAngleDegrees = 6f;
        public const float MaxAimAssistDistance = 15f;
        public const float MaxRumbleIntensity = 0.3f;
        public const int MaxRumbleMilliseconds = 120;
        public const float MaxStickDeadzone = 0.5f;

        /// <summary>Clamp every field into its allowed range. Returns this.</summary>
        public InputPreferences Clamp()
        {
            Version = CurrentVersion;
            StickDeadzone = Mathf.Clamp(StickDeadzone, 0f, MaxStickDeadzone);
            RumbleIntensity = Mathf.Clamp(RumbleIntensity, 0f, MaxRumbleIntensity);
            AimAssistAngleDegrees = Mathf.Clamp(AimAssistAngleDegrees, 0f, MaxAimAssistAngleDegrees);
            AimAssistDistance = Mathf.Clamp(AimAssistDistance, 0f, MaxAimAssistDistance);
            return this;
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
                   Mathf.Approximately(AimAssistDistance, other.AimAssistDistance);
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
