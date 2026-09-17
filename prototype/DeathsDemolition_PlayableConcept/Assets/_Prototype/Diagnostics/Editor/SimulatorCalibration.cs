using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.Telemetry;
using UnityEngine;

namespace DD.Prototype.Diagnostics.EditorTools
{
    /// <summary>
    /// WP-045 (ADR-045-1) · Simulator ↔ telemetry calibration. Reads every
    /// <c>dd-run-telemetry-v1</c> file of a folder, averages the runs per round, runs
    /// <see cref="EconomySimulator"/> for the same rounds (no purchases when the runs bought
    /// nothing — the QA bot skips the shop), and reports the deviation of the model from the
    /// measured runs: gold income, kill mix, TTK per tier, round time, damage taken. It also
    /// derives the model parameters that would close the gap (implied hit fraction from TTK,
    /// implied engaged fraction from damage taken) — as a REPORT, never by editing data.
    /// </summary>
    public static class SimulatorCalibration
    {
        public const string Schema = "dd-calibration-v1";

        [Serializable]
        public sealed class RoundComparison
        {
            public int round;
            public int runs;                       // telemetry runs that reached this round
            public float realGoldIncome, simGoldIncome, goldDeviationPct;
            public float realKills, simSpawns, killsDeviationPct;
            public float[] realTierShare = new float[4];
            public float[] simTierShare = new float[4];
            public float realArmouredShare, simArmouredShare;
            public float realTtkStandard, simTtkStandard, ttkStandardDeviationPct;
            public float realTtkAll, simTtkExpectedAll;
            public float realSeconds, simClearSeconds, secondsDeviationPct;
            public float realDamageTaken, simDamageBudgetRisk;   // sim: riskProxy (damage / budget); real: damage / (max health), death rounds included
            public float realRiskProxy;
            public int runsReachingRound;                        // runs that entered this round (completed or died in it)
            public int deathsInRound;
        }

        [Serializable]
        public sealed class Report
        {
            public string schema = Schema;
            public string utc;
            public string weapon;
            public int runsRead, runsUsed;
            public int roundsCompared;
            public bool purchasesObserved;
            public float hitFractionUsed, engagedFractionUsed;
            public float impliedHitFraction;        // hit × Σ simTTK / Σ realTTK (Standard, rounds with samples)
            public float impliedEngagedFraction;    // engaged × Σ realRisk / Σ simRisk (death rounds included)
            public float impliedPickupFraction;     // pickup × Σ realGold / Σ simGold (completed rounds), capped at 1
            public int firstDeathRoundReal;         // earliest round a run died in (0 = none)
            public float meanAbsGoldDeviationPct, meanAbsTtkDeviationPct, meanAbsSecondsDeviationPct;
            public RoundComparison[] rounds = Array.Empty<RoundComparison>();
            public string[] notes = Array.Empty<string>();
        }

        public static List<RunTelemetryDocument> Load(string folder)
        {
            var list = new List<RunTelemetryDocument>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
            foreach (string f in Directory.GetFiles(folder, "run-*.json"))
            {
                RunTelemetryDocument d = RunTelemetry.FromJson(File.ReadAllText(f));
                if (d != null) list.Add(d);
            }

            list.Sort((a, b) => string.CompareOrdinal(a.utc, b.utc));
            return list;
        }

        /// <summary>Compares the runs (same weapon expected; the first run's weapon selects the simulator track).</summary>
        public static Report Compare(List<RunTelemetryDocument> runs, EconomySimulator.Options simOptions)
        {
            var r = new Report { utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), runsRead = runs != null ? runs.Count : 0 };
            if (runs == null || runs.Count == 0) { r.notes = new[] { "no telemetry runs" }; return r; }

            string weapon = runs[0].weapon;
            var used = new List<RunTelemetryDocument>();
            bool anyPurchase = false;
            int maxRound = 0;
            foreach (RunTelemetryDocument d in runs)
            {
                if (d.weapon != weapon || d.rounds == null || d.rounds.Length == 0) continue;
                used.Add(d);
                anyPurchase |= d.upgradesPurchased > 0;
                foreach (RoundRecord rr in d.rounds) maxRound = Math.Max(maxRound, rr.round);
            }

            r.weapon = weapon; r.runsUsed = used.Count; r.purchasesObserved = anyPurchase;
            if (used.Count == 0 || maxRound == 0) { r.notes = new[] { "no completed rounds for weapon " + weapon }; return r; }

