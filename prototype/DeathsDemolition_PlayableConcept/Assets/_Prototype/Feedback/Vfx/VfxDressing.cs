using System.Collections.Generic;
using DD.Prototype.Core.Pooling;
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
        public const int DecalCapacity = 64;
        public const int FragmentBurstCapacity = 8;
        public const int SmokeBurstCapacity = 8;

        // PROTOTYPE-ONLY placement numbers (manifest usage notes where they exist).
        private const float ImpactSurfaceLift = 0.03f;
        private const float DecalSecondsEnvironment = 12f;
        private const float DecalSecondsSplat = 30f;
        private const float DecalSecondsScorch = 12f;
        private const float DecalSecondsSplinter = 8f;
        private const float DecalAlphaCap = 0.85f;
        private const float ScorchAlphaCap = 0.40f;
        private const float SplatAlphaCap = 0.70f;

        private PrototypePool<FlipbookEffect> _flipbooks;
        private PrototypePool<DecalEffect> _decals;
        private PrototypePool<AtlasBurstEffect> _fragments;
        private PrototypePool<AtlasBurstEffect> _smoke;
        private PrototypePool<AtlasBurstEffect> _sparks;
        private PrototypePool<AtlasBurstEffect> _leaves;
        private PrototypePool<AtlasBurstEffect> _paper;
        private readonly BloodSystem _blood = new BloodSystem();
        private static readonly List<VfxCatalogEntry> s_deathSheets = new List<VfxCatalogEntry>(4);
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

        /// <summary>AD M6: shock ring dust colour (h ~30°, s 0.22, v 0.62).</summary>
        private static readonly Color ShockRingDustTint = new Color(0.62f, 0.55f, 0.48f, 1f);
        private const float DeathSheetAlphaStepSeconds = 0.28f;
        private const float DeathSheetAlphaStepTo = 0.35f;
        private const float DeathSheetNearCameraMetres = 1.5f;
        private const float DeathSheetNearSize = 1.0f;
        private const float DeathSheetNearAlphaCap = 0.6f;

        public bool IsReady => _ready;
        public int Played { get; private set; }
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
                _flipbooks = PoolService.GetOrCreate(Template<FlipbookEffect>(templateRoot, "FX_AP010_Flipbook"),
                    FlipbookCapacity / 3, FlipbookCapacity, PoolExhaustedPolicy.RecycleOldest);
                _decals = PoolService.GetOrCreate(Template<DecalEffect>(templateRoot, "FX_AP010_Decal"),
                    DecalCapacity / 3, DecalCapacity, PoolExhaustedPolicy.RecycleOldest);

                if (VfxLibrary.TryGetPlayable(VfxIds.AtlasArmorBreak, out VfxCatalogEntry atlas))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010_ArmorFragments");
                    t.Configure(atlas, true, 1.5f, 3.2f, 45f);
                    _fragments = PoolService.GetOrCreate(t, 2, FragmentBurstCapacity, PoolExhaustedPolicy.RecycleOldest);
                }

                if (VfxLibrary.TryGetPlayable(VfxIds.CardSmoke, out VfxCatalogEntry smoke))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010_Smoke");
                    t.Configure(smoke, false, 0.4f, 1.2f, 30f);
                    _smoke = PoolService.GetOrCreate(t, 2, SmokeBurstCapacity, PoolExhaustedPolicy.RecycleOldest);
                }

                // AP-010B v003 wave 2: sprite-sheet bursts (spark on ricochet, leaf on foliage, paper on paper).
                if (VfxLibrary.TryGetPlayable(VfxIds.Spark, out VfxCatalogEntry spark))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Sparks");
                    t.Configure(spark, true, 2.5f, 5f, 35f);
                    _sparks = PoolService.GetOrCreate(t, 2, SparkBurstCapacity, PoolExhaustedPolicy.RecycleOldest);
                }

                if (VfxLibrary.TryGetPlayable(VfxIds.LeafBurst, out VfxCatalogEntry leaf))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Leaves");
                    t.Configure(leaf, true, 0.8f, 2f, 50f);
                    _leaves = PoolService.GetOrCreate(t, 1, LeafBurstCapacity, PoolExhaustedPolicy.RecycleOldest);
                }

                if (VfxLibrary.TryGetPlayable(VfxIds.PaperTear, out VfxCatalogEntry paper))
                {
                    AtlasBurstEffect t = Template<AtlasBurstEffect>(templateRoot, "FX_AP010B_Paper");
                    t.Configure(paper, true, 0.6f, 1.6f, 45f);
                    _paper = PoolService.GetOrCreate(t, 1, PaperBurstCapacity, PoolExhaustedPolicy.RecycleOldest);
                }
            }

            if (!_wave2Subscribed)
            {
                _wave2Subscribed = true;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponSpawned += OnSpecialWeaponSpawned;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponPickedUp += OnSpecialWeaponPickedUp;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponGroundExpired += OnSpecialWeaponGroundExpired;
                DD.Prototype.Weapons.WeaponEvents.SpecialWeaponEnded += OnSpecialWeaponEnded;
                DD.Prototype.Props.BreakableProp.Broke += OnBreakableBroke;
            }

            _blood.Configure(templateRoot, _flipbooks);
            _ready = true;
        }

        /// <summary>Per frame from the director: blood far-LOD absorb + merged mesh rebuild.</summary>
        public void Tick(float now)
        {
            if (_ready)
            {
                _blood.Tick(now);
            }
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
            if (!_ready)
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
            bool played = PlayFlipbook(fb, point + normal * ImpactSurfaceLift, FlipbookEffect.Facing.Camera, Quaternion.identity, scale, Color.white, 1f, 0f);
            if (decal != null)
            {
                PlayDecal(decal, point, normal, 0f, DecalSecondsEnvironment, DecalAlphaCap, Color.white);
            }

            return played;
        }

        /// <summary>Family 1 on a body: flesh or armour impact sheet at the hit point (no decal: the body moves). An armour hit adds the AP-010B crack dust (plate damaged, not yet broken).</summary>
        public bool PlayBodyImpact(bool armored, Vector3 point, Vector3 normal, float scale)
        {
            if (!_ready)
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
            if (!_ready)
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
                if (fx != null) { fx.Play(splash, FlipbookEffect.Facing.Camera, splash.WorldSizeMetres, Color.white); any = true; }
            }

            if (VfxLibrary.TryGetPlayable(VfxIds.WaterSplashRing, out VfxCatalogEntry ring))
            {
                FlipbookEffect fx = _flipbooks.Get(point + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
                if (fx != null) { fx.Play(ring, FlipbookEffect.Facing.Fixed, ring.WorldSizeMetres, Color.white); any = true; }
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
            PlayGroundCard(VfxIds.WeaponPickupFlash, PlayerFeet(_lastBeaconPoint));
        }

        private void OnSpecialWeaponGroundExpired()
        {
            PlayGroundCard(VfxIds.WeaponExpire, _lastBeaconPoint);
        }

        private void OnSpecialWeaponEnded()
        {
            PlayGroundCard(VfxIds.WeaponExpire, PlayerFeet(_lastBeaconPoint) + Vector3.up * 1.0f);
        }

        private void OnBreakableBroke(DD.Prototype.Props.BreakableProp prop, Vector3 point, Vector3 direction)
        {
            if (!_ready || prop == null || !VfxLibrary.TryGetPlayable(VfxIds.BreakableResidue, out VfxCatalogEntry residue) || _decals == null)
            {
                return;
            }

            // 2×2 atlas: TL ceramic / TR wood / BL straw / BR cloth — picked by the prop's name, wood when unknown.
            string name = prop.name.ToLowerInvariant();
            int cell = name.Contains("pot") || name.Contains("cerami") || name.Contains("jar") || name.Contains("lantern") ? 0 : name.Contains("straw") || name.Contains("bale") ? 2 : name.Contains("cloth") || name.Contains("banner") ? 3 : 1;
            Vector3 ground = prop.transform.position;
            DecalEffect decal = _decals.Get(ground + Vector3.up * 0.01f, Quaternion.LookRotation(Vector3.up, Vector3.forward));
            if (decal != null)
            {
                decal.Play(residue, residue.WorldSizeMetres, DecalSecondsSplat, DecalAlphaCap, Color.white, Random.Range(0f, 360f), cell);
                DecalsPlayed++;
                Wave2Played++;
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
            if (!_ready)
            {
                return false;
            }

            if (_blood.IsReady)
            {
                return killed || _blood.PlayHit(point, normal, shotDirection, critical ? BloodSystem.HitClass.Critical : BloodSystem.HitClass.Normal);
            }

            return PlayBodyImpact(false, point, normal, critical ? 1.3f : 1f);
        }

        /// <summary>Family 2: muzzle sheet parented to the muzzle (4 used cells of 16, manifest known issue 4).</summary>
        public bool PlayMuzzle(Transform muzzle, float scale)
        {
            if (!_ready || muzzle == null || !VfxLibrary.TryGetPlayable(VfxIds.Muzzle, out VfxCatalogEntry row))
            {
                return false;
            }

            FlipbookEffect fx = _flipbooks.Get(muzzle.position, muzzle.rotation, muzzle);
            if (fx == null)
            {
                return false;
            }

            fx.Play(row, FlipbookEffect.Facing.Camera, row.WorldSizeMetres * Mathf.Max(0.5f, scale), Color.white);
            fx.SetAlign(muzzle.forward, true);   // AD M5: the v003 cone's +u points down the barrel, not a sphere
            Played++;
            return true;
        }

        // ------------------------------------------------------------------ explosion

        /// <summary>Family 3: fireball sheet + ground shockwave sheet + smoke puffs + scorch decal.</summary>
        public bool PlayExplosion(Vector3 origin, float radius)
        {
            if (!_ready)
            {
                return false;
            }

            bool any = PlayFlipbook(VfxIds.Explosion, origin, FlipbookEffect.Facing.Camera, Quaternion.identity, 1f, Color.white, 1f, 0f);
            // Manifest: ring seen frontally, ground-aligned 5 m quad, 0.3 s.
            // AD M6: the ground ring reads as dust, not white fog — dust tint (s ≤ 0.25) and alpha ≤ 0.5.
            any |= PlayFlipbook(VfxIds.Shockwave, origin + Vector3.up * 0.05f, FlipbookEffect.Facing.Fixed, Quaternion.Euler(90f, 0f, 0f), 1f, ShockRingDustTint, 0.5f, 0.3f);
            if (_smoke != null)
            {
                AtlasBurstEffect puff = _smoke.Get(origin + Vector3.up * 0.6f, Quaternion.LookRotation(Vector3.up));
                if (puff != null)
                {
                    puff.Play(3, 0.8f, 1.1f, 1.5f);   // manifest: 3-5 billboards 0.8-1.2 m, 1.5 s (3: readability)
                }
            }

            PlayDecal(VfxIds.DecalScorch, origin, Vector3.up, 0f, DecalSecondsScorch, ScorchAlphaCap, Color.white);
            return any;
        }

        // ------------------------------------------------------------------ armour break

        /// <summary>Family 4: armour impact sheet + atlas fragments + lacquer splinter on the ground under the hit.</summary>
        public bool PlayArmorBreak(Vector3 point, Vector3 normal, Vector3 groundPoint, float scale, Vector3 shotDirection)
        {
            if (!_ready)
            {
                return false;
            }

            bool any = PlayFlipbook(VfxIds.ImpactArmor, point + normal * ImpactSurfaceLift, FlipbookEffect.Facing.Camera, Quaternion.identity, scale * 1.3f, Color.white, 1f, 0f);
            // Fragments come from the WP-017 plate hook (PlayArmorFragments, at the plate anchor);
            // the hit path draws the sheet at the impact and the splinter under the body.
            PlayDecal(VfxIds.DecalLacquerSplinter, groundPoint, Vector3.up, 0f, DecalSecondsSplinter, 0.7f, Color.white);
            // WP-019b: the flesh under the plate bleeds too — kept below the critical level so the plate loss still reads (brief 3).
            if (_blood.IsReady)
            {
                any |= _blood.PlayHit(point, normal, shotDirection, BloodSystem.HitClass.ArmorBreak);
            }

            return any;
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
            if (!_ready)
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

        private static void ImpactIdsFor(SurfaceKind surface, out string flipbook, out string decal)
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
            Played++;
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
            DecalsPlayed++;
        }
    }
}
