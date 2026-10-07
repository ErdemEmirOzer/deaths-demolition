using UnityEngine;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-096 · zombie locomotion v4, the pure parts (EditMode-testable, zero allocation, no Unity objects):
    /// <list type="bullet">
    /// <item>P1 the cadence speed: the body's REAL forward ground speed, never above its own walk intent, never negative
    /// (a knockback / crowd shove does not speed the stepping up; a jammed body does not walk in place) — and the
    /// critically damped smoothing it is read through.</item>
    /// <item>P2 the per-zombie move speed jitter (variety moved off the playback rate, so the rate stays honest).</item>
    /// <item>P3 / P5 the gait playback rate inside the clip's band and the absolute-speed gait on / off switch.</item>
    /// <item>P6 the tier acceleration table and the speed ramp.</item>
    /// <item>P7 the heading smoother (angular velocity capped, angular acceleration limited) and the facing-follow shares.</item>
    /// </list>
    /// ZombieEnemy (gameplay) and ZombieSkinnedVisual (the driver) call these; nothing here reads a transform.
    /// </summary>
    public static class ZombieLocomotionMath
    {
        // ------------------------------------------------------------------ P2 · move speed jitter

        [PrototypeOnly(PendingDecisions.CombatScaling,
            "WP-096 K1 (C-00 karar isteği 07.10, default until S-04 rules): per-zombie MOVE SPEED spread, ± fraction. Replaces " +
            "the ±8 % cadence spread (GaitSpread) that slid the feet by design; the mean over the spawn sequence is exactly 1, so " +
            "the WP-041 balance is unchanged. Invented, tune; 0 = off.", true)]
        public const float SpeedSpread = 0.04f;

        /// <summary>
        /// The move speed multiplier of the <paramref name="ordinal"/>-th driven spawn. Antithetic golden-ratio pairs: spawns 2k and
        /// 2k+1 get 1 ± d with d = spread × (frac(k · 0.381966) · 2 − 1), so the mean over any even number of consecutive spawns
        /// starting at an even ordinal is exactly 1 (an odd count is off by at most spread / count) and the spread fills
        /// [1 − spread, 1 + spread] evenly (low-discrepancy). <paramref name="variety"/> scales it (0 = off, the tests' switch).
        /// </summary>
        public static float SpeedJitter(int ordinal, float variety)
        {
            if (ordinal < 0) ordinal = -ordinal;
            int pair = ordinal >> 1;
            float u = Mathf.Repeat(pair * 0.381966f, 1f) * 2f - 1f;
            float d = variety * SpeedSpread * u;
            return (ordinal & 1) == 0 ? 1f + d : 1f - d;
        }

        // ------------------------------------------------------------------ P1 · cadence speed

        /// <summary>
        /// The speed the gait cadence follows: the body's real forward ground speed (displacement this frame · body forward),
        /// floored at 0 (a body shoved backwards does not step forwards) and capped at its own walk intent
        /// <paramref name="ownSpeed"/> (a shove from behind or a knockback does not speed the stepping up). A jammed body
        /// (real speed 0) gets 0 — it does not walk in place at full cadence.
        /// </summary>
        public static float CadenceSpeed(float forwardSpeed, float ownSpeed)
        {
            float cap = ownSpeed > 0f ? ownSpeed : 0f;
            return forwardSpeed < 0f ? 0f : forwardSpeed > cap ? cap : forwardSpeed;
        }

        /// <summary>
        /// Critically damped follow of <paramref name="target"/> (ω = 2 / <paramref name="smoothSeconds"/>, Unity SmoothDamp's
        /// convention). Exact solution over the step for a target held constant during it, so the result does not depend on the
        /// frame rate (30 / 60 / 120 fps land on the same curve). No overshoot from rest.
        /// </summary>
        public static void CriticallyDamped(ref float value, ref float rate, float target, float smoothSeconds, float dt)
        {
            if (dt <= 0f) return;
            if (smoothSeconds <= 1e-4f) { value = target; rate = 0f; return; }
            float omega = 2f / smoothSeconds;
            float e0 = value - target;
            float c = rate + omega * e0;
            float decay = Mathf.Exp(-omega * dt);
            value = target + (e0 + c * dt) * decay;
            rate = (rate - omega * c * dt) * decay;
        }

        // ------------------------------------------------------------------ P3 / P5 · gait rate

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P5: absolute ground speed (m/s) at which the gait clip takes over from the idle (on) and below which it hands " +
            "back (off) — hysteresis so a body hovering at the threshold does not flicker. Replaces the top-speed share " +
            "(GaitFullSpeed01 0.35) whose idle↔gait blend bent the cadence. Invented, tune.", true)]
        public const float GaitOnSpeed = 0.30f, GaitOffSpeed = 0.22f;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P3: a body at or above this share of its own top speed is cruising — only then does the clip's rate floor " +
            "(minRate) apply; below it (start, stop, front-line creep ×0.15, chokepoint queue, suppression) the rate is the honest " +
            "one, because the floor there makes the feet outrun the body. Invented, tune.", true)]
        public const float CruiseSpeedShare = 0.8f;

        /// <summary>Playback rate with no foot slide: ground speed over the clip's no-slide speed scaled by the body (1 when unknown).</summary>
        public static float HonestRate(float speed, float clipSpeed, float bodyScale)
        {
            float carried = clipSpeed * bodyScale;
            if (carried <= 0.01f) return 1f;
            return speed > 0f ? speed / carried : 0f;
        }

        /// <summary>
        /// The gait clip's playback rate: honest, capped at <paramref name="maxRate"/> (faster travel slides — the cadence would
        /// read frantic), floored at <paramref name="minRate"/> only while <paramref name="cruising"/>.
        /// </summary>
        public static float GaitRate(float speed, float clipSpeed, float bodyScale, float minRate, float maxRate, bool cruising)
        {
            float honest = HonestRate(speed, clipSpeed, bodyScale);
            if (honest > maxRate) return maxRate;
            if (cruising && honest < minRate) return minRate;
            return honest;
        }

        /// <summary>WP-096 P5: the gait switch with hysteresis (on at ≥ <see cref="GaitOnSpeed"/>, off below <see cref="GaitOffSpeed"/>).</summary>
        public static bool GaitOn(bool wasOn, float speed) => wasOn ? speed >= GaitOffSpeed : speed >= GaitOnSpeed;

        // ------------------------------------------------------------------ P6 · acceleration

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P6: own-locomotion acceleration by tier, m/s² — Standard, Runner, Guard, Executioner. Invented (unity-specialist " +
            "diagnosis §8 P6), tune against the start / stop clips when AP-033 lands.", true)]
        public const float AccelStandard = 6f, AccelRunner = 10f, AccelGuard = 4f, AccelUltra = 3f;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P6: braking = this × the tier acceleration (a body stops faster than it starts; keeps the approach-stop " +
            "overshoot inside the attack range margin). Invented, tune.", true)]
        public const float BrakeScale = 2f;

        public static float AccelerationFor(EnemyTier tier)
        {
            switch (tier)
            {
                case EnemyTier.Special: return AccelRunner;
                case EnemyTier.Elite: return AccelGuard;
                case EnemyTier.Ultra: return AccelUltra;
                default: return AccelStandard;
            }
        }

        /// <summary>Speed ramp: towards <paramref name="target"/> at <paramref name="accel"/> when speeding up, <paramref name="brake"/> when slowing.</summary>
        public static float Accelerate(float current, float target, float accel, float brake, float dt)
        {
            if (dt <= 0f) return current;
            float a = target >= current ? accel : brake;
            return a <= 0f ? target : Mathf.MoveTowards(current, target, a * dt);
        }

        // ------------------------------------------------------------------ P7 · heading

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P7: seconds the heading takes to reach the tier's turn-rate cap from rest (angular acceleration = cap / this); " +
            "the cap stays 120 + 62·v °/s. Invented, tune.", true)]
        public const float TurnRampSeconds = 0.15f;

        /// <summary>
        /// One step of the heading smoother, degrees: the angular velocity <paramref name="omega"/> (°/s, persists between frames)
        /// moves towards the time-optimal rate — the largest that can still brake to rest on the target at
        /// <paramref name="maxAlpha"/>, capped at <paramref name="maxOmega"/> — by at most <paramref name="maxAlpha"/>·dt. Landing
        /// on the target ends the turn (no overshoot). <paramref name="maxAlpha"/> ≤ 0 = the old constant-rate turn. Returns the new yaw.
        /// </summary>
        public static float StepYaw(float yaw, ref float omega, float targetYaw, float maxOmega, float maxAlpha, float dt)
        {
            if (dt <= 0f) return yaw;
            float error = Mathf.DeltaAngle(yaw, targetYaw);
            if (maxAlpha <= 0f)
            {
                float flat = Mathf.Clamp(error, -maxOmega * dt, maxOmega * dt);
                omega = flat / dt;
                return yaw + flat;
            }

            float abs = error < 0f ? -error : error;
            float reach = Mathf.Min(maxOmega, Mathf.Sqrt(2f * maxAlpha * abs));
            float desired = error < 0f ? -reach : reach;
            omega = Mathf.MoveTowards(omega, desired, maxAlpha * dt);
            if (omega > maxOmega) omega = maxOmega;
            else if (omega < -maxOmega) omega = -maxOmega;
            float step = omega * dt;
            if ((error >= 0f && step >= error) || (error <= 0f && step <= error))
            {
                omega = 0f;
                return yaw + error;
            }

            return yaw + step;
        }

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P7 (velocity follows the facing): heading error (°) up to which the own speed is kept in full, and the error " +
            "from which it is down to TurnSpeedShareMin — a body that must turn hard slows instead of orbiting a corner or " +
            "moonwalking sideways. 100° is the diagnosis' pivot threshold. Invented, tune.", true)]
        public const float TurnFullSpeedError = 35f, TurnSlowError = 100f;

        [PrototypeOnly(PendingDecisions.None, "WP-096 P7: own speed share kept while turning harder than TurnSlowError (the pivot hook; no pivot clip yet). Invented, tune.", true)]
        public const float TurnSpeedShareMin = 0.2f;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P7: heading error (°) up to which the walk direction is the body's facing (no crab-walk), and from which it is " +
            "the path heading again (a hard turn keeps the path, at the reduced speed). Invented, tune.", true)]
        public const float FacingFollowError = 20f, PathFollowError = 70f;

        /// <summary>Own speed share at a heading error (degrees, 0…180): 1 up to <see cref="TurnFullSpeedError"/>, smoothly down to <see cref="TurnSpeedShareMin"/> at <see cref="TurnSlowError"/>.</summary>
        public static float HeadingSpeedShare(float errorDegrees)
        {
            float t = Smooth01((errorDegrees - TurnFullSpeedError) / (TurnSlowError - TurnFullSpeedError));
            return 1f - (1f - TurnSpeedShareMin) * t;
        }

        /// <summary>Walk direction blend at a heading error: 0 = the body's facing, 1 = the path heading (smooth between <see cref="FacingFollowError"/> and <see cref="PathFollowError"/>).</summary>
        public static float FacingBlend(float errorDegrees) => Smooth01((errorDegrees - FacingFollowError) / (PathFollowError - FacingFollowError));

        private static float Smooth01(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            return x * x * (3f - 2f * x);
        }
    }
}
