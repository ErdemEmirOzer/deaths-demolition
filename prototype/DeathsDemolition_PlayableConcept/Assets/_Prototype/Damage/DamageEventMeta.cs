using System;
using UnityEngine;
using DD.Prototype.Core.Config;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Damage
{
    /// <summary>Which resolution path a damage event took. Not the same as <see cref="DamageSourceKind"/>:
    /// a Special Weapon may deliver an Explosion-flavoured hit through the DIRECT path.</summary>
    public enum DamageKind
    {
        /// <summary>One region was struck; REQ-082..084 routing.</summary>
        Direct = 0,

        /// <summary>One blast budget split across the three regions; REQ-085..090 routing.</summary>
        Explosion = 1,

        /// <summary>Debug / cleanup kill (F1 panel, round cleanup). Never a weapon.</summary>
        Debug = 2
    }

    /// <summary>
    /// The ten event classes the WP-006 contract asks the feedback, animation and audio
    /// tasks to tell apart. Flags, because one physical hit is several of them at once:
    /// a helmet-breaking direct hit is HelmetArmorHit | ArmorDamaged | ArmorBroken.
    /// Derived from the event payload by <see cref="DamageEventClassifier"/>; no new
    /// static event is raised for any of them.
    /// </summary>
    [Flags]
    public enum DamageEventClass
    {
        None = 0,
        FleshHit = 1 << 0,
        HelmetArmorHit = 1 << 1,
        TorsoArmorHit = 1 << 2,
        LegArmorHit = 1 << 3,
        ArmorDamaged = 1 << 4,
        ArmorBroken = 1 << 5,
        DirectHealthDamage = 1 << 6,
        ExplosionArmorDamage = 1 << 7,
        ExplosionHealthDamage = 1 << 8,
        EnemyDeath = 1 << 9
    }

    /// <summary>
    /// Everything the WP-006 event contract adds on top of the frozen payloads, as one
    /// readonly value: life and transaction identity, the routing kind, the struck slot,
    /// armour durability before/after per slot, health before/after, and the per-region
    /// breakdown of one call. Carried by <see cref="EnemyDamageInfo.Meta"/> and
    /// <see cref="EnemyDeathInfo"/>/<see cref="ArmorBreakInfo"/> through their new
    /// constructor overloads; a producer that uses the old constructors leaves it at
    /// <c>default</c> (every id 0, <see cref="IsPresent"/> false).
    ///
    /// No arrays, no references: passing it by <c>in</c> costs nothing and raising an
    /// event with it allocates nothing.
    /// </summary>
    public readonly struct DamageEventMeta
    {
        public DamageEventMeta(int lifeId, int transactionId, int eventId, DamageKind kind, ArmorSlot slot,
            float healthBefore, float healthAfter,
            float helmetBefore, float helmetAfter, float bodyBefore, float bodyAfter, float legBefore, float legAfter,
            float headHealthDamage, float upperHealthDamage, float lowerHealthDamage,
            float headArmorDamage, float upperArmorDamage, float lowerArmorDamage)
        {
            LifeId = lifeId;
            TransactionId = transactionId;
            EventId = eventId;
            Kind = kind;
            Slot = slot;
            HealthBefore = healthBefore;
            HealthAfter = healthAfter;
            HelmetBefore = helmetBefore;
            HelmetAfter = helmetAfter;
            BodyBefore = bodyBefore;
            BodyAfter = bodyAfter;
            LegBefore = legBefore;
            LegAfter = legAfter;
            HeadHealthDamage = headHealthDamage;
            UpperHealthDamage = upperHealthDamage;
            LowerHealthDamage = lowerHealthDamage;
            HeadArmorDamage = headArmorDamage;
            UpperArmorDamage = upperArmorDamage;
            LowerArmorDamage = lowerArmorDamage;
        }

        /// <summary>Identity of the enemy LIFE (one per pool checkout, <see cref="EnemyLifeIds"/>). 0 = unknown producer.</summary>
        public readonly int LifeId;

        /// <summary>Identity of the weapon-side transaction (one shot / one blast / one external hit, <see cref="DamageTransaction"/>). 0 = outside any transaction (debug kill).</summary>
        public readonly int TransactionId;

        /// <summary>Monotonic identity of this event (<see cref="DamageEventIds"/>). Never repeats within a play session.</summary>
        public readonly int EventId;

        /// <summary>Direct or Explosion routing (or Debug).</summary>
        public readonly DamageKind Kind;

        /// <summary>Direct hit: the armour slot of the struck region (None when the region has no slot). Explosion: None.</summary>
        public readonly ArmorSlot Slot;

        public readonly float HealthBefore;
        public readonly float HealthAfter;

        // Armour durability per slot before and after this call. A slot that was never
        // equipped reads 0/0; a slot that broke in this call reads (x > 0) / 0.
        public readonly float HelmetBefore;
        public readonly float HelmetAfter;
        public readonly float BodyBefore;
        public readonly float BodyAfter;
        public readonly float LegBefore;
        public readonly float LegAfter;

        // Per-region breakdown of this call (explosion: up to three regions; direct: one).
        public readonly float HeadHealthDamage;
        public readonly float UpperHealthDamage;
        public readonly float LowerHealthDamage;
        public readonly float HeadArmorDamage;
        public readonly float UpperArmorDamage;
        public readonly float LowerArmorDamage;

        /// <summary>True when a WP-006 producer filled this in (EventId is never 0 then).</summary>
        public bool IsPresent => EventId != 0;

        /// <summary>Total armour durability removed by this call, all slots.</summary>
        public float ArmorAbsorbed => HeadArmorDamage + UpperArmorDamage + LowerArmorDamage;

        /// <summary>Total health removed by this call, all regions.</summary>
        public float HealthRemoved => HeadHealthDamage + UpperHealthDamage + LowerHealthDamage;

        public float ArmorBefore(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return HelmetBefore;
                case ArmorSlot.Body: return BodyBefore;
                case ArmorSlot.Leg: return LegBefore;
                default: return 0f;
            }
        }

        public float ArmorAfter(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return HelmetAfter;
                case ArmorSlot.Body: return BodyAfter;
                case ArmorSlot.Leg: return LegAfter;
                default: return 0f;
            }
        }

        public float HealthDamageIn(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return HeadHealthDamage;
                case BodyRegion.LowerBody: return LowerHealthDamage;
                default: return UpperHealthDamage;
            }
        }

        public float ArmorDamageIn(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return HeadArmorDamage;
                case BodyRegion.LowerBody: return LowerArmorDamage;
                default: return UpperArmorDamage;
            }
        }
    }

    /// <summary>
    /// One id per weapon-side damage transaction: one <c>HitscanEngine.Fire</c> (a
    /// bullet or one pellet, with its pierce/chain/ricochet/explosion branches), one
    /// standalone blast, one Special Weapon external hit. Ambient and single-threaded,
    /// like everything else in the damage path; <see cref="Begin"/>/<see cref="End"/>
    /// are not re-entrant on purpose - a nested Begin is a bug and is counted.
    ///
    /// The enemy stamps <see cref="Current"/> into its events and refuses a SECOND
    /// application inside the same transaction (see ZombieEnemy), which is the
    /// "same event / ticket processed twice must not break, reward or dress twice"
    /// rule of the contract, enforced on the receiving side as well as in the engine.
    /// </summary>
    public static class DamageTransaction
    {
        private static int s_last;
        private static int s_current;
        private static int s_nestedBegins;

        /// <summary>Id of the transaction in flight, or 0 between transactions.</summary>
        public static int Current => s_current;

        /// <summary>Last id handed out. Test readout.</summary>
        public static int Last => s_last;

        /// <summary>How many times Begin was called while another transaction was still open. Should stay 0.</summary>
        public static int NestedBegins => s_nestedBegins;

        public static int Begin()
        {
            if (s_current != 0)
            {
                s_nestedBegins++;
            }

            s_last++;
            if (s_last <= 0)
            {
                s_last = 1;
            }

            s_current = s_last;
            return s_current;
        }

        public static void End()
        {
            s_current = 0;
        }

        /// <summary>WP-069 (ADR-069-1): hand out a new id WITHOUT opening it (a projectile's id, resumed per step).</summary>
        public static int NextId()
        {
            s_last++;
            if (s_last <= 0)
            {
                s_last = 1;
            }

            return s_last;
        }

        /// <summary>
        /// WP-069 (ADR-069-1): continue a transaction handed out earlier by <see cref="Begin"/>.
        /// A projectile's flight spans frames; each step's hit resolution resumes the id the
        /// projectile was born with, so its penetration, chain, ricochet and blast stay ONE
        /// transaction (the enemy refuses a second application inside it). Close with <see cref="End"/>.
        /// </summary>
        public static void Resume(int id)
        {
            if (s_current != 0)
            {
                s_nestedBegins++;
            }

            s_current = id > 0 ? id : Begin();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_last = 0;
            s_current = 0;
            s_nestedBegins = 0;
        }
    }

    /// <summary>Monotonic id for every damage event a producer raises. Never 0 once handed out.</summary>
    public static class DamageEventIds
    {
        private static int s_last;

        public static int Last => s_last;

        public static int Next()
        {
            s_last++;
            if (s_last <= 0)
            {
                s_last = 1;
            }

            return s_last;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_last = 0;
        }
    }

    /// <summary>Helpers over the frozen <see cref="DamageResult"/> that the contract file may not grow.</summary>
    public static class DamageResults
    {
        /// <summary>
        /// True when the call changed nothing: no health removed, no armour removed, no
        /// break, no kill. A dead receiver answers every hit this way, and the weapon
        /// layer must not dress, count or reward an empty result.
        /// </summary>
        public static bool IsEmpty(in DamageResult result)
        {
            return result.HealthDamage <= 0f && result.ArmorDamage <= 0f && !result.ArmorBroke && !result.Killed;
        }
    }

    /// <summary>
    /// Turns one <see cref="EnemyDamageInfo"/> into the contract's event classes. Pure,
    /// allocation-free, and the single definition the feedback/animation/audio tasks share.
    /// </summary>
    public static class DamageEventClassifier
    {
        public static DamageEventClass Classify(in EnemyDamageInfo info)
        {
            DamageResult r = info.Result;
            DamageEventMeta m = info.Meta;
            bool explosion = m.IsPresent ? m.Kind == DamageKind.Explosion : info.Source == DamageSourceKind.Explosion;

            DamageEventClass c = DamageEventClass.None;

            if (r.ArmorDamage > 0f)
            {
                c |= DamageEventClass.ArmorDamaged;
                c |= explosion ? DamageEventClass.ExplosionArmorDamage : DamageEventClass.None;

                if (m.IsPresent)
                {
                    if (m.HeadArmorDamage > 0f) c |= DamageEventClass.HelmetArmorHit;
                    if (m.UpperArmorDamage > 0f) c |= DamageEventClass.TorsoArmorHit;
                    if (m.LowerArmorDamage > 0f) c |= DamageEventClass.LegArmorHit;
                }
                else
                {
                    c |= SlotClass(DamageRegions.SlotFor(r.Region));
                }
            }

            if (r.ArmorBroke)
            {
                c |= DamageEventClass.ArmorBroken;
            }

            if (r.HealthDamage > 0f)
            {
                c |= explosion ? DamageEventClass.ExplosionHealthDamage : DamageEventClass.DirectHealthDamage;

                if (!explosion)
                {
                    c |= DamageEventClass.FleshHit;
                }
            }

            if (r.Killed)
            {
                c |= DamageEventClass.EnemyDeath;
            }

            return c;
        }

        private static DamageEventClass SlotClass(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return DamageEventClass.HelmetArmorHit;
                case ArmorSlot.Body: return DamageEventClass.TorsoArmorHit;
                case ArmorSlot.Leg: return DamageEventClass.LegArmorHit;
                default: return DamageEventClass.None;
            }
        }
    }
}
