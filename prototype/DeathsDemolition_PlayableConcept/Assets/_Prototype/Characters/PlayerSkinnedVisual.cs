using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016/018 · The AP-006 player on the M1 player rig. Lives on the <c>Player_AP006</c>
    /// prefab root (written by <c>ArtCharacterPrefabBuilder</c>); <c>PlayerRigFactory</c>
    /// instantiates it under the rig's <c>Visual</c> node, hides the primitive parts and calls
    /// <see cref="Attach"/>. Skin = albedo swap on the one shared material through a property
    /// block (three renderers, one material asset — WP-016 §2); the weapon mount moves to
    /// <c>socket_weapon_r</c>.
    ///
    /// WP-018 (ADR-018-2): the AP-009 Animator owns the pose — base layer 2-D locomotion
    /// (idle / walk_fwd / walk_back / strafe_L / strafe_R on MoveX·MoveY, rate LocoRate),
    /// deaths ×2 and the pickup; <c>override_upper</c> hit_light; <c>additive</c> fire_recoil;
    /// <c>aim_layer</c> = <see cref="PlayerAimLayer"/> turning Spine/Chest/UpperChest toward the
    /// aim (REQ-038) on top of the base. <c>PlayerProceduralAnimator</c> forwards its hooks
    /// here and writes nothing to the hidden block rig; the ProceduralPoseBridge is gone.
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
        [SerializeField] private CharacterAnimationEvents _events;
        [SerializeField] private PlayerAimLayer _aimLayer;

        private MaterialPropertyBlock _block;
        // Animator.StringToHash may be negative: never use the id as a "not set" sentinel.
        private static readonly int SpeedId = Animator.StringToHash(CharacterAnimatorContract.Speed);
        private static readonly int MoveXId = Animator.StringToHash(CharacterAnimatorContract.MoveX);
        private static readonly int MoveYId = Animator.StringToHash(CharacterAnimatorContract.MoveY);
        private static readonly int LocoRateId = Animator.StringToHash(CharacterAnimatorContract.LocoRate);
        private static readonly int FireId = Animator.StringToHash(CharacterAnimatorContract.Fire);
        private static readonly int HitId = Animator.StringToHash(CharacterAnimatorContract.HitLight);
        private static readonly int DieId = Animator.StringToHash(CharacterAnimatorContract.Die);
        private static readonly int DeathStyleId = Animator.StringToHash(CharacterAnimatorContract.DeathStyle);
        private static readonly int PickupId = Animator.StringToHash(CharacterAnimatorContract.Pickup);
        private static readonly int AliveId = Animator.StringToHash(CharacterAnimatorContract.Alive);

        private Func<Vector3> _planarVelocity;
        private Func<float> _referenceSpeed;
        private Func<Vector3> _aimDirection;
        private int _appliedSkin = -1;
        private bool _authored;
        private bool _dying;
        private bool _deathEnded;
        private bool _subscribed;
        private Vector2 _move;
        private float _rate;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds per unit of the locomotion blend parameters (MoveX / MoveY / LocoRate smoothing). " +
            "Same value as the WP-016 Speed smoothing. Invented.", true)]
        private const float MoveSmoothing = 0.12f;

        [PrototypeOnly(PendingDecisions.None,
            "Highest playback rate of the walk clips: the 6.2 m/s Run speed over walk_fwd 2.92 m/s is " +
            "×2.12 (AP-009 README known issue 1 — a run cycle is the follow-up if this reads as skating). Cap, invented.", true)]
        private const float MaxLocoRate = 2.4f;

        public Animator Animator => _animator;
        public LODGroup LodGroup => _lodGroup;
        public SkinnedMeshRenderer[] Renderers => _renderers;
        public Transform WeaponSocket => _weaponSocket;
        public Transform BackSocket => _backSocket;
        public CharacterAnimationSet AnimationSet => _animation;
        public CharacterAnimationEvents Events => _events;
        public PlayerAimLayer AimLayer => _aimLayer;
        public int AppliedSkinIndex => _appliedSkin;
        /// <summary>True when the AP-009 controller drives this body (false on the rig-test set).</summary>
        public bool Authored => _authored;
        public bool IsDying => _dying;
        /// <summary>death_end arrived (the clip holds its last frame).</summary>
        public bool DeathFinished => _deathEnded;
        /// <summary>Smoothed locomotion blend input (tests).</summary>
        public Vector2 Move => _move;
        public float LocoRate => _rate;

        /// <summary>Editor (prefab builder): wire the serialized references once.</summary>
        public void Configure(Animator animator, LODGroup lodGroup, SkinnedMeshRenderer[] renderers, Transform weaponSocket, Transform backSocket,
            CharacterAnimationSet animation, CharacterAnimationEvents events, PlayerAimLayer aimLayer)
        {
            _animator = animator;
            _lodGroup = lodGroup;
            _renderers = renderers ?? new SkinnedMeshRenderer[0];
            _weaponSocket = weaponSocket;
            _backSocket = backSocket;
            _animation = animation;
            _events = events;
            _aimLayer = aimLayer;
        }

        /// <summary>
        /// Bind to the M1 rig: <paramref name="visual"/> is the rig's Visual node (this object is
        /// re-parented under it at the origin). The delegates read the motor / aim each frame
        /// (no reference into the player module from here). Returns the socket the weapon
        /// mount should be parented to (null when the rig has none).
        /// </summary>
        public Transform Attach(Transform visual, Func<Vector3> planarVelocity, Func<float> referenceSpeed, Func<Vector3> aimDirection)
        {
            transform.SetParent(visual, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _planarVelocity = planarVelocity;
            _referenceSpeed = referenceSpeed;
            _aimDirection = aimDirection;

            if (_animator != null)
            {
                // The player is always on screen; AlwaysAnimate keeps the death hold / pickup
                // timing exact even when the camera cuts (results hand-off).
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.applyRootMotion = false;
                if (_animation != null && _animation.Controller != null && _animator.runtimeAnimatorController == null)
                {
                    _animator.runtimeAnimatorController = _animation.Controller;
                }
            }

            _authored = _animation != null && _animation.IsAuthored && _animator != null && _animator.runtimeAnimatorController != null;
            if (_events == null && _animator != null) _events = _animator.GetComponent<CharacterAnimationEvents>();
            if (_aimLayer == null && _animator != null) _aimLayer = _animator.GetComponent<PlayerAimLayer>();
            if (_aimLayer != null) _aimLayer.Bind(_animator, aimDirection, _authored);
            if (!_subscribed)
            {
                if (_events != null) _events.Raised += OnAnimationEvent;
                WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
                _subscribed = true;
            }

            if (_authored)
            {
                _animator.SetBool(AliveId, true);
            }

            return _weaponSocket;
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                if (_events != null) _events.Raised -= OnAnimationEvent;
                WeaponEvents.SpecialWeaponPickedUp -= OnSpecialPickedUp;
                _subscribed = false;
            }
        }

        private void OnAnimationEvent(CharacterAnimationEvent e)
        {
            if (e == CharacterAnimationEvent.DeathEnd && _dying) _deathEnded = true;
        }

        private void OnSpecialPickedUp(float activeDuration)
        {
            PlayPickup();
        }

        // ------------------------------------------------------------------ driver hooks

        /// <summary>Weapon fired: the additive recoil clip (once per trigger pull; stacks naturally by re-trigger).</summary>
        public void PlayFire()
        {
            if (!_authored || _dying) return;
            _animator.SetTrigger(FireId);
        }

        /// <summary>Damage taken: the upper-body hit clip. Never touches movement, aim or fire (REQ-044/045).</summary>
        public void PlayHit()
        {
            if (!_authored || _dying) return;
            _animator.SetTrigger(HitId);
        }

        /// <summary>
        /// One-way death. <paramref name="localHitDirection"/> = where the killing hit pushed the body
        /// (character space): forward → death_fwd, backward → death_back, else <paramref name="coin"/>.
        /// </summary>
        public void PlayDeath(Vector3 localHitDirection, bool coin)
        {
            if (!_authored || _dying) return;
            _dying = true;
            _deathEnded = false;
            int style = localHitDirection.z > 0.2f ? 0 : localHitDirection.z < -0.2f ? 1 : coin ? 0 : 1;
            _animator.SetBool(AliveId, false);
            _animator.SetInteger(DeathStyleId, style);
            _animator.SetTrigger(DieId);
        }

        /// <summary>Special Weapon picked up (WP-007 event): the grab clip on the base layer; movement is not locked.</summary>
        public void PlayPickup()
        {
            if (!_authored || _dying) return;
            _animator.SetTrigger(PickupId);
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

        private float _speed;

        private void Update()
        {
            if (_animator == null || _animation == null) return;
            float dt = Time.deltaTime;
            Vector3 v = _planarVelocity != null ? _planarVelocity() : Vector3.zero;
            float mps = v.magnitude;

            if (!_authored)
            {
                float target = _animation.SpeedParameterFor(mps);
                _speed = dt > 0f ? Mathf.MoveTowards(_speed, target, dt / MoveSmoothing) : target;
                _animator.SetFloat(SpeedId, _speed);
                return;
            }

            if (_dying) return;

            // Local (character-space) velocity over the set's reference speed: the 2-D blend axes.
            // The child time scales were baked against the same reference, so |v| / reference is
            // the exact no-slide rate at the blend corners; near idle the rate eases back to 1 so
            // the idle breath does not freeze.
            float reference = _animation.LocomotionReferenceSpeed;
            Vector3 local = transform.InverseTransformDirection(v) / reference;
            Vector2 targetMove = new Vector2(Mathf.Clamp(local.x, -1f, 1f), Mathf.Clamp(local.z, -1f, 1f));
            float speed01 = Mathf.Clamp01(mps / reference);
            float targetRate = Mathf.Lerp(1f, Mathf.Min(speed01, MaxLocoRate), speed01);
            if (dt > 0f)
            {
                _move = Vector2.MoveTowards(_move, targetMove, dt / MoveSmoothing);
                _rate = Mathf.MoveTowards(_rate, targetRate, dt / MoveSmoothing);
            }
            else
            {
                _move = targetMove;
                _rate = targetRate;
            }

            _animator.SetFloat(MoveXId, _move.x);
            _animator.SetFloat(MoveYId, _move.y);
            // Child time scales in the blend tree are reference / clip speed_mps, so the state
            // speed is simply |v| / reference (idle at 0 has no foot to slide).
            _animator.SetFloat(LocoRateId, Mathf.Max(_rate, 0.05f));
            _ = _referenceSpeed;   // the motor's live top speed stays available for a follow-up run cycle
        }

        /// <summary>Blend-tree child time scale that shows no foot slide at the reference speed (builder).</summary>
        public static float ChildTimeScale(float referenceSpeed, float clipSpeedMps)
        {
            return clipSpeedMps > 0.01f ? Mathf.Min(MaxLocoRate, Mathf.Max(0.1f, referenceSpeed) / clipSpeedMps) : 1f;
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
