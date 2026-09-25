using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 · the AP-022 attack clips' own events (S-06 WP-071 d9bc62ff; the clips play in place, S-03 moves the body):
    /// <c>attack_step</c> steps the body forward by the style's root step (Swipe 0.35 m, Lunge 1.4 m — Standard ×0.8 —,
    /// Sweep 0.25, Overhead 0.5, BiteGrab 0.25) with an ease-out over <see cref="StepSeconds"/>, through the
    /// CharacterController like every other displacement, and replaces that swing's timer dash; <c>grab_end</c> ends the
    /// hold (the second bite share). A body without the events (block rig, older clips) keeps the timers.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076: seconds over which a clip's attack_step root step is applied (ease-out).", true)]
        public const float StepSeconds = 0.25f;

        public static int ClipSteps { get; private set; }
        public static int ClipGrabEnds { get; private set; }

        private CharacterAnimationEvents _boundEvents;
        private KnockbackProfile _stepProfile;
        private Vector3 _stepDir;
        private float _stepTime = -1f;

        /// <summary>Subscribes once per body (lazily: the driven visual finds its events component after spawn).</summary>
        private void BindClipEvents()
        {
            if (_boundEvents != null || _skinned == null) return;
            CharacterAnimationEvents ev = _skinned.Events;
            if (ev == null) return;
            ev.Raised += OnClipEvent;
            _boundEvents = ev;
        }

        private void UnbindClipEvents()
        {
            if (_boundEvents != null) _boundEvents.Raised -= OnClipEvent;
            _boundEvents = null;
        }

        private void OnClipEvent(CharacterAnimationEvent e)
        {
            if (!_alive || _dying || _attack == null) return;
            switch (e)
            {
                case CharacterAnimationEvent.AttackStep:
                    float metres = _attack.TakeClipStep(_tier);
                    if (metres > 0f)
                    {
                        _stepProfile = new KnockbackProfile(metres, StepSeconds);
                        _stepDir = FlatForward(transform);
                        _stepTime = 0f;
                        ClipSteps++;
                    }
                    break;
                case CharacterAnimationEvent.GrabEnd:
                    if (_attack.IsGrabbing) { _attack.OnClipGrabEnd(); ClipGrabEnds++; }
                    break;
            }
        }

        /// <summary>The step's velocity this frame (zero when no step runs or the swing was cut).</summary>
        private Vector3 TickClipStep(float dt)
        {
            if (_stepTime < 0f) return Vector3.zero;
            if (!_attack.IsAttacking || _stepTime >= _stepProfile.Seconds) { _stepTime = -1f; return Vector3.zero; }
            Vector3 v = _stepDir * _stepProfile.SpeedAt(_stepTime);
            _stepTime += dt;
            return v;
        }

        private void ResetClipStep() => _stepTime = -1f;

        public static void ResetClipEventStatics() { ClipSteps = 0; ClipGrabEnds = 0; }
    }
}
