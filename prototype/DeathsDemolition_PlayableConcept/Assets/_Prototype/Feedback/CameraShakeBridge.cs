using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The single place a shake request can enter the camera, and therefore the single
    /// place REQ-126 can be enforced: <b>a shake never breaks aim.</b>
    ///
    /// Every amplitude that passes through here is multiplied by the player-facing
    /// intensity setting and then hard-clamped to
    /// <c>PrototypeConfig.Feedback.MaxShakeAmplitude</c>. A caller cannot exceed the
    /// ceiling by asking louder, and turning the setting to zero silences shake
    /// completely without any caller knowing.
    ///
    /// It binds itself to <see cref="CameraShakeService"/> on load, so no scene wiring is
    /// required. <see cref="Handler"/> stays public so a test, a replay tool or a future
    /// camera can take the calls instead.
    ///
    /// The indirection earns its keep in three ways beyond decoupling: the amplitude
    /// ceiling is applied exactly once for every caller, the player-facing intensity
    /// setting has one place to live, and <see cref="ShakeProfile"/> lets weapon code say
    /// WHY it is shaking while the camera decides what that looks like.
    /// </summary>
    public static class CameraShakeBridge
    {
        /// <summary>
        /// Where clamped shake requests go. Auto-bound to the camera service; reassign to
        /// intercept. Null is not an error, it just means nothing is listening yet.
        /// </summary>
        public static Action<float, float, ShakeProfile> Handler = DefaultHandler;

        /// <summary>
        /// Where clamped DIRECTIONAL shake requests go.
        ///
        /// Kept separate from <see cref="Handler"/> so an existing three-argument
        /// interceptor keeps working untouched. A caller that supplies a direction and
        /// finds nothing bound here falls back to <see cref="Handler"/> and loses the
        /// direction, never the shake.
        ///
        /// Only shakes whose motion has a real world axis should use this: a shotgun kick
        /// (pushed against the shot), an armour break or an Elite death (pushed away from
        /// the impact). Sustained automatic fire deliberately does NOT, because a
        /// directional shake repeated fifteen times a second becomes a drift, and a drift
        /// is the one camera behaviour REQ-126 rules out.
        /// </summary>
        public static Action<float, float, ShakeProfile, Vector3> DirectionalHandler = DefaultDirectionalHandler;

        /// <summary>Maps a reason-for-shaking onto the camera's motion profile.</summary>
        public static ShakeKind ToShakeKind(ShakeProfile profile)
        {
            switch (profile)
            {
                // Sustained automatic fire: high frequency, non-directional, so a held
                // trigger never turns into a directional drift away from the cursor.
                case ShakeProfile.ClassicFire:
                case ShakeProfile.HeavyFire:
                    return ShakeKind.Rattle;

                // One heavy discrete shot.
                case ShakeProfile.ShotgunFire:
                    return ShakeKind.Punch;

                // Low swell for things that happen to the world rather than to the gun.
                case ShakeProfile.HeavyDeath:
                case ShakeProfile.Explosion:
                    return ShakeKind.Rumble;

                default:
                    return ShakeKind.Bump;
            }
        }

        private static void DefaultHandler(float amplitude, float duration, ShakeProfile profile)
        {
            CameraShakeService.Shake(amplitude, duration, ToShakeKind(profile));
        }

        private static void DefaultDirectionalHandler(
            float amplitude, float duration, ShakeProfile profile, Vector3 worldDirection)
        {
            CameraShakeService.Shake(amplitude, duration, ToShakeKind(profile), worldDirection);
        }

        private static float s_intensityScale = 1f;
        private static bool s_missingHandlerLogged;

        /// <summary>
        /// PROTOTYPE-ONLY pause-menu "Camera Shake Intensity". 0 disables shake,
        /// 1 is nominal. Values above 1 are allowed but still clamped by the ceiling.
        /// </summary>
        public static float IntensityScale
        {
            get => s_intensityScale;
            set => s_intensityScale = Mathf.Max(0f, value);
        }

        /// <summary>Amplitude actually delivered on the most recent shake. F1 readout.</summary>
        public static float LastAmplitude { get; private set; }

        /// <summary>True once anything has bound a handler.</summary>
        public static bool HasHandler => Handler != null;

        /// <summary>
        /// Request a shake. <paramref name="amplitude"/> is in metres of camera offset
        /// before clamping; <paramref name="duration"/> in seconds.
        /// </summary>
        public static void Shake(float amplitude, float duration, ShakeProfile profile)
        {
            Shake(amplitude, duration, profile, Vector3.zero);
        }

        /// <summary>
        /// Request a directional shake. <paramref name="worldDirection"/> is the world
        /// axis the camera should be pushed along - for weapon recoil that is the
        /// OPPOSITE of the shot direction, for an impact it is away from the impact.
        /// <see cref="Vector3.zero"/> means "no preferred direction" and behaves exactly
        /// like the three-argument overload.
        ///
        /// The clamp is identical either way: a direction changes the shape of the
        /// motion, never how far the camera is allowed to move.
        /// </summary>
        public static void Shake(float amplitude, float duration, ShakeProfile profile, Vector3 worldDirection)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            FeelSnapshot feel = FeelRuntime.Current;
            if (!feel.Enabled)
            {
                LastAmplitude = 0f;
                return;
            }
            float ceiling = MaxAmplitude();
            float scaled = Mathf.Min(amplitude * feel.Shake.designGain * s_intensityScale, ceiling);
            LastAmplitude = scaled;

            if (scaled <= 0.0005f)
            {
                return;
            }

            if (worldDirection.sqrMagnitude > 1e-6f)
            {
                Action<float, float, ShakeProfile, Vector3> directional = DirectionalHandler;
                if (directional != null)
                {
                    try
                    {
                        directional.Invoke(scaled, duration, profile, worldDirection.normalized);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }

                    return;
                }
            }

            Action<float, float, ShakeProfile> handler = Handler;
            if (handler == null)
            {
                if (!s_missingHandlerLogged)
                {
                    s_missingHandlerLogged = true;
                    Debug.Log(
                        "[CameraShakeBridge] No shake handler bound yet. Weapons will play without camera " +
                        "response until the camera task assigns CameraShakeBridge.Handler. Not an error.");
                }

                return;
            }

            try
            {
                handler.Invoke(scaled, duration, profile);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static float MaxAmplitude()
        {
            return FeelRuntime.Current.Shake.nominalAggregateCapMeters;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Handler = DefaultHandler;
            DirectionalHandler = DefaultDirectionalHandler;
            s_intensityScale = 1f;
            s_missingHandlerLogged = false;
            LastAmplitude = 0f;
        }
    }
}
