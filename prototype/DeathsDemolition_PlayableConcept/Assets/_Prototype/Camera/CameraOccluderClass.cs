using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>WP-070 (direction doc E-20): what kind of CameraOccluder an object is.</summary>
    public enum CameraOccluderKind
    {
        /// <summary>Walls, fences, screens: fade in top-down and third person.</summary>
        Wall = 0,

        /// <summary>Roofs, eaves, canopies: fade in top-down only (TP looks under them).</summary>
        Roof = 1
    }

    /// <summary>
    /// WP-070 · Optional marker on a CameraOccluder object (or a parent of its colliders).
    /// S-05 (WP-074, E-20) tags roofs; an untagged occluder counts as <see cref="CameraOccluderKind.Wall"/>.
    /// Read by <see cref="CameraOcclusionFader"/> under <see cref="CameraOccluderPolicy.FadeCameraLine"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraOccluderClass : MonoBehaviour
    {
        [SerializeField] private CameraOccluderKind _kind = CameraOccluderKind.Wall;

        public CameraOccluderKind Kind
        {
            get { return _kind; }
            set { _kind = value; }
        }

        /// <summary>Kind of the occluder owning <paramref name="collider"/> (Wall when unmarked).</summary>
        public static CameraOccluderKind Of(Collider collider)
        {
            CameraOccluderClass marker = collider != null ? collider.GetComponentInParent<CameraOccluderClass>() : null;
            return marker != null ? marker.Kind : CameraOccluderKind.Wall;
        }
    }
}
