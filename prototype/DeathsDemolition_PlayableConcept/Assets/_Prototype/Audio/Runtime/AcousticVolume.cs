using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-097 K1 (03 §4.1) · an enterable building's acoustic space, placed on a prefab (WP-092 interiors) or a scene object: an
    /// oriented box (<see cref="Size"/> in this transform's local space, centred on it) that says "inside here the shot rings as
    /// <see cref="Enclosure"/>". First in <see cref="AcousticSpaceResolver"/>'s order (volume → S-05 JSON map → probe guess → Open),
    /// so it holds in every camera mode. Registers itself while enabled (<see cref="AcousticVolumes"/>, at most 64); the
    /// containment test is a point transform, no physics, no allocation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AcousticVolume : MonoBehaviour
    {
        [SerializeField] private AcousticSpace _enclosure = AcousticSpace.Hall;
        [SerializeField] private Vector3 _size = new Vector3(10f, 4f, 10f);

        public AcousticSpace Enclosure
        {
            get => _enclosure;
            set => _enclosure = value;
        }

        /// <summary>Box size in local units (the transform's scale applies on top).</summary>
        public Vector3 Size
        {
            get => _size;
            set => _size = new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        /// <summary>True when <paramref name="worldPoint"/> is inside the box.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= _size.x * 0.5f && Mathf.Abs(local.y) <= _size.y * 0.5f && Mathf.Abs(local.z) <= _size.z * 0.5f;
        }

        private void OnEnable() => AcousticVolumes.Register(this);

        private void OnDisable() => AcousticVolumes.Unregister(this);

        private void OnValidate()
        {
            _size = new Vector3(Mathf.Abs(_size.x), Mathf.Abs(_size.y), Mathf.Abs(_size.z));
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = _enclosure == AcousticSpace.Corridor ? new Color(0.2f, 0.8f, 1f, 0.6f) : new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }

    /// <summary>WP-097 K1: the enabled <see cref="AcousticVolume"/>s (fixed capacity 64, allocation-free lookup).</summary>
    public static class AcousticVolumes
    {
        public const int Capacity = 64;

        private static readonly AcousticVolume[] s_items = new AcousticVolume[Capacity];
        private static int s_count;
        private static bool s_overflowWarned;

        public static int Count => s_count;

        /// <summary>Adds a volume (idempotent). False when the 64 slots are full — the volume is then ignored (one warning).</summary>
        public static bool Register(AcousticVolume volume)
        {
            if (volume == null)
            {
                return false;
            }

            for (int i = 0; i < s_count; i++)
            {
                if (s_items[i] == volume) return true;
            }

            if (s_count >= Capacity)
            {
                if (!s_overflowWarned)
                {
                    s_overflowWarned = true;
                    Debug.LogWarning("[Audio] AcousticVolumes: more than " + Capacity + " enabled volumes; the extra are ignored.");
                }

                return false;
            }

            s_items[s_count++] = volume;
            return true;
        }

        public static void Unregister(AcousticVolume volume)
        {
            for (int i = 0; i < s_count; i++)
            {
                if (s_items[i] != volume) continue;
                s_items[i] = s_items[s_count - 1];
                s_items[s_count - 1] = null;
                s_count--;
                return;
            }
        }

        /// <summary>The first enabled volume containing <paramref name="point"/> answers. False = no volume here.</summary>
        public static bool TryResolve(Vector3 point, out AcousticSpace space)
        {
            for (int i = 0; i < s_count; i++)
            {
                AcousticVolume v = s_items[i];
                if (v != null && v.Contains(point))
                {
                    space = v.Enclosure;
                    return true;
                }
            }

            space = AcousticSpace.Open;
            return false;
        }

        /// <summary>Domain-reload-off safe: no volume survives into the next play session.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            System.Array.Clear(s_items, 0, s_items.Length);
            s_count = 0;
            s_overflowWarned = false;
        }
    }
}
