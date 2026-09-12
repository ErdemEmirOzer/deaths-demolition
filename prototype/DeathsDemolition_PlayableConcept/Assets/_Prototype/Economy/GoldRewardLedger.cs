using System.Collections.Generic;

namespace DD.Prototype.Economy
{
    /// <summary>Why a kill did or did not earn a gold packet. <see cref="Granted"/> is the only yes.</summary>
    public enum GoldRewardVerdict
    {
        Granted = 0,

        /// <summary>No run is open: menus, results, a death event after the run ended.</summary>
        RunNotLive = 1,

        /// <summary>The enemy's life began before this run opened. A late event from a previous run.</summary>
        StaleLife = 2,

        /// <summary>This life was already rewarded. A second death callback for the same body.</summary>
        Duplicate = 3,

        /// <summary>The event carried no life id (0). Only a foreign producer could do this.</summary>
        InvalidLife = 4,

        /// <summary>The kill is worth nothing, so no packet exists for it.</summary>
        NoValue = 5
    }

    /// <summary>
    /// Reward eligibility, and nothing else: is THIS death, of THIS enemy life, in THIS
    /// run, entitled to exactly one gold packet?
    ///
    /// The single question is answered idempotently. A life id is claimed at most once
    /// per run; a life that began before the run opened (its id is at or below the
    /// watermark taken at <see cref="BeginRun"/>) is refused as stale; anything while no
    /// run is open is refused. Despawns, cleanups, aborts, pool returns and failed
    /// activations never reach this class at all - they raise no death event - so they
    /// are covered by construction, and the tests prove it by raising nothing.
    ///
    /// Pure: no Unity, no events, no allocation after the first run (the set is reused).
    /// </summary>
    public sealed class GoldRewardLedger
    {
        private const int InitialCapacity = 256;

        private readonly HashSet<int> _claimed = new HashSet<int>(InitialCapacity);

        /// <summary>Run the ledger is open for. 0 while none.</summary>
        public int RunId { get; private set; }

        public bool IsRunLive => RunId > 0;

        /// <summary>Highest enemy life id that existed when the run opened. Lives at or below it are stale.</summary>
        public int LifeWatermark { get; private set; }

        public int Granted { get; private set; }
        public int RejectedRunNotLive { get; private set; }
        public int RejectedStale { get; private set; }
        public int RejectedDuplicate { get; private set; }
        public int RejectedInvalid { get; private set; }
        public int RejectedNoValue { get; private set; }

        /// <summary>Every refusal this run, all reasons.</summary>
        public int Rejected => RejectedRunNotLive + RejectedStale + RejectedDuplicate + RejectedInvalid + RejectedNoValue;

        /// <summary>Opens the ledger for a run. Every claim from the previous run is forgotten.</summary>
        public void BeginRun(int runId, int lifeWatermark)
        {
            _claimed.Clear();
            RunId = runId > 0 ? runId : 1;
            LifeWatermark = lifeWatermark;
            ResetCounters();
        }

        /// <summary>Closes the ledger. Claims after this are refused until the next run.</summary>
        public void EndRun()
        {
            _claimed.Clear();
            RunId = 0;
            LifeWatermark = 0;
        }

        /// <summary>
        /// Claims the reward for one enemy life. Granted exactly once per life per run.
        /// <paramref name="value"/> is the packet's gold; a non-positive value is refused
        /// without consuming the life's claim.
        /// </summary>
        public GoldRewardVerdict TryClaim(int lifeId, int value)
        {
            if (!IsRunLive)
            {
                RejectedRunNotLive++;
                return GoldRewardVerdict.RunNotLive;
            }

            if (lifeId <= 0)
            {
                RejectedInvalid++;
                return GoldRewardVerdict.InvalidLife;
            }

            if (lifeId <= LifeWatermark)
            {
                RejectedStale++;
                return GoldRewardVerdict.StaleLife;
            }

            if (_claimed.Contains(lifeId))
            {
                RejectedDuplicate++;
                return GoldRewardVerdict.Duplicate;
            }

            if (value <= 0)
            {
                RejectedNoValue++;
                return GoldRewardVerdict.NoValue;
            }

            _claimed.Add(lifeId);
            Granted++;
            return GoldRewardVerdict.Granted;
        }

        /// <summary>True when this life already earned its packet this run.</summary>
        public bool IsClaimed(int lifeId) => _claimed.Contains(lifeId);

        private void ResetCounters()
        {
            Granted = 0;
            RejectedRunNotLive = 0;
            RejectedStale = 0;
            RejectedDuplicate = 0;
            RejectedInvalid = 0;
            RejectedNoValue = 0;
        }
    }
}
