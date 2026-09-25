using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rendering;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B6 · Namekuji (sw_gel, Sliquifier-like slippery gel — direction §2.2, §4, §5 row "Namekuji birikinti / erime").
    ///
    ///   · The blob in flight is READ each frame from <see cref="ProjectileSystem"/> (Kind Gel): a 0.18 m drop stretched
    ///     × 1.4 along its velocity (DD/VFX Gel: 6 Hz / 0.015 m wobble, smoothness 0.95, emissive #FF5FD2 × 3, #B040FF
    ///     fresnel rim) and four tail droplets placed behind it from the same state (stateless), one tracked light
    ///     #FF5FD2 / 3 / 3 m; first person: nothing inside 0.5 m of the eye.
    ///   · Fired → the nozzle curtain (6 droplets, 0.02 m, 0.3 s, gravity) + a Ø 0.30 splash for 0.15 s + a
    ///     #FF5FD2 / 6 / 4 m / 0.20 s flash (the canister slosh is <see cref="SpecialWeaponGlow"/>'s).
    ///   · ProjectileEvents.Impact (Kind Gel) → a fan of 8 droplets along the normal (0.4 m, 0.25 s), a 0.6 m
    ///     normal-aligned wet splat for 0.2 s, and on a wall three 0.4 m runs sliding down for 0.8 s.
    ///   · FieldSpawned (Count = puddle id, Radius 1.6 / 0.9, Seconds 25 / 8, Direction = surface normal) → a puddle on
    ///     DD/VFX Puddle (3 splat variants × a random yaw): life T 0–10 % spreads 0.6 → 1 (ease-out), 10–80 % live
    ///     (smoothness 0.92, emissive × 0.6 + 0.3 Hz pulse, 3 bubbles / s, two normal layers at 0.15 Hz), 80–100 % dries
    ///     (emission → 0, colour → #6E3560 matte, rim cracks), the last 2 s fade; its end time is re-read from S-03's
    ///     <see cref="GelPuddleRegistry"/> every 0.25 s (merged puddles share the longest life, the cap's oldest fades
    ///     early). FieldEnded (Count = id) → a 0.5 s fade from wherever it is. The edge ring stays visible in every
    ///     phase (§4.5 danger read). 16 visual slots over the registry's 12 live (+ fading).
    ///   · Hit (direct) / S-03 SpecialSlide beat → the body's gel film (<see cref="ZombieSkinFx"/>, 6 s) and drips
    ///     (2 / s for 3 s, 0.02 m, a 0.1 m spot where they land); the slide adds two 0.04 m magenta smears 0.6 s + 4
    ///     droplets. SpecialFall → a 0.5 m splat under the body. Liquefied (Origin = feet) → 10 droplets up + a 0.6 m
    ///     splat (the dissolve edge needs the skin shader — Performance behaviour on every tier until then).
    ///   · PlayerSlideStarted / Ended → while sliding, two parallel 1.0 s smears every 0.1 s + 3 droplets / s.
    /// §4.1 player guard: a splat between the camera and the player's chest draws at α × 0.5 (TP / top-down; FP has no
    /// body on screen). Tier (§5): 12 puddles on every tier; bubbles + pulse on Cinematic / High, flat emission on
    /// Performance. Draws: puddles + splats 1, blobs + droplets 1, smears 1 (DD/VFX Ribbon). 0 B per frame.
    /// </summary>
    [DefaultExecutionOrder(956)]
    public sealed class GelSystem : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.GelSystem");

        public const int MaxPuddles = 16;
        public const int MaxSplats = 8;
        public const int MaxDrops = 128;
        public const int MaxSmears = 64;
        public const int MaxDrips = 8;
        public const int BlobCapacity = 16;
        public const int LightKey = 0x4E41;   // "NA": the one Namekuji light (MuzzleSystem.TrackLight)

        public const float SpreadShare = 0.10f, DryShare = 0.80f, FadeTailSeconds = 2f, EndFadeSeconds = 0.5f;
        public const float PollSeconds = 0.25f;
        public const float DripSeconds = 3f, DripRate = 2f;
        public const float Gravity = 9.81f;

        private static readonly Vector4 GelHdr = SpecialVfxAssets.Hdr(0xFF5FD2, 3f);
        private static readonly Vector4 DropHdr = SpecialVfxAssets.Hdr(0xFF8AE0, 2f);
        private static readonly Vector4 RimHdr = SpecialVfxAssets.Hdr(0xB040FF, 1.5f, 3f);
        private static readonly Vector4 SmearHdr = SpecialVfxAssets.Hdr(0xFF5FD2, 1f);
        private static readonly Color LightColour = SpecialVfxAssets.Srgb(0xFF5FD2);

        public static bool Fancy => VfxTier.Current != VfxTierLevel.Performance;

        public enum PuddlePhase { Spreading, Live, Drying, Fading }

        private struct Puddle
        {
            public bool Alive;
            public int Id;
            public Vector3 Centre;
            public Quaternion Rot;
            public float Radius, Born, ExpiresAt, EndedAt, NextPoll;
            public int Variant;
        }

        private struct Splat
        {
            public bool Alive;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Size, Born, Life;
        }

        private enum DropKind : byte { Flying, Spot, Run }

        private struct Drop
        {
            public bool Alive;
            public DropKind Kind;
            public Vector3 P, V;
            public float Size, Born, Life, FloorY;
        }

        private struct Smear
        {
            public bool Alive;
            public Vector3 A, B;
            public float Born, Life, Width;
        }

        private struct Drip
        {
            public bool Alive;
            public Transform Body;
            public float Born, Next;
        }

        private static GelSystem s_instance;

        private readonly Puddle[] _puddles = new Puddle[MaxPuddles];
        private readonly Splat[] _splats = new Splat[MaxSplats];
        private readonly Drop[] _drops = new Drop[MaxDrops];
        private readonly Smear[] _smears = new Smear[MaxSmears];
        private readonly Drip[] _drips = new Drip[MaxDrips];
        private int _nextDrop, _nextSmear;

        private const int PuddleDraw = MaxPuddles + MaxSplats;
        private const int GelDraw = BlobCapacity * 5 + MaxDrops;
        private readonly Matrix4x4[] _puddleM = new Matrix4x4[PuddleDraw];
        private readonly Vector4[] _puddleP = new Vector4[PuddleDraw], _puddleX = new Vector4[PuddleDraw];
        private readonly Matrix4x4[] _gelM = new Matrix4x4[GelDraw];
        private readonly Vector4[] _gelCol = new Vector4[GelDraw], _gelRim = new Vector4[GelDraw], _gelWob = new Vector4[GelDraw];
        private readonly Matrix4x4[] _identity = new Matrix4x4[MaxSmears];
        private readonly Vector4[] _sHead = new Vector4[MaxSmears], _sTail = new Vector4[MaxSmears], _sHc = new Vector4[MaxSmears],
            _sTc = new Vector4[MaxSmears], _sSpan = new Vector4[MaxSmears], _sExtra = new Vector4[MaxSmears];
        private MaterialPropertyBlock _puddleBlock, _gelBlock, _smearBlock;
        private Material _puddle, _gel, _ribbon;
        private int _groundMask;
        private bool _fancyApplied;

        // Player slide.
        private bool _sliding;
        private Vector3 _lastFeet;
        private float _nextSlideSmear, _nextSlideDrop;

        private static readonly int PuddleParamsId = Shader.PropertyToID("_PuddleParams"), PuddleExtraId = Shader.PropertyToID("_PuddleExtra"),
            GelColorId = Shader.PropertyToID("_GelColor"), GelRimId = Shader.PropertyToID("_GelRim"), GelWobbleId = Shader.PropertyToID("_GelWobble"),
            HeadId = Shader.PropertyToID("_Head"), TailId = Shader.PropertyToID("_Tail"), HeadColorId = Shader.PropertyToID("_HeadColor"),
            TailColorId = Shader.PropertyToID("_TailColor"), SpanId = Shader.PropertyToID("_Span"), ExtraId = Shader.PropertyToID("_Extra"),
            FancyId = Shader.PropertyToID("_Fancy");

        // ------------------------------------------------------------------ probes

        public static GelSystem Instance => s_instance;
        public bool Ready => _puddle != null;
        /// <summary>Test seam: the player the slide smears follow (null = the primary rig).</summary>
        public static Transform PlayerOverride { get; set; }
        public int PuddlesStarted { get; private set; }
        public int PuddlesEnded { get; private set; }
        public int Impacts { get; private set; }
        public int Runs { get; private set; }
        public int DropsEmitted { get; private set; }
        public int SmearsEmitted { get; private set; }
        public int Films { get; private set; }
        public int Liquefies { get; private set; }
        public int NozzleBursts { get; private set; }
        public int BlobsDrawn { get; private set; }
        public int PuddlesDrawn { get; private set; }
        public int GuardedSplats { get; private set; }
        public bool PlayerSliding => _sliding;
        public int LivePuddles { get { int n = 0; for (int i = 0; i < MaxPuddles; i++) if (_puddles[i].Alive) n++; return n; } }
        public int LiveDrops { get { int n = 0; for (int i = 0; i < MaxDrops; i++) if (_drops[i].Alive) n++; return n; } }
        public int LiveSmears { get { int n = 0; for (int i = 0; i < MaxSmears; i++) if (_smears[i].Alive) n++; return n; } }

        /// <summary>The visual phase of a puddle at its life share (tests: the §2.2 T table).</summary>
        public static PuddlePhase PhaseAt(float life01, float secondsLeft)
        {
            if (secondsLeft <= FadeTailSeconds) return PuddlePhase.Fading;
            if (life01 < SpreadShare) return PuddlePhase.Spreading;
            return life01 < DryShare ? PuddlePhase.Live : PuddlePhase.Drying;
        }

        /// <summary>Radius share while spreading: 0.6 → 1 over the first 10 % (ease-out quad).</summary>
        public static float SpreadScale(float life01)
        {
            float k = Mathf.Clamp01(life01 / SpreadShare);
            return 0.6f + 0.4f * (1f - (1f - k) * (1f - k));
        }

        /// <summary>§4.1 player guard: 0.5 when <paramref name="p"/> sits between the camera and the player's chest on screen, else 1.</summary>
        public static float GuardFactor(Vector3 camera, Vector3 chest, Vector3 p, float radius)
        {
            Vector3 ray = chest - camera;
            float len2 = ray.sqrMagnitude;
            if (len2 < 1e-4f) return 1f;
            float t = Vector3.Dot(p - camera, ray) / len2;
            if (t <= 0f || t >= 1f) return 1f;                       // behind the camera or behind the player: no cover
            Vector3 closest = camera + ray * t;
            return (p - closest).sqrMagnitude < (0.45f + radius) * (0.45f + radius) ? 0.5f : 1f;
        }

        // ------------------------------------------------------------------ life cycle

        public static GelSystem Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[GelSystem]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<GelSystem>();
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
            for (int i = 0; i < MaxSmears; i++) _identity[i] = Matrix4x4.identity;
            _puddleBlock = new MaterialPropertyBlock();
            _gelBlock = new MaterialPropertyBlock();
            _smearBlock = new MaterialPropertyBlock();
            _groundMask = (1 << 0) | (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            if (PrototypeShaders.IsReady)
            {
                _puddle = new Material(PrototypeShaders.VfxPuddle) { name = "DD_GelPuddle (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
                _puddle.SetFloat("_Mode", 0f);
                _puddle.SetTexture("_Atlas", SpecialVfxAssets.SplatAtlas);
                _gel = new Material(PrototypeShaders.VfxGel) { name = "DD_GelBlob (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
                _gel.SetFloat("_Additive", 0f);
                _ribbon = new Material(PrototypeShaders.VfxRibbon) { name = "DD_GelSmear (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            }

            SpecialVfxAssets.Warm();
            SceneManager.activeSceneChanged += OnSceneChanged;
            Rearm();
        }

        private void OnDestroy()
        {
            Disarm();
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (s_instance == this) s_instance = null;
            if (_puddle != null) Destroy(_puddle);
            if (_gel != null) Destroy(_gel);
            if (_ribbon != null) Destroy(_ribbon);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            Clear();
            Rearm();
        }

        public void Rearm()
        {
            Disarm();
            SpecialWeaponUsageEvents.Usage += OnUsage;
            ProjectileEvents.Impact += OnImpact;
            EnemyCombatEvents.Raised += OnBeat;
        }

        private void Disarm()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            ProjectileEvents.Impact -= OnImpact;
            EnemyCombatEvents.Raised -= OnBeat;
        }

        public void Clear()
        {
            for (int i = 0; i < MaxPuddles; i++) _puddles[i].Alive = false;
            for (int i = 0; i < MaxSplats; i++) _splats[i].Alive = false;
            for (int i = 0; i < MaxDrops; i++) _drops[i].Alive = false;
            for (int i = 0; i < MaxSmears; i++) _smears[i].Alive = false;
            for (int i = 0; i < MaxDrips; i++) _drips[i] = default;
            _sliding = false;
        }

        // ------------------------------------------------------------------ buses (public: the alloc test calls them directly)

        public void OnUsage(SpecialUsageEvent e)
        {
            if (e.Id != SpecialWeaponId.Namekuji) return;
            float now = Time.time;
            switch (e.Kind)
            {
                case SpecialUsageKind.Fired:
                    Nozzle(now, e.Origin, e.Direction);
                    break;
                case SpecialUsageKind.FieldSpawned:
                    StartPuddle(now, e.Count, e.Origin, e.Direction, e.Radius, e.Seconds);
                    break;
                case SpecialUsageKind.FieldEnded:
                    EndPuddle(now, e.Count);
                    break;
                case SpecialUsageKind.Hit:
                    if (e.Target.Root != null) FilmBody(now, e.Target.Root, e.Target.Receiver as ZombieEnemy);
                    break;
                case SpecialUsageKind.Liquefied:
                    Liquefies++;
                    Burst(now, e.Origin, Vector3.up, 10, 1.8f, 3.2f, 0.02f, 0.035f, e.Origin.y);
                    AddSplat(now, e.Origin + Vector3.up * 0.02f, Vector3.up, 0.6f, 0.35f);
                    break;
                case SpecialUsageKind.PlayerSlideStarted:
                    _sliding = true;
                    _lastFeet = e.Origin;
                    _nextSlideSmear = now;
                    _nextSlideDrop = now;
                    break;
                case SpecialUsageKind.PlayerSlideEnded:
                    _sliding = false;
                    break;
            }
        }

        public void OnImpact(ProjectileImpactInfo info)
        {
            if (info.Kind != ProjectileKind.Gel || !info.Stopped) return;
            PlayImpact(Time.time, info.Position, info.Normal);
        }

        public void OnBeat(EnemyCombatEventInfo info)
        {
            float now = Time.time;
            switch (info.Kind)
            {
                case EnemyCombatEventKind.SpecialSlide:
                {
                    Vector3 feet = info.Position;
                    Vector3 dir = info.Direction.sqrMagnitude > 1e-4f ? info.Direction : Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * 0.12f;
                    AddSmear(now, feet + side - dir * 1.0f + Vector3.up * 0.02f, feet + side + Vector3.up * 0.02f, 0.6f, 0.04f);
                    AddSmear(now, feet - side - dir * 1.0f + Vector3.up * 0.02f, feet - side + Vector3.up * 0.02f, 0.6f, 0.04f);
                    Burst(now, feet + Vector3.up * 0.05f, Vector3.up + dir * 0.5f, 4, 1.2f, 2.2f, 0.015f, 0.025f, feet.y);
                    if (info.Body != null && info.Body.TryGetComponent(out ZombieEnemy body)) FilmBody(now, info.Body, body);
                    break;
                }
                case EnemyCombatEventKind.SpecialFall:
                    AddSplat(now, info.Position + Vector3.up * 0.02f, Vector3.up, 0.5f, 0.3f);
                    break;
            }
        }

        // ------------------------------------------------------------------ events → records

        private void Nozzle(float now, Vector3 origin, Vector3 aim)
        {
            NozzleBursts++;
            Vector3 m = origin, fwd = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.forward;
            WeaponController c = WeaponRegistry.ActiveController;
            Transform muzzle = c != null && c.SpecialWeaponActive ? c.SpecialRigMuzzle : null;
            if (muzzle != null)
            {
                m = CameraModeService.Current == CameraMode.FirstPerson ? CameraView.ViewmodelToWorld(muzzle.position) : muzzle.position;
                fwd = muzzle.forward;
            }

            float floorY = m.y - 1.3f;
            if (Physics.Raycast(m, Vector3.down, out RaycastHit floor, 3f, _groundMask, QueryTriggerInteraction.Ignore)) floorY = floor.point.y;
            Burst(now, m + fwd * 0.05f, fwd + Vector3.down * 0.3f, 6, 1.0f, 2.2f, 0.02f, 0.02f, floorY, 0.3f);
            AddSplat(now, m + fwd * 0.08f, -fwd, 0.30f, 0.15f);
            MuzzleSystem.Instance?.FlashAt(m, LightColour, 6f, 4f, 0.05f, 0.15f);
        }

        /// <summary>A blob's stopping contact (public for the capture agent / tests).</summary>
        public void PlayImpact(float now, Vector3 point, Vector3 normal)
        {
            Impacts++;
            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            float floorY = n.y > 0.5f ? point.y : point.y - 2f;
            if (n.y <= 0.5f && Physics.Raycast(point + n * 0.1f, Vector3.down, out RaycastHit floor, 4f, _groundMask, QueryTriggerInteraction.Ignore)) floorY = floor.point.y;
            Burst(now, point + n * 0.03f, n, 8, 1.6f, 2.6f, 0.015f, 0.03f, floorY, 0.25f);   // 0.4 m fan, 0.25 s
            AddSplat(now, point + n * 0.01f, n, 0.6f, 0.2f);
            if (n.y < 0.5f)
            {
                // A wall: three runs down the face (0.4 m, 0.8 s); the puddle below is S-02's (FieldSpawned).
                Vector3 across = Vector3.Cross(n, Vector3.up).normalized;
                for (int k = -1; k <= 1; k++)
                {
                    SpawnDrop(DropKind.Run, point + n * 0.012f + across * (0.12f * k), Vector3.down * 0.5f, 0.03f, now, 0.8f, floorY);
                    Runs++;
                }
            }

            MuzzleSystem.Instance?.FlashAt(point + n * 0.1f, LightColour, 3f, 2f, 0.03f, 0.12f);
        }

        private void StartPuddle(float now, int id, Vector3 centre, Vector3 normal, float radius, float seconds)
        {
            if (radius <= 0f || seconds <= 0f) return;
            int slot = -1, oldest = 0;
            for (int i = 0; i < MaxPuddles; i++)
            {
                if (_puddles[i].Alive && _puddles[i].Id == id) { slot = i; break; }
                if (slot < 0 && !_puddles[i].Alive) slot = i;
                if (_puddles[i].Born < _puddles[oldest].Born) oldest = i;
            }

            if (slot < 0) slot = oldest;
            // A wall contact's normal is the wall's; the puddle itself always lies on the floor S-02 found below it.
            Vector3 up = normal.y > 0.7f ? normal.normalized : Vector3.up;
            uint h = (uint)id * 2654435761u;
            _puddles[slot] = new Puddle
            {
                Alive = true, Id = id, Centre = centre + up * (0.02f + 0.002f * (id % 5)), Radius = radius, Born = now,
                ExpiresAt = now + seconds, EndedAt = -1f, NextPoll = now + PollSeconds, Variant = (int)(h % 3u),
                Rot = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, SpecialVfxAssets.Hash01(h) * 360f, 0f)
            };
            PuddlesStarted++;
        }

        private void EndPuddle(float now, int id)
        {
            for (int i = 0; i < MaxPuddles; i++)
            {
                if (!_puddles[i].Alive || _puddles[i].Id != id) continue;
                if (_puddles[i].EndedAt < 0f) _puddles[i].EndedAt = now;
                PuddlesEnded++;
                return;
            }
        }

        private void FilmBody(float now, Transform root, ZombieEnemy body)
        {
            Films++;
            if (body != null && body.Skinned != null) ZombieSkinFx.Gel(body.Skinned);
            int slot = -1;
            for (int i = 0; i < MaxDrips; i++)
            {
                if (_drips[i].Alive && _drips[i].Body == root) { slot = i; break; }
                if (slot < 0 && !_drips[i].Alive) slot = i;
            }

            if (slot < 0) return;   // 8 dripping bodies at once: the film still plays
            _drips[slot] = new Drip { Alive = true, Body = root, Born = now, Next = now };
        }

        private void Burst(float now, Vector3 at, Vector3 axis, int count, float speedMin, float speedMax, float sizeMin, float sizeMax, float floorY, float life = 0.6f)
        {
            Vector3 n = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            for (int k = 0; k < count; k++)
            {
                uint h = (uint)(DropsEmitted + k) * 2654435761u;
                Vector3 v = (n + SpecialVfxAssets.HashDir(h) * 0.8f).normalized * Mathf.Lerp(speedMin, speedMax, SpecialVfxAssets.Hash01(h + 1u));
                SpawnDrop(DropKind.Flying, at, v, Mathf.Lerp(sizeMin, sizeMax, SpecialVfxAssets.Hash01(h + 2u)), now, life, floorY);
            }
        }

        private void SpawnDrop(DropKind kind, Vector3 p, Vector3 v, float size, float now, float life, float floorY)
        {
            ref Drop d = ref _drops[_nextDrop];
            _nextDrop = (_nextDrop + 1) % MaxDrops;   // a ring: the oldest droplet makes room
            d = new Drop { Alive = true, Kind = kind, P = p, V = v, Size = size, Born = now, Life = life, FloorY = floorY };
            DropsEmitted++;
        }

        private void AddSplat(float now, Vector3 pos, Vector3 normal, float size, float life)
        {
            int slot = 0;
            for (int i = 0; i < MaxSplats; i++)
            {
                if (!_splats[i].Alive) { slot = i; break; }
                if (_splats[i].Born < _splats[slot].Born) slot = i;
            }

            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            _splats[slot] = new Splat
            {
                Alive = true, Pos = pos, Size = size, Born = now, Life = life,
                Rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, SpecialVfxAssets.Hash01((uint)Impacts * 97u + (uint)DropsEmitted) * 360f, 0f)
            };
        }

        private void AddSmear(float now, Vector3 a, Vector3 b, float life, float width)
        {
            _smears[_nextSmear] = new Smear { Alive = true, A = a, B = b, Born = now, Life = life, Width = width };
            _nextSmear = (_nextSmear + 1) % MaxSmears;
            SmearsEmitted++;
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            using var marker = s_marker.Auto();
            Frame(Time.time, Time.deltaTime);
        }

        /// <summary>One frame: step + draw (public: the alloc test runs whole frames on the calling thread).</summary>
        public void Frame(float now, float dt)
        {
            Step(now, dt);
            Draw(now);
        }

        public void Step(float now, float dt)
        {
            ZombieSkinFx.Tick(now);

            for (int i = 0; i < MaxPuddles; i++)
            {
                ref Puddle p = ref _puddles[i];
                if (!p.Alive) continue;
                if (p.EndedAt < 0f && now >= p.NextPoll)
                {
                    p.NextPoll = now + PollSeconds;
                    if (GelPuddleRegistry.TryGet(p.Id, out GelPuddleInfo info) && info.ExpiresAt > p.Born) p.ExpiresAt = info.ExpiresAt;
                }

                bool ended = p.EndedAt >= 0f && now - p.EndedAt >= EndFadeSeconds;
                if (ended || now > p.ExpiresAt + EndFadeSeconds) p.Alive = false;
            }

            for (int i = 0; i < MaxSplats; i++)
            {
                if (_splats[i].Alive && now - _splats[i].Born > _splats[i].Life) _splats[i].Alive = false;
            }

            for (int i = 0; i < MaxSmears; i++)
            {
                if (_smears[i].Alive && now - _smears[i].Born > _smears[i].Life) _smears[i].Alive = false;
            }

            for (int i = 0; i < MaxDrops; i++)
            {
                ref Drop d = ref _drops[i];
                if (!d.Alive) continue;
                if (now - d.Born > d.Life) { d.Alive = false; continue; }
                switch (d.Kind)
                {
                    case DropKind.Flying:
                        d.V.y -= Gravity * dt;
                        d.P += d.V * dt;
                        if (d.P.y <= d.FloorY)
                        {
                            // Landed: a 0.1 m spot for 1.5 s (§2.2 "yere 0,1 m nokta").
                            d.Kind = DropKind.Spot;
                            d.P.y = d.FloorY + 0.006f;
                            d.V = Vector3.zero;
                            d.Born = now;
                            d.Life = 1.5f;
                        }

                        break;
                    case DropKind.Run:
                        d.P += d.V * dt;
                        if (d.P.y <= d.FloorY) d.Alive = false;
                        break;
                }
            }

            for (int i = 0; i < MaxDrips; i++)
            {
                ref Drip dr = ref _drips[i];
                if (!dr.Alive) continue;
                if (dr.Body == null || now - dr.Born > DripSeconds) { dr = default; continue; }
                if (now < dr.Next) continue;
                dr.Next = now + 1f / DripRate;
                uint h = (uint)(DropsEmitted * 7919 + i);
                Vector3 root = dr.Body.position;
                Vector3 at = root + Vector3.up * Mathf.Lerp(0.6f, 1.3f, SpecialVfxAssets.Hash01(h)) + dr.Body.right * (SpecialVfxAssets.Hash01(h + 1u) * 0.3f - 0.15f);
                SpawnDrop(DropKind.Flying, at, Vector3.zero, 0.02f, now, 3f, root.y);
            }

            if (_sliding)
            {
                Transform player = PlayerOverride != null ? PlayerOverride : (PlayerRegistry.Primary != null ? PlayerRegistry.Primary.transform : null);
                if (player != null)
                {
                    Vector3 feet = player.position;
                    if (now >= _nextSlideSmear)
                    {
                        _nextSlideSmear = now + 0.1f;
                        Vector3 move = feet - _lastFeet;
                        move.y = 0f;
                        if (move.sqrMagnitude > 0.0025f)
                        {
                            Vector3 side = Vector3.Cross(Vector3.up, move.normalized) * 0.1f;
                            AddSmear(now, _lastFeet + side + Vector3.up * 0.02f, feet + side + Vector3.up * 0.02f, 1.0f, 0.04f);
                            AddSmear(now, _lastFeet - side + Vector3.up * 0.02f, feet - side + Vector3.up * 0.02f, 1.0f, 0.04f);
                            _lastFeet = feet;
                        }
                    }

                    if (now >= _nextSlideDrop)
                    {
                        _nextSlideDrop = now + 1f / 3f;   // 3 / s
                        Burst(now, feet + Vector3.up * 0.08f, Vector3.up, 1, 1.0f, 1.8f, 0.015f, 0.02f, feet.y);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ draw

        private void Draw(float now)
        {
            if (_puddle == null) return;
            if (_fancyApplied != Fancy)
            {
                _fancyApplied = Fancy;
                _puddle.SetFloat(FancyId, _fancyApplied ? 1f : 0f);
            }

            bool fp = CameraModeService.Current == CameraMode.FirstPerson;
            bool haveCam = FeedbackCameraRef.TryGetPosition(out Vector3 cam);
            Transform player = PlayerRegistry.Primary != null ? PlayerRegistry.Primary.transform : null;
            bool guard = haveCam && !fp && player != null;
            Vector3 chest = player != null ? player.position + Vector3.up * 1.3f : Vector3.zero;

            // Puddles + splats: one instanced call on DD/VFX Puddle.
            int n = 0;
            for (int i = 0; i < MaxPuddles; i++)
            {
                ref Puddle p = ref _puddles[i];
                if (!p.Alive) continue;
                float span = Mathf.Max(0.1f, p.ExpiresAt - p.Born);
                float life01 = Mathf.Clamp01((now - p.Born) / span);
                float dry = Mathf.Clamp01((life01 - DryShare) / (1f - DryShare));
                float alpha = Mathf.Clamp01((p.ExpiresAt - now) / FadeTailSeconds);
                if (p.EndedAt >= 0f) alpha *= Mathf.Clamp01(1f - (now - p.EndedAt) / EndFadeSeconds);
                float d = p.Radius * 2f * SpreadScale(life01);
                _puddleM[n] = Matrix4x4.TRS(p.Centre, p.Rot, new Vector3(d, 1f, d));
                _puddleP[n] = new Vector4(life01, p.Variant, dry, alpha);
                _puddleX[n] = new Vector4(p.Id * 1.37f, p.Radius, 0f, 0f);
                n++;
            }

            PuddlesDrawn = n;
            for (int i = 0; i < MaxSplats; i++)
            {
                ref Splat s = ref _splats[i];
                if (!s.Alive) continue;
                float k = Mathf.Clamp01((now - s.Born) / Mathf.Max(0.01f, s.Life));
                float size = s.Size * (0.5f + 0.5f * (1f - (1f - k) * (1f - k)));
                float alpha = 1f - k * k;
                if (guard)
                {
                    float gf = GuardFactor(cam, chest, s.Pos, s.Size * 0.5f);
                    if (gf < 1f) GuardedSplats++;
                    alpha *= gf;
                }

                _puddleM[n] = Matrix4x4.TRS(s.Pos, s.Rot, new Vector3(size, 1f, size));
                _puddleP[n] = new Vector4(0.3f, i % 3, 0f, alpha);
                _puddleX[n] = new Vector4(i * 3.1f, s.Size * 0.5f, 1f, 0f);   // z = 1: a splat (no bubbles)
                n++;
            }

            if (n > 0)
            {
                _puddleBlock.SetVectorArray(PuddleParamsId, _puddleP);
                _puddleBlock.SetVectorArray(PuddleExtraId, _puddleX);
                Graphics.DrawMeshInstanced(SpecialVfxAssets.GroundQuad, 0, _puddle, _puddleM, n, _puddleBlock, ShadowCastingMode.Off, true, 0, null, LightProbeUsage.Off);
            }

            // Blobs (+ 4 tail droplets each, read from the projectile state) and the droplet pool: one call on DD/VFX Gel.
            int g = 0, blobs = 0;
            Vector3 lightAt = Vector3.zero;
            float lightD = float.MaxValue;
            int live = ProjectileSystem.LiveCount;
            for (int i = 0; i < live && blobs < BlobCapacity; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                if (p.Kind != ProjectileKind.Gel) continue;
                Vector3 pos = p.Position;
                float camDist = haveCam ? (pos - cam).magnitude : 100f;
                if (fp && camDist < 0.5f) continue;                             // FP: born 0.5 m out of the eye
                Vector3 dir = p.Direction;
                Quaternion rot = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right);
                _gelM[g] = Matrix4x4.TRS(pos, rot, new Vector3(0.13f, 0.13f, 0.18f));   // 0.18 m, × 1.4 along the flight
                _gelCol[g] = new Vector4(GelHdr.x, GelHdr.y, GelHdr.z, 0.92f);
                _gelRim[g] = RimHdr;
                _gelWob[g] = new Vector4(0.015f, 6f, p.Generation * 0.37f, 1f);
                g++;
                blobs++;
                for (int k = 1; k <= 4; k++)
                {
                    Vector3 tp = pos - dir * (0.10f + 0.07f * k) + Vector3.down * (0.006f * k * k);
                    float ts = 0.035f - 0.004f * k;
                    _gelM[g] = Matrix4x4.TRS(tp, rot, new Vector3(ts, ts, ts * 1.3f));
                    _gelCol[g] = new Vector4(DropHdr.x, DropHdr.y, DropHdr.z, 0.9f - 0.12f * k);
                    _gelRim[g] = RimHdr;
                    _gelWob[g] = new Vector4(0.003f, 8f, p.Generation + k, 1f);
                    g++;
                }

                if (camDist < lightD) { lightD = camDist; lightAt = pos; }
            }

            BlobsDrawn = blobs;
            if (blobs > 0) MuzzleSystem.Instance?.TrackLight(LightKey, lightAt, LightColour, 3f, 3f);

            for (int i = 0; i < MaxDrops && g < GelDraw; i++)
            {
                ref Drop d = ref _drops[i];
                if (!d.Alive) continue;
                float k = Mathf.Clamp01((now - d.Born) / Mathf.Max(0.01f, d.Life));
                Vector3 scale;
                Quaternion rot;
                switch (d.Kind)
                {
                    case DropKind.Spot:
                        scale = new Vector3(0.1f, 0.012f, 0.1f) * (1f - 0.3f * k);
                        rot = Quaternion.identity;
                        break;
                    case DropKind.Run:
                        scale = new Vector3(d.Size, d.Size * 2.2f, d.Size * 0.5f);
                        rot = Quaternion.identity;
                        break;
                    default:
                        float speed = d.V.magnitude;
                        scale = new Vector3(d.Size, d.Size, d.Size * (1f + Mathf.Min(1.5f, speed * 0.15f)));
                        rot = speed > 0.01f ? Quaternion.LookRotation(d.V / speed, Mathf.Abs(d.V.y / speed) < 0.95f ? Vector3.up : Vector3.right) : Quaternion.identity;
                        break;
                }

                _gelM[g] = Matrix4x4.TRS(d.P, rot, scale);
                _gelCol[g] = new Vector4(DropHdr.x, DropHdr.y, DropHdr.z, 0.9f * (1f - k * k));
                _gelRim[g] = RimHdr;
                _gelWob[g] = new Vector4(0.002f, 9f, i, 1f);
                g++;
            }

            if (g > 0)
            {
                _gelBlock.SetVectorArray(GelColorId, _gelCol);
                _gelBlock.SetVectorArray(GelRimId, _gelRim);
                _gelBlock.SetVectorArray(GelWobbleId, _gelWob);
                Graphics.DrawMeshInstanced(SpecialVfxAssets.Sphere, 0, _gel, _gelM, g, _gelBlock, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            }

            // Smears: DD/VFX Ribbon, uniform strips (bead share 1), fading over their life.
            int ns = 0;
            float fpFlag = fp ? 1f : 0f;
            for (int i = 0; i < MaxSmears; i++)
            {
                ref Smear m = ref _smears[i];
                if (!m.Alive) continue;
                float k = Mathf.Clamp01((now - m.Born) / Mathf.Max(0.01f, m.Life));
                float len = (m.B - m.A).magnitude;
                _sHead[ns] = new Vector4(m.B.x, m.B.y, m.B.z, m.Width);
                _sTail[ns] = new Vector4(m.A.x, m.A.y, m.A.z, 1f);
                _sHc[ns] = new Vector4(SmearHdr.x, SmearHdr.y, SmearHdr.z, 0.6f * (1f - k));
                _sTc[ns] = new Vector4(SmearHdr.x, SmearHdr.y, SmearHdr.z, 0f);
                _sSpan[ns] = new Vector4(1f, 1f + len, 0f, 0f);
                _sExtra[ns] = new Vector4(1.5f, 0f, fpFlag, 0f);
                ns++;
            }

            if (ns > 0 && _ribbon != null)
            {
                _smearBlock.SetVectorArray(HeadId, _sHead);
                _smearBlock.SetVectorArray(TailId, _sTail);
                _smearBlock.SetVectorArray(HeadColorId, _sHc);
                _smearBlock.SetVectorArray(TailColorId, _sTc);
                _smearBlock.SetVectorArray(SpanId, _sSpan);
                _smearBlock.SetVectorArray(ExtraId, _sExtra);
                Graphics.DrawMeshInstanced(SpecialVfxAssets.RibbonQuad, 0, _ribbon, _identity, ns, _smearBlock, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            PlayerOverride = null;
        }
    }
}
