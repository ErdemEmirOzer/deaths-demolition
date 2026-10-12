using System.Collections.Generic;
using DD.Prototype.Core.Pooling;
using DD.Prototype.CameraSystem;
using DD.Prototype.Rendering;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · The AP-010 layer of the feedback chain. Owned and called by
    /// <see cref="HitFeedbackDirector"/> AFTER its budget check, so the single-owner rule
    /// and the FeedbackBudget ceilings of WP-002 are untouched: this class never decides
    /// whether an effect plays, only what it looks like. When the catalog is absent every
    /// method returns false and the director keeps its WP-002 procedural burst.
    ///
    /// Three pools serve all eight families: flipbook quads, decal quads and two
    /// atlas-sprite bursts (armour fragments, smoke). RecycleOldest, like the director's.
    /// WP-019b: the blood layer (<see cref="BloodSystem"/>, directive §10) owns its own
    /// 128-slot mark pool and the droplet pool; flesh hits, armour breaks and deaths route
    /// through it, the flesh sheet stays as the fallback when the blood layer is absent.
    /// </summary>
    public sealed class VfxDressing
    {
        public const int FlipbookCapacity = 48;
        /// <summary>WP-073 B2 (direction §5): impact marks 64 / 128 / 256 by tier (read at configure).</summary>
        public static int DecalCapacity => VfxTier.Pick(64, 128, 384);   // DEC-100: Cinematic 384
        public const int FragmentBurstCapacity = 8;
        public const int SmokeBurstCapacity = 8;

        // PROTOTYPE-ONLY placement numbers (manifest usage notes where they exist).
        private const float ImpactSurfaceLift = 0.03f;
        private const float DecalSecondsEnvironment = 12f;
        /// <summary>WP-073 B2: environment marks 8 s on Performance, 12 s above (direction §5).</summary>
        private static float ImpactDecalSeconds => VfxTier.Pick(8f, DecalSecondsEnvironment, 20f);   // DEC-100: Cinematic marks stay 20 s
        private const float DecalSecondsSplat = 30f;
        private const float DecalSecondsSplinter = 8f;
        private const float DecalAlphaCap = 0.85f;
        private const float SplatAlphaCap = 0.70f;

        private PrototypePool<FlipbookEffect> _flipbooks;
        private PrototypePool<DecalEffect> _decals;
        private PrototypePool<AtlasBurstEffect> _fragments;
        private PrototypePool<AtlasBurstEffect> _smoke;
        private MuzzleSystem _muzzleSystem;
        private PrototypePool<AtlasBurstEffect> _sparks;
        private PrototypePool<AtlasBurstEffect> _leaves;
        private PrototypePool<AtlasBurstEffect> _paper;
        private readonly BloodSystem _blood = new BloodSystem();
        private VfxEventBinder _binder;
        private PrototypePool<AtlasBurstEffect> _straw;
        private PrototypePool<CasingEffect> _casings;
        /// <summary>WP-073 B1: the casing pool (direction §1.3: 24 / 48 / 96 by tier; the pool is sized once, at the tier in effect).</summary>
        public const int TdCasingBudget = 12;
        public static int CasingCapacity => VfxTier.Pick(24, 48, 128);   // DEC-100: Cinematic 128
        public int CasingsEjected { get; private set; }
        public PrototypePool<CasingEffect> CasingPool => _casings;
        private readonly LanternFlames _lanterns = new LanternFlames();
        // WP-101 (R4, AP-041): rows resolved once at configure (a null class / false set = the AP-010 row plays), the per-class
        // flash variant turn, the per-set hole variant turn and the smoke card throttle. Sized once: nothing allocates per shot.
        private readonly VfxCatalogEntry[][] _ap041Muzzle = new VfxCatalogEntry[5][];
        private VfxCatalogEntry _ap041Smoke;
        private bool _ap041Impacts;
        private readonly bool[] _ap041DecalSet = new bool[Ap041Vfx.DecalSets];
        private readonly int[] _ap041MuzzleNext = new int[5];
        private readonly int[] _ap041DecalNext = new int[Ap041Vfx.DecalSets];
        private float _ap041SmokeAt = -1f;
        private static readonly List<VfxCatalogEntry> s_deathSheets = new List<VfxCatalogEntry>(4);
        private readonly Dictionary<int, BreakEventId> _breakIds = new Dictionary<int, BreakEventId>(64);
        public const int StrawBurstCapacity = 4;
        /// <summary>WP-047 wave-3 effects played (claw swipe, spawn dust, dust step, mag drop, pump smoke, break debris).</summary>
        public int Wave3Played { get; private set; }
        public string LastFootstepEffectId { get; private set; }
        public VfxEventBinder Binder => _binder;
        public MuzzleVariant LastMuzzleVariant { get; private set; }
        public int MuzzlePlays { get; private set; }
        public string LastMuzzleNote { get; private set; } = string.Empty;
        /// <summary>WP-101 (R4, AP-041): the catalog row the last flash played (fb_muzzle, or an fb_ap041_muzzle_*_td_0n row top-down).</summary>
        public string LastMuzzleRowId { get; private set; } = string.Empty;
        /// <summary>WP-101: the life (s) the last flash was given (test readout; the 50 ms TD flash may be gone a frame later).</summary>
        public float LastMuzzleLifeSeconds { get; private set; } = -1f;
        /// <summary>WP-101: the AP-041 TD variant index (0..2) of the last AP-041 flash; -1 before the first.</summary>
        public int LastAp041MuzzleVariant { get; private set; } = -1;
        public int Ap041MuzzlePlays { get; private set; }
        public int Ap041SmokeCards { get; private set; }
        /// <summary>WP-101 test seam: the profile the next flashes use instead of the held weapon's (null = the held weapon's).</summary>
        internal static VfxWeaponProfile ProfileOverrideForTests;
        public LanternFlames Lanterns => _lanterns;
        private int _deathToggle;
        private Vector3 _lastBeaconPoint;
        private bool _wave2Subscribed;
        public const int SparkBurstCapacity = 8;
        public const int LeafBurstCapacity = 6;
        public const int PaperBurstCapacity = 4;
        /// <summary>WP-019b wave-2 effects played (spark / crack dust / leaf / water / paper / residue / pickup flash / expire / special-ultra death).</summary>
        public int Wave2Played { get; private set; }
        private bool _ready;
        private int _splatToggle;

        /// <summary>AD M6: shock ring dust colour (h ~30°, s 0.22, v 0.62). WP-073 B3: read by <see cref="ExplosionDirector"/>.</summary>
        internal static readonly Color ShockRingDustTint = new Color(0.62f, 0.55f, 0.48f, 1f);
        private const float DeathSheetAlphaStepSeconds = 0.28f;
        private const float DeathSheetAlphaStepTo = 0.35f;
        private const float DeathSheetNearCameraMetres = 1.5f;
        private const float DeathSheetNearSize = 1.0f;
        private const float DeathSheetNearAlphaCap = 0.6f;

        public bool IsReady => _ready;
        /// <summary>Audit seam (allocation bisect): true = every Play* returns false so the director's WP-002 fallbacks run instead. Default false.</summary>
        public static bool AuditMute;
        /// <summary>Audit seam (allocation bisect): PlayExplosion steps — 1 card, 2 shockwave, 4 smoke, 8 scorch decal. Default all.</summary>
        public static int AuditExplosionMask = -1;
        public int Played { get; private set; }
        internal void CountExplosionCard() => Played++;
        internal void CountExplosionDecal() => DecalsPlayed++;
        /// <summary>WP-047H: v004 asymmetric armour-break sheets played (0 when the row is absent → the ×1.3 armour sheet fallback).</summary>
        public int ArmorBreakSheets { get; private set; }
        public int DecalsPlayed { get; private set; }

        public PrototypePool<FlipbookEffect> FlipbookPool => _flipbooks;
        public PrototypePool<DecalEffect> DecalPool => _decals;
        public BloodSystem Blood => _blood;

        /// <summary>Builds the pools when the catalog is present. Safe to call again (idempotent).</summary>
        public void Configure(Transform templateRoot)
        {
            if (!VfxLibrary.IsReady)
            {
                _ready = false;
                return;
            }

            if (_flipbooks == null)
            {
                VfxLibrary.WarmMaterials(templateRoot);
                ResolveAp041();
                // WP-047 (S-02 WP-053 finding): every pool is prewarmed to capacity at configure time — growing on demand
                // cost ≈ 12 allocations per Instantiate inside the hot path (the scorch decals of the first explosions
                // were the "+100 per window"), and decals live long enough that the growth spanned a whole round.
                _flipbooks = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(Template<FlipbookEffect>(templateRoot, "FX_AP010_Flipbook"),
                    FlipbookCapacity, FlipbookCapacity, PoolExhaustedPolicy.RecycleOldest));
                _decals = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(Template<DecalEffect>(templateRoot, "FX_AP010_Decal"),
                    DecalCapacity, DecalCapacity, PoolExhaustedPolicy.RecycleOldest));

                if (VfxLibrary.TryGetPlayable(VfxIds.AtlasArmorBreak, out VfxCatalogEntry atlas))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010_ArmorFragments");
                    t.Configure(atlas, true, 1.5f, 3.2f, 45f);
                    _fragments = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(t, FragmentBurstCapacity, FragmentBurstCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                if (VfxLibrary.TryGetPlayable(VfxIds.CardSmoke, out VfxCatalogEntry smoke))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010_Smoke");
                    t.Configure(smoke, false, 0.4f, 1.2f, 30f);
                    _smoke = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(t, SmokeBurstCapacity, SmokeBurstCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                // AP-010B v003 wave 2: sprite-sheet bursts (spark on ricochet, leaf on foliage, paper on paper).
                if (VfxLibrary.TryGetPlayable(VfxIds.Spark, out VfxCatalogEntry spark))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Sparks");
                    t.Configure(spark, true, 2.5f, 5f, 35f);
                    _sparks = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(t, SparkBurstCapacity, SparkBurstCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                if (VfxLibrary.TryGetPlayable(VfxIds.LeafBurst, out VfxCatalogEntry leaf))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Leaves");
                    t.Configure(leaf, true, 0.8f, 2f, 50f);
                    _leaves = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(t, LeafBurstCapacity, LeafBurstCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                if (VfxLibrary.TryGetPlayable(VfxIds.PaperTear, out VfxCatalogEntry paper))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Paper");
                    t.Configure(paper, true, 0.6f, 1.6f, 45f);
                    _paper = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(t, PaperBurstCapacity, PaperBurstCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                // WP-047 C: straw stalks for the bale break.
                if (VfxLibrary.TryGetPlayable(VfxIds.StrawPuff, out VfxCatalogEntry straw))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Straw");
                    t.Configure(straw, true, 0.5f, 1.4f, 60f);
                    _straw = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(t, StrawBurstCapacity, StrawBurstCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                // WP-047E · AP-013C casings (P-06): one pooled mesh per shot from the Ejection socket; absent library = no casings.
                if (_casings == null && CasingLibrary.Current != null && CasingLibrary.Current.IsComplete)
                {
                    var go = new GameObject("FX_AP013_Casing");
                    go.transform.SetParent(templateRoot, false);
                    go.SetActive(false);
                    _casings = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(go.AddComponent<CasingEffect>(), CasingCapacity, CasingCapacity, PoolExhaustedPolicy.RecycleOldest));
                }

                _lanterns.Configure(templateRoot, _flipbooks);
                // WP-073 B2: the barrel heat strip and haze live for seconds; on the shared pool a busy fight's RecycleOldest took
                // them over (the strip then moved someone else's card). Their own two-card pool.
                _barrelCards = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(Template<FlipbookEffect>(templateRoot, "FX_WP073_BarrelCard"),
                    2, 2, PoolExhaustedPolicy.RecycleOldest));
                ImpactSystem.Ensure(this);
                RocketVisualSystem.Ensure();
                ExplosionDirector.Ensure(this);
                ArcSystem.Ensure();           // WP-073 B6: Raiju (direction §2.1)
                GelSystem.Ensure();           // WP-073 B6: Namekuji (§2.2)
                GroundFireSystem.Ensure();    // WP-073: S-04's K10b burning ground (RunTreeGroundFire)
                SpecialWeaponGlow.Ensure();   // WP-073 B6: the two models' emissive states
            }
            else
            {
                Refill();   // WP-069 (CI cb78bfb4): a pool that lost instances behind its back is topped up here, not in a hit
            }

            if (_binder == null)
            {
                _binder = new VfxEventBinder(this);
            }

            _binder.Subscribe();
            RocketVisualSystem.Ensure();
            ExplosionDirector.Ensure(this);

            if (!_wave2Subscribed)
            {
                _wave2Subscribed = true;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponSpawned += OnSpecialWeaponSpawned;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponPickedUp += OnSpecialWeaponPickedUp;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponGroundExpired += OnSpecialWeaponGroundExpired;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponEnded += OnSpecialWeaponEnded;
                DD.Prototype.Weapons.WeaponEvents.WeaponEquipped += OnWeaponEquipped;
                DD.Prototype.Rounds.RunEvents.RoundStarted += OnRoundStarted;
                DD.Prototype.Props.BreakableProp.Broke += OnBreakableBroke;
            }

            _blood.Configure(templateRoot, _flipbooks);
            _ready = true;
        }

        /// <summary>WP-069 (CI cb78bfb4): every dressing pool back to capacity at configure / scene load (FeedbackPoolWarmer.Refill). Returns the instances added.</summary>
        public int Refill()
        {
            if (_flipbooks == null)
            {
                return 0;
            }

            return FeedbackPoolWarmer.Refill(_flipbooks) + FeedbackPoolWarmer.Refill(_decals) + FeedbackPoolWarmer.Refill(_fragments) +
                   FeedbackPoolWarmer.Refill(_smoke) + FeedbackPoolWarmer.Refill(_sparks) + FeedbackPoolWarmer.Refill(_leaves) +
                   FeedbackPoolWarmer.Refill(_paper) + FeedbackPoolWarmer.Refill(_straw) + FeedbackPoolWarmer.Refill(_casings) +
                   FeedbackPoolWarmer.Refill(_barrelCards);
        }

        /// <summary>Per frame from the director: blood far-LOD absorb + merged mesh rebuild, delayed cards, barrel heat.</summary>
        public void Tick(float now)
        {
            if (_ready)
            {
                _blood.Tick(now);
                _binder?.Tick(now);
                _lanterns.Tick(now);
                TickBarrelHeat(now);
            }
        }

        // ------------------------------------------------------------------ WP-047D · barrel heat (AP-010B `barrel_heat` strip)

        public const float HeatPerShotA = 0.10f, HeatPerShotB = 0.06f, HeatPerShotC = 0.18f;
        public const float HeatDecayPerSecond = 0.35f;
        public const float HeatShowAbove = 0.25f, HeatHideBelow = 0.08f;

        private float _heat;
        private float _heatTickAt = -1f;
        private FlipbookEffect _heatCard;
        private Transform _heatMuzzle;
        private PrototypePool<FlipbookEffect> _barrelCards;
        private readonly FlipbookEffect[] _muzzleCards = new FlipbookEffect[8];
        private readonly Transform[] _muzzleParents = new Transform[8];
        private int _muzzleCardNext;
        private Transform _heatBody;
        private DD.Prototype.Weapons.WeaponVisualRig _heatRig;
        private float _heatLength = 0.45f;

        public float BarrelHeat => _heat;
        public bool BarrelHeatVisible => _heatCard != null && _heatCard.IsSpawned;

        private void AddHeat(MuzzleVariant variant, Transform muzzle)
        {
            _heat = Mathf.Clamp01(_heat + (variant == MuzzleVariant.B ? HeatPerShotB : variant == MuzzleVariant.C ? HeatPerShotC : HeatPerShotA));
            // The barrel's visual axis: rig body → muzzle socket (the socket's forward is the aim; in the lowered pose the
            // barrel hangs). The rig is looked up once per muzzle transform (a weapon swap hands over a new one).
            if (!ReferenceEquals(muzzle, _heatMuzzle))
            {
                _heatMuzzle = muzzle;
                DD.Prototype.Weapons.WeaponVisualRig rig = muzzle != null ? muzzle.GetComponentInParent<DD.Prototype.Weapons.WeaponVisualRig>() : null;
                _heatRig = rig;
                _heatBody = rig != null ? rig.Body : null;
            }
        }

        /// <summary>
        /// AP-013C casing from the rig's Ejection socket. WP-073 B1: the class profile's throw (+X 35° up, 10° back, its
        /// speed) and rest time per tier (3 / 5 / 8 s rifle); without a profile the WP-047E throw and 1.6 s.
        /// </summary>
        private void EjectCasing(MuzzleVariant variant, Transform muzzle, VfxWeaponProfile profile, int shotSerial = -1)
        {
            if (_casings == null || muzzle == null || (profile != null && !profile.HasCasing)) return;
            DD.Prototype.Weapons.WeaponController active = DD.Prototype.Weapons.WeaponRegistry.ActiveController;
            if (active != null && active.Classic != null && active.Classic.Definition != null
                && active.Classic.Definition.presentation.casing == DD.Prototype.Core.Weapons.WeaponCasing.None) return;
            CasingLibrary lib = CasingLibrary.Current;
            if (lib == null) return;
            DD.Prototype.Weapons.WeaponVisualRig rig = _heatMuzzle == muzzle ? _heatRig : muzzle.GetComponentInParent<DD.Prototype.Weapons.WeaponVisualRig>();
            Transform port = rig != null ? rig.Ejection : muzzle;
            float seed = Random.value;
            Vector3 velocity;
            float life = CasingEffect.LifeSeconds;
            MuzzleVariant mesh = variant;
            float scale = 1f;
            if (profile != null)
            {
                scale = 0.85f + 0.3f * seed;
                float speed = profile.CasingSpeed * scale;
                velocity = (port.right * 0.819f + Vector3.up * 0.574f - port.forward * 0.174f) * speed;
                life = VfxTier.Pick(profile.RestSeconds.x, profile.RestSeconds.y, profile.RestSeconds.z);
                mesh = profile.CasingMesh;
            }
            else
            {
                velocity = port.right * (1.9f + seed * 0.8f) + Vector3.up * (1.4f + seed * 0.6f) - port.forward * 0.4f;
            }

            MakeTdCasingRoom(_casings);
            CasingEffect c = _casings.Get(port.position, Quaternion.LookRotation(port.forward, port.up));
            if (c == null) return;
            c.Play(lib.MeshFor(mesh), lib.Material, velocity, seed, life);
            CasingsEjected++;
            // WP-087 (C-00 05.10): the throw this casing got, with its shot — the casing sound lands where this casing lands
            DD.Prototype.Core.Config.ClassicWeaponId weapon = active != null && active.Classic != null ? active.Classic.Id : default;
            CasingEvents.RaiseLaunched(new CasingLaunchInfo(shotSerial >= 0 ? shotSerial : active != null ? active.ShotSerial : 0, weapon, port.position, velocity, scale));
        }

        /// <summary>Recycle oldest visible TD casing before the next real launch; one launch event still follows each new casing.</summary>
        public static void MakeTdCasingRoom(DD.Prototype.Core.Pooling.PrototypePool<CasingEffect> pool)
        {
            if (CameraModeService.Current != CameraMode.TopDown || pool == null) return;
            while (pool.CountSpawned >= TdCasingBudget) pool.Spawned[0].Despawn();
        }

        /// <summary>The weapons whose shells follow the mechanism, never the flash: sg_1 pump (clip or 0.40 s fallback) and
        /// sg_2 break-open (M-21, C-00 30.09 — no shell on the shot, two CasingEjected events when the breech opens on reload).</summary>
        public static bool MechanismEjects(DD.Prototype.Core.Config.ClassicWeaponId id) =>
            id == DD.Prototype.Core.Config.ClassicWeaponId.CombatShotgun || id == DD.Prototype.Core.Config.ClassicWeaponId.Sg2;

        /// <summary>One shell per CasingEjected mechanism event of a <see cref="MechanismEjects"/> weapon.</summary>
        public void PlayPumpCasing(DD.Prototype.Weapons.WeaponMechanismEvent info)
        {
            if (!_ready || info.Kind != DD.Prototype.Weapons.WeaponMechanismKind.CasingEjected || !MechanismEjects(info.Weapon)) return;
            DD.Prototype.Weapons.WeaponController active = DD.Prototype.Weapons.WeaponRegistry.ActiveController;
            if (active == null || active.SpecialWeaponActive || active.Classic == null || active.Classic.Id != info.Weapon) return;
            EjectCasing(MuzzleVariant.C, active.Muzzle, VfxWeaponProfiles.For(info.Weapon), info.ShotSerial);
        }

        private Vector3 BarrelAxis()
        {
            if (_heatBody != null && _heatMuzzle != null)
            {
                Vector3 axis = _heatMuzzle.position - _heatBody.position;
                if (axis.sqrMagnitude > 1e-4f) return axis.normalized;
            }

            return _heatMuzzle != null ? _heatMuzzle.forward : Vector3.forward;
        }

        // WP-047E · `barrel_heat_distort` haze card over the strip (DD/VFX HeatHaze: refraction when the pipeline asset has the
        // opaque texture, a faint shimmer otherwise — WP-029's asset keeps it off; the keyword follows the asset).
        private FlipbookEffect _hazeCard;
        private VfxCatalogEntry _hazeRow;
        private bool _hazeTried;

        public bool BarrelHazeVisible => _hazeCard != null && _hazeCard.IsSpawned;
        public bool HazeRefracts { get; private set; }
        public bool HazeUsesTile { get; private set; }

        private VfxCatalogEntry HazeRow()
        {
            if (_hazeTried) return _hazeRow;
            _hazeTried = true;
            // AP-014C normal tile first (WP-047E), the AP-010B data card as the fallback.
            VfxCatalogEntry data = VfxLibrary.Get(VfxIds.BarrelHeatDistortTile);
            bool tile = data != null && data.Texture != null;
            if (!tile) data = VfxLibrary.Get(VfxIds.BarrelHeatDistort);
            Shader shader = PrototypeShaders.VfxHeatHaze;
            if (data == null || data.Texture == null || shader == null) return null;
            var m = new Material(shader) { name = "M_barrel_heat_haze (runtime)" };
            m.SetTexture("_Sheet", data.Texture);
            m.SetFloat("_TileMode", tile ? 1f : 0f);
            HazeUsesTile = tile;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            HazeRefracts = urp != null && urp.supportsCameraOpaqueTexture;
            if (HazeRefracts) m.EnableKeyword("DD_OPAQUE_TEXTURE"); else m.DisableKeyword("DD_OPAQUE_TEXTURE");
            _hazeRow = new VfxCatalogEntry
            {
                Id = VfxIds.BarrelHeatDistort, Family = VfxFamily.Weapon, Kind = VfxKind.Flipbook, SourceFile = data.SourceFile, ManifestFile = data.ManifestFile,
                Texture = data.Texture, Material = m, Columns = 1, Rows = 1, FramesUsed = 1, Fps = 1f, DurationSeconds = 0f, Loop = true,
                WorldSizeMetres = data.WorldSizeMetres > 0f ? data.WorldSizeMetres : 0.06f, WorldHeightMetres = data.WorldHeightMetres > 0f ? data.WorldHeightMetres : 0.3f,
                OriginUv = new Vector2(0.5f, 0.5f), DefaultTint = Color.white, HdrIntensity = 1f
            };
            return _hazeRow;
        }

        private void TickBarrelHaze(Vector3 barrel)
        {
            // WP-073 B1: the haze card is Cinematic-only (direction §1.1) and never in first person — at the eye it drew as a
            // translucent rectangle across the frame (AD B1 MUST 2).
            bool hazeAllowed = VfxTier.Current == VfxTierLevel.Cinematic
                && DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            if (_heatCard == null || !hazeAllowed)
            {
                if (_hazeCard != null) { if (_hazeCard.IsSpawned) _barrelCards.Release(_hazeCard); _hazeCard = null; }
                return;
            }

            VfxCatalogEntry row = HazeRow();
            if (row == null) return;
            // The haze rises off the strip: half a strip above the barrel line, camera-yaw so it stays a vertical shimmer.
            // AD SHOULD (WP-047E): half size, centred on the strip (it read as a separate smudge below the barrel).
            Vector3 at = _heatMuzzle.position - barrel * (_heatLength * 0.5f) + Vector3.up * (row.WorldHeightMetres * 0.25f);
            if (_hazeCard == null || !_hazeCard.IsSpawned)
            {
                _hazeCard = _barrelCards.Get(at, Quaternion.identity);
                if (_hazeCard == null) return;
                _hazeCard.Play(row, FlipbookEffect.Facing.CameraYaw, row.WorldSizeMetres * 1.25f, Color.white, _heat, 0f, 3600f);
                return;
            }

            _hazeCard.transform.position = at;
            _hazeCard.SetAlphaCap(_heat);
        }

        private void TickBarrelHeat(float now)
        {
            if (_heatTickAt < 0f) _heatTickAt = now;
            float dt = Mathf.Max(0f, now - _heatTickAt);
            _heatTickAt = now;
            _heat = Mathf.Max(0f, _heat - HeatDecayPerSecond * dt);

            if (_heatCard != null && (!_heatCard.IsSpawned || _heatMuzzle == null || _heat < HeatHideBelow))
            {
                if (_heatCard.IsSpawned) _barrelCards.Release(_heatCard);
                _heatCard = null;
                TickBarrelHaze(Vector3.forward);
                return;
            }

            if (_heatCard == null)
            {
                if (_heat < HeatShowAbove || _heatMuzzle == null || !VfxLibrary.TryGetPlayable(VfxIds.BarrelHeat, out VfxCatalogEntry row))
                {
                    return;
                }

                // Manifest: 0.08 × 0.45 m strip, muzzle end at v = 0 → the card sits half its length behind the muzzle.
                float length = row.WorldHeightMetres > 0f ? row.WorldHeightMetres : 0.45f;
                // Not parented: a weapon swap rebuilds the rig and would take the pooled card down with the muzzle.
                _heatCard = _barrelCards.Get(_heatMuzzle.position - BarrelAxis() * (length * 0.5f), Quaternion.identity);
                if (_heatCard == null)
                {
                    return;
                }

                _heatLength = length;
                _heatCard.Play(row, FlipbookEffect.Facing.Camera, row.WorldSizeMetres, Color.white, _heat, 0f, 3600f);
                _heatCard.SetAlign(BarrelAxis(), false);
                return;
            }

            Vector3 barrel = BarrelAxis();
            _heatCard.transform.position = _heatMuzzle.position - barrel * (_heatLength * 0.5f);
            _heatCard.SetAlign(barrel, false);
            _heatCard.SetAlphaCap(_heat);
            TickBarrelHaze(barrel);
        }

        private static T Template<T>(Transform root, string name) where T : PooledEffect
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.SetActive(false);
            return go.AddComponent<T>();
        }

        // ------------------------------------------------------------------ impacts

        /// <summary>Family 1: surface impact flipbook (+ decal on static surfaces). False = not dressed here.</summary>
        public bool PlaySurfaceImpact(SurfaceKind surface, Vector3 point, Vector3 normal, float scale)
        {
            if (!_ready || AuditMute)
            {
                return false;
            }

            // AP-010B wave 2 first: the surface-specific effects the v002 set lacked (matrix rows Foliage / Water / Paper).
            switch (surface)
            {
                case SurfaceKind.Foliage:
                    if (PlayBurst(_leaves, point, normal, 10, 0.03f, 0.10f, 0.9f)) { Wave2Played++; return true; }
                    break;
                case SurfaceKind.Water:
                    if (PlayWaterSplash(point, normal)) { Wave2Played++; return true; }
                    break;
                case SurfaceKind.Paper:
                    if (PlayBurst(_paper, point, normal, 8, 0.03f, 0.10f, 0.8f)) { Wave2Played++; return true; }
                    break;
            }

            string fb, decal;
            ImpactIdsFor(surface, out fb, out decal);
            _lastFlipbook = null;
            _lastDecal = null;
            bool played = PlayFlipbook(fb, point + normal * ImpactSurfaceLift, FlipbookEffect.Facing.Camera, Quaternion.identity, scale, Color.white, 1f, 0f);
            if (decal != null)
            {
                PlayDecal(decal, point, normal, 0f, ImpactDecalSeconds, DecalAlphaCap, Color.white);
            }

            // WP-073 B2: the projectile's Impact event follows this call in the same stack (HitscanEngine) and refines it by calibre.
            _lastSurfaceFrame = Time.frameCount;
            _lastSurfacePoint = point;
            _lastSurfaceCard = _lastFlipbook;
            _lastSurfaceDecal = _lastDecal;
            _lastSurfaceDecalSpall = Ap041Vfx.IsAp041Decal(decal);
            _lastSurfaceDecalSet = Ap041Vfx.DecalSetOf(surface);
            return played;
        }

        /// <summary>Family 1 on a body: flesh or armour impact sheet at the hit point (no decal: the body moves). An armour hit adds the AP-010B crack dust (plate damaged, not yet broken).</summary>
        public bool PlayBodyImpact(bool armored, Vector3 point, Vector3 normal, float scale)
        {
            if (!_ready || AuditMute)
            {
                return false;
            }

            bool played = PlayFlipbook(armored ? VfxIds.ImpactArmor : VfxIds.ImpactFlesh, point + normal * ImpactSurfaceLift,
                FlipbookEffect.Facing.Camera, Quaternion.identity, scale, Color.white, 1f, 0f);
            if (armored && VfxLibrary.TryGetPlayable(VfxIds.ArmorCrackDust, out VfxCatalogEntry dust))
            {
                // Origin (0.3, 0.5), +u outward: rolled to the hit normal. Manifest N1: the flake count lives in the sheet, no emitter here.
                FlipbookEffect fx = _flipbooks.Get(point + normal * ImpactSurfaceLift, Quaternion.identity);
                if (fx != null)
                {
                    fx.Play(dust, FlipbookEffect.Facing.Camera, dust.WorldSizeMetres, Color.white, 1f, 0.25f);
                    fx.SetAlign(normal, true);
                    Wave2Played++;
                    played = true;
                }
            }

            return played;
        }

        /// <summary>AP-010B wave 2: sparks at a ricochet point (sprite burst + the streak flipbook rolled to the reflection).</summary>
        public bool PlaySpark(Vector3 point, Vector3 reflection)
        {
            if (!_ready || AuditMute)
            {
                return false;
            }

            Vector3 n = reflection.sqrMagnitude > 1e-4f ? reflection.normalized : Vector3.up;
            bool any = PlayBurst(_sparks, point, n, 8, 0.01f, 0.04f, 0.35f);
            if (VfxLibrary.TryGetPlayable(VfxIds.SparkStreak, out VfxCatalogEntry streak))
            {
                FlipbookEffect fx = _flipbooks.Get(point, Quaternion.identity);
                if (fx != null)
                {
                    fx.Play(streak, FlipbookEffect.Facing.Camera, streak.WorldSizeMetres, Color.white);
                    fx.SetAlign(n, true);
                    any = true;
                }
            }

            if (any) Wave2Played++;
            return any;
        }

        private bool PlayWaterSplash(Vector3 point, Vector3 normal)
        {
            bool any = false;
            if (VfxLibrary.TryGetPlayable(VfxIds.WaterSplash, out VfxCatalogEntry splash))
            {
                FlipbookEffect fx = _flipbooks.Get(point, Quaternion.identity);
                if (fx != null) { fx.Play(splash, FlipbookEffect.Facing.Camera, splash.WorldSizeMetres, WaterLook.SplashTint); any = true; }   // WP-047D: the pond's DD/Water v2 shallow colour
            }

            if (VfxLibrary.TryGetPlayable(VfxIds.WaterSplashRing, out VfxCatalogEntry ring))
            {
                FlipbookEffect fx = _flipbooks.Get(point + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
                if (fx != null) { fx.Play(ring, FlipbookEffect.Facing.Fixed, ring.WorldSizeMetres, WaterLook.SplashTint); any = true; }
            }

            return any;
        }

        private bool PlayBurst(PrototypePool<AtlasBurstEffect> pool, Vector3 point, Vector3 normal, int count, float sizeMin, float sizeMax, float life)
        {
            if (pool == null)
            {
                return false;
            }

            AtlasBurstEffect burst = pool.Get(point, Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up));
            if (burst == null)
            {
                return false;
            }

            burst.Play(count, sizeMin, sizeMax, life);
            Played++;
            return true;
        }

        // ------------------------------------------------------------------ wave 2 event hooks

        private void OnSpecialWeaponSpawned(Vector3 point, float seconds)
        {
            _lastBeaconPoint = point;
        }

        private void OnSpecialWeaponPickedUp(float seconds)
        {
            ReleaseMuzzleCards();
            PlayGroundCard(VfxIds.WeaponPickupFlash, PlayerFeet(_lastBeaconPoint));
        }

        private void OnSpecialWeaponGroundExpired()
        {
            PlayGroundCard(VfxIds.WeaponExpire, _lastBeaconPoint);
        }

        private void OnRoundStarted(int round)
        {
            WaterLook.PrimeAtRoundStart();
        }

        private void OnWeaponEquipped(DD.Prototype.Core.Config.ClassicWeaponId id)
        {
            ReleaseMuzzleCards();
        }

        /// <summary>
        /// WP-073: a muzzle card rides as a child of the muzzle socket, and an equip rebuilds the weapon model (Destroy). A card
        /// still live then died with the model and the pool re-instantiated it inside a later hit (≈ 18 allocations each, the
        /// wave-3 spawn dust after a swap). Destroy is deferred to the end of the frame and the equip event comes in the same
        /// call, so the live cards go back to the pool here and survive.
        /// </summary>
        private void ReleaseMuzzleCards()
        {
            for (int i = 0; i < _muzzleCards.Length; i++)
            {
                FlipbookEffect fx = _muzzleCards[i];
                if (fx != null && fx.IsSpawned && _muzzleParents[i] != null && fx.transform.parent == _muzzleParents[i])
                {
                    fx.Despawn();
                }

                _muzzleCards[i] = null;
                _muzzleParents[i] = null;
            }
        }

        private void OnSpecialWeaponEnded()
        {
            ReleaseMuzzleCards();
            PlayGroundCard(VfxIds.WeaponExpire, PlayerFeet(_lastBeaconPoint) + Vector3.up * 1.0f);
        }

        private void OnBreakableBroke(DD.Prototype.Props.BreakableProp prop, Vector3 point, Vector3 direction)
        {
            if (!_ready || prop == null || !VfxLibrary.TryGetPlayable(VfxIds.BreakableResidue, out VfxCatalogEntry residue) || _decals == null)
            {
                return;
            }

            // WP-047 C: the AP-017 event identity (six break families) picks the residue cell, the debris sheet and the dust card.
            // `Object.name` marshals a new string per read (ADR-037-1): the id is resolved once per prop instance.
            int propId = prop.GetInstanceID();
            if (!_breakIds.TryGetValue(propId, out BreakEventId breakId))
            {
                breakId = BreakableVfx.EventIdFor(prop.name);
                if (_breakIds.Count >= 256) _breakIds.Clear();
                _breakIds[propId] = breakId;
            }

            BreakableVfxProfile profile = BreakableVfx.Get(breakId);
            Vector3 ground = prop.transform.position;
            DecalEffect decal = _decals.Get(ground + Vector3.up * 0.01f, Quaternion.LookRotation(Vector3.up, Vector3.forward));
            if (decal != null)
            {
                decal.Play(residue, residue.WorldSizeMetres, DecalSecondsSplat, DecalAlphaCap, Color.white, Random.Range(0f, 360f), profile.ResidueCell);
                DecalsPlayed++;
                Wave2Played++;
            }

            Vector3 n = direction.sqrMagnitude > 1e-4f ? -direction.normalized : Vector3.up;
            if (profile.DebrisCount > 0)
            {
                PrototypePool<AtlasBurstEffect> pool = profile.DebrisSheetId == VfxIds.LeafBurst ? _leaves : profile.DebrisSheetId == VfxIds.PaperTear ? _paper : profile.DebrisSheetId == VfxIds.StrawPuff ? _straw : null;
                if (PlayBurst(pool, point, n, profile.DebrisCount, 0.03f, 0.12f, 1.0f)) Wave3Played++;
            }

            if (profile.DustSheetId.Length > 0 && VfxLibrary.TryGetPlayable(profile.DustSheetId, out VfxCatalogEntry dust))
            {
                FlipbookEffect fx = _flipbooks.Get(point + n * 0.05f, Quaternion.identity);
                if (fx != null)
                {
                    fx.Play(dust, FlipbookEffect.Facing.Camera, profile.DustSize > 0f ? profile.DustSize : dust.WorldSizeMetres, Color.white);
                    Played++;
                    Wave3Played++;
                }
            }
        }

        private bool PlayGroundCard(string id, Vector3 point)
        {
            if (!_ready || !VfxLibrary.TryGetPlayable(id, out VfxCatalogEntry row))
            {
                return false;
            }

            FlipbookEffect fx = _flipbooks.Get(point, Quaternion.identity);
            if (fx == null)
            {
                return false;
            }

            fx.Play(row, FlipbookEffect.Facing.Camera, row.WorldSizeMetres, Color.white);
            Played++;
            Wave2Played++;
            return true;
        }

        private static Vector3 PlayerFeet(Vector3 fallback)
        {
            DD.Prototype.PlayerSystem.PlayerRig rig = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
            return rig != null ? rig.transform.position : fallback;
        }

        /// <summary>
        /// WP-019b · A flesh hit: the blood layer (directional splash + droplets + marks); the
        /// plain flesh sheet when the blood layer is absent. A killing hit returns true without
        /// drawing — the death path (<see cref="PlayZombieDeath"/>) carries the kill's blood.
        /// </summary>
        public bool PlayFleshHit(Vector3 point, Vector3 normal, Vector3 shotDirection, bool critical, bool killed)
        {
            // AP037 owns the TD flesh layer (droplets, stain) only after its complete asset set is warmed. User 10.10: the brush drops
            // alone left a hit unreadable from the TD camera - the blood splash card draws with them (a kill: the death path).
            if (FeelBrushVfxSystem.OwnsTdLayers)
            {
                if (!killed && _ready && !AuditMute) PlayTdHit(point, critical);
                return true;
            }
            if (!_ready || AuditMute)
            {
                return false;
            }

            if (_blood.IsReady)
            {
                return killed || _blood.PlayHit(point, normal, shotDirection, critical ? BloodSystem.HitClass.Critical : BloodSystem.HitClass.Normal);
            }

            return PlayBodyImpact(false, point, normal, critical ? 1.3f : 1f);
        }

        /// <summary>
        /// User 10.10 / AD 11.10 M2: a top-down hit must read at the hit point, far out too. The brush drops are 2–8 cm and the
        /// splash card lay along the shot normal (edge-on to the TD camera, dark red on a dark zombie): a camera-facing flesh impact
        /// (<see cref="TdHitCardMetres"/>, the lighter AP-010 sheet) pulled <see cref="TdHitTowardCamera"/> toward the camera, and a
        /// warm flash for <see cref="TdHitFlashSeconds"/> fading over its last 60 % (the fireball sheet's first frame held, HDR - the card
        /// that made the TD blast flash read) - the flash is off with ReduceFlashes.
        /// </summary>
        public const float TdHitCardMetres = .7f, TdHitFlashMetres = .5f, TdHitFlashSeconds = .1f, TdHitTowardCamera = .2f;
        /// <summary>WP-101 (AD r1 M5): the hit flash in the #FFF1C8 family (was (1, .78, .5) × 6 - its bloom painted the barrels
        /// saturated yellow).</summary>
        public static readonly Color TdHitFlashTint = new Color(1f, .95f, .82f) * 4f;
        /// <summary>The flesh card lifted so it reads on a dark zombie (a luminance step, not only a hue).</summary>
        public static readonly Color TdHitCardTint = new Color(1.4f, .62f, .5f);
        /// <summary>
        /// WP-101 (R4, AP-041): the photographic flesh sheet is painted in the blood band already (mean linear ≈ 0.32 / 0.06 / 0.06),
        /// so it only takes a luminance lift, not the AP-010 recolour (which would push it darker on a dark zombie). AD calibrates.
        /// </summary>
        public static readonly Color TdHitCardTintAp041 = new Color(1.35f, 1.15f, 1.15f);
        public int TdHitCards { get; private set; }
        public int TdHitFlashes { get; private set; }
        private void PlayTdHit(Vector3 point, bool critical)
        {
            Vector3 at = point + (FeedbackCameraRef.TryGetPosition(out Vector3 cam) ? (cam - point).normalized : Vector3.up) * TdHitTowardCamera;
            // WP-101 (R4, AP-041): the photographic flesh sheet first (only the card's texture changes; the FeelBrush flow is as before).
            bool ap041 = VfxLibrary.TryGetPlayable(VfxIds.Ap041ImpactFlesh, out VfxCatalogEntry blood);
            if (ap041 || VfxLibrary.TryGetPlayable(VfxIds.ImpactFlesh, out blood) || VfxLibrary.TryGetPlayable(VfxIds.BloodSplash01, out blood))
            {
                FlipbookEffect card = _flipbooks.Get(at, Quaternion.identity);
                if (card != null) { card.Play(blood, FlipbookEffect.Facing.Camera, TdHitCardMetres * (critical ? 1.3f : 1f), ap041 ? TdHitCardTintAp041 : TdHitCardTint, 1f, .3f); Played++; TdHitCards++; }
            }
            if (GameFeelSettings.FlashScale >= .99f && VfxLibrary.TryGetPlayable(VfxIds.Explosion, out VfxCatalogEntry flash))
            {
                FlipbookEffect fx = _flipbooks.Get(at, Quaternion.identity);
                if (fx != null)
                {
                    fx.Play(flash, FlipbookEffect.Facing.Camera, TdHitFlashMetres * (critical ? 1.3f : 1f), TdHitFlashTint, 1f, .6f, TdHitFlashSeconds);   // bright, then fades over the last 60 %
                    fx.HoldFrame(0); Played++; TdHitFlashes++;
                }
            }
        }

        /// <summary>Family 2: muzzle sheet parented to the muzzle (4 used cells of 16, manifest known issue 4).</summary>
        // WP-101 (sonra-r2 GC gate): the diagnostic note named the weapon with Enum.ToString - a fresh string per shot
        private static readonly string[] s_classicNames = new string[16];
        private static string ClassicName(DD.Prototype.Core.Config.ClassicWeaponId id)
        {
            int i = (int)id;
            if (i < 0 || i >= s_classicNames.Length) return "classic";
            return s_classicNames[i] ?? (s_classicNames[i] = id.ToString());
        }

        public bool PlayMuzzle(Transform muzzle, float scale)
        {
            if (!_ready || muzzle == null || !VfxLibrary.TryGetPlayable(VfxIds.Muzzle, out VfxCatalogEntry row))
            {
                return false;
            }

            // WP-073 B6: a Special's muzzle is ArcSystem's discharge ring / GelSystem's nozzle curtain (from Fired); true = handled,
            // so the director does not fall back to the WP-002 card either (it played the rifle card + heat + a casing before).
            DD.Prototype.Weapons.WeaponController holder = DD.Prototype.Weapons.WeaponRegistry.ActiveController;
            if (holder != null && holder.SpecialWeaponActive)
            {
                LastMuzzleNote = "special";
                return true;
            }

            FlipbookEffect fx = _flipbooks.Get(muzzle.position, muzzle.rotation, muzzle);
            if (fx == null)
            {
                return false;
            }

            _muzzleCards[_muzzleCardNext] = fx;
            _muzzleParents[_muzzleCardNext] = muzzle;
            _muzzleCardNext = (_muzzleCardNext + 1) % _muzzleCards.Length;

            // WP-047 B: A / B / C variant per classic weapon (DEC-083); a Special in hand keeps A.
            MuzzleVariant variant = MuzzleVariant.A;
            DD.Prototype.Weapons.WeaponController active = DD.Prototype.Weapons.WeaponRegistry.ActiveController;
            if (active != null && active.Classic != null && !active.SpecialWeaponActive)
            {
                variant = VfxEventBinder.VariantFor(active.Classic.Id);
            }

            LastMuzzleVariant = variant;
            LastMuzzleRowId = row.Id;
            MuzzlePlays++;
            AddHeat(variant, muzzle);
            // WP-073 B1: the class profile drives the flash, the light, the smoke and the casing (a Special keeps A).
            VfxWeaponProfile profile = ProfileOverrideForTests ?? VfxWeaponProfiles.Active();
            bool firstPerson = DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            // sg_1 pump / sg_2 break-open shells come from the mechanism event (PlayPumpCasing), never the flash.
            if (active == null || active.Classic == null || !MechanismEjects(active.Classic.Id))
                EjectCasing(variant, muzzle, profile);
            LastMuzzleNote = active == null ? "no-active" : active.Classic == null ? "no-classic" : active.SpecialWeaponActive ? "special" : ClassicName(active.Classic.Id);
            float size = row.WorldSizeMetres * Mathf.Max(0.5f, scale);
            // Reduce flashes: the sheet at × 0.4 (the light holds a steady 0.8 in MuzzleSystem).
            Color tint = GameFeelSettings.FlashScale < 0.99f ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            if (profile != null)
            {
                // §1.1 absolute size: the stretched quad's long axis = the table length (AD r4 M2); FP keeps its 0.85 bloom.
                size = profile.FlashLength > 0f
                    ? profile.FlashLength / Mathf.Max(1f, Mathf.Max(profile.FlashStretch.x, profile.FlashStretch.y))
                    : size * profile.FlashScale;
                size *= firstPerson ? 0.85f : 1f;
                tint = new Color(tint.r * profile.FlashIntensity, tint.g * profile.FlashIntensity, tint.b * profile.FlashIntensity, tint.a);
                // WP-101 (R4, AP-041): top-down the class's photographic flash (and its smoke card) replaces the AP-010 sheet;
                // FP / TP and the launcher keep the sheet below.
                if (!PlayAp041Muzzle(fx, muzzle, profile, tint, out bool smokeCard))
                {
                    fx.Play(row, FlipbookEffect.Facing.Camera, size, tint, 1f, 0f, Mathf.Max(0.05f, row.DurationSeconds * profile.FlashDurationScale));
                    if (profile.FlashIntensity < 0.999f) fx.SetEmissiveScale(profile.FlashIntensity);
                    fx.SetStretch(profile.FlashStretch.x, profile.FlashStretch.y);
                }

                if (profile.Class == VfxWeaponClass.Shotgun)
                {
                    _binder?.SchedulePumpSmoke(muzzle.position - muzzle.forward * 0.35f + Vector3.up * 0.05f, -muzzle.forward + Vector3.up * 0.6f, Time.time);
                }

                if (_muzzleSystem == null)
                {
                    _muzzleSystem = MuzzleSystem.Ensure();
                }

                _muzzleSystem?.Play(muzzle, profile, firstPerson, !smokeCard);
                // M-60 (direction §1.1 rl_1): the backblast from the tube's rear — the Muzzle socket's forward runs down the barrel.
                if (profile.Class == VfxWeaponClass.Launcher && ExplosionDirector.Instance != null)
                {
                    Vector3 rear = muzzle.position - muzzle.forward * ExplosionDirector.TubeRearFromMuzzle;
                    if (firstPerson) rear = DD.Prototype.CameraSystem.CameraView.ViewmodelToWorld(rear);
                    ExplosionDirector.Instance.PlayBackblast(rear, -muzzle.forward);
                }

                // FP: the barrel points away from the eye, so a quad aligned along it is edge-on (a 4 px sliver — AD B1
                // MUST 5): in first person the flash is a pure camera-facing bloom at 0.85 scale; elsewhere +u runs
                // down the barrel (AD M5).
                if (!firstPerson)
                {
                    fx.SetAlign(muzzle.forward, true);
                }
                LastMuzzleLifeSeconds = fx.LifeSeconds;
                Played++;
                return true;
            }

            switch (variant)
            {
                case MuzzleVariant.B:
                    fx.Play(row, FlipbookEffect.Facing.Camera, size * 0.75f, tint, 1f, 0f, Mathf.Max(0.05f, row.DurationSeconds * 0.6f));
                    break;
                case MuzzleVariant.C:
                    fx.Play(row, FlipbookEffect.Facing.Camera, size * 1.25f, tint);
                    fx.SetStretch(1f, 1.3f);
                    _binder?.SchedulePumpSmoke(muzzle.position - muzzle.forward * 0.35f + Vector3.up * 0.05f, -muzzle.forward + Vector3.up * 0.6f, Time.time);
                    break;
                default:
                    fx.Play(row, FlipbookEffect.Facing.Camera, size, tint);
                    fx.SetStretch(1.35f, 1f);
                    break;
            }

            fx.SetAlign(muzzle.forward, true);   // AD M5: the v003 cone's +u points down the barrel, not a sphere
            LastMuzzleLifeSeconds = fx.LifeSeconds;
            Played++;
            return true;
        }

        // ------------------------------------------------------------------ WP-047 wave 3

        public static readonly Color ClawTargetTint = new Color(0.85f * 1.4f, 0.82f * 1.4f, 0.75f * 1.4f, 1f);

        /// <summary>Block tint that lands the card on <see cref="ClawTargetTint"/> after the manifest tint multiplies it.</summary>
        public static Color ClawTintFor(VfxCatalogEntry row)
        {
            Color m = row != null ? row.DefaultTint : Color.white;
            return new Color(ClawTargetTint.r / Mathf.Max(0.05f, m.r), ClawTargetTint.g / Mathf.Max(0.05f, m.g), ClawTargetTint.b / Mathf.Max(0.05f, m.b), 1f);
        }


        /// <summary>attack_commit: the swipe card at the attack socket, +u along the strike (dark tint from the manifest).</summary>
        public bool PlayClawSwipe(Vector3 position, Vector3 direction, DD.Prototype.Core.Config.EnemyTier tier)
        {
            if (!_ready || !VfxLibrary.TryGetPlayable(VfxIds.ClawSwipe, out VfxCatalogEntry row))
            {
                return false;
            }

            FlipbookEffect fx = _flipbooks.Get(position, Quaternion.identity);
            if (fx == null)
            {
                return false;
            }

            float scale = tier >= DD.Prototype.Core.Config.EnemyTier.Elite ? 1.3f : tier == DD.Prototype.Core.Config.EnemyTier.Special ? 1.1f : 1f;
            // AD M3 (WP-047E): the manifest tint (0.22, 0.20, 0.19) is a dark streak that vanished on dark bodies; the card is driven
            // to (0.85, 0.82, 0.75) × 1.4 through the block tint (which multiplies the manifest tint), alpha 0.8.
            fx.Play(row, FlipbookEffect.Facing.Camera, row.WorldSizeMetres * scale, ClawTintFor(row), 0.8f);
            fx.SetAlign(direction, true);
            Wave3Played++;
            return true;
        }

        /// <summary>spawn_rise (off-screen by DEC-081): ground-breaking dust at the feet, sized from the manifest (1.76 × 1.1 m).</summary>
        public const float SpawnDustLift = 0.5f;

        public bool PlaySpawnDust(Vector3 feet, Vector3 facing)
        {
            if (!_ready || !VfxLibrary.TryGetPlayable(VfxIds.SpawnDust, out VfxCatalogEntry row))
            {
                return false;
            }

            // AD M2 (WP-047): the card's bottom edge on the ground plane read as a straight cut (the flipbook shader has no depth
            // fade); the cloud rises with the body, so the card sits half a metre up — the ground break is the body's own dust.
            FlipbookEffect fx = _flipbooks.Get(feet + Vector3.up * SpawnDustLift, Quaternion.identity);
            if (fx == null)
            {
                return false;
            }

            fx.Play(row, FlipbookEffect.Facing.CameraYaw, row.WorldSizeMetres, Color.white, 1f, 0.3f);
            Wave3Played++;
            return true;
        }

        /// <summary>Foot event: a small dust card at the foot, surface-tinted (earth / stone / wood; none on water — the ring plays there).</summary>
        public bool PlayDustStep(Vector3 point, Vector3 normal, Vector3 forward, SurfaceKind surface)
        {
            if (!_ready || AuditMute)
            {
                return false;
            }

            if (surface == SurfaceKind.Water)
            {
                if (!VfxLibrary.TryGetPlayable(VfxIds.WaterSplashRing, out VfxCatalogEntry ring)) return false;
                FlipbookEffect rfx = _flipbooks.Get(point + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
                if (rfx == null) return false;
                rfx.Play(ring, FlipbookEffect.Facing.Fixed, ring.WorldSizeMetres * 0.35f, Color.white);
                LastFootstepEffectId = VfxIds.WaterSplashRing;
                Wave3Played++;
                return true;
            }

            if (!VfxLibrary.TryGetPlayable(VfxIds.DustStep, out VfxCatalogEntry row))
            {
                return false;
            }

            FlipbookEffect fx = _flipbooks.Get(point + normal * 0.02f, Quaternion.identity);
            if (fx == null)
            {
                return false;
            }

            // The sheet's manifest default (earth) rides in the property block; the per-surface tint replaces it (ratio).
            Color tint = surface == SurfaceKind.Wood || surface == SurfaceKind.Paper ? DustWoodTint : surface == SurfaceKind.Dirt || surface == SurfaceKind.Foliage ? DustEarthTint : DustStoneTint;
            fx.Play(row, FlipbookEffect.Facing.CameraYaw, row.WorldSizeMetres, Replace(tint, row.DefaultTint));
            LastFootstepEffectId = VfxIds.DustStep;
            Wave3Played++;
            return true;
        }

        /// <summary>Reload: the dropped magazine card lands under the player (+ a small bounce-dust puff).</summary>
        public bool PlayMagDrop()
        {
            if (!_ready || _decals == null || !VfxLibrary.TryGetPlayable(VfxIds.MagDrop, out VfxCatalogEntry row))
            {
                return false;
            }

            DD.Prototype.PlayerSystem.PlayerRig rig = DD.Prototype.PlayerSystem.PlayerRegistry.Primary;
            if (rig == null)
            {
                return false;
            }

            Vector3 at = rig.transform.position + rig.transform.right * 0.35f + rig.transform.forward * 0.2f;
            if (!Physics.Raycast(at + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 2.5f, (1 << 0) | (1 << LayerMask.NameToLayer("Environment")), QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            DecalEffect card = _decals.Get(hit.point, Quaternion.LookRotation(hit.normal, rig.transform.forward));
            if (card == null)
            {
                return false;
            }

            card.Play(row, row.WorldSizeMetres, DecalSecondsEnvironment, 1f, Color.white, Random.Range(0f, 360f));
            DecalsPlayed++;
            if (VfxLibrary.TryGetPlayable(VfxIds.MagDropDust, out VfxCatalogEntry dust))
            {
                FlipbookEffect fx = _flipbooks.Get(hit.point + Vector3.up * 0.03f, Quaternion.identity);
                if (fx != null) fx.Play(dust, FlipbookEffect.Facing.Camera, dust.WorldSizeMetres, Replace(DustStoneTint, dust.DefaultTint));
            }

            Wave3Played++;
            return true;
        }

        /// <summary>Shotgun pump: a small smoke puff venting from the port 0.35 s after the shot.</summary>
        public bool PlayPumpSmoke(Vector3 anchor, Vector3 direction)
        {
            if (!_ready || _smoke == null)
            {
                return false;
            }

            AtlasBurstEffect puff = _smoke.Get(anchor, Quaternion.LookRotation(direction.sqrMagnitude > 1e-4f ? direction : Vector3.up));
            if (puff == null)
            {
                return false;
            }

            puff.Play(2, 0.12f, 0.2f, 0.8f);
            Wave3Played++;
            return true;
        }

        /// <summary>A block tint that lands on <paramref name="wanted"/> when the sheet's manifest default multiplies in (FlipbookEffect.Play).</summary>
        private static Color Replace(Color wanted, Color manifestDefault)
        {
            return new Color(wanted.r / Mathf.Max(0.01f, manifestDefault.r), wanted.g / Mathf.Max(0.01f, manifestDefault.g), wanted.b / Mathf.Max(0.01f, manifestDefault.b), 1f);
        }

        private static readonly Color DustEarthTint = new Color(0.55f, 0.48f, 0.40f, 1f);
        private static readonly Color DustStoneTint = new Color(0.58f, 0.58f, 0.56f, 1f);
        private static readonly Color DustWoodTint = new Color(0.62f, 0.55f, 0.44f, 1f);

        // ------------------------------------------------------------------ explosion

        /// <summary>Family 3 (WP-073 B3): the layered blast is <see cref="ExplosionDirector"/>'s (direction §1.5); the audit mask passes through.</summary>
        public bool PlayExplosion(Vector3 origin, float radius, ExplosionSource source = ExplosionSource.Small)
        {
            if (!_ready || AuditMute)
            {
                return false;
            }

            return ExplosionDirector.Instance != null && ExplosionDirector.Instance.Play(origin, radius, AuditExplosionMask, source);
        }

        // ------------------------------------------------------------------ armour break

        /// <summary>Family 4: armour impact sheet + atlas fragments + lacquer splinter on the ground under the hit.</summary>
        public bool PlayArmorBreak(Vector3 point, Vector3 normal, Vector3 groundPoint, float scale, Vector3 shotDirection)
        {
            if (!_ready || AuditMute)
            {
                return false;
            }

            // WP-047H (AP-010B v004, AD WP-019 MUST): the asymmetric break sheet — one dominant shard + two chips leave along +u,
            // rolled to the blood card's splash direction (60 % shot + 40 % bounce) so the plate breaks away from the shot;
            // the plain armour sheet ×1.3 stays as the fallback when the v004 row is absent.
            bool any = PlayArmorBreakSheet(point + normal * ImpactSurfaceLift, BloodSystem.SplashDirection(normal, shotDirection), scale)
                       || PlayFlipbook(VfxIds.ImpactArmor, point + normal * ImpactSurfaceLift, FlipbookEffect.Facing.Camera, Quaternion.identity, scale * 1.3f, Color.white, 1f, 0f);
            // Fragments come from the WP-017 plate hook (PlayArmorFragments, at the plate anchor);
            // the hit path draws the sheet at the impact and the splinter under the body.
            PlayDecal(VfxIds.DecalLacquerSplinter, groundPoint, Vector3.up, 0f, DecalSecondsSplinter, 0.7f, Color.white);
            // WP-019b: the flesh under the plate bleeds too — kept below the critical level so the plate loss still reads (brief 3).
            if (_blood.IsReady && !FeelBrushVfxSystem.OwnsTdLayers)
            {
                any |= _blood.PlayHit(point, normal, shotDirection, BloodSystem.HitClass.ArmorBreak);
            }

            return any;
        }

        private bool PlayArmorBreakSheet(Vector3 position, Vector3 direction, float scale)
        {
            if (!VfxLibrary.TryGetPlayable(VfxIds.ArmorBreak, out VfxCatalogEntry row))
            {
                return false;
            }

            FlipbookEffect fx = _flipbooks.Get(position, Quaternion.identity);
            if (fx == null)
            {
                return false;
            }

            fx.Play(row, FlipbookEffect.Facing.Camera, row.WorldSizeMetres * Mathf.Max(0.25f, scale), Color.white, 1f, 0f);
            fx.SetAlign(direction, true);
            Played++;
            ArmorBreakSheets++;
            return true;
        }

        /// <summary>WP-017 plate hook (ArmorVisualBinder.AnyBroken): atlas fragments at the plate anchor.</summary>
        public bool PlayArmorFragments(Vector3 anchor, Vector3 normal)
        {
            if (!_ready || _fragments == null)
            {
                return false;
            }

            AtlasBurstEffect burst = _fragments.Get(anchor, Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up));
            if (burst == null)
            {
                return false;
            }

            burst.Play(10, 0.02f, 0.06f, 0.9f);
            Played++;
            return true;
        }

        // ------------------------------------------------------------------ death

        /// <summary>
        /// Family 5: death sheet at the body's feet (origin_uv 0.5/0.25). WP-019b: the sheet
        /// steps to alpha 0.35 after 0.28 s and shrinks/dims when the camera is within 1.5 m
        /// (readability, brief 4); the blood layer adds two cards, droplets and the growing
        /// pool. Without the blood layer the WP-019 ground splat stays.
        /// </summary>
        public bool PlayZombieDeath(Vector3 feet, Vector3 hitDirection, bool heavy, Vector3 hitPoint, Vector3 normal, FeedbackWeight weight = FeedbackWeight.Standard)
        {
            if (FeelBrushVfxSystem.OwnsTdLayers) return true;
            if (!_ready || AuditMute)
            {
                return false;
            }

            // AP-010B wave 2: Special / Ultra kills get their own sheet (replaces A6, manifest ledger); Elite = heavy A6.
            string tierSheet = weight == FeedbackWeight.Special ? VfxIds.DeathSpecial : weight == FeedbackWeight.Ultra ? VfxIds.DeathUltra : null;
            if (tierSheet != null && VfxLibrary.TryGetPlayable(tierSheet, out VfxCatalogEntry tierRow))
            {
                FlipbookEffect tfx = _flipbooks.Get(feet, Quaternion.identity);
                if (tfx != null)
                {
                    tfx.Play(tierRow, FlipbookEffect.Facing.Camera, tierRow.WorldSizeMetres, Color.white);
                    tfx.SetAlphaStep(DeathSheetAlphaStepSeconds, DeathSheetAlphaStepTo);
                    Played++;
                    Wave2Played++;
                }

                bool blood = _blood.IsReady && _blood.PlayDeath(feet, hitPoint, normal, hitDirection, heavy);
                return tfx != null || blood;
            }

            float scale = heavy ? 1.15f : 1f;
            float alphaCap = 1f;
            if (FeedbackCameraRef.TryGetPosition(out Vector3 camera) && Vector3.Distance(camera, feet) <= DeathSheetNearCameraMetres
                && VfxLibrary.TryGetPlayable(VfxIds.ZombieDeath, out VfxCatalogEntry deathRow) && deathRow.WorldSizeMetres > 0f)
            {
                scale = DeathSheetNearSize / deathRow.WorldSizeMetres;
                alphaCap = DeathSheetNearAlphaCap;
            }

            bool any = false;
            // AP-010B A6 death splashes 01–03 (3.0 m, origin (0.5, 0.08), painted in band) rotate; the v002 grey sheet is the recoloured fallback.
            VfxCatalogEntry row = null;
            if (BloodSlots.Resolve(BloodSlots.DeathSheet, s_deathSheets) > 0)
            {
                row = s_deathSheets[(_deathToggle++) % s_deathSheets.Count];
            }

            if (row != null)
            {
                FlipbookEffect fx = _flipbooks.Get(feet, Quaternion.identity);
                if (fx != null)
                {
                    bool authored = row.Family == VfxFamily.Blood;
                    Color deathTint = authored ? BloodPalette.AuthoredSheetTint : _blood.IsReady ? BloodPalette.TintFor(row, BloodPalette.Fresh, BloodPalette.DeathSheetCalibration) : Color.white;
                    fx.Play(row, FlipbookEffect.Facing.Camera, authored ? row.WorldSizeMetres * (heavy ? 1.15f : 1f) : row.WorldSizeMetres * scale, deathTint, alphaCap, 0f);
                    if (authored) fx.SetAlign(hitDirection, true);
                    fx.SetAlphaStep(DeathSheetAlphaStepSeconds, DeathSheetAlphaStepTo);
                    Played++;
                    any = true;
                }
            }

            if (_blood.IsReady)
            {
                any |= _blood.PlayDeath(feet, hitPoint, normal, hitDirection, heavy);
                return any;
            }

            string splat = (_splatToggle++ & 1) == 0 ? VfxIds.DecalZombieSplat01 : VfxIds.DecalZombieSplat02;
            float spin = hitDirection.sqrMagnitude > 1e-4f ? Mathf.Atan2(hitDirection.x, hitDirection.z) * Mathf.Rad2Deg : 0f;
            PlayDecal(splat, feet, Vector3.up, spin, DecalSecondsSplat, SplatAlphaCap, Color.white);
            return any;
        }

        // ------------------------------------------------------------------ internals

        // ------------------------------------------------------------------ WP-101 (R4, AP-041) · photographic set

        /// <summary>Top-down flash HDR gain over the premultiplied white core (bloom lifts it; ReduceFlashes keeps × 1). AD calibrates.</summary>
        public const float Ap041MuzzleHdr = 2f;
        /// <summary>The flash never lives under two 60 Hz frames (SMG: 50 ms × 0.6 → 33 ms, still the shortest of the classes).</summary>
        public const float Ap041MinFlashSeconds = 2f / 60f;
        /// <summary>The smoke card is sized against the rifle's smoke end (0.60 m → the manifest 0.8 m card) and capped at alpha 0.5 (AP-041 contract: dense cloud over the player ≤ 0.5).</summary>
        public const float Ap041SmokeReferenceEnd = 0.60f, Ap041SmokeReferenceAlpha = 0.40f, Ap041SmokeAlphaCap = 0.5f;
        /// <summary>One smoke card per this many seconds (Performance / High / Cinematic): held fire keeps ≤ 2 / 4 / 7 cards of the shared 48-card pool.</summary>
        public static float Ap041SmokeInterval => VfxTier.Pick(0.35f, 0.2f, 0.16f);

        /// <summary>True when the AP-041 impact sheets are imported (every camera mode plays them).</summary>
        public bool Ap041ImpactsReady => _ap041Impacts;
        /// <summary>True when the class's three TD flashes are imported.</summary>
        public bool Ap041MuzzleReady(Ap041MuzzleClass muzzleClass) => muzzleClass != Ap041MuzzleClass.None && _ap041Muzzle[(int)muzzleClass] != null;

        /// <summary>Resolves the AP-041 rows once (configure): a class / set counts only when all of its variants are playable.</summary>
        private void ResolveAp041()
        {
            for (int c = 0; c < _ap041Muzzle.Length; c++)
            {
                VfxCatalogEntry[] rows = new VfxCatalogEntry[Ap041Vfx.MuzzleVariants];
                bool all = true;
                for (int v = 0; v < rows.Length; v++)
                {
                    all &= VfxLibrary.TryGetPlayable(Ap041Vfx.MuzzleId((Ap041MuzzleClass)c, v), out rows[v]);
                }

                _ap041Muzzle[c] = all ? rows : null;
            }

            _ap041Smoke = VfxLibrary.TryGetPlayable(VfxIds.Ap041MuzzleSmoke, out VfxCatalogEntry smoke) ? smoke : null;
            _ap041Impacts = VfxLibrary.TryGetPlayable(VfxIds.Ap041ImpactStone, out _) && VfxLibrary.TryGetPlayable(VfxIds.Ap041ImpactWood, out _)
                            && VfxLibrary.TryGetPlayable(VfxIds.Ap041ImpactEarth, out _) && VfxLibrary.TryGetPlayable(VfxIds.Ap041ImpactMetal, out _)
                            && VfxLibrary.TryGetPlayable(VfxIds.Ap041ImpactFlesh, out _);
            for (int s = 0; s < _ap041DecalSet.Length; s++)
            {
                bool all = true;
                for (int v = 0; v < Ap041Vfx.DecalVariants; v++) all &= VfxLibrary.TryGetPlayable(Ap041Vfx.DecalId(s, v), out _);
                _ap041DecalSet[s] = all;
            }
        }

        /// <summary>
        /// WP-101 (R4, AP-041) · The top-down flash. The class's three TD variants take turns (a per-class counter); the quad's
        /// long axis is the §1.1 table length (<see cref="VfxWeaponProfile.FlashLength"/>, Wp073FlashSizeTests) and its height the
        /// manifest [w, h] ratio - no stretch, so the pivot (origin u ≈ 0.11, the muzzle end of the cone) stays on the socket and
        /// +u runs down the barrel (the caller aligns it). Life = the manifest 50 ms × the class's duration scale, floored at
        /// <see cref="Ap041MinFlashSeconds"/>. ReduceFlashes: the caller's 0.4 fade, no HDR gain. The smoke card (one per
        /// <see cref="Ap041SmokeInterval"/>) stands in for the particle puff: <paramref name="smokeHandled"/> true = no puff.
        /// False = not top-down, the launcher, or the class's rows are not imported (the AP-010 sheet plays).
        /// </summary>
        private bool PlayAp041Muzzle(FlipbookEffect fx, Transform muzzle, VfxWeaponProfile profile, Color tint, out bool smokeHandled)
        {
            smokeHandled = false;
            if (CameraModeService.Current != CameraMode.TopDown) return false;
            Ap041MuzzleClass cls = Ap041Vfx.MuzzleClassOf(profile.Class);
            VfxCatalogEntry[] rows = cls != Ap041MuzzleClass.None ? _ap041Muzzle[(int)cls] : null;
            if (rows == null) return false;

            int v = _ap041MuzzleNext[(int)cls];
            _ap041MuzzleNext[(int)cls] = (v + 1) % Ap041Vfx.MuzzleVariants;
            VfxCatalogEntry tdRow = rows[v];
            float hdr = GameFeelSettings.FlashScale < 0.99f ? 1f : Ap041MuzzleHdr;
            float life = Mathf.Max(Ap041MinFlashSeconds, tdRow.DurationSeconds * profile.FlashDurationScale);
            float length = profile.FlashLength > 0f ? profile.FlashLength : tdRow.WorldSizeMetres;
            fx.Play(tdRow, FlipbookEffect.Facing.Camera, length, new Color(tint.r * hdr, tint.g * hdr, tint.b * hdr, tint.a), 1f, 0f, life);
            LastMuzzleRowId = tdRow.Id;
            LastAp041MuzzleVariant = v;
            Ap041MuzzlePlays++;
            Played++;   // the TD flash is a flipbook like the AP-010 sheet it replaces (VfxGameFeelTests counts them)

            if (_ap041Smoke == null || profile.SmokeSeconds <= 0f) return true;
            float now = Time.time;
            if (_ap041SmokeAt >= 0f && now >= _ap041SmokeAt && now - _ap041SmokeAt < Ap041SmokeInterval) { smokeHandled = true; return true; }
            FlipbookEffect card = _flipbooks.Get(muzzle.position + muzzle.forward * 0.3f, Quaternion.identity);
            if (card == null) return true;   // review: an empty pool leaves the particle smoke to MuzzleSystem
            smokeHandled = true;
            float size = _ap041Smoke.WorldSizeMetres * Mathf.Clamp(profile.SmokeEnd / Ap041SmokeReferenceEnd, 0.75f, 1.7f);
            float alpha = Mathf.Clamp(profile.SmokeAlpha / Ap041SmokeReferenceAlpha, 0.5f, 1f);
            card.Play(_ap041Smoke, FlipbookEffect.Facing.Camera, size, new Color(1f, 1f, 1f, alpha), Ap041SmokeAlphaCap, 0.35f,
                _ap041Smoke.DurationSeconds * VfxTier.Pick(0.75f, 1f, 1.4f));
            _ap041SmokeAt = now;
            Ap041SmokeCards++;
            Played++;
            return true;
        }

        /// <summary>The impact sheet a surface plays now (AP-041 when imported, else AP-010) - probe / test readout, no variant turn.</summary>
        public string ImpactCardId(SurfaceKind surface)
        {
            LegacyImpactIdsFor(surface, out string flipbook, out _);
            return _ap041Impacts ? Ap041Vfx.ImpactIdFor(surface) : flipbook;
        }

        /// <summary>
        /// WP-101 (R4, AP-041): the photographic sheet of the same surface group in every camera mode, and the hole's four variants
        /// in turn on stone / wood / metal (dirt keeps the AP-010 earth mark; surfaces without a mark stay without). Rows not
        /// imported → the AP-010 ids. <see cref="RefineSurfaceImpact"/> still sizes the hole by calibre and drops the light-calibre
        /// stone card, whichever set drew them.
        /// </summary>
        private void ImpactIdsFor(SurfaceKind surface, out string flipbook, out string decal)
        {
            LegacyImpactIdsFor(surface, out flipbook, out decal);
            if (_ap041Impacts) flipbook = Ap041Vfx.ImpactIdFor(surface);
            int set = decal != null ? Ap041Vfx.DecalSetOf(surface) : -1;
            if (set >= 0 && _ap041DecalSet[set])
            {
                int v = _ap041DecalNext[set];
                _ap041DecalNext[set] = (v + 1) % Ap041Vfx.DecalVariants;
                decal = Ap041Vfx.DecalId(set, v);
            }
        }

        private static void LegacyImpactIdsFor(SurfaceKind surface, out string flipbook, out string decal)
        {
            switch (surface)
            {
                case SurfaceKind.Wood:
                case SurfaceKind.Foliage:
                case SurfaceKind.Paper:
                    flipbook = VfxIds.ImpactWood; decal = surface == SurfaceKind.Wood ? VfxIds.DecalImpactWood : null; return;
                case SurfaceKind.Dirt:
                case SurfaceKind.Water:
                    flipbook = VfxIds.ImpactEarth; decal = surface == SurfaceKind.Dirt ? VfxIds.DecalImpactEarth : null; return;
                case SurfaceKind.Metal:
                    flipbook = VfxIds.ImpactArmor; decal = VfxIds.DecalImpactArmor; return;
                case SurfaceKind.Flesh:
                    flipbook = VfxIds.ImpactFlesh; decal = null; return;
                default:
                    flipbook = VfxIds.ImpactStone; decal = VfxIds.DecalImpactStone; return;
            }
        }

        private bool PlayFlipbook(string id, Vector3 position, FlipbookEffect.Facing facing, Quaternion rotation, float scale, Color tint, float alphaCap, float fadeOut)
        {
            if (!VfxLibrary.TryGetPlayable(id, out VfxCatalogEntry row))
            {
                return false;
            }

            FlipbookEffect fx = _flipbooks.Get(position, rotation);
            if (fx == null)
            {
                return false;
            }

            fx.Play(row, facing, row.WorldSizeMetres * Mathf.Max(0.25f, scale), tint, alphaCap, fadeOut);
            _lastFlipbook = fx;
            Played++;
            return true;
        }

        // ------------------------------------------------------------------ WP-073 B2 · calibre refinement of a surface impact

        private FlipbookEffect _lastFlipbook, _lastSurfaceCard;
        private DecalEffect _lastDecal, _lastSurfaceDecal;
        private bool _lastSurfaceDecalSpall;   // WP-101: the last surface hole is an AP-041 sheet (hole + spall)
        private int _lastSurfaceDecalSet;      // its set (AD r1 M6: metal × 1.4)
        private int _lastSurfaceFrame = -1;
        private Vector3 _lastSurfacePoint;

        /// <summary>
        /// WP-073 B2 (direction §1.4): the projectile's Impact refines the surface impact this frame's
        /// <see cref="PlaySurfaceImpact"/> just dressed at <paramref name="point"/> — the hole takes the calibre's size
        /// (light 0.06 / medium 0.09 / heavy 0.14 m) and the wet world has no dust (K3): on stone the dust card plays
        /// only for heavy rounds (the spray and chips of <see cref="ImpactSystem"/> carry the rest). False when this frame
        /// dressed nothing there (budget, hitscan path).
        /// </summary>
        public bool RefineSurfaceImpact(Vector3 point, SurfaceKind surface, DD.Prototype.Core.Weapons.WeaponCaliber calibre)
        {
            if (_lastSurfaceFrame != Time.frameCount || (_lastSurfacePoint - point).sqrMagnitude > 1e-4f)
            {
                return false;
            }

            if (_lastSurfaceDecal != null && _lastSurfaceDecal.IsSpawned)
            {
                _lastSurfaceDecal.SetSize(_lastSurfaceDecalSpall ? Ap041Vfx.SpalledHole(ImpactSystem.HoleSize(calibre), _lastSurfaceDecalSet) : ImpactSystem.HoleSize(calibre));
            }

            if (surface == SurfaceKind.Stone && calibre != DD.Prototype.Core.Weapons.WeaponCaliber.Heavy && _lastSurfaceCard != null && _lastSurfaceCard.IsSpawned)
            {
                _lastSurfaceCard.Despawn();
            }

            _lastSurfaceFrame = -1;
            return true;
        }

        private void PlayDecal(string id, Vector3 point, Vector3 normal, float spinDegrees, float seconds, float alphaCap, Color tint)
        {
            if (_decals == null || !VfxLibrary.TryGetPlayable(id, out VfxCatalogEntry row))
            {
                return;
            }

            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
            Vector3 up = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            DecalEffect decal = _decals.Get(point, Quaternion.LookRotation(n, up));
            if (decal == null)
            {
                return;
            }

            float spin = spinDegrees != 0f ? spinDegrees : Random.Range(0f, 360f);
            decal.Play(row, row.WorldSizeMetres, seconds, alphaCap, tint, spin);
            _lastDecal = decal;
            DecalsPlayed++;
        }
    }
}
