using DD.Prototype.Core.Pooling;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Base for every short-lived visual this prototype throws into the world.
    ///
    /// Two rules the whole feedback layer depends on:
    ///
    ///   * Ageing uses <c>Time.deltaTime</c>, not unscaled time. That is what makes
    ///     pause freeze the effects along with the simulation, and it is also what lets
    ///     a hit-stop hold a spray of blood in the air for its 45 ms instead of the
    ///     spray finishing while the world stands still.
    ///   * Nothing here ever calls Despawn on another object. The pool contract forbids
    ///     it and a cascade in a crowd is exactly how a frame gets lost.
    /// </summary>
    public abstract class PooledEffect : PooledBehaviour
    {
        /// <summary>Seconds since this instance was checked out.</summary>
        protected float Age { get; private set; }

        /// <summary>Seconds before the instance returns itself to the pool.</summary>
        protected float Lifetime { get; set; } = 0.6f;
        private int _localTimeGroupId;
        /// <summary>WP-104 B4: spawned while the tree preview ran — ages on VfxClock's unscaled clock (a world effect keeps the scaled one).</summary>
        protected bool PreviewClock { get; private set; }
        public int LocalTimeGroupId => _localTimeGroupId;

        /// <summary>0..1 progress through the lifetime.</summary>
        protected float Progress => Lifetime > 0f ? Mathf.Clamp01(Age / Lifetime) : 1f;

        protected override void OnSpawned()
        {
            Age = 0f;
            _localTimeGroupId = HitStopService.CaptureSpawnGroup();
            PreviewClock = DD.Prototype.Feedback.Vfx.VfxClock.PreviewActive;   // WP-104 B4
        }

        /// <summary>
        /// Put the instance back at the start of its life without going through the
        /// pool. Only used by effects that merge repeat events into one visual.
        /// </summary>
        protected void RestartLife()
        {
            Age = 0f;
        }

        protected virtual void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            Age += HitStopService.VisualDelta(_localTimeGroupId, DD.Prototype.Feedback.Vfx.VfxClock.DeltaFor(PreviewClock), Time.unscaledDeltaTime);   // WP-104 B4

            Tick(Progress);

            if (Age >= Lifetime)
            {
                Despawn();
            }
        }

        /// <summary>Animate here. <paramref name="t"/> runs 0 to 1 across the lifetime.</summary>
        protected virtual void Tick(float t)
        {
        }

        protected override void OnDespawned()
        {
            _localTimeGroupId = 0;
        }
    }
}
