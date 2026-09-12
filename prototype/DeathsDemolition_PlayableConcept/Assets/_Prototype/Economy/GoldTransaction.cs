using System;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// Why gold moved. Every wallet transaction carries exactly one of these, so the
    /// ledger can be read back without guessing (WP-004). The five product sources are
    /// the approved list; nothing else is added here without a product decision.
    /// </summary>
    public enum GoldSource
    {
        /// <summary>A packet the player walked into or the magnet pulled in.</summary>
        EnemyPickup = 0,

        /// <summary>A packet still on the ground when the round completed (REQ-076).</summary>
        RoundClearAutoCollect = 1,

        /// <summary>A round-end upgrade card. The only spend source.</summary>
        UpgradePurchase = 2,

        /// <summary>
        /// The run opened. Reserved for a transaction log of the run boundary; today the
        /// boundary is a wipe (<c>GoldWallet.InitializeForRun</c>) and writes no transaction.
        /// </summary>
        RunInitialization = 3,

        /// <summary>
        /// The run ended. Reserved likewise; the boundary is <c>GoldWallet.ResetForRunEnd</c>
        /// (REQ-018) and writes no transaction.
        /// </summary>
        RunReset = 4
    }

    /// <summary>
    /// Why a wallet transaction was refused. <see cref="Applied"/> is the only success.
    /// </summary>
    public enum GoldTransactionResult
    {
        Applied = 0,

        /// <summary>Amount was zero or negative.</summary>
        InvalidAmount = 1,

        /// <summary>The same transaction id was already applied this run.</summary>
        Duplicate = 2,

        /// <summary>A spend larger than the balance. Nothing changed.</summary>
        InsufficientBalance = 3,

        /// <summary>No run is open on the wallet.</summary>
        NoRun = 4
    }

    /// <summary>
    /// The wallet as one value. Published on every change so a HUD or a screen never has
    /// to keep a balance of its own (the wallet is the only authority).
    /// </summary>
    public readonly struct GoldWalletSnapshot : IEquatable<GoldWalletSnapshot>
    {
        /// <summary>Run this wallet belongs to. 0 outside a run.</summary>
        public readonly int RunId;

        public readonly int Balance;

        /// <summary>Everything credited this run, spent or not.</summary>
        public readonly int TotalEarned;

        public readonly int TotalSpent;

        /// <summary>Gold that arrived through packets the player collected.</summary>
        public readonly int EarnedByPickup;

        /// <summary>Gold that arrived through round-end auto-collection.</summary>
        public readonly int EarnedByAutoCollect;

        /// <summary>Transactions applied this run. Rises by one per accepted transaction.</summary>
        public readonly int Version;

        public GoldWalletSnapshot(int runId, int balance, int totalEarned, int totalSpent,
            int earnedByPickup, int earnedByAutoCollect, int version)
        {
            RunId = runId;
            Balance = balance;
            TotalEarned = totalEarned;
            TotalSpent = totalSpent;
            EarnedByPickup = earnedByPickup;
            EarnedByAutoCollect = earnedByAutoCollect;
            Version = version;
        }

        public bool Equals(GoldWalletSnapshot other)
        {
            return RunId == other.RunId && Balance == other.Balance && TotalEarned == other.TotalEarned &&
                   TotalSpent == other.TotalSpent && EarnedByPickup == other.EarnedByPickup &&
                   EarnedByAutoCollect == other.EarnedByAutoCollect && Version == other.Version;
        }

        public override bool Equals(object obj) => obj is GoldWalletSnapshot other && Equals(other);

        public override int GetHashCode() => (RunId * 397) ^ Version;
    }
}
