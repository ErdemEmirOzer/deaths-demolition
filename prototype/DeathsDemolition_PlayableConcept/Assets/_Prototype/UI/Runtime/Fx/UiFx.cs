using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>WP-068 slice 4 · DD/UI/Fx modes (§3). The value rides in uv1.x; the shader keeps the same numbers.</summary>
    public enum UiFxMode
    {
        None = 0,
        /// <summary>Time-map threshold reveal with a wet 0.04 front (banner, underline, seal, death bleed, curtain).</summary>
        InkReveal = 1,
        /// <summary>Glint band × sprite alpha × crack tile; ≤ +0.25 brightness × FlashScale. Kinpaku only on money / Special.</summary>
        Leaf = 2,
        /// <summary>1.12 → 1.00 InCubic 90 ms + 40 ms hold, inked in through the hanko rim.</summary>
        Stamp = 3,
        /// <summary>Menu-only slow band, α ≤ 0.05; absent under reduced motion.</summary>
        Sheen = 4,
        /// <summary>Fill + segments + ghost trail on one quad.</summary>
        SegmentBar = 5,
        /// <summary>Distance-field sprite: outline / glow / erosion from the alpha channel.</summary>
        SdfGlyph = 6,
        /// <summary>Quads built once, moved in the vertex stage (<see cref="UiFxParticles"/>).</summary>
        Particles = 7
    }

    /// <summary>Which ink-kit mark a mode reads (A8 layout: one mark per channel).</summary>
    public enum UiInkChannel
    {
        Brush = 0,
        Bleed = 1,
        Hanko = 2,
        LeafCrack = 3
    }

    /// <summary>
    /// WP-068 slice 4 · The DD/UI/Fx driver on a Graphic. uGUI ignores MaterialPropertyBlocks, so the mode, the start
    /// time and the parameters go into the mesh (uv1..uv3) — written once per <see cref="Play"/> / <see cref="Configure"/>
    /// (one vertex rebuild), after which the GPU computes the animation from <c>_DD_UiTime</c>: no per-frame CPU work,
    /// no allocation. Every UiFx shares <see cref="SharedMaterial"/>, so only an atlas change breaks a batch.
    /// TMP does not take BaseMeshEffect — text reveals use the motion core's Clip channel (slice 2).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class UiFx : BaseMeshEffect
    {
        public const string ShaderName = "DD/UI/Fx";

        private const AdditionalCanvasShaderChannels Channels =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;

        [SerializeField] private UiFxMode _mode = UiFxMode.InkReveal;
        [SerializeField] private float _duration = 0.28f;
        [SerializeField] private float _loopPeriod;
        [SerializeField] private Vector3 _params;
        [SerializeField] private UiInkChannel _channel = UiInkChannel.Brush;

        // Before the first Play the element shows its end state (start far in the past), so a static layout never
        // renders half-revealed.
        private float _start = -10000f;

        private static Material s_material;

        public UiFxMode Mode => _mode;
        public float StartTime => _start;
        public float Duration => _duration;

        /// <summary>The one DD/UI/Fx material (process-long; the shader ships from a Resources folder).</summary>
        public static Material SharedMaterial
        {
            get
            {
                if (s_material == null)
                {
                    Shader shader = Shader.Find(ShaderName);
                    if (shader == null)
                    {
                        return null;
                    }

                    s_material = new Material(shader) { name = "DD_UiFx (shared)", hideFlags = HideFlags.DontSave };
                }

                return s_material;
            }
        }

        /// <summary>Set the mode and its parameters (§3 per mode; see the shader). Rebuilds the vertices once.</summary>
        public UiFx Configure(UiFxMode mode, float duration, Vector3 parameters, UiInkChannel channel = UiInkChannel.Brush, float loopPeriod = 0f)
        {
            _mode = mode;
            _duration = Mathf.Max(0.01f, duration);
            _params = parameters;
            _channel = channel;
            _loopPeriod = Mathf.Max(0f, loopPeriod);
            Dirty();
            return this;
        }

        /// <summary>Token-duration form (§2.5 <see cref="UiDuration"/>): banners Screen/Reveal, stamps Instant, glints Reveal.</summary>
        public UiFx Configure(UiFxMode mode, UiDuration duration, Vector3 parameters, UiInkChannel channel = UiInkChannel.Brush, float loopPeriod = 0f)
        {
            return Configure(mode, UiMotionTokens.Seconds(duration), parameters, channel, loopPeriod);
        }

        /// <summary>The §2.5 motion class each mode obeys (the shader applies it: Decor → end state under RM, Flash × FlashScale, Data untouched).</summary>
        public static UiMotionClass ClassOf(UiFxMode mode)
        {
            switch (mode)
            {
                case UiFxMode.Leaf:
                case UiFxMode.Particles:   // one-shot sparks; the looping drift is Decor (frozen under RM)
                    return UiMotionClass.Flash;
                case UiFxMode.SegmentBar:
                    return UiMotionClass.Data;
                default:
                    return UiMotionClass.Decor;
            }
        }

        /// <summary>Start the one-shot now (unscaled clock). Reduced motion: the shader shows the end state at once.</summary>
        public void Play()
        {
            _start = UiFxGlobals.Now;
            Dirty();
        }

        /// <summary>Show the start state (nothing inked yet) until <see cref="Play"/>.</summary>
        public void Hold()
        {
            _start = float.MaxValue * 0.5f;
            Dirty();
        }

        /// <summary>Change one parameter vector (a bar's fill / ghost) — one vertex rebuild.</summary>
        public void SetParams(Vector3 parameters)
        {
            _params = parameters;
            Dirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            UiFxGlobals.EnsureInstalled();
            Attach(graphic);
        }

        /// <summary>Bind the shared material and the extra canvas channels to a graphic (UiFxParticles uses it too).</summary>
        internal static void Attach(Graphic g)
        {
            if (g == null)
            {
                return;
            }

            Material shared = SharedMaterial;
            if (shared != null && (g.material == null || g.material == g.defaultMaterial))
            {
                g.material = shared;
            }

            Canvas c = g.canvas;
            if (c != null && (c.additionalShaderChannels & Channels) != Channels)
            {
                c.additionalShaderChannels |= Channels;
            }
        }

        private void Dirty()
        {
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive())
            {
                return;
            }

            Rect r = graphic.rectTransform.rect;
            float w = Mathf.Max(1e-3f, r.width), h = Mathf.Max(1e-3f, r.height);
            var fx = new Vector4((float)_mode, _start, _duration, _loopPeriod);
            var prm = new Vector4(_params.x, _params.y, _params.z, (float)_channel);
            UIVertex v = default;
            int n = vh.currentVertCount;
            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.uv1 = fx;
                v.uv2 = prm;
                // zw: the Stamp pivot (rect centre), otherwise the rect size in px (pixel-space tiles / jitter cells — AD MUST 1).
                v.uv3 = _mode == UiFxMode.Stamp
                    ? new Vector4((v.position.x - r.xMin) / w, (v.position.y - r.yMin) / h, r.center.x, r.center.y)
                    : new Vector4((v.position.x - r.xMin) / w, (v.position.y - r.yMin) / h, w, h);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
