using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.Damage
{
    /// <summary>WP-076 §2.1 · the body's state at the moment of death (S-06 A3.5 S1/S2 families read it).</summary>
    public enum ZombieBodyState
    {
        Standing = 0,
        Staggered = 1,
        Sliding = 2,
        Downed = 3,
        Electrocuted = 4
    }

    /// <summary>
    /// WP-076 §2 / DEC-096 · the killing push S-03 hands the animation driver. S-03 fills it once in Die(); S-06 picks the
    /// death family, onset, ragdoll hand-off and settle from it (WP-071 A3). S-03 knows no bones: <see cref="Point"/> is a
    /// world point, S-06 resolves the nearest ragdoll bone. Tier / weight are already known to the driver, not carried here.
    /// </summary>
    public readonly struct DeathImpulse
    {
        /// <summary>Unit world direction of the push (shooter → body; explosion centre → body). Never zero (fallback: body forward).</summary>
        public readonly Vector3 Direction;
        /// <summary>DamageForce units (1 = one rifle bullet), after the knockback governor. ≥ 0.</summary>
        public readonly float Force;
        public readonly DamageSourceKind Source;
        /// <summary>ADR-069-1 weapon class; meaningful only when <see cref="WeaponKnown"/>.</summary>
        public readonly WeaponClass WeaponClass;
        /// <summary>False only for the debug / cleanup Kill() (the temporary enum's Unknown).</summary>
        public readonly bool WeaponKnown;
        public readonly BodyRegion Region;
        public readonly Vector3 Point;
        /// <summary>Flat body speed at death, m/s (locomotion + knockback) — the momentum family reads ≥ 1.5.</summary>
        public readonly float BodySpeed;
        public readonly ZombieBodyState State;

        public DeathImpulse(Vector3 direction, float force, DamageSourceKind source, WeaponClass weaponClass, bool weaponKnown,
            BodyRegion region, Vector3 point, float bodySpeed, ZombieBodyState state)
        {
            Direction = direction;
            Force = force;
            Source = source;
            WeaponClass = weaponClass;
            WeaponKnown = weaponKnown;
            Region = region;
            Point = point;
            BodySpeed = bodySpeed;
            State = state;
        }

        /// <summary>Builds the impulse with the §2.1 guarantees: unit direction (zero → <paramref name="fallbackForward"/>), force ≥ 0, speed ≥ 0.</summary>
        public static DeathImpulse Build(Vector3 direction, Vector3 fallbackForward, float force, DamageSourceKind source,
            bool debugKill, BodyRegion region, Vector3 point, float bodySpeed, ZombieBodyState state, float classifyWeight = -1f)
        {
            Vector3 d = direction;
            if (d.sqrMagnitude < 1e-6f) d = fallbackForward;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
            d.Normalize();
            float f = Mathf.Max(0f, float.IsNaN(force) ? 0f : force);
            // the class is read from the single-hit weight before the governor (a saturated LMG stays an LMG)
            bool known = WeaponClassifier.TryClassify(source, classifyWeight >= 0f ? classifyWeight : f, debugKill, out WeaponClass weaponClass);
            return new DeathImpulse(d, f, source, weaponClass, known, region, point, Mathf.Max(0f, bodySpeed), state);
        }
    }

    /// <summary>
    /// WP-076 §3.3 · weapon class from the hit until DamageHit carries ADR-069-1's class directly (then this stays for tests).
    /// Pellet → Shotgun; Explosion → Launcher; Chain → Special; Bullet / Ricochet by force: ≥ 3.0 Sniper, [0.7, 0.95) Lmg,
    /// &lt; 0.7 Smg, otherwise AssaultRifle; the debug kill → unknown.
    /// </summary>
    public static class WeaponClassifier
    {
        public const float SniperForce = 3.0f, LmgForceMin = 0.7f, LmgForceMax = 0.95f;

        public static bool TryClassify(DamageSourceKind source, float impactForce, bool debugKill, out WeaponClass weaponClass)
        {
            weaponClass = WeaponClass.AssaultRifle;
            if (debugKill) return false;
            switch (source)
            {
                case DamageSourceKind.Pellet: weaponClass = WeaponClass.Shotgun; break;
                case DamageSourceKind.Explosion: weaponClass = WeaponClass.Launcher; break;
                case DamageSourceKind.Chain: weaponClass = WeaponClass.Special; break;
                default:
                    weaponClass = impactForce >= SniperForce ? WeaponClass.Sniper
                        : impactForce < LmgForceMin ? WeaponClass.Smg
                        : impactForce < LmgForceMax ? WeaponClass.Lmg
                        : WeaponClass.AssaultRifle;
                    break;
            }
            return true;
        }
    }
}
