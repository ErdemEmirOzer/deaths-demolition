using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// Scene-side handle for one spawn point - THE binding surface for task G-06.
    ///
    /// HOW TO USE IT IN THE MAP SCENE
    /// ------------------------------
    ///   1. Create an empty GameObject where the entrance is, e.g. "Spawn_N_PalaceGateWest".
    ///   2. Add this component.
    ///   3. Type one of the twelve authored ids into Point Id (see
    ///      <see cref="DefaultSpawnPointLayout"/>: N_PalaceGateWest, N_PalaceGateEast,
    ///      NE_StonePathCorner, E_SakuraGroveNorth, E_SakuraGroveSouth, SE_PondApproach,
    ///      S_BridgeSouthEnd, S_ShorePathWest, SW_BambooEntry, W_BambooCorridorSouth,
    ///      W_BambooCorridorNorth, NW_ServiceGate).
    ///   4. Put it on walkable ground inside the NavMesh. That is the whole binding.
    ///
    /// Region, base weight and enabled state stay in the layout unless explicitly
    /// overridden here. Ids that never get a marker keep their authored coordinates, so
    /// the spawn system also works on a scene with no markers at all.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPointMarker : MonoBehaviour
    {
        [Tooltip("Must match an Id from the authored layout, or it is added as a new point.")]
        [SerializeField] private string _pointId = "";

        [Tooltip("Only used when Override Region is on.")]
        [SerializeField] private RingRegion _region = RingRegion.North;

        [SerializeField] private bool _overrideRegion = false;

        [Tooltip("Negative means: keep the layout's designer base weight.")]
        [SerializeField] private float _baseWeightOverride = -1f;

        [Tooltip("Turn off to disable this entrance without deleting it.")]
        [SerializeField] private bool _pointEnabled = true;

        [Header("Gizmo")]
        [SerializeField] private float _gizmoRadius = 1.2f;

        [Tooltip("WP-076 B6: Edge (the ±55 ring) or Inner (a v6 mid gate behind cover; inner-gate spawn rules).")]
        [SerializeField] private SpawnPointKind _kind = SpawnPointKind.Edge;
        public SpawnPointKind Kind => _kind;

        public string PointId => _pointId;
        public RingRegion Region => _region;
        public bool OverrideRegion => _overrideRegion;
        public float BaseWeightOverride => _baseWeightOverride;
        public bool PointEnabled => _pointEnabled;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_pointId))
            {
                NavigationDiagnostics.WarnThrottled(
                    "spawn.marker.empty", 5f,
                    "[Spawn] SpawnPointMarker on '" + name + "' has an empty Point Id and was ignored.");
                return;
            }

            SpawnPointRegistry.RegisterMarker(this);
        }

        private void OnDisable()
        {
            SpawnPointRegistry.UnregisterMarker(this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = RegionColor(_region);
            Gizmos.DrawWireSphere(transform.position, _gizmoRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2.5f);

            Vector3 toCenter = RingRegionMap.MapCenter - transform.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.01f)
            {
                Gizmos.DrawRay(transform.position + Vector3.up * 0.2f, toCenter.normalized * 2.5f);
            }
        }

        private static Color RegionColor(RingRegion region)
        {
            switch (region)
            {
                case RingRegion.North: return new Color(0.9f, 0.85f, 0.7f, 1f);
                case RingRegion.East: return new Color(1f, 0.6f, 0.8f, 1f);
                case RingRegion.South: return new Color(0.5f, 0.8f, 1f, 1f);
                case RingRegion.West: return new Color(0.6f, 0.9f, 0.55f, 1f);
                default: return new Color(0.8f, 0.8f, 0.8f, 1f);
            }
        }
    }
}
