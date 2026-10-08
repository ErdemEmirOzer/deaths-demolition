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
    /// WP-093 Faz 1b · the ambient layer the atmosphere (WP-073 B5, <see cref="AtmosphereSystem"/>) does not draw (LOOK-BIBLE §5):
    /// thin smoke, and expanding rings on the water — sparse drops on the ponds, drops where the eave drip line crosses a plaza puddle,
    /// and a ring wherever one of the atmosphere's eave / bamboo drips or a petal / leaf touches the water.
    ///
    /// WHY A SEPARATE SYSTEM. AtmosphereSystem's tier table is a contract its tests pin (600 / 1 800 / ≥ 3 000 live, 4 / 5 / 5 draws);
    /// a sixth and seventh layer would break both, and rings need a flat (horizontal) card the camera-facing Atmo layers do not have.
    /// This system adds its own two draws and reads the atmosphere only through two 0 B seams: the drip landing log
    /// (<see cref="AtmosphereSystem.DripLandingWrites"/>) and its petal / leaf particle systems (sampled 4× a second).
    ///
    /// SMOKE (§5). In the v6 world the bible's sources (<see cref="V6SmokeSources"/>: the R4 teahouse roof hole and the storehouse
    /// court); elsewhere every roof (CameraOccluderClass Roof) with a NavRegionVolume Room under it. #AEB5BC, α 0.06–0.10 (cap 0.12),
    /// 0.6 → 2.5 m, rising 0.35 m/s and bending with <see cref="WorldWind"/> as it climbs (a small start drift, then the WindZone
    /// through External Forces), faded to 0 at 7 m over the player's floor (the TD camera sits ≥ 8 m above that). DD/VFX Atmo, the
    /// Wisp frame: lit like the steam, and the shader's player guard. A source is silent while its column comes within 4 m of the
    /// camera → player ray or while the player stands under its roof (TD cut-away). Nearest 1 / 3 / 4 sources within 40 m.
    ///
    /// RINGS (§5). DD/VFX Flipbook (alpha), a procedural thin annulus on a horizontal billboard 1.2 cm over the surface, growing
    /// 30 % → 100 % with an ease-out. Pond: Ø 1.0 m over 1.8 s at a luma lift ≈ +0.04 (α 0.08 of the pale sky tone), one per 8 m² of
    /// water per 10 s on High; a falling petal / leaf: Ø 0.25 m; a drip on a puddle: Ø 0.12–0.25 m, 0.5 s, ≤ 12 live. Puddles are
    /// where the ground shader puts them (<see cref="PuddleField"/> mirrors DD_GroundTiles' clustered field, read from the plaza
    /// material) inside <see cref="PlazaRects"/> and within 0.8 m of an eave line (the drip line). Never within 1 m of the player;
    /// the rings lie on the ground under every body (depth-tested), so they cannot cover the player, a zombie or a projectile.
    ///
    /// TIERS (Performance / High / Cinematic): sources 1 / 3 / 4 × 8 / 14 / 20 wisps, rings ≤ 12 / 32 / 64 live, drip and leaf
    /// rings High and Cinematic only. 0 B per <see cref="Step"/> (anchors sized at <see cref="Rescan"/>, EmitParams, fixed arrays).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(965)]   // after AtmosphereSystem (960): the drips it logged this frame are read the same frame
    public sealed class AmbientVfxSystem : MonoBehaviour
    {
        private static readonly ProfilerMarker s_marker = new ProfilerMarker("WP093.AmbientVfx");

        public enum Kind
        {
            Smoke = 0,
            Rings = 1
        }

        public const int KindCount = 2;

        /// <summary>What made a ring (counted per cause for tests and captures).</summary>
        public enum RingCause
        {
            PondDrop = 0,
            PuddleDrop = 1,
            Drip = 2,
            Leaf = 3,
            Toro = 4,       // WP-092 M8: a floating tōrō rocking on the pond, every 3–5 s
            Footstep = 5,   // WP-092 M4: player / zombie feet on the rain mirror
            DripStep = 6    // WP-092 M5: a lit curtain drop landing (MomentVfxSystem)
        }

        public const int RingCauseCount = 7;

        // ------------------------------------------------------------------ numbers (LOOK-BIBLE §5)

        public const float WeightRefreshSeconds = 0.5f;
        public const float SmokeRange = 40f;
        public const float RingRange = 30f;
        public const float PuddleRange = 20f;
        /// <summary>A ring is never born within this of the player (xz).</summary>
        public const float RingPlayerClear = 1.0f;
        /// <summary>A roof does not smoke while the player is within its footprint + this margin (TD roof cut-away).</summary>
        public const float SmokePlayerMargin = 1.5f;
        /// <summary>§5: the smoke never comes within this of the camera → player ray.</summary>
        public const float SmokeRayClear = 4f;
        /// <summary>§5: the smoke has faded to 0 this high over the player's floor.</summary>
        public const float SmokeCeiling = 7f;
        public const float SmokeRise = 0.35f;
        public const float SmokeAlphaMin = 0.06f, SmokeAlphaMax = 0.10f;
        public const float SmokeAlphaCap = 0.12f;
        public const float SmokeSizeStart = 0.6f, SmokeSizeEnd = 2.5f;
        public const float RingAlphaCap = 0.35f;
        /// <summary>§5: the pond ring's 1.8 s is the longest ring.</summary>
        public const float RingLifeMax = 1.8f;
        public const float RingLift = 0.012f;
        public const float LeafSampleSeconds = 0.25f;
        public const int MaxPuddlePoints = 4096;
        public const float PuddleGridStep = 0.4f;
        /// <summary>A puddle point counts as "under the drip line" within this of an eave line (xz).</summary>
        public const float DripLineReach = 0.8f;
        /// <summary>§5: ≤ 12 puddle rings in the frame.</summary>
        public const int MaxPuddleRings = 12;
        public const int MaxPendingDrips = 32;
        public const int MaxLeafSeeds = 64;

        private static readonly int[] s_smokeSources = { 1, 3, 4 };
        private static readonly int[] s_wispsPerSource = { 8, 14, 20 };
        private static readonly int[] s_ringCap = { 12, 32, 64 };
        private static readonly float[] s_puddleRate = { 0.5f, 1.2f, 2.2f };
        // §5: one ring per 8 m² of water per 10 s on High (≈ 0.6–0.8 / s on a v6 pond).
        private static readonly float[] s_pondRatePerM2 = { 0.006f, 0.0125f, 0.015f };
        private static readonly float[] s_eventRingsPerSecond = { 0f, 4f, 8f };   // drip + leaf rings (token bucket)

        public static int SmokeSources(VfxTierLevel tier) => s_smokeSources[(int)tier];
        public static int WispsPerSource(VfxTierLevel tier) => s_wispsPerSource[(int)tier];
        public static int SmokeTarget(VfxTierLevel tier) => s_smokeSources[(int)tier] * s_wispsPerSource[(int)tier];
        public static int SmokeCap(VfxTierLevel tier) => Mathf.CeilToInt(SmokeTarget(tier) * 1.2f) + 4;
        public static int RingCap(VfxTierLevel tier) => s_ringCap[(int)tier];
        public static float PuddleDropsPerSecond(VfxTierLevel tier) => s_puddleRate[(int)tier];
        public static float PondDropsPerSquareMetre(VfxTierLevel tier) => s_pondRatePerM2[(int)tier];
        public static bool EventRings(VfxTierLevel tier) => s_eventRingsPerSecond[(int)tier] > 0f;

        /// <summary>WorldV5Ground.PlazaRects / Ap005SurfaceTags.V6PlazaRects (x0, z0, x1, z1) — mirrored (DD.Art references this assembly), guarded by an EditMode test.</summary>
        public static readonly Vector4[] PlazaRects =
        {
            new Vector4(-16f, -14f, 16f, 19.7f),
            new Vector4(-4f, -18f, 4f, -14f),
            new Vector4(-4f, 19.7f, 4f, 20.3f),
            new Vector4(-25.7f, 20.3f, 25.7f, 37.5f),
            new Vector4(-25.7f, 37.5f, -18f, 50.5f),
            new Vector4(18f, 37.5f, 25.7f, 50.5f),
        };

        /// <summary>LOOK-BIBLE §5 smoke sources in the v6 world (xz): the R4 teahouse roof hole, the storehouse court.</summary>
        public static readonly Vector2[] V6SmokeSources =
        {
            new Vector2(-32f, -36f),
            new Vector2(-38f, 39f),
        };

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        /// <summary>§5 smoke #AEB5BC (a little jitter toward #B7BDC3).</summary>
        public static readonly Color SmokeTint = Hex(0xAEB5BC);
        private static readonly Color s_smokeB = Hex(0xB7BDC3);
        /// <summary>Rings carry the cold sky they reflect (Look B: warmth only in sun / shafts / lantern light).</summary>
        public static readonly Color RingTint = Hex(0xDCE3EA);

        // ------------------------------------------------------------------ anchors

        private struct Pond
        {
            public Vector3 Centre;     // surface centre (y = water top)
            public float SemiX, SemiZ, Yaw;
            public float Area;
        }

        private struct Source
        {
            public Vector3 Point;      // where the wisps leave (ridge / roof hole)
            public Vector3 Axis;       // half the emitting segment (along the ridge), world
            public Vector3 Right, Forward;   // the footprint's horizontal axes (unit), world
            public Vector2 Half;       // footprint half extents along Right / Forward (the TD cut-away test)
            public Vector3 Centre;     // footprint centre
        }

        private Pond[] _ponds = new Pond[0];
        private Source[] _sources = new Source[0];
        private Vector3[] _puddles = new Vector3[0];
        private int[] _pondsInRange = new int[0];
        private int[] _puddlesInRange = new int[0];
        private readonly int[] _smokeActive = new int[4];
        private readonly float[] _puddleRingDeath = new float[MaxPuddleRings];
        private int _pondsInRangeCount, _puddlesInRangeCount, _smokeActiveCount;
        private float _plazaY;
        private float _puddleScale = 7f, _puddleField = 0.62f, _puddleRamp = 0.06f, _wetness = 1f;
        private Bounds _plazaBounds;
        private bool _havePlaza, _puddlesUnderEaves;

        // ------------------------------------------------------------------ state

        public static AmbientVfxSystem Active { get; private set; }

        private readonly ParticleSystem[] _systems = new ParticleSystem[KindCount];
        private readonly Material[] _materials = new Material[KindCount];
        private readonly int[] _caps = new int[KindCount];
        private readonly int[] _ringsByCause = new int[RingCauseCount];
        private readonly Vector4[] _pendingDrips = new Vector4[MaxPendingDrips];   // xyz landing, w atmosphere clock at landing
        private readonly uint[] _leafSeeds = new uint[MaxLeafSeeds];
        private ParticleSystem.Particle[] _sample = new ParticleSystem.Particle[0];
        private int _pendingCount, _leafSeedWrite, _dripRead = -1;
        private float _smokeAccumulator, _puddleAccumulator, _pondAccumulator, _eventTokens, _leafClock, _clock;
        private ParticleSystem.EmitParams _smokeEp, _ringEp;
        private VfxTierLevel _tier;
        private bool _tierApplied, _scanned, _ownsGuard;
        private int _settleFrame = -1;
        private float _weightClock;
        private Vector3 _weightFocus;
        private Transform _scanRoot;
        private uint _rng = 0x2545F491u;

        /// <summary>Tests / captures: the camera (default Camera.main) and a fixed focus (default the player, else the camera).</summary>
        public Camera CameraOverride;
        public Vector3? FocusOverride;

        /// <summary>Test isolation (the AtmosphereSystem pattern): when set, <see cref="Rescan"/> counts only the world under this root (and the v6 bible sources are not used).</summary>
        public Transform ScanRoot
        {
            get => _scanRoot;
            set => _scanRoot = value;
        }

        public bool Ready => _materials[0] != null && _materials[1] != null;
        public VfxTierLevel Tier => _tier;
        public int LiveCount(Kind kind) => _systems[(int)kind] != null ? _systems[(int)kind].particleCount : 0;
        public int CapOf(Kind kind) => _caps[(int)kind];
        public ParticleSystem SystemOf(Kind kind) => _systems[(int)kind];
        public Material MaterialOf(Kind kind) => _materials[(int)kind];
        public int Ponds => _ponds.Length;
        public int SmokeSourceCount => _sources.Length;
        public int PuddlePoints => _puddles.Length;
        /// <summary>True when the puddle points are the drip line ∩ puddles (§5); false = no eave crosses a puddle, all puddles at a quarter rate.</summary>
        public bool PuddlesUnderEaves => _puddlesUnderEaves;
        public int ActiveSmokeSources => _smokeActiveCount;
        public int PondsInRange => _pondsInRangeCount;
        public int PuddlesInRange => _puddlesInRangeCount;
        public int Rings(RingCause cause) => _ringsByCause[(int)cause];
        public Vector3 PuddlePoint(int i) => _puddles[i];
        public Vector3 SmokeSourcePoint(int i) => _sources[i].Point;

        public int LivePuddleRings
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxPuddleRings; i++) if (_puddleRingDeath[i] > _clock) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Puts the one ambient system under <paramref name="parent"/> (WorldAtmosphere.Build / BuildFor, next to the atmosphere). Idempotent.</summary>
        public static AmbientVfxSystem Attach(Transform parent)
        {
            if (Active != null) return Active;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ddNoAmbientVfx") >= 0) return null;   // A/B evidence: the world without WP-093 1b
            var go = new GameObject("[Ambient VFX (WP-093 1b)]");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<AmbientVfxSystem>();
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            Active = this;
            if (!PrototypeShaders.IsReady) return;
            Shader atmo = PrototypeShaders.VfxAtmo, flipbook = PrototypeShaders.VfxFlipbook;
            if (atmo == null || !atmo.isSupported || flipbook == null || !flipbook.isSupported) return;

            _systems[(int)Kind.Smoke] = CreateSmoke(atmo);
            _systems[(int)Kind.Rings] = CreateRings(flipbook);
            _sample = new ParticleSystem.Particle[1024];
            // WP-092 moments ride on the same object (one attach in WorldAtmosphere, one teardown in the tests).
            if (GetComponent<MomentVfxSystem>() == null) gameObject.AddComponent<MomentVfxSystem>();
        }

        private void Start()
        {
            if (Active != this || !Ready) return;
            Rescan();
            _settleFrame = Time.frameCount + 30;   // a world that finishes building over a few frames: one more scan, inside its first second
        }

        private void OnDestroy()
        {
            if (Active != this) return;
            for (int i = 0; i < KindCount; i++) if (_materials[i] != null) Destroy(_materials[i]);
            if (_ownsGuard) Shader.SetGlobalVector(GuardId, Vector4.zero);
            Active = null;
        }

        private void Update()
        {
            // The settle scan allocates (FindObjectsOfType): here, never inside Step (the 0 B contract tests call Step directly).
            if (_settleFrame >= 0 && Time.frameCount >= _settleFrame && Ready)
            {
                _settleFrame = -1;
                Rescan();
            }

            Step(Time.deltaTime);
        }

        // ------------------------------------------------------------------ layer setup (start-up only)

        private static readonly int GuardId = Shader.PropertyToID("_DD_AtmoGuard");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private ParticleSystem CreateSmoke(Shader shader)
        {
            ParticleSystem ps = NewSystem("Ambient_Smoke", 120, true);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.12f);
            noise.frequency = 0.25f;
            noise.octaveCount = 1;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.1f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // §5 "bends with the wind after 2 m": a small start drift, then the WindZone's push accumulates as the wisp climbs.
            ParticleSystem.ExternalForcesModule forces = ps.externalForces;
            forces.enabled = true;
            forces.multiplier = 0.6f;

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;   // z carries the atlas frame (DD/VFX Atmo): it must stay 1
            float grow = SmokeSizeEnd / SmokeSizeStart;
            var curve = new ParticleSystem.MinMaxCurve(grow, AnimationCurve.EaseInOut(0f, 1f / grow, 1f, 1f));
            size.x = curve;
            size.y = curve;
            size.z = new ParticleSystem.MinMaxCurve(1f);

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.alignment = ParticleSystemRenderSpace.View;
            psr.sortMode = ParticleSystemSortMode.Distance;
            psr.sortingFudge = 10f;
            psr.maxParticleSize = 0.5f;
            psr.SetActiveVertexStreams(new List<ParticleSystemVertexStream>(AtmosphereSystem.Streams));   // start-up only

            // The atmosphere's steam look, darker and thinner: lit by sun / cookie, the player guard, soft on High / Cinematic.
            var m = new Material(shader) { name = "DD_VfxAtmo_Smoke (WP-093, runtime)", hideFlags = HideFlags.DontSave };
            m.SetTexture(MainTexId, AtmosphereAtlas.Texture);
            m.SetFloat("_AtmoAlpha", SmokeAlphaCap);
            m.SetFloat("_AtmoPollen", 0f);
            m.SetFloat("_AtmoGlint", 0.15f);
            m.SetVector("_AtmoTwinkle", new Vector4(0f, 0f, 0f, 0.75f));
            m.SetVector("_AtmoMotion", Vector4.zero);
            m.SetVector("_AtmoFade", new Vector4(1.5f, 3.0f, 0.6f, 50f));
            m.SetVector("_AtmoLight", new Vector4(0.75f, 1.1f, 1.1f, 0.7f));
            m.SetVector("_AtmoPx", new Vector4(1.5f, 4096f, 0f, 0f));   // haze keeps its world size (the steam rule)
            psr.sharedMaterial = m;
            _materials[(int)Kind.Smoke] = m;
            ps.gameObject.SetActive(true);
            return ps;
        }

        private ParticleSystem CreateRings(Shader shader)
        {
            ParticleSystem ps = NewSystem("Ambient_Rings", 80, false);

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            // Ease-out growth 30 % → 100 % of the start size (the final diameter): §5 pond Ø 0.3 → 1.0 m.
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.3f, 1.6f, 1.6f), new Keyframe(1f, 1f, 0.15f, 0.15f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            psr.sortMode = ParticleSystemSortMode.None;
            psr.maxParticleSize = 2f;

            var m = new Material(shader) { name = "DD_VfxFlipbook_Rings (WP-093, runtime)", hideFlags = HideFlags.DontSave };
            AmbientVfxTextures.SetupFlipbook(m, AmbientVfxTextures.Ring, false, RingAlphaCap);
            psr.sharedMaterial = m;
            _materials[(int)Kind.Rings] = m;
            ps.gameObject.SetActive(true);
            return ps;
        }

        private ParticleSystem NewSystem(string name, int maxParticles, bool size3D)
        {
            var go = new GameObject(name);
            go.SetActive(false);   // inactive until every module is written (a live system rejects maxParticles / duration)
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize3D = size3D;
            main.startLifetime = 1f;
            main.maxParticles = maxParticles;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.minParticleSize = 0f;
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.lightProbeUsage = LightProbeUsage.Off;
            psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            psr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        private void ApplyTier(VfxTierLevel tier)
        {
            _tier = tier;
            _tierApplied = true;
            _caps[(int)Kind.Smoke] = SmokeCap(tier);
            _caps[(int)Kind.Rings] = RingCap(tier);
            for (int i = 0; i < KindCount; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.maxParticles = Mathf.Max(1, _caps[i]);
                if (ps.particleCount > _caps[i]) ps.Clear();   // a step down takes effect the same frame
            }

            if (tier == VfxTierLevel.Performance) _materials[(int)Kind.Smoke].DisableKeyword("_DD_ATMO_SOFT");
            else _materials[(int)Kind.Smoke].EnableKeyword("_DD_ATMO_SOFT");
            _weightClock = 0f;
        }

        // ------------------------------------------------------------------ world scan (start-up / world change only; allocates)

        private bool InScope(Transform t) => _scanRoot == null || (t != null && t.IsChildOf(_scanRoot));

        private static bool Has(string text, string token) => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Finds the ponds, the smoke sources and the puddle points of the loaded world. Allocates; never per frame.</summary>
        public void Rescan()
        {
            var ponds = new List<Pond>(8);
            Bounds plaza = default;
            bool havePlaza = false;
            Material plazaMaterial = null;

            Renderer[] all = FindObjectsOfType<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Transform t = r.transform;
                if (!InScope(t)) continue;
                string name = t.name;
                if (name == AtmosphereSystem.PlazaName)
                {
                    if (!havePlaza) { plaza = r.bounds; plazaMaterial = r.sharedMaterial; havePlaza = true; }
                    else plaza.Encapsulate(r.bounds);
                    continue;
                }

                string mat = r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;
                bool namedPond = name.StartsWith("Pond", StringComparison.Ordinal) && name.EndsWith("_Water", StringComparison.Ordinal);
                Bounds b = r.bounds;
                // A flat water surface only (a waterfall sheet carries "water" in its material too).
                bool waterMat = Has(mat, "water") && !Has(mat, "fall") && !Has(name, "fall") && b.size.y < 0.5f;
                if (!namedPond && !waterMat) continue;
                Vector3 s = t.lossyScale;
                ponds.Add(new Pond
                {
                    Centre = new Vector3(b.center.x, b.max.y, b.center.z),
                    SemiX = namedPond ? s.x * 0.5f : b.extents.x,
                    SemiZ = namedPond ? s.z * 0.5f : b.extents.z,
                    Yaw = namedPond ? t.eulerAngles.y * Mathf.Deg2Rad : 0f,
                    Area = Mathf.PI * (namedPond ? s.x * s.z * 0.25f : b.extents.x * b.extents.z)
                });
            }

            _ponds = ponds.ToArray();
            _pondsInRange = new int[_ponds.Length];
            _pondsInRangeCount = 0;

            var eaves = new List<Vector4>(64);
            ScanSources(havePlaza, eaves);
            ScanPuddles(havePlaza, plaza, plazaMaterial, eaves);

            _scanned = true;
            _weightClock = 0f;
            _pendingCount = 0;
        }

        private struct RoofBox
        {
            public Vector3 Centre, Right, Forward, Top;
            public Vector2 Half;
        }

        private void ScanSources(bool v6, List<Vector4> eaves)
        {
            // Every roof's footprint (CameraOccluderClass Roof): the eave lines (the drip line over the puddles) and the smoke candidates.
            var boxes = new List<RoofBox>(32);
            CameraOccluderClass[] occluders = FindObjectsOfType<CameraOccluderClass>();
            for (int i = 0; i < occluders.Length; i++)
            {
                CameraOccluderClass o = occluders[i];
                if (o == null || o.Kind != CameraOccluderKind.Roof || !InScope(o.transform)) continue;
                RoofBox box;
                var col = o.GetComponent<BoxCollider>();
                if (col != null)
                {
                    Transform t = o.transform;
                    Vector3 h = Vector3.Scale(col.size * 0.5f, t.lossyScale);
                    Vector3 right = t.right, forward = t.forward;
                    right.y = 0f;
                    forward.y = 0f;
                    right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
                    forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
                    Vector3 centre = t.TransformPoint(col.center);
                    box = new RoofBox { Centre = centre, Right = right, Forward = forward, Half = new Vector2(Mathf.Abs(h.x), Mathf.Abs(h.z)), Top = new Vector3(centre.x, centre.y + Mathf.Abs(h.y), centre.z) };
                }
                else
                {
                    Renderer[] parts = o.GetComponentsInChildren<Renderer>(false);   // start-up only
                    if (parts.Length == 0) continue;
                    Bounds rb = parts[0].bounds;
                    for (int k = 1; k < parts.Length; k++) rb.Encapsulate(parts[k].bounds);
                    box = new RoofBox { Centre = rb.center, Right = Vector3.right, Forward = Vector3.forward, Half = new Vector2(rb.extents.x, rb.extents.z), Top = new Vector3(rb.center.x, rb.max.y, rb.center.z) };
                }

                boxes.Add(box);
                Vector3 ex = box.Right * box.Half.x, ez = box.Forward * box.Half.y, c = box.Centre;
                Vector3 p0 = c - ex - ez, p1 = c + ex - ez, p2 = c + ex + ez, p3 = c - ex + ez;
                eaves.Add(new Vector4(p0.x, p0.z, p1.x, p1.z));
                eaves.Add(new Vector4(p1.x, p1.z, p2.x, p2.z));
                eaves.Add(new Vector4(p2.x, p2.z, p3.x, p3.z));
                eaves.Add(new Vector4(p3.x, p3.z, p0.x, p0.z));
            }

            var sources = new List<Source>(8);
            if (v6 && _scanRoot == null)
            {
                // LOOK-BIBLE §5: the two art-directed sources; the height is whatever is there (the teahouse roof, the court floor).
                for (int i = 0; i < V6SmokeSources.Length; i++)
                {
                    Vector2 xz = V6SmokeSources[i];
                    if (!Physics.Raycast(new Vector3(xz.x, 80f, xz.y), Vector3.down, out RaycastHit hit, 160f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var src = new Source
                    {
                        Point = hit.point + Vector3.up * 0.15f, Axis = Vector3.right * 0.15f, Right = Vector3.right, Forward = Vector3.forward,
                        Half = new Vector2(1.5f, 1.5f), Centre = hit.point
                    };
                    for (int k = 0; k < boxes.Count; k++)
                        if (InsideBox(boxes[k], hit.point, 0f)) { src.Centre = boxes[k].Centre; src.Right = boxes[k].Right; src.Forward = boxes[k].Forward; src.Half = boxes[k].Half; break; }
                    sources.Add(src);
                }
            }
            else
            {
                // Any other world: a roof smokes if a NavRegionVolume Room centre lies under it (a building with an interior).
                var rooms = new List<Vector3>(16);
                NavRegionMarker[] markers = FindObjectsOfType<NavRegionMarker>();
                for (int i = 0; i < markers.Length; i++)
                {
                    if (markers[i] == null || !InScope(markers[i].transform)) continue;
                    NavRegionVolume v = markers[i].Volume;
                    if (v != null && v.kind == NavRegionKind.Room) rooms.Add(v.center);
                }

                for (int i = 0; i < boxes.Count; i++)
                {
                    RoofBox box = boxes[i];
                    bool interior = false;
                    for (int k = 0; k < rooms.Count && !interior; k++) interior = InsideBox(box, rooms[k], 0.5f);
                    if (!interior) continue;
                    // One source per building: a second roof part over the same footprint (palace kit) is skipped.
                    bool duplicate = false;
                    for (int k = 0; k < sources.Count && !duplicate; k++) duplicate = InsideFootprint(sources[k], box.Centre, 0f);
                    if (duplicate) continue;
                    // The ridge runs along the longer horizontal side; wisps leave its middle 40 %.
                    sources.Add(new Source
                    {
                        Point = box.Top, Axis = box.Half.x >= box.Half.y ? box.Right * (box.Half.x * 0.2f) : box.Forward * (box.Half.y * 0.2f),
                        Right = box.Right, Forward = box.Forward, Half = box.Half, Centre = box.Centre
                    });
                }
            }

            _sources = sources.ToArray();
            _smokeActiveCount = 0;
        }

        private static bool InsideBox(RoofBox box, Vector3 p, float margin)
        {
            Vector3 d = p - box.Centre;
            return Mathf.Abs(Vector3.Dot(d, box.Right)) <= box.Half.x + margin && Mathf.Abs(Vector3.Dot(d, box.Forward)) <= box.Half.y + margin;
        }

        private static bool InsideFootprint(Source s, Vector3 p, float margin)
        {
            Vector3 d = p - s.Centre;
            return Mathf.Abs(Vector3.Dot(d, s.Right)) <= s.Half.x + margin && Mathf.Abs(Vector3.Dot(d, s.Forward)) <= s.Half.y + margin;
        }

        private void ScanPuddles(bool havePlaza, Bounds plaza, Material material, List<Vector4> eaves)
        {
            _havePlaza = havePlaza;
            _plazaBounds = plaza;
            _puddlesUnderEaves = false;
            if (!havePlaza)
            {
                _puddles = new Vector3[0];
                _puddlesInRange = new int[0];
                _puddlesInRangeCount = 0;
                return;
            }

            _plazaY = plaza.max.y;
            _puddleScale = Prop(material, "_PuddleScale", 7f);
            _puddleField = Prop(material, "_PuddleField", 0.62f);
            _puddleRamp = Mathf.Max(0.01f, Prop(material, "_PuddleFieldRamp", 0.06f));
            _wetness = Prop(material, "_Wetness", 1f);

            // §5: drops land where the eave drip line crosses a puddle. Without such a crossing every puddle takes a light drop instead.
            var underEaves = new List<Vector3>(256);
            var anywhere = new List<Vector3>(1024);
            if (_wetness > 0.3f)
            {
                for (int r = 0; r < PlazaRects.Length; r++)
                {
                    Vector4 rect = PlazaRects[r];
                    float x0 = Mathf.Max(rect.x, plaza.min.x), x1 = Mathf.Min(rect.z, plaza.max.x);
                    float z0 = Mathf.Max(rect.y, plaza.min.z), z1 = Mathf.Min(rect.w, plaza.max.z);
                    for (float x = x0 + PuddleGridStep * 0.5f; x < x1; x += PuddleGridStep)
                    for (float z = z0 + PuddleGridStep * 0.5f; z < z1; z += PuddleGridStep)
                    {
                        if (!InPuddle(x, z) || OnPond(x, z, -0.3f) >= 0) continue;
                        var p = new Vector3(x, _plazaY, z);
                        if (NearEave(eaves, x, z) && underEaves.Count < MaxPuddlePoints) underEaves.Add(p);
                        else if (anywhere.Count < MaxPuddlePoints) anywhere.Add(p);
                    }
                }
            }

            _puddlesUnderEaves = underEaves.Count > 0;
            _puddles = _puddlesUnderEaves ? underEaves.ToArray() : anywhere.ToArray();
            _puddlesInRange = new int[_puddles.Length];
            _puddlesInRangeCount = 0;
        }

        private static bool NearEave(List<Vector4> eaves, float x, float z)
        {
            for (int i = 0; i < eaves.Count; i++)
            {
                Vector4 e = eaves[i];
                float ex = e.z - e.x, ez = e.w - e.y;
                float len2 = ex * ex + ez * ez;
                float t = len2 > 1e-6f ? Mathf.Clamp01(((x - e.x) * ex + (z - e.y) * ez) / len2) : 0f;
                float dx = x - (e.x + ex * t), dz = z - (e.y + ez * t);
                if (dx * dx + dz * dz <= DripLineReach * DripLineReach) return true;
            }

            return false;
        }

        private static float Prop(Material m, string name, float fallback) => m != null && m.HasProperty(name) ? m.GetFloat(name) : fallback;

        // ------------------------------------------------------------------ DD_GroundTiles puddle field (CPU mirror)

        private static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>DDGroundHash (Hash without Sine, 3-in): frac(p.xyx · 0.1031), += dot(p3, p3.yzx + 33.33), frac((x + y) · z).</summary>
        public static float GroundHash(float px, float py)
        {
            float x = Frac(px * 0.1031f), y = Frac(py * 0.1031f), z = Frac(px * 0.1031f);
            float d = x * (y + 33.33f) + y * (z + 33.33f) + z * (x + 33.33f);
            x += d;
            y += d;
            z += d;
            return Frac((x + y) * z);
        }

        public static float GroundValueNoise(float px, float py)
        {
            float ix = Mathf.Floor(px), iy = Mathf.Floor(py);
            float fx = px - ix, fy = py - iy;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            float a = GroundHash(ix, iy), b = GroundHash(ix + 1f, iy), c = GroundHash(ix, iy + 1f), d = GroundHash(ix + 1f, iy + 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        /// <summary>DDPuddleField(xz): 0.65 · noise(xz / scale) + 0.35 · noise(xz / (0.36 scale) + (17.3, 5.1)).</summary>
        public static float PuddleField(float x, float z, float scale)
        {
            float s2 = scale * 0.36f;
            return 0.65f * GroundValueNoise(x / scale, z / scale) + 0.35f * GroundValueNoise(x / s2 + 17.3f, z / s2 + 5.1f);
        }

        /// <summary>Standing water: the shader's field term at ≥ 0.6 of its ramp (well inside the visible puddle, not on its rim).</summary>
        public bool InPuddle(float x, float z)
        {
            float f = (PuddleField(x, z, _puddleScale) - _puddleField) / _puddleRamp;
            return f >= 0.6f;
        }

        private bool OnPlaza(float x, float z)
        {
            if (!_havePlaza || x < _plazaBounds.min.x || x > _plazaBounds.max.x || z < _plazaBounds.min.z || z > _plazaBounds.max.z) return false;
            for (int i = 0; i < PlazaRects.Length; i++)
            {
                Vector4 r = PlazaRects[i];
                if (x > r.x && x < r.z && z > r.y && z < r.w) return true;
            }

            return false;
        }

        /// <summary>Index of the pond whose ellipse (shrunk by <paramref name="inset"/> m; negative grows it) holds (x, z), else −1.</summary>
        private int OnPond(float x, float z, float inset)
        {
            for (int i = 0; i < _ponds.Length; i++)
            {
                Pond p = _ponds[i];
                float dx = x - p.Centre.x, dz = z - p.Centre.z;
                float c = Mathf.Cos(p.Yaw), s = Mathf.Sin(p.Yaw);
                // Inverse of the scan's local → world yaw (x' = x cos + z sin, z' = −x sin + z cos).
                float lx = dx * c - dz * s, lz = dx * s + dz * c;
                float ax = Mathf.Max(0.05f, p.SemiX - inset), az = Mathf.Max(0.05f, p.SemiZ - inset);
                if ((lx * lx) / (ax * ax) + (lz * lz) / (az * az) <= 1f) return i;
            }

            return -1;
        }

        // ------------------------------------------------------------------ per frame (0 B)

        /// <summary>One frame: tier, ranges (every 0.5 s), smoke, pond / puddle drops, drip and leaf rings.</summary>
        public void Step(float dt)
        {
            using (s_marker.Auto())
            {
                if (!Ready || dt <= 0f) return;
                _clock += dt;
                VfxTierLevel tier = VfxTier.Current;
                if (!_tierApplied || tier != _tier) ApplyTier(tier);
                if (!_scanned) return;

                Camera cam = CameraOverride != null ? CameraOverride : Camera.main;
                bool haveCam = cam != null;
                Vector3 camPos = haveCam ? cam.transform.position : Vector3.zero;
                Transform player = EnemyTargeting.Player;
                Vector3 focus = FocusOverride ?? (player != null ? player.position : haveCam ? camPos + cam.transform.forward * 6f : Vector3.zero);
                bool havePlayer = player != null;
                Vector3 playerPos = havePlayer ? player.position : new Vector3(1e6f, 0f, 1e6f);
                bool fp = CameraModeService.Current == CameraMode.FirstPerson;

                // The Atmo shader's player guard is the atmosphere's to publish; without it (A/B, -ddNoAtmoV3) this system writes it.
                AtmosphereSystem atmo = AtmosphereSystem.Active;
                bool atmoLive = atmo != null && atmo.isActiveAndEnabled && atmo.Ready;
                if (!atmoLive && haveCam)
                {
                    _ownsGuard = true;
                    Vector3 chest = playerPos + Vector3.up * 1.1f;
                    Shader.SetGlobalVector(GuardId, havePlayer && !fp ? new Vector4(chest.x, chest.y, chest.z, CameraModeService.Current == CameraMode.TopDown ? 0.9f : 0.7f) : Vector4.zero);
                }

                _weightClock -= dt;
                if (_weightClock <= 0f || (focus - _weightFocus).sqrMagnitude > 16f)
                    RefreshRanges(focus, playerPos, haveCam && havePlayer && !fp, camPos);

                EmitSmoke(dt);
                EmitPondDrops(dt, playerPos);
                EmitPuddleDrops(dt, playerPos);

                _eventTokens = Mathf.Min(_eventTokens + s_eventRingsPerSecond[(int)_tier] * dt, 4f);
                if (atmoLive && EventRings(_tier))
                {
                    DrainDrips(atmo, playerPos);
                    _leafClock -= dt;
                    if (_leafClock <= 0f)
                    {
                        _leafClock = LeafSampleSeconds;
                        if (_pondsInRangeCount > 0)
                        {
                            SampleLeaves(atmo.SystemOf(AtmosphereSystem.Layer.Petals), playerPos);
                            SampleLeaves(atmo.SystemOf(AtmosphereSystem.Layer.Leaves), playerPos);
                        }
                    }
                }
                else if (atmo != null)
                {
                    _dripRead = atmo.DripLandingWrites;   // nothing to replay when the tier turns the events back on
                    _pendingCount = 0;
                }
            }
        }

        private void RefreshRanges(Vector3 focus, Vector3 playerPos, bool rayGuard, Vector3 camPos)
        {
            _weightClock = WeightRefreshSeconds;
            _weightFocus = focus;

            int n = 0;
            for (int i = 0; i < _ponds.Length; i++)
                if (DistanceXZ(_ponds[i].Centre, focus) - Mathf.Max(_ponds[i].SemiX, _ponds[i].SemiZ) <= RingRange) _pondsInRange[n++] = i;
            _pondsInRangeCount = n;

            n = 0;
            float r2 = PuddleRange * PuddleRange;
            for (int i = 0; i < _puddles.Length; i++)
            {
                float dx = _puddles[i].x - focus.x, dz = _puddles[i].z - focus.z;
                if (dx * dx + dz * dz <= r2) _puddlesInRange[n++] = i;
            }

            _puddlesInRangeCount = n;

            // The nearest sources within range (insertion into ≤ 4 slots, no allocation); never one the player stands under, never
            // one whose column (source → 7 m over the floor) comes within 4 m of the camera → player ray.
            Vector3 chest = playerPos + Vector3.up * 1.1f;
            float ceiling = focus.y + SmokeCeiling;
            int want = Mathf.Min(s_smokeSources[(int)_tier], _smokeActive.Length);
            _smokeActiveCount = 0;
            for (int i = 0; i < _sources.Length; i++)
            {
                Source src = _sources[i];
                float d = DistanceXZ(src.Point, focus);
                if (d > SmokeRange || InsideFootprint(src, playerPos, SmokePlayerMargin)) continue;
                if (rayGuard && ColumnNearRay(src.Point, Mathf.Max(src.Point.y, ceiling), camPos, chest) < SmokeRayClear) continue;
                int slot = _smokeActiveCount;
                while (slot > 0 && DistanceXZ(_sources[_smokeActive[slot - 1]].Point, focus) > d)
                {
                    if (slot < want) _smokeActive[slot] = _smokeActive[slot - 1];
                    slot--;
                }

                if (slot < want)
                {
                    _smokeActive[slot] = i;
                    if (_smokeActiveCount < want) _smokeActiveCount++;
                }
            }
        }

        /// <summary>The closest the vertical column (base → <paramref name="top"/>) comes to the segment camera → chest (5 samples up the column).</summary>
        public static float ColumnNearRay(Vector3 basePoint, float top, Vector3 camera, Vector3 chest)
        {
            Vector3 seg = chest - camera;
            float len2 = Mathf.Max(seg.sqrMagnitude, 1e-4f);
            float best = float.MaxValue;
            for (int k = 0; k <= 4; k++)
            {
                var p = new Vector3(basePoint.x, Mathf.Lerp(basePoint.y, top, k * 0.25f), basePoint.z);
                float t = Mathf.Clamp01(Vector3.Dot(p - camera, seg) / len2);
                best = Mathf.Min(best, (p - (camera + seg * t)).magnitude);
            }

            return best;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ smoke

        private void EmitSmoke(float dt)
        {
            ParticleSystem ps = _systems[(int)Kind.Smoke];
            if (_smokeActiveCount == 0) return;
            float ceiling = _weightFocus.y + SmokeCeiling;
            int target = _smokeActiveCount * s_wispsPerSource[(int)_tier];
            int live = ps.particleCount;
            _smokeAccumulator += target / 6f * dt + Mathf.Max(0, target - live) * dt * 0.1f;
            int n = (int)_smokeAccumulator;
            _smokeAccumulator -= n;
            n = Mathf.Min(n, Mathf.Max(0, _caps[(int)Kind.Smoke] - live));
            n = Mathf.Min(n, 8);
            Vector3 wind = WorldWind.Direction * WorldWind.Strength;
            for (int k = 0; k < n; k++)
            {
                Source src = _sources[_smokeActive[(int)(Next01() * _smokeActiveCount) % _smokeActiveCount]];
                Vector3 pos = src.Point + src.Axis * Signed() + new Vector3(Signed() * 0.12f, Next01() * 0.15f, Signed() * 0.12f);
                float rise = SmokeRise * Mathf.Lerp(0.85f, 1.15f, Next01());
                // Faded to 0 at the ceiling (§5: 7 m over the floor): the life ends there.
                float life = Mathf.Clamp((ceiling - pos.y) / rise, 2f, 14f);
                Vector3 vel = new Vector3(Signed() * 0.03f, rise, Signed() * 0.03f) + wind * 0.08f;
                float size = SmokeSizeStart * Mathf.Lerp(0.92f, 1.08f, Next01());
                Color colour = Color.Lerp(SmokeTint, s_smokeB, Next01() * 0.4f);
                colour.a = Mathf.Lerp(SmokeAlphaMin, SmokeAlphaMax, Next01());
                _smokeEp.position = pos;
                _smokeEp.velocity = vel;
                _smokeEp.startLifetime = life;
                _smokeEp.startSize3D = new Vector3(size, size, AtmosphereAtlas.Wisp);
                _smokeEp.rotation = Next01() * 360f;
                _smokeEp.angularVelocity = Signed() * 8f;
                _smokeEp.startColor = colour;
                _smokeEp.randomSeed = NextUInt();
                ps.Emit(_smokeEp, 1);
            }
        }

        // ------------------------------------------------------------------ rings

        private void EmitPondDrops(float dt, Vector3 playerPos)
        {
            if (_pondsInRangeCount == 0) return;
            float area = 0f;
            for (int i = 0; i < _pondsInRangeCount; i++) area += _ponds[_pondsInRange[i]].Area;
            _pondAccumulator += Mathf.Min(area * s_pondRatePerM2[(int)_tier], 3f) * dt;
            while (_pondAccumulator >= 1f)
            {
                _pondAccumulator -= 1f;
                // Pick a pond by area, then a point inside 90 % of its ellipse.
                float r = Next01() * area;
                int pi = _pondsInRange[0];
                for (int i = 0; i < _pondsInRangeCount; i++)
                {
                    pi = _pondsInRange[i];
                    r -= _ponds[pi].Area;
                    if (r <= 0f) break;
                }

                Pond p = _ponds[pi];
                float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * 0.9f;
                float x = Mathf.Cos(ang) * rad * p.SemiX, z = Mathf.Sin(ang) * rad * p.SemiZ;
                float c = Mathf.Cos(p.Yaw), s = Mathf.Sin(p.Yaw);
                var at = new Vector3(p.Centre.x + x * c + z * s, p.Centre.y, p.Centre.z - x * s + z * c);
                Ring(at, RingCause.PondDrop, playerPos);
            }
        }

        private void EmitPuddleDrops(float dt, Vector3 playerPos)
        {
            if (_puddlesInRangeCount == 0) return;
            _puddleAccumulator += s_puddleRate[(int)_tier] * (_puddlesUnderEaves ? 1f : 0.25f) * dt;
            while (_puddleAccumulator >= 1f)
            {
                _puddleAccumulator -= 1f;
                Vector3 p = _puddles[_puddlesInRange[(int)(Next01() * _puddlesInRangeCount) % _puddlesInRangeCount]];
                float x = p.x + Signed() * PuddleGridStep * 0.5f, z = p.z + Signed() * PuddleGridStep * 0.5f;
                if (!InPuddle(x, z)) { x = p.x; z = p.z; }
                Ring(new Vector3(x, _plazaY, z), RingCause.PuddleDrop, playerPos);
            }
        }

        /// <summary>
        /// A drop (or a leaf) touched the ground at <paramref name="at"/>: a ring if it is water — a pond, or a puddle of the plaza.
        /// Returns true when a ring was born. Rate-limited by the event bucket on the tier (High 4 / s, Cinematic 8 / s).
        /// </summary>
        public bool NotifyLanding(Vector3 at, bool leaf, Vector3 playerPos)
        {
            if (!Ready || !EventRings(_tier) || _eventTokens < 1f) return false;
            int pond = OnPond(at.x, at.z, 0.1f);
            Vector3 surface;
            RingCause cause;
            if (pond >= 0)
            {
                surface = new Vector3(at.x, _ponds[pond].Centre.y, at.z);
                cause = leaf ? RingCause.Leaf : RingCause.Drip;
            }
            else if (!leaf && OnPlaza(at.x, at.z) && InPuddle(at.x, at.z) && Mathf.Abs(at.y - _plazaY) < 0.6f)
            {
                surface = new Vector3(at.x, _plazaY, at.z);
                cause = RingCause.PuddleDrop;
            }
            else
            {
                return false;
            }

            if (!Ring(surface, cause, playerPos)) return false;
            if (cause == RingCause.PuddleDrop) _ringsByCause[(int)RingCause.Drip]++;   // a drip, drawn with the puddle look
            _eventTokens -= 1f;
            return true;
        }

        private void DrainDrips(AtmosphereSystem atmo, Vector3 playerPos)
        {
            int writes = atmo.DripLandingWrites;
            if (_dripRead < 0 || writes - _dripRead > AtmosphereSystem.MaxDripLandings) _dripRead = Mathf.Max(0, writes - AtmosphereSystem.MaxDripLandings);
            for (; _dripRead < writes; _dripRead++)
            {
                Vector4 l = atmo.DripLanding(_dripRead);
                // Keep only drops that will land on water (cheap test now, the ring when the drop arrives).
                if (_pendingCount >= MaxPendingDrips) break;
                if (OnPond(l.x, l.z, 0.1f) < 0 && !(OnPlaza(l.x, l.z) && InPuddle(l.x, l.z))) continue;
                _pendingDrips[_pendingCount++] = l;
            }

            float now = atmo.Clock;
            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                Vector4 l = _pendingDrips[i];
                if (l.w > now) continue;
                if (now - l.w < 0.5f) NotifyLanding(new Vector3(l.x, l.y, l.z), false, playerPos);   // a stale one (tier off and on) is dropped
                _pendingDrips[i] = _pendingDrips[--_pendingCount];
            }
        }

        private void SampleLeaves(ParticleSystem ps, Vector3 playerPos)
        {
            if (ps == null || ps.particleCount == 0) return;
            int n = ps.GetParticles(_sample, Mathf.Min(ps.particleCount, _sample.Length), 0);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = _sample[i].position;
                int pond = OnPond(p.x, p.z, 0.1f);
                if (pond < 0) continue;
                float h = p.y - _ponds[pond].Centre.y;
                if (h < -0.3f || h > 0.12f) continue;   // still falling above the water, or lying on the bank plane under it
                uint seed = _sample[i].randomSeed;
                if (SeenLeaf(seed)) continue;
                _leafSeeds[_leafSeedWrite] = seed;
                _leafSeedWrite = (_leafSeedWrite + 1) % MaxLeafSeeds;
                NotifyLanding(p, true, playerPos);
            }
        }

        private bool SeenLeaf(uint seed)
        {
            for (int i = 0; i < MaxLeafSeeds; i++) if (_leafSeeds[i] == seed && seed != 0u) return true;
            return false;
        }

        /// <summary>Per-cause look (§5): final diameter (m), life (s), alpha, and whether a slower inner ring follows.</summary>
        public static void RingLook(RingCause cause, out float diameter, out float lifeMin, out float lifeMax, out float alpha, out bool inner)
        {
            switch (cause)
            {
                // Pond: Ø 0.3 → 1.0 m over 1.8 s, a luma lift of ≈ +0.04 (α 0.08 of the pale sky tone on dark water).
                case RingCause.PondDrop: diameter = 1.0f; lifeMin = 1.6f; lifeMax = 1.8f; alpha = 0.08f; inner = true; break;
                // A puddle drop: Ø 0.12–0.25 m, 0.5 s (brighter: the puddle mirrors the sky).
                case RingCause.PuddleDrop: diameter = 0.25f; lifeMin = 0.45f; lifeMax = 0.5f; alpha = 0.14f; inner = false; break;
                // An eave / bamboo drip on a pond: the pond ring, a little smaller.
                case RingCause.Drip: diameter = 0.7f; lifeMin = 1.2f; lifeMax = 1.5f; alpha = 0.08f; inner = true; break;
                // A petal / leaf settling on the water: Ø 0.25 m.
                case RingCause.Leaf: diameter = 0.25f; lifeMin = 1.0f; lifeMax = 1.2f; alpha = 0.08f; inner = false; break;
                // M8 tōrō: Ø 0.3–0.8 m (the caller picks), a slow ring.
                case RingCause.Toro: diameter = 0.55f; lifeMin = 1.4f; lifeMax = 1.6f; alpha = 0.08f; inner = false; break;
                // M4 footstep on the rain mirror: Ø 0.2–0.7 m (the caller picks), 0.6 s.
                case RingCause.Footstep: diameter = 0.45f; lifeMin = 0.55f; lifeMax = 0.6f; alpha = 0.14f; inner = false; break;
                // M5 curtain drop landing: the puddle look.
                default: diameter = 0.2f; lifeMin = 0.45f; lifeMax = 0.5f; alpha = 0.14f; inner = false; break;
            }
        }

        /// <summary>True when (x, z) is on a scanned pond's surface (MomentVfxSystem's M8 water mist).</summary>
        public bool IsOnPond(float x, float z, out float surfaceY)
        {
            int p = OnPond(x, z, 0.2f);
            surfaceY = p >= 0 ? _ponds[p].Centre.y : 0f;
            return p >= 0;
        }

        /// <summary>
        /// WP-092 · a ring of a given cause and diameter at <paramref name="at"/> (its y is the surface), inside the tier cap. The
        /// player clearance does not apply when <paramref name="atPlayerOk"/> (a footstep ring is AT the feet, under the body).
        /// </summary>
        public bool EmitCausedRing(Vector3 at, RingCause cause, float diameter, bool atPlayerOk)
        {
            if (!Ready || !_tierApplied) return false;
            ParticleSystem ps = _systems[(int)Kind.Rings];
            if (!atPlayerOk)
            {
                Transform player = EnemyTargeting.Player;
                if (player != null)
                {
                    float dx = at.x - player.position.x, dz = at.z - player.position.z;
                    if (dx * dx + dz * dz < RingPlayerClear * RingPlayerClear) return false;
                }
            }

            if (ps.particleCount + 1 > _caps[(int)Kind.Rings]) return false;
            RingLook(cause, out _, out float lifeMin, out float lifeMax, out float alpha, out _);
            Color colour = RingTint;
            colour.a = alpha;
            _ringEp.position = at + Vector3.up * RingLift;
            _ringEp.velocity = Vector3.zero;
            _ringEp.startLifetime = Mathf.Lerp(lifeMin, lifeMax, Next01());
            _ringEp.startSize = diameter;
            _ringEp.rotation = Next01() * 360f;
            _ringEp.startColor = colour;
            _ringEp.randomSeed = NextUInt();
            ps.Emit(_ringEp, 1);
            _ringsByCause[(int)cause]++;
            return true;
        }

        private bool Ring(Vector3 at, RingCause cause, Vector3 playerPos)
        {
            ParticleSystem ps = _systems[(int)Kind.Rings];
            float dx = at.x - playerPos.x, dz = at.z - playerPos.z;
            if (dx * dx + dz * dz < RingPlayerClear * RingPlayerClear) return false;
            RingLook(cause, out float diameter, out float lifeMin, out float lifeMax, out float alpha, out bool inner);
            int need = inner ? 2 : 1;
            if (ps.particleCount + need > _caps[(int)Kind.Rings]) return false;

            float life = Mathf.Lerp(lifeMin, lifeMax, Next01());
            if (cause == RingCause.PuddleDrop)
            {
                // §5: ≤ 12 puddle rings in the frame.
                int slot = -1;
                for (int i = 0; i < MaxPuddleRings && slot < 0; i++) if (_puddleRingDeath[i] <= _clock) slot = i;
                if (slot < 0) return false;
                _puddleRingDeath[slot] = _clock + life;
                diameter = Mathf.Lerp(0.12f, 0.25f, Next01());
            }
            else
            {
                diameter *= Mathf.Lerp(0.9f, 1.1f, Next01());
            }

            Color colour = RingTint;
            colour.a = alpha;
            _ringEp.position = at + Vector3.up * RingLift;
            _ringEp.velocity = Vector3.zero;
            _ringEp.startLifetime = life;
            _ringEp.startSize = diameter;
            _ringEp.rotation = Next01() * 360f;
            _ringEp.startColor = colour;
            _ringEp.randomSeed = NextUInt();
            ps.Emit(_ringEp, 1);
            if (inner)
            {
                colour.a = alpha * 0.6f;
                _ringEp.startSize = diameter * 0.55f;
                _ringEp.startLifetime = life * 0.85f;
                _ringEp.startColor = colour;
                _ringEp.randomSeed = NextUInt();
                ps.Emit(_ringEp, 1);
            }

            _ringsByCause[(int)cause]++;
            return true;
        }

        // ------------------------------------------------------------------ rng (xorshift32, allocation-free)

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

        /// <summary>Tests: a fixed seed.</summary>
        public void Seed(uint seed) => _rng = seed == 0 ? 0x2545F491u : seed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
        }
    }

    /// <summary>
    /// WP-093 · the procedural sprites of the ambient layer (built once, linear, mipmapped, clamped): a soft round dot for the lantern
    /// sparks, a gaussian glow for the lantern halo and a thin annulus for the water rings. And the DD/VFX Flipbook material setup
    /// they share (one cell, no emissive, alpha 5/10 or additive 1/1).
    /// </summary>
    public static class AmbientVfxTextures
    {
        private static Texture2D s_dot, s_ring, s_glow;

        public static Texture2D Dot => s_dot != null ? s_dot : s_dot = Build("DD_AmbientDot (WP-093, procedural)", 32, DotAlpha);
        public static Texture2D Ring => s_ring != null ? s_ring : s_ring = Build("DD_AmbientRing (WP-093, procedural)", 128, RingAlpha);
        public static Texture2D Glow => s_glow != null ? s_glow : s_glow = Build("DD_AmbientGlow (WP-093, procedural)", 64, GlowAlpha);

        /// <summary>Soft dot: a bright core (r ≤ 0.25) in a gaussian halo, 0 at the edge.</summary>
        public static float DotAlpha(float r)
        {
            if (r >= 1f) return 0f;
            // GLSL smoothstep(0.15, 0.35, r): Unity's Mathf.SmoothStep(from, to, t) interpolates between from and to instead
            float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.35f, r));
            float halo = Mathf.Exp(-r * r * 6f) * (1f - r);
            return Mathf.Clamp01(Mathf.Max(core, halo * 0.6f));
        }

        /// <summary>Glow: a pure gaussian falling to 0 at the edge (no core: the flame card is the core).</summary>
        public static float GlowAlpha(float r)
        {
            if (r >= 1f) return 0f;
            return Mathf.Clamp01(Mathf.Exp(-r * r * 4f) * (1f - r * r));
        }

        /// <summary>Thin ring: a crest at r 0.86 (≈ 6 % of the diameter wide) with a faint inner trail; 0 at the centre and the edge.</summary>
        public static float RingAlpha(float r)
        {
            if (r >= 0.98f) return 0f;
            float crest = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.045f, 2f));
            float trail = Mathf.Exp(-Mathf.Pow((r - 0.74f) / 0.06f, 2f)) * 0.25f;
            return Mathf.Clamp01(crest + trail);
        }

        private static Texture2D Build(string name, int size, Func<float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 0, hideFlags = HideFlags.DontSave
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(r) * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);   // non-readable: the CPU copy is released
            return tex;
        }

        /// <summary>DD/VFX Flipbook as a one-cell sprite: alpha (5 / 10) or additive (1 / 1; the shader fades additive by colour).</summary>
        public static void SetupFlipbook(Material m, Texture2D sheet, bool additive, float alphaCap)
        {
            m.SetTexture("_Sheet", sheet);
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_Cols", 1f);
            m.SetFloat("_Rows", 1f);
            m.SetFloat("_Frame", 0f);
            m.SetFloat("_Alpha", 1f);
            m.SetFloat("_AlphaCap", alphaCap);
            m.SetFloat("_UseEmissive", 0f);
            m.SetFloat("_ScrollV", 0f);
            m.SetFloat("_SrcBlend", additive ? 1f : 5f);
            m.SetFloat("_DstBlend", additive ? 1f : 10f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZTest", 4f);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_dot = null;   // DontSave: rebuilt on demand (the AtmosphereAtlas rule)
            s_ring = null;
            s_glow = null;
        }
    }
}
