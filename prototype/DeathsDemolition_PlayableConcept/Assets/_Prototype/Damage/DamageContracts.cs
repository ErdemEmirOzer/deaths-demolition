using UnityEngine;
using CoreCfg = DD.Prototype.Core.Config;

namespace DD.Prototype.Damage
{
    // =====================================================================================
    // CROSS-TASK CONTRACT - DO NOT CHANGE THESE SIGNATURES
    // -------------------------------------------------------------------------------------
    // G-03 (weapons), G-07 (round / gold) and G-09 (audio) all bind to the types in this
    // file. The shapes below are frozen for the wave; add new helpers in separate types
    // instead of widening these.
    //
    // NOTE ON THE DUPLICATE ENUM
    // DD.Prototype.Core.Config already declares its own BodyRegion, used by the config
    // section signatures (ArmorConfigSection.ExposedMultiplierFor, ExplosionConfigSection
    // .ShareFor). The wave contract mandates DD.Prototype.Damage.BodyRegion as the shared
    // gameplay type, so both exist. Their members are declared in the same order with the
    // same values; DamageRegions does the conversion explicitly rather than by cast so a
    // future reorder fails loudly instead of silently mis-mapping a hit region.
    //
    // Any file that imports both DD.Prototype.Damage and DD.Prototype.Core.Config MUST
    // alias:  using BodyRegion = DD.Prototype.Damage.BodyRegion;
    // =====================================================================================

    /// <summary>Body region a direct hit resolved to. Matches the three armour zones.</summary>
    public enum BodyRegion
    {
        Head = 0,
        UpperBody = 1,
        LowerBody = 2
    }

    /// <summary>What produced a damage event. Drives feedback intensity, not the maths.</summary>
    public enum DamageSourceKind
    {
        Bullet = 0,
        Pellet = 1,
        Explosion = 2,
        Chain = 3,
        Ricochet = 4
    }

    /// <summary>One direct hit on one body region.</summary>
    public struct DamageHit
    {
        /// <summary>Raw damage before any regional multiplier. Never pre-multiply this.</summary>
        public float Amount;

        /// <summary>Region resolved from the collider that was struck.</summary>
        public BodyRegion Region;

        /// <summary>World-space impact point. Used to place effects, never for maths.</summary>
        public Vector3 Point;

        /// <summary>
        /// Normalised world-space travel direction of the projectile (shooter -> target).
        /// Knockback and directional flinch push ALONG this vector.
        /// </summary>
        public Vector3 Direction;

        /// <summary>
        /// Weapon weight expressed as a reaction scalar. 1.0 == one standard rifle bullet.
        /// See <see cref="DamageForce"/>. PROTOTYPE-ONLY scale, invented.
        /// </summary>
        public float ImpactForce;

        /// <summary>Which system produced the hit.</summary>
        public DamageSourceKind Source;
    }

    /// <summary>Outcome of one hit or one explosion on one receiver.</summary>
    public struct DamageResult
    {
        /// <summary>Health actually removed. Zero whenever the region was protected.</summary>
        public float HealthDamage;

        /// <summary>Armour durability actually removed, already clamped. Surplus is discarded.</summary>
        public float ArmorDamage;

        /// <summary>An armour piece went from protecting to broken during this call.</summary>
        public bool ArmorBroke;

        /// <summary>This call took the receiver from alive to dead. True at most once.</summary>
        public bool Killed;

        /// <summary>The region was protected when the hit landed, so no health was touched.</summary>
        public bool WasArmored;

        /// <summary>
        /// Direct hit: the region that was struck.
        /// Explosion: the region that contributed the most health damage, else the region
        /// whose armour broke, else UpperBody.
        /// </summary>
        public BodyRegion Region;
    }

    /// <summary>Anything a bullet, pellet, chain or ricochet can hit.</summary>
    public interface IDamageReceiver
    {
        DamageResult ApplyHit(in DamageHit hit);
    }

    /// <summary>Anything an explosion can splash. Split model lives in the receiver.</summary>
    public interface IExplosionReceiver
    {
        DamageResult ApplyExplosion(float totalDamage, Vector3 origin);
    }

    // =====================================================================================
    // Helpers - not part of the frozen contract, safe to extend.
    // =====================================================================================

