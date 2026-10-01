using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-069 · the first-person weapon feel layer (C-00 26.09; values WP-073 A4.1 / A4.2 / A4.7, S-07's
    /// <see cref="CameraModesFeel"/> weight classes). One offset in the viewmodel anchor's space on top of the fpView rest pose,
    /// independent of the FP arms:
    ///   · sway — the view model trails the look: 1.2 cm and 1.2° per 100°/s (rotation ≤ 2°), spring 8 Hz ζ 0.9
    ///     (W1 10 Hz ×0.8, W3/W4 6 Hz ζ 0.85 ×1.4), running ×1.2, ADS ×0.35 at 12 Hz;
    ///   · bob — walk / run (camera band × 1.5 / 1.6, weight scale), faded under 0.35 m/s, ADS ×0.2;
    ///   · kick — per shot, the A4.2 view-model row (back / up cm, pitch / yaw / roll °), auto strings stacking
    ///     ×min(1 + 0.13 (n − 1), 1.8) until 0.25 s without a shot, ADS back ×0.4 / pitch ×1 / the rest ×0.6; a spring with
    ///     the weight class's damping settles it within the row's recovery time;
    ///   · the ADS weight eased (cubic-bezier 0.22, 1, 0.36, 1) from the gameplay's linear one.
    /// Reduced motion zeroes sway, bob and kick (C-00); the ADS pose and lens stay. At rest (no look, no speed, no shot)
    /// the offset is exactly zero — the fpView rest pose does not change. Pure state, no allocation.
    /// </summary>
    public sealed class FpWeaponFeel
    {
        /// <summary>A4.2 view-model kick columns: back / up (cm), pitch / yaw / roll (°, roll + = top to the left), recovery (s).</summary>
        public readonly struct VmKick
        {
            public readonly float BackCm, UpCm, PitchDeg, YawDeg, RollDeg, RecoverySeconds;

            public VmKick(float back, float up, float pitch, float yaw, float roll, float recovery)
            {
                BackCm = back; UpCm = up; PitchDeg = pitch; YawDeg = yaw; RollDeg = roll; RecoverySeconds = recovery;
            }
        }

        // A4.2 "VM geri / yukarı cm · pitch / yaw / roll °" and "VM top. s". Roll: toward the hand side (clockwise, −) except smg_2 (left, +).
        private static readonly string[] s_kickIds = { "ar_1", "ar_2", "smg_1", "smg_2", "sg_1", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1", "sw_arc", "sw_gel" };
        private static readonly VmKick[] s_kicks =
        {
            new VmKick(3.0f, 0.6f, 2.2f, 0.5f, -0.7f, 0.11f),
            new VmKick(3.5f, 0.8f, 2.6f, 0.6f, -0.8f, 0.14f),
            new VmKick(2.0f, 0.4f, 1.4f, 0.8f, -0.5f, 0.09f),
            new VmKick(2.2f, 0.5f, 1.6f, 0.9f, 0.6f, 0.09f),
            new VmKick(8.0f, 2.0f, 6.5f, 0.8f, -1.4f, 0.30f),
            new VmKick(9.0f, 2.4f, 7.5f, 1.0f, -1.6f, 0.32f),
            new VmKick(3.5f, 0.7f, 2.6f, 1.0f, -1.2f, 0.12f),
            new VmKick(3.2f, 0.6f, 2.4f, 0.9f, -1.0f, 0.12f),
            new VmKick(9.0f, 2.6f, 7.5f, 0.5f, -1.2f, 0.34f),
            new VmKick(12.0f, 3.0f, 9.0f, 1.5f, -1.8f, 0.55f),
            new VmKick(5.0f, 1.2f, 4.0f, 1.0f, -0.8f, 0.40f),
            new VmKick(3.0f, 0.8f, 2.0f, 0.6f, -0.5f, 0.20f),
        };

        /// <summary>The A4.2 view-model row of a weapon id (a weapon without a row kicks like ar_1).</summary>
        public static VmKick KickFor(string weaponId)
        {
            for (int i = 0; i < s_kickIds.Length; i++)
            {
                if (s_kickIds[i] == weaponId) return s_kicks[i];
            }

            return s_kicks[0];
        }

        // ---- A4.1 / A4.2 constants
        public const float SwayCmPer100 = 1.2f, SwayDegPer100 = 1.2f, SwayRotCapDeg = 2f, SwayRateCapDegPerSec = 300f;
        public const float SwayRunScale = 1.2f, SwayAdsScale = 0.35f, SwayAdsHz = 12f;
        public const float VmBobWalkScale = 1.5f, VmBobRunScale = 1.6f, VmBobAdsScale = 0.2f, WalkStepHz = 1.9f, RunStepHz = 3.0f;
        public const float AutoStackStep = 0.13f, AutoStackCap = 1.8f, AutoStackResetSeconds = 0.25f;
        public const float AdsKickBack = 0.4f, AdsKickPitch = 1f, AdsKickOther = 0.6f;
        private const float MaxSubstep = 1f / 240f;

        /// <summary>
        /// WP-069 M4 (C-00 01.10, S-07 AD r4): a kick is fed over a ramp of this share of the row's recovery (≤ KickRampMaxSeconds),
        /// eased in (u²) — the same total, only spread. rl_1 (0.55 s) takes ≈ 3 frames and the firing frame shows ≈ 11 % of it, so
        /// the rocket that leaves the un-kicked muzzle in that frame stays at the tube mouth (the whole 12 cm / 9° in one frame put
        /// it ~120 px below). A ramp under one frame (rifles / SMGs / LMGs, 0.09–0.14 s) stays instant: their snap is unchanged.
        /// </summary>
        public const float KickRampShare = 0.1f, KickRampMaxSeconds = 0.05f;

        // ---- state
        private Vector3 _swayPos, _swayPosVel, _swayRot, _swayRotVel;   // metres; degrees (pitch, yaw, roll)
        private Vector3 _kickPos, _kickPosVel, _kickRot, _kickRotVel;
        private float _kickOmega = 40f, _kickZeta = 0.65f;
        private Vector3 _kickCapPos, _kickCapRot;
        private Vector3 _rampPos, _rampRot;   // M4: the kick being fed, its total
        private float _rampSeconds, _rampT, _rampFed;   // ramp length, time into it, cumulative share fed (0…1)
        private int _shotsInString;
        private float _sinceShot = 999f;
        private float _bobPhase, _bobAmp;

        /// <summary>The anchor-space position offset of the weapon root (metres).</summary>
        public Vector3 Position { get; private set; }

        /// <summary>The anchor-space rotation offset about the weapon root.</summary>
        public Quaternion Rotation { get; private set; } = Quaternion.identity;

        /// <summary>The ADS weight on the eased curve (0 hip … 1 on the sights).</summary>
        public float AdsEased { get; private set; }

        public int ShotsInString => _shotsInString;

        public void Reset()
        {
            _swayPos = _swayPosVel = _swayRot = _swayRotVel = Vector3.zero;
            _kickPos = _kickPosVel = _kickRot = _kickRotVel = Vector3.zero;
            _rampPos = _rampRot = Vector3.zero;
            _rampSeconds = _rampT = _rampFed = 0f;
            _shotsInString = 0;
            _sinceShot = 999f;
            _bobPhase = _bobAmp = 0f;
            AdsEased = 0f;
            Position = Vector3.zero;
            Rotation = Quaternion.identity;
        }

        /// <summary>cubic-bezier(0.22, 1, 0.36, 1) — A4.1's ADS ease-out, evaluated at t (0..1).</summary>
        public static float EaseAds(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f || t >= 1f) return t;
            // solve x(u) = t for u (Newton, then bisection guard), return y(u)
            float u = t;
            for (int i = 0; i < 8; i++)
            {
                float x = Bezier(u, 0.22f, 0.36f) - t;
                float dx = BezierDerivative(u, 0.22f, 0.36f);
                if (Mathf.Abs(x) < 1e-5f) break;
                if (Mathf.Abs(dx) < 1e-6f) break;
                u = Mathf.Clamp01(u - x / dx);
            }

            return Bezier(u, 1f, 1f);
        }

        private static float Bezier(float u, float p1, float p2)
        {
            float v = 1f - u;
            return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
        }

        private static float BezierDerivative(float u, float p1, float p2)
        {
            float v = 1f - u;
            return 3f * v * v * p1 + 6f * v * u * (p2 - p1) + 3f * u * u * (1f - p2);
        }

        /// <summary>A4.1: the FP world-FOV factor at full ADS — AR / SMG / LMG / Special 0.85, shotgun 0.90, sr_1 scope 0.45, launcher 0.80.</summary>
        public static float AdsWorldFovScale(WeaponClass cls)
        {
            switch (cls)
            {
                case WeaponClass.Shotgun: return 0.90f;
                case WeaponClass.Sniper: return 0.45f;
                case WeaponClass.Launcher: return 0.80f;
                default: return 0.85f;
            }
        }

        /// <summary>A4.1 sway spring by weight class (Hz, ζ, amplitude scale).</summary>
        public static void SwaySpring(FeelWeight w, out float hz, out float zeta, out float scale)
        {
            switch (w)
            {
                case FeelWeight.W1: hz = 10f; zeta = 0.9f; scale = 0.8f; break;
                case FeelWeight.W3:
                case FeelWeight.W4: hz = 6f; zeta = 0.85f; scale = 1.4f; break;
                default: hz = 8f; zeta = 0.9f; scale = 1f; break;
            }
        }

        /// <summary>
        /// One shot: the row's kick (ADS-scaled at the eased weight <paramref name="adsEased"/>), stacked in an auto string;
        /// <paramref name="yawSign"/> ±1 picks the yaw side. The spring takes the weight class's damping and settles within the
        /// row's recovery time.
        /// </summary>
        public void Shot(string weaponId, FeelWeight weight, float adsEased, float yawSign, bool reducedMotion)
        {
            VmKick k = KickFor(weaponId);
            _shotsInString = _sinceShot < AutoStackResetSeconds ? _shotsInString + 1 : 1;
            _sinceShot = 0f;
            _kickZeta = CameraModesFeel.KickSpring(weight).Zeta;
            _kickOmega = 3f / (Mathf.Max(0.3f, _kickZeta) * Mathf.Max(0.05f, k.RecoverySeconds));   // envelope e^(−ζωt) at 5 % by the recovery time
            if (reducedMotion) return;
            float stack = Mathf.Min(1f + AutoStackStep * (_shotsInString - 1), AutoStackCap);
            float a = Mathf.Clamp01(adsEased);
            float back = Mathf.Lerp(1f, AdsKickBack, a), pitch = Mathf.Lerp(1f, AdsKickPitch, a), other = Mathf.Lerp(1f, AdsKickOther, a);
            var pos = new Vector3(0f, k.UpCm * 0.01f * other, -k.BackCm * 0.01f * back) * stack;
            var rot = new Vector3(-k.PitchDeg * pitch, k.YawDeg * other * Mathf.Sign(yawSign == 0f ? 1f : yawSign), k.RollDeg * other) * stack;   // x − = muzzle up
            _kickCapPos = new Vector3(0f, k.UpCm * 0.01f, k.BackCm * 0.01f) * AutoStackCap;
            _kickCapRot = new Vector3(k.PitchDeg, k.YawDeg, Mathf.Abs(k.RollDeg)) * AutoStackCap;
            // M4: a new shot first lands what is left of the previous ramp (the total is never lost), then ramps its own kick.
            FeedRamp(1f);
            float ramp = Mathf.Min(KickRampMaxSeconds, k.RecoverySeconds * KickRampShare);
            if (ramp < 1f / 60f)   // under a frame: instant, as before (ar_1 / SMGs / LMGs)
            {
                _kickPos = Clamp(_kickPos + pos, _kickCapPos);
                _kickRot = Clamp(_kickRot + rot, _kickCapRot);
                return;
            }

            _rampPos = pos; _rampRot = rot;
            _rampSeconds = ramp; _rampT = 0f; _rampFed = 0f;
        }

        /// <summary>M4: feed the ramp up to cumulative share <paramref name="target"/> (0…1, eased in by the caller).</summary>
        private void FeedRamp(float target)
        {
            if (_rampSeconds <= 0f) return;
            float step = Mathf.Clamp01(target) - _rampFed;
            if (step > 0f)
            {
                _kickPos = Clamp(_kickPos + _rampPos * step, _kickCapPos);
                _kickRot = Clamp(_kickRot + _rampRot * step, _kickCapRot);
                _rampFed += step;
            }

            if (_rampFed >= 1f - 1e-6f) _rampSeconds = 0f;
        }

        /// <summary>M4 (tests): the share of the last kick still waiting to be fed (0 once landed).</summary>
        public float KickPending => _rampSeconds > 0f ? 1f - _rampFed : 0f;

        private static Vector3 Clamp(Vector3 v, Vector3 cap)
        {
            return new Vector3(Mathf.Clamp(v.x, -cap.x, cap.x), Mathf.Clamp(v.y, -cap.y, cap.y), Mathf.Clamp(v.z, -cap.z, cap.z));
        }

        /// <summary>
        /// Advance by <paramref name="dt"/>: look rates (°/s; yaw + = right, pitch + = up), planar speed (m/s), the gameplay's
        /// linear ADS weight, the weight class; reduced motion zeroes sway / bob / kick.
        /// </summary>
        public void Tick(float dt, float yawRateDegPerSec, float pitchRateDegPerSec, float planarSpeed, float adsLinear01, FeelWeight weight, bool reducedMotion)
        {
            AdsEased = EaseAds(adsLinear01);
            if (dt <= 0f)
            {
                return;
            }

            _sinceShot += dt;
            if (_sinceShot >= AutoStackResetSeconds) _shotsInString = 0;
            float ads = AdsEased;

            // sway target
            SwaySpring(weight, out float hz, out float zeta, out float scale);
            float run = Mathf.InverseLerp(CameraModesFeel.BobWalkSpeed, CameraModesFeel.BobRunSpeed, planarSpeed);
            scale *= Mathf.Lerp(1f, SwayRunScale, run) * Mathf.Lerp(1f, SwayAdsScale, ads);
            hz = Mathf.Lerp(hz, SwayAdsHz, ads);
            float yaw = Mathf.Clamp(yawRateDegPerSec, -SwayRateCapDegPerSec, SwayRateCapDegPerSec) * 0.01f;
            float pitch = Mathf.Clamp(pitchRateDegPerSec, -SwayRateCapDegPerSec, SwayRateCapDegPerSec) * 0.01f;
            Vector3 swayPosTarget = reducedMotion ? Vector3.zero : new Vector3(-yaw, -pitch, 0f) * (SwayCmPer100 * 0.01f * scale);
            Vector3 swayRotTarget = reducedMotion
                ? Vector3.zero
                : new Vector3(Mathf.Clamp(pitch * SwayDegPer100 * scale, -SwayRotCapDeg, SwayRotCapDeg), Mathf.Clamp(-yaw * SwayDegPer100 * scale, -SwayRotCapDeg, SwayRotCapDeg), 0f);

            // bob
            float bobTarget = reducedMotion || planarSpeed < CameraModesFeel.BobMinSpeed ? 0f : 1f;
            _bobAmp = Mathf.MoveTowards(_bobAmp, bobTarget, dt / CameraModesFeel.BobFadeSeconds);
            float stepHz = Mathf.Lerp(WalkStepHz, RunStepHz, run);
            _bobPhase = Mathf.Repeat(_bobPhase + dt * stepHz * 0.5f * 2f * Mathf.PI, 2f * Mathf.PI);   // lateral at half the step rate
            float vert = Mathf.Lerp(CameraModesFeel.BobWalk.VerticalCm * VmBobWalkScale, CameraModesFeel.BobRun.VerticalCm * VmBobRunScale, run) * 0.01f;
            float lat = Mathf.Lerp(CameraModesFeel.BobWalk.LateralCm * VmBobWalkScale, CameraModesFeel.BobRun.LateralCm * VmBobRunScale, run) * 0.01f;
            float walkIn = Mathf.Clamp01(planarSpeed / CameraModesFeel.BobWalkSpeed);
            float bobScale = _bobAmp * walkIn * CameraModesFeel.BobWeightScale(weight) * Mathf.Lerp(1f, VmBobAdsScale, ads);
            var bob = new Vector3(lat * Mathf.Sin(_bobPhase), -vert * 0.5f * (1f - Mathf.Cos(2f * _bobPhase)), 0f) * bobScale;

            // M4: the kick ramp, eased in (u²) — the firing frame gets the smallest share
            if (_rampSeconds > 0f)
            {
                _rampT += dt;
                float u = Mathf.Clamp01(_rampT / _rampSeconds);
                FeedRamp(u * u);
            }

            // springs (sub-stepped: stable at 12 Hz and the stiffest kick)
            float left = dt;
            float omega = 2f * Mathf.PI * hz;
            while (left > 1e-6f)
            {
                float h = Mathf.Min(left, MaxSubstep);
                Spring(ref _swayPos, ref _swayPosVel, swayPosTarget, omega, zeta, h);
                Spring(ref _swayRot, ref _swayRotVel, swayRotTarget, omega, zeta, h);
                Spring(ref _kickPos, ref _kickPosVel, Vector3.zero, _kickOmega, _kickZeta, h);
                Spring(ref _kickRot, ref _kickRotVel, Vector3.zero, _kickOmega, _kickZeta, h);
                left -= h;
            }

            if (reducedMotion)
            {
                _kickPos = _kickPosVel = _kickRot = _kickRotVel = Vector3.zero;
            }

            Position = _swayPos + _kickPos + bob;
            Vector3 r = _swayRot + _kickRot;
            Rotation = Quaternion.Euler(r.x, r.y, r.z);
        }

        private static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float omega, float zeta, float h)
        {
            // semi-implicit Euler on x'' = ω²(target − x) − 2ζω x'
            v += (omega * omega * (target - x) - 2f * zeta * omega * v) * h;
            x += v * h;
        }
    }
}
