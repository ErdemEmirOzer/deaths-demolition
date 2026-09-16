using System.Collections.Generic;
using System.Globalization;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.EditMode
{
    /// <summary>WP-032 · Generated telegraph clips + layer, IK pass on the zombie base layer, prefab IK components, stride/rate bookkeeping.</summary>
    [TestFixture]
    [Category("WP032")]
    public sealed class ArtAnimationPolishTests
    {
        [Test]
        public void ZombiController_TelegraphKatmani_3UretilmisAdditiveKlip_IkPass()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            var c = set.Controller as AnimatorController;
            Assert.IsNotNull(c);
            int tele = ArtAnimatorBuilder.LayerIndex(c, C.TelegraphLayer);
            Assert.AreEqual(3, tele, "telegraph katmanı 4. katman");
            Assert.AreEqual(AnimatorLayerBlendingMode.Additive, c.layers[tele].blendingMode);
            Assert.IsNotNull(c.layers[tele].avatarMask, "üst gövde maskesi");
            Assert.IsTrue(c.layers[0].iKPass, "taban katman IK pass (LookAt + foot IK)");
            bool hasTrigger = false, hasStyle = false, hasRate = false;
            foreach (AnimatorControllerParameter p in c.parameters)
            {
                if (p.name == C.Telegraph && p.type == AnimatorControllerParameterType.Trigger) hasTrigger = true;
                if (p.name == C.TelegraphStyle && p.type == AnimatorControllerParameterType.Int) hasStyle = true;
                if (p.name == C.TelegraphRate && p.type == AnimatorControllerParameterType.Float) hasRate = true;
            }

            Assert.IsTrue(hasTrigger && hasStyle && hasRate, "Telegraph / TelegraphStyle / TelegraphRate parametreleri");
            for (int i = 0; i < 3; i++)
            {
                AnimatorState s = ArtAnimatorBuilder.FindState(c, tele, C.TelegraphState + "_" + i);
                Assert.IsNotNull(s, "Telegraph_" + i);
                var clip = s.motion as AnimationClip;
                Assert.IsNotNull(clip, "üretilmiş klip");
                Assert.AreEqual(C.TelegraphClips[i], clip.name);
                Assert.IsTrue(clip.humanMotion, clip.name + " Humanoid (kas eğrileri)");
                Assert.AreEqual(1f, clip.length, 1e-3f, clip.name + " 1 s");
                Assert.IsFalse(clip.isLooping);
                Assert.IsTrue(s.speedParameterActive && s.speedParameter == C.TelegraphRate);
                Assert.AreEqual(1, s.transitions.Length, "→ Empty");
                // frame 0 is the (zero) reference pose of every curve: the additive delta starts and ends at rest
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, b);
                    Assert.AreEqual(0f, curve.Evaluate(0f), 1e-4f, clip.name + " " + b.propertyName + " t=0");
                    Assert.AreEqual(0f, curve.Evaluate(1f), 1e-4f, clip.name + " " + b.propertyName + " t=1");
                    Assert.Greater(Mathf.Abs(curve.Evaluate(0.75f)), 0.05f, clip.name + " " + b.propertyName + " tepe");
                }
            }

            // the DEC-029 window lives on the base layer: the telegraph layer has no hit_window events at all
            foreach (ChildAnimatorState s in c.layers[tele].stateMachine.states)
            {
                var clip = s.state.motion as AnimationClip;
                if (clip != null) Assert.AreEqual(0, AnimationUtility.GetAnimationEvents(clip).Length, clip.name + " olay taşımamalı");
            }
        }

        [Test]
        public void Prefablar_LookAt_FootIk_Bilesenleri()
        {
            var zombie = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.ZombiePrefab);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.PlayerPrefab);
            var zv = zombie.GetComponent<ZombieSkinnedVisual>();
            var pv = player.GetComponent<PlayerSkinnedVisual>();
            Assert.IsNotNull(zv.LookAt); Assert.IsNotNull(zv.FootIk);
            Assert.IsNotNull(pv.LookAt); Assert.IsNotNull(pv.FootIk);
            Assert.AreEqual(zv.Animator.gameObject, zv.LookAt.gameObject, "LookAt Animator ile aynı nesnede (OnAnimatorIK)");
            Assert.AreEqual(pv.Animator.gameObject, pv.FootIk.gameObject);
            var pc = pv.AnimationSet.Controller as AnimatorController;
            Assert.IsTrue(pc.layers[ArtAnimatorBuilder.LayerIndex(pc, C.AimLayer)].iKPass, "oyuncu IK pass (aim_layer)");
        }

        [Test]
        public void StrideEslemesi_ManifestHizlari_KaymaTablosu()
        {
            var zombie = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.ZombieAnimationSet);
            var player = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.PlayerAnimationSet);
            var config = new EnemyConfigSection();
            var rows = new List<string>();
            // Zombie gaits at the tier speeds (rate inside the driver band → slip 0)
            (string clip, float speed)[] gaits =
            {
                (C.ZombieShambleA, config.Find(EnemyTier.Standard).MoveSpeed), (C.ZombieShambleB, config.Find(EnemyTier.Standard).MoveSpeed), (C.ZombieShambleC, config.Find(EnemyTier.Standard).MoveSpeed),
                (C.ZombieShambleB, config.Find(EnemyTier.Elite).MoveSpeed), (C.ZombieRun, config.Find(EnemyTier.Special).MoveSpeed)
            };
            foreach ((string clip, float speed) g in gaits)
            {
                CharacterAnimationSet.Entry e = zombie.Find(g.clip);
                float honest = StrideMatch.HonestRate(g.speed, e.speedMps);
                float rate = Mathf.Clamp(honest, ZombieSkinnedVisual.MinGaitRate, ZombieSkinnedVisual.MaxGaitRate);
                float slip = StrideMatch.SlipPerStep(g.speed, e.speedMps, rate, e.seconds);
                rows.Add(g.clip + " v=" + g.speed + " rate=" + rate.ToString("F2", CultureInfo.InvariantCulture) + " stride=" + StrideMatch.StrideMeters(e.speedMps, e.seconds).ToString("F2", CultureInfo.InvariantCulture) + " slip/step=" + (slip * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm");
                Assert.LessOrEqual(slip, 0.02f, g.clip + " @ " + g.speed + " m/s kayma > 2 cm/adım");
            }

            // Player: forward walk honest at 6.2; back / strafe cycles hit the 2.4 cap → reported slip (AP-009B: run / strafe cycles)
            float v = player.LocomotionReferenceSpeed;
            float fwd = StrideMatch.SlipPerStep(v, player.SpeedOf(C.PlayerWalkFwd), PlayerSkinnedVisual.ChildTimeScale(v, player.SpeedOf(C.PlayerWalkFwd)), player.Find(C.PlayerWalkFwd).seconds);
            float back = StrideMatch.SlipPerStep(v, player.SpeedOf(C.PlayerWalkBack), PlayerSkinnedVisual.ChildTimeScale(v, player.SpeedOf(C.PlayerWalkBack)), player.Find(C.PlayerWalkBack).seconds);
            float strafe = StrideMatch.SlipPerStep(v, player.SpeedOf(C.PlayerStrafeLeft), PlayerSkinnedVisual.ChildTimeScale(v, player.SpeedOf(C.PlayerStrafeLeft)), player.Find(C.PlayerStrafeLeft).seconds);
            rows.Add("player fwd slip/step=" + (fwd * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm, back=" + (back * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm (cap " + PlayerSkinnedVisual.MaxLocoRate + "), strafe=" + (strafe * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm (cap)");
            Debug.Log("[DD-TEST] wp032.stride\n" + string.Join("\n", rows));
            Assert.LessOrEqual(fwd, 0.02f, "ileri yürüyüş 6,2 m/s'de kaymamalı");
            Assert.Greater(strafe, 0.02f, "strafe döngüsü 6,2 m/s'de tavana çarpar — AP-009B devri (bilinen)");
        }
    }
}
