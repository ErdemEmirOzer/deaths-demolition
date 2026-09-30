using System;
using System.IO;
using System.Linq;
using System.Text;
using DD.Prototype.Core.Build;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// WP-025: the one batchmode build entry used by <c>tools/build.sh</c>.
    ///
    ///   Unity -batchmode -nographics -quit -projectPath … -executeMethod DD.Prototype.Core.EditorTools.DdBuild.Build
    ///         -ddMode dev|release|release-probe -ddOut /abs/DeathsDemolition.app [-ddReport /abs/report.json]
    ///         (release-probe, WP-078: release options + scripting define DD_PERF_PROBE so PilotPerfProbe
    ///          is compiled in; never distributed, its own gate mode — not the release baseline)
    ///         [-ddTarget mac|win64]   (WP-066W / DEC-089; win64 → -ddOut /abs/DeathsDemolition-win64/DeathsDemolition.exe)
    ///         [-ddCompression none|lz4|lz4hc]   (WP-066C measurement; absent = today's uncompressed player data)
    ///
    /// Stamps <c>build-info.json</c>, builds every enabled scene in Build Settings for
    /// macOS (default) or Windows x64 (Mono, the installed WindowsStandaloneSupport module),
    /// writes a JSON report (result, seconds, bytes, stamp) and restores the placeholder
    /// stamp. Throws on failure so the Unity process exits non-zero (exit-code contract for
    /// the scripts). Player settings are not modified here; whatever ProjectSettings says ships.
    /// </summary>
    public static class DdBuild
    {
        private const string ModeFlag = "-ddMode";
        private const string OutFlag = "-ddOut";
        private const string ReportFlag = "-ddReport";
        private const string TargetFlag = "-ddTarget";
        private const string CompressionFlag = "-ddCompression";

        /// <summary>WP-078: release settings + <see cref="PerfProbeDefine"/>; not distributed.</summary>
        public const string ReleaseProbeMode = "release-probe";

        /// <summary>Scripting define that compiles PilotPerfProbe into a non-development player.</summary>
        public const string PerfProbeDefine = "DD_PERF_PROBE";

        public static void Build()
        {
            string mode = Arg(ModeFlag) ?? "release";
            string output = Arg(OutFlag);
            string report = Arg(ReportFlag);
            string targetName = Arg(TargetFlag) ?? "mac";
            string compression = Arg(CompressionFlag);
            BuildOptions compressionOption;
            switch (compression)
            {
                case null: case "none": compressionOption = BuildOptions.None; break;
                case "lz4": compressionOption = BuildOptions.CompressWithLz4; break;
                case "lz4hc": compressionOption = BuildOptions.CompressWithLz4HC; break;
                default: throw new Exception("[DdBuild] " + CompressionFlag + " must be none, lz4 or lz4hc (was '" + compression + "').");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new Exception("[DdBuild] " + OutFlag + " <path.app> is required.");
            }

            if (mode != "dev" && mode != "release" && mode != ReleaseProbeMode)
            {
                throw new Exception("[DdBuild] " + ModeFlag + " must be dev, release or " + ReleaseProbeMode + " (was '" + mode + "').");
            }

            BuildTarget target;
            switch (targetName)
            {
                case "mac": target = BuildTarget.StandaloneOSX; break;
                case "win64": target = BuildTarget.StandaloneWindows64; break;
                default: throw new Exception("[DdBuild] " + TargetFlag + " must be mac or win64 (was '" + targetName + "').");
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                throw new Exception("[DdBuild] build support for " + target + " is not installed in this Unity (module install is a user decision).");
            }

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || !EditorBuildSettings.scenes[0].enabled)
            {
                throw new Exception("[DdBuild] No enabled scenes in Build Settings (the entry scene must be enabled).");
            }

            // WP-074 B1: a dev build may carry extra dev-only scenes (DD_BUILD_EXTRA_SCENES, ';'-separated asset paths — e.g. the
            // World v5 greybox for the S-03 spawn simulation / S-10 bot soak); release builds never do.
            string extra = Environment.GetEnvironmentVariable("DD_BUILD_EXTRA_SCENES");
            if (mode == "dev" && !string.IsNullOrEmpty(extra))
            {
                string[] listed = extra.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                foreach (string missing in listed.Where(p => !File.Exists(p))) Debug.LogWarning("[DdBuild] extra dev scene not found: " + missing);
                string[] added = listed.Where(p => File.Exists(p) && !scenes.Contains(p)).ToArray();
                // S-01 review: an evidence build must not come out without the scene it was asked for and pass by accident.
                if (listed.Length > 0 && added.Length == 0 && !listed.Any(p => scenes.Contains(p)))
                    throw new Exception("[DdBuild] DD_BUILD_EXTRA_SCENES names no existing scene: " + extra);
                scenes = scenes.Concat(added).ToArray();
                Debug.Log("[DdBuild] extra dev scenes: " + string.Join(", ", added));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildInfo stamp = BuildStamp.Write(mode);
            DateTime t0 = DateTime.UtcNow;
            BuildSummary summary;
            BuildPlayerOptions options;
            try
            {
                options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = target,
                    options = (mode == "dev" ? BuildOptions.Development : BuildOptions.None) | compressionOption,
                    extraScriptingDefines = mode == ReleaseProbeMode ? new[] { PerfProbeDefine } : null,
                };
                summary = BuildPipeline.BuildPlayer(options).summary;
            }
            finally
            {
                BuildStamp.RestorePlaceholder();
            }

            double seconds = (DateTime.UtcNow - t0).TotalSeconds;
            Debug.Log(string.Format("[DdBuild] result={0} mode={1} scenes={2} size={3:0.0} MB seconds={4:0} path={5}{6}",
                summary.result, mode, scenes.Length, summary.totalSize / (1024f * 1024f), seconds, output,
                (target == BuildTarget.StandaloneOSX ? "" : " target=" + targetName) + (compression == null ? "" : " compression=" + compression)));

            if (!string.IsNullOrEmpty(report))
            {
                bool development = (options.options & BuildOptions.Development) != 0;
                string backend = PlayerSettings.GetScriptingBackend(BuildPipeline.GetBuildTargetGroup(target)).ToString();
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report, ReportJson(summary.result.ToString(), mode, target == BuildTarget.StandaloneOSX ? null : targetName, compression,
                    output, scenes, development, backend, summary.totalSize, summary.totalErrors, summary.totalWarnings, seconds, stamp.ToJson()));
            }

            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception("[DdBuild] FAILED: " + summary.result + " errors=" + summary.totalErrors);
            }
        }

        // target is null for macOS and compression null when the flag is absent, so those two keys keep the
        // pre-WP-066W/066C shape. WP-078 A6 (S-10, C-00 30.09): development, sceneList (build order: path + guid) and
        // scriptingBackend are appended after "scenes" (the count stays for older readers). Pure, so tests can call it.
        public static string ReportJson(string result, string mode, string target, string compression, string output, string[] scenePaths,
            bool development, string scriptingBackend, ulong totalSize, int totalErrors, int totalWarnings, double seconds, string stampJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"result\": \"").Append(result).Append("\",\n");
            sb.Append("  \"mode\": \"").Append(mode).Append("\",\n");
            if (target != null) sb.Append("  \"target\": \"").Append(target).Append("\",\n");
            if (compression != null) sb.Append("  \"compression\": \"").Append(compression).Append("\",\n");
            sb.Append("  \"output\": \"").Append(Escape(output)).Append("\",\n");
            sb.Append("  \"scenes\": ").Append(scenePaths.Length).Append(",\n");
            sb.Append("  \"development\": ").Append(development ? "true" : "false").Append(",\n");
            sb.Append("  \"sceneList\": [");
            for (int i = 0; i < scenePaths.Length; i++)
            {
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"path\": \"").Append(Escape(scenePaths[i])).Append("\", \"guid\": \"")
                  .Append(AssetDatabase.AssetPathToGUID(scenePaths[i])).Append("\" }");
            }
            sb.Append(scenePaths.Length == 0 ? "],\n" : "\n  ],\n");
            sb.Append("  \"scriptingBackend\": \"").Append(Escape(scriptingBackend)).Append("\",\n");
            sb.Append("  \"totalSize\": ").Append(totalSize).Append(",\n");
            sb.Append("  \"totalErrors\": ").Append(totalErrors).Append(",\n");
            sb.Append("  \"totalWarnings\": ").Append(totalWarnings).Append(",\n");
            sb.Append("  \"seconds\": ").Append(seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"unity\": \"").Append(Application.unityVersion).Append("\",\n");
            sb.Append("  \"stamp\": ").Append(stampJson.Replace("\n", "\n  ")).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
