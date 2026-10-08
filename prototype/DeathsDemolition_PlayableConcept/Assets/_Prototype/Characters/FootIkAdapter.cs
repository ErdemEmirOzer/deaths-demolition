using System;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-032 · ADR-032-1 · Foot placement on the real ground through the built-in Humanoid
    /// IK goals (no Animation Rigging): each foot's animated position is dropped onto the
    /// ground hit below it (steps, thresholds, the bridge) and the pelvis is lowered by the
    /// deeper foot so no leg over-stretches. Budgeted: only near bodies (not
    /// <see cref="CharacterAnimatorLod.Far"/>) and at most <see cref="MaxBodiesPerFrame"/>
    /// per frame (two raycasts each), off while dying / rising (the clip owns the floor
    /// contact there). Zero allocation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootIkAdapter : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Bodies that get foot IK in one frame (nearest LOD0/1 first by arrival). Cost cap ≤ 0.6 ms @ 40. Invented.", true)]
        public const int MaxBodiesPerFrame = 12;

        [PrototypeOnly(PendingDecisions.None, "Raycast starts this far above the animated ankle and reaches this far below (steps / thresholds). Invented.", true)]
        public const float RayUp = 0.5f, RayDown = 0.6f;

        [PrototypeOnly(PendingDecisions.None, "Largest pelvis drop / foot lift the adapter applies, metres. Invented.", true)]
        public const float MaxCorrection = 0.35f;

        [PrototypeOnly(PendingDecisions.None, "Position / rotation IK weight response (1/s). Invented.", true)]
        public const float Response = 12f;

        [PrototypeOnly(PendingDecisions.None, "WP-071 foot lock (event-driven: the clip's plant events): unused height slack, blend time, release at this fraction of the leg reach. Invented / III A2.4.", true)]
        public const float LockHeight = 0.025f, LockBlendSeconds = 0.06f, LockMaxStretch = 1.0f;   // B2: 0.96 released every walking stance (the clip's own stance leg is ~97 % extended) — release at full reach
        [PrototypeOnly(PendingDecisions.None, "WP-071 foot lock bodies per frame (Yüksek: player + ≤ 8 zombies; quality tier sets it). Invented.", true)]
        public static int MaxLockBodiesPerFrame = 9;
        [PrototypeOnly(PendingDecisions.None, "WP-071 (C-00 01.10, 4a): a plant within this time of the last accepted one is the same footfall — every clip of a locomotion blend fires its own foot_l / foot_r 1–2 frames apart (diagonals: 3 per step, each re-picking the held foot). Invented.", true)]
        public static float PlantDebounceSeconds = 0.1f;
        [PrototypeOnly(PendingDecisions.None, "WP-071 (C-00 01.10, 4a r25): the stretch release is relative — the reach at the lock frame plus this share of the leg (the IK-pass hip vs the IK target reads ≈ 10 % over the bone geometry, so the absolute 1.0 released every lock one frame later). Invented.", true)]
        public const float LockStretchSlack = 0.05f;

        [SerializeField] private Animator _animator;
        [SerializeField, Range(0f, 1f)] private float _rotationWeight = 0.6f;

        private Func<bool> _enabled;
        private Func<bool> _near;
        private Func<bool> _lockGate;
        // WP-071 foot lock state per foot (0 left, 1 right)
        private readonly bool[] _locked = new bool[2];
        private readonly float[] _lockWeight = new float[2];
        private readonly Vector3[] _lockPos = new Vector3[2];
        private readonly float[] _lockReach = new float[2];
        private int _plantPending;
        private float _lastPlantTime = -1f;
        /// <summary>WP-071 diagnostics: locked feet released by the leg stretch / the gate closing / a lift event / the other foot's plant.</summary>
        public int ReleasedByStretch { get; private set; }
        public int ReleasedByGate { get; private set; }
        public int ReleasedByLift { get; private set; }
        public int ReleasedByPlant { get; private set; }
        /// <summary>WP-071 diagnostics: plant events accepted / dropped as the same footfall.</summary>
        public int PlantsAccepted { get; private set; }
        public int PlantsDebounced { get; private set; }
        /// <summary>WP-071 diagnostics: the leg length the stretch release uses, and hip→held point / leg length at the last stretch release.</summary>
        public float LegLength => _legLength;
        public float LastReleaseRatio { get; private set; }
        private readonly bool[] _releasePending = new bool[2];
        private readonly float[] _gapThisFrame = new float[2];
        private readonly Vector3[] _targetThisFrame = new Vector3[2];
        private float _legLength;
        private bool _lockThisFrame;
        private static int s_locked;
        /// <summary>WP-071: feet currently held by the lock (tests / probes).</summary>
        public int LockedFeet => (_locked[0] ? 1 : 0) + (_locked[1] ? 1 : 0);
        public bool IsLocked(int foot) => _locked[foot];
        /// <summary>WP-071 lock readouts of the last solve (tests): gate open, left-foot height over its ground contact, moving back in body space.</summary>
        public bool LockGateOpen => _lockThisFrame;
        public float LastHeightGap { get; private set; }

        public static int LockBodiesThisFrame => s_locked;
        private float _weight;
        private float _pelvisOffset;
        private float _leftLift, _rightLift;
        private int _ikFrame = -1;
        private int _lastServedFrame = -2;
        private bool _servedThisFrame;
        private static int s_frame = -1;
        private static int s_served;
        private static int s_raycasts;

        /// <summary>Bodies served this frame / raycasts this frame (probe / tests).</summary>
        public static int ServedThisFrame => s_served;
        public static int RaycastsThisFrame => s_raycasts;
        public float Weight => _weight;
        public float PelvisOffset => _pelvisOffset;
        public float LeftLift => _leftLift;
        public float RightLift => _rightLift;
        public bool Served => _servedThisFrame;
        /// <summary>Tests: force the adapter on/off regardless of the near gate (-1 = automatic).</summary>
        public static int Force = -1;
        /// <summary>Tests only: every served body allocates once (proves the crowd allocation gate catches a per-body allocation).</summary>
        public static bool ProbeAllocPerServe;
        private static byte[] s_probeSink;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 (PreserveSwingLift): the lower animated sole further than this above its ground is a flight phase (a run) — the " +
            "common correction eases to 0 instead of pulling the body's feet down. Invented, tune.", true)]
        public const float SwingFlightGap = 0.10f;

        /// <summary>
        /// WP-096 (S-03, zombie owner sets it): one common height correction for both feet — the one that puts the LOWER animated
        /// sole on its ground — instead of dropping each foot onto its own ground hit. The per-foot drop flattened the swinging foot
        /// onto the floor whenever it was within <see cref="MaxCorrection"/> of it (every stride: the feet shuffled, and every frame
        /// read as contact). The player keeps the per-foot solve (S-06's adapter; false by default).
        /// </summary>
        public bool PreserveSwingLift;
        private float _commonDelta;

        // ------------------------------------------------------------------ WP-096 zombie lock (S-03 08.10, zombie owner only)
        [PrototypeOnly(PendingDecisions.None,
            "WP-096 ZombieLock: a held foot may sit at most this far (horizontal, m) from where the clip puts it — a SAFETY bound only " +
            "(a missed lift event); the stance ends on the clip's own foot_*_off. ik1 (08.10): at 0.08 m the heel-to-toe roll of the " +
            "ankle (the IK goal) released 84/85 feral_A and 57/60 lurch_A locks mid-stance and the release blend WAS the slide. Tune.", true)]
        public const float ZombieLockMaxOffset = 0.25f;
        [PrototypeOnly(PendingDecisions.None,
            "WP-096 ZombieLock: a body served / locked last frame keeps its slot while it is within this factor of last frame's " +
            "budget cut distance (hysteresis — the near-first ranking does not flicker a body in and out). Invented.", true)]
        public const float ZombiePriorityHysteresis = 1.15f;

        /// <summary>
        /// WP-096 (S-03 08.10, zombie owner sets it, false for the player): (1) a plant event locks the foot its event names (the
        /// AP-033 labels are verified against the geometric stance by the intake calibration: 0.93–1.00) instead of the lower
        /// animated foot; (2) the held foot is released once it would sit more than <see cref="ZombieLockMaxOffset"/> from its
        /// animated position (<see cref="ReleasedByOffset"/>) instead of the leg-stretch rule; (3) the IK and lock budgets go to the
        /// NEAREST zombies (camera distance, one-frame-lagged cut with hysteresis) instead of the Animator update order.
        /// </summary>
        public bool ZombieLock;
        /// <summary>Owner-selected clip contract: both foot_*_off events exist, so a plant must preserve the other foot's
        /// double-support stance until its own lift. False keeps the legacy other-plant release for clips without lift events.</summary>
        public bool ZombieExplicitLiftEvents;
        private int _zombiePlantPending;
        private readonly float[] _zombieLastPlantTime = { -1f, -1f };
        /// <summary>WP-096 diagnostics: locks released by <see cref="ZombieLockMaxOffset"/>.</summary>
        public int ReleasedByOffset { get; private set; }
        /// <summary>WP-096 diagnostics: frames this body wanted the IK / the lock and the budget refused it.</summary>
        public int BudgetDeniedServe { get; private set; }
        public int BudgetDeniedLock { get; private set; }
        private int _plantFoot = -1;
        private int _lastLockFrame = -2;
        private const int RankCapacity = 32;
        private static readonly float[] s_serveRank = new float[RankCapacity], s_lockRank = new float[RankCapacity];
        private static int s_serveRankN, s_lockRankN, s_otherServed, s_otherLocked;
        private static float s_serveCut = float.MaxValue, s_lockCut = float.MaxValue;
        private static Vector3 s_rankOrigin;
        private static bool s_rankOriginValid;

        /// <summary>Insert a distance into a sorted fixed-size rank (the nearest <paramref name="keep"/> kept), zero allocation.</summary>
        private static void Rank(float[] rank, ref int n, int keep, float d)
        {
            keep = Mathf.Clamp(keep, 0, rank.Length);
            if (keep == 0) return;
            if (n >= keep && d >= rank[keep - 1]) return;
            int i = Mathf.Min(n, keep - 1);
            while (i > 0 && rank[i - 1] > d) { rank[i] = rank[i - 1]; i--; }
            rank[i] = d;
            if (n < keep) n++;
        }

        /// <summary>Last frame's cut: the distance of the last body that fitted the budget (MaxValue when the budget was not full).</summary>
        private static float Cut(float[] rank, int n, int keep) => keep <= 0 ? -1f : n >= keep ? rank[keep - 1] : float.MaxValue;
        /// <summary>WP-096 readout: the common correction of the last PreserveSwingLift solve (metres).</summary>
        public float CommonDelta => _commonDelta;


        // Read-only diagnostics. Both switches default to false and never take part in an IK/lock decision.
        public bool TraceZombieLock;
        public bool TraceFootIkGoals;

        public readonly struct TracePassStamp
        {
            public readonly int Frame, LayerIndex;
            public readonly uint Sequence;
            internal TracePassStamp(int frame, int layer, uint sequence) { Frame = frame; LayerIndex = layer; Sequence = sequence; }
        }

        public readonly struct TraceReleaseCounters
        {
            public readonly int Stretch, Gate, Lift, Plant, Offset;
            internal TraceReleaseCounters(int stretch, int gate, int lift, int plant, int offset)
            { Stretch = stretch; Gate = gate; Lift = lift; Plant = plant; Offset = offset; }
        }

        public readonly struct TraceLockState
        {
            public readonly bool Locked, ReleasePending, GateOpenCached, GateEvaluatedThisPass, Served, SupportsZombieData;
            public readonly Vector3 LockPosition;
            public readonly float LockWeight, AdapterWeight;
            public readonly int PlantPending, ZombiePlantPending;
            public readonly TraceReleaseCounters Releases;
            internal TraceLockState(bool locked, Vector3 position, float lockWeight, bool releasePending, int plantPending,
                int zombiePlantPending, bool gate, bool gateEvaluated, bool served, float weight, bool zombieData, TraceReleaseCounters releases)
            {
                Locked = locked; LockPosition = position; LockWeight = lockWeight; ReleasePending = releasePending;
                PlantPending = plantPending; ZombiePlantPending = zombiePlantPending; GateOpenCached = gate;
                GateEvaluatedThisPass = gateEvaluated; Served = served; AdapterWeight = weight;
                SupportsZombieData = zombieData; Releases = releases;
            }
        }

        [Flags]
        public enum TraceLockBranches : byte
        {
            None = 0, UnlockedAtEntry = 1, GateRelease = 2, OffsetRelease = 4, StretchRelease = 8,
            HeldReturn = 16, RawTargetReturn = 32, LiftRelease = 64, PlantRelease = 128
        }

        public readonly struct LockTraceSnapshot
        {
            public readonly bool Captured, OffsetEvaluated;
            public readonly TracePassStamp Stamp;
            public readonly int Foot;
            public readonly Vector3 AnimatedInput, TargetInput, OffsetXZ, ReturnedTarget;
            public readonly float OffSquared;
            public readonly TraceLockState Before, After;
            public readonly TraceLockBranches Branches;
            internal LockTraceSnapshot(TracePassStamp stamp, int foot, Vector3 animated, Vector3 target, TraceLockState before,
                TraceLockState after, bool offsetEvaluated, Vector3 offsetXZ, float offSquared, Vector3 returned, TraceLockBranches branches)
            {
                Captured = true; Stamp = stamp; Foot = foot; AnimatedInput = animated; TargetInput = target;
                Before = before; After = after; OffsetEvaluated = offsetEvaluated; OffsetXZ = offsetXZ;
                OffSquared = offSquared; ReturnedTarget = returned; Branches = branches;
            }
        }

        /// <summary>Internal Humanoid goal/request readback, never a solved scene-bone pose.</summary>
        public readonly struct GoalTraceReadback
        {
            public readonly Quaternion RootWorldRotation, LeftWorldRotation, RightWorldRotation;
            public readonly Vector3 LeftWorldPosition, RightWorldPosition;
            public readonly float LeftPositionWeight, RightPositionWeight, LeftRotationWeight, RightRotationWeight;
            internal GoalTraceReadback(Quaternion root, Vector3 leftPosition, Vector3 rightPosition, Quaternion leftRotation,
                Quaternion rightRotation, float leftPW, float rightPW, float leftRW, float rightRW)
            {
                RootWorldRotation = root; LeftWorldPosition = leftPosition; RightWorldPosition = rightPosition;
                LeftWorldRotation = leftRotation; RightWorldRotation = rightRotation;
                LeftPositionWeight = leftPW; RightPositionWeight = rightPW; LeftRotationWeight = leftRW; RightRotationWeight = rightRW;
            }
        }

        public readonly struct PairTraceState
        {
            public readonly TraceLockState Left, Right;
            public readonly float PelvisOffset, CommonDelta;
            internal PairTraceState(TraceLockState left, TraceLockState right, float pelvis, float common)
            { Left = left; Right = right; PelvisOffset = pelvis; CommonDelta = common; }
        }

        public enum GoalTraceOutcome : byte { NotCaptured, WeightZero, BudgetFade, PairCompleted, IndividualCompleted }

        public readonly struct GoalPassTraceSnapshot
        {
            public readonly bool Captured, PairCaptured, Allowed, Budget;
            public readonly TracePassStamp Stamp;
            public readonly int ForceAtEntry;
            public readonly GoalTraceOutcome Outcome;
            public readonly GoalTraceReadback InternalGoalEntry, InternalGoalAfterPair;
            public readonly PairTraceState BeforePair, AfterPair, ExitState;
            internal GoalPassTraceSnapshot(TracePassStamp stamp, int force, bool allowed, bool budget, GoalTraceOutcome outcome,
                GoalTraceReadback entry, bool pairCaptured, PairTraceState before, PairTraceState after,
                GoalTraceReadback afterGoal, PairTraceState exitState)
            {
                Captured = true; Stamp = stamp; ForceAtEntry = force; Allowed = allowed; Budget = budget; Outcome = outcome;
                InternalGoalEntry = entry; PairCaptured = pairCaptured; BeforePair = before; AfterPair = after;
                InternalGoalAfterPair = afterGoal; ExitState = exitState;
            }
        }

        private uint _tracePassSequence;
        private TracePassStamp _traceStamp;
        private bool _traceGateEvaluated, _tracePairCaptured;
        private int _traceForceAtEntry;
        private LockTraceSnapshot _traceLockLeft, _traceLockRight;
        private GoalPassTraceSnapshot _traceGoals;
        private GoalTraceReadback _traceGoalEntry, _traceGoalAfterPair;
        private PairTraceState _traceBeforePair, _traceAfterPair;
        public LockTraceSnapshot LeftLockTrace => TraceZombieLock ? _traceLockLeft : default;
        public LockTraceSnapshot RightLockTrace => TraceZombieLock ? _traceLockRight : default;
        public GoalPassTraceSnapshot GoalPassTrace => TraceFootIkGoals ? _traceGoals : default;

        public bool TryReadLockTrace(int foot, out LockTraceSnapshot snapshot)
        {
            snapshot = foot == 0 ? LeftLockTrace : foot == 1 ? RightLockTrace : default;
            return isActiveAndEnabled && snapshot.Captured && snapshot.Stamp.Frame == Time.frameCount &&
                   snapshot.Stamp.Sequence == _tracePassSequence && snapshot.Stamp.LayerIndex == _traceStamp.LayerIndex;
        }

        public bool TryReadGoalPassTrace(out GoalPassTraceSnapshot snapshot)
        {
            snapshot = GoalPassTrace;
            return isActiveAndEnabled && snapshot.Captured && snapshot.Stamp.Frame == Time.frameCount &&
                   snapshot.Stamp.Sequence == _tracePassSequence && snapshot.Stamp.LayerIndex == _traceStamp.LayerIndex;
        }

        public void Configure(Animator animator)
        {
            _animator = animator;
            ClearTraceSnapshots();
        }

        /// <summary>Owner binding: allowed (alive, not dying/rising) and near (LOD0/1).</summary>
        public void Bind(Func<bool> enabledGate, Func<bool> nearGate) => Bind(enabledGate, nearGate, null);

        /// <summary>WP-071: + the foot-lock gate (owner: cadence rate ≤ <see cref="CadenceBand.LockMaxRate"/>, moving, near).</summary>
        public void Bind(Func<bool> enabledGate, Func<bool> nearGate, Func<bool> lockGate)
        {
            _enabled = enabledGate;
            _near = nearGate;
            _lockGate = lockGate;
            ClearTraceSnapshots();
            ClearLock();
            _weight = 0f;
            _pelvisOffset = 0f;
        }

        public static void ResetStatics()
        {
            s_frame = -1;
            s_served = 0;
            s_raycasts = 0;
            s_locked = 0;
            Force = -1;
            ProbeAllocPerServe = false;
            MaxLockBodiesPerFrame = 9;
            PlantDebounceSeconds = 0.1f;
            s_serveRankN = s_lockRankN = s_otherServed = s_otherLocked = 0;   // WP-096 ZombieLock ranking
            s_serveCut = s_lockCut = float.MaxValue;
        }

        private void ClearLock()
        {
            for (int i = 0; i < 2; i++) { _locked[i] = false; _lockWeight[i] = 0f; _releasePending[i] = false; }
            _plantPending = 0;
            _zombiePlantPending = 0;
            _plantFoot = -1;
        }

        private void OnDisable()
        {
            ClearTraceSnapshots();
            _weight = 0f;
            _pelvisOffset = 0f;
            _commonDelta = 0f;
            ClearLock();
            _zombieLastPlantTime[0] = _zombieLastPlantTime[1] = -1f;
        }


        private void ClearTraceSnapshots()
        {
            _traceLockLeft = _traceLockRight = default;
            _traceGoals = default;
            _traceStamp = default;
        }

        private void BeginTracePass(int layerIndex)
        {
            if (!TraceZombieLock && !TraceFootIkGoals) return;
            unchecked { _tracePassSequence++; }
            _traceStamp = new TracePassStamp(Time.frameCount, layerIndex, _tracePassSequence);
            _traceLockLeft = _traceLockRight = default;
            _traceGoals = default;
            _traceGateEvaluated = _tracePairCaptured = false;
            _traceForceAtEntry = Force;
            _traceBeforePair = _traceAfterPair = default;
            _traceGoalAfterPair = default;
            if (TraceFootIkGoals) _traceGoalEntry = ReadGoalTraceReadback(_animator.GetIKPosition(AvatarIKGoal.LeftFoot), _animator.GetIKPosition(AvatarIKGoal.RightFoot));
        }

        private TraceLockState ReadTraceLockState(int foot) =>
            new TraceLockState(_locked[foot], _lockPos[foot], _lockWeight[foot], _releasePending[foot],
                _plantPending, _zombiePlantPending, _lockThisFrame, _traceGateEvaluated, _servedThisFrame, _weight, true,
                new TraceReleaseCounters(ReleasedByStretch, ReleasedByGate, ReleasedByLift, ReleasedByPlant, ReleasedByOffset));

        private PairTraceState ReadTracePairState() =>
            new PairTraceState(ReadTraceLockState(0), ReadTraceLockState(1), _pelvisOffset, _commonDelta);

        private GoalTraceReadback ReadGoalTraceReadback(Vector3 left, Vector3 right) =>
            new GoalTraceReadback(_animator.transform.rotation, left, right,
                _animator.GetIKRotation(AvatarIKGoal.LeftFoot), _animator.GetIKRotation(AvatarIKGoal.RightFoot),
                _animator.GetIKPositionWeight(AvatarIKGoal.LeftFoot), _animator.GetIKPositionWeight(AvatarIKGoal.RightFoot),
                _animator.GetIKRotationWeight(AvatarIKGoal.LeftFoot), _animator.GetIKRotationWeight(AvatarIKGoal.RightFoot));

        private void TracePairBefore()
        {
            if (TraceFootIkGoals) _traceBeforePair = ReadTracePairState();
        }

        private void TracePairAfter()
        {
            if (!TraceFootIkGoals) return;
            _traceAfterPair = ReadTracePairState();
            _traceGoalAfterPair = ReadGoalTraceReadback(_animator.GetIKPosition(AvatarIKGoal.LeftFoot), _animator.GetIKPosition(AvatarIKGoal.RightFoot));
            _tracePairCaptured = true;
        }

        private void PublishGoalTrace(bool allowed, bool budget, GoalTraceOutcome outcome)
        {
            if (!TraceFootIkGoals) return;
            _traceGoals = new GoalPassTraceSnapshot(_traceStamp, _traceForceAtEntry, allowed, budget, outcome, _traceGoalEntry,
                _tracePairCaptured, _traceBeforePair, _traceAfterPair, _traceGoalAfterPair, ReadTracePairState());
        }

        private void PublishLockTrace(int foot, Vector3 animated, Vector3 target, TraceLockState before,
            bool offEvaluated, Vector3 off, float offSquared, Vector3 returned, TraceLockBranches branches)
        {
            var snapshot = new LockTraceSnapshot(_traceStamp, foot, animated, target, before, ReadTraceLockState(foot),
                offEvaluated, off, offSquared, returned, branches);
            if (foot == 0) _traceLockLeft = snapshot;
            else _traceLockRight = snapshot;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || !_animator.isHuman) return;
            if (_ikFrame == Time.frameCount) return;
            _ikFrame = Time.frameCount;
            BeginTracePass(layerIndex);
            if (s_frame != Time.frameCount)
            {
                s_frame = Time.frameCount;
                s_served = 0;
                s_raycasts = 0;
                s_locked = 0;
                // WP-096 ZombieLock: last frame's near-first cuts (the slots the non-zombie bodies took stay theirs)
                s_serveCut = Cut(s_serveRank, s_serveRankN, MaxBodiesPerFrame - s_otherServed);
                s_lockCut = Cut(s_lockRank, s_lockRankN, MaxLockBodiesPerFrame - s_otherLocked);
                s_serveRankN = s_lockRankN = s_otherServed = s_otherLocked = 0;
                Camera cam = Camera.main;
                s_rankOriginValid = cam != null;
                if (s_rankOriginValid) s_rankOrigin = cam.transform.position;
            }

            bool allowed = Force == 1 || (Force != 0 && (_enabled == null || _enabled()) && (_near == null || _near()));
            bool budget = allowed && s_served < MaxBodiesPerFrame;
            float rankDistance = 0f;
            if (ZombieLock && allowed && s_rankOriginValid)
            {
                // near-first: rank every zombie that wants the IK; admit it when it was inside last frame's cut (+ hysteresis if it was served)
                rankDistance = Vector3.Distance(_animator.transform.position, s_rankOrigin);
                Rank(s_serveRank, ref s_serveRankN, MaxBodiesPerFrame, rankDistance);
                float cut = _lastServedFrame == Time.frameCount - 1 ? s_serveCut * ZombiePriorityHysteresis : s_serveCut;
                if (rankDistance > cut) budget = false;
            }

            if (allowed && !budget) BudgetDeniedServe++;
            if (budget && !ZombieLock) s_otherServed++;
            _servedThisFrame = budget;
            // R1-B AI3 (C-00 30.09): the foot lock lives only while the IK is served every frame — a gap (budget, LOD / manual
            // step, rise, disabled Animator) would otherwise blend the foot back toward a stale world point (a pop up to ~0.4 m).
            if (!budget || _lastServedFrame != Time.frameCount - 1) ClearLock();
            if (budget) _lastServedFrame = Time.frameCount;
            float k = 1f - Mathf.Exp(-Response * Time.deltaTime);
            _weight = Mathf.Lerp(_weight, budget ? 1f : 0f, k);
            if (_weight < 0.001f)
            {
                _weight = 0f;
                _pelvisOffset = 0f;
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
                _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
                PublishGoalTrace(allowed, budget, GoalTraceOutcome.WeightZero);
                return;
            }

            if (!budget)
            {
                // Fading out: keep the last targets, just lower the weights.
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, _weight);
                _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, _weight);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, _weight * _rotationWeight);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, _weight * _rotationWeight);
                PublishGoalTrace(allowed, budget, GoalTraceOutcome.BudgetFade);
                return;
            }

            s_served++;
            if (ProbeAllocPerServe) s_probeSink = new byte[16];   // tests: the positive control for the crowd allocation gate
            bool wantsLock = _lockGate != null && _lockGate();
            _lockThisFrame = wantsLock && s_locked < MaxLockBodiesPerFrame;
            if (wantsLock && ZombieLock && s_rankOriginValid)
            {
                // WP-096: the lock slots go to the nearest zombies too (a body holding a lock keeps it inside the hysteresis band)
                Rank(s_lockRank, ref s_lockRankN, MaxLockBodiesPerFrame, rankDistance);
                float cut = _lastLockFrame == Time.frameCount - 1 ? s_lockCut * ZombiePriorityHysteresis : s_lockCut;
                if (rankDistance > cut) _lockThisFrame = false;
            }

            if (wantsLock && !_lockThisFrame) BudgetDeniedLock++;
            if (_lockThisFrame) { s_locked++; _lastLockFrame = Time.frameCount; if (!ZombieLock) s_otherLocked++; }
            if (TraceZombieLock || TraceFootIkGoals) _traceGateEvaluated = true;
            if (_legLength <= 0f)
            {
                Transform up = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), lo = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), ft = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (up != null && lo != null && ft != null) _legLength = Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, ft.position);
            }

            // Consume both feet's events before either target is sent. The old right-foot callback processed a left plant
            // after the left target had already been written, leaving one extra unlocked frame on every left landing.
            if (ZombieLock) PrepareZombieLocks();

            float footHeight = _animator.leftFeetBottomHeight;
            float leftDelta, rightDelta;
            if (PreserveSwingLift)
            {
                TracePairBefore();
                SolvePair(footHeight, k, out leftDelta, out rightDelta);
                TracePairAfter();
            }
            else
            {
                Solve(AvatarIKGoal.LeftFoot, footHeight, out leftDelta);
                Solve(AvatarIKGoal.RightFoot, footHeight, out rightDelta);
            }
            _leftLift = leftDelta;
            _rightLift = rightDelta;

            // Pelvis follows the lower foot (a negative delta = the ground is below the animated foot).
            // WP-032D (WP-053 bisect): the three-argument Mathf.Min is the params overload — one float[] per served body per frame.
            float lowest = Mathf.Min(Mathf.Min(leftDelta, rightDelta), 0f);
            _pelvisOffset = Mathf.Lerp(_pelvisOffset, Mathf.Max(-MaxCorrection, lowest), k);
            if (Mathf.Abs(_pelvisOffset) > 1e-4f)
            {
                Vector3 body = _animator.bodyPosition;
                body.y += _pelvisOffset * _weight;
                _animator.bodyPosition = body;
            }
            PublishGoalTrace(allowed, budget, _tracePairCaptured ? GoalTraceOutcome.PairCompleted : GoalTraceOutcome.IndividualCompleted);
        }

        /// <summary>
        /// WP-071 · III A2.4 foot lock: in stance (the animated foot at the ground and moving back in body space) the foot is
        /// held at its landing point; it releases on lift-off, when the leg would stretch past <see cref="LockMaxStretch"/>
        /// (the slide wins), or when the gate closes — blended over <see cref="LockBlendSeconds"/>.
        /// </summary>
        private Vector3 Lock(int foot, Vector3 animated, Vector3 target, float groundFootY)
        {
            bool trace = TraceZombieLock;
            TraceLockState traceBefore = trace ? ReadTraceLockState(foot) : default;
            TraceLockBranches traceBranches = trace && !_locked[foot] ? TraceLockBranches.UnlockedAtEntry : TraceLockBranches.None;
            bool traceOffEvaluated = false;
            Vector3 traceOff = default;
            float traceOffSquared = float.NaN;
            float gap = animated.y - groundFootY;
            if (foot == 0) LastHeightGap = gap;
            // event-driven stance: a plant event locks the LOWER foot at its landing point and frees the other (the stance
            // window "own contact → the other foot's contact", as the AP-009 slip gates define it); foot_*_off frees one foot.
            if (!ZombieLock && _plantPending == 1 && foot == 1)
            {
                // Legacy player solve: both feet have been read, so pick the lower one.
                int lower = _gapThisFrame[0] <= gap ? 0 : 1;
                if (_lockThisFrame)
                {
                    _locked[lower] = true;
                    _lockPos[lower] = lower == 1 ? target : _targetThisFrame[0];
                    Transform lockHip = _animator.GetBoneTransform(lower == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                    _lockReach[lower] = lockHip != null ? Vector3.Distance(lockHip.position, _lockPos[lower]) : 0f;   // same metric as the release
                }

                if (_locked[1 - lower])
                {
                    ReleasedByPlant++;
                    if (trace && foot == 1 - lower) traceBranches |= TraceLockBranches.PlantRelease;
                }
                _locked[1 - lower] = false;
                _plantPending = 0;
            }

            if (foot == 0) { _gapThisFrame[0] = gap; _targetThisFrame[0] = target; }
            if (!ZombieLock && _releasePending[foot])
            {
                if (_locked[foot]) { ReleasedByLift++; if (trace) traceBranches |= TraceLockBranches.LiftRelease; }
                _locked[foot] = false; _releasePending[foot] = false;
            }
            if (!_lockThisFrame)
            {
                if (_locked[foot]) { ReleasedByGate++; if (trace) traceBranches |= TraceLockBranches.GateRelease; }
                _locked[foot] = false;
            }
            if (_locked[foot] && ZombieLock)
            {
                // WP-096: never hold the foot far from where the (honest) clip puts it — no over-stretched leg dragged with the body
                Vector3 off = _lockPos[foot] - animated;
                off.y = 0f;
                if (trace) { traceOffEvaluated = true; traceOff = off; traceOffSquared = off.sqrMagnitude; }
                if (off.sqrMagnitude > ZombieLockMaxOffset * ZombieLockMaxOffset)
                {
                    _locked[foot] = false; ReleasedByOffset++;
                    if (trace) traceBranches |= TraceLockBranches.OffsetRelease;
                }
            }
            else if (_locked[foot] && _legLength > 0f)
            {
                Transform hip = _animator.GetBoneTransform(foot == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                float reach = hip != null ? Vector3.Distance(hip.position, _lockPos[foot]) : 0f;
                // r25: relative to the reach at the lock frame (same metric) — the body passes over the held foot and lets it go past its landing reach
                float limit = Mathf.Max(_legLength * LockMaxStretch, _lockReach[foot] + _legLength * LockStretchSlack);
                if (hip != null && reach > limit)
                {
                    _locked[foot] = false; ReleasedByStretch++; LastReleaseRatio = reach / _legLength;   // the slide wins
                    if (trace) traceBranches |= TraceLockBranches.StretchRelease;
                }
            }

            float step = Time.deltaTime / LockBlendSeconds;
            _lockWeight[foot] = Mathf.MoveTowards(_lockWeight[foot], _locked[foot] ? 1f : 0f, step);
            if (_lockWeight[foot] <= 0f)
            {
                if (trace) PublishLockTrace(foot, animated, target, traceBefore, traceOffEvaluated, traceOff, traceOffSquared,
                    target, traceBranches | TraceLockBranches.RawTargetReturn);
                return target;
            }
            Vector3 held = _lockPos[foot];
            held.y = target.y;
            Vector3 returned = Vector3.Lerp(target, held, _lockWeight[foot]);
            if (trace) PublishLockTrace(foot, animated, target, traceBefore, traceOffEvaluated, traceOff, traceOffSquared,
                returned, traceBranches | TraceLockBranches.HeldReturn);
            return returned;
        }

        /// <summary>Zombie-only event solve. Explicit lift clips can have both feet planted at once; an opposite plant is
        /// not a lift. Event order within the frame is resolved by NotifyPlant / NotifyLift before the shared IK pass.</summary>
        private void PrepareZombieLocks()
        {
            Vector3 left = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 right = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            int plants = _zombiePlantPending;
            if (_plantPending != 0) plants |= 1 << (left.y <= right.y ? 0 : 1);
            for (int foot = 0; foot < 2; foot++)
            {
                if (_releasePending[foot])
                {
                    if (_locked[foot]) ReleasedByLift++;
                    _locked[foot] = false;
                }
                if (!_lockThisFrame)
                {
                    if (_locked[foot]) ReleasedByGate++;
                    _locked[foot] = false;
                }
                _releasePending[foot] = false;
            }
            if (_lockThisFrame)
            {
                for (int foot = 0; foot < 2; foot++)
                {
                    if ((plants & (1 << foot)) == 0) continue;
                    _locked[foot] = true;
                    _lockPos[foot] = foot == 0 ? left : right;
                    _lockWeight[foot] = 1f;
                    int other = 1 - foot;
                    if (!ZombieExplicitLiftEvents && (plants & (1 << other)) == 0)
                    {
                        if (_locked[other]) ReleasedByPlant++;
                        _locked[other] = false;
                    }
                }
            }
            _zombiePlantPending = 0;
            _plantPending = 0;
        }

        /// <summary>WP-071: the clip's foot_l / foot_r contact (owner forwards the event; the lower foot is taken, names aside).</summary>
        public void NotifyPlant() => NotifyPlant(-1);

        /// <summary>WP-096: the plant names its foot (0 left, 1 right; −1 = the lower animated foot) — used while <see cref="ZombieLock"/>.</summary>
        public void NotifyPlant(int foot)
        {
            if (ZombieLock && foot >= 0 && foot < 2)
            {
                // Debounce each foot independently: a rapid opposing step is a different contact, not a blend duplicate.
                float previous = _zombieLastPlantTime[foot];
                if (previous >= 0f && Time.time - previous < PlantDebounceSeconds) { PlantsDebounced++; return; }
                _zombieLastPlantTime[foot] = Time.time;
                PlantsAccepted++;
                _zombiePlantPending |= 1 << foot;
                _releasePending[foot] = false;   // a later plant supersedes an earlier lift / transition cleanup
                return;
            }
            // WP-071 (C-00 01.10, 4a): one footfall, one plant — the blend's other clips fire the same contact 1–2 frames later
            if (_lastPlantTime >= 0f && Time.time - _lastPlantTime < PlantDebounceSeconds) { PlantsDebounced++; return; }
            _lastPlantTime = Time.time;
            PlantsAccepted++;
            _plantPending = 1;
            _plantFoot = foot;
        }

        /// <summary>WP-071: foot_l_off / foot_r_off (AP-022) — free that foot now (0 left, 1 right).</summary>
        public void NotifyLift(int foot)
        {
            _releasePending[foot] = true;
            if (ZombieLock) _zombiePlantPending &= ~(1 << foot);   // a later lift supersedes this frame's earlier plant
        }

        /// <summary>
        /// WP-096 PreserveSwingLift solve: both feet raycast first, the correction that grounds the lower sole is shared by both
        /// (eased with the weight response) plus each foot's ground height over the lower foot's ground, so the swing foot keeps
        /// its animated lift, a step still lifts the foot above it, and a flight phase is left alone.
        /// </summary>
        private void SolvePair(float footHeight, float k, out float leftDelta, out float rightDelta)
        {
            Vector3 animatedL = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 animatedR = _animator.GetIKPosition(AvatarIKGoal.RightFoot);
            s_raycasts += 2;
            bool hitL = Physics.Raycast(animatedL + Vector3.up * RayUp, Vector3.down, out RaycastHit groundL, RayUp + RayDown, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore);
            bool hitR = Physics.Raycast(animatedR + Vector3.up * RayUp, Vector3.down, out RaycastHit groundR, RayUp + RayDown, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore);
            float gapL = hitL ? animatedL.y - (groundL.point.y + footHeight) : float.MaxValue;
            float gapR = hitR ? animatedR.y - (groundR.point.y + footHeight) : float.MaxValue;
            float lower = Mathf.Min(gapL, gapR);
            float want = !hitL && !hitR || lower > SwingFlightGap ? 0f : Mathf.Clamp(-lower, -MaxCorrection, MaxCorrection);
            _commonDelta = Mathf.Lerp(_commonDelta, want, k);
            // each foot also follows its own ground relative to the lower foot's (a step under one foot lifts that foot by the
            // step, on the flat the term is 0 and the swing keeps its animated lift)
            float reference = gapL <= gapR ? groundL.point.y : groundR.point.y;
            ApplyFoot(AvatarIKGoal.LeftFoot, 0, animatedL, hitL, groundL, footHeight, hitL ? groundL.point.y - reference : 0f, out leftDelta);
            ApplyFoot(AvatarIKGoal.RightFoot, 1, animatedR, hitR, groundR, footHeight, hitR ? groundR.point.y - reference : 0f, out rightDelta);
        }

        private void ApplyFoot(AvatarIKGoal goal, int foot, Vector3 animated, bool hit, in RaycastHit ground, float footHeight, float terrain, out float delta)
        {
            if (!hit)
            {
                delta = 0f;
                _animator.SetIKPositionWeight(goal, 0f);
                _animator.SetIKRotationWeight(goal, 0f);
                return;
            }

            delta = Mathf.Clamp(_commonDelta + terrain, -MaxCorrection, MaxCorrection);
            Vector3 target = animated;
            target.y = animated.y + delta;
            target = Lock(foot, animated, target, ground.point.y + footHeight);
            _animator.SetIKPositionWeight(goal, _weight);
            _animator.SetIKPosition(goal, target);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, ground.normal);
            _animator.SetIKRotationWeight(goal, _weight * _rotationWeight);
            _animator.SetIKRotation(goal, tilt * _animator.GetIKRotation(goal));
        }

        private void Solve(AvatarIKGoal goal, float footHeight, out float delta)
        {
            Vector3 animated = _animator.GetIKPosition(goal);
            Vector3 origin = animated + Vector3.up * RayUp;
            delta = 0f;
            s_raycasts++;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, RayUp + RayDown, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore))
            {
                float targetY = hit.point.y + footHeight;
                delta = Mathf.Clamp(targetY - animated.y, -MaxCorrection, MaxCorrection);
                Vector3 target = animated;
                target.y = animated.y + delta;
                target = Lock(goal == AvatarIKGoal.LeftFoot ? 0 : 1, animated, target, targetY);
                _animator.SetIKPositionWeight(goal, _weight);
                _animator.SetIKPosition(goal, target);
                Quaternion animatedRot = _animator.GetIKRotation(goal);
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, hit.normal);
                _animator.SetIKRotationWeight(goal, _weight * _rotationWeight);
                _animator.SetIKRotation(goal, tilt * animatedRot);
            }
            else
            {
                _animator.SetIKPositionWeight(goal, 0f);
                _animator.SetIKRotationWeight(goal, 0f);
            }
        }
    }
}
