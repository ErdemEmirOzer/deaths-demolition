using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.World
{
    /// <summary>
    /// R3 #1 (S-05; C-00 07.10 decision B; measured K/wp-038m/evidence-probe) · An authored world loaded additively — WorldBootstrap
    /// in the run, MenuCinematic in the menu — leaves the active scene where it was, and Unity does not bind that world's baked
    /// light probes: <c>LightmapSettings.lightProbes</c> stays null and every probe-lit renderer lights from the active scene's flat
    /// ambient SH. The world's builder bakes, then writes the baked probes here (WorldV5Lighting); this component binds them while
    /// its world is loaded but not the active scene and gives the previous probes back when the world goes. No name or object-life
    /// guess: the reference is the bake's own.
    /// <para>The ground and the water are left out of the probes on purpose (WorldV5Lighting.ProbeOff, AD 07.10 M1): they keep the
    /// active scene's ambient, the approved Look B tone; the characters, bamboo, hall and props take the baked light.</para>
    /// <para><see cref="Enabled"/> is the before / after switch for the AD frames and the Z1 / M6 re-measurement; it acts at once.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldBakedProbes : MonoBehaviour
    {
        [SerializeField] private LightProbes _probes;
        [SerializeField] private int _bakedCount;
        [SerializeField] private string _source;

        private LightProbes _previous;
        private bool _bound;
        private static bool s_enabled = true;

        /// <summary>The loaded world's component (the last enabled one), or null.</summary>
        public static WorldBakedProbes Current { get; private set; }

        /// <summary>Before / after switch (AD frames, measurements), applied at once. Back to true on the next play session.</summary>
        public static bool Enabled
        {
            get { return s_enabled; }
            set
            {
                if (s_enabled == value) return;
                s_enabled = value;
                if (Current != null) Current.Apply();
            }
        }

        /// <summary>The baked probes the builder wrote (null before the first bake).</summary>
        public LightProbes Probes => _probes;

        /// <summary>The probe count the bake reported when it wrote <see cref="Probes"/>.</summary>
        public int BakedCount => _bakedCount;

        /// <summary>Where the builder took the probes from: "lightingData" (the bake's own) or "standalone" (a copy next to the scene).</summary>
        public string Source => _source;

        /// <summary>True while this world's probes are the ones the renderers sample.</summary>
        public bool IsBound => _bound && _probes != null && LightmapSettings.lightProbes == _probes;

#if UNITY_EDITOR
        /// <summary>Builder only (WorldV5Lighting after the bake).</summary>
        public void SetBaked(LightProbes probes, int bakedCount, string source)
        {
            _probes = probes;
            _bakedCount = bakedCount;
            _source = source;
        }
#endif

        private void OnEnable()
        {
            Current = this;
            Apply();
        }

        private void OnDisable()
        {
            Unbind();
            if (Current == this) Current = null;
        }

        private void Apply()
        {
            // the active world binds its own probes (Unity does); only an additive world needs the hand-over
            bool want = s_enabled && _probes != null && gameObject.scene != SceneManager.GetActiveScene();
            if (want && !_bound)
            {
                LightProbes current = LightmapSettings.lightProbes;
                _previous = current == _probes ? null : current;
                LightmapSettings.lightProbes = _probes;
                _bound = true;
                Debug.Log("[World] light probes: bound " + _probes.count + " baked light probes of '" + gameObject.scene.name + "' (" + _source + ", active scene '" + SceneManager.GetActiveScene().name + "').");
            }
            else if (!want && _bound)
            {
                Unbind();
            }
        }

        private void Unbind()
        {
            if (!_bound) return;
            if (LightmapSettings.lightProbes == _probes) LightmapSettings.lightProbes = _previous;
            _previous = null;
            _bound = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_enabled = true;
            Current = null;
        }
    }
}
