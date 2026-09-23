using DD.Prototype.PlayerSystem;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Art
{
    /// <summary>
    /// WP-038M · Safety net under the AP-005 world: a player whose feet drop below <see cref="FallY"/> is put back on the nearest
    /// NavMesh point (sampled at ground level from the fall position clamped into the playable square), with no damage. The
    /// boundary ring (WorldBoundaryBuilder) keeps bodies inside; this catches anything that still gets out — a hole, a physics
    /// push. Enemies have their own net (WP-061K despawn). One log line per recovery; the per-frame path reads one position.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldFallGuard : MonoBehaviour
    {
        public const float FallY = -3f;

        [SerializeField] private float _halfExtent = 44f;
        [SerializeField] private float _sampleRadius = 12f;
        [SerializeField] private Vector3 _fallback = new Vector3(0f, 0.1f, -3f);

        /// <summary>Recoveries since load (tests / probes).</summary>
        public static int PlayerRecoveries { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCounters()
        {
            PlayerRecoveries = 0;
        }

        public void Configure(float halfExtent, float sampleRadius, Vector3 fallback)
        {
            _halfExtent = halfExtent;
            _sampleRadius = sampleRadius;
            _fallback = fallback;
        }

        public float HalfExtent => _halfExtent;

        private void Update()
        {
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null) return;
            Vector3 p = rig.transform.position;
            if (p.y >= FallY) return;
            Vector3 to = Recover(p);
            rig.Teleport(to, rig.transform.rotation);
            PlayerRecoveries++;
            Debug.LogWarning("[World] fall guard: player recovered " + p + " -> " + to);
        }

        /// <summary>The nearest NavMesh point to the fall position at ground level (clamped into the playable square); fallback = run start.</summary>
        public Vector3 Recover(Vector3 from)
        {
            var probe = new Vector3(Mathf.Clamp(from.x, -_halfExtent, _halfExtent), 0f, Mathf.Clamp(from.z, -_halfExtent, _halfExtent));
            return NavMesh.SamplePosition(probe, out NavMeshHit hit, _sampleRadius, NavMesh.AllAreas) ? hit.position + Vector3.up * 0.05f : _fallback;
        }
    }
}
