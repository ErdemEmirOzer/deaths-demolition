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
    ///         -ddMode dev|release -ddOut /abs/DeathsDemolition.app [-ddReport /abs/report.json]
    ///
    /// Stamps <c>build-info.json</c>, builds every enabled scene in Build Settings for
    /// macOS, writes a JSON report (result, seconds, bytes, stamp) and restores the placeholder
    /// stamp. Throws on failure so the Unity process exits non-zero (exit-code contract for
    /// the scripts). Player settings are not modified here; whatever ProjectSettings says ships.
    /// </summary>
    public static class DdBuild
    {
        private const string ModeFlag = "-ddMode";
        private const string OutFlag = "-ddOut";
        private const string ReportFlag = "-ddReport";

        public static void Build()
        {
            string mode = Arg(ModeFlag) ?? "release";
            string output = Arg(OutFlag);
            string report = Arg(ReportFlag);
            if (string.IsNullOrEmpty(output))
            {
                throw new Exception("[DdBuild] " + OutFlag + " <path.app> is required.");
            }

            if (mode != "dev" && mode != "release")
            {
                throw new Exception("[DdBuild] " + ModeFlag + " must be dev or release (was '" + mode + "').");
            }

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || !EditorBuildSettings.scenes[0].enabled)
            {
                throw new Exception("[DdBuild] No enabled scenes in Build Settings (the entry scene must be enabled).");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildInfo stamp = BuildStamp.Write(mode);
            DateTime t0 = DateTime.UtcNow;
            BuildSummary summary;
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    options = mode == "dev" ? BuildOptions.Development : BuildOptions.None,
                };
                summary = BuildPipeline.BuildPlayer(options).summary;
            }
            finally
            {
                BuildStamp.RestorePlaceholder();
            }

            double seconds = (DateTime.UtcNow - t0).TotalSeconds;
            Debug.Log(string.Format("[DdBuild] result={0} mode={1} scenes={2} size={3:0.0} MB seconds={4:0} path={5}",
                summary.result, mode, scenes.Length, summary.totalSize / (1024f * 1024f), seconds, output));

            if (!string.IsNullOrEmpty(report))
            {
                WriteReport(report, mode, output, scenes.Length, summary, seconds, stamp);
            }

            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception("[DdBuild] FAILED: " + summary.result + " errors=" + summary.totalErrors);
            }
        }

        private static void WriteReport(string path, string mode, string output, int scenes, BuildSummary s, double seconds, BuildInfo stamp)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"result\": \"").Append(s.result).Append("\",\n");
            sb.Append("  \"mode\": \"").Append(mode).Append("\",\n");
            sb.Append("  \"output\": \"").Append(Escape(output)).Append("\",\n");
            sb.Append("  \"scenes\": ").Append(scenes).Append(",\n");
            sb.Append("  \"totalSize\": ").Append(s.totalSize).Append(",\n");
            sb.Append("  \"totalErrors\": ").Append(s.totalErrors).Append(",\n");
            sb.Append("  \"totalWarnings\": ").Append(s.totalWarnings).Append(",\n");
            sb.Append("  \"seconds\": ").Append(seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"unity\": \"").Append(Application.unityVersion).Append("\",\n");
            sb.Append("  \"stamp\": ").Append(stamp.ToJson().Replace("\n", "\n  ")).Append("\n");
            sb.Append("}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
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
