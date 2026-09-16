using System;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>The AP-009 animation events (ADR-018-1). Names = the manifest's event names.</summary>
    public enum CharacterAnimationEvent
    {
        None = 0,
        Fire = 1,
        HitWindowStart = 2,
        HitWindowEnd = 3,
        FootLeft = 4,
        FootRight = 5,
        DeathEnd = 6,
        RiseEnd = 7,
        Pickup = 8,
        Breath = 9,
        Hit = 10,
        StaggerEnd = 11
    }

    /// <summary>
    /// WP-018 · ADR-018-1 · THE ANIMATION EVENT CONTRACT. One component on the same GameObject
    /// as the character's Animator (AnimationEvents can only call methods there); the AP-009
    /// clips carry <c>AnimationEvent</c>s named after the manifest (written at import by
    /// <c>ArtAnimationClipContract</c>) and Unity calls the same-named methods below.
    ///
    /// Consumers: the owner (ZombieSkinnedVisual / PlayerSkinnedVisual) subscribes to
    /// <see cref="Raised"/> for gameplay (DEC-029 hit window, death end); S-07 effects and
    /// S-08 audio subscribe to the static <see cref="Any"/> (footsteps, fire, breath) — read,
    /// never re-published. Zero allocation per event; the payload is the component (whose
    /// transform is the character) and the enum.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimationEvents : MonoBehaviour
    {
        /// <summary>The manifest event names, index = (int)CharacterAnimationEvent.</summary>
        public static readonly string[] Names =
        {
            "", "fire", "hit_window_start", "hit_window_end", "foot_l", "foot_r",
            "death_end", "rise_end", "pickup", "breath", "hit", "stagger_end"
        };

        public static CharacterAnimationEvent Parse(string name)
        {
            for (int i = 1; i < Names.Length; i++)
            {
                if (Names[i] == name) return (CharacterAnimationEvent)i;
            }

            return CharacterAnimationEvent.None;
        }

        /// <summary>Per-character subscribers (owner).</summary>
        public event Action<CharacterAnimationEvent> Raised;

        /// <summary>Project-wide subscribers (effects, audio).</summary>
        public static event Action<CharacterAnimationEvents, CharacterAnimationEvent> Any;

        /// <summary>Total events raised since the last reset (tests / probes).</summary>
        public static int RaisedCount { get; private set; }

        /// <summary>Last event raised on this component (tests).</summary>
        public CharacterAnimationEvent Last { get; private set; }

        /// <summary>Time.time of the last event (tests).</summary>
        public float LastTime { get; private set; }

        public static void ResetStatics()
        {
            Any = null;
            RaisedCount = 0;
        }

        // ------------------------------------------ AnimationEvent targets (manifest names)
        public void fire() => Raise(CharacterAnimationEvent.Fire);
        public void hit_window_start() => Raise(CharacterAnimationEvent.HitWindowStart);
        public void hit_window_end() => Raise(CharacterAnimationEvent.HitWindowEnd);
        public void foot_l() => Raise(CharacterAnimationEvent.FootLeft);
        public void foot_r() => Raise(CharacterAnimationEvent.FootRight);
        public void death_end() => Raise(CharacterAnimationEvent.DeathEnd);
        public void rise_end() => Raise(CharacterAnimationEvent.RiseEnd);
        public void pickup() => Raise(CharacterAnimationEvent.Pickup);
        public void breath() => Raise(CharacterAnimationEvent.Breath);
        public void hit() => Raise(CharacterAnimationEvent.Hit);
        public void stagger_end() => Raise(CharacterAnimationEvent.StaggerEnd);

        /// <summary>Tests / probes: raise an event as if the clip had fired it.</summary>
        public void RaiseForTest(CharacterAnimationEvent e) => Raise(e);

        private void Raise(CharacterAnimationEvent e)
        {
            Last = e;
            LastTime = Time.time;
            RaisedCount++;
            Raised?.Invoke(e);
            Any?.Invoke(this, e);
        }
    }
}
