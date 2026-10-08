using System;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>AP-033 p3 in-place motor data. Curve coordinates are Unity metres and degrees, frame origin zero.</summary>
    [Serializable]
    public sealed class ZombieTransitionData
    {
        public const string RateParameter = "ZombieTransitionRate";
        public static readonly string[] Clips =
        {
            "AP033_zombie_start_walk", "AP033_zombie_start_run", "AP033_zombie_stop_walk_l", "AP033_zombie_stop_walk_r",
            "AP033_zombie_stop_run_l", "AP033_zombie_stop_run_r", "AP033_zombie_turn90_L", "AP033_zombie_turn180_L",
            "AP033_zombie_turn45_run_L", "AP033_zombie_stumble_recover"
        };
        [Serializable]
        public struct Sample { public float frame, x, z, yaw; }
        public string kind, attachInClip, attachOutClip;
        public Sample[] curve;
        public Vector2 translate, rotate;
        public int blendInEnd, blendOutStart;
        public float attachInPhase, attachOutPhase, entrySpeed, exitSpeed, turnAngle;
        public bool IsTurn => kind == "turn";
        public bool Valid(int frames)
        {
            if (curve == null || curve.Length != frames || frames <= 1 || (kind != "start" && kind != "stop" && kind != "turn" && kind != "stumble")
                || blendInEnd < 0 || blendOutStart <= blendInEnd || blendOutStart >= frames
                || translate.x < 0 || translate.y < translate.x || translate.y >= frames
                || rotate.x < 0 || rotate.y < rotate.x || rotate.y >= frames
                || attachOutPhase < 0f || attachOutPhase >= 1f) return false;
            for (int i = 0; i < curve.Length; i++)
            {
                Sample s = curve[i];
                if (s.frame != i || float.IsNaN(s.x) || float.IsNaN(s.z) || float.IsNaN(s.yaw)
                    || float.IsInfinity(s.x) || float.IsInfinity(s.z) || float.IsInfinity(s.yaw)) return false;
            }
            return true;
        }
        public Vector3 At(float frame)
        {
            if (curve == null || curve.Length == 0) return Vector3.zero;
            int i = Mathf.Clamp(Mathf.FloorToInt(frame), 0, curve.Length - 1), j = Mathf.Min(i + 1, curve.Length - 1);
            Sample a = curve[i], b = curve[j];
            float t = Mathf.Clamp01(frame - a.frame);
            return new Vector3(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.z, b.z, t), Mathf.Lerp(a.yaw, b.yaw, t));
        }
        /// <summary>Only the authored drive window contributes; crossing an endpoint in one large tick remains bounded.</summary>
        public Vector2 Travel(float from, float to)
        {
            Vector3 a = At(Mathf.Clamp(from, translate.x, translate.y)), b = At(Mathf.Clamp(to, translate.x, translate.y));
            return new Vector2(b.x - a.x, b.y - a.y);
        }
        /// <summary>Normalised authored yaw inside its drive window. Stop/stumble yaw never gains motor authority.</summary>
        public float TurnProgress(float frame)
        {
            if (!IsTurn || rotate.y <= rotate.x) return 0f;
            float first = At(rotate.x).z, last = At(rotate.y).z;
            return Mathf.Abs(last - first) > 0.01f ? Mathf.Clamp01((At(Mathf.Clamp(frame, rotate.x, rotate.y)).z - first) / (last - first)) : 0f;
        }
        public static float NominalYaw(string clip) => clip == Clips[7] ? 180f : clip == Clips[6] ? 90f : clip == Clips[8] ? 45f : 0f;
    }
}
