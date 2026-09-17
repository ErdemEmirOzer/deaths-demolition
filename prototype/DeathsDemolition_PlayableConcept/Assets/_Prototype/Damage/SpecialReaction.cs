using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Damage
{
    /// <summary>WP-055 · what a Special Weapon does to a body beyond its damage (the damage stays on the WP-007 hit path).</summary>
    public enum SpecialReactionKind
    {
        /// <summary>Kagura Bell front: a shove away from the origin plus a short daze (tier-resisted).</summary>
        Push = 0,
        /// <summary>Ofuda Loom seal: the body is netted — slowed or rooted for the seal's life (tier-scaled).</summary>
        Web = 1,
        /// <summary>Tide Jewel well: one pull tick toward the core (the Ultra is not moved, only slowed).</summary>
        Pull = 2,
    }

    /// <summary>
    /// WP-055 · One Special Weapon reaction request. The weapon states the physical intent (direction,
    /// force, duration); the receiver resolves it against its tier (resistance, root vs slow, pull vs
    /// hold) and raises the enemy beat. Readonly struct by value.
    /// </summary>
    public readonly struct SpecialReaction
    {
        public SpecialReaction(SpecialReactionKind kind, SpecialWeaponId source, Vector3 direction, float force, float seconds, int transactionId)
        {
            Kind = kind;
            Source = source;
            Direction = direction;
            Force = force;
            Seconds = seconds;
            TransactionId = transactionId;
        }

        public SpecialReactionKind Kind { get; }
        public SpecialWeaponId Source { get; }
        /// <summary>Push: away from the origin; Pull: toward the core (flat, unit).</summary>
        public Vector3 Direction { get; }
        /// <summary>The weapon's ImpactForce (WP-007 values) — the receiver scales it into a velocity.</summary>
        public float Force { get; }
        /// <summary>Web: the seal's remaining life; Pull: the tick interval; Push: 0 (the receiver's own daze).</summary>
        public float Seconds { get; }
        /// <summary>The attack transaction the request belongs to (a body reacts once per transaction).</summary>
        public int TransactionId { get; }
    }

    /// <summary>
    /// WP-055 · CROSS-TASK CONTRACT: implemented next to <see cref="IDamageReceiver"/> by the pooled enemy;
    /// a Special Weapon asks for the reaction through <c>DamageTarget.Receiver as ISpecialReactionReceiver</c>
    /// right after its WP-007 damage call. Weapons never learn the enemy type.
    /// </summary>
    public interface ISpecialReactionReceiver
    {
        /// <summary>Apply (tier-resolved) or refuse (dead, rising, same transaction). Returns true when the body reacted.</summary>
        bool ApplySpecialReaction(in SpecialReaction reaction);
    }
}
