using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// CROSS-TASK CONTRACT - implemented by the enemy behaviour (task G-04).
    ///
    /// WHY THIS LAYER EXISTS
    /// ---------------------
    /// Two approved requirements cannot both be satisfied by a single NavMeshAgent
    /// avoidance setting:
    ///
    ///   REQ-046 / DEC-032  the player cannot walk through zombies, zombies can
    ///                      physically block the player's route, being surrounded is
    ///                      a real threat;
    ///   REQ-047 / DEC-033  zombies do not form a solid traffic lock with each other,
    ///                      they flow as a swarm, pushing each other lightly instead
    ///                      of stacking, so the narrow south bridge does not seize up.
    ///
    /// Put the player into the agents' avoidance set and REQ-046 dies (agents steer
    /// around the player, nothing ever blocks). Leave the player out and the agents'
    /// own quality-based avoidance locks the bridge, so REQ-047 dies.
    ///
    /// Therefore the split, and it must not be blurred:
    ///   * PATH and WALKABLE PATH DISTANCE come from the NavMesh
    ///     (<see cref="CrowdService.PathDistance"/>, <see cref="CrowdService.IsReachable"/>,
    ///      <see cref="NavPathFollower"/>);
    ///   * SEPARATION, PUSH and PLAYER BLOCKING come from this custom crowd layer
    ///     (<see cref="CrowdSolver"/>).
    ///
    /// HOW AN ENEMY USES IT
    /// --------------------
    ///   1. OnEnable  : CrowdService.Register(this)
    ///   2. OnDisable : CrowdService.Unregister(this)   (mandatory - a destroyed member
    ///                  that never unregisters is defensively dropped, but that costs a
    ///                  frame of wrong crowd density)
    ///   3. Every frame the solver calls <see cref="ApplySteering"/> exactly once with a
    ///      CORRECTION velocity - see the remarks on that method.
    ///
    /// The interface deliberately carries no max-speed field: locomotion speed stays the
    /// enemy's business (it comes from the tier config), the crowd layer only ever hands
    /// back a correction.
    /// </summary>
    public interface ICrowdMember
    {
        /// <summary>World position of the member this frame. Read once per solver tick.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// Physical radius used for soft separation, in metres. Comes from the tier's
        /// PROTOTYPE-ONLY BodyRadius (0.4 Runner ... 0.9 Executioner).
        /// </summary>
        float Radius { get; }

        /// <summary>
        /// Relative mass. A pair sharing an overlap splits the correction by
        /// m_other / (m_self + m_other), so an Executioner (mass 5) shoulders a
        /// Standard (mass 1) aside instead of being pushed by it. Must be &gt; 0.
        /// </summary>
        float Mass { get; }

        /// <summary>
        /// Where the member wants to go: the next NavMesh path corner, not the final
        /// goal. Used by the solver to (a) tell a head-on jam from a queue and (b) pick
        /// the tangential side each member slides to, which is what keeps the narrow
        /// bridge flowing instead of locking.
        /// </summary>
        Vector3 SteeringTarget { get; }

        /// <summary>
        /// Called once per solver tick with the CORRECTION velocity in m/s, world space,
        /// horizontal (y is always 0).
        ///
        /// CONTRACT: this is NOT the final velocity. The member composes:
        ///
        ///     finalVelocity = ownLocomotionVelocity + correction;
        ///     move(finalVelocity * dt)   // through NavMesh-clamped movement
        ///
        /// The correction contains, summed:
        ///   * soft mass-weighted separation from neighbouring members,
        ///   * a tangential slide component that breaks head-on symmetry,
        ///   * approach damping so contacts settle instead of oscillating,
        ///   * the player contact constraint: the inward component of the member's own
        ///     measured velocity is cancelled at the player's surface, plus a slow
        ///     depenetration if it is already overlapping. The member is braked against
        ///     the player, never flung away from it - that is what lets a wall of
        ///     zombies keep standing in the player's way (REQ-046).
        ///
        /// Its magnitude is clamped by CrowdTuning.MaxCorrectionSpeed, so it can never
        /// dominate the member's own locomotion.
        ///
        /// Movement MUST be applied through NavMesh-clamped motion (NavMeshAgent.Move,
        /// or <see cref="NavPathFollower.ClampToNavMesh"/>), otherwise a strong push can
        /// shove a member off the walkable surface.
        /// </summary>
        void ApplySteering(Vector3 velocity);
    }
}
