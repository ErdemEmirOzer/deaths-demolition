using System.IO;
using DD.Prototype.Qa;
using DD.Prototype.Qa.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Qa.Tests.EditMode
{
    /// <summary>WP-026 · The spawn-mix QA slice runs WP-027's simulator and writes the gate report (violations == 0 on the shipped config).</summary>
    [Category("WP026")]
    public sealed class QaSpawnMixCliTests
    {
        [Test]
        public void SimulatorDilimi_DosyalarVeKapi()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "wp026-spawnmix-test");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            bool pass = QaSpawnMixCli.RunTo(dir, 26u, 10, 1000);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "spawn-mix.json")), "spawn-mix.json yok");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "spawn-mix.txt")), "spawn-mix.txt yok");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "qa-report.json")), "qa-report.json yok");
            QaReport report = QaReport.FromJson(File.ReadAllText(Path.Combine(dir, "qa-report.json")));
            StringAssert.Contains("\"schema\": \"dd-spawn-mix-v1\"", File.ReadAllText(Path.Combine(dir, "spawn-mix.json")));
            Assert.AreEqual("spawnmix.violations", report.gates[0].id);
            Debug.Log("[DD-TEST] wp026.spawnmix\n" + File.ReadAllText(Path.Combine(dir, "spawn-mix.txt")));
            Assert.IsTrue(pass, report.Summarize());
            Directory.Delete(dir, true);
        }
    }
}
