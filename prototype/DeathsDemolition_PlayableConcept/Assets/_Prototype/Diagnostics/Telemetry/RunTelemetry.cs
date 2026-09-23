using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Diagnostics.Telemetry
{
    /// <summary>One round of a run, as the recorder saw it. Plain value fields only (JsonUtility).</summary>
    [Serializable]
    public struct RoundRecord
    {
        public int round;
        public float startSeconds;             // Time.time at RoundStarted
        public float seconds;                  // RoundStarted → RoundCompleted (or run end)
        public bool completed;
        public int goldStart;                  // wallet at RoundStarted (RunEvents.GoldChanged balance)
        public int goldIncome;                 // Σ positive wallet deltas during the round (pickups)
        public int goldSpent;                  // Σ negative wallet deltas (cards, rerolls)
        public int goldEnd;
        public int killsStandard, killsSpecial, killsElite, killsUltra;
        public int killsArmoured;              // kills whose spawn composition carried ≥1 piece
        public int armourPiecesAtKill;         // Σ pieces of the killed enemies' spawn composition
        public float damageTaken;              // Σ EnemyAttackLandedInfo.Damage
        public int hitsTaken;
        public int specialSpawns, specialPickups;
        public int cardsBought;                // Σ tier increases of the active weapon's modifiers during the round
        public int[] tiersAtEnd;               // WeaponModifierState tiers (10) at round end
        public float ttkSumStandard, ttkSumSpecial, ttkSumElite, ttkSumUltra;   // first player hit → death, seconds
        public int ttkCountStandard, ttkCountSpecial, ttkCountElite, ttkCountUltra;
        public int outOfWorldRemovals;         // WP-061K: bodies despawned for leaving the world (fell / stranded) — no reward
    }

    /// <summary>One time-to-kill sample (first player hit → death).</summary>
    [Serializable]
    public struct TtkSample
    {
        public int round;
        public int tier;
        public int armourPieces;
        public float seconds;
    }

    /// <summary>The whole run document (`dd-run-telemetry-v1`).</summary>
    [Serializable]
    public sealed class RunTelemetryDocument
    {
        public string schema = RunTelemetry.Schema;
        public string utc;
        public string build;                   // Application.version
        public string weapon;
        public string endReason;
        public int reachedRound, completedRounds;
        public float runSeconds;
        public int totalKills, goldEarned, goldSpent, upgradesPurchased, rerollsUsed, armorPiecesBroken;
        public int completedRoundXp, killXp;
        public int roundCount;
        public RoundRecord[] rounds = Array.Empty<RoundRecord>();
        public TtkSample[] ttk = Array.Empty<TtkSample>();
        public int ttkDropped;                 // samples beyond the ring capacity
        public bool truncated;                 // rounds beyond the record capacity were not stored
    }

    /// <summary>
    /// WP-045 (ADR-045-1) · Local, offline run telemetry for simulator calibration. Observes the
    /// existing run / damage / weapon events, accumulates into PREALLOCATED struct buffers (no
    /// managed allocation on the hot path: every handler writes fields of a struct array or a
    /// pre-sized dictionary), and writes ONE JSON per run at run end to
    /// <c>persistentDataPath/telemetry/run-&lt;utc&gt;.json</c>. It carries no profile or identity data
    /// (no lineage, ordinal, level, achievements, user name), only what happened in the run.
    /// <c>-ddTelemetry 0</c> disables it; <c>-ddTelemetryDir &lt;dir&gt;</c> redirects the output
    /// (bots, tests). Round records are capped at <see cref="RoundCapacity"/> and TTK samples at
    /// <see cref="TtkCapacity"/> so a file stays well under 200 KB.
    /// </summary>
    public static class RunTelemetry
    {
        public const string Schema = "dd-run-telemetry-v1";
        public const string Folder = "telemetry";
        public const int RoundCapacity = 64;
        public const int TtkCapacity = 512;
        private const int LifeTrackCapacity = 256;

        private static bool s_installed;
        private static bool s_enabled = true;

        /// <summary>WP-022F (S-04 review): the event subscription is a separate fact from "installed" - the CLI can install without subscribing.</summary>
        private static bool s_subscribed;

        /// <summary>WP-022F: <c>-ddTelemetry 0</c> on the command line; the Settings toggle cannot override it (bots, tests).</summary>
        private static bool s_commandLineOff;
        private static string s_directory;
        private static bool s_inRun;
        private static float s_runStart;
        private static int s_roundIndex = -1;
        private static int s_wallet;
        private static int s_specialSpawns, s_specialPickups;
        private static readonly RoundRecord[] s_rounds = new RoundRecord[RoundCapacity];
        private static readonly int[][] s_tierBuffers = new int[RoundCapacity][];
        private static readonly TtkSample[] s_ttk = new TtkSample[TtkCapacity];
        private static int s_ttkCount, s_ttkDropped;
        private static readonly Dictionary<int, float> s_firstHit = new Dictionary<int, float>(LifeTrackCapacity);
        private static readonly int[] s_tierScratch = new int[WeaponModifierState.BranchCount];
        private static readonly int[] s_tierAtRoundStart = new int[WeaponModifierState.BranchCount];
        private static bool s_truncated;
        private static RunTelemetryDocument s_last;
        private static string s_lastPath;

        public static bool Enabled => s_enabled;

        /// <summary>True when the command line switched telemetry off for this process (the Settings toggle is then a no-op).</summary>
        public static bool CommandLineOff => s_commandLineOff;
        public static bool InRun => s_inRun;

        /// <summary>The last written document (null before any run) — tests, F1.</summary>
        public static RunTelemetryDocument LastDocument => s_last;

        public static string LastPath => s_lastPath;

        public static string Directory
        {
            get
            {
                if (string.IsNullOrEmpty(s_directory))
                {
                    s_directory = Path.Combine(Application.persistentDataPath, Folder);
                }

                return s_directory;
            }
        }

        /// <summary>Current round record (a copy) — tests / F1 readout.</summary>
        public static RoundRecord CurrentRound => s_roundIndex >= 0 && s_roundIndex < RoundCapacity ? s_rounds[s_roundIndex] : default;

        public static int TtkSampleCount => s_ttkCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
            ParseArgs(Environment.GetCommandLineArgs());
            if (!s_enabled)
            {
                return;
            }

            EnsureSubscribed();
        }

        private static void EnsureSubscribed()
        {
            if (s_subscribed)
            {
                return;
            }

            s_subscribed = true;
            Subscribe();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_installed = false;
            s_subscribed = false;
            s_commandLineOff = false;
            s_enabled = true;
            s_directory = null;
            Reset();
            s_last = null;
            s_lastPath = null;
        }

        /// <summary>Parses <c>-ddTelemetry 0|1</c> and <c>-ddTelemetryDir &lt;dir&gt;</c>. Public for tests.</summary>
        public static void ParseArgs(string[] args)
        {
            if (args == null)
            {
                return;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-ddTelemetry", StringComparison.Ordinal))
                {
                    s_enabled = !(args[i + 1] == "0" || string.Equals(args[i + 1], "off", StringComparison.OrdinalIgnoreCase) || string.Equals(args[i + 1], "false", StringComparison.OrdinalIgnoreCase));
                    s_commandLineOff = !s_enabled;
                }
                else if (string.Equals(args[i], "-ddTelemetryDir", StringComparison.Ordinal))
                {
                    s_directory = args[i + 1];
                }
            }
        }

        private static void Subscribe()
        {
            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundStarted += OnRoundStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
            RunEvents.GoldChanged += OnGoldChanged;
            RunEvents.RunEnded += OnRunEnded;
            DamageEvents.EnemyDamaged += OnEnemyDamaged;
            DamageEvents.EnemyDied += OnEnemyDied;
            DamageEvents.PlayerAttacked += OnPlayerAttacked;
            ZombieEnemy.OutOfWorldRemoved += OnOutOfWorldRemoved;   // WP-061K
            WeaponEvents.SpecialWeaponSpawned += OnSpecialSpawned;
            WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
        }

        // ------------------------------------------------------------------ recording

        private static void Reset()
        {
            s_inRun = false;
            s_roundIndex = -1;
            s_wallet = 0;
            s_specialSpawns = 0;
            s_specialPickups = 0;
            s_ttkCount = 0;
            s_ttkDropped = 0;
            s_truncated = false;
            s_firstHit.Clear();
            Array.Clear(s_rounds, 0, s_rounds.Length);
        }

        /// <summary>Begins a run record. Public so a test can drive the recorder without a scene.</summary>
        public static void BeginRun()
        {
            Reset();
            s_inRun = true;
            s_runStart = Time.time;
        }

        private static void OnRunStarted(int startRound)
        {
            if (!s_enabled)
            {
                // Switched off from the Settings pane (WP-022E): no run is recorded and
                // nothing is written until it is switched back on before a run.
                return;
            }

            BeginRun();
        }

        /// <summary>
        /// WP-022E · the player's Settings toggle ("RUN TELEMETRY"). Persisted by the UI
        /// (ui-settings.json); applied here at load and on change. Enabling subscribes
        /// once; disabling keeps the subscription and skips the next runs (a run already
        /// in progress still completes its file). The command line (-ddTelemetry 0)
        /// still wins for bots and tests because it is parsed at install.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            if (s_commandLineOff)
            {
                // -ddTelemetry 0 owns this process (bots, tests): the toggle is a no-op and
                // Enabled stays false, so the flag never lies about a missing subscription.
                return;
            }

            s_enabled = enabled;
            if (enabled)
            {
                EnsureSubscribed();
            }
        }

        public static void OnRoundStarted(int round)
        {
            if (!s_inRun)
            {
                BeginRun();
            }

            CloseRound(false);
            if (s_roundIndex + 1 >= RoundCapacity)
            {
                s_truncated = true;
                return;
            }

            s_roundIndex++;
            ref RoundRecord r = ref s_rounds[s_roundIndex];
            r = default;
            r.round = round;
            r.startSeconds = Time.time;
            r.goldStart = s_wallet;
            r.goldEnd = s_wallet;
            ReadTiers(s_tierAtRoundStart);
        }

        public static void OnRoundCompleted(int round)
        {
            CloseRound(true);
        }

        private static void CloseRound(bool completed)
        {
            if (s_roundIndex < 0 || s_roundIndex >= RoundCapacity)
            {
                return;
            }

            ref RoundRecord r = ref s_rounds[s_roundIndex];
            if (r.seconds > 0f)
            {
                return;   // already closed
            }

            r.seconds = Mathf.Max(0.0001f, Time.time - r.startSeconds);
            r.completed = completed;
            r.goldEnd = s_wallet;
            r.specialSpawns = s_specialSpawns;
            r.specialPickups = s_specialPickups;
            s_specialSpawns = 0;
            s_specialPickups = 0;
            ReadTiers(s_tierScratch);
            int bought = 0;
            for (int i = 0; i < s_tierScratch.Length; i++)
            {
                bought += Mathf.Max(0, s_tierScratch[i] - s_tierAtRoundStart[i]);
            }

            r.cardsBought = bought;
            int[] buffer = s_tierBuffers[s_roundIndex];
            if (buffer == null)
            {
                buffer = new int[WeaponModifierState.BranchCount];   // once per round slot for the process (round boundary, not the hot path)
                s_tierBuffers[s_roundIndex] = buffer;
            }

            Array.Copy(s_tierScratch, buffer, buffer.Length);
            r.tiersAtEnd = buffer;
        }

        private static void ReadTiers(int[] into)
        {
            Array.Clear(into, 0, into.Length);
            WeaponController wc = WeaponRegistry.ActiveController;
            if (wc != null && wc.Modifiers != null)
            {
                wc.Modifiers.CopyTiersTo(into);
            }
        }

        public static void OnGoldChanged(int balance)
        {
            int delta = balance - s_wallet;
            s_wallet = balance;
            if (s_roundIndex < 0 || s_roundIndex >= RoundCapacity)
            {
                return;
            }

            ref RoundRecord r = ref s_rounds[s_roundIndex];
            if (delta > 0)
            {
                r.goldIncome += delta;
            }
            else if (delta < 0)
            {
                r.goldSpent -= delta;
            }

            r.goldEnd = balance;
        }

        public static void OnEnemyDamaged(EnemyDamageInfo info)
        {
            if (!s_inRun)
            {
                return;
            }

            int life = info.Meta.LifeId;
            if (life == 0 || s_firstHit.ContainsKey(life))
            {
                return;
            }

            if (s_firstHit.Count >= LifeTrackCapacity)
            {
                return;   // bounded: a flood of untracked lives is dropped, never grown
            }

            s_firstHit[life] = Time.time;
        }

        /// <summary>WP-061K: one per removed body — a per-round counter and one log line (rare event; the string is not a hot path).</summary>
        public static void OnOutOfWorldRemoved(EnemyTier tier, Vector3 where, ZombieEnemy.OutOfWorldReason reason)
        {
            Debug.Log("[DD-TELEMETRY] enemy.outOfWorld tier=" + tier + " reason=" + reason + " pos=" + where.ToString("F1") + " round=" + (s_roundIndex >= 0 && s_roundIndex < RoundCapacity ? s_rounds[s_roundIndex].round : 0));
            if (!s_inRun || s_roundIndex < 0 || s_roundIndex >= RoundCapacity) return;
            s_rounds[s_roundIndex].outOfWorldRemovals++;
        }

        public static void OnEnemyDied(EnemyDeathInfo info)
        {
            if (!s_inRun || s_roundIndex < 0 || s_roundIndex >= RoundCapacity)
            {
                return;
            }

            ref RoundRecord r = ref s_rounds[s_roundIndex];
            int tier = (int)info.Tier;
            int pieces = info.Reward.IsValid ? info.Reward.Armor.PieceCount : 0;
            switch (info.Tier)
            {
                case EnemyTier.Special: r.killsSpecial++; break;
                case EnemyTier.Elite: r.killsElite++; break;
                case EnemyTier.Ultra: r.killsUltra++; break;
                default: r.killsStandard++; break;
            }

            if (pieces > 0)
            {
                r.killsArmoured++;
                r.armourPiecesAtKill += pieces;
            }

            float first;
            int life = info.Meta.LifeId;
            if (life != 0 && s_firstHit.TryGetValue(life, out first))
            {
                s_firstHit.Remove(life);
                float seconds = Mathf.Max(0f, Time.time - first);
                switch (info.Tier)
                {
                    case EnemyTier.Special: r.ttkSumSpecial += seconds; r.ttkCountSpecial++; break;
                    case EnemyTier.Elite: r.ttkSumElite += seconds; r.ttkCountElite++; break;
                    case EnemyTier.Ultra: r.ttkSumUltra += seconds; r.ttkCountUltra++; break;
                    default: r.ttkSumStandard += seconds; r.ttkCountStandard++; break;
                }

                if (s_ttkCount < TtkCapacity)
                {
                    ref TtkSample t = ref s_ttk[s_ttkCount++];
                    t.round = r.round;
                    t.tier = tier;
                    t.armourPieces = pieces;
                    t.seconds = seconds;
                }
                else
                {
                    s_ttkDropped++;
                }
            }
        }

        public static void OnPlayerAttacked(EnemyAttackLandedInfo info)
        {
            if (s_roundIndex < 0 || s_roundIndex >= RoundCapacity)
            {
                return;
            }

            ref RoundRecord r = ref s_rounds[s_roundIndex];
            r.damageTaken += Mathf.Max(0f, info.Damage);
            r.hitsTaken++;
        }

        private static void OnSpecialSpawned(Vector3 position, float groundLifetime)
        {
            s_specialSpawns++;
        }

        private static void OnSpecialPickedUp(float activeSeconds)
        {
            s_specialPickups++;
        }

        private static void OnRunEnded(RunSummary summary)
        {
            EndRun(summary);
        }

        /// <summary>Closes the run, builds the document and writes it (allocation is fine here). Public for tests.</summary>
        public static RunTelemetryDocument EndRun(in RunSummary summary)
        {
            if (!s_inRun)
            {
                return null;
            }

            CloseRound(false);
            var doc = new RunTelemetryDocument
            {
                utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                build = Application.version,
                weapon = summary.Weapon.ToString(),
                endReason = summary.EndReason.ToString(),
                reachedRound = summary.ReachedRound,
                completedRounds = summary.CompletedRounds,
                runSeconds = Mathf.Max(0f, Time.time - s_runStart),
                totalKills = summary.TotalKills,
                goldEarned = summary.GoldEarned,
                goldSpent = summary.GoldSpent,
                upgradesPurchased = summary.UpgradesPurchased,
                rerollsUsed = summary.RerollsUsed,
                armorPiecesBroken = summary.ArmorPiecesBroken,
                completedRoundXp = summary.CompletedRoundXp,
                killXp = summary.KillXp,
                roundCount = s_roundIndex + 1,
                truncated = s_truncated,
                ttkDropped = s_ttkDropped
            };

            var rounds = new RoundRecord[Math.Max(0, s_roundIndex + 1)];
            for (int i = 0; i < rounds.Length; i++)
            {
                rounds[i] = s_rounds[i];
                if (rounds[i].tiersAtEnd == null)
                {
                    rounds[i].tiersAtEnd = new int[WeaponModifierState.BranchCount];
                }
            }

            doc.rounds = rounds;
            var ttk = new TtkSample[s_ttkCount];
            Array.Copy(s_ttk, ttk, s_ttkCount);
            doc.ttk = ttk;

            s_last = doc;
            s_inRun = false;
            s_lastPath = Write(doc);
            return doc;
        }

        private static string Write(RunTelemetryDocument doc)
        {
            try
            {
                string dir = Directory;
                System.IO.Directory.CreateDirectory(dir);
                string name = "run-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + (Environment.TickCount & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture) + ".json";
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, JsonUtility.ToJson(doc));
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Telemetry] Run telemetry not written: " + e.Message);
                return null;
            }
        }

        public static string ToJson(RunTelemetryDocument doc) => JsonUtility.ToJson(doc, true);

        public static RunTelemetryDocument FromJson(string json)
        {
            try
            {
                RunTelemetryDocument doc = JsonUtility.FromJson<RunTelemetryDocument>(json);
                return doc != null && doc.schema == Schema ? doc : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Editor / development seam: enable and redirect without command-line args.</summary>
        public static class TestSeam
        {
            public static void Configure(bool enabled, string directory)
            {
                s_enabled = enabled;
                s_commandLineOff = false;
                s_directory = directory;
                if (enabled)
                {
                    EnsureSubscribed();
                }
            }
        }
#endif
    }
}
