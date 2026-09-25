using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Rendering;
using DD.Prototype.Rounds;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 · burning ground for S-04's K10b zones (<see cref="RunTreeGroundFire"/>: Ash Cloud / Hibana leave a blast area
    /// burning 2–3 s). Per zone, on the ground under the blast: licking flames (the AP-010B lantern_flame 4 × 4 loop, alpha-blended
    /// with an HDR core, hot yellow → orange → a dark red end, low on the ground), rising embers (HDR streaks), a flickering ground glow and a soot char disc that
    /// outlives the fire by 3 s (the top-down read), a thin dark smoke column (MuzzleSystem's shared smoke), one flickering fire light (MuzzleSystem.TrackLight,
    /// the nearest zones) and, Cinematic only and only where the pipeline refracts (camera opaque texture), a
    /// heat-haze card over the fire (DD/VFX HeatHaze, as the barrel haze).
    /// Lifecycle: 0.25 s ignition ramp, full while it burns, dying to a quarter over the last 0.7 s, then Extinguished: flames
    /// out in 0.6 s, a last ember burst and a smoke puff. Ash Cloud and Hibana on the same rocket ignite two zones at the same
    /// point: they share one visual zone (the larger radius, the later end).
    ///
    /// Budget (DEC-100 Cinematic VFX ≤ 5.5 ms): four world-space ParticleSystems (flames / embers / glow / char, one draw each),
    /// caps per tier below, no per-frame allocation; up to 3 haze quads (Cinematic) and the tier's light slots.
    /// </summary>
    public sealed class GroundFireSystem : MonoBehaviour
    {
        public const int MaxZones = RunTreeGroundFire.Capacity;
        public const float RampSeconds = 0.25f, DyingSeconds = 0.7f, OutSeconds = 0.6f, MergeDistance = 1f;
        private const int LightKeyBase = 0x47460000;   // "GF": MuzzleSystem.TrackLight keys, one per zone slot
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");

        // Caps / rates per tier (Performance, High, Cinematic).
        public static int FlameCap => VfxTier.Pick(120, 260, 420);
        public static int EmberCap => VfxTier.Pick(40, 140, 260);
        public static int GlowCap => VfxTier.Pick(16, 32, 48);
        public static int CharCap => MaxZones;
        public static int LightZones => VfxTier.Pick(1, 2, 3);
        public static int HazeZones => VfxTier.Pick(0, 0, 3);
        public static float FlamesPerSquareMetre => VfxTier.Pick(1.0f, 1.8f, 2.5f);   // per second
        public static float EmbersPerSecond => VfxTier.Pick(4f, 9f, 14f);              // per zone of 10 m², clamped ×0.5–3
        public static float SmokeEvery => VfxTier.Pick(0f, 0.6f, 0.4f);                // seconds; 0 = none

        // Alpha-blended, not additive: on the V6 plaza's light stone an additive flame washed out to a pale pink drop (first
        // capture); a saturated orange over the ground with an HDR core (bloom) reads as fire on light and dark ground alike.
        private static readonly Color FlameTint = new Color(2.0f, 1.55f, 1.2f, 1f);
        private static readonly Color CharColour = new Color(0.10f, 0.07f, 0.055f, 0.62f);   // #1A120E soot under the fire
        private static readonly Color EmberColour = new Color(1f, 0.45f, 0.12f, 1f) * 6f;
        private static readonly Color GlowColour = new Color(1.6f, 0.62f, 0.16f, 1f);   // HDR warm orange, alpha-blended
        private static readonly Color LightColour = new Color(1f, 0.55f, 0.25f, 1f);
        private static readonly Color SmokeColour = new Color(0.18f, 0.165f, 0.157f, 0.35f);   // #2E2A28

        private struct Zone
        {
            public bool Alive, Ending;
            public int IdA, IdB;
            public Vector3 Centre;
            public float Radius, Born, EndsAt, EndedAt, Seed;
            public float FlameAcc, EmberAcc, NextGlow, NextSmoke;
        }

        private static GroundFireSystem s_instance;
        public static GroundFireSystem Instance => s_instance;

        private readonly Zone[] _zones = new Zone[MaxZones];
        private readonly Transform[] _haze = new Transform[3];
        private readonly MeshRenderer[] _hazeRenderers = new MeshRenderer[3];
        private MaterialPropertyBlock _hazeBlock;
        private ParticleSystem _flames, _embers, _glow, _char;
        private Material _flameMat, _emberMat, _glowMat, _charMat, _hazeMat;
        private Texture2D _dot, _soft;
        private VfxTierLevel _tier;
        private bool _tierApplied;
        private int _groundMask;
        private uint _rng = 0x2545F491u;

        // Counters (tests / capture log).
        public int LiveZones { get; private set; }
        public int ZonesStarted { get; private set; }
        public int ZonesMerged { get; private set; }
        public int LitZones { get; private set; }
        public int HazeShown { get; private set; }
        /// <summary>The heat-haze card exists only when the pipeline gives the camera opaque texture (refraction).</summary>
        public bool HazeRefracts { get; private set; }
        public int FlamesLive => _flames != null ? _flames.particleCount : 0;
        public int EmbersLive => _embers != null ? _embers.particleCount : 0;
        public int GlowLive => _glow != null ? _glow.particleCount : 0;
        public int CharLive => _char != null ? _char.particleCount : 0;
        public ParticleSystem SystemOf(int layer) => layer == 0 ? _flames : layer == 1 ? _embers : layer == 2 ? _glow : _char;
        public Camera CameraOverride { get; set; }

        public static GroundFireSystem Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[GroundFireSystem]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<GroundFireSystem>();
            }

            s_instance.Rearm();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            _groundMask = (1 << 0) | (1 << PrototypeLayers.Environment);
            _hazeBlock = new MaterialPropertyBlock();
            Build();
            SceneManager.activeSceneChanged += OnSceneChanged;
            Rearm();
        }

        private void OnDestroy()
        {
            Disarm();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (s_instance == this) s_instance = null;
            DestroyIfSet(_flameMat);
            DestroyIfSet(_emberMat);
            DestroyIfSet(_glowMat);
            DestroyIfSet(_charMat);
            DestroyIfSet(_hazeMat);
            DestroyIfSet(_dot);
            DestroyIfSet(_soft);
        }

        private static void DestroyIfSet(Object o)
        {
            if (o != null) Destroy(o);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            Clear();
            Rearm();
        }

        /// <summary>Subscribes (again) to the ground-fire pool; RunTreeGroundFire.ResetStatics drops every listener.</summary>
        public void Rearm()
        {
            Disarm();
            RunTreeGroundFire.Ignited += OnIgnited;
            RunTreeGroundFire.Extinguished += OnExtinguished;
        }

        private void Disarm()
        {
            RunTreeGroundFire.Ignited -= OnIgnited;
            RunTreeGroundFire.Extinguished -= OnExtinguished;
        }

        /// <summary>Every zone and particle off (scene change, tests).</summary>
        public void Clear()
        {
            for (int i = 0; i < MaxZones; i++) _zones[i] = default;
            if (_flames != null) _flames.Clear();
            if (_embers != null) _embers.Clear();
            if (_glow != null) _glow.Clear();
            if (_char != null) _char.Clear();
            for (int i = 0; i < _haze.Length; i++) if (_haze[i] != null) _haze[i].gameObject.SetActive(false);
            LiveZones = 0;
        }

        // ------------------------------------------------------------------ events

        /// <summary>RunTreeGroundFire.Ignited (public: tests drive it without the other listeners — audio).</summary>
        public void OnIgnited(GroundFireInfo info)
        {
            float now = Time.time;
            Vector3 centre = Ground(info.Center);
            float ends = now + info.Seconds;

            // Ash Cloud + Hibana on one rocket: the same point twice → one visual zone.
            for (int i = 0; i < MaxZones; i++)
            {
                ref Zone z = ref _zones[i];
                if (!z.Alive || z.Ending || z.IdB != 0) continue;
                Vector3 d = z.Centre - centre;
                if (d.x * d.x + d.z * d.z > MergeDistance * MergeDistance) continue;
                z.IdB = info.Id;
                z.Radius = Mathf.Max(z.Radius, info.Radius);
                z.EndsAt = Mathf.Max(z.EndsAt, ends);
                ZonesMerged++;
                return;
            }

            int slot = -1;
            for (int i = 0; i < MaxZones && slot < 0; i++) if (!_zones[i].Alive) slot = i;
            if (slot < 0)
            {
                // Full (16 visual zones): the one closest to its end gives way.
                float soonest = float.MaxValue;
                for (int i = 0; i < MaxZones; i++)
                {
                    float end = _zones[i].Ending ? _zones[i].EndedAt : _zones[i].EndsAt;
                    if (end < soonest) { soonest = end; slot = i; }
                }
            }

            _zones[slot] = new Zone
            {
                Alive = true, IdA = info.Id, Centre = centre, Radius = Mathf.Max(0.5f, info.Radius), Born = now, EndsAt = ends,
                Seed = Next01() * 100f, NextGlow = now, NextSmoke = now + 0.15f
            };
            ZonesStarted++;
            // The char: a dark soot disc that outlives the fire by 3 s (the burnt ground is what top-down reads after).
            var charParams = new ParticleSystem.EmitParams
            {
                position = centre + Vector3.up * 0.015f, velocity = Vector3.zero, startSize = Mathf.Max(0.5f, info.Radius) * 2.1f,
                startLifetime = info.Seconds + 3f, startColor = CharColour, rotation = Next01() * 360f
            };
            _char.Emit(charParams, 1);
        }

        /// <summary>RunTreeGroundFire.Extinguished.</summary>
        public void OnExtinguished(int id)
        {
            for (int i = 0; i < MaxZones; i++)
            {
                ref Zone z = ref _zones[i];
                if (!z.Alive || z.Ending) continue;
                if (z.IdA == id) z.IdA = 0;
                else if (z.IdB == id) z.IdB = 0;
                else continue;
                if (z.IdA == 0 && z.IdB == 0) GoOut(ref z, Time.time);
                return;
            }
        }

        private void GoOut(ref Zone z, float now)
        {
            z.Ending = true;
            z.EndedAt = now;
            // The last breath: an ember burst and a smoke puff over the embers.
            int burst = VfxTier.Pick(4, 10, 16);
            for (int k = 0; k < burst; k++) EmitEmber(z, 1.3f);
            MuzzleSystem smoke = MuzzleSystem.Instance;
            if (smoke != null && SmokeEvery > 0f)
            {
                smoke.PuffAt(z.Centre + Vector3.up * 0.6f, 1.4f + z.Radius * 0.3f, 2.8f, SmokeColour, Vector3.up * 0.45f + Wind() * 0.3f);
            }
        }

        private Vector3 Ground(Vector3 p)
        {
            // The blast may be on a body 1 m up: the fire burns on the ground under it.
            if (Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out RaycastHit hit, 6f, _groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.02f;
            }

            return p;
        }

        // ------------------------------------------------------------------ per frame (0 B)

        private void LateUpdate()
        {
            Step(Time.time, Time.deltaTime);
        }

        public void Step(float now, float dt)
        {
            VfxTierLevel tier = VfxTier.Current;
            if (!_tierApplied || tier != _tier) ApplyTier(tier);
            if (dt <= 0f) return;

            Camera cam = CameraOverride != null ? CameraOverride : Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            int live = 0;
            for (int i = 0; i < MaxZones; i++)
            {
                ref Zone z = ref _zones[i];
                if (!z.Alive) continue;
                // A zone whose Extinguished never came (a pool cleared without events) goes out on its own clock.
                if (!z.Ending && now > z.EndsAt + 0.5f) GoOut(ref z, now);
                float k = Intensity(z, now);
                if (z.Ending && now - z.EndedAt >= OutSeconds)
                {
                    z.Alive = false;
                    continue;
                }

                live++;
                EmitZone(ref z, k, now, dt);
            }

            LiveZones = live;
            LightNearest(camPos, now);
            HazeNearest(cam, camPos, now);
        }

        /// <summary>0..1: ignition ramp × the dying tail × the going-out fade.</summary>
        public static float Intensity(float born, float endsAt, bool ending, float endedAt, float now)
        {
            float k = Mathf.Clamp01((now - born) / RampSeconds);
            // Going out: no new flames; the embers and the glow fade from 0.4 to 0 over OutSeconds.
            if (ending) return k * 0.4f * Mathf.Clamp01(1f - (now - endedAt) / OutSeconds);
            float left = endsAt - now;
            if (left < DyingSeconds) k *= Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(left / DyingSeconds));
            return k;
        }

        private static float Intensity(in Zone z, float now) => Intensity(z.Born, z.EndsAt, z.Ending, z.EndedAt, now);

        private void EmitZone(ref Zone z, float k, float now, float dt)
        {
            float area = Mathf.PI * z.Radius * z.Radius;
            if (!z.Ending)
            {
                z.FlameAcc += FlamesPerSquareMetre * area * k * dt;
                int n = Mathf.Min((int)z.FlameAcc, 24);
                z.FlameAcc -= (int)z.FlameAcc;
                for (int j = 0; j < n; j++) EmitFlame(z, k);
            }

            z.EmberAcc += EmbersPerSecond * Mathf.Clamp(area / 10f, 0.5f, 3f) * k * dt;
            int e = Mathf.Min((int)z.EmberAcc, 8);
            z.EmberAcc -= (int)z.EmberAcc;
            for (int j = 0; j < e; j++) EmitEmber(z, 1f);

            if (now >= z.NextGlow)
            {
                // Two overlapping 0.5 s discs every 0.25 s: a continuous glow whose brightness flickers per disc.
                z.NextGlow = now + 0.25f;
                float flicker = 0.8f + 0.4f * Mathf.PerlinNoise(now * 6f, z.Seed);
                Color c = GlowColour * flicker;
                c.a = 0.42f * Mathf.Max(k, z.Ending ? 0f : 0.1f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = z.Centre + Vector3.up * 0.03f, velocity = Vector3.zero, startSize = z.Radius * 2.3f, startLifetime = 0.5f,
                    startColor = c, rotation = Next01() * 360f
                };
                _glow.Emit(ep, 1);
            }

            float every = SmokeEvery;
            if (every > 0f && !z.Ending && now >= z.NextSmoke)
            {
                z.NextSmoke = now + every;
                MuzzleSystem smoke = MuzzleSystem.Instance;
                if (smoke != null)
                {
                    Vector3 at = z.Centre + Disc(z.Radius * 0.6f) + Vector3.up * Mathf.Lerp(0.8f, 1.4f, Next01());
                    Color sc = SmokeColour;
                    sc.a *= k;
                    smoke.PuffAt(at, Mathf.Lerp(1.2f, 1.8f, Next01()), 2.5f, sc, Vector3.up * 0.55f + Wind() * 0.35f);
                }
            }
        }

        private void EmitFlame(in Zone z, float k)
        {
            Vector3 off = Disc(z.Radius * 0.92f);
            float edge = off.magnitude / Mathf.Max(z.Radius, 0.01f);   // taller in the middle, low tongues at the rim
            // Tongues licking the ground: small, taller than wide (1 : 1.7), their foot on the stone, a slow rise and a short life
            // (the first capture's 0.8–1.4 m/s over 0.8 s lifted them a metre off the ground — floating drops).
            float size = Mathf.Lerp(0.25f, 0.55f, Next01()) * (1f - 0.4f * edge) * (0.6f + 0.4f * k);
            var ep = new ParticleSystem.EmitParams
            {
                position = z.Centre + off + Vector3.up * (size * 0.75f),
                velocity = new Vector3(Signed() * 0.08f, Mathf.Lerp(0.35f, 0.7f, Next01()), Signed() * 0.08f) + Wind() * 0.2f,
                startSize3D = new Vector3(size, size * 1.7f, 1f), startLifetime = Mathf.Lerp(0.35f, 0.6f, Next01()), rotation = Signed() * 10f,
                startColor = new Color(1f, 1f, 1f, 0.7f + 0.3f * k)
            };
            _flames.Emit(ep, 1);
        }

        private void EmitEmber(in Zone z, float speed)
        {
            Vector3 off = Disc(z.Radius * 0.85f);
            var ep = new ParticleSystem.EmitParams
            {
                position = z.Centre + off + Vector3.up * Mathf.Lerp(0.1f, 0.5f, Next01()),
                velocity = new Vector3(Signed() * 0.4f, Mathf.Lerp(1.2f, 2.6f, Next01()) * speed, Signed() * 0.4f) + Wind() * 0.5f,
                startSize = Mathf.Lerp(0.02f, 0.04f, Next01()), startLifetime = Mathf.Lerp(1.0f, 2.2f, Next01()),
                startColor = Color.white
            };
            _embers.Emit(ep, 1);
        }

        // ------------------------------------------------------------------ light / haze for the nearest zones

        private void LightNearest(Vector3 camPos, float now)
        {
            MuzzleSystem lights = MuzzleSystem.Instance;
            int budget = LightZones;
            int lit = 0;
            for (int pass = 0; pass < budget; pass++)
            {
                int best = Nearest(camPos, pass);
                if (best < 0) break;
                Zone z = _zones[best];
                float k = Intensity(z, now);
                float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(now * 9f, z.Seed + 3.7f);
                if (lights != null && k > 0.02f)
                {
                    lights.TrackLight(LightKeyBase + best, z.Centre + Vector3.up * 0.8f, LightColour, (1.6f + z.Radius * 0.25f) * flicker * k, z.Radius * 2.2f + 2f);
                    lit++;
                }
            }

            LitZones = lit;
        }

        // The pass-th nearest live zone to the camera (0 B: a selection by rank, zones ≤ 16).
        private int Nearest(Vector3 camPos, int rank)
        {
            int found = -1;
            float bound = -1f;
            for (int r = 0; r <= rank; r++)
            {
                found = -1;
                float best = float.MaxValue;
                for (int i = 0; i < MaxZones; i++)
                {
                    if (!_zones[i].Alive) continue;
                    float d = (_zones[i].Centre - camPos).sqrMagnitude + i * 1e-4f;   // ties broken by slot
                    if (d > bound && d < best) { best = d; found = i; }
                }

                if (found < 0) return -1;
                bound = best;
            }

            return found;
        }

        private void HazeNearest(Camera cam, Vector3 camPos, float now)
        {
            int budget = _hazeMat != null ? Mathf.Min(HazeZones, _haze.Length) : 0;
            bool firstPerson = CameraModeService.Current == CameraMode.FirstPerson;
            int shown = 0;
            for (int h = 0; h < _haze.Length; h++)
            {
                Transform card = _haze[h];
                if (card == null) continue;
                int zi = h < budget ? Nearest(camPos, h) : -1;
                if (zi < 0)
                {
                    if (card.gameObject.activeSelf) card.gameObject.SetActive(false);
                    continue;
                }

                Zone z = _zones[zi];
                Vector3 centre = z.Centre + Vector3.up * 0.9f;
                Vector3 toCam = camPos - centre;
                toCam.y = 0f;
                // Never at the eye (the barrel haze's AD B1 MUST 2): the camera must stand clear of the fire.
                if (cam == null || toCam.magnitude < z.Radius + (firstPerson ? 3f : 1.5f))
                {
                    if (card.gameObject.activeSelf) card.gameObject.SetActive(false);
                    continue;
                }

                if (!card.gameObject.activeSelf) card.gameObject.SetActive(true);
                card.SetPositionAndRotation(centre, Quaternion.LookRotation(-toCam.normalized, Vector3.up));
                card.localScale = new Vector3(z.Radius * 1.9f, 1.8f, 1f);
                _hazeBlock.SetFloat(AlphaCapId, 0.7f * Intensity(z, now));
                _hazeRenderers[h].SetPropertyBlock(_hazeBlock);
                shown++;
            }

            HazeShown = shown;
        }

        // ------------------------------------------------------------------ build

        private void ApplyTier(VfxTierLevel tier)
        {
            _tier = tier;
            _tierApplied = true;
            SetCap(_flames, FlameCap);
            SetCap(_embers, EmberCap);
            SetCap(_glow, GlowCap);
            SetCap(_char, CharCap);
        }

        private static void SetCap(ParticleSystem ps, int cap)
        {
            if (ps == null) return;
            ParticleSystem.MainModule m = ps.main;
            m.maxParticles = cap;
        }

        private void Build()
        {
            _dot = BuildDot(32, 2.2f);
            _soft = BuildDot(64, 1.4f);

            VfxCatalogEntry flameRow = VfxLibrary.Get(VfxIds.LanternFlame);
            _flameMat = Particle("DD_GroundFireFlames (runtime)", flameRow != null && flameRow.Texture != null ? flameRow.Texture : (Texture)_soft, FlameTint, false);
            _emberMat = Particle("DD_GroundFireEmbers (runtime)", _dot, Color.white, true);
            _glowMat = Particle("DD_GroundFireGlow (runtime)", _soft, Color.white, false);
            _charMat = Particle("DD_GroundFireChar (runtime)", _soft, Color.white, false);

            _flames = NewSystem("GroundFireFlames", _flameMat, ParticleSystemRenderMode.Billboard, -0.05f);
            ParticleSystem.TextureSheetAnimationModule sheet = _flames.textureSheetAnimation;
            sheet.enabled = flameRow != null && flameRow.Texture != null;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = flameRow != null ? Mathf.Max(1, flameRow.Columns) : 4;
            sheet.numTilesY = flameRow != null ? Mathf.Max(1, flameRow.Rows) : 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);   // normalised (0..1), a random phase per tongue
            sheet.cycleCount = 1;
            ParticleSystemRenderer flameRenderer = _flames.GetComponent<ParticleSystemRenderer>();
            flameRenderer.alignment = ParticleSystemRenderSpace.View;
            ParticleSystem.MainModule fm = _flames.main;
            fm.startSize3D = true;
            Gradient flameColour = new Gradient();
            flameColour.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.82f, 0.42f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.14f), 0.25f),
                        new GradientColorKey(new Color(0.95f, 0.30f, 0.06f), 0.6f), new GradientColorKey(new Color(0.30f, 0.05f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule fc = _flames.colorOverLifetime;
            fc.enabled = true;
            fc.color = new ParticleSystem.MinMaxGradient(flameColour);
            ParticleSystem.SizeOverLifetimeModule fs = _flames.sizeOverLifetime;
            fs.enabled = true;
            fs.separateAxes = false;
            fs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.55f)));
            ParticleSystem.NoiseModule fn = _flames.noise;
            fn.enabled = true;
            fn.strength = 0.3f;
            fn.frequency = 1.2f;
            fn.scrollSpeed = 0.8f;
            fn.quality = ParticleSystemNoiseQuality.Low;

            _embers = NewSystem("GroundFireEmbers", _emberMat, ParticleSystemRenderMode.Stretch, -0.02f);
            var er = _embers.GetComponent<ParticleSystemRenderer>();
            er.velocityScale = 0.03f;
            er.lengthScale = 1f;
            Gradient emberColour = new Gradient();
            emberColour.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.5f), new GradientColorKey(new Color(0.6f, 0.1f, 0.03f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule ec = _embers.colorOverLifetime;
            ec.enabled = true;
            ec.color = new ParticleSystem.MinMaxGradient(emberColour);
            if (_emberMat.HasProperty("_BaseColor")) _emberMat.SetColor("_BaseColor", EmberColour);
            ParticleSystem.NoiseModule en = _embers.noise;
            en.enabled = true;
            en.strength = 0.8f;
            en.frequency = 0.9f;
            en.quality = ParticleSystemNoiseQuality.Low;

            _glow = NewSystem("GroundFireGlow", _glowMat, ParticleSystemRenderMode.HorizontalBillboard, 0f);
            Gradient glowAlpha = new Gradient();
            glowAlpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule gc = _glow.colorOverLifetime;
            gc.enabled = true;
            gc.color = new ParticleSystem.MinMaxGradient(glowAlpha);

            _char = NewSystem("GroundFireChar", _charMat, ParticleSystemRenderMode.HorizontalBillboard, 0f);
            _char.GetComponent<ParticleSystemRenderer>().sortingFudge = 40f;   // under the glow and the flames
            Gradient charAlpha = new Gradient();
            charAlpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule cc = _char.colorOverLifetime;
            cc.enabled = true;
            cc.color = new ParticleSystem.MinMaxGradient(charAlpha);

            BuildHaze();
            ApplyTier(VfxTier.Current);
        }

        private void BuildHaze()
        {
            Shader shader = PrototypeShaders.IsReady ? PrototypeShaders.VfxHeatHaze : null;
            VfxCatalogEntry data = VfxLibrary.Get(VfxIds.BarrelHeatDistortTile);
            bool tile = data != null && data.Texture != null;
            if (!tile) data = VfxLibrary.Get(VfxIds.BarrelHeatDistort);
            if (shader == null || data == null || data.Texture == null) return;
            // Refraction only: without the camera opaque texture the shader's shimmer fallback drew the card as a grey rectangle
            // over the fire (capture 2, as the barrel haze in B1 AD MUST 2). WP-029's pipeline keeps the opaque texture off.
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            HazeRefracts = urp != null && urp.supportsCameraOpaqueTexture;
            if (!HazeRefracts) return;
            _hazeMat = new Material(shader) { name = "DD_GroundFireHaze (runtime)", hideFlags = HideFlags.DontSave };
            _hazeMat.SetTexture("_Sheet", data.Texture);
            _hazeMat.SetFloat("_TileMode", tile ? 1f : 0f);
            _hazeMat.EnableKeyword("DD_OPAQUE_TEXTURE");
            Mesh quad = VfxQuad();
            for (int i = 0; i < _haze.Length; i++)
            {
                var go = new GameObject("GroundFireHaze_" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _hazeMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                _haze[i] = go.transform;
                _hazeRenderers[i] = r;
            }
        }

        private static Mesh s_quad;

        private static Mesh VfxQuad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "GroundFireHazeQuad", hideFlags = HideFlags.DontSave };
            s_quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            s_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            s_quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            s_quad.RecalculateBounds();
            return s_quad;
        }

        private static Material Particle(string name, Texture texture, Color baseColour, bool additive)
        {
            var m = new Material(FeedbackMaterials.ParticleAlpha()) { name = name, hideFlags = HideFlags.DontSave };
            m.SetTexture("_BaseMap", texture);
            m.mainTexture = texture;
            if (additive && m.HasProperty("_Blend")) m.SetFloat("_Blend", 1f);
            if (additive && m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.One);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColour);
            return m;
        }

        private ParticleSystem NewSystem(string name, Material material, ParticleSystemRenderMode mode, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = mode == ParticleSystemRenderMode.HorizontalBillboard ? 20f : 0f;   // the glow sorts under the flames
            ps.Play();
            return ps;
        }

        private static Texture2D BuildDot(int size, float power)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_GroundFireDot", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), power);
                    byte b = (byte)(a * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, b);
                }
            }

            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 Wind() => WorldWind.Direction * WorldWind.Strength;

        private Vector3 Disc(float radius)
        {
            float ang = Next01() * 6.2831853f, rad = Mathf.Sqrt(Next01()) * radius;
            return new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
        }

        private float Next01()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        private float Signed() => Next01() * 2f - 1f;
    }
}
