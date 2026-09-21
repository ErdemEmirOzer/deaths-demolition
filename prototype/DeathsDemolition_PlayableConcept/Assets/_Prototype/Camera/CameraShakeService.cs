using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// Motion profile of a shake. Amplitude and duration say how big and how long;
    /// the kind says what shape the motion has.
    /// </summary>
    public enum ShakeKind
    {
        /// <summary>High frequency, non directional. Automatic fire, small impacts.</summary>
        Rattle = 0,

        /// <summary>One directional kick that decays fast. Shotgun, heavy single shot.</summary>
        Punch = 1,

        /// <summary>Low frequency swell that rises and falls. Explosion, Ultra footfall.</summary>
        Rumble = 2,

        /// <summary>Soft short displacement. The player taking damage.</summary>
        Bump = 3
    }

    /// <summary>
    /// The single entry point for camera shake. Weapons, explosions and damage
    /// feedback all call this; nothing else is allowed to move the camera.
    ///
    ///     CameraShakeService.Shake(0.06f, 0.08f, ShakeKind.Rattle);
    ///     CameraShakeService.Shake(0.16f, 0.22f, ShakeKind.Punch, muzzleForward);
    ///
    /// GUARANTEES THE CALLER CAN RELY ON
    /// ---------------------------------
    /// 1. AIM IS NEVER DISTURBED. Shake is applied to a child node of the camera rig.
    ///    Every aim query goes through PrototypeCameraRig.ScreenPointToAimPlane, which
    ///    rebuilds the ray from the rig's shake-free pose. A shaking camera therefore
    ///    cannot move the world point under the cursor by even a pixel.
    /// 2. AMPLITUDE IS CLAMPED. The summed offset is clamped to
    ///    PrototypeConfig.Feedback.MaxShakeAmplitude metres and scaled by
    ///    PrototypeConfig.Camera.ShakeIntensity (the pause-menu slider). A caller that
    ///    asks for something absurd gets the ceiling, not a broken screen.
    /// 3. IT IS SAFE TO CALL FROM ANYWHERE, AT ANY TIME. With no camera rig alive the
    ///    call is a no-op; nothing queues up and nothing leaks.
    /// 4. PAUSE FREEZES IT. The rig advances shakes with scaled delta time.
    ///
    /// Amplitude is expressed in metres of camera displacement before clamping.
    /// Suggested starting points (all PROTOTYPE-ONLY, tune in play):
    ///   SMG shot        0.035  / 0.06 s / Rattle
    ///   Rifle shot      0.055  / 0.07 s / Rattle
    ///   Shotgun shot    0.150  / 0.20 s / Punch
    ///   Rotary cannon   0.045  / 0.05 s / Rattle
    ///   Explosion       0.200  / 0.45 s / Rumble
    ///   Player damaged  0.090  / 0.25 s / Bump
    /// </summary>
    public static class CameraShakeService
    {
        /// <summary>Beyond this, the weakest running shake is recycled.</summary>
        public const int MaxConcurrentShakes = 12;

        private struct ActiveShake
        {
            public float Amplitude;
            public float Duration;
            public float Elapsed;
            public float Seed;
            public ShakeKind Kind;
            public Vector3 Direction;
            public bool HasDirection;
            public ShakeKernelFeel Kernel;
        }

        private static readonly ActiveShake[] s_shakes = new ActiveShake[MaxConcurrentShakes];
        private static int s_count;
        private static PrototypeCameraRig s_rig;

        /// <summary>Number of shakes currently running. Debug readout.</summary>
        public static int ActiveCount
        {
            get { return s_count; }
        }

        /// <summary>True when a camera rig is listening. Calls made while false are dropped.</summary>
        public static bool HasReceiver
        {
            get { return s_rig != null; }
        }

        /// <summary>Non-directional shake. This is the signature other systems bind to.</summary>
        public static void Shake(float amplitude, float duration, ShakeKind kind)
        {
            Shake(amplitude, duration, kind, Vector3.zero);
        }

        /// <summary>
        /// Directional shake. <paramref name="worldDirection"/> is the direction the
        /// camera should be pushed in world space, for example the muzzle forward
        /// vector for recoil or "away from the blast" for an explosion. Pass
        /// Vector3.zero for a non-directional shake.
        /// </summary>
        public static void Shake(float amplitude, float duration, ShakeKind kind, Vector3 worldDirection)
        {
            if (s_rig == null)
            {
                return;
            }

            if (amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            var shake = new ActiveShake
            {
                Amplitude = amplitude,
                Duration = duration,
                Elapsed = 0f,
                Seed = Random.Range(0f, 128f),
                Kind = kind,
                Direction = worldDirection.sqrMagnitude > 1e-6f ? worldDirection.normalized : Vector3.zero,
                HasDirection = worldDirection.sqrMagnitude > 1e-6f,
                // The snapshot is detached and replaced atomically; retaining this section
                // latches the event envelope without a hot-path clone/allocation.
                Kernel = FeelRuntime.Current.Shake.kernels
            };

            int activeLimit = Mathf.Clamp(FeelRuntime.Current.Shake.maxConcurrent, 1, MaxConcurrentShakes);
            if (s_count < activeLimit)
            {
                s_shakes[s_count++] = shake;
                return;
            }

            // Full: overwrite whichever running shake has the least left to give.
            int weakest = 0;
            float weakestScore = float.MaxValue;
            for (int i = 0; i < s_count; i++)
            {
                float remaining = Mathf.Max(0f, s_shakes[i].Duration - s_shakes[i].Elapsed);
                float score = s_shakes[i].Amplitude * remaining;
                if (score < weakestScore)
                {
                    weakestScore = score;
                    weakest = i;
                }
            }

            s_shakes[weakest] = shake;
        }

        /// <summary>Drop every running shake. Used on death, scene change and round reset.</summary>
        public static void StopAll()
        {
            s_count = 0;
        }

        /// <summary>Called by <see cref="PrototypeCameraRig"/> only.</summary>
        internal static void Register(PrototypeCameraRig rig)
        {
            s_rig = rig;
            s_count = 0;
        }

        /// <summary>Called by <see cref="PrototypeCameraRig"/> only.</summary>
        internal static void Unregister(PrototypeCameraRig rig)
        {
            if (s_rig != rig)
            {
                return;
            }

            s_rig = null;
            s_count = 0;
        }

        /// <summary>
        /// Advances every running shake and returns the summed offset expressed in the
        /// camera's own axes (x right, y up, z forward) plus a roll in degrees.
        /// Called exactly once per frame by the rig.
        /// </summary>
        internal static void Sample(
            float deltaTime,
            float maxAmplitude,
            float intensityScale,
            Quaternion frameRotation,
            out Vector3 localOffset,
            out float rollDegrees)
        {
            localOffset = Vector3.zero;
            rollDegrees = 0f;

            if (s_count == 0)
            {
                return;
            }

            Quaternion inverseFrame = Quaternion.Inverse(frameRotation);
            Vector3 sum = Vector3.zero;
            float roll = 0f;
            int write = 0;

            for (int i = 0; i < s_count; i++)
            {
                ActiveShake shake = s_shakes[i];
                shake.Elapsed += deltaTime;

                if (shake.Elapsed >= shake.Duration)
                {
                    continue;
                }

                float u = Mathf.Clamp01(shake.Elapsed / shake.Duration);
                Vector3 contribution;
                float contributionRoll;
                Evaluate(shake, u, inverseFrame, out contribution, out contributionRoll);

                sum += contribution;
                roll += contributionRoll;

                s_shakes[write++] = shake;
            }

            s_count = write;

            float ceiling = Mathf.Max(0f, maxAmplitude) * Mathf.Max(0f, intensityScale);
            localOffset = Vector3.ClampMagnitude(sum * Mathf.Max(0f, intensityScale), ceiling);

            // Roll ceiling is derived from the amplitude ceiling so one slider governs
            // the whole camera response: 1 metre of allowed shake -> 6 degrees of roll.
            float rollCeiling = ceiling * FeelRuntime.Current.Shake.rollDegreesPerMeter;
            rollDegrees = Mathf.Clamp(roll * Mathf.Max(0f, intensityScale), -rollCeiling, rollCeiling);
        }

        private static void Evaluate(
            ActiveShake shake,
            float u,
            Quaternion inverseFrame,
            out Vector3 localOffset,
            out float rollDegrees)
        {
            float amp = shake.Amplitude;
            Vector3 localDirection = shake.HasDirection
                ? inverseFrame * shake.Direction
                : Vector3.zero;

            ShakeKernelFeel kernel = shake.Kernel;
            switch (shake.Kind)
            {
                case ShakeKind.Punch:
                {
                    // One kick that overshoots slightly then settles.
                    float env = Mathf.Pow(1f - u, kernel.punchDecayPower);
                    float wobble = Mathf.Cos(u * Mathf.PI * kernel.punchCosinePiFactor);
                    Vector3 axis = shake.HasDirection
                        ? new Vector3(localDirection.x, localDirection.y, 0f)
                        : Vector3.up;

                    if (axis.sqrMagnitude < 1e-6f)
                    {
                        axis = Vector3.up;
                    }

                    localOffset = axis.normalized * (amp * env * wobble);
                    rollDegrees = amp * env * wobble * kernel.punchRollDegreesPerMeter;
                    return;
                }

                case ShakeKind.Rumble:
                {
                    // Low frequency swell, felt more than seen.
                    float env = Mathf.Sin(u * Mathf.PI);
                    float t = shake.Elapsed * kernel.rumbleFrequency + shake.Seed;
                    float nx = Mathf.PerlinNoise(t, shake.Seed) * 2f - 1f;
                    float ny = Mathf.PerlinNoise(shake.Seed, t) * 2f - 1f;
                    float nz = Mathf.PerlinNoise(t * 0.7f, t * 0.3f) * 2f - 1f;

                    Vector3 noise = new Vector3(nx, ny, nz * kernel.rumbleZShare) * (amp * env);
                    Vector3 push = localDirection * (amp * env * kernel.rumbleDirectionShare);

                    localOffset = noise + push;
                    rollDegrees = nx * amp * env * kernel.rumbleRollDegreesPerMeter;
                    return;
                }

                case ShakeKind.Bump:
                {
                    // Soft single displacement, deliberately gentle: this one plays
                    // while the player is being hit and must not obscure the fight.
                    float env = Mathf.Pow(1f - u, kernel.bumpDecayPower);
                    Vector3 axis = shake.HasDirection ? localDirection : Vector3.down;
                    axis.z *= kernel.bumpZShare;

                    localOffset = axis * (amp * env * Mathf.Cos(u * Mathf.PI * kernel.bumpCosinePiFactor));
                    rollDegrees = 0f;
                    return;
                }

                default:
                {
                    // Rattle: high frequency noise, no preferred direction.
                    float env = 1f - u;
                    float t = shake.Elapsed * kernel.rattleFrequency + shake.Seed;
                    float nx = Mathf.PerlinNoise(t, shake.Seed * 1.7f) * 2f - 1f;
                    float ny = Mathf.PerlinNoise(shake.Seed * 2.3f, t) * 2f - 1f;

                    localOffset = new Vector3(nx, ny, 0f) * (amp * env);
                    rollDegrees = 0f;
                    return;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_count = 0;
            s_rig = null;
        }
    }
}
