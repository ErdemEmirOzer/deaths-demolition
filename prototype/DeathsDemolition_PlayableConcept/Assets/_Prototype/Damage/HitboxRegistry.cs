using System.Collections.Generic;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Damage
{
    // =====================================================================================
    // CROSS-TASK CONTRACT - this is how G-03 turns a raycast hit into damage.
    // -------------------------------------------------------------------------------------
    //     var hit = new DamageHit { Amount = 11f, Point = rh.point, Direction = dir,
    //                               ImpactForce = DamageForce.RifleBullet,
    //                               Source = DamageSourceKind.Bullet };
    //     if (DamageQuery.TryApply(rh.collider, ref hit, out DamageResult result)) { ... }
    //
    // TryApply fills hit.Region for you from the collider that was struck; whatever the
    // caller put in Region is overwritten. Everything else is the caller's.
    //
    // WHY A REGISTRY AND NOT GetComponent
    // Region colliders are created once per pooled instance and live as long as the pool.
    // Registration therefore happens once, at Awake, and a hit is a single dictionary
    // lookup on the collider's instance id. No GetComponent, no GetComponentInParent and
    // no allocation on the bullet path, which is the rule for a 40-enemy crowd.
    //
    // WHY THE COLLIDERS ARE TRIGGERS
    // Region hitboxes sit on the EnemyHitbox layer and are marked isTrigger so they can
    // never push the player, the environment or each other. Physics.queriesHitTriggers is
    // true by default, so raycasts still find them - but a caller that passes
    // QueryTriggerInteraction.Ignore will silently hit nothing. Use
    // DamageQuery.HitboxTriggerMode when you raycast.
    // =====================================================================================

    /// <summary>Collider instance id -> damage receiver and body region.</summary>
    public static class HitboxRegistry
    {
        private struct Entry
        {
            public IDamageReceiver Receiver;
            public IExplosionReceiver Explosion;
            public BodyRegion Region;
            public Transform Owner;
        }

        private static readonly Dictionary<int, Entry> s_byCollider = new Dictionary<int, Entry>(256);

        public static int Count => s_byCollider.Count;

        /// <summary>
        /// Bind a collider to a receiver. Call once, when the collider is created.
        /// Registering the same collider twice overwrites, it does not duplicate.
        /// </summary>
        public static void Register(Collider collider, IDamageReceiver receiver, IExplosionReceiver explosion,
            BodyRegion region, Transform owner)
        {
            if (collider == null || receiver == null)
            {
                return;
            }

            s_byCollider[collider.GetInstanceID()] = new Entry
            {
                Receiver = receiver,
                Explosion = explosion,
                Region = region,
                Owner = owner
            };
        }

        public static void Unregister(Collider collider)
        {
            if (collider == null)
            {
                return;
            }

            s_byCollider.Remove(collider.GetInstanceID());
        }

        /// <summary>Cheap id-only removal, for OnDestroy paths where the collider is already gone.</summary>
        public static void Unregister(int colliderInstanceId)
        {
            s_byCollider.Remove(colliderInstanceId);
        }

        public static bool TryResolve(Collider collider, out IDamageReceiver receiver, out BodyRegion region)
        {
            receiver = null;
            region = BodyRegion.UpperBody;

            if (collider == null || !s_byCollider.TryGetValue(collider.GetInstanceID(), out Entry entry))
            {
                return false;
            }

            receiver = entry.Receiver;
            region = entry.Region;
            return receiver != null;
        }

        public static bool TryResolveExplosion(Collider collider, out IExplosionReceiver receiver)
        {
            receiver = null;
            if (collider == null || !s_byCollider.TryGetValue(collider.GetInstanceID(), out Entry entry))
            {
                return false;
            }

            receiver = entry.Explosion;
            return receiver != null;
        }

        /// <summary>
        /// Root transform behind a collider. Lets piercing and ricochet skip every other
        /// collider that belongs to the same enemy without a GetComponentInParent walk.
        /// </summary>
        public static bool TryResolveOwner(Collider collider, out Transform owner)
        {
            owner = null;
            if (collider == null || !s_byCollider.TryGetValue(collider.GetInstanceID(), out Entry entry))
            {
                return false;
            }

            owner = entry.Owner;
            return owner != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_byCollider.Clear();
        }
    }

    /// <summary>Convenience façade over <see cref="HitboxRegistry"/> for weapon code.</summary>
    public static class DamageQuery
    {
        /// <summary>
        /// Pass this to every Physics query that is meant to find enemy hitboxes.
        /// Region hitboxes are triggers; Ignore would make weapons hit nothing.
        /// </summary>
        public const QueryTriggerInteraction HitboxTriggerMode = QueryTriggerInteraction.Collide;

        /// <summary>
        /// Resolve the collider, stamp the correct region into the hit and apply it.
        /// Returns false when the collider is not an enemy hitbox (world geometry, etc.).
        /// </summary>
        public static bool TryApply(Collider collider, ref DamageHit hit, out DamageResult result)
        {
            result = default;

            if (!HitboxRegistry.TryResolve(collider, out IDamageReceiver receiver, out BodyRegion region))
            {
                return false;
            }

            hit.Region = region;
            result = receiver.ApplyHit(in hit);
            return true;
        }

        /// <summary>Region a collider represents, without applying anything. For hit markers and decals.</summary>
        public static bool TryResolveRegion(Collider collider, out BodyRegion region)
        {
            return HitboxRegistry.TryResolve(collider, out _, out region);
        }
    }
}
