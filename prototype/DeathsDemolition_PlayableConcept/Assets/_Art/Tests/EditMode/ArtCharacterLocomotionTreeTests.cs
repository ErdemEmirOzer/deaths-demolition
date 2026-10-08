using System;
using System.Collections.Generic;
using System.Globalization;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-082 R3 (S-06, LOCO-SOZLESME v1 §2–§4, §11) · the AP-026 v002 hero locomotion as built: the AP026 manifest's 121 clips
    /// imported on the v1.1 clip contract; the player <c>Locomotion</c> state = Simple1D on LocoSpeed (stand · Walk8 · Run8 · Sprint8),
    /// each ring a SimpleDirectional2D of the eight loops at their nominal unit positions with the contract time scales; the 96 v4
    /// states on V4Time with the per-clip exit time / loop offset; the set's tables equal the tree; and the AP-026 clips face the
    /// same way on the hero as the AP-022 clips the game shipped with (the 180° import offset holds for this export too).
    /// </summary>
    [TestFixture]
    [Category("WP082")]
    public sealed class ArtCharacterLocomotionTreeTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static CharacterAnimationSet Set()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(ArtCharacterPrefabBuilder.PlayerAnimationSet);
            Assert.IsNotNull(set);
            return set;
        }

        private static AnimatorController Controller() => AssetDatabase.LoadAssetAtPath<AnimatorController>(ArtAnimatorBuilder.PlayerController);

        [Test]
        public void Ap026Manifesti_121Klip_Import_Humanoid_Sozlesme()
        {
            Dictionary<string, ArtAnimationClipContract.ClipSpec> m = ArtAnimationClipContract.LocoManifest();
            Assert.IsNotNull(m, ArtAnimationClipContract.LocoManifestPath);
            Assert.AreEqual(121, m.Count);
            int loops = 0, starts = 0, stops = 0;
            var errors = new List<string>();
            foreach (ArtAnimationClipContract.ClipSpec s in m.Values)
            {
                if (s.loop && s.name != C.PlayerIdleAp026) loops++;
                else if (s.name.Contains("_start_")) starts++;
                else if (s.name.Contains("_stop_")) stops++;
                var importer = AssetImporter.GetAtPath(ArtAnimationClipContract.FbxPath(s)) as ModelImporter;
                if (importer == null) { errors.Add(s.name + ": not imported"); continue; }
                List<string> v = ArtCharacterContract.Validate(importer);
                if (v.Count > 0) errors.Add(s.name + ": " + string.Join("; ", v));
                AnimationClip clip = ArtAnimationClipContract.ClipOf(s);
                if (clip == null) { errors.Add(s.name + ": no take"); continue; }
                if (!clip.humanMotion) errors.Add(s.name + ": not humanoid");
                if (clip.isLooping != s.loop) errors.Add(s.name + ": loop " + clip.isLooping);
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(24, loops); Assert.AreEqual(48, starts); Assert.AreEqual(48, stops);
            Debug.Log("[DD-TEST] wp082.loco.manifest clips=" + m.Count + " loops=" + loops + " starts=" + starts + " stops=" + stops);
        }

        [Test]
        public void Locomotion_1DHiz_3Halka8Yon_EsiklerVeTimeScale_SetTablolariyla()
        {
            AnimatorController c = Controller();
            Assert.IsNotNull(c);
            var names = new HashSet<string>();
            foreach (AnimatorControllerParameter p in c.parameters) names.Add(p.name);
            Assert.IsTrue(names.Contains(C.LocoSpeed) && names.Contains(LocomotionV4.TimeParam) && names.Contains(LocomotionV4.ClipParam), "LocoSpeed / V4Time / V4Clip");

            AnimatorState loco = c.layers[0].stateMachine.defaultState;
            Assert.AreEqual(C.LocomotionState, loco.name);
            Assert.IsTrue(loco.speedParameterActive && loco.speedParameter == C.LocoRate);
            var tree = (BlendTree)loco.motion;
            Assert.AreEqual(BlendTreeType.Simple1D, tree.blendType);
            Assert.AreEqual(C.LocoSpeed, tree.blendParameter);
            Assert.AreEqual(4, tree.children.Length);

            CharacterAnimationSet.LocomotionTables t = Set().Locomotion;
            Assert.IsTrue(t.HasRings, "set: 24 loops + 4 thresholds");
            Assert.AreEqual(24, t.loops.Count);
            Assert.AreEqual(96, t.v4.Count);
            Assert.AreEqual(4, t.v4GaitSpeedsMpsTemplate.Length);
            Assert.AreEqual(0f, t.v4GaitSpeedsMpsTemplate[1], "jog off");
            for (int i = 0; i < 4; i++) Assert.AreEqual(t.ringThresholdMps[i], tree.children[i].threshold, 1e-5f, "eşik " + i + " = set tablosu");
            Assert.AreEqual(C.PlayerStandStill, tree.children[0].motion.name);

            var rows = new Dictionary<string, CharacterAnimationSet.LocoLoop>();
            foreach (CharacterAnimationSet.LocoLoop l in t.loops) rows[l.clip] = l;
            string[][] rings = { C.PlayerWalk, C.PlayerRun, C.PlayerSprint };
            var log = new List<string>();
            for (int r = 0; r < 3; r++)
            {
                var sub = (BlendTree)tree.children[r + 1].motion;
                Assert.AreEqual(ArtAnimatorBuilder.RingTreeNames[r], sub.name);
                Assert.AreEqual(BlendTreeType.SimpleDirectional2D, sub.blendType);
                Assert.AreEqual(C.MoveX, sub.blendParameter); Assert.AreEqual(C.MoveY, sub.blendParameterY);
                Assert.AreEqual(8, sub.children.Length);
                for (int d = 0; d < 8; d++)
                {
                    ChildMotion ch = sub.children[d];
                    Assert.AreEqual(rings[r][d], ch.motion.name);
                    float a = d * 45f * Mathf.Deg2Rad;
                    Assert.AreEqual(Mathf.Sin(a), ch.position.x, 1e-4f, ch.motion.name + " x");
                    Assert.AreEqual(Mathf.Cos(a), ch.position.y, 1e-4f, ch.motion.name + " y");
                    Assert.IsTrue(rows.TryGetValue(ch.motion.name, out CharacterAnimationSet.LocoLoop row), ch.motion.name + " set satırı");
                    Assert.AreEqual(r + 1, row.ringSlot); Assert.AreEqual(d, row.dirIndex);
                    // the child time scale gives back the natural cadence: delivered cycle seconds / time scale = cycle_seconds_natural
                    var clip = (AnimationClip)ch.motion;
                    Assert.AreEqual(row.cycleSecondsNatural, clip.length / ch.timeScale, 0.002f, ch.motion.name + " doğal döngü süresi");
                }

                log.Add(string.Format(Inv, "{0} t={1:F4} ts={2:F4}", sub.name, tree.children[r + 1].threshold, sub.children[0].timeScale));
            }

            Debug.Log("[DD-TEST] wp082.loco.tree " + string.Join(" | ", log) + " k=" + t.heroStrideScale.ToString("F4", Inv));
        }

        [Test]
        public void V4_96Durum_V4TimeIle_GecisDegerleriTablodan()
        {
            AnimatorController c = Controller();
            CharacterAnimationSet.LocomotionTables t = Set().Locomotion;
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorStateMachine v4 = null;
            foreach (ChildAnimatorStateMachine ch in sm.stateMachines) if (ch.stateMachine.name == LocomotionV4.StateMachine) v4 = ch.stateMachine;
            Assert.IsNotNull(v4, "LocomotionV4 alt durum makinesi");
            Assert.AreEqual(96, v4.states.Length);
            var byName = new Dictionary<string, AnimatorState>();
            foreach (ChildAnimatorState s in v4.states) byName[s.state.name] = s.state;
            var anyInto = new Dictionary<AnimatorState, AnimatorStateTransition>();
            foreach (AnimatorStateTransition tr in sm.anyStateTransitions) if (tr.destinationState != null) anyInto[tr.destinationState] = tr;
            int checkedRows = 0;
            foreach (CharacterAnimationSet.LocoV4Clip row in t.v4)
            {
                Assert.IsTrue(byName.TryGetValue(LocomotionV4.StateName(row.v4Id), out AnimatorState st), row.clip);
                Assert.AreEqual(row.clip, st.motion.name);
                Assert.IsTrue(st.timeParameterActive, row.clip + " motion time");
                Assert.AreEqual(LocomotionV4.TimeParam, st.timeParameter);
                bool start = row.v4Id < LocomotionV4.StopBase;
                Assert.AreEqual(start ? 0.3f : 0.1f, anyInto[st].duration, 1e-5f, row.clip + " giriş");
                Assert.AreEqual(1, st.transitions.Length);
                AnimatorStateTransition back = st.transitions[0];
                Assert.AreEqual(C.LocomotionState, back.destinationState.name);
                Assert.IsTrue(back.hasExitTime);
                Assert.AreEqual(start ? row.exitTNorm : 1f, back.exitTime, 1e-5f, row.clip + " çıkış zamanı");
                Assert.AreEqual(start ? row.loopOffsetNorm : 0f, back.offset, 1e-5f, row.clip + " döngü ofseti");
                Assert.AreEqual(start ? 0.1f : 0.15f, back.duration, 1e-5f, row.clip + " çıkış harmanı");
                Assert.AreEqual(row.samples, row.loopPhaseEquivNormPerFrame.Length);
                Assert.AreEqual(33, start ? row.lutTAtDist.Length : row.lutTAtRemaining.Length, row.clip + " LUT");
                checkedRows++;
            }

            Assert.AreEqual(96, checkedRows);
        }

        [Test]
        public void Ap026Klipleri_KahramandaAP022IleAyniYoneBakar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.PlayerPrefab);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                Assert.IsTrue(animator != null && animator.isHuman);
                CharacterAnimationSet set = Set();
                float ap026 = BodyForwardDot(animator, set.Clip(C.PlayerWalk[0]));
                float ap026Sprint = BodyForwardDot(animator, set.Clip(C.PlayerSprint[0]));
                float ap022 = BodyForwardDot(animator, set.Clip(C.PlayerWalkFwd));
                Debug.Log(string.Format(Inv, "[DD-TEST] wp082.loco.facing walkFwd·root={0:F3} sprintFwd·root={1:F3} ap022WalkFwd·root={2:F3}", ap026, ap026Sprint, ap022));
                Assert.Greater(Mathf.Abs(ap022), 0.7f, "AP-022 referansı belirgin bir yöne bakar");
                Assert.AreEqual(Mathf.Sign(ap022), Mathf.Sign(ap026), "AP-026 walk_fwd AP-022 ile aynı yöne bakar");
                Assert.Greater(Mathf.Abs(ap026), 0.7f);
                Assert.AreEqual(Mathf.Sign(ap022), Mathf.Sign(ap026Sprint), "AP-026 sprint_fwd AP-022 ile aynı yöne bakar");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The body's facing (shoulder line × up, hip line × up averaged) at the clip's frame 0, dotted with the root's forward.</summary>
        private static float BodyForwardDot(Animator animator, AnimationClip clip)
        {
            Assert.IsNotNull(clip);
            clip.SampleAnimation(animator.gameObject, 0f);
            Vector3 shoulders = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            Vector3 hips = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            Vector3 fwd = Vector3.Cross(shoulders.normalized + hips.normalized, Vector3.up);
            fwd.y = 0f;
            return Vector3.Dot(fwd.normalized, animator.transform.forward);
        }
    }
}
