using System;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-071 r2 (P-06 30.09, AR / smg_1 TP stock 3.7 cm): a measurement, not a gate. The TP idle_aim of ar_1 / ar_2 (class ar) and
    /// smg_1 (class smg) evaluated at frame 0 on the player body's Avatar (Player_AP006) and on the clip FBX's own Avatar (each clip
    /// is imported CreateFromThisModel), the right-arm chain and UpperChest in character space and their difference (mm) — P-06
    /// compares them with the raw Blender frame 0. Run explicitly with DD_S06_RETARGET_OUT=&lt;file.json&gt;.
    /// </summary>
    [TestFixture]
    [Explicit("S-06 measurement for P-06 — DD_S06_RETARGET_OUT=<file.json>")]
    [Category("Diagnostics")]
    public sealed class AnimationRetargetProbeTests
    {
        private static readonly HumanBodyBones[] Bones = { HumanBodyBones.UpperChest, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
        private static readonly string[] Clips = { "AP022_tp_ar_idle_aim", "AP022_tp_smg_idle_aim" };

        [Test]
        public void TpIdleAim_F0_GovdeAvatari_KlipAvatari_Farki()
        {
            string output = Environment.GetEnvironmentVariable("DD_S06_RETARGET_OUT");
            if (string.IsNullOrEmpty(output)) Assert.Fail("DD_S06_RETARGET_OUT not set (explicit diagnostic — TANI-TESTI-POLITIKASI: no Ignore)");
            var manifest = ArtAnimationClipContract.Manifest();
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(ArtCharacterPrefabBuilder.PlayerPrefab);
            Assert.IsNotNull(body, "Player prefab");
            var inv = CultureInfo.InvariantCulture;
            var json = new StringBuilder("{\n  \"schema\": \"wp071.tp-retarget-f0.v1\",\n  \"clips\": [");
            for (int k = 0; k < Clips.Length; k++)
            {
                Assert.IsTrue(manifest.TryGetValue(Clips[k], out var spec), Clips[k]);
                string fbx = ArtAnimationClipContract.FbxPath(spec);
                var clip = ArtAnimationClipContract.ClipOf(spec);
                var clipModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                Assert.IsNotNull(clip, fbx);
                Vector3[] onBody = Sample(body, clip), onOwn = Sample(clipModel, clip);
                json.Append(k == 0 ? "\n    " : ",\n    ").Append("{\"clip\": \"").Append(Clips[k]).Append("\", \"fbx\": \"").Append(Path.GetFileName(fbx)).Append("\", \"bones\": [");
                for (int b = 0; b < Bones.Length; b++)
                {
                    json.Append(b == 0 ? "" : ", ").Append("{\"bone\": \"").Append(Bones[b]).Append("\", \"bodyAvatar\": ").Append(V(onBody[b], inv))
                        .Append(", \"clipAvatar\": ").Append(V(onOwn[b], inv)).Append(", \"deltaMm\": ").Append(((onBody[b] - onOwn[b]).magnitude * 1000f).ToString("F1", inv)).Append('}');
                }

                json.Append("]}");
            }

            json.Append("\n  ],\n  \"note\": \"character space (root at origin, facing +Z); frame 0; weapons ar_1 / ar_2 share AP022_tp_ar_idle_aim, smg_1 uses AP022_tp_smg_idle_aim\"\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, json.ToString());
            Debug.Log("[DD-TEST] wp071.retarget → " + output);
        }

        // The clip at t = 0 on a fresh instance of `model` (its own Avatar), bone positions in the instance's root space.
        private static Vector3[] Sample(GameObject model, AnimationClip clip)
        {
            var result = new Vector3[Bones.Length];
            GameObject go = UnityEngine.Object.Instantiate(model);
            PlayableGraph graph = PlayableGraph.Create("wp071-retarget");
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = go.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator, model.name + " Animator");
                Assert.IsTrue(animator.isHuman, model.name + " Humanoid");
                var output = AnimationPlayableOutput.Create(graph, "out", animator);
                output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                graph.Evaluate(0f);
                for (int b = 0; b < Bones.Length; b++)
                {
                    Transform t = animator.GetBoneTransform(Bones[b]);
                    result[b] = t != null ? go.transform.InverseTransformPoint(t.position) : new Vector3(float.NaN, float.NaN, float.NaN);
                }
            }
            finally
            {
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(go);
            }

            return result;
        }

        private static string V(Vector3 v, CultureInfo inv) =>
            "[" + v.x.ToString("F4", inv) + ", " + v.y.ToString("F4", inv) + ", " + v.z.ToString("F4", inv) + "]";
    }
}
