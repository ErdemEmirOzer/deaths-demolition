using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-093 Faz2 / TD only. Synthetic dry floor + real AP037 resource materials. DamageEvents drives the real
    /// FeelEvents bridge; Tick advances the presentation deterministically. No weapon/player/FP/TP game is launched.
    /// Existing scene owners and bus subscriptions are saved and restored, never destroyed by this fixture.
    /// </summary>
    [TestFixture, Category("WP093Faz2"), Category("TD")]
    public sealed class Wp093FeelBrushVfxTests
    {
        private static readonly Vector3 O = new Vector3(900f, 0f, 900f);
        private static readonly string[] MaterialNames = { "KillA", "KillB", "Stain", "Multi", "Shock", "Droplets" };
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<KeyValuePair<FieldInfo, object>> _savedDelegates = new List<KeyValuePair<FieldInfo, object>>();
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private FeelBrushVfxSystem _system, _previousOwner;
        private TracerRibbonRenderer _tracer, _previousTracer;
        private bool _previousTracerEnabled, _previousInterimTrails, _hasTracerFixture;
        private GameObject _root, _enemyGo;
        private Material[] _materials;
        private MeshRenderer[] _renderers;
        private Light[] _lights;
        private CameraMode _previousMode;
        private bool _previousEnabled;
        private float _previousTimeScale, _previousFlash, _previousClock;
        private int _previousLast;
        private FeelEventThresholds _previousThresholds;
        private Scene _originalScene, _temporaryScene;

        [SetUp]
        public void SetUp()
        {
            _originalScene = SceneManager.GetActiveScene();
            _previousMode = CameraModeService.Current;
            _previousTimeScale = Time.timeScale;
            _previousFlash = GameFeelSettings.FlashScale;
            _previousClock = FeelEvents.TestClock;
            _previousLast = FeelEvents.TestLastOfRound;
            _previousThresholds = FeelEvents.Thresholds;
            _previousOwner = FeelBrushVfxSystem.Instance;
            if (_previousOwner != null)
            {
                _previousEnabled = _previousOwner.enabled;
                _previousOwner.enabled = false;
            }
            typeof(FeelBrushVfxSystem).GetField("s_instance", StaticFields).SetValue(null, null);
            // Isolate preinstalled presentation/gameplay subscribers while retaining the production bridge itself.
            SaveAndClearDelegates(typeof(FeelEvents));
            SaveAndClearDelegates(typeof(DamageEvents));
            SaveAndClearDelegates(typeof(RunEvents));
            FeelEvents.Rearm();
            typeof(FeelEvents).GetMethod("ClearKillWindows", StaticFields).Invoke(null, null);
            FeelEvents.Thresholds = FeelEventThresholds.Default;
            FeelEvents.TestClock = 10f;
            FeelEvents.TestLastOfRound = 0;
            DamageHitContext.Clear();
            SetModeState(CameraMode.TopDown);
            Time.timeScale = 1f;
            GameFeelSettings.FlashScale = 1f;

            _root = new GameObject("WP093 Faz2 fixture");
            var floor = new GameObject("WP093 dry floor");
            floor.transform.SetParent(_root.transform, false);
            floor.transform.position = O + Vector3.down * .1f;
            floor.AddComponent<BoxCollider>().size = new Vector3(80f, .2f, 80f);
            _enemyGo = new GameObject("WP093 pooled enemy identity");
            _enemyGo.transform.SetParent(_root.transform, false);
            _enemyGo.transform.position = O;
            Physics.SyncTransforms();

            _materials = new Material[6];
            for (int i = 0; i < 6; i++)
            {
                _materials[i] = Resources.Load<Material>(FeelBrushVfxSystem.ResourceRoot + MaterialNames[i]);
                Assert.That(_materials[i], Is.Not.Null, "Missing production AP037 material: " + MaterialNames[i]);
            }
            var owner = new GameObject("WP093 test brush owner");
            owner.transform.SetParent(_root.transform, false);
            _system = owner.AddComponent<FeelBrushVfxSystem>();
            Assert.That(_system.Warmup(_materials), Is.True, "AP037 shader/mask intake must be valid; no Assume skip");
            _renderers = owner.GetComponentsInChildren<MeshRenderer>(true);
            _lights = owner.GetComponentsInChildren<Light>(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_originalScene.IsValid() && _originalScene.isLoaded && SceneManager.GetActiveScene() != _originalScene)
                SceneManager.SetActiveScene(_originalScene);
            if (_root != null) Object.DestroyImmediate(_root);
            if (_hasTracerFixture)
            {
                typeof(TracerRibbonRenderer).GetField("s_instance", StaticFields).SetValue(null, _previousTracer);
                if (_previousTracer != null) _previousTracer.enabled = _previousTracerEnabled;
                ProjectileDriver.DrawInterimTrails = _previousInterimTrails;
            }
            _tracer = _previousTracer = null;
            _hasTracerFixture = false;
            foreach (Material material in _ownedMaterials) if (material != null) Object.DestroyImmediate(material);
            _ownedMaterials.Clear();
            if (_temporaryScene.IsValid() && _temporaryScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_temporaryScene);
                if (unload != null) while (!unload.isDone) yield return null;
            }
            FeelEvents.Disarm();
            for (int i = _savedDelegates.Count - 1; i >= 0; i--)
                _savedDelegates[i].Key.SetValue(null, _savedDelegates[i].Value);
            _savedDelegates.Clear();
            FeelEvents.Rearm();
            typeof(FeelEvents).GetMethod("ClearKillWindows", StaticFields).Invoke(null, null);
            FeelEvents.TestClock = _previousClock;
            FeelEvents.TestLastOfRound = _previousLast;
            FeelEvents.Thresholds = _previousThresholds;
            DamageHitContext.Clear();
            SetModeState(_previousMode);
            Time.timeScale = _previousTimeScale;
            GameFeelSettings.FlashScale = _previousFlash;
            typeof(FeelBrushVfxSystem).GetField("s_instance", StaticFields).SetValue(null, _previousOwner);
            if (_previousOwner != null) _previousOwner.enabled = _previousEnabled;
        }

        [Test]
        public void Warmup_RealAssets_FixedSharedMeshPool_OwnsTdLayers()
        {
            Assert.That(_system.AssetsReady && FeelBrushVfxSystem.OwnsTdLayers, Is.True);
            Assert.That(_renderers.Length, Is.EqualTo(84), "24 transient + 40 stains + 12 drops + 8 shocks");
            Assert.That(_lights.Length, Is.EqualTo(8));
            Mesh mesh = _renderers[0].GetComponent<MeshFilter>().sharedMesh;
            foreach (MeshRenderer renderer in _renderers)
            {
                Assert.That(renderer.enabled, Is.False);
                Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            }
            foreach (Light light in _lights) Assert.That(light.enabled, Is.False);
        }

        [Test]
        public void Bridge_FleshAndCritical_ForwardCone_BackSpray_AndThreeHeadDrops()
        {
            Hit(life: 1);
            Assert.That(_system.HitBursts, Is.EqualTo(1));
            Assert.That(_system.StainMarks, Is.EqualTo(1));
            Assert.That(_system.LiveDroplets, Is.InRange(6, 12));
            AssertTint(_materials[5], FeelBrushVfxPolicy.BloodTint);
            _system.Tick(.01f, .01f);
            int forward = 0, backward = 0;
            foreach (MeshRenderer renderer in _renderers)
            {
                if (!renderer.enabled || renderer.sharedMaterial != _materials[5]) continue;
                Vector3 move = renderer.transform.position - (O + Vector3.up);
                if (move.x > 0f) forward++; else backward++;
                Assert.That(Mathf.Abs(move.z), Is.LessThanOrEqualTo(Mathf.Abs(move.x) * Mathf.Tan(15f * Mathf.Deg2Rad) + .0001f));
            }
            Assert.That(forward, Is.InRange(4, 8));
            Assert.That(backward, Is.GreaterThan(0), "§4.2: exit spray must travel against the shot");

            RunEvents.PublishRoundStarted(2);
            Hit(life: 2, critical: true);
            Assert.That(_system.LiveDroplets, Is.LessThanOrEqualTo(12));
            AssertTint(_materials[5], FeelBrushVfxPolicy.CriticalTint);
            int headDrops = 0;
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled && renderer.sharedMaterial == _materials[5]
                    && Mathf.Abs(renderer.transform.position.y - (O.y + 1.08f)) < .0001f) headDrops++;
            Assert.That(headDrops, Is.EqualTo(3), "§4.2: head burst reserves three pool entries");
        }

        [Test]
        public void Bridge_ArmorOnlyAndGroundFire_DoNotSpawnBlood()
        {
            Hit(life: 1, health: 0f);
            DamageHitContext.MarkGroundFire();
            try { Hit(life: 1); }
            finally { DamageHitContext.Clear(); }
            Assert.That(_system.HitBursts, Is.Zero);
            Assert.That(_system.StainMarks, Is.Zero);
            Assert.That(ActiveQuads(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Stain_PerLifeQuarterSecondThrottle_AndRoundReset()
        {
            Hit(life: 10);
            Hit(life: 10);
            Assert.That(_system.StainMarks, Is.EqualTo(1), "same life, same quarter-second");
            Hit(life: 11);
            Assert.That(_system.StainMarks, Is.EqualTo(2), "pooled Transform's new LifeId must not inherit the old stamp");
            float until = Time.time + .26f;
            while (Time.time < until) yield return null;
            Hit(life: 11);
            Assert.That(_system.StainMarks, Is.EqualTo(3));
            RunEvents.PublishRoundStarted(2);
            Assert.That(ActiveQuads(), Is.Zero);
            Hit(life: 11);
            Assert.That(_system.StainMarks, Is.EqualTo(4), "round reset clears stamp history");
        }

        [Test]
        public void Bridge_KillDirection_HeavyScale_AtlasAndQuarterSecondLifetime()
        {
            Kill(O, life: 1);
            MeshRenderer normal = FindBloodKill();
            Assert.That(normal.transform.localScale.x, Is.EqualTo(1.2f).Within(.0001f));
            Assert.That(normal.transform.position.x, Is.EqualTo(O.x + .48f).Within(.0001f));
            Assert.That(Vector3.Dot(normal.transform.right, Vector3.right), Is.GreaterThan(.999f));
            _system.Tick(.125f, .125f);
            var block = new MaterialPropertyBlock();
            normal.GetPropertyBlock(block);
            Assert.That(block.GetVector("_AtlasUV"), Is.EqualTo(FeelBrushVfxPolicy.AtlasUv(8, 4, 4)));
            _system.Tick(.125f, .125f);
            Assert.That(normal.enabled, Is.False);
            Kill(O, life: 2, heavy: true);
            Assert.That(FindBloodKill().transform.localScale.x, Is.EqualTo(1.92f).Within(.0001f));
        }

        [Test]
        public void Bridge_GroupTriple_RingUsesDeathCentroid_NotLastDeathPosition()
        {
            int previous = FeelEvents.PushHitGroup(73);
            try
            {
                Kill(O, 1); Kill(O + Vector3.right * 2f, 2); Kill(O + Vector3.right * 10f, 3);
            }
            finally { FeelEvents.PopHitGroup(previous); }
            Assert.That(_system.MultiRings, Is.EqualTo(1));
            MeshRenderer ring = FindOnly(_materials[3]);
            Assert.That(ring.transform.position.x, Is.EqualTo(O.x + 4f).Within(.001f));
            Assert.That(_system.LastMultiCenter.x, Is.EqualTo(O.x + 4f).Within(.001f));
            Assert.That(ring.transform.localScale.x * .840f * .5f, Is.EqualTo(1.5f).Within(.001f));
            _system.Tick(.15f, .15f);
            var block = new MaterialPropertyBlock(); ring.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_Reveal"), Is.EqualTo(.875f).Within(.0001f));
            _system.Tick(.15f, .15f);
            Assert.That(ring.enabled, Is.False);
        }

        [Test]
        public void Bridge_ChainFour_RingUsesAllDeathsInTheSlidingWindow()
        {
            Kill(O, 1); Kill(O + Vector3.right * 2f, 2); Kill(O + Vector3.right * 4f, 3); Kill(O + Vector3.right * 10f, 4);
            Assert.That(_system.MultiRings, Is.EqualTo(1));
            Assert.That(FindOnly(_materials[3]).transform.position.x, Is.EqualTo(O.x + 4f).Within(.001f));
        }

        [Test]
        public void Explosion_GrowsFromZeroToOnePointTwoRadius_AndFadesWithPooledLight()
        {
            const float radius = 3f;
            FeelEvents.RaiseExplosion(O, radius, 9, default);
            MeshRenderer ring = FindOnly(_materials[4]);
            Assert.That(ring.transform.localScale.x * .829f * .5f, Is.LessThan(.01f), "§4.5: first frame starts at zero");
            Assert.That(_system.LiveExplosions, Is.EqualTo(1));
            Light light = FindLight();
            Assert.That(light.range, Is.EqualTo(7.5f).Within(.0001f));
            Assert.That(light.intensity, Is.EqualTo(30f).Within(.0001f));
            _system.Tick(.09f, .09f);
            Assert.That(ring.transform.localScale.x * .829f * .5f, Is.EqualTo(3.6f * .875f).Within(.001f));
            _system.Tick(.09f, .09f);
            Assert.That(ring.transform.localScale.x * .829f * .5f, Is.EqualTo(3.6f).Within(.001f));
            _system.Tick(.08f, .08f);
            Assert.That(ring.enabled, Is.False);
            Assert.That(_system.LiveExplosions, Is.Zero);
            _system.Tick(.04f, .04f);
            Assert.That(light.enabled, Is.False);
        }

        [Test]
        public void Explosion_AdmissionIncludesDelayedDebrisLifetime_AfterRingRetires()
        {
            const float radius = 2f;
            Assert.That(FeelBrushVfxSystem.ExplosionReservationSeconds, Is.EqualTo(1.25f),
                ".05-second debris delay + maximum 1.2-second debris lifetime");
            for (int i = 0; i < 8; i++)
            {
                Vector3 origin = O + Vector3.right * (i * 2f);
                Assert.That(FeelBrushVfxSystem.TryReserveExplosion(origin, radius), Is.True,
                    "shared admission must accept exactly eight reservation/event pairs");
                FeelEvents.RaiseExplosion(origin, radius, 0, default);
            }
            Assert.That(_system.LiveExplosions, Is.EqualTo(8), "an event consumes its pending reservation, not a second slot");
            _system.Tick(.26f, .26f);
            Assert.That(_system.LiveExplosions, Is.Zero);
            Assert.That(Count(_materials[4]), Is.Zero, "the .25-second ring has ended");

            Vector3 ninth = O + Vector3.right * 30f;
            Assert.That(FeelBrushVfxSystem.TryReserveExplosion(ninth, radius), Is.False,
                "ring retirement must not release the debris/smoke budget before the delayed debris has expired");
            FeelEvents.RaiseExplosion(ninth, radius, 0, default);
            Assert.That(_system.LiveExplosions, Is.Zero, "a direct FeelEvents producer obeys the same admission cap");
            _system.Tick(.93f, .93f); // 1.19 seconds since the admitted eight explosions
            Assert.That(FeelBrushVfxSystem.TryReserveExplosion(ninth, radius), Is.False);
            _system.Tick(.02f, .02f); // 1.21 seconds; the delayed debris can still be alive
            Assert.That(FeelBrushVfxSystem.TryReserveExplosion(ninth, radius), Is.False);
            _system.Tick(.05f, .05f); // 1.26 seconds; reservations are now free
            Assert.That(FeelBrushVfxSystem.TryReserveExplosion(ninth, radius), Is.True);
            FeelEvents.RaiseExplosion(ninth, radius, 0, default);
            Assert.That(_system.LiveExplosions, Is.EqualTo(1));
            Assert.That(FindOnly(_materials[4]).transform.position.x, Is.EqualTo(ninth.x).Within(.001f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Explosion_WaterOrMissingGround_KeepsLightWithoutDrawingGroundRing(bool water)
        {
            Transform floor = _root.transform.Find("WP093 dry floor");
            if (water) floor.gameObject.AddComponent<SurfaceTag>().Surface = SurfaceKind.Water;
            else floor.gameObject.SetActive(false);
            Physics.SyncTransforms();
            bool hit = Physics.Raycast(O + Vector3.up, Vector3.down, out RaycastHit ground, 5f,
                ~0, QueryTriggerInteraction.Ignore);
            Assert.That(hit, Is.EqualTo(water), "fixture must distinguish water from a genuine raycast miss");
            if (water) Assert.That(SurfaceResolver.Resolve(ground.collider), Is.EqualTo(SurfaceKind.Water));

            FeelEvents.RaiseExplosion(O, 2f, 0, default);
            Assert.That(Count(_materials[4]), Is.Zero, "water/miss cannot receive a projected ground ring");
            Assert.That(_system.LiveExplosions, Is.Zero, "LiveExplosions counts ground rings, not reservations");
            Light light = FindLight();
            Assert.That(light.transform.position, Is.EqualTo(O + Vector3.up * .3f));
            Assert.That(light.intensity, Is.EqualTo(30f).Within(.0001f));
            Assert.That(light.range, Is.EqualTo(5f).Within(.0001f));
            _system.Tick(.3f, .3f);
            Assert.That(light.enabled, Is.False);
            Assert.That(FeelBrushVfxSystem.OwnsTdLayers, Is.True, "missing ground must not disable the brush owner");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KillAndLastStroke_ProjectTheirOffsetAlongSlopedGround(bool lastZombie)
        {
            Transform floor = _root.transform.Find("WP093 dry floor");
            floor.rotation = Quaternion.Euler(0f, 0f, 20f);
            Physics.SyncTransforms();
            Vector3 deathPosition = O + Vector3.up * 1.4f;
            FeelEvents.TestLastOfRound = lastZombie ? 1 : 0;
            Kill(deathPosition, 1);
            MeshRenderer kill = FindBloodKill();
            AssertProjectedOnSlope(kill, floor, deathPosition);
            Assert.That(kill.transform.localScale.x, Is.EqualTo(lastZombie ? 1.92f : 1.2f).Within(.0001f));
            if (lastZombie)
            {
                Assert.That(_system.LastStrokes, Is.EqualTo(1));
                AssertProjectedOnSlope(FindInkStroke(), floor, deathPosition);
            }
        }

        [Test]
        public void Bridge_LastZombie_ProtectedKillSurvivesInterleavedMultiAtFullPool()
        {
            for (int i = 0; i < 60; i++) Kill(O, i + 1);
            int previous = FeelEvents.PushHitGroup(77);
            try
            {
                Kill(O, 61); Kill(O, 62);
                FeelEvents.TestLastOfRound = 1;
                Kill(O, 63);
            }
            finally { FeelEvents.PopHitGroup(previous); }
            Assert.That(_system.LastStrokes, Is.EqualTo(1));
            MeshRenderer stroke = FindInkStroke();
            Assert.That(stroke.transform.localScale.x, Is.EqualTo(1.92f).Within(.0001f));
            Assert.That(stroke.transform.position.x, Is.EqualTo(O.x + .768f).Within(.001f));
            Assert.That(Vector3.Dot(stroke.transform.right, Vector3.right), Is.GreaterThan(.999f));
            Assert.That(HasBloodKillOfSize(1.92f), Is.True, "pending death must survive MultiKill's pooled allocation");
            PublishLast(new FeelLastZombie(O, 3, EnemyTier.Standard, _enemyGo.transform));
            Assert.That(_system.LastStrokes, Is.EqualTo(1), "same final death's duplicate cannot produce another stroke");
        }

        [Test]
        public void LastZombie_MismatchedEnemyOrRound_CannotConsumePendingFinalDeath()
        {
            // Temporarily intercept only LastZombie, allowing the production kill bridge to mark the pending context.
            FieldInfo field = typeof(FeelEvents).GetField("LastZombie", StaticFields);
            object subscription = field.GetValue(null);
            field.SetValue(null, null);
            FeelEvents.TestLastOfRound = 1;
            try { Kill(O, 1); }
            finally { field.SetValue(null, subscription); }
            PublishLast(new FeelLastZombie(O, 99, EnemyTier.Standard, _enemyGo.transform));
            PublishLast(new FeelLastZombie(O, 3, EnemyTier.Standard, _root.transform));
            Assert.That(_system.LastStrokes, Is.Zero);
            PublishLast(new FeelLastZombie(O, 3, EnemyTier.Standard, _enemyGo.transform));
            Assert.That(_system.LastStrokes, Is.EqualTo(1));
        }

        [Test]
        public void PlayerDeath_WinsOverSameFrameLastZombie_AndRoundRearms()
        {
            Hit(1);
            var damage = new PlayerDamageInfo(null, 100f, 100f, 100f, 0f, 100f, O, Vector3.left, "WP093 fixture", true);
            FeelEvents.OnPlayerDamaged(damage);
            Assert.That(ActiveQuads(), Is.Zero);
            int hits = _system.HitBursts, kills = _system.KillBursts;
            FeelEvents.TestLastOfRound = 1;
            Kill(O, 1); Hit(2); FeelEvents.RaiseExplosion(O, 2f, 0, default);
            Assert.That(_system.HitBursts, Is.EqualTo(hits));
            Assert.That(_system.KillBursts, Is.EqualTo(kills));
            Assert.That(_system.LastStrokes, Is.Zero);
            Assert.That(ActiveQuads(), Is.Zero);
            RunEvents.PublishRoundStarted(4);
            Hit(2);
            Assert.That(_system.HitBursts, Is.EqualTo(hits + 1));
            Assert.That(_enemyGo.transform.position, Is.EqualTo(O));
            Assert.That(Time.timeScale, Is.EqualTo(1f), "brush layer must not own gameplay time");
        }

        [UnityTest]
        public IEnumerator SceneChange_ClearsDeathAndStamps_RearmsExactlyOnce()
        {
            Hit(1);
            FeelEvents.OnPlayerDamaged(new PlayerDamageInfo(null, 100f, 100f, 100f, 0f, 100f, O, Vector3.left, "fixture", true));
            _temporaryScene = SceneManager.CreateScene("WP093_Faz2_Reset_" + Guid.NewGuid().ToString("N"));
            Assert.That(SceneManager.SetActiveScene(_temporaryScene), Is.True);
            Assert.That(ActiveQuads(), Is.Zero);
            _system.enabled = false; _system.enabled = true;
            _system.enabled = false; _system.enabled = true;
            int hits = _system.HitBursts, stains = _system.StainMarks;
            Hit(1);
            Assert.That(_system.HitBursts, Is.EqualTo(hits + 1), "scene/re-enable must not multiply subscriptions");
            Assert.That(_system.StainMarks, Is.EqualTo(stains + 1), "same LifeId can stain immediately in the new scene");
            yield return null;
        }

        [Test]
        public void LeavingTd_ReleasesOwnershipAndClearsOnlyItsPresentation()
        {
            Hit(1); FeelEvents.RaiseExplosion(O, 2f, 0, default);
            Assert.That(ActiveQuads(), Is.GreaterThan(0));
            // Change only the mode state: no look-mode rig, input, weapon or UI path is exercised in this TD exit test.
            SetModeState(CameraMode.ThirdPerson);
            Assert.That(FeelBrushVfxSystem.OwnsTdLayers, Is.False);
            _system.Tick(0f, .02f);
            Assert.That(ActiveQuads(), Is.Zero);
            Assert.That(_system.LiveDroplets + _system.LiveExplosions, Is.Zero);
            foreach (Light light in _lights) Assert.That(light.enabled, Is.False);
            int hits = _system.HitBursts;
            Hit(2);
            Assert.That(_system.HitBursts, Is.EqualTo(hits));
            SetModeState(CameraMode.TopDown);
            Hit(2);
            Assert.That(_system.HitBursts, Is.EqualTo(hits + 1));
        }

        [Test]
        public void MissingMaterial_ReleasesFallbackImmediately_AndCompleteWarmupRecovers()
        {
            Hit(1); FeelEvents.RaiseExplosion(O, 2f, 0, default);
            var incomplete = (Material[])_materials.Clone(); incomplete[3] = null;
            Assert.That(_system.Warmup(incomplete), Is.False);
            Assert.That(_system.AssetsReady || FeelBrushVfxSystem.OwnsTdLayers, Is.False);
            Assert.That(ActiveQuads(), Is.Zero, "fallback takes ownership without a stale full-brush frame");
            foreach (Light light in _lights) Assert.That(light.enabled, Is.False);
            int hits = _system.HitBursts;
            Hit(2);
            Assert.That(_system.HitBursts, Is.EqualTo(hits));
            Assert.That(_system.Warmup(_materials), Is.True);
            Hit(2);
            Assert.That(_system.HitBursts, Is.EqualTo(hits + 1));
            Assert.That(_system.transform.childCount, Is.EqualTo(92), "recovery must reuse 84 quads + 8 lights");
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Warmup_MultiAndShockRequireRevealTimeMask_OrReleaseFallback(int slot)
        {
            Hit(1); FeelEvents.RaiseExplosion(O, 2f, 0, default);
            Assert.That(ActiveQuads(), Is.GreaterThan(0));
            var incomplete = (Material[])_materials.Clone();
            var altered = new Material(_materials[slot]); _ownedMaterials.Add(altered);
            altered.SetTexture("_TimeMask", null);
            Assert.That(altered.GetTexture("_TimeMask"), Is.Null, "test must remove the reveal mask, not the main mask");
            Assert.That(altered.GetTexture("_Mask"), Is.Not.Null);
            incomplete[slot] = altered;
            Assert.That(_system.Warmup(incomplete), Is.False, MaterialNames[slot] + " reveal mask is required");
            Assert.That(_system.AssetsReady || FeelBrushVfxSystem.OwnsTdLayers, Is.False);
            Assert.That(ActiveQuads(), Is.Zero);
            foreach (Light light in _lights) Assert.That(light.enabled, Is.False);
            Assert.That(_system.Warmup(_materials), Is.True, "complete shared production materials restore ownership");
            Assert.That(FeelBrushVfxSystem.OwnsTdLayers, Is.True);
        }

        [TestCase(3)]
        [TestCase(4)]
        public void Warmup_MultiAndShockRejectWrongShaderContract_OrReleaseFallback(int slot)
        {
            Hit(1); FeelEvents.RaiseExplosion(O, 2f, 0, default);
            // the project's shader library, not Shader.Find (ShaderLibraryTests: null in a build, a broken material in silence)
            Shader shader = DD.Prototype.Rendering.PrototypeShaders.Unlit;
            Assert.That(shader, Is.Not.Null, "a real non-AP037 shader is needed for the negative fixture");
            Assert.That(shader, Is.Not.SameAs(_materials[slot].shader));
            Assert.That(shader.isSupported, Is.True, "negative fixture must fail for its shader contract, not platform support");
            var invalid = (Material[])_materials.Clone();
            var altered = new Material(_materials[slot]) { shader = shader }; _ownedMaterials.Add(altered);
            invalid[slot] = altered;
            Assert.That(_system.Warmup(invalid), Is.False, "a supported but incompatible shader cannot claim TD layers");
            Assert.That(_system.AssetsReady || FeelBrushVfxSystem.OwnsTdLayers, Is.False);
            Assert.That(ActiveQuads(), Is.Zero);
            foreach (Light light in _lights) Assert.That(light.enabled, Is.False);
            Assert.That(_system.Warmup(_materials), Is.True);
        }

        [Test]
        public void Overflow_StainsDropsBlastsAndTransientsRemainInsideFixedPools()
        {
            int children = _system.transform.childCount;
            for (int i = 0; i < 160; i++)
            {
                Hit(i + 1); Kill(O, i + 1);
                FeelEvents.RaiseExplosion(O, 2f, i + 1, default);
            }
            Assert.That(_system.LiveDroplets, Is.InRange(1, 12));
            Assert.That(_system.LiveExplosions, Is.EqualTo(8));
            Assert.That(Count(_materials[2]), Is.EqualTo(40));
            Assert.That(ActiveQuads(), Is.LessThanOrEqualTo(84));
            Assert.That(_system.transform.childCount, Is.EqualTo(children));
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled) Assert.That(Array.IndexOf(_materials, renderer.sharedMaterial), Is.GreaterThanOrEqualTo(0), "no material clones");
            _system.Tick(100f, 100f);
            Assert.That(_system.LiveDroplets + _system.LiveExplosions, Is.Zero);
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled) Assert.That(renderer.sharedMaterial, Is.SameAs(_materials[2]), "only round stains can survive 100 seconds");
        }

        [Test]
        public void PausedTick_DoesNotAdvanceScaledBrushOrBlastLightEnvelopes()
        {
            Kill(O, 1); FeelEvents.RaiseExplosion(O, 2f, 0, default);
            MeshRenderer kill = FindBloodKill(); Light light = FindLight();
            _system.Tick(0f, 10f);
            Assert.That(kill.enabled, Is.True);
            Assert.That(light.enabled, Is.True);
            Assert.That(light.intensity, Is.EqualTo(30f).Within(.0001f));
            _system.Tick(.3f, .3f);
            Assert.That(kill.enabled || light.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator WarmedRealEventBridgeAndTick_ZeroGc_WithPositiveControl()
        {
            for (int i = 0; i < 4; i++)
            {
                Hit(i + 1, critical: true); Kill(O, i + 1);
                FeelEvents.RaiseExplosion(O, 2f, i + 1, default);
                _system.Tick(.016f, .016f);
            }
            yield return null;
            object[] keep = null;
            Assert.That(() =>
            {
                keep = new object[4096];
                for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64];
            }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "invalid GC recorder: positive control missed");
            GC.KeepAlive(keep);
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Hit(i + 100, critical: (i & 1) != 0);
                    Kill(O, i + 100);
                    FeelEvents.RaiseExplosion(O, 2f, i + 100, default);
                    _system.Tick(1f / 60f, 1f / 60f);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "warmed FeelEvents + brush paths allocated");
            Assert.That(_system.LiveDroplets, Is.LessThanOrEqualTo(12));
            Assert.That(_system.LiveExplosions, Is.LessThanOrEqualTo(8));
        }

        // User 10.10 (C-00): every round rides to the impact, its tail running in over TdTailOutSeconds. WP-101 (user 11.10, "çok küçük
        // ve fazla turuncu"; AD r1): a tracer round draws its class's white-hot core (layer 0, added) and its heat halo; every other
        // round its premultiplied body streak (layer 2 with the halos - an added light line vanished on the light stone).
        [Test]
        public void TdTracer_NormalWeaponClasses_TracerCoreOrBodyStreakToTheImpact_TailRunsInSixtyMs()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            ClassicWeaponId[] weapons = { ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg,
                ClassicWeaponId.Lmg1, ClassicWeaponId.CombatShotgun };
            for (int i = 0; i < weapons.Length; i++)
            {
                ProjectileSpawnInfo spawn = TdSpawn(int.MaxValue - i, weapons[i], O + Vector3.forward * i);
                Assert.That(tracer.RecordTdProjectile(in spawn), Is.True);
                tracer.StopTdProjectile(spawn.ProjectileId, spawn.Position + Vector3.right * 3f);
            }
            // layer 2 (premultiplied): the tracers' halos and the body streaks; layer 0 (added): the tracer cores and the plain pellet
            // bars - in record order
            var inLayer2 = new System.Collections.Generic.List<int>();
            var inLayer0 = new System.Collections.Generic.List<int>();
            int tracers = 0;
            for (int i = 0; i < 4; i++)
            {
                VfxWeaponProfile profile = VfxWeaponProfiles.For(weapons[i]);
                bool pellet = weapons[i] == ClassicWeaponId.CombatShotgun;
                bool isTracer = TracerRibbonRenderer.IsTdTracer(profile, int.MaxValue - i, pellet, 0);
                if (isTracer) tracers++;
                if (isTracer || !pellet) inLayer2.Add(i);
                if (isTracer || pellet) inLayer0.Add(i);
            }
            Assert.That(tracers, Is.InRange(1, 3), "the ids give a mix of tracer and plain rounds (the plain pellet)");
            Assert.That(DrawTd(tracer, 2), Is.EqualTo(inLayer2.Count));
            Vector4[] head = TracerBuffer(tracer, "_head");
            Vector4[] extra = TracerBuffer(tracer, "_extra");
            Vector4[] span = TracerBuffer(tracer, "_span");
            for (int k = 0; k < inLayer2.Count; k++)
            {
                int i = inLayer2[k];
                VfxWeaponProfile profile = VfxWeaponProfiles.For(weapons[i]);
                bool isTracer = TracerRibbonRenderer.IsTdTracer(profile, int.MaxValue - i, weapons[i] == ClassicWeaponId.CombatShotgun, 0);
                float width = isTracer ? TracerRibbonRenderer.TdCoreWidth(profile.Class) * TracerRibbonRenderer.TdHaloScale : TracerRibbonRenderer.TdBodyWidth;
                Assert.That(head[k].w, Is.EqualTo(width).Within(.00001f), weapons[i] + (isTracer ? " halo" : " body"));
                Assert.That(extra[k], Is.EqualTo(new Vector4(isTracer ? 0f : TracerRibbonRenderer.TdBodyMinPx, 0f, 0f, 0f)), "TD pixel floor only - no head dot");
                Assert.That(span[k].z, Is.EqualTo(isTracer ? -TracerRibbonRenderer.TdHaloEdgeShare : 0f), "the amber rim on the halo only");
                Assert.That(new Vector3(head[k].x, head[k].y, head[k].z), Is.EqualTo(O + Vector3.forward * i + Vector3.right * 3f));
            }
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(inLayer0.Count), "the white-hot cores and the plain pellet's glint bar");
            head = TracerBuffer(tracer, "_head"); extra = TracerBuffer(tracer, "_extra");
            for (int k = 0; k < inLayer0.Count; k++)
            {
                int i = inLayer0[k];
                bool pellet = weapons[i] == ClassicWeaponId.CombatShotgun;
                VfxWeaponProfile profile = VfxWeaponProfiles.For(weapons[i]);
                bool isTracer = TracerRibbonRenderer.IsTdTracer(profile, int.MaxValue - i, pellet, 0);
                Assert.That(head[k].w, Is.EqualTo(isTracer ? TracerRibbonRenderer.TdCoreWidth(profile.Class) : TracerRibbonRenderer.TdPelletWidth).Within(.00001f), weapons[i].ToString());
                Assert.That(extra[k].x, Is.GreaterThanOrEqualTo(isTracer ? TracerRibbonRenderer.TdCoreMinPx : TracerRibbonRenderer.TdBodyMinPx));
            }
            tracer.TickTd(TracerRibbonRenderer.TdTailOutSeconds - .001f);
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(4));
            Assert.That(tracer.LiveTdSmoke, Is.Zero);
            tracer.TickTd(.002f);
            Assert.That(tracer.LiveTdTracers, Is.Zero);
            Assert.That(DrawTd(tracer, 0), Is.Zero);
        }

        // User 10.10: a round reaching an enemy 30 m out is drawn all the way (the old tracer ended ≈ 5 m out); ReduceFlashes dims the halo.
        [Test]
        public void TdTracer_RidesItsRoundToAFarImpact_HeatHalo_ReduceFlashesDims()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            VfxWeaponProfile ar = VfxWeaponProfiles.For(ClassicWeaponId.AssaultRifle);
            Assert.That(tracer.Emit(O, O + Vector3.forward * 30f, ar, false, false, false), Is.True);
            tracer.TickTd(.08f);   // twice the old 40 ms life
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(1), "still flying");
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Vector4 h = TracerBuffer(tracer, "_head")[0];
            Assert.That(Vector3.Distance(new Vector3(h.x, h.y, h.z), O), Is.EqualTo(Mathf.Min(30f, ar.Speed * .08f)).Within(.01f), "the head is the round");
            float flash = GameFeelSettings.FlashScale;
            try
            {
                GameFeelSettings.FlashScale = 1f;
                Assert.That(DrawTd(tracer, 2), Is.EqualTo(1));
                Assert.That(TracerBuffer(tracer, "_headColor")[0].w, Is.EqualTo(TracerRibbonRenderer.TdHaloAlpha).Within(1e-5f));
                GameFeelSettings.FlashScale = .4f;
                Assert.That(DrawTd(tracer, 2), Is.EqualTo(1));
                Assert.That(TracerBuffer(tracer, "_headColor")[0].w, Is.EqualTo(TracerRibbonRenderer.TdHaloAlpha * .4f).Within(1e-5f), "ReduceFlashes dims the halo");
            }
            finally { GameFeelSettings.FlashScale = flash; }
            tracer.TickTd(1f);   // it arrived at 30 m (≈ 0.12 s) and its tail ran in
            Assert.That(tracer.LiveTdTracers, Is.Zero);
        }

        [Test]
        public void TdTracer_SniperKeepsStoppedHead_CoreTailsOutInSixtyMs_SmokeAtPointFour()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            ProjectileSpawnInfo spawn = TdSpawn(int.MaxValue - 100, ClassicWeaponId.Sr1, O);
            Assert.That(tracer.RecordTdProjectile(in spawn), Is.True);
            Vector3 stopped = O + new Vector3(23f, 0f, 2f);
            tracer.StopTdProjectile(spawn.ProjectileId, stopped);
            tracer.TickTd(TracerRibbonRenderer.TdTailOutSeconds - .001f);   // user 10.10: the core tails out into the impact
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(1));
            Assert.That(tracer.LiveTdSmoke, Is.EqualTo(1));
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Assert.That(TracerBuffer(tracer, "_head")[0].w, Is.EqualTo(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Sniper)).Within(.00001f));
            Assert.That(TracerBuffer(tracer, "_extra")[0], Is.EqualTo(new Vector4(TracerRibbonRenderer.TdHeavyCoreMinPx, 0f, 0f, 0f)));
            Assert.That(DrawTd(tracer, 1), Is.EqualTo(1));
            float firstSmokeAlpha = TracerBuffer(tracer, "_headColor")[0].w;
            tracer.TickTd(.002f);
            Assert.That(tracer.LiveTdTracers, Is.Zero);
            Assert.That(tracer.LiveTdSmoke, Is.EqualTo(1));
            Assert.That(DrawTd(tracer, 0), Is.Zero);
            Assert.That(DrawTd(tracer, 1), Is.EqualTo(1));
            Vector4 head = TracerBuffer(tracer, "_head")[0];
            Assert.That(new Vector3(head.x, head.y, head.z), Is.EqualTo(stopped), "impact stops the head instead of extrapolating the projectile");
            Assert.That(TracerBuffer(tracer, "_headColor")[0].w, Is.LessThan(firstSmokeAlpha));
            tracer.TickTd(.399f - TracerRibbonRenderer.TdTailOutSeconds - .001f); // .399 seconds from birth, not from impact
            Assert.That(tracer.LiveTdSmoke, Is.EqualTo(1));
            tracer.TickTd(.002f);
            Assert.That(tracer.LiveTdSmoke, Is.Zero);
            Assert.That(DrawTd(tracer, 1), Is.Zero);
        }

        // WP-101 (user 11.10: "çok küçük ve fazla turuncu ... ultra gerçekçi"): white-hot core, warm white-yellow halo - no orange
        // tracer; the streak is the round's motion blur (v × 1/60 s) and grows with the class; the body streak is much shorter.
        [Test]
        public void TdTracer_Wp101_WhiteHotCore_NoOrange_RealScaleByClass()
        {
            Color.RGBToHSV(TracerRibbonRenderer.TdCoreHead, out float coreHue, out float coreSat, out _);
            Assert.That(coreSat, Is.LessThanOrEqualTo(.12f), "the core at the bullet is white-hot");
            Color.RGBToHSV(TracerRibbonRenderer.TdHaloColor, out float haloHue, out float haloSat, out _);
            Assert.That(haloHue * 360f, Is.InRange(30f, 50f), "the halo is warm yellow, not orange (R3: 22°)");
            Assert.That(haloSat, Is.LessThanOrEqualTo(.45f), "the halo is a pale warm veil (R3: S 1)");
            Assert.That(TracerRibbonRenderer.TdHaloAlpha, Is.LessThanOrEqualTo(.65f), "the halo veils, never paints the ground (R3: α 1)");
            Color.RGBToHSV(TracerRibbonRenderer.TdCoreTail, out float tailHue, out _, out _);
            Assert.That(tailHue * 360f, Is.GreaterThanOrEqualTo(25f), "the fading end only warms toward amber");
            Assert.That(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Smg), Is.LessThan(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle)));
            Assert.That(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle), Is.LessThan(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Lmg)));
            Assert.That(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Lmg), Is.LessThan(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Sniper)));
            Assert.That(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle), Is.GreaterThanOrEqualTo(.10f), "≈ 3 × the R3 3.5 cm core (sonra-r1: × 2 read as a 2 px thread)");
            TracerRibbonRenderer tracer = AttachTdTracer();
            VfxWeaponProfile ar = VfxWeaponProfiles.For(ClassicWeaponId.AssaultRifle);
            Assert.That(tracer.Emit(O, O + Vector3.forward * 60f, ar, false, false, false), Is.True);
            tracer.TickTd(.1f);
            Assert.That(DrawTd(tracer, 0), Is.EqualTo(1));
            Vector4 h = TracerBuffer(tracer, "_head")[0], t = TracerBuffer(tracer, "_tail")[0];
            float streak = Vector3.Distance(new Vector3(h.x, h.y, h.z), new Vector3(t.x, t.y, t.z));
            Assert.That(streak, Is.EqualTo(Mathf.Clamp(ar.Speed * TracerRibbonRenderer.TdExposureSeconds, TracerRibbonRenderer.TdTailMin, TracerRibbonRenderer.TdTailMax)).Within(.01f),
                "a tracer streak is the round's one-exposure blur");
            Assert.That(Mathf.Clamp(ar.Speed * TracerRibbonRenderer.TdBodyExposureSeconds, TracerRibbonRenderer.TdBodyMin, TracerRibbonRenderer.TdBodyMax),
                Is.LessThan(streak * .5f), "the body streak is much shorter than the tracer");
            Assert.That(TracerRibbonRenderer.TdTracerEveryN(VfxWeaponClass.Sniper), Is.EqualTo(1), "every sniper round is a tracer");
        }

        [Test]
        public void TdTracer_FortyEightRecords_RejectOverflow_ClearReleasesAndPauseKeepsAges()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            for (int i = 0; i < 48; i++)
            {
                ProjectileSpawnInfo spawn = TdSpawn(int.MaxValue - i, ClassicWeaponId.CombatShotgun, O);
                Assert.That(tracer.RecordTdProjectile(in spawn), Is.True);
            }
            ProjectileSpawnInfo excess = TdSpawn(int.MaxValue - 100, ClassicWeaponId.AssaultRifle, O);
            Assert.That(tracer.RecordTdProjectile(in excess), Is.False);
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(48));
            tracer.TickTd(0f); tracer.TickTd(-1f);
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(48), "zero/negative presentation delta cannot age paused tracers");
            tracer.StopTdProjectile(int.MaxValue, O + Vector3.right, cleared: true);
            tracer.TickTd(0f);
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(47));
            Assert.That(tracer.RecordTdProjectile(in excess), Is.True, "cleared projectile releases its record immediately");
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(48));
            // User 10.10 (C-00): a tracer rides its projectile to the impact - the old 40 ms life no longer ends it
            tracer.TickTd(.041f);
            Assert.That(tracer.LiveTdTracers, Is.EqualTo(48), "in flight past 41 ms");
            tracer.TickTd(TracerRibbonRenderer.TdMaxFlightSeconds);
            Assert.That(tracer.LiveTdTracers, Is.Zero, "a record that never hears its impact ends at the flight ceiling");
            ProjectileSpawnInfo sniper = TdSpawn(int.MaxValue - 101, ClassicWeaponId.Sr1, O);
            Assert.That(tracer.RecordTdProjectile(in sniper), Is.True);
            tracer.Clear();
            Assert.That(tracer.LiveTdTracers + tracer.LiveTdSmoke, Is.Zero, "scene clear resets both shared record counters");
        }

        [Test]
        public void TdCasing_PreAdmissionRecyclesOldest_ThenNewCheckoutKeepsTwelve_WithoutFakeLaunch()
        {
            PrototypePool<CasingEffect> pool = BuildCasingPool();
            Assert.That(VfxDressing.TdCasingBudget, Is.EqualTo(12));
            for (int i = 0; i < 12; i++) Assert.That(pool.Get(O + Vector3.right * i, Quaternion.identity), Is.Not.Null);
            CasingEffect oldest = pool.Spawned[0], second = pool.Spawned[1];
            int launchCount = CasingEvents.LaunchedCount;
            VfxDressing.MakeTdCasingRoom(pool);
            Assert.That(pool.CountSpawned, Is.EqualTo(11), "make room before the real launch checks out the twelfth casing");
            Assert.That(oldest.IsSpawned, Is.False);
            Assert.That(pool.Spawned[0], Is.SameAs(second), "ordered spawned list identifies oldest, not arbitrary pool index");
            CasingEffect next = pool.Get(O + Vector3.up, Quaternion.identity);
            Assert.That(next, Is.SameAs(oldest));
            Assert.That(pool.CountSpawned, Is.EqualTo(12));
            for (int i = 0; i < 100; i++)
            {
                VfxDressing.MakeTdCasingRoom(pool);
                Assert.That(pool.Get(O, Quaternion.identity), Is.Not.Null);
                Assert.That(pool.CountSpawned, Is.EqualTo(12));
            }
            Assert.That(pool.CountTotal, Is.EqualTo(24), "pool storage stays prewarmed; visible TD budget remains twelve");
            Assert.That(CasingEvents.LaunchedCount, Is.EqualTo(launchCount), "budget recycling must not synthesize casing/sound launches");
        }

        [UnityTest]
        public IEnumerator TdTracerAndCasing_WarmedPublicSeamsZeroGc_WithPositiveControl()
        {
            TracerRibbonRenderer tracer = AttachTdTracer();
            PrototypePool<CasingEffect> pool = BuildCasingPool();
            for (int i = 0; i < 12; i++) pool.Get(O, Quaternion.identity);
            for (int i = 0; i < 32; i++)
            {
                ProjectileSpawnInfo spawn = TdSpawn(int.MaxValue - i, (i & 1) == 0 ? ClassicWeaponId.Sr1 : ClassicWeaponId.AssaultRifle, O);
                tracer.RecordTdProjectile(in spawn); tracer.StopTdProjectile(spawn.ProjectileId, O + Vector3.right * 5f);
                tracer.TickTd(.05f); VfxDressing.MakeTdCasingRoom(pool); pool.Get(O, Quaternion.identity);
            }
            tracer.Clear();
            yield return null;
            object[] keep = null;
            Assert.That(() =>
            {
                keep = new object[4096];
                for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64];
            }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "invalid GC recorder: positive control missed");
            GC.KeepAlive(keep);
            Assert.That(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    ProjectileSpawnInfo spawn = TdSpawn(int.MaxValue - 1000 - i,
                        (i & 1) == 0 ? ClassicWeaponId.Sr1 : ClassicWeaponId.AssaultRifle, O);
                    tracer.RecordTdProjectile(in spawn); tracer.StopTdProjectile(spawn.ProjectileId, O + Vector3.right * 5f);
                    tracer.TickTd(.05f); VfxDressing.MakeTdCasingRoom(pool); pool.Get(O, Quaternion.identity);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "warmed TD tracer admission/stop/tick and casing budget allocated");
            Assert.That(tracer.LiveTdTracers + tracer.LiveTdSmoke, Is.LessThanOrEqualTo(48));
            Assert.That(pool.CountSpawned, Is.EqualTo(12));
        }

        private TracerRibbonRenderer AttachTdTracer()
        {
            _hasTracerFixture = true;
            FieldInfo instance = typeof(TracerRibbonRenderer).GetField("s_instance", StaticFields);
            _previousTracer = (TracerRibbonRenderer)instance.GetValue(null);
            _previousInterimTrails = ProjectileDriver.DrawInterimTrails;
            if (_previousTracer != null)
            {
                _previousTracerEnabled = _previousTracer.enabled;
                _previousTracer.enabled = false;
            }
            SaveAndClearDelegates(typeof(ProjectileEvents));
            instance.SetValue(null, null); // avoid Awake's duplicate-owner Destroy path
            var owner = new GameObject("WP093 TD tracer fixture"); owner.transform.SetParent(_root.transform, false);
            _tracer = owner.AddComponent<TracerRibbonRenderer>();
            _tracer.enabled = false; // public TickTd alone advances this deterministic fixture
            Assert.That(_tracer.Ready, Is.True, "real DD/VFX Ribbon shader must be present");
            return _tracer;
        }

        private PrototypePool<CasingEffect> BuildCasingPool()
        {
            var prefabGo = new GameObject("WP093 casing prefab"); prefabGo.transform.SetParent(_root.transform, false);
            CasingEffect prefab = prefabGo.AddComponent<CasingEffect>(); prefabGo.SetActive(false);
            // Construct the real typed pool under our root, avoiding global PoolService registration/cleanup side effects.
            return (PrototypePool<CasingEffect>)Activator.CreateInstance(typeof(PrototypePool<CasingEffect>),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { prefab, _root.transform, 24, 24, PoolExhaustedPolicy.ReturnNull }, null);
        }

        private static ProjectileSpawnInfo TdSpawn(int id, ClassicWeaponId weapon, Vector3 origin) =>
            new ProjectileSpawnInfo(id, int.MaxValue, weapon,
                weapon == ClassicWeaponId.CombatShotgun ? ProjectileKind.Pellet : ProjectileKind.Bullet,
                WeaponCaliber.Medium, origin, Vector3.right, 100f, 0);

        private static int DrawTd(TracerRibbonRenderer tracer, int layer) =>
            (int)typeof(TracerRibbonRenderer).GetMethod("DrawTd", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(tracer, new object[] { layer });

        private static Vector4[] TracerBuffer(TracerRibbonRenderer tracer, string name) =>
            (Vector4[])typeof(TracerRibbonRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tracer);

        private static void AssertProjectedOnSlope(MeshRenderer renderer, Transform floor, Vector3 deathPosition)
        {
            Vector3 normal = floor.up;
            Vector3 topPlane = floor.position + normal * .1f; // fixture BoxCollider height .2
            Assert.That(Vector3.Dot(renderer.transform.forward, normal), Is.GreaterThan(.9999f),
                "quad normal must follow the hit ground normal");
            Assert.That(Vector3.Dot(renderer.transform.position - topPlane, normal), Is.EqualTo(.015f).Within(.002f),
                "directional offset must stay tangent to the ground after the .015m surface lift");
            Assert.That(renderer.transform.position.x - deathPosition.x, Is.GreaterThan(.3f),
                "shot-directed offset must survive projection");
        }

        private void Hit(int life, float health = 12f, bool critical = false)
        {
            var result = new DamageResult { HealthDamage = health, ArmorDamage = health <= 0f ? 8f : 0f,
                WasArmored = health <= 0f, Region = critical ? BodyRegion.Head : BodyRegion.UpperBody };
            var meta = new DamageEventMeta(life, 1, 1, DamageKind.Direct, ArmorSlot.None, 100f, 100f - health,
                0f, 0f, 0f, 0f, 0f, 0f, critical ? health : 0f, critical ? 0f : health, 0f, 0f, 0f, 0f);
            var info = new EnemyDamageInfo(_enemyGo.transform, EnemyTier.Standard, in result, O + Vector3.up,
                Vector3.right + Vector3.up, DamageSourceKind.Bullet, 1f, .5f, false, in meta);
            DamageEvents.RaiseEnemyDamaged(in info);
        }

        private void Kill(Vector3 position, int life, bool heavy = false)
        {
            var info = new EnemyDeathInfo(_enemyGo.transform, heavy ? EnemyTier.Elite : EnemyTier.Standard,
                position, Vector3.right + Vector3.up, BodyRegion.UpperBody, DamageSourceKind.Bullet,
                false, heavy, 3, 0, 0, 0f, 0f, life);
            DamageEvents.RaiseEnemyDied(in info);
        }

        private static void PublishLast(FeelLastZombie payload)
        {
            var handler = (Action<FeelLastZombie>)typeof(FeelEvents).GetField("LastZombie", StaticFields).GetValue(null);
            Assert.That(handler, Is.Not.Null);
            handler(payload);
        }

        private void SaveAndClearDelegates(Type type)
        {
            foreach (EventInfo signal in type.GetEvents(StaticFields))
            {
                FieldInfo field = type.GetField(signal.Name, StaticFields);
                Assert.That(field, Is.Not.Null, "event backing field: " + signal.Name);
                _savedDelegates.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
                field.SetValue(null, null);
            }
        }

        private static void SetModeState(CameraMode mode) =>
            typeof(CameraModeService).GetField("s_current", StaticFields).SetValue(null, mode);

        private int ActiveQuads()
        {
            int count = 0; foreach (MeshRenderer renderer in _renderers) if (renderer.enabled) count++;
            return count;
        }

        private int Count(Material material)
        {
            int count = 0; foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled && renderer.sharedMaterial == material) count++;
            return count;
        }

        private MeshRenderer FindOnly(Material material)
        {
            MeshRenderer found = null;
            foreach (MeshRenderer renderer in _renderers)
            {
                if (!renderer.enabled || renderer.sharedMaterial != material) continue;
                Assert.That(found, Is.Null, "expected one active " + material.name);
                found = renderer;
            }
            Assert.That(found, Is.Not.Null, "missing active " + material.name);
            return found;
        }

        private MeshRenderer FindBloodKill()
        {
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled && (renderer.sharedMaterial == _materials[0] || renderer.sharedMaterial == _materials[1])
                    && Tint(renderer) == FeelBrushVfxPolicy.BloodTint) return renderer;
            Assert.Fail("missing blood kill quad"); return null;
        }

        private MeshRenderer FindInkStroke()
        {
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled && renderer.sharedMaterial == _materials[0] && Tint(renderer) == FeelBrushVfxPolicy.InkTint) return renderer;
            Assert.Fail("missing last-zombie ink stroke"); return null;
        }

        private bool HasBloodKillOfSize(float size)
        {
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled && (renderer.sharedMaterial == _materials[0] || renderer.sharedMaterial == _materials[1])
                    && Tint(renderer) == FeelBrushVfxPolicy.BloodTint && Mathf.Abs(renderer.transform.localScale.x - size) < .0001f) return true;
            return false;
        }

        private static Color Tint(MeshRenderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block.GetColor("_Tint");
        }

        private void AssertTint(Material material, Color tint)
        {
            int found = 0;
            foreach (MeshRenderer renderer in _renderers)
                if (renderer.enabled && renderer.sharedMaterial == material)
                {
                    Color actual = Tint(renderer);
                    Assert.That(Vector4.Distance((Vector4)actual, (Vector4)tint), Is.LessThan(1e-6f),
                        "MPB round-trip float precision; authored tint must remain within one millionth.");
                    found++;
                }
            Assert.That(found, Is.GreaterThan(0));
        }

        private Light FindLight()
        {
            foreach (Light light in _lights) if (light.enabled) return light;
            Assert.Fail("missing active blast light"); return null;
        }
    }
}
