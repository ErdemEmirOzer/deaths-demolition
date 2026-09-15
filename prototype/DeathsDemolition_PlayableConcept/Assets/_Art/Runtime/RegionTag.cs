using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-014 · The AP-005 region an imported instance belongs to (placement.json
    /// <c>region</c>): center, north, east, south, west. Data only — the atmosphere seam
    /// and the tests read it; nothing runs per instance (ADR-014-1).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RegionTag : MonoBehaviour
    {
        [SerializeField] private string _region = string.Empty;
        [SerializeField] private string _role = string.Empty;
        [SerializeField] private string _navMesh = string.Empty;
        [SerializeField] private string _instanceId = string.Empty;

        public string Region => _region;
        public string Role => _role;
        public string NavMesh => _navMesh;
        public string InstanceId => _instanceId;

        public void Set(string instanceId, string region, string role, string navMesh)
        {
            _instanceId = instanceId;
            _region = region;
            _role = role;
            _navMesh = navMesh;
        }
    }
}
