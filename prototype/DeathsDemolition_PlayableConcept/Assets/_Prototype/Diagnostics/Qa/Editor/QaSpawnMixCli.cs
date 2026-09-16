using System;
using System.IO;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Qa;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Qa.EditorTools
{
    /// <summary>
    /// WP-026 · Batchmode entry for the spawner mix statistic. Runs WP-027's
    /// <see cref="SpawnMixSimulator"/> (the stable API: <c>Run(Options) → Report</c>, JSON
    /// <c>dd-spawn-mix-v1</c>) and adds the QA gate slice: violations == 0 (README ceilings on
    /// the free draw, forbidden pairs, armour exclusions, DEC-022 guarantee), notes reported.
    ///
    /// <c>Unity -batchmode -nographics -quit -executeMethod DD.Prototype.Qa.EditorTools.QaSpawnMixCli.Run
    ///   -ddOut &lt;dir&gt; [-ddSeed 26] [-ddRounds 10] [-ddSpawns 1000]</c>
    /// </summary>
    public static class QaSpawnMixCli
    {
        public static void Run()
        {
            string outDir = QaArgs.Arg("-ddOut");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.LogError("[QaSpawnMixCli] -ddOut missing");
                EditorApplication.Exit(2);
                return;
            }

            int exit = 1;
            try
            {
                exit = RunTo(outDir, (uint)QaArgs.ArgInt("-ddSeed", 26, 0, int.MaxValue), QaArgs.ArgInt("-ddRounds", 10, 1, 50), QaArgs.ArgInt("-ddSpawns", 1000, 10, 100000)) ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[QaSpawnMixCli] " + e);
            }

            EditorApplication.Exit(exit);
        }

        /// <summary>Returns the gate verdict; writes spawn-mix.json (dd-spawn-mix-v1), spawn-mix.txt and qa-report.json/.txt.</summary>
        public static bool RunTo(string outDir, uint seed, int rounds, int spawnsPerRound)
        {
            Directory.CreateDirectory(outDir);
            var options = new SpawnMixSimulator.Options { seed = seed, firstRound = 1, lastRound = rounds, spawnsPerRound = spawnsPerRound };
            SpawnMixSimulator.Report r = SpawnMixSimulator.RunAndWrite(options, Path.Combine(outDir, "spawn-mix.json"));
            string summary = SpawnMixSimulator.Summarize(r);
            File.WriteAllText(Path.Combine(outDir, "spawn-mix.txt"), summary);

            QaReport report = QaReport.Create("SpawnMix", (int)seed);
            report.Add("spawnmix.violations", r.violations.Length == 0, r.violations.Length.ToString(), "== 0",
                r.violations.Length == 0 ? "README ceilings (free draw), forbidden pairs, armour exclusions, DEC-022 guarantee" : string.Join(" | ", r.violations), "spawn-mix.json");
            report.Add("spawnmix.simulated", r.totalSpawns > 0 && r.rounds.Length == rounds, r.totalSpawns + " spawns / " + r.rounds.Length + " rounds",
                ">= " + spawnsPerRound + " × " + rounds, "look " + r.lookSimulated + ", armour variants " + r.armourVariantsSimulated, "spawn-mix.json");
            report.Add("spawnmix.notes", true, r.notes.Length.ToString(), "report only",
                r.notes.Length == 0 ? "" : string.Join(" | ", r.notes), "spawn-mix.json");
            report.summary = summary.Split('\n')[0];
            report.Write(outDir);
            Debug.Log("[QaSpawnMixCli] " + (report.pass ? "PASS" : "FAIL") + " " + report.summary);
            return report.pass;
        }
    }
}
