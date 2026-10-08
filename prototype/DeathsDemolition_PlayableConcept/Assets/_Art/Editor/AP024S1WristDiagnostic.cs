// R3 S1 editor diagnostic: run only under a separately granted canonical batch wrapper.
// No runtime animation/IK mutation in CaptureCurrent. Near-limit is informational.
using System;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public static class AP024S1WristDiagnostic
{
    [Serializable]
    public sealed class WristReadout
    {
        public bool valid;
        public int unity_frame;
        public string phase;
        public Quaternion hand_local_xyzw;
        public Quaternion hand_world_xyzw;
        public Quaternion lowerarm_world_xyzw;
        public Quaternion lowerarm_local_xyzw;
        public Quaternion upperarm_world_xyzw;
        public string mapped_hand_parent;
        public Quaternion forearm_to_hand_relative_xyzw;
        public float[] left_hand_muscles;
        public string[] muscle_names;
        public bool near_limit_information_only;
        public bool near_limit_alone_is_failure = false;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Finite(Quaternion value) =>
        Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);

    public static int[] LeftHandMuscles()
    {
        int bone = Array.FindIndex(HumanTrait.BoneName,
            n => n.Replace(" ", "") == "LeftHand");
        if (bone < 0) throw new InvalidOperationException("HumanTrait LeftHand not found");
        var result = new int[3];
        for (int axis = 0; axis < 3; ++axis)
            result[axis] = HumanTrait.MuscleFromBone(bone, axis);
        return result;
    }

    // Call after the existing animation/IK chain has completed (existing probe's
    // LateUpdate/end-of-frame observation), and label the phase explicitly.
    // GetHumanPose is an informational read; it is not a source-rotation oracle.
    public static WristReadout CaptureCurrent(Animator animator, string phase)
    {
        var result = new WristReadout { unity_frame = Time.frameCount, phase = phase };
        if (animator == null || animator.avatar == null ||
            !animator.avatar.isValid || !animator.avatar.isHuman)
            return result;
        Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform forearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform upperarm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        if (hand == null || forearm == null || upperarm == null) return result;
        result.hand_local_xyzw = hand.localRotation;
        result.hand_world_xyzw = hand.rotation;
        result.lowerarm_world_xyzw = forearm.rotation;
        result.lowerarm_local_xyzw = forearm.localRotation;
        result.upperarm_world_xyzw = upperarm.rotation;
        result.mapped_hand_parent = hand.parent == null ? "NONE" : hand.parent.name;
        result.forearm_to_hand_relative_xyzw =
            Quaternion.Inverse(forearm.rotation) * hand.rotation;
        int[] ids = LeftHandMuscles();
        var values = new System.Collections.Generic.List<float>();
        var names = new System.Collections.Generic.List<string>();
        var handler = new HumanPoseHandler(animator.avatar, animator.transform);
        try
        {
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            foreach (int id in ids)
            {
                if (id < 0) continue;
                float value = pose.muscles[id];
                if (!Finite(value)) return result;
                values.Add(value);
                names.Add(HumanTrait.MuscleName[id]);
                // This flag never changes validity or clip acceptance.
                result.near_limit_information_only |= Mathf.Abs(value) >= .98f;
            }
        }
        finally { handler.Dispose(); }
        result.left_hand_muscles = values.ToArray();
        result.muscle_names = names.ToArray();
        result.valid = Finite(result.hand_local_xyzw) && Finite(result.hand_world_xyzw)
            && Finite(result.lowerarm_world_xyzw) && Finite(result.lowerarm_local_xyzw)
            && Finite(result.upperarm_world_xyzw) && Finite(result.forearm_to_hand_relative_xyzw);
        return result;
    }
}

#if UNITY_EDITOR
public static class AP024S1AvatarLimitExport
{
    [Serializable]
    public sealed class AxisLimit
    {
        public int human_dof_axis;
        public int muscle_index;
        public string muscle_name;
        public bool use_default_values;
        public float serialized_min_deg, serialized_max_deg, serialized_center_deg;
        public float effective_min_deg, effective_max_deg;
        public string effective_limit_source;
    }

    [Serializable]
    public sealed class Sample
    {
        public float[] normalized_left_hand_muscles;
        public AP024S1WristDiagnostic.WristReadout actual_bone_pose;
    }

    [Serializable]
    public sealed class Snapshot
    {
        public string schema = "AP024_S1_AVATAR_LIMITS_V1";
        public string unity_version, model_asset, model_guid, model_dependency_hash, avatar_name;
        public bool avatar_valid, avatar_human;
        public string human_bone_name, rig_bone_name;
        public AxisLimit[] left_hand_limits;
        public Quaternion model_initial_hand_local_xyzw;
        public Quaternion avatar_skeleton_hand_local_xyzw;
        public Quaternion avatar_skeleton_lowerarm_local_xyzw;
        public bool avatar_skeleton_lowerarm_reference_found;
        public bool avatar_skeleton_reference_found;
        public AP024S1WristDiagnostic.WristReadout human_pose_zero_muscles;
        public Sample[] left_hand_endpoint_grid;
        public string source_axis_equivalence = "NOT_ESTABLISHED";
        public bool near_limit_alone_is_failure = false;
        public string acceptance = "DIAGNOSTIC_ONLY";
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        if (i >= 0 && i + 1 < args.Length) return args[i + 1];
        // gbatch.sh preserves the environment while keeping its canonical guard wrapper.
        string env = name == "-s1AvatarModel" ? "DD_S1_AVATAR_MODEL" :
            name == "-s1AvatarOut" ? "DD_S1_AVATAR_OUT" : null;
        string value = env == null ? null : Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("Missing " + name);
        return value;
    }

