using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B2 · The surface × calibre impact layer (direction §1.4). It reads <see cref="ProjectileEvents.Impact"/>
    /// and adds what the legacy surface dressing (<see cref="VfxDressing.PlaySurfaceImpact"/>: the AP-010 card + hole, which
    /// the hitscan path still uses on its own) does not carry:
    /// <list type="bullet">
    /// <item>debris per surface — wet spray (the wet world has no dust, K3), slate chips, wood splinters and lacquer
    /// flakes, mud, sparks — counted light ×0.7 / medium ×1.0 / heavy ×1.6 and by tier ×0.4 / ×1.0 / ×1.3;</item>
    /// <item>the hole sized by calibre (0.06 / 0.09 / 0.14 m) and no stone dust card below heavy;</item>
    /// <item>a grazing hit on stone / metal / armour throws a short ember streak along the bounce;</item>
    /// <item>the impact light (#FFE0B0, 3, 2 m, 2 frames) on metal / armour — Cinematic every calibre, High heavy only;</item>
    /// <item>on bodies: the armour sparks + lacquer flake, and the exit spray of a round that passed through (gore v3).</item>
    /// </list>
    /// Two world-space particle systems (debris alpha, sparks additive) fed through <c>Emit(EmitParams)</c> — no object
    /// per impact, 0 B per hit; at most 24 / 40 / 48 impacts dressed per 0.4 s by tier. The atlas is drawn in code at
    /// start-up (direction §6-3: procedural, no credit). Rockets are B3's (<c>ExplosionDirector</c>).
    /// </summary>
    public sealed class ImpactSystem : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.Impacts");

        public const float ConcurrentWindowSeconds = 0.4f;
        public const float GrazingDot = 0.26f;   // |dir · normal| below this = a grazing hit (≈ 15°)
        public static readonly Color ImpactLightColour = new Color(1f, 0.878f, 0.69f, 1f);   // #FFE0B0
        public static readonly Color32 SprayColour = new Color32(217, 225, 230, 128);        // #D9E1E6 α 0.5
        public static readonly Color32 StoneSprayColour = new Color32(217, 225, 230, 166);   // AD B2 MUST 2: the stone spray leads (α 0.65)
        /// <summary>AD B2 MUST 1: the impact light on wet slate — 1.2 / 1.2 m (was 3 / 2 m: a clipped white disc), 2 frames, one at a time.</summary>
        public const float ImpactLightIntensity = 1.2f, ImpactLightRange = 1.2f;
        private static readonly Color32 ColdSpark = new Color32(96, 96, 96, 255);   // × the material's HDR ×4 ≈ ×1.5 (AD B2 MUST 2)
        private static readonly Color32 HotSpark = new Color32(255, 255, 255, 255);

        // Atlas cells (4 × 4).
        private const int CellStone = 0, CellSplinter = 4, CellLacquer = 6, CellMud = 8, CellSpray = 10, CellGrey = 12;

        private static ImpactSystem s_instance;
        private VfxDressing _dressing;
        private ParticleSystem _debris;
        private ParticleSystem _sparks;
        private ParticleSystem.TextureSheetAnimationModule _sheet;
        private Texture2D _atlas;
        private Texture2D _dot;
        private Material _debrisMaterial;
        private Material _sparkMaterial;
        private readonly float[] _recent = new float[48];
        private int _recentNext;

        public static ImpactSystem Instance => s_instance;

        public int Impacts { get; private set; }
        public int Skipped { get; private set; }
        public int DebrisEmitted { get; private set; }
        public int SparksEmitted { get; private set; }
        public int Ricochets { get; private set; }
        public int LightsAsked { get; private set; }
        public int ExitSprays { get; private set; }
        public int DebrisAlive => _debris != null ? _debris.particleCount : 0;
        public int SparksAlive => _sparks != null ? _sparks.particleCount : 0;
        public Texture2D Atlas => _atlas;

        /// <summary>Hole diameter by calibre (direction §1.4).</summary>
        public static float HoleSize(WeaponCaliber calibre)
        {
            switch (calibre)
            {
                case WeaponCaliber.Light: return 0.06f;
                case WeaponCaliber.Heavy:
                case WeaponCaliber.Explosive: return 0.14f;
                default: return 0.09f;
            }
        }

        public static float CalibreScale(WeaponCaliber calibre)
        {
            switch (calibre)
            {
                case WeaponCaliber.Light: return 0.7f;
                case WeaponCaliber.Heavy:
                case WeaponCaliber.Explosive: return 1.6f;
                default: return 1f;
            }
        }

        public static float TierScale => VfxTier.Pick(0.4f, 1f, 1.3f);
        public static int ConcurrentCap => VfxTier.Pick(24, 40, 48);

        /// <summary>Whether this hit lights its point (metal / armour; Cinematic every calibre, High heavy only, Performance never).</summary>
        public static bool WantsLight(SurfaceKind surface, WeaponCaliber calibre, VfxTierLevel tier)
        {
            if (surface != SurfaceKind.Metal && surface != SurfaceKind.Armor) return false;
            if (tier == VfxTierLevel.Cinematic) return true;
            return tier == VfxTierLevel.High && calibre >= WeaponCaliber.Heavy;
        }

        public static ImpactSystem Ensure(VfxDressing dressing)
        {
            if (s_instance == null)
            {
                var go = new GameObject("[ImpactSystem]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<ImpactSystem>();
            }

            s_instance._dressing = dressing;
            s_instance.Rearm();
            MuzzleSystem.Ensure();   // the impact light's pool: built at configure, not by the first heavy metal hit (≈ 200 allocations)
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
            Build();
            ApplyTier(VfxTier.Current);
            VfxTier.Changed += ApplyTier;
            SceneManager.activeSceneChanged += OnSceneChanged;
            Rearm();
        }

        private void OnDestroy()
        {
            ProjectileEvents.Impact -= OnImpact;
            VfxTier.Changed -= ApplyTier;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (s_instance == this) s_instance = null;
            if (_debrisMaterial != null) Destroy(_debrisMaterial);
            if (_sparkMaterial != null) Destroy(_sparkMaterial);
            if (_atlas != null) Destroy(_atlas);
            if (_dot != null) Destroy(_dot);
        }

        // A reset of the projectile bus (tests) drops every listener: re-subscribe on each scene change (remove-then-add).
        private void OnSceneChanged(Scene from, Scene to) => Rearm();

        private void Rearm()
        {
            ProjectileEvents.Impact -= OnImpact;
            ProjectileEvents.Impact += OnImpact;
        }

        private void OnImpact(ProjectileImpactInfo info) => Play(in info);

        // ------------------------------------------------------------------ the matrix

        /// <summary>Dresses one projectile impact (public for the capture agent and tests; the bus calls it).</summary>
        public void Play(in ProjectileImpactInfo info)
        {
            using var marker = s_marker.Auto();
            if (info.Kind >= ProjectileKind.Rocket || info.Caliber == WeaponCaliber.Explosive || VfxDressing.AuditMute)
            {
                return;   // B3: the rocket's contact and blast belong to the explosion director; B6: the Special orb / gel
            }

            Vector3 n = info.Normal.sqrMagnitude > 1e-4f ? info.Normal.normalized : -info.Direction.normalized;
            Vector3 d = info.Direction.sqrMagnitude > 1e-4f ? info.Direction.normalized : -n;

            if (info.Flesh)
            {
                PlayBody(in info, n, d);
                return;
            }

            _dressing?.RefineSurfaceImpact(info.Position, info.Surface, info.Caliber);
            if (!TakeSlot())
            {
                Skipped++;
                return;
            }

            Impacts++;
            float k = CalibreScale(info.Caliber) * TierScale;
            Vector3 p = info.Position + n * 0.02f;
            Vector3 bounce = Vector3.Reflect(d, n);
            bool grazing = Mathf.Abs(Vector3.Dot(d, n)) < GrazingDot;
            switch (info.Surface)
            {
                case SurfaceKind.Stone:
                    // Wet stone reads cold (AD B2 MUST 2): the spray leads, the two sparks are short (≈ 0.04 m) and dim.
                    Spray(p, n, Count(8, k), 1.5f, 3f, 0.03f, 0.05f, 0.15f, 0.25f, StoneSprayColour);
                    Debris(CellStone, 4, p, n, Count(4, k), 0.6f, 2f, 4f, 0.02f, 0.045f, 0.5f, 0.9f);
                    Sparks(p, Vector3.Lerp(n, bounce, 0.5f), Count(2, k), 1f, 1.4f, 0.1f, 0.18f, 0.55f, ColdSpark);
                    break;
                case SurfaceKind.Wood:
                    Debris(CellSplinter, 2, p, n, Count(8, k), 0.7f, 2f, 4f, 0.035f, 0.08f, 0.5f, 0.9f);
                    Debris(CellLacquer, 2, p, n, Count(2, k), 0.7f, 1.5f, 3f, 0.025f, 0.045f, 0.5f, 0.8f);
                    break;
                case SurfaceKind.Foliage:
                    Spray(p, n, Count(4, k), 1f, 2.5f, 0.02f, 0.04f, 0.2f, 0.3f);
                    break;
                case SurfaceKind.Water:
                    Spray(p, Vector3.up, Count(6, k), 2f, 3.5f, 0.03f, 0.06f, 0.25f, 0.4f);
                    break;
                case SurfaceKind.Metal:
                    Sparks(p, Vector3.Lerp(n, bounce, 0.6f), Count(10, k), 4f, 8f, 0.15f, 0.3f);
                    break;
                case SurfaceKind.Armor:
                    Sparks(p, Vector3.Lerp(n, bounce, 0.6f), Count(3, k), 3f, 6f, 0.12f, 0.25f);
                    Debris(CellLacquer, 2, p, n, Count(1, k), 0.6f, 1.5f, 3f, 0.025f, 0.045f, 0.5f, 0.8f);
                    break;
                case SurfaceKind.Dirt:
                    Debris(CellMud, 2, p, n, Count(8, k), 0.6f, 1.5f, 3.5f, 0.025f, 0.055f, 0.4f, 0.7f);
                    break;
                case SurfaceKind.Paper:
                    break;   // the AP-010B paper tear burst of the legacy dressing is the whole row
                default:
                    Debris(CellGrey, 2, p, n, Count(4, k), 0.6f, 1.5f, 3f, 0.02f, 0.045f, 0.4f, 0.8f);
                    break;
            }

            if (grazing && (info.Surface == SurfaceKind.Stone || info.Surface == SurfaceKind.Metal || info.Surface == SurfaceKind.Armor || info.Surface == SurfaceKind.Default))
            {
                // Ricochet (direction §1.4 column): stone one cool spark line; default three short embers (0.6 m, 0.08 s);
                // metal embers + 3 sparks; armour embers + 6 sparks.
                if (info.Surface == SurfaceKind.Stone)
                {
                    Sparks(p, bounce, 1, 5f, 6f, 0.07f, 0.09f, 0.05f, ColdSpark);
                }
                else
                {
                    Sparks(p, bounce, 3, 7f, 8f, 0.07f, 0.09f, 0.08f);
                    if (info.Surface == SurfaceKind.Metal) Sparks(p, Vector3.Lerp(n, bounce, 0.7f), 3, 3f, 6f, 0.12f, 0.25f);
                    else if (info.Surface == SurfaceKind.Armor) Sparks(p, Vector3.Lerp(n, bounce, 0.7f), 6, 3f, 6f, 0.12f, 0.25f);
                }

                Ricochets++;
            }

            if (WantsLight(info.Surface, info.Caliber, VfxTier.Current))
            {
                Light(p);
            }
        }

        private void PlayBody(in ProjectileImpactInfo info, Vector3 n, Vector3 d)
        {
            if (info.Armor)
            {
                if (!TakeSlot())
                {
                    Skipped++;
                    return;
                }

                Impacts++;
                float k = CalibreScale(info.Caliber) * TierScale;
                Vector3 bounce = Vector3.Reflect(d, n);
                Sparks(info.Position + n * 0.02f, Vector3.Lerp(n, bounce, 0.6f), Count(3, k), 3f, 6f, 0.12f, 0.25f);
                Debris(CellLacquer, 2, info.Position + n * 0.02f, n, Count(1, k), 0.6f, 1.5f, 3f, 0.025f, 0.045f, 0.5f, 0.8f);
                if (WantsLight(SurfaceKind.Armor, info.Caliber, VfxTier.Current)) Light(info.Position + n * 0.05f);
                return;
            }

            // Gore v3: a round that went on through the body sprays out of the back.
            if (!info.Stopped && _dressing != null && _dressing.Blood.IsReady)
            {
                if (_dressing.Blood.PlayExitSpray(info.Position + d * 0.35f, d, info.Caliber >= WeaponCaliber.Heavy)) ExitSprays++;
            }
        }

        private static int Count(int medium, float k) => Mathf.Max(1, Mathf.RoundToInt(medium * k));

        private bool TakeSlot()
        {
            float now = Time.time;
            int cap = Mathf.Min(ConcurrentCap, _recent.Length);
            int live = 0;
            for (int i = 0; i < _recent.Length; i++)
            {
                if (_recent[i] > 0f && now - _recent[i] < ConcurrentWindowSeconds) live++;
            }

            if (live >= cap)
            {
                return false;
            }

            _recent[_recentNext] = Mathf.Max(now, 1e-4f);
            _recentNext = (_recentNext + 1) % _recent.Length;
            return true;
        }

        private void Light(Vector3 at)
        {
            MuzzleSystem lights = MuzzleSystem.Ensure();
            if (lights != null && lights.FlashAt(at, ImpactLightColour, ImpactLightIntensity, ImpactLightRange, 2f / 60f, 0.05f, true))
            {
                LightsAsked++;
            }
        }

        // ------------------------------------------------------------------ emitters

        private void Debris(int cell0, int cells, Vector3 at, Vector3 axis, int count, float spread, float speedMin, float speedMax,
            float sizeMin, float sizeMax, float lifeMin, float lifeMax)
        {
            if (_debris == null) return;
            SetCells(cell0, cells);
            var e = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false, startColor = Color.white };
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (axis + Random.insideUnitSphere * spread).normalized * Random.Range(speedMin, speedMax);
                e.velocity = v;
                e.startSize = Random.Range(sizeMin, sizeMax);
                e.startLifetime = Random.Range(lifeMin, lifeMax);
                e.rotation = Random.Range(0f, 360f);
                e.angularVelocity = Random.Range(-720f, 720f);
                _debris.Emit(e, 1);
            }

            DebrisEmitted += count;
        }

        private void Spray(Vector3 at, Vector3 axis, int count, float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax)
        {
            Spray(at, axis, count, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, SprayColour);
        }

        private void Spray(Vector3 at, Vector3 axis, int count, float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color32 colour)
        {
            if (_debris == null) return;
            SetCells(CellSpray, 2);
            var e = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false, startColor = colour };
            for (int i = 0; i < count; i++)
            {
                e.velocity = (axis + Random.insideUnitSphere * 0.7f).normalized * Random.Range(speedMin, speedMax);
                e.startSize = Random.Range(sizeMin, sizeMax);
                e.startLifetime = Random.Range(lifeMin, lifeMax);
                e.rotation = 0f;
                e.angularVelocity = 0f;
                _debris.Emit(e, 1);
            }

            DebrisEmitted += count;
        }

        // The start frame is normalised over the sheet (0 … 1 = cells 0 … 16): a random cell in [cell0, cell0 + cells).
        private void SetCells(int cell0, int cells)
        {
            _sheet.startFrame = new ParticleSystem.MinMaxCurve(cell0 / 16f, (cell0 + cells - 0.02f) / 16f);
        }

        /// <summary>Tests: one still debris particle of an atlas cell (the atlas-cell mapping render check).</summary>
        public void ProbeEmit(int cell, Vector3 at, float size)
        {
            if (_debris == null) return;
            SetCells(cell, 1);
            var e = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false, startColor = Color.white, velocity = Vector3.zero, startSize = size, startLifetime = 2f, rotation = 0f, angularVelocity = 0f };
            ParticleSystem.MainModule main = _debris.main;
            main.gravityModifier = 0f;   // held still for the render check (gravity would take it out of frame in a slow test frame)
            _debris.Emit(e, 1);
        }

        public void ClearParticles()
        {
            if (_debris != null)
            {
                _debris.Clear();
                ParticleSystem.MainModule main = _debris.main;
                main.gravityModifier = 1f;
            }

            if (_sparks != null) _sparks.Clear();
        }

        public const int ProbeCellMud = CellMud, ProbeCellSplinter = CellSplinter, ProbeCellStone = CellStone;

        private void Sparks(Vector3 at, Vector3 axis, int count, float speedMin, float speedMax, float lifeMin, float lifeMax, float spread = 0.55f)
        {
            Sparks(at, axis, count, speedMin, speedMax, lifeMin, lifeMax, spread, HotSpark);
        }

        private void Sparks(Vector3 at, Vector3 axis, int count, float speedMin, float speedMax, float lifeMin, float lifeMax, float spread, Color32 colour)
        {
            if (_sparks == null) return;
            var e = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false, startColor = colour };
            for (int i = 0; i < count; i++)
            {
                e.velocity = (axis.normalized + Random.insideUnitSphere * spread).normalized * Random.Range(speedMin, speedMax);
                e.startSize = Random.Range(0.012f, 0.025f);
                e.startLifetime = Random.Range(lifeMin, lifeMax);
                _sparks.Emit(e, 1);
            }

            SparksEmitted += count;
        }

        // ------------------------------------------------------------------ build

        private void ApplyTier(VfxTierLevel tier)
        {
            if (_debris != null)
            {
                ParticleSystem.MainModule m = _debris.main;
                m.maxParticles = VfxTier.Pick(160, 400, 520);
            }

            if (_sparks != null)
            {
                ParticleSystem.MainModule m = _sparks.main;
                m.maxParticles = VfxTier.Pick(64, 200, 260);
            }
        }

        private void Build()
        {
            _atlas = BuildAtlas(256);
            _dot = BuildDot(32);
            _debrisMaterial = new Material(FeedbackMaterials.ParticleAlpha()) { name = "DD_ImpactDebris (runtime)", hideFlags = HideFlags.DontSave };
            _debrisMaterial.SetTexture("_BaseMap", _atlas);
            _debrisMaterial.mainTexture = _atlas;
            _sparkMaterial = new Material(FeedbackMaterials.ParticleAlpha()) { name = "DD_ImpactSparks (runtime)", hideFlags = HideFlags.DontSave };
            _sparkMaterial.SetTexture("_BaseMap", _dot);
            _sparkMaterial.mainTexture = _dot;
            // Additive, HDR: the bloom picks the sparks up (#FFE0B0 × 4, direction §1.4).
            if (_sparkMaterial.HasProperty("_Blend")) _sparkMaterial.SetFloat("_Blend", 1f);
            if (_sparkMaterial.HasProperty("_DstBlend")) _sparkMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            Color hot = ImpactLightColour * 4f;
            hot.a = 1f;
            if (_sparkMaterial.HasProperty("_BaseColor")) _sparkMaterial.SetColor("_BaseColor", hot);

            _debris = NewSystem("ImpactDebris", _debrisMaterial, 1f, ParticleSystemRenderMode.Billboard);
            _sheet = _debris.textureSheetAnimation;
            _sheet.enabled = true;
            _sheet.mode = ParticleSystemAnimationMode.Grid;
            _sheet.numTilesX = 4;
            _sheet.numTilesY = 4;
            _sheet.animation = ParticleSystemAnimationType.WholeSheet;
            _sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            _sheet.cycleCount = 1;

            _sparks = NewSystem("ImpactSparks", _sparkMaterial, 0.6f, ParticleSystemRenderMode.Stretch);
            var sparkRenderer = _sparks.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.velocityScale = 0.035f;
            sparkRenderer.lengthScale = 1f;
            ParticleSystem.SizeOverLifetimeModule size = _sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
        }

        private ParticleSystem NewSystem(string name, Material material, float gravity, ParticleSystemRenderMode mode)
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
            main.maxParticles = 400;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.2f;
            renderer.sortMode = ParticleSystemSortMode.None;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ procedural atlas (direction §6-3, credit 0)

        /// <summary>
        /// 4 × 4 cells: 0–3 wet slate chips, 4–5 pale wood splinters, 6–7 red-lacquer flakes on black, 8–9 wet mud, 10–11 white
        /// spray drops (tinted per emit), 12–13 grey chips, 14–15 slate again. Lit from the top-left in the paint (particles are unlit).
        /// </summary>
        public static Texture2D BuildAtlas(int size)
        {
            int cell = size / 4;
            var px = new Color32[size * size];
            var rng = new System.Random(7301);
            var poly = new Vector2[8];
            for (int c = 0; c < 16; c++)
            {
                int ox = (c % 4) * cell, oy = (3 - c / 4) * cell;   // cell 0 top-left (texture-sheet order)
                int kind = c < 4 || c >= 14 ? 0 : c < 6 ? 1 : c < 8 ? 2 : c < 10 ? 3 : c < 12 ? 4 : 5;
                int verts;
                float aspect = kind == 1 ? 0.18f : 1f;
                float radius = kind == 2 ? 0.28f : kind == 3 ? 0.36f : 0.4f;
                verts = kind == 3 || kind == 4 ? 8 : 5 + rng.Next(3);
                for (int v = 0; v < verts; v++)
                {
                    float a = (v + (float)rng.NextDouble() * 0.5f) / verts * Mathf.PI * 2f;
                    float r = radius * (kind == 4 ? 1f : 0.65f + 0.35f * (float)rng.NextDouble());
                    poly[v] = new Vector2(Mathf.Cos(a) * r * (kind == 1 ? 1.1f : 1f), Mathf.Sin(a) * r * aspect);
                }

                float rot = (float)rng.NextDouble() * Mathf.PI;
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                for (int y = 0; y < cell; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        // 2 × 2 supersampled coverage in cell space (−0.5 … 0.5), the shape rotated.
                        float cover = 0f;
                        for (int s = 0; s < 4; s++)
                        {
                            float ux = (x + 0.25f + 0.5f * (s & 1)) / cell - 0.5f;
                            float uy = (y + 0.25f + 0.5f * (s >> 1)) / cell - 0.5f;
                            float qx = ux * cs + uy * sn, qy = -ux * sn + uy * cs;
                            if (kind == 4)
                            {
                                float r2 = (ux * ux + uy * uy) / (0.4f * 0.4f);
                                cover += Mathf.Clamp01(1f - r2) * 0.25f;
                            }
                            else if (Inside(poly, verts, new Vector2(qx, qy)))
                            {
                                cover += 0.25f;
                            }
                        }

                        float fx = (float)x / cell - 0.5f, fy = (float)y / cell - 0.5f;
                        float light = Mathf.Clamp01(0.55f + (fy - fx) * 0.9f);   // top-left light
                        Color col;
                        switch (kind)
                        {
                            case 0: col = Color.Lerp(new Color(0.30f, 0.32f, 0.36f), new Color(0.78f, 0.81f, 0.86f), light); break;   // wet slate: the broken face is lighter than the wet top, and its edge catches the sky
                            case 1: col = Color.Lerp(new Color(0.48f, 0.38f, 0.26f), new Color(0.82f, 0.70f, 0.52f), light) * (0.9f + 0.1f * Mathf.Sin(fx * 90f)); break;
                            case 2: col = fy > 0f ? Color.Lerp(new Color(0.36f, 0.10f, 0.08f), new Color(0.62f, 0.2f, 0.15f), light) : new Color(0.09f, 0.07f, 0.07f); break;
                            case 3: col = Color.Lerp(new Color(0.18f, 0.14f, 0.11f), new Color(0.45f, 0.37f, 0.29f), light); break;
                            case 4: col = Color.Lerp(Color.white * 0.85f, Color.white, light); break;
                            default: col = Color.Lerp(new Color(0.38f, 0.40f, 0.43f), new Color(0.75f, 0.77f, 0.80f), light); break;
                        }

                        px[(oy + y) * size + ox + x] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), (byte)(Mathf.Clamp01(cover) * 255f));
                    }
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_ImpactDebrisAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private static bool Inside(Vector2[] poly, int count, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static Texture2D BuildDot(int size)
        {
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_ImpactSparkDot", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
