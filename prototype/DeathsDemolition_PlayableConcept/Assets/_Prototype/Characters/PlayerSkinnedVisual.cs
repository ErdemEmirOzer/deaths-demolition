using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016 · The AP-006 player on the M1 player rig. Lives on the <c>Player_AP006</c>
    /// prefab root (written by <c>ArtCharacterPrefabBuilder</c>); <c>PlayerRigFactory</c>
    /// instantiates it under the rig's <c>Visual</c> node, hides the primitive parts and calls
    /// <see cref="Attach"/>. Skin = albedo swap on the one shared material through a property
    /// block (three renderers, one material asset — WP-016 §2); the weapon mount moves to
    /// <c>socket_weapon_r</c>; locomotion = <see cref="CharacterAnimationSet"/> blend driven
    /// by the motor's speed; lean / recoil / reload arm / flinch / death come from the M1
    /// pseudo-animator through <see cref="ProceduralPoseBridge"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSkinnedVisual : MonoBehaviour
    {
        public const string PrefabName = "Player_AP006";
        public const string WeaponSocketName = "socket_weapon_r";
        public const string BackSocketName = "socket_back";
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        [SerializeField] private Animator _animator;
        [SerializeField] private LODGroup _lodGroup;
        [SerializeField] private SkinnedMeshRenderer[] _renderers = new SkinnedMeshRenderer[0];
        [SerializeField] private Transform _weaponSocket;
        [SerializeField] private Transform _backSocket;
        [SerializeField] private CharacterAnimationSet _animation;
        [SerializeField] private ProceduralPoseBridge _bridge;

        private MaterialPropertyBlock _block;
        // Animator.StringToHash may be negative: never use the id as a "not set" sentinel.
        private static readonly int SpeedId = Animator.StringToHash(CharacterAnimationSet.SpeedParameter);
        private System.Func<float> _speedMetersPerSecond;
        private int _appliedSkin = -1;

        public Animator Animator => _animator;
        public LODGroup LodGroup => _lodGroup;
        public SkinnedMeshRenderer[] Renderers => _renderers;
        public Transform WeaponSocket => _weaponSocket;
        public Transform BackSocket => _backSocket;
        public CharacterAnimationSet AnimationSet => _animation;
        public ProceduralPoseBridge Bridge => _bridge;
        public int AppliedSkinIndex => _appliedSkin;

        /// <summary>Editor (prefab builder): wire the serialized references once.</summary>
        public void Configure(Animator animator, LODGroup lodGroup, SkinnedMeshRenderer[] renderers, Transform weaponSocket, Transform backSocket,
            CharacterAnimationSet animation, ProceduralPoseBridge bridge)
        {
            _animator = animator;
            _lodGroup = lodGroup;
            _renderers = renderers ?? new SkinnedMeshRenderer[0];
            _weaponSocket = weaponSocket;
            _backSocket = backSocket;
            _animation = animation;
            _bridge = bridge;
        }

        /// <summary>
        /// Bind to the M1 rig: <paramref name="visual"/> is the rig's Visual node (this object is
        /// re-parented under it at the origin), the joints are the pseudo-animator's driver
        /// transforms. Torso → Chest (lean, recoil pitch, flinch), Head → Head, Arm.R →
        /// RightUpperArm with the authored −72° aim raise kept (identity rest) so the weapon arm
        /// reads as aiming, hips/legs/left arm stay with the clip. Returns the socket the weapon
        /// mount should be parented to (null when the rig has none).
        /// </summary>
        public Transform Attach(Transform visual, Transform torso, Transform head, Transform armRight, Transform armRightMesh, System.Func<float> speedMetersPerSecond)
        {
            transform.SetParent(visual, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _speedMetersPerSecond = speedMetersPerSecond;

            if (_animator != null)
            {
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.applyRootMotion = false;
                if (_animation != null && _animation.Controller != null && _animator.runtimeAnimatorController == null)
                {
                    _animator.runtimeAnimatorController = _animation.Controller;
                }

            }

            if (_bridge != null)
            {
                _bridge.Configure(visual);
                Transform chest = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Chest);
                Transform headBone = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Head);
                Transform upperArmR = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.RightUpperArm);
                Transform lowerArmR = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.RightLowerArm);
                _bridge.Add("torso→Chest", torso, chest);
                _bridge.Add("head→Head", head, headBone);
                _bridge.Add("armR→RightUpperArm", armRight, upperArmR, armRightMesh, lowerArmR, conjugateLimb: true, identityRest: true);
                _bridge.CaptureRest();
                _bridge.Active = true;
            }

            return _weaponSocket;
        }

        /// <summary>Albedo swap for a skin (index = PlayerSkin). Same material asset for every skin; one property block per renderer.</summary>
        public void ApplySkin(int skinIndex, Texture2D albedo)
        {
            if (albedo == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                SkinnedMeshRenderer r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetTexture(BaseMapId, albedo);
                r.SetPropertyBlock(_block);
            }

            _appliedSkin = skinIndex;
        }

        /// <summary>The albedo currently bound on the first renderer (tests).</summary>
        public Texture BoundAlbedo()
        {
            if (_renderers.Length == 0 || _renderers[0] == null) return null;
            if (_block == null) _block = new MaterialPropertyBlock();
            _renderers[0].GetPropertyBlock(_block);
            return _block.GetTexture(BaseMapId);
        }

        private const float SpeedSmoothing = 0.12f;   // seconds per unit of Speed
        private float _speed;

        private void Update()
        {
            if (_animator == null || _animation == null) return;
            float mps = _speedMetersPerSecond != null ? _speedMetersPerSecond() : 0f;
            float target = _animation.SpeedParameterFor(mps);
            _speed = Time.deltaTime > 0f ? Mathf.MoveTowards(_speed, target, Time.deltaTime / SpeedSmoothing) : target;
            _animator.SetFloat(SpeedId, _speed);
        }

        /// <summary>Renderers of the M1 primitive body to hide when this visual is attached.</summary>
        public static void HidePrimitiveParts(Transform visual, ICollection<Renderer> hidden)
        {
            if (visual == null) return;
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].enabled)
                {
                    renderers[i].enabled = false;
                    hidden?.Add(renderers[i]);
                }
            }
        }
    }
}
