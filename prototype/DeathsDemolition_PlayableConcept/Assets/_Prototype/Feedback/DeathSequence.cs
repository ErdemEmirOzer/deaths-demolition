using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// WP-059 · the player's death beat on the clock: the world eases into
    /// <see cref="GameFeelSettings.DeathSlowScale"/> over the in-ramp, holds while the
    /// collapse clip plays, eases back to 1 before the results hand-off. Unscaled time
    /// throughout, so the beat itself cannot stretch. The knobs live in
    /// <see cref="Vfx.GameFeelSettings"/> (the one game-feel source); <see cref="PushIn01"/>
    /// is what the camera rig reads for its push-in.
    ///
    /// Clock ownership follows <see cref="HitStopService"/>: a request is refused while the
    /// game is stopped, a running hit-stop is ended first, the scale is only given back
    /// when it is still ours, and <see cref="Rounds.RunFlowClock"/> cancels the beat before
    /// it freezes (a pause or the results hold must never remember 0.3 as "running").
    /// </summary>
    public static class DeathSequence
    {
        private static float _scale = 1f;
        private static float _inSeconds;
        private static float _holdSeconds;
        private static float _outSeconds;
        private static float _elapsed;
        private static float _applied = 1f;
        private static float _restoreScale = 1f;

        public static bool IsActive { get; private set; }

        /// <summary>Beats started since the counter was reset (tests / F1).</summary>
        public static int Count { get; private set; }

        /// <summary>Unscaled seconds since the beat began; 0 when idle.</summary>
        public static float Elapsed => IsActive ? _elapsed : 0f;

        /// <summary>
        /// 0..1 how far the camera push-in has come: eases in with the slow-down and HOLDS after the beat
        /// ends on its own (the body stays framed until the results scene replaces the camera; a snap
        /// back would read as a pop). Dropped by <see cref="Cancel"/>, <see cref="ReleasePushIn"/> and
        /// a new camera rig (PrototypeCameraRig.OnEnable).
        /// </summary>
        public static float PushIn01 { get; private set; }

        /// <summary>Let the camera go (a new run's rig, tests).</summary>
        public static void ReleasePushIn()
        {
            if (!IsActive)
            {
                PushIn01 = 0f;
            }
        }

        /// <summary>Total unscaled length of the beat (in + hold + out).</summary>
        public static float TotalSeconds => _inSeconds + _holdSeconds + _outSeconds;

        /// <summary>
        /// Start the beat. <paramref name="scale"/> is the held time scale, the ramps are
        /// unscaled seconds. Refused (returns false) while the clock is already stopped or a
        /// beat is running.
        /// </summary>
        public static bool Begin(float scale, float inSeconds, float holdSeconds, float outSeconds)
        {
            if (IsActive)
            {
                return false;
            }

            if (Time.timeScale <= 0.01f)
            {
                return false;
            }

            HitStopService.Cancel();

            _scale = Mathf.Clamp(scale, Vfx.GameFeelSettings.MinDeathSlowScale, 1f);
            _inSeconds = Mathf.Max(0f, inSeconds);
            _holdSeconds = Mathf.Max(0f, holdSeconds);
            _outSeconds = Mathf.Max(0f, outSeconds);
            _elapsed = 0f;
            _restoreScale = Time.timeScale;
            IsActive = true;
            Count++;
            PushIn01 = 0f;
            Apply(ScaleAt(0f));
            DeathSequenceRunner.Ensure();
            return true;
        }

        /// <summary>Advance with unscaled time. Called by the runner; tests may drive it directly.</summary>
        public static void Tick(float unscaledDeltaTime)
        {
            if (!IsActive)
            {
                return;
            }

            // Somebody else took the clock (pause / results hold / a scene change): the beat is over, the scale is theirs.
            if (Mathf.Abs(Time.timeScale - _applied) > 0.0001f)
            {
                IsActive = false;
                return;
            }

            _elapsed += Mathf.Max(0f, unscaledDeltaTime);
            if (_elapsed >= TotalSeconds)
            {
                Finish();
                return;
            }

            Apply(ScaleAt(_elapsed));
        }

        /// <summary>End now and give the clock back (if it is still ours); the push-in goes with it.</summary>
        public static void Cancel()
        {
            if (!IsActive)
            {
                return;
            }

            Finish();
            PushIn01 = 0f;
        }

        public static void ResetCounters()
        {
            Count = 0;
        }

        /// <summary>The time scale the beat wants at <paramref name="t"/> unscaled seconds (pure; tests).</summary>
        public static float ScaleAt(float t)
        {
            if (t < _inSeconds)
            {
                return Mathf.Lerp(_restoreScale, _scale, _inSeconds > 0f ? Mathf.SmoothStep(0f, 1f, t / _inSeconds) : 1f);
            }

            if (t < _inSeconds + _holdSeconds)
            {
                return _scale;
            }

            float outT = _outSeconds > 0f ? (t - _inSeconds - _holdSeconds) / _outSeconds : 1f;
            return Mathf.Lerp(_scale, _restoreScale, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outT)));
        }

        private static void Apply(float scale)
        {
            _applied = scale;
            Time.timeScale = scale;
            float inT = _inSeconds > 0f ? Mathf.Clamp01(_elapsed / _inSeconds) : 1f;
            PushIn01 = Mathf.SmoothStep(0f, 1f, inT);
        }

        private static void Finish()
        {
            IsActive = false;
            _elapsed = 0f;
            if (Mathf.Abs(Time.timeScale - _applied) < 0.0001f)
            {
                Time.timeScale = _restoreScale > 0.01f ? _restoreScale : 1f;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsActive = false;
            PushIn01 = 0f;
            Count = 0;
            _elapsed = 0f;
            _applied = 1f;
            _restoreScale = 1f;
        }
    }

    /// <summary>Ticks <see cref="DeathSequence"/> with unscaled time; one hidden persistent object, created on the first beat.</summary>
    public sealed class DeathSequenceRunner : MonoBehaviour
    {
        private static DeathSequenceRunner s_instance;

        internal static void Ensure()
        {
            if (s_instance != null || !Application.isPlaying)
            {
                return;   // EditMode tests tick the sequence themselves
            }

            var go = new GameObject("[Death Sequence]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<DeathSequenceRunner>();
        }

        private void Update()
        {
            DeathSequence.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
                DeathSequence.Cancel();
            }
        }
    }
}
