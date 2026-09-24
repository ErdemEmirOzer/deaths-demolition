using System;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 · AP-022 rig v002 (player): drives the eight twist bones the clips never key (Humanoid extra transforms) from the
    /// twist of their driver, P-06's convention — <c>upperarm_twist</c> −0.5 × UpperArm twist (counter, the shoulder side keeps its
    /// volume), <c>lowerarm_twist_01/02</c> +0.4 / +0.8 × Hand twist relative to the forearm (the wrist's candy-wrap), <c>thigh_twist</c>
    /// −0.5 × UpperLeg twist. Twist = swing-twist about the bone's own axis (parent → child at rest). Runs after the Animator, the
    /// mount alignment (900) and the grip IK (910), so the arm it reads is the final one. Absolute from rest every frame (no drift),
    /// zero allocation; a rig without the bones (v001) configures to nothing.
    /// </summary>
    [DefaultExecutionOrder(920)]
    [DisallowMultipleComponent]
    public sealed class TwistCorrector : MonoBehaviour
    {
        /// <summary>Global switch (tests / A-B).</summary>
        public static bool Enabled = true;

        [System.Serializable]
        private struct Link
        {
            public Transform Bone, Driver;
            public Quaternion BoneRest, DriverRest;
            public Vector3 DriverAxis, ParentAxis;   // the twist axis in the driver's rest frame / in the bone's parent frame
            public float Weight;
        }

        [SerializeField] private Link[] _links = new Link[0];
        private float[] _last = new float[8];

        public int LinkCount => _links.Length;
        /// <summary>Last applied angle per link (degrees; tests).</summary>
        public float AngleOf(int i) => i >= 0 && i < _links.Length ? _last[i] : 0f;
        public Transform BoneOf(int i) => i >= 0 && i < _links.Length ? _links[i].Bone : null;
        public float WeightOf(int i) => i >= 0 && i < _links.Length ? _links[i].Weight : 0f;

        /// <summary>Tests: puts link <paramref name="i"/>'s driver at its rest pose twisted by <paramref name="degrees"/> about its axis.</summary>
        public void PoseDriverForTest(int i, float degrees)
        {
            if (i < 0 || i >= _links.Length || _links[i].Driver == null) return;
            _links[i].Driver.localRotation = _links[i].DriverRest * Quaternion.AngleAxis(degrees, _links[i].DriverAxis);
        }

        /// <summary>
        /// Editor (prefab builder): captures the rest pose from the imported model (bind pose) into the serialized links.
        /// <paramref name="bone"/> resolves a bone by name (the avatar template's bone names are the Unity human names).
        /// </summary>
        public void Configure(Func<string, Transform> bone)
        {
            _links = new Link[8];
            _count = 0;
            for (int s = 0; s < 2 && bone != null; s++)
            {
                string side = s == 0 ? "Left" : "Right", suffix = s == 0 ? "_l" : "_r";
                Transform upper = bone(side + "UpperArm"), lower = bone(side + "LowerArm"), hand = bone(side + "Hand");
                Transform thigh = bone(side + "UpperLeg"), shin = bone(side + "LowerLeg");
                if (upper != null && lower != null) AddSelf(bone("upperarm_twist" + suffix), upper, lower, -0.5f);
                if (thigh != null && shin != null) AddSelf(bone("thigh_twist" + suffix), thigh, shin, -0.5f);
                if (lower != null && hand != null)
                {
                    AddChild(bone("lowerarm_twist_01" + suffix), hand, 0.4f);
                    AddChild(bone("lowerarm_twist_02" + suffix), hand, 0.8f);
                }
            }

            Array.Resize(ref _links, _count);
        }

        private int _count;

        // The bone sits under its driver: the driver's own twist about driver → next.
        private void AddSelf(Transform bone, Transform driver, Transform next, float weight)
        {
            if (bone == null || _count >= _links.Length) return;
            Vector3 axis = next.localPosition.normalized;
            _links[_count++] = new Link { Bone = bone, Driver = driver, BoneRest = bone.localRotation, DriverRest = driver.localRotation, DriverAxis = axis, ParentAxis = axis, Weight = weight };
        }

        // The bone sits under the forearm, the driver is the hand: the hand's twist about forearm → hand, read in the hand's rest frame.
        private void AddChild(Transform bone, Transform hand, float weight)
        {
            if (bone == null || _count >= _links.Length) return;
            Vector3 axis = hand.localPosition.normalized;
            _links[_count++] = new Link { Bone = bone, Driver = hand, BoneRest = bone.localRotation, DriverRest = hand.localRotation, DriverAxis = Quaternion.Inverse(hand.localRotation) * axis, ParentAxis = axis, Weight = weight };
        }

        private void LateUpdate()
        {
            if (Enabled) Apply();
        }

        /// <summary>One correction pass (LateUpdate; tests call it directly between poses).</summary>
        public void Apply()
        {
            if (_last.Length < _links.Length) _last = new float[_links.Length];
            for (int i = 0; i < _links.Length; i++)
            {
                ref Link l = ref _links[i];
                if (l.Bone == null || l.Driver == null) continue;
                float angle = l.Weight * TwistAngle(Quaternion.Inverse(l.DriverRest) * l.Driver.localRotation, l.DriverAxis);
                _last[i] = angle;
                l.Bone.localRotation = Quaternion.AngleAxis(angle, l.ParentAxis) * l.BoneRest;
            }
        }

        /// <summary>Swing-twist: the signed twist of <paramref name="q"/> about the unit <paramref name="axis"/>, degrees in (−180, 180].</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            float p = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float a = 2f * Mathf.Atan2(p, q.w) * Mathf.Rad2Deg;
            if (a > 180f) a -= 360f; else if (a <= -180f) a += 360f;
            return a;
        }
    }
}
