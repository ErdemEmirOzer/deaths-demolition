#if DEVELOPMENT_BUILD || UNITY_EDITOR || DD_PERF_PROBE
using System;
using System.IO;
using DD.Prototype.UI;
using UnityEngine;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-078 (C-00 §4.14 a, 25.09): <c>-ddUiDataRoot &lt;dir&gt;</c> keeps a measured Player's UI files
    /// (ui-settings / load-timing / ui-hints) in <paramref name="dir"/> instead of the player's own folder —
    /// the perf matrix (PilotPerfProbe) and any other scripted run; QaRunBot does the same for its runs
    /// under <c>&lt;out&gt;/ui-data</c>. BeforeSceneLoad, like ProfileSandboxFlag; the override drops anything
    /// read earlier, so the run starts from default settings (the tier comes from <c>-ddQualityTier</c>).
    /// Also compiled into the release-probe build (DD_PERF_PROBE), which measures without DEVELOPMENT_BUILD.
    /// </summary>
    public static class QaUiDataSandbox
    {
        public const string Flag = "-ddUiDataRoot";

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
                    Directory.CreateDirectory(args[i + 1]);
                    UiDataPaths.SetRootOverride(args[i + 1]);
                    Debug.Log("[UiDataSandbox] ui data root override -> " + args[i + 1]);
                    return;
                }
            }
        }
    }
}
#endif
