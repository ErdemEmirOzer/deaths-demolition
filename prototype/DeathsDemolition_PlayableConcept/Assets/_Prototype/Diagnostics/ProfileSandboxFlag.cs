#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using DD.Prototype.Profile;
using UnityEngine;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-025: makes <c>-ddProfilePath &lt;file&gt;</c> work on its own in development builds, so
    /// <c>tools/smoke.sh</c> can run a dev player against a sandbox profile without arming any
    /// probe (the probes only honour the flag together with their own <c>-dd…Probe</c> flag).
    /// Same ordering as the probes: after SubsystemRegistration (where ProfileStore.ResetStatics
    /// clears overrides), before the Boot scene touches the profile. Release builds: no code.
    /// </summary>
    public static class ProfileSandboxFlag
    {
        public const string Flag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (Application.isEditor)
            {
                return;
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], Flag, StringComparison.Ordinal) && !string.IsNullOrEmpty(args[i + 1]))
                {
                    ProfileStore.SetPathOverride(args[i + 1]);
                    Debug.Log("[ProfileSandbox] profile path override -> " + args[i + 1]);
                    return;
                }
            }
        }
    }
}
#endif
