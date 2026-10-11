using System;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Difficulty
{
    /// <summary>
    /// WP-102 · the difficulty of the run. <see cref="Selected"/> is what the menu / Prepare Run picks (S-09); at the load of the
    /// Run scene <see cref="BeginRun"/> fixes <see cref="RunMode"/> and — Hard / Demolition only — makes a mode-applied copy of the
    /// active PrototypeConfig the active one (the EnemyVersionProbe clone / swap-back pattern), so every system that reads the
    /// config (stats, rounds, tier mix, armour, spawn placement) plays the mode without a call site of its own. Leaving the run
    /// (<see cref="EndRun"/>) puts the shipped instance back. Normal never clones: the shipped asset runs exactly as r3-final.
    /// The crowd AI, the attack slots, the chokepoint queue and the gait pools read <see cref="Current"/> (null in Normal).
    /// </summary>
    public static class DifficultyService
    {
        /// <summary>Launch override for bot / capture runs (development builds and the Editor only): <c>DD_DIFFICULTY=demolition</c>.</summary>
        public const string EnvironmentVariable = "DD_DIFFICULTY";
        /// <summary>
        /// Launch override, command line: <c>-ddDifficulty normal|hard|demolition</c> — every build, release-probe included (S-10 WP-111:
        /// the R4 FINAL measures Demolition perf on that build). Logged once as <c>[DD] difficulty=&lt;mode&gt; source=commandline</c>.
        /// </summary>
        public const string CommandLineFlag = "-ddDifficulty";

        private static DifficultyMode s_selected;
        private static bool s_launchRead, s_launchOverride, s_overrideNoted;
        private static PrototypeConfig s_shipped, s_clone;

        /// <summary>
        /// The mode the next run starts with (menu / Prepare Run, S-09). A launch override (<see cref="CommandLineFlag"/>, or
        /// <see cref="EnvironmentVariable"/> in development builds) wins: the menu's write is then ignored (logged once), so a bot or a
        /// FINAL probe started with a mode plays that mode whatever the menu remembers (gt 12.10).
        /// </summary>
        public static DifficultyMode Selected
        {
            get { ReadLaunchOverride(); return s_selected; }
            set
            {
                ReadLaunchOverride();
                if (s_launchOverride)
                {
                    if (value != s_selected && !s_overrideNoted)
                    {
                        s_overrideNoted = true;
                        Debug.Log("[DD] difficulty: menu selection " + Id(value) + " ignored - launch override " + Id(s_selected));
                    }

                    return;
                }

                if (s_selected == value) return;
                s_selected = value;
                SelectedChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// WP-097 R4 (S-08): the menu / Prepare Run pick changed - the mode-select sound (Hard / Demolition), S-09's badge. Raised by
        /// the <see cref="Selected"/> setter on a change only; the launch override (and a menu write it ignores) and
        /// <see cref="ResetForTests"/> stay silent.
        /// </summary>
        public static event Action<DifficultyMode> SelectedChanged;

        /// <summary>A launch override set the mode: the menu cannot change it this session.</summary>
        public static bool LaunchOverride { get { ReadLaunchOverride(); return s_launchOverride; } }

        /// <summary>The mode of the run in progress (or the last one): results, score and profile keys (S-04).</summary>
        public static DifficultyMode RunMode { get; private set; }

        /// <summary>The run's mode settings while a Hard / Demolition run is live; null in Normal and outside a run.</summary>
        public static DifficultyModeSettings Current { get; private set; }

        /// <summary>Raised by <see cref="BeginRun"/> with the run's mode (HUD badge, S-09).</summary>
        public static event Action<DifficultyMode> RunStarted;

        public static int AttackSlotBonus => Current != null ? Mathf.Max(0, Current.AttackSlotBonus) : 0;
        /// <summary>The run's opening gold (GoldService credits it as the run opens; 0 in Normal).</summary>
        public static int StartingGold => Current != null ? Mathf.Max(0, Current.StartingGold) : 0;

        private static float s_goldRemainder;

        /// <summary>
        /// One kill's gold × the mode's GoldScale, exact over the run: the fraction is carried to the next kill (Hard 2 → 2, 3, 2, 3…).
        /// Normal (and a scale of 1) returns the amount unchanged.
        /// </summary>
        public static int ScaleGold(int amount)
        {
            int whole = ScaleGold(amount, out float carry);
            CommitGoldCarry(carry);
            return whole;
        }

        /// <summary>
        /// <see cref="ScaleGold(int)"/> without moving the carried fraction: GoldService commits <paramref name="carry"/> only once the
        /// ledger grants the kill, so a refused claim does not eat the fraction (gt 12.10).
        /// </summary>
        public static int ScaleGold(int amount, out float carry)
        {
            carry = s_goldRemainder;
            DifficultyModeSettings s = Current;
            if (s == null || amount <= 0 || s.GoldScale <= 0f || Mathf.Approximately(s.GoldScale, 1f)) return amount;
            float exact = amount * s.GoldScale + s_goldRemainder;
            int whole = Mathf.FloorToInt(exact);
            carry = exact - whole;
            return whole;
        }

        /// <summary>The fraction <see cref="ScaleGold(int, out float)"/> returned, carried to the next kill.</summary>
        public static void CommitGoldCarry(float carry) => s_goldRemainder = carry;
        public static float QueueSecondsScale => Current != null && Current.QueueSecondsScale > 0f ? Current.QueueSecondsScale : 1f;

        /// <summary>PrototypeUiFlow's scene load: entering the Run scene begins the run's mode, any other scene ends it.</summary>
        public static void OnSceneLoad(bool enteringRun)
        {
            if (enteringRun) BeginRun();
            else EndRun();
        }

        public static void BeginRun()
        {
            EndRun();
            s_goldRemainder = 0f;
            RunMode = Selected;
            DifficultyModeSettings s = RunMode == DifficultyMode.Normal ? null : DifficultyProfile.Active.For(RunMode);
            if (s != null && !s.IsNeutral)
            {
                PrototypeConfig shipped = PrototypeConfig.Active;
                if (shipped != null)
                {
                    PrototypeConfig clone = PrototypeConfig.Clone(shipped);
                    clone.name = shipped.name + " (" + RunMode + ")";
                    Apply(clone, s);
                    s_shipped = shipped;
                    s_clone = clone;
                    Current = s;
                    PrototypeConfig.SetActive(clone);
                }
            }

            Debug.Log("[DD-DIFF] run mode " + RunMode + (Current != null ? " (config " + PrototypeConfig.Active.name + ")" : ""));
            RunStarted?.Invoke(RunMode);
        }

        /// <summary>The shipped config back (only when the active one is still this service's copy: a harness swap is left alone).</summary>
        public static void EndRun()
        {
            Current = null;
            if (s_clone == null) return;
            if (ReferenceEquals(PrototypeConfig.Active, s_clone) && s_shipped != null) PrototypeConfig.SetActive(s_shipped);
            if (Application.isPlaying) UnityEngine.Object.Destroy(s_clone);
            else UnityEngine.Object.DestroyImmediate(s_clone);
            s_clone = null;
            s_shipped = null;
        }

        /// <summary>
        /// The mode's numbers onto <paramref name="config"/> (a copy — never the shipped asset). Public for the tests and the bot's
        /// balance report; deterministic, no allocation beyond the config itself.
        /// </summary>
        public static void Apply(PrototypeConfig config, DifficultyModeSettings s)
        {
            if (config == null || s == null || s.IsNeutral) return;
            for (int i = 0; i < config.Enemies.Tiers.Count; i++)
            {
                EnemyTierSettings t = config.Enemies.Tiers[i];
                if (t == null) continue;
                int k = (int)t.Tier;
                if (At(s.MoveSpeed, k) > 0f) t.MoveSpeed = At(s.MoveSpeed, k);
                if (AtInt(s.IntroductionRound, k) > 0) t.IntroductionRound = AtInt(s.IntroductionRound, k);
                if (At(s.SpawnWeight, k) >= 0f) t.SpawnWeight = At(s.SpawnWeight, k);
                if (At(s.SpawnWeightGrowthPerRound, k) >= 0f) t.SpawnWeightGrowthPerRound = At(s.SpawnWeightGrowthPerRound, k);
                if (At(s.SpawnWeightMax, k) >= 0f) t.SpawnWeightMax = At(s.SpawnWeightMax, k);
                t.AttackDamage *= s.DamageScale;
                t.TelegraphTime = Mathf.Max(Mathf.Min(t.TelegraphTime, s.MinTelegraphSeconds), t.TelegraphTime * s.TelegraphScale);
                t.AttackRecoveryTime *= s.RecoveryScale;
                t.AttackCooldown *= s.CooldownScale;
                t.HealthMultiplier *= s.HealthScale;
                if (t.ArmorChances != null)
                {
                    t.ArmorChances.ChanceGrowthPerRound *= s.ArmorGrowthScale;
                    t.ArmorChances.MaxChance = Mathf.Clamp01(t.ArmorChances.MaxChance + s.ArmorMaxAdd);
                }
            }

            RoundConfigSection r = config.Rounds;
            if (s.HealthMultiplierPerRound >= 0f) r.HealthMultiplierPerRound = s.HealthMultiplierPerRound;
            r.PoolBase *= s.PoolScale;
            r.PoolPerRound *= s.PoolScale;
            r.PoolPowerScale *= s.PoolScale;
            if (s.ActiveBase >= 0f) r.ActiveBase = s.ActiveBase;
            if (s.ActivePerRound >= 0f) r.ActivePerRound = s.ActivePerRound;
            if (s.ActiveCap > 0) r.ActiveCap = Mathf.Min(s.ActiveCap, r.AbsoluteSafetyCeiling);
            if (s.SpawnRefillInterval > 0f) r.SpawnRefillInterval = s.SpawnRefillInterval;
            if (s.MaxSpawnsPerRefillTick > 0) r.MaxSpawnsPerRefillTick = s.MaxSpawnsPerRefillTick;

            SpawnConfigSection p = config.Spawn;
            if (s.MinSpawnDistance > 0f) p.MinSpawnDistance = s.MinSpawnDistance;
            if (s.PreferredDistanceMin > 0f) p.PreferredDistanceMin = s.PreferredDistanceMin;
            if (s.PreferredDistanceMax > 0f) p.PreferredDistanceMax = s.PreferredDistanceMax;
            if (s.AheadOfPlayerBonus > 0f) p.AheadOfPlayerBonus = s.AheadOfPlayerBonus;
            config.NotifyChanged();
        }

        // ---------------------------------------------------------------------------------------------------------- gaits

        private static readonly int[] s_runCrowd = { CharacterAnimatorContract.GaitRunFeralAV4, CharacterAnimatorContract.GaitRunStumbleAV4, CharacterAnimatorContract.GaitRunFeralBV4 };
        private static readonly int[] s_runElite = { CharacterAnimatorContract.GaitRunStumbleAV4, CharacterAnimatorContract.GaitRunFeralBV4 };
        private static readonly int[] s_ultraFastHeavyWalk = { CharacterAnimatorContract.GaitWalkHeavyBV4, CharacterAnimatorContract.GaitWalkHeavyV3 };
        private static readonly int[] s_fastHeavyWalk = { CharacterAnimatorContract.GaitWalkHeavyV3 };

        /// <summary>
        /// The run's gait pool for a tier when the mode needs another one (its speed is outside the shipped pool's cadence band —
        /// a faster speed on a walk clip slides the feet; the band is per body size: rate = speed / (clip speed × body scale));
        /// null = the shipped pool (ZombieGaitPools).
        /// </summary>
        public static int[] GaitPoolFor(int tier)
        {
            DifficultyModeSettings s = Current;
            if (s == null) return null;
            if (tier == 0 && s.RunningCrowd) return s_runCrowd;
            if (tier == 2 && s.RunningElite) return s_runElite;
            if (tier == 3 && s.UltraFastHeavyWalk) return s_ultraFastHeavyWalk;
            if (tier == 2 && s.EliteFastHeavyWalk) return s_fastHeavyWalk;
            return null;
        }

        // ----------------------------------------------------------------------------------------------------- launch / reset

        /// <summary>The launch override is read at start-up (so the log line is there even before a run) and on first use.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReadLaunchOverrideAtStartup() => ReadLaunchOverride();

        private static void ReadLaunchOverride()
        {
            if (s_launchRead) return;
            s_launchRead = true;
            string value = null, source = null;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(value)) source = "environment";
#endif
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], CommandLineFlag, StringComparison.OrdinalIgnoreCase)) { value = args[i + 1]; source = "commandline"; }
            if (source == null) return;
            if (TryParse(value, out DifficultyMode m))
            {
                s_selected = m;
                s_launchOverride = true;
                Debug.Log("[DD] difficulty=" + Id(m) + " source=" + source);
            }
            else Debug.LogWarning("[DD] difficulty: unknown value '" + value + "' (" + source + ") — Normal");
        }

        /// <summary>
        /// The mode's stable, text-independent identifier — "normal" / "hard" / "demolition": the command-line value, the log line and
        /// the UI string keys (S-09 "difficulty.&lt;id&gt;.*") bind to it. Never changes; <see cref="TryParse"/> reads it back.
        /// </summary>
        public static string Id(DifficultyMode m) => m == DifficultyMode.Hard ? "hard" : m == DifficultyMode.Demolition ? "demolition" : "normal";

        /// <summary>"normal" / "hard" / "zor" / "demolition" (case-insensitive) → mode.</summary>
        public static bool TryParse(string value, out DifficultyMode mode)
        {
            mode = DifficultyMode.Normal;
            if (string.IsNullOrEmpty(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "normal": mode = DifficultyMode.Normal; return true;
                case "hard": case "zor": mode = DifficultyMode.Hard; return true;
                case "demolition": mode = DifficultyMode.Demolition; return true;
                default: return false;
            }
        }

        /// <summary>Tests: back to a clean Normal state (no clone active, nothing selected).</summary>
        public static void ResetForTests()
        {
            EndRun();
            s_selected = DifficultyMode.Normal;
            s_launchRead = true;
            s_launchOverride = false;
            s_overrideNoted = false;
            RunMode = DifficultyMode.Normal;
        }

        /// <summary>Tests: a launch override as if the build had been started with <c>-ddDifficulty</c>.</summary>
        internal static void SetLaunchOverrideForTests(DifficultyMode mode)
        {
            s_launchRead = true;
            s_launchOverride = true;
            s_overrideNoted = false;
            s_selected = mode;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_selected = DifficultyMode.Normal;
            s_launchRead = false;
            s_launchOverride = false;
            s_overrideNoted = false;
            s_shipped = null;
            s_clone = null;
            Current = null;
            RunMode = DifficultyMode.Normal;
            RunStarted = null;
            SelectedChanged = null;
        }

        private static float At(float[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : -1f;
        private static int AtInt(int[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : 0;
    }
}
