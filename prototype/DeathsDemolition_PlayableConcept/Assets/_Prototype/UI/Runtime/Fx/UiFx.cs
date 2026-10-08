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
        Particles = 7,
        /// <summary>
        /// WP-081 ikon v2 B5b · AP-028-B's O1 glyph material: the SDF's own relief (warm upper-left key, the vertical bevel,
        /// the sheen band); WP-098A: AP-034 v002.1 row d (AD default) - I1 cavity / I2 grain, the ink within its ceiling
        /// (never under tint × (1 − (0.06 + 0.06 s))), the ink lob and the size-ramped glow outside the ink only. No clock:
        /// uv1 = (8, sheen −1 off | 0..1, glow 0..0.08, spread px24), uv2 = (stroke draw, lit 0|1, opacity, −)
        /// (<see cref="UiFx.ConfigureIcon"/>, <see cref="UiFx.SetIcon"/>). UiIconButton's O1 keys only (<see cref="UiArt.IsO1Glyph"/>).
        /// </summary>
        Icon = 8,
        /// <summary>AP-035 A8 time-map reveal; Icon remains mode 8.</summary>
        LogoInk = 9
    }

    /// <summary>Which ink-kit mark a mode reads (AP-012 v004 A8: one mark per channel, each in its own rect; the static
    /// mask of the same name is its sprite — <see cref="UiFxGlobals.InkMask"/>).</summary>
    public enum UiInkChannel
    {
        /// <summary>R · Ink.BrushUnderline — the banner / title brush stroke, left → right.</summary>
        Underline = 0,
        /// <summary>G · Ink.BrushDivider — the thin section divider stroke, left → right.</summary>
        Divider = 1,
        /// <summary>B · Ink.Hanko — the seal, centre out, the faint rim last.</summary>
        Hanko = 2,
        /// <summary>A · Ink.Blot — seeps from its deepest point outward (death bleed, curtain).</summary>
        Blot = 3,
        /// <summary>WP-098A · AP-034 M3, its own map over the whole rect, top-left → bottom-right (the brush curtain; wipe mode only).</summary>
        Wipe = 4
    }

    /// <summary>
    /// WP-068 slice 4 · The DD/UI/Fx driver on a Graphic. uGUI ignores MaterialPropertyBlocks, so the mode, the start
    /// time and the parameters go into the mesh (uv1..uv3) — written once per <see cref="Play"/> / <see cref="Configure"/>
    /// (one vertex rebuild), after which the GPU computes the animation from <c>_DD_UiTime</c>: no per-frame CPU work,
    /// no allocation. Every UiFx shares <see cref="SharedMaterial"/>, so only an atlas change breaks a batch.
    /// InkReveal parameters.y: 0 draws the mark (its shape clips), 1 wipes the whole rect.
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
        [SerializeField] private UiInkChannel _channel = UiInkChannel.Underline;

        // Icon (8): uv1.yzw - sheen (−1 = off), glow, spread. Written by the icon's state, never by a clock.
        [SerializeField] private Vector3 _iconLight = new Vector3(-1f, 0f, 0f);
        [SerializeField] private float _iconLift;   // WP-098A M7: Icon uv2.w - the lob's Elev1 → Elev2 (0..1)

        // Before the first Play the element shows its end state (start far in the past), so a static layout never
        // renders half-revealed.
        private float _start = -10000f;

        private static Material s_material;

        /// <summary>WP-081 ikon v2 B5b · Icon at rest, light channels: sheen off, no glow, no spread (uv1.yzw).</summary>
        public static readonly Vector3 IconRestLight = new Vector3(-1f, 0f, 0f);

        /// <summary>WP-081 ikon v2 B5b · Icon at rest, parameters: fully drawn, lit, opaque (uv2.xyz).</summary>
        public static readonly Vector3 IconRestParams = new Vector3(1f, 1f, 1f);

        public UiFxMode Mode => _mode;
        public float StartTime => _start;
        public float Duration => _duration;

        /// <summary>The mode parameters (uv2.xyz); Icon: stroke draw, lit, opacity.</summary>
        public Vector3 Parameters => _params;

        /// <summary>Icon only: sheen (−1 off | 0..1), glow (0..0.08), spread (px24) - uv1.yzw.</summary>
        public Vector3 IconLight => _iconLight;

        /// <summary>The one DD/UI/Fx material (process-long; the shader ships from a Resources folder).</summary>
        public static Material SharedMaterial
        {
            get
            {
                if (s_material == null)
                {
                    Shader shader = DD.Prototype.Rendering.PrototypeShaders.IsReady ? DD.Prototype.Rendering.PrototypeShaders.UiFx : null;
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
        public UiFx Configure(UiFxMode mode, float duration, Vector3 parameters, UiInkChannel channel = UiInkChannel.Underline, float loopPeriod = 0f)
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
        public UiFx Configure(UiFxMode mode, UiDuration duration, Vector3 parameters, UiInkChannel channel = UiInkChannel.Underline, float loopPeriod = 0f)
        {
            return Configure(mode, UiMotionTokens.Seconds(duration), parameters, channel, loopPeriod);
        }

        /// <summary>
        /// WP-081 ikon v2 B5b · switch to Icon (8) at rest: sheen off, no glow, no spread; fully drawn, opaque, and lit - or
        /// the flat token when <paramref name="lit"/> is false (a disabled control). One vertex rebuild.
        /// </summary>
        public UiFx ConfigureIcon(bool lit)
        {
            _iconLight = IconRestLight;
            return Configure(UiFxMode.Icon, 1f, new Vector3(IconRestParams.x, lit ? 1f : 0f, IconRestParams.z));
        }

        /// <summary>
        /// WP-081 ikon v2 · the Icon channels: <paramref name="light"/> = (sheen −1 off | 0..1, glow 0..0.08, spread px24
        /// 0..0.4), <paramref name="parameters"/> = (stroke draw 0..1, lit 0..1, opacity 0..1). One vertex rebuild, none when
        /// nothing changed (B5c's tracks write only while they run; at rest nothing is written). No allocation.
        /// </summary>
        public void SetIcon(Vector3 light, Vector3 parameters)
        {
            if (light == _iconLight && parameters == _params)
            {
                return;
            }

            _iconLight = light;
            _params = parameters;
            Dirty();
        }

        /// <summary>WP-081 ikon v2 B5b · the light switch alone: lit (1) or the flat token (0, a disabled control).</summary>
        public void SetIconLit(bool lit)
        {
            SetIcon(_iconLight, new Vector3(_params.x, lit ? 1f : 0f, _params.z));
        }

        /// <summary>WP-098A (HAREKET-MENU §3) · the Icon channels with the lift (uv2.w 0..1, the lob Elev1 → Elev2); one rebuild, none unchanged.</summary>
        public void SetIcon(Vector3 light, Vector3 parameters, float lift)
        {
            if (lift == _iconLift) { SetIcon(light, parameters); return; }
            _iconLift = lift;
            _iconLight = light;
            _params = parameters;
            Dirty();
        }

        public float IconLift => _iconLift;

        /// <summary>The §2.5 motion class each mode obeys (the shader applies it: Decor → end state under RM, Flash × FlashScale, Data untouched).</summary>
        public static UiMotionClass ClassOf(UiFxMode mode)
        {
            switch (mode)
            {
                case UiFxMode.Leaf:
                case UiFxMode.Particles:   // one-shot sparks; the looping drift is Decor (frozen under RM)
                case UiFxMode.Icon:        // sheen and glow peaks × FlashScale; reduced motion rests the light channels (the shader)
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

        /// <summary>
        /// WP-098A (KIT-API §3) · start the one-shot <paramref name="delay"/> s from now on the unscaled clock (one vertex
        /// rebuild): a pane's marks ink in after its scrim without a timer of their own. Until then the shader shows the start
        /// state (nothing inked; Reduced Motion: the end state). A negative delay starts it in the past - −<see cref="Duration"/>
        /// lands it drawn at once (a cut), with no wet front.
        /// </summary>
        public void Play(float delay)
        {
            _start = UiFxGlobals.Now + delay;
            Dirty();
        }

        /// <summary>WP-094: the one-shot starts <paramref name="delay"/> s from now (a banner drawn after a beat; nothing inked before).</summary>
        public void PlayDelayed(float delay)
        {
            _start = UiFxGlobals.Now + Mathf.Max(0f, delay);
            Dirty();
        }

        /// <summary>WP-094: a new length for the next play, mode / parameters / channel kept (one vertex rebuild).</summary>
        public UiFx SetDuration(float seconds)
        {
            _duration = Mathf.Max(0.01f, seconds);
            Dirty();
            return this;
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
            // Icon (8) has no clock: uv1.yzw carry its light channels as they are (sheen, glow, spread).
            var fx = _mode == UiFxMode.Icon
                ? new Vector4((float)_mode, _iconLight.x, _iconLight.y, _iconLight.z)
                : new Vector4((float)_mode, _start, _duration, _loopPeriod);
            var prm = new Vector4(_params.x, _params.y, _params.z, _mode == UiFxMode.Icon ? _iconLift : (float)_channel);
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
