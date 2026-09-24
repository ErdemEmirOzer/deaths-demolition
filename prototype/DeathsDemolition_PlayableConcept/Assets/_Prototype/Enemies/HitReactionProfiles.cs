using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>WP-076 §3.4: the knockback feel S-06 asked for, as data — distance over seconds, ease-out.</summary>
    public readonly struct KnockbackProfile
    {
        public readonly float Distance;
        public readonly float Seconds;

        public KnockbackProfile(float distance, float seconds) { Distance = distance; Seconds = seconds; }

        /// <summary>Ease-out speed at <paramref name="t"/> seconds: d(t) = D·(1 − (1 − t/T)²) → v = 2D/T·(1 − t/T).</summary>
        public float SpeedAt(float t) => Seconds <= 0f || t >= Seconds ? 0f : 2f * Distance / Seconds * (1f - t / Seconds);
    }

    /// <summary>WP-076 §3.4: the knockback in progress, read by S-06 per frame (no event).</summary>
    public struct KnockbackState
    {
        public bool Active;
        /// <summary>Flat unit world direction of the shove.</summary>
        public Vector3 Direction;
        /// <summary>Current ease-out speed, m/s.</summary>
        public float Speed;
        /// <summary>Total seconds of this shove.</summary>
        public float Seconds;
        public float Progress01;
    }

    /// <summary>
    /// WP-076 §3.1 · per-weapon-class hit response on top of the REQ-121..129 accumulator (EnemyHitReactionState, unchanged):
    /// the knockback profile once the ≥ 4.5 gate is earned (Shotgun 0.6 m, Launcher 1.2 m, 0.25 s; Elite ×0.5, Ultra 0),
    /// LMG suppression (0.35 s half-life hit counter ≥ 4 → speed ×0.70 until 0.30 s after the last hit; each hit adds
    /// 0.35 m/s sustained push, cap 1.2; Elite ×0.45, Ultra 0) and the sniper heavy-flinch floor where the gate stays shut.
    /// </summary>
    public static class HitReactionProfiles
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §3.4 (S-06 A5.4): knockback distance (m) Shotgun / Launcher and seconds (ease-out); Elite scale, Ultra never.", true)]
        public const float ShotgunKnockbackMetres = 0.6f, LauncherKnockbackMetres = 1.2f, KnockbackSeconds = 0.25f, EliteKnockbackScale = 0.5f;

        [PrototypeOnly(PendingDecisions.None, "WP-076 §3.1 LMG suppression: counter half-life (s), threshold, speed factor, hold after the last hit (s), push per hit (m/s), push cap, push half-life (s), Elite scale.", true)]
        public const float SuppressionHalfLife = 0.35f, SuppressionThreshold = 4f, SuppressionSpeedFactor = 0.70f, SuppressionHoldSeconds = 0.30f,
                           SuppressionPushPerHit = 0.35f, SuppressionPushCap = 1.2f, SuppressionPushHalfLife = 0.35f, SuppressionEliteScale = 0.45f;

        [PrototypeOnly(PendingDecisions.None, "WP-076 §3.1: a sniper hit that does not stagger (Elite, cooldown) still flinches at least this hard (1.33 = the stagger intensity of 3.2 / 2.4).", true)]
        public const float SniperHeavyFlinch = 1.33f;

        public static KnockbackProfile Knockback(WeaponClass weaponClass, bool known)
        {
            return known && weaponClass == WeaponClass.Launcher
                ? new KnockbackProfile(LauncherKnockbackMetres, KnockbackSeconds)
                : new KnockbackProfile(ShotgunKnockbackMetres, KnockbackSeconds);
        }

        public static float KnockbackTierScale(EnemyTier tier) => tier == EnemyTier.Ultra ? 0f : tier == EnemyTier.Elite ? EliteKnockbackScale : 1f;

        public static float SuppressionTierScale(EnemyTier tier) => tier == EnemyTier.Ultra ? 0f : tier == EnemyTier.Elite ? SuppressionEliteScale : 1f;
    }
}
