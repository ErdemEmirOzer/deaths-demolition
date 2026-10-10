using UnityEngine;

namespace DD.Prototype.Characters
{
    public sealed partial class ZombieSkinnedVisual
    {
        private readonly CharacterAnimationSet.Entry[] _transitionEntries = new CharacterAnimationSet.Entry[10];
        private static readonly int TransitionRateId = Animator.StringToHash(ZombieTransitionData.RateParameter);
        private static readonly int[] TransitionStates = MakeTransitionHashes(false), TransitionRightStates = MakeTransitionHashes(true);
        private CharacterAnimationSet.Entry _transitionEntry;
        private int _transitionId = -1;
        private bool _transitionMirror, _intentMoving;
        private float _transitionFrame, _transitionRate = 1f, _transitionScale = 1f, _transitionYaw, _transitionCooldown, _attachRemaining;
        private Quaternion _transitionBasis;
        private int _savedGait;
        public string TransitionClip => _transitionEntry != null ? _transitionEntry.name : null;
        public bool TransitionActive => _transitionEntry != null;
        public bool TransitionMirror => _transitionMirror;
        public float TransitionYawDegrees => _transitionYaw;
        public int TransitionStarts { get; private set; }
        public int TransitionCompletions { get; private set; }
        public float LastAttachOutPhase { get; private set; }

