using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-082 / WP-083 B2b (C-00 05.10, S-02 magazine contract v1): the left hand's magazine socket — where a reload's magazine sits
    /// while the hand carries it (WeaponMagazineOwnership HandOld / HandNew). One name for the third-person body and the first-person
    /// arms. A rig that already carries the socket (the Hero v2 rig standard, AP-026) keeps its own; otherwise it is created under the
    /// Humanoid LeftHand at the palm centre — half-way from the wrist to the middle finger's base, in the hand's own space, so any
    /// humanoid rig gets a palm-centred socket that follows every hand pose. Zero allocation after creation.
    /// </summary>
    public static class MagazineSocket
    {
        public const string Name = "socket_mag_l";

        /// <summary>Palm centre as a share of the wrist → middle-finger-base distance.</summary>
        public const float PalmShare = 0.5f;

        /// <summary>The socket under the animator's LeftHand (found, or created at the palm centre); null without a humanoid left hand.</summary>
        public static Transform Ensure(Animator animator)
        {
            if (animator == null || !animator.isHuman) return null;
            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (hand == null) return null;
            Transform existing = Find(hand);
            if (existing != null) return existing;

            var socket = new GameObject(Name).transform;
            socket.SetParent(hand, false);
            socket.gameObject.layer = hand.gameObject.layer;
            Transform middle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            socket.localPosition = middle != null ? hand.InverseTransformPoint(Vector3.Lerp(hand.position, middle.position, PalmShare)) : Vector3.zero;
            socket.localRotation = Quaternion.identity;
            return socket;
        }

        private static Transform Find(Transform root)
        {
            if (root.name == Name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = Find(root.GetChild(i));
                if (t != null) return t;
            }

            return null;
        }
    }
}
