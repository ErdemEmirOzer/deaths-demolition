using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Core.Build;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-025: offline crash / error log for players. Listens to
    /// <see cref="Application.logMessageReceivedThreaded"/> (Exception, Error, Assert) and
    /// <see cref="AppDomain.UnhandledException"/>, and appends each entry to
    /// <c>&lt;persistentDataPath&gt;/Logs/dd-crash-&lt;utc&gt;.txt</c> with the build stamp, the
    /// session id and the current run/round. Nothing is uploaded; there is no telemetry.
    ///
    /// Privacy: the home directory and the account name are masked before anything is
    /// written (<see cref="Mask(string,string,string)"/>), so a file the player shares by hand
    /// carries no path or account name. Editor sessions are skipped (test noise); a player
    /// can turn the reporter off with <c>-ddNoCrashLog</c>.
    /// </summary>
    public static class CrashLogReporter
    {
        public const string LogsFolder = "Logs";
        public const string FilePrefix = "dd-crash-";
        public const int MaxEntriesPerSession = 200;
        public const int KeepFiles = 10;
        private const string DisableFlag = "-ddNoCrashLog";

        private static readonly object s_gate = new object();
        private static bool s_installed;
        private static bool s_headerWritten;
        private static int s_entries;
        private static int s_suppressed;
        private static string s_path;
        private static string s_sessionId;
        private static string s_home;
        private static string s_user;
        private static string s_header;
        private static readonly Dictionary<string, int> s_seen = new Dictionary<string, int>(32);

        /// <summary>Entries written this session (tests / F1 readout).</summary>
        public static int Entries => s_entries;

        /// <summary>The file of this session, or null while nothing has been written.</summary>
        public static string CurrentFile => s_path;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            if (s_installed || Application.isEditor || HasFlag(DisableFlag))
            {
                return;
            }

            Install(Application.persistentDataPath);
        }

        /// <summary>Installs against an explicit root (tests use a temp folder).</summary>
        public static void Install(string root)
        {
            lock (s_gate)
            {
                if (s_installed)
                {
                    return;
                }

                s_installed = true;
                s_sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
                s_home = HomeDirectory();
                s_user = Environment.UserName;
                string folder = Path.Combine(root, LogsFolder);
                s_path = Path.Combine(folder, FilePrefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + ".txt");
                // Resources / SystemInfo are main-thread APIs; the header is built here, on the
                // main thread, because the first entry may arrive from a worker thread.
                s_header = Header();
                Application.logMessageReceivedThreaded += OnLog;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
                Application.quitting += OnQuit;
            }
        }

        /// <summary>Removes the hooks (tests). Does not delete the file.</summary>
        public static void Uninstall()
        {
            lock (s_gate)
            {
                if (!s_installed)
                {
                    return;
                }

                Application.logMessageReceivedThreaded -= OnLog;
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
                Application.quitting -= OnQuit;
                s_installed = false;
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }

            Record(type.ToString(), condition, stackTrace);
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            Record("Unhandled", ex != null ? ex.GetType().Name + ": " + ex.Message : Convert.ToString(e.ExceptionObject),
                ex != null ? ex.StackTrace : string.Empty);
        }

        private static void OnQuit()
        {
            if (s_entries > 0)
            {
                Append("-- quit  entries=" + s_entries + " suppressed=" + s_suppressed + "\n");
            }
        }

        /// <summary>Writes one entry (public so a probe can exercise the path). Thread-safe.</summary>
        public static void Record(string kind, string message, string stackTrace)
        {
            lock (s_gate)
            {
                if (!s_installed)
                {
                    return;
                }

                string key = kind + "|" + message;
                int seen;
                s_seen.TryGetValue(key, out seen);
                s_seen[key] = seen + 1;
                if (seen > 0 || s_entries >= MaxEntriesPerSession)
                {
                    s_suppressed++;
                    return;
                }

                s_entries++;
                var sb = new StringBuilder(256);
                if (!s_headerWritten)
                {
                    s_headerWritten = true;
                    sb.Append(s_header ?? string.Empty);
                }

                sb.Append("-- ").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")).Append("  ").Append(kind)
                  .Append("  ").Append(RunContext()).Append('\n');
                sb.Append(Mask(message, s_home, s_user)).Append('\n');
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    sb.Append(Mask(stackTrace.TrimEnd(), s_home, s_user)).Append('\n');
                }

                sb.Append('\n');
                Append(sb.ToString());
            }
        }

        private static string Header()
        {
            BuildInfo info = BuildInfo.Read();
            var sb = new StringBuilder(256);
            sb.Append("Deaths Demolition crash log\n");
            sb.Append("session=").Append(s_sessionId).Append('\n');
            sb.Append("build=").Append(info.Describe()).Append('\n');
            sb.Append("platform=").Append(Application.platform).Append(" unity=").Append(Application.unityVersion)
              .Append(" os=").Append(Mask(SystemInfo.operatingSystem, s_home, s_user)).Append('\n');
            sb.Append("device=").Append(SystemInfo.graphicsDeviceType).Append(' ').Append(SystemInfo.systemMemorySize).Append("MB\n\n");
            return sb.ToString();
        }

        private static string RunContext()
        {
            try
            {
                RunHudState state = RunEvents.Current;
                RunDirector director = RunDirector.Instance;
                string runId = director != null ? director.RunId.ToString() : "-";
                return "run=" + runId + " round=" + state.Round + " phase=" + state.Phase;
            }
            catch (Exception)
            {
                return "run=? round=? phase=?";
            }
        }

        private static void Append(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(s_path));
                File.AppendAllText(s_path, text);
                Prune(Path.GetDirectoryName(s_path), s_path);
            }
            catch (Exception)
            {
                // The reporter must never throw into the game loop.
            }
        }

        private static bool s_pruned;

        private static void Prune(string folder, string current)
        {
            if (s_pruned)
            {
                return;
            }

            s_pruned = true;
            try
            {
                string[] files = Directory.GetFiles(folder, FilePrefix + "*.txt");
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - KeepFiles; i++)
                {
                    if (!string.Equals(files[i], current, StringComparison.Ordinal))
                    {
                        File.Delete(files[i]);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Masks the home directory (→ "~") and the account name (→ "&lt;user&gt;") wherever they
        /// appear, including the /Users/&lt;name&gt; and /home/&lt;name&gt; spellings. Pure; tests call it directly.
        /// </summary>
        public static string Mask(string text, string homeDirectory, string userName)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string result = text;
            if (!string.IsNullOrEmpty(homeDirectory) && homeDirectory.Length > 1)
            {
                string home = homeDirectory.TrimEnd('/', '\\');
                result = result.Replace(home, "~");
                result = result.Replace(home.Replace('/', '\\'), "~");
            }

            if (!string.IsNullOrEmpty(userName) && userName.Length > 1)
            {
                result = result.Replace("/Users/" + userName, "/Users/<user>");
                result = result.Replace("/home/" + userName, "/home/<user>");
                result = result.Replace("\\Users\\" + userName, "\\Users\\<user>");
                result = ReplaceWord(result, userName, "<user>");
            }

            return result;
        }

        private static string ReplaceWord(string text, string word, string replacement)
        {
            var sb = new StringBuilder(text.Length);
            int index = 0;
            while (index < text.Length)
            {
                int hit = text.IndexOf(word, index, StringComparison.Ordinal);
                if (hit < 0)
                {
                    sb.Append(text, index, text.Length - index);
                    break;
                }

                bool leftOk = hit == 0 || !char.IsLetterOrDigit(text[hit - 1]);
                int end = hit + word.Length;
                bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                sb.Append(text, index, hit - index);
                sb.Append(leftOk && rightOk ? replacement : word);
                index = end;
            }

            return sb.ToString();
        }

        private static string HomeDirectory()
        {
            try
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home))
                {
                    home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                return home;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool HasFlag(string flag)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], flag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Uninstall();
            s_headerWritten = false;
            s_entries = 0;
            s_suppressed = 0;
            s_path = null;
            s_sessionId = null;
            s_pruned = false;
            s_header = null;
            s_seen.Clear();
        }
    }
}
