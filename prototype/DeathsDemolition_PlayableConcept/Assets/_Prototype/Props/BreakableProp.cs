using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Props
{
    /// <summary>
    /// WP-013 · REQ-113 binding for the AP-003B <c>small_breakable</c> family. The generated
    /// prefab already has both states (ADR-012-2): <c>Geometry</c> (intact LODs + trigger
    /// proxy) and an inactive <c>Broken</c> child (the <c>_BROKEN_LOD</c> meshes + their own
    /// proxy). This component only flips between them:
    ///
    ///   intact  --Hit()-->  broken : Geometry off, Broken on, one burst of chips + dust
    ///
    /// No gameplay rule is added: no health, no reward, no damage type. A single impact
    /// of any weapon breaks the prop (the props are lanterns, crates and pots, not enemies),
    /// and a broken prop stays broken until <see cref="ResetToIntact"/> (pool return / new run).
    ///
    /// HOW A HIT ARRIVES. The weapon layer does not know about props; <see cref="HitscanEngine"/>
    /// already stops a shot on world geometry and reports a surface impact. <see cref="PropImpacts"/>
    /// is the one static hook it calls with the collider it hit; the prop registered its
    /// colliders on enable, so the lookup is a dictionary read and never a GetComponent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreakableProp : MonoBehaviour
    {
        public const string GeometryName = "Geometry";
        public const string BrokenName = "Broken";

        /// <summary>Resources name of the burst prefab (Assets/_Art/VFX/Resources). Absent = no burst, still breaks.</summary>
        public const string BurstResourceName = "DD_BreakBurst";

        public enum State
        {
            Intact = 0,
            Broken = 1
        }

        [SerializeField] private Transform _geometry = null;
        [SerializeField] private Transform _broken = null;

        [Tooltip("Scale of the chip burst; 1 = the prefab's own size.")]
        [SerializeField] private float _burstScale = 1f;

        private readonly List<Collider> _registered = new List<Collider>(4);

        private static int s_breakCount;

        public State Current { get; private set; }
        public bool IsBroken => Current == State.Broken;

        /// <summary>Number of props broken since the last domain reload. Test / probe readout.</summary>
        public static int BreakCount => s_breakCount;

        /// <summary>Raised after a prop broke. (prop, hit point, hit direction).</summary>
        public static event System.Action<BreakableProp, Vector3, Vector3> Broke;

        public Transform Geometry => _geometry;
        public Transform Broken => _broken;

        /// <summary>Resolves the two state roots by name when the fields are empty (the prefab builder fills them; a hand-placed copy may not).</summary>
        public void ResolveChildren()
        {
            if (_geometry == null)
            {
                _geometry = transform.Find(GeometryName);
            }

            if (_broken == null)
            {
                _broken = transform.Find(BrokenName);
            }
        }

        private void Awake()
        {
            ResolveChildren();
            ApplyState(State.Intact);
        }

        private void OnEnable()
        {
            Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        /// <summary>One impact. Returns true when this call broke the prop (false when it was already broken).</summary>
        public bool Hit(Vector3 point, Vector3 direction)
        {
            if (Current == State.Broken)
            {
                return false;
            }

            ApplyState(State.Broken);
            s_breakCount++;
            PlayBurst(point, direction);
            Broke?.Invoke(this, point, direction);
            return true;
        }

        /// <summary>Back to intact (pool return, new run). Colliders re-register for the intact proxy.</summary>
        public void ResetToIntact()
        {
            ApplyState(State.Intact);
        }

        private void ApplyState(State state)
        {
            Current = state;
            if (_geometry != null)
            {
                _geometry.gameObject.SetActive(state == State.Intact);
            }

            if (_broken != null)
            {
                _broken.gameObject.SetActive(state == State.Broken);
            }

            if (isActiveAndEnabled)
            {
                Register();
            }
        }

        private void Register()
        {
            Unregister();
            // Both state roots: a bullet may hit the broken proxy too (it simply does nothing).
            GetComponentsInChildren(true, _registered);
            for (int i = 0; i < _registered.Count; i++)
            {
                PropImpacts.Register(_registered[i], this);
            }
        }

        private void Unregister()
        {
            for (int i = 0; i < _registered.Count; i++)
            {
                PropImpacts.Unregister(_registered[i], this);
            }

            _registered.Clear();
        }

        private void PlayBurst(Vector3 point, Vector3 direction)
        {
            if (!Application.isPlaying)
            {
                return;   // edit-mode state tests: no instantiate/destroy outside play
            }

            var prefab = Resources.Load<GameObject>(BurstResourceName);
            if (prefab == null)
            {
                return;
            }

            Vector3 up = direction.sqrMagnitude > 1e-4f ? -direction.normalized : Vector3.up;
            GameObject burst = Instantiate(prefab, point, Quaternion.LookRotation(up, Vector3.up));
            burst.transform.localScale = Vector3.one * Mathf.Max(0.1f, _burstScale);
            var ps = burst.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ParticleSystem.MainModule main = ps.main;
                main.stopAction = ParticleSystemStopAction.Destroy;
                ps.Play(true);
            }
            else
            {
                Destroy(burst, 4f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_breakCount = 0;
            Broke = null;
        }
    }

    /// <summary>
    /// The weapon layer's one door into props: "this collider took an impact at this
    /// point". Colliders are registered by the prop; an unregistered collider costs one
    /// dictionary miss and nothing else. Allocation-free on the hot path.
    /// </summary>
    public static class PropImpacts
    {
        private static readonly Dictionary<Collider, BreakableProp> s_byCollider = new Dictionary<Collider, BreakableProp>(64);

        public static int RegisteredCount => s_byCollider.Count;

        public static void Register(Collider collider, BreakableProp prop)
        {
            if (collider != null && prop != null)
            {
                s_byCollider[collider] = prop;
            }
        }

        public static void Unregister(Collider collider, BreakableProp prop)
        {
            if (collider != null && s_byCollider.TryGetValue(collider, out BreakableProp current) && current == prop)
            {
                s_byCollider.Remove(collider);
            }
        }

        public static bool TryGet(Collider collider, out BreakableProp prop)
        {
            if (collider != null && s_byCollider.TryGetValue(collider, out prop))
            {
                return prop != null;
            }

            prop = null;
            return false;
        }

        /// <summary>Called by the hitscan for every world-geometry impact. Returns true when a prop broke.</summary>
        public static bool Report(Collider collider, Vector3 point, Vector3 direction)
        {
            return TryGet(collider, out BreakableProp prop) && prop.Hit(point, direction);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_byCollider.Clear();
        }
    }
}
