using System;
using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Enemies;
using DD.Prototype.Rendering;
using DD.Prototype.World;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-093 × WP-092 · the moment VFX of M1–M8 (ANLAR.md `s07` lines, data in <see cref="WorldMoments"/>), bound to each moment's area
    /// and live only in the layout world. Four pooled world-space particle layers + one floor card per door:
    ///
    ///   Mist (DD/VFX Atmo, Wisp frame, FLAT horizontal cards — ground mist never stands up into a body): M1 lane ≤ 0.3 m α 0.10 drifting
    ///     +x; M3 cold pocket x 45–52 z 52–54.5 ≤ 0.3 m α 0.08 #9AA6B2, static; M8 water mist ≤ 0.4 m α 0.08 over the pond only. Cap 0.12.
    ///   Petals (DD/VFX Atmo petals): M2 hanafubuki — each gust front (WorldWind strength rising through 0.55) lifts ≤ 30 petals over
    ///     1.5–2.5 s from the garden floor; ≤ 50 live.
    ///   Motes (DD/VFX Atmo motes): 15–30 in M3's door light wedge.
    ///   Drips (DD/VFX Flipbook additive, stretched, #E8C89A α 0.25, 1.2–1.5 cm): M1 / M6 warm glints round the lamps, M3 in the wedge,
    ///     M5 the lit eave curtain r 2.5 m round the lamps (≤ 40) whose landings ring the wet stone (≤ 12 live), M7 under the lanterns.
    ///   Door shaft (M3): a floor trapezoid out of B5's east door, additive #E8C89A α ≤ 0.12, breathing with the nearest lamp's sample.
    ///   Footstep rings (M4): player and zombie foot events on the rain mirror → AmbientVfxSystem rings Ø 0.2–0.7 m, 0.6 s, ≤ 16 live.
    ///
    /// The roof-vent smoke (M6) is AmbientVfxSystem's V6 source; the pendulum sway (M1 / M5 / M7), the M4 / M8 reflection streaks and the
    /// tōrō are LanternFlames'. Readability: nothing here stands on the camera → player ray — the mist and the shaft lie on the floor, the
    /// Atmo layers carry the shader's player guard, drips are 1.5 cm at α 0.25. Tiers scale the caps; 0 B per <see cref="Step"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(966)]
    public sealed class MomentVfxSystem : MonoBehaviour
    {
        private static readonly ProfilerMarker s_marker = new ProfilerMarker("WP093.Moments");

        public enum Layer
        {
            Mist = 0,
            Petals = 1,
            Motes = 2,
            Drips = 3
        }

        public const int LayerCount = 4;
        public const int MaxMoments = 8;
        public const int MaxLampsPerMoment = 16;
        public const float MomentReach = 40f;
        public const int MaxPetalsPerGust = 30;
        public const int MaxCurtainDrops = 40;
        public const int MaxCurtainRings = 12;
        public const int MaxFootstepRings = 16;
        public const float FootstepRingLife = 0.6f;
        public const float MistAlphaCap = 0.12f;
        public const float ShaftAlphaCap = 0.12f;
        public const float DripAlpha = 0.25f;
        public const float GustOn = 0.55f, GustOff = 0.45f;

        // [layer, tier] live caps (Performance, High, Cinematic).
        private static readonly int[,] s_caps =
        {
            { 24, 48, 72 },   // mist
            { 20, 50, 50 },   // petals (≤ 50 in frame)
            { 15, 22, 30 },   // motes (15–30)
            { 20, 40, 60 },   // drips (the M5 curtain ≤ 40 of them)
        };

        private static readonly float[] s_dripRateScale = { 0.5f, 1f, 1.3f };

        public static int CapFor(Layer layer, VfxTierLevel tier) => s_caps[(int)layer, (int)tier];

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        public static readonly Color DripTint = Hex(0xE8C89A);
        public static readonly Color ShaftTint = Hex(0xE8C89A);
        private static readonly Color s_petalA = Hex(0xF2C4D0), s_petalB = Hex(0xE8A9BF), s_mote = Hex(0xEAD7B8);

        public static MomentVfxSystem Active { get; private set; }

        private readonly ParticleSystem[] _systems = new ParticleSystem[LayerCount];
        private readonly Material[] _materials = new Material[LayerCount];
        private readonly int[] _caps = new int[LayerCount];
        private readonly float[] _accumulator = new float[MaxMoments + 1];   // mist per moment, [MaxMoments] = motes
        private readonly float[] _dripAccumulator = new float[MaxMoments];
        private readonly int[,] _momentLamps = new int[MaxMoments, MaxLampsPerMoment];
        private readonly int[] _momentLampCount = new int[MaxMoments];
        private readonly bool[] _inReach = new bool[MaxMoments];
        private readonly float[] _curtainDeath = new float[MaxCurtainDrops];
        private readonly float[] _curtainRingDeath = new float[MaxCurtainRings];
        private readonly Vector4[] _curtainLand = new Vector4[MaxCurtainDrops];   // xyz landing, w clock
        private readonly float[] _footDeath = new float[MaxFootstepRings];
        private readonly GameObject[] _shafts = new GameObject[2];
        private readonly MeshRenderer[] _shaftRenderers = new MeshRenderer[2];
        private readonly int[] _shaftMoment = { -1, -1 };
        private readonly List<Mesh> _meshes = new List<Mesh>(2);
        private MaterialPropertyBlock _shaftBlock;
        private Material _shaftMaterial;
        private ParticleSystem.EmitParams _ep;
        private Action<CharacterAnimationEvents, CharacterAnimationEvent> _onAnimation;
        private VfxTierLevel _tier;
        private bool _tierApplied, _scanned, _gustArmed = true;
        private float _clock, _refreshClock, _gustLeft, _gustRate, _gustAccumulator;
        private int _gustBudget, _settleFrame = -1;
        private uint _rng = 0x51ED270Bu;

        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        /// <summary>Tests / captures.</summary>
        public Vector3? FocusOverride;
        public Camera CameraOverride;

        public bool Ready => _materials[0] != null && _materials[3] != null;
        public bool MomentsLive => WorldMoments.Live;
        public int LiveCount(Layer l) => _systems[(int)l] != null ? _systems[(int)l].particleCount : 0;
        public int CapOf(Layer l) => _caps[(int)l];
        public ParticleSystem SystemOf(Layer l) => _systems[(int)l];
        public Material MaterialOf(Layer l) => _materials[(int)l];
        public int Gusts { get; private set; }
        public int GustPetals { get; private set; }
        public int CurtainDrops { get; private set; }
        public int CurtainRings { get; private set; }
        public int FootstepRings { get; private set; }
        public int Emitted(Layer l) => _emitted[(int)l];
        private readonly int[] _emitted = new int[LayerCount];
        public int LampsIn(int moment) => moment >= 0 && moment < MaxMoments ? _momentLampCount[moment] : 0;
        public GameObject ShaftOf(int k) => k >= 0 && k < _shafts.Length ? _shafts[k] : null;
        public int LiveCurtainDrops { get { int n = 0; for (int i = 0; i < MaxCurtainDrops; i++) if (_curtainDeath[i] > _clock) n++; return n; } }
        public int LiveCurtainRings { get { int n = 0; for (int i = 0; i < MaxCurtainRings; i++) if (_curtainRingDeath[i] > _clock) n++; return n; } }
        public int LiveFootstepRings(float now) { int n = 0; for (int i = 0; i < MaxFootstepRings; i++) if (_footDeath[i] > now) n++; return n; }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(this);
                return;
            }

            Active = this;
            if (!PrototypeShaders.IsReady) return;
            Shader atmo = PrototypeShaders.VfxAtmo, flipbook = PrototypeShaders.VfxFlipbook;
            if (atmo == null || !atmo.isSupported || flipbook == null || !flipbook.isSupported) return;

            var streams = new List<ParticleSystemVertexStream>(AtmosphereSystem.Streams);   // start-up only
            _systems[(int)Layer.Mist] = AtmoLayer("Moment_Mist", 80, ParticleSystemRenderMode.HorizontalBillboard, streams, atmo, Layer.Mist);
            _systems[(int)Layer.Petals] = AtmoLayer("Moment_Petals", 60, ParticleSystemRenderMode.Billboard, streams, atmo, Layer.Petals);
            _systems[(int)Layer.Motes] = AtmoLayer("Moment_Motes", 40, ParticleSystemRenderMode.Billboard, streams, atmo, Layer.Motes);
            _systems[(int)Layer.Drips] = DripLayer(flipbook);
            _shaftMaterial = new Material(flipbook) { name = "DD_VfxFlipbook_DoorShaft (WP-093, runtime)", hideFlags = HideFlags.DontSave };
            AmbientVfxTextures.SetupFlipbook(_shaftMaterial, Texture2D.whiteTexture, true, ShaftAlphaCap);
            _onAnimation = OnAnimationEvent;
        }

        private void OnEnable()
        {
            if (_onAnimation != null) CharacterAnimationEvents.Any += _onAnimation;
        }

        private void OnDisable()
        {
            if (_onAnimation != null) CharacterAnimationEvents.Any -= _onAnimation;
        }

        private void Start()
        {
            if (Active != this || !Ready) return;
            Rescan();
            _settleFrame = Time.frameCount + LanternFlames.SettleFrames + 2;   // after the lantern settle scan
        }

        private void OnDestroy()
        {
            if (Active != this) return;
            for (int i = 0; i < LayerCount; i++) if (_materials[i] != null) Destroy(_materials[i]);
            if (_shaftMaterial != null) Destroy(_shaftMaterial);
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
            Active = null;
        }

        private void Update()
        {
            if (_settleFrame >= 0 && Time.frameCount >= _settleFrame && Ready)
            {
                _settleFrame = -1;
                Rescan();
            }

            Step(Time.deltaTime);
        }

        private ParticleSystem NewSystem(string name, int max, bool size3D)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize3D = size3D;
            main.startLifetime = 5f;
            main.maxParticles = max;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.shadowCastingMode = ShadowCastingMode.Off;
            psr.receiveShadows = false;
            psr.lightProbeUsage = LightProbeUsage.Off;
            psr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            psr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        private ParticleSystem AtmoLayer(string name, int max, ParticleSystemRenderMode mode, List<ParticleSystemVertexStream> streams, Shader shader, Layer layer)
        {
            ParticleSystem ps = NewSystem(name, max, true);
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, layer == Layer.Mist ? 0.2f : 0.1f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            if (layer != Layer.Mist)
            {
                ParticleSystem.NoiseModule noise = ps.noise;
                noise.enabled = true;
                noise.strength = new ParticleSystem.MinMaxCurve(layer == Layer.Petals ? 0.5f : 0.06f);
                noise.frequency = layer == Layer.Petals ? 0.4f : 0.5f;
                noise.octaveCount = 1;
                noise.quality = ParticleSystemNoiseQuality.Medium;
                ParticleSystem.ExternalForcesModule forces = ps.externalForces;
                forces.enabled = true;
                forces.multiplier = layer == Layer.Petals ? 1.2f : 0.1f;
            }

            if (layer == Layer.Petals)
            {
                ParticleSystem.MainModule main = ps.main;
                main.gravityModifier = 0.12f;   // they lift on the gust, then settle
            }

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = mode;
            psr.alignment = ParticleSystemRenderSpace.View;
            psr.sortMode = layer == Layer.Mist ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
            psr.sortingFudge = layer == Layer.Mist ? 10f : 0f;
            psr.maxParticleSize = layer == Layer.Mist ? 2f : 0.5f;
            psr.SetActiveVertexStreams(streams);

            var m = new Material(shader) { name = "DD_VfxAtmo_" + layer + " (WP-093 moments, runtime)", hideFlags = HideFlags.DontSave };
            m.SetTexture("_MainTex", AtmosphereAtlas.Texture);
            switch (layer)
            {
                case Layer.Mist:
                    m.SetFloat("_AtmoAlpha", MistAlphaCap);
                    m.SetFloat("_AtmoGlint", 0.15f);
                    m.SetVector("_AtmoTwinkle", new Vector4(0f, 0f, 0f, 0.85f));
                    m.SetVector("_AtmoMotion", Vector4.zero);
                    m.SetVector("_AtmoFade", new Vector4(1.5f, 3f, 0.4f, 50f));
                    m.SetVector("_AtmoLight", new Vector4(0.8f, 1.1f, 1.1f, 0.8f));
                    m.SetVector("_AtmoPx", new Vector4(1.5f, 4096f, 0f, 0f));
                    break;
                case Layer.Petals:   // AtmosphereSystem's petal look
                    m.SetFloat("_AtmoAlpha", 0.85f);
                    m.SetFloat("_AtmoGlint", 0.5f);
                    m.SetVector("_AtmoTwinkle", new Vector4(0f, 0f, 0f, 0.8f));
                    m.SetVector("_AtmoMotion", new Vector4(0.6f, 1.2f, 0.04f, 0f));
                    m.SetVector("_AtmoFade", new Vector4(0.35f, 1.2f, 0.3f, 60f));
                    m.SetVector("_AtmoLight", new Vector4(0.75f, 1.35f, 1.4f, 1f));
                    m.SetVector("_AtmoPx", new Vector4(1.5f, 24f, 0f, 0f));
                    break;
                default:             // motes in the door light
                    m.SetFloat("_AtmoAlpha", 0.35f);
                    m.SetFloat("_AtmoGlint", 1f);
                    m.SetVector("_AtmoTwinkle", new Vector4(3f, 7f, 0.45f, 0.6f));
                    m.SetVector("_AtmoMotion", Vector4.zero);
                    m.SetVector("_AtmoFade", new Vector4(0.35f, 1f, 0.3f, 45f));
                    m.SetVector("_AtmoLight", new Vector4(1.0f, 1.6f, 2.0f, 1f));
                    m.SetVector("_AtmoPx", new Vector4(1.5f, 24f, 0f, 0f));
                    break;
            }

            m.SetFloat("_AtmoPollen", 0f);
            psr.sharedMaterial = m;
            _materials[(int)layer] = m;
            ps.gameObject.SetActive(true);
            return ps;
        }

        private ParticleSystem DripLayer(Shader shader)
        {
            ParticleSystem ps = NewSystem("Moment_Drips", 80, false);
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = 1f;
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.04f;
            psr.lengthScale = 1.2f;
            psr.sortMode = ParticleSystemSortMode.None;
            psr.minParticleSize = 0.0012f;
            psr.maxParticleSize = 0.03f;
            var m = new Material(shader) { name = "DD_VfxFlipbook_LitDrips (WP-093 moments, runtime)", hideFlags = HideFlags.DontSave };
            AmbientVfxTextures.SetupFlipbook(m, AmbientVfxTextures.Dot, true, 1f);
            psr.sharedMaterial = m;
            _materials[(int)Layer.Drips] = m;
            ps.gameObject.SetActive(true);
            return ps;
        }

        private void ApplyTier(VfxTierLevel tier)
        {
            _tier = tier;
            _tierApplied = true;
            for (int i = 0; i < LayerCount; i++)
            {
                _caps[i] = s_caps[i, (int)tier];
                ParticleSystem ps = _systems[i];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.maxParticles = Mathf.Max(1, _caps[i]);
                if (ps.particleCount > _caps[i]) ps.Clear();
            }

            if (tier == VfxTierLevel.Performance) _materials[(int)Layer.Mist].DisableKeyword("_DD_ATMO_SOFT");
            else _materials[(int)Layer.Mist].EnableKeyword("_DD_ATMO_SOFT");
        }

        // ------------------------------------------------------------------ scan (start-up / settle; allocates)

        /// <summary>Builds the door shaft cards and re-reads the moments' lamps (from <see cref="LanternFlames.Current"/>).</summary>
        public void Rescan()
        {
            if (WorldMoments.Override == null) WorldMoments.LayoutPresent = GameObject.Find(WorldMoments.LayoutRootName) != null;
            for (int k = 0; k < _shafts.Length; k++)
            {
                if (_shafts[k] != null) Destroy(_shafts[k]);
                _shafts[k] = null;
                _shaftRenderers[k] = null;
                _shaftMoment[k] = -1;
            }

            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
            _meshes.Clear();
            if (WorldMoments.Live)
            {
                MomentSpec[] moments = WorldMoments.Current;
                int s = 0;
                for (int m = 0; m < moments.Length && m < MaxMoments && s < _shafts.Length; m++)
                    if (moments[m].Door) BuildShaft(s++, m, moments[m]);
            }

            RefreshLamps();
            _scanned = true;
        }

        private void BuildShaft(int k, int moment, MomentSpec spec)
        {
            Vector3 dir = spec.DoorDir.sqrMagnitude > 1e-4f ? spec.DoorDir.normalized : Vector3.right;
            dir.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 o = spec.DoorPoint + Vector3.up * 0.02f;
            float w0 = spec.DoorWidth * 0.5f, w1 = spec.DoorWidth * 0.75f, len = spec.ShaftLength;
            // A floor trapezoid: full at the door (α 0.105 so the lamp's +12 % stays under the 0.12 cap), gone at its far edge.
            var mesh = new Mesh { name = "DoorShaft_" + spec.Id, hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { o - side * w0, o + side * w0, o + dir * len + side * w1, o + dir * len - side * w1 };
            mesh.uv = new[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
            Color near = ShaftTint, far = ShaftTint;
            near.a = 0.105f;
            far.a = 0f;
            mesh.colors = new[] { near, near, far, far };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var go = new GameObject("DoorShaft_" + spec.Id);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _shaftMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _shafts[k] = go;
            _shaftRenderers[k] = mr;
            _shaftMoment[k] = moment;
        }

        private void RefreshLamps()
        {
            for (int m = 0; m < MaxMoments; m++) _momentLampCount[m] = 0;
            LanternFlames lamps = LanternFlames.Current;
            if (lamps == null || !WorldMoments.Live) return;
            for (int i = 0; i < lamps.AnchorCount; i++)
            {
                int m = lamps.MomentOf(i);
                if (m < 0 || m >= MaxMoments || _momentLampCount[m] >= MaxLampsPerMoment || lamps.KindOf(i) == LampKind.Toro) continue;
                _momentLamps[m, _momentLampCount[m]++] = i;
            }
        }

        // ------------------------------------------------------------------ per frame (0 B)

        public void Step(float dt)
        {
            using (s_marker.Auto())
            {
                if (!Ready || dt <= 0f) return;
                _clock += dt;
                VfxTierLevel tier = VfxTier.Current;
                if (!_tierApplied || tier != _tier) ApplyTier(tier);
                if (!_scanned || !WorldMoments.Live) return;

                Camera cam = CameraOverride != null ? CameraOverride : Camera.main;
                Transform player = EnemyTargeting.Player;
                Vector3 focus = FocusOverride ?? (player != null ? player.position : cam != null ? cam.transform.position : Vector3.zero);

                _refreshClock -= dt;
                MomentSpec[] moments = WorldMoments.Current;
                int count = Mathf.Min(moments.Length, MaxMoments);
                if (_refreshClock <= 0f)
                {
                    _refreshClock = 1f;
                    RefreshLamps();   // 0 B: fixed arrays
                    for (int m = 0; m < count; m++)
                    {
                        float dx = moments[m].Center.x - focus.x, dz = moments[m].Center.y - focus.z, r = moments[m].Radius + MomentReach;
                        _inReach[m] = dx * dx + dz * dz <= r * r;
                    }
                }

                TickGust(dt);
                LanternFlames lamps = LanternFlames.Current;
                for (int m = 0; m < count; m++)
                {
                    if (!_inReach[m]) continue;
                    MomentSpec spec = moments[m];
                    if (spec.Mist != MomentMist.None) EmitMist(m, spec, dt);
                    if (spec.Hanafubuki) EmitPetals(spec, dt);
                    if (spec.Door) EmitMotes(spec, dt);
                    if (spec.Drips != MomentDrips.None) EmitDrips(m, spec, dt, lamps);
                }

                LandCurtain();
                TickShafts(lamps);
            }
        }

        private void EmitMist(int m, MomentSpec spec, float dt)
        {
            ParticleSystem ps = _systems[(int)Layer.Mist];
            int perMoment = _caps[(int)Layer.Mist] / 3;   // three mist moments share the layer
            _accumulator[m] += perMoment / 8f * dt;
            int n = (int)_accumulator[m];
            _accumulator[m] -= n;
            AmbientVfxSystem ambient = AmbientVfxSystem.Active;
            for (int k = 0; k < n && ps.particleCount < _caps[(int)Layer.Mist]; k++)
            {
                float x, z, y0 = spec.GroundY;
                if (spec.Mist == MomentMist.Pocket)
                {
                    x = Mathf.Lerp(spec.MistRect.x, spec.MistRect.z, Next01());
                    z = Mathf.Lerp(spec.MistRect.y, spec.MistRect.w, Next01());
                }
                else
                {
                    float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * spec.Radius * 0.8f;
                    x = spec.Center.x + Mathf.Cos(ang) * rad;
                    z = spec.Center.y + Mathf.Sin(ang) * rad;
                    if (spec.Mist == MomentMist.Water)
                    {
                        if (ambient == null || !ambient.IsOnPond(x, z, out float surface)) continue;   // over the water only
                        y0 = surface;
                    }
                }

                float size = Mathf.Lerp(0.8f, 1.4f, Next01());
                // Flat cards: the whole puff lies between 5 cm and the moment's ceiling (≤ 0.3 / 0.4 m).
                float y = y0 + Mathf.Lerp(0.05f, Mathf.Max(0.06f, spec.MistTop - 0.05f), Next01());
                Color c = Color.Lerp(spec.MistColour, Color.white, 0.1f * Next01());
                c.a = Mathf.Min(spec.MistAlpha, MistAlphaCap);
                Emit(Layer.Mist, ps, new Vector3(x, y, z), spec.MistDrift + new Vector3(Signed() * 0.02f, 0f, Signed() * 0.02f),
                    Mathf.Lerp(6f, 10f, Next01()), new Vector3(size, size, AtmosphereAtlas.Wisp), c, Signed() * 6f);
            }
        }

        /// <summary>Tests / captures: start a gust front now (the wind's own rise does the same).</summary>
        public void TriggerGust()
        {
            _gustBudget = MaxPetalsPerGust;
            float seconds = Mathf.Lerp(1.5f, 2.5f, Next01());
            _gustLeft = seconds;
            _gustRate = _gustBudget / seconds;
            Gusts++;
        }

        private void TickGust(float dt)
        {
            float w = WorldWind.Strength;
            if (_gustArmed && w >= GustOn) { _gustArmed = false; TriggerGust(); }
            else if (!_gustArmed && w <= GustOff) _gustArmed = true;
            if (_gustLeft > 0f) _gustLeft -= dt;
        }

        private void EmitPetals(MomentSpec spec, float dt)
        {
            if (_gustLeft <= 0f || _gustBudget <= 0) return;
            ParticleSystem ps = _systems[(int)Layer.Petals];
            _gustAccumulator += _gustRate * dt;
            int n = Mathf.Min((int)_gustAccumulator, _gustBudget);
            _gustAccumulator -= n;
            Vector3 wind = WorldWind.Direction * (1f + WorldWind.Strength);
            for (int k = 0; k < n && ps.particleCount < _caps[(int)Layer.Petals]; k++)
            {
                float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * spec.Radius * 0.7f;
                var p = new Vector3(spec.Center.x + Mathf.Cos(ang) * rad, spec.GroundY + Mathf.Lerp(0.05f, 0.4f, Next01()), spec.Center.y + Mathf.Sin(ang) * rad);
                Vector3 v = new Vector3(Signed() * 0.2f, Mathf.Lerp(0.8f, 1.4f, Next01()), Signed() * 0.2f) + wind;
                float size = Mathf.Lerp(0.08f, 0.14f, Next01());
                Color c = Color.Lerp(s_petalA, s_petalB, Next01());
                c.a = 0.85f;
                Emit(Layer.Petals, ps, p, v, Mathf.Lerp(3f, 5f, Next01()), new Vector3(size * 0.8f, size, Next01() < 0.5f ? AtmosphereAtlas.PetalYoung : AtmosphereAtlas.PetalAged), c, Signed() * 110f);
                _gustBudget--;
                GustPetals++;
            }
        }

        private void WedgePoint(MomentSpec spec, float yMin, float yMax, out Vector3 p)
        {
            Vector3 dir = spec.DoorDir.sqrMagnitude > 1e-4f ? spec.DoorDir.normalized : Vector3.right;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            float u = Next01();
            float half = spec.DoorWidth * 0.5f * (1f + 0.5f * u);
            p = spec.DoorPoint + dir * (0.3f + u * (spec.ShaftLength - 0.3f)) + side * (Signed() * half);
            p.y = spec.DoorPoint.y + Mathf.Lerp(yMin, yMax, Next01());
        }

        private void EmitMotes(MomentSpec spec, float dt)
        {
            ParticleSystem ps = _systems[(int)Layer.Motes];
            int target = _caps[(int)Layer.Motes];
            _accumulator[MaxMoments] += target / 8f * dt + Mathf.Max(0, target - ps.particleCount) * dt * 0.2f;
            int n = (int)_accumulator[MaxMoments];
            _accumulator[MaxMoments] -= n;
            for (int k = 0; k < n && ps.particleCount < target; k++)
            {
                WedgePoint(spec, 0.3f, 1.8f, out Vector3 p);
                Color c = s_mote;
                c.a = 0.3f;
                float size = Mathf.Lerp(0.02f, 0.035f, Next01());
                Emit(Layer.Motes, ps, p, new Vector3(Signed() * 0.02f, 0.03f, Signed() * 0.02f), Mathf.Lerp(6f, 10f, Next01()), new Vector3(size, size, AtmosphereAtlas.Mote), c, 0f);
            }
        }

        private void EmitDrips(int m, MomentSpec spec, float dt, LanternFlames lamps)
        {
            ParticleSystem ps = _systems[(int)Layer.Drips];
            int lampsHere = _momentLampCount[m];
            if (spec.Drips != MomentDrips.Wedge && (lamps == null || lampsHere == 0)) return;
            _dripAccumulator[m] += spec.DripRate * s_dripRateScale[(int)_tier] * dt;
            int n = (int)_dripAccumulator[m];
            _dripAccumulator[m] -= n;
            for (int k = 0; k < n && ps.particleCount < _caps[(int)Layer.Drips]; k++)
            {
                Vector3 p;
                float ground = spec.GroundY;
                int curtainSlot = -1;
                if (spec.Drips == MomentDrips.Wedge)
                {
                    WedgePoint(spec, 3.1f, 3.3f, out p);   // B5's eave over the door
                    ground = spec.DoorPoint.y - 0.25f;
                }
                else
                {
                    int a = _momentLamps[m, (int)(Next01() * lampsHere) % lampsHere];
                    Vector3 f = lamps.MovedFlamePointOf(a);
                    if (spec.Drips == MomentDrips.Curtain)
                    {
                        for (int s = 0; s < MaxCurtainDrops && curtainSlot < 0; s++) if (_curtainDeath[s] <= _clock) curtainSlot = s;
                        if (curtainSlot < 0) break;   // ≤ 40 curtain drops
                        p = new Vector3(f.x + Signed() * spec.DripRadius, f.y + 0.5f, f.z + Signed() * 0.25f);
                    }
                    else if (spec.Drips == MomentDrips.Under)
                    {
                        float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * spec.DripRadius;
                        p = new Vector3(f.x + Mathf.Cos(ang) * rad, f.y - 0.2f, f.z + Mathf.Sin(ang) * rad);
                    }
                    else
                    {
                        float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * spec.DripRadius;
                        p = new Vector3(f.x + Mathf.Cos(ang) * rad, f.y + 1.2f, f.z + Mathf.Sin(ang) * rad);
                    }
                }

                float h = Mathf.Max(0.2f, p.y - ground);
                float life = Mathf.Sqrt(2f * h / 9.81f) + 0.03f;   // lands, then gone (initial 1.5 m/s down shortens it a little)
                Color c = DripTint;
                c.a = DripAlpha;
                float size = Mathf.Lerp(0.012f, 0.015f, Next01());
                Emit(Layer.Drips, ps, p, new Vector3(0f, -1.5f, 0f), life, new Vector3(size, size, 1f), c, 0f, true);
                if (curtainSlot >= 0)
                {
                    _curtainDeath[curtainSlot] = _clock + life;
                    _curtainLand[curtainSlot] = new Vector4(p.x, ground, p.z, _clock + life * 0.85f);
                    CurtainDrops++;
                }
            }
        }

        // M5: a curtain drop that has landed rings the wet stone (≤ 12 live).
        private void LandCurtain()
        {
            AmbientVfxSystem ambient = AmbientVfxSystem.Active;
            for (int s = 0; s < MaxCurtainDrops; s++)
            {
                Vector4 l = _curtainLand[s];
                if (l.w <= 0f || l.w > _clock) continue;
                _curtainLand[s].w = 0f;
                int slot = -1;
                for (int k = 0; k < MaxCurtainRings && slot < 0; k++) if (_curtainRingDeath[k] <= _clock) slot = k;
                if (slot < 0 || ambient == null) continue;
                if (ambient.EmitCausedRing(new Vector3(l.x, l.y, l.z), AmbientVfxSystem.RingCause.DripStep, Mathf.Lerp(0.12f, 0.25f, Next01()), false))
                {
                    _curtainRingDeath[slot] = _clock + 0.5f;
                    CurtainRings++;
                }
            }
        }

        private void TickShafts(LanternFlames lamps)
        {
            if (_shaftBlock == null) _shaftBlock = new MaterialPropertyBlock();   // first frame only
            for (int k = 0; k < _shafts.Length; k++)
            {
                MeshRenderer r = _shaftRenderers[k];
                if (r == null) continue;
                int m = _shaftMoment[k];
                float s = 0f;
                if (lamps != null && m >= 0 && _momentLampCount[m] > 0) s = lamps.SignalOf(_momentLamps[m, 0]);
                _shaftBlock.SetFloat(AlphaId, 1f + LanternFlames.LightFlickerAmplitude * Mathf.Clamp01(GameFeelSettings.FlashScale) * s);
                r.SetPropertyBlock(_shaftBlock);
            }
        }

        // ------------------------------------------------------------------ M4 footstep rings

        private void OnAnimationEvent(CharacterAnimationEvents sender, CharacterAnimationEvent evt)
        {
            if ((evt != CharacterAnimationEvent.FootLeft && evt != CharacterAnimationEvent.FootRight) || sender == null) return;
            Transform body = sender.transform;
            Vector3 foot = body.position + body.forward * 0.2f + body.right * (evt == CharacterAnimationEvent.FootLeft ? -0.14f : 0.14f);
            NotifyFootstep(foot, Time.time);
        }

        /// <summary>A foot planted at <paramref name="foot"/>: a ring if it is on a moment with footstep rings (M4), ≤ 16 live. 0 B.</summary>
        public bool NotifyFootstep(Vector3 foot, float now)
        {
            if (!Ready || !WorldMoments.Live) return false;
            int m = WorldMoments.Find(foot);
            if (m < 0 || !WorldMoments.Current[m].FootstepRings) return false;
            int slot = -1;
            for (int k = 0; k < MaxFootstepRings && slot < 0; k++) if (_footDeath[k] <= now) slot = k;
            if (slot < 0) return false;
            AmbientVfxSystem ambient = AmbientVfxSystem.Active;
            if (ambient == null) return false;
            if (!ambient.EmitCausedRing(foot, AmbientVfxSystem.RingCause.Footstep, Mathf.Lerp(0.2f, 0.7f, Next01()), true)) return false;
            _footDeath[slot] = now + FootstepRingLife;
            FootstepRings++;
            return true;
        }

        // ------------------------------------------------------------------ emit / rng

        private void Emit(Layer layer, ParticleSystem ps, Vector3 pos, Vector3 vel, float life, Vector3 size3D, Color colour, float spin, bool uniform = false)
        {
            _ep.position = pos;
            _ep.velocity = vel;
            _ep.startLifetime = life;
            if (uniform) _ep.startSize = size3D.x;
            else _ep.startSize3D = size3D;
            _ep.rotation = Next01() * 360f;
            _ep.angularVelocity = spin;
            _ep.startColor = colour;
            _ep.randomSeed = NextUInt();
            ps.Emit(_ep, 1);
            _emitted[(int)layer]++;
        }

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

        public void Seed(uint seed) => _rng = seed == 0 ? 0x51ED270Bu : seed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
        }
    }
}