            EconomySimulator.Options o = simOptions ?? new EconomySimulator.Options();
            o.firstRound = 1; o.lastRound = maxRound; o.buyEveryRound = anyPurchase;
            ClassicWeaponId id;
            if (Enum.TryParse(weapon, out id)) o.weapons = new[] { id };
            EconomySimulator.Report sim = EconomySimulator.Run(o);
            r.hitFractionUsed = o.hitFraction; r.engagedFractionUsed = o.engagedFraction;
            EconomySimulator.WeaponTrack track = sim.weapons.Length > 0 ? sim.weapons[0] : null;
            if (track == null) { r.notes = new[] { "simulator has no track for " + weapon }; return r; }

            var rows = new List<RoundComparison>();
            float sumSimTtk = 0f, sumRealTtk = 0f, sumSimDmgRisk = 0f, sumRealRisk = 0f, sumSimGold = 0f, sumRealGold = 0f;
            float absGold = 0f, absTtk = 0f, absSec = 0f; int nGold = 0, nTtk = 0, nSec = 0; int firstDeath = 0;
            PrototypeConfig config = o.config != null ? o.config : PrototypeConfig.Active;
            float maxHealth = config != null ? config.Player.MaxHealth : 100f;
            for (int round = 1; round <= maxRound; round++)
            {
                var c = new RoundComparison { round = round };
                float gold = 0f, kills = 0f, sec = 0f, dmg = 0f, ttkStd = 0f, ttkAll = 0f;
                int ttkStdN = 0, ttkAllN = 0, armoured = 0;
                var tierKills = new float[4];
                foreach (RunTelemetryDocument d in used)
                {
                    foreach (RoundRecord rr in d.rounds)
                    {
                        if (rr.round != round) continue;
                        c.runsReachingRound++;
                        dmg += rr.damageTaken;                       // damage counts in every round entered — death rounds carry most of it
                        if (!rr.completed)
                        {
                            c.deathsInRound++;
                            if (firstDeath == 0 || round < firstDeath) firstDeath = round;
                            continue;
                        }

                        c.runs++;
                        gold += rr.goldIncome; sec += rr.seconds;
                        int k = rr.killsStandard + rr.killsSpecial + rr.killsElite + rr.killsUltra;
                        kills += k; armoured += rr.killsArmoured;
                        tierKills[0] += rr.killsStandard; tierKills[1] += rr.killsSpecial; tierKills[2] += rr.killsElite; tierKills[3] += rr.killsUltra;
                        ttkStd += rr.ttkSumStandard; ttkStdN += rr.ttkCountStandard;
                        ttkAll += rr.ttkSumStandard + rr.ttkSumSpecial + rr.ttkSumElite + rr.ttkSumUltra;
                        ttkAllN += rr.ttkCountStandard + rr.ttkCountSpecial + rr.ttkCountElite + rr.ttkCountUltra;
                    }
                }

                if (c.runsReachingRound == 0) continue;
                EconomySimulator.RoundRow s = null;
                foreach (EconomySimulator.RoundRow sr in track.rounds) if (sr.round == round) { s = sr; break; }
                if (s == null) continue;

                c.realGoldIncome = c.runs > 0 ? gold / c.runs : 0f; c.simGoldIncome = s.goldIncome;
                c.goldDeviationPct = Pct(c.simGoldIncome, c.realGoldIncome);
                c.realKills = c.runs > 0 ? kills / c.runs : 0f; c.simSpawns = s.pool; c.killsDeviationPct = Pct(c.simSpawns, c.realKills);
                float totalTier = tierKills[0] + tierKills[1] + tierKills[2] + tierKills[3];
                int simTotal = s.tierCounts[0] + s.tierCounts[1] + s.tierCounts[2] + s.tierCounts[3];
                for (int t = 0; t < 4; t++)
                {
                    c.realTierShare[t] = totalTier > 0 ? tierKills[t] / totalTier : 0f;
                    c.simTierShare[t] = simTotal > 0 ? s.tierCounts[t] / (float)simTotal : 0f;
                }

                c.realArmouredShare = kills > 0 ? armoured / kills : 0f;
                float simArm = 0f; for (int t = 0; t < 4; t++) simArm += s.armouredShare[t] * s.tierCounts[t];
                c.simArmouredShare = simTotal > 0 ? simArm / simTotal : 0f;
                c.realTtkStandard = ttkStdN > 0 ? ttkStd / ttkStdN : 0f; c.simTtkStandard = s.ttk[0].ttkExpected;
                c.ttkStandardDeviationPct = c.realTtkStandard > 0f ? Pct(c.simTtkStandard, c.realTtkStandard) : 0f;
                c.realTtkAll = ttkAllN > 0 ? ttkAll / ttkAllN : 0f;
                float wsum = 0f; for (int t = 0; t < 4; t++) wsum += s.ttk[t].ttkExpected * s.tierCounts[t];
                c.simTtkExpectedAll = simTotal > 0 ? wsum / simTotal : 0f;
                c.realSeconds = c.runs > 0 ? sec / c.runs : 0f; c.simClearSeconds = s.clearSeconds; c.secondsDeviationPct = Pct(c.simClearSeconds, c.realSeconds);
                c.realDamageTaken = dmg / c.runsReachingRound; c.simDamageBudgetRisk = s.riskProxy; c.realRiskProxy = maxHealth > 0f ? c.realDamageTaken / maxHealth : 0f;

                if (c.realGoldIncome > 0f) { absGold += Mathf.Abs(c.goldDeviationPct); nGold++; sumSimGold += c.simGoldIncome; sumRealGold += c.realGoldIncome; }
                if (c.realTtkStandard > 0f) { absTtk += Mathf.Abs(c.ttkStandardDeviationPct); nTtk++; sumSimTtk += c.simTtkStandard; sumRealTtk += c.realTtkStandard; }
                if (c.realSeconds > 0f) { absSec += Mathf.Abs(c.secondsDeviationPct); nSec++; }
                sumSimDmgRisk += c.simDamageBudgetRisk; sumRealRisk += c.realRiskProxy;
                rows.Add(c);
            }

