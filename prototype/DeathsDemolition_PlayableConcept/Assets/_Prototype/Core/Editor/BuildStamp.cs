using System;
using System.Diagnostics;
using System.IO;
using DD.Prototype.Core.Build;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// WP-025: writes the version stamp (<c>git describe --tags --always --dirty</c>, commit,
    /// branch, UTC date, build mode, Unity version) into
    /// <c>Assets/_Prototype/Core/Resources/build-info.json</c> for a build, and restores the
    /// committed placeholder afterwards so the working tree stays clean.
    /// </summary>
    public static class BuildStamp
    {
        public const string AssetPath = "Assets/_Prototype/Core/Resources/build-info.json";

        public static string AbsolutePath => Path.Combine(Path.GetDirectoryName(Application.dataPath), AssetPath);

        /// <summary>Repository root (the directory that holds .git), or null.</summary>
        public static string RepositoryRoot()
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        /// <summary>Builds the stamp from git; falls back to "unknown" fields when git is unavailable.</summary>
        public static BuildInfo Generate(string mode)
        {
            string root = RepositoryRoot();
            var info = new BuildInfo
            {
                stamped = true,
                describe = Git(root, "describe --tags --always --dirty") ?? "unknown",
                commit = Git(root, "rev-parse --short=12 HEAD") ?? "unknown",
                branch = Git(root, "rev-parse --abbrev-ref HEAD") ?? "unknown",
                date = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                mode = mode ?? "unknown",
                unity = Application.unityVersion,
            };
            return info;
        }

        public static string PlaceholderJson()
        {
            return new BuildInfo().ToJson();
        }

        /// <summary>Writes the stamp for <paramref name="mode"/> and imports the asset. Returns it.</summary>
        public static BuildInfo Write(string mode)
        {
            BuildInfo info = Generate(mode);
            File.WriteAllText(AbsolutePath, info.ToJson());
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[BuildStamp] " + info.Describe());
            return info;
        }

        /// <summary>Restores the committed placeholder (stamped=false).</summary>
        public static void RestorePlaceholder()
        {
            File.WriteAllText(AbsolutePath, PlaceholderJson());
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        [MenuItem("Tools/Deaths Demolition/Build/Print version stamp")]
        public static void Print()
        {
            Debug.Log("[BuildStamp] " + Generate("editor").Describe());
        }

        public static string Git(string root, string args)
        {
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(5000);
                    return p.ExitCode == 0 && output.Length > 0 ? output : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
