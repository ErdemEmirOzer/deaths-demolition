using System;
using System.IO;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Where the UI's files live (user-data rule). A Player keeps them in
    /// <c>Application.persistentDataPath</c>. A process launched with <c>-runTests</c> (every
    /// batch test run, in any test assembly), the test assemblies' own fixtures
    /// (TestUiDataIsolation, for interactive runs too) and capture runs (<c>-ddUiCapture</c>)
    /// all use a process-local folder instead (the WP-051 dd-pid pattern), so nothing a test
    /// or a capture does reaches the player's own files. Moving the root makes every UI file
    /// re-read from the new place.
    /// </summary>
    public static class UiDataPaths
    {
        private static string s_rootOverride;

        /// <summary>The folder ui-settings.json, load-timing.json and ui-hints.json live in.</summary>
        public static string Root
        {
            get { return s_rootOverride ?? (RunsUnderTestRunner ? ProcessLocalRoot : Application.persistentDataPath); }
        }

        private static int s_testRunner = -1;

        /// <summary>
        /// True when this process was started with <c>-runTests</c> (the Unity test runner in
        /// batch). It is the default for every test assembly, because a namespace-less
        /// [SetUpFixture] is kept only once per assembly and a new assembly would otherwise
        /// start on the player's files (u1, 2026-09-24).
        /// </summary>
        public static bool RunsUnderTestRunner
        {
            get
            {
                if (s_testRunner < 0)
                {
                    s_testRunner = Array.IndexOf(Environment.GetCommandLineArgs(), "-runTests") >= 0 ? 1 : 0;
                }

                return s_testRunner == 1;
            }
        }

        /// <summary>True while the files are redirected away from persistentDataPath.</summary>
        public static bool IsProcessLocal
        {
            get { return s_rootOverride != null || RunsUnderTestRunner; }
        }

        /// <summary>This process's private folder under temporaryCachePath.</summary>
        public static string ProcessLocalRoot
        {
            get { return Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "ui"); }
        }

        /// <summary>Redirect every UI file to <paramref name="root"/> (null = persistentDataPath) and forget what was loaded.</summary>
        public static void SetRootOverride(string root)
        {
            s_rootOverride = root;
            UiSettings.SetPathOverride(null);
            UiLoadTiming.SetPathOverride(null);
            UiHintState.SetPathOverride(null);
        }

        public static void UseProcessLocalRoot()
        {
            SetRootOverride(ProcessLocalRoot);
        }

        internal static T Read<T>(string path, string fileName) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] " + fileName + " could not be read; using defaults. " + e.Message);
                return null;
            }
        }

        internal static void Write(string path, string fileName, object data)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] " + fileName + " could not be written: " + e.Message);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_rootOverride = null;
        }
    }

    /// <summary>The serialised shape of load-timing.json. Parallel arrays (JsonUtility has no dictionaries).</summary>
    [Serializable]
    public sealed class UiLoadTimingData
    {
        public int Version = 1;
        public string[] Scenes = new string[0];
        public float[] Seconds = new float[0];
    }

    /// <summary>
    /// WP-068 slice 10 (direction §15.1): the last measured load per scene, which picks the
    /// SCREEN or CURTAIN depth. A machine-local cache in <c>load-timing.json</c>, kept out of
    /// ui-settings.json: the game writes that file only when the player changes a setting.
    /// Absent or unreadable, every scene counts as unmeasured and the default threshold
    /// applies. The load-time keys older ui-settings.json files carry are never read.
    /// </summary>
    public static class UiLoadTiming
    {
        public const string FileName = "load-timing.json";

        private static readonly UiLoadTimingData s_data = new UiLoadTimingData();
        private static bool s_loaded;
        private static string s_pathOverride;

        public static string FilePath
        {
            get { return s_pathOverride ?? Path.Combine(UiDataPaths.Root, FileName); }
        }

        /// <summary>Test seam: redirect the file and forget the loaded state (null = the UiDataPaths root).</summary>
        public static void SetPathOverride(string path)
        {
            s_pathOverride = path;
            s_loaded = false;
        }

        /// <summary>The last measured load of <paramref name="scene"/> in seconds, or -1 when it has never been measured.</summary>
        public static float LastLoadSeconds(string scene)
        {
            EnsureLoaded();
            for (int i = 0; i < s_data.Scenes.Length; i++)
            {
                if (s_data.Scenes[i] == scene)
                {
                    return s_data.Seconds[i];
                }
            }

            return -1f;
        }

        /// <summary>Writes back a completed load's duration (never the profile, never ui-settings.json).</summary>
        public static void RecordLoad(string scene, float seconds)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(scene))
            {
                return;
            }

            int index = Array.IndexOf(s_data.Scenes, scene);
            if (index < 0)
            {
                index = s_data.Scenes.Length;
                Array.Resize(ref s_data.Scenes, index + 1);
                Array.Resize(ref s_data.Seconds, index + 1);
                s_data.Scenes[index] = scene;
            }

            s_data.Seconds[index] = seconds;
            UiDataPaths.Write(FilePath, FileName, s_data);
        }

        private static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            UiLoadTimingData loaded = UiDataPaths.Read<UiLoadTimingData>(FilePath, FileName);
            bool valid = loaded != null && loaded.Scenes != null && loaded.Seconds != null && loaded.Seconds.Length == loaded.Scenes.Length;
            s_data.Scenes = valid ? loaded.Scenes : new string[0];
            s_data.Seconds = valid ? loaded.Seconds : new float[0];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_loaded = false;
            s_pathOverride = null;
        }
    }

    /// <summary>The serialised shape of ui-hints.json.</summary>
    [Serializable]
    public sealed class UiHintStateData
    {
        public int Version = 1;

        /// <summary>HINT tips already shown on this machine (one bit per HINT, UiLoadingTips.HintBit).</summary>
        public int Seen;
    }

    /// <summary>
    /// WP-068 slice 10b (U9): which HINT loading tips this machine has already shown, so each
    /// shows once. Written by the game when a HINT appears, so it lives in its own
    /// <c>ui-hints.json</c> and never in ui-settings.json.
    /// </summary>
    public static class UiHintState
    {
        public const string FileName = "ui-hints.json";

        private static readonly UiHintStateData s_data = new UiHintStateData();
        private static bool s_loaded;
        private static string s_pathOverride;

        public static string FilePath
        {
            get { return s_pathOverride ?? Path.Combine(UiDataPaths.Root, FileName); }
        }

        /// <summary>Test seam: redirect the file and forget the loaded state (null = the UiDataPaths root).</summary>
        public static void SetPathOverride(string path)
        {
            s_pathOverride = path;
            s_loaded = false;
        }

        /// <summary>The once-per-machine HINT mask (UiLoadingTips).</summary>
        public static int Seen
        {
            get
            {
                EnsureLoaded();
                return s_data.Seen;
            }
            set
            {
                EnsureLoaded();
                if (value == s_data.Seen)
                {
                    return;
                }

                s_data.Seen = value;
                UiDataPaths.Write(FilePath, FileName, s_data);
            }
        }

        private static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            UiHintStateData loaded = UiDataPaths.Read<UiHintStateData>(FilePath, FileName);
            s_data.Seen = loaded != null ? loaded.Seen : 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_loaded = false;
            s_pathOverride = null;
        }
    }
}
