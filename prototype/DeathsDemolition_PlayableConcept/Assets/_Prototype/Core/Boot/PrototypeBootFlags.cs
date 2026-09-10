using UnityEngine;

namespace DD.Prototype.Core.Boot
{
    /// <summary>
    /// Development flags read at boot.
    ///
    /// PROTOTYPE_DIRECT_TO_RUN skips Main Menu and Selection and drops straight into a
    /// run. It can be set three ways, in ascending priority:
    ///
    ///   1. The scripting define PROTOTYPE_DIRECT_TO_RUN
    ///      (Tools > Deaths Demolition > Toggle PROTOTYPE_DIRECT_TO_RUN)
    ///   2. PrototypeConfig.System.DirectToRun, editable live in the F1 panel
    ///   3. The session override below, set by the panel or by another system
    ///
    /// Never ship a build with this on: it bypasses the whole meta flow.
    /// </summary>
    public static class PrototypeBootFlags
    {
        public const string DirectToRunDefine = "PROTOTYPE_DIRECT_TO_RUN";
        private const string DirectToRunPrefKey = "DD.Prototype.DirectToRun";

        private static bool? s_sessionOverride;

        /// <summary>True when the scripting define is compiled in.</summary>
        public static bool DirectToRunDefined
        {
            get
            {
#if PROTOTYPE_DIRECT_TO_RUN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Resolved value. Falls back to the config field, then to the compile-time define.
        /// </summary>
        public static bool DirectToRun
        {
            get
            {
                if (s_sessionOverride.HasValue)
                {
                    return s_sessionOverride.Value;
                }

                if (DirectToRunDefined)
                {
                    return true;
                }

                Config.PrototypeConfig config = Config.PrototypeConfig.Active;
                if (config != null && config.System.DirectToRun)
                {
                    return true;
                }

                return PlayerPrefs.GetInt(DirectToRunPrefKey, 0) != 0;
            }
            set
            {
                s_sessionOverride = value;
                PlayerPrefs.SetInt(DirectToRunPrefKey, value ? 1 : 0);
            }
        }

        /// <summary>Drop the session override so the config value governs again.</summary>
        public static void ClearSessionOverride()
        {
            s_sessionOverride = null;
        }

        /// <summary>Scene the boot sequence should hand off to.</summary>
        public static string FirstSceneName => DirectToRun ? PrototypeScenes.Run : PrototypeScenes.MainMenu;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_sessionOverride = null;
        }
    }
}
