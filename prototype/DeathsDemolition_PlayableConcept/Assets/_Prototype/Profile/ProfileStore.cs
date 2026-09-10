using System;
using System.IO;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>
    /// Reads and writes the local profile file. Offline, no service, no cloud.
    ///
    /// FAILURE POLICY - acceptance criterion 27
    /// ----------------------------------------
    /// A profile that will not parse is a data problem, not a program fault, and the
    /// prototype must keep running with a fresh profile instead of throwing. So every
    /// failure path here does the same three things:
    ///
    ///   1. logs at WARNING level - never Error, never an exception that reaches the
    ///      console, because criterion 27 says normal use must produce neither;
    ///   2. keeps the unreadable bytes by renaming them aside, so the failure can still
    ///      be investigated and nothing the player earned is silently shredded;
    ///   3. returns a valid default profile.
    ///
    /// Saving is atomic: the JSON goes to a temporary file first and only then replaces
    /// the real one. Killing the process mid-write can therefore lose the last run, but
    /// it cannot leave a half-written file behind - which is the most common way a save
    /// becomes corrupt in the first place.
    /// </summary>
    public static class ProfileStore
    {
        /// <summary>PROTOTYPE-ONLY file name. One local profile, no slots.</summary>
        public const string FileName = "prototype-profile.json";

        private const string TempSuffix = ".writing";
        private const string CorruptSuffix = ".corrupt";

        private static string s_pathOverride;

        /// <summary>Absolute path of the save file.</summary>
        public static string Path
        {
            get
            {
                if (!string.IsNullOrEmpty(s_pathOverride))
                {
                    return s_pathOverride;
                }

                return System.IO.Path.Combine(Application.persistentDataPath, FileName);
            }
        }

        /// <summary>True when the last <see cref="Load"/> had to fall back to defaults.</summary>
        public static bool LastLoadFellBack { get; private set; }

        /// <summary>Why the last load fell back, in English. Shown on the profile pane.</summary>
        public static string LastLoadMessage { get; private set; } = string.Empty;

        /// <summary>Redirects the file, for tests and for the F1 panel. Null restores the default.</summary>
        public static void SetPathOverride(string path)
        {
            s_pathOverride = path;
        }

        public static ProfileData Load()
        {
            LastLoadFellBack = false;
            LastLoadMessage = string.Empty;

            string path = Path;
            string json = null;

            try
            {
                if (!File.Exists(path))
                {
                    // First launch. Not a failure, and not worth a console line.
                    return Fresh();
                }

                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return FallBack("The profile file could not be read (" + e.GetType().Name + ").", path, false);
            }

            if (string.IsNullOrEmpty(json))
            {
                return FallBack("The profile file was empty.", path, true);
            }

            ProfileData data;
            try
            {
                data = JsonUtility.FromJson<ProfileData>(json);
            }
            catch (Exception e)
            {
                return FallBack("The profile file is not valid JSON (" + e.GetType().Name + ").", path, true);
            }

            if (data == null)
            {
                return FallBack("The profile file did not contain a profile.", path, true);
            }

            if (data.SaveVersion <= 0 || data.SaveVersion > ProfileData.CurrentVersion)
            {
                return FallBack(
                    "The profile file reports save version " + data.SaveVersion +
                    ", which this build cannot read (it understands 1 to " + ProfileData.CurrentVersion + ").",
                    path,
                    true);
            }

            data.Sanitize();
            return data;
        }

        public static bool Save(ProfileData data)
        {
            if (data == null)
            {
                return false;
            }

            data.Sanitize();
            data.LastSavedUtc = DateTime.UtcNow.ToString("o");

            string path = Path;
            string temp = path + TempSuffix;

            try
            {
                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temp, JsonUtility.ToJson(data, true));

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                // A save that cannot be written must not interrupt play either.
                Debug.LogWarning(
                    "[Profile] Could not write the profile to '" + path + "' (" + e.GetType().Name + ": " +
                    e.Message + "). Progress from this session will be lost when the game closes.");

                TryDelete(temp);
                return false;
            }
        }

        /// <summary>Deletes the save. Debug affordance only; nothing in normal play calls it.</summary>
        public static void Delete()
        {
            TryDelete(Path);
        }

        private static ProfileData Fresh()
        {
            var data = new ProfileData();
            data.Sanitize();
            return data;
        }

        private static ProfileData FallBack(string reason, string path, bool preserveFile)
        {
            LastLoadFellBack = true;
            LastLoadMessage = reason;

            string kept = preserveFile ? PreserveCorruptFile(path) : null;

            // WARNING, not Error. The prototype is fully playable from here; only the
            // previous profile is gone, and it has been kept on disk if it existed.
            Debug.LogWarning(
                "[Profile] " + reason + " Starting from a new profile instead." +
                (string.IsNullOrEmpty(kept) ? string.Empty : " The unreadable file was kept as '" + kept + "'."));

            return Fresh();
        }

        /// <summary>
        /// Renames the unreadable file aside instead of overwriting it. Best effort: if
        /// even this fails, it is swallowed, because the caller is already on a recovery
        /// path and a second warning would say nothing new.
        /// </summary>
        private static string PreserveCorruptFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string target = path + CorruptSuffix + "." + DateTime.UtcNow.Ticks + ".json";
                File.Move(path, target);
                return System.IO.Path.GetFileName(target);
            }
            catch
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Nothing useful to do, and nothing depends on it.
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_pathOverride = null;
            LastLoadFellBack = false;
            LastLoadMessage = string.Empty;
        }
    }
}
