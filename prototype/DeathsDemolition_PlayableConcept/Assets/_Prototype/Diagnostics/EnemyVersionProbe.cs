#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-010 · test-only enemy version / hybrid difficulty probe for the real Player.
    ///
    /// Drives one real run through the public flow API and records, in the Player rather
    /// than the Editor, what the package's definition of done asks for: no version before
    /// its introduction round, at least one guaranteed debut in the introduction round
    /// (never the round's first enemy), the debut forced into a pool of two, a non-zero
    /// share of every introduced version afterwards, an injected DEC-021 row applied to
    /// real spawns (armour chance, tier weight, forced count) and the formula back once the
    /// row is gone, the same seed producing the same composition, a living enemy's stat
    /// profile untouched by a round boundary, a mid-round death dropping the owed debut
    /// and the next run starting clean, tier kill statistics and the gold version bonus
    /// agreeing with the economy, and the subscriber / static counts of run 1 equalling run
    /// 2 with one director and one selector. Nothing here changes product behaviour: it
    /// calls the same public methods the F1 panel and the screens call, and every round it
    /// plays is exhausted through the real director, the real pool and the real ledger.
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddEnemyVersionProbe</c> a development
    /// Player allocates nothing either. The profile path is redirected to the isolated file
    /// the runner passes, so the real profile is never touched.
    ///
    /// Command line:
    ///   -ddEnemyVersionProbe &lt;outDir&gt;   enable; write stage JSON files into outDir, then quit
    ///   -ddProfilePath &lt;file&gt;           redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class EnemyVersionProbe
    {
        private const string OutFlag = "-ddEnemyVersionProbe";
        private const string ProfileFlag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(Arg(OutFlag)))
            {
                return;
            }

            string profilePath = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[EnemyVersionProbe] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            var carrier = new GameObject("EnemyVersionProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [Serializable]
        public sealed class Stage
        {
            public string stage;
            public string utc;
            public string scene;
            public bool pass;
            public string note;
            public string detail;
            public int runId;
            public int round;
            public int poolTotal;
            public int spawnedTotal;
            public int standard;
            public int special;
            public int elite;
            public int ultra;
            public int guaranteedFlags;
            public int pendingObligations;
            public int shortfall;
            public int ignoredForcedCounts;
            public string composition;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 30f;
            private const float RefillStep = 0.45f;   // = config.Rounds.SpawnRefillInterval (read at runtime; this is the fallback)
            private string _outDir;
            private readonly List<Stage> _stages = new List<Stage>();
            private readonly List<EnemySpawnContext> _spawns = new List<EnemySpawnContext>(256);
            private readonly List<bool> _helmets = new List<bool>(256);
            private EnemySpawnDirector _director;
            private RunDirector _run;
            private PlayerRig _rig;
            private PrototypeConfig _shipped;
            private PrototypeConfig _clone;

            internal void Configure(string outDir)
            {
                _outDir = outDir;
            }

            private void OnSpawned(EnemySpawnContext c)
            {
                _spawns.Add(c);
                bool helmet = false;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket == c.Ticket)
                    {
                        helmet = live[i].Armor != null && live[i].Armor.IsEquipped(ArmorSlot.Helmet);
                        break;
                    }
                }

                _helmets.Add(helmet);
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null &&
                                             WeaponRegistry.ActiveController != null && RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");
                Bind();
                Dictionary<string, int> subsRun1 = SubscriberSnapshot();
                _shipped = PrototypeConfig.Active;
                float refill = _shipped != null ? _shipped.Rounds.SpawnRefillInterval : RefillStep;
                int introSpecial = Intro(EnemyTier.Special), introElite = Intro(EnemyTier.Elite), introUltra = Intro(EnemyTier.Ultra);

                // ---------------------------------------------------------------- 01: no version before its introduction round
                bool preOk = _shipped != null && _shipped.RoundDifficultyOverrides.Count == 0;
                var preDetail = new System.Text.StringBuilder();
                foreach (EnemyTier tier in new[] { EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra })
                {
                    int r = Intro(tier) - 1;
                    yield return ExhaustRound(r, new SeededTierRandom(1000u + (uint)r), refill);
                    int n = Count(tier);
                    preOk &= n == 0 && _director.RemainingInPool == 0;
                    preDetail.Append(tier).Append("@R").Append(r).Append("=").Append(n).Append("/").Append(_spawns.Count).Append(' ');
                }

                Stage s1 = Capture("01-no-version-before-introduction", "1");
                s1.pass = preOk;
                s1.detail = preDetail.ToString() + "shippedOverrideRows=" + (_shipped != null ? _shipped.RoundDifficultyOverrides.Count : -1);
                s1.note = "the round before each introduction round is exhausted through the real director: the version never spawns";
                Write(s1);

                // ---------------------------------------------------------------- 02: introduction round guarantees the debut, never as the first enemy (N repeats)
                bool introOk = true;
                int notFirst = 0, pacedDebuts = 0, chancePaid = 0, lateDebuts = 0;
                var introDetail = new System.Text.StringBuilder();
                (EnemyTier tier, uint seed)[] plan = { (EnemyTier.Special, 21u), (EnemyTier.Elite, 22u), (EnemyTier.Ultra, 23u), (EnemyTier.Ultra, 24u), (EnemyTier.Ultra, 25u) };
                for (int k = 0; k < plan.Length; k++)
                {
                    int r = Intro(plan[k].tier);
                    yield return ExhaustRound(r, new SeededTierRandom(plan[k].seed), refill);
                    int n = Count(plan[k].tier);
                    int g = GuaranteedCount(plan[k].tier);
                    int firstIndex = FirstIndex(plan[k].tier);
                    int debutIndex = GuaranteedIndex(plan[k].tier);
                    // The owed debut is released once the pacing share of the pool is spent
                    // (index >= ceil(pool * fraction), the production fraction read from the
                    // director); a WEIGHTED draw may pick the version earlier (R3: ≈ 15 %) and
                    // pays the debt by chance (flagged, ADR-010-2). Verdict, per repeat: the flag
                    // never lands AFTER the pacing release (lateDebuts == 0); across repeats: at
                    // least one flag lands exactly on the release (pacing observed) and at least
                    // 3 of 5 repeats do not open with the version ("not always first").
                    int releaseIndex = Mathf.CeilToInt(_spawns.Count * _director.GuaranteedIntroPoolFraction);
                    bool ok = n >= 1 && g == 1 && _director.TierSelector.PendingObligations == 0 && _director.TierSelector.Policy.ShortfallCount == 0;
                    if (firstIndex > 0) notFirst++;
                    if (debutIndex == releaseIndex) pacedDebuts++;
                    else if (debutIndex >= 0 && debutIndex < releaseIndex) chancePaid++;
                    else lateDebuts++;
                    introOk &= ok;
                    introDetail.Append(plan[k].tier).Append("@R").Append(r).Append(" seed=").Append(plan[k].seed).Append(" n=").Append(n).Append(" guaranteed=").Append(g).Append(" firstIndex=").Append(firstIndex).Append(" debutIndex=").Append(debutIndex).Append("/").Append(_spawns.Count).Append(" release=").Append(releaseIndex).Append(ok ? " " : "! ");
                }

                introOk &= notFirst >= 3 && pacedDebuts >= 1 && lateDebuts == 0 && pacedDebuts + chancePaid == plan.Length;
                Stage s2 = Capture("02-introduction-round-guaranteed-not-first", "2");
                s2.pass = introOk;
                s2.detail = introDetail.ToString() + "notFirst=" + notFirst + "/" + plan.Length + " paced=" + pacedDebuts + " chancePaid=" + chancePaid + " late=" + lateDebuts + " fraction=" + _director.GuaranteedIntroPoolFraction;
                s2.note = "each introduction round (5 repeats: Special, Elite, Ultra x3 seeds) spawns the version at least once and flags exactly one guaranteed debut; no flag lands after the pacing release index ceil(pool*fraction), at least one lands exactly on it (pacing observed) and the rest were paid early by the weighted draw; the debut is not always the round's first enemy (>= 3 of 5 repeats later)";
                Write(s2);

                // ---------------------------------------------------------------- 03: a pool of two still delivers the debut (clone config, formula pool = 2)
                _clone = UnityEngine.Object.Instantiate(_shipped);
                _clone.Rounds.PoolBase = 2f; _clone.Rounds.PoolPerRound = 0f; _clone.Rounds.PoolPowerScale = 0f;
                PrototypeConfig.SetActive(_clone);
                yield return null;
                yield return ExhaustRound(introSpecial, new SeededTierRandom(31u), refill);
                Stage s3 = Capture("03-small-pool-forces-debut", "3");
                s3.pass = _director.Population.SpawnedTotal == 2 && Count(EnemyTier.Special) == 1 && GuaranteedCount(EnemyTier.Special) == 1 &&
                          _director.TierSelector.Policy.ShortfallCount == 0 && _director.TierSelector.PendingObligations == 0;
                s3.detail = "pool=" + _director.Population.SpawnedTotal + " special=" + Count(EnemyTier.Special) + " guaranteed=" + GuaranteedCount(EnemyTier.Special) + " composition=" + Composition();
                s3.note = "with the round pool shrunk to two (clone config), the Special debut is forced into it; no shortfall";
                Write(s3);
                PrototypeConfig.SetActive(_shipped);
                UnityEngine.Object.Destroy(_clone); _clone = null;
                yield return null;

                // ---------------------------------------------------------------- 04: after every introduction, every version has a non-zero share
                int distRound = introUltra + 4;
                yield return ExhaustRound(distRound, new SeededTierRandom(41u), refill);
                Stage s4 = Capture("04-weighted-distribution-all-versions", "4");
                s4.pass = Count(EnemyTier.Standard) > 0 && Count(EnemyTier.Special) > 0 && Count(EnemyTier.Elite) > 0 && Count(EnemyTier.Ultra) > 0 &&
                          _director.TierSelector.PendingObligations == 0;
                s4.detail = "round=" + distRound + " pool=" + _spawns.Count + " weights S/Sp/E/U=" + _shipped.TierSpawnWeight(EnemyTier.Standard, distRound) + "/" +
                            _shipped.TierSpawnWeight(EnemyTier.Special, distRound) + "/" + _shipped.TierSpawnWeight(EnemyTier.Elite, distRound) + "/" + _shipped.TierSpawnWeight(EnemyTier.Ultra, distRound);
                s4.note = "a round past every introduction, exhausted with a seeded source: each of the four versions appears (weights are the shipped PrototypeOnly values)";
                Write(s4);

                // ---------------------------------------------------------------- 05: injected DEC-021 row on real spawns, then the formula back
                int ovRound = introUltra + 1;
                _clone = UnityEngine.Object.Instantiate(_shipped);
                var row = new RoundDifficultyOverride
                {
                    Round = ovRound,
                    OverrideArmorChances = true, ArmorChances = new ArmorChanceSettings { HelmetChance = 1f, BodyChance = 0f, LegChance = 0f, ChanceGrowthPerRound = 0f, MaxChance = 1f },
                    OverrideTierWeights = true, TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = EnemyTier.Special, Weight = 0f }, new TierWeightOverride { Tier = EnemyTier.Elite, Weight = 0f } },
                    OverrideForcedCounts = true, ForcedTierCounts = new List<TierCountOverride> { new TierCountOverride { Tier = EnemyTier.Elite, Count = 4 } }
                };
                bool accepted = _clone.ReplaceRoundDifficultyOverrides(new[] { row }, out List<string> failures);
                PrototypeConfig.SetActive(_clone);
                yield return null;
                yield return ExhaustRound(ovRound, new SeededTierRandom(51u), refill);
                int helmetsOn = 0; for (int i = 0; i < _helmets.Count; i++) if (_helmets[i]) helmetsOn++;
                int eliteWithRow = Count(EnemyTier.Elite), specialWithRow = Count(EnemyTier.Special), spawnsWithRow = _spawns.Count;
                bool rowOk = accepted && failures.Count == 0 && eliteWithRow == 4 && specialWithRow == 0 && helmetsOn == spawnsWithRow &&
                             _director.TierSelector.Policy.ShortfallCount == 0 && _shipped.RoundDifficultyOverrides.Count == 0;
                bool refusedWhileActive = !_clone.ReplaceRoundDifficultyOverrides(null, out _);   // the active asset refuses (Y-1 hardening)
                PrototypeConfig.SetActive(_shipped);
                yield return null;
                bool removed = _clone.ReplaceRoundDifficultyOverrides(null, out _);
                bool formulaBack = refusedWhileActive && removed && Mathf.Abs(_clone.TierSpawnWeight(EnemyTier.Special, ovRound) - _shipped.TierSpawnWeight(EnemyTier.Special, ovRound)) < 1e-5f &&
                                   Mathf.Abs(_clone.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, ovRound) - _shipped.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, ovRound)) < 1e-6f &&
                                   _clone.ForcedTierCount(EnemyTier.Elite, ovRound) == 0;
                yield return ExhaustRound(ovRound, new SeededTierRandom(51u), refill);   // shipped asset active: the formula's weights
                int specialAfter = Count(EnemyTier.Special);
                Stage s5 = Capture("05-injected-override-row-applied-then-formula", "5");
                s5.pass = rowOk && formulaBack && specialAfter > 0;
                s5.detail = "round=" + ovRound + " accepted=" + accepted + " failures=" + failures.Count + " | withRow: spawns=" + spawnsWithRow + " helmets=" + helmetsOn + " elite=" + eliteWithRow + " special=" + specialWithRow +
                            " | refusedWhileActive=" + refusedWhileActive + " rowRemoved=" + removed + " formulaBack=" + formulaBack + " specialAfter=" + specialAfter + " shippedRows=" + _shipped.RoundDifficultyOverrides.Count;
                s5.note = "a row injected into a clone (helmet chance 1, Special weight 0, Elite weight 0 + forced 4) shapes real spawns: every body wears a helmet, no Special, exactly four Elite (the forced count alone, no weighted draw); the shipped asset stays empty; with the row removed the weights, chances and forced counts are the formula's again and Special returns";
                Write(s5);
                UnityEngine.Object.Destroy(_clone); _clone = null;
                yield return null;

                // ---------------------------------------------------------------- 06: same seed, same composition
                yield return ExhaustRound(introUltra, new SeededTierRandom(0xC0FFEEu), refill);
                string compA = Composition();
                yield return ExhaustRound(introUltra, new SeededTierRandom(0xC0FFEEu), refill);
                string compB = Composition();
                yield return ExhaustRound(introUltra, new SeededTierRandom(0xBEEFu), refill);
                string compC = Composition();
                Stage s6 = Capture("06-same-seed-same-composition", "6");
                s6.pass = compA == compB && compA.Length == _director.Population.SpawnedTotal && compA != compC;
                s6.detail = "round=" + introUltra + " lenA=" + compA.Length + " A==B=" + (compA == compB) + " A!=C=" + (compA != compC) + " A=" + compA;
                s6.note = "the real director with a seeded source produces the identical tier sequence twice for the same round; a different seed differs";
                Write(s6);

                // ---------------------------------------------------------------- 07: a living enemy's stat profile is not touched by a round boundary
                _director.ForceActiveTarget(0, false);
                _run.BeginRound(introElite);
                yield return null;
                ZombieEnemy probeBody = EnemyFactory.Spawn(EnemyTier.Elite, introElite, _rig.transform.position + new Vector3(0f, 0f, 8f), Quaternion.identity);
                EnemyStatSnapshot before = probeBody != null ? probeBody.Stats : default;
                float healthBefore = probeBody != null ? probeBody.Health : -1f;
                _run.BeginRound(introElite + 3);
                yield return null;
                yield return null;
                bool statOk = probeBody != null && probeBody.IsAlive && before.IsValid && probeBody.Stats.SameProfileAs(in before) && probeBody.SpawnRound == introElite &&
                              Mathf.Approximately(probeBody.Health, healthBefore) && Mathf.Abs(before.MaxHealth - _shipped.EnemyHealth(EnemyTier.Elite, introElite)) < 1e-3f &&
                              Mathf.Abs(_shipped.EnemyHealth(EnemyTier.Elite, introElite + 3) - probeBody.MaxHealth) > 1e-3f;
                Stage s7 = Capture("07-stat-snapshot-frozen-across-round-boundary", "7");
                s7.pass = statOk;
                s7.detail = "eliteSpawnRound=" + introElite + " maxHealth=" + before.MaxHealth + " nowRound=" + (introElite + 3) + " healthAtNewRound=" + _shipped.EnemyHealth(EnemyTier.Elite, introElite + 3) +
                            " alive=" + (probeBody != null && probeBody.IsAlive) + " sameProfile=" + (probeBody != null && probeBody.Stats.SameProfileAs(in before));
                s7.note = "an Elite priced at its spawn round keeps that profile (health, damage, speed, timings) after the run moves three rounds on";
                Write(s7);
                if (probeBody != null) probeBody.Remove();
                yield return null;

                // ---------------------------------------------------------------- 08: death mid-round drops the owed debut; the next run starts clean
                _director.ForceActiveTarget(0, false);
                _run.BeginRound(introUltra);
                yield return null;
                int owedBefore = _director.TierSelector.PendingGuaranteed;
                _rig.Health.DiagnosisInvulnerable = false;
                _run.EndRun(RunEndReason.PlayerDeath, "probe");
                yield return null;
                int owedAfter = _director.TierSelector.PendingObligations;
                RunPhase phaseAfter = _run.Phase;
                int runIdOld = _run.RunId;
                // Second run through the real flow (death → results → menu → selection → run).
                yield return WaitForScene(PrototypeScenes.Results);
                yield return null;
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting && RunDirector.Instance.RunId != runIdOld, "second run Fighting");
                Bind();
                _shipped = PrototypeConfig.Active;
                Stage s8 = Capture("08-death-mid-round-drops-debt-second-run-clean", "8");
                s8.pass = owedBefore == 1 && owedAfter == 0 && phaseAfter == RunPhase.Dead && _director.Round == 1 && _director.TierSelector.PendingObligations == 0 &&
                          _director.TierSelector.Policy.ShortfallCount == 0 && _run.RunId > runIdOld;
                s8.detail = "owedBefore=" + owedBefore + " owedAfter=" + owedAfter + " phaseAfter=" + phaseAfter + " run " + runIdOld + "->" + _run.RunId + " round=" + _director.Round;
                s8.note = "dying in the Ultra introduction round drops the owed debut immediately; the next run (real death → results → menu → run flow) begins at round 1 with nothing owed";
                Write(s8);

                // ---------------------------------------------------------------- 09: tier kill statistics and the gold version bonus agree with the economy
                _director.ForceActiveTarget(0, false);
                _run.BeginRound(introUltra);
                yield return null;
                _director.StopRound();
                EnemyFactory.ClearAll();
                yield return null;
                var deaths = new List<EnemyDeathInfo>();
                Action<EnemyDeathInfo> onDied = d => deaths.Add(d);
                DamageEvents.EnemyDied += onDied;
                int[] plannedKills = { 3, 2, 2, 1 };
                EnemyTier[] tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
                int killsBefore = _run.Stats.TotalKills;
                for (int t = 0; t < tiers.Length; t++)
                {
                    for (int k = 0; k < plannedKills[t]; k++)
                    {
                        ZombieEnemy z = EnemyFactory.Spawn(tiers[t], introUltra, _rig.transform.position + new Vector3(3f + k, 0f, 6f + t), Quaternion.identity);
                        if (z != null) z.Kill();
                    }
                }

                yield return null;
                DamageEvents.EnemyDied -= onDied;
                bool killsOk = deaths.Count == 8 && _run.Stats.TotalKills - killsBefore == 8;
                bool goldOk = deaths.Count == 8;
                for (int t = 0; t < tiers.Length; t++) killsOk &= _run.Stats.KillsFor(tiers[t]) >= plannedKills[t];
                int attributed = _run.Stats.KillsFor(EnemyTier.Standard) + _run.Stats.KillsFor(EnemyTier.Special) + _run.Stats.KillsFor(EnemyTier.Elite) + _run.Stats.KillsFor(EnemyTier.Ultra);
                killsOk &= attributed == _run.Stats.TotalKills;
                var goldDetail = new System.Text.StringBuilder();
                for (int i = 0; i < deaths.Count; i++)
                {
                    EnemyDeathInfo d = deaths[i];
                    int expectedVersion = GoldRewardCalculator.VersionBonus(d.Tier, _shipped);
                    bool ok = d.Reward.IsValid && d.Reward.Value.VariantContribution == expectedVersion && d.Reward.Value.Base == GoldRewardCalculator.BaseReward(_shipped) &&
                              d.Reward.Value.ArmorContribution == GoldRewardCalculator.ArmorBonus(d.Reward.Armor, _shipped) && d.GoldReward == d.Reward.Total;
                    goldOk &= ok;
                    goldDetail.Append(d.Tier).Append('=').Append(d.Reward.Value.Base).Append('+').Append(d.Reward.Value.VariantContribution).Append('+').Append(d.Reward.Value.ArmorContribution).Append(ok ? " " : "! ");
                }

                Stage s9 = Capture("09-tier-kill-stats-and-gold-version-bonus", "9");
                s9.pass = killsOk && goldOk;
                s9.detail = "kills S/Sp/E/U=" + _run.Stats.KillsFor(EnemyTier.Standard) + "/" + _run.Stats.KillsFor(EnemyTier.Special) + "/" + _run.Stats.KillsFor(EnemyTier.Elite) + "/" + _run.Stats.KillsFor(EnemyTier.Ultra) +
                            " total=" + _run.Stats.TotalKills + " attributed=" + attributed + " | gold " + goldDetail;
                s9.note = "3/2/2/1 kills by version: the run statistics count each version under its enum and the total equals the attributed sum (no unattributed kills); every death carries base + version bonus + spawn armour bonus exactly as the economy computes them";
                Write(s9);

                // ---------------------------------------------------------------- 10: subscriber and static counts run 1 = run 2; one director, one selector
                Dictionary<string, int> subsRun2 = SubscriberSnapshot();
                var diff = new System.Text.StringBuilder();
                bool subsOk = true;
                foreach (KeyValuePair<string, int> kv in subsRun1)
                {
                    int now = subsRun2.TryGetValue(kv.Key, out int v) ? v : -1;
                    if (now != kv.Value) { subsOk = false; diff.Append(kv.Key).Append(':').Append(kv.Value).Append("->").Append(now).Append(' '); }
                }

                int directors = UnityEngine.Object.FindObjectsOfType<EnemySpawnDirector>(false).Length;
                int runDirectors = UnityEngine.Object.FindObjectsOfType<RunDirector>(false).Length;
                Stage s10 = Capture("10-subscribers-and-statics-run1-equals-run2", "10");
                bool bridgeOk = DD.Prototype.Core.EnemyTierAuthorityBridge.IsBound && DD.Prototype.Core.EnemyTierAuthorityBridge.CurrentRound() == _director.Round;
                s10.pass = subsOk && directors == 1 && runDirectors == 1 && ReferenceEquals(_director.TierSelector, EnemySpawnDirector.Instance.TierSelector) &&
                           _director.TierSelector.Policy.IgnoredForcedCounts == 0 && bridgeOk;
                s10.detail = "watched=" + subsRun1.Count + " diffs=[" + diff + "] directors=" + directors + " runDirectors=" + runDirectors + " shortfallAllRuns=" + _director.TierSelector.Policy.ShortfallCount +
                             " bridgeBound=" + bridgeOk + " f1Schedule='" + DD.Prototype.Core.EnemyTierAuthorityBridge.Describe() + "'";
                s10.note = "every watched static event has the same subscriber count in run 2 as in run 1; exactly one spawn director, one run director and one tier selector exist";
                Write(s10);

                // summary
                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "EnemyVersionProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[EnemyVersionProbe] DONE stages=" + _stages.Count + " pass=" + passCount);

                Unbind();
                EnemyFactory.ClearAll();
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.Quit();
            }

            // ------------------------------------------------------------------ helpers

            private void Bind()
            {
                Unbind();
                _run = RunDirector.Instance;
                _director = EnemySpawnDirector.Instance;
                _rig = PlayerRegistry.Primary;
                _rig.Health.DiagnosisInvulnerable = true;
                _director.EnemySpawned += OnSpawned;
            }

            private void Unbind()
            {
                if (_director != null)
                {
                    _director.EnemySpawned -= OnSpawned;
                    _director.TierSelector.SetRandom(null);
                }
            }

            private int Intro(EnemyTier tier) => RoundDifficultyOverrideRules.IntroductionRoundOf(PrototypeConfig.Active, tier);

            /// <summary>Begin <paramref name="round"/> with the given source and exhaust its pool through the real director by killing everything each frame (accelerated frame step).</summary>
            private IEnumerator ExhaustRound(int round, IEnemyTierRandom random, float refill)
            {
                _spawns.Clear();
                _helmets.Clear();
                RunFlowClock.ClearHolds();
                _director.ForceActiveTarget(0, false);
                _director.TierSelector.SetRandom(random);
                _run.BeginRound(round);
                yield return null;
                Time.captureDeltaTime = refill;
                float t0 = Time.realtimeSinceStartup;
                while (_director.RemainingInPool > 0 && Time.realtimeSinceStartup - t0 < 90f)
                {
                    KillAllLiving();
                    yield return null;
                }

                Time.captureDeltaTime = 0f;
                _director.TierSelector.SetRandom(null);
                yield return null;
            }

            private static int KillAllLiving()
            {
                int n = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (live[i] != null && live[i].IsAlive) { live[i].Kill(); n++; }
                }

                return n;
            }

            private int Count(EnemyTier tier)
            {
                int n = 0;
                for (int i = 0; i < _spawns.Count; i++) if (_spawns[i].Tier == tier) n++;
                return n;
            }

            private int GuaranteedCount(EnemyTier tier)
            {
                int n = 0;
                for (int i = 0; i < _spawns.Count; i++) if (_spawns[i].Tier == tier && _spawns[i].GuaranteedIntroduction) n++;
                return n;
            }

            private int FirstIndex(EnemyTier tier)
            {
                for (int i = 0; i < _spawns.Count; i++) if (_spawns[i].Tier == tier) return i;
                return -1;
            }

            private int GuaranteedIndex(EnemyTier tier)
            {
                for (int i = 0; i < _spawns.Count; i++) if (_spawns[i].Tier == tier && _spawns[i].GuaranteedIntroduction) return i;
                return -1;
            }

            private string Composition()
            {
                var sb = new System.Text.StringBuilder(_spawns.Count);
                for (int i = 0; i < _spawns.Count; i++) sb.Append((int)_spawns[i].Tier);
                return sb.ToString();
            }

            private static readonly (Type Type, string Name)[] s_watchedEvents =
            {
                (typeof(RunEvents), "RunStarted"), (typeof(RunEvents), "RoundStarted"), (typeof(RunEvents), "RoundCompleted"), (typeof(RunEvents), "RoundPoolExhausted"),
                (typeof(RunEvents), "GoldChanged"), (typeof(RunEvents), "PhaseChanged"), (typeof(RunEvents), "RunEnded"), (typeof(RunFlowClock), "FrozenChanged"),
                (typeof(ProfileService), "Changed"), (typeof(WeaponRegistry), "ActiveChanged"), (typeof(PlayerHealth), "AnyPlayerDied"),
                (typeof(DamageEvents), "ArmorBroken"), (typeof(DamageEvents), "EnemyDamaged"), (typeof(DamageEvents), "EnemyDied"), (typeof(DamageEvents), "PlayerAttacked"),
                (typeof(WeaponEvents), "SpecialWeaponSpawned"), (typeof(WeaponEvents), "SpecialWeaponEnded"), (typeof(SpecialWeaponRoundBridge), "RoundStarted"),
                (typeof(PrototypeConfig), "ActiveChanged"),
            };

            private static Dictionary<string, int> SubscriberSnapshot()
            {
                var result = new Dictionary<string, int>();
                for (int i = 0; i < s_watchedEvents.Length; i++)
                {
                    FieldInfo field = s_watchedEvents[i].Type.GetField(s_watchedEvents[i].Name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var d = field != null ? field.GetValue(null) as Delegate : null;
                    if (field == null)
                    {
                        throw new InvalidOperationException("watched static event missing: " + s_watchedEvents[i].Type.Name + "." + s_watchedEvents[i].Name + " (the subscriber table is stale)");
                    }

                    result[s_watchedEvents[i].Type.Name + "." + s_watchedEvents[i].Name] = d == null ? 0 : d.GetInvocationList().Length;
                }

                return result;
            }

            private Stage Capture(string stage, string kit)
            {
                TierSelectionPolicy policy = _director != null ? _director.TierSelector.Policy : null;
                return new Stage
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    runId = _run != null ? _run.RunId : 0,
                    round = _director != null ? _director.Round : 0,
                    poolTotal = policy != null ? policy.PoolTotal : 0,
                    spawnedTotal = _director != null ? _director.Population.SpawnedTotal : 0,
                    standard = Count(EnemyTier.Standard),
                    special = Count(EnemyTier.Special),
                    elite = Count(EnemyTier.Elite),
                    ultra = Count(EnemyTier.Ultra),
                    guaranteedFlags = GuaranteedCount(EnemyTier.Special) + GuaranteedCount(EnemyTier.Elite) + GuaranteedCount(EnemyTier.Ultra),
                    pendingObligations = policy != null ? policy.PendingObligations : 0,
                    shortfall = policy != null ? policy.ShortfallCount : 0,
                    ignoredForcedCounts = policy != null ? policy.IgnoredForcedCounts : 0,
                    composition = Composition()
                };
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " | " + _stages[i].detail;
                }
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[EnemyVersionProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.detail);
            }

            private static IEnumerator WaitForScene(string name)
            {
                yield return WaitUntil(
                    () => SceneManager.GetActiveScene().name == name && SceneManager.GetActiveScene().isLoaded,
                    "scene '" + name + "' not active");
            }

            private static IEnumerator WaitUntil(Func<bool> condition, string what)
            {
                float start = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - start > SceneTimeout)
                    {
                        Debug.LogError("[EnemyVersionProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        Application.Quit(3);
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
