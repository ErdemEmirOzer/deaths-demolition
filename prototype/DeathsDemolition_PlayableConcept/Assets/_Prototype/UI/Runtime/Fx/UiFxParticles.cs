using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 4 · The Particles mode: N quads built once (base-size quads on their emitter point, corner in uv3), moved
    /// by the vertex stage from <c>_DD_UiTime</c>. Replaces per-frame CPU drift (UiDrift ≈ 1.2 allocations / frame).
    /// Burst: one-shot sparks (HUD: Special ready, ≤ 0.3 s; Flash class). Loop: petals / rain drifting down (menu; Decor,
    /// frozen under reduced motion).
    /// WP-098A (HAREKET-MENU M17, C-00 08.10) · <see cref="Scatter"/>: a loop's emitters spread over the whole rect, not only its
    /// top edge (slow motes - 14–24 px/s over a 9–14 s loop - would otherwise only ever fill a band under the top); and
    /// <see cref="Cells"/>: each quad reads one cell of a sheet (AP-034 P1: a row of four ash flakes, a row of leaves), cell
    /// i mod n laid left → right from the first. Both are written into the mesh once - no per-frame CPU work.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UiFxParticles : MaskableGraphic
    {
        [SerializeField] private int _count = 12;
        [SerializeField] private float _size = 6f;
        [SerializeField] private float _speed = 320f;
        [SerializeField] private float _gravity = 600f;
        [SerializeField] private float _life = 0.3f;
        [SerializeField] private float _loopPeriod;
        [SerializeField] private Texture _texture;
        [SerializeField] private float _startRadius;
        private float _start = -10000f;
        [SerializeField] private bool _scatter;
        [SerializeField] private Rect _cell = new Rect(0f, 0f, 1f, 1f);
        [SerializeField] private int _cells = 1;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;
        public int Count => _count;

        /// <summary>
        /// <paramref name="startRadius"/> (reference px, bursts only): each spark starts on a circle of that radius at its own
        /// angle and travels outward from it — the Special ring's sparks leave the stroke (r 66 → 96), not the centre over the
        /// timer (AD 8c SHOULD-2). 0 = from the centre, as before.
        /// </summary>
        public UiFxParticles Configure(int count, float size, float speed, float gravity, float life, float loopPeriod = 0f, Texture texture = null,
            float startRadius = 0f)
        {
            _startRadius = Mathf.Max(0f, startRadius);
            _count = Mathf.Clamp(count, 1, 256);
            _size = size;
            _speed = speed;
            _gravity = gravity;
            _life = Mathf.Max(0.05f, life);
            _loopPeriod = Mathf.Max(0f, loopPeriod);
            _texture = texture;
            SetVerticesDirty();
            return this;
        }

        /// <summary>WP-098A M17: a loop's emitters over the whole rect (true) or along its top edge (false, the rain). One vertex rebuild.</summary>
        public UiFxParticles Scatter(bool scatter)
        {
            _scatter = scatter;
            SetVerticesDirty();
            return this;
        }

        /// <summary>
        /// WP-098A M17: quad i draws cell (i mod <paramref name="count"/>) of the texture, the cells <paramref name="first"/>.width
        /// apart to the right of <paramref name="first"/> (texture uv, bottom-left). One vertex rebuild.
        /// </summary>
        public UiFxParticles Cells(Rect first, int count)
        {
            _cell = first;
            _cells = Mathf.Max(1, count);
            SetVerticesDirty();
            return this;
        }

        public void Play()
        {
            _start = UiFxGlobals.Now;
            SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            UiFxGlobals.EnsureInstalled();
            UiFx.Attach(this);
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            var fx = new Vector4((float)UiFxMode.Particles, _start, _life, _loopPeriod);
            Color32 c = color;
            for (int i = 0; i < _count; i++)
            {
                float seed = (i + 0.5f) / _count;
                // Loop: emitters spread over the rect's width at its top; burst: all at the centre.
                Vector3 origin = _loopPeriod > 0f
                    ? new Vector3(Mathf.Lerp(r.xMin, r.xMax, Hash(seed * 17.3f)), _scatter ? Mathf.Lerp(r.yMin, r.yMax, Hash(seed * 31.7f + 0.5f)) : r.yMax, 0f)
                    : new Vector3(r.center.x, r.center.y, 0f);
                var cell = new Vector2(_cell.x + (i % _cells) * _cell.width, _cell.y);
                var prm = new Vector4(_size, _speed, _gravity, seed);
                int b = vh.currentVertCount;
                float half = _size * 0.5f;   // a real quad on the CPU (uGUI drops zero-area geometry); the shader re-centres it
                float radius = _loopPeriod > 0f ? 0f : _startRadius;
                float u1 = cell.x + _cell.width, v1 = cell.y + _cell.height;
                vh.AddVert(Vert(origin + new Vector3(-half, -half), c, new Vector2(cell.x, cell.y), fx, prm, -1f, -1f, radius));
                vh.AddVert(Vert(origin + new Vector3(-half, half), c, new Vector2(cell.x, v1), fx, prm, -1f, 1f, radius));
                vh.AddVert(Vert(origin + new Vector3(half, half), c, new Vector2(u1, v1), fx, prm, 1f, 1f, radius));
                vh.AddVert(Vert(origin + new Vector3(half, -half), c, new Vector2(u1, cell.y), fx, prm, 1f, -1f, radius));
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b + 2, b + 3, b);
            }
        }

        private static UIVertex Vert(Vector3 p, Color32 c, Vector2 uv, Vector4 fx, Vector4 prm, float cx, float cy, float radius)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            v.uv0 = uv;
            v.uv1 = fx;
            v.uv2 = prm;
            v.uv3 = new Vector4(cx, cy, radius, 0f);   // z: the burst's start radius
            return v;
        }

        private static float Hash(float x)
        {
            float s = Mathf.Sin(x * 12.9898f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }
    }
}
