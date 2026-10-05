using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Characters;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-083 B3b (C-00 05.10, locomotion v4 skeleton): the start / stop / pivot contract over the AP-026 clip plan, the event
    /// detector on scripted velocity streams, and the Animator sub-state machine the builder adds for the clips a set carries.
    /// </summary>
    [TestFixture]
    [Category("WP083")]
    public sealed class ArtLocomotionV4Tests
    {
        private const float Dt = 1f / 60f;
        private static readonly float[] GaitSpeeds = { 1.4f, 3.0f, 4.5f, 6.2f };

        [Test]
        public void V4Sozlesmesi_KimlikAdBirebir_SekizYon()
        {
            var names = new HashSet<string>();
            for (int id = 0; id < LocomotionV4.Count; id++)
            {
                string n = LocomotionV4.ClipName(id);
                Assert.IsNotNull(n, "id " + id);
                Assert.IsTrue(n.StartsWith(LocomotionV4.ClipPrefix), n);
                Assert.IsTrue(names.Add(n), "çift klip adı " + n);
                Assert.AreEqual("V4_" + n.Substring(LocomotionV4.ClipPrefix.Length), LocomotionV4.StateName(id));
            }

            Assert.IsNull(LocomotionV4.ClipName(LocomotionV4.Count));
            Assert.AreEqual("AP026_tp_walk_start_fwd_l", LocomotionV4.ClipName(LocomotionV4.StartId(0, 0, true)));
            Assert.AreEqual("AP026_tp_sprint_stop_fwd_l_r", LocomotionV4.ClipName(LocomotionV4.StopId(3, 7, false)));
            Assert.AreEqual("AP026_tp_pivot_135_r_l", LocomotionV4.ClipName(LocomotionV4.PivotId(1, true, true)));
            Assert.AreEqual("AP026_tp_turn_45_r", LocomotionV4.ClipName(LocomotionV4.TurnId(false)));

            Vector2[] dirs = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(1, -1), new Vector2(0, -1), new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1) };
            for (int i = 0; i < 8; i++) Assert.AreEqual(i, LocomotionV4.Direction8(dirs[i]), LocomotionV4.Directions[i]);
            Assert.AreEqual(0, LocomotionV4.Direction8(new Vector2(0.3f, 1f)), "22° → fwd");
            Assert.AreEqual(-1, LocomotionV4.Direction8(Vector2.zero));
            Assert.AreEqual(3, LocomotionV4.GaitFor(6.2f, GaitSpeeds));
            Assert.AreEqual(1, LocomotionV4.GaitFor(3.1f, GaitSpeeds));
        }

        private static List<int> Run(LocomotionV4Events d, bool commanded, Quaternion body, float targetSpeed, bool leftPlanted, params (Vector3 velocity, float seconds)[] segments)
        {
            // rest 0.3 s, then the velocity ramps linearly from each segment's start to its end value; with commanded = true the
            // motor's command is the segment's end value from its first frame (input changes at once, the body follows)
            d.Reset();
            var events = new List<int>();
            Vector3 v = Vector3.zero;
            var all = new List<(Vector3, float)> { (Vector3.zero, 0.3f) };
            all.AddRange(segments);
            foreach ((Vector3 to, float seconds) in all)
            {
                int frames = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
                Vector3 from = v;
                for (int f = 1; f <= frames; f++)
                {
                    v = Vector3.Lerp(from, to, (float)f / frames);
                    int id = d.Feed(v, commanded ? to : (Vector3?)null, body, targetSpeed, leftPlanted, GaitSpeeds, Dt);
                    if (id != LocomotionV4.None) events.Add(id);
                }
            }

            return events;
        }

        private static string Names(List<int> ids)
        {
            var n = new List<string>();
            foreach (int id in ids) n.Add(LocomotionV4.ClipName(id));
            return string.Join(", ", n);
        }

        /// <summary>
        /// Both headings (the motor's command, and the velocity prediction): start, stop, a 30° drift (no event), a body turned 90°;
        /// with the command a reversal is one pivot 180 and a 90° cut one pivot 90; from velocity alone a reversal is stop + start back.
        /// </summary>
        [Test]
        public void V4Olaylari_Basla_Dur_Pivot_Sapma([Values(false, true)] bool commanded)
        {
            var d = new LocomotionV4Events();
            Quaternion id0 = Quaternion.identity;
            Vector3 fwd45 = Vector3.forward * 4.5f, drift = Quaternion.Euler(0f, 30f, 0f) * fwd45;
            // the free (lead) foot: right planted → left leads
            CollectionAssert.AreEqual(new[] { LocomotionV4.StartId(3, 0, true) },
                Run(d, commanded, id0, 6.2f, false, (Vector3.forward * 6.2f, 0.3f), (Vector3.forward * 6.2f, 0.5f)), "kalkış: sprint, ileri");
            CollectionAssert.AreEqual(new[] { LocomotionV4.StartId(1, 2, false), LocomotionV4.StopId(1, 2, false) },
                Run(d, commanded, id0, 3f, true, (Vector3.right * 3f, 0.2f), (Vector3.right * 3f, 0.5f), (Vector3.zero, 0.2f), (Vector3.zero, 0.3f)), "kalkış + duruş: jog, sağ");
            List<int> e = Run(d, commanded, id0, 4.5f, true, (fwd45, 0.2f), (fwd45, 0.4f), (drift, 0.3f), (drift, 0.4f));
            Assert.AreEqual(1, e.Count, "30° sapma olay değil: " + Names(e));
            CollectionAssert.AreEqual(new[] { LocomotionV4.StartId(0, 6, false) },
                Run(d, commanded, Quaternion.Euler(0f, 90f, 0f), 1.4f, true, (Vector3.forward * 1.4f, 0.3f), (Vector3.forward * 1.4f, 0.3f)), "gövde sağa dönük: dünya ileri = gövde solu (yürüyüş)");

            e = Run(d, commanded, id0, 4.5f, true, (fwd45, 0.2f), (fwd45, 0.5f), (-fwd45, 0.35f), (-fwd45, 0.5f));
            if (commanded)
            {
                CollectionAssert.AreEqual(new[] { LocomotionV4.StartId(2, 0, false), LocomotionV4.PivotId(2, true, false) }, e, "komutla geri dönüş = pivot 180: " + Names(e));
                e = Run(d, true, id0, 4.5f, true, (fwd45, 0.2f), (fwd45, 0.5f), (Vector3.right * 4.5f, 0.25f), (Vector3.right * 4.5f, 0.5f));
                CollectionAssert.AreEqual(new[] { LocomotionV4.StartId(2, 0, false), LocomotionV4.PivotId(0, true, false) }, e, "komutla 90° sağ = pivot 90 sağ: " + Names(e));
            }
            else
            {
                CollectionAssert.AreEqual(new[] { LocomotionV4.StartId(2, 0, false), LocomotionV4.StopId(2, 0, false), LocomotionV4.StartId(2, 4, false) }, e,
                    "hızdan geri dönüş = dur + geri kalkış: " + Names(e));
            }
        }

        [Test]
        public void V4AltDurum_YalnizVarolanKliplerle_KontrolcuDegismezKlipYokken()
        {
            var c = new AnimatorController();
            c.AddLayer("Base");
            c.AddParameter(CharacterAnimatorContract.Alive, AnimatorControllerParameterType.Bool);
            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState loco = sm.AddState(CharacterAnimatorContract.LocomotionState);
            var made = new List<Object> { c };
            try
            {
                var none = new Dictionary<string, AnimationClip> { { "AP022_tp_walk_fwd", new AnimationClip() } };
                made.Add(none["AP022_tp_walk_fwd"]);
                Assert.AreEqual(0, ArtAnimatorBuilder.AddLocomotionV4(c, sm, loco, none));
                Assert.AreEqual(1, c.parameters.Length, "klip yokken parametre eklenmemeli");
                Assert.AreEqual(0, sm.stateMachines.Length);

                int[] ids = { LocomotionV4.StartId(0, 0, true), LocomotionV4.StopId(2, 2, false), LocomotionV4.PivotId(2, true, true) };
                var clips = new Dictionary<string, AnimationClip>(none);
                foreach (int id in ids)
                {
                    var clip = new AnimationClip { name = LocomotionV4.ClipName(id) };
                    made.Add(clip);
                    clips[clip.name] = clip;
                }

                Assert.AreEqual(3, ArtAnimatorBuilder.AddLocomotionV4(c, sm, loco, clips));
                AnimatorControllerParameter p = System.Array.Find(c.parameters, x => x.name == LocomotionV4.ClipParam);
                Assert.IsNotNull(p);
                Assert.AreEqual(AnimatorControllerParameterType.Int, p.type);
                Assert.AreEqual(LocomotionV4.None, p.defaultInt);
                Assert.AreEqual(1, sm.stateMachines.Length);
                AnimatorStateMachine v4 = sm.stateMachines[0].stateMachine;
                Assert.AreEqual(LocomotionV4.StateMachine, v4.name);
                Assert.AreEqual(3, v4.states.Length);
                Assert.AreEqual(3, sm.anyStateTransitions.Length);
                for (int i = 0; i < ids.Length; i++)
                {
                    AnimatorStateTransition t = sm.anyStateTransitions[i];
                    Assert.AreEqual(LocomotionV4.StateName(ids[i]), t.destinationState.name);
                    Assert.AreSame(clips[LocomotionV4.ClipName(ids[i])], t.destinationState.motion);
                    Assert.IsFalse(t.canTransitionToSelf);
                    AnimatorCondition cond = System.Array.Find(t.conditions, x => x.parameter == LocomotionV4.ClipParam);
                    Assert.AreEqual(AnimatorConditionMode.Equals, cond.mode);
                    Assert.AreEqual(ids[i], (int)cond.threshold);
                    Assert.IsTrue(System.Array.Exists(t.conditions, x => x.parameter == CharacterAnimatorContract.Alive && x.mode == AnimatorConditionMode.If), "Alive koşulu");
                    Assert.AreSame(loco, t.destinationState.transitions[0].destinationState, "kliple Locomotion'a dönüş");
                }
            }
            finally
            {
                foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            }
        }
    }
}
