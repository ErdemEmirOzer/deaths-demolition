using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// Translates a weapon into the shared reaction scale of
    /// <see cref="DamageForce"/>, where 1.0 is one Assault Rifle bullet.
    ///
    /// This exists because the two sides of the contract measure "weapon weight"
    /// differently. <c>ClassicWeaponSettings.StaggerImpulse</c> is a designer trim knob
    /// with shipped values of 0.15 / 0.08 / 0.85; the enemy's reaction state thresholds
    /// (stagger at 2.4, knockback at 4.5) are expressed in DamageForce units. Sending the
    /// raw config number would mean a rifle bullet arriving at 0.15 and no weapon ever
    /// producing a reaction.
    ///
    /// So the weapon identity picks the contract value and the config knob becomes a
    /// multiplier normalised against its own shipped default. With defaults untouched the
    /// three weapons deliver exactly 1.00 / 0.55 / 0.85; doubling StaggerImpulse in the F1
    /// panel doubles the reaction, which is what a designer expects that slider to do.
    /// </summary>
    public static class WeaponImpulse
    {
        // Shipped defaults of ClassicWeaponSettings.StaggerImpulse. Only used as the
        // denominator of the trim, so editing the config still works and editing these
        // is never necessary.
        private const float AssaultRifleShippedTrim = 0.15f;
        private const float AutoSmgShippedTrim = 0.08f;
        private const float ShotgunShippedTrim = 0.85f;

        /// <summary>Reaction force for one bullet or one pellet of a classic weapon.</summary>
        public static float ForClassic(ClassicWeaponSettings settings)
        {
            if (settings == null)
            {
                return DamageForce.RifleBullet;
            }

            switch (settings.Id)
            {
                case ClassicWeaponId.AutoSmg:
                    return DamageForce.SmgBullet * Trim(settings.StaggerImpulse, AutoSmgShippedTrim);

                case ClassicWeaponId.CombatShotgun:
                    return DamageForce.ShotgunPellet * Trim(settings.StaggerImpulse, ShotgunShippedTrim);

                default:
                    return DamageForce.RifleBullet * Trim(settings.StaggerImpulse, AssaultRifleShippedTrim);
            }
        }

        private static float Trim(float configured, float shippedDefault)
        {
            if (shippedDefault <= 0.0001f)
            {
                return 1f;
            }

            return Mathf.Max(0f, configured) / shippedDefault;
        }
    }

    /// <summary>
    /// A backstop against one target being buried in reaction force inside a single
    /// window. NOT the primary REQ-124 mechanism.
    ///
    /// REQ-124 is enforced where it belongs: the enemy's reaction state accumulates
    /// impact with a half-life and refuses a second stagger inside
    /// <c>FeedbackConfigSection.StaggerCooldown</c>. That is the right place, because it
    /// is the only place that knows whether the enemy is already reacting.
    ///
    /// What this class guards is the case the enemy cannot see coming: a late-run build
    /// where one trigger pull lands eight pellets, each piercing three targets, each
    /// chaining twice and each rolling a ricochet. A legitimate shotgun blast is
    /// 8 x 0.85 = 6.8 force on one torso and must pass through untouched - the request
    /// explicitly wants shotguns to stagger. Only well past that does this attenuate, and
    /// when it does it says so in the F1 readout rather than silently.
    ///
    /// If <see cref="ThrottledCount"/> is climbing during ordinary play, the ceiling is
    /// wrong, not the weapon.
    /// </summary>
    public static class StaggerGovernor
    {
        /// <summary>
        /// PROTOTYPE-ONLY: cumulative force one target may absorb inside
        /// <see cref="WindowSeconds"/> before further impulses are damped. Set above a
        /// full shotgun blast on purpose.
        /// </summary>
        public const float BurstImpulseCeiling = 9f;

        /// <summary>
        /// PROTOTYPE-ONLY: accumulation window. Matched to the enemy reaction state's own
        /// impact half-life so the two agree on what "one burst" means.
        /// </summary>
        public const float WindowSeconds = 0.15f;

        /// <summary>PROTOTYPE-ONLY: fraction that survives once the ceiling is passed.</summary>
        public const float DampedFraction = 0.15f;

        private struct Window
        {
            public float StartTime;
            public float Accumulated;
        }

        private static readonly Dictionary<int, Window> s_windows = new Dictionary<int, Window>(64);

        /// <summary>Impulses damped since the last reset. Should stay at 0 in ordinary play.</summary>
        public static int ThrottledCount { get; private set; }

        /// <summary>
        /// Returns the impulse that should actually be sent. Call once per landed hit,
        /// immediately before building the DamageHit.
        /// </summary>
        public static float Govern(int targetId, float requestedImpulse)
        {
            if (requestedImpulse <= 0f)
            {
                return requestedImpulse;
            }

            float now = Time.time;

            if (!s_windows.TryGetValue(targetId, out Window window)
                || now - window.StartTime > WindowSeconds)
            {
                window.StartTime = now;
                window.Accumulated = 0f;
            }

            if (window.Accumulated >= BurstImpulseCeiling)
            {
                window.Accumulated += requestedImpulse * DampedFraction;
                s_windows[targetId] = window;

                ThrottledCount++;
                return requestedImpulse * DampedFraction;
            }

            window.Accumulated += requestedImpulse;
            s_windows[targetId] = window;
            return requestedImpulse;
        }

        public static void ResetCounters()
        {
            ThrottledCount = 0;
        }

        /// <summary>Forget every target. Call between runs so a pooled id cannot inherit a window.</summary>
        public static void Clear()
        {
            s_windows.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_windows.Clear();
            ThrottledCount = 0;
        }
    }
}
