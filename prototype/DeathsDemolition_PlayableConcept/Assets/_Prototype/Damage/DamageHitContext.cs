using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Damage
{
    /// <summary>
    /// WP-069 (ADR-069-1 §9) · ambient facts about the direct hit being applied RIGHT NOW, beside the
    /// frozen <see cref="DamageHit"/> contract (which must not widen). Set by the weapon engine around one
    /// <c>IDamageReceiver.ApplyHit</c> call and cleared right after — the same single-threaded ambient
    /// pattern as <see cref="DamageTransaction"/>. Readers: <see cref="RegionalDamageBody"/> (the weapon's
    /// head / armour multipliers) and any synchronous damage-event handler that wants the weapon and the
    /// struck collider (S-06 ragdoll / directional reactions, S-03 AI) — read inside the handler, never later.
    /// Outside a weapon hit every value is the neutral rule (multipliers 1, no weapon, no collider).
    /// </summary>
    public static class DamageHitContext
    {
        /// <summary>Extra head multiplier on top of the regional ×2.0 (1 = the rule).</summary>
        public static float HeadMultiplier { get; private set; } = 1f;

        /// <summary>Multiplier on the damage dealt to armour durability (1 = the rule).</summary>
        public static float ArmorMultiplier { get; private set; } = 1f;

        /// <summary>True while a classic weapon's projectile hit is being applied.</summary>
        public static bool FromClassicWeapon { get; private set; }

        /// <summary>That weapon's runtime index (valid when <see cref="FromClassicWeapon"/>).</summary>
        public static ClassicWeaponId Weapon { get; private set; }

        /// <summary>The struck collider (hitbox → bone); null for area hits.</summary>
        public static Collider HitCollider { get; private set; }

        /// <summary>WP-069 B4: the hit ignores regional armour (both Specials: electricity / gel — DIRECTION §7a/§7b).</summary>
        public static bool IgnoreArmor { get; private set; }

        /// <summary>WP-069 B4: true while a Special Weapon's damage is being applied (attribution: chain / gel).</summary>
        public static bool FromSpecialWeapon { get; private set; }

        /// <summary>That Special (valid when <see cref="FromSpecialWeapon"/>).</summary>
        public static SpecialWeaponId Special { get; private set; }

        public static void Set(float headMultiplier, float armorMultiplier, bool fromClassicWeapon, ClassicWeaponId weapon, Collider hitCollider)
        {
            HeadMultiplier = headMultiplier > 0f ? headMultiplier : 1f;
            ArmorMultiplier = armorMultiplier > 0f ? armorMultiplier : 1f;
            FromClassicWeapon = fromClassicWeapon;
            Weapon = weapon;
            HitCollider = hitCollider;
            IgnoreArmor = false;
            FromSpecialWeapon = false;
        }

        /// <summary>WP-069 B4: a Special Weapon hit — neutral multipliers, optional armour bypass, the Special's id.</summary>
        public static void SetSpecial(SpecialWeaponId special, bool ignoreArmor, Collider hitCollider)
        {
            HeadMultiplier = 1f;
            ArmorMultiplier = 1f;
            FromClassicWeapon = false;
            Weapon = default;
            HitCollider = hitCollider;
            IgnoreArmor = ignoreArmor;
            FromSpecialWeapon = true;
            Special = special;
        }

        public static void Clear()
        {
            HeadMultiplier = 1f;
            ArmorMultiplier = 1f;
            FromClassicWeapon = false;
            Weapon = default;
            HitCollider = null;
            IgnoreArmor = false;
            FromSpecialWeapon = false;
            Special = default;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
        }
    }
}
