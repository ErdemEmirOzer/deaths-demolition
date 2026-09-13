using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-007 (AP-4): "is this drop position valid and reachable?" as an injectable
    /// question, so the director's placement policy can be tested without a baked NavMesh
    /// (TM-14..18) and the shipped answer stays exactly the NavMesh sample it always was.
    /// </summary>
    public interface ISpecialDropValidator
    {
        /// <summary>True when a body can stand at (or within the sample tolerance of) this point.</summary>
        bool IsReachable(Vector3 drop);

        /// <summary>
        /// Project a free-form candidate (no authored point) onto reachable ground within
        /// <paramref name="maxDistance"/>. False when there is no reachable ground there -
        /// the caller must then NOT drop, rather than drop at the raw candidate.
        /// </summary>
        bool TryProject(Vector3 candidate, float maxDistance, out Vector3 projected);
    }

    /// <summary>The shipped validator: <see cref="NavMesh.SamplePosition"/>, all areas. Stateless, one shared instance.</summary>
    public sealed class NavMeshDropValidator : ISpecialDropValidator
    {
        public static readonly NavMeshDropValidator Instance = new NavMeshDropValidator();

        private NavMeshDropValidator()
        {
        }

        public bool IsReachable(Vector3 drop)
        {
            return NavMesh.SamplePosition(drop, out _, SpecialWeaponDirector.NavSampleRadius, NavMesh.AllAreas);
        }

        public bool TryProject(Vector3 candidate, float maxDistance, out Vector3 projected)
        {
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                projected = hit.position;
                return true;
            }

            projected = candidate;
            return false;
        }
    }
}
