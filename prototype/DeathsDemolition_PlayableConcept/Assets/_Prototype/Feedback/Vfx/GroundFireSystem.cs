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
    /// burning 2–3 s). Per zone, on the ground under the blast (AD 25.09, AD-WP073-B45GF MUSTs 1–5):
    ///   · a flame bed — 65 % low licks (1 : 1.0–1.2, 0.25–0.45 m, base 0–5 cm) and 35 % taller tongues (1 : 1.7) rising out of
    ///     it: the AP-010B lantern_flame 4 × 4 loop, alpha-blended with an HDR core, yellow → orange → dark red;
    ///   · an additive flame carpet the size of the zone (#FF8A2A α 0.35) flickering at 6 Hz;
    ///   · embers ≤ 3.5 m up (1.0–2.2 m/s, 1.2–1.8 s, 2–4 cm, streak ≤ 8 cm);
    ///   · a char: a procedural mottled soot disc (the AP-010 decal_scorch's α tops out at 0.40), #0E0B09 α 0.7, r 0.87 × the
    ///     zone, held 3 s past the fire, faded over 2 s;
    ///   · the heat wave as faint additive feathered-noise quads (#FFD9A0 α 0.06, 1.2 m, noise cycling ~6.7 Hz) — a refraction
    ///     card (DD/VFX HeatHaze) only where the pipeline gives the camera opaque texture (it drew a grey rectangle without);
    ///   · thin smoke (#3A3632 α 0.25, MuzzleSystem's shared smoke) and a flickering #FFA050 light (6, 5 m, ~8 Hz) for the nearest
    ///     zones (MuzzleSystem.TrackLight).
    /// Lifecycle: 0.25 s ignition ramp, full while it burns, dying to a quarter over the last 0.7 s, no tongue past the end, then
    /// Extinguished: out in 0.6 s with an ember burst and a puff. Ash Cloud and Hibana on the same rocket ignite two zones at the
    /// same point: they share one visual zone (the larger radius, the later end).
    ///
    /// WP-104 B4-1 (AD-B4 M8): a run-tree zone (<see cref="OnTreeIgnited"/>: S-04's FireGround proc through TreeVfxBinder) also lays
    /// the ember underdraw — its char disc once more in #E0702A at α ≤ 0.30 × the zone's intensity, alpha-blended between the char
    /// and the glow — so light stone reads the burning ground as fire, not grey smoke. RunTreeGroundFire's own zones draw as before.
    ///
    /// Budget (DEC-100 Cinematic VFX ≤ 5.5 ms): five world-space ParticleSystems (flames / embers / carpet / char / shimmer, one
    /// draw each) and the tree zones' underdraw (one draw while a tree zone burns), caps per tier below, no per-frame allocation.
    /// </summary>
    public sealed class GroundFireSystem : MonoBehaviour
    {
        public const int MaxZones = RunTreeGroundFire.Capacity;
        public const float RampSeconds = 0.25f, DyingSeconds = 0.7f, OutSeconds = 0.6f, MergeDistance = 1f;
        private const int LightKeyBase = 0x47460000;   // "GF": MuzzleSystem.TrackLight keys, one per zone slot
        private static readonly int AlphaCapId = Shader.PropertyToID("_AlphaCap");

        // Caps / rates per tier (Performance, High, Cinematic).
        public static int FlameCap => VfxTier.Pick(160, 320, 480);
        public static int EmberCap => VfxTier.Pick(40, 140, 260);
        public static int GlowCap => VfxTier.Pick(16, 32, 48);
        public static int CharCap => MaxZones;
        public static int ShimmerCap => VfxTier.Pick(0, 60, 90);
        public static int LightZones => VfxTier.Pick(1, 2, 3);
        public static int HazeZones => VfxTier.Pick(0, 0, 3);
        public static float FlamesPerSquareMetre => VfxTier.Pick(1.4f, 2.6f, 4.0f);   // per second (the V6 plaza's light stone needs the density)
        public static float EmbersPerSecond => VfxTier.Pick(4f, 9f, 14f);              // per zone of 10 m², clamped ×0.5–3
        public static float SmokeEvery => VfxTier.Pick(0f, 0.6f, 0.4f);                // seconds; 0 = none

        // Alpha-blended, not additive: on the V6 plaza's light stone an additive flame washed out to a pale pink drop (first
        // capture); a saturated orange over the ground with an HDR core (bloom) reads as fire on light and dark ground alike.
        private static readonly Color FlameTint = new Color(2.0f, 1.55f, 1.2f, 1f);
        private static readonly Color CharColour = new Color(0.055f, 0.043f, 0.035f, 0.7f);   // #0E0B09 α 0.7 over decal_scorch (AD MUST 4)
        public const float CharHoldSeconds = 3f, CharFadeSeconds = 2f;
        private static readonly Color EmberColour = new Color(1f, 0.45f, 0.12f, 1f) * 6f;
        private static readonly Color GlowColour = new Color(1f, 0.541f, 0.165f, 0.35f);   // #FF8A2A, additive (AD MUST 2)
        private static readonly Color ShimmerColour = new Color(1f, 0.851f, 0.627f, 0.06f); // #FFD9A0 α 0.06 (AD MUST 5 b)
        public static float ShimmerEvery => VfxTier.Pick(0f, 0.12f, 0.08f);                  // s per zone; Performance none
        private static readonly Color LightColour = new Color(1f, 0.627f, 0.314f, 1f);        // #FFA050 (AD SHOULD 3)
        private static readonly Color SmokeColour = new Color(0.227f, 0.212f, 0.196f, 0.25f); // #3A3632 α 0.25
        /// <summary>WP-104 B4-1: the tree zones' ember underdraw sorts between the char (fudge 40) and the glow (20), 5 mm over the char.</summary>
        public const float TreeUnderSortingFudge = 30f, TreeUnderLift = 0.02f;

        private struct Zone
        {
            public bool Alive, Ending;
            public int IdA, IdB;
            public Vector3 Centre;
            public float Radius, Born, EndsAt, EndedAt, Seed;
            public float FlameAcc, EmberAcc, NextGlow, NextSmoke, NextShimmer;
            /// <summary>WP-104 B4-1: a run-tree zone (the ember underdraw) and its char disc's size and turn (the underdraw's shape).</summary>
            public bool Tree;
            public float CharSize, CharTurn;
        }

        private static GroundFireSystem s_instance;
        public static GroundFireSystem Instance => s_instance;

        private readonly Zone[] _zones = new Zone[MaxZones];
        private readonly Transform[] _haze = new Transform[3];
        private readonly MeshRenderer[] _hazeRenderers = new MeshRenderer[3];
        private MaterialPropertyBlock _hazeBlock;
        private ParticleSystem _flames, _embers, _glow, _char, _shimmer, _treeUnder;
        private Material _flameMat, _emberMat, _glowMat, _charMat, _shimmerMat, _hazeMat, _treeUnderMat;
        private ParticleSystem.Particle[] _treeUnderParticles;
        private int _treeUnderDrawn;
        private Texture2D _dot, _soft, _noise, _charTex;
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
        public int ShimmerLive => _shimmer != null ? _shimmer.particleCount : 0;
        public ParticleSystem SystemOf(int layer) => layer == 0 ? _flames : layer == 1 ? _embers : layer == 2 ? _glow : _char;
        /// <summary>WP-104 B4-1 (AD-B4 M8): the tree zones' ember underdraws drawn this frame (one particle per burning tree zone).</summary>
        public int TreeUnderLive => _treeUnder != null ? _treeUnder.particleCount : 0;
        public ParticleSystem TreeUnderSystem => _treeUnder;
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
            DestroyIfSet(_shimmerMat);
            DestroyIfSet(_treeUnderMat);
            DestroyIfSet(_noise);
            DestroyIfSet(_charTex);
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
            if (_shimmer != null) _shimmer.Clear();
            if (_treeUnder != null) _treeUnder.Clear();
            _treeUnderDrawn = 0;
            for (int i = 0; i < _haze.Length; i++) if (_haze[i] != null) _haze[i].gameObject.SetActive(false);
            LiveZones = 0;
        }

        // ------------------------------------------------------------------ events

        /// <summary>RunTreeGroundFire.Ignited (public: tests drive it without the other listeners — audio).</summary>
        public void OnIgnited(GroundFireInfo info) => Ignite(in info, false);

        /// <summary>WP-104 B4-1: S-04's FireGround proc (TreeVfxBinder) — the same zone plus the AD-B4 M8 ember underdraw. A tree zone
        /// that merges into a live one gives that zone the underdraw (its char's shape).</summary>
        public void OnTreeIgnited(GroundFireInfo info) => Ignite(in info, true);

        private void Ignite(in GroundFireInfo info, bool tree)
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
                z.Tree |= tree;
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
                Seed = Next01() * 100f, NextGlow = now, NextSmoke = now + 0.15f, Tree = tree
            };
            ZonesStarted++;
            // The char: a dark soot disc that outlives the fire by 3 s (the burnt ground is what top-down reads after). Its size and
            // turn are kept: a tree zone's ember underdraw draws the same disc (WP-104 B4-1).
            float charSize = Mathf.Max(0.5f, info.Radius) * 0.87f * 2f, charTurn = Next01() * 360f;
            _zones[slot].CharSize = charSize;
            _zones[slot].CharTurn = charTurn;
            var charParams = new ParticleSystem.EmitParams
            {
                position = centre + Vector3.up * 0.015f, velocity = Vector3.zero, startSize = charSize,
                startLifetime = info.Seconds + CharHoldSeconds + CharFadeSeconds, startColor = CharColour, rotation = charTurn
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
            Vector3 camPos = cam != null ? cam.transform.position : new Vector3(0f, -1000f, 0f);
            _camPos = camPos;
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
            DrawTreeUnder(now);
            LightNearest(camPos, now);
            HazeNearest(cam, camPos, now);
        }

        /// <summary>
        /// WP-104 B4-1 (AD-B4 M8): one still particle per burning tree zone, set by hand every frame (0 B) — its char disc (size and
        /// turn) in #E0702A at α <see cref="TreeVfxLadder.FireUnderAlpha"/> × the zone's intensity, so it ramps in, dies and goes out
        /// with the flames and never outlives them (the char does, by 3 s).
        /// </summary>
        private void DrawTreeUnder(float now)
        {
            if (_treeUnder == null) return;
            int n = 0;
            for (int i = 0; i < MaxZones; i++)
            {
                ref Zone z = ref _zones[i];
                if (!z.Alive || !z.Tree) continue;
                float k = Intensity(z, now);
                if (k <= 0f) continue;
                Color c = TreeVfxLadder.FireUnderTint;
                c.a = TreeVfxLadder.FireUnderAlpha * k;
                ref ParticleSystem.Particle p = ref _treeUnderParticles[n++];
                p.position = z.Centre + Vector3.up * TreeUnderLift;
                p.velocity = Vector3.zero;
                p.startSize = z.CharSize;
                p.rotation = z.CharTurn;
                p.startColor = c;
                p.startLifetime = 1f;
                p.remainingLifetime = 1f;
            }

            if (n == 0 && _treeUnderDrawn == 0) return;
            _treeUnder.SetParticles(_treeUnderParticles, n);
            _treeUnderDrawn = n;
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
            if (!z.Ending && now < z.EndsAt)   // no tongue is born past the zone's end (AD SHOULD 1: 0 by end + 0.3 s)
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
                // AD MUST 2: a ground-projected additive flame carpet the size of the zone (#FF8A2A, α 0.35) flickering at 6 Hz —
                // short discs every 1/12 s, each carrying the 6 Hz sine at its birth (plus a little noise).
                z.NextGlow = now + 1f / 12f;
                float flicker = 0.7f + 0.3f * Mathf.Sin(now * 6f * 6.2831853f + z.Seed) + 0.1f * (Mathf.PerlinNoise(now * 3f, z.Seed) - 0.5f);
                Color c = GlowColour;
                c.a = 0.35f * Mathf.Clamp01(flicker) * Mathf.Max(k, z.Ending ? 0f : 0.1f);
                var ep = new ParticleSystem.EmitParams
                {
                    position = z.Centre + Vector3.up * 0.03f, velocity = Vector3.zero, startSize = z.Radius * 2f, startLifetime = 0.2f,
                    startColor = c, rotation = Next01() * 360f
                };
                _glow.Emit(ep, 1);
            }

            if (_shimmer != null && k > 0.05f && now >= z.NextShimmer)
            {
                // AD MUST 5 (b): the heat wave without refraction — faint additive warm quads (#FFD9A0, α ≤ 0.06) of feathered noise,
                // 1.2 m, rising over the flames and cycling their noise at ~6 Hz. Never grey, never a rectangle.
                z.NextShimmer = now + ShimmerEvery;
                Vector3 at = z.Centre + Disc(z.Radius * 0.6f) + Vector3.up * Mathf.Lerp(0.7f, 1.4f, Next01());
                Color c = ShimmerColour;
                c.a *= k;
                var sp = new ParticleSystem.EmitParams
                {
                    position = at, velocity = Vector3.up * Mathf.Lerp(0.4f, 0.7f, Next01()) + Wind() * 0.2f, startSize = 1.2f,
                    startLifetime = 0.6f, startColor = c, rotation = Next01() * 360f
                };
                _shimmer.Emit(sp, 1);
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

        private Vector3 _camPos;
        private const float CameraClearance = 1.5f;   // no flame / ember is born within 1.5 m of the camera (a first-person blob, capture 6)

        private bool NearCamera(Vector3 p) => (p - _camPos).sqrMagnitude < CameraClearance * CameraClearance;

        private void EmitFlame(in Zone z, float k)
        {
            Vector3 off = Disc(z.Radius * 0.92f);
            if (NearCamera(z.Centre + off)) return;
            float edge = off.magnitude / Mathf.Max(z.Radius, 0.01f);   // taller in the middle, low tongues at the rim
            // Tongues licking the ground: small, taller than wide (1 : 1.7), their foot on the stone, a slow rise and a short life
            // (the first capture's 0.8–1.4 m/s over 0.8 s lifted them a metre off the ground — floating drops).
            // AD (25.09) MUST 1 — a flame bed: 65 % low licks (1 : 1.0–1.2, 0.25–0.45 m wide, base 0–0.05 m, barely rising) and
            // 35 % taller tongues (1 : 1.7) rising out of the bed; the billboard's pivot is its centre, so the centre sits half a
            // height above the base (the size-over-life curve opens at 0.6: 0.3 × height).
            bool lick = Next01() < 0.65f;
            float width = Mathf.Lerp(0.25f, 0.45f, Next01()) * (1f - 0.3f * edge) * (0.7f + 0.3f * k);
            float aspect = lick ? Mathf.Lerp(1.0f, 1.2f, Next01()) : 1.7f;
            float height = width * aspect;
            float baseY = lick ? Mathf.Lerp(0f, 0.05f, Next01()) : Mathf.Lerp(0.02f, 0.08f, Next01());
            var ep = new ParticleSystem.EmitParams
            {
                position = z.Centre + off + Vector3.up * (baseY + 0.3f * height),
                velocity = new Vector3(Signed() * 0.05f, lick ? Mathf.Lerp(0.1f, 0.25f, Next01()) : Mathf.Lerp(0.35f, 0.6f, Next01()), Signed() * 0.05f) + Wind() * 0.15f,
                startSize3D = new Vector3(width, height, 1f), startLifetime = lick ? Mathf.Lerp(0.3f, 0.5f, Next01()) : Mathf.Lerp(0.4f, 0.6f, Next01()),
                rotation = Signed() * 8f, startColor = new Color(1f, 1f, 1f, 0.75f + 0.25f * k)
            };
            _flames.Emit(ep, 1);
        }

        private void EmitEmber(in Zone z, float speed)
        {
            Vector3 off = Disc(z.Radius * 0.85f);
            if (NearCamera(z.Centre + off + Vector3.up * 0.3f)) return;
            var ep = new ParticleSystem.EmitParams
            {
                // AD MUST 3: ≤ 3.5 m up (1.0–2.2 m/s against a light pull, 1.2–1.8 s: ≤ 3.2 m with the 0.5 m start), 2–4 cm.
                position = z.Centre + off + Vector3.up * Mathf.Lerp(0.1f, 0.5f, Next01()),
                velocity = new Vector3(Signed() * 0.35f, Mathf.Min(2.5f, Mathf.Lerp(1.0f, 2.2f, Next01()) * speed), Signed() * 0.35f) + Wind() * 0.4f,
                startSize = Mathf.Lerp(0.02f, 0.04f, Next01()), startLifetime = Mathf.Lerp(1.2f, 1.8f, Next01()),
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
                float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(now * 8f, z.Seed + 3.7f);   // ~8 Hz
                if (lights != null && k > 0.02f)
                {
                    lights.TrackLight(LightKeyBase + best, z.Centre + Vector3.up * 0.8f, LightColour, 6f * flicker * k, 5f);
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
            SetCap(_shimmer, ShimmerCap);
            SetCap(_treeUnder, MaxZones);
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
            _glowMat = Particle("DD_GroundFireGlow (runtime)", _soft, Color.white, true);
            // The char is a procedural mottled soot disc: the AP-010 decal_scorch's alpha tops out at 0.40 (mean 0.11) — made for
            // the decal system — so at the AD's α 0.7 it peaked near 0.28 and vanished on the V6 plaza's light stone (capture 5).
            _charTex = BuildCharTexture(128);
            _charMat = Particle("DD_GroundFireChar (runtime)", _charTex, Color.white, false);
            _noise = BuildNoiseSheet(64);
            _shimmerMat = Particle("DD_GroundFireShimmer (runtime)", _noise, Color.white, true);

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
            er.velocityScale = 0.02f;   // ≤ 8 cm streak: 0.9 × 4 cm + 2.2 m/s × 0.02 (AD MUST 3)
            er.lengthScale = 0.9f;
            ParticleSystem.MainModule em = _embers.main;
            em.gravityModifier = 0.08f;
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
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(1f, 0.64f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule cc = _char.colorOverLifetime;
            cc.enabled = true;
            cc.color = new ParticleSystem.MinMaxGradient(charAlpha);

            // WP-104 B4-1 (AD-B4 M8): the tree zones' ember underdraw — the char's disc, alpha-blended #E0702A, over the char and under
            // the additive glow. No emission: DrawTreeUnder sets one particle per tree zone every frame, so the system loops (it must
            // keep simulating and drawing them) and its colour is the particle's own (no colour over lifetime).
            _treeUnderMat = Particle("DD_GroundFireTreeUnder (runtime)", _charTex, Color.white, false);
            _treeUnder = NewSystem("GroundFireTreeUnder", _treeUnderMat, ParticleSystemRenderMode.HorizontalBillboard, 0f);
            _treeUnder.GetComponent<ParticleSystemRenderer>().sortingFudge = TreeUnderSortingFudge;
            _treeUnder.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule um = _treeUnder.main;
            um.loop = true;
            _treeUnder.Play();
            _treeUnderParticles = new ParticleSystem.Particle[MaxZones];

            _shimmer = NewSystem("GroundFireShimmer", _shimmerMat, ParticleSystemRenderMode.Billboard, 0f);
            ParticleSystem.TextureSheetAnimationModule ss = _shimmer.textureSheetAnimation;
            ss.enabled = true;
            ss.mode = ParticleSystemAnimationMode.Grid;
            ss.numTilesX = 2;
            ss.numTilesY = 2;
            ss.animation = ParticleSystemAnimationType.WholeSheet;
            ss.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            ss.cycleCount = 1;   // 4 noise frames over the 0.6 s life ≈ 6.7 Hz
            Gradient shimmerAlpha = new Gradient();
            shimmerAlpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule shc = _shimmer.colorOverLifetime;
            shc.enabled = true;
            shc.color = new ParticleSystem.MinMaxGradient(shimmerAlpha);

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

        // 2 × 2 frames of feathered value noise (white, noise in α): the heat shimmer. The feather fades the outer 40 % of each
        // frame to 0 — no hard edge, no rectangle (AD MUST 5).
        private static Texture2D BuildNoiseSheet(int cell)
        {
            int size = cell * 2;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_GroundFireShimmer", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float half = (cell - 1) * 0.5f;
            for (int f = 0; f < 4; f++)
            {
                int ox = (f % 2) * cell, oy = (f / 2) * cell;
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                        float feather = Mathf.Clamp01((1f - d) / 0.4f);
                        float n = Mathf.PerlinNoise(x * 0.11f + f * 7.3f, y * 0.11f + f * 3.1f) * 0.7f + Mathf.PerlinNoise(x * 0.27f + f * 1.7f, y * 0.27f) * 0.3f;
                        float a = Mathf.Clamp01((n - 0.35f) * 2.2f) * feather * feather;
                        px[(oy + y) * size + ox + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
            }

            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // A soot disc: a dark mottled interior (value noise, α 0.75–1) and a ragged, noise-bitten edge — no clean circle.
        private static Texture2D BuildCharTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_GroundFireChar", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float n = Mathf.PerlinNoise(x * 0.07f + 3.1f, y * 0.07f + 7.7f) * 0.65f + Mathf.PerlinNoise(x * 0.23f, y * 0.23f + 1.3f) * 0.35f;
                    float rim = 0.62f + 0.3f * n;                                   // where the edge frays out
                    float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - rim) / 0.22f));
                    float body = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.12f + 9.4f, y * 0.12f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(edge * body) * 255f));
                }
            }

            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
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
