using System;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-070 (M4-R1 N2): the three camera modes the player can switch between from the
    /// Pause menu. <see cref="TopDown"/> is the original fixed-angle camera and stays the
    /// default; nothing about it changes when the other modes exist.
    /// </summary>
    public enum CameraMode
    {
        /// <summary>Distant, elevated, fixed-angle camera with cursor aim (REQ-030..038). Default.</summary>
        TopDown = 0,

        /// <summary>Over-the-shoulder camera; mouse / right-stick look, screen-centre aim.</summary>
        ThirdPerson = 1,

        /// <summary>Eye-level camera; body visible, head hidden, viewmodel on its own layer and FOV.</summary>
        FirstPerson = 2
    }

    /// <summary>Which shoulder the third-person camera sits behind.</summary>
    public enum CameraShoulder
    {
        Right = 0,
        Left = 1
    }

    /// <summary>A mode change as observers see it.</summary>
    public readonly struct CameraModeChange
    {
        public readonly CameraMode From;
        public readonly CameraMode To;

        /// <summary>True when the rig snapped (run start, tests); false for the smooth in-game transition.</summary>
        public readonly bool Instant;

        public CameraModeChange(CameraMode from, CameraMode to, bool instant)
        {
            From = from;
            To = to;
            Instant = instant;
        }

        public override string ToString()
        {
            return From + " -> " + To + (Instant ? " (instant)" : "");
        }
    }

    /// <summary>
    /// WP-070: the player-facing camera settings. Plain data; <c>CameraModeService</c> owns the
    /// live instance and raises <c>SettingsChanged</c>. Persistence (ui-settings.json /
    /// input-settings.json) maps onto these fields; the ranges below are the only values
    /// the service accepts (<see cref="Sanitize"/>).
    /// </summary>
    [Serializable]
    public sealed class CameraModeSettings
    {
        public const float MinLookSensitivity = 0.02f;
        public const float MaxLookSensitivity = 0.30f;
        public const float MinStickLookDegreesPerSecond = 60f;
        public const float MaxStickLookDegreesPerSecond = 360f;
        public const float MinFirstPersonFov = 70f;
        public const float MaxFirstPersonFov = 110f;

        /// <summary>Aspect the horizontal FP FOV is defined at (Hor+: vertical FOV fixed, wider screens see more).</summary>
        public const float FovReferenceAspect = 16f / 9f;
        public const float MinThirdPersonFov = 55f;
        public const float MaxThirdPersonFov = 70f;

        /// <summary>Mode the player chose. The run starts in it.</summary>
        public CameraMode Mode = CameraMode.TopDown;

        /// <summary>Mouse look, degrees of yaw/pitch per pixel of raw mouse delta (TP/FP; no smoothing/acceleration).</summary>
        public float LookSensitivity = 0.08f;

        /// <summary>Right-stick look speed at full deflection (TP/FP).</summary>
        public float StickLookDegreesPerSecond = 180f;

        /// <summary>Inverts vertical look (mouse and stick) in TP/FP.</summary>
        public bool InvertY;

        /// <summary>
        /// First-person world FOV in HORIZONTAL degrees at the 16:9 reference aspect (Hor+;
        /// C-00 decision 2026-09-24: default 90, range 70–110). The rig converts it once to
        /// Unity's vertical FOV with <see cref="FovReferenceAspect"/>, so other aspects keep
        /// the vertical angle and see more / less at the sides.
        /// </summary>
        public float FirstPersonFov = 90f;

        /// <summary>Third-person world FOV in VERTICAL degrees (≈ 91° horizontal at 16:9 for 60).</summary>
        public float ThirdPersonFov = 60f;

        /// <summary>Third-person shoulder side.</summary>
        public CameraShoulder Shoulder = CameraShoulder.Right;

        /// <summary>0 disables first-person locomotion bob (motion-sickness option), 1 is the authored bob.</summary>
        public float HeadBobScale = 1f;

        /// <summary>
        /// Accessibility master switch (direction doc D-10): bob 0, FOV punch 0, shake 0.3×,
        /// instant mode transitions, no TP fire squeeze. Recoil (a game rule) stays.
        /// </summary>
        public bool ReducedMotion;

        public CameraModeSettings Clone()
        {
            return (CameraModeSettings)MemberwiseClone();
        }

        /// <summary>Clamps every field into its accepted range; unknown enum values fall back to defaults.</summary>
        public void Sanitize()
        {
            if (!Enum.IsDefined(typeof(CameraMode), Mode))
            {
                Mode = CameraMode.TopDown;
            }

            if (!Enum.IsDefined(typeof(CameraShoulder), Shoulder))
            {
                Shoulder = CameraShoulder.Right;
            }

            LookSensitivity = Clamp(LookSensitivity, MinLookSensitivity, MaxLookSensitivity, 0.08f);
            StickLookDegreesPerSecond = Clamp(StickLookDegreesPerSecond, MinStickLookDegreesPerSecond, MaxStickLookDegreesPerSecond, 180f);
            FirstPersonFov = Clamp(FirstPersonFov, MinFirstPersonFov, MaxFirstPersonFov, 90f);
            ThirdPersonFov = Clamp(ThirdPersonFov, MinThirdPersonFov, MaxThirdPersonFov, 60f);
            HeadBobScale = Clamp(HeadBobScale, 0f, 1f, 1f);
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return Mathf.Clamp(value, min, max);
        }
    }
}