            r.rounds = rows.ToArray(); r.roundsCompared = rows.Count;
            r.meanAbsGoldDeviationPct = nGold > 0 ? absGold / nGold : 0f;
            r.meanAbsTtkDeviationPct = nTtk > 0 ? absTtk / nTtk : 0f;
            r.meanAbsSecondsDeviationPct = nSec > 0 ? absSec / nSec : 0f;
            r.impliedHitFraction = sumRealTtk > 0f ? Mathf.Clamp(o.hitFraction * sumSimTtk / sumRealTtk, 0.05f, 1f) : o.hitFraction;
            r.impliedEngagedFraction = sumSimDmgRisk > 0f ? Mathf.Clamp(o.engagedFraction * sumRealRisk / sumSimDmgRisk, 0.01f, 1f) : o.engagedFraction;
            r.impliedPickupFraction = sumSimGold > 0f ? Mathf.Clamp(o.goldPickupFraction * sumRealGold / sumSimGold, 0.05f, 1f) : o.goldPickupFraction;
            r.firstDeathRoundReal = firstDeath;
            r.notes = new[]
            {
                anyPurchase ? "purchases observed: simulator ran the greedy card model" : "no purchases in telemetry (QA bot skips the shop): simulator ran with buyEveryRound = false; affordability is not calibrated here",
                "deviation % = (sim − real) / real; positive = the model over-estimates",
                "implied parameters are the single-factor values that would make Σ TTK / Σ damage match; a rerun with them is the calibration step, data values never change"
            };
            return r;
        }

        private static float Pct(float sim, float real)
        {
            return real > 0f ? (sim - real) / real * 100f : 0f;
        }

        public static string ToJson(Report r) => JsonUtility.ToJson(r, true);

