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
    public sealed partial class TracerRibbonRenderer : MonoBehaviour   // WP-104 B4: + TracerRibbonRenderer.Tree.cs (the run tree's look)
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.TracerRibbons");

        public const int Capacity = 256;
        public const int TdCapacity = 48;
        /// <summary>
        /// WP-101 (R4, user 11.10: "mermiler bu sefer çok küçük ve fazla turuncu olmuş. mermi projectileleri ultra gerçekçi olacak").
        /// The R3 top-down tracer was a 3.5 cm core (2 px at the TD distance) inside a 0.26 m orange glow at α 1 — the orange WAS the
        /// tracer. Now every round is drawn as what a camera sees of a real bullet, and only the class's rhythm carries a tracer:
        /// <list type="bullet">
        /// <item>BODY (every round that is not a tracer): the bullet's own motion-blur streak — a mid-brass line
        /// (<see cref="TdBodyWidth"/>), v × <see cref="TdBodyExposureSeconds"/> long (<see cref="TdBodyMin"/>–<see cref="TdBodyMax"/>), drawn
        /// premultiplied at α <see cref="TdBodyAlpha"/>: a shade darker than the light stone and lighter than the wet slate (AD r1 M4 - an
        /// added light line vanished on the light stone).</item>
        /// <item>TRACER (every <see cref="TdTracerEveryN"/>th round of its class, Third Eye's marked round, sg_2's centre pellet): a
        /// white-hot core streak at the class's real-scale width (<see cref="TdCoreWidth"/>, ≥ 3 px; LMG / sniper ≥ 4 px), as long as
        /// the round travels in one exposure (v × <see cref="TdExposureSeconds"/>, <see cref="TdTailMin"/>–<see cref="TdTailMax"/>,
        /// sniper ≤ <see cref="TdSniperTailMax"/>); a full-intensity <see cref="TdTracerBead"/> head (the bullet), a soft exponential tail (<see cref="TdCoreFalloff"/>), white (<see cref="TdCoreHead"/>)
        /// at the bullet to warm yellow, and only the faint last third warms toward amber (<see cref="TdCoreTail"/>, where its α is
        /// already ≤ 0.3). A heat halo (<see cref="TdHaloScale"/> × the core, warm white <see cref="TdHaloColor"/> → <see cref="TdHaloTail"/>,
        /// its outer half warming to the amber rim <see cref="TdHaloEdge"/> (AD r1 M3: the light stone needs an edge), premultiplied at
        /// α <see cref="TdHaloAlpha"/> - the halo carries the streak's width and weight (AD r1 M1/M2: an added white core alone read 2 px
        /// on the light stone); none on Performance).</item>
        /// <item>Shotgun pellets: <see cref="PelletBarLength"/> warm-white glint bars, added with the cores (<see cref="TdPelletWidth"/>,
        /// α <see cref="TdPelletAlpha"/> - a dark bar vanished on the light stone), dead by
        /// <see cref="PelletFadeDistance"/>; the sniper keeps its 0.4 s grey vapour trail.</item>
        /// </list>
        /// Every round rides its projectile to the impact (or the range end), then its tail runs into the stopped head over
        /// <see cref="TdTailOutSeconds"/>. ReduceFlashes (GameFeelSettings.FlashScale) dims the core's HDR (× 0.6 at the floor) and the
        /// halo's HDR (× FlashScale, ≥ 0.4 — WP-104 B4-1 AD-B4 M3: never its α, a steady trail is no flash); bodies and pellets keep
        /// their look (form, not flash). Same pooled 48 records, three instanced draws (tracer cores + pellet bars · vapour · halos +
        /// bodies), 0 B.
        /// </summary>
        public const float TdTailOutSeconds = .06f, TdMaxFlightSeconds = 1.5f, TdExposureSeconds = 1f / 60f, TdTailMin = 1.5f, TdTailMax = 5.5f,
            TdSniperTailMax = 10f, TdSniperExposureSeconds = 1f / 40f, TdCoreFalloff = 1.6f, TdCoreRampShare = .25f, TdHaloScale = 4f, TdHaloAlpha = .6f, TdHaloFalloff = 1.2f, TdHaloEdgeShare = 1f, TdHaloHdr = 2.2f,
            TdBodyExposureSeconds = 1f / 160f, TdBodyMin = .6f, TdBodyMax = 2.5f, TdBodyWidth = .09f, TdBodyAlpha = .85f, TdPelletAlpha = .75f, TdPelletWidth = .05f,
            TdCoreMinPx = 4f, TdHeavyCoreMinPx = 5f, TdBodyMinPx = 2f, TdTracerBead = .25f;
        /// <summary>WP-101: white-hot at the bullet; the shader's head→tail blend (smoothstep) passes warm yellow ≈ (1, .82, .63)
        /// mid-streak and reaches the faint amber only at the fading end; the halo a warm white-yellow (no orange). The body a warm
        /// brass-grey; the pellets a warm white.</summary>
        public static readonly Color TdCoreHead = new Color(1f, .97f, .9f) * 2.4f,
            // TdCoreTail r9: S .32 - an added amber over the wet slate read saturated (AD r1 M3)
            TdCoreTail = new Color(1f, .86f, .68f) * 1.1f, TdHaloColor = new Color(1f, .92f, .78f),
            TdHaloTail = new Color(1f, .84f, .66f), TdHaloEdge = new Color(1f, .86f, .72f),
            // AD r1 M4: drawn premultiplied (never added) - a mid brass a shade darker than the light stone, lighter than the wet slate
            TdBodyColor = new Color(.21f, .19f, .15f), TdPelletColor = new Color(1f, .96f, .88f) * 1.6f;   // sonra-r11: a dark bar vanished on the light stone - the R3 warm-white glint, added   // r6: S ≤ .3 (M3 counted the brass as amber)

        /// <summary>WP-101: the class's real-scale tracer core width (m) — a 5.56 tracer's burning base reads ≈ 6–7 cm on a camera at
        /// TD distance once motion-blurred; the 7.62 belt and the .50-class sniper wider. At the TD camera ≈ 71 px/m (wp101 once-b46a3ca4:
        /// 6 m = 427 px; 9 m capture ≈ 95 px/m): ar ≈ 8–10 px, smg 7–9, lmg 9–12, sr 11–15 (R3 core 2.5 px + a 15 px orange glow).</summary>
        public static float TdCoreWidth(VfxWeaponClass c)
        {
            switch (c)
            {
                // sonra-r1 (≈ 95 px/m): ×1.0 read as a 2 px white thread beside the R3 streak's 15 px - "çok küçük" again; × 1.6
                case VfxWeaponClass.Smg: return .10f;
                case VfxWeaponClass.Lmg: return .13f;
                case VfxWeaponClass.Sniper: return .16f;
                case VfxWeaponClass.Shotgun: return .08f;   // sg_2's centre tracer pellet
                default: return .11f;
            }
        }

        /// <summary>WP-101: the top-down tracer rhythm (every Nth round of the class carries a tracer; the rest draw only their body
        /// streak). Denser than a real belt (1 : 4–5) so the stream still reads where it lands.</summary>
        public static int TdTracerEveryN(VfxWeaponClass c)
        {
            switch (c)
            {
                // AD r1 M1/M2 (the streak's weight ≥ R3, where every round glowed): rifles, the LMG belt and the sniper all tracer;
                // the SMG keeps one body streak between two tracers (and the shotgun its plain pellets) - the body / tracer split
                case VfxWeaponClass.Smg: return 2;
                default: return 1;
            }
        }

        /// <summary>Whether a top-down round with this id is a tracer (pellets: sg_2's centre pellet only).</summary>
        public static bool IsTdTracer(VfxWeaponProfile profile, int roundId, bool pellet, int pelletIndex)
        {
            if (pellet) return profile.CentreTracer && pelletIndex == 0;
            int n = TdTracerEveryN(profile.Class);
            if (n <= 1) return true;
            int m = roundId % n;
            return (m < 0 ? m + n : m) == 0;
        }

        private struct TdRound
        {
            public bool Alive, Sniper, Stopped, Pellet, Tracer;
            public int Id;
            public float Age, Speed, Target, StopAge, Core, Halo, CoreMinPx;
            public int Look, Merged; public float Size, CapPx, Base; public bool Extra;   // WP-104 B4: look, S-02 size, ceiling, WP-101 width, volley
            public float HaloPx, TrailScale, HaloHdr;   // WP-104 B4-1 (AD-B4 §1): the tier's halo step (px at 1080p), trail factor, V's halo HDR
            public Vector3 Origin, Head, Direction;
        }
        // a shotgun pellet's bar dies out by PelletFadeDistance as in third person (Wp073FireVfxTests: gone by 6 m on a 40 m shot)
        private static bool TdCoreLive(in TdRound r) => !(r.Pellet && !r.Tracer && (r.Head - r.Origin).sqrMagnitude >= PelletFadeDistance * PelletFadeDistance)
            && (r.Stopped ? r.Age - r.StopAge < TdTailOutSeconds : r.Age < TdMaxFlightSeconds);
        private static void TdLook(ref TdRound r, VfxWeaponProfile profile, bool tracer)
        {
            r.Sniper = profile.Class == VfxWeaponClass.Sniper;
            r.Tracer = tracer;
            r.Core = tracer ? TdCoreWidth(profile.Class) : TdBodyWidth;
            r.Halo = tracer ? r.Core * TdHaloScale : 0f;
            r.CoreMinPx = !tracer ? TdBodyMinPx : r.Sniper || profile.Class == VfxWeaponClass.Lmg ? TdHeavyCoreMinPx : TdCoreMinPx;
            StyleTd(ref r, tracer);   // WP-104 B4: the run's tier ladder, element and size (neutral, size 1: as above)
        }
        /// <summary>The streak behind the head (m): a tracer's one-exposure blur, a body's short one, a pellet's bar.</summary>
        private static float TdTrail(in TdRound r)
        {
            if (r.Pellet && !r.Tracer) return PelletBarLength;
            // WP-104 B4-1 (AD-B4 §1): a tracer's one-exposure trail × its Shot tier (1.0 … 1.45, ≤ 10 m); WP-101's own at 1
            return r.Tracer ? TracerStyle.ScaledTrail(r.Sniper ? Mathf.Clamp(r.Speed * TdSniperExposureSeconds, TdTailMin, TdSniperTailMax) : Mathf.Clamp(r.Speed * TdExposureSeconds, TdTailMin, TdTailMax), r.TrailScale)
                : Mathf.Clamp(r.Speed * TdBodyExposureSeconds, TdBodyMin, TdBodyMax);
        }
        private readonly TdRound[] _tdRounds = new TdRound[TdCapacity];
        private int _tdSyntheticId;
        public int LiveTdTracers { get; private set; }
        public int LiveTdSmoke { get; private set; }
        /// <summary>WP-101: live top-down records that carry a tracer (the rest draw their body streak only).</summary>
        public int LiveTdTracerRounds { get; private set; }

        /// <summary>TD birth admission: each pellet is a shot, bounded to 48 shared tracer/smoke records.</summary>
        public bool RecordTdProjectile(in ProjectileSpawnInfo info)
        {
            if (CameraModeService.Current != CameraMode.TopDown || info.Kind == ProjectileKind.Rocket
                || info.Kind == ProjectileKind.Orb || info.Kind == ProjectileKind.Gel) return false;
            if (info.VolleyIndex > 0 && !AdmitTdVolley(in info)) return false;   // WP-104 B4: S-02's extra volleys hold ≤ 32 of the 48, past it they merge
            for (int i = 0; i < TdCapacity; i++)
            {
                if (_tdRounds[i].Alive) continue;
                VfxWeaponProfile profile = VfxWeaponProfiles.For(info.WeaponId);
                bool pellet = info.Kind == ProjectileKind.Pellet;
                _tdRounds[i] = new TdRound { Alive = true, Id = info.ProjectileId,
                    Origin = info.Position, Head = info.Position, Direction = info.Direction.normalized,
                    Speed = info.Speed, Target = float.PositiveInfinity, Pellet = pellet, Size = info.SizeScale, Extra = info.VolleyIndex > 0 };   // WP-104 B4: S-02 size / volley
                TdLook(ref _tdRounds[i], profile, TdTracerOf(profile, in info, pellet, i));   // WP-104 B4: the rhythm over main rounds (no extras = the id itself)
                TdMarkedAtBirth(i, in info);   // WP-104 B4 (AD K3): a marked round is violet from its first frame, even if it hits at once
                LiveTdTracers++;
                if (_tdRounds[i].Tracer) LiveTdTracerRounds++;
                if (_tdRounds[i].Sniper) LiveTdSmoke++;
                return true;
            }
            TdRefused(in info);   // WP-104 B4: a refused main round is no merge target
            return false;
        }
        public void StopTdProjectile(int id, Vector3 position, bool cleared = false)
        {
            for (int i = 0; i < TdCapacity; i++)
            {
                ref TdRound r = ref _tdRounds[i];
                if (!r.Alive || r.Id != id) continue;
                if (!r.Stopped) { r.Head = position; r.Stopped = true; r.StopAge = r.Age; }
                if (cleared) r.Alive = false;
                return;
            }
        }
        /// <summary>Presentation clock; projectile positions are read, never simulated or changed.</summary>
        public void TickTd(float delta)
        {
            delta = Mathf.Max(0f, delta);
            for (int pIndex = 0; pIndex < ProjectileSystem.LiveCount; pIndex++)
            {
                ref readonly ProjectileState p = ref ProjectileSystem.Get(pIndex);
                for (int i = 0; i < TdCapacity; i++)
                {
                    ref TdRound r = ref _tdRounds[i];
                    if (!r.Alive || r.Stopped || r.Id != p.Generation) continue;
                    Vector3 lift = MuzzleCatchUp(p, delta);
                    r.Head = p.Position + lift; r.Origin = p.Origin + lift;
                    r.Direction = p.Direction;
                    // DEC-112 Third Eye: the burst's marked round is always a full tracer
                    if (!r.Tracer && !r.Pellet && IsThirdEyeRound(p.TreeFlags)) TdLook(ref r, VfxWeaponProfiles.For(p.WeaponId), true);
                    TdMarked(ref r, in p);   // WP-104 B4 (AD K3): the chaos core's marked round (RunTreeHooks.TreeRoundMarkedChaos)
                    break;
                }
            }
            LiveTdTracers = LiveTdSmoke = LiveTdTracerRounds = 0;
            for (int i = 0; i < TdCapacity; i++)
            {
                ref TdRound r = ref _tdRounds[i]; if (!r.Alive) continue;
                r.Age += TdAgeDelta(in r, delta);   // WP-104 B4: a real round keeps the game clock under the tree preview (= delta outside it)
                if (r.Id < 0 && !r.Stopped)
                {
                    float distance = Mathf.Min(r.Target, r.Age * r.Speed);
                    r.Head = r.Origin + r.Direction * distance;
                    if (distance >= r.Target) { r.Stopped = true; r.StopAge = r.Speed > 0f ? Mathf.Min(r.Age, r.Target / r.Speed) : r.Age; }   // the true arrival, not the frame that saw it
                }
                bool core = TdCoreLive(r), smoke = r.Sniper && r.Age < .4f;
                if (!core && !smoke) { r.Alive = false; continue; }
                if (core) { LiveTdTracers++; if (r.Tracer) LiveTdTracerRounds++; }
                if (smoke) LiveTdSmoke++;
            }
        }
        private void ClearTd()
        {
            for (int i = 0; i < TdCapacity; i++) _tdRounds[i] = default;
            ClearTdTree();   // WP-104 B4: the volley rhythm / merge state goes with the records
            LiveTdTracers = LiveTdSmoke = LiveTdTracerRounds = 0;
        }
        // layer 0: tracer cores + body streaks + pellet bars (additive, the streak material) · 1: sniper vapour · 2: tracer heat halo
        private int DrawTd(int layer)
        {
            int n = 0;
            float flash = Mathf.Clamp01(GameFeelSettings.FlashScale);
            for (int i = 0; i < TdCapacity; i++)
            {
                ref TdRound r = ref _tdRounds[i];
                // AD r1 (WP-101): 0 = the tracer cores (added) · 1 = the sniper vapour · 2 = premultiplied: the tracer halos (not on
                // Performance) and every body streak / pellet bar (additive white vanished on the light stone)
                bool plainPellet = r.Pellet && !r.Tracer;
                if (!r.Alive || (layer != 1 && !TdCoreLive(r)) || (layer == 1 && (!r.Sniper || r.Age >= .4f))
                    || (layer == 0 && !r.Tracer && !plainPellet) || (layer == 2 && (plainPellet || (r.Tracer && _performance)))) continue;
                Vector3 tail;
                if (layer == 1) tail = r.Origin;
                else
                {
                    float trail = Mathf.Min((r.Head-r.Origin).magnitude, TdTrail(r));
                    if (r.Stopped) trail *= 1f - Mathf.Clamp01((r.Age - r.StopAge) / TdTailOutSeconds);   // the tail runs into the impact
                    tail = r.Head - r.Direction * trail;
                }
                float length = (r.Head-tail).magnitude; if (length < .01f) continue;
                Color head, back;
                float width, alpha, minPx = 0f, cover = 0f;
                if (layer == 1) { head = back = new Color(.48f, .50f, .52f); width = .12f; alpha = .16f * (1f - r.Age / .4f); }
                // × TdHaloHdr: the sun-lit stone is HDR > 1 before the tonemap - an LDR premultiplied veil only darkened it (sonra-r4)
                else if (layer == 2 && r.Tracer)
                {
                    // WP-104 B4: element / marked halo · B4-1 (AD-B4 M3 / S1 / M5): FlashScale dims its HDR (V's boost first), never its
                    // α; an element / marked halo covers the ground it veils (_TailColor.a)
                    float hdr = TdHaloHdr * TracerStyle.HaloHdr(r.HaloHdr, flash);
                    head = TracerStyle.Halo(r.Look, TdHaloColor) * hdr; back = TracerStyle.Halo(r.Look, TdHaloTail) * hdr; width = r.Halo;
                    alpha = TdHaloAlpha * TracerStyle.HaloFlash(r.Look, flash); cover = TracerStyle.HaloCover(r.Look);
                }
                else if (layer == 2)
                {
                    head = back = TracerStyle.Body(r.Look, TdBodyColor); width = r.Core; alpha = TdBodyAlpha; minPx = r.CoreMinPx;   // WP-104 B4: K1
                }
                else if (plainPellet)
                {
                    head = back = TracerStyle.Pellet(r.Look, TdPelletColor); width = TdPelletWidth; alpha = TdPelletAlpha; minPx = TdBodyMinPx;   // WP-104 B4: K1
                }
                else
                {
                    float hdr = Mathf.Lerp(.35f, 1f, flash);   // AD r1 M7: Reduce Flashes (0.4) → × 0.61 HDR
                    head = TracerStyle.CoreHead(r.Look, TdCoreHead) * hdr; back = TracerStyle.CoreTail(r.Look, TdCoreTail) * hdr; width = r.Core; alpha = 1f; minPx = r.CoreMinPx;   // WP-104 B4: K1 / K3
                }
                // span: the vapour (layer 1, tail = the muzzle) keeps the shader's 0.4 m fade-in off the muzzle; a streak's tail is
                // mid-air, so its span starts 1 m out (review: from 0 the fade ate 0.4 m of a 0.5–1.6 m body / pellet bar)
                float tailD = layer == 1 ? 0f : 1f;
                Write(n, 0, r.Head, tail, width, layer == 0 && r.Tracer ? TdTracerBead : 0f, tailD + length, tailD, 0f, 0f, head, back, false, false);
                // Metre widths with the TD pixel floor (x; 0 = the material's 2 px); no head dot.
                _extra[n] = new Vector4(minPx, TdStep(layer, in r), TdCap(layer, in r), TdBase(layer, in r));   // WP-104 B4: z < 0 = max px at 1080p (AD §2 / A5), w < 0 = its floor (−m: the WP-101 width); B4-1: y < 0 = a halo's tier step (+px at 1080p); 0 = WP-101
                if (layer == 2 && r.Tracer && r.Look == TracerStyle.NeutralLook) _span[n].z = -TdHaloEdgeShare;   // the halo's amber rim (DD_VfxRibbon _EdgeTint) - WP-104 B4: the neutral look's (an element halo is its own glow, K1)
                _headColor[n].w = alpha; _tailColor[n].w = layer == 1 ? alpha * .25f : cover;   // WP-104 B4-1: a halo's coverage gain (0 = WP-101)
                n++;
            }
            if (n == 0) return 0;
            if (layer == 2) SyncTdEdgeTint(flash);   // WP-104 B4-1 (AD-B4 M3): the neutral halo's amber rim dims with its HDR
            _block.SetVectorArray(HeadId, _head); _block.SetVectorArray(TailId, _tail);
            _block.SetVectorArray(HeadColorId, _headColor); _block.SetVectorArray(TailColorId, _tailColor);
            _block.SetVectorArray(SpanId, _span); _block.SetVectorArray(ExtraId, _extra);
            Material material = layer == 2 && _tdGlowMaterial != null ? _tdGlowMaterial : layer == 0 && _tdCoreMaterial != null ? _tdCoreMaterial : _material;
            Graphics.DrawMeshInstanced(_quad, 0, material, _matrices, n, _block, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            return n;
        }
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
        private Material _material, _tdGlowMaterial, _tdCoreMaterial;
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
                // WP-101: the TD heat halo is premultiplied (One / OneMinusSrcAlpha) - a warm white-yellow veil, never an added orange
                _tdGlowMaterial = new Material(_material) { name = "DD_VfxRibbon TD halo (runtime)", hideFlags = HideFlags.DontSave };
                _tdGlowMaterial.SetFloat("_SrcBlend", (float)BlendMode.One); _tdGlowMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetTail(_tdGlowMaterial, TdHaloFalloff, .2f);
                _tdGlowMaterial.SetColor("_EdgeTint", TdHaloEdge * TdHaloHdr);
                // WP-101: the TD streaks (tracer cores, body streaks, pellet bars) stay additive with a motion-blur tail - a soft
                // exponential instead of the ribbons' k 3.2 / 40 % ramp (that read as a short dot with a wisp)
                _tdCoreMaterial = new Material(_material) { name = "DD_VfxRibbon TD streak (runtime)", hideFlags = HideFlags.DontSave };
                SetTail(_tdCoreMaterial, TdCoreFalloff, TdCoreRampShare);
                BuildTdInk();   // WP-104 B4 (AD K3): the marked round's sumi core, Transparent+11
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
            ProjectileEvents.Expired += OnExpired;
        }

        // the tail: exponential k, its last share a linear ramp starting where the curve is (no step)
        private static void SetTail(Material m, float falloff, float rampShare)
        {
            m.SetFloat("_TailFalloff", falloff); m.SetFloat("_TailRampShare", rampShare);
            m.SetFloat("_TailRampLevel", Mathf.Exp(-(1f - rampShare) * falloff));
        }

        private void OnDestroy()
        {
            ProjectileEvents.Impact -= OnImpact;
            ProjectileEvents.Spawned -= OnSpawned;
            ProjectileEvents.Expired -= OnExpired;
            if (s_instance != this) return;
            s_instance = null;
            ProjectileDriver.DrawInterimTrails = true;
            if (_material != null) Destroy(_material);
            if (_tdGlowMaterial != null) Destroy(_tdGlowMaterial);
            if (_tdCoreMaterial != null) Destroy(_tdCoreMaterial);
            if (_tdInkMaterial != null) Destroy(_tdInkMaterial);   // WP-104 B4
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

            if (CameraModeService.Current == CameraMode.TopDown)
            {
                var info = new ProjectileSpawnInfo(--_tdSyntheticId, -1, default, ProjectileKind.Bullet, default,
                    origin, (end-origin).normalized, profile.Speed, 0);
                // Legacy hitscan specials keep their explicit profile, including the sniper envelope.
                if (!RecordTdProjectile(info)) return true;
                for (int i = 0; i < TdCapacity; i++)
                    if (_tdRounds[i].Alive && _tdRounds[i].Id == info.ProjectileId)
                    {
                        // a legacy hitscan shot is a tracer (a pellet only as sg_2's centre one); the admission counted the default look
                        bool wasTracer = _tdRounds[i].Tracer, wasSniper = _tdRounds[i].Sniper;
                        _tdRounds[i].Pellet = pellet;
                        TdLook(ref _tdRounds[i], profile, !pellet || (profile.CentreTracer && centrePellet));
                        LiveTdTracerRounds += (_tdRounds[i].Tracer ? 1 : 0) - (wasTracer ? 1 : 0);
                        LiveTdSmoke += (_tdRounds[i].Sniper ? 1 : 0) - (wasSniper ? 1 : 0);
                        _tdRounds[i].Target = (end-origin).magnitude;
                        break;
                    }
                return true;
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
            if (CameraModeService.Current == CameraMode.TopDown) RecordTdProjectile(info);
            if (info.Kind == ProjectileKind.Pellet && info.PelletIndex == 0)
            {
                _centrePellets[_centreNext] = info.ProjectileId;
                _centreNext = (_centreNext + 1) % _centrePellets.Length;
            }
        }

        private void OnExpired(ProjectileExpireInfo info) =>
            StopTdProjectile(info.ProjectileId, info.Position, info.Reason == ProjectileEndReason.Cleared);

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
            if (CameraModeService.Current == CameraMode.TopDown)
            {
                if (hit.Stopped) StopTdProjectile(hit.ProjectileId, hit.Position);
                return;
            }
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
            ClearTd();
            for (int i = 0; i < Capacity; i++) _rounds[i].Alive = false;
            _live = 0;
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            using var marker = s_marker.Auto();   // WP-073 §5: per-tier ms, read by ProfilerRecorder (S-10 / tier-ms.md)
            if (_material != null) ProjectileDriver.DrawInterimTrails = false;
            float dt = VfxClock.Delta;   // WP-104 B4: the presentation clock (unscaled while the tree preview runs)
            if (CameraModeService.Current == CameraMode.TopDown)
            {
                TickTd(dt);
                _live = LiveTdTracers; _firstPerson = false; _performance = VfxTier.Current == VfxTierLevel.Performance;
                DrawnCore = _material != null ? DrawTd(0) : 0;
                DrawnGlow = _material != null ? DrawTd(1) + DrawTd(2) : 0;   // sniper vapour + the tracers' heat halo (none on Performance)
                if (_material != null) DrawTdInk();   // WP-104 B4 (AD K3): the marked rounds' sumi cores, one queue step later
                DrawnGlint = 0;
                return;
            }
            ClearTd();
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
