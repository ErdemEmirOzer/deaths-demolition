using DD.Prototype.Core.Config;
using UnityEngine;
using DD.Prototype.Enemies;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-018 · What the M1 <c>ZombieAnimator</c> forwards to when a skinned body with the
    /// AP-009 controller is attached. The gameplay calls (ZombieEnemy → ZombieAnimator) stay
    /// exactly as they were; only the pose owner changes.
    /// </summary>
    /// <summary>WP-032E · Enemy Special Weapon reaction visuals (existing clips; AP-009F may bring dedicated ones).</summary>
    public enum ZombieSpecialVisual
    {
        None = 0,
        /// <summary>Ofuda Loom bind: the body is held mid-pose (Animator speed 0), look-at off.</summary>
        Netted = 1,
        /// <summary>Kagura Bell ring: hit_special_stumble once, then the queue sway for the daze.</summary>
        Dazed = 2
    }

    public interface IZombieAnimationDriver
    {
        bool Authored { get; }
        /// <summary>death_end arrived (the clip holds its last frame; the pool may take the body).</summary>
        bool DeathFinished { get; }
        void ResetForSpawn(EnemyTier tier, float topSpeedMetersPerSecond, string fit, int idleVariant, bool rise);
        float PlayAttack(ZombieAttackStyle style, float telegraphSeconds);
        void CancelAttack();
        /// <summary>Light hit. WP-032C: <paramref name="localHit"/> is the hit direction in the body's space (x lateral, z forward) — the upper-body read is the v003 directional × tier clip.</summary>
        void PlayFlinch(Vector3 localHit, BodyRegion region, bool upperBody);
        void PlayHeavyStagger();
        /// <summary>WP-032C: an armour piece broke on a region (0 head, 1 torso, 2 leg) — the full-body armorbreak clip (replaces the heavy stagger for that moment).</summary>
        void PlayArmorBreak(int region);
        /// <summary>WP-032C: Runner stumble (hit_special_stumble) — S-03 gameplay call.</summary>
        void PlayStumble();
        /// <summary>DeathStyle: 0 fwd, 1 back, 2 side, 3 kneel; WP-032C: 4 side_R, 5 special_A, 6 special_B, 7 ultra.</summary>
        void PlayDeath(int deathStyle);
        IAttackWindowSink WindowSink { get; set; }
        /// <summary>WP-032: additive wind-up emphasis weight (0..1).</summary>
        void SetTelegraphEmphasis(float emphasis01);
        /// <summary>WP-032: idle-in-crowd / queue wait (WP-030 hands the flag in through ZombieAnimator.SetWaiting).</summary>
        void SetWaiting(bool waiting);
        /// <summary>WP-032E: the Special Weapon reaction the body shows (S-03 WP-055): Netted = frozen where it stands (Ofuda Loom), Dazed = stumble then sway (Kagura Bell); None restores.</summary>
        void SetSpecialVisual(ZombieSpecialVisual state);
    }

    /// <summary>
    /// WP-018 · DEC-029 · Receives the clip's hit window. Implemented by <c>EnemyAttackState</c>:
    /// the window is the ONLY thing that opens the attack hitbox once a clip drives the attack.
    /// </summary>
    public interface IAttackWindowSink
    {
        void OnHitWindowStart();
        void OnHitWindowEnd();
    }
}
