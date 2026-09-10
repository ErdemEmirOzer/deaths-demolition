using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// PROTOTYPE-ONLY: standalone build so the prototype can be played in a real,
    /// resizable window instead of the Editor's Game view. The request asks for a
    /// prototype playable in Play Mode; this is an addition, not a replacement.
    /// </summary>
    public static class PrototypeBuild
    {
        [MenuItem("Tools/Deaths Demolition/Build Standalone (macOS)")]
        public static void BuildMac()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] No enabled scenes in Build Settings.");
                return;
            }

            string root = Path.GetDirectoryName(Application.dataPath);
            string target = Path.Combine(root, "Build", "DeathsDemolition.app");
            Directory.CreateDirectory(Path.GetDirectoryName(target));

            // Windowed at the project's default resolution, and resizable.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = target,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            Debug.Log(string.Format(
                "[Build] result={0} scenes={1} size={2:0.0} MB path={3}",
                s.result, scenes.Length, s.totalSize / (1024f * 1024f), target));

            if (s.result != BuildResult.Succeeded)
            {
                throw new Exception("[Build] FAILED: " + s.result);
            }
        }
    }
}
