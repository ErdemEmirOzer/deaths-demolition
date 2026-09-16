using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016 · The clips a character's minimal Animator plays, as one asset so AP-009 can
    /// swap the rig-test placeholders for authored animation without touching code:
    /// a 1-D locomotion blend (idle at 0, walk at 1) driven by <see cref="SpeedParameter"/>
    /// = ground speed / <see cref="WalkSpeedMetersPerSecond"/>. The controller is generated
    /// from this asset by the art pipeline (<c>ArtCharacterPrefabBuilder.BuildController</c>).
    /// Attack, hit reaction and death stay procedural (the M1 pseudo-animators, bridged onto
    /// the skinned bones by <see cref="ProceduralPoseBridge"/>) until AP-009 delivers clips.
    /// </summary>
    public sealed class CharacterAnimationSet : ScriptableObject
    {
        public const string SpeedParameter = "Speed";
        public const string LocomotionState = "Locomotion";

        [SerializeField] private AnimationClip _idle;
        [SerializeField] private AnimationClip _walk;
        [Tooltip("Ground speed at which the walk clip plays at 1x (Speed parameter 1). AP-006/007 rig-test walk ≈ 1.4 m/s.")]
        [SerializeField] private float _walkSpeedMetersPerSecond = 1.4f;
        [SerializeField] private RuntimeAnimatorController _controller;
        [Tooltip("Provenance: which AP delivered the clips (rig tests until AP-009).")]
        [SerializeField] private string _source = "AP-006 rig tests (RigTest_Idle / RigTest_Walk); AP-009 replaces";

        public AnimationClip Idle => _idle;
        public AnimationClip Walk => _walk;
        public float WalkSpeedMetersPerSecond => Mathf.Max(0.1f, _walkSpeedMetersPerSecond);
        public RuntimeAnimatorController Controller => _controller;
        public string Source => _source;

        /// <summary>Speed parameter for a ground speed (0 idle … 1 walk clip speed, clamped at 2).</summary>
        public float SpeedParameterFor(float metersPerSecond)
        {
            return Mathf.Clamp(metersPerSecond / WalkSpeedMetersPerSecond, 0f, 2f);
        }
    }
}
