#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-009 · development-only profile persistence probe for the real Player.
    ///
    /// Proves the profile contract across REAL process boundaries, in four launches
    /// that share one isolated profile path (never the real one):
    ///
    ///   phase A  (fresh profile)  01 a completed round grants round XP · 02 the unfinished
    ///            last round grants none · 03 its kills and kill XP are kept · 04 gold and
    ///            playtime grant no XP · 05 the same completion identity is not applied
    ///            twice · 09a new unlock/achievement is "new" once · 11 abort produces no
    ///            profile delta · 12a a second clean run applies; also records the
    ///            expected old/new profiles and an interrupted-save snapshot for C/D
    ///   phase B  (relaunch)       06 replaying A's last summary is refused · 08 level and
    ///            unlocks came back from disk · 09b the replay shows nothing "new" ·
    ///            12b a clean run in the new process applies independently
    ///   phase C  (interrupted save, primary present) 10a the OLD complete profile is used,
    ///            the unverified candidate is discarded
    ///   phase D  (interrupted save, primary missing) 10b the NEW complete profile is rolled
    ///            forward from the validated candidate
    ///
    /// Nothing here changes product behaviour: it drives the same public flow API the
    /// screens use and reads public state. Compiled ONLY into development builds and
    /// the Editor (file-level #if); a release Player contains none of it, and without
    /// <c>-ddProfileProbe</c> a development Player allocates nothing either.
    ///
    /// Command line:
    ///   -ddProfileProbe &lt;outDir&gt;       enable; stage JSON files go to outDir, then quit
    ///   -ddProfileProbePhase A|B|C|D    which launch this is (default A)
    ///   -ddProfilePath &lt;file&gt;          redirect the profile file (required; keeps the real profile untouched)
    /// </summary>
    public static class ProfilePersistenceProbe
    {
        private const string OutFlag = "-ddProfileProbe";
        private const string PhaseFlag = "-ddProfileProbePhase";
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
                Debug.Log("[ProfileProbe] profile path override -> " + profilePath);
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

            var carrier = new GameObject("ProfilePersistenceProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir, Arg(PhaseFlag) ?? "A");
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
            public string phase;
            public string utc;
            public string scene;
            public bool pass;
            public string note;
            public string detail;

            public int profileLevel;
            public int xpIntoLevel;
            public int totalXpEarned;
            public int runsPlayed;
            public int roundsCompleted;
            public int totalKills;
            public int unlockedWeapons;
            public int unlockedBranches;
            public int achievements;
            public long lastAppliedOrdinal;
            public string lineage;
            public string lastCompletionId;
            public string lastOutcome;
            public int committerEvents;
            public int committerCommits;
            public string loadStatus;
            public string loadRecovery;
            public bool saveLocked;
            public string ramHash;
            public string diskHash;
        }

        /// <summary>What phase A hands to B (JsonUtility needs a serialisable class, not the summary struct).</summary>
        [Serializable]
        public sealed class Handoff
        {
            public string completionId;
            public int reachedRound;
            public int completedRounds;
            public int totalKills;
            public int standardKills;
            public int completedRoundXp;
            public int killXp;
            public int goldEarned;
            public float runDuration;

            public int profileLevel;
            public int xpIntoLevel;
            public int totalXpEarned;
            public int runsPlayed;
            public int totalKillsLifetime;
            public string unlockedWeapons;
            public string unlockedBranches;
            public string achievements;
            public int newUnlocksFirstTime;
            public int newAchievementsFirstTime;

            /// <summary>Session-local RunDirector.RunId of A's first run: B's first run gets the same number.</summary>
            public int firstRunId;
            public long lastOrdinal;
            public string lineage;

            /// <summary>A's FIRST applied completion (stage 02), for the relaunch replay of stage 21.</summary>
            public string firstCompletionId;
            public int firstReachedRound;
            public int firstCompletedRounds;
            public int firstTotalKills;
            public int firstKillXp;
            public int firstRoundXp;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 30f;
            private string _outDir;
            private string _phase;
            private readonly List<Stage> _stages = new List<Stage>();
            private int _changed;

            internal void Configure(string outDir, string phase)
            {
                _outDir = outDir;
                _phase = phase;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);
                ProfileService.Changed += () => _changed++;

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;

                switch (_phase)
                {
                    case "B": yield return PhaseB(); break;
                    case "C": yield return PhaseInterrupted("10a-interrupted-primary-present-old-kept", false); break;
                    case "D": yield return PhaseInterrupted("10b-interrupted-primary-missing-new-rolled-forward", true); break;
                    case "V2": yield return PhaseLegacyV2(); break;
                    case "L1": yield return PhaseLease(true); break;
                    case "L2": yield return PhaseLease(false); break;
                    default: yield return PhaseA(); break;
                }

                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary-" + _phase + ".txt"),
                    "ProfilePersistenceProbe phase " + _phase + " " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[ProfileProbe] DONE phase=" + _phase + " stages=" + _stages.Count + " pass=" + passCount);

                if (RunDirector.Instance != null && RunDirector.Instance.IsRunLive)
                {
                    PrototypeUiFlow.AbortRunToMainMenu();
                }
                else if (PrototypeUiFlow.CurrentSceneName != PrototypeScenes.MainMenu)
                {
                    PrototypeUiFlow.GoToMainMenu();
                }

                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.Quit();
            }

            // ================================================================== phase A

            private IEnumerator PhaseA()
            {
                // Fresh isolated profile: the probe owns this path; nothing of a previous run may leak in.
                ProfileService.ResetProfile();
                PrototypeConfig config = PrototypeConfig.Active;
                int levelStart = ProfileService.Current.ProfileLevel;
                int xpStart = ProfileService.Current.TotalXpEarned;
                int killsStart = ProfileService.Current.TotalKills;
                int eventsStart = ProfileRunCommitter.EventsSeen;
                int commitsStart = ProfileRunCommitter.Commits;

                yield return EnterRun();
                RunDirector run = RunDirector.Instance;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                GoldService gold = GoldService.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = true;
                int firstRunId = run.RunId;

                // ---------------------------------------------------------------- 01: complete round 1 → round XP
                int cleared = 0;
                Action<int> onCleared = r => cleared++;
                RunEvents.RoundCompleted += onCleared;
                float t0 = Time.realtimeSinceStartup;
                while (cleared == 0 && Time.realtimeSinceStartup - t0 < 90f)
                {
                    KillAllLiving();
                    yield return null;
                }

                RunEvents.RoundCompleted -= onCleared;
                yield return WaitUntil(() => run.Phase == RunPhase.Upgrading, "upgrade screen after round 1");
                int round1Kills = run.Stats.TotalKills;
                int expectedRound1Xp = config.RoundCompletionXp(1);
                Stage s1 = Capture("01-completed-round-grants-round-xp");
                s1.pass = cleared == 1 && run.Stats.CompletedRounds == 1 && expectedRound1Xp > 0 && run.Stats.CompletedRoundXp == expectedRound1Xp &&
                          ProfileService.Current.TotalXpEarned == xpStart;
                s1.detail = "completedRounds=" + run.Stats.CompletedRounds + " completedRoundXp=" + run.Stats.CompletedRoundXp + " expected=" + expectedRound1Xp +
                            " round1Kills=" + round1Kills + " profileXpUnchangedDuringRun=" + (ProfileService.Current.TotalXpEarned == xpStart);
                s1.note = "round 1 completed: the run's round XP equals RoundCompletionXp(1); the profile is untouched until the run ends";
                Write(s1);

                // ---------------------------------------------------------------- 02/03: kills in the unfinished round 2, then death
                run.CloseUpgradeScreen();
                yield return WaitUntil(() => run.Phase == RunPhase.Fighting && run.Round == 2, "round 2");
                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                EnemyFactory.ClearAll();
                gold.ClearOrbs(false);
                yield return null;
                for (int i = 0; i < 3; i++)
                {
                    ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, rig.transform.position + new Vector3(12f + i * 1.5f, 0f, 2f), Quaternion.identity);
                    if (z != null) z.Kill();
                    yield return null;
                }

                yield return null;
                int killsBeforeDeath = run.Stats.TotalKills;
                float runTime = run.Stats.RunTime;
                _changed = 0;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.Death");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death");
                yield return WaitForScene(PrototypeScenes.Results);
                for (int i = 0; i < 30; i++) yield return null;

                RunSummary summary = RunEvents.LastRunSummary;
                RunRewardReport r1 = ProfileService.LastReport;
                int expectedKillXp = summary.TotalKills * config.KillXp(EnemyTier.Standard);
                Stage s2 = Capture("02-unfinished-last-round-grants-no-round-xp");
                s2.pass = r1 != null && r1.Applied && summary.ReachedRound == 2 && summary.CompletedRounds == 1 &&
                          summary.CompletedRoundXp == expectedRound1Xp && r1.CompletedRoundXp == expectedRound1Xp &&
                          ProfileService.Current.RoundsCompleted == 1;
                s2.detail = "reached=" + summary.ReachedRound + " completed=" + summary.CompletedRounds + " roundXp=" + summary.CompletedRoundXp +
                            " (round 2 would have added " + config.RoundCompletionXp(2) + ") receiptRoundXp=" + (r1 != null ? r1.CompletedRoundXp : -1);
                s2.note = "died in round 2: only round 1's XP is granted; the unfinished round grants none";
                Write(s2);

                Stage s3 = Capture("03-half-round-kills-and-kill-xp-kept");
                s3.pass = r1 != null && summary.TotalKills == killsBeforeDeath && summary.TotalKills >= round1Kills + 3 &&
                          summary.KillXp == expectedKillXp && r1.KillXp == expectedKillXp &&
                          ProfileService.Current.TotalKills == killsStart + summary.TotalKills &&
                          ProfileService.Current.StandardKills == killsStart + summary.StandardKills;
                s3.detail = "summaryKills=" + summary.TotalKills + " (round1 " + round1Kills + " + 3 in round 2) killXp=" + summary.KillXp +
                            " expected=" + expectedKillXp + " profileKills=" + ProfileService.Current.TotalKills;
                s3.note = "the three kills of the unfinished round count in statistics and in kill XP";
                Write(s3);

                // ---------------------------------------------------------------- 04: gold and playtime grant no XP
                int xpDelta = ProfileService.Current.TotalXpEarned - xpStart;
                Stage s4 = Capture("04-gold-and-playtime-grant-no-xp");
                s4.pass = r1 != null && summary.GoldEarned > 0 && summary.RunDuration > 0f && runTime > 0f &&
                          xpDelta == summary.CompletedRoundXp + summary.KillXp && r1.TotalXpEarned == xpDelta &&
                          ProfileService.Current.BestRunXp == xpDelta;
                s4.detail = "goldEarned=" + summary.GoldEarned + " runDuration=" + summary.RunDuration.ToString("0.0") + "s profileXpDelta=" + xpDelta +
                            " = roundXp " + summary.CompletedRoundXp + " + killXp " + summary.KillXp;
                s4.note = "gold was earned and time was played; profile XP moved by exactly round XP + kill XP";
                Write(s4);

                // ---------------------------------------------------------------- 05: same identity twice → not applied
                string ramBefore = RamHash();
                string diskBefore = DiskHash();
                int commitsAfterFirst = ProfileRunCommitter.Commits;
                int changedAfterFirst = _changed;
                RunEvents.PublishRunEnded(summary);
                yield return null;
                RunRewardReport again = ProfileService.LastReport;
                Stage s5 = Capture("05-same-completion-identity-not-applied-twice");
                s5.pass = ProfileRunCommitter.EventsSeen == eventsStart + 2 && ProfileRunCommitter.Commits == commitsAfterFirst &&
                          commitsAfterFirst == commitsStart + 1 && ReferenceEquals(again, r1) && _changed == changedAfterFirst && changedAfterFirst == 1 &&
                          RamHash() == ramBefore && DiskHash() == diskBefore && ProfileService.LastVerdict == CompletionTokenVerdict.AlreadyApplied;
                s5.detail = "id=" + summary.CompletionId + " events=" + (ProfileRunCommitter.EventsSeen - eventsStart) + " commits=" + (ProfileRunCommitter.Commits - commitsStart) +
                            " changed=" + _changed + " ramSame=" + (RamHash() == ramBefore) + " diskSame=" + (DiskHash() == diskBefore);
                s5.note = "a duplicate RunEnded with the same completion id reaches the committer but changes nothing";
                Write(s5);

                // ---------------------------------------------------------------- 09a: new unlock/achievement is "new" once
                Stage s9 = Capture("09a-new-content-reported-once");
                s9.pass = r1 != null && r1.NewAchievements.Count >= 1 && ReferenceEquals(again, r1);
                s9.detail = "firstReceipt newUnlocks=" + (r1 != null ? r1.NewUnlocks.Count : -1) + " newAchievements=" + (r1 != null ? r1.NewAchievements.Count : -1) +
                            " replayReturnedOriginalReceipt=" + ReferenceEquals(again, r1);
                s9.note = "the first receipt lists the newly earned content; the replay hands back that same receipt, nothing is 'new' again";
                Write(s9);

                // ---------------------------------------------------------------- 11: abort → no profile delta
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return EnterRun();
                run = RunDirector.Instance; dir = EnemySpawnDirector.Instance; gold = GoldService.Instance; rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = true;
                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                EnemyFactory.ClearAll();
                gold.ClearOrbs(false);
                yield return null;
                for (int i = 0; i < 2; i++)
                {
                    ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, rig.transform.position + new Vector3(12f + i * 1.5f, 0f, 2f), Quaternion.identity);
                    if (z != null) z.Kill();
                    yield return null;
                }

                string ramAbort = RamHash();
                string diskAbort = DiskHash();
                int eventsAbort = ProfileRunCommitter.EventsSeen;
                int runsAbort = ProfileService.Current.RunsPlayed;
                int xpAbort = ProfileService.Current.TotalXpEarned;
                _changed = 0;
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return new WaitForSecondsRealtime(2.5f);
                Stage s11 = Capture("11-abort-no-profile-delta");
                s11.pass = SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && ProfileService.LastReport == null && _changed == 0 &&
                           ProfileRunCommitter.EventsSeen == eventsAbort && RamHash() == ramAbort && DiskHash() == diskAbort &&
                           ProfileService.Current.RunsPlayed == runsAbort && ProfileService.Current.TotalXpEarned == xpAbort;
                s11.detail = "changed=" + _changed + " events=" + (ProfileRunCommitter.EventsSeen - eventsAbort) + " ramSame=" + (RamHash() == ramAbort) +
                             " diskSame=" + (DiskHash() == diskAbort) + " lastReportNull=" + (ProfileService.LastReport == null);
                s11.note = "pause → abort → main menu: no Results, no ApplyRun, no Changed, RAM and disk hashes unchanged (B5 characterisation)";
                Write(s11);

                // ---------------------------------------------------------------- 12a: second clean run applies; interrupted-save snapshot for C/D
                yield return EnterRun();
                run = RunDirector.Instance; rig = PlayerRegistry.Primary;
                string idRun3 = run.CompletionId;
                int runsBefore3 = ProfileService.Current.RunsPlayed;
                string interruptedPrimary = Path.Combine(_outDir, "interrupted-primary");
                string interruptedMissing = Path.Combine(_outDir, "interrupted-missing");
                ProfileStore.SaveStageHook = stage =>
                {
                    if (stage == ProfileSaveStage.Replace)
                    {
                        // "The process died here": primary = old, .writing = validated new, .bak = old.
                        SnapshotProfileDir(interruptedPrimary, false);
                        SnapshotProfileDir(interruptedMissing, true);
                    }
                };
                _changed = 0;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.Death2");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death 2");
                yield return WaitForScene(PrototypeScenes.Results);
                for (int i = 0; i < 30; i++) yield return null;
                ProfileStore.SaveStageHook = null;
                RunRewardReport r3 = ProfileService.LastReport;
                string expectedNew = File.ReadAllText(ProfileStore.Path);
                string expectedOld = File.Exists(ProfileStore.BackupPath) ? File.ReadAllText(ProfileStore.BackupPath) : string.Empty;
                File.WriteAllText(Path.Combine(_outDir, "expected-old.json"), expectedOld);
                File.WriteAllText(Path.Combine(_outDir, "expected-new.json"), expectedNew);
                bool snapshotsOk = File.Exists(Path.Combine(interruptedPrimary, ProfileStore.FileName)) &&
                                   File.Exists(Path.Combine(interruptedPrimary, ProfileStore.FileName + ProfileStore.CandidateSuffix)) &&
                                   !File.Exists(Path.Combine(interruptedMissing, ProfileStore.FileName)) &&
                                   File.Exists(Path.Combine(interruptedMissing, ProfileStore.FileName + ProfileStore.CandidateSuffix)) &&
                                   File.Exists(Path.Combine(interruptedMissing, ProfileStore.FileName + ProfileStore.BackupSuffix));
                Stage s12 = Capture("12a-second-clean-run-applies");
                s12.pass = r3 != null && r3.Applied && r3.CompletionId == idRun3 && idRun3 != summary.CompletionId && _changed == 1 &&
                           ProfileService.Current.RunsPlayed == runsBefore3 + 1 && ReadDisk().RunsPlayed == runsBefore3 + 1 && snapshotsOk &&
                           expectedOld.Length > 0 && expectedNew.Length > 0 && expectedOld != expectedNew;
                s12.detail = "id3=" + idRun3 + " runsPlayed=" + ProfileService.Current.RunsPlayed + " disk=" + ReadDisk().RunsPlayed + " snapshots=" + snapshotsOk;
                s12.note = "a second clean run with its own identity applies once; the interrupted-save snapshots for phases C/D were taken at the replace step";
                Write(s12);

                // ================================================================ WP-009H stages 13-20
                // ---------------------------------------------------------------- 13: more real completions than the old ring (8) held
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                int runsBefore13 = ProfileService.Current.RunsPlayed;
                long ordBefore13 = ProfileService.Current.LastAppliedCompletionOrdinal;
                var tokens13 = new List<string>();
                var summaries13 = new List<RunSummary>();
                bool allApplied13 = true;
                for (int k = 0; k < 9; k++)
                {
                    yield return EnterRun();
                    run = RunDirector.Instance; rig = PlayerRegistry.Primary;
                    tokens13.Add(run.CompletionId);
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.Death13");
                    yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death 13." + k);
                    yield return WaitForScene(PrototypeScenes.Results);
                    for (int i = 0; i < 20; i++) yield return null;
                    RunRewardReport rk = ProfileService.LastReport;
                    allApplied13 &= rk != null && rk.Applied && rk.CompletionId == tokens13[k];
                    summaries13.Add(RunEvents.LastRunSummary);
                    PrototypeUiFlow.GoToMainMenu();
                    yield return WaitForScene(PrototypeScenes.MainMenu);
                }

                Stage s13 = Capture("13-nine-more-real-completions-applied");
                s13.pass = allApplied13 && ProfileService.Current.RunsPlayed == runsBefore13 + 9 && ProfileService.Current.LastAppliedCompletionOrdinal == ordBefore13 + 9 &&
                           ReadDisk().LastAppliedCompletionOrdinal == ordBefore13 + 9;
                s13.detail = "runsPlayed " + runsBefore13 + "->" + ProfileService.Current.RunsPlayed + " ordinal " + ordBefore13 + "->" + ProfileService.Current.LastAppliedCompletionOrdinal +
                             " (total completions this profile: " + ProfileService.Current.LastAppliedCompletionOrdinal + " > old ring capacity 8)";
                s13.note = "nine more real runs: every one applied once; the profile's high-water mark advanced with each save";
                Write(s13);

                // ---------------------------------------------------------------- 14: the FIRST completion of this process (stage 02) replayed → refused, hashes equal
                string ram14 = RamHash(); string disk14 = DiskHash(); int changed14 = _changed; _changed = 0;
                RunRewardReport r14 = ProfileRunCommitter.Commit(summary);
                Stage s14 = Capture("14-first-completion-refused-after-more-than-eight-runs");
                s14.pass = r14 != null && !r14.Applied && r14.Outcome == ProfileCommitOutcome.Duplicate && ProfileService.LastVerdict == CompletionTokenVerdict.AlreadyApplied &&
                           RamHash() == ram14 && DiskHash() == disk14 && _changed == 0 && ProfileService.Current.RunsPlayed == runsBefore13 + 9;
                s14.detail = "firstId=" + summary.CompletionId + " outcome=" + (r14 != null ? r14.Outcome.ToString() : "null") + " verdict=" + ProfileService.LastVerdict +
                             " ramSame=" + (RamHash() == ram14) + " diskSame=" + (DiskHash() == disk14) + " changed=" + _changed;
                s14.note = "WP-009 would have applied this again (its ring held the last 8 only); the ordinal high-water mark refuses it with no mutation";
                Write(s14);
                string ilkOrta13 = tokens13[4];
                RunRewardReport r14b = ProfileRunCommitter.Commit(summaries13[4]);
                RunRewardReport r14c = ProfileRunCommitter.Commit(summaries13[8]);
                Stage s14b = Capture("14b-middle-and-last-completions-refused");
                s14b.pass = r14b != null && !r14b.Applied && r14c != null && (r14c.Outcome == ProfileCommitOutcome.Duplicate || ReferenceEquals(r14c, ProfileService.LastReport)) &&
                            RamHash() == ram14 && DiskHash() == disk14 && _changed == 0;
                s14b.detail = "middle=" + ilkOrta13 + " → " + (r14b != null ? r14b.Outcome.ToString() : "null") + " last → " + (r14c != null ? r14c.Outcome.ToString() : "null");
                s14b.note = "middle and last completions of the series are refused too; the last one hands back its original receipt";
                Write(s14b);

                // ---------------------------------------------------------------- 15: same summary values, NEW authoritative completion → applied
                yield return EnterRun();
                run = RunDirector.Instance; rig = PlayerRegistry.Primary;
                RunSummary same = summary;             // identical stats to the first completion
                same.CompletionId = run.CompletionId;   // but the capability of THIS run (issued by StartRun)
                int runsBefore15 = ProfileService.Current.RunsPlayed;
                _changed = 0;
                RunRewardReport r15 = ProfileRunCommitter.Commit(same);
                Stage s15 = Capture("15-same-values-new-authoritative-completion-applied");
                s15.pass = r15 != null && r15.Applied && ProfileService.Current.RunsPlayed == runsBefore15 + 1 && _changed == 1 &&
                           !CompletionAuthority.HasLiveCapability && ProfileService.Current.LastAppliedCompletionOrdinal == ordBefore13 + 10;
                s15.detail = "newId=" + same.CompletionId + " applied=" + (r15 != null && r15.Applied) + " runsPlayed=" + ProfileService.Current.RunsPlayed + " liveConsumed=" + !CompletionAuthority.HasLiveCapability;
                s15.note = "a distinct run with byte-identical statistics applies once: identical content is never a duplicate reason";
                Write(s15);
                // The live run is now consumed; its own death would be refused as already applied. Abort it (B5: no profile write).
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);

                // ---------------------------------------------------------------- 16/17: forged / legacy tokens refused, no mutation
                string ram16 = RamHash(); string disk16 = DiskHash(); _changed = 0;
                ProfileData pp = ProfileService.Current;
                RunSummary forged = summary;
                forged.CompletionId = CompletionAuthority.TestSeam.Forge(pp.ProfileLineageId, pp.LastAppliedCompletionOrdinal + 1, CompletionAuthority.TestSeam.NewNonce());
                RunRewardReport r16 = ProfileRunCommitter.Commit(forged);
                RunSummary foreign = summary;
                foreign.CompletionId = CompletionAuthority.TestSeam.Forge("fedcba9876543210fedcba9876543210", pp.LastAppliedCompletionOrdinal + 1, CompletionAuthority.TestSeam.NewNonce());
                RunRewardReport r16b = ProfileRunCommitter.Commit(foreign);
                Stage s16 = Capture("16-random-unissued-token-refused");
                s16.pass = r16 != null && r16.Outcome == ProfileCommitOutcome.InvalidIdentity && r16b != null && r16b.Outcome == ProfileCommitOutcome.InvalidIdentity &&
                           RamHash() == ram16 && DiskHash() == disk16 && _changed == 0;
                s16.detail = "unissued(next ordinal, random nonce) → " + (r16 != null ? r16.Outcome.ToString() : "null") + "; foreign lineage → " + (r16b != null ? r16b.Outcome.ToString() : "null") + " ramSame=" + (RamHash() == ram16) + " diskSame=" + (DiskHash() == disk16);
                s16.note = "a well-formed token this process never issued (and one from another lineage) is refused fail-closed";
                Write(s16);

                RunSummary legacy = summary;
                legacy.CompletionId = Guid.NewGuid().ToString("N");
                RunRewardReport r17 = ProfileRunCommitter.Commit(legacy);
                Stage s17 = Capture("17-legacy-v2-raw-guid-refused");
                s17.pass = r17 != null && r17.Outcome == ProfileCommitOutcome.InvalidIdentity && ProfileService.LastVerdict == CompletionTokenVerdict.Malformed &&
                           RamHash() == ram16 && DiskHash() == disk16 && _changed == 0;
                s17.detail = "legacyGuid=" + legacy.CompletionId + " → " + (r17 != null ? r17.Outcome.ToString() : "null") + " verdict=" + ProfileService.LastVerdict;
                s17.note = "the WP-009 raw-GUID identity namespace is retired: a bare GUID is malformed to the new parser";
                Write(s17);

                // ---------------------------------------------------------------- 18: SaveFailed → retry → Applied → second retry refused
                yield return EnterRun();
                run = RunDirector.Instance; rig = PlayerRegistry.Primary;
                int runsBefore18 = ProfileService.Current.RunsPlayed;
                long ordBefore18 = ProfileService.Current.LastAppliedCompletionOrdinal;
                string disk18 = DiskHash();
                ProfileStore.SaveStageHook = st => { if (st == ProfileSaveStage.Replace) throw new IOException("ProfileProbe injected replace fault"); };
                _changed = 0;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.Death18");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death 18");
                yield return WaitForScene(PrototypeScenes.Results);
                for (int i = 0; i < 20; i++) yield return null;
                ProfileStore.SaveStageHook = null;
                RunRewardReport r18a = ProfileService.LastReport;
                bool failedHonest = r18a != null && r18a.Outcome == ProfileCommitOutcome.SaveFailed && ProfileService.Current.RunsPlayed == runsBefore18 && DiskHash() == disk18 &&
                                    CompletionAuthority.HasLiveCapability && _changed == 0;
                RunSummary s18sum = RunEvents.LastRunSummary;
                RunRewardReport r18b = ProfileRunCommitter.Commit(s18sum);
                bool retryApplied = r18b != null && r18b.Applied && ProfileService.Current.RunsPlayed == runsBefore18 + 1 && ReadDisk().RunsPlayed == runsBefore18 + 1 &&
                                    ReadDisk().LastAppliedCompletionOrdinal == ordBefore18 + 1 && _changed == 1 && !CompletionAuthority.HasLiveCapability;
                string disk18b = DiskHash();
                RunRewardReport r18c = ProfileRunCommitter.Commit(s18sum);
                bool secondRefused = r18c != null && ReferenceEquals(r18c, r18b) && ProfileService.Current.RunsPlayed == runsBefore18 + 1 && DiskHash() == disk18b && _changed == 1;
                Stage s18 = Capture("18-savefailed-retry-applied-second-retry-refused");
                s18.pass = failedHonest && retryApplied && secondRefused;
                s18.detail = "failed=" + (r18a != null ? r18a.Outcome.ToString() : "null") + " liveKept=" + failedHonest + " retry=" + (r18b != null ? r18b.Outcome.ToString() : "null") +
                             " ordinal " + ordBefore18 + "->" + ProfileService.Current.LastAppliedCompletionOrdinal + " secondRetryRefused=" + secondRefused;
                s18.note = "a failed save keeps the capability live (no ordinal advance); the same token then applies exactly once - stats and ordinal in one write - and is refused afterwards";
                Write(s18);
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);

                // ---------------------------------------------------------------- 19: aborted run's token stays invalid
                yield return EnterRun();
                run = RunDirector.Instance;
                string abortToken = run.CompletionId;
                RunSummary abortSum = summary;
                abortSum.CompletionId = abortToken;
                string ram19 = RamHash(); string disk19 = DiskHash(); _changed = 0;
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                RunRewardReport r19 = ProfileRunCommitter.Commit(abortSum);
                Stage s19 = Capture("19-aborted-run-token-invalid");
                s19.pass = r19 != null && r19.Outcome == ProfileCommitOutcome.InvalidIdentity && ProfileService.LastVerdict == CompletionTokenVerdict.NotIssued &&
                           !CompletionAuthority.HasLiveCapability && RamHash() == ram19 && DiskHash() == disk19 && _changed == 0;
                s19.detail = "abortToken=" + abortToken + " → " + (r19 != null ? r19.Outcome.ToString() : "null") + " verdict=" + ProfileService.LastVerdict + " ramSame=" + (RamHash() == ram19) + " diskSame=" + (DiskHash() == disk19);
                s19.note = "abort invalidates the run's capability and leaves the profile untouched (B5); the token can never be presented later";
                Write(s19);

                // Phase B replays A's LAST APPLIED completion (the stage-18 retry; StartRun of the
                // later abort/reset runs cleared LastReport, so it is named explicitly) and A's FIRST (stage 02).
                r3 = r18b;

                // Hand-off for phase B.
                ProfileData p = ProfileService.Current;
                var handoff = new Handoff
                {
                    completionId = r3 != null ? r3.CompletionId : string.Empty,
                    reachedRound = r3 != null ? r3.Summary.ReachedRound : 0,
                    completedRounds = r3 != null ? r3.Summary.CompletedRounds : 0,
                    totalKills = r3 != null ? r3.Summary.TotalKills : 0,
                    standardKills = r3 != null ? r3.Summary.StandardKills : 0,
                    completedRoundXp = r3 != null ? r3.Summary.CompletedRoundXp : 0,
                    killXp = r3 != null ? r3.Summary.KillXp : 0,
                    goldEarned = r3 != null ? r3.Summary.GoldEarned : 0,
                    runDuration = r3 != null ? r3.Summary.RunDuration : 0f,
                    profileLevel = p.ProfileLevel,
                    xpIntoLevel = p.XpIntoLevel,
                    totalXpEarned = p.TotalXpEarned,
                    runsPlayed = p.RunsPlayed,
                    totalKillsLifetime = p.TotalKills,
                    unlockedWeapons = string.Join(",", p.UnlockedWeapons),
                    unlockedBranches = string.Join(",", p.UnlockedBranches),
                    achievements = string.Join(",", p.Achievements),
                    newUnlocksFirstTime = r1 != null ? r1.NewUnlocks.Count : 0,
                    newAchievementsFirstTime = r1 != null ? r1.NewAchievements.Count : 0,
                    firstRunId = firstRunId,
                    lastOrdinal = p.LastAppliedCompletionOrdinal,
                    lineage = p.ProfileLineageId,
                    firstCompletionId = summary.CompletionId,
                    firstReachedRound = summary.ReachedRound,
                    firstCompletedRounds = summary.CompletedRounds,
                    firstTotalKills = summary.TotalKills,
                    firstKillXp = summary.KillXp,
                    firstRoundXp = summary.CompletedRoundXp
                };
                File.WriteAllText(Path.Combine(_outDir, "handoff-A.json"), JsonUtility.ToJson(handoff, true));
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
            }

            // ================================================================== phase B

            private IEnumerator PhaseB()
            {
                string handoffPath = Path.Combine(_outDir, "handoff-A.json");
                Handoff h = File.Exists(handoffPath) ? JsonUtility.FromJson<Handoff>(File.ReadAllText(handoffPath)) : null;
                ProfileData p = ProfileService.Current;

                // ---------------------------------------------------------------- 08: level and unlocks restored from disk
                Stage s8 = Capture("08-level-and-unlocks-restored-after-relaunch");
                s8.pass = h != null && ProfileStore.LastLoadStatus == ProfileLoadStatus.Ok && ProfileStore.LastLoadRecovery == ProfileLoadRecovery.None &&
                          !ProfileStore.SaveLocked && p.ProfileLevel == h.profileLevel && p.XpIntoLevel == h.xpIntoLevel && p.TotalXpEarned == h.totalXpEarned &&
                          p.RunsPlayed == h.runsPlayed && p.TotalKills == h.totalKillsLifetime && string.Join(",", p.UnlockedWeapons) == h.unlockedWeapons &&
                          string.Join(",", p.UnlockedBranches) == h.unlockedBranches && string.Join(",", p.Achievements) == h.achievements &&
                          p.LastAppliedCompletionOrdinal == h.lastOrdinal && p.ProfileLineageId == h.lineage && ProfileService.LastReport == null;
                s8.detail = h == null ? "handoff missing" : "level=" + p.ProfileLevel + " xp=" + p.TotalXpEarned + " runs=" + p.RunsPlayed + " weapons=" + string.Join(",", p.UnlockedWeapons) +
                            " branches=" + string.Join(",", p.UnlockedBranches) + " achievements=" + string.Join(",", p.Achievements) + " loadStatus=" + ProfileStore.LastLoadStatus;
                s8.note = "new process: the profile A wrote came back field by field; the applied identity is on disk; no receipt in memory";
                Write(s8);

                // ---------------------------------------------------------------- 06: replay after relaunch refused
                string ramBefore = RamHash();
                string diskBefore = DiskHash();
                _changed = 0;
                RunRewardReport replay = null;
                if (h != null)
                {
                    var summary = new RunSummary
                    {
                        CompletionId = h.completionId, ReachedRound = h.reachedRound, CompletedRounds = h.completedRounds,
                        TotalKills = h.totalKills, StandardKills = h.standardKills, CompletedRoundXp = h.completedRoundXp, KillXp = h.killXp,
                        TotalXpEarned = h.completedRoundXp + h.killXp, GoldEarned = h.goldEarned, RunDuration = h.runDuration,
                        EndReason = RunEndReason.PlayerDeath
                    };
                    replay = ProfileRunCommitter.Commit(summary);
                }

                Stage s6 = Capture("06-replay-after-relaunch-refused");
                s6.pass = replay != null && replay.Outcome == ProfileCommitOutcome.Duplicate && !replay.Applied && _changed == 0 &&
                          ProfileRunCommitter.Commits == 0 && RamHash() == ramBefore && DiskHash() == diskBefore &&
                          ProfileService.Current.RunsPlayed == (h != null ? h.runsPlayed : -1);
                s6.detail = "outcome=" + (replay != null ? replay.Outcome.ToString() : "null") + " changed=" + _changed + " commits=" + ProfileRunCommitter.Commits +
                            " ramSame=" + (RamHash() == ramBefore) + " diskSame=" + (DiskHash() == diskBefore);
                s6.note = "the last summary of the previous process, replayed through the committer, is refused: the identity lives in the saved profile";
                Write(s6);

                // ---------------------------------------------------------------- 09b: nothing "new" on replay
                Stage s9 = Capture("09b-no-new-content-on-replay");
                s9.pass = replay != null && replay.NewUnlocks.Count == 0 && replay.NewAchievements.Count == 0 && h != null && h.newAchievementsFirstTime >= 1;
                s9.detail = "replay newUnlocks=" + (replay != null ? replay.NewUnlocks.Count : -1) + " newAchievements=" + (replay != null ? replay.NewAchievements.Count : -1) +
                            " firstTime(A) newAchievements=" + (h != null ? h.newAchievementsFirstTime : -1);
                s9.note = "content that was 'new' in A's receipt is not reported again by the refused replay";
                Write(s9);

                // ---------------------------------------------------------------- 12b: a clean run in this process applies independently
                yield return EnterRun();
                RunDirector run = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                string idB = run.CompletionId;
                int runsBefore = ProfileService.Current.RunsPlayed;
                _changed = 0;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.DeathB");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death B");
                yield return WaitForScene(PrototypeScenes.Results);
                for (int i = 0; i < 30; i++) yield return null;
                RunRewardReport rb = ProfileService.LastReport;
                Stage s12 = Capture("12b-clean-run-in-new-process-applies");
                s12.pass = rb != null && rb.Applied && rb.CompletionId == idB && h != null && idB != h.completionId && _changed == 1 &&
                           ProfileService.Current.RunsPlayed == runsBefore + 1 && ReadDisk().RunsPlayed == runsBefore + 1 &&
                           ProfileService.Current.LastAppliedCompletionOrdinal == h.lastOrdinal + 1 &&
                           run.RunId == h.firstRunId;
                s12.detail = "idB=" + idB + " runsPlayed=" + ProfileService.Current.RunsPlayed + " disk=" + ReadDisk().RunsPlayed + " changed=" + _changed +
                             " sessionRunId=" + run.RunId + " (A's first run had " + h.firstRunId + ": the session serial restarts, the completion id does not)";
                s12.note = "a fresh identity in the new process applies once; the previous process's identity stays refused; the session RunId repeats across launches and is therefore not the identity";
                Write(s12);

                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);

                // ---------------------------------------------------------------- 21: A's FIRST completion (ordinal 1, > 8 runs ago) after relaunch → refused
                string ram21 = RamHash(); string disk21 = DiskHash(); _changed = 0;
                RunRewardReport r21 = null;
                if (h != null)
                {
                    r21 = ProfileRunCommitter.Commit(new RunSummary
                    {
                        CompletionId = h.firstCompletionId, ReachedRound = h.firstReachedRound, CompletedRounds = h.firstCompletedRounds,
                        TotalKills = h.firstTotalKills, StandardKills = h.firstTotalKills, KillXp = h.firstKillXp, CompletedRoundXp = h.firstRoundXp,
                        TotalXpEarned = h.firstKillXp + h.firstRoundXp, EndReason = RunEndReason.PlayerDeath
                    });
                }

                Stage s21 = Capture("21-first-completion-refused-after-relaunch");
                s21.pass = r21 != null && !r21.Applied && r21.Outcome == ProfileCommitOutcome.Duplicate && ProfileService.LastVerdict == CompletionTokenVerdict.AlreadyApplied &&
                           RamHash() == ram21 && DiskHash() == disk21 && _changed == 0 && ProfileService.Current.LastAppliedCompletionOrdinal > 9;
                s21.detail = "firstId=" + (h != null ? h.firstCompletionId : "?") + " → " + (r21 != null ? r21.Outcome.ToString() : "null") + " verdict=" + ProfileService.LastVerdict +
                             " mark=" + ProfileService.Current.LastAppliedCompletionOrdinal + " ramSame=" + (RamHash() == ram21) + " diskSame=" + (DiskHash() == disk21);
                s21.note = "in a NEW process, the very first completion of the profile (more than eight runs back) is refused by the persisted high-water mark";
                Write(s21);

                // ---------------------------------------------------------------- 20: reset → new lineage; a pre-reset token is foreign
                yield return EnterRun();
                run = RunDirector.Instance;
                string preResetToken = run.CompletionId;
                RunSummary summary20 = RunEvents.LastRunSummary;
                string oldLineage = ProfileService.Current.ProfileLineageId;
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                ProfileService.ResetProfile();
                string newLineage = ProfileService.Current.ProfileLineageId;
                RunSummary preReset = summary20;
                preReset.CompletionId = preResetToken;
                string ram20 = RamHash(); string disk20 = DiskHash(); _changed = 0;
                RunRewardReport r20 = ProfileRunCommitter.Commit(preReset);
                ProfileData afterReset = ProfileService.Current;
                Stage s20 = Capture("20-reset-new-lineage-pre-reset-token-refused");
                s20.pass = oldLineage != newLineage && afterReset.LastAppliedCompletionOrdinal == 0 && afterReset.RunsPlayed == 0 && r20 != null &&
                           r20.Outcome == ProfileCommitOutcome.InvalidIdentity && ProfileService.LastVerdict == CompletionTokenVerdict.ForeignLineage &&
                           RamHash() == ram20 && DiskHash() == disk20 && _changed == 0 && ReadDisk().ProfileLineageId == newLineage;
                s20.detail = "lineage " + oldLineage + " → " + newLineage + " preResetToken → " + (r20 != null ? r20.Outcome.ToString() : "null") + " verdict=" + ProfileService.LastVerdict;
                s20.note = "reset mints a new lineage (ordinal 0) written over the primary; every token of the old lineage is foreign";
                Write(s20);

                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
            }

            // ================================================================== phase V2 (legacy fixture → v3, legacy replay refused)

            private IEnumerator PhaseLegacyV2()
            {
                // The launch was pointed at a v2 fixture (8 GUIDs in the retired ring); loading migrated it in memory.
                ProfileData p = ProfileService.Current;
                string fixturePath = Path.Combine(_outDir, "v2-fixture-ids.txt");
                string[] ids = File.Exists(fixturePath) ? File.ReadAllLines(fixturePath) : new string[0];
                bool migrated = ProfileStore.LastLoadStatus == ProfileLoadStatus.Ok && p.SaveVersion == 3 && ProfileData.IsLineageId(p.ProfileLineageId) &&
                                p.LastAppliedCompletionOrdinal == 0 && p.RunsPlayed == 11 && p.ProfileLevel == 2 && p.TotalXpEarned == 140 && p.TotalKills == 31;
                Assert(ProfileService.Save(), "v3 write");
                string json = File.ReadAllText(ProfileStore.Path);
                bool ringGone = json.IndexOf("AppliedCompletionIds", StringComparison.Ordinal) < 0 && json.IndexOf("\"SaveVersion\": 3", StringComparison.Ordinal) >= 0;
                string ram = RamHash(); string disk = DiskHash(); _changed = 0;
                bool allRefused = ids.Length == 8;
                for (int i = 0; i < ids.Length; i++)
                {
                    RunRewardReport r = ProfileRunCommitter.Commit(new RunSummary { CompletionId = ids[i], ReachedRound = 2, CompletedRounds = 1, KillXp = 5, TotalKills = 5, StandardKills = 5, CompletedRoundXp = 20, EndReason = RunEndReason.PlayerDeath });
                    allRefused &= r != null && r.Outcome == ProfileCommitOutcome.InvalidIdentity && ProfileService.LastVerdict == CompletionTokenVerdict.Malformed;
                }

                Stage s22 = Capture("22-v2-fixture-migrated-legacy-ring-replay-refused");
                s22.pass = migrated && ringGone && allRefused && RamHash() == ram && DiskHash() == disk && _changed == 0;
                s22.detail = "migrated=" + migrated + " ringGoneOnDisk=" + ringGone + " legacyIds=" + ids.Length + " allRefused=" + allRefused + " ramSame=" + (RamHash() == ram) + " diskSame=" + (DiskHash() == disk);
                s22.note = "a real v2 document (11 runs, last 8 GUIDs) migrates losslessly to v3 (lineage minted, ordinal 0, ring dropped); every legacy GUID - in the ring or evicted - is refused";
                Write(s22);

                // And a real run on the migrated profile applies once.
                yield return EnterRun();
                RunDirector run = RunDirector.Instance; PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.DeathV2");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death v2");
                yield return WaitForScene(PrototypeScenes.Results);
                for (int i = 0; i < 20; i++) yield return null;
                RunRewardReport rv = ProfileService.LastReport;
                Stage s22b = Capture("22b-real-run-on-migrated-profile-applies");
                s22b.pass = rv != null && rv.Applied && ProfileService.Current.RunsPlayed == 12 && ProfileService.Current.LastAppliedCompletionOrdinal == 1;
                s22b.detail = "runsPlayed=" + ProfileService.Current.RunsPlayed + " ordinal=" + ProfileService.Current.LastAppliedCompletionOrdinal;
                s22b.note = "the migrated profile keeps its 11 legacy runs and records the new one as ordinal 1 of the new lineage";
                Write(s22b);
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
            }

            private static void Assert(bool ok, string what)
            {
                if (!ok) Debug.LogWarning("[ProfileProbe] check failed: " + what);
            }

            // ================================================================== phases L1 / L2 (two real processes, one profile path)

            private IEnumerator PhaseLease(bool holder)
            {
                string marker = Path.Combine(_outDir, "lease-holder-ready.txt");
                if (holder)
                {
                    // L1: owner. Publish readiness, hold the lease for a while so L2 can start meanwhile.
                    bool owner = ProfileStore.OwnsProfile && !ProfileStore.SaveLocked;
                    bool saved = ProfileService.Save();
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                    Stage s23 = Capture("23a-lease-holder-is-writer");
                    s23.pass = owner && saved;
                    s23.detail = "owns=" + ProfileStore.OwnsProfile + " saveLocked=" + ProfileStore.SaveLocked + " saved=" + saved + " lock=" + ProfileStore.LockPath;
                    s23.note = "first process: holds the fcntl lease on the lock file and saves normally";
                    Write(s23);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 12f) yield return null;
                    bool stillOwner = ProfileStore.OwnsProfile && ProfileService.Save();
                    Stage s23c = Capture("23c-lease-holder-still-writer-after-second-process");
                    s23c.pass = stillOwner;
                    s23c.detail = "owns=" + ProfileStore.OwnsProfile;
                    s23c.note = "the loser's attempt changed nothing for the owner";
                    Write(s23c);
                }
                else
                {
                    // L2: started while L1 holds the lease → must be read-only; save / reset / commit refused.
                    bool readOnly = ProfileStore.SaveLocked && !ProfileStore.OwnsProfile;
                    string reason = ProfileStore.SaveLockReason;
                    string disk = DiskHash();
                    bool saveRefused = !ProfileService.Save();
                    ProfileService.ResetProfile();
                    bool resetRefused = DiskHash() == disk;
                    yield return EnterRun();
                    RunDirector run = RunDirector.Instance; PlayerRig rig = PlayerRegistry.Primary;
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ProfileProbe.DeathL2");
                    yield return WaitUntil(() => run.Phase == RunPhase.Dead, "death L2");
                    yield return WaitForScene(PrototypeScenes.Results);
                    for (int i = 0; i < 20; i++) yield return null;
                    RunRewardReport r = ProfileService.LastReport;
                    bool commitRefused = r != null && r.Outcome == ProfileCommitOutcome.SaveFailed && DiskHash() == disk;
                    Stage s23b = Capture("23b-second-process-read-only-cannot-save-reset-commit");
                    s23b.pass = readOnly && saveRefused && resetRefused && commitRefused && File.Exists(marker);
                    s23b.detail = "saveLocked=" + readOnly + " reason='" + reason + "' saveRefused=" + saveRefused + " resetRefused=" + resetRefused + " commit=" + (r != null ? r.Outcome.ToString() : "null") + " diskSame=" + (DiskHash() == disk);
                    s23b.note = "second process on the same profile path: the lease is held by the first, so this one is read-only - no save, no reset, no completion commit (honest 'not saved' receipt)";
                    Write(s23b);
                    PrototypeUiFlow.GoToMainMenu();
                    yield return WaitForScene(PrototypeScenes.MainMenu);
                }
            }

            // ================================================================== phases C / D

            private IEnumerator PhaseInterrupted(string stageName, bool expectNew)
            {
                string expectedPath = Path.Combine(_outDir, expectNew ? "expected-new.json" : "expected-old.json");
                string expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath) : string.Empty;
                ProfileData p = ProfileService.Current;
                string actual = File.Exists(ProfileStore.Path) ? File.ReadAllText(ProfileStore.Path) : string.Empty;
                ProfileData expectedData;
                bool expectedParses = ProfileMigration.Parse(expected, out expectedData) == ProfileLoadStatus.Ok;

                Stage s = Capture(stageName);
                s.pass = expected.Length > 0 && expectedParses && actual == expected && p.IsValid() && p.RunsPlayed == expectedData.RunsPlayed &&
                         !File.Exists(ProfileStore.CandidatePath) && ProfileStore.LastLoadStatus == (expectNew ? ProfileLoadStatus.Missing : ProfileLoadStatus.Ok) &&
                         ProfileStore.LastLoadRecovery == (expectNew ? ProfileLoadRecovery.RolledForwardCandidate : ProfileLoadRecovery.None) && !ProfileStore.SaveLocked;
                s.detail = "runsPlayed=" + p.RunsPlayed + " expectedRunsPlayed=" + (expectedParses ? expectedData.RunsPlayed : -1) + " bytesEqual=" + (actual == expected) +
                           " loadStatus=" + ProfileStore.LastLoadStatus + " recovery=" + ProfileStore.LastLoadRecovery + " candidateLeft=" + File.Exists(ProfileStore.CandidatePath);
                s.note = expectNew
                    ? "primary missing + validated candidate + backup: the NEW complete profile is rolled forward, byte for byte what A wrote"
                    : "primary present + unverified candidate: the OLD complete profile is kept, byte for byte, and the candidate is discarded";
                Write(s);
                yield return null;

                if (expectNew)
                {
                    // Last phase: the probe removes its own profile data (evidence JSON stays).
                    ProfileStore.ReleaseOwnership();
                    TryDeleteDir(Path.Combine(_outDir, "interrupted-primary"));
                    TryDeleteDir(Path.Combine(_outDir, "interrupted-missing"));
                }
            }

            // ================================================================== helpers

            private IEnumerator EnterRun()
            {
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && GoldService.Instance != null &&
                                             PlayerRegistry.Primary != null && RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");
            }

            private static void SnapshotProfileDir(string target, bool dropPrimary)
            {
                TryDeleteDir(target);
                Directory.CreateDirectory(target);
                // The probe path may use any file name (-ddProfilePath); phases C/D open the
                // snapshot under the store's default name, so the suffixes are re-based.
                string dir = Path.GetDirectoryName(ProfileStore.Path);
                string baseName = Path.GetFileName(ProfileStore.Path);
                foreach (string f in Directory.GetFiles(dir, baseName + "*"))
                {
                    string name = Path.GetFileName(f);
                    string suffix = name.Substring(baseName.Length);
                    if (suffix == ProfileStore.LockSuffix || suffix.StartsWith(ProfileStore.CorruptSuffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (dropPrimary && suffix.Length == 0)
                    {
                        continue;
                    }

                    using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var dst = new FileStream(Path.Combine(target, ProfileStore.FileName + suffix), FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        src.CopyTo(dst);
                    }
                }
            }

            private static void TryDeleteDir(string dir)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch (Exception)
                {
                    // best effort
                }
            }

            private static ProfileData ReadDisk()
            {
                ProfileData d;
                return ProfileStore.ReadFile(ProfileStore.Path, out d) == ProfileLoadStatus.Ok ? d : new ProfileData { RunsPlayed = -1 };
            }

            private static string Sha(byte[] bytes)
            {
                using (var sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
                }
            }

            private static string RamHash()
            {
                return Sha(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(ProfileService.Current)));
            }

            private static string DiskHash()
            {
                return File.Exists(ProfileStore.Path) ? Sha(File.ReadAllBytes(ProfileStore.Path)) : "missing";
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " | " + _stages[i].note + " | " + _stages[i].detail;
                }
            }

            private static void SetSpawning(EnemySpawnDirector dir, bool on)
            {
                System.Reflection.FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(dir, on);
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

            private Stage Capture(string stage)
            {
                ProfileData p = ProfileService.Current;
                RunRewardReport r = ProfileService.LastReport;
                return new Stage
                {
                    stage = stage,
                    phase = _phase,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    profileLevel = p.ProfileLevel,
                    xpIntoLevel = p.XpIntoLevel,
                    totalXpEarned = p.TotalXpEarned,
                    runsPlayed = p.RunsPlayed,
                    roundsCompleted = p.RoundsCompleted,
                    totalKills = p.TotalKills,
                    unlockedWeapons = p.UnlockedWeapons.Count,
                    unlockedBranches = p.UnlockedBranches.Count,
                    achievements = p.Achievements.Count,
                    lastAppliedOrdinal = p.LastAppliedCompletionOrdinal,
                    lineage = p.ProfileLineageId,
                    lastCompletionId = r != null ? r.CompletionId : string.Empty,
                    lastOutcome = r != null ? r.Outcome.ToString() : "none",
                    committerEvents = ProfileRunCommitter.EventsSeen,
                    committerCommits = ProfileRunCommitter.Commits,
                    loadStatus = ProfileStore.LastLoadStatus.ToString(),
                    loadRecovery = ProfileStore.LastLoadRecovery.ToString(),
                    saveLocked = ProfileStore.SaveLocked,
                    ramHash = RamHash(),
                    diskHash = DiskHash()
                };
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[ProfileProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.detail);
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
                        Debug.LogError("[ProfileProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
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
