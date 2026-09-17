using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · A projected-looking mark: one quad laid on the surface (3 cm off it along
    /// the normal, random spin about the normal), alpha-blended, held for its life and
    /// faded out over the last third. Not a URP decal projector — that needs a renderer
    /// feature (asset change) and the flat surfaces these land on (ground, walls, plates)
    /// read the same. Alpha is capped per row so the ground stays legible (DEC-077).
    ///
    /// WP-019b (blood): optional growth (the death pool 0.4 → 1.1 m), wet → dry recolour
    /// over 30 s (<see cref="BloodPalette"/>), an early fade request (LRU eviction) and a
    /// priority class so the blood pool evicts impact marks before death pools.
    /// </summary>
    public sealed class DecalEffect : PooledEffect
    {
        public enum Priority
        {
            /// <summary>Environment / impact marks: evicted first.</summary>
            Impact = 0,
            /// <summary>Death pools: evicted last.</summary>
            Pool = 1
        }

        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int FrameId = Shader.PropertyToID("_Frame");

        /// <summary>3 cm: the AP005 tiles sit up to 2 cm above the ground collider the mark ray hits (a 1 cm mark vanished under the tile).</summary>
        public const float SurfaceOffset = 0.03f;
        private const float DryingUpdateInterval = 0.25f;

        private Transform _quad;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private VfxCatalogEntry _row;
        private float _alphaCap;
        private float _fadeFrom = 0.66f;
        private float _size;
        private float _aspect = 1f;
        private float _growFrom;
        private float _growSeconds;
        private bool _drying;
        private bool _authored;
        private float _nextDryingUpdate;
        private Color _colour;
        private int _cell;
        private bool _fadeRequested;

        public VfxCatalogEntry Row => _row;
        public Priority EvictionClass { get; private set; }
        /// <summary>Seconds since Play (pool age for the LRU).</summary>
        public float AgeSeconds => Age;
        public bool IsFading => _fadeRequested || Progress > _fadeFrom;
        public float CurrentSize => _growSeconds > 0f ? Mathf.Lerp(_growFrom, _size, GrowT()) : _size;
        /// <summary>The colour (before the recolour gain) the mark shows now.</summary>
        public Color CurrentColour => _colour;
        public float AlphaCap => _alphaCap;
        public int AtlasCell => _cell;
        /// <summary>The renderer child (world corners for the merged-LOD absorb).</summary>
        public Transform Quad => _quad;
        public float RemainingSeconds => Mathf.Max(0f, Lifetime - Age);

        private void Awake()
        {
            EnsureQuad();
        }

        private void EnsureQuad()
        {
            if (_quad != null)
            {
                return;
            }

            var go = new GameObject("Quad");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _quad = go.transform;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Places the mark at this transform's position facing along its forward (= surface normal).</summary>
        public void Play(VfxCatalogEntry row, float worldSize, float seconds, float alphaCap, Color tint, float spinDegrees, int atlasCell = 0)
        {
            EnsureQuad();
            _row = row;
            _alphaCap = Mathf.Clamp01(alphaCap);
            _size = worldSize > 0f ? worldSize : Mathf.Max(0.05f, row.WorldSizeMetres);
            _growSeconds = 0f;
            _drying = false;
            _colour = tint;
            _cell = atlasCell;
            _fadeFrom = 0.66f;
            _fadeRequested = false;
            EvictionClass = Priority.Impact;
            // The quad's normal is -Z: rotate it to look along -forward so the face points out of the surface.
            _quad.localRotation = Quaternion.Euler(0f, 180f, spinDegrees);
            _quad.localPosition = new Vector3(0f, 0f, SurfaceOffset);
            // Non-square marks ([w, h] world sizes: the 1.4 × 0.5 m drag, the 0.45 × 0.25 m footprint) keep their aspect.
            _aspect = row.WorldHeightMetres > 0f && row.WorldSizeMetres > 0f ? row.WorldHeightMetres / row.WorldSizeMetres : 1f;
            _quad.localScale = new Vector3(_size, _size * _aspect, 1f);
            _renderer.sharedMaterial = row.Material;
            _renderer.enabled = true;
            _block.SetFloat(FrameId, atlasCell);
            _block.SetFloat(AlphaId, 1f);
            _block.SetFloat(AlphaCapId, _alphaCap);
            _block.SetColor(TintId, tint);
            _renderer.SetPropertyBlock(_block);
            Lifetime = Mathf.Max(0.1f, seconds);
            RestartLife();
        }

        /// <summary>Grows from <paramref name="fromSize"/> to the Play size over <paramref name="seconds"/> (cubic ease-out; the death pool).</summary>
        public void SetGrow(float fromSize, float seconds)
        {
            _growFrom = Mathf.Max(0.02f, fromSize);
            _growSeconds = Mathf.Max(0f, seconds);
            ApplyScale();
        }

        /// <summary>Re-targets the size (impact merge rule: an existing mark grows ×1.15 instead of a new one).</summary>
        public void SetSize(float size)
        {
            _size = Mathf.Max(0.02f, size);
            ApplyScale();
        }

        /// <summary>Wet → dry blood recolour over the life (the tint passed to Play is replaced by <see cref="BloodPalette.DecalColourAt"/> × recolour gain).</summary>
        public void SetDrying(bool drying, bool authored = false)
        {
            _drying = drying;
            _authored = authored;
            _nextDryingUpdate = 0f;
            if (drying)
            {
                ApplyDrying(0f);
                _renderer.SetPropertyBlock(_block);
            }
        }

        public void SetEvictionClass(Priority priority)
        {
            EvictionClass = priority;
        }

        /// <summary>Fade out over <paramref name="seconds"/> starting now (LRU eviction / round reset). False when already ending sooner (nothing changed).</summary>
        public bool BeginFade(float seconds)
        {
            float remaining = Lifetime - Age;
            if (remaining <= seconds)
            {
                _fadeRequested = true;
                return false;
            }

            Lifetime = Age + Mathf.Max(0.05f, seconds);
            _fadeFrom = Lifetime > 0f ? Age / Lifetime : 1f;
            _fadeRequested = true;
            return true;
        }

        /// <summary>Fixes the size (no more growth) and stops the recolour; the merged chunk copies the current state.</summary>
        public void Freeze()
        {
            _size = CurrentSize;
            _growSeconds = 0f;
            _drying = false;
        }

        /// <summary>Hide the renderer while the instance stays checked out (the merged chunk draws it now).</summary>
        public void SetRendererEnabled(bool enabled)
        {
            if (_renderer != null)
            {
                _renderer.enabled = enabled;
            }
        }

        protected override void Tick(float t)
        {
            bool dirty = false;
            if (t > _fadeFrom)
            {
                _block.SetFloat(AlphaId, 1f - (t - _fadeFrom) / (1f - _fadeFrom));
                dirty = true;
            }

            if (_growSeconds > 0f && Age <= _growSeconds + 0.05f)
            {
                ApplyScale();
            }

            if (_drying && Age >= _nextDryingUpdate)
            {
                _nextDryingUpdate = Age + DryingUpdateInterval;
                ApplyDrying(Age);
                dirty = true;
            }

            if (dirty)
            {
                _renderer.SetPropertyBlock(_block);
            }
        }

        private float GrowT()
        {
            if (_growSeconds <= 0f)
            {
                return 1f;
            }

            float u = Mathf.Clamp01(Age / _growSeconds);
            float inv = 1f - u;
            return 1f - inv * inv * inv;
        }

        private void ApplyScale()
        {
            float s = CurrentSize;
            _quad.localScale = new Vector3(s, s * _aspect, 1f);
        }

        private void ApplyDrying(float age)
        {
            _colour = BloodPalette.DecalColourAt(age);
            if (_authored)
            {
                // Painted-in-band mark: only the wet → dry darkening (the ratio of the two band colours) is applied.
                Color wet = BloodPalette.DecalWet;
                _block.SetColor(TintId, new Color(
                    Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(_colour.r) / Mathf.GammaToLinearSpace(wet.r)),
                    Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(_colour.g) / Mathf.GammaToLinearSpace(wet.g)),
                    Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(_colour.b) / Mathf.GammaToLinearSpace(wet.b)), 1f));
                return;
            }

            _block.SetColor(TintId, BloodPalette.TintFor(_row, _colour, BloodPalette.MarkCalibration));
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            _row = null;
            _drying = false;
            _authored = false;
            _growSeconds = 0f;
            _fadeRequested = false;
            if (_renderer != null)
            {
                _renderer.enabled = true;
            }
        }
    }
}
