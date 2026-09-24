using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-074 B1 · The playable square of the loaded world, readable at runtime (S-10 QaRunBot ring margin / safe-spot grid,
    /// spawn tooling): the invisible ring's inner face is at ±<see cref="HalfExtent"/> m around the origin and the walkable
    /// NavMesh stops <see cref="NavMeshInset"/> m inside it (WP-038R). Written by WorldBoundaryBuilder on the World Boundary
    /// object (AP005 ±45, World v5 greybox ±55); <see cref="Active"/> is the enabled one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldBounds : MonoBehaviour
    {
        [SerializeField] private float _halfExtent = 45f;
        [SerializeField] private float _navMeshInset = 0.5f;

        public float HalfExtent => _halfExtent;
        public float NavMeshInset => _navMeshInset;
        public static WorldBounds Active { get; private set; }

        public void Configure(float halfExtent, float navMeshInset)
        {
            _halfExtent = halfExtent;
            _navMeshInset = navMeshInset;
        }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }
    }
}
