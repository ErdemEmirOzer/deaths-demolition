using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-083 B2e (C-00 05.10, R2 H3 contactDeviationFramesMax; the user's "reload magazine animation" note) · magazine contact on the
    /// reload beats. On the Detach beat (the spent magazine leaves the well) and the Insert beat (the fresh one goes in), the left
    /// hand's <c>socket_mag_l</c> (palm centre) is AT the magazine seat. Around each beat a two-bone solve pulls the left hand
    /// there; between the beats the clip carries the hand.
    /// <para>
    /// The beat times are known when the reload starts:
    /// <list type="bullet">
    /// <item>the reload clip's own mag_detach / mag_out and mag_insert / mag_in events (read once from the clip; the clip plays over
    /// the gameplay duration);</item>
    /// <item>the ownership machine's deadline marks (<see cref="WeaponMagazineOwnership.MagazineMarks"/> plus the grace);</item>
    /// <item>whichever comes first — when WeaponMagazineOwnership is expected to land the beat.</item>
    /// </list>
    /// The prediction only starts the reach. The beat as it really lands (WeaponMagazineOwnership.Stepped — the TP or the FP clip,
    /// or the deadline) decides the hold: the hand waits on the seat for it, up to <see cref="MaxWaitSeconds"/>.
    /// </para>
    /// <para>
    /// The weight rises over <see cref="LeadSeconds"/> before a beat, holds for <see cref="HoldSeconds"/> after it and releases over
    /// <see cref="ReleaseSeconds"/>, smoothstepped. The seat is the weapon's own <c>part_mag</c> authored place (WeaponMagazineVisual's
    /// part), read at the reload start while the part is still in the well. Used by the third-person body (PlayerSkinnedVisual) and the first-person arms
    /// (FirstPersonArms), each with its own weapon.
    /// </para>
    /// <para>
    /// LateUpdate 945 runs after WeaponGripIk (910), which is off during a reload, and after the FP arms (940). On by default; when
    /// the AP-026 reload clips put the hand on the well themselves, <see cref="Strength"/> comes down. Zero allocation per frame (the
    /// clip's events are read once per reload).
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(945)]
    public sealed class MagazineContactIk : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "WP-083 B2e: magazine contact on the reload beats — on (C-00 05.10, H3); tests / A-B switch it off.", true)]
        public static bool Enabled = true;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2e: the contact's peak weight (1 = the palm on the seat). Lowered once AP-026's reload clips carry the contact.", true)]
        public static float Strength = 1f;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2e: lead-in before a beat, hold after it, release (s) — the ±0.15 s window (C-00 05.10). Invented.", true)]
        public const float LeadSeconds = 0.10f, HoldSeconds = 0.05f, ReleaseSeconds = 0.10f;

        /// <summary>A left-hand miss larger than this (m) after its own solve makes the right arm bring the weapon closer.</summary>
        public const float ReachSlack = 0.005f;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2e: how long the hand waits on the seat past the predicted beat for the real one (s). Invented.", true)]
        public const float MaxWaitSeconds = 0.4f;

        private Transform _upper, _lower, _hand, _palm, _rUpper, _rLower, _rHand;
        private Func<Transform> _magazinePart;
        private Func<bool> _gate;
        private Transform _seatParent;
        private Vector3 _seatLocal;
        private bool _reloading;
        private float _start, _detachAt, _insertAt, _detachSeen, _insertSeen;
        private WeaponMagazineOwnership _owner;

        /// <summary>The contact weight this frame (0 between beats / off).</summary>
        public float Weight { get; private set; }
        /// <summary>The reload's predicted Detach / Insert beat times, seconds after its start (tests).</summary>
        public float DetachAt => _detachAt;
        public float InsertAt => _insertAt;
        /// <summary>palm (socket_mag_l) ↔ seat after the last solve at full weight (m; NaN otherwise; tests).</summary>
        public float LastError { get; private set; } = float.NaN;

        public static void ResetStatics()
        {
            Enabled = true;
            Strength = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => ResetStatics();

        /// <summary>
        /// Owner binding: the animator (left arm), its socket_mag_l, the held weapon's magazine part (WeaponMagazineVisual.Part — the
        /// same model instance whether the view is third or first person) and the gate (alive, this body shown).
        /// </summary>
        public void Configure(Animator animator, Transform palmSocket, Func<Transform> magazinePart, Func<bool> gate)
        {
            _upper = HumanoidBones.Bone(animator, HumanBodyBones.LeftUpperArm);
            _lower = HumanoidBones.Bone(animator, HumanBodyBones.LeftLowerArm);
            _hand = HumanoidBones.Bone(animator, HumanBodyBones.LeftHand);
            _rUpper = HumanoidBones.Bone(animator, HumanBodyBones.RightUpperArm);
            _rLower = HumanoidBones.Bone(animator, HumanBodyBones.RightLowerArm);
            _rHand = HumanoidBones.Bone(animator, HumanBodyBones.RightHand);
            _palm = palmSocket;
            _magazinePart = magazinePart;
            _gate = gate;
            CancelReload();
        }

        /// <summary>A reload starts now: its gameplay duration and the clip that plays it (null = the deadline marks alone).</summary>
        public void BeginReload(float seconds, AnimationClip clip)
        {
            CancelReload();
            if (seconds <= 0.05f) return;
            _start = Time.time;
            float grace = WeaponMagazineOwnership.DeadlineGraceSeconds;
            _detachAt = WeaponMagazineOwnership.MagazineMarks[(int)MagazineStep.Detach] * seconds + grace;
            _insertAt = WeaponMagazineOwnership.MagazineMarks[(int)MagazineStep.Insert] * seconds + grace;
            if (clip != null && clip.length > 0.01f)
            {
                foreach (AnimationEvent e in clip.events)
                {
                    if (!WeaponMagazineOwnership.TryStepFor(CharacterAnimationEvents.Parse(e.functionName), out MagazineStep step)) continue;
                    float at = e.time / clip.length * seconds;
                    if (step == MagazineStep.Detach) _detachAt = Mathf.Min(_detachAt, at);
                    else if (step == MagazineStep.Insert) _insertAt = Mathf.Min(_insertAt, at);
                }
            }

            _detachSeen = _insertSeen = float.NaN;
            // the beats as they land (clip event or deadline — the predicted time only starts the reach)
            DD.Prototype.Weapons.WeaponController c = WeaponRegistry.ActiveController;
            _owner = c != null ? c.Magazine : null;
            if (_owner != null) _owner.Stepped += OnStep;
            _reloading = true;
            ResolveSeat();
        }

        private void OnStep(MagazineStep step, bool fromEvent)
        {
            if (!_reloading) return;
            float t = Time.time - _start;
            if (step == MagazineStep.Detach) _detachSeen = t;
            else if (step == MagazineStep.Insert) _insertSeen = t;
        }

        /// <summary>
        /// One beat's weight: the reach starts <see cref="LeadSeconds"/> before the predicted time; the hand then stays on the seat
        /// until the beat really lands (at most <see cref="MaxWaitSeconds"/> past the prediction) and for <see cref="HoldSeconds"/> after.
        /// </summary>
        private static float BeatWeight(float t, float predicted, float seen)
        {
            if (!float.IsNaN(seen))
            {
                if (t >= seen) return Window(t - seen);
                return t >= predicted ? 1f : Mathf.Max(Window(t - predicted), Window(t - seen));
            }

            if (t < predicted) return Window(t - predicted);
            return t - predicted <= MaxWaitSeconds ? 1f : Window(t - predicted - MaxWaitSeconds);
        }

        // The seat: the part's authored place, taken while the part is still in the weapon (the owner's magazine visual binds its part
        // with the reload, possibly after this component hears the start — so it is retried each frame until the Detach beat).
        private void ResolveSeat()
        {
            if (_seatParent != null) return;
            Transform part = _magazinePart != null ? _magazinePart() : null;
            if (part == null || part.parent == null || part.parent.name == MagazineSocket.Name) return;
            _seatParent = part.parent;
            _seatLocal = part.localPosition;
        }

        /// <summary>The reload ended early (interrupted / switched / died): the hand goes back to the clip.</summary>
        public void CancelReload()
        {
            if (_owner != null) _owner.Stepped -= OnStep;
            _owner = null;
            _reloading = false;
            _seatParent = null;
            Weight = 0f;
            LastError = float.NaN;
        }

        private void LateUpdate()
        {
            Weight = 0f;
            if (!_reloading || !Enabled || _palm == null || _upper == null || _lower == null || _hand == null) return;
            float t = Time.time - _start;
            if (_seatParent == null && t < _detachAt) ResolveSeat();
            if (_seatParent == null) return;   // no magazine part (a tube / rocket weapon): no well to touch
            if (_gate != null && !_gate()) return;
            float insertEnd = (float.IsNaN(_insertSeen) ? _insertAt + MaxWaitSeconds : _insertSeen) + HoldSeconds + ReleaseSeconds;
            if (t > insertEnd) { CancelReload(); return; }
            float w = Mathf.Max(BeatWeight(t, _detachAt, _detachSeen), BeatWeight(t, _insertAt, _insertSeen)) * Mathf.Clamp01(Strength);
            Weight = w;
            if (w <= 0f) { LastError = float.NaN; return; }
            Vector3 seat = _seatParent.TransformPoint(_seatLocal);
            Vector3 handGoal = seat - (_palm.position - _hand.position);   // the palm (socket) on the seat, the hand's own rotation kept
            WeaponGripIk.Solve(_upper, _lower, _hand, Vector3.Lerp(_hand.position, handGoal, w));
            // out of the left arm's reach (a long magazine well, sr_1): the hands meet — the right arm brings the weapon (it rides the
            // right hand) the rest of the way, then the left hand closes on the moved seat
            Vector3 residual = (seat - _palm.position) * w;
            if (residual.sqrMagnitude > ReachSlack * ReachSlack && _rUpper != null && _rLower != null && _rHand != null && _seatParent.IsChildOf(_rHand))
            {
                Quaternion rRot = _rHand.rotation;
                WeaponGripIk.Solve(_rUpper, _rLower, _rHand, _rHand.position - residual);
                _rHand.rotation = rRot;
                seat = _seatParent.TransformPoint(_seatLocal);
                handGoal = seat - (_palm.position - _hand.position);
                WeaponGripIk.Solve(_upper, _lower, _hand, Vector3.Lerp(_hand.position, handGoal, w));
            }

            LastError = w >= 1f ? Vector3.Distance(_palm.position, seat) : float.NaN;
        }

        /// <summary>The contact weight at <paramref name="dt"/> seconds from a beat (smoothstep in, hold, smoothstep out).</summary>
        public static float Window(float dt)
        {
            if (dt < -LeadSeconds) return 0f;
            if (dt < 0f) return Mathf.SmoothStep(0f, 1f, (dt + LeadSeconds) / LeadSeconds);
            if (dt <= HoldSeconds) return 1f;
            if (dt < HoldSeconds + ReleaseSeconds) return Mathf.SmoothStep(1f, 0f, (dt - HoldSeconds) / ReleaseSeconds);
            return 0f;
        }
    }
}
