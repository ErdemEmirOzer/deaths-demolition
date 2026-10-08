using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// WP-076 B6 §5.2 · a region volume authored in a world scene (S-05's v6 builder): a Room (R1–R7), a Gate for each
    /// room exit or chokepoint throat (roomId = the room it opens), a Bridge / Corridor. It registers with
    /// <see cref="RegionNavProfile"/> while enabled; once any scene volume exists the profile uses the scene's volumes
    /// instead of its own World_AP005 list. Centre = this transform; size is a world-axis box (XZ), or polygonXZ (world
    /// XZ, ≥ 3 points) when set. Values are the NavRegionVolume fields (capacity 0 = never queue, costMultiplier ≥ 1).
    /// </summary>
    public sealed class NavRegionMarker : MonoBehaviour
    {
        [SerializeField] private string _id = "";
        [SerializeField] private NavRegionKind _kind = NavRegionKind.Room;
        [SerializeField] private Vector3 _size = new Vector3(8f, 6f, 8f);
        [SerializeField] private Vector2[] _polygonXZ = new Vector2[0];
        [SerializeField] [Range(1f, 4f)] private float _costMultiplier = 1f;
        [SerializeField] [Range(0, 40)] private int _capacity = 0;
        [SerializeField] private bool _hasAlternative = false;
        [SerializeField] private Vector3 _alternative = Vector3.zero;
        [SerializeField] private string _roomId = "";
        [SerializeField] private string _areaName = "";

        private NavRegionVolume _volume;

        public NavRegionVolume Volume => _volume;

        /// <summary>Builder / tests: set every field at once (then re-registers if enabled).</summary>
        public void Configure(string id, NavRegionKind kind, Vector3 size, Vector2[] polygonXZ, float costMultiplier, int capacity,
            bool hasAlternative, Vector3 alternative, string roomId, string areaName = "")
        {
            _id = id; _kind = kind; _size = size; _polygonXZ = polygonXZ ?? new Vector2[0]; _costMultiplier = Mathf.Max(1f, costMultiplier);
            _capacity = Mathf.Max(0, capacity); _hasAlternative = hasAlternative; _alternative = alternative; _roomId = roomId ?? ""; _areaName = areaName ?? "";
            if (Application.isPlaying && isActiveAndEnabled) { Unregister(); Register(); }
        }

        private void OnEnable() => Register();
        private void OnDisable() => Unregister();

        /// <summary>Registers this volume with the profile (OnEnable in play; tests / edit-time callers call it directly).</summary>
        public void Register()
        {
            Unregister();
            _volume = CreateVolume();
            RegionNavProfile.RegisterSceneVolume(_volume);
        }

        /// <summary>WP-093 · The volume this marker describes, NOT registered (readers that must not touch the registry: the
        /// vegetation probe, which also runs in edit mode where OnEnable never registered anything).</summary>
        public NavRegionVolume CreateVolume()
        {
            return new NavRegionVolume
            {
                id = _id, kind = _kind, center = transform.position, size = _size, costMultiplier = Mathf.Max(1f, _costMultiplier),
                capacity = Mathf.Max(0, _capacity), hasAlternative = _hasAlternative, alternative = _alternative, areaName = _areaName,
                roomId = _roomId, polygonXZ = _polygonXZ != null && _polygonXZ.Length >= 3 ? _polygonXZ : null
            };
        }

        public void Unregister()
        {
            if (_volume != null) RegionNavProfile.UnregisterSceneVolume(_volume);
            _volume = null;
        }
    }
}
