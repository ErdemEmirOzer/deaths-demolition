using UnityEngine;
using DD.Prototype.Core;
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
            _pathFollower.BodyRadius = Radius;   // WP-076 B9 M-23: a wide body's corner reach = max(0.65, radius + 0.1)
            _pathFollower.HoldsChokepoints = _versionProfile != null && _versionProfile.HoldsChokepoints;   // WP-040: Elite B corridor closing
            RollCrowdFeel(ref _random);   // WP-054: flank side / angle for this life
            _flankOpen = true; _nextFlankOpenCheck = 0f; _flankGoalOffset = Vector3.zero; _flankToEdge = false; _flankLineOpen = true;   // WP-061M / WP-076 B9 M-25
            RegionNavRouter.EnsureAreaCosts();
        }

        /// <summary>
        /// Relative mass. An Executioner (5) shoulders a Standard (1) aside instead of
        /// being pushed by it, which is what keeps the swarm flowing (DEC-033) while a wall
        /// of bodies can still block the player (DEC-032).
        /// </summary>
        /// <summary>WP-054: inside a Gate volume the mass is scaled (heavy ×1.5 takes the threshold, light ×0.6 opens to the sides); elsewhere the tier mass.</summary>
        public float Mass => _separationMass * _gateMassScale;

        private float _gateMassScale = 1f;
        /// <summary>WP-054 readouts.</summary>
        public float GateMassScale => _gateMassScale;
        public int FlankSign => _flankSign;
        public float FlankAngle => _flankAngle;
        public bool WallSliding => _wallSliding;

        private int _flankSign;
        private float _flankAngle;
        private float _wallContactSince = -1f, _wallLastContact = -1f;
        private bool _wallSliding;

        /// <summary>WP-054: per-life rolls (flank side / angle) — called from ApplyNavProfileForLife with the spawn seed's random.</summary>
        private void RollCrowdFeel(ref EnemyRandom random)
        {
            CrowdFeelSettings feel = CrowdFeelSettings.Active;
            _flankSign = random.Chance(feel.FlankShareOf(_tier)) ? (random.Chance(0.5f) ? 1 : -1) : 0;
            _flankAngle = random.Range(feel.FlankAngleMin, feel.FlankAngleMax);
            _gateMassScale = 1f;
            _wallContactSince = -1f; _wallLastContact = -1f; _wallSliding = false;
        }

        /// <summary>
        /// WP-054: the navigation goal for this life — the flank goal while far, the target itself inside the straight radius.
        /// WP-076 B9 (M-25, C-00 30.09): the flank decision and its goal are made on the FlankOpenCheckSeconds timer only
        /// (see <see cref="RefreshFlankDecision"/>); per frame this is pure arithmetic — no NavMesh query, no allocation.
        /// </summary>
        private Vector3 CrowdFeelGoal(Vector3 target)
        {
            if (_flankSign == 0) return target;
            if (Time.time >= _nextFlankOpenCheck) RefreshFlankDecision(target);
            if (!_flankOpen) return target;
            // Inside the straight radius the approach is the target itself (FlankGoal's own rule), without waiting for the
            // next decision tick.
            Vector3 to = target - transform.position; to.y = 0f;
            float straight = CrowdFeelSettings.Active.FlankStraightRadius;
            if (to.sqrMagnitude <= straight * straight) return target;
            // Ray clear at the last decision: the WP-054 flank goal itself, recomputed per frame (arithmetic, as before 12c7215c).
            // Ray stopped near an edge: the accepted hit, held as an offset from the target so it rides the player between checks.
            return _flankToEdge ? target + _flankGoalOffset : CrowdFeelSettings.Active.FlankGoal(transform.position, target, _flankSign, _flankAngle);
        }

        // WP-061M: the flank goal is an OPEN-GROUND approach. It is recomputed from the body's position, so where the
        // NavMesh between the body and the player is partitioned (the west bamboo pocket: a north link and a south edge)
        // it swung from one entrance to the other as the body moved and the body walked the two routes back and forth
        // for ever (S-05 WP-038R: "stopped" at (−30.4, −23.9) with a complete path). The flank only applies while the
        // straight NavMesh line to the player is open; checked every FlankOpenCheckSeconds (NavMesh.Raycast, 0 alloc).
        private const float FlankOpenCheckSeconds = 0.5f;
        private bool _flankOpen = true;
        private float _nextFlankOpenCheck;
        /// <summary>WP-076 B9 (M-25): accepted flank goal − target at the last decision (zero = the target itself).</summary>
        private Vector3 _flankGoalOffset;
        private bool _flankToEdge;
        private bool _flankLineOpen = true;

        /// <summary>WP-076 B9 (M-25) readout: the last flank decision found the straight NavMesh line to the player open (WP-061M).</summary>
        public bool FlankLineOpen => _flankLineOpen;

        /// <summary>WP-061M readout: whether this body currently uses its flank goal (open line to the player, and — WP-076 B9 M-25 — the flank point reachable along the NavMesh within CrowdFeelSettings.FlankProjectMaxMetres).</summary>
        public bool FlankActive => _flankSign != 0 && _flankOpen;

        /// <summary>
        /// WP-076 B9 (M-25, C-00 30.09; AI-SPAWN-06/07 on 12c7215c): the flank decision, once per FlankOpenCheckSeconds.
        ///   1. WP-061M: the straight NavMesh line body → target must be open, else no flank.
        ///   2. NavMesh.Raycast body → flank point: no hit → the flank point; hit → the hit when its XZ distance to the
        ///      flank point is ≤ CrowdFeelSettings.FlankProjectMaxMetres (the flank point is just past an edge: walk to the
        ///      edge), else no flank.
        /// Replaces 12c7215c's SamplePosition + 1 cm "heading dead zone" test, which measured a 3D nearest-point
        /// projection in XZ and so rejected flanks on flat ground (a 0.2 m height gap or a 3° slope ≈ 1 cm), and ran
        /// every frame. Two NavMesh.Raycast per flank body per 0.5 s at most; no allocation.
        /// </summary>
        private void RefreshFlankDecision(Vector3 target)
        {
            _nextFlankOpenCheck = Time.time + FlankOpenCheckSeconds;
            _flankGoalOffset = Vector3.zero; _flankToEdge = false;
            Vector3 self = transform.position;
            _flankOpen = !UnityEngine.AI.NavMesh.Raycast(self, target, out UnityEngine.AI.NavMeshHit _, UnityEngine.AI.NavMesh.AllAreas);
            _flankLineOpen = _flankOpen;
            if (!_flankOpen) return;

            CrowdFeelSettings feel = CrowdFeelSettings.Active;
            Vector3 flank = feel.FlankGoal(self, target, _flankSign, _flankAngle);
            if (!UnityEngine.AI.NavMesh.Raycast(self, flank, out UnityEngine.AI.NavMeshHit hit, CrowdService.Paths.AreaMask))
                return;   // open: the per-frame WP-054 goal
            float dx = hit.position.x - flank.x, dz = hit.position.z - flank.z;
            float max = feel.FlankProjectMaxMetres;
            if (dx * dx + dz * dz <= max * max)
            {
                _flankGoalOffset = hit.position - target;
                _flankToEdge = true;
                return;
            }
            _flankOpen = false;
        }

        /// <summary>
        /// WP-054 / WP-055E: world side contact feeds the wall-friction hysteresis. Read from the CharacterController's
        /// CollisionFlags after Move (M3 K1: the OnControllerColliderHit message allocates a ControllerColliderHit per
        /// call — one per zombie per grounded frame, ≈ 138 B / 1.7 allocations, 91 % of the frame's GC); a side contact
        /// is a wall only when Environment geometry is inside the capsule ring (zombie-zombie contacts share the flag),
        /// asked with Physics.CheckCapsule — no allocation, and only on contact frames.
        /// </summary>
        private void NoteWallContact(CollisionFlags flags)
        {
            if (!_alive || _dying || (flags & CollisionFlags.Sides) == 0 || _controller == null) return;
            float half = Mathf.Max(0f, _controller.height * 0.5f - _controller.radius);
            Vector3 centre = transform.TransformPoint(_controller.center);
            if (!Physics.CheckCapsule(centre + Vector3.up * half, centre - Vector3.up * half, _controller.radius + WallProbeMargin, 1 << PrototypeLayers.Environment, QueryTriggerInteraction.Ignore)) return;
            float now = Time.time;
            if (_wallContactSince < 0f || now - _wallLastContact > CrowdFeelSettings.Active.WallExitSeconds) _wallContactSince = now;
            _wallLastContact = now;
        }

        private const float WallProbeMargin = 0.06f;

        /// <summary>WP-054: the speed factor from wall friction this tick (1 = free).</summary>
        private float WallFrictionFactor()
        {
            CrowdFeelSettings feel = CrowdFeelSettings.Active;
            float now = Time.time;
            bool inContact = _wallLastContact >= 0f && now - _wallLastContact <= feel.WallExitSeconds;
            _wallSliding = inContact && _wallContactSince >= 0f && now - _wallContactSince >= feel.WallEnterSeconds;
            if (!inContact) _wallContactSince = -1f;
            return _wallSliding ? feel.WallFriction : 1f;
        }

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

            // WP-054: gate threshold push — inside a Gate volume the tier mass is scaled (never on the bridge / corridor).
            NavRegionVolume here = RegionNavProfile.Active.Find(position);
            _gateMassScale = here != null && here.kind == NavRegionKind.Gate ? CrowdFeelSettings.Active.GateMassFor(_tier) : 1f;

            Vector3 toCorner = _steeringCorner - position;
            toCorner.y = 0f;

            if (toCorner.sqrMagnitude < NavPathFollower.SteeringDeadZoneSquared)
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
