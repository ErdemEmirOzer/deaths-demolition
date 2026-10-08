#if UNITY_EDITOR
using System.Reflection;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.PlayMode
{
    [TestFixture]
    public sealed class ZombieTransitionP3PlayModeTests
    {
        private GameObject _body;
        private ZombieSkinnedVisual _visual;
        private Animator _animator;
        private const float Dt = 1f / 60f;
        private static readonly MethodInfo Begin = typeof(ZombieSkinnedVisual).GetMethod("BeginTransition", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Art/Characters/Zombie/Zombie_AP007.prefab");
            Assert.IsNotNull(prefab);
            _body = Object.Instantiate(prefab); _body.transform.position = new Vector3(0f, 150f, 0f);
            _visual = _body.GetComponentInChildren<ZombieSkinnedVisual>(true);
            _animator = _body.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(_visual); Assert.IsNotNull(_animator); Assert.IsNotNull(Begin);
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            Assert.IsTrue(_visual.Authored);
        }
        [TearDown]
        public void TearDown() { if (_body != null) Object.DestroyImmediate(_body); }

        [Test]
        public void P3_EveryClip_RealAnimator_MotorYaw_AttachOut_AndMirror()
        {
            for (int id = 0; id < ZombieTransitionData.Clips.Length; id++)
            for (int right = 0; right < (id >= 6 && id <= 8 ? 2 : 1); right++)
            {
                _body.transform.rotation = Quaternion.identity;
                _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
                string name = ZombieTransitionData.Clips[id];
                var data = _visual.AnimationSet.Find(name).transition;
                Assert.IsTrue((bool)Begin.Invoke(_visual, new object[] { id, right == 1, 2.7f, Quaternion.identity }), name);
                bool seen = false; float yaw = 0f; int ticks = 0;
                while (_visual.TransitionActive && ticks++ < 300)
                {
                    float requested = data.kind == "stop" ? 0f : 2.7f;
                    Assert.IsTrue(_visual.StepTransition(Vector3.forward, requested, 2.7f, true, Dt, out Vector3 v, out float y), name);
                    _body.transform.position += v * Dt;
                    _body.transform.rotation = Quaternion.AngleAxis(y, Vector3.up) * _body.transform.rotation;
                    yaw += y;
                    _visual.SetSpeed(Mathf.Max(0f, Vector3.Dot(v, _body.transform.forward)), Dt);
                    _animator.Update(Dt);
                    string state = name + (right == 1 ? "_R" : "");
                    seen |= _animator.GetCurrentAnimatorStateInfo(0).IsName(state)
                        || (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).IsName(state));
                }
                Assert.IsTrue(seen, name + " not played"); Assert.Less(ticks, 300, name + " stuck");
                Assert.IsFalse(_animator.applyRootMotion, name);
                float expected = ZombieTransitionData.NominalYaw(name) * (right == 1 ? 1f : -1f);
                Assert.That(yaw, Is.EqualTo(expected).Within(0.01f), name);
                Assert.That(_visual.LastAttachOutPhase, Is.EqualTo(data.attachOutPhase).Within(1e-5f), name);
                Assert.AreEqual(1, _visual.TransitionCompletions, name);
            }
        }

        [Test]
        public void P3_IntentStartStop_StumbleAndCombatInterrupt_ClearMotorAndMirror()
        {
            Assert.IsTrue(_visual.StepTransition(Vector3.forward, 2.7f, 0f, true, Dt, out _, out _));
            Assert.AreEqual(ZombieTransitionData.Clips[0], _visual.TransitionClip);
            _visual.PlayHeavyStagger(); Assert.IsFalse(_visual.TransitionActive);
            _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            _visual.StepTransition(Vector3.forward, 2.7f, 2.7f, true, Dt, out _, out _);
            Assert.IsTrue(_visual.StepTransition(Vector3.zero, 0f, 2.7f, true, Dt, out _, out _));
            StringAssert.StartsWith("AP033_zombie_stop_walk_", _visual.TransitionClip);
            _visual.PlayDeath(0); Assert.IsFalse(_visual.TransitionActive);
            _visual.ResetForSpawn(EnemyTier.Special, 4.79f, "A", 0, false);
            _visual.PlayStumble(); Assert.AreEqual(ZombieTransitionData.Clips[9], _visual.TransitionClip);
            Assert.IsFalse(_visual.StepTransition(Vector3.forward, 4.79f, 2.7f, false, Dt, out _, out _));
            Assert.IsFalse(_visual.TransitionActive);
            _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            Assert.IsTrue((bool)Begin.Invoke(_visual, new object[] { 6, true, 2.7f, Quaternion.identity }));
            _animator.Update(0.2f);
            var events = _body.GetComponentInChildren<CharacterAnimationEvents>(true);
            Assert.IsNotNull(events);
            events.pivot_plant(0); Assert.AreEqual(1, events.LastInt);
            events.foot_l(); Assert.AreEqual(CharacterAnimationEvent.FootRight, events.Last);
            _visual.PlayHeavyStagger(); Assert.AreEqual(0, events.ZombieTransitionMirrorState);
            _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            _visual.StepTransition(Vector3.forward, 2.7f, 0f, true, Dt, out _, out _);
            _visual.PlayAttack(ZombieAttackStyle.Swipe, 0.5f); Assert.IsFalse(_visual.TransitionActive);
            _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, true);
            Assert.IsTrue(_visual.IsRising);
            Assert.IsFalse(_visual.StepTransition(Vector3.forward, 2.7f, 0f, true, Dt, out _, out _));
            _visual.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            Assert.AreEqual(0, _visual.TransitionStarts); Assert.AreEqual(0, _visual.TransitionCompletions);
            Assert.AreEqual(0, events.ZombieTransitionMirrorState);
            Assert.IsTrue(_visual.StepTransition(Vector3.forward, 2.7f, 0f, true, Dt, out _, out _));
            _visual.SetWaiting(true); Assert.IsFalse(_visual.TransitionActive);
        }
    }
}
#endif
