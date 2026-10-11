namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 · the per-frame area-query budget of the tree systems (<see cref="TreeGuardrails.OverlapQueriesPerFrame"/>).
    /// Every tree area test (blast, zone tick, hole pull, fire spread, Thunder Cascade bolt) asks <see cref="TryUse"/> first;
    /// a refused caller waits for the next frame (nothing is dropped: zone ticks, pulls and blasts stay due).
    /// The tests themselves are registry scans over the 64 status slots (<see cref="TreeStatusSystem.Query"/>): no PhysX,
    /// no collider dedupe, no allocation. The budget bounds the per-frame cost of the scans and of the damage they fan out.
    /// </summary>
    public static class TreeTargetQuery
    {
        /// <summary>Queries used in the frame in progress.</summary>
        public static int UsedThisFrame { get; private set; }

        /// <summary>Queries used in the last finished frame (F1).</summary>
        public static int LastFrameUsed { get; private set; }

        /// <summary>Highest per-frame use since the last reset (F1 / tests: never above the guardrail).</summary>
        public static int PeakPerFrame { get; private set; }

        /// <summary>Requests refused because the frame's budget was spent (they retry next frame).</summary>
        public static int Deferred { get; private set; }

        public static int Remaining => TreeGuardrails.OverlapQueriesPerFrame - UsedThisFrame;

        /// <summary>Takes one query from the frame budget; false when it is spent.</summary>
        public static bool TryUse()
        {
            if (UsedThisFrame >= TreeGuardrails.OverlapQueriesPerFrame)
            {
                Deferred++;
                return false;
            }

            UsedThisFrame++;
            if (UsedThisFrame > PeakPerFrame) PeakPerFrame = UsedThisFrame;
            return true;
        }

        internal static void BeginFrame()
        {
            LastFrameUsed = UsedThisFrame;
            UsedThisFrame = 0;
        }

        internal static void Reset()
        {
            UsedThisFrame = 0;
            LastFrameUsed = 0;
            PeakPerFrame = 0;
            Deferred = 0;
        }
    }
}
