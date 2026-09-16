using UnityEngine;
using DD.Prototype.Navigation;

namespace DD.Prototype.Enemies
{
    // =====================================================================================
    // ISOLATION BOUNDARY - the ONLY file in Enemies/ that touches DD.Prototype.Navigation.
    // -------------------------------------------------------------------------------------
    // G-05 owns Assets/_Prototype/Navigation/. Everything this enemy knows about paths and
    // crowds lives here, so a change in that contract costs one file and the combat,
    // damage, armour and animation systems never notice.
    //
    // THE SPLIT, AS G-05 DEFINES IT
    //   * PATH and reachability come from the NavMesh, through NavPathFollower. The
    //     follower yields the NEXT CORNER, not the final goal.
    //   * SEPARATION, push and player blocking come from the custom crowd layer, which
    //     hands back a CORRECTION velocity, not a final one.
    //
    // The composition rule is therefore, verbatim from ICrowdMember:
    //     finalVelocity = ownLocomotionVelocity + correction
    // Getting this wrong in the obvious way - treating the correction as the velocity -
    // would stop the enemy dead whenever the crowd had nothing to say, so the sign of a
    // correct implementation is that a lone zombie on an empty map still walks.
    // =====================================================================================

    public sealed partial class ZombieEnemy : ICrowdMember
    {
        private NavPathFollower _pathFollower;
        private bool _crowdRegistered;

        private Vector3 _crowdCorrection;
        private Vector3 _steeringCorner;
        private bool _hasSteeringCorner;

        /// <summary>Spreads NavMesh clamp queries across frames so 40 enemies never sample together.</summary>
        private int _clampPhase;

        // ---------------------------------------------------------------- ICrowdMember

        public Vector3 Position => transform.position;

        /// <summary>Physical radius from the tier's PROTOTYPE-ONLY BodyRadius.</summary>
        /// <summary>WP-030: separation radius follows the AP-007 body fit (A slim / B / C broad); the collision capsule stays the config radius.</summary>
        public float Radius => _bodyRadius * _fitRadiusScale;

        private float _fitRadiusScale = 1f;
        private float _navSpeedFactor = 1f;

        /// <summary>WP-030: the follower's regional state for readouts / tests.</summary>
        public NavPathFollower PathFollower => _pathFollower;

        /// <summary>WP-030: called once per life after the look is known (ZombieEnemy.Initialise).</summary>
        private void ApplyNavProfileForLife(uint seed)
        {
            string fit = _appearance != null && _appearance.IsSkinned ? _appearance.Look.fit : "B";
            _fitRadiusScale = RegionNavProfile.Active.FitRadiusScale(fit);
            if (_pathFollower == null) _pathFollower = new NavPathFollower();
            _pathFollower.SetRouteSeed(seed);
            _pathFollower.HoldsChokepoints = _versionProfile != null && _versionProfile.HoldsChokepoints;   // WP-040: Elite B corridor closing
            RegionNavRouter.EnsureAreaCosts();
        }

        /// <summary>
        /// Relative mass. An Executioner (5) shoulders a Standard (1) aside instead of
        /// being pushed by it, which is what keeps the swarm flowing (DEC-033) while a wall
        /// of bodies can still block the player (DEC-032).
        /// </summary>
        public float Mass => _separationMass;

        /// <summary>
        /// The NEXT PATH CORNER, as the crowd solver requires - not the player's position.
        /// The solver uses it to tell a head-on jam from a queue and to pick which side each
        /// body slides to, so handing it the final goal would collapse that distinction and
        /// re-lock the narrow south bridge.
        ///
        /// A committed or staggered enemy asks to stay where it is, so the solver does not
        /// shove an Executioner out of its own smash.
        /// </summary>
        public Vector3 SteeringTarget
        {
            get
            {
                if (!_alive || _dying)
                {
                    return transform.position;
                }

                if (_animator != null && _animator.IsStaggering)
                {
                    return transform.position;
                }
                if (IsRising)   // WP-030: a rising body (timer or AP-009 clip) holds its ground; the crowd flows around it
                {
                    return transform.position;
                }

                if (_attack != null && _attack.IsAttacking)
                {
                    return transform.position;
                }

                return _hasSteeringCorner ? _steeringCorner : transform.position;
            }
        }