    /// <summary>
    /// PROTOTYPE-ONLY reaction-force scale for <see cref="DamageHit.ImpactForce"/>.
    /// INVENTED: the request never quantifies "weapon weight". 1.0 is defined as one
    /// Assault Rifle bullet, and every reaction threshold in the enemy is expressed in
    /// these units so one number governs the whole feel.
    /// </summary>
    public static class DamageForce
    {
        /// <summary>Assault Rifle bullet. The unit of the scale.</summary>
        [CoreCfg.PrototypeOnly(CoreCfg.PendingDecisions.None,
            "Anchor of the whole reaction scale: 1.0 IS one Assault Rifle bullet. Moving it " +
            "rescales every stagger and knockback threshold at once.", true)]
        public const float RifleBullet = 1.0f;

        /// <summary>Auto SMG bullet. Lighter, fires ~1.5x faster.</summary>
        [CoreCfg.PrototypeOnly(CoreCfg.PendingDecisions.None,
            "Reaction weight of an SMG bullet. The request only says 'faster small hit " +
            "reaction' and gives no number.", true)]
        public const float SmgBullet = 0.55f;

        /// <summary>One Combat Shotgun pellet. Eight of them land inside one window.</summary>
        [CoreCfg.PrototypeOnly(CoreCfg.PendingDecisions.None,
            "Reaction weight of one pellet. 8 x 0.85 = 6.8 is what makes a shell stagger; the " +
            "request only says 'strong recoil, stagger and close-range effect'.", true)]
        public const float ShotgunPellet = 0.85f;

        /// <summary>An explosion at its centre. Falloff should scale this down.</summary>
        [CoreCfg.PrototypeOnly(CoreCfg.PendingDecisions.None,
            "Reaction weight of a blast at its centre. Not in the request.", true)]
        public const float ExplosionCore = 4.0f;

        /// <summary>Chain arc. Deliberately weak so chains never stun-lock.</summary>
        [CoreCfg.PrototypeOnly(CoreCfg.PendingDecisions.None,
            "Reaction weight of a chain arc. Deliberately under the stagger threshold " +
            "(REQ-124, no stun-lock). Not in the request.", true)]
        public const float Chain = 0.25f;

        /// <summary>Ricochet. Same as the bullet that spawned it, halved.</summary>
        [CoreCfg.PrototypeOnly(CoreCfg.PendingDecisions.None,
            "Reaction weight of a ricochet. Not in the request.", true)]
        public const float Ricochet = 0.5f;
    }

    /// <summary>Region conversion and classification. No allocations, no branching cost.</summary>
    public static class DamageRegions
    {
        /// <summary>Damage-layer region -> config-layer region.</summary>
        public static CoreCfg.BodyRegion ToCore(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return CoreCfg.BodyRegion.Head;
                case BodyRegion.UpperBody: return CoreCfg.BodyRegion.UpperBody;
                case BodyRegion.LowerBody: return CoreCfg.BodyRegion.LowerBody;
                default: return CoreCfg.BodyRegion.UpperBody;
            }
        }

        /// <summary>Config-layer region -> damage-layer region.</summary>
        public static BodyRegion FromCore(CoreCfg.BodyRegion region)
        {
            switch (region)
            {
                case CoreCfg.BodyRegion.Head: return BodyRegion.Head;
                case CoreCfg.BodyRegion.UpperBody: return BodyRegion.UpperBody;
                case CoreCfg.BodyRegion.LowerBody: return BodyRegion.LowerBody;
                default: return BodyRegion.UpperBody;
            }
        }

        /// <summary>Armour piece that protects a region, or None.</summary>
        public static CoreCfg.ArmorSlot SlotFor(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return CoreCfg.ArmorSlot.Helmet;
                case BodyRegion.UpperBody: return CoreCfg.ArmorSlot.Body;
                case BodyRegion.LowerBody: return CoreCfg.ArmorSlot.Leg;
                default: return CoreCfg.ArmorSlot.None;
            }
        }

        /// <summary>Region an armour piece protects.</summary>
        public static BodyRegion RegionFor(CoreCfg.ArmorSlot slot)
        {
            switch (slot)
            {
                case CoreCfg.ArmorSlot.Helmet: return BodyRegion.Head;
                case CoreCfg.ArmorSlot.Leg: return BodyRegion.LowerBody;
                default: return BodyRegion.UpperBody;
            }
        }

        /// <summary>
        /// The prototype's definition of a critical hit: an unarmoured head shot from a
        /// direct hit. There is no IsCritical field on DamageResult (frozen contract), so
        /// feedback derives it here and everyone agrees on the same rule.
        /// Explosions never produce criticals - REQ-089.
        /// </summary>
        public static bool IsCritical(in DamageResult result, DamageSourceKind source)
        {
            return source != DamageSourceKind.Explosion
                   && result.Region == BodyRegion.Head
                   && !result.WasArmored
                   && result.HealthDamage > 0f;
        }
    }
}