        public static string ToMarkdown(Report r)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Calibration v1 — " + r.weapon + " · runs " + r.runsUsed + "/" + r.runsRead + " · rounds " + r.roundsCompared + " · purchases " + (r.purchasesObserved ? "yes" : "no"));
            sb.AppendLine();
            sb.AppendLine("Model in: hit " + r.hitFractionUsed.ToString("0.00", ic) + ", engaged " + r.engagedFractionUsed.ToString("0.00", ic) + " → implied hit **" + r.impliedHitFraction.ToString("0.00", ic) + "**, implied engaged **" + r.impliedEngagedFraction.ToString("0.00", ic) + "**, implied pickup **" + r.impliedPickupFraction.ToString("0.00", ic) + "**; mean |dev| gold " + r.meanAbsGoldDeviationPct.ToString("0", ic) + "%, TTK(Standard) " + r.meanAbsTtkDeviationPct.ToString("0", ic) + "%, round time " + r.meanAbsSecondsDeviationPct.ToString("0", ic) + "%; first death round (real) R" + r.firstDeathRoundReal);
            sb.AppendLine();
            sb.AppendLine("| R | runs (reached/deaths) | gold real/sim (dev%) | kills real / sim pool | tier share real → sim S/Sp/E/U | armoured real/sim | TTK Std real/sim (dev%) | TTK all real/sim | seconds real/sim (dev%) | dmg taken real | risk real/sim |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (RoundComparison c in r.rounds)
            {
                sb.Append("| ").Append(c.round).Append(" | ").Append(c.runs).Append(" (").Append(c.runsReachingRound).Append('/').Append(c.deathsInRound).Append(") | ")
                  .Append(c.realGoldIncome.ToString("0", ic)).Append('/').Append(c.simGoldIncome.ToString("0", ic)).Append(" (").Append(c.goldDeviationPct.ToString("+0;-0", ic)).Append("%) | ")
                  .Append(c.realKills.ToString("0", ic)).Append(" / ").Append(c.simSpawns.ToString("0", ic)).Append(" | ");
                for (int t = 0; t < 4; t++) sb.Append(Mathf.RoundToInt(c.realTierShare[t] * 100)).Append(t < 3 ? "/" : " → ");
                for (int t = 0; t < 4; t++) sb.Append(Mathf.RoundToInt(c.simTierShare[t] * 100)).Append(t < 3 ? "/" : " | ");
                sb.Append(Mathf.RoundToInt(c.realArmouredShare * 100)).Append("%/").Append(Mathf.RoundToInt(c.simArmouredShare * 100)).Append("% | ")
                  .Append(c.realTtkStandard.ToString("0.00", ic)).Append('/').Append(c.simTtkStandard.ToString("0.00", ic)).Append(" (").Append(c.ttkStandardDeviationPct.ToString("+0;-0", ic)).Append("%) | ")
                  .Append(c.realTtkAll.ToString("0.00", ic)).Append('/').Append(c.simTtkExpectedAll.ToString("0.00", ic)).Append(" | ")
                  .Append(c.realSeconds.ToString("0", ic)).Append('/').Append(c.simClearSeconds.ToString("0", ic)).Append(" (").Append(c.secondsDeviationPct.ToString("+0;-0", ic)).Append("%) | ")
                  .Append(c.realDamageTaken.ToString("0", ic)).Append(" | ")
                  .Append(c.realRiskProxy.ToString("0.00", ic)).Append('/').Append(c.simDamageBudgetRisk.ToString("0.00", ic)).AppendLine(" |");
            }

            sb.AppendLine();
            foreach (string n in r.notes) sb.AppendLine("> " + n);
            return sb.ToString();
        }

        /// <summary>Batch entry: <c>-executeMethod DD.Prototype.Diagnostics.EditorTools.SimulatorCalibration.RunFromCommandLine -ddTelemetryIn &lt;dir&gt; -ddOut &lt;dir&gt; [-ddHit f] [-ddEngaged f]</c>.</summary>
        public static void RunFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string inDir = null, outDir = null; float hit = 0.70f, engaged = 0.30f;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-ddTelemetryIn") inDir = args[i + 1];
                else if (args[i] == "-ddOut") outDir = args[i + 1];
                else if (args[i] == "-ddHit") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out hit);
                else if (args[i] == "-ddEngaged") float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out engaged);
            }

            if (string.IsNullOrEmpty(inDir) || string.IsNullOrEmpty(outDir)) throw new ArgumentException("-ddTelemetryIn <dir> -ddOut <dir> required");
            List<RunTelemetryDocument> runs = Load(inDir);
            Report r = Compare(runs, new EconomySimulator.Options { hitFraction = hit, engagedFraction = engaged });
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "calibration-v1.json"), ToJson(r));
            File.WriteAllText(Path.Combine(outDir, "calibration-v1.md"), ToMarkdown(r));
            Debug.Log("[Calibration] runs=" + r.runsUsed + " rounds=" + r.roundsCompared + " impliedHit=" + r.impliedHitFraction + " impliedEngaged=" + r.impliedEngagedFraction);
        }
    }
}
