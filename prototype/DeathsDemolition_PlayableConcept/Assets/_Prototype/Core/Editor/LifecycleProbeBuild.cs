using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// WP-001 · builds a macOS DEVELOPMENT Player for the lifecycle probe, from the command
    /// line only, without touching PlayerSettings. The default CLI switch
    /// (<c>-buildOSXUniversalPlayer</c>) cannot request a development build, and
    /// <see cref="PrototypeBuild.BuildMac"/> rewrites PlayerSettings, so neither fits a
    /// verification copy whose ProjectSettings must stay byte-identical to the manifest.
    ///
    ///   Unity -batchmode -nographics -quit -projectPath &lt;copy&gt;
    ///         -executeMethod DD.Prototype.Core.EditorTools.LifecycleProbeBuild.BuildDevelopmentPlayer
    ///         -ddOut /abs/path/DeathsDemolition-dev.app
    ///
    /// <c>BuildOptions.Development</c> defines DEVELOPMENT_BUILD, which is the only gate that
    /// compiles <c>Diagnostics/LifecycleProbe.cs</c> into the Player. Scenes come from
    /// EditorBuildSettings: the enabled entries exactly as the release build uses them, in
    /// order, followed by the DISABLED entries (WP-015, ADR-015-2: a disabled Build Settings
    /// entry is a dev-only scene — the AP-001 pilot the perf probe measures and the library
    /// showcase — that the release Player must not carry).
    /// </summary>
    public static class LifecycleProbeBuild
    {
        private const string OutFlag = "-ddOut";

        public static void BuildDevelopmentPlayer()
        {
            string target = Arg(OutFlag);
            if (string.IsNullOrEmpty(target))
            {
                throw new Exception("[ProbeBuild] " + OutFlag + " <path.app> is required.");
            }

            string[] scenes = DevelopmentScenes(EditorBuildSettings.scenes);

            if (scenes.Length == 0 || !EditorBuildSettings.scenes[0].enabled)
            {
                throw new Exception("[ProbeBuild] No enabled scenes in Build Settings (the entry scene must be enabled).");
            }

            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = target,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log(string.Format(
                "[ProbeBuild] result={0} scenes={1} size={2:0.0} MB development=true path={3}",
                s.result, scenes.Length, s.totalSize / (1024f * 1024f), target));

            if (s.result != BuildResult.Succeeded)
            {
                throw new Exception("[ProbeBuild] FAILED: " + s.result);
            }
        }

        /// <summary>Release scenes (enabled, in order) then the dev-only scenes (disabled, in order). Pure; tested.</summary>
        public static string[] DevelopmentScenes(EditorBuildSettingsScene[] entries)
        {
            return entries.Where(s => s.enabled).Select(s => s.path)
                .Concat(entries.Where(s => !s.enabled).Select(s => s.path))
                .ToArray();
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
