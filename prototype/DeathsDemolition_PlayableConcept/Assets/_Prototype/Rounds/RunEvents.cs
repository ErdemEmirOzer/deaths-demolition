using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>Where the run currently is. Exactly one of these is true at a time.</summary>
    public enum RunPhase
    {
        /// <summary>No run is live. Menus, results screen, editor scene opened directly.</summary>
        None = 0,

        /// <summary>The round is being fought. This is the only phase the simulation runs in freely.</summary>
        Fighting = 1,

        /// <summary>Pool empty, map clear. Simulation frozen, "ROUND COMPLETE" beat playing.</summary>
        RoundComplete = 2,

        /// <summary>The round-end upgrade screen is open. Simulation still frozen.</summary>
        Upgrading = 3,

        /// <summary>Upgrade screen closed, 3 s countdown to the next round. Simulation running again.</summary>
        Countdown = 4,

        /// <summary>The player died. The run is over; there is no revive.</summary>
        Dead = 5
    }

    /// <summary>Why a run ended. Carried on <see cref="RunSummary"/>.</summary>
    public enum RunEndReason
    {
        /// <summary>Health reached zero (REQ-048). The normal ending.</summary>
        PlayerDeath = 0,

        /// <summary>The player quit to the main menu from the pause screen.</summary>
        Aborted = 1,

        /// <summary>Ended from the F1 panel. Never happens in normal play.</summary>
        Debug = 2
    }

    /// <summary>
    /// Everything the HUD needs about the run, refreshed every frame by
    /// <see cref="RunDirector"/>. Poll it; do not subscribe to redraw a number.
    ///
    /// Same idiom as <c>WeaponEvents.Classic</c> / <c>WeaponEvents.Special</c> on
    /// purpose: a snapshot for per-frame labels, events for the moments worth
    /// animating.
    /// </summary>
    public struct RunHudState
    {
        // ---- the eleven fields the HUD contract asks for ---------------------

        /// <summary>1-based round number.</summary>
        public int Round;

        /// <summary>Enemies of this round's pool that have not been released yet.</summary>
        public int PoolRemaining;

        /// <summary>Enemies alive on the map right now.</summary>
        public int AliveEnemies;

        /// <summary>Gold in the run wallet. Reset to zero when the run ends.</summary>
        public int Gold;

        /// <summary>Kills so far this run, all tiers.</summary>
        public int RunKills;

        public int StandardKills;
        public int SpecialKills;
        public int EliteKills;
        public int UltraKills;

        public RunPhase Phase;

        /// <summary>Seconds left on the pre-round countdown. 0 outside <see cref="RunPhase.Countdown"/>.</summary>
        public float CountdownRemaining;

        // ---- additive fields, safe to ignore ---------------------------------

        /// <summary>Total pool size of this round, from PrototypeConfig.TotalEnemyPool(round).</summary>
        public int PoolTotal;

        /// <summary>
        /// Kills this round divided by the round's pool. This is the same number the
        /// Special Weapon director schedules against; it is kill progress, never elapsed
        /// time, because a round ends when the pool is emptied.
        /// </summary>
        public float RoundProgress01;

        /// <summary>Kills scored inside the current round only.</summary>
        public int RoundKills;

        /// <summary>
        /// True once the round's pool is empty and only the stragglers on the map are
        /// left. The HUD may switch "Enemies Remaining" to a "clear the map" state.
        /// </summary>
        public bool PoolExhausted;

        /// <summary>True while <see cref="RunFlowClock"/> is holding the simulation still.</summary>
        public bool SimulationFrozen;

        /// <summary>Seconds of run time elapsed, paused time excluded.</summary>
        public float RunTime;

        /// <summary>The request's HUD line: pool left plus alive.</summary>
        public int EnemiesRemaining => PoolRemaining + AliveEnemies;

        public bool IsRunLive => Phase != RunPhase.None && Phase != RunPhase.Dead;
    }

    /// <summary>
    /// What the Run Results screen is built from.
    ///
    /// COVERAGE OF THE REQUEST'S TWELVE RESULT LINES
    ///   1  Reached Round ........... <see cref="ReachedRound"/>
    ///   2  Total Kills ............. <see cref="TotalKills"/>
    ///   3  Standard Kills .......... <see cref="StandardKills"/>
    ///   4  Special Kills ........... <see cref="SpecialKills"/>
    ///   5  Elite Kills ............. <see cref="EliteKills"/>
    ///   6  Ultra Kills ............. <see cref="UltraKills"/>
    ///   7  Completed Round XP ...... <see cref="CompletedRoundXp"/>
    ///   8  Kill XP ................. <see cref="KillXp"/>
    ///   9  Total XP Earned ......... <see cref="TotalXpEarned"/>
    ///  10  Profile Level Progress .. the profile system computes it from
    ///                                <see cref="CompletedRoundXp"/> + <see cref="KillXp"/>
    ///                                (WP-009: <see cref="TotalXpEarned"/> is informational and
    ///                                is never read as an XP source); this struct is its input.
    ///  11  Newly Unlocked Content .. same, from the level the XP produces.
    ///  12  Achievements Earned ..... same, from <see cref="ReachedRound"/>,
    ///                                <see cref="UltraKills"/>, <see cref="TotalKills"/> and
    ///                                <see cref="ArmorPiecesBroken"/> - the four prototype
    ///                                achievements need exactly those four facts.
    ///
    /// Lines 10-12 are deliberately NOT computed here. This task owns the run; the
    /// profile owns what a run does to a profile, and a run summary that already knew
    /// the player's level would have made the profile system's job unfalsifiable.
    /// Everything those three lines need is present as an input.
    /// </summary>
    public struct RunSummary
    {
        // ---- rounds ----------------------------------------------------------

        /// <summary>Highest round the player was fighting in, completed or not.</summary>
        public int ReachedRound;

        /// <summary>Rounds actually finished. An unfinished round grants no round XP.</summary>
        public int CompletedRounds;

        // ---- kills -----------------------------------------------------------

        public int TotalKills;
        public int StandardKills;
        public int SpecialKills;
        public int EliteKills;
        public int UltraKills;

        // ---- xp --------------------------------------------------------------

        /// <summary>Sum of PrototypeConfig.RoundCompletionXp(r) over completed rounds only.</summary>
        public int CompletedRoundXp;

        /// <summary>Sum of PrototypeConfig.KillXp(tier) over every kill, half-round kills included.</summary>
        public int KillXp;

        /// <summary>CompletedRoundXp + KillXp. Gold and playtime contribute nothing.</summary>
        public int TotalXpEarned;

        // ---- achievement evidence (feeds result line 12) ----------------------

        /// <summary>Armour plates broken this run. "Armor Breaker" needs this.</summary>
        public int ArmorPiecesBroken;

        /// <summary>Highest enemy tier killed this run. Convenience over the per-tier counts.</summary>
        public EnemyTier HighestTierKilled;

        // ---- run identity ----------------------------------------------------

        /// <summary>
        /// Opaque completion capability of this run, issued by the profile's identity
        /// authority when the run starts (WP-009H: profile lineage + the next monotonic
        /// ordinal + a one-time nonce; WP-009's random GUID is retired). The ONLY thing
        /// the profile uses to accept a summary exactly once - in this process, after a
        /// reload, after a relaunch, however many runs later; the session-local
        /// <see cref="RunDirector.RunId"/> is not an identity. Null or empty means "no
        /// identity": such a summary is never folded into the profile. Shown nowhere; not
        /// a timestamp; not a run history.
        /// </summary>
        public string CompletionId;

        /// <summary>The classic weapon carried for the whole run.</summary>
        public ClassicWeaponId Weapon;

        /// <summary>Build identity from WeaponModifierState.DescribeBuild(), e.g. "P2 / FR1".</summary>
        public string BuildDescription;

        /// <summary>Gold picked up across the run. Informational; grants no XP.</summary>
        public int GoldEarned;

        /// <summary>
        /// Gold spent this run: upgrade cards PLUS paid offer rerolls (WP-008, product
        /// decision 19). Results shows this one number; there is no separate reroll line
        /// (decision 20). The split below exists for tests, probes and the F1 panel.
        /// </summary>
        public int GoldSpent;

        /// <summary>WP-008: the part of <see cref="GoldSpent"/> that went to upgrade cards.</summary>
        public int GoldSpentOnUpgrades;

        /// <summary>WP-008: the part of <see cref="GoldSpent"/> that went to paid rerolls.</summary>
        public int GoldSpentOnRerolls;

        /// <summary>Gold still unspent when the run ended. It is destroyed with the run.</summary>
        public int GoldUnspent;

        /// <summary>Upgrade cards bought across the run.</summary>
        public int UpgradesPurchased;

        /// <summary>WP-008: paid rerolls used across the run.</summary>
        public int RerollsUsed;

        /// <summary>WP-077: in-run tree keystones owned at the end of the run (branch keystones + weapon seal).</summary>
        public int TreeKeystones;

        /// <summary>WP-077: in-run tree capstones owned at the end of the run.</summary>
        public int TreeCapstones;

        /// <summary>WP-077: Hearth stacks bought during the run.</summary>
        public int TreeHearthStacks;

        /// <summary>WP-077: Special Link II owned at the end of the run (0/1).</summary>
        public int TreeLinkII;

        /// <summary>Seconds of run time, paused and frozen time excluded.</summary>
        public float RunDuration;

        public RunEndReason EndReason;

        /// <summary>SourceId of the killing blow when the run ended in death, else null.</summary>
        public string KilledBy;

        public int KillsFor(EnemyTier tier)
        {
            switch (tier)
            {
                case EnemyTier.Standard: return StandardKills;
                case EnemyTier.Special: return SpecialKills;
                case EnemyTier.Elite: return EliteKills;
                case EnemyTier.Ultra: return UltraKills;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// The read-only run surface for the HUD (G-08), the results screen and the
    /// profile system.
    ///
    /// Two access styles, the same split the weapon surface uses:
    ///
    ///   * <see cref="Current"/> is a snapshot refreshed every frame by
    ///     <see cref="RunDirector"/>. Round number, gold, enemies remaining and the
    ///     countdown label all read it in their own Update. Raising an event sixty
    ///     times a second so a HUD can print a number is waste.
    ///   * The events fire on CHANGES only - round start, round complete, gold change,
    ///     phase change, run end. Those are the moments worth animating.
    ///
    /// Everything named Publish* is written by the run and economy systems. Consumers
    /// must treat them as private.
    /// </summary>
    public static class RunEvents
    {
        /// <summary>Current run state. Refreshed every frame while a run is live.</summary>
        public static RunHudState Current { get; private set; }

        /// <summary>The last completed run's summary. Survives the scene change to Run Results.</summary>
        public static RunSummary LastRunSummary { get; private set; }

        // ------------------------------------------------------------------ events

        /// <summary>A run began. Argument is the starting round (1 in normal play).</summary>
        public static event Action<int> RunStarted;

        /// <summary>A round began and the pool started flowing. Argument is the round number.</summary>
        public static event Action<int> RoundStarted;

        /// <summary>The pool is empty and the map is clear. Argument is the round that ended.</summary>
        public static event Action<int> RoundCompleted;

        /// <summary>
        /// The pool ran dry but enemies are still alive. The HUD can switch its
        /// "Enemies Remaining" readout to the last-stand state here.
        /// </summary>
        public static event Action<int> RoundPoolExhausted;

        /// <summary>Run gold changed. Argument is the new balance.</summary>
        public static event Action<int> GoldChanged;

        /// <summary>The run moved to a new phase. This is the screen-driving event.</summary>
        public static event Action<RunPhase> PhaseChanged;

        /// <summary>
        /// Fired once per second boundary of the pre-round countdown, 3 then 2 then 1.
        /// A HUD that only wants the number can read Current.CountdownRemaining instead.
        /// </summary>
        public static event Action<int> CountdownTick;

        /// <summary>The run is over. Run Results is built from this payload.</summary>
        public static event Action<RunSummary> RunEnded;

        // ------------------------------------------------------------------ producers

        public static void PublishState(in RunHudState state)
        {
            Current = state;
        }

        /// <summary>
        /// Gold is published by the economy rather than waiting for the director's next
        /// frame, so a collected orb and the HUD counter move together.
        /// </summary>
        public static void PublishGold(int balance)
        {
            RunHudState state = Current;
            if (state.Gold == balance)
            {
                return;
            }

            state.Gold = balance;
            Current = state;

            Invoke(GoldChanged, balance);
        }

        public static void PublishRunStarted(int startRound) => Invoke(RunStarted, startRound);

        public static void PublishRoundStarted(int round) => Invoke(RoundStarted, round);

        public static void PublishRoundCompleted(int round) => Invoke(RoundCompleted, round);

        public static void PublishRoundPoolExhausted(int round) => Invoke(RoundPoolExhausted, round);

        public static void PublishPhaseChanged(RunPhase phase) => Invoke(PhaseChanged, phase);

        public static void PublishCountdownTick(int secondsLeft) => Invoke(CountdownTick, secondsLeft);

        public static void PublishRunEnded(in RunSummary summary)
        {
            LastRunSummary = summary;
            InvokeEachIsolated(RunEnded, summary);
        }

        /// <summary>Called by the director when a run starts, so a stale HUD cannot linger.</summary>
        public static void ClearState()
        {
            Current = default;
        }

        /// <summary>
        /// RunEnded only (WP-009): every subscriber runs even if an earlier one throws, so
        /// the profile commit (<c>ProfileRunCommitter</c>) cannot be skipped by a broken
        /// screen listener. Once per run, so the invocation-list copy is not a hot-path cost.
        /// </summary>
        private static void InvokeEachIsolated(Action<RunSummary> handler, RunSummary argument)
        {
            if (handler == null)
            {
                return;
            }

            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<RunSummary>)list[i]).Invoke(argument);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private static void Invoke<T>(Action<T> handler, T argument)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(argument);
            }
            catch (Exception e)
            {
                // A broken HUD listener must never stop a round from ending.
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = default;
            LastRunSummary = default;

            RunStarted = null;
            RoundStarted = null;
            RoundCompleted = null;
            RoundPoolExhausted = null;
            GoldChanged = null;
            PhaseChanged = null;
            CountdownTick = null;
            RunEnded = null;
        }
    }
}
