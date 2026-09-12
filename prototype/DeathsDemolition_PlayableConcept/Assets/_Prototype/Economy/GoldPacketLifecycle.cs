namespace DD.Prototype.Economy
{
    /// <summary>Where one gold packet is in its life. Exactly one of these at a time.</summary>
    public enum GoldPacketState
    {
        /// <summary>Never begun, or returned to the pool. Holds no gold.</summary>
        Recycled = 0,

        /// <summary>Just dropped; the landing hop is playing. Auto-collect may still take it.</summary>
        Spawned = 1,

        /// <summary>Lying on the ground. Stays there until collected - it never expires.</summary>
        Waiting = 2,

        /// <summary>Inside the magnet radius, travelling to the player.</summary>
        Magnetizing = 3,

        /// <summary>Taken by the player (contact or magnet). Credited exactly once.</summary>
        Collected = 4,

        /// <summary>Taken by the round-end sweep. Credited exactly once.</summary>
        AutoCollected = 5
    }

    /// <summary>How a packet was taken. Maps 1:1 onto the two pickup <see cref="GoldSource"/>s.</summary>
    public enum GoldCollectKind
    {
        Manual = 0,
        RoundClearAuto = 1
    }

    /// <summary>
    /// The ownership state of one gold packet, kept apart from its visuals so the
    /// guarantees can be proved without a scene:
    ///
    ///   * <see cref="Begin"/> initialises a life once; a second Begin without a Recycle
    ///     is refused, so a pooled body cannot be double-armed;
    ///   * <see cref="TryCollect"/> succeeds at most once per life, from Spawned, Waiting
    ///     or Magnetizing only, so a manual pickup and the round-end sweep racing for the
    ///     same packet produce one credit, never two;
    ///   * <see cref="Recycle"/> ends the life from any state; a recycled packet holds no
    ///     gold, and the <see cref="Generation"/> counter lets any late caller tell an
    ///     old life from the body's new one;
    ///   * there is no timer here and no expiry state - gold on the ground stays there;
    ///   * the magnet clock only advances by the delta it is fed, so a paused frame
    ///     (delta 0) leaves it where it was.
    ///
    /// The transaction id the packet will credit under is fixed at Begin, so the wallet
    /// refuses a second credit for the same packet even if this guard were bypassed.
    /// </summary>
    public sealed class GoldPacketLifecycle
    {
        public GoldPacketState State { get; private set; } = GoldPacketState.Recycled;

        /// <summary>Gold this packet carries. 0 once recycled.</summary>
        public int Amount { get; private set; }

        /// <summary>Wallet transaction id reserved for this life. Unique within the run.</summary>
        public int TransactionId { get; private set; }

        /// <summary>Run this packet belongs to. A packet never crosses into another run.</summary>
        public int RunId { get; private set; }

        /// <summary>Round the packet dropped in. Diagnostics and the round-end sweep's report.</summary>
        public int Round { get; private set; }

        /// <summary>Rises by one on every Begin. A callback that remembers an older value is stale.</summary>
        public int Generation { get; private set; }

        /// <summary>Scaled seconds spent magnetizing. Does not advance on a zero delta. Observability for tests and the F1 panel; the body moves by its own delta.</summary>
        public float MagnetClock { get; private set; }

        public bool IsOutstanding =>
            State == GoldPacketState.Spawned || State == GoldPacketState.Waiting || State == GoldPacketState.Magnetizing;

        public bool IsTaken => State == GoldPacketState.Collected || State == GoldPacketState.AutoCollected;

        /// <summary>Starts a life. Refused while a life is already in progress or taken-but-not-recycled.</summary>
        public bool Begin(int transactionId, int amount, int runId, int round)
        {
            if (State != GoldPacketState.Recycled)
            {
                return false;
            }

            if (amount <= 0 || transactionId <= 0 || runId <= 0)
            {
                return false;
            }

            Generation++;
            TransactionId = transactionId;
            Amount = amount;
            RunId = runId;
            Round = round;
            MagnetClock = 0f;
            State = GoldPacketState.Spawned;
            return true;
        }

        /// <summary>The landing hop finished. Spawned → Waiting.</summary>
        public void Land()
        {
            if (State == GoldPacketState.Spawned)
            {
                State = GoldPacketState.Waiting;
            }
        }

        /// <summary>The player entered the magnet radius. Waiting → Magnetizing.</summary>
        public bool Attract()
        {
            if (State != GoldPacketState.Waiting)
            {
                return false;
            }

            State = GoldPacketState.Magnetizing;
            MagnetClock = 0f;
            return true;
        }

        /// <summary>Advances the magnet clock by a scaled delta. A paused frame feeds 0 and nothing moves.</summary>
        public void TickMagnet(float scaledDelta)
        {
            if (State == GoldPacketState.Magnetizing && scaledDelta > 0f)
            {
                MagnetClock += scaledDelta;
            }
        }

        /// <summary>
        /// Takes the packet. True exactly once per life, and only from an outstanding
        /// state; <paramref name="amount"/> is the gold to credit (0 when refused).
        /// </summary>
        public bool TryCollect(GoldCollectKind kind, out int amount)
        {
            if (!IsOutstanding)
            {
                amount = 0;
                return false;
            }

            State = kind == GoldCollectKind.RoundClearAuto ? GoldPacketState.AutoCollected : GoldPacketState.Collected;
            amount = Amount;
            return true;
        }

        /// <summary>
        /// Ends the life from any state. Returns true if gold was still outstanding, i.e.
        /// the packet was removed WITHOUT being credited (run reset).
        /// </summary>
        public bool Recycle()
        {
            bool lostGold = IsOutstanding;

            State = GoldPacketState.Recycled;
            Amount = 0;
            TransactionId = 0;
            RunId = 0;
            Round = 0;
            MagnetClock = 0f;
            return lostGold;
        }
    }
}
