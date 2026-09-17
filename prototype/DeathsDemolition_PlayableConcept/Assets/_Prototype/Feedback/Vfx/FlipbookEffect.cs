using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · One pooled quad that plays an AP-010 sheet: the catalog row gives the
    /// material (one per row, shared), the grid, the frame count, the fps and the world
    /// size; the frame index and the fade travel through a MaterialPropertyBlock, so the
    /// instance owns no material at all (ADR-019-1: ≤ 1 material per instance).
    ///
    /// Orientation: <see cref="Facing.Camera"/> billboards every frame (impacts, muzzle,
    /// explosion, death); <see cref="Facing.Fixed"/> keeps the rotation given at Play
    /// (the ground-aligned shockwave and beacon ring).
    ///
    /// Origin: the manifest's <c>origin_uv</c> is the point of the frame that sits on the
    /// event position; the renderer child is offset so that point lands on this transform.
    /// </summary>
    public sealed class FlipbookEffect : PooledEffect
    {
        public enum Facing
        {
            Camera = 0,
            Fixed = 1,
            /// <summary>Billboard about the world up axis only: ground-anchored sheets (spawn dust, dust step, flames) never dip below the floor.</summary>
            CameraYaw = 2
        }

        private static readonly int FrameId = Shader.PropertyToID("_Frame");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        private Transform _quad;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private VfxCatalogEntry _row;
        private Facing _facing;
        private float _fps;
        private int _frames;
        private bool _loop;
        private float _fadeOutFrom;
        private float _alphaCap;
        private int _lastFrame = -1;
        private Vector3 _align;
        private float _growFrom, _growTo, _growAspect, _growSeconds;
        private int _holdFrame = -1;
        private bool _alignRight;
        private float _alphaStepAt = -1f;
        private float _alphaStepTo = 1f;

        /// <summary>Catalog row playing on this instance (test readout).</summary>
        public VfxCatalogEntry Row => _row;
        public int CurrentFrame => _lastFrame;
        /// <summary>Seconds this instance plays for (test readout: the blood splash must stay ≤ 0.3 s).</summary>
        public float LifeSeconds => Lifetime;
        public Vector3 AlignDirection => _align;

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
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = VfxLibrary.Quad;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _quad = go.transform;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Plays <paramref name="row"/> once (or looping for <paramref name="seconds"/> when the row loops).
        /// <paramref name="worldSize"/> ≤ 0 uses the row's manifest size. Alpha fades out over the last
        /// <paramref name="fadeOutFraction"/> of the life; <paramref name="alphaCap"/> clamps the sheet alpha (DEC-077).
        /// </summary>
        public void Play(VfxCatalogEntry row, Facing facing, float worldSize, Color tint, float alphaCap = 1f,
            float fadeOutFraction = 0f, float seconds = -1f)
        {
            EnsureQuad();
            _row = row;
            _facing = facing;
            _fps = Mathf.Max(1f, row.Fps > 0f ? row.Fps : 30f);
            _frames = Mathf.Max(1, row.FramesUsed);
            _loop = row.Loop;
            _alphaCap = Mathf.Clamp01(alphaCap);
            _fadeOutFrom = Mathf.Clamp01(1f - fadeOutFraction);
            _lastFrame = -1;
            _align = Vector3.zero;
            _alignRight = false;
            _growSeconds = 0f;
            _holdFrame = -1;
            _alphaStepAt = -1f;
            _alphaStepTo = 1f;
            // A one-shot sheet asked to finish sooner than its manifest length plays every frame faster (blood splash 12 frames in 0.26 s).
            if (seconds > 0f && !_loop && _frames > 1)
            {
                _fps = _frames / seconds;
            }

            float size = worldSize > 0f ? worldSize : Mathf.Max(0.05f, row.WorldSizeMetres);
            float aspect = row.Texture != null ? (float)row.Texture.width / Mathf.Max(1, row.Texture.height) : 1f;
            // A non-square texture (the beacon column 512×2048) keeps its aspect: width = size.
            float cellAspect = aspect * row.Rows / Mathf.Max(1, row.Columns);
            // v003 [w, h] world sizes (spawn dust 1.76 × 1.1, claw 1.1 × 0.7): the authored height wins over the cell aspect.
            float height = row.WorldHeightMetres > 0f && row.WorldSizeMetres > 0f && Mathf.Abs(row.WorldHeightMetres - row.WorldSizeMetres) > 1e-4f
                ? size * row.WorldHeightMetres / row.WorldSizeMetres
                : size / Mathf.Max(0.01f, cellAspect);
            _quad.localScale = new Vector3(size, height, 1f);
            _quad.localPosition = new Vector3((0.5f - row.OriginUv.x) * _quad.localScale.x, (0.5f - row.OriginUv.y) * _quad.localScale.y, 0f);
            _quad.localRotation = Quaternion.identity;

            _renderer.sharedMaterial = row.Material;
            _block.SetFloat(FrameId, 0f);
            _block.SetFloat(AlphaId, 1f);
            _block.SetFloat(AlphaCapId, _alphaCap);
            // LA (greyscale) v003 sheets carry their colour in the manifest tint (× hdr when additive); the caller's tint
            // multiplies it. v002 rows have a white default, so nothing changes for them. The property block overrides
            // the material's _Tint, which is why the manifest tint must ride along here (a white block made claw_swipe white).
            Color manifest = row.DefaultTint;
            if (row.Additive && row.HdrIntensity > 1f) manifest *= row.HdrIntensity;
            manifest.a = 1f;
            _block.SetColor(TintId, tint * manifest);
            _renderer.SetPropertyBlock(_block);

            float life = seconds > 0f ? seconds : row.DurationSeconds > 0f ? row.DurationSeconds : _frames / _fps;
            Lifetime = life;
            RestartLife();
            Face();
        }

        /// <summary>
        /// Camera-facing cards only: roll the card so an axis follows this world direction (a directional
        /// splash). <paramref name="alignRight"/> = the sheet's +u (AP-010B cones travel +u), else its up (+v).
        /// </summary>
        public void SetAlign(Vector3 worldDirection, bool alignRight = false)
        {
            _align = worldDirection;
            _alignRight = alignRight;
            Face();
        }

        /// <summary>Freeze the sheet on one cell (the sealed Ofuda talisman: frame 1 for the seal's life).</summary>
        public void HoldFrame(int frame)
        {
            _holdFrame = Mathf.Clamp(frame, 0, Mathf.Max(0, _frames - 1));
        }

        /// <summary>Non-uniform stretch of the card (muzzle variants: A long / C wide) — multiplies the Play size per axis.</summary>
        public void SetStretch(float x, float y)
        {
            Vector3 sc = _quad.localScale;
            sc.x = Mathf.Abs(sc.x) * x * Mathf.Sign(sc.x == 0f ? 1f : sc.x);
            sc.y = Mathf.Abs(sc.y) * y;
            _quad.localScale = sc;
        }

        /// <summary>Grow the card from <paramref name="fromSize"/> to its Play size over <paramref name="seconds"/> (the Kagura ring follows the wave front).</summary>
        public void SetGrow(float fromSize, float seconds)
        {
            _growFrom = Mathf.Max(0.01f, fromSize);
            _growTo = _quad.localScale.x;
            _growAspect = _quad.localScale.y / Mathf.Max(0.0001f, _quad.localScale.x);
            _growSeconds = Mathf.Max(0.01f, seconds);
        }

        /// <summary>Mirror the sheet along its +u axis (random handedness breaks a symmetric authored burst — AD M2).</summary>
        public void SetMirror(bool mirror)
        {
            Vector3 sc = _quad.localScale;
            sc.x = Mathf.Abs(sc.x) * (mirror ? -1f : 1f);
            _quad.localScale = sc;
        }

        /// <summary>After <paramref name="atSeconds"/> the alpha drops to <paramref name="toAlpha"/> over 0.1 s (death sheet: 1 → 0.35 after 0.28 s, readability).</summary>
        public void SetAlphaStep(float atSeconds, float toAlpha)
        {
            _alphaStepAt = atSeconds;
            _alphaStepTo = Mathf.Clamp01(toAlpha);
        }

        protected override void Tick(float t)
        {
            if (_growSeconds > 0f && Age <= _growSeconds + 0.05f)
            {
                float u = Mathf.Clamp01(Age / _growSeconds);
                float s = Mathf.Lerp(_growFrom, _growTo, u);
                _quad.localScale = new Vector3(s, s * _growAspect, 1f);
            }

            int frame = Mathf.FloorToInt(t * Lifetime * _fps);
            frame = _holdFrame >= 0 ? _holdFrame : _loop ? frame % _frames : Mathf.Min(frame, _frames - 1);
            float alpha = t > _fadeOutFrom && _fadeOutFrom < 1f ? 1f - (t - _fadeOutFrom) / (1f - _fadeOutFrom) : 1f;
            bool stepping = _alphaStepAt >= 0f && Age > _alphaStepAt;
            if (stepping)
            {
                alpha *= Mathf.Lerp(1f, _alphaStepTo, Mathf.Clamp01((Age - _alphaStepAt) / 0.1f));
            }

            if (frame != _lastFrame || _fadeOutFrom < 1f || stepping)
            {
                _lastFrame = frame;
                _block.SetFloat(FrameId, frame);
                _block.SetFloat(AlphaId, alpha);
                _renderer.SetPropertyBlock(_block);
            }

            Face();
        }

        private void Face()
        {
            if (_facing == Facing.Fixed)
            {
                return;
            }

            if (FeedbackCameraRef.TryGetPosition(out Vector3 viewer))
            {
                Vector3 to = viewer - transform.position;
                if (_facing == Facing.CameraYaw)
                {
                    to.y = 0f;
                }

                if (to.sqrMagnitude > 1e-4f)
                {
                    // The quad's mesh normal is -Z, so it must look AWAY from the viewer to face it.
                    Vector3 forward = -to.normalized;
                    Vector3 up = Vector3.up;
                    if (_align.sqrMagnitude > 1e-4f)
                    {
                        Vector3 projected = Vector3.ProjectOnPlane(_align, to);
                        if (projected.sqrMagnitude > 1e-4f)
                        {
                            // right = cross(up, forward) → for right = r choose up = cross(forward, r).
                            up = _alignRight ? Vector3.Cross(forward, projected.normalized) : projected;
                        }
                    }

                    transform.rotation = Quaternion.LookRotation(forward, up);
                }
            }
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            _row = null;
        }
    }
}
