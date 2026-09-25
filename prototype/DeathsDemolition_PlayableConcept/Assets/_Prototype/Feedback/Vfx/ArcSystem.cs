using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Enemies;
using DD.Prototype.Rendering;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B6 · Raiju (sw_arc, DG-2-like chain lightning — direction §2.1, §4, §5 row "Raiju ark / dal / rim").
    ///
    /// Everything is drawn from S-02's WP-069 B4 surface, nothing is pushed by the weapon:
    ///   · ChargeStarted (Seconds 0.25) → three 0.15 m arcs round the muzzle electrode, re-rolled every 30 ms, and the
    ///     #7FD3FF light ramping 0 → 8 / 3 m; ChargeCancelled / Deactivated end it (the weapon glow's coil ramp is
    ///     <see cref="SpecialWeaponGlow"/>'s).
    ///   · Fired → the ring discharge: three arcs between three electrodes on a Ø 0.50 ring for 0.20 s, a 0.5 s
    ///     after-flicker, an ozone puff (α 0.15, Ø 0.4 m, 0.6 s) and a #7FD3FF / 20 / 9 m / 0.25 s flash.
    ///   · The orb in flight is READ each frame from <see cref="ProjectileSystem"/> (Kind Orb; ADR-069-1 — no event per
    ///     frame): a Ø 0.25 core (DD/VFX Gel, emissive #EAF6FF × 10, wobble × 0.3), a Ø 0.45 additive plasma shell
    ///     rippling at 4 Hz, 4–6 tendrils of 0.6 m re-rolled at 10 Hz (stateless: hashed from the projectile id and the
    ///     tick), the ground arcs when the floor is within 2.5 m (0.5 m ring, 3 arcs / 0.1 s + a bright spot), and one
    ///     tracked light #7FD3FF / 8 / 6 m. First person: hidden inside 0.6 m of the eye, HDR × 0.4 for its first 0.3 s.
    ///     Past the tier's mesh distance (∞ / 25 / 15 m) the orb is a ribbon head-dot.
    ///   · Hit per hop (Count = hop index, Origin = the previous body's centre or the orb contact, Origin + Direction =
    ///     this body's centre, Seconds = its tremble): the main arc — 16 points re-sampled every 33 ms, ± (0.08 × d / 6 m)
    ///     jitter in two octaves, core 0.06 m × 8 + halo 0.25 m #A08CFF × 1.5, visible 0.12 s — its branches (2 / 3 / 4
    ///     per re-sample by tier, 25–40 % of the arc, 0.02 m, 40 ms), 12 tip sparks, the light jumping to the body, the
    ///     electrocution (rim via <see cref="ZombieSkinFx"/>, joint arcs head–chest–hips–hands every 60 ms on
    ///     Cinematic / High, head smoke from 0.6 s — 4 puffs/s for 1.5 s α 0.3 + 2 embers, the feet arc ring on
    ///     Cinematic) and a Lichtenberg burn under the feet (0.6 m, #2A2420 α 0.6, 10 s).
    ///   · FieldEnded: Count 1 = the orb reached a body (a contact flash); Count 0 = fizzle (Seconds 0.3: a crackle of
    ///     short arcs, 6 sparks, a 0.4 m burn when the floor is near).
    ///   · SelfShock → two arcs from the chain body into the player's chest and hips (0.4 s).
    /// Caps (§5): main arcs 8 / 8 / 6 (the oldest is retired — a new hop always shows), branches 32 / 24 / 12, shocked
    /// bodies 8, burns 24. Arcs are camera-facing DD/VFX Ribbon segments (bead share 1 = uniform strip) drawn with
    /// Graphics.DrawMeshInstanced — one call for cores, one for halos — so the whole effect is ≤ 5 draws. 0 B per frame.
    /// </summary>
    [DefaultExecutionOrder(955)]
    public sealed class ArcSystem : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.ArcSystem");

        public const int MaxArcs = 64;
        public const int MaxPoints = 16;
        public const int MaxBranches = 32;
        public const int MaxShocks = 8;
        public const int MaxBurns = 24;
        public const int CoreCapacity = 768;
        public const int HaloCapacity = 256;
        public const int OrbCapacity = 16;
        public const int LightKey = 0x5241;   // "RA": the one Raiju light (MuzzleSystem.TrackLight)

        public const float ResampleSeconds = 0.033f;
        public const float HopVisibleSeconds = 0.12f;
        public const float BranchSeconds = 0.04f;
        public const float JointArcRefresh = 0.06f;
        public const float BurnSeconds = 10f;
        public const float BurnSize = 0.6f, FizzleBurnSize = 0.4f;
        public const float GroundArcReach = 2.5f;

        // Direction §2.1 palette (sRGB hex → linear HDR).
        private static readonly Vector4 CoreHdr = SpecialVfxAssets.Hdr(0xEAF6FF, 8f);
        private static readonly Vector4 HaloHdr = SpecialVfxAssets.Hdr(0xA08CFF, 1.5f, 0.35f);
        private static readonly Vector4 BranchHdr = SpecialVfxAssets.Hdr(0x7FD3FF, 4f);
        private static readonly Vector4 GroundHdr = SpecialVfxAssets.Hdr(0xBFE9FF, 6f);
        private static readonly Vector4 OrbCoreHdr = SpecialVfxAssets.Hdr(0xEAF6FF, 10f);
        private static readonly Vector4 OrbShellHdr = SpecialVfxAssets.Hdr(0x7FD3FF, 4f);
        private static readonly Vector4 OrbRim = SpecialVfxAssets.Hdr(0x7FD3FF, 2f, 2f);
        private static readonly Color LightColour = SpecialVfxAssets.Srgb(0x7FD3FF);
        private static readonly Color32 SparkColour = new Color32(191, 233, 255, 255);   // #BFE9FF
        private static readonly Color32 EmberColour = new Color32(255, 120, 60, 255);
        private static readonly Color SmokeColour = new Color(0.62f, 0.68f, 0.78f, 0.3f);
        private static readonly Color OzoneColour = new Color(0.80f, 0.90f, 1f, 0.15f);

        /// <summary>Tier numbers (direction §5 row "Raiju ark / dal / rim"; WP-069 §9 dal 4 / 3 / 2).</summary>
        public static int MainArcCap => VfxTier.Pick(6, 8, 8);
        public static int BranchCap => VfxTier.Pick(12, 24, 32);
        public static int BranchesPerHop => VfxTier.Pick(2, 3, 4);
        public static bool JointArcs => VfxTier.Current != VfxTierLevel.Performance;
        public static bool FeetRing => VfxTier.Current == VfxTierLevel.Cinematic;
        public static float MeshDistance => VfxTier.Pick(15f, 25f, float.MaxValue);

        private enum ArcKind : byte { Chain, Discharge, Self, Fizzle, Charge, Joint, Ground }

        private struct Arc
        {
            public bool Alive;
            public ArcKind Kind;
            public Vector3 From, To;
            public Transform FromT, ToT;
            public float Born, Life, NextSample;
            public int Points;
            public uint Seed;
            public int Samples;
            public float Amp;          // < 0: relative (× length / 6 m), ≥ 0: metres
            public float Core, Halo;
        }

        private struct Branch
        {
            public bool Alive;
            public Vector3 A, B;
            public float Born;
        }

        private struct Shock
        {
            public bool Alive;
            public Transform Root;
            public Transform Head, Chest, Hips, HandL, HandR;
            public float Born, Seconds;
            public float NextJoint, NextRing, NextSmoke;
            public int Puffs;
            public bool Embers;
        }

        private struct Burn
        {
            public bool Alive;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Size, Born;
            public int Variant;
        }

        private static ArcSystem s_instance;

        private readonly Arc[] _arcs = new Arc[MaxArcs];
        private readonly Vector3[] _pts = new Vector3[MaxArcs * MaxPoints];
        private readonly Branch[] _branches = new Branch[MaxBranches];
        private readonly Shock[] _shocks = new Shock[MaxShocks];
        private readonly Burn[] _burns = new Burn[MaxBurns];

        // Instanced draw data (fixed-size arrays: MPB arrays keep their first length).
        private readonly Matrix4x4[] _identity = new Matrix4x4[CoreCapacity];
        private readonly Vector4[] _cHead = new Vector4[CoreCapacity], _cTail = new Vector4[CoreCapacity], _cHc = new Vector4[CoreCapacity],
            _cTc = new Vector4[CoreCapacity], _cSpan = new Vector4[CoreCapacity], _cExtra = new Vector4[CoreCapacity];
        private readonly Vector4[] _hHead = new Vector4[HaloCapacity], _hTail = new Vector4[HaloCapacity], _hHc = new Vector4[HaloCapacity],
            _hTc = new Vector4[HaloCapacity], _hSpan = new Vector4[HaloCapacity], _hExtra = new Vector4[HaloCapacity];
        private readonly Matrix4x4[] _orbM = new Matrix4x4[OrbCapacity], _shellM = new Matrix4x4[OrbCapacity];
        private readonly Vector4[] _orbCol = new Vector4[OrbCapacity], _orbRim = new Vector4[OrbCapacity], _orbWob = new Vector4[OrbCapacity];
        private readonly Vector4[] _shellCol = new Vector4[OrbCapacity], _shellRim = new Vector4[OrbCapacity], _shellWob = new Vector4[OrbCapacity];
        private readonly Matrix4x4[] _burnM = new Matrix4x4[MaxBurns];
        private readonly Vector4[] _burnP = new Vector4[MaxBurns];
        private int _nCore, _nHalo;

        private MaterialPropertyBlock _coreBlock, _haloBlock, _orbBlock, _shellBlock, _burnBlock;
        private Material _ribbon, _orbCore, _orbShell, _burn;
        private int _nextArc, _nextBranch;
        private int _groundMask;

        // Charge / discharge state.
        private bool _charging;
        private float _chargeBorn, _chargeSeconds, _nextChargeArc;
        private float _afterFlickerUntil, _nextAfterFlicker;
        private Vector3 _hopLightPos;
        private float _hopLightUntil;

        private static readonly int HeadId = Shader.PropertyToID("_Head"), TailId = Shader.PropertyToID("_Tail"),
            HeadColorId = Shader.PropertyToID("_HeadColor"), TailColorId = Shader.PropertyToID("_TailColor"),
            SpanId = Shader.PropertyToID("_Span"), ExtraId = Shader.PropertyToID("_Extra"),
            GelColorId = Shader.PropertyToID("_GelColor"), GelRimId = Shader.PropertyToID("_GelRim"), GelWobbleId = Shader.PropertyToID("_GelWobble"),
            PuddleParamsId = Shader.PropertyToID("_PuddleParams"), PuddleExtraId = Shader.PropertyToID("_PuddleExtra");

        // ------------------------------------------------------------------ probes (tests / evidence)

        public static ArcSystem Instance => s_instance;
        public bool Ready => _ribbon != null;
        public int ChainArcs { get; private set; }
        public int Discharges { get; private set; }
        public int ChargeArcs { get; private set; }
        public int Fizzles { get; private set; }
        public int SelfShocks { get; private set; }
        public int Shocks { get; private set; }
        public int BurnsPlaced { get; private set; }
        public int SparksAsked { get; private set; }
        public int SmokePuffs { get; private set; }
        public int BranchesSpawned { get; private set; }
        /// <summary>Orbs drawn last frame, as a mesh or (past the tier's mesh distance) a head-dot.</summary>
        public int OrbsDrawn { get; private set; }
        public int OrbMeshes { get; private set; }
        public int CoreInstances => _nCore;
        public int HaloInstances => _nHalo;
        public bool Charging => _charging;

        public int LiveMainArcs => CountArcs(true);
        public int LiveArcs => CountArcs(false);
        public int LiveBranches { get { int n = 0; for (int i = 0; i < MaxBranches; i++) if (_branches[i].Alive) n++; return n; } }
        public int LiveShocks { get { int n = 0; for (int i = 0; i < MaxShocks; i++) if (_shocks[i].Alive) n++; return n; } }
        public int LiveBurns { get { int n = 0; for (int i = 0; i < MaxBurns; i++) if (_burns[i].Alive) n++; return n; } }

        // ------------------------------------------------------------------ life cycle

        /// <summary>Called by <see cref="VfxDressing.Configure"/> (next to ImpactSystem.Ensure): one DDOL instance, re-armed.</summary>
        public static ArcSystem Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[ArcSystem]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<ArcSystem>();
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
            for (int i = 0; i < CoreCapacity; i++) _identity[i] = Matrix4x4.identity;
            _coreBlock = new MaterialPropertyBlock();
            _haloBlock = new MaterialPropertyBlock();
            _orbBlock = new MaterialPropertyBlock();
            _shellBlock = new MaterialPropertyBlock();
            _burnBlock = new MaterialPropertyBlock();
            _groundMask = (1 << 0) | (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            BuildMaterials();
            SpecialVfxAssets.Warm();
            SceneManager.activeSceneChanged += OnSceneChanged;
            Rearm();
        }

        private void BuildMaterials()
        {
            if (!PrototypeShaders.IsReady) return;   // library not rebuilt yet: the system stays dark (Ready false), never Shader.Find
            _ribbon = new Material(PrototypeShaders.VfxRibbon) { name = "DD_ArcRibbon (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            _orbCore = new Material(PrototypeShaders.VfxGel) { name = "DD_OrbCore (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            _orbCore.SetFloat("_Additive", 0f);
            _orbShell = new Material(PrototypeShaders.VfxGel) { name = "DD_OrbShell (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            _orbShell.SetFloat("_Additive", 1f);
            _orbShell.SetFloat("_SrcBlend", (float)BlendMode.One);
            _orbShell.SetFloat("_DstBlend", (float)BlendMode.One);
            _burn = new Material(PrototypeShaders.VfxPuddle) { name = "DD_ArcBurn (runtime)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            _burn.SetFloat("_Mode", 1f);
            _burn.SetTexture("_Atlas", SpecialVfxAssets.BurnAtlas);
        }

        private void OnDestroy()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (s_instance == this) s_instance = null;
            if (_ribbon != null) Destroy(_ribbon);
            if (_orbCore != null) Destroy(_orbCore);
            if (_orbShell != null) Destroy(_orbShell);
            if (_burn != null) Destroy(_burn);
        }

        // Static-bus rearm lesson (ResetForTests / ResetStatics drop every subscriber): remove-then-add on each scene change.
        private void OnSceneChanged(Scene from, Scene to)
        {
            Clear();
            Rearm();
        }

        public void Rearm()
        {
            SpecialWeaponUsageEvents.Usage -= OnUsage;
            SpecialWeaponUsageEvents.Usage += OnUsage;
        }

        /// <summary>Every record off (scene change, run end, tests).</summary>
        public void Clear()
        {
            for (int i = 0; i < MaxArcs; i++) _arcs[i].Alive = false;
            for (int i = 0; i < MaxBranches; i++) _branches[i].Alive = false;
            for (int i = 0; i < MaxShocks; i++) _shocks[i] = default;
            for (int i = 0; i < MaxBurns; i++) _burns[i].Alive = false;
            _charging = false;
            _afterFlickerUntil = 0f;
            _hopLightUntil = 0f;
            ZombieSkinFx.Clear();
        }

        // ------------------------------------------------------------------ the usage bus (public: the alloc test calls it directly)

        public void OnUsage(SpecialUsageEvent e)
        {
            if (e.Id != SpecialWeaponId.Raiju) return;
            float now = Time.time;
            switch (e.Kind)
            {
                case SpecialUsageKind.ChargeStarted:
                    _charging = true;
                    _chargeBorn = now;
                    _chargeSeconds = Mathf.Max(0.05f, e.Seconds);
                    _nextChargeArc = now;
                    break;
                case SpecialUsageKind.ChargeCancelled:
                case SpecialUsageKind.Deactivated:
                    _charging = false;
                    break;
                case SpecialUsageKind.Fired:
                    _charging = false;
                    Discharge(now, e.Origin, e.Direction);
                    break;
                case SpecialUsageKind.Hit:
                    Hop(now, in e);
                    break;
                case SpecialUsageKind.FieldEnded:
                    if (e.Count == 0) Fizzle(now, e.Origin, e.Seconds);
                    else MuzzleSystem.Instance?.FlashAt(e.Origin, LightColour, 10f, 5f, 0.03f, 0.15f);
                    break;
                case SpecialUsageKind.SelfShock:
                    SelfShock(now, e.Origin, e.Origin + e.Direction);
                    break;
            }
        }

        // ------------------------------------------------------------------ events → records

        private void Discharge(float now, Vector3 fallbackOrigin, Vector3 fallbackForward)
        {
            if (!MuzzlePose(fallbackOrigin, fallbackForward, out Vector3 m, out Vector3 fwd, out Vector3 right, out Vector3 up)) return;
            Discharges++;
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = Electrode(m, right, up, k), b = Electrode(m, right, up, (k + 1) % 3);
                // Capture 1: 5 points at 3.5 cm read as a clean triangle (a UI glyph, not electricity) — 9 points at 9 cm, and a
                // thinner second arc per edge on its own seed so the ring crackles.
                SpawnArc(ArcKind.Discharge, a, b, null, null, now, 0.20f, 9, 0.09f, 0.03f, 0.12f);
                SpawnArc(ArcKind.Discharge, a, b, null, null, now, 0.14f, 9, 0.12f, 0.015f, 0.06f);
            }

            _afterFlickerUntil = now + 0.20f + 0.5f;
            _nextAfterFlicker = now + 0.20f;
            MuzzleSystem.Instance?.PuffAt(m + fwd * 0.1f, 0.4f, 0.6f, OzoneColour, fwd * 0.2f);
            MuzzleSystem.Instance?.FlashAt(m, LightColour, 20f, 9f, 0.05f, 0.20f);
        }

        private static Vector3 Electrode(Vector3 muzzle, Vector3 right, Vector3 up, int k)
        {
            float a = Mathf.PI * 0.5f + k * Mathf.PI * 2f / 3f;
            return muzzle + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.25f;   // Ø 0.50 ring
        }

        private void Hop(float now, in SpecialUsageEvent e)
        {
            Vector3 from = e.Origin;
            Vector3 to = e.Origin + e.Direction;
            ChainArcs++;
            SpawnArc(ArcKind.Chain, from, to, null, null, now, HopVisibleSeconds, MaxPoints, -0.08f, 0.06f, 0.25f);
            _hopLightPos = to;
            _hopLightUntil = now + HopVisibleSeconds;
            ImpactSystem.Instance?.EmitSparks(to, -e.Direction, 12, SparkColour, 2.5f, 5.5f, 0.12f, 0.2f);
            SparksAsked += 12;

            Transform root = e.Target.Root;
            if (root == null) return;
            ZombieSkinnedVisual visual = (e.Target.Receiver as ZombieEnemy) != null ? ((ZombieEnemy)e.Target.Receiver).Skinned : null;
            float seconds = Mathf.Max(0.3f, e.Seconds);
            AddShock(now, root, visual, seconds);
            PlaceBurn(now, root.position, Vector3.up, BurnSize);
        }

        private void AddShock(float now, Transform root, ZombieSkinnedVisual visual, float seconds)
        {
            int slot = -1, oldest = 0;
            for (int i = 0; i < MaxShocks; i++)
            {
                if (_shocks[i].Alive && _shocks[i].Root == root) { slot = i; break; }
                if (slot < 0 && !_shocks[i].Alive) slot = i;
                if (_shocks[i].Born < _shocks[oldest].Born) oldest = i;
            }

            if (slot < 0) slot = oldest;
            ref Shock s = ref _shocks[slot];
            if (s.Alive && s.Root == root)
            {
                s.Seconds = Mathf.Max(s.Seconds, now - s.Born + seconds);   // a new hop extends, never stacks
            }
            else
            {
                Animator anim = visual != null ? visual.Animator : null;
                s = new Shock
                {
                    Alive = true, Root = root, Born = now, Seconds = seconds, NextJoint = now, NextRing = now, NextSmoke = now + 0.6f,
                    Head = HumanoidBones.Bone(anim, HumanBodyBones.Head), Chest = HumanoidBones.Bone(anim, HumanBodyBones.Chest),
                    Hips = HumanoidBones.Bone(anim, HumanBodyBones.Hips), HandL = HumanoidBones.Bone(anim, HumanBodyBones.LeftHand),
                    HandR = HumanoidBones.Bone(anim, HumanBodyBones.RightHand)
                };
                Shocks++;
            }

            if (visual != null) ZombieSkinFx.Shock(visual, seconds);
        }

        private void Fizzle(float now, Vector3 at, float seconds)
        {
            Fizzles++;
            seconds = Mathf.Max(0.1f, seconds);
            for (int k = 0; k < 2; k++)
            {
                Vector3 a = at + SpecialVfxAssets.HashDir((uint)(Fizzles * 17 + k)) * 0.25f;
                SpawnArc(ArcKind.Fizzle, at, a, null, null, now, seconds, 4, 0.04f, 0.02f, 0f);
            }

            ImpactSystem.Instance?.EmitSparks(at, Vector3.up, 6, SparkColour, 1.5f, 3.5f, 0.15f, 0.3f);
            SparksAsked += 6;
            if (Physics.Raycast(at + Vector3.up * 0.2f, Vector3.down, out RaycastHit floor, 2f, _groundMask, QueryTriggerInteraction.Ignore))
            {
                PlaceBurn(now, floor.point, floor.normal, FizzleBurnSize);
            }
        }

        private void SelfShock(float now, Vector3 player, Vector3 contact)
        {
            SelfShocks++;
            SpawnArc(ArcKind.Self, contact + Vector3.up * 1.0f, player + Vector3.up * 1.2f, null, null, now, 0.4f, 10, -0.08f, 0.04f, 0.15f);
            SpawnArc(ArcKind.Self, contact + Vector3.up * 0.8f, player + Vector3.up * 0.6f, null, null, now, 0.4f, 10, -0.08f, 0.04f, 0.15f);
        }

        private void PlaceBurn(float now, Vector3 pos, Vector3 normal, float size)
        {
            int slot = -1, oldest = 0;
            for (int i = 0; i < MaxBurns; i++)
            {
                if (!_burns[i].Alive) { slot = i; break; }
                if (_burns[i].Born < _burns[oldest].Born) oldest = i;
            }

            if (slot < 0) slot = oldest;
            uint h = (uint)(BurnsPlaced * 2654435761u);
            Quaternion onSurface = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up);
            _burns[slot] = new Burn
            {
                Alive = true, Pos = pos + normal * 0.015f, Size = size, Born = now, Variant = (int)(h % 3u),
                Rot = onSurface * Quaternion.Euler(0f, SpecialVfxAssets.Hash01(h) * 360f, 0f)
            };
            BurnsPlaced++;
        }

        // ------------------------------------------------------------------ arc records

        private void SpawnArc(ArcKind kind, Vector3 from, Vector3 to, Transform fromT, Transform toT, float now, float life, int points,
            float amp, float core, float halo)
        {
            bool main = IsMain(kind);
            int slot = -1;
            if (main && CountArcs(true) >= MainArcCap)
            {
                slot = OldestMain();   // the cap retires the oldest: a new hop always shows
            }
            else
            {
                for (int n = 0; n < MaxArcs; n++)
                {
                    int i = (_nextArc + n) % MaxArcs;
                    if (!_arcs[i].Alive) { slot = i; break; }
                }

                if (slot < 0) slot = _nextArc;
            }

            _nextArc = (slot + 1) % MaxArcs;
            ref Arc a = ref _arcs[slot];
            a.Alive = true;
            a.Kind = kind;
            a.From = from;
            a.To = to;
            a.FromT = fromT;
            a.ToT = toT;
            a.Born = now;
            a.Life = life;
            a.NextSample = now;
            a.Points = Mathf.Clamp(points, 2, MaxPoints);
            a.Seed = (uint)(slot * 7919 + ChainArcs * 104729 + Discharges * 31 + ChargeArcs * 131 + Shocks * 17);
            a.Samples = 0;
            a.Amp = amp;
            a.Core = core;
            a.Halo = halo;
            Sample(slot, now);
        }

        private static bool IsMain(ArcKind k) => k == ArcKind.Chain || k == ArcKind.Discharge || k == ArcKind.Self || k == ArcKind.Fizzle;

        private int CountArcs(bool mainOnly)
        {
            int n = 0;
            for (int i = 0; i < MaxArcs; i++)
            {
                if (_arcs[i].Alive && (!mainOnly || IsMain(_arcs[i].Kind))) n++;
            }

            return n;
        }

        private int OldestMain()
        {
            int best = -1;
            for (int i = 0; i < MaxArcs; i++)
            {
                if (!_arcs[i].Alive || !IsMain(_arcs[i].Kind)) continue;
                if (best < 0 || _arcs[i].Born < _arcs[best].Born) best = i;
            }

            return best < 0 ? 0 : best;
        }

        // Value noise in [-1, 1] along the arc (two octaves are summed by the caller).
        private static float Noise(uint seed, float x)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            float a = SpecialVfxAssets.Hash01(seed + (uint)i * 2246822519u) * 2f - 1f;
            float b = SpecialVfxAssets.Hash01(seed + (uint)(i + 1) * 2246822519u) * 2f - 1f;
            f = f * f * (3f - 2f * f);
            return a + (b - a) * f;
        }

        /// <summary>Re-sample one arc: endpoints fixed, interior displaced on two perpendiculars, sin(πt) envelope.</summary>
        private void Sample(int ai, float now)
        {
            ref Arc a = ref _arcs[ai];
            if (a.FromT != null) a.From = a.FromT.position;
            if (a.ToT != null) a.To = a.ToT.position;
            Vector3 axis = a.To - a.From;
            float len = axis.magnitude;
            Vector3 dir = len > 1e-4f ? axis / len : Vector3.up;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 side2 = Vector3.Cross(dir, side);
            float amp = a.Amp < 0f ? Mathf.Max(0.03f, -a.Amp * len / 6f) : a.Amp;
            uint s = a.Seed + (uint)a.Samples * 7919u;
            int n = a.Points;
            int baseIndex = ai * MaxPoints;
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)(n - 1);
                float env = Mathf.Sin(t * Mathf.PI);
                float n1 = Noise(s, t * 4f) + 0.5f * Noise(s + 101u, t * 9f);
                float n2 = Noise(s + 37u, t * 4f) + 0.5f * Noise(s + 211u, t * 9f);
                _pts[baseIndex + k] = a.From + axis * t + (side * n1 + side2 * n2) * (amp * env);
            }

            a.Samples++;
            a.NextSample = now + ResampleSeconds;

            if (a.Kind == ArcKind.Chain && n > 5)
            {
                int count = BranchesPerHop;
                for (int b = 0; b < count; b++) SpawnBranch(ai, s + (uint)b * 53u, dir, side, side2, len, now);
            }
        }

        private void SpawnBranch(int ai, uint seed, Vector3 dir, Vector3 side, Vector3 side2, float len, float now)
        {
            if (LiveBranches >= BranchCap) return;
            int n = _arcs[ai].Points;
            int k = 2 + (int)(SpecialVfxAssets.Hash01(seed) * (n - 4));
            Vector3 start = _pts[ai * MaxPoints + k];
            float phi = SpecialVfxAssets.Hash01(seed * 3u + 1u) * Mathf.PI * 2f;
            float tilt = Mathf.Lerp(35f, 60f, SpecialVfxAssets.Hash01(seed * 5u + 2u)) * Mathf.Deg2Rad;
            Vector3 perp = side * Mathf.Cos(phi) + side2 * Mathf.Sin(phi);
            Vector3 bdir = dir * Mathf.Cos(tilt) + perp * Mathf.Sin(tilt);
            float blen = len * Mathf.Lerp(0.25f, 0.40f, SpecialVfxAssets.Hash01(seed * 7u + 3u));
            for (int i = 0; i < MaxBranches; i++)
            {
                int j = (_nextBranch + i) % MaxBranches;
                if (_branches[j].Alive) continue;
                _branches[j] = new Branch { Alive = true, A = start, B = start + bdir * blen, Born = now };
                _nextBranch = (j + 1) % MaxBranches;
                BranchesSpawned++;
                return;
            }
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            using var marker = s_marker.Auto();   // §5: per-tier ms (ProfilerRecorder, tier-ms.md)
            Frame(Time.time);
        }

        /// <summary>One frame: step + draw (public: the alloc test runs whole frames on the calling thread).</summary>
        public void Frame(float now)
        {
            Step(now);
            Draw(now);
        }

        /// <summary>Advance every record to <paramref name="now"/> (public: the alloc test steps it directly).</summary>
        public void Step(float now)
        {
            ZombieSkinFx.Tick(now);

            // Charge: electrode arcs every 30 ms, the light ramp (the weapon's own coil ramp is SpecialWeaponGlow's).
            if (_charging)
            {
                float t01 = (now - _chargeBorn) / _chargeSeconds;
                if (t01 > 1.5f)
                {
                    _charging = false;   // no Fired / Cancelled arrived (weapon dropped mid-charge): never glow forever
                }
                else if (now >= _nextChargeArc && MuzzlePose(Vector3.zero, Vector3.forward, out Vector3 m, out Vector3 fwd, out Vector3 right, out Vector3 up))
                {
                    _nextChargeArc = now + 0.03f;
                    for (int k = 0; k < 3; k++)
                    {
                        float a = SpecialVfxAssets.Hash01((uint)(ChargeArcs * 3 + k) * 2654435761u) * Mathf.PI * 2f;
                        Vector3 tip = m + fwd * 0.03f + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.15f;
                        SpawnArc(ArcKind.Charge, m + fwd * 0.02f, tip, null, null, now, 0.035f, 4, 0.02f, 0.012f, 0f);
                    }

                    ChargeArcs += 3;
                }
            }

            // The discharge's 0.5 s after-flicker: a random electrode pair crackles, 30 % per 50 ms.
            if (now < _afterFlickerUntil && now >= _nextAfterFlicker)
            {
                _nextAfterFlicker = now + 0.05f;
                uint h = (uint)(Discharges * 977 + Mathf.FloorToInt(now * 20f));
                if (SpecialVfxAssets.Hash01(h) < 0.3f && MuzzlePose(Vector3.zero, Vector3.forward, out Vector3 em, out _, out Vector3 eRight, out Vector3 eUp))
                {
                    int k = (int)(SpecialVfxAssets.Hash01(h * 3u) * 3f) % 3;
                    SpawnArc(ArcKind.Discharge, Electrode(em, eRight, eUp, k), Electrode(em, eRight, eUp, (k + 1) % 3), null, null, now, 0.04f, 4, 0.03f, 0.02f, 0f);
                }
            }

            for (int i = 0; i < MaxArcs; i++)
            {
                ref Arc a = ref _arcs[i];
                if (!a.Alive) continue;
                if (now - a.Born > a.Life || (a.FromT == null && !ReferenceEquals(a.FromT, null)) || (a.ToT == null && !ReferenceEquals(a.ToT, null)))
                {
                    a.Alive = false;   // expired, or the bone it followed was destroyed
                    continue;
                }

                if (now >= a.NextSample) Sample(i, now);
            }

            for (int i = 0; i < MaxBranches; i++)
            {
                if (_branches[i].Alive && now - _branches[i].Born > BranchSeconds) _branches[i].Alive = false;
            }

            for (int i = 0; i < MaxBurns; i++)
            {
                if (_burns[i].Alive && now - _burns[i].Born > BurnSeconds) _burns[i].Alive = false;
            }

            StepShocks(now);
        }

        private void StepShocks(float now)
        {
            bool joints = JointArcs, ring = FeetRing;
            for (int i = 0; i < MaxShocks; i++)
            {
                ref Shock s = ref _shocks[i];
                if (!s.Alive) continue;
                float t = now - s.Born;
                bool smoking = t < 0.6f + 1.5f;
                if (s.Root == null || (t > s.Seconds && !smoking))
                {
                    s.Alive = false;
                    continue;
                }

                Vector3 root = s.Root.position;
                if (t < s.Seconds && joints && now >= s.NextJoint)
                {
                    // "İskelet parıltısı" (WP-069 §8.1): head–chest–hips + chest–hands, 5 points, 0.02 m, 60 ms.
                    s.NextJoint = now + JointArcRefresh;
                    Joint(s.Head, root + Vector3.up * 1.65f, s.Chest, root + Vector3.up * 1.25f, now);
                    Joint(s.Chest, root + Vector3.up * 1.25f, s.Hips, root + Vector3.up * 0.95f, now);
                    Joint(s.Chest, root + Vector3.up * 1.25f, s.HandL, root + Vector3.up * 1.0f - s.Root.right * 0.35f, now);
                    Joint(s.Chest, root + Vector3.up * 1.25f, s.HandR, root + Vector3.up * 1.0f + s.Root.right * 0.35f, now);
                }

                if (t < s.Seconds && ring && now >= s.NextRing)
                {
                    s.NextRing = now + 0.1f;
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (k / 3f + SpecialVfxAssets.Hash01((uint)(i * 97 + Mathf.FloorToInt(now * 10f) * 13 + k))) * Mathf.PI * 2f;
                        Vector3 p0 = root + new Vector3(Mathf.Cos(a), 0.03f, Mathf.Sin(a)) * 0.4f;
                        Vector3 p1 = root + new Vector3(Mathf.Cos(a + 0.7f), 0.03f, Mathf.Sin(a + 0.7f)) * 0.4f;
                        SpawnArc(ArcKind.Ground, p0, p1, null, null, now, 0.1f, 4, 0.05f, 0.02f, 0f);
                    }
                }

                // Head smoke from 0.6 s: 4 puffs / s for 1.5 s (α 0.3) + 2 embers at its start.
                if (t >= 0.6f && smoking && now >= s.NextSmoke)
                {
                    s.NextSmoke = now + 0.25f;
                    Vector3 head = s.Head != null ? s.Head.position : root + Vector3.up * 1.65f;
                    MuzzleSystem.Instance?.PuffAt(head + Vector3.up * 0.12f, 0.28f, 1.2f, SmokeColour, Vector3.up * 0.35f);
                    SmokePuffs++;
                    if (!s.Embers)
                    {
                        s.Embers = true;
                        ImpactSystem.Instance?.EmitSparks(head, Vector3.up, 2, EmberColour, 0.4f, 0.9f, 0.6f, 1.0f);
                        SparksAsked += 2;
                    }
                }
            }
        }

        private void Joint(Transform a, Vector3 aFallback, Transform b, Vector3 bFallback, float now)
        {
            SpawnArc(ArcKind.Joint, a != null ? a.position : aFallback, b != null ? b.position : bFallback, a, b, now, JointArcRefresh, 5, 0.03f, 0.02f, 0f);
        }

        // ------------------------------------------------------------------ draw

        private void Draw(float now)
        {
            _nCore = 0;
            _nHalo = 0;
            if (_ribbon == null) return;
            bool fp = CameraModeService.Current == CameraMode.FirstPerson;
            float fpFlag = fp ? 1f : 0f;

            for (int i = 0; i < MaxArcs; i++)
            {
                ref Arc a = ref _arcs[i];
                if (!a.Alive) continue;
                float age01 = (now - a.Born) / Mathf.Max(0.001f, a.Life);
                float alpha = age01 < 0.7f ? 1f : Mathf.Clamp01((1f - age01) / 0.3f);
                Vector4 core = a.Kind == ArcKind.Chain || a.Kind == ArcKind.Self ? CoreHdr : a.Kind == ArcKind.Ground ? GroundHdr : BranchHdr;
                int b = i * MaxPoints;
                for (int k = 0; k < a.Points - 1; k++)
                {
                    Seg(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, _pts[b + k], _pts[b + k + 1], a.Core, core, alpha, 2f, fpFlag);
                    if (a.Halo > 0f)
                    {
                        Seg(ref _nHalo, HaloCapacity, _hHead, _hTail, _hHc, _hTc, _hSpan, _hExtra, _pts[b + k], _pts[b + k + 1], a.Halo, HaloHdr, alpha * HaloHdr.w, 3f, fpFlag);
                    }
                }
            }

            for (int i = 0; i < MaxBranches; i++)
            {
                if (!_branches[i].Alive) continue;
                Seg(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, _branches[i].A, _branches[i].B, 0.02f, BranchHdr, 1f, 1.5f, fpFlag);
            }

            DrawOrbs(now, fp, fpFlag);
            TrackLight(now);

            if (_nCore > 0) DrawRibbons(_coreBlock, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, _nCore);
            if (_nHalo > 0) DrawRibbons(_haloBlock, _hHead, _hTail, _hHc, _hTc, _hSpan, _hExtra, _nHalo);
            DrawBurns(now);
        }

        private static void Seg(ref int n, int cap, Vector4[] head, Vector4[] tail, Vector4[] hc, Vector4[] tc, Vector4[] span, Vector4[] extra,
            Vector3 a, Vector3 b, float width, Vector4 colour, float alpha, float minPx, float fpFlag)
        {
            if (n >= cap || alpha <= 0.001f) return;
            float len = (b - a).magnitude;
            head[n] = new Vector4(b.x, b.y, b.z, width);
            tail[n] = new Vector4(a.x, a.y, a.z, 1f);                 // bead share 1: a uniform strip (no tracer tail falloff)
            hc[n] = new Vector4(colour.x, colour.y, colour.z, alpha);
            tc[n] = new Vector4(colour.x, colour.y, colour.z, 0f);
            span[n] = new Vector4(1f, 1f + len, 0f, 0f);              // past the 0.4 m muzzle fade-in, no distance fade
            extra[n] = new Vector4(minPx, 0f, fpFlag, 0f);
            n++;
        }

        private static void Dot(ref int n, int cap, Vector4[] head, Vector4[] tail, Vector4[] hc, Vector4[] tc, Vector4[] span, Vector4[] extra,
            Vector3 p, float size, Vector4 colour, float alpha, float dotPx, float fpFlag)
        {
            if (n >= cap) return;
            head[n] = new Vector4(p.x, p.y, p.z, size * 0.5f);
            tail[n] = new Vector4(p.x, p.y - 0.01f, p.z, 1f);
            hc[n] = new Vector4(colour.x, colour.y, colour.z, alpha);
            tc[n] = new Vector4(colour.x, colour.y, colour.z, 0f);
            span[n] = new Vector4(1f, 1.01f, 0f, 0f);
            extra[n] = new Vector4(2f, dotPx, fpFlag, 1f);            // w = 1: drawn as a soft round head dot
            n++;
        }

        private void DrawRibbons(MaterialPropertyBlock block, Vector4[] head, Vector4[] tail, Vector4[] hc, Vector4[] tc, Vector4[] span, Vector4[] extra, int n)
        {
            block.SetVectorArray(HeadId, head);
            block.SetVectorArray(TailId, tail);
            block.SetVectorArray(HeadColorId, hc);
            block.SetVectorArray(TailColorId, tc);
            block.SetVectorArray(SpanId, span);
            block.SetVectorArray(ExtraId, extra);
            Graphics.DrawMeshInstanced(SpecialVfxAssets.RibbonQuad, 0, _ribbon, _identity, n, block, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
        }

        private void DrawOrbs(float now, bool fp, float fpFlag)
        {
            int orbs = 0, seen = 0;
            bool haveCam = FeedbackCameraRef.TryGetPosition(out Vector3 cam);
            float meshDistance = MeshDistance;
            int live = ProjectileSystem.LiveCount;
            for (int i = 0; i < live && orbs < OrbCapacity; i++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(i);
                if (p.Kind != ProjectileKind.Orb) continue;
                Vector3 pos = p.Position;
                float camDist = haveCam ? (pos - cam).magnitude : 100f;
                if (fp && camDist < 0.6f) continue;                          // born 0.6 m out of the eye
                float hdr = fp && p.Age < 0.3f ? 0.4f : 1f;                    // FP: HDR × 0.4 for the first 0.3 s
                uint gen = (uint)p.Generation;
                seen++;

                if (camDist > meshDistance)
                {
                    Dot(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, pos, 0.25f, OrbCoreHdr * hdr, 1f, 6f, fpFlag);
                }
                else
                {
                    float ripple = 1f + 0.05f * Mathf.Sin(now * Mathf.PI * 2f * 4f + gen);
                    _orbM[orbs] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.25f);
                    _orbCol[orbs] = OrbCoreHdr * hdr;
                    _orbCol[orbs].w = 1f;
                    _orbRim[orbs] = OrbRim;
                    _orbWob[orbs] = new Vector4(0.0045f, 6f, gen * 0.37f, 1f);   // the gel wobble × 0.3
                    _shellM[orbs] = Matrix4x4.TRS(pos, Quaternion.Euler(0f, now * 90f, now * 40f), Vector3.one * (0.45f * ripple));
                    _shellCol[orbs] = OrbShellHdr * hdr;
                    _shellCol[orbs].w = 1f;
                    _shellRim[orbs] = new Vector4(OrbShellHdr.x, OrbShellHdr.y, OrbShellHdr.z, 1.5f);
                    _shellWob[orbs] = new Vector4(0.02f, 4f, gen * 0.61f, 1f);
                    orbs++;
                }

                // Tendrils: 4–6 × 0.6 m, re-rolled at 10 Hz (stateless — hashed from the projectile id and the tick).
                uint tick = (uint)Mathf.FloorToInt(now * 10f);
                int count = 4 + (int)(SpecialVfxAssets.Hash01(gen * 31u + tick) * 3f);
                for (int k = 0; k < count; k++)
                {
                    uint h = gen * 2654435761u + (uint)k * 97u + tick * 131u;
                    Vector3 d = SpecialVfxAssets.HashDir(h);
                    float len = 0.6f * Mathf.Lerp(0.6f, 1f, SpecialVfxAssets.Hash01(h + 7u));
                    Vector3 kink = SpecialVfxAssets.HashDir(h + 13u) * 0.06f;
                    Vector3 p0 = pos + d * 0.12f, p1 = pos + d * (len * 0.5f) + kink, p2 = pos + d * len;
                    Seg(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, p0, p1, 0.012f, BranchHdr * hdr, 1f, 1.5f, fpFlag);
                    Seg(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, p1, p2, 0.008f, BranchHdr * hdr, 0.7f, 1.5f, fpFlag);
                }

                // Ground arcs: the floor within 2.5 m → 3 short arcs on a 0.5 m ring + a bright spot (wet slate conducts).
                if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, GroundArcReach, _groundMask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 g = hit.point + hit.normal * 0.03f;
                    float strength = 1f - hit.distance / GroundArcReach;
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (k / 3f + SpecialVfxAssets.Hash01(gen * 7u + tick * 3u + (uint)k)) * Mathf.PI * 2f;
                        Vector3 r0 = g + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f;
                        Vector3 mid = g + new Vector3(Mathf.Cos(a + 0.35f), 0f, Mathf.Sin(a + 0.35f)) * 0.42f + Vector3.up * 0.05f;
                        Vector3 r1 = g + new Vector3(Mathf.Cos(a + 0.7f), 0f, Mathf.Sin(a + 0.7f)) * 0.5f;
                        Seg(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, r0, mid, 0.015f, GroundHdr, strength, 1.5f, fpFlag);
                        Seg(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, mid, r1, 0.015f, GroundHdr, strength, 1.5f, fpFlag);
                    }

                    Dot(ref _nCore, CoreCapacity, _cHead, _cTail, _cHc, _cTc, _cSpan, _cExtra, g, 0.3f, GroundHdr * 0.35f, strength, 4f, fpFlag);
                }

                if (!_charging && now >= _hopLightUntil)
                {
                    _orbLightPos = pos;
                    _orbLight = true;
                }
            }

            OrbsDrawn = seen;
            OrbMeshes = orbs;
            if (orbs == 0 || _orbCore == null) return;
            _orbBlock.SetVectorArray(GelColorId, _orbCol);
            _orbBlock.SetVectorArray(GelRimId, _orbRim);
            _orbBlock.SetVectorArray(GelWobbleId, _orbWob);
            Graphics.DrawMeshInstanced(SpecialVfxAssets.Sphere, 0, _orbCore, _orbM, orbs, _orbBlock, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            _shellBlock.SetVectorArray(GelColorId, _shellCol);
            _shellBlock.SetVectorArray(GelRimId, _shellRim);
            _shellBlock.SetVectorArray(GelWobbleId, _shellWob);
            Graphics.DrawMeshInstanced(SpecialVfxAssets.Sphere, 0, _orbShell, _shellM, orbs, _shellBlock, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
        }

        private Vector3 _orbLightPos;
        private bool _orbLight;

        // One Raiju light (§2.1: "tek Light, 8 hedefte 8 atlama"): charge ramp > hop jump > the orb in flight.
        private void TrackLight(float now)
        {
            MuzzleSystem light = MuzzleSystem.Instance;
            if (light == null) { _orbLight = false; return; }
            if (_charging && MuzzlePose(Vector3.zero, Vector3.forward, out Vector3 m, out _, out _, out _))
            {
                float k = Mathf.Clamp01((now - _chargeBorn) / _chargeSeconds);
                light.TrackLight(LightKey, m, LightColour, 8f * k, 3f);
            }
            else if (now < _hopLightUntil)
            {
                light.TrackLight(LightKey, _hopLightPos, LightColour, 8f, 6f);
            }
            else if (_orbLight)
            {
                light.TrackLight(LightKey, _orbLightPos, LightColour, 8f, 6f);
            }

            _orbLight = false;
        }

        private void DrawBurns(float now)
        {
            if (_burn == null) return;
            int n = 0;
            for (int i = 0; i < MaxBurns; i++)
            {
                if (!_burns[i].Alive) continue;
                float age = now - _burns[i].Born;
                float fade = Mathf.Clamp01((BurnSeconds - age) / 2f);   // the last 2 s fade
                _burnM[n] = Matrix4x4.TRS(_burns[i].Pos, _burns[i].Rot, new Vector3(_burns[i].Size, 1f, _burns[i].Size));
                _burnP[n] = new Vector4(age / BurnSeconds, _burns[i].Variant, 0f, 0.6f * fade);   // #2A2420 α 0.6
                n++;
            }

            if (n == 0) return;
            _burnBlock.SetVectorArray(PuddleParamsId, _burnP);
            Graphics.DrawMeshInstanced(SpecialVfxAssets.GroundQuad, 0, _burn, _burnM, n, _burnBlock, ShadowCastingMode.Off, true, 0, null, LightProbeUsage.Off);
        }

        // ------------------------------------------------------------------ muzzle

        /// <summary>
        /// The Special muzzle in world space (first person: the viewmodel muzzle mapped by CameraView.ViewmodelToWorld, as
        /// MuzzleSystem does) with a frame; false when no Special is in hand and no fallback was given.
        /// </summary>
        private static bool MuzzlePose(Vector3 fallback, Vector3 fallbackForward, out Vector3 point, out Vector3 fwd, out Vector3 right, out Vector3 up)
        {
            WeaponController c = WeaponRegistry.ActiveController;
            Transform muzzle = c != null && c.SpecialWeaponActive ? c.SpecialRigMuzzle : null;
            if (muzzle != null)
            {
                bool firstPerson = CameraModeService.Current == CameraMode.FirstPerson;
                point = firstPerson ? CameraView.ViewmodelToWorld(muzzle.position) : muzzle.position;
                fwd = muzzle.forward;
                right = muzzle.right;
                up = muzzle.up;
                return true;
            }

            point = fallback;
            fwd = fallbackForward.sqrMagnitude > 1e-6f ? fallbackForward.normalized : Vector3.forward;
            right = Vector3.Cross(Vector3.up, fwd).sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, fwd).normalized : Vector3.right;
            up = Vector3.Cross(fwd, right);
            return fallback != Vector3.zero;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
