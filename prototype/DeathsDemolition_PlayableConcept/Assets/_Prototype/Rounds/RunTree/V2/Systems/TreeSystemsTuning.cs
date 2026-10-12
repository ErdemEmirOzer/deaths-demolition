using DD.Prototype.Core.Config;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B3 · the tree systems' own numbers that are not node values (those live in <see cref="TreeStats"/>, resolved
    /// from run-tree-v2) and not perf caps (those live in <see cref="TreeGuardrails"/>). Design rules quote GD-TREE; the rest
    /// are system choices marked invented. One place, so no system repeats a literal (O7).
    /// </summary>
    public static class TreeSystemsTuning
    {
        // ------------------------------------------------------------------ geometry

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: horizontal reach added to every tree area test (the body is not a point; DamageTargetRegistry uses the body radius, the receiver contract carries none).", true)]
        public const float BodyReach = 0.4f;

        // ------------------------------------------------------------------ status (GD-TREE §2.4)

        [PrototypeOnly(PendingDecisions.None, "GD-TREE el_fire3 Inferno: a burning body spreads fire to one neighbour per second.")]
        public const float FireSpreadInterval = 1f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE el_ice3 Shatter: the ice blast leaves 2 cold stacks on the bodies around.")]
        public const int ShatterIceStacks = 2;

        // ------------------------------------------------------------------ areas (GD-TREE §2.4, §6)

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: zone damage tick (s), the RunTreeGroundFire tick (WP-077 K10b).")]
        public const float AreaTickSeconds = 0.5f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE el_venom2 Plague Cloud: 2 venom stacks per second inside the cloud.")]
        public const float VenomCloudStacksPerSecond = 2f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE sy_wildfire_rounds: an explosive round leaves a 1.5 m fire ground for 2 s.")]
        public const float WildfireRoundsRadius = 1.5f, WildfireRoundsSeconds = 2f;

        // ------------------------------------------------------------------ blasts (GD-TREE §2.5, §6)

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: blasts closer than this are fused when the frame / second budget is exceeded (K6: damage summed, radius max).", true)]
        public const float BlastMergeDistance = 1.5f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE §8.3: a blast that waited this long past the 12/s cap lands as damage only (no VFX / audio proc).")]
        public const float BlastMaxWaitSeconds = 0.25f;

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: a blast reaching more bodies than this spreads its application over TreeGuardrails.MassDeathSpreadFrames frames.", true)]
        public const int MassDeathChunk = 8;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE kx_chain: chance halves per generation when the node gives no falloff.")]
        public const float ChainFalloffDefault = 0.5f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE sy_thunder_cascade: chance ×0.7 per generation; each death blast throws 2 bolts of 0.25×SG. Bolt reach is invented.")]
        public const float ThunderCascadeFalloff = 0.7f, ThunderCascadeShare = 0.25f, ThunderCascadeReach = 6f;
        public const int ThunderCascadeBolts = 2;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE sy_toxic_rail: a round that already pierced leaves 2 venom stacks on every body.")]
        public const int ToxicRailStacks = 2;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE sy_meltdown: a critical hit counts 3 toward the nuke.")]
        public const float MeltdownCritCount = 3f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE sy_barrage: every 4th rocket splits into 3 parts of 0.3× the rocket's damage, 1.5 m. Part offset is invented.")]
        public const int BarrageEvery = 4, BarrageParts = 3;
        public const float BarragePartShare = 0.3f, BarragePartRadius = 1.5f, BarragePartOffset = 1.2f;

        // ------------------------------------------------------------------ black hole (GD-TREE kx_hole, sy_frozen_gravity)

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: pull tick (10 Hz, GDEV §3); how long one pull holds on the body (1.5 pull intervals, S-03 plan ≈0.15 s); pull speed when the catalog gives no HolePullSpeed (the retired Tide cap 3.2, rounded). Tier shares and the core dead zone are the body's (TreeStats HolePull*Share).", true)]
        public const float HolePullHz = 10f, HolePullHoldSeconds = 0.15f, HolePullSpeedFallback = 3f;

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: the hole's damage tick (s); HoleShare is per body per second.", true)]
        public const float HoleDamageTickSeconds = 0.5f;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE §8.3: black holes active with the Twin Cores awakening (both cores owned).")]
        public const int TwinCoresBlackHoles = 2;

        [PrototypeOnly(PendingDecisions.None, "GD-TREE sy_frozen_gravity: bodies inside freeze after 1 s; the collapse hits frozen bodies ×1.5 and throws a 3 m ice blast of 1.0×SG.")]
        public const float FrozenGravityAfter = 1f, FrozenGravityCollapseBonus = 1.5f, FrozenGravityIceRadius = 3f, FrozenGravityIceShare = 1f;

        // ------------------------------------------------------------------ anchor (player) pulses

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: how often a ready Element Nova / Doomsday Bell looks for a body in range (s).", true)]
        public const float AnchorCheckSeconds = 0.25f;
    
        /// <summary>WP-104 guard waves (Stone Guardian shield break, Undying Oath): how far beyond the body the push aims, its hold
        /// and its speed (PrototypeOnly tuning; the radii and stun come from the nodes).</summary>
        public const float GuardWavePushDistance = 3f, GuardWavePushSeconds = 0.3f, GuardWavePushSpeed = 9f;
}
}
