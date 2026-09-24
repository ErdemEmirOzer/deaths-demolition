using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests
{
    /// <summary>
    /// WP-068 user-data rule: PlayMode tests never write the player's UI files.
    ///
    /// Evidence (2026-09-24): the flow suites recorded real load times, and the settings
    /// suites toggled HINTS and TEXT SCALE, straight into the Editor's
    /// persistentDataPath/ui-settings.json. Before any PlayMode test runs, this fixture moves
    /// every UI file (ui-settings.json, load-timing.json, ui-hints.json) to a process-local
    /// folder (the WP-051 dd-pid pattern). As a side effect, no test inherits the machine's
    /// saved settings (Reduced Motion left on, a 1.25 text scale).
    ///
    /// The fixture sits on the DD.Prototype.Tests namespace, which holds every test here.
    /// Unity's NUnit keeps only one namespace-less [SetUpFixture] per assembly, and
    /// TestProfileIsolation already has that place (u1: a second one never ran). As in
    /// TestProfileIsolation, OneTimeTearDown deliberately leaves the redirect in place, because
    /// a reveal still in flight can record a load after teardown. The next Play Mode entry
    /// resets it (UiDataPaths.ResetStatics).
    /// </summary>
    [SetUpFixture]
    public sealed class TestUiDataIsolation
    {
        public static readonly string[] FileNames = { UiSettings.FileName, UiLoadTiming.FileName, UiHintState.FileName };

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string root = UiDataPaths.ProcessLocalRoot;
            foreach (string name in FileNames)
            {
                string leftover = Path.Combine(root, name); // this process's own test files only
                if (File.Exists(leftover))
                {
                    File.Delete(leftover);
                }
            }

            UiDataPaths.UseProcessLocalRoot();
            Assert.AreEqual(Path.Combine(root, UiSettings.FileName), UiSettings.FilePath, "UI files not redirected; tests could write the player's settings.");
            Debug.Log("[DD-TEST] ui.isolation root=" + root);
        }

        /// <summary>The player's real UI files (name, SHA-256, write time; "absent" when missing).</summary>
        public static string RealFiles()
        {
            var text = new StringBuilder();
            using (SHA256 sha = SHA256.Create())
            {
                foreach (string name in FileNames)
                {
                    string path = Path.Combine(Application.persistentDataPath, name);
                    text.Append(name).Append(" = ");
                    if (File.Exists(path))
                    {
                        text.Append(System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty));
                        text.Append(" @ ").Append(File.GetLastWriteTimeUtc(path).Ticks);
                    }
                    else
                    {
                        text.Append("absent");
                    }

                    text.Append('\n');
                }
            }

            return text.ToString();
        }
    }
}
