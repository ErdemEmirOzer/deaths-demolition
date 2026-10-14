using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Difficulty;
using DD.Prototype.Navigation;
using Unity.Profiling;

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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // dev / editor only: a release log line carries a stack trace into the FINAL perf window (gt 12.10)
            Debug.Log("[DD-DIFF] pursuit.fallback " + _aiRole + " → Chase (" + why + ") tier=" + _tier + " dist=" + distance.ToString("F1") + " pos=" + transform.position.ToString("F1"));
#endif
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
            if (Time.time >= _nextPursuitCheck)
                using (s_pursuitDecideMarker.Auto()) RefreshPursuitDecision(target, distance, mode);
            if (_cutActive) return target + _cutOffset;
            return target + PursuitPlanner.InterceptOffset(distance, PursuitPlanner.PlayerVelocity, _moveSpeed, mode.InterceptLeadMaxSeconds) * _leadScale;
        }

        /// <summary>WP-102: the pursuit decision's cost (R4 test policy: new per-frame cost carries a marker).</summary>
        private static readonly ProfilerMarker s_pursuitDecideMarker = new ProfilerMarker("WP102.Pursuit.Decide");

        /// <summary>
        /// WP-102: the pursuit decision, once per FlankOpenCheckSeconds. A cutter tries the points 1 / 1.6 / 2.4 × CutDistance ahead
        /// of the player along its escape direction (PursuitPlanner: where it runs, or the ring's open side), each pulled back to
        /// where the NavMesh line from the player stops; the nearest it can reach before the player wins. Otherwise the intercept
        /// lead is scaled to where the NavMesh line from the player towards it stops. At most 5 NavMesh queries (the clamp, up to
        /// three cut lines, the lead line), no allocation.
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
            ResetNavKeep();   // a1-fix: the NavMesh keep anchors afresh for the new life

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

        // ------------------------------------------------- a1-fix (R4 FINAL-2 a1x, C-00 14.10): the body stays on the NavMesh

        // Replaces the throttled ClampToWalkable (WP-003). That clamp ran only at |crowd correction + pull| ≥ 0.5 m/s, on one frame in
        // three, and moved the body with a transform.position write. The physics settings have auto sync off, and with auto sync off
        // the next CharacterController.Move can start from the controller's old position and undo that write. Two FINAL-2 reds came
        // from it:
        //   * WP-103B grove: the Not Walkable mask puts the NavMesh edge 0.25–0.75 m inside floor the capsule can walk, with no
        //     collider between them. The crowd pushed a body that was waiting in the ring (speed ≈ 0, so the clamp never ran) 0.6–0.7 m
        //     into that band, and it stayed there for 11 s.
        //   * WP-104 B3 pull: a body leaning on an axis lantern (collider narrower than its NavMesh hole) stood 0.11–0.14 m off the mesh.

        [PrototypeOnly(PendingDecisions.None, "a1-fix (R4 FINAL-2 a1x, C-00 14.10): every grounded frame, the XZ move from the last on-mesh " +
            "point is traced along the NavMesh (NavMesh.Raycast). Past an edge, the body takes the hit point plus the slide along the edge, " +
            "applied by a second CharacterController.Move so colliders stay respected. Overshoot left alone, per body: max(NavKeepToleranceMin, " +
            "agent radius − capsule radius − skin) + NavKeepToleranceMargin. That is the overshoot of a capsule touching a wall: Standard " +
            "0.5 − 0.45 − 0.03 = 0.02 → 0.03 m, Runner 0.5 − 0.40 − 0.03 = 0.07 → 0.08 m, so the WP-054 wall friction still sees the wall. " +
            "Both stay under the 0.10 m of the WP-104 B3 pull test. NavKeepProbe m for a fresh anchor (spawn, landing, a jump of " +
            "NavKeepAnchorJump m). NavKeepReturnSpeed m/s on top of the frame's own step for a body found off the mesh. NavKeepInset m inside " +
            "the edge for the slide trace. NavKeepStill m: a body that moved less since its last check is not traced again. NavKeepAirFrames: " +
            "airborne frames that keep the anchor.", true)]
        private const float NavKeepToleranceMin = 0.02f, NavKeepToleranceMargin = 0.01f, NavKeepProbe = 1f, NavKeepAnchorJump = 1.5f,
            NavKeepReturnSpeed = 2f, NavKeepInset = 0.01f, NavKeepStill = 0.005f, NavKeepEdgeStill = 0.02f, NavKeepSlideMin = 0.02f;
        private const int NavKeepAirFrames = 1;

        [PrototypeOnly(PendingDecisions.None, "a1-fix: the open-floor disc. After a body is found on the mesh, the distance to the nearest " +
            "NavMesh edge (FindClosestEdge, minus NavKeepDiscMargin) is a disc that needs no trace for NavKeepDiscSeconds. A disc smaller than " +
            "NavKeepDiscMin is not kept. Near an edge, the edge query rests for NavKeepDiscSeconds, and one trace per moving frame is cheaper.", true)]
        private const float NavKeepDiscSeconds = 1f, NavKeepDiscMargin = 0.05f, NavKeepDiscMin = 0.3f;

        [PrototypeOnly(PendingDecisions.None, "a1-fix: NavMeshLink crossing (V6: 3 links incl. the WP-103B grove link; AP005: 9). A body whose " +
            "steering corner is within NavKeepLinkEndMetres of a link end, and which stands within width / 2 + its radius + " +
            "NavKeepLinkMarginMetres of the link segment, is crossing the link. The keep lets it go (NavMesh.Raycast does not follow links) and " +
            "anchors afresh on the far mesh.", true)]
        private const float NavKeepLinkEndMetres = 0.5f, NavKeepLinkMarginMetres = 0.3f;

        /// <summary>a1-fix: the per-frame cost of keeping the bodies on the NavMesh (R4 test policy: new per-frame cost carries a marker).</summary>
        private static readonly ProfilerMarker s_navKeepMarker = new ProfilerMarker("WP111.Crowd.NavKeep");

        /// <summary>a1-fix readouts (evidence, allocation-free): NavMesh queries, edge contacts, corrective moves, the largest correction (m), link crossings let go.</summary>
        public static int NavKeepQueries { get; private set; }
        public static int NavKeepEdgeHits { get; private set; }
        public static int NavKeepClamps { get; private set; }
        public static float NavKeepMaxCorrection { get; private set; }
        public static int NavKeepLinkPasses { get; private set; }

        /// <summary>Tests / evidence: zero the a1-fix readouts.</summary>
        public static void ResetNavKeepCounters() { NavKeepQueries = 0; NavKeepEdgeHits = 0; NavKeepClamps = 0; NavKeepMaxCorrection = 0f; NavKeepLinkPasses = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetNavKeepStatics()
        {
            ResetNavKeepCounters();
            s_navLinksDirty = true;
            s_navLinkCount = 0;
            s_navAgentRadius = -1f;
        }

        // ---- the scene's NavMeshLinks, gathered once per scene load / unload (FindObjectsOfType allocates: never per frame)

        private struct NavLinkSegment { public Vector3 A, B; public float HalfWidth; }
        private static NavLinkSegment[] s_navLinks = new NavLinkSegment[16];
        private static int s_navLinkCount;
        private static bool s_navLinksDirty = true;
        private static float s_navAgentRadius = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BindNavLinkScenes()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnNavLinkSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnNavLinkSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnNavLinkSceneUnloaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnNavLinkSceneUnloaded;
        }

        private static void OnNavLinkSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => s_navLinksDirty = true;
        private static void OnNavLinkSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => s_navLinksDirty = true;

        /// <summary>Gathers the enabled NavMeshLinks in world space. Runs only on the first link check after a scene change.</summary>
        private static void RefreshNavLinks()
        {
            s_navLinksDirty = false;
            s_navLinkCount = 0;
            Unity.AI.Navigation.NavMeshLink[] links = UnityEngine.Object.FindObjectsOfType<Unity.AI.Navigation.NavMeshLink>();
            for (int i = 0; i < links.Length; i++)
            {
                Unity.AI.Navigation.NavMeshLink l = links[i];
                if (l == null || !l.isActiveAndEnabled) continue;
                if (s_navLinkCount == s_navLinks.Length) System.Array.Resize(ref s_navLinks, s_navLinks.Length * 2);
                Transform t = l.transform;   // NavMeshLink adds itself at transform.position / rotation (no scale)
                s_navLinks[s_navLinkCount++] = new NavLinkSegment
                {
                    A = t.position + t.rotation * l.startPoint,
                    B = t.position + t.rotation * l.endPoint,
                    HalfWidth = Mathf.Max(0f, l.width) * 0.5f,
                };
            }
        }

        /// <summary>The agent radius the NavMesh was baked with (its edge stands this far from a wall); 0.5 when unknown.</summary>
        private static float NavAgentRadius
        {
            get
            {
                if (s_navAgentRadius < 0f)
                {
                    float r = UnityEngine.AI.NavMesh.GetSettingsByID(0).agentRadius;
                    s_navAgentRadius = r > 0f ? r : 0.5f;
                }

                return s_navAgentRadius;
            }
        }

        /// <summary>a1-fix readout: the last frame (Time.frameCount) this body met a NavMesh edge; −1 never.</summary>
        public int NavEdgeFrame => _navEdgeFrame;

        private Vector3 _navAnchor, _navEdgeNormal, _navCheckedAt, _navDiscCentre;
        private bool _navAnchorValid, _navEdgeHold;
        // a1-fix cost (gt-4 MUST-2): resting within tolerance against an edge (a Runner rubbing a wall) is not traced again until it moves
        // NavKeepEdgeStill; a return capped short of the mesh (fresh anchor) keeps the body checked even while it stands (gt-4 SHOULD)
        private bool _navEdgeTouch, _navResidual;
        private int _navEdgeFrame = -1, _navAirFrames;
        private float _navDiscRadiusSqr, _navDiscUntil, _navEdgeQuietUntil;

        /// <summary>A new life (RegisterCrowdMembership): the next keep anchors afresh.</summary>
        private void ResetNavKeep()
        {
            _navAnchorValid = false;
            _navEdgeHold = false;
            _navEdgeFrame = -1;
            _navAirFrames = 0;
            _navEdgeNormal = Vector3.zero;
            _navDiscUntil = 0f;
            _navEdgeQuietUntil = 0f;
            _navEdgeTouch = false;
            _navResidual = false;
        }

        /// <summary>
        /// a1-fix, before the Move: once the keep has pulled the body back from an edge, and for as long as the body stays against
        /// that edge, the part of this frame's motion that points out across the edge is removed. The body slides along the edge like
        /// along a wall. Without this, the crowd correction would push it out each frame and the keep would pull it back each frame
        /// (clip, push, clip: a visible tremble). A body only touching a wall within its tolerance is never held, so its wall
        /// contact (WP-054 wall friction) is unchanged. No query; vertical untouched.
        /// </summary>
        private void HoldAtNavEdge(ref Vector3 motion)
        {
            if (!_navEdgeHold || _navEdgeFrame != Time.frameCount - 1) return;
            float d = motion.x * _navEdgeNormal.x + motion.z * _navEdgeNormal.z;   // the normal points into the mesh
            if (d >= 0f) return;
            motion.x -= d * _navEdgeNormal.x;
            motion.z -= d * _navEdgeNormal.z;
        }

        /// <summary>
        /// a1-fix, after the Move: the body's final position stays on the NavMesh (XZ). It runs on every grounded frame with no speed or
        /// push threshold, so a body standing still in the crowd is covered as well.
        /// <para>The check, cheapest first:
        /// (1) the body moved less than NavKeepStill since its last check: nothing to do;
        /// (2) it is inside its open-floor disc (FindClosestEdge, refreshed at most every NavKeepDiscSeconds): on the mesh, no trace;
        /// (3) one NavMesh.Raycast from the last on-mesh point to where the controller put it. No hit: on the mesh. A hit: the target
        /// is the hit point plus the rest of the move slid along the edge (a second Raycast checks the slide, as move-along-surface does).
        /// Tracing from the anchor never jumps across a thin mask strip or off a bridge deck, which a nearest-point sample can.</para>
        /// <para>An overshoot within the body's tolerance stays (wall contact). A larger one is undone by a second
        /// CharacterController.Move: the controller's own position stays in sync (a transform write may not survive the next Move), and the
        /// capsule cannot be put back into a collider. Because this runs on every frame, a pushed body is stopped at the edge in the same
        /// frame it crosses it and never gets into the band past it. A 0.6–0.75 m correction (a visible jump) needs a body that is already
        /// deep past the edge, which only a fresh anchor (landing, teleport) can meet; there the return is capped at the frame's step +
        /// NavKeepReturnSpeed.</para>
        /// <para>Let go: a body crossing a NavMeshLink (Raycast does not follow links; checked only after a hit, see
        /// <see cref="CrossingNavLink"/>), and an airborne body for more than NavKeepAirFrames frames (fall, knock-up); their anchor is taken
        /// afresh on the far mesh / on landing. A rising body: the timed rise returns before the Move; the AP-009 clip rise
        /// (the skinned body's IsRising) does go through the Move, so the keep lets it go explicitly (IsRising) and anchors afresh when
        /// it walks. Not reached: death and the corpse (TickDying). Gravity (y) is never changed.</para>
        /// <para>Cost: open floor ≈ 0 queries (two per disc refresh); a moving body within NavKeepDiscMin of an edge one Raycast per frame;
        /// at an edge one more Raycast and one Move; a body standing still none. No allocation (NavMeshHit is a struct).</para>
        /// </summary>
        private void KeepOnNavMesh(Vector3 beforeMove, float dt)
        {
            using (s_navKeepMarker.Auto())
            {
                if (_controller == null || !_controller.enabled) return;
                if (!_controller.isGrounded)
                {
                    // one airborne frame (a step, a bump) keeps the anchor; a longer one (a fall, a knock-up) drops it
                    if (++_navAirFrames > NavKeepAirFrames) { _navAnchorValid = false; _navEdgeHold = false; _navDiscUntil = 0f; }
                    return;
                }

                _navAirFrames = 0;
                if (IsRising)
                {
                    // the rise (timer or AP-009 clip) holds its ground and is let go; the walk after it anchors afresh
                    _navAnchorValid = false; _navEdgeHold = false; _navDiscUntil = 0f;
                    return;
                }

                Vector3 p = transform.position;
                float now = Time.time;
                if (_navAnchorValid)
                {
                    float cx = p.x - _navCheckedAt.x, cz = p.z - _navCheckedAt.z;
                    float still = _navEdgeTouch ? NavKeepEdgeStill : NavKeepStill;
                    if (!_navResidual && cx * cx + cz * cz < still * still)
                    {
                        if (_navEdgeHold) _navEdgeFrame = Time.frameCount;   // still against the edge it was held at
                        return;   // (1) not moved since the last check
                    }

                    if (now < _navDiscUntil)
                    {
                        float dx = p.x - _navDiscCentre.x, dz = p.z - _navDiscCentre.z;
                        if (dx * dx + dz * dz <= _navDiscRadiusSqr)
                        {
                            _navAnchor = p; _navCheckedAt = p; _navEdgeHold = false; _navEdgeTouch = false;
                            return;   // (2) open floor: on the mesh without a query
                        }
                    }
                }

                float tol = Mathf.Max(NavKeepToleranceMin, NavAgentRadius - (_controller.radius + _controller.skinWidth)) + NavKeepToleranceMargin;
                Vector3 target;
                bool edge = false;
                Vector3 normal = Vector3.zero;
                float ax = p.x - _navAnchor.x, az = p.z - _navAnchor.z;
                bool fresh = !_navAnchorValid || ax * ax + az * az > NavKeepAnchorJump * NavKeepAnchorJump;
                target = p;
                if (!fresh)
                {
                    NavKeepQueries++;
                    if (!UnityEngine.AI.NavMesh.Raycast(_navAnchor, p, out UnityEngine.AI.NavMeshHit hit, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        OnNavMeshAt(p, now);   // (3) the whole move stayed on the mesh
                        return;
                    }

                    // A hit with no edge normal is NavMesh.Raycast's answer for a source it cannot place on the mesh (NavPathService
                    // SegmentBlocked note). Never pin the body to such an anchor; take one afresh.
                    if (hit.normal.x * hit.normal.x + hit.normal.z * hit.normal.z < 1e-8f) fresh = true;
                    else
                    {
                        edge = true;
                        target = SlideAlongNavEdge(hit, _navAnchor, p, out normal);
                    }
                }

                if (fresh)
                {
                    NavKeepQueries++;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit sample, NavKeepProbe, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        _navAnchorValid = false;   // no mesh within the probe: the out-of-world net's case
                        return;
                    }

                    target = sample.position;
                }

                float ox = target.x - p.x, oz = target.z - p.z;
                float off2 = ox * ox + oz * oz;
                if (!edge && off2 <= tol * tol)
                {
                    OnNavMeshAt(p, now);   // a fresh anchor on the mesh
                    return;
                }

                if (off2 > tol * tol && CrossingNavLink(p))
                {
                    // on a NavMeshLink: let it cross; the far mesh anchors it afresh (only asked after a hit or an off-mesh sample)
                    NavKeepLinkPasses++;
                    _navAnchorValid = false; _navEdgeHold = false; _navDiscUntil = 0f;
                    return;
                }

                // on the mesh (XZ), the body's own height; an edge anchor sits NavKeepInset inside, so the next trace starts on the mesh
                _navAnchorValid = true;
                _navAnchor = new Vector3(target.x + normal.x * NavKeepInset, p.y, target.z + normal.z * NavKeepInset);
                _navDiscUntil = 0f;
                if (edge)
                {
                    NavKeepEdgeHits++;
                    // held: pulled back now, or held last frame and still against the edge
                    _navEdgeHold = off2 > tol * tol || (_navEdgeHold && _navEdgeFrame == Time.frameCount - 1);
                    _navEdgeFrame = Time.frameCount;
                    _navEdgeNormal = normal;
                }
                else _navEdgeHold = false;

                _navEdgeTouch = edge && off2 <= tol * tol;
                if (off2 <= tol * tol) { _navCheckedAt = p; _navResidual = false; return; }

                float sx = p.x - beforeMove.x, sz = p.z - beforeMove.z;
                float cap = Mathf.Sqrt(sx * sx + sz * sz) + NavKeepReturnSpeed * dt;
                float off = Mathf.Sqrt(off2);
                _navResidual = off > cap;   // the return is capped: check again next frame even if the body stands
                if (off > cap) { float k = cap / off; ox *= k; oz *= k; off = cap; }
                if (off > NavKeepMaxCorrection) NavKeepMaxCorrection = off;
                NavKeepClamps++;
                // the same downward step as the walk, so the controller keeps its ground contact (isGrounded) for the next frame
                _controller.Move(new Vector3(ox, Mathf.Min(0f, _verticalVelocity) * dt, oz));
                _navCheckedAt = transform.position;
            }
        }

        /// <summary>The body stands on the mesh at <paramref name="p"/>: it becomes the anchor, and the open-floor disc is refreshed when due.</summary>
        private void OnNavMeshAt(Vector3 p, float now)
        {
            _navAnchorValid = true;
            _navAnchor = p;
            _navCheckedAt = p;
            _navEdgeHold = false;
            _navEdgeTouch = false;
            _navResidual = false;
            if (now < _navEdgeQuietUntil) return;   // near an edge: no edge query for a while
            NavKeepQueries++;
            float radius = -1f;
            if (UnityEngine.AI.NavMesh.FindClosestEdge(p, out UnityEngine.AI.NavMeshHit e, UnityEngine.AI.NavMesh.AllAreas))
            {
                float ex = e.position.x - p.x, ez = e.position.z - p.z;
                radius = Mathf.Min(e.distance, Mathf.Sqrt(ex * ex + ez * ez)) - NavKeepDiscMargin;
            }

            if (radius < NavKeepDiscMin)
            {
                _navDiscUntil = 0f;
                _navEdgeQuietUntil = now + NavKeepDiscSeconds;
                return;
            }

            _navDiscCentre = p;
            _navDiscRadiusSqr = radius * radius;
            _navDiscUntil = now + NavKeepDiscSeconds;
        }

        /// <summary>
        /// a1-fix: the body is walking a NavMeshLink. Its steering corner is one of the link's ends (the path goes through the link),
        /// and it stands in the link's corridor (width / 2 + its radius + NavKeepLinkMarginMetres from the segment, XZ). A body pushed off
        /// the mesh next to a link whose path does not use the link is still kept. Asked only after a hit; a loop over the scene's
        /// few links (V6 3, AP005 9), no allocation.
        /// </summary>
        private bool CrossingNavLink(Vector3 p)
        {
            if (s_navLinksDirty) RefreshNavLinks();
            if (s_navLinkCount == 0 || !_hasSteeringCorner) return false;
            float end2 = NavKeepLinkEndMetres * NavKeepLinkEndMetres;
            for (int i = 0; i < s_navLinkCount; i++)
            {
                NavLinkSegment l = s_navLinks[i];
                if (FlatSqr(_steeringCorner, l.A) > end2 && FlatSqr(_steeringCorner, l.B) > end2) continue;
                float abx = l.B.x - l.A.x, abz = l.B.z - l.A.z;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 1e-8f ? Mathf.Clamp01(((p.x - l.A.x) * abx + (p.z - l.A.z) * abz) / len2) : 0f;
                float qx = p.x - (l.A.x + abx * t), qz = p.z - (l.A.z + abz * t);
                float reach = l.HalfWidth + Radius + NavKeepLinkMarginMetres;
                if (qx * qx + qz * qz <= reach * reach) return true;
            }

            return false;
        }

        private static float FlatSqr(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        /// <summary>
        /// The hit point plus the rest of the move along the edge, traced again from just inside the edge. <paramref name="inward"/> is
        /// the edge normal turned into the mesh (against the trace from the anchor, which leaves the mesh through this edge). Zero when
        /// the hit gives no usable normal: then the target is the hit itself.
        /// </summary>
        private Vector3 SlideAlongNavEdge(UnityEngine.AI.NavMeshHit hit, Vector3 anchor, Vector3 p, out Vector3 inward)
        {
            Vector3 h = hit.position;
            inward = hit.normal;
            inward.y = 0f;
            float m2 = inward.x * inward.x + inward.z * inward.z;
            if (m2 < 1e-8f) { inward = Vector3.zero; return h; }
            inward /= Mathf.Sqrt(m2);
            float rx = p.x - h.x, rz = p.z - h.z;              // the part of the move past the edge
            // Into the mesh = against the trace. The trace (anchor → p) leaves the mesh through this edge, so it runs against the inward
            // normal. That holds even when the hit is at the trace's start (an anchor on the edge), where "towards the anchor" is
            // undefined; a trace along the edge falls back to "against the overshoot".
            float side = -((p.x - anchor.x) * inward.x + (p.z - anchor.z) * inward.z);
            if (side * side < 1e-12f) side = -(rx * inward.x + rz * inward.z);
            if (side < 0f) inward = -inward;
            float d = Mathf.Min(0f, rx * inward.x + rz * inward.z);   // the outward part only
            float slideX = rx - d * inward.x, slideZ = rz - d * inward.z;
            if (slideX * slideX + slideZ * slideZ < NavKeepSlideMin * NavKeepSlideMin) return h;   // a slide too short to matter: no second trace (cost)

            Vector3 from = new Vector3(h.x + inward.x * NavKeepInset, h.y, h.z + inward.z * NavKeepInset);
            Vector3 to = new Vector3(from.x + slideX, p.y, from.z + slideZ);
            NavKeepQueries++;
            return UnityEngine.AI.NavMesh.Raycast(from, to, out UnityEngine.AI.NavMeshHit slid, UnityEngine.AI.NavMesh.AllAreas) ? slid.position : to;
        }
    }
}
