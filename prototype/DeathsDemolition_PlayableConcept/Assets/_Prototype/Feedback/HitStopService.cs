using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// REQ-127: combat hit-stop is a local VFX time envelope. It never owns Time.timeScale.
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
        private static float _presentationScale = StopTimeScale;
        private static float _lastEndUnscaledTime = -999f;
        private static int _groupId;
        private static int _spawnGroupId;

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
        public static float BaseTimeScale => Time.timeScale;
        public static int ActiveGroupId => IsActive ? _groupId : 0;
        public static float RemainingRealSeconds => Mathf.Max(0f, _remaining);

        /// <summary>
        /// Ask for a hit stop. Silently refused when one is already running, when the
        /// game is paused, or when the previous one ended less than
        /// <paramref name="minInterval"/> seconds ago.
        /// </summary>
        public static void Request(float duration, float minInterval)
        {
            RequestForGroup(0, duration, minInterval, StopTimeScale);
        }

        public static bool RequestForGroup(int groupId, float duration, float minInterval, float presentationScale)
        {
            if (duration <= 0f)
            {
                return false;
            }

            if (IsActive)
            {
                // Extend rather than restart, so two heavy hits in the same frame do not
                // double the freeze.
                if (_groupId == groupId) _remaining = Mathf.Min(0.04f, Mathf.Max(_remaining, duration));
                else Refused++;
                return _groupId == groupId;
            }

            if (Time.timeScale <= 0.01f)
            {
                // Paused, or someone else already owns the clock.
                Refused++;
                return false;
            }

            if (Time.unscaledTime - _lastEndUnscaledTime < minInterval)
            {
                Refused++;
                return false;
            }

            _groupId = groupId;
            _presentationScale = Mathf.Clamp(presentationScale, StopTimeScale, 1f);
            _remaining = Mathf.Min(0.04f, duration);
            IsActive = true;
            Count++;
            return true;
        }

        public static void BeginSpawnGroup(int groupId) { _spawnGroupId = groupId; }
        public static void EndSpawnGroup() { _spawnGroupId = 0; }
        public static int CaptureSpawnGroup() => _spawnGroupId;

        /// <summary>Exact overlap prevents a 35 ms token becoming two 30 fps frames.</summary>
        public static float VisualDelta(int groupId, float scaledDelta, float unscaledDelta)
        {
            if (!IsActive || groupId == 0 || groupId != _groupId || unscaledDelta <= 0f) return scaledDelta;
            float overlap = Mathf.Min(unscaledDelta, Mathf.Max(0f, _remaining));
            return overlap * _presentationScale + Mathf.Max(0f, unscaledDelta - overlap);
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

#if UNITY_EDITOR
        public static void ResetForTests()
        {
            ResetStatics();
        }
#endif

        private static void Finish()
        {
            IsActive = false;
            _remaining = 0f;
            _lastEndUnscaledTime = Time.unscaledTime;

            _groupId = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsActive = false;
            _remaining = 0f;
            _presentationScale = StopTimeScale;
            _lastEndUnscaledTime = -999f;
            _groupId = 0;
            _spawnGroupId = 0;
            Count = 0;
            Refused = 0;
        }
    }
}
