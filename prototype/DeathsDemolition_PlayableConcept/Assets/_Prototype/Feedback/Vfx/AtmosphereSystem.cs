using System;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.Rendering;
using DD.Prototype.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B5 · Atmosphere v3 (direction §3 A3, DEC-100 Cinematic default, DEC-102 "broken clouds, high sun, realistic shafts,
    /// quality up a lot"): the air of the World v5 garden — dust and pollen hanging and glinting in the sun shafts, sakura petals and
    /// bamboo / garden leaves carried on the wind, steam lifting off the ponds and the wet slate, drips from the eaves and the bamboo.
    ///
    /// ONE SYSTEM, FIVE DRAWS. Five world-space ParticleSystems, one per layer (<see cref="Layer"/>), one shader (DD/VFX Atmo, one
    /// material per layer). The systems never emit on their own (emission and shape off): this component emits every grain with
    /// <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/> at a point it picks from the WORLD'S OWN FEATURES — sakura
    /// crowns, bamboo clumps, pond discs, the plaza slate, the roof eaves, the room interiors and S-05's shaft cards — found once when
    /// the world is up (<see cref="Rescan"/>) and weighted every half second by distance to the player, so the density is where the
    /// player is and the grains belong to what is around them. Motion is Unity's: Noise (curl, strength 0.35 m/s, 0.18, two octaves,
    /// scroll 0.12), External Forces (the one WindZone of <see cref="WindDriver"/>: a gust that bends the bamboo carries the petals),
    /// a ground collision plane for petals and leaves (they land and lie ~6 s). Light is the shader's: the cloud cookie, the sun's
    /// shadow map and S-05's shaft cards (published here as <c>_DD_AtmoShaftA/B</c>) decide where a grain glints.
    ///
    /// EYE LEVEL. In third and first person a near field (10 × 5 × 10 m, 3 m ahead of the camera) of fine motes is emitted into the
    /// mote layer — world-space, so it does not swim with the camera — and blends to interior dust within 1 s when the camera walks
    /// under a roof. Top-down has no near field; its share goes to a 12 m field round the player. Nothing is emitted within 0.6 m of
    /// the camera and the shader zeroes α within 0.35 m.
    ///
    /// TIERS (<see cref="VfxTier"/>; live grains Performance / High / Cinematic = 600 / 1 770 / 5 130, draws 4 / 5 / 5; direction §5
    /// High 1 800 / 0.60 ms, Cinematic raised under DEC-100): see <see cref="TargetLive"/>. Noise quality Low / Medium / High, soft
    /// particles off on Performance, no drips on Performance.
    ///
    /// 0 B PER FRAME. Anchors, index and weight arrays are sized at <see cref="Rescan"/>; <see cref="Step"/> touches only structs,
    /// fixed arrays and Unity calls that do not allocate (Emit(EmitParams), particleCount, SetGlobalVector/VectorArray). Tests call
    /// <see cref="Step"/> directly under the thread-filtered allocation constraint.
    ///
    /// The legacy WP-013 drift fields (<see cref="AtmosphereDrift"/>, DD_Pollen / DD_SakuraPetals) keep playing with emission 0 while
    /// this system is <see cref="Ready"/> (the shader library carries DD/VFX Atmo), and come back if it goes away.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(960)]
    public sealed class AtmosphereSystem : MonoBehaviour
    {
        private static readonly ProfilerMarker s_marker = new ProfilerMarker("WP073.Atmosphere");

        public const string ShaderName = "DD/VFX Atmo";

        /// <summary>S-05's shaft card group (WorldV5Atmosphere.ShaftGroupName, an editor assembly — mirrored here, guarded by a test).</summary>
        public const string ShaftGroupName = "Shafts (C4)";

        /// <summary>WorldV5Ground.PlazaName (editor assembly): the wet slate the steam lifts from.</summary>
        public const string PlazaName = "Plaza_Slate";

        public const int MaxShafts = 20;
        public const float CameraClear = 0.6f;
        public const float WeightRefreshSeconds = 0.5f;
        public const float RegionBlendSeconds = 1f;

        public enum Layer
        {
            Motes = 0,
            Petals = 1,
            Leaves = 2,
            Steam = 3,
            Drips = 4
        }

        public const int LayerCount = 5;

        /// <summary>Where a grain can be born. Each source feeds exactly one layer (<see cref="LayerOf"/>).</summary>
        public enum Source
        {
            Field = 0,       // motes: the open air round the player (courtyard dust-pollen)
            Shaft = 1,       // motes: inside S-05's shaft cards (the glint)
            Interior = 2,    // motes: NavRegionMarker rooms (slow dry dust, lit by the doors)
            Sakura = 3,      // petals: sakura crowns
            Bamboo = 4,      // leaves: bamboo clumps
            BroadLeaf = 5,   // leaves: momiji / garden trees
            Water = 6,       // steam: pond discs
            WetGround = 7,   // steam: the plaza slate
            Eave = 8,        // drips: roof eave lines (CameraOccluderClass Roof)
            BambooTip = 9    // drips: the upper third of the bamboo clumps
        }

        public const int SourceCount = 10;

        private enum Shape : byte
        {
            Box = 0,
            Crown = 1,
            ShaftVolume = 2,
            Ellipse = 3,
            Line = 4
        }

        private struct Anchor
        {
            public Vector3 Centre;     // box / crown centre, shaft top, ellipse centre, line start
            public Vector3 Half;       // half extents; shaft: (radius, length, 0); ellipse: (semi x, 0, semi z)
            public Vector3 Axis;       // line: start → end; shaft: light travel direction
            public float Yaw;          // ellipse yaw (rad)
            public float Ground;       // world y the drips / petals fall to
            public float Size;         // area or length: the weight before distance
            public float Peak;         // shaft peak weight
            public float LitFloor;     // shaft _MinLit
            public Transform Owner;    // shaft card (active state read per refresh; no allocation)
            public Source Source;
            public Shape Shape;
        }

        private struct SourceSet
        {
            public int[] All;          // every anchor of this source (fixed at Rescan)
            public int[] InRange;      // the anchors in range this refresh, compacted to the front (valid up to Count)
            public float[] Cum;        // cumulative distance-weighted weights, parallel to InRange
            public int Count;
            public float Total;
        }

        /// <summary>Per-layer look. Colours are the Islak Arduvaz palette (sRGB).</summary>
        private struct Look
        {
            public float LifeMin, LifeMax, SizeMin, SizeMax, Aspect, Alpha, AlphaCap, Glint, Noise, Wind, Gravity, Range, Grow, SpinDeg;
            public Color A, B;
            public byte FrameA, FrameB;
            public Vector4 Twinkle, Motion, Fade, Light;
            public bool Stretched, Collide, SortByDistance;
        }

        // ------------------------------------------------------------------ tier table (direction §5 · QUALITY-TIERS-V2 §4.2 · DEC-100)

        // Live grains per layer, [layer, tier] (Performance, High, Cinematic). High sums to the contract's 1 800 (0.60 ms);
        // Performance to 600; Cinematic is the DEC-100 "top quality inside 30 fps" density (≈ 1.7 × the r1 3 000).
        private static readonly int[,] s_live =
        {
            { 380, 1150, 3600 },   // motes (world field + shafts + interiors + near field)
            { 110,  260,  700 },   // sakura petals
            {  60,  180,  460 },   // leaves
            {  50,  150,  300 },   // steam wisps
            {   0,   30,   72 },   // drips (≈ 0 / 36 / 90 per second × 0.8 s)
        };

        // Of the motes, the near-field (TP / FP) share; top-down moves it to a 12 m field round the player.
        private static readonly int[] s_nearShare = { 100, 350, 1400 };

        // Source fractions inside a layer (renormalised over the sources that have an anchor in range).
        private static readonly float[] s_sourceFraction = { 0.55f, 0.30f, 0.15f, 1f, 0.7f, 0.3f, 0.6f, 0.4f, 0.6f, 0.4f };
        private static readonly Layer[] s_sourceLayer =
        {
            Layer.Motes, Layer.Motes, Layer.Motes, Layer.Petals, Layer.Leaves, Layer.Leaves, Layer.Steam, Layer.Steam, Layer.Drips, Layer.Drips
        };

        public static int TargetLive(Layer layer, VfxTierLevel tier) => s_live[(int)layer, (int)tier];
        public static int NearShare(VfxTierLevel tier) => s_nearShare[(int)tier];
        public static Layer LayerOf(Source source) => s_sourceLayer[(int)source];

        public static int TotalTarget(VfxTierLevel tier)
        {
            int n = 0;
            for (int i = 0; i < LayerCount; i++) n += s_live[i, (int)tier];
            return n;
        }

        /// <summary>Draw calls a tier costs (a layer with a zero target is not drawn).</summary>
        public static int DrawsFor(VfxTierLevel tier)
        {
            int n = 0;
            for (int i = 0; i < LayerCount; i++) if (s_live[i, (int)tier] > 0) n++;
            return n;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        private static readonly Look[] s_looks = BuildLooks();

        private static Look[] BuildLooks()
        {
            var looks = new Look[LayerCount];
            // Motes: #E6DCC8 dust-pollen (field / shafts), #C8BFB0 interior dust; 2–5 cm; α 0.30; rise 0.05 m/s; strong glint.
            looks[(int)Layer.Motes] = new Look
            {
                LifeMin = 8f, LifeMax = 14f, SizeMin = 0.02f, SizeMax = 0.05f, Aspect = 1f, Alpha = 0.30f, AlphaCap = 0.35f, Glint = 1f,
                Noise = 1f, Wind = 0.6f, Gravity = 0f, Range = 40f, Grow = 1f, SpinDeg = 0f,
                A = Hex(0xE6DCC8), B = Hex(0xF2EBDD), FrameA = AtmosphereAtlas.Mote, FrameB = AtmosphereAtlas.Pollen,
                Twinkle = new Vector4(3f, 7f, 0.45f, 0.45f), Motion = Vector4.zero, Fade = new Vector4(0.35f, 1.0f, 0.3f, 45f),
                Light = new Vector4(0.7f, 1.6f, 2.0f, 1f)
            };
            // Petals: #F2C4D0 young → #E8A9BF aged; 8–18 cm (Cinematic up to 20); tumble 0.6 rev/s, sway 1.2 Hz / 0.04 m; fall 0.35 m/s.
            looks[(int)Layer.Petals] = new Look
            {
                LifeMin = 10f, LifeMax = 22f, SizeMin = 0.08f, SizeMax = 0.18f, Aspect = 0.8f, Alpha = 0.85f, AlphaCap = 0.85f, Glint = 0.5f,
                Noise = 1f, Wind = 1.2f, Gravity = 0f, Range = 60f, Grow = 1f, SpinDeg = 110f,
                A = Hex(0xF2C4D0), B = Hex(0xE8A9BF), FrameA = AtmosphereAtlas.PetalYoung, FrameB = AtmosphereAtlas.PetalAged,
                Twinkle = new Vector4(0f, 0f, 0f, 0.8f), Motion = new Vector4(0.6f, 1.2f, 0.04f, 0f), Fade = new Vector4(0.35f, 1.2f, 0.3f, 60f),
                Light = new Vector4(0.75f, 1.35f, 1.4f, 1f), Collide = true
            };
            // Leaves: bamboo #6E7F55 (moss) → dry #9A8F6A; 0.10 × 0.03 slivers + broad leaves; flutter 1.5 rev/s, noise × 1.6.
            looks[(int)Layer.Leaves] = new Look
            {
                LifeMin = 9f, LifeMax = 18f, SizeMin = 0.07f, SizeMax = 0.11f, Aspect = 1f, Alpha = 0.85f, AlphaCap = 0.85f, Glint = 0.35f,
                Noise = 1.6f, Wind = 1.4f, Gravity = 0f, Range = 55f, Grow = 1f, SpinDeg = 200f,
                A = Hex(0x6E7F55), B = Hex(0x9A8F6A), FrameA = AtmosphereAtlas.LeafSliver, FrameB = AtmosphereAtlas.LeafBroad,
                Twinkle = new Vector4(0f, 0f, 0f, 0.8f), Motion = new Vector4(1.5f, 0f, 0f, 0f), Fade = new Vector4(0.35f, 1.2f, 0.3f, 55f),
                Light = new Vector4(0.7f, 1.3f, 1.3f, 1f), Collide = true
            };
            // Steam: #D0D7DE α 0.06–0.12, 0.6–1.2 m growing × 2.2, rise 0.18 m/s; shows where the sun warms the wet stone.
            looks[(int)Layer.Steam] = new Look
            {
                LifeMin = 5f, LifeMax = 8f, SizeMin = 0.6f, SizeMax = 1.2f, Aspect = 1f, Alpha = 0.10f, AlphaCap = 0.12f, Glint = 0.25f,
                Noise = 0.5f, Wind = 0.5f, Gravity = 0f, Range = 45f, Grow = 2.2f, SpinDeg = 12f,
                A = Hex(0xD0D7DE), B = Hex(0xDCE1E6), FrameA = AtmosphereAtlas.Wisp, FrameB = AtmosphereAtlas.Wisp,
                Twinkle = new Vector4(0f, 0f, 0f, 0.6f), Motion = Vector4.zero, Fade = new Vector4(1.5f, 3.0f, 0.6f, 45f),
                Light = new Vector4(0.8f, 1.2f, 1.2f, 0.35f), SortByDistance = true
            };
            // Drips: #DDE3E8 α 0.30, 8 mm streaks falling under gravity from the eaves / bamboo tips; bright in the sun.
            looks[(int)Layer.Drips] = new Look
            {
                LifeMin = 0.5f, LifeMax = 1.1f, SizeMin = 0.006f, SizeMax = 0.010f, Aspect = 1f, Alpha = 0.30f, AlphaCap = 0.35f, Glint = 1.2f,
                Noise = 0f, Wind = 0.1f, Gravity = 1f, Range = 25f, Grow = 1f, SpinDeg = 0f,
                A = Hex(0xDDE3E8), B = Hex(0xEEF2F5), FrameA = AtmosphereAtlas.Drip, FrameB = AtmosphereAtlas.Drip,
                Twinkle = new Vector4(5f, 9f, 0.6f, 0.5f), Motion = new Vector4(0f, 0f, 0f, 1f), Fade = new Vector4(0.35f, 1.0f, 0.1f, 25f),
                Light = new Vector4(0.7f, 1.8f, 2.4f, 1f), Stretched = true
            };
            return looks;
        }

        private static readonly Color s_interiorDust = Hex(0xC8BFB0);
        private static readonly Color s_nearMote = Hex(0xEDE6D8);

        // ------------------------------------------------------------------ shader globals

        private static readonly int GuardId = Shader.PropertyToID("_DD_AtmoGuard");
        private static readonly int ShaftCountId = Shader.PropertyToID("_DD_AtmoShaftCount");
        private static readonly int ShaftAId = Shader.PropertyToID("_DD_AtmoShaftA");
        private static readonly int ShaftBId = Shader.PropertyToID("_DD_AtmoShaftB");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int AlphaId = Shader.PropertyToID("_AtmoAlpha");
        private static readonly int GlintId = Shader.PropertyToID("_AtmoGlint");
        private static readonly int TwinkleId = Shader.PropertyToID("_AtmoTwinkle");
        private static readonly int MotionId = Shader.PropertyToID("_AtmoMotion");
        private static readonly int FadeId = Shader.PropertyToID("_AtmoFade");
        private static readonly int LightId = Shader.PropertyToID("_AtmoLight");
        private static readonly int PxId = Shader.PropertyToID("_AtmoPx");
        private const string SoftKeyword = "_DD_ATMO_SOFT";

        /// <summary>The vertex streams DD/VFX Atmo reads, in its TEXCOORD packing order.</summary>
        public static readonly ParticleSystemVertexStream[] Streams =
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.Center, ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent,
            ParticleSystemVertexStream.Rotation, ParticleSystemVertexStream.SizeXYZ
        };

        // ------------------------------------------------------------------ state

        public static AtmosphereSystem Active { get; private set; }

        private readonly ParticleSystem[] _systems = new ParticleSystem[LayerCount];
        private readonly Material[] _materials = new Material[LayerCount];
        private readonly float[] _accumulator = new float[LayerCount];
        private readonly bool[] _prefill = new bool[LayerCount];
        private readonly int[] _caps = new int[LayerCount];
        private readonly SourceSet[] _sets = new SourceSet[SourceCount];
        private readonly Vector4[] _shaftA = new Vector4[MaxShafts];
        private readonly Vector4[] _shaftB = new Vector4[MaxShafts];
        private readonly int[] _emitted = new int[LayerCount];

        private Anchor[] _anchors = new Anchor[0];
        private AtmosphereDrift[] _legacy = new AtmosphereDrift[0];
        private Material _base;
        private Transform _groundPlane;
        private ParticleSystem.EmitParams _ep;
        private VfxTierLevel _tier;
        private bool _tierApplied;
        private bool _scanned;
        private float _weightClock;
        private Vector3 _weightFocus;
        private int _shaftsDirtyFrames;
        private int _activeShafts;
        private float _interiorBlend;
        private Vector3 _lightAxis = Vector3.down;
        private uint _rng = 0x9E3779B9u;

        /// <summary>Tests / captures: the camera to follow (default Camera.main) and a fixed focus (default the player, else the camera).</summary>
        public Camera CameraOverride;
        public Vector3? FocusOverride;

        /// <summary>True when DD/VFX Atmo came from the shader library and is supported (otherwise the legacy drift fields stay on).</summary>
        public bool Ready => _base != null;

        public VfxTierLevel Tier => _tier;
        public int ActiveShafts => _activeShafts;
        public int LiveCount(Layer layer) => _systems[(int)layer] != null ? _systems[(int)layer].particleCount : 0;
        public int Emitted(Layer layer) => _emitted[(int)layer];
        public int CapOf(Layer layer) => _caps[(int)layer];
        public ParticleSystem SystemOf(Layer layer) => _systems[(int)layer];
        public int AnchorCount(Source source) => _sets[(int)source].All != null ? _sets[(int)source].All.Length : 0;

        public int TotalLive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < LayerCount; i++) n += LiveCount((Layer)i);
                return n;
            }
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Puts the one atmosphere system under <paramref name="parent"/> (the world's "Atmosphere" group; WorldAtmosphere.Build /
        /// BuildFor call it after the legacy drift). Idempotent: an existing system is returned.
        /// </summary>
        public static AtmosphereSystem Attach(Transform parent)
        {
            if (Active != null) return Active;
            var go = new GameObject("[Atmosphere v3 (WP-073 B5)]");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<AtmosphereSystem>();
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            Active = this;
            Shader shader = PrototypeShaders.IsReady ? PrototypeShaders.VfxAtmo : null;
            if (shader == null || !shader.isSupported) return;

            _base = new Material(shader) { name = "DD_VfxAtmo (runtime)", hideFlags = HideFlags.DontSave };
            _base.SetTexture(MainTexId, AtmosphereAtlas.Texture);

            var ground = new GameObject("AtmoGroundPlane");
            ground.transform.SetParent(transform, false);
            _groundPlane = ground.transform;

            var streams = new List<ParticleSystemVertexStream>(Streams);   // start-up only
            for (int i = 0; i < LayerCount; i++) _systems[i] = CreateLayer((Layer)i, streams);
        }

        private void Start()
        {
            if (Active != this || !Ready) return;
            Rescan();
            _legacy = FindObjectsOfType<AtmosphereDrift>();
            for (int i = 0; i < _legacy.Length; i++) if (_legacy[i] != null) _legacy[i].SetEnabledScale(0f);
        }

        private void OnDestroy()
        {
            if (Active != this) return;   // a rejected duplicate owns nothing
            for (int i = 0; i < _legacy.Length; i++) if (_legacy[i] != null) _legacy[i].SetEnabledScale(1f);
            for (int i = 0; i < LayerCount; i++) if (_materials[i] != null) Destroy(_materials[i]);
            if (_base != null) Destroy(_base);
            Shader.SetGlobalFloat(ShaftCountId, 0f);
            Shader.SetGlobalVector(GuardId, Vector4.zero);
            if (Active == this) Active = null;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        // ------------------------------------------------------------------ layer setup (start-up only)

        private ParticleSystem CreateLayer(Layer layer, List<ParticleSystemVertexStream> streams)
        {
            Look look = s_looks[(int)layer];
            var go = new GameObject("Atmo_" + layer);

            // Inactive until every module is written (WorldAtmosphere.MakeDrift: a live system rejects duration / maxParticles).
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize3D = true;
            main.startLifetime = look.LifeMax;
            main.gravityModifier = look.Gravity;
            main.maxParticles = Mathf.CeilToInt(s_live[(int)layer, (int)VfxTierLevel.Cinematic] * 1.25f) + 8;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = look.Noise > 0f;
            noise.separateAxes = false;
            noise.strength = new ParticleSystem.MinMaxCurve(0.35f * look.Noise);
            noise.frequency = 0.18f;
            noise.octaveCount = 2;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.12f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.High;

            ParticleSystem.ExternalForcesModule forces = ps.externalForces;
            forces.enabled = true;
            forces.multiplier = look.Wind;

            // Fade in over the first 12 %, out over the last 10 % (petals / leaves: the last 8 %, they lie on the ground before).
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            float outAt = look.Collide ? 0.92f : layer == Layer.Steam ? 0.6f : 0.9f;
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, layer == Layer.Drips ? 0.02f : 0.12f), new GradientAlphaKey(1f, outAt), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            if (look.Grow > 1f)
            {
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.separateAxes = true;   // z carries the atlas frame: it must stay 1
                var grow = new ParticleSystem.MinMaxCurve(look.Grow, AnimationCurve.EaseInOut(0f, 1f / look.Grow, 1f, 1f));
                size.x = grow;
                size.y = grow;
                size.z = new ParticleSystem.MinMaxCurve(1f);
            }

            if (look.Collide)
            {
                ParticleSystem.CollisionModule collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.Planes;
                collision.SetPlane(0, _groundPlane);
                collision.dampen = new ParticleSystem.MinMaxCurve(0.92f);
                collision.bounce = new ParticleSystem.MinMaxCurve(0f);
                collision.lifetimeLoss = new ParticleSystem.MinMaxCurve(0f);
                collision.radiusScale = 0.3f;
                collision.sendCollisionMessages = false;
            }

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = look.Stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (look.Stretched)
            {
                psr.velocityScale = 0.045f;
                psr.lengthScale = 1.2f;
            }

            psr.alignment = ParticleSystemRenderSpace.View;
            psr.sortMode = look.SortByDistance ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
            psr.minParticleSize = 0f;
            psr.maxParticleSize = 0.5f;   // the shader clamps to 24 px; this is only the CPU-side safety net
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.lightProbeUsage = LightProbeUsage.Off;
            psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            psr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            psr.sortingFudge = layer == Layer.Steam ? 10f : 0f;   // steam behind the grains it lifts past
            psr.SetActiveVertexStreams(streams);

            var material = new Material(_base) { name = "DD_VfxAtmo_" + layer + " (runtime)", hideFlags = HideFlags.DontSave };
            material.SetFloat(AlphaId, look.AlphaCap);
            material.SetFloat(GlintId, look.Glint);
            material.SetVector(TwinkleId, look.Twinkle);
            material.SetVector(MotionId, look.Motion);
            material.SetVector(FadeId, look.Fade);
            material.SetVector(LightId, look.Light);
            material.SetVector(PxId, new Vector4(1.5f, 24f, 0f, 0f));
            psr.sharedMaterial = material;
            _materials[(int)layer] = material;

            go.SetActive(true);
            return ps;
        }

        private void ApplyTier(VfxTierLevel tier)
        {
            bool raised = !_tierApplied || tier > _tier;
            _tier = tier;
            _tierApplied = true;
            for (int i = 0; i < LayerCount; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps == null) continue;
                int target = s_live[i, (int)tier];
                _caps[i] = target == 0 ? 0 : Mathf.CeilToInt(target * 1.2f) + 4;

                ParticleSystem.MainModule main = ps.main;
                main.maxParticles = Mathf.Max(1, _caps[i]);
                // Lowering maxParticles does not kill live grains: a step down (a settings change, rare) clears the layer and
                // prefills it at the new tier, so the cut takes effect the same frame.
                if (target == 0) ps.Clear();
                else if (!raised && ps.particleCount > _caps[i]) { ps.Clear(); _prefill[i] = true; }

                ParticleSystem.NoiseModule noise = ps.noise;
                noise.quality = tier == VfxTierLevel.Cinematic ? ParticleSystemNoiseQuality.High
                    : tier == VfxTierLevel.High ? ParticleSystemNoiseQuality.Medium : ParticleSystemNoiseQuality.Low;

                if (tier == VfxTierLevel.Performance) _materials[i].DisableKeyword(SoftKeyword);
                else _materials[i].EnableKeyword(SoftKeyword);

                if (raised) _prefill[i] = true;
            }

            _shaftsDirtyFrames = 2;   // AtmosphereTierGroup re-cuts the shaft cards on the same change
        }

        // ------------------------------------------------------------------ world scan (start-up / world change only; allocates)

        /// <summary>Finds the anchors in the loaded world: crowns, clumps, ponds, slate, eaves, rooms, shaft cards. Allocates; never per frame.</summary>
        public void Rescan()
        {
            var list = new List<Anchor>(256);
            ScanRenderers(list);
            ScanRoofs(list);
            ScanInteriors(list);
            ScanShafts(list);
            _anchors = list.ToArray();

            var counts = new int[SourceCount];
            for (int i = 0; i < _anchors.Length; i++) counts[(int)_anchors[i].Source]++;
            for (int s = 0; s < SourceCount; s++)
            {
                _sets[s].All = new int[counts[s]];
                _sets[s].InRange = new int[counts[s]];
                _sets[s].Cum = new float[counts[s]];
                _sets[s].Count = 0;
                counts[s] = 0;
            }

            for (int i = 0; i < _anchors.Length; i++)
            {
                int s = (int)_anchors[i].Source;
                _sets[s].All[counts[s]++] = i;
            }

            _scanned = true;
            _weightClock = 0f;
            _shaftsDirtyFrames = 1;
        }

        private static bool Has(string text, string token) => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static Transform OwnerOf(Transform t)
        {
            // The prefab instance that carries the LODGroup (AP-023 trees and clumps), else the renderer itself.
            Transform p = t;
            for (int depth = 0; depth < 4 && p != null; depth++, p = p.parent)
                if (p.GetComponent<LODGroup>() != null) return p;
            return t;
        }

        private void ScanRenderers(List<Anchor> list)
        {
            var byOwner = new Dictionary<int, int>(256);
            var bounds = new List<Bounds>(256);
            var sources = new List<Source>(256);
            var owners = new List<Transform>(256);

            Renderer[] all = FindObjectsOfType<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Transform owner = OwnerOf(r.transform);
                string name = owner.name;
                if (Has(name, "Vista")) continue;   // the distant forest band is outside every range anyway
                string mat = r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;

                Source source;
                if (Has(name, "sakura") || Has(mat, "sakura")) source = Source.Sakura;
                else if (Has(name, "bamboo") && !Has(name, "fence")) source = Source.Bamboo;
                else if (Has(name, "momiji") || Has(name, "maple")) source = Source.BroadLeaf;
                else if ((name.StartsWith("Pond", StringComparison.Ordinal) && name.EndsWith("_Water", StringComparison.Ordinal)) || Has(mat, "water")) source = Source.Water;
                else if (name == PlazaName) source = Source.WetGround;
                else continue;

                int id = owner.GetInstanceID();
                if (byOwner.TryGetValue(id, out int at))
                {
                    Bounds b = bounds[at];
                    b.Encapsulate(r.bounds);
                    bounds[at] = b;
                    continue;
                }

                byOwner[id] = bounds.Count;
                bounds.Add(r.bounds);
                sources.Add(source);
                owners.Add(owner);
            }

            for (int i = 0; i < bounds.Count; i++)
            {
                Bounds b = bounds[i];
                Vector3 e = b.extents;
                switch (sources[i])
                {
                    case Source.Sakura:
                    case Source.BroadLeaf:
                        // The crown: the upper 60 % of the tree's bounds; petals / leaves fall to its base.
                        list.Add(new Anchor
                        {
                            Source = sources[i], Shape = Shape.Crown, Centre = new Vector3(b.center.x, b.center.y + 0.3f * e.y, b.center.z),
                            Half = new Vector3(e.x * 0.85f, e.y * 0.5f, e.z * 0.85f), Ground = b.min.y, Size = Mathf.Max(1f, e.x * e.z)
                        });
                        break;

                    case Source.Bamboo:
                        list.Add(new Anchor
                        {
                            Source = Source.Bamboo, Shape = Shape.Crown, Centre = new Vector3(b.center.x, b.center.y + 0.25f * e.y, b.center.z),
                            Half = new Vector3(e.x, e.y * 0.6f, e.z), Ground = b.min.y, Size = Mathf.Max(1f, e.x * e.z)
                        });
                        list.Add(new Anchor
                        {
                            Source = Source.BambooTip, Shape = Shape.Box, Centre = new Vector3(b.center.x, b.max.y - e.y * 0.4f, b.center.z),
                            Half = new Vector3(e.x * 0.9f, e.y * 0.3f, e.z * 0.9f), Ground = b.min.y, Size = Mathf.Max(0.5f, e.x * e.z)
                        });
                        break;

                    case Source.Water:
                    {
                        // The greybox pond is a unit disc scaled to the ellipse (WorldV5Dress.Ponds); any other water uses its bounds.
                        Transform t = owners[i];
                        bool disc = t.name.StartsWith("Pond", StringComparison.Ordinal);
                        Vector3 s = t.lossyScale;
                        list.Add(new Anchor
                        {
                            Source = Source.Water, Shape = Shape.Ellipse, Centre = new Vector3(b.center.x, b.max.y, b.center.z),
                            Half = disc ? new Vector3(s.x * 0.5f, 0f, s.z * 0.5f) : new Vector3(e.x, 0f, e.z),
                            Yaw = disc ? t.eulerAngles.y * Mathf.Deg2Rad : 0f, Ground = b.max.y, Size = Mathf.Max(1f, e.x * e.z)
                        });
                        break;
                    }

                    case Source.WetGround:
                        list.Add(new Anchor
                        {
                            Source = Source.WetGround, Shape = Shape.Box, Centre = new Vector3(b.center.x, b.max.y + 0.2f, b.center.z),
                            Half = new Vector3(e.x, 0.15f, e.z), Ground = b.max.y, Size = Mathf.Max(1f, e.x * e.z)
                        });
                        break;
                }
            }
        }

        private void ScanRoofs(List<Anchor> list)
        {
            CameraOccluderClass[] roofs = FindObjectsOfType<CameraOccluderClass>();
            for (int i = 0; i < roofs.Length; i++)
            {
                CameraOccluderClass roof = roofs[i];
                if (roof == null || roof.Kind != CameraOccluderKind.Roof) continue;
                var box = roof.GetComponent<BoxCollider>();
                if (box == null) continue;

                // The eave rectangle: the collider box's bottom face, in the roof's own (possibly yawed) frame.
                Transform t = roof.transform;
                Vector3 c = box.center, h = box.size * 0.5f;
                float y = c.y - h.y;
                Vector3 p0 = t.TransformPoint(new Vector3(c.x - h.x, y, c.z - h.z));
                Vector3 p1 = t.TransformPoint(new Vector3(c.x + h.x, y, c.z - h.z));
                Vector3 p2 = t.TransformPoint(new Vector3(c.x + h.x, y, c.z + h.z));
                Vector3 p3 = t.TransformPoint(new Vector3(c.x - h.x, y, c.z + h.z));
                AddEave(list, p0, p1);
                AddEave(list, p1, p2);
                AddEave(list, p2, p3);
                AddEave(list, p3, p0);
            }
        }

        private static void AddEave(List<Anchor> list, Vector3 a, Vector3 b)
        {
            Vector3 mid = (a + b) * 0.5f;
            float ground = mid.y - 3f;
            // One downward ray from just below the eave (start-up only; RaycastHit is a struct, no allocation).
            if (Physics.Raycast(mid + Vector3.down * 0.1f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)) ground = hit.point.y;
            float length = Vector3.Distance(a, b);
            if (length < 0.5f || mid.y - ground < 0.8f) return;
            list.Add(new Anchor { Source = Source.Eave, Shape = Shape.Line, Centre = a, Axis = b - a, Ground = ground, Size = length });
        }

        private void ScanInteriors(List<Anchor> list)
        {
            NavRegionMarker[] markers = FindObjectsOfType<NavRegionMarker>();
            for (int i = 0; i < markers.Length; i++)
            {
                NavRegionVolume v = markers[i] != null ? markers[i].Volume : null;
                if (v == null || v.kind != NavRegionKind.Room) continue;
                Vector3 size = v.size;
                list.Add(new Anchor
                {
                    Source = Source.Interior, Shape = Shape.Box, Centre = v.center + Vector3.up * 1.6f,
                    Half = new Vector3(size.x * 0.45f, 1.3f, size.z * 0.45f), Ground = v.center.y, Size = Mathf.Max(1f, size.x * size.z)
                });
            }
        }

        private void ScanShafts(List<Anchor> list)
        {
            GameObject group = GameObject.Find(ShaftGroupName);
            if (group == null) return;
            Transform g = group.transform;
            for (int i = 0; i < g.childCount && i < MaxShafts * 2; i++)
            {
                Transform card = g.GetChild(i);
                var r = card.GetComponent<MeshRenderer>();
                Material m = r != null ? r.sharedMaterial : null;
                float peak = m != null && m.HasProperty("_PeakAlpha") ? m.GetFloat("_PeakAlpha") : 0.2f;
                float litFloor = m != null && m.HasProperty("_MinLit") ? m.GetFloat("_MinLit") : 0f;
                Vector3 s = card.localScale;   // x = width, y = length (WorldV5Atmosphere.Piece)
                list.Add(new Anchor
                {
                    Source = Source.Shaft, Shape = Shape.ShaftVolume, Centre = card.position, Half = new Vector3(s.x * 0.5f, s.y, 0f),
                    Axis = Vector3.down, Peak = Mathf.Clamp01(peak / 0.22f), LitFloor = litFloor, Owner = card, Size = s.x * s.y
                });
            }
        }

        // ------------------------------------------------------------------ per frame (0 B)

        /// <summary>One frame: tier, weights (every 0.5 s), shaft publish (on tier change), player guard, emission for every layer.</summary>
        public void Step(float dt)
        {
            using (s_marker.Auto())
            {
                if (!Ready || dt <= 0f) return;
                VfxTierLevel tier = VfxTier.Current;
                if (!_tierApplied || tier != _tier) ApplyTier(tier);
                if (!_scanned) return;

                Camera cam = CameraOverride != null ? CameraOverride : Camera.main;
                if (cam == null) return;
                Transform ct = cam.transform;
                Vector3 camPos = ct.position, camFwd = ct.forward;
                CameraMode mode = CameraModeService.Current;
                Transform player = EnemyTargeting.Player;
                Vector3 focus = FocusOverride ?? (player != null ? player.position : camPos + camFwd * 6f);

                if (_shaftsDirtyFrames > 0 && --_shaftsDirtyFrames == 0) PublishShafts();

                _weightClock -= dt;
                if (_weightClock <= 0f || (focus - _weightFocus).sqrMagnitude > 25f) RefreshWeights(focus);

                // The ground plane petals / leaves land on follows the player's floor.
                Vector3 gp = _groundPlane.position;
                if (Mathf.Abs(gp.y - focus.y) > 0.25f) _groundPlane.position = new Vector3(0f, focus.y + 0.02f, 0f);

                // Player guard: chest point + radius; off in first person (the camera is the head).
                if (player != null && mode != CameraMode.FirstPerson)
                {
                    Vector3 chest = player.position + Vector3.up * 1.1f;
                    Shader.SetGlobalVector(GuardId, new Vector4(chest.x, chest.y, chest.z, mode == CameraMode.TopDown ? 0.9f : 0.7f));
                }
                else
                {
                    Shader.SetGlobalVector(GuardId, Vector4.zero);
                }

                // Interior blend for the near field (1 s ease), from the camera's own position.
                bool inside = InsideInterior(camPos);
                _interiorBlend = Mathf.MoveTowards(_interiorBlend, inside ? 1f : 0f, dt / RegionBlendSeconds);

                for (int i = 0; i < LayerCount; i++) EmitLayer((Layer)i, dt, focus, camPos, camFwd, mode);
            }
        }

        private bool InsideInterior(Vector3 p)
        {
            int[] all = _sets[(int)Source.Interior].All;
            if (all == null) return false;
            for (int k = 0; k < all.Length; k++)
            {
                Anchor a = _anchors[all[k]];
                Vector3 d = p - a.Centre;
                if (Mathf.Abs(d.x) <= a.Half.x && Mathf.Abs(d.z) <= a.Half.z && d.y > -2.5f && d.y < 2.5f) return true;
            }

            return false;
        }

        private void RefreshWeights(Vector3 focus)
        {
            _weightClock = WeightRefreshSeconds;
            _weightFocus = focus;
            for (int s = 0; s < SourceCount; s++)
            {
                int[] all = _sets[s].All;
                if (all == null) continue;
                float range = s_looks[(int)s_sourceLayer[s]].Range;
                float total = 0f;
                int n = 0;
                int[] inRange = _sets[s].InRange;
                float[] cum = _sets[s].Cum;
                for (int k = 0; k < all.Length; k++)
                {
                    Anchor a = _anchors[all[k]];
                    if (a.Owner != null && !a.Owner.gameObject.activeInHierarchy) continue;   // a shaft card cut by the tier
                    float d = DistanceXZ(a, focus);
                    if (d > range) continue;
                    // Area / length × distance falloff (half weight at 20 m); a shaft counts 6 × its peak (the glint lives there).
                    float w = a.Size * (a.Source == Source.Shaft ? 6f * a.Peak : 1f) / (1f + (d / 20f) * (d / 20f));
                    total += w;
                    inRange[n] = all[k];
                    cum[n] = total;
                    n++;
                }

                _sets[s].Count = n;
                _sets[s].Total = total;
            }
        }

        private static float DistanceXZ(Anchor a, Vector3 p)
        {
            Vector3 c = a.Shape == Shape.Line ? a.Centre + a.Axis * 0.5f : a.Centre;
            float dx = c.x - p.x, dz = c.z - p.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void PublishShafts()
        {
            CloudShadowDriver cloud = CloudShadowDriver.Active;
            Light sun = cloud != null ? cloud.Sun : RenderSettings.sun;
            _lightAxis = sun != null ? sun.transform.forward : Vector3.down;

            int n = 0;
            if (_sets[(int)Source.Shaft].All != null)
            {
                for (int k = 0; k < _anchors.Length && n < MaxShafts; k++)
                {
                    if (_anchors[k].Source != Source.Shaft) continue;
                    _anchors[k].Axis = _lightAxis;
                    Transform owner = _anchors[k].Owner;
                    if (owner == null || !owner.gameObject.activeInHierarchy) continue;
                    Anchor a = _anchors[k];
                    _shaftA[n] = new Vector4(a.Centre.x, a.Centre.y, a.Centre.z, a.Half.x);
                    _shaftB[n] = new Vector4(a.Half.y, a.Peak, a.LitFloor, 0f);
                    n++;
                }
            }

            for (int i = n; i < MaxShafts; i++)
            {
                _shaftA[i] = Vector4.zero;
                _shaftB[i] = Vector4.zero;
            }

            _activeShafts = n;
            Shader.SetGlobalVectorArray(ShaftAId, _shaftA);   // always the full 20: the global array size locks on first set
            Shader.SetGlobalVectorArray(ShaftBId, _shaftB);
            Shader.SetGlobalFloat(ShaftCountId, n);
            _weightClock = 0f;   // the in-range shaft set changed
        }

        private void EmitLayer(Layer layer, float dt, Vector3 focus, Vector3 camPos, Vector3 camFwd, CameraMode mode)
        {
            int li = (int)layer;
            ParticleSystem ps = _systems[li];
            int target = s_live[li, (int)_tier];
            if (ps == null || target == 0) return;

            Look look = s_looks[li];
            int live = ps.particleCount;
            float meanLife = 0.5f * (look.LifeMin + look.LifeMax);
            if (look.Collide) meanLife += 6f;   // petals / leaves: the fall is added per grain; the mean fall is ≈ the rest time
            _accumulator[li] += target / meanLife * dt;
            int n = (int)_accumulator[li];
            _accumulator[li] -= n;

            bool prefill = _prefill[li];
            if (prefill) n = Mathf.Max(n, target - live);
            n = Mathf.Min(n, Mathf.Max(0, _caps[li] - live));
            n = Mathf.Min(n, prefill ? 1500 : 96);   // a Cinematic prefill spreads over ≤ 3 frames
            if (prefill) _prefill[li] = live + n < target;

            int nearShare = layer == Layer.Motes ? s_nearShare[(int)_tier] : 0;
            float nearFraction = target > 0 ? (float)nearShare / target : 0f;
            for (int k = 0; k < n; k++)
            {
                if (nearFraction > 0f && Next01() < nearFraction)
                {
                    EmitNear(li, ps, look, focus, camPos, camFwd, mode, prefill);
                    continue;
                }

                int source = PickSource(layer);
                if (source < 0) break;   // nothing of this layer within range
                EmitFrom(li, ps, look, source, prefill);
            }
        }

        private int PickSource(Layer layer)
        {
            // The open-air field needs no anchor: it is always available to the motes.
            float total = 0f;
            for (int s = 0; s < SourceCount; s++)
                if (s_sourceLayer[s] == layer && (s == (int)Source.Field || _sets[s].Count > 0)) total += s_sourceFraction[s];
            if (total <= 0f) return -1;   // no crown / clump / pond / eave of this layer within range

            float r = Next01() * total;
            for (int s = 0; s < SourceCount; s++)
            {
                if (s_sourceLayer[s] != layer || (s != (int)Source.Field && _sets[s].Count == 0)) continue;
                r -= s_sourceFraction[s];
                if (r <= 0f) return s;
            }

            return layer == Layer.Motes ? (int)Source.Field : -1;
        }

        private int PickAnchor(int source)
        {
            SourceSet set = _sets[source];
            if (set.Count == 0) return -1;
            float r = Next01() * set.Total;
            int lo = 0, hi = set.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (set.Cum[mid] < r) lo = mid + 1;
                else hi = mid;
            }

            return set.InRange[lo];
        }

        private void EmitFrom(int li, ParticleSystem ps, Look look, int source, bool prefill)
        {
            Vector3 wind = WorldWind.Direction * WorldWind.Strength;
            Color colour = Color.Lerp(look.A, look.B, Next01());
            byte frame = Next01() < 0.5f ? look.FrameA : look.FrameB;
            float size = Mathf.Lerp(look.SizeMin, look.SizeMax, Next01());
            float life = Mathf.Lerp(look.LifeMin, look.LifeMax, Next01());
            float aspect = look.Aspect;
            Vector3 pos, vel;

            if (source == (int)Source.Field)
            {
                // The open-air field: a box round the focus (the player), 0.3–6 m up.
                Vector3 f = _weightFocus;
                pos = new Vector3(f.x + Signed() * 30f, f.y + Mathf.Lerp(0.3f, 6f, Next01()), f.z + Signed() * 30f);
                vel = new Vector3(Signed() * 0.03f, 0.05f + Signed() * 0.02f, Signed() * 0.03f) + wind * 0.1f;
                Emit(li, ps, pos, vel, size, aspect, frame, colour, look, life, prefill);
                return;
            }

            int ai = PickAnchor(source);
            if (ai < 0) return;
            Anchor a = _anchors[ai];

            switch (a.Shape)
            {
                case Shape.ShaftVolume:
                {
                    Vector3 axis = a.Axis;
                    Vector3 side = Vector3.Cross(axis, Vector3.up);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
                    side.Normalize();
                    Vector3 up = Vector3.Cross(side, axis);
                    float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * a.Half.x;
                    pos = a.Centre + axis * (Next01() * a.Half.y) + side * (Mathf.Cos(ang) * rad) + up * (Mathf.Sin(ang) * rad);
                    vel = new Vector3(Signed() * 0.02f, 0.03f, Signed() * 0.02f) + wind * 0.06f;
                    colour = Color.Lerp(look.B, Color.white, 0.2f);
                    frame = AtmosphereAtlas.Pollen;
                    break;
                }

                case Shape.Crown:
                    pos = a.Centre + Vector3.Scale(a.Half, new Vector3(Signed(), Signed(), Signed()));
                    if (a.Source == Source.Sakura)
                    {
                        vel = new Vector3(Signed() * 0.08f, -0.35f + Signed() * 0.1f, Signed() * 0.08f) + wind * 0.3f;
                        life = FallLife(pos.y - a.Ground, 0.35f, 6f, look);
                    }
                    else
                    {
                        // Leaves: bamboo slivers 10:3 from the clumps, broad leaves from the garden trees.
                        bool bamboo = a.Source == Source.Bamboo;
                        frame = bamboo ? AtmosphereAtlas.LeafSliver : AtmosphereAtlas.LeafBroad;
                        aspect = bamboo ? 10f / 3f : 1f;
                        size = bamboo ? Mathf.Lerp(0.03f, 0.035f, Next01()) : size;   // y size; x = size × aspect (0.10–0.12 m)
                        if (!bamboo) colour = Color.Lerp(new Color(0.494f, 0.369f, 0.314f), look.B, Next01() * 0.5f);   // #7E5E50 muted momiji
                        vel = new Vector3(Signed() * 0.1f, -0.45f + Signed() * 0.15f, Signed() * 0.1f) + wind * 0.35f;
                        life = FallLife(pos.y - a.Ground, 0.45f, 4f, look);
                    }

                    break;

                case Shape.Ellipse:
                {
                    float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01());
                    float x = Mathf.Cos(ang) * rad * a.Half.x, z = Mathf.Sin(ang) * rad * a.Half.z;
                    float cy = Mathf.Cos(a.Yaw), sy = Mathf.Sin(a.Yaw);
                    pos = a.Centre + new Vector3(x * cy + z * sy, Mathf.Lerp(0.05f, 0.4f, Next01()), -x * sy + z * cy);
                    vel = new Vector3(Signed() * 0.05f, 0.18f + Signed() * 0.05f, Signed() * 0.05f) + wind * 0.1f;
                    break;
                }

                case Shape.Line:
                {
                    pos = a.Centre + a.Axis * Next01() + Vector3.down * 0.03f;
                    vel = new Vector3(0f, -0.2f, 0f);
                    life = Mathf.Sqrt(2f * Mathf.Max(0.2f, pos.y - a.Ground) / 9.81f) + 0.04f;   // lands, then gone
                    break;
                }

                default:   // Box: interiors, wet slate, bamboo tips
                    pos = a.Centre + Vector3.Scale(a.Half, new Vector3(Signed(), Signed(), Signed()));
                    if (a.Source == Source.Interior)
                    {
                        vel = new Vector3(Signed() * 0.01f, 0.02f, Signed() * 0.01f);
                        colour = s_interiorDust;
                        size = Mathf.Lerp(0.01f, 0.03f, Next01());
                        frame = AtmosphereAtlas.Mote;
                    }
                    else if (a.Source == Source.BambooTip)
                    {
                        vel = new Vector3(0f, -0.2f, 0f);
                        life = Mathf.Sqrt(2f * Mathf.Max(0.2f, pos.y - a.Ground) / 9.81f) + 0.04f;
                    }
                    else
                    {
                        vel = new Vector3(Signed() * 0.04f, 0.2f + Signed() * 0.05f, Signed() * 0.04f) + wind * 0.1f;   // steam off the slate
                    }

                    break;
            }

            Emit(li, ps, pos, vel, size, aspect, frame, colour, look, life, prefill);
        }

        private void EmitNear(int li, ParticleSystem ps, Look look, Vector3 focus, Vector3 camPos, Vector3 camFwd, CameraMode mode, bool prefill)
        {
            Vector3 pos;
            if (mode == CameraMode.TopDown)
            {
                pos = new Vector3(focus.x + Signed() * 12f, focus.y + Mathf.Lerp(0.3f, 4f, Next01()), focus.z + Signed() * 12f);
            }
            else
            {
                // 10 × 5 × 10 m, 3 m ahead of the camera on the ground plane; never within 0.6 m of the lens.
                var flat = new Vector3(camFwd.x, 0f, camFwd.z);
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                Vector3 c = camPos + flat * 3f + Vector3.down * 0.5f;
                pos = c + new Vector3(Signed() * 5f, Signed() * 2.5f, Signed() * 5f);
                Vector3 d = pos - camPos;
                float m = d.magnitude;
                if (m < CameraClear) pos = camPos + (m > 1e-4f ? d / m : flat) * (CameraClear + Next01() * 0.5f);
            }

            Color colour = Color.Lerp(s_nearMote, s_interiorDust, _interiorBlend);
            float size = Mathf.Lerp(0.008f, 0.02f, Next01());
            var vel = new Vector3(Signed() * 0.02f, Mathf.Lerp(0.05f, 0.02f, _interiorBlend), Signed() * 0.02f) + WorldWind.Direction * (WorldWind.Strength * 0.08f);
            Emit(li, ps, pos, vel, size, 1f, AtmosphereAtlas.Mote, colour, look, Mathf.Lerp(look.LifeMin, look.LifeMax, Next01()), prefill);
        }

        private static float FallLife(float height, float speed, float rest, Look look)
        {
            float fall = Mathf.Max(0.5f, height) / speed;
            return Mathf.Min(fall + rest, look.LifeMax + rest);
        }

        private void Emit(int li, ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float aspect, byte frame, Color colour, Look look, float life, bool prefill)
        {
            // A prefill grain is born part-way through a shorter life, so a fresh world (or a raised tier) is full at once and turns over naturally.
            if (prefill) life *= Mathf.Lerp(0.15f, 1f, Next01());
            colour.a = look.Alpha;
            _ep.position = pos;
            _ep.velocity = vel;
            _ep.startLifetime = life;
            _ep.startSize3D = new Vector3(size * aspect, size, frame);
            _ep.rotation = Next01() * 360f;
            _ep.angularVelocity = look.SpinDeg * Signed();
            _ep.startColor = colour;
            _ep.randomSeed = NextUInt();
            ps.Emit(_ep, 1);
            _emitted[li]++;
        }

        // xorshift32: deterministic, allocation-free.
        private uint NextUInt()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x;
            return x;
        }

        private float Next01() => (NextUInt() & 0xFFFFFF) / 16777216f;
        private float Signed() => Next01() * 2f - 1f;

        /// <summary>Tests: a fixed seed (the capture agent sets one too, so AD frames compare).</summary>
        public void Seed(uint seed) => _rng = seed == 0 ? 0x9E3779B9u : seed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
        }
    }
}
