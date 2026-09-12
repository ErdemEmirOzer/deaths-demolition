namespace DD.Prototype.Rounds
{
    /// <summary>
    /// What the round-end screen opened with (WP-004): the balance after the sweep, what
    /// the sweep took, and the build at that moment. Captured by <see cref="RunDirector"/>
    /// the instant the upgrade screen opens, so a test or a readout can prove the
    /// screen's balance already contained the auto-collected gold.
    /// </summary>
    public readonly struct RoundEndEconomySnapshot
    {
        public readonly int RunId;

        /// <summary>The round that was just completed.</summary>
        public readonly int Round;

        /// <summary>Wallet balance before the round-end sweep ran.</summary>
        public readonly int BalanceBeforeSweep;

        /// <summary>Packets the sweep took off the floor.</summary>
        public readonly int SweptPackets;

        /// <summary>Gold those packets carried.</summary>
        public readonly int SweptAmount;

        /// <summary>Wallet balance the upgrade screen opened with. Includes the sweep.</summary>
        public readonly int BalanceAtOpen;

        /// <summary>Shop session the screen belongs to; a purchase must quote it.</summary>
        public readonly int ShopSessionId;

        /// <summary>Build identity at open, e.g. "P1 / FR2". "Stock" for none.</summary>
        public readonly string BuildDescription;

        public RoundEndEconomySnapshot(int runId, int round, int balanceBeforeSweep, int sweptPackets,
            int sweptAmount, int balanceAtOpen, int shopSessionId, string buildDescription)
        {
            RunId = runId;
            Round = round;
            BalanceBeforeSweep = balanceBeforeSweep;
            SweptPackets = sweptPackets;
            SweptAmount = sweptAmount;
            BalanceAtOpen = balanceAtOpen;
            ShopSessionId = shopSessionId;
            BuildDescription = buildDescription;
        }

        public bool IsValid => RunId > 0 && Round > 0;
    }
}
