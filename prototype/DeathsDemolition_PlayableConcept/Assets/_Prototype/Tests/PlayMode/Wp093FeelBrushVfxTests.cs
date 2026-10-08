using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
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
        private FeelBrushVfxSystem _system, _previousOwner;
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
                Assert.That(Mathf.Abs(move.z), Is.LessThanOrEqual(Mathf.Abs(move.x) * Mathf.Tan(15f * Mathf.Deg2Rad) + .0001f));
            }
            Assert.That(forward, Is.InRange(4, 8));
            Assert.That(backward, Is.GreaterThan(0), "§4.2: exit spray must travel against the shot");

            RunEvents.PublishRoundStarted(2);
            Hit(life: 2, critical: true);
            Assert.That(_system.LiveDroplets, Is.LessThanOrEqual(12));
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
            Assert.That(ActiveQuads(), Is.LessThanOrEqual(84));
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
            Assert.That(_system.LiveDroplets, Is.LessThanOrEqual(12));
            Assert.That(_system.LiveExplosions, Is.LessThanOrEqual(8));
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
                if (renderer.enabled && renderer.sharedMaterial == material) { Assert.That(Tint(renderer), Is.EqualTo(tint)); found++; }
            Assert.That(found, Is.GreaterThan(0));
        }

        private Light FindLight()
        {
            foreach (Light light in _lights) if (light.enabled) return light;
            Assert.Fail("missing active blast light"); return null;
        }
    }
}