    // Future batch -executeMethod entry. Do not run until the root's engine grant.
    // -s1AvatarModel Assets/_Art/.../Hero_v2_TD_LOD0.fbx -s1AvatarOut <new .json>
    public static void Execute()
    {
        string assetPath = Arg("-s1AvatarModel");
        string output = Path.GetFullPath(Arg("-s1AvatarOut"));
        const string ownRoot = "/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/" +
            "asset-production/AP-024/04-tools/r3-s1-20261008/";
        if (!output.StartsWith(ownRoot, StringComparison.Ordinal) || File.Exists(output))
            throw new InvalidOperationException("Snapshot must be a NEW file in S1 tools folder");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (model == null) throw new InvalidOperationException("Model asset missing: " + assetPath);
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try
        {
            instance = PrefabUtility.InstantiatePrefab(model, preview) as GameObject;
            if (instance == null) throw new InvalidOperationException("Preview instance failed");
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null ||
                !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Actual model Avatar is not valid Humanoid");
            animator.enabled = false;
            animator.Rebind();
            Avatar avatar = animator.avatar;
            HumanDescription desc = avatar.humanDescription;
            HumanBone hand = desc.human.Single(
                b => b.humanName.Replace(" ", "") == "LeftHand");
            int[] ids = AP024S1WristDiagnostic.LeftHandMuscles();
            var limits = new AxisLimit[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                int id = ids[axis];
                limits[axis] = new AxisLimit {
                    human_dof_axis = axis, muscle_index = id,
                    muscle_name = id < 0 ? "NO_MUSCLE" : HumanTrait.MuscleName[id],
                    use_default_values = hand.limit.useDefaultValues,
                    serialized_min_deg = hand.limit.min[axis],
                    serialized_max_deg = hand.limit.max[axis],
                    serialized_center_deg = hand.limit.center[axis],
                    effective_min_deg = id < 0 ? 0 :
                        (hand.limit.useDefaultValues ? HumanTrait.GetMuscleDefaultMin(id) : hand.limit.min[axis]),
                    effective_max_deg = id < 0 ? 0 :
                        (hand.limit.useDefaultValues ? HumanTrait.GetMuscleDefaultMax(id) : hand.limit.max[axis]),
                    effective_limit_source = hand.limit.useDefaultValues ?
                        "HumanTrait.GetMuscleDefaultMin/Max(actual muscle index)" : "Avatar.humanDescription.human.limit"
                };
            }
            Transform mappedHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var snapshot = new Snapshot {
                unity_version = Application.unityVersion,
                model_asset = assetPath, model_guid = AssetDatabase.AssetPathToGUID(assetPath),
                model_dependency_hash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString(),
                avatar_name = avatar.name, avatar_valid = avatar.isValid, avatar_human = avatar.isHuman,
                human_bone_name = hand.humanName, rig_bone_name = hand.boneName,
                left_hand_limits = limits, model_initial_hand_local_xyzw = mappedHand.localRotation
            };
            foreach (SkeletonBone bone in desc.skeleton)
                if (bone.name == hand.boneName)
                {
                    snapshot.avatar_skeleton_hand_local_xyzw = bone.rotation;
                    snapshot.avatar_skeleton_reference_found = true;
                    break;
                }
            Transform mappedLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            foreach (SkeletonBone bone in desc.skeleton)
                if (bone.name == mappedLower.name)
                {
                    snapshot.avatar_skeleton_lowerarm_local_xyzw = bone.rotation;
                    snapshot.avatar_skeleton_lowerarm_reference_found = true;
                    break;
                }
            var poseHandler = new HumanPoseHandler(avatar, animator.transform);
            try
            {
                var pose = new HumanPose();
                poseHandler.GetHumanPose(ref pose);
                pose.muscles = new float[HumanTrait.MuscleCount];
                poseHandler.SetHumanPose(ref pose);
                snapshot.human_pose_zero_muscles =
                    AP024S1WristDiagnostic.CaptureCurrent(animator, "avatar-calibration-zero");
                int[] active = ids.Where(id => id >= 0).ToArray();
                var samples = new System.Collections.Generic.List<Sample>();
                float[] grid = { -1f, -.5f, 0f, .5f, 1f };
                int count = 1;
                for (int i = 0; i < active.Length; ++i) count *= grid.Length;
                for (int index = 0; index < count; ++index)
                {
                    Array.Clear(pose.muscles, 0, pose.muscles.Length);
                    var requested = new float[active.Length];
                    int n = index;
                    for (int axis = 0; axis < active.Length; ++axis)
                    {
                        requested[axis] = grid[n % grid.Length]; n /= grid.Length;
                        pose.muscles[active[axis]] = requested[axis];
                    }
                    poseHandler.SetHumanPose(ref pose);
                    samples.Add(new Sample {
                        normalized_left_hand_muscles = requested,
                        actual_bone_pose = AP024S1WristDiagnostic.CaptureCurrent(
                            animator, "avatar-calibration-grid")
                    });
                }
                snapshot.left_hand_endpoint_grid = samples.ToArray();
            }
            finally { poseHandler.Dispose(); }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(snapshot, true));
            Debug.Log("AP024 S1 actual Avatar limits diagnostic: " + output);
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
