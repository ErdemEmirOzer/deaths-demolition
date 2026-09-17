using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-032F · AP-006C secondary chains (SECONDARY-CHAINS-CONTRACT.md): reads the AP006C / AP007C chain JSON next to the
    /// character, finds the `sec_&lt;chain&gt;_&lt;i&gt;` transforms under the Animator (extra transforms of the Humanoid
    /// import — never in the human map, never keyed by the clips) and configures a <see cref="SecondaryChainDriver"/>
    /// with the contract parameters and the rest directions captured from the FBX rest pose (parent-bone space, plus
    /// each bone's local axis toward its next particle). A character without the bones gets no driver.
    /// </summary>
    public static class ArtSecondaryChains
    {
        public const string LogPrefix = "[ArtSecondaryChains] ";

        public sealed class ChainSpec
        {
            public string name;
            public string parent;
            public int segments;
            public float length;
            public float stiffness, damping, gravity, collisionRadius, bodyRadius, maxAngle;
        }

        public static List<ChainSpec> Read(string jsonPath)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), jsonPath);
            var list = new List<ChainSpec>();
            if (!File.Exists(full)) return list;
            var root = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(full)), "chains");
            foreach (object o in ArtJson.AsList(ArtJson.Require(root, "chains"), "chains"))
            {
                var c = ArtJson.AsObject(o, "chain");
                list.Add(new ChainSpec
                {
                    name = ArtJson.AsString(ArtJson.Require(c, "name"), "name"),
                    parent = ArtJson.AsString(ArtJson.Require(c, "parent"), "parent"),
                    segments = ArtJson.AsInt(ArtJson.Require(c, "segments"), "segments"),
                    length = ArtJson.AsFloat(ArtJson.Require(c, "length"), "length"),
                    stiffness = ArtJson.AsFloat(ArtJson.Require(c, "stiffness"), "stiffness"),
                    damping = ArtJson.AsFloat(ArtJson.Require(c, "damping"), "damping"),
                    gravity = ArtJson.Get(c, "gravity") is object g ? ArtJson.AsFloat(g, "gravity") : 9.81f,
                    collisionRadius = ArtJson.AsFloat(ArtJson.Require(c, "collision_radius"), "collision_radius"),
                    bodyRadius = ArtJson.AsFloat(ArtJson.Require(c, "body_radius"), "body_radius"),
                    maxAngle = ArtJson.Get(c, "max_angle_deg") is object m ? ArtJson.AsFloat(m, "max_angle_deg") : 60f,
                });
            }

            return list;
        }

        /// <summary>The core bone the contract names as the chain parent, resolved through the avatar.</summary>
        private static HumanBodyBones ParentBone(string name)
        {
            switch (name)
            {
                case "Hips": return HumanBodyBones.Hips;
                case "Head": return HumanBodyBones.Head;
                case "UpperChest": return HumanBodyBones.UpperChest;
                case "Chest": return HumanBodyBones.Chest;
                case "Spine": return HumanBodyBones.Spine;
                default: return HumanBodyBones.LastBone;
            }
        }

        /// <summary>The capsule's far end along the parent bone (the core child), or null → the driver extends along the bone's up.</summary>
        private static (Transform end, float length) CapsuleOf(Animator animator, HumanBodyBones parent)
        {
            switch (parent)
            {
                case HumanBodyBones.Hips: return (animator.GetBoneTransform(HumanBodyBones.Spine), 0.15f);
                case HumanBodyBones.Spine: return (animator.GetBoneTransform(HumanBodyBones.Chest), 0.15f);
                case HumanBodyBones.Chest: return (animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Neck), 0.2f);
                case HumanBodyBones.UpperChest: return (animator.GetBoneTransform(HumanBodyBones.Neck), 0.24f);
                default: return (null, 0.2f);   // Head: no core child — extend along the head's up
            }
        }

        public static int Build(GameObject instance, Animator animator, string jsonPath)
        {
            List<ChainSpec> specs = Read(jsonPath);
            var chains = new List<SecondaryChainDriver.Chain>();
            var missing = new List<string>();
            foreach (ChainSpec spec in specs)
            {
                HumanBodyBones parentBone = ParentBone(spec.parent);
                Transform parent = parentBone != HumanBodyBones.LastBone ? animator.GetBoneTransform(parentBone) : null;
                if (parent == null) { missing.Add(spec.name + ": parent " + spec.parent); continue; }
                var bones = new Transform[spec.segments];
                bool ok = true;
                for (int i = 0; i < spec.segments; i++)
                {
                    bones[i] = FindDeep(instance.transform, "sec_" + spec.name + "_" + (i + 1));
                    if (bones[i] == null) { ok = false; missing.Add(spec.name + ": sec_" + spec.name + "_" + (i + 1)); break; }
                }

                if (!ok) continue;
                float segment = spec.length / Mathf.Max(1, spec.segments);
                var restLocal = new Vector3[spec.segments];
                var axisLocal = new Vector3[spec.segments];
                for (int i = 0; i < spec.segments; i++)
                {
                    // rest direction from the rest pose: toward the next bone; the last segment continues the previous one (or hangs down)
                    Vector3 dir;
                    if (i + 1 < spec.segments) dir = (bones[i + 1].position - bones[i].position).normalized;
                    else if (spec.segments > 1) dir = (bones[i].position - bones[i - 1].position).normalized;
                    else dir = Vector3.down;
                    restLocal[i] = parent.InverseTransformDirection(dir);
                    axisLocal[i] = Quaternion.Inverse(bones[i].rotation) * dir;
                }

                (Transform end, float capLen) = CapsuleOf(animator, parentBone);
                chains.Add(new SecondaryChainDriver.Chain
                {
                    name = spec.name, parent = parent, parentEnd = end, bones = bones, segmentLength = segment,
                    stiffness = spec.stiffness, damping = spec.damping, gravity = spec.gravity, maxAngleDegrees = spec.maxAngle,
                    collisionRadius = spec.collisionRadius, bodyRadius = spec.bodyRadius, capsuleLength = capLen,
                    restLocal = restLocal, boneAxisLocal = axisLocal
                });
            }

            SecondaryChainDriver driver = animator.GetComponent<SecondaryChainDriver>();
            if (chains.Count == 0)
            {
                if (driver != null) UnityEngine.Object.DestroyImmediate(driver);
                if (specs.Count > 0) Debug.LogWarning(LogPrefix + Path.GetFileName(jsonPath) + ": no chain bones on the rig (" + string.Join("; ", missing) + ")");
                return 0;
            }

            if (driver == null) driver = animator.gameObject.AddComponent<SecondaryChainDriver>();
            driver.Configure(chains.ToArray());
            EditorUtility.SetDirty(driver);
            Debug.Log(LogPrefix + Path.GetFileName(jsonPath) + ": " + chains.Count + "/" + specs.Count + " chains bound" + (missing.Count > 0 ? " (missing: " + string.Join("; ", missing) + ")" : ""));
            return chains.Count;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }

            return null;
        }
    }
}
