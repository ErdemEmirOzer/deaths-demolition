using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    [TestFixture]
    public sealed class ZombieTransitionP3Tests
    {
        [Test]
        public void P3_TenNonLoopingHumanoidClips_MotorDataAndStates_Gait22Unchanged()
        {
            var m = ArtAnimationClipContract.Manifest();
            Assert.AreEqual(254, m.Count);
            Assert.AreEqual(22, CharacterAnimatorContract.ZombieGaitClips.Length);
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            Assert.IsNotNull(set);
            var controller = set.Controller as AnimatorController;
            Assert.IsNotNull(controller);
            foreach (string name in ZombieTransitionData.Clips)
            {
                var spec = m[name]; var e = set.Find(name);
                Assert.IsFalse(spec.loop, name); Assert.IsFalse(spec.rootXZ || spec.rootYaw, name + " must stay in-place at import");
                Assert.IsTrue(spec.transition.Valid(spec.frames), name);
                Assert.IsNotNull(e, name); Assert.IsNotNull(e.clip, name);
                Assert.IsFalse(e.clip.isLooping, name); Assert.IsTrue(e.clip.isHumanMotion, name);
                Assert.AreEqual(0f, e.unitySpeedMps, name + " must not enter gait calibration");
                Assert.IsTrue(e.transition.Valid(e.frames), name);
                Assert.IsNotNull(ArtAnimatorBuilder.FindState(controller, 0, name), name);
                if (ZombieTransitionData.NominalYaw(name) > 0f)
                {
                    var mirrored = ArtAnimatorBuilder.FindState(controller, 0, name + "_R");
                    Assert.IsNotNull(mirrored, name + "_R"); Assert.IsTrue(mirrored.mirror);
                }
            }
        }

        [Test]
        public void P3_AuthoredDriveWindows_NoTravelOutside_NoStopYaw_90And180Scaled()
        {
            var m = ArtAnimationClipContract.Manifest();
            foreach (string name in ZombieTransitionData.Clips)
            {
                var spec = m[name]; var d = spec.transition;
                Assert.That(d.Travel(-10f, d.translate.x).sqrMagnitude, Is.LessThan(1e-8f), name);
                Assert.That(d.Travel(d.translate.y, spec.frames + 10f).sqrMagnitude, Is.LessThan(1e-8f), name);
                Vector2 sum = Vector2.zero;
                for (float f = 0; f < spec.frames - 1; f += 0.25f) sum += d.Travel(f, Mathf.Min(f + 0.25f, spec.frames - 1));
                Assert.That((sum - d.Travel(0f, spec.frames - 1)).magnitude, Is.LessThan(1e-4f), name);
                if (d.IsTurn)
                {
                    float total = 0f, nominal = ZombieTransitionData.NominalYaw(name);
                    for (float f = 0; f < spec.frames - 1; f += 0.25f)
                        total += nominal * (d.TurnProgress(Mathf.Min(f + 0.25f, spec.frames - 1)) - d.TurnProgress(f));
                    Assert.That(total, Is.EqualTo(nominal).Within(0.001f), name + " must use nominal yaw, not raw83/165");
                }
                else Assert.AreEqual(0f, d.TurnProgress(spec.frames - 1), name + " visual yaw never drives facing");
            }
            Assert.That(m[ZombieTransitionData.Clips[9]].transition.attachOutPhase, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.AreEqual(CharacterAnimatorContract.ZombieWalkLurchAV4, m[ZombieTransitionData.Clips[9]].transition.attachOutClip);
        }
    }
}
