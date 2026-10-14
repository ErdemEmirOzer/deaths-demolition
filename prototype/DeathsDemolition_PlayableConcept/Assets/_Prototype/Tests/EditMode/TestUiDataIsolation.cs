using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests
{
    /// <summary>
    /// WP-068 user-data rule: EditMode tests never write the player's UI files.
    ///
    /// Before any EditMode test runs, this fixture moves every UI file (ui-settings.json,
    /// load-timing.json, ui-hints.json) to a process-local folder (the WP-051 dd-pid pattern).
    /// As a side effect, no test inherits the machine's saved settings. The fixture sits on the
    /// DD.Prototype.Tests namespace, which holds every test here (Unity's NUnit keeps only
    /// one namespace-less [SetUpFixture] per assembly). Nothing writes after an EditMode run,
    /// so OneTimeTearDown gives the Editor its real files back.
    /// </summary>
    [SetUpFixture]
    public sealed class TestUiDataIsolation
    {
        public static readonly string[] FileNames = { UiSettings.FileName, UiLoadTiming.FileName, UiHintState.FileName };

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string sandbox = System.Environment.GetEnvironmentVariable("DD_TEST_SANDBOX_ROOT");
            string root = string.IsNullOrEmpty(sandbox) ? UiDataPaths.ProcessLocalRoot : Path.Combine(sandbox, "ui");
            foreach (string name in FileNames)
            {
                string leftover = Path.Combine(root, name); // this process's own test files only
                if (File.Exists(leftover))
                {
                    File.Delete(leftover);
                }
            }

            if (string.IsNullOrEmpty(sandbox)) UiDataPaths.UseProcessLocalRoot();
            else UiDataPaths.SetRootOverride(root);
            Assert.AreEqual(Path.Combine(root, UiSettings.FileName), UiSettings.FilePath, "UI files not redirected; tests could write the player's settings.");
            Debug.Log("[DD-TEST] ui.isolation root=" + root);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            UiDataPaths.SetRootOverride(null);
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
