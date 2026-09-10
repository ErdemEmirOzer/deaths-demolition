using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// An authored place a Special Weapon may land (REQ-054).
    ///
    /// The world task places these. They self-register, so the director never searches
    /// the scene and adding one is a pure level-editing act with no code change.
    ///
    /// The request wants the drop to be reachable but to pull the player off their safe
    /// lap of the ring route, so a point carries a designer weight and a flag saying
    /// whether it sits off the main loop. Both feed the pick.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpecialWeaponSpawnPoint : MonoBehaviour
    {
        private static readonly List<SpecialWeaponSpawnPoint> s_points = new List<SpecialWeaponSpawnPoint>(16);

        [Tooltip("Relative chance this point is chosen. 0 disables it.")]
        [SerializeField] private float _weight = 1f;

        [Tooltip("True for points that force a detour off the safe ring route. Weighted up.")]
        [SerializeField] private bool _offRoute = false;

        [Tooltip("Height above the point the weapon floats at.")]
        [SerializeField] private float _hoverHeight = 0.8f;

        /// <summary>Every registered point. Never null, may be empty.</summary>
        public static IReadOnlyList<SpecialWeaponSpawnPoint> All => s_points;

        public float Weight => Mathf.Max(0f, _weight);

        public bool OffRoute => _offRoute;

        /// <summary>Where the pickup actually sits.</summary>
        public Vector3 DropPosition => transform.position + Vector3.up * _hoverHeight;

        private void OnEnable()
        {
            if (!s_points.Contains(this))
            {
                s_points.Add(this);
            }
        }

        private void OnDisable()
        {
            s_points.Remove(this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _offRoute ? new Color(1f, 0.55f, 0.2f, 0.9f) : new Color(1f, 0.85f, 0.35f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, 0.6f);
            Gizmos.DrawLine(transform.position, DropPosition);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_points.Clear();
        }
    }
}
