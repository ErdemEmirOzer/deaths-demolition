using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B1 · The tracer is the bullet (direction §1.2). Each frame the live projectiles are READ from S-02's
    /// <see cref="ProjectileSystem"/> (ADR-069-1: no event per frame, no object per round): the head is the projectile,
    /// the tail trails it by L = clamp(v × 0.020 s, 1.2, 8 m) (sniper × 0.025, ≤ 10 m) but never past the muzzle. Every
    /// round carries a thin cold glint; every Nth (the class's rhythm, keyed on the round's ProjectileId — WP-069 R1-A #5 —
    /// so a round keeps its choice from spawn to impact) a full Kor tracer — core + glow; shotgun pellets are 0.5 m warm glint bars from 0.6 m
    /// out, gone by 6 m. When a round stops on an impact (<see cref="ProjectileEvents.Impact"/>) its head stays there
    /// and the tail runs in behind it — a local "tail-out" ribbon. Hitscan callers (a Special's legacy shot) still use
    /// <see cref="Emit"/>. Drawn with <c>Graphics.DrawMeshInstanced</c> on DD/VFX Ribbon, one call per layer (core,
    /// glow, glint), pooled to <see cref="Capacity"/>, 0 B per frame; the interim WP-069 streak is switched off while
    /// this renderer is ready. Tiers: Performance drops glow and glint and halves the HDR.
    /// </summary>
    [DefaultExecutionOrder(950)]
    public sealed class TracerRibbonRenderer : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.TracerRibbons");

        public const int Capacity = 256;
        public const string ShaderName = "DD/VFX Ribbon";
        public const float PelletBarLength = 0.5f;
        /// <summary>WP-073 B2 (AD B1 SHOULD): in first person the bars are shorter and softer — 0.30 m at α 0.6 (they crossed the whole lower frame).</summary>
        public const float PelletBarLengthFirstPerson = 0.30f;
        public const float PelletBarAlphaFirstPerson = 0.6f;
        public const float PelletBarStart = 0.6f;
        public const float PelletFadeDistance = 6f;
        private const float MaxLife = 1.5f;

        // A local ribbon: a hitscan round in flight, or a projectile's tail running in after it stopped.
        private struct Round
        {
            public bool Alive;
            public Vector3 Origin;
            public Vector3 Dir;
            public float Target;
            public float Head;
            public float Speed;
            public float Length;
            public float Age;
            public float Core;
            public float Glow;
            public float Bead;
            public float FadeDistance;
            public float FadeIn;
            public Color HeadColor;
            public Color TailColor;
            public bool Glint;
        }

        private static TracerRibbonRenderer s_instance;

        private readonly Round[] _rounds = new Round[Capacity];
        private readonly Matrix4x4[] _matrices = new Matrix4x4[Capacity];
        private readonly Vector4[] _head = new Vector4[Capacity];
        private readonly Vector4[] _tail = new Vector4[Capacity];
        private readonly Vector4[] _headColor = new Vector4[Capacity];
        private readonly Vector4[] _tailColor = new Vector4[Capacity];
        private readonly Vector4[] _span = new Vector4[Capacity];
        private readonly Vector4[] _extra = new Vector4[Capacity];
        private MaterialPropertyBlock _block;
        private Material _material;
        private Mesh _quad;
        private int _next;
        private int _live;
        private bool _firstPerson;
        private bool _performance;
        private float _frameSeconds;

        /// <summary>
        /// WP-087 (a) (C-00 05.10, marker alignment audit): in top-down a shot's ray runs at the WP-042 hit-parity height (1.39 m over the
        /// root) while the weapon is drawn at 1.22–1.54 m, so its tracer started 6–17 cm off the visible muzzle. The ribbon (and a pellet
        /// bar) is drawn shifted by the shot's TracerOrigin − Origin — the muzzle the controller fired from — on its first frame and
        /// eases onto the ray by its second at the latest. Presentation only: the ray, its hits and their timing are untouched. First and
        /// third person fire from the muzzle itself, so the shift is zero there.
        /// </summary>
        public static Vector3 MuzzleCatchUp(in ProjectileState p, float frameSeconds)
        {
            if (!p.Shot.HasTracerOrigin) return Vector3.zero;
            Vector3 offset = p.Shot.TracerOrigin - p.Origin;
            return offset.sqrMagnitude < 1e-8f ? Vector3.zero : offset * CatchUpWeight(p.Age, frameSeconds);
        }

        /// <summary>WP-087 (a): the share of the muzzle offset still drawn at <paramref name="age"/> — 1 on the frame the round is first
        /// drawn (its age is at most one frame), eased to 0 over the next frame (so on the ray by the second frame at the latest).</summary>
        public static float CatchUpWeight(float age, float frameSeconds)
        {
            if (frameSeconds <= 0f) return age <= 0f ? 1f : 0f;
            float x = (age - frameSeconds) / frameSeconds;
            return x <= 0f ? 1f : x >= 1f ? 0f : 1f - Mathf.SmoothStep(0f, 1f, x);
        }

        private static readonly int HeadId = Shader.PropertyToID("_Head");
        private static readonly int TailId = Shader.PropertyToID("_Tail");
        private static readonly int HeadColorId = Shader.PropertyToID("_HeadColor");
        private static readonly int TailColorId = Shader.PropertyToID("_TailColor");
        private static readonly int SpanId = Shader.PropertyToID("_Span");
        private static readonly int ExtraId = Shader.PropertyToID("_Extra");

        // AD B1 MUST 7: a full tracer's core is ≥ 3 px with a ≥ 6 px head dot; the glint and pellet bars stay 2 px.
        public const float CoreMinPx = 3f;
        public const float ThinMinPx = 2f;
        public const float HeadDotPx = 6f;

        /// <summary>Local ribbons alive (hitscan rounds + tail-outs).</summary>
        public int LiveCount => _live;

        /// <summary>Instances drawn last frame per layer (tests): core, glow, glint.</summary>
        public int DrawnCore { get; private set; }
        public int DrawnGlow { get; private set; }
        public int DrawnGlint { get; private set; }

        /// <summary>Local ribbons emitted since start-up, how many were full tracers, tail-outs started (tests).</summary>
        public int Emitted { get; private set; }
        public int EmittedTracers { get; private set; }
        public int TailOuts { get; private set; }

        public static bool Exists => s_instance != null;

        public static TracerRibbonRenderer Instance
        {
            get
            {
                if (s_instance == null && Application.isPlaying)
                {
                    var go = new GameObject("[TracerRibbons]");
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<TracerRibbonRenderer>();
                }

                return s_instance;
            }
        }

        /// <summary>Whether the ribbon shader is available (the legacy tracer / interim streak take over otherwise).</summary>
        public bool Ready => _material != null;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            Shader shader = DD.Prototype.Rendering.PrototypeShaders.IsReady ? DD.Prototype.Rendering.PrototypeShaders.VfxRibbon : null;
            if (shader != null && shader.isSupported)
            {
                _material = new Material(shader) { name = "DD_VfxRibbon (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            }

            _block = new MaterialPropertyBlock();
            _quad = BuildQuad();
            for (int i = 0; i < Capacity; i++)
            {
                _matrices[i] = Matrix4x4.identity;
            }

            ProjectileDriver.DrawInterimTrails = _material == null;
            ProjectileEvents.Impact += OnImpact;
            ProjectileEvents.Spawned += OnSpawned;
        }

        private void OnDestroy()
        {
            ProjectileEvents.Impact -= OnImpact;
            ProjectileEvents.Spawned -= OnSpawned;
            if (s_instance != this) return;
            s_instance = null;
            ProjectileDriver.DrawInterimTrails = true;
            if (_material != null) Destroy(_material);
            if (_quad != null) Destroy(_quad);
        }

        // ------------------------------------------------------------------ rhythm

        // DEC-112 Third Eye (cap.ar_2, S-02 060931e0): the burst's 3rd round carries RunTreeHooks.TreeRoundThirdEye and is always a
        // full Kor tracer (hit or miss), its glow toned the capstone's cold steel #9FB8D0 (the same tone ZombieSkinFx.Steel lights).
        [DD.Prototype.Core.Config.PrototypeOnly(DD.Prototype.Core.Config.PendingDecisions.None, "DEC-112 Third Eye trail glow #9FB8D0 × 2.0 (S-07; AD round pending).", true)]
        public const float ThirdEyeGlowIntensity = 2f;
        private static readonly Color ThirdEyeGlow = new Color(0.624f, 0.722f, 0.816f) * ThirdEyeGlowIntensity;

        /// <summary>DEC-112: the round is Third Eye's marked 3rd round (tests).</summary>
        public static bool IsThirdEyeRound(int treeFlags) => (treeFlags & DD.Prototype.Rounds.RunTreeHooks.TreeRoundThirdEye) != 0;

        /// <summary>Whether a round with this id is its class's full tracer (every Nth; 0 = never, 1 = always).</summary>
        public static bool IsTracerRound(VfxWeaponProfile profile, int roundId)
        {
            int n = profile.TracerEveryN;
            if (n <= 0) return false;
            if (n == 1) return true;
            int m = roundId % n;
            return (m < 0 ? m + n : m) == 0;
        }

        // ------------------------------------------------------------------ local ribbons

        /// <summary>
        /// A hitscan round from <paramref name="origin"/> to <paramref name="end"/> travelling at the class speed
        /// (a Special's legacy shot, tests). Returns false when the ribbon path is unavailable.
        /// </summary>
        public bool Emit(Vector3 origin, Vector3 end, VfxWeaponProfile profile, bool pellet, bool centrePellet, bool firstPerson)
        {
            if (_material == null || profile == null)
            {
                return false;
            }

            Vector3 axis = end - origin;
            float target = axis.magnitude;
            if (target < 0.05f)
            {
                return true;
            }

            Emitted++;
            bool tracer = pellet ? profile.CentreTracer && centrePellet : IsTracerRound(profile, Emitted);
            if (tracer) EmittedTracers++;
            ref Round r = ref NextRound();
            Setup(ref r, profile, pellet && !tracer, tracer);
            r.Origin = origin;
            r.Dir = axis / target;
            r.Target = target;
            r.Head = 0f;
            return true;
        }

        // The impact payload has no pellet index: the centre pellets (index 0, sg_2's tracer) are remembered at spawn.
        private readonly int[] _centrePellets = new int[16];
        private int _centreNext;

        private void OnSpawned(ProjectileSpawnInfo info)
        {
            if (info.Kind == ProjectileKind.Pellet && info.PelletIndex == 0)
            {
                _centrePellets[_centreNext] = info.ProjectileId;
                _centreNext = (_centreNext + 1) % _centrePellets.Length;
            }
        }

        private bool IsCentrePellet(int projectileId)
        {
            for (int i = 0; i < _centrePellets.Length; i++)
            {
                if (_centrePellets[i] == projectileId) return true;
            }

            return false;
        }

        // A stopped projectile: its head stays on the impact, the tail runs in (length already flown, ≤ L).
        private void OnImpact(ProjectileImpactInfo hit)
        {
            if (_material == null || !hit.Stopped || hit.Kind == ProjectileKind.Rocket || hit.Kind == ProjectileKind.Orb || hit.Kind == ProjectileKind.Gel)
            {
                return;
            }

            VfxWeaponProfile profile = VfxWeaponProfiles.For(hit.WeaponId);
            bool pellet = hit.Kind == ProjectileKind.Pellet;
            bool tracer = pellet ? profile.CentreTracer && IsCentrePellet(hit.ProjectileId) : IsTracerRound(profile, hit.ProjectileId);
            if (pellet && !tracer) return;   // a pellet bar simply ends where it hit
            ref Round r = ref NextRound();
            Setup(ref r, profile, false, tracer);
            if (!r.Alive) return;
            float length = r.Length;
            Vector3 dir = hit.Direction.sqrMagnitude > 1e-6f ? hit.Direction.normalized : Vector3.forward;
            r.Origin = hit.Position - dir * length;
            r.Dir = dir;
            r.Target = length;
            r.Head = length;
            r.Speed = Mathf.Max(20f, hit.Speed > 1f ? hit.Speed : profile.Speed);
            TailOuts++;
        }

        private ref Round NextRound()
        {
            ref Round r = ref _rounds[_next];
            _next = (_next + 1) % Capacity;
            return ref r;
        }

        private void Setup(ref Round r, VfxWeaponProfile profile, bool pelletBar, bool tracer)
        {
            bool performance = VfxTier.Current == VfxTierLevel.Performance;
            r.Alive = true;
            r.Age = 0f;
            r.Speed = Mathf.Max(20f, profile.Speed);
            r.FadeDistance = 0f;
            r.FadeIn = 0f;
            if (pelletBar)
            {
                r.Length = PelletBarLength;
                r.Core = profile.CoreWidth;
                r.Glow = 0f;
                r.Bead = 0f;
                r.Glint = false;
                r.FadeDistance = PelletFadeDistance;
                r.FadeIn = PelletBarStart;
                r.HeadColor = profile.HeadColor;
                r.TailColor = profile.TailColor;
                return;
            }

            r.Length = profile.TracerLength;
            r.Core = tracer ? profile.CoreWidth : 0f;
            r.Glow = tracer && !performance ? profile.GlowWidth : 0f;
            r.Bead = profile.HeadBead;
            r.Glint = !performance;
            r.HeadColor = profile.HeadColor * (performance ? 0.5f : 1f);
            r.TailColor = profile.TailColor * (performance ? 0.5f : 1f);
            if (!tracer && !r.Glint) r.Alive = false;
        }

        /// <summary>Drop every local ribbon (scene change, tests).</summary>
        public void Clear()
        {
            for (int i = 0; i < Capacity; i++) _rounds[i].Alive = false;
            _live = 0;
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            using var marker = s_marker.Auto();   // WP-073 §5: per-tier ms, read by ProfilerRecorder (S-10 / tier-ms.md)
            if (_material != null) ProjectileDriver.DrawInterimTrails = false;
            float dt = Time.deltaTime;
            _live = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ref Round r = ref _rounds[i];
                if (!r.Alive) continue;
                r.Age += dt;
                r.Head += r.Speed * dt;
                float tail = r.Head - r.Length;
                // Past the impact the head stays there and the tail runs in behind it.
                if (tail >= r.Target || r.Age > MaxLife || (r.FadeDistance > 0f && tail > r.FadeDistance))
                {
                    r.Alive = false;
                    continue;
                }

                _live++;
            }

            if (_material == null)
            {
                DrawnCore = DrawnGlow = DrawnGlint = 0;
                return;
            }

            _firstPerson = CameraModeService.Current == CameraMode.FirstPerson;
            _performance = VfxTier.Current == VfxTierLevel.Performance;
            _frameSeconds = dt;
            DrawnCore = DrawLayer(0);
            DrawnGlow = _performance ? 0 : DrawLayer(1);
            DrawnGlint = _performance ? 0 : DrawLayer(2);
        }

        // layer 0 core, 1 glow (× 0.35, wide), 2 glint (0.02 m cold white α 0.35).
        private int DrawLayer(int layer)
        {
            int n = 0;
            int projectiles = ProjectileSystem.LiveCount;
            for (int i = 0; i < projectiles && n < Capacity; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                if (p.Kind == ProjectileKind.Rocket || p.Kind == ProjectileKind.Orb || p.Kind == ProjectileKind.Gel) continue;   // meshes (B3 / B6)
                VfxWeaponProfile profile = VfxWeaponProfiles.For(p.WeaponId);
                bool pellet = p.Kind == ProjectileKind.Pellet;
                bool thirdEye = !pellet && IsThirdEyeRound(p.TreeFlags);
                bool tracer = pellet ? profile.CentreTracer && p.PelletIndex == 0 : thirdEye || IsTracerRound(profile, p.Generation);
                bool bar = pellet && !tracer;
                float width;
                float length;
                float fadeDistance = 0f, fadeIn = 0f, bead;
                if (bar)
                {
                    if (layer != 0) continue;
                    width = profile.CoreWidth;
                    length = _firstPerson ? PelletBarLengthFirstPerson : PelletBarLength;
                    fadeDistance = PelletFadeDistance;
                    fadeIn = PelletBarStart;
                    bead = 0f;
                    if (p.Travelled - length > fadeDistance) continue;
                }
                else
                {
                    width = layer == 0 ? (tracer ? profile.CoreWidth : 0f) : layer == 1 ? (tracer ? profile.GlowWidth : 0f) : VfxWeaponProfiles.GlintWidth;
                    length = profile.TracerLength;
                    bead = profile.HeadBead;
                }

                if (width <= 0f) continue;
                if (n >= Capacity - 1) break;
                float headD = p.Travelled;
                float tailD = Mathf.Max(0f, headD - length);
                if (fadeIn > 0f) tailD = Mathf.Max(tailD, Mathf.Min(fadeIn, headD));
                if (headD - tailD < 0.01f) continue;
                Vector3 dir = p.Direction;
                Vector3 lift = MuzzleCatchUp(p, _frameSeconds);   // WP-087 (a): from the visible muzzle onto the ray
                Vector3 h = p.Position + lift;
                Vector3 t = h - dir * (headD - tailD);
                bool steel = thirdEye && layer == 1;
                Write(n, layer, h, t, width, bead, headD, tailD, fadeDistance, fadeIn, steel ? ThirdEyeGlow : profile.HeadColor, steel ? ThirdEyeGlow : profile.TailColor, bar, false);
                n++;
                if (layer == 0 && tracer && !bar)
                {
                    Write(n, layer, h, t, width, bead, headD, tailD, fadeDistance, fadeIn, profile.HeadColor, profile.TailColor, false, true);
                    n++;
                }
            }

            for (int i = 0; i < Capacity && n < Capacity; i++)
            {
                ref Round r = ref _rounds[i];
                if (!r.Alive) continue;
                float width = layer == 0 ? r.Core : layer == 1 ? r.Glow : (r.Glint ? VfxWeaponProfiles.GlintWidth : 0f);
                if (width <= 0f) continue;
                if (n >= Capacity - 1) break;
                float headD = Mathf.Min(r.Head, r.Target);
                float tailD = Mathf.Max(Mathf.Max(0f, r.Head - r.Length), r.FadeIn > 0f ? Mathf.Min(r.FadeIn, headD) : 0f);
                if (headD - tailD < 0.01f) continue;
                bool bar = r.FadeDistance > 0f;
                Write(n, layer, r.Origin + r.Dir * headD, r.Origin + r.Dir * tailD, width, r.Bead, headD, tailD, r.FadeDistance, r.FadeIn,
                    r.HeadColor, r.TailColor, bar, false);
                n++;
                if (layer == 0 && r.Core > 0f && !bar && r.Head < r.Target)
                {
                    Write(n, layer, r.Origin + r.Dir * headD, r.Origin + r.Dir * tailD, width, r.Bead, headD, tailD, 0f, 0f,
                        r.HeadColor, r.TailColor, false, true);
                    n++;
                }
            }

            if (n == 0) return 0;
            _block.SetVectorArray(HeadId, _head);
            _block.SetVectorArray(TailId, _tail);
            _block.SetVectorArray(HeadColorId, _headColor);
            _block.SetVectorArray(TailColorId, _tailColor);
            _block.SetVectorArray(SpanId, _span);
            _block.SetVectorArray(ExtraId, _extra);
            Graphics.DrawMeshInstanced(_quad, 0, _material, _matrices, n, _block, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            return n;
        }

        private void Write(int n, int layer, Vector3 h, Vector3 t, float width, float bead, float headD, float tailD,
            float fadeDistance, float fadeIn, Color headColor, Color tailColor, bool bar, bool headDot)
        {
            float len = Mathf.Max(0.01f, headD - tailD);
            float beadShare = bead > 0f ? Mathf.Clamp01(bead / len) : 0f;
            Color hc, tc;
            float alpha;
            if (layer == 2)
            {
                hc = VfxWeaponProfiles.GlintColor;
                tc = VfxWeaponProfiles.GlintColor * 0.4f;
                alpha = VfxWeaponProfiles.GlintAlpha;
                beadShare = 0f;
            }
            else
            {
                float hdr = _performance ? 0.5f : 1f;
                hc = headColor * hdr;
                tc = tailColor * hdr;
                alpha = layer == 1 ? 0.35f : 1f;
                if (layer == 1) { hc *= 0.35f; tc *= 0.35f; beadShare *= 0.5f; }
                if (bar) { alpha = _firstPerson ? PelletBarAlphaFirstPerson : 0.8f; beadShare = 0f; }
            }

            _head[n] = new Vector4(h.x, h.y, h.z, width);
            _tail[n] = new Vector4(t.x, t.y, t.z, beadShare);
            _headColor[n] = new Vector4(hc.r, hc.g, hc.b, alpha);
            _tailColor[n] = new Vector4(tc.r, tc.g, tc.b, 0f);
            _span[n] = new Vector4(tailD, headD, fadeDistance, fadeIn);
            float minPx = layer == 0 && !bar ? CoreMinPx : ThinMinPx;
            _extra[n] = new Vector4(minPx, headDot ? HeadDotPx : 0f, _firstPerson ? 1f : 0f, headDot ? 1f : 0f);
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "DD_RibbonQuad", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(1f, -1f, 0f) };
            mesh.uv = new[] { new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            // The vertex stage places the strip in world space; the identity-matrix instances must never be culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }

        // The renderer exists from the first scene so the interim streak never shows when the shader is there.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            _ = Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