namespace DD.Prototype.CameraSystem
{
    /// <summary>WP-070: what the occlusion fader may fade in a mode.</summary>
    public enum CameraOccluderPolicy
    {
        /// <summary>Top-down: every CameraOccluder between camera and player (roofs, walls) fades.</summary>
        FadeAll = 0,

        /// <summary>Third person: only occluders crossing the camera↔player line fade (the camera also pulls in).</summary>
        FadeCameraLine = 1,

        /// <summary>First person: nothing fades.</summary>
        None = 2
    }

    /// <summary>
    /// WP-070 · Per-mode rendering profile. Single-writer rule (S-05, WP-075): the camera
    /// writes only camera-local values (near/far clip, occluder policy); the quality module
    /// applies the global ones (LOD bias, shadow distance/cascades) as quality-tier base ×
    /// this profile on <c>CameraModeService.Changed</c>. Defaults are proposals; S-05 / S-07
    /// may replace them through <c>CameraModeService.SetProfile</c>.
    /// </summary>
    [Serializable]
    public struct CameraModeProfile
    {
        /// <summary>
        /// Multiplier on the quality tier's lodBias base (S-05 QUALITY-TIERS-V2 §3:
        /// lodBias = max(1, tierBase × factor); bases 1.0 / 2.0 / 2.5 → High TP 1.2, FP 1.4).
        /// </summary>
        public float LodBiasFactor;

        /// <summary>Shadow distance in metres (quality module clamps to its tier).</summary>
        public float ShadowDistance;

        /// <summary>Proposed shadow cascade count (quality module decides per tier).</summary>
        public int ShadowCascades;

        public float NearClip;
        public float FarClip;

        public CameraOccluderPolicy OccluderPolicy;

        /// <summary>Scale on the aggregate shake amplitude (1 = today's top-down shake).</summary>
        public float ShakeScale;

        public static CameraModeProfile Default(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.ThirdPerson:
                    return new CameraModeProfile
                    {
                        LodBiasFactor = 0.6f, ShadowDistance = 60f, ShadowCascades = 3, NearClip = 0.05f, FarClip = 450f,
                        OccluderPolicy = CameraOccluderPolicy.FadeCameraLine, ShakeScale = 0.55f,
                    };
                case CameraMode.FirstPerson:
                    return new CameraModeProfile
                    {
                        LodBiasFactor = 0.7f, ShadowDistance = 70f, ShadowCascades = 4, NearClip = 0.03f, FarClip = 450f,
                        OccluderPolicy = CameraOccluderPolicy.None, ShakeScale = 0.3f,
                    };
                default:
                    return new CameraModeProfile
                    {
                        LodBiasFactor = 1f, ShadowDistance = 60f, ShadowCascades = 2, NearClip = 0.3f, FarClip = 260f,
                        OccluderPolicy = CameraOccluderPolicy.FadeAll, ShakeScale = 1f,
                    };
            }
        }
    }
}

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-070 (direction doc D-8): what the camera wants from the OS cursor. <c>UiCursor</c>
    /// (S-09) is the single writer of the cursor; it reads this intent and decides with its
    /// own pause/menu priority.
    /// </summary>
    public enum CameraCursorIntent
    {
        /// <summary>Top-down, pause, menus, look blocks: the cursor is free (and shown by UiCursor).</summary>
        Free = 0,

        /// <summary>TP/FP gameplay: lock and hide the cursor, mouse delta drives the view.</summary>
        Locked = 1
    }
}
