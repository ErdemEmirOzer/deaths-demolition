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
        public const string DirectToRunArg = "-ddDirectToRun";

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

                // WP-052C: `-ddDirectToRun` on the command line (tools/smoke.sh e4). Process-local, so a
                // smoke run never has to write the machine-wide PlayerPrefs key that used to drop OTHER
                // sessions' players straight into Run (S-10 run5 "instant death" root cause).
                if (HasCommandLineFlag(DirectToRunArg))
                {
                    return true;
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Config.PrototypeConfig config = Config.PrototypeConfig.Active;
                if (config != null && config.System.DirectToRun)
                {
                    return true;
                }

                return PlayerPrefs.GetInt(DirectToRunPrefKey, 0) != 0;
#else
                // WP-112 (C-00 12.10): a release player never skips the main menu on a stored switch - the F1 panel's
                // machine-wide PlayerPrefs key or the prototype config field; only the process-local smoke flag above.
                return false;
#endif
            }
            set
            {
                s_sessionOverride = value;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                PlayerPrefs.SetInt(DirectToRunPrefKey, value ? 1 : 0);
#endif
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
    
        private static bool HasCommandLineFlag(string flag)
        {
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], flag, System.StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (System.Exception)
            {
            }

            return false;
        }
    }
}