        /// <summary>
        /// One CORRECTION velocity per solver tick. Accumulated, never treated as the final
        /// velocity. Cleared after it is consumed in Update.
        /// </summary>
        public void ApplySteering(Vector3 velocity)
        {
            if (!_alive || _dying)
            {
                _crowdCorrection = Vector3.zero;
                return;
            }

            velocity.y = 0f;
            _crowdCorrection = velocity;
        }

        // ----------------------------------------------------------------- registration

        private void RegisterCrowdMembership()
        {
            if (_pathFollower == null)
            {
                _pathFollower = new NavPathFollower();
            }
            else
            {
                _pathFollower.Invalidate();
            }

            _crowdCorrection = Vector3.zero;
            _steeringCorner = transform.position;
            _hasSteeringCorner = false;
            _clampPhase = Mathf.Abs(GetInstanceID()) % 3;

            if (_crowdRegistered)
            {
                return;
            }

            CrowdService.Register(this);
            _crowdRegistered = true;
        }

        private void UnregisterCrowdMembership()
        {
            _crowdCorrection = Vector3.zero;

            if (!_crowdRegistered)
            {
                return;
            }

            CrowdService.Unregister(this);
            _crowdRegistered = false;
        }

        // -------------------------------------------------------------- used by Update

        /// <summary>
        /// Who the swarm walks towards. Prefers the crowd layer's registered player, which
        /// is the same transform the solver uses for its blocking constraint, and falls
        /// back to the enemy family's own target so a zombie dropped in by the F1 panel
        /// still works with no navigation scene set up.
        /// </summary>
        private Transform ResolveTarget()
        {
            Transform fromCrowd = CrowdService.PlayerTransform;
            return fromCrowd != null ? fromCrowd : EnemyTargeting.Player;
        }

        /// <summary>
        /// Advance the path and produce the direction the enemy wants to walk in.
        /// Returns false when there is no usable heading this frame.
        /// </summary>
        private bool TickNavigation(Vector3 position, Vector3 goal, out Vector3 heading)
        {
            heading = Vector3.zero;

            if (_pathFollower == null)
            {
                _pathFollower = new NavPathFollower();
            }

            // False means the corner is a straight-line fallback (no NavMesh, or the shared
            // per-frame query budget was spent). Either way it is still a usable heading;
            // the flag only says how trustworthy it is.
            _hasSteeringCorner = _pathFollower.Tick(position, goal, out _steeringCorner);
            _navSpeedFactor = _pathFollower.SpeedFactor;

            Vector3 toCorner = _steeringCorner - position;
            toCorner.y = 0f;

            if (toCorner.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            heading = toCorner.normalized;
            return true;
        }

        /// <summary>Take this frame's crowd correction and clear it.</summary>
        private Vector3 ConsumeCrowdCorrection()
        {
            Vector3 correction = _crowdCorrection;
            _crowdCorrection = Vector3.zero;
            return correction;
        }

        /// <summary>
        /// Put the body back on the walkable surface after a strong push.
        ///
        /// This enemy has no NavMeshAgent - agent avoidance is exactly what the S4 split
        /// exists to avoid - so the clamp is manual. It is throttled to one sample every
        /// third frame per enemy, phase-offset per instance, and skipped entirely when the
        /// correction was negligible: a full-crowd clamp every frame would put 40 NavMesh
        /// samples into a single frame for no visible gain.
        /// </summary>
        private void ClampToWalkable(Vector3 appliedCorrection)
        {
            if (appliedCorrection.sqrMagnitude < 0.25f)
            {
                return;
            }

            if ((Time.frameCount + _clampPhase) % 3 != 0)
            {
                return;
            }

            Vector3 current = transform.position;
            Vector3 clamped = NavPathFollower.ClampToNavMesh(current);

            // Only write when it actually moved: assigning transform.position on a
            // CharacterController every frame is not free.
            if ((clamped - current).sqrMagnitude > 0.0004f)
            {
                transform.position = clamped;
            }
        }
    }
}
