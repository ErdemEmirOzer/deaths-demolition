using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-093 Faz2, TD only. AP-037 grayscale masks, runtime tints, fixed prewarmed quads/lights.
    /// Listens exclusively to FeelEvents. Existing AP-010 debris/smoke and shot/casing paths retain ownership.
    /// This class never changes timeScale, camera, enemy transforms, player health or gameplay state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FeelBrushVfxSystem : MonoBehaviour
    {
        public const string ResourceRoot = "VFX/AP037/";
        public const int TransientCapacity = 24, StainCapacity = 40;
        public const float ExplosionReservationSeconds = 1.25f; // .05 s delayed debris + 1.2 s maximum lifetime.
        private const int StainStart = TransientCapacity;
        private const int DropletStart = StainStart + StainCapacity;
        private const int BlastStart = DropletStart + FeelBrushVfxPolicy.DropletCapacity;
        private const int Capacity = BlastStart + FeelBrushVfxPolicy.ExplosionCapacity;
        private static readonly string[] MaterialNames = { "KillA", "KillB", "Stain", "Multi", "Shock", "Droplets" };
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int UvId = Shader.PropertyToID("_AtlasUV");
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int WetId = Shader.PropertyToID("_WetScale");
        private static int FloorMask => (1 << 0) | PrototypeLayers.AimBlockMask;
        private static FeelBrushVfxSystem s_instance;
        public static FeelBrushVfxSystem Instance => s_instance;
        /// <summary>Test seam (like <see cref="Warmup"/>): a fresh run's presentation - every slot, light and reservation free, alive again.</summary>
        public void ResetForTest() => ResetRun();
        public static bool OwnsTdLayers => s_instance != null && s_instance.isActiveAndEnabled && s_instance.AssetsReady
            && CameraModeService.Current == CameraMode.TopDown;

        private enum Kind { None, Kill, Multi, LastStroke, Stain, Droplet, Shock }
        private sealed class Slot
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public Kind Kind;
            public float Age, Life, Size, Alpha, FloorY;
            public int Cell, Frame = -1, GroupId;
            public Vector3 Position, Velocity;
            public Color Tint;
        }
        private struct StainStamp
        {
            public Transform Enemy;
            public int LifeId;
            public float At;
        }
        private sealed class BlastLight
        {
            public Light Light;
            public float Age, ReservationRemaining, Radius;
            public Vector3 Origin;
            public bool Pending, Rocket;
            public int GroupId;
        }

        private readonly Slot[] _slots = new Slot[Capacity];
        private readonly BlastLight[] _lights = new BlastLight[FeelBrushVfxPolicy.ExplosionCapacity];
        private readonly StainStamp[] _stamps = new StainStamp[128];
        private readonly Material[] _materials = new Material[6];
        private readonly FeelBrushKillWindow _killWindow = new FeelBrushKillWindow();
        private Action<FeelHit> _hit;
        private Action<FeelKill> _kill;
        private Action<FeelMultiKill> _multi;
        private Action<FeelExplosion> _explosion;
        private Action<FeelLastZombie> _last;
        private Action<FeelPlayerDamage> _damage;
        private Action<FeelRound> _round;
        private Action<CameraModeChange> _modeChanged;
        private bool _built, _dead;
        private int _stampCursor, _lastKillSlot = -1;
        private uint _random = 3701;
        private FeelKill _lastRoundKill;
        private bool _hasLastRoundKill;
        private Mesh _mesh;
        public bool AssetsReady { get; private set; }
        public int LiveDroplets { get; private set; }
        public int LiveExplosions { get; private set; }
        /// <summary>M-62: blast lights lit with a shadow now - Cinematic only, a rocket's only, at most one.</summary>
        public int ShadowedLightsLit
        {
            get
            {
                int n = 0;
                if (!_built) return 0;
                for (int i = 0; i < _lights.Length; i++) if (_lights[i].Light.enabled && _lights[i].Light.shadows != LightShadows.None) n++;
                return n;
            }
        }
        public int HitBursts { get; private set; }
        public int KillBursts { get; private set; }
        public int StainMarks { get; private set; }
        public int LastStrokes { get; private set; }
        public int MultiRings { get; private set; }
        public Vector3 LastMultiCenter { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() { s_instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_instance != null) return;
            // Domain reload disabled: recover an existing enabled owner rather than duplicate it.
            var existing = FindObjectOfType<FeelBrushVfxSystem>();
            if (existing != null) { s_instance = existing; existing.Rearm(); return; }
            var go = new GameObject("FeelBrushVfxSystem (AP037 TD)");
            DontDestroyOnLoad(go);
            go.AddComponent<FeelBrushVfxSystem>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { enabled = false; return; }
            s_instance = this;
            _hit = OnHit; _kill = OnKill; _multi = OnMulti; _explosion = OnExplosion;
            _last = OnLast; _damage = OnDamage; _round = OnRound; _modeChanged = OnModeChanged;
            for (int i = 0; i < _materials.Length; i++) _materials[i] = Resources.Load<Material>(ResourceRoot + MaterialNames[i]);
            Warmup(_materials);
        }

        /// <summary>Initialization/test seam. Materials are shared; no instances are created. Call before events, never per frame.</summary>
        public bool Warmup(Material[] materials)
        {
            AssetsReady = materials != null && materials.Length == 6;
            if (AssetsReady)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (materials[i] == null || materials[i].shader == null || !materials[i].shader.isSupported
                        || materials[i].shader.name != "DeathsDemolition/VFX/AP037Brush"
                        || !materials[i].HasProperty(UvId) || !materials[i].HasProperty(TintId)
                        || !materials[i].HasProperty(AlphaId) || !materials[i].HasProperty(RevealId)
                        || !materials[i].HasProperty(WetId) || !materials[i].HasProperty("_UseTime")
                        || !materials[i].HasProperty("_InkCap") || !materials[i].HasProperty("_Soft")
                        || !materials[i].HasProperty("_Mask") || !materials[i].HasProperty("_TimeMask")
                        || materials[i].GetTexture("_Mask") == null
                        || ((i == 3 || i == 4) && (materials[i].GetTexture("_TimeMask") == null
                            || materials[i].GetFloat("_UseTime") < .5f)))
                    { AssetsReady = false; break; }
                }
            }
            if (!AssetsReady) { ClearVisuals(); return false; } // Existing TD layers keep ownership until all assets exist.
            for (int i = 0; i < 6; i++) _materials[i] = materials[i];
            if (_built) { ClearVisuals(); return true; }
            _mesh = new Mesh { name = "AP037 shared quad", hideFlags = HideFlags.DontSave };
            _mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
            _mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.RecalculateBounds();
            for (int i = 0; i < Capacity; i++)
            {
                var go = new GameObject("AP037 pooled quad");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.enabled = false;
                _slots[i] = new Slot { Transform = go.transform, Renderer = renderer };
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject("AP037 pooled blast light"); go.transform.SetParent(transform, false);
                var light = go.AddComponent<Light>(); light.type = LightType.Point;
                light.color = new Color(1f, .62f, .32f); light.enabled = false;
                _lights[i] = new BlastLight { Light = light };
            }
            _built = true;
            return true;
        }

        private void OnEnable()
        {
            if (s_instance != this) return;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
            Rearm();
        }
        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged; Unsubscribe(); ClearVisuals();
        }
        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (_mesh != null) Destroy(_mesh);
        }
        private void OnSceneChanged(Scene before, Scene after) { ResetRun(); Rearm(); }
        private void Unsubscribe()
        {
            FeelEvents.EnemyHit -= _hit; FeelEvents.EnemyKilled -= _kill; FeelEvents.MultiKill -= _multi;
            FeelEvents.Explosion -= _explosion; FeelEvents.LastZombie -= _last;
            FeelEvents.PlayerDamaged -= _damage; FeelEvents.RoundStarted -= _round;
            CameraModeService.Changed -= _modeChanged;
        }
        private void Rearm()
        {
            Unsubscribe();
            FeelEvents.EnemyHit += _hit; FeelEvents.EnemyKilled += _kill; FeelEvents.MultiKill += _multi;
            FeelEvents.Explosion += _explosion; FeelEvents.LastZombie += _last;
            FeelEvents.PlayerDamaged += _damage; FeelEvents.RoundStarted += _round;
            CameraModeService.Changed += _modeChanged;
        }
        private bool CanPlay => AssetsReady && CameraModeService.Current == CameraMode.TopDown && !_dead;
        private void OnDamage(FeelPlayerDamage damage) { if (damage.Fatal) { _dead = true; ClearVisuals(); } }
        private void OnRound(FeelRound round) { ResetRun(); }
        private void OnModeChanged(CameraModeChange change) { if (CameraModeService.Current != CameraMode.TopDown) ClearVisuals(); }
        private void ResetRun()
        {
            ClearVisuals(); _dead = false; _stampCursor = 0; _random = 3701;
            for (int i = 0; i < _stamps.Length; i++) _stamps[i] = default;
        }
        private void ClearVisuals()
        {
            _killWindow.Clear(); _hasLastRoundKill = false; _lastRoundKill = default; _lastKillSlot = -1;
            LastMultiCenter = Vector3.zero;
            if (!_built) return;
            for (int i = 0; i < _slots.Length; i++) Hide(i);
            for (int i = 0; i < _lights.Length; i++)
            { _lights[i].Light.enabled = false; _lights[i].ReservationRemaining = 0f; _lights[i].Pending = false; _lights[i].Rocket = false; }
            LiveDroplets = 0; LiveExplosions = 0;
        }
        private void Hide(int index)
        {
            Slot slot = _slots[index];
            slot.Kind = Kind.None; slot.Renderer.enabled = false;
        }
        private float Random01()
        {
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            return (_random & 0xffffff) / 16777216f;
        }
        private int Acquire(int start, int count, bool replace)
        {
            int oldest = -1; float age = -1f;
            for (int i = start; i < start + count; i++)
            {
                // Preserve EnemyKilled -> MultiKill -> LastZombie synchronous context.
                if (_hasLastRoundKill && i == _lastKillSlot) continue;
                if (_slots[i].Kind == Kind.None) return i;
                if (_slots[i].Age > age) { age = _slots[i].Age; oldest = i; }
            }
            return replace ? oldest : -1;
        }
        private void Begin(int index, Kind kind, int material, Vector3 at, Vector3 direction, Vector3 normal,
            float size, float life, Color tint, float alpha, int cell = 0, int groupId = 0)
        {
            Slot slot = _slots[index]; slot.Kind = kind; slot.Age = 0f; slot.Life = life; slot.Size = size;
            slot.Alpha = alpha; slot.Position = at; slot.Tint = tint; slot.Cell = cell; slot.Frame = -1;
            slot.Velocity = Vector3.zero; slot.GroupId = groupId;
            slot.Transform.SetPositionAndRotation(at, FeelBrushVfxPolicy.GroundRotation(direction, normal));
            slot.Transform.localScale = Vector3.one * (kind == Kind.Shock ? size * .001f : size);
            slot.Renderer.sharedMaterial = _materials[material];
            slot.Block.Clear(); slot.Block.SetColor(TintId, tint); slot.Block.SetFloat(AlphaId, alpha);
            slot.Block.SetFloat(RevealId, kind == Kind.Multi || kind == Kind.Shock ? 0f : 1f);
            slot.Block.SetFloat(WetId, 1f);
            slot.Block.SetVector(UvId, FeelBrushVfxPolicy.AtlasUv(cell, 4, kind == Kind.Stain ? 2 : 4));
            if (kind == Kind.Multi || kind == Kind.Shock) slot.Block.SetVector(UvId, new Vector4(1f, 1f, 0f, 0f));
            slot.Renderer.SetPropertyBlock(slot.Block); slot.Renderer.enabled = true;
        }
        private static bool Ground(Vector3 at, out Vector3 point, out Vector3 normal)
        {
            if (Physics.Raycast(at + Vector3.up, Vector3.down, out RaycastHit hit, 5f, FloorMask, QueryTriggerInteraction.Ignore)
                && SurfaceResolver.Resolve(hit.collider) != SurfaceKind.Water)
            { normal = hit.normal; point = hit.point + normal * .015f; return true; }
            point = at; normal = Vector3.up; return false;
        }
        private bool AllowStain(Transform enemy, int lifeId)
        {
            if (enemy == null) return false;
            for (int i = 0; i < _stamps.Length; i++)
            {
                if (_stamps[i].Enemy != enemy || _stamps[i].LifeId != lifeId) continue;
                if (Time.time - _stamps[i].At < FeelBrushVfxPolicy.StainInterval) return false;
                _stamps[i].At = Time.time; return true;
            }
            _stamps[_stampCursor] = new StainStamp { Enemy = enemy, LifeId = lifeId, At = Time.time };
            _stampCursor = (_stampCursor + 1) % _stamps.Length;
            return true;
        }
        private void OnHit(FeelHit hit)
        {
            if (!CanPlay || hit.GroundFire || hit.HealthDamage <= 0f) return; // Armor sparks keep the existing owner.
            Vector3 direction = FeelBrushVfxPolicy.FlatDirection(hit.Direction);
            Color tint = hit.Critical ? FeelBrushVfxPolicy.CriticalTint : FeelBrushVfxPolicy.BloodTint;
            int forward = 4 + (int)(Random01() * 5f);
            int reserve = hit.Critical ? 3 : 0;
            EmitDroplets(hit.Point, direction, forward, 1f, tint, reserve, hit.GroupId);
            EmitDroplets(hit.Point, -direction, Mathf.Max(1, forward / 2), .6f, tint, reserve, hit.GroupId);
            if (hit.Critical) EmitDroplets(hit.Point + Vector3.up * .08f, direction, 3, 1.15f, tint, 0, hit.GroupId);
            if (Ground(hit.Point, out Vector3 point, out Vector3 normal) && AllowStain(hit.Enemy, hit.LifeId))
            {
                int cell = 4 + (int)(Random01() * 4f);
                float size = cell == 4 ? .6f : cell == 5 ? .8f : cell == 6 ? 1f : 1.1f;
                Begin(Acquire(StainStart, StainCapacity, true), Kind.Stain, 2, point, direction, normal, size,
                    BloodSystem.RoundDecalSeconds, tint, .75f, cell, hit.GroupId);
                StainMarks++;
            }
            HitBursts++;
        }
        private void EmitDroplets(Vector3 at, Vector3 direction, int count, float speedScale, Color tint, int reserve, int groupId)
        {
            int free = 0;
            for (int i = DropletStart; i < BlastStart; i++) if (_slots[i].Kind == Kind.None) free++;
            count = Mathf.Min(count, Mathf.Max(0, free - reserve));
            float floor = Ground(at, out Vector3 p, out Vector3 n) ? p.y : at.y - 1.2f;
            for (int i = 0; i < count; i++)
            {
                int index = Acquire(DropletStart, FeelBrushVfxPolicy.DropletCapacity, false);
                if (index < 0) break;
                Vector3 travel = Quaternion.AngleAxis((Random01() - .5f) * 30f, Vector3.up) * direction;
                int cell = (int)(Random01() * 12f);
                float size = cell < 6 ? .04f + .04f * Random01() : .08f;
                Begin(index, Kind.Droplet, 5, at, travel, Vector3.up, size, .4f + .2f * Random01(), tint, .95f, cell, groupId);
                _slots[index].Velocity = travel * ((1.8f + 1.2f * Random01()) * speedScale)
                    + Vector3.up * ((.4f + .5f * Random01()) * speedScale);
                _slots[index].FloorY = floor;
                LiveDroplets++;
            }
        }
        private void OnKill(FeelKill kill)
        {
            if (!CanPlay) return;
            _killWindow.Record(kill.Position, kill.GroupId, Time.unscaledTime);
            Vector3 direction = FeelBrushVfxPolicy.FlatDirection(kill.Direction);
            float scale = kill.Heavy || kill.LastOfRound ? 1.6f : 1f;
            float size = 1.2f * scale;
            if (!Ground(kill.Position, out Vector3 point, out Vector3 normal)) return;
            Vector3 tangent = Vector3.ProjectOnPlane(direction, normal).normalized;
            int index = Acquire(0, TransientCapacity, true);
            Begin(index, Kind.Kill, Random01() < .5f ? 0 : 1,
                point + tangent * (.4f * size), tangent, normal, size, FeelBrushVfxPolicy.KillSeconds,
                kill.Critical ? FeelBrushVfxPolicy.CriticalTint : FeelBrushVfxPolicy.BloodTint, 1f, 1, kill.GroupId);
            if (kill.LastOfRound) { _lastRoundKill = kill; _hasLastRoundKill = true; _lastKillSlot = index; }
            KillBursts++;
        }
        private void OnMulti(FeelMultiKill multi)
        {
            if (!CanPlay) return;
            bool groupCenter = multi.SameGroup && multi.Tier <= multi.GroupTier;
            Vector3 center = _killWindow.Center(Time.unscaledTime, groupCenter, multi.GroupId, multi.Position);
            if (!Ground(center, out Vector3 point, out Vector3 normal)) return;
            LastMultiCenter = center;
            Vector3 direction = Quaternion.AngleAxis(Random01() * 360f, normal) * Vector3.forward;
            Begin(Acquire(0, TransientCapacity, true), Kind.Multi, 3, point, direction, normal,
                FeelBrushVfxPolicy.MultiQuadMetres(multi.Tier), FeelBrushVfxPolicy.MultiSeconds,
                FeelBrushVfxPolicy.InkTint, .9f, 0, groupCenter ? multi.GroupId : 0);
            MultiRings++;
        }
        /// <summary>Shared admission for debris, ring and light. Holds each slot through the longest delayed debris layer.</summary>
        public static bool TryReserveExplosion(Vector3 origin, float radius) => TryReserveExplosion(origin, radius, false);

        /// <summary>M-62 (C-00 30.09): a rocket's blast is never the one dropped - with all eight slots held it takes over the oldest
        /// small blast's (explosive rounds, bomblets: its ring and light end, its debris was never a small blast's).</summary>
        public static bool TryReserveExplosion(Vector3 origin, float radius, bool rocket)
        {
            return s_instance != null && s_instance.ReserveExplosion(origin, radius, rocket) >= 0;
        }
        private int ReserveExplosion(Vector3 origin, float radius, bool rocket)
        {
            if (!CanPlay) return -1;
            int index = -1, small = -1;
            for (int i = 0; i < _lights.Length && index < 0; i++)
            {
                BlastLight light = _lights[i];
                if (light.ReservationRemaining <= 0f) index = i;
                else if (!light.Rocket && (small < 0 || light.ReservationRemaining < _lights[small].ReservationRemaining)) small = i;
            }
            if (index < 0 && rocket && small >= 0)
            {
                index = small;
                _lights[index].Light.enabled = false;
                if (_slots[BlastStart + index].Kind == Kind.Shock) { LiveExplosions--; Hide(BlastStart + index); }
            }
            if (index < 0) return -1;
            BlastLight taken = _lights[index];
            taken.ReservationRemaining = ExplosionReservationSeconds; taken.Origin = origin; taken.Radius = radius;
            taken.Pending = true; taken.GroupId = 0; taken.Rocket = rocket;
            return index;
        }
        private void OnExplosion(FeelExplosion explosion)
        {
            if (!CanPlay) return;
            int lightIndex = -1;
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i].Pending && _lights[i].Origin == explosion.Origin
                    && Mathf.Abs(_lights[i].Radius - explosion.Radius) < .0001f)
                { lightIndex = i; break; }
            // Direct FeelEvents publishers use the same budget as the legacy debris bridge.
            if (lightIndex < 0) lightIndex = ReserveExplosion(explosion.Origin, explosion.Radius, explosion.Source == ExplosionSource.Rocket);
            if (lightIndex < 0) return;
            var light = _lights[lightIndex]; light.Pending = false;
            if (Ground(explosion.Origin, out Vector3 point, out Vector3 normal))
            {
                Begin(BlastStart + lightIndex, Kind.Shock, 4, point, Vector3.forward, normal,
                    FeelBrushVfxPolicy.ShockQuadMetres(explosion.Radius), FeelBrushVfxPolicy.ShockSeconds,
                    FeelBrushVfxPolicy.DustTint, .6f, 0, explosion.GroupId);
                LiveExplosions++;
            }
            // Air/water blasts still flash; only the dry-ground brush ring requires a floor.
            light.Age = 0f; light.GroupId = explosion.GroupId;
            light.Light.transform.position = explosion.Origin + Vector3.up * .3f;
            light.Light.range = 2.5f * Mathf.Max(.05f, explosion.Radius);
            light.Light.intensity = 30f * Mathf.Clamp01(GameFeelSettings.FlashScale);
            // M-62 (C-00 30.09, perf): only a rocket casts the Cinematic shadow, and only while no other blast light does (≤ 1).
            light.Light.shadows = explosion.Source == ExplosionSource.Rocket && ExplosionDirector.LightCastsShadows && ShadowedLightsLit == 0
                ? LightShadows.Soft : LightShadows.None;
            light.Light.enabled = true;
        }
        private void OnLast(FeelLastZombie last)
        {
            if (!CanPlay || !_hasLastRoundKill || _lastRoundKill.Enemy != last.Enemy || _lastRoundKill.Round != last.Round) return;
            Vector3 direction = FeelBrushVfxPolicy.FlatDirection(_lastRoundKill.Direction);
            // The protected same-call kill already carries x1.6 and its critical tint/group.
            if (!Ground(last.Position, out Vector3 point, out Vector3 normal)) return;
            direction = Vector3.ProjectOnPlane(direction, normal).normalized;
            Begin(Acquire(0, TransientCapacity, true), Kind.LastStroke, 0, point + direction * .768f,
                direction, normal, 1.92f, FeelBrushVfxPolicy.KillSeconds, FeelBrushVfxPolicy.InkTint, 1f, 1, _lastRoundKill.GroupId);
            _hasLastRoundKill = false; _lastRoundKill = default; _lastKillSlot = -1;
            LastStrokes++;
        }
        private void Update() { Tick(Time.deltaTime, Time.unscaledDeltaTime); }
        /// <summary>Allocation-free advancement. Paused scaled clock never advances envelopes.</summary>
        public void Tick(float scaledDelta, float unscaledDelta)
        {
            if (!_built) return;
            if (!CanPlay) { ClearVisuals(); return; }
            if (scaledDelta <= 0f) return;
            unscaledDelta = Mathf.Max(0f, unscaledDelta);
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot slot = _slots[i]; if (slot.Kind == Kind.None) continue;
                float dt = HitStopService.VisualDelta(slot.GroupId, scaledDelta, unscaledDelta);
                if (dt <= 0f) continue;
                slot.Age += dt;
                if (slot.Age >= slot.Life)
                {
                    if (slot.Kind == Kind.Droplet) LiveDroplets--;
                    if (slot.Kind == Kind.Shock) LiveExplosions--;
                    Hide(i); continue;
                }
                float t = slot.Age / slot.Life;
                float alpha = slot.Alpha;
                if (slot.Kind == Kind.Kill || slot.Kind == Kind.LastStroke)
                {
                    int frame = FeelBrushVfxPolicy.KillFrame(t);
                    if (frame != slot.Frame)
                    { slot.Frame = frame; slot.Block.SetVector(UvId, FeelBrushVfxPolicy.AtlasUv(frame, 4, 4)); }
                }
                else if (slot.Kind == Kind.Multi)
                {
                    slot.Block.SetFloat(RevealId, FeelBrushVfxPolicy.EaseOutCubic(t));
                    alpha *= 1f - Mathf.Clamp01((t - .75f) / .25f);
                }
                else if (slot.Kind == Kind.Shock)
                {
                    float grow = FeelBrushVfxPolicy.EaseOutCubic(slot.Age / FeelBrushVfxPolicy.ShockGrowSeconds);
                    slot.Transform.localScale = Vector3.one * (slot.Size * Mathf.Max(.001f, grow));
                    slot.Block.SetFloat(RevealId, grow); alpha *= 1f - t;
                }
                else if (slot.Kind == Kind.Droplet)
                {
                    slot.Velocity += Vector3.down * (9.81f * dt); slot.Position += slot.Velocity * dt;
                    if (slot.Position.y <= slot.FloorY) { LiveDroplets--; Hide(i); continue; }
                    slot.Transform.position = slot.Position;
                    alpha *= 1f - Mathf.Clamp01((t - .6f) / .4f);
                }
                else if (slot.Kind == Kind.Stain)
                {
                    slot.Block.SetFloat(WetId, Mathf.Clamp01(1f - slot.Age / 1.5f));
                    alpha *= Mathf.Clamp01((slot.Life - slot.Age) / 2f);
                    // Settled dry stains need no repeated renderer writes.
                    if (slot.Age > 1.5f + dt && slot.Life - slot.Age > 2f) continue;
                }
                slot.Block.SetFloat(AlphaId, alpha); slot.Renderer.SetPropertyBlock(slot.Block);
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                BlastLight light = _lights[i];
                float dt = HitStopService.VisualDelta(light.GroupId, scaledDelta, unscaledDelta);
                if (dt <= 0f) continue;
                light.ReservationRemaining = Mathf.Max(0f, light.ReservationRemaining - dt);
                if (light.ReservationRemaining <= 0f) light.Pending = false;
                if (!light.Light.enabled) continue;
                light.Age += dt; float t = Mathf.Clamp01((light.Age - 2f / 60f) / .25f);
                light.Light.intensity = 30f * (1f - t) * (1f - t) * Mathf.Clamp01(GameFeelSettings.FlashScale);
                if (t >= 1f) light.Light.enabled = false;
            }
        }
    }
}
