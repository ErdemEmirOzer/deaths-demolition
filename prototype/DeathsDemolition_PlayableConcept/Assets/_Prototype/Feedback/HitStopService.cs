using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// REQ-127: hit-stop is a punctuation mark, not a texture.
    ///
    /// The request is explicit that an automatic weapon must NOT hit-stop on every
    /// bullet, and only a heavy impact, an armour break or a heavy death may use a very
    /// short one. Three things enforce that here and nowhere else:
    ///
    ///   * only the feedback director calls <see cref="Request"/>, and only for tiers
    ///     ArmorBreak and above or for a Heavy-power weapon;
    ///   * <c>Feedback.HitStopMinInterval</c> rate-limits it, so a burst that breaks
    ///     three plates in 200 ms still stops the world once;
    ///   * the world is slowed, not frozen. A true zero freeze at 60 fps reads as a
    ///     dropped frame; a 6% time scale for 45 ms reads as weight.
    ///
    /// Pause safety: a request is refused while the game is already stopped, and the
    /// restore only happens if nothing else has touched <c>Time.timeScale</c> in the
    /// meantime. Pausing during a hit-stop therefore cannot un-pause the game.
    /// </summary>
    public static class HitStopService
    {
        /// <summary>PROTOTYPE-ONLY: time scale during a hit stop. 0 would read as a dropped frame.</summary>
        public const float StopTimeScale = 0.06f;

        private static float _remaining;
        private static float _appliedScale;
        private static float _restoreScale = 1f;
        private static float _lastEndUnscaledTime = -999f;

        public static bool IsActive { get; private set; }

        /// <summary>Number of hit stops played since the counter was reset. F1 readout.</summary>
        public static int Count { get; private set; }

        /// <summary>Number of requests refused by the rate limit. F1 readout - proves REQ-127 is holding.</summary>
        public static int Refused { get; private set; }

        /// <summary>
        /// The time scale that will be restored when the current hit stop ends, or the
        /// live one when none is running.
        ///
        /// THE PAUSE SYSTEM MUST USE THIS, not <c>Time.timeScale</c>, when it remembers
        /// what to restore on resume. Reading the raw value during a hit stop would
        /// remember 0.06 and leave the game running at 6% speed after unpausing.
        /// Calling <see cref="Cancel"/> before pausing is the other correct answer.
        /// </summary>
        public static float BaseTimeScale => IsActive ? _restoreScale : Time.timeScale;

        /// <summary>
        /// Ask for a hit stop. Silently refused when one is already running, when the
        /// game is paused, or when the previous one ended less than
        /// <paramref name="minInterval"/> seconds ago.
        /// </summary>
        public static void Request(float duration, float minInterval)
        {
            if (duration <= 0f)
            {
                return;
            }

            if (IsActive)
            {
                // Extend rather than restart, so two heavy hits in the same frame do not
                // double the freeze.
                _remaining = Mathf.Max(_remaining, duration);
                return;
            }

            if (Time.timeScale <= 0.01f)
            {
                // Paused, or someone else already owns the clock.
                Refused++;
                return;
            }

            if (Time.unscaledTime - _lastEndUnscaledTime < minInterval)
            {
                Refused++;
                return;
            }

            _restoreScale = Time.timeScale;
            _appliedScale = StopTimeScale;
            _remaining = duration;
            IsActive = true;
            Count++;

            Time.timeScale = _appliedScale;
        }

        /// <summary>Ticked by the feedback director with unscaled time.</summary>
        public static void Tick(float unscaledDeltaTime)
        {
            if (!IsActive)
            {
                return;
            }

            _remaining -= unscaledDeltaTime;
            if (_remaining > 0f)
            {
                return;
            }

            Finish();
        }

        /// <summary>End immediately. The pause system should call this before freezing.</summary>
        public static void Cancel()
        {
            if (!IsActive)
            {
                return;
            }

            Finish();
        }

        public static void ResetCounters()
        {
            Count = 0;
            Refused = 0;
        }

        private static void Finish()
        {
            IsActive = false;
            _remaining = 0f;
            _lastEndUnscaledTime = Time.unscaledTime;

            // Only give the clock back if it is still ours. If a pause menu set the
            // scale to 0 while we were running, restoring 1 here would un-pause the game.
            if (Mathf.Abs(Time.timeScale - _appliedScale) < 0.0001f)
            {
                Time.timeScale = _restoreScale;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsActive = false;
            _remaining = 0f;
            _appliedScale = 0f;
            _restoreScale = 1f;
            _lastEndUnscaledTime = -999f;
            Count = 0;
            Refused = 0;
        }
    }
}
