#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-001 · test-only lifecycle probe for the real Player.
    ///
    /// Proves, across a REAL process boundary, what the PlayMode tests cannot: that a
    /// normally finished run reaches the persistent profile, that a normal quit keeps it,
    /// that a fresh launch loads it back, and that nothing run-scoped (gold, upgrades,
    /// health, selection) survives the relaunch (DEC-006, DEC-007, DEC-037, DEC-039).
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if). A release
    /// Player contains none of this; without <c>-ddProbe</c> a development Player
    /// allocates nothing either. No product behaviour is changed: the probe only drives
    /// the same public flow API the screens use and reads public state.
    ///
    /// Command line:
    ///   -ddProbe &lt;outDir&gt;          enable; write snapshots into outDir, then quit normally
    ///   -ddProfilePath &lt;file&gt;      redirect the profile file (keeps the real profile untouched)
    ///   -ddProbeRun                 also play one run to Results, then return to Main Menu
    ///
    /// Snapshots (JSON): boot.json (Main Menu reached), results.json (after ApplyRun),
    /// session.json (back in Main Menu, same process), and probe.log lines in Player.log.
    /// </summary>
    public static class LifecycleProbe
    {
        private const string OutFlag = "-ddProbe";
        private const string ProfileFlag = "-ddProfilePath";
        private const string RunFlag = "-ddProbeRun";

        // Runs after SubsystemRegistration (where ProfileStore.ResetStatics clears any
        // override) and before the Boot scene's Awake, i.e. before anything can touch
        // ProfileService.EnsureLoaded. That ordering is what makes the redirect stick.
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
                Debug.Log("[Probe] profile path override -> " + profilePath);
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

            var carrier = new GameObject("LifecycleProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir, HasFlag(RunFlag));
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

        private static bool HasFlag(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Everything the acceptance criteria compare across the process boundary.</summary>
        [Serializable]
        public sealed class Snapshot
        {
            public string stage;
            public string utc;
            public string scene;
            public string profilePath;
            public bool profileFileExists;

            // Persistent profile (DEC-007) — must survive quit/relaunch.
            public int profileLevel;
            public int xpIntoLevel;
            public int totalXpEarned;
            public int highestRound;
            public int runsPlayed;
            public int roundsCompleted;
            public int totalKills;
            public int bestRunXp;
            public int achievementsCount;
            public int unlockedWeaponsCount;
            public int unlockedBranchesCount;
            public string lastSavedUtc;

            // Session-scoped selection (DEC-037 within a process, DEC-039 across processes).
            public string selectedSkin;
            public string selectedWeapon;
            public string registrySelectedWeapon;

            // Run-scoped state (DEC-006) — must be absent/zero on a fresh launch.
            public bool runDirectorPresent;
            public bool runLive;
            public int round;
            public int goldBalance;
            public int fireRateTier;
            public float playerHealth;
            public float playerHealthMax;
            public bool goldServicePresent;
            public int liveOrbs;
            public int holdCount;
            public float timeScale;

            // Last run summary as reported to the profile (only meaningful in results.json).
            public string lastRunEndReason;
            public int lastRunReachedRound;
            public int lastRunKills;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 25f;
            private string _outDir;
            private bool _playRun;

            internal void Configure(string outDir, bool playRun)
            {
                _outDir = outDir;
                _playRun = playRun;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;
                Write("boot", Capture("boot"));

                if (_playRun)
                {
                    yield return PlayOneRun();
                }

                // Normal shutdown path: the same call the Main Menu's QUIT button makes
                // (ProfileService.Save + Application.Quit → OnApplicationQuit → Save).
                yield return new WaitForSecondsRealtime(0.5f);
                Debug.Log("[Probe] quitting normally");
                PrototypeUiFlow.Quit();
            }

            private IEnumerator PlayOneRun()
            {
                // DEC-037 evidence: pick a non-default skin; it must be remembered inside
                // this process and forgotten by the next one (DEC-039). The classic weapon
                // stays on the first unlocked one because a level-1 profile has no other.
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedSkin = PlayerSkin.IndigoBlack;
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                Debug.Log("[Probe] selection skin=" + PrototypeUiFlow.SelectedSkin + " weapon=" + PrototypeUiFlow.SelectedWeapon);

                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && PlayerRegistry.Primary != null &&
                                             WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting,
                    "run did not reach Fighting");

                RunDirector director = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController weapon = WeaponRegistry.ActiveController;

                // Dirty the run-scoped state on purpose so its absence after relaunch means something.
                if (director.Wallet != null)
                {
                    director.Wallet.Credit(137, rig.transform.position);
                }

                weapon.Modifiers.AddTier(UpgradeBranch.FireRate, 1, PrototypeConfig.Active);
                Debug.Log("[Probe] run dirtied gold=" + (director.Wallet != null ? director.Wallet.Balance : -1) +
                          " fireRateTier=" + weapon.Modifiers.GetTier(UpgradeBranch.FireRate));

                // Let the round breathe a little, then die normally through PlayerHealth.
                yield return new WaitForSecondsRealtime(2f);
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp-001-probe");
                yield return WaitUntil(() => director.Phase == RunPhase.Dead, "run did not reach Dead");
                yield return WaitForScene(PrototypeScenes.Results);
                yield return null;
                Write("results", Capture("results"));

                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;
                Write("session", Capture("session"));
            }

            private static Snapshot Capture(string stage)
            {
                ProfileData p = ProfileService.Current;
                RunDirector d = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController w = WeaponRegistry.ActiveController;
                RunSummary s = RunEvents.LastRunSummary;

                var snap = new Snapshot
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    profilePath = ProfileStore.Path,
                    profileFileExists = File.Exists(ProfileStore.Path),
                    profileLevel = p.ProfileLevel,
                    xpIntoLevel = p.XpIntoLevel,
                    totalXpEarned = p.TotalXpEarned,
                    highestRound = p.HighestRound,
                    runsPlayed = p.RunsPlayed,
                    roundsCompleted = p.RoundsCompleted,
                    totalKills = p.TotalKills,
                    bestRunXp = p.BestRunXp,
                    achievementsCount = p.Achievements != null ? p.Achievements.Count : 0,
                    unlockedWeaponsCount = p.UnlockedWeapons != null ? p.UnlockedWeapons.Count : 0,
                    unlockedBranchesCount = p.UnlockedBranches != null ? p.UnlockedBranches.Count : 0,
                    lastSavedUtc = p.LastSavedUtc,
                    selectedSkin = PrototypeUiFlow.SelectedSkin.ToString(),
                    selectedWeapon = PrototypeUiFlow.SelectedWeapon.ToString(),
                    registrySelectedWeapon = WeaponRegistry.SelectedWeapon.ToString(),
                    runDirectorPresent = d != null,
                    runLive = d != null && d.IsRunLive,
                    round = d != null ? d.Round : 0,
                    goldBalance = d != null && d.Wallet != null ? d.Wallet.Balance : 0,
                    fireRateTier = w != null ? w.Modifiers.GetTier(UpgradeBranch.FireRate) : 0,
                    playerHealth = rig != null && rig.Health != null ? rig.Health.Current : 0f,
                    playerHealthMax = rig != null && rig.Health != null ? rig.Health.Max : 0f,
                    goldServicePresent = GoldService.Instance != null,
                    liveOrbs = GoldService.Instance != null ? GoldService.Instance.LiveOrbCount : 0,
                    holdCount = RunFlowClock.HoldCount,
                    timeScale = Time.timeScale,
                    lastRunEndReason = s.EndReason.ToString(),
                    lastRunReachedRound = s.ReachedRound,
                    lastRunKills = s.TotalKills
                };
                return snap;
            }

            private void Write(string name, Snapshot snap)
            {
                string path = Path.Combine(_outDir, name + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(snap, true));
                Debug.Log("[Probe] " + name + " -> " + path + " | runsPlayed=" + snap.runsPlayed +
                          " skin=" + snap.selectedSkin + " weapon=" + snap.selectedWeapon +
                          " runDirector=" + snap.runDirectorPresent + " gold=" + snap.goldBalance);
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
                        Debug.LogError("[Probe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
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
