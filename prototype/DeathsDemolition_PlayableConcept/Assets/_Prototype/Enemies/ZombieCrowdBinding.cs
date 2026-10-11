using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Difficulty;
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
            DifficultyModeSettings mode = DifficultyService.Current;   // WP-102: null in Normal
            float flankShare = mode != null && _tier == EnemyTier.Standard && mode.FlankShareStandard >= 0f ? mode.FlankShareStandard : feel.FlankShareOf(_tier);
            _flankSign = random.Chance(flankShare) ? (random.Chance(0.5f) ? 1 : -1) : 0;
            _flankAngle = random.Range(feel.FlankAngleMin, feel.FlankAngleMax);
            // WP-102: a non-flanking body of a Hard / Demolition run takes a role. Normal draws nothing more — its rolls are unchanged.
            _aiRole = ZombieAiRole.Chase;
            _cutActive = false; _cutOffset = Vector3.zero; _leadScale = 1f; _nextPursuitCheck = 0f;
            _pursuitBestDistance = float.MaxValue; _pursuitProgressAt = -1f;
            if (mode != null && _flankSign == 0)
            {
                float u = random.Next01();
                _aiRole = u < mode.CutShare ? ZombieAiRole.Cut : u < mode.CutShare + mode.InterceptShare ? ZombieAiRole.Intercept : ZombieAiRole.Chase;
            }
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
            if (_aiRole != ZombieAiRole.Chase) return PursuitGoal(target);   // WP-102 (Hard / Demolition)
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

        // ------------------------------------------------------------------------------------------------ WP-102 pursuit roles

        private ZombieAiRole _aiRole;
        private float _nextPursuitCheck;
        private Vector3 _cutOffset;
        private bool _cutActive;
        private float _leadScale = 1f;
        private static readonly float[] CutScales = { 1f, 1.6f, 2.4f };
        private float _pursuitBestDistance = float.MaxValue, _pursuitProgressAt = -1f;

        /// <summary>WP-102: a pursuit role is dropped when the body has not closed on the player by this much…</summary>
        private const float PursuitProgressMetres = 0.75f;
        /// <summary>…within this long (s): its goal is not getting it anywhere — it chases the target as in Normal from then on.</summary>
        private const float PursuitStallSeconds = 3f;

        /// <summary>WP-102 readout: pursuit roles dropped since start-up (a waiting body, or no progress) — evidence and tests.</summary>
        public static int PursuitFallbacks { get; private set; }

        /// <summary>WP-102 readout: this life's AI role (Chase in Normal and for flankers).</summary>
        public ZombieAiRole AiRole => _aiRole;
        /// <summary>WP-102 readout: the last decision sent this cutter to a point on the player's escape path.</summary>
        public bool CutActive => _cutActive;
        /// <summary>WP-102 readout: the accepted cut point relative to the player (zero when not cutting).</summary>
        public Vector3 CutOffset => _cutOffset;

        private void DropPursuitRole(string why, float distance)
        {
            Debug.Log("[DD-DIFF] pursuit.fallback " + _aiRole + " → Chase (" + why + ") tier=" + _tier + " dist=" + distance.ToString("F1") + " pos=" + transform.position.ToString("F1"));
            _aiRole = ZombieAiRole.Chase;
            _cutActive = false; _cutOffset = Vector3.zero; _leadScale = 1f;
            PursuitFallbacks++;
        }

        /// <summary>WP-102 tests (InternalsVisibleTo): give this life a role, the next pursuit decision on the next goal read.</summary>
        internal void SetAiRoleForTests(ZombieAiRole role)
        {
            _aiRole = role;
            _cutActive = false; _cutOffset = Vector3.zero; _leadScale = 1f; _nextPursuitCheck = 0f;
            _pursuitBestDistance = float.MaxValue; _pursuitProgressAt = -1f;
            if (role != ZombieAiRole.Chase) _flankSign = 0;
        }

        /// <summary>
        /// WP-102: the navigation goal of an intercepting / cutting body. Inside the straight radius the target itself (as the flank).
        /// Otherwise a cutter that could get there first walks to its accepted point on the player's escape path (held as an offset
        /// from the player between decisions), and an interceptor — or a cutter that cannot get ahead — to where the player will be:
        /// player + velocity × min(distance / own speed, lead cap), the lead clamped to the NavMesh at the last decision. The
        /// decision runs on the flank's timer (FlankOpenCheckSeconds); per frame this is arithmetic only.
        /// </summary>
        private Vector3 PursuitGoal(Vector3 target)
        {
            DifficultyModeSettings mode = DifficultyService.Current;
            if (mode == null) return target;
            Vector3 to = target - transform.position; to.y = 0f;
            float straight = CrowdFeelSettings.Active.FlankStraightRadius;
            float dist2 = to.sqrMagnitude;
            if (dist2 <= straight * straight) { _pursuitProgressAt = -1f; return target; }
            float distance = Mathf.Sqrt(dist2);
            // a waiting body (queued, or stalled in the crowd) is exempt from the stuck watchdog: a pursuit goal must never hold it
            // there (bot b1 s26: the round's last body waited out of sight for ever) — and a goal that brings it no closer is dropped
            float now = Time.time;
            if (_pursuitProgressAt < 0f || distance < _pursuitBestDistance - PursuitProgressMetres) { _pursuitBestDistance = distance; _pursuitProgressAt = now; }
            if (_waiting || now - _pursuitProgressAt > PursuitStallSeconds)
            {
                DropPursuitRole(_waiting ? "waiting" : "no progress", distance);
                return target;
            }
            if (Time.time >= _nextPursuitCheck) RefreshPursuitDecision(target, distance, mode);
            if (_cutActive) return target + _cutOffset;
            return target + PursuitPlanner.InterceptOffset(distance, PursuitPlanner.PlayerVelocity, _moveSpeed, mode.InterceptLeadMaxSeconds) * _leadScale;
        }

        /// <summary>
        /// WP-102: the pursuit decision, once per FlankOpenCheckSeconds. A cutter tries the points 1 / 1.6 / 2.4 × CutDistance ahead
        /// of the player along its escape direction (PursuitPlanner: where it runs, or the ring's open side), each pulled back to
        /// where the NavMesh line from the player stops; the nearest it can reach before the player wins. Otherwise the intercept
        /// lead is scaled to where the NavMesh line from the player towards it stops. At most 4 NavMesh queries, no allocation.
        /// </summary>
        private void RefreshPursuitDecision(Vector3 target, float distance, DifficultyModeSettings mode)
        {
            _nextPursuitCheck = Time.time + FlankOpenCheckSeconds;
            _cutActive = false; _cutOffset = Vector3.zero; _leadScale = 1f;
            Vector3 from = NavPathFollower.ClampToNavMesh(target, 2f);
            int area = CrowdService.Paths.AreaMask;
            if (_aiRole == ZombieAiRole.Cut)
            {
                Vector3 escape = PursuitPlanner.EscapeDirection;
                float playerSpeed = PursuitPlanner.PlayerSpeed;
                Vector3 self = transform.position;
                for (int i = 0; i < CutScales.Length; i++)
                {
                    float d = mode.CutDistance * CutScales[i];
                    Vector3 cut = from + escape * d;
                    if (UnityEngine.AI.NavMesh.Raycast(from, cut, out UnityEngine.AI.NavMeshHit hit, area))
                    {
                        cut = hit.position;
                        float hx = cut.x - from.x, hz = cut.z - from.z;
                        d = Mathf.Sqrt(hx * hx + hz * hz);
                    }

                    if (d < 1.5f) break;   // the player stands against the edge that way: nothing to cut
                    float cx = cut.x - self.x, cz = cut.z - self.z;
                    if (PursuitPlanner.CutReachable(Mathf.Sqrt(cx * cx + cz * cz), _moveSpeed, d, playerSpeed, mode.CutArrivalMarginSeconds))
                    {
                        _cutActive = true;
                        _cutOffset = cut - target;
                        return;
                    }
                }
            }

            Vector3 lead = PursuitPlanner.InterceptOffset(distance, PursuitPlanner.PlayerVelocity, _moveSpeed, mode.InterceptLeadMaxSeconds);
            float leadLength = lead.magnitude;
            if (leadLength > 0.5f && UnityEngine.AI.NavMesh.Raycast(from, from + lead, out UnityEngine.AI.NavMeshHit h, area))
            {
                float lx = h.position.x - from.x, lz = h.position.z - from.z;
                _leadScale = Mathf.Clamp01(Mathf.Sqrt(lx * lx + lz * lz) / leadLength);
            }
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
