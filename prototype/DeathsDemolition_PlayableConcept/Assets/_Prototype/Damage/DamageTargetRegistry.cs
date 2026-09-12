using System.Collections.Generic;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Damage
{
    // =====================================================================================
    // CROSS-TASK CONTRACT - area and multi-target queries for G-03.
    // -------------------------------------------------------------------------------------
    // The Explosion, Chain and Ricochet upgrade branches all need "which enemies are near
    // this point". Doing that with Physics.OverlapSphere against a crowd of 40 means an
    // allocation or a shared buffer plus a registry lookup per collider, and it picks up
    // three region hitboxes per enemy that then have to be de-duplicated.
    //
    // This registry holds exactly one entry per LIVING enemy, so a query is a linear scan
    // over at most 40 structs with no physics involved and no duplicates by construction.
    // Enemies register on spawn and unregister the instant they die - a corpse playing its
    // death animation is not a valid chain or explosion target.
    // =====================================================================================

    /// <summary>One living, damageable body. Value type; do not cache across frames.</summary>
    public readonly struct DamageTarget
    {
        public readonly Transform Transform;
        public readonly IDamageReceiver Receiver;
        public readonly IExplosionReceiver Explosion;

        /// <summary>World-space position at query time.</summary>
        public readonly Vector3 Position;

        /// <summary>Body radius, so a blast can account for the enemy's size.</summary>
        public readonly float Radius;

        public DamageTarget(Transform transform, IDamageReceiver receiver, IExplosionReceiver explosion,
            Vector3 position, float radius)
        {
            Transform = transform;
            Receiver = receiver;
            Explosion = explosion;
            Position = position;
            Radius = radius;
        }

        public bool IsValid => Transform != null && Receiver != null;
    }

    /// <summary>Every living enemy, for area queries. Registration is spawn/death scoped.</summary>
    public static class DamageTargetRegistry
    {
        private sealed class Entry
        {
            public Transform Transform;
            public IDamageReceiver Receiver;
            public IExplosionReceiver Explosion;
            public float Radius;

            /// <summary>Waist-height offset so a blast at ground level still reads as centred.</summary>
            public float CentreHeight;
        }

        private static readonly List<Entry> s_entries = new List<Entry>(48);

        /// <summary>Living registered bodies.</summary>
        public static int Count => s_entries.Count;

        public static void Register(Transform transform, IDamageReceiver receiver, IExplosionReceiver explosion,
            float radius, float centreHeight)
        {
            if (transform == null || receiver == null)
            {
                return;
            }

            for (int i = 0; i < s_entries.Count; i++)
            {
                if (s_entries[i].Transform == transform)
                {
                    s_entries[i].Receiver = receiver;
                    s_entries[i].Explosion = explosion;
                    s_entries[i].Radius = radius;
                    s_entries[i].CentreHeight = centreHeight;
                    return;
                }
            }

            s_entries.Add(new Entry
            {
                Transform = transform,
                Receiver = receiver,
                Explosion = explosion,
                Radius = radius,
                CentreHeight = centreHeight
            });
        }

        public static void Unregister(Transform transform)
        {
            if (transform == null)
            {
                return;
            }

            for (int i = s_entries.Count - 1; i >= 0; i--)
            {
                if (s_entries[i].Transform == transform || s_entries[i].Transform == null)
                {
                    s_entries.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Living bodies whose centre is within <paramref name="radius"/> of <paramref name="centre"/>.
        /// Clears and refills <paramref name="results"/>; supply a reusable list to stay allocation-free.
        /// </summary>
        public static int Query(Vector3 centre, float radius, List<DamageTarget> results, Transform exclude = null)
        {
            if (results == null)
            {
                return 0;
            }

            results.Clear();

            for (int i = 0; i < s_entries.Count; i++)
            {
                Entry e = s_entries[i];
                if (e.Transform == null || e.Transform == exclude)
                {
                    continue;
                }

                Vector3 position = e.Transform.position;
                position.y += e.CentreHeight;

                // Account for body size: a blast that only clips the edge of a large
                // Executioner still counts, a Runner at the same centre distance may not.
                float reach = radius + e.Radius;
                if ((position - centre).sqrMagnitude > reach * reach)
                {
                    continue;
                }

                results.Add(new DamageTarget(e.Transform, e.Receiver, e.Explosion, position, e.Radius));
            }

            return results.Count;
        }

        /// <summary>Nearest living body to a point, for Chain and Ricochet.</summary>
        public static bool TryFindNearest(Vector3 centre, float radius, Transform exclude, out DamageTarget target)
        {
            target = default;

            float best = radius * radius;
            Entry bestEntry = null;
            Vector3 bestPosition = Vector3.zero;

            for (int i = 0; i < s_entries.Count; i++)
            {
                Entry e = s_entries[i];
                if (e.Transform == null || e.Transform == exclude)
                {
                    continue;
                }

                Vector3 position = e.Transform.position;
                position.y += e.CentreHeight;

                float d = (position - centre).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestEntry = e;
                    bestPosition = position;
                }
            }

            if (bestEntry == null)
            {
                return false;
            }

            target = new DamageTarget(bestEntry.Transform, bestEntry.Receiver, bestEntry.Explosion,
                bestPosition, bestEntry.Radius);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_entries.Clear();
        }
    }

    // WP-006: the former ExplosionUtility.Detonate (a second, undressed blast path with
    // no production caller) was removed. The single canonical blast entry point is
    // Weapons/HitscanEngine.Explode - one application per enemy, one dressing, one budget.
}