        private static int[] MakeTransitionHashes(bool right)
        {
            var result = new int[ZombieTransitionData.Clips.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Animator.StringToHash(ZombieTransitionData.Clips[i] + (right ? "_R" : ""));
            return result;
        }
        private void SetExplicitLiftContract(CharacterAnimationSet.Entry entry)
        {
            if (_footIk != null) _footIk.ZombieExplicitLiftEvents = entry != null && entry.events != null
                && entry.events.IndexOf("foot_l_off=", System.StringComparison.Ordinal) >= 0
                && entry.events.IndexOf("foot_r_off=", System.StringComparison.Ordinal) >= 0;
        }
        private void ResetTransitions()
        {
            CancelTransition(false);
            for (int i = 0; i < _transitionEntries.Length; i++) _transitionEntries[i] = _animation.Find(ZombieTransitionData.Clips[i]);
            _intentMoving = false; _transitionCooldown = 0f; _attachRemaining = 0f;
            TransitionStarts = 0; TransitionCompletions = 0; LastAttachOutPhase = 0f;
        }
        private bool BeginTransition(int id, bool mirror, float speed, Quaternion basis)
        {
            CharacterAnimationSet.Entry e = _transitionEntries[id];
            if (!_authored || _dying || _rising || e == null || e.transition == null || !e.transition.Valid(e.frames)) return false;
            int hash = mirror ? TransitionRightStates[id] : TransitionStates[id];
            if (!_animator.HasState(0, hash)) return false;
            CancelTransition(false);
            _transitionEntry = e; _transitionId = id; _transitionFrame = 0f; _transitionYaw = 0f;
            _transitionMirror = mirror; _transitionBasis = basis; _savedGait = _gait;
            float native = Mathf.Max(e.transition.entrySpeed, e.transition.exitSpeed);
            float body = Mathf.Clamp(_animator.transform.lossyScale.y, MinGaitBodyScale, MaxGaitBodyScale);
            _transitionRate = Mathf.Clamp(speed / Mathf.Max(0.1f, native * body), 0.6f, 1.25f);
            _transitionScale = speed / Mathf.Max(0.1f, native * _transitionRate);
            _animator.applyRootMotion = false;
            _animator.SetFloat(TransitionRateId, _transitionRate);
            _animator.CrossFadeInFixedTime(hash, e.transition.blendInEnd / (30f * _transitionRate), 0, 0f);
            if (_events != null) _events.ZombieTransitionMirrorState = mirror ? hash : 0;
            SetExplicitLiftContract(e);
            TransitionStarts++;
            return true;
        }
        /// <summary>Motor intent before acceleration. Returns world velocity and yaw step; controller collision stays authoritative.</summary>
        public bool StepTransition(Vector3 heading, float requestedSpeed, float realSpeed, bool allowed, float dt, out Vector3 velocity, out float yawStep)
        {
            velocity = Vector3.zero; yawStep = 0f;
            if (!_authored || dt <= 0f) return false;
            _transitionCooldown = Mathf.Max(0f, _transitionCooldown - dt);
            if (!allowed || _dying || _rising || IsHeavyStaggering || _special != ZombieSpecialVisual.None)
            {
                CancelTransition(!_dying && !_rising && !IsHeavyStaggering); _intentMoving = requestedSpeed > 0.15f; return false;
            }
            if (_attachRemaining > 0f)
            {
                _attachRemaining -= dt;
                if (_attachRemaining <= 0f) RestoreTransitionGait();
            }
            bool moving = requestedSpeed > 0.15f && heading.sqrMagnitude > 0.0001f;
            if (_transitionEntry != null && ((_transitionId <= 1 && !moving) || (_transitionId >= 2 && _transitionId <= 5 && moving)))
                CancelTransition(true);
            if (_transitionEntry == null && _transitionCooldown <= 0f && !_waiting)
            {
                bool run = _topSpeed >= 3.2f;
                float angle = moving ? Vector3.SignedAngle(transform.forward, heading, Vector3.up) : 0f;
                if (moving && Mathf.Abs(angle) >= (run ? 35f : 70f) && realSpeed > 0.15f)
                    BeginTransition(Mathf.Abs(angle) >= 150f ? 7 : Mathf.Abs(angle) >= 70f ? 6 : 8, angle > 0f, requestedSpeed, transform.rotation);
                else if (moving && !_intentMoving && realSpeed < 0.35f)
                    BeginTransition(run ? 1 : 0, false, requestedSpeed, transform.rotation);
                else if (!moving && _intentMoving && realSpeed > 0.15f)
                {
                    float phase = Mathf.Repeat(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
                    int first = run ? 4 : 2;
                    var l = _transitionEntries[first]; var r = _transitionEntries[first + 1];
                    int side = l != null && l.transition != null && r != null && r.transition != null
                        && PhaseDistance(phase, r.transition.attachInPhase) < PhaseDistance(phase, l.transition.attachInPhase) ? 1 : 0;
                    BeginTransition(first + side, false, realSpeed, transform.rotation);
                }
            }
            _intentMoving = moving;
            if (_transitionEntry == null) return false;
            CharacterAnimationSet.Entry entry = _transitionEntry; ZombieTransitionData data = entry.transition;
            float from = _transitionFrame, to = Mathf.Min(entry.frames - 1, from + dt * 30f * _transitionRate);
            Vector2 travel = data.Travel(from, to) * _transitionScale;
            if (_transitionMirror) travel.x = -travel.x;
            velocity = _transitionBasis * new Vector3(travel.x, 0f, travel.y) / dt;
            float targetYaw = ZombieTransitionData.NominalYaw(entry.name) * (_transitionMirror ? 1f : -1f);
            yawStep = targetYaw * (data.TurnProgress(to) - data.TurnProgress(from));
            _transitionYaw += yawStep; _transitionFrame = to;
            if (to >= entry.frames - 1) { TransitionCompletions++; CompleteTransition(); }
            return true;
        }
        private static float PhaseDistance(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a * 360f, b * 360f));
        private void CompleteTransition()
        {
            var data = _transitionEntry.transition;
            LastAttachOutPhase = data.attachOutPhase;
            int attach = _savedGait;
            for (int i = 0; i < CharacterAnimatorContract.ZombieGaitClips.Length; i++)
                if (CharacterAnimatorContract.ZombieGaitClips[i] == data.attachOutClip) { attach = i; break; }
            _animator.SetFloat(GaitId, attach);
            bool idle = data.exitSpeed <= 0.01f;
            _speed01 = idle ? 0f : 1f;
            _animator.SetFloat(Speed01Id, _speed01);
            string attachName = CharacterAnimatorContract.ZombieGaitClips[attach];
            _gaitClipSpeed = Mathf.Max(0.1f, _animation.SpeedOf(attachName));
            _rateMin = _animation.MinRateOf(attachName); _rateMax = _animation.MaxRateOf(attachName);
            if (_events != null) { _events.MirrorLocomotionFeet = CharacterAnimatorContract.IsGaitMirrored(attach); }
            SetExplicitLiftContract(_animation.Find(CharacterAnimatorContract.ZombieGaitClips[attach]));
            float blend = (_transitionEntry.frames - 1 - data.blendOutStart) / (30f * _transitionRate);
            _animator.CrossFadeInFixedTime(CharacterAnimatorContract.LocomotionState, blend, 0,
                data.attachOutPhase * Mathf.Max(0.01f, _animation.Find(CharacterAnimatorContract.ZombieGaitClips[attach]).seconds));
            _transitionEntry = null; _transitionId = -1; _transitionMirror = false;
            _attachRemaining = blend; _transitionCooldown = blend + 0.25f;
            ReleaseTransitionFeet();
        }
        private void RestoreTransitionGait()
        {
            if (_events != null) _events.ZombieTransitionMirrorState = 0;
            _animator.SetFloat(GaitId, _gait);
            RefreshGaitClip();
            if (_waiting || _dying || _rising) return;
            // The authored attach already owns the Locomotion clock. A second self-crossfade would reseed that clock.
            if (_animator.GetCurrentAnimatorStateInfo(0).IsName(CharacterAnimatorContract.LocomotionState)) return;
            if (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).IsName(CharacterAnimatorContract.LocomotionState)) return;
            // Retain recovery when neither active state is Locomotion (for example an interrupted attach).
            _animator.CrossFade(CharacterAnimatorContract.LocomotionState, 0.12f, 0);
        }
        private void ReleaseTransitionFeet() { if (_footIk != null) { _footIk.NotifyLift(0); _footIk.NotifyLift(1); } }
        /// <summary>Drops the p3 start / stop / turn in progress. Returns true when one was active (its scripted crossfade may still be
        /// running on the base layer — no any-state transition interrupts a scripted crossfade).</summary>
        private bool CancelTransition(bool returnToLocomotion)
        {
            bool active = _transitionEntry != null || _attachRemaining > 0f;
            _transitionEntry = null; _transitionId = -1; _transitionMirror = false; _attachRemaining = 0f;
            if (_events != null) _events.ZombieTransitionMirrorState = 0;
            if (!active) return false;
            ReleaseTransitionFeet(); RefreshGaitClip();
            _animator.SetFloat(GaitId, _gait);
            _transitionCooldown = 0.25f;
            if (returnToLocomotion) _animator.CrossFade(CharacterAnimatorContract.LocomotionState, 0.1f, 0);
            return true;
        }
    }
}
