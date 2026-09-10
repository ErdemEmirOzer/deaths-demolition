using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// PROTOTYPE-ONLY tuning of the custom crowd layer.
    ///
    /// WHY IT IS NOT IN PrototypeConfig
    /// --------------------------------
    /// Assets/_Prototype/Core is owned by another task in this wave, so this task may not
    /// add a CrowdConfigSection to PrototypeConfig. Everything here is therefore serialized
    /// on <see cref="NavigationSystemBehaviour"/> and still edits live (the behaviour
    /// re-reads it every tick). When Core reopens, this block should move into
    /// PrototypeConfig as-is under the name "Crowd".
    ///
    /// EVERY NUMBER BELOW IS INVENTED. The request specifies the BEHAVIOUR ("soft
    /// separation", "light pushing", "no total traffic lock", "zombies can physically
    /// block the player") but no coefficients whatsoever.
    /// </summary>
    [Serializable]
    public sealed class CrowdTuning
    {
        [Header("Soft separation between members (REQ-047 / DEC-033)")]
        [PrototypeOnly(PendingDecisions.None,
            "Multiplier on (radiusA + radiusB) that defines 'too close'. 1 = bodies just touch.", true)]
        [Range(0.6f, 1.6f)] public float SpacingScale = 1.05f;

        [PrototypeOnly(PendingDecisions.None,
            "Separation stiffness, 1/s. Overlap of 1 m produces this many m/s of correction.", true)]
        public float SeparationStiffness = 7f;

        [PrototypeOnly(PendingDecisions.None,
            "Ceiling on the separation speed produced by a single neighbour pair, m/s.", true)]
        public float MaxSeparationSpeed = 2.2f;

        [PrototypeOnly(PendingDecisions.None,
            "Damping on the closing speed of a pair. Stops contact chatter without freezing the flow.", true)]
        [Range(0f, 1f)] public float ApproachDamping = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Sideways slide added to a head-on pair. This is the anti-deadlock term: the two " +
            "members deterministically pick opposite sides, so a narrow bridge flows instead of locking.", true)]
        [Range(0f, 1.5f)] public float TangentialBias = 0.6f;

        [PrototypeOnly(PendingDecisions.None,
            "Neighbours considered per member per tick. A hard cap on the worst case, not a quality knob.", true)]
        [Range(4, 24)] public int MaxNeighborsPerMember = 12;

        [Header("Player contact (REQ-046 / DEC-032)")]
        [PrototypeOnly(PendingDecisions.None,
            "Extra clearance kept around the player capsule, m.", true)]
        public float PlayerContactPadding = 0.06f;

        [PrototypeOnly(PendingDecisions.None,
            "Fraction of a member's inward speed cancelled at the player's surface. " +
            "1 = it stops dead against the player and keeps standing in the way. " +
            "This is a BRAKE, never a push: pushing would let the player plough through the swarm.", true)]
        [Range(0f, 1f)] public float PlayerBraceStrength = 1f;

        [PrototypeOnly(PendingDecisions.None,
            "Stiffness of the slow depenetration used only when a member is ALREADY inside the " +
            "player capsule (spawn overlap, teleport). Deliberately weak.", true)]
        public float PlayerDepenetrationStiffness = 6f;

        [PrototypeOnly(PendingDecisions.None,
            "Ceiling on that depenetration, m/s. Must stay far below the player's 6.2 m/s or the " +
            "player would shove the crowd aside and REQ-046 would collapse.", true)]
        public float MaxPlayerDepenetrationSpeed = 0.9f;

        [Header("Output")]
        [PrototypeOnly(PendingDecisions.None,
            "Ceiling on the total correction handed to ApplySteering, m/s. Keeps the crowd layer " +
            "from ever dominating a member's own locomotion.", true)]
        public float MaxCorrectionSpeed = 2.8f;

        [PrototypeOnly(PendingDecisions.None,
            "Corrections below this are delivered as zero, so idle members cost nothing downstream.", true)]
        public float CorrectionDeadzone = 0.02f;

        [Header("Budget")]
        [PrototypeOnly(PendingDecisions.None,
            "Spatial hash cell size, m. Should be about 2x the largest body radius (Executioner 0.9).", true)]
        public float CellSize = 1.8f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds between solver ticks. 0 = every frame. Raise it only to buy frame time back.", true)]
        [Range(0f, 0.05f)] public float SolveInterval = 0f;

        [PrototypeOnly(PendingDecisions.None,
            "Smoothing on the measured per-member velocity used for damping and the player brace.", true)]
        [Range(0f, 1f)] public float VelocitySmoothing = 0.35f;

        /// <summary>Clamps everything into a sane range after a live edit.</summary>
        public void Sanitize()
        {
            SpacingScale = Mathf.Clamp(SpacingScale, 0.6f, 1.6f);
            SeparationStiffness = Mathf.Max(0f, SeparationStiffness);
            MaxSeparationSpeed = Mathf.Max(0f, MaxSeparationSpeed);
            ApproachDamping = Mathf.Clamp01(ApproachDamping);
            TangentialBias = Mathf.Clamp(TangentialBias, 0f, 1.5f);
            MaxNeighborsPerMember = Mathf.Clamp(MaxNeighborsPerMember, 4, 24);
            PlayerContactPadding = Mathf.Max(0f, PlayerContactPadding);
            PlayerBraceStrength = Mathf.Clamp01(PlayerBraceStrength);
            PlayerDepenetrationStiffness = Mathf.Max(0f, PlayerDepenetrationStiffness);
            MaxPlayerDepenetrationSpeed = Mathf.Max(0f, MaxPlayerDepenetrationSpeed);
            MaxCorrectionSpeed = Mathf.Max(0.01f, MaxCorrectionSpeed);
            CorrectionDeadzone = Mathf.Max(0f, CorrectionDeadzone);
            CellSize = Mathf.Max(0.5f, CellSize);
            SolveInterval = Mathf.Clamp(SolveInterval, 0f, 0.05f);
            VelocitySmoothing = Mathf.Clamp01(VelocitySmoothing);
        }
    }

    /// <summary>What the solver knows about the player. Filled by the crowd runtime.</summary>
    public struct CrowdPlayerState
    {
        public bool HasPlayer;
        public Vector3 Position;
        public float Radius;
        public Vector3 Velocity;
    }
}
