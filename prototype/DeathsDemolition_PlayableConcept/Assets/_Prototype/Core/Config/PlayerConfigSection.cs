using System;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>PROTOTYPE-ONLY player values. Every number here is placeholder.</summary>
    [Serializable]
    public sealed class PlayerConfigSection
    {
        /// <summary>
        /// C-00 01.10 (user decision): running backward — against the way the character faces — is ×0.65 (6.2 → ≈ 4.03 m/s). AP-022's
        /// run_back carries ≈ 4.3 m/s; at 6.2 the feet slid 30 % (TP budget 20 %, S-06). The one source: PlayerMotor.BackwardScale
        /// applies it, S-06's StrideEslemesi and the run_back blend radius read it. Strafe is unchanged.
        /// </summary>
        public const float BackwardSpeedMultiplier = 0.65f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 100")]
        public float MaxHealth = 100f;

        [PrototypeOnly(PendingDecisions.None, "Request value: 6.2 m/s")]
        public float MoveSpeed = 6.2f;

        /// <summary>
        /// How fast speed is BUILT.
        ///
        /// The request asked for 28. Playing the build with 28 against the new braking
        /// rates showed the theory behind it was wrong: stopping had been cut to 0.108 s
        /// while starting stayed at 0.217 s, so every small correction cost speed that
        /// then had to be rebuilt on the slow ramp. Measured over stop-start play that is
        /// 5.9% less ground covered than before the braking work - the opposite of the
        /// sharpness the change was for.
        ///
        /// 45 m/s^2 reaches full speed in 0.133 s, just behind the 0.108 s stop. The
        /// character still builds speed slightly slower than it sheds it, so a run does
        /// not read as weightless, but the asymmetry no longer drags. Past 60 the gain
        /// flattens (0.25 s of running only travels 5 cm further from 60 to 75) while the
        /// start becomes instant, so the useful range ends well below that.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Request value was 28; raised to 45 after the user played the build and "
            + "reported the movement as heavier, not sharper. Their call, not a balance pass.",
            true)]
        public float Acceleration = 45f;

        /// <summary>
        /// How fast speed is SHED once the stick / keys go back to neutral. Separate from
        /// <see cref="Acceleration"/> because sharpness lives in the asymmetry: the player
        /// judges control by how quickly the character STOPS being wrong, not by how
        /// quickly it gets going.
        ///
        /// 60 m/s^2 against a 6.2 m/s top speed is a 0.103 s / 0.320 m stop. That distance
        /// is under one body radius (CollisionRadius 0.45), so the character always halts
        /// inside its own footprint - which is what "I can put myself exactly there" means.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Braking rate used when the movement input is released. Invented, no request value.", true)]
        public float Deceleration = 60f;

        /// <summary>
        /// Braking rate applied to the part of the current velocity the NEW input is
        /// fighting (counter-strafe). Harder than <see cref="Deceleration"/> because a
        /// redirect is an explicit change of intent: every metre still travelled the old
        /// way is a metre the player did not ask for.
        ///
        /// 90 m/s^2 flips a full-speed run in 0.069 s after 0.214 m of overshoot.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Braking rate applied to the velocity the new input opposes. Invented, no request value.", true)]
        public float CounterStrafeBraking = 90f;

        [PrototypeOnly(PendingDecisions.None, "Request value: 720 deg/s")]
        public float RotationSpeed = 720f;

        [PrototypeOnly(PendingDecisions.HealthRegen, "Request value: 4.5 s")]
        public float HealthRegenDelay = 4.5f;

        [PrototypeOnly(PendingDecisions.HealthRegen, "Request value: 12 HP/s")]
        public float HealthRegenRate = 12f;

        [PrototypeOnly(PendingDecisions.HealthRegen, "Request value: regen up to full health (1.0)")]
        [Range(0f, 1f)]
        public float HealthRegenCeilingFraction = 1f;

        [PrototypeOnly(PendingDecisions.None, "Capsule radius used for collision with the swarm.", true)]
        public float CollisionRadius = 0.45f;

        [PrototypeOnly(PendingDecisions.None, "Capsule height.", true)]
        public float CollisionHeight = 1.8f;

        [PrototypeOnly(PendingDecisions.None, "Muzzle/aim plane height above ground for the aim raycast.", true)]
        public float AimPlaneHeight = 1.15f;
    }

    /// <summary>
    /// PROTOTYPE-ONLY camera framing. Distant, elevated, fixed-angle third person.
    /// The player can never rotate it by hand.
    /// </summary>
    [Serializable]
    public sealed class CameraConfigSection
    {
        [PrototypeOnly(PendingDecisions.None, "Request value: ~15 m")]
        public float Height = 15f;

        [PrototypeOnly(PendingDecisions.None, "Request value: ~13 m")]
        public float BackDistance = 13f;

        [PrototypeOnly(PendingDecisions.None, "Request value: ~42")]
        public float FieldOfView = 42f;

        [PrototypeOnly(PendingDecisions.None, "Request value: ~0.12 s")]
        public float FollowSmoothTime = 0.12f;

        [PrototypeOnly(PendingDecisions.None, "Request value: max ~2.5 m aim look-ahead")]
        public float MaxAimLookAhead = 2.5f;

        [PrototypeOnly(PendingDecisions.None, "Smoothing applied to the look-ahead offset itself.", true)]
        public float LookAheadSmoothTime = 0.18f;

        [PrototypeOnly(PendingDecisions.None, "Global shake scale, exposed in the pause menu.", true)]
        [Range(0f, 2f)]
        public float ShakeIntensity = 1f;

        [PrototypeOnly(PendingDecisions.None, "Seconds for an occluding object to fade to its transparent state.", true)]
        public float OcclusionFadeTime = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "Alpha an occluding object fades down to.", true)]
        [Range(0f, 1f)]
        public float OcclusionTargetAlpha = 0.22f;
    }
}
