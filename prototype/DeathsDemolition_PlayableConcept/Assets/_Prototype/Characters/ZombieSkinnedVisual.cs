using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016 · The AP-007 zombie on the M1 zombie rig (ADR-016-2). Lives on the
    /// <c>Zombie_AP007</c> prefab root (written by <c>ArtCharacterPrefabBuilder</c>): a model
    /// with the 56-bone skeleton, an Animator (Humanoid avatar, locomotion blend), a LODGroup
    /// with four LOD levels and, per level, four SkinnedMeshRenderer slots (body, top,
    /// bottom, head). <see cref="Assemble"/> re-points every slot to the look's meshes by
    /// bone name — the module FBX files list only the bones they weight, so each mesh
    /// carries its own bone order in the library — and hides empty slots (H0 bald).
    /// One material for the whole crowd; the enemy version selects one of four shared
    /// variants (cloth-masked tint), never an instance material.
    ///
    /// Attached under the M1 rig's <c>Visual</c> node by <see cref="Attach"/>; the hitboxes,
    /// the attack socket and the armour anchors move onto the bones so a bullet lands where
    /// the body is drawn and the attack overlap follows the animated chest.
    ///
    /// WP-018 · this component is the zombie's <see cref="IZombieAnimationDriver"/>: the
    /// AP-009 Animator (ADR-018-2 layers: base gait / attacks / stagger / deaths / rise,
    /// override_upper hit L/R, additive regional flinches) replaces the M1 procedural pose
    /// and the ProceduralPoseBridge. <c>ZombieAnimator</c> keeps its gameplay API and
    /// forwards to this driver; the DEC-029 hit window comes back from the clip through
    /// <see cref="CharacterAnimationEvents"/> into <c>EnemyAttackState</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZombieSkinnedVisual : MonoBehaviour, IZombieAnimationDriver
    {
        public const string PrefabName = "Zombie_AP007";
        public const string AnchorHelmet = "anchor_helmet";
        public const string AnchorTorso = "anchor_torso";
        public const string AnchorLegs = "anchor_legs";
        public const int SlotCount = 4;   // body, top, bottom, head
        public const int SlotBody = 0, SlotTop = 1, SlotBottom = 2, SlotHead = 3;

        [SerializeField] private Animator _animator;
        [SerializeField] private LODGroup _lodGroup;
        [Tooltip("LOD-major: index = lod * SlotCount + slot.")]
        [SerializeField] private SkinnedMeshRenderer[] _slots = new SkinnedMeshRenderer[ZombieModuleLibrary.LodCount * SlotCount];
        [SerializeField] private Transform _skeletonRoot;
        [SerializeField] private Transform _anchorHelmet;
        [SerializeField] private Transform _anchorTorso;
        [SerializeField] private Transform _anchorLegs;
        [SerializeField] private CharacterAnimationSet _animation;
        [SerializeField] private CharacterAnimationEvents _events;
        [SerializeField] private CharacterLookAt _lookAt;      // WP-032
        [SerializeField] private FootIkAdapter _footIk;        // WP-032
        [SerializeField] private CharacterAnimatorLod _lod;    // WP-032 (near gate for IK)
        [Tooltip("WP-017 · AP-008 armour renderers, LOD-major: index = (lod * ArmorSlotCount + (helmet 0 | body 1 | leg 2)) * ArmorPartCount + (main 0 | sub 1). Empty until the armour pipeline ran.")]
        [SerializeField] private SkinnedMeshRenderer[] _armorSlots = new SkinnedMeshRenderer[0];

        private Dictionary<string, Transform> _bones;
        // Animator.StringToHash may be negative: never use the id as a "not set" sentinel.
        private static readonly int SpeedId = Animator.StringToHash(CharacterAnimationSet.SpeedParameter);
        private ZombieLook _look;
        private int _materialTier = -1;

        public Animator Animator => _animator;
        public LODGroup LodGroup => _lodGroup;
        public Transform SkeletonRoot => _skeletonRoot;
        public Transform HelmetAnchor => _anchorHelmet;
        public Transform TorsoAnchor => _anchorTorso;
        public Transform LegsAnchor => _anchorLegs;
        public CharacterAnimationEvents Events => _events;
        public CharacterLookAt LookAt => _lookAt;
        public FootIkAdapter FootIk => _footIk;
        public ZombieLook Look => _look;
        public int MaterialTier => _materialTier;
        public CharacterAnimationSet AnimationSet => _animation;

        public SkinnedMeshRenderer Slot(int lod, int slot)
        {
            int i = lod * SlotCount + slot;
            return i >= 0 && i < _slots.Length ? _slots[i] : null;
        }

        public const int ArmorSlotCount = ArmorVisualBinder.SlotCount;   // helmet, body, leg
        public const int ArmorPartCount = ZombieArmorLibrary.PartCount;   // main plate, sub-mesh
        public const int ArmorRendererCount = ZombieModuleLibrary.LodCount * ArmorSlotCount * ArmorPartCount;

        /// <summary>WP-017 · True when the prefab carries the AP-008 armour renderers (two per slot per LOD).</summary>
        public bool HasArmorSlots => _armorSlots != null && _armorSlots.Length == ArmorRendererCount;

        public SkinnedMeshRenderer ArmorRenderer(int lod, int slot, int part)
        {
            int i = (lod * ArmorSlotCount + slot) * ArmorPartCount + part;
            return _armorSlots != null && i >= 0 && i < _armorSlots.Length ? _armorSlots[i] : null;
        }

        /// <summary>Editor (prefab builder): the armour renderers, LOD-major.</summary>
        public void ConfigureArmor(SkinnedMeshRenderer[] armorSlots)
        {
            _armorSlots = armorSlots ?? new SkinnedMeshRenderer[0];
        }

        /// <summary>Re-point one armour renderer to a library mesh by bone name (null = renderer off).</summary>
        public void BindArmor(int lod, int slot, int part, ZombieModuleLibrary.MeshRef mesh)
        {
            SkinnedMeshRenderer r = ArmorRenderer(lod, slot, part);
            if (r == null) return;
            EnsureBones();
            Bind(r, mesh);
        }

        /// <summary>The shared armour material on every armour renderer (no instance material).</summary>
        public void ApplyArmorMaterial(Material m)
        {
            if (_armorSlots == null || m == null) return;
            for (int i = 0; i < _armorSlots.Length; i++)
            {
                if (_armorSlots[i] != null && _armorSlots[i].sharedMaterial != m) _armorSlots[i].sharedMaterial = m;
            }
        }

        /// <summary>Editor (prefab builder).</summary>
        public void Configure(Animator animator, LODGroup lodGroup, SkinnedMeshRenderer[] slots, Transform skeletonRoot,
            Transform anchorHelmet, Transform anchorTorso, Transform anchorLegs, CharacterAnimationSet animation, CharacterAnimationEvents events,
            CharacterLookAt lookAt = null, FootIkAdapter footIk = null, CharacterAnimatorLod lod = null)
        {
            _lookAt = lookAt;
            _footIk = footIk;
            _lod = lod;
            _animator = animator;
            _lodGroup = lodGroup;
            _slots = slots;
            _skeletonRoot = skeletonRoot;
            _anchorHelmet = anchorHelmet;
            _anchorTorso = anchorTorso;
            _anchorLegs = anchorLegs;
            _animation = animation;
            _events = events;
        }

        public Transform Bone(string name)
        {
            EnsureBones();
            return name != null && _bones.TryGetValue(name, out Transform t) ? t : null;
        }

        private void EnsureBones()
        {
            if (_bones != null) return;
            _bones = new Dictionary<string, Transform>(64);
            if (_skeletonRoot == null) return;
            foreach (Transform t in _skeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!_bones.ContainsKey(t.name)) _bones.Add(t.name, t);
            }
        }

        /// <summary>
        /// Re-point the sixteen slots to the look's meshes. Every module mesh is bound by bone
        /// NAME against this instance's skeleton; a slot whose mesh is missing (H0 head, a
        /// module absent from the library) is disabled. Idempotent per look.
        /// </summary>
        /// <summary>True once <see cref="Attach"/> bound this visual to a rig (diagnostics).</summary>
        public bool Attached { get; private set; }

        public bool Assemble(ZombieModuleLibrary library, in ZombieLook look)
        {
            if (library == null || !look.IsValid) return false;
            EnsureBones();
            _look = look;
            ZombieModuleLibrary.Body body = library.FindBody(look.fit);
            ZombieModuleLibrary.Module top = library.FindModule(look.top);
            ZombieModuleLibrary.Module bottom = library.FindModule(look.bottom);
            ZombieModuleLibrary.Module head = look.head == ZombieModuleLibrary.HeadNone ? null : library.FindModule(look.head);
            ZombieModuleLibrary.Variant topV = top != null ? top.Find(look.topColourway, look.fit) : null;
            ZombieModuleLibrary.Variant bottomV = bottom != null ? bottom.Find(look.bottomColourway, look.fit) : null;
            ZombieModuleLibrary.Variant headV = head != null ? head.Find(ZombieModuleLibrary.NoColourway, look.fit) : null;
            bool ok = body != null && topV != null && bottomV != null;

            for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
            {
                Bind(Slot(lod, SlotBody), body != null ? body.lods[lod] : null);
                Bind(Slot(lod, SlotTop), topV != null ? topV.lods[lod] : null);
                Bind(Slot(lod, SlotBottom), bottomV != null ? bottomV.lods[lod] : null);
                Bind(Slot(lod, SlotHead), headV != null ? headV.lods[lod] : null);
            }

            return ok;
        }

        private void Bind(SkinnedMeshRenderer renderer, ZombieModuleLibrary.MeshRef mesh)
        {
            if (renderer == null) return;
            if (mesh == null || mesh.mesh == null || mesh.boneNames == null)
            {
                renderer.sharedMesh = null;
                renderer.enabled = false;
                return;
            }

            var bones = new Transform[mesh.boneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                _bones.TryGetValue(mesh.boneNames[i], out bones[i]);
            }

            renderer.sharedMesh = mesh.mesh;
            renderer.bones = bones;
            renderer.rootBone = Bone(mesh.rootBone) ?? _skeletonRoot;
            renderer.enabled = true;
        }

        /// <summary>The shared material variant of an enemy version on every slot (no instance material).</summary>
        public void ApplyMaterial(ZombieModuleLibrary library, int tierIndex)
        {
            Material m = library != null ? library.MaterialFor(tierIndex) : null;
            if (m == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].sharedMaterial != m) _slots[i].sharedMaterial = m;
            }

            _materialTier = tierIndex;
        }

        /// <summary>Enabled renderers at LOD 0 (tests / probe).</summary>
        public int ActiveSlotCount()
        {
            int n = 0;
            for (int s = 0; s < SlotCount; s++)
            {
                SkinnedMeshRenderer r = Slot(0, s);
                if (r != null && r.enabled && r.sharedMesh != null) n++;
            }

            return n;
        }

        /// <summary>
        /// Parent under the rig's Visual node and become the enemy's animation driver: the
        /// Animator (AP-009 controller when the set is authored, the WP-016 rig-test blend
        /// otherwise) owns every pose; the M1 block rig only keeps its gameplay nodes.
        /// </summary>
        public void Attach(ZombieRig rig, ZombieAnimator animator)
        {
            if (rig == null || rig.Visual == null) return;
            transform.SetParent(rig.Visual, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _rig = rig;

            if (_animator != null)
            {
                // ADR-018-2 → ADR-032-1: the hitboxes and the attack socket hang from the bones, so a
                // culled body (CullUpdateTransforms) froze them in its LAST written pose — a pooled body
                // spawned off screen attacked with a socket left behind its back from a previous life
                // (measured, WP-032). The bones must follow the state machine whenever the body is
                // alive; the crowd budget is the far-body step (CharacterAnimatorLod), not culling.
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.applyRootMotion = false;
                if (_animation != null && _animation.Controller != null && _animator.runtimeAnimatorController == null)
                {
                    _animator.runtimeAnimatorController = _animation.Controller;
                }
            }

            _authored = _animation != null && _animation.IsAuthored && _animator != null && _animator.runtimeAnimatorController != null;
            if (_events == null && _animator != null) _events = _animator.GetComponent<CharacterAnimationEvents>();
            if (_events != null && !_subscribed)
            {
                _events.Raised += OnAnimationEvent;
                _subscribed = true;
            }

            if (animator != null)
            {
                animator.SkinnedLocomotion = true;
                animator.Driver = _authored ? this : null;
            }

            BindIk();
            Attached = true;
        }

        /// <summary>WP-032: LookAt at the threat (EnemyTargeting.Player head) and foot IK on near, living bodies.</summary>
        private void BindIk()
        {
            if (_animator == null) return;
            if (_lookAt == null) _lookAt = _animator.GetComponent<CharacterLookAt>();
            if (_footIk == null) _footIk = _animator.GetComponent<FootIkAdapter>();
            if (_lod == null) _lod = GetComponent<CharacterAnimatorLod>();
            _telegraphLayer = -1;
            for (int i = 0; i < _animator.layerCount; i++)
            {
                if (_animator.GetLayerName(i) == CharacterAnimatorContract.TelegraphLayer) _telegraphLayer = i;
            }

            if (_lookAt != null)
            {
                _lookAt.Bind(
                    () => EnemyTargeting.HasPlayer ? EnemyTargeting.Player.position + Vector3.up * ThreatLookHeight : Vector3.zero,
                    () => _authored && !_dying && !_rising && !_waiting && (_lod == null || !_lod.Far));
            }

            if (_footIk != null)
            {
                _footIk.Bind(() => _authored && !_dying && !_rising, () => _lod == null || !_lod.Far);
            }
        }

        private void Awake()
        {
            // Standalone use (tests, showcase): the driver works without a rig; Attach re-evaluates.
            if (_events == null && _animator != null) _events = _animator.GetComponent<CharacterAnimationEvents>();
            if (_events != null && !_subscribed)
            {
                _events.Raised += OnAnimationEvent;
                _subscribed = true;
            }

            _authored = _animation != null && _animation.IsAuthored && _animator != null && _animator.runtimeAnimatorController != null;
            BindIk();   // WP-032: standalone actors get the threat look-at / foot IK too
        }

        private void OnDestroy()
        {
            if (_events != null && _subscribed) _events.Raised -= OnAnimationEvent;
            _subscribed = false;
        }

        /// <summary>Drive the locomotion blend from the enemy's ground speed (called by ZombieEnemy each tick).</summary>
        public void SetSpeed(float metersPerSecond, float dt)
        {
            if (_animator == null || _animation == null) return;
            if (_authored)
            {
                SetLocomotion(metersPerSecond, dt);
                return;
            }

            // WP-016 rig-test blend. Own smoothing (the damped SetFloat overload measured as a no-op on these animators).
            float target = _animation.SpeedParameterFor(metersPerSecond);
            _speed = dt > 0f ? Mathf.MoveTowards(_speed, target, dt / SpeedSmoothing) : target;
            _animator.SetFloat(SpeedId, _speed);
        }

        private const float SpeedSmoothing = 0.12f;   // seconds per unit of Speed
        private float _speed;

        // ==================================================================== WP-018 driver

        private static readonly int Speed01Id = Animator.StringToHash(CharacterAnimatorContract.Speed01);
        private static readonly int GaitRateId = Animator.StringToHash(CharacterAnimatorContract.GaitRate);
        private static readonly int GaitId = Animator.StringToHash(CharacterAnimatorContract.Gait);
        private static readonly int IdleVariantId = Animator.StringToHash(CharacterAnimatorContract.IdleVariant);
        private static readonly int AttackId = Animator.StringToHash(CharacterAnimatorContract.Attack);
        private static readonly int AttackStyleId = Animator.StringToHash(CharacterAnimatorContract.AttackStyle);
        private static readonly int AttackRateId = Animator.StringToHash(CharacterAnimatorContract.AttackRate);
        private static readonly int StaggerId = Animator.StringToHash(CharacterAnimatorContract.Stagger);
        private static readonly int HitLeftId = Animator.StringToHash(CharacterAnimatorContract.HitLeft);
        private static readonly int HitRightId = Animator.StringToHash(CharacterAnimatorContract.HitRight);
        private static readonly int FlinchHeadId = Animator.StringToHash(CharacterAnimatorContract.FlinchHead);
        private static readonly int FlinchTorsoId = Animator.StringToHash(CharacterAnimatorContract.FlinchTorso);
        private static readonly int FlinchLegId = Animator.StringToHash(CharacterAnimatorContract.FlinchLeg);
        private static readonly int DieId = Animator.StringToHash(CharacterAnimatorContract.Die);
        private static readonly int DeathStyleId = Animator.StringToHash(CharacterAnimatorContract.DeathStyle);
        private static readonly int RiseId = Animator.StringToHash(CharacterAnimatorContract.Rise);
        private static readonly int AliveId = Animator.StringToHash(CharacterAnimatorContract.Alive);
        private static readonly int TelegraphId = Animator.StringToHash(CharacterAnimatorContract.Telegraph);
        private static readonly int TelegraphStyleId = Animator.StringToHash(CharacterAnimatorContract.TelegraphStyle);
        private static readonly int TelegraphRateId = Animator.StringToHash(CharacterAnimatorContract.TelegraphRate);

        [PrototypeOnly(PendingDecisions.None,
            "Height above the threat's feet the zombie looks at (the player's head), metres. Invented.", true)]
        private const float ThreatLookHeight = 1.5f;

        [PrototypeOnly(PendingDecisions.None, "Attack overlap origin on the Visual node: chest height (AP-007 chest 1.17 m − M1 drop 0.05). Invented.", true)]
        private const float AttackSocketHeight = 1.12f;

        [PrototypeOnly(PendingDecisions.None, "Attack overlap origin ahead of the root, metres (= M1 ZombieBodyBuilder.AttackSocketForward). Invented.", true)]
        private const float AttackSocketForward = 0.55f;

        private int _telegraphLayer = -1;
        private float _telegraphEmphasis = 1f;
        private bool _waiting;

        [PrototypeOnly(PendingDecisions.None,
            "Speed fraction (of the version's top speed) at which the gait blend is fully on. The " +
            "idle↔gait crossfade starts at ZombieAnimator.IdlePoseSpeedThreshold; this is where the " +
            "walk cycle owns the pose. Invented.", true)]
        private const float GaitFullSpeed01 = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Lowest playback rate of the gait clip (a crawl below this rate reads as a freeze frame). Invented.", true)]
        public const float MinGaitRate = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Highest playback rate of a gait clip. The Standard's 2.7 m/s over shamble_A 1.46 m/s is ×1.85 (AP-009 README). Invented cap.", true)]
        public const float MaxGaitRate = 2.6f;

        private ZombieRig _rig;
        private bool _authored;
        private bool _subscribed;
        private int _gait;
        private float _gaitClipSpeed = 1f;
        private float _topSpeed = 1f;
        private float _speed01;
        private bool _deathEnded;
        private bool _dying;
        private bool _rising;
        private bool _windowOpen;
        private int _lastWindowStartFrame = -1;
        private int _lastWindowEndFrame = -1;

        /// <summary>True when the AP-009 controller drives this body (false on the rig-test set).</summary>
        public bool Authored => _authored;
        public int Gait => _gait;
        public bool DeathFinished => _deathEnded;
        public bool IsRising => _rising;
        /// <summary>True between hit_window_start and hit_window_end of the current attack clip (DEC-029 readout).</summary>
        public bool HitWindowOpen => _windowOpen;
        public int LastWindowStartFrame => _lastWindowStartFrame;
        public int LastWindowEndFrame => _lastWindowEndFrame;

        /// <summary>Attack-window sink: EnemyAttackState of the owner (set by ZombieEnemy through ZombieAnimator).</summary>
        public IAttackWindowSink WindowSink { get; set; }

        /// <summary>WP-032: additive wind-up emphasis weight this life runs with (0..1; tier telegraph scale).</summary>
        public float TelegraphEmphasis => _telegraphEmphasis;
        /// <summary>WP-032: idle-in-crowd / queue wait (WP-030 hands this in; the body idles with the twitch variant).</summary>
        public bool Waiting => _waiting;
        public int TelegraphLayerIndex => _telegraphLayer;

        private void OnAnimationEvent(CharacterAnimationEvent e)
        {
            switch (e)
            {
                case CharacterAnimationEvent.HitWindowStart:
                    _windowOpen = true;
                    _lastWindowStartFrame = Time.frameCount;
                    WindowSink?.OnHitWindowStart();
                    break;
                case CharacterAnimationEvent.HitWindowEnd:
                    _windowOpen = false;
                    _lastWindowEndFrame = Time.frameCount;
                    WindowSink?.OnHitWindowEnd();
                    break;
                case CharacterAnimationEvent.DeathEnd:
                    if (_dying) _deathEnded = true;
                    break;
                case CharacterAnimationEvent.RiseEnd:
                    _rising = false;
                    break;
            }
        }

        // ---------------------------------------------------- IZombieAnimationDriver

        /// <summary>Spawn: version gait, fit, top speed; every trigger cleared, Alive on. Rise plays when asked (spawn points).</summary>
        public void ResetForSpawn(EnemyTier tier, float topSpeedMetersPerSecond, string fit, int idleVariant, bool rise)
        {
            if (!_authored) return;
            _topSpeed = Mathf.Max(0.1f, topSpeedMetersPerSecond);
            _gait = tier == EnemyTier.Special ? 3 : fit == "B" ? 1 : fit == "C" ? 2 : 0;
            _gaitClipSpeed = Mathf.Max(0.1f, _animation.SpeedOf(CharacterAnimatorContract.ZombieGaitClips[_gait]));
            _speed01 = 0f;
            _dying = false;
            _deathEnded = false;
            _windowOpen = false;
            _rising = false;

            _animator.ResetTrigger(AttackId);
            _animator.ResetTrigger(StaggerId);
            _animator.ResetTrigger(HitLeftId);
            _animator.ResetTrigger(HitRightId);
            _animator.ResetTrigger(FlinchHeadId);
            _animator.ResetTrigger(FlinchTorsoId);
            _animator.ResetTrigger(FlinchLegId);
            _animator.ResetTrigger(DieId);
            _animator.ResetTrigger(RiseId);
            _animator.ResetTrigger(TelegraphId);
            _waiting = false;
            if (_telegraphLayer >= 0) _animator.SetLayerWeight(_telegraphLayer, _telegraphEmphasis);
            if (_lookAt != null) _lookAt.ResetWeight();
            _animator.SetBool(AliveId, true);
            _animator.SetFloat(GaitId, _gait);
            _animator.SetFloat(IdleVariantId, idleVariant & 1);
            _animator.SetFloat(Speed01Id, 0f);
            _animator.SetFloat(GaitRateId, 1f);
            // A pooled body comes back with whatever state it died in: restart the base layer and
            // write the pose NOW, so the bone-mounted gameplay nodes are valid on the spawn frame.
            _animator.Play(CharacterAnimatorContract.LocomotionState, CharacterAnimatorContract.BaseLayer, 0f);
            _animator.Update(0f);
            if (rise)
            {
                _rising = true;
                _animator.SetTrigger(RiseId);
            }
        }

        /// <summary>Re-read the gait after the look changed (fit A/B/C decides the shamble).</summary>
        public void SetFit(string fit, EnemyTier tier)
        {
            if (!_authored) return;
            _gait = tier == EnemyTier.Special ? 3 : fit == "B" ? 1 : fit == "C" ? 2 : 0;
            _gaitClipSpeed = Mathf.Max(0.1f, _animation.SpeedOf(CharacterAnimatorContract.ZombieGaitClips[_gait]));
            _animator.SetFloat(GaitId, _gait);
        }

        private void SetLocomotion(float metersPerSecond, float dt)
        {
            float target = Mathf.Clamp01(metersPerSecond / _topSpeed / GaitFullSpeed01);
            _speed01 = dt > 0f ? Mathf.MoveTowards(_speed01, target, dt / SpeedSmoothing) : target;
            _animator.SetFloat(Speed01Id, _speed01);
            // The rate multiplies the whole Locomotion tree (idle included): ease it back to 1 as
            // the body stops so the idle sway does not crawl at MinGaitRate.
            float rate = Mathf.Clamp(metersPerSecond / _gaitClipSpeed, MinGaitRate, MaxGaitRate);
            _animator.SetFloat(GaitRateId, Mathf.Lerp(1f, rate, _speed01));
        }

        /// <summary>
        /// Start an attack clip. <paramref name="telegraphSeconds"/> is the tier's TelegraphTime:
        /// the clip is rate-scaled so its hit_window_start lands on it (the code timings stay the
        /// source of truth — AP-009 README §event contract). Returns the playback rate.
        /// </summary>
        public float PlayAttack(ZombieAttackStyle style, float telegraphSeconds)
        {
            if (!_authored || _dying) return 1f;
            int index = style == ZombieAttackStyle.Lunge ? 1 : style == ZombieAttackStyle.Swipe ? 0 : 2;   // Sweep and Overhead share the two-armed sweep
            float windowStart = WindowStartSeconds(CharacterAnimatorContract.ZombieAttackClips[index]);
            float rate = telegraphSeconds > 0.01f && windowStart > 0.01f ? windowStart / telegraphSeconds : 1f;
            rate = Mathf.Clamp(rate, 0.25f, 4f);
            _windowOpen = false;
            _animator.SetInteger(AttackStyleId, index);
            _animator.SetFloat(AttackRateId, rate);
            _animator.SetTrigger(AttackId);
            // WP-032: the additive wind-up emphasis spans the wind-up (1 s clip at 1 / telegraph) and is gone by hit_window_start.
            if (_telegraphLayer >= 0 && _telegraphEmphasis > 0.001f && telegraphSeconds > 0.05f)
            {
                _animator.SetInteger(TelegraphStyleId, index);
                _animator.SetFloat(TelegraphRateId, 1f / telegraphSeconds);
                _animator.SetTrigger(TelegraphId);
            }

            return rate;
        }

        /// <summary>The manifest hit_window_start of an attack clip, in seconds at rate 1.</summary>
        public float WindowStartSeconds(string clipName)
        {
            CharacterAnimationSet.Entry e = _animation != null ? _animation.Find(clipName) : null;
            if (e == null || string.IsNullOrEmpty(e.events)) return 0f;
            int frame = FrameOf(e.events, "hit_window_start");
            return frame >= 0 ? frame / (float)CharacterAnimatorContract.Fps : 0f;
        }

        /// <summary>The manifest hit_window_end of an attack clip, in seconds at rate 1.</summary>
        public float WindowEndSeconds(string clipName)
        {
            CharacterAnimationSet.Entry e = _animation != null ? _animation.Find(clipName) : null;
            if (e == null || string.IsNullOrEmpty(e.events)) return 0f;
            int frame = FrameOf(e.events, "hit_window_end");
            return frame >= 0 ? frame / (float)CharacterAnimatorContract.Fps : 0f;
        }

        /// <summary>"name=frame;…" lookup, allocation-free (called on every attack start).</summary>
        public static int FrameOf(string events, string name)
        {
            int at = events.IndexOf(name, System.StringComparison.Ordinal);
            while (at >= 0)
            {
                int eq = at + name.Length;
                bool boundary = (at == 0 || events[at - 1] == ';') && eq < events.Length && events[eq] == '=';
                if (boundary)
                {
                    int value = 0, i = eq + 1;
                    if (i >= events.Length || events[i] < '0' || events[i] > '9') return -1;
                    while (i < events.Length && events[i] >= '0' && events[i] <= '9')
                    {
                        value = value * 10 + (events[i] - '0');
                        i++;
                    }

                    return value;
                }

                at = events.IndexOf(name, at + 1, System.StringComparison.Ordinal);
            }

            return -1;
        }

        /// <summary>A stagger cancelled the attack: back to locomotion (the attack clip stops where it is).</summary>
        public void CancelAttack()
        {
            if (!_authored || _dying) return;
            _windowOpen = false;
            _animator.ResetTrigger(AttackId);
            _animator.ResetTrigger(TelegraphId);
        }

        /// <summary>WP-032: wind-up emphasis (0..1) from the version's telegraph scale (shorter wind-up → full emphasis).</summary>
        public void SetTelegraphEmphasis(float emphasis01)
        {
            _telegraphEmphasis = Mathf.Clamp01(emphasis01);
            if (_authored && _telegraphLayer >= 0) _animator.SetLayerWeight(_telegraphLayer, _telegraphEmphasis);
        }

        /// <summary>WP-032: idle-in-crowd / queue wait (WP-030): the body holds the twitch idle and stops tracking the threat.</summary>
        public void SetWaiting(bool waiting)
        {
            if (!_authored) return;
            _waiting = waiting;
            _animator.SetFloat(IdleVariantId, waiting ? 1f : 0f);
        }

        /// <summary>Light directional hit: override_upper L/R by the bullet's lateral side + the regional additive flinch.</summary>
        public void PlayFlinch(float lateral, DD.Prototype.Damage.BodyRegion region, bool upperBody)
        {
            if (!_authored || _dying) return;
            switch (region)
            {
                case DD.Prototype.Damage.BodyRegion.Head: _animator.SetTrigger(FlinchHeadId); break;
                case DD.Prototype.Damage.BodyRegion.LowerBody: _animator.SetTrigger(FlinchLegId); break;
                default: _animator.SetTrigger(FlinchTorsoId); break;
            }

            if (upperBody)
            {
                _animator.SetTrigger(lateral < 0f ? HitLeftId : HitRightId);
            }
        }

        /// <summary>Full-body heavy stagger (armour break, ADR-018-2). Cancels the attack clip.</summary>
        public void PlayHeavyStagger()
        {
            if (!_authored || _dying) return;
            _windowOpen = false;
            _animator.ResetTrigger(AttackId);
            _animator.SetTrigger(StaggerId);
        }

        /// <summary>One of the four death clips; the body holds the last frame until death_end and the pool takes it.</summary>
        public void PlayDeath(int deathStyle)
        {
            if (!_authored) return;
            _dying = true;
            _deathEnded = false;
            _windowOpen = false;
            _rising = false;
            _animator.ResetTrigger(AttackId);
            _animator.ResetTrigger(StaggerId);
            _animator.ResetTrigger(HitLeftId);
            _animator.ResetTrigger(HitRightId);
            _animator.SetBool(AliveId, false);
            _animator.SetInteger(DeathStyleId, Mathf.Clamp(deathStyle, 0, 3));
            _animator.SetTrigger(DieId);
        }

        /// <summary>Armour binder hook (ArmorVisualBinder): a broken plate is the heavy stagger's only trigger.</summary>
        public void NotifyArmorBroken()
        {
            PlayHeavyStagger();
        }

        /// <summary>
        /// Move the rig's three hitboxes and the armour placeholder roots onto the bones /
        /// anchors, so they follow the drawn body. Positions are the AP-007 anchor origins
        /// (head 1.60 m, chest 1.17 m, hips 0.93 m) plus the M1 hitbox offsets in metres.
        /// </summary>
        public void RehomeGameplayNodes(ZombieRig rig)
        {
            EnsureBones();
            Transform head = HumanoidBones.Bone(_animator, HumanBodyBones.Head);
            Transform chest = HumanoidBones.Bone(_animator, HumanBodyBones.Chest);
            Transform hips = HumanoidBones.Bone(_animator, HumanBodyBones.Hips);
            if (head == null || chest == null || hips == null) return;

            Rehome(rig.HeadHitbox != null ? rig.HeadHitbox.transform : null, head, new Vector3(0f, 0.10f, 0f));
            Rehome(rig.UpperHitbox != null ? rig.UpperHitbox.transform : null, chest, new Vector3(0f, 0.02f, 0f));
            Rehome(rig.LowerHitbox != null ? rig.LowerHitbox.transform : null, hips, new Vector3(0f, -0.40f, 0f));
            // WP-018 hung the attack overlap origin on the animated Chest bone; WP-032 measured that the
            // AP-007 bind pose faces the model's −Z while the (corrected) AP-009 clips face +Z, so a
            // bone-baked forward offset ends up BEHIND the animated body (and froze with a culled
            // pose). The socket therefore lives on the Visual node at the M1 chest height, 0.55 m ahead
            // of the root — the same deterministic geometry the M1 block chest gave (minus its lean);
            // DEC-029 is a timing contract (the clip's hit window), not a hand-tracking one.
            if (rig.AttackSocket != null && rig.Visual != null)
            {
                rig.AttackSocket.SetParent(rig.Visual, false);
                rig.AttackSocket.localPosition = new Vector3(0f, AttackSocketHeight, AttackSocketForward);
                rig.AttackSocket.localRotation = Quaternion.identity;
            }

            if (rig.HeadHitbox is SphereCollider sphere) sphere.center = Vector3.zero;
            if (rig.UpperHitbox is CapsuleCollider upper) upper.center = Vector3.zero;
            if (rig.LowerHitbox is CapsuleCollider lower) lower.center = Vector3.zero;

            // Armour placeholders: roots on the AP-007 anchors (identity orientation at the joint origins).
            RehomeAnchor(rig.HelmetRoot, _anchorHelmet ?? head, Vector3.zero);
            RehomeAnchor(rig.BodyArmorRoot, _anchorTorso ?? chest, new Vector3(0f, 0.10f, 0f));
            RehomeAnchor(rig.LegArmorRoot, _anchorLegs ?? hips, Vector3.zero);
            RehomeLimbPiece(rig, "Kote_L", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            RehomeLimbPiece(rig, "Kote_R", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            RehomeLimbPiece(rig, "Haidate_L", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
            RehomeLimbPiece(rig, "Haidate_R", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
            RehomeLimbPiece(rig, "Suneate_L", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            RehomeLimbPiece(rig, "Suneate_R", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        private static void Rehome(Transform node, Transform bone, Vector3 offsetUp)
        {
            if (node == null || bone == null) return;
            node.SetParent(bone, false);
            node.position = bone.position + offsetUp;
            node.rotation = Quaternion.identity;
        }

        private static void RehomeAnchor(GameObject root, Transform anchor, Vector3 offset)
        {
            if (root == null || anchor == null) return;
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.position = anchor.position + offset;
            root.transform.rotation = Quaternion.identity;
        }

        private void RehomeLimbPiece(ZombieRig rig, string pieceName, HumanBodyBones bone, HumanBodyBones child)
        {
            GameObject piece = FindPiece(rig, pieceName);
            Transform b = HumanoidBones.Bone(_animator, bone);
            Transform c = HumanoidBones.Bone(_animator, child);
            if (piece == null || b == null || c == null) return;
            Vector3 dir = c.position - b.position;
            float length = dir.magnitude;
            if (length < 1e-4f) return;
            Vector3 local = piece.transform.localPosition;   // authored: (x, −0.45·limb, z) along the block's −Y
            float along = -local.y;
            piece.transform.SetParent(b, false);
            piece.transform.rotation = Quaternion.FromToRotation(Vector3.down, dir / length);
            piece.transform.position = b.position + dir / length * along + piece.transform.rotation * new Vector3(local.x, 0f, local.z);
        }

        private static GameObject FindPiece(ZombieRig rig, string name)
        {
            GameObject[][] groups = { rig.HelmetPieces, rig.BodyArmorPieces, rig.LegArmorPieces };
            for (int g = 0; g < groups.Length; g++)
            {
                GameObject[] pieces = groups[g];
                if (pieces == null) continue;
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i] != null && pieces[i].name == name) return pieces[i];
                }
            }

            return null;
        }

        /// <summary>Hide the M1 block renderers (cloth blocks, limbs) but keep the armour pieces, which stay usable placeholders until AP-008.</summary>
        public static void HideBlockRenderers(ZombieRig rig)
        {
            if (rig == null) return;
            Disable(rig.PelvisBlock);
            Disable(rig.TorsoBlock);
            Disable(rig.HeadBlock);
            Disable(rig.SkirtBlock);
            Disable(rig.SashBlock);
            if (rig.LimbBlocks != null)
            {
                for (int i = 0; i < rig.LimbBlocks.Length; i++) Disable(rig.LimbBlocks[i]);
            }
        }

        private static void Disable(Renderer r)
        {
            if (r != null) r.enabled = false;
        }
    }
}
