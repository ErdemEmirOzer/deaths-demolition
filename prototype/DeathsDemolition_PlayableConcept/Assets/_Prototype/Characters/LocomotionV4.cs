using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-083 B3b (C-00 05.10, R2 N2-05 · locomotion v4 skeleton): the start / stop / pivot / turn contract over the AP-026 clip
    /// plan (AP026-clips-v1: four gaits × eight directions × lead foot for starts and stops, pivots 90 / 135 / 180 to either side
    /// on either foot, turn 45 in place). One stable id per clip drives the Animator's LocomotionV4 sub-state machine through the
    /// int parameter <see cref="ClipParam"/> (set for one frame, AnyState → the clip's state; no trigger left armed when a clip is
    /// missing). ArtAnimatorBuilder creates a state only for the clips the set carries. Rig-agnostic (names and directions only).
    /// WP-082 R3 (LOCO-SOZLESME §6–§8): the states are driven by the motion-time parameter <see cref="TimeParam"/>
    /// (<see cref="LocomotionV4Clock"/>: distance-matched starts, remaining-distance stops).
    /// </summary>
    public static class LocomotionV4
    {
        public const string ClipPrefix = "AP026_tp_";
        public const string StateMachine = "LocomotionV4";
        /// <summary>Int parameter: the clip id to enter this frame, <see cref="None"/> otherwise.</summary>
        public const string ClipParam = "V4Clip";
        /// <summary>WP-082 R3: float parameter, every LocomotionV4 state's motion time (normalised 0..1, monotone; 0 while no v4 state plays). Written before <see cref="ClipParam"/>.</summary>
        public const string TimeParam = "V4Time";
        public const int None = -1;

        /// <summary>The eight travel directions in body space, clockwise from forward (AP-026 direction names).</summary>
        public static readonly string[] Directions = { "fwd", "fwd_r", "right", "back_r", "back", "back_l", "left", "fwd_l" };
        public static readonly string[] Gaits = { "walk", "jog", "run", "sprint" };
        public static readonly int[] PivotAngles = { 90, 135, 180 };

        public const int StartBase = 0, StopBase = 64, PivotBase = 128, TurnBase = 140, Count = 142;

        /// <summary>WP-082 R3: the short-name hash of every id's state (<see cref="StateName"/>), computed once.</summary>
        private static readonly int[] s_stateHash = BuildStateHashes();

        private static int[] BuildStateHashes()
        {
            var h = new int[Count];
            for (int id = 0; id < Count; id++) h[id] = Animator.StringToHash(StateName(id));
            return h;
        }

        /// <summary>WP-082 R3: the state's short-name hash of an id (0 outside 0..Count-1).</summary>
        public static int StateHash(int id) => id >= 0 && id < Count ? s_stateHash[id] : 0;

        /// <summary>Direction index (0..7) of a body-space planar vector (x right, y forward); -1 for a zero vector.</summary>
        public static int Direction8(Vector2 local)
        {
            if (local.sqrMagnitude < 1e-8f) return -1;
            float deg = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;   // 0 forward, +90 right
            return ((Mathf.RoundToInt(deg / 45f) % 8) + 8) % 8;
        }

        public static int StartId(int gait, int dir, bool leftFoot) => StartBase + gait * 16 + dir * 2 + (leftFoot ? 0 : 1);
        public static int StopId(int gait, int dir, bool leftFoot) => StopBase + gait * 16 + dir * 2 + (leftFoot ? 0 : 1);
        public static int PivotId(int angleIndex, bool toRight, bool leftFoot) => PivotBase + angleIndex * 4 + (toRight ? 2 : 0) + (leftFoot ? 0 : 1);
        public static int TurnId(bool leftFoot) => TurnBase + (leftFoot ? 0 : 1);

        public static bool IsStart(int id) => id >= StartBase && id < StopBase;
        public static bool IsStop(int id) => id >= StopBase && id < PivotBase;

        /// <summary>
        /// WP-082 R3 (LOCO-SOZLESME §7.1, S-06 B3): the start's lead foot — sideways and diagonal, the foot on the travel side (the
        /// open step; the other foot crosses over and its clip is 25 → 31–37 frames); forward the left, backward the right
        /// (<c>loco_constants.json#start_lead_default</c>, 24/24 the shortest clip or a tie).
        /// </summary>
        public static bool StartLeadLeft(int dir) => dir == 0 || dir >= 5;

        /// <summary>The AP-026 clip name of an id (null outside 0..Count-1).</summary>
        public static string ClipName(int id)
        {
            if (id < 0 || id >= Count) return null;
            string foot = (id & 1) == 0 ? "l" : "r";
            if (id < PivotBase)
            {
                int k = id < StopBase ? id - StartBase : id - StopBase;
                return ClipPrefix + Gaits[k / 16] + (id < StopBase ? "_start_" : "_stop_") + Directions[(k % 16) / 2] + "_" + foot;
            }

            if (id < TurnBase)
            {
                int k = id - PivotBase;
                return ClipPrefix + "pivot_" + PivotAngles[k / 4] + ((k & 2) != 0 ? "_r_" : "_l_") + foot;
            }

            return ClipPrefix + "turn_45_" + foot;
        }

        /// <summary>The sub-state machine's state name of an id (the clip name without the prefix).</summary>
        public static string StateName(int id)
        {
            string clip = ClipName(id);
            return clip == null ? null : "V4_" + clip.Substring(ClipPrefix.Length);
        }

        /// <summary>The gait band of a speed: the nearest gait reference speed (m/s) in <paramref name="gaitSpeeds"/> (walk…sprint; +∞ = gait off).</summary>
        public static int GaitFor(float speed, float[] gaitSpeeds)
        {
            int best = 0;
            for (int i = 1; i < gaitSpeeds.Length && i < Gaits.Length; i++)
                if (Mathf.Abs(speed - gaitSpeeds[i]) < Mathf.Abs(speed - gaitSpeeds[best])) best = i;
            return best;
        }

        /// <summary>
        /// WP-082 R3: a uniform lookup table read at <paramref name="x"/> (index = x / step, linear between entries, clamped at
        /// both ends). Zero allocation.
        /// </summary>
        public static float LutLookup(float[] lut, float step, float x)
        {
            if (lut == null || lut.Length == 0) return 0f;
            if (step <= 1e-6f || x <= 0f) return lut[0];
            float i = x / step;
            int last = lut.Length - 1;
            if (i >= last) return lut[last];
            int a = (int)i;
            return Mathf.LerpUnclamped(lut[a], lut[a + 1], i - a);
        }

        /// <summary>WP-082 R3: a per-frame table read at a normalised clip time (frame = t × (n − 1), linear).</summary>
        public static float FrameLookup(float[] perFrame, float tNorm)
        {
            if (perFrame == null || perFrame.Length == 0) return 0f;
            int last = perFrame.Length - 1;
            float f = Mathf.Clamp01(tNorm) * last;
            int a = (int)f;
            if (a >= last) return perFrame[last];
            // the phase tables wrap (… 0.98, 0.01 …): interpolate on the circle
            float p = perFrame[a], q = perFrame[a + 1];
            float d = q - p;
            if (d > 0.5f) d -= 1f; else if (d < -0.5f) d += 1f;
            float r = p + d * (f - a);
            return r - Mathf.Floor(r);
        }

        /// <summary>Circular distance of two cycle phases (0..0.5).</summary>
        public static float PhaseDistance(float a, float b)
        {
            float d = Mathf.Abs(a - b);
            d -= Mathf.Floor(d);
            return d > 0.5f ? 1f - d : d;
        }
    }

    /// <summary>
    /// WP-082 R3 (LOCO-SOZLESME §7–§8, §11): the v4 tables indexed by id, built once from the set's
    /// <see cref="CharacterAnimationSet.LocomotionTables"/> (the builder's precomputed arrays) — no JSON, no allocation per frame.
    /// </summary>
    public sealed class LocomotionV4Tables
    {
        private readonly CharacterAnimationSet.LocoV4Clip[] _byId = new CharacterAnimationSet.LocoV4Clip[LocomotionV4.Count];

        /// <summary>k: template metres → hero metres.</summary>
        public float StrideScale { get; private set; } = 1f;
        public int Count { get; private set; }

        public LocomotionV4Tables(CharacterAnimationSet.LocomotionTables source)
        {
            if (source == null) return;
            StrideScale = source.heroStrideScale > 0.01f ? source.heroStrideScale : 1f;
            if (source.v4 == null) return;
            for (int i = 0; i < source.v4.Count; i++)
            {
                CharacterAnimationSet.LocoV4Clip c = source.v4[i];
                if (c == null || c.v4Id < 0 || c.v4Id >= LocomotionV4.Count || c.seconds <= 1e-3f) continue;
                if (LocomotionV4.IsStart(c.v4Id) && (c.lutTAtDist == null || c.lutTAtDist.Length < 2)) continue;
                if (LocomotionV4.IsStop(c.v4Id) && (c.lutTAtRemaining == null || c.lutTAtRemaining.Length < 2)) continue;
                if (_byId[c.v4Id] == null) Count++;
                _byId[c.v4Id] = c;
            }
        }

        public bool Has(int id) => id >= 0 && id < LocomotionV4.Count && _byId[id] != null;
        public CharacterAnimationSet.LocoV4Clip Get(int id) => Has(id) ? _byId[id] : null;
        public float Seconds(int id) => Has(id) ? _byId[id].seconds : 1f;

        /// <summary>Start: the clip time at <paramref name="heroMetres"/> of travel (§7.2).</summary>
        public float StartTimeAt(int id, float heroMetres)
        {
            CharacterAnimationSet.LocoV4Clip c = Get(id);
            return c == null ? 0f : LocomotionV4.LutLookup(c.lutTAtDist, c.distStepMTemplate, heroMetres / StrideScale);
        }

        /// <summary>Stop: the earliest clip time with ≤ <paramref name="heroMetres"/> of travel left (§8.3; 0 → settle).</summary>
        public float StopTimeAt(int id, float heroMetres)
        {
            CharacterAnimationSet.LocoV4Clip c = Get(id);
            if (c == null) return 0f;
            if (heroMetres <= 1e-4f) return c.settleTNorm;
            return LocomotionV4.LutLookup(c.lutTAtRemaining, c.remainingStepMTemplate, heroMetres / StrideScale);
        }

        /// <summary>The loop phase (0 = foot_l) the clip's pose at <paramref name="tNorm"/> stands for.</summary>
        public float PhaseAt(int id, float tNorm)
        {
            CharacterAnimationSet.LocoV4Clip c = Get(id);
            return c == null ? 0f : LocomotionV4.FrameLookup(c.loopPhaseEquivNormPerFrame, tNorm);
        }

        public float ExitT(int id) => Has(id) ? (LocomotionV4.IsStart(id) ? _byId[id].exitTNorm : 1f) : 1f;
        public float ExitOffset(int id) => Has(id) && LocomotionV4.IsStart(id) ? _byId[id].loopOffsetNorm : 0f;

        /// <summary>
        /// §8.3: the stop variant (l / r) whose entry frame's loop phase is nearest <paramref name="phaseNow"/> (tie → l), and its
        /// entry time. Returns false when neither variant has a table.
        /// </summary>
        public bool PickStop(int gait, int dir, float remainingHeroMetres, float phaseNow, out bool leftFoot, out float entryT)
        {
            int l = LocomotionV4.StopId(gait, dir, true), r = LocomotionV4.StopId(gait, dir, false);
            bool hasL = Has(l), hasR = Has(r);
            leftFoot = true;
            entryT = 0f;
            if (!hasL && !hasR) return false;
            float tL = hasL ? StopTimeAt(l, remainingHeroMetres) : 0f, tR = hasR ? StopTimeAt(r, remainingHeroMetres) : 0f;
            float cL = hasL ? LocomotionV4.PhaseDistance(PhaseAt(l, tL), phaseNow) : float.MaxValue;
            float cR = hasR ? LocomotionV4.PhaseDistance(PhaseAt(r, tR), phaseNow) : float.MaxValue;
            leftFoot = cL <= cR;
            entryT = leftFoot ? tL : tR;
            return true;
        }
    }

    /// <summary>
    /// WP-083 B3b: start / stop / pivot detection — the event a v4 clip answers. The decision reads where the body is heading: the
    /// motor's commanded velocity when the owner gives it (PlayerMotor.CommandedPlanarVelocity: input × speed before acceleration),
    /// else the velocity a short <see cref="Lookahead"/> ahead (velocity + smoothed acceleration × lookahead, never past a stop along
    /// the travel — from velocity alone a brake and a reversal look the same until the body has stopped, so without the command a
    /// reversal reads as stop + start and pivots come only from the command).
    /// <list type="bullet">
    /// <item>Start: from rest (≥ <see cref="RestSeconds"/> under <see cref="RestSpeed"/>, or still now) the heading reaches
    /// <see cref="MoveSpeed"/> — direction of the heading in body space, gait = the target speed's band.</item>
    /// <item>Pivot: moving at ≥ <see cref="MoveSpeed"/>, the heading turns ≥ <see cref="PivotMinDegrees"/> from the travel (nearest
    /// of 90 / 135 / 180, side by sign; then <see cref="PivotCooldown"/>). WP-082 R3 (§6): never while <see cref="AimDriven"/> (top-down:
    /// the body faces the aim, a pivot clip would turn the hips against it) — the 8-way tree carries the turn.</item>
    /// <item>Stop: moving ≥ <see cref="MovingSeconds"/>, the heading stays under <see cref="MoveSpeed"/> × 0.5 — on the motor's command
    /// the first such frame (WP-082 R3 §8.1), from the prediction for <see cref="StopConfirm"/> — direction / gait of the last steady
    /// (not braking) travel.</item>
    /// </list>
    /// The lead foot: with <see cref="ContractLead"/> (WP-082 R3 §7.1 / §8.3) the start takes <see cref="LocomotionV4.StartLeadLeft"/>
    /// and the stop the variant whose entry phase matches <see cref="PhaseNow"/> (<see cref="Tables"/>); without it (tests of the
    /// B3b detector) the foot NOT planted. Zero allocation.
    /// </summary>
    public sealed class LocomotionV4Events
    {
        public const float RestSpeed = 0.15f, MoveSpeed = 0.6f, RestSeconds = 0.1f, MovingSeconds = 0.2f;
        public const float Lookahead = 0.2f, AccelSmoothing = 0.05f, PivotMinDegrees = 67.5f, PivotCooldown = 0.3f, StopConfirm = 0.05f;
        /// <summary>WP-082 R3 §8.2: the motor's braking rate the remaining stop distance assumes (PlayerConfigSection.Deceleration default, m/s²).</summary>
        public const float DefaultDeceleration = 60f;

        private enum Phase { Rest, Moving, Stopping }

        private Phase _phase;
        private float _rest, _moving, _cooldown, _stopHold;
        private Vector3 _lastVelocity, _accel, _steadyDir, _lastDir;
        private float _steadySpeed;
        private bool _hasLast;

        public int LastEvent { get; private set; } = LocomotionV4.None;
        /// <summary>The heading of the last tick (commanded, or predicted; tests / diagnostics).</summary>
        public Vector3 Heading { get; private set; }

        /// <summary>WP-082 R3 §6: the body's facing belongs to the aim (top-down) — no pivot / turn ids. Written by the owner before <see cref="Feed"/>.</summary>
        public bool AimDriven { get; set; }
        /// <summary>WP-082 R3 §7.1 / §8.3: the contract's lead-foot rule instead of the free foot (S-06 B3).</summary>
        public bool ContractLead { get; set; }
        /// <summary>WP-082 R3: the v4 tables (stop variant + entry time); null = no distance tables.</summary>
        public LocomotionV4Tables Tables { get; set; }
        /// <summary>WP-082 R3 §8.3: the current loop phase (0 = foot_l) of the pose, written by the owner before <see cref="Feed"/>.</summary>
        public float PhaseNow { get; set; }
        /// <summary>WP-082 R3 §8.2: the braking rate for d_rem = v² / (2 a).</summary>
        public float Deceleration { get; set; } = DefaultDeceleration;
        /// <summary>WP-082 R3 §7.2 option A: starts off (the loop + LocomotionLean only). Tests / measurement switch.</summary>
        public static bool StartsEnabled = true;
        /// <summary>WP-082 R3: the last stop's entry time (§8.3; 0 for the other events).</summary>
        public float EntryT { get; private set; }
        /// <summary>WP-082 R3: moving (not resting, not braking to a stop).</summary>
        public bool IsMoving => _phase == Phase.Moving;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => StartsEnabled = true;

        public void Reset()
        {
            _phase = Phase.Rest;
            _rest = _moving = _cooldown = _stopHold = 0f;
            _lastVelocity = _accel = _steadyDir = _lastDir = Vector3.zero;
            _steadySpeed = 0f;
            _hasLast = false;
            LastEvent = LocomotionV4.None;
            Heading = Vector3.zero;
            EntryT = 0f;
        }

        /// <summary>One tick: returns the clip id to play now or <see cref="LocomotionV4.None"/>.</summary>
        public int Feed(Vector3 worldVelocity, Vector3? commanded, Quaternion bodyRotation, float targetSpeed, bool leftPlanted, float[] gaitSpeeds, float dt)
        {
            if (dt <= 1e-5f) return LocomotionV4.None;
            worldVelocity.y = 0f;
            if (_hasLast) _accel = Vector3.Lerp(_accel, (worldVelocity - _lastVelocity) / dt, 1f - Mathf.Exp(-dt / AccelSmoothing));
            _lastVelocity = worldVelocity;
            _hasLast = true;
            float speed = worldVelocity.magnitude;
            if (speed > RestSpeed) _lastDir = worldVelocity / speed;
            Vector3 heading;
            if (commanded.HasValue)
            {
                heading = commanded.Value;
                heading.y = 0f;
            }
            else
            {
                heading = worldVelocity + _accel * Lookahead;
                float along = Vector3.Dot(heading, _lastDir);
                if (along < 0f) heading -= _lastDir * along;   // a brake predicts a stop, never a reversal
            }

            Heading = heading;
            float ahead = heading.magnitude;
            bool braking = Vector3.Dot(_accel, worldVelocity) < 0f;
            bool lead = !leftPlanted;   // B3b detector: the lead foot steps first, the free one (ContractLead replaces it)
            _cooldown = Mathf.Max(0f, _cooldown - dt);
            int id = LocomotionV4.None;

            switch (_phase)
            {
                case Phase.Rest:
                {
                    bool settled = _rest >= RestSeconds;
                    _rest = speed < RestSpeed ? _rest + dt : 0f;
                    if (ahead >= MoveSpeed)
                    {
                        if (settled || speed < RestSpeed) id = Start(heading, bodyRotation, Mathf.Max(ahead, targetSpeed), lead, gaitSpeeds);
                        _phase = Phase.Moving;
                        _moving = _stopHold = 0f;
                    }

                    break;
                }
                case Phase.Moving:
                    _moving += dt;
                    if (speed >= MoveSpeed && !braking) { _steadyDir = worldVelocity / speed; _steadySpeed = speed; }
                    if (!AimDriven && _cooldown <= 0f && speed >= MoveSpeed && ahead >= MoveSpeed)
                    {
                        float turn = Vector3.SignedAngle(worldVelocity, heading, Vector3.up);
                        if (Mathf.Abs(turn) >= PivotMinDegrees)
                        {
                            int a = Mathf.Abs(turn) < 112.5f ? 0 : Mathf.Abs(turn) < 157.5f ? 1 : 2;
                            id = LocomotionV4.PivotId(a, turn > 0f, lead);
                            _cooldown = PivotCooldown;
                            _stopHold = 0f;
                            break;
                        }
                    }

                    _stopHold = (braking || commanded.HasValue) && ahead < MoveSpeed * 0.5f ? _stopHold + dt : 0f;
                    // WP-082 R3 §8.1: on the motor's command the stop is decided on the first frame (no StopConfirm wait — at
                    // 6.2 m/s 0.05 s would leave 0.085 of the 0.32 m to the clip); the prediction keeps the confirmation
                    if (commanded.HasValue ? _stopHold > 0f : _stopHold >= StopConfirm - 1e-4f)
                    {
                        if (_moving >= MovingSeconds && _steadyDir.sqrMagnitude > 0f)
                        {
                            int dir = LocomotionV4.Direction8(Local(bodyRotation, _steadyDir));
                            if (dir >= 0) id = Stop(LocomotionV4.GaitFor(_steadySpeed, gaitSpeeds), dir, lead, speed);
                        }

                        _phase = Phase.Stopping;
                    }

                    break;
                case Phase.Stopping:
                    if (speed < RestSpeed) { _phase = Phase.Rest; _rest = 0f; }
                    else if (!braking && ahead >= MoveSpeed)
                    {
                        // re-accelerated before rest: a start only when the travel turned back (a reversal without the motor's command)
                        if (Vector3.Dot(worldVelocity, _steadyDir) < 0f) id = Start(heading, bodyRotation, Mathf.Max(ahead, targetSpeed), lead, gaitSpeeds);
                        _phase = Phase.Moving;
                        _moving = _stopHold = 0f;
                    }

                    break;
            }

            if (id != LocomotionV4.None) LastEvent = id;
            return id;
        }

        private int Start(Vector3 heading, Quaternion body, float speed, bool lead, float[] gaitSpeeds)
        {
            EntryT = 0f;
            if (!StartsEnabled) return LocomotionV4.None;
            int dir = LocomotionV4.Direction8(Local(body, heading));
            if (dir < 0) return LocomotionV4.None;
            if (ContractLead) lead = LocomotionV4.StartLeadLeft(dir);
            return LocomotionV4.StartId(LocomotionV4.GaitFor(speed, gaitSpeeds), dir, lead);
        }

        private int Stop(int gait, int dir, bool lead, float speed)
        {
            EntryT = 0f;
            if (!ContractLead) return LocomotionV4.StopId(gait, dir, lead);
            // §8.2–8.3: the remaining braking distance picks the entry frame; the variant whose entry phase meets the pose's phase
            float remaining = speed * speed / (2f * Mathf.Max(0.1f, Deceleration));
            if (Tables != null && Tables.PickStop(gait, dir, remaining, PhaseNow, out bool left, out float t))
            {
                EntryT = t;
                return LocomotionV4.StopId(gait, dir, left);
            }

            // no tables: the foot that lands next in the loop (foot_l at phase 0, foot_r at ≈ 0.5)
            float p = PhaseNow - Mathf.Floor(PhaseNow);
            return LocomotionV4.StopId(gait, dir, p >= 0.5f);
        }

        private static Vector2 Local(Quaternion body, Vector3 dirW)
        {
            Vector3 l = Quaternion.Inverse(body) * dirW;
            return new Vector2(l.x, l.z);
        }
    }

    /// <summary>
    /// WP-082 R3 (LOCO-SOZLESME §7.2, §8.4): the motion time of the playing v4 clip. A start follows the distance the body has
    /// really travelled (template metres = hero metres / k through the clip's LUT; monotone; at most <see cref="StartRateCap"/> ×
    /// the clip's own rate, past it the feet slide), a stop follows the remaining braking distance (never behind the clip's own
    /// rate; motor stopped → settle). The owner writes <see cref="T"/> to <see cref="LocomotionV4.TimeParam"/>. Zero allocation.
    /// </summary>
    public sealed class LocomotionV4Clock
    {
        public enum Kind { None, Start, Stop, Other }

        /// <summary>Tick result: keep playing, leave to Locomotion (the clip's end / exit), or cancel the start (the body did not go).</summary>
        public enum Step { Playing, Exit, Cancel }

        /// <summary>§7.2 option B' (3.0): the start's highest playback rate × its own rate; +∞ = pure distance matching (option B).</summary>
        public static float StartRateCap = 3f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => StartRateCap = 3f;

        /// <summary>§7.2: a start whose body stays under <see cref="LocomotionV4Events.RestSpeed"/> this long is cancelled (a wall).</summary>
        public const float CancelSeconds = 0.1f;
        /// <summary>A position step longer than this in one tick is a teleport, not travel (metres).</summary>
        public const float MaxStepMetres = 0.5f;

        private Kind _kind;
        private int _id = LocomotionV4.None;
        private float _t, _dist, _slow, _seconds = 1f;
        private Vector3 _lastPos;

        public Kind Current => _kind;
        public int Id => _id;
        /// <summary>The normalised motion time (0..1).</summary>
        public float T => _t;
        /// <summary>The start's travelled distance, hero metres (tests).</summary>
        public float Distance => _dist;

        public void Reset()
        {
            _kind = Kind.None;
            _id = LocomotionV4.None;
            _t = _dist = _slow = 0f;
            _seconds = 1f;
        }

        /// <summary>A v4 clip begins at <paramref name="entryT"/> (start: 0; stop: the remaining-distance entry).</summary>
        public void Begin(int id, float entryT, float seconds, Vector3 position)
        {
            _id = id;
            _kind = LocomotionV4.IsStart(id) ? Kind.Start : LocomotionV4.IsStop(id) ? Kind.Stop : Kind.Other;
            _t = Mathf.Clamp01(entryT);
            _dist = 0f;
            _slow = 0f;
            _seconds = Mathf.Max(0.05f, seconds);
            _lastPos = position;
        }

        /// <summary>The clip ended or was left: no v4 time (the owner writes 0).</summary>
        public void End() => Reset();

        /// <summary>One frame of the clip's time. <paramref name="exitT"/> = the start's exit_t_norm (stops / others: 1).</summary>
        public Step Tick(LocomotionV4Tables tables, Vector3 position, float speed, float deceleration, float exitT, float dt)
        {
            if (_kind == Kind.None || dt <= 0f) return Step.Playing;
            Vector3 d = position - _lastPos;
            d.y = 0f;
            _lastPos = position;
            float step = d.magnitude;
            float natural = dt / _seconds;
            switch (_kind)
            {
                case Kind.Start:
                {
                    if (step <= MaxStepMetres) _dist += step;
                    float tD = tables != null ? tables.StartTimeAt(_id, _dist) : _t + natural;
                    float tN = _t + natural * StartRateCap;
                    _t = Mathf.Clamp01(Mathf.Max(_t, Mathf.Min(tD, tN)));   // monotone; over the cap the feet slide
                    _slow = speed < LocomotionV4Events.RestSpeed ? _slow + dt : 0f;
                    if (_slow >= CancelSeconds) return Step.Cancel;
                    return _t >= exitT - 1e-4f ? Step.Exit : Step.Playing;
                }
                case Kind.Stop:
                {
                    float remaining = speed * speed / (2f * Mathf.Max(0.1f, deceleration));
                    float tD = tables != null ? tables.StopTimeAt(_id, remaining) : 0f;
                    _t = Mathf.Clamp01(Mathf.Max(_t + natural, tD));   // never behind the motor; at rest the clip settles at its own rate
                    return _t >= 1f - 1e-4f ? Step.Exit : Step.Playing;
                }
                default:
                    _t = Mathf.Clamp01(_t + natural);
                    return _t >= exitT - 1e-4f ? Step.Exit : Step.Playing;
            }
        }

        /// <summary>After the exit the outgoing clip keeps its own rate through the blend (never past 1).</summary>
        public void Coast(float dt)
        {
            if (_kind != Kind.None && dt > 0f) _t = Mathf.Min(1f, _t + dt / _seconds);
        }
    }
}
