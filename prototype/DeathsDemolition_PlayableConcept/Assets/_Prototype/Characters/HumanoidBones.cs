using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>WP-018 · Humanoid bone lookup that tolerates a missing Animator / avatar (returns null).</summary>
    public static class HumanoidBones
    {
        public static Transform Bone(Animator animator, HumanBodyBones bone)
        {
            return animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        }
    }
}
