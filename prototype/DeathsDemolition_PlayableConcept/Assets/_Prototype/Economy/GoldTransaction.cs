using System;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// Why gold moved. Every wallet transaction carries exactly one of these, so the
    /// ledger can be read back without guessing (WP-004). The five WP-004 sources plus
    /// the WP-008 reroll (DEC-050 / REQ-079) are the approved list; nothing else is
    /// added here without a product decision.
    /// </summary>
    public enum GoldSource
    {
        /// <summary>A packet the player walked into or the magnet pulled in.</summary>
        EnemyPickup = 0,

        /// <summary>A packet still on the ground when the round completed (REQ-076).</summary>
        RoundClearAutoCollect = 1,

        /// <summary>A round-end upgrade card. One of the two spend sources.</summary>
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
        RunReset = 4,

        /// <summary>
        /// The one paid reroll of a round-end offer (WP-008, DEC-050 / REQ-079). The other
        /// spend source. Results report it INSIDE "Gold Spent" together with purchases
        /// (product decision 19/20); the wallet keeps the split for tests and probes.
        /// </summary>
        OfferReroll = 5,

        /// <summary>
        /// WP-077 Purse ("Kese"): +10 % of the gold picked up during a round, paid when the round-end
        /// offer opens. An income source like a pickup (counts toward TotalEarned), never a refund.
        /// </summary>
        PurseBonus = 6,

        /// <summary>WP-077 Hearth ("Ocak"): a direct tree purchase outside the card limit. A spend source reported inside "Gold Spent".</summary>
        TreeHearth = 7,

        /// <summary>WP-077 K10 Reforge ("Yeniden Dövme"): swapping an owned fork for its partner, at the partner's price, no refund.</summary>
        TreeReforge = 8
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
        NoRun = 4,

        /// <summary>WP-077 K10: a refund of a spend this wallet has no record of (never applied, or already refunded).</summary>
        UnknownTransaction = 5
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

        /// <summary>Gold spent on upgrade cards (WP-008 split of <see cref="TotalSpent"/>).</summary>
        public readonly int SpentOnUpgrades;

        /// <summary>Gold spent on offer rerolls (WP-008 split of <see cref="TotalSpent"/>).</summary>
        public readonly int SpentOnRerolls;

        /// <summary>Transactions applied this run. Rises by one per accepted transaction.</summary>
        public readonly int Version;

        public GoldWalletSnapshot(int runId, int balance, int totalEarned, int totalSpent,
            int earnedByPickup, int earnedByAutoCollect, int version)
            : this(runId, balance, totalEarned, totalSpent, earnedByPickup, earnedByAutoCollect, totalSpent, 0, version)
        {
        }

        public GoldWalletSnapshot(int runId, int balance, int totalEarned, int totalSpent,
            int earnedByPickup, int earnedByAutoCollect, int spentOnUpgrades, int spentOnRerolls, int version)
        {
            RunId = runId;
            Balance = balance;
            TotalEarned = totalEarned;
            TotalSpent = totalSpent;
            EarnedByPickup = earnedByPickup;
            EarnedByAutoCollect = earnedByAutoCollect;
            SpentOnUpgrades = spentOnUpgrades;
            SpentOnRerolls = spentOnRerolls;
            Version = version;
        }

        public bool Equals(GoldWalletSnapshot other)
        {
            return RunId == other.RunId && Balance == other.Balance && TotalEarned == other.TotalEarned &&
                   TotalSpent == other.TotalSpent && EarnedByPickup == other.EarnedByPickup &&
                   EarnedByAutoCollect == other.EarnedByAutoCollect && SpentOnUpgrades == other.SpentOnUpgrades &&
                   SpentOnRerolls == other.SpentOnRerolls && Version == other.Version;
        }

        public override bool Equals(object obj) => obj is GoldWalletSnapshot other && Equals(other);

        public override int GetHashCode() => (RunId * 397) ^ Version;
    }
}
