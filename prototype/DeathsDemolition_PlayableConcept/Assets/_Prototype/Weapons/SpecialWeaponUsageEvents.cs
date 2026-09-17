using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>What a Special Weapon USAGE event marks (WP-056).</summary>
    public enum SpecialUsageKind
    {
        /// <summary>The weapon is in hand (after pickup); the hold clip / HUD slot / loop cue start.</summary>
        Activated = 0,

        /// <summary>One trigger pull resolved: the bell rang, the dart left, the jewel was thrown.</summary>
        Fired = 1,

        /// <summary>A persistent field appeared: a wave front (bell), a seal on a body (loom), a well on the ground (jewel).</summary>
        FieldSpawned = 2,

        /// <summary>A field finished: the wave reached its rim, the web collapsed or its last seal expired, the well imploded.</summary>
        FieldEnded = 3,

        /// <summary>The weapon left the hand (timer, cancel); the classic weapon is back.</summary>
        Deactivated = 4,

        /// <summary>
        /// A field touched ONE body (S-03 reaction per target): the wave front crossed it, a pull tick
        /// moved it, a talisman sealed it. Carries the target, the force on the DamageForce scale,
        /// the falloff at that distance and the damage transaction the hit belongs to.
        /// </summary>
        Hit = 5
    }

    /// <summary>
    /// WP-056 · one Special Weapon usage event as data for the consumers outside the weapon:
    /// S-03 enemy reactions (stumble at a wave front, sealed pose, pull lean), S-07 sw_* sheets,
    /// S-08 cues. <see cref="UsageId"/> is the row's family from <see cref="SpecialWeaponUsageIds"/>
    /// (fire family for Fired, field family for the field kinds). Positions are world space;
    /// <see cref="Radius"/> / <see cref="Seconds"/> / <see cref="Count"/> mean what the kind
    /// says (wave radius + travel, seal seconds + seal count, well radius + seconds, ended fields'
    /// crossed / collapsed / pulled count). A readonly struct on a static event: no allocation
    /// per publish (ADR-037-1).
    /// </summary>
    public readonly struct SpecialUsageEvent
    {
        public readonly SpecialWeaponId Id;
        public readonly SpecialUsageKind Kind;
        public readonly string UsageId;
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly float Radius;
        public readonly float Seconds;
        public readonly int Count;

        // ---- Hit kind only (default otherwise)
        /// <summary>The body the field touched (root / id / height from the damage registry); <c>IsValid</c> false on non-Hit kinds.</summary>
        public readonly DamageTarget Target;
        /// <summary>Reaction force on the DamageForce scale (1 = one rifle bullet); the wave's impact force, the well's pull force, 0 for a seal.</summary>
        public readonly float Force;
        /// <summary>Strength at the body's distance, 0..1 (the wave's rim falloff; 1 for pull / seal).</summary>
        public readonly float Falloff01;
        /// <summary>The damage transaction the hit was applied in (<c>DamageTransaction.Current</c> at publish; 0 outside one).</summary>
        public readonly int TransactionId;

        public SpecialUsageEvent(SpecialWeaponId id, SpecialUsageKind kind, string usageId, Vector3 origin, Vector3 direction, float radius, float seconds, int count)
            : this(id, kind, usageId, origin, direction, radius, seconds, count, default, 0f, 1f, 0)
        {
        }

        public SpecialUsageEvent(SpecialWeaponId id, SpecialUsageKind kind, string usageId, Vector3 origin, Vector3 direction, float radius, float seconds, int count,
            DamageTarget target, float force, float falloff01, int transactionId)
        {
            Id = id;
            Kind = kind;
            UsageId = usageId;
            Origin = origin;
            Direction = direction;
            Radius = radius;
            Seconds = seconds;
            Count = count;
            Target = target;
            Force = force;
            Falloff01 = falloff01;
            TransactionId = transactionId;
        }
    }

    /// <summary>The usage bus (WP-056). Read-only for consumers; published by the weapon layer only.</summary>
    public static class SpecialWeaponUsageEvents
    {
        public static event Action<SpecialUsageEvent> Usage;

        /// <summary>Events published since the last reset (tests / probes).</summary>
        public static int Count { get; private set; }

        /// <summary>The last event published (tests).</summary>
        public static SpecialUsageEvent Last { get; private set; }

        public static void Publish(in SpecialUsageEvent e)
        {
            Count++;
            Last = e;
            Usage?.Invoke(e);
        }

        public static void Publish(SpecialWeaponId id, SpecialUsageKind kind, string usageId, Vector3 origin, Vector3 direction, float radius, float seconds, int count)
        {
            Publish(new SpecialUsageEvent(id, kind, usageId, origin, direction, radius, seconds, count));
        }

        /// <summary>A targeted Hit (WP-056, C-00 / S-03 handshake): origin = the hit point, direction = the reaction direction.</summary>
        public static void PublishHit(SpecialWeaponId id, string usageId, in DamageTarget target, Vector3 point, Vector3 direction, float force, float falloff01, float seconds)
        {
            Publish(new SpecialUsageEvent(id, SpecialUsageKind.Hit, usageId, point, direction, 0f, seconds, 1,
                target, force, falloff01, DD.Prototype.Damage.DamageTransaction.Current));
        }

        /// <summary>Tests: drop subscribers and counters.</summary>
        public static void ResetForTests()
        {
            Usage = null;
            Count = 0;
            Last = default;
        }
    }
}
