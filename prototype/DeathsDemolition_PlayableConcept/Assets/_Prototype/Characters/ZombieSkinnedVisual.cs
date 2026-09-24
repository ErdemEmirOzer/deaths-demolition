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
        /// <summary>
        /// WP-038G: rendering layers of every character renderer — the default bit (sun, lanterns, decals) plus bit 1, the
        /// character fill light's layer (ArtLightingBake.CharacterLightLayer). Runtime-created character renderers (tier parts,
        /// weapons) that should take the fill set this too.
        /// </summary>
        public const uint CharacterRenderingLayers = 1u | (1u << 1);
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
        /// <summary>WP-020 · Per module slot (body/top/bottom/head): the bound module's material names in sub-mesh order; null = atlas only.</summary>
        private readonly string[][] _slotMaterials = new string[SlotCount][];
        /// <summary>WP-038C · Per renderer (LOD-major) override when the bound mesh carries its own material list (fewer sub-meshes than the module).</summary>
        private readonly string[][] _rendererMaterials = new string[ZombieModuleLibrary.LodCount * SlotCount][];

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
            _slotMaterials[SlotBody] = null;
            _slotMaterials[SlotTop] = top != null ? top.materials : null;
            _slotMaterials[SlotBottom] = bottom != null ? bottom.materials : null;
            _slotMaterials[SlotHead] = head != null ? head.materials : null;

            for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
            {
                Bind(Slot(lod, SlotBody), body != null ? body.lods[lod] : null);
                Bind(Slot(lod, SlotTop), topV != null ? topV.lods[lod] : null);
                Bind(Slot(lod, SlotBottom), bottomV != null ? bottomV.lods[lod] : null);
                Bind(Slot(lod, SlotHead), headV != null ? headV.lods[lod] : null);
                _rendererMaterials[lod * SlotCount + SlotBody] = null;
                _rendererMaterials[lod * SlotCount + SlotTop] = topV != null ? topV.lods[lod].materials : null;
                _rendererMaterials[lod * SlotCount + SlotBottom] = bottomV != null ? bottomV.lods[lod].materials : null;
                _rendererMaterials[lod * SlotCount + SlotHead] = headV != null ? headV.lods[lod].materials : null;
            }

            // A re-assembled look changes the sub-mesh layout: re-apply the current version's material arrays.
            if (_materialTier >= 0) ApplyMaterial(library, _materialTier);
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

        /// <summary>
        /// The shared material variant of an enemy version on every slot (no instance material). WP-020: a
        /// module with two sub-meshes (AP-007 v002 cloth atlas) takes the library's cached [atlas, cloth]
        /// array for the version; single-material slots keep the plain shared material.
        /// </summary>
        public void ApplyMaterial(ZombieModuleLibrary library, int tierIndex)
        {
            Material m = library != null ? library.MaterialFor(tierIndex) : null;
            if (m == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                SkinnedMeshRenderer r = _slots[i];
                if (r == null) continue;
                string[] names = (i < _rendererMaterials.Length ? _rendererMaterials[i] : null) ?? _slotMaterials[i % SlotCount];
                if (names != null && names.Length > 1)
                {
                    Material[] set = library.MaterialsFor(names, tierIndex);
                    if (!SameMaterials(r, set)) r.sharedMaterials = set;
                }
                else
                {
                    Material single = names != null && names.Length == 1 && names[0] == ZombieModuleLibrary.MaterialCloth ? library.ClothMaterialFor(tierIndex) : m;
                    if (r.sharedMaterial != single) r.sharedMaterial = single;
                }
            }

            _materialTier = tierIndex;
        }

        private static bool SameMaterials(Renderer r, Material[] set)
        {
            Material[] current = r.sharedMaterials;
            if (current.Length != set.Length) return false;
            for (int i = 0; i < set.Length; i++) if (current[i] != set[i]) return false;
            return true;
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
            // WP-032F: the secondary chains (mino / hair / ties) run on LOD0/1 bodies only, after the Animator; the pool reset restarts them
            if (_chains == null) _chains = _animator.GetComponent<SecondaryChainDriver>();
            if (_chains != null) _chains.Bind(() => _authored && (_lod == null || !_lod.Far));
            _telegraphLayer = -1;
            for (int i = 0; i < _animator.layerCount; i++)
            {
                if (_animator.GetLayerName(i) == CharacterAnimatorContract.TelegraphLayer) _telegraphLayer = i;
            }

            if (_lookAt != null)
            {
                _lookAt.Bind(
                    () => EnemyTargeting.HasPlayer ? EnemyTargeting.Player.position + Vector3.up * ThreatLookHeight : Vector3.zero,
                    () => _authored && !_dying && !_rising && !_waiting && _special == ZombieSpecialVisual.None && (_lod == null || !_lod.Far));
            }

            if (_footIk != null)
            {
                _footIk.Bind(() => _authored && !_dying && !_rising, () => _lod == null || !_lod.Far,
                    () => _speed01 > 0.2f && _lastRate <= CadenceBand.LockMaxRate);   // WP-071: foot lock only where the cadence is honest
            }

            // WP-071 B2: procedural lean (accel / turn) on the spine, near bodies only
            if (_lean == null) _lean = _animator.GetComponent<LocomotionLean>() ?? _animator.gameObject.AddComponent<LocomotionLean>();
            _lean.Configure(HumanoidBones.Bone(_animator, HumanBodyBones.Spine), transform, () => _authored && !_dying && !_rising && (_lod == null || !_lod.Far));
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
            EndRagdoll();
        }

        private void OnDisable() => EndRagdoll();

        // ---------------------------------------------------- WP-071 death → ragdoll (K4 / K5)

        private Transform[] _rdBones, _rdTips;
        private Vector3[] _rdPrev, _rdVel;
        private RagdollProxyPool.Proxy _ragdoll;
        private bool _ragdollAnimatorOff;
        private bool _handoffNow;
        private float _deathClock = -1f, _handoffAt;
        private Vector3 _deathPush;
        private float _deathIntensity = 1f;

        /// <summary>WP-071: the death family of the current death (tests / probes; S-03 WP-076 reads it).</summary>
        public ZombieDeathFamily DeathFamily { get; private set; }
        /// <summary>WP-071: a ragdoll proxy is falling for this body right now.</summary>
        public bool RagdollActive => _ragdoll != null;
        /// <summary>WP-071: this life's death went to physics (false = the authored fall, e.g. pool full).</summary>
        public bool DiedByRagdoll { get; private set; }
        /// <summary>WP-071: seconds after PlayDeath at which the ragdoll took over (−1 = none).</summary>
        public float RagdollHandoffSeconds { get; private set; } = -1f;

        /// <summary>WP-071: the killing push (flat world direction the body is shoved) and its intensity (M1 death intensity, ≈ 1 … 3); call before PlayDeath.</summary>
        public void SetDeathImpulse(Vector3 worldPush, float intensity)
        {
            worldPush.y = 0f;
            _deathPush = worldPush.sqrMagnitude > 1e-4f ? worldPush.normalized : Vector3.zero;
            _deathIntensity = Mathf.Max(0f, intensity);
            _hasFullImpulse = false;
        }

        private bool _hasFullImpulse;
        private DD.Prototype.Damage.DeathImpulse _impulse;

        /// <summary>
        /// WP-071 B3: S-03's killing impulse (DEC-096). The clip family stays ZombieAnimator's pick (direction / region / intensity);
        /// the impulse adds what it knows: the body state (electrocuted → arc_electro, sliding → gel_slip), the weapon class
        /// (shotgun throws, sniper snaps, launcher lifts and takes the legs, LMG drops), and the body's run speed carried into the fall.
        /// </summary>
        public void SetDeathImpulse(in DD.Prototype.Damage.DeathImpulse impulse)
        {
            SetDeathImpulse(impulse.Direction, Mathf.Max(_deathIntensity, impulse.Force));
            _impulse = impulse;
            _hasFullImpulse = true;
        }

        /// <summary>WP-071 B3: push multiplier and lift (m/s) per weapon class (III A3.2; the class tells how a body goes down).</summary>
        public static void PushOf(DD.Prototype.Core.Weapons.WeaponClass c, out float scale, out float lift)
        {
            switch (c)
            {
                case DD.Prototype.Core.Weapons.WeaponClass.Shotgun: scale = 1.35f; lift = 0.6f; break;
                case DD.Prototype.Core.Weapons.WeaponClass.Sniper: scale = 1.2f; lift = 0.2f; break;
                case DD.Prototype.Core.Weapons.WeaponClass.Launcher: scale = 1.7f; lift = 3.0f; break;
                case DD.Prototype.Core.Weapons.WeaponClass.Lmg: scale = 0.85f; lift = 0f; break;
                case DD.Prototype.Core.Weapons.WeaponClass.Smg: scale = 0.9f; lift = 0f; break;
                default: scale = 1f; lift = 0f; break;
            }
        }

        private void EnsureRagdollBones()
        {
            if (_rdBones != null || _animator == null) return;
            int n = RagdollProxyPool.BodyCount;
            _rdBones = new Transform[n];
            _rdTips = new Transform[n];
            _rdPrev = new Vector3[n];
            _rdVel = new Vector3[n];
            HumanBodyBones[] tips =
            {
                HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
            };
            for (int i = 0; i < n; i++)
            {
                _rdBones[i] = HumanoidBones.Bone(_animator, RagdollProxyPool.Bones[i]);
                _rdTips[i] = HumanoidBones.Bone(_animator, tips[i]);
            }

            if (_rdBones[1] == null) _rdBones[1] = HumanoidBones.Bone(_animator, HumanBodyBones.Spine);
            RagdollProxyPool.Warm();
        }

        private string DeathClipName(int deathStyle)
        {
            int i = Mathf.Clamp(deathStyle, 0, CharacterAnimatorContract.ZombieDeathClips.Length - 1);
            string v3 = CharacterAnimatorContract.ZombieDeathClipsV3[i];
            return _animation != null && _animation.Find(v3) != null ? v3 : CharacterAnimatorContract.ZombieDeathClips[i];
        }

        private bool _visualRestCaptured;
        private Vector3 _visualRestPos;
        private Quaternion _visualRestRot;

        private void CaptureVisualRest()
        {
            if (_visualRestCaptured || _animator == null) return;
            _visualRestCaptured = true;
            _visualRestPos = _animator.transform.localPosition;
            _visualRestRot = _animator.transform.localRotation;
        }

        /// <summary>WP-071: a pooled body comes back with the Animator's node where the prefab put it (the death's root motion moved it).</summary>
        private void RestoreVisualRest()
        {
            if (_animator == null) return;
            _animator.applyRootMotion = false;
            if (_visualRestCaptured) { _animator.transform.localPosition = _visualRestPos; _animator.transform.localRotation = _visualRestRot; }
        }

        private static float HandoffSecondsOf(ZombieDeathFamily f) =>
            f == ZombieDeathFamily.FallBack ? 0.27f : f == ZombieDeathFamily.FallForward ? 0.30f : f == ZombieDeathFamily.SpinSide ? 0.33f
            : f == ZombieDeathFamily.LegsOut ? 0.37f : f == ZombieDeathFamily.UltraTimber ? 1.10f : 0.30f;

        private void BeginDeathClock(int deathStyle)
        {
            DeathFamily = ZombieDeathFamilies.OfDeathIndex(deathStyle);
            if (_hasFullImpulse)
            {
                // WP-071 B3: what the clip index cannot know
                if (_impulse.State == DD.Prototype.Damage.ZombieBodyState.Electrocuted || _special == ZombieSpecialVisual.Electrocuted) DeathFamily = ZombieDeathFamily.ArcElectro;
                else if (_impulse.State == DD.Prototype.Damage.ZombieBodyState.Sliding || _special == ZombieSpecialVisual.Slipping) DeathFamily = ZombieDeathFamily.GelSlip;
                else if (_impulse.WeaponKnown && _impulse.WeaponClass == DD.Prototype.Core.Weapons.WeaponClass.Launcher && DeathFamily != ZombieDeathFamily.UltraTimber) DeathFamily = ZombieDeathFamily.LegsOut;
            }

            ZombieDeathFamilies.Record(DeathFamily);   // WP-078 soak distribution
            DiedByRagdoll = false;
            RagdollHandoffSeconds = -1f;
            _handoffNow = false;
            _handoffAt = HandoffSecondsOf(DeathFamily);
            // WP-071 AP-022: an onset clip that names its own hand-off drives it (the event); the clock is then only the fallback
            string deathClip = DeathClipName(deathStyle);
            CharacterAnimationSet.Entry de = deathClip != null ? _animation.Find(deathClip) : null;
            int hf = de != null && !string.IsNullOrEmpty(de.events) ? FrameOf(de.events, "ragdoll_handoff") : -1;
            if (hf >= 0) _handoffAt = hf / (float)CharacterAnimatorContract.Fps + 0.25f;
            // ... and its authored travel / heading is applied to the visual (the gameplay root no longer moves after death, A3.7)
            if (de != null && deathClip.StartsWith("AP022_", System.StringComparison.Ordinal)) { CaptureVisualRest(); _animator.applyRootMotion = true; }
            _deathClock = 0f;
            if (_rdBones == null) return;
            for (int i = 0; i < _rdBones.Length; i++) if (_rdBones[i] != null) _rdPrev[i] = _rdBones[i].position;
        }

        [PrototypeOnly(PendingDecisions.None, "WP-071 B3 electrocution: Animator speed while seized, spasm frequency (Hz) and amplitude (degrees) on spine / head / upper arms. Invented / II A5.3.", true)]
        public const float ElectroAnimatorSpeed = 0.15f, SpasmHz = 6f, SpasmDegrees = 7f;
        private float _spasmClock;
        /// <summary>WP-071 B3: last spasm offset applied (degrees; tests).</summary>
        public float LastSpasm { get; private set; }

        private void Spasm()
        {
            if (_special != ZombieSpecialVisual.Electrocuted || _dying || _rdBones == null) return;
            _spasmClock += Time.deltaTime;
            // three incommensurate sines (II A5.3: head 4 Hz, arms 6 Hz, legs 2 Hz irregular) — deterministic, no allocation
            float t = _spasmClock * SpasmHz;
            float a = Mathf.Sin(t * 6.2832f) * SpasmDegrees, b = Mathf.Sin(t * 4.1887f + 1.3f) * SpasmDegrees, c = Mathf.Sin(t * 9.4248f + 0.7f) * SpasmDegrees * 0.6f;
            LastSpasm = a;
            if (_rdBones[1] != null) _rdBones[1].rotation = Quaternion.AngleAxis(a * 0.5f, transform.right) * Quaternion.AngleAxis(c, transform.forward) * _rdBones[1].rotation;
            if (_rdBones[2] != null) _rdBones[2].rotation = Quaternion.AngleAxis(b, transform.forward) * _rdBones[2].rotation;
            if (_rdBones[3] != null) _rdBones[3].rotation = Quaternion.AngleAxis(c * 2f, transform.forward) * _rdBones[3].rotation;
            if (_rdBones[5] != null) _rdBones[5].rotation = Quaternion.AngleAxis(-a * 1.5f, transform.forward) * _rdBones[5].rotation;
        }

        private void LateUpdate()
        {
            Spasm();
            if (_deathClock < 0f) return;
            float dt = Time.deltaTime;
            if (_ragdoll != null)
            {
                if (!_ragdoll.active)
                {
                    _ragdoll = null;   // the pool went away with its scene mid-fall: the body keeps its pose and finishes
                    _deathClock = -1f;
                    _deathEnded = true;
                    return;
                }

                if (RagdollProxyPool.Settled(_ragdoll))
                {
                    RagdollProxyPool.Release(_ragdoll);   // the fallen pose stays on the bones
                    _ragdoll = null;
                    _deathClock = -1f;
                    _deathEnded = true;
                }

                return;
            }

            if (!_dying || _rdBones == null || _rdBones[0] == null) return;
            _deathClock += dt;
            // the bones' own motion under the onset clip carries into the ragdoll (no zero-velocity hand-off)
            float inv = dt > 1e-5f ? 1f / dt : 0f;
            for (int i = 0; i < _rdBones.Length; i++)
            {
                if (_rdBones[i] == null) continue;
                Vector3 now = _rdBones[i].position;
                _rdVel[i] = (now - _rdPrev[i]) * inv;
                _rdPrev[i] = now;
            }

            if (!_handoffNow && _deathClock < _handoffAt) return;
            _handoffNow = false;
            Vector3 push = _deathPush.sqrMagnitude > 0f ? _deathPush : -transform.forward;
            float speed = Mathf.Clamp(1.0f + 0.9f * _deathIntensity, 1.4f, 4.5f) * (DeathFamily == ZombieDeathFamily.UltraTimber ? 0.5f : 1f);
            Vector3 extra = Vector3.zero;
            if (_hasFullImpulse)
            {
                float scale = 1f, lift = 0f;
                if (_impulse.WeaponKnown) PushOf(_impulse.WeaponClass, out scale, out lift);
                speed *= scale;
                extra = Vector3.up * lift + transform.forward * (_impulse.BodySpeed * 0.6f);   // a running body carries its momentum into the fall
                if (DeathFamily == ZombieDeathFamily.GelSlip) extra += transform.forward * 1.5f + Vector3.up * 0.5f;   // the feet shoot forward on the gel
            }
            float spin = DeathFamily == ZombieDeathFamily.SpinSide ? 3.5f * (Vector3.Cross(transform.forward, push).y >= 0f ? 1f : -1f) : 0f;
            // review R1-A #12: a far-LOD body's bones move only on its 15 Hz steps — the per-frame velocity is 0 or a spike, so it gets the push alone
            bool farBody = _lod != null && _lod.Far;
            _ragdoll = RagdollProxyPool.Acquire(GetInstanceID(), _rdBones, _rdTips, farBody ? null : _rdVel, push * speed, spin, extra);
            if (_ragdoll == null)
            {
                _deathClock = -1f;   // pool full: the authored fall finishes the death (death_end)
                return;
            }

            DiedByRagdoll = true;
            RagdollHandoffSeconds = _deathClock;
            if (_lod != null) _lod.Suspend();
            _ragdollAnimatorOff = _animator.enabled;
            _animator.enabled = false;
        }

        private void EndRagdoll()
        {
            if (_ragdoll != null) RagdollProxyPool.Release(_ragdoll);
            _ragdoll = null;
            _deathClock = -1f;
            _handoffNow = false;
            if (_ragdollAnimatorOff && _animator != null) _animator.enabled = true;
            _ragdollAnimatorOff = false;
            if (_lod != null && _lod.Suspended) _lod.Resume();
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
        private static readonly int AttackMirrorId = Animator.StringToHash(CharacterAnimatorContract.AttackMirror);
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
        private static readonly int WaitingId = Animator.StringToHash(CharacterAnimatorContract.Waiting);       // WP-032B
        private static readonly int WaitPhaseId = Animator.StringToHash(CharacterAnimatorContract.WaitPhase);   // WP-032B
        private static readonly int HitId = Animator.StringToHash(CharacterAnimatorContract.HitLight);           // WP-032C directional × tier
        private static readonly int HitDirId = Animator.StringToHash(CharacterAnimatorContract.HitDir);
        private static readonly int HitTierId = Animator.StringToHash(CharacterAnimatorContract.HitTier);
        private static readonly int RiseVariantId = Animator.StringToHash(CharacterAnimatorContract.RiseVariant);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGaitStatics() { s_gaitOrdinal = 0; GaitVariety = 1f; }
        private static int s_riseOrdinal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRiseOrdinal() { s_riseOrdinal = 0; }
        private static readonly int ArmorBreakId = Animator.StringToHash(CharacterAnimatorContract.ArmorBreak);
        private static readonly int ArmorBreakRegionId = Animator.StringToHash(CharacterAnimatorContract.ArmorBreakRegion);
        private static readonly int StumbleId = Animator.StringToHash(CharacterAnimatorContract.Stumble);
        private int _hitTier;   // WP-032C: 0 std (Standard / Special), 1 elite, 2 ultra — from the spawn tier
        /// <summary>WP-032C: the v003 hit tier index the directional reactions use (tests).</summary>
        public int HitTier => _hitTier;

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
            "Lowest playback rate of the gait clip. WP-071 K3: the cadence band (CadenceBand.Min).", true)]
        public const float MinGaitRate = CadenceBand.Min;

        [PrototypeOnly(PendingDecisions.None,
            "Highest playback rate of a gait clip. WP-071 K3 (N5): the cadence band (CadenceBand.Max) — faster travel slides (Standard 2.7 m/s over shamble_A is ×1.2 + slide until AP-022 shamble_fast).", true)]
        public const float MaxGaitRate = CadenceBand.Max;

        [PrototypeOnly(PendingDecisions.None, "WP-071 III A2.5 per-spawn cadence spread (± fraction) so a crowd does not march in step. Invented.", true)]
        public const float GaitSpread = 0.08f;
        /// <summary>WP-071: scales the per-spawn gait spread (1 = design, 0 = off — tests that measure exact rates).</summary>
        public static float GaitVariety = 1f;
        private static int s_gaitOrdinal;
        private float _gaitMul = 1f;
        private float _lastRate = 1f;
        private float _lastSpeedMps;
        private LocomotionLean _lean;
        /// <summary>WP-071: this spawn's cadence multiplier (gait profile) and the gait clip rate last set (tests).</summary>
        public float GaitMultiplier => _gaitMul;
        public float GaitRate => _lastRate;
        public LocomotionLean Lean => _lean;

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
        private float _heavyStaggerUntil = -1f;   // WP-040: the hit_heavy_stagger clip holds the body until stagger_end (capped)
        private bool _windowOpen;
        private int _lastWindowStartFrame = -1;
        private int _lastWindowEndFrame = -1;

        /// <summary>True when the AP-009 controller drives this body (false on the rig-test set).</summary>
        public bool Authored => _authored;
        public int Gait => _gait;
        public bool DeathFinished => _deathEnded;
        public bool IsRising => _rising;

        [PrototypeOnly(PendingDecisions.None, "WP-040: cap on the heavy stagger hold when the clip's stagger_end never arrives (clip 0.9 s + grace).", true)]
        public const float HeavyStaggerCapSeconds = 1.4f;
        /// <summary>WP-040: the armour-break heavy stagger clip is playing (from PlayHeavyStagger until the clip's stagger_end, capped). The enemy neither walks nor swings meanwhile.</summary>
        public bool IsHeavyStaggering => _heavyStaggerUntil > 0f && Time.time < _heavyStaggerUntil;
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
                    if (_dying && _ragdoll == null) _deathEnded = true;
                    break;
                case CharacterAnimationEvent.FootLeft:
                case CharacterAnimationEvent.FootRight:
                case CharacterAnimationEvent.FootLOff:
                case CharacterAnimationEvent.FootROff:
                    if (_footIk != null) PlayerSkinnedVisual.FootLockEvent(_footIk, e);   // WP-071 B2 foot lock
                    break;
                case CharacterAnimationEvent.RagdollHandoff:
                    if (_dying) _handoffNow = true;   // WP-071: an AP-022 onset clip names its own hand-off frame
                    break;
                case CharacterAnimationEvent.RiseEnd:
                    _rising = false;
                    break;
                case CharacterAnimationEvent.StaggerEnd:
                    _heavyStaggerUntil = -1f;   // WP-040
                    break;
            }
        }

        // ---------------------------------------------------- IZombieAnimationDriver

        /// <summary>Spawn: version gait, fit, top speed; every trigger cleared, Alive on. Rise plays when asked (spawn points).</summary>
        public void ResetForSpawn(EnemyTier tier, float topSpeedMetersPerSecond, string fit, int idleVariant, bool rise)
        {
            if (!_authored) return;
            EndRagdoll();   // WP-071: a pooled body comes back animated (proxy returned, Animator + LOD running)
            RestoreVisualRest();
            _hasFullImpulse = false;
            _lastOverrideHit = -10f;
            _kbActive = false; _kbVelocity = Vector3.zero; _tremorClock = 0f;   // WP-071 B5
            EnsureRagdollBones();
            RagdollProxyPool.ArmRoundLog();   // WP-078 per-round line
            _topSpeed = Mathf.Max(0.1f, topSpeedMetersPerSecond);
            _gait = tier == EnemyTier.Special ? 3 : fit == "B" ? 1 : fit == "C" ? 2 : 0;
            _gaitClipSpeed = Mathf.Max(0.1f, _animation.SpeedOf(CharacterAnimatorContract.ZombieGaitClips[_gait]));
            _speed01 = 0f;
            _dying = false;
            _deathEnded = false;
            _windowOpen = false;
            _rising = false;
            _heavyStaggerUntil = -1f;   // WP-040

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
            _animator.ResetTrigger(HitId);
            _animator.ResetTrigger(ArmorBreakId);
            _animator.ResetTrigger(StumbleId);
            // WP-032C: the directional hit reads scale with the tier (StaggerSusceptibility 1.0 / 0.45 / 0.12 in the clips); the
            // rise variant (A 48 f / B 36 f / C 28 f) is a deterministic per-spawn pick — B/C are the ≤ 1.2 s camera-off cuts.
            _hitTier = tier == EnemyTier.Elite ? 1 : tier == EnemyTier.Ultra ? 2 : 0;
            _animator.SetInteger(HitTierId, _hitTier);
            // WP-032F: the pick walks a spawn ordinal — the instance-id bits it used before went constant once the 73-bone rig
            // changed the id stride between pooled bodies (six spawns, one variant).
            _animator.SetInteger(RiseVariantId, (idleVariant + s_riseOrdinal++) % CharacterAnimatorContract.ZombieRiseClips.Length);
            _waiting = false;
            _special = ZombieSpecialVisual.None;   // WP-032E: a pooled body comes back free and at speed 1
            _animator.speed = 1f;
            if (_chains != null) _chains.Reset();   // WP-032F: tails restart from the rest pose at the new spawn point
            _animator.SetBool(WaitingId, false);
            // WP-032B: the queue idle's cycle offset — deterministic per spawn (the idle variant coin's neighbour), so a throat queue is out of step.
            _animator.SetFloat(WaitPhaseId, Mathf.Repeat((idleVariant * 0.618034f) + (GetInstanceID() & 1023) / 1024f, 1f));
            if (_telegraphLayer >= 0) _animator.SetLayerWeight(_telegraphLayer, _telegraphEmphasis);
            if (_lookAt != null) _lookAt.ResetWeight();
            _animator.SetBool(AliveId, true);
            _animator.SetFloat(GaitId, _gait);
            _animator.SetFloat(IdleVariantId, idleVariant & 1);
            _animator.SetFloat(Speed01Id, 0f);
            _animator.SetFloat(GaitRateId, 1f);
            // A pooled body comes back with whatever state it died in: restart the base layer and
            // write the pose NOW, so the bone-mounted gameplay nodes are valid on the spawn frame.
            // WP-071 III A2.5 gait profile: a deterministic per-spawn cadence spread and cycle phase (the crowd is out of step)
            int g = s_gaitOrdinal++;
            _gaitMul = 1f + GaitVariety * GaitSpread * (Mathf.Repeat(g * 0.381966f, 1f) * 2f - 1f);
            _lastRate = 1f;
            if (_lean != null) _lean.ResetLean();
            _animator.Play(CharacterAnimatorContract.LocomotionState, CharacterAnimatorContract.BaseLayer, GaitVariety > 0f ? Mathf.Repeat(g * 0.618034f, 1f) : 0f);
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
            // WP-071 K3: the cadence stays in the band (slide covers the rest), spread per spawn (gait profile)
            float rate = Mathf.Clamp(metersPerSecond / _gaitClipSpeed * _gaitMul, MinGaitRate, MaxGaitRate);
            _lastRate = rate;
            _lastSpeedMps = metersPerSecond;
            _animator.SetFloat(GaitRateId, Mathf.Lerp(1f, rate, _speed01));
            if (_lean != null) _lean.Feed(transform.forward * metersPerSecond + _kbVelocity);   // B5: + the knockback shove
        }

        /// <summary>
        /// Start an attack clip. <paramref name="telegraphSeconds"/> is the tier's TelegraphTime:
        /// the clip is rate-scaled so its hit_window_start lands on it (the code timings stay the
        /// source of truth — AP-009 README §event contract). Returns the playback rate.
        /// </summary>
        public float PlayAttack(ZombieAttackStyle style, float telegraphSeconds)
        {
            if (!_authored || _dying) return 1f;
            // WP-032B: the Ultra overhead has its own v002 clip (Attack_Overhead); a set without it (v001) falls back to the sweep.
            // WP-071 B5: bite_grab plays the lunge (both arms reach, the dive closes) until AP-022's attack_bite_grab
            int index = style == ZombieAttackStyle.Lunge || style == ZombieAttackStyle.BiteGrab ? 1 : style == ZombieAttackStyle.Swipe ? 0
                : style == ZombieAttackStyle.Overhead && _animation.Find(CharacterAnimatorContract.ZombieAttackOverhead) != null ? 3 : 2;
            bool mirror = style == ZombieAttackStyle.Swipe && WindowSink != null && WindowSink.SwipeMirror;
            // WP-071 AP-022 slice 1: bite_grab and swipe_L have their own clips (states 4 / 5)
            bool v3 = _animation.Find(CharacterAnimatorContract.ZombieAttackClipsV3[0]) != null;
            if (v3 && style == ZombieAttackStyle.BiteGrab) index = 4;
            if (v3 && mirror) { index = 5; mirror = false; }
            string clipName = v3 ? CharacterAnimatorContract.ZombieAttackClipsV3[index] : CharacterAnimatorContract.ZombieAttackClips[index];
            float windowStart = WindowStartSeconds(clipName);
            float rate = telegraphSeconds > 0.01f && windowStart > 0.01f ? windowStart / telegraphSeconds : 1f;
            rate = Mathf.Clamp(rate, 0.25f, 4f);
            _windowOpen = false;
            _animator.SetInteger(AttackStyleId, index);
            _animator.SetBool(AttackMirrorId, mirror);   // swipe_L as a mirror only on a set without the AP-022 clip
            _animator.SetFloat(AttackRateId, rate);
            _animator.SetTrigger(AttackId);
            // WP-032: the additive wind-up emphasis spans the wind-up (1 s clip at 1 / telegraph) and is gone by hit_window_start.
            if (_telegraphLayer >= 0 && _telegraphEmphasis > 0.001f && telegraphSeconds > 0.05f)
            {
                _animator.SetInteger(TelegraphStyleId, index == 4 ? 1 : index == 5 ? 0 : Mathf.Min(index, 2));   // the overhead shares the sweep's wide-arm emphasis; bite = lunge's, swipe_L = swipe's
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

        /// <summary>WP-032: idle-in-crowd / queue wait (WP-030): the body holds the queue idle (WP-032B: the v002 idle_queue Wait state, cycle offset per body) and stops tracking the threat.</summary>
        public void SetWaiting(bool waiting)
        {
            if (!_authored) return;
            _waiting = waiting;
            _animator.SetBool(WaitingId, waiting || _special == ZombieSpecialVisual.Dazed);
        }

        private ZombieSpecialVisual _special;
        private SecondaryChainDriver _chains;   // WP-032F
        /// <summary>WP-032F: the secondary-motion driver on the Model (null on the rig-test body).</summary>
        public SecondaryChainDriver Chains => _chains;
        /// <summary>WP-032E: the Special Weapon reaction currently shown.</summary>
        public ZombieSpecialVisual SpecialVisual => _special;

        /// <summary>
        /// WP-032E: Special Weapon reactions on the existing clips. Netted (Ofuda Loom) freezes the Animator where it is
        /// (speed 0; hit reads and the window stop with it — the bind is a full stop) and drops the look-at; Dazed (Kagura
        /// Bell) plays the Runner stumble once and parks the body in the queue sway with the look-at off; None restores speed
        /// 1 and the wait flag S-03 last set. Death and the pool reset clear it.
        /// </summary>
        public void SetSpecialVisual(ZombieSpecialVisual state)
        {
            if (!_authored) return;
            if (_special == state) return;
            ZombieSpecialVisual previous = _special;
            _special = state;
            switch (state)
            {
                case ZombieSpecialVisual.Netted:
                    _windowOpen = false;
                    _animator.ResetTrigger(AttackId);
                    _animator.ResetTrigger(TelegraphId);
                    _animator.speed = 0f;
                    break;
                case ZombieSpecialVisual.Dazed:
                case ZombieSpecialVisual.Slipping:   // WP-071 B3: the gel takes the feet — the stumble, then the sway (AP-022 hit_gel_slip replaces it)
                    _animator.speed = 1f;
                    _windowOpen = false;
                    _animator.ResetTrigger(AttackId);
                    if (!_dying) _animator.SetTrigger(StumbleId);
                    _animator.SetBool(WaitingId, true);
                    break;
                case ZombieSpecialVisual.Electrocuted:   // WP-071 B3: seized — slowed pose + the procedural spasm (AP-022 hit_electro_loop replaces it)
                    _windowOpen = false;
                    _animator.ResetTrigger(AttackId);
                    _animator.ResetTrigger(TelegraphId);
                    _animator.speed = ElectroAnimatorSpeed;
                    _spasmClock = 0f;
                    break;
                default:
                    _animator.speed = 1f;
                    if (previous == ZombieSpecialVisual.Dazed || previous == ZombieSpecialVisual.Slipping) _animator.SetBool(WaitingId, _waiting);
                    break;
            }
        }

        /// <summary>Light directional hit: override_upper L/R by the bullet's lateral side + the regional additive flinch.</summary>
        // ---------------------------------------------------- WP-071 B5 hit response (S-03 §3.4 read surface)

        [PrototypeOnly(PendingDecisions.None, "WP-071 B5: LMG suppression tremble — one additive torso flinch this often while suppressed, seconds. Invented / III A5.3.", true)]
        public const float SuppressionTremorSeconds = 0.15f;
        private bool _kbActive;
        private Vector3 _kbVelocity;
        private float _tremorClock;
        /// <summary>WP-071 B5 readouts: knockback reads played, suppression tremors.</summary>
        public int KnockbackReads { get; private set; }
        public int SuppressionTremors { get; private set; }

        public void SetHitResponse(in KnockbackState knockback, bool suppressed)
        {
            if (!_authored || _dying || _rising) { _kbActive = false; _kbVelocity = Vector3.zero; return; }
            _kbVelocity = knockback.Active ? knockback.Direction * knockback.Speed : Vector3.zero;   // the lean reads the real shove
            if (knockback.Active && !_kbActive)
            {
                // the shove's first frame: the directional hit read at full weight (a knockback is never a suppressed accumulation)
                _animator.SetInteger(HitDirId, HitDirectionIndex(transform.InverseTransformDirection(knockback.Direction)));
                _animator.SetTrigger(HitId);
                _lastOverrideHit = Time.time;
                KnockbackReads++;
            }

            _kbActive = knockback.Active;
            if (suppressed)
            {
                _tremorClock -= Time.deltaTime;
                if (_tremorClock <= 0f)
                {
                    _tremorClock = SuppressionTremorSeconds;
                    _animator.SetTrigger(FlinchTorsoId);
                    SuppressionTremors++;
                }
            }
            else
            {
                _tremorClock = 0f;
            }
        }

        [PrototypeOnly(PendingDecisions.None, "WP-071 B3 (III A5.3): the override hit read re-triggers at most this often; faster hits (LMG) feed the additive flinch only — continuous reaction without a stun-lock. Invented.", true)]
        public const float HitRetriggerSeconds = 0.22f;
        private float _lastOverrideHit = -10f;
        /// <summary>WP-071 B3 readouts: override hit reads played / suppressed (window-safe or accumulated).</summary>
        public int OverrideHits { get; private set; }
        public int SuppressedHits { get; private set; }

        public void PlayFlinch(Vector3 localHit, DD.Prototype.Damage.BodyRegion region, bool upperBody)
        {
            if (!_authored || _dying) return;
            // WP-071 B3 (K9 / III A5): inside the DEC-029 hit window only the additive flinch plays (an override would cut the
            // swing while its hitbox stays open); a hit faster than HitRetriggerSeconds after the last read accumulates
            if (upperBody && (_windowOpen || Time.time - _lastOverrideHit < HitRetriggerSeconds)) { upperBody = false; SuppressedHits++; }
            switch (region)
            {
                case DD.Prototype.Damage.BodyRegion.Head: _animator.SetTrigger(FlinchHeadId); break;
                case DD.Prototype.Damage.BodyRegion.LowerBody: _animator.SetTrigger(FlinchLegId); break;
                default: _animator.SetTrigger(FlinchTorsoId); break;
            }

            if (upperBody)
            {
                // WP-032C: Hit_<dir>_<tier> — the hit direction is where the impulse pushes the body (M1 convention: +z = pushed
                // forward, i.e. shot from behind → the "back" read; ±x = shoved sideways); ties go to the lateral read.
                _animator.SetInteger(HitDirId, HitDirectionIndex(localHit));
                _animator.SetTrigger(HitId);
                _lastOverrideHit = Time.time;
                OverrideHits++;
            }
        }

        /// <summary>WP-032C: 0 front (pushed back = hit from the front), 1 back, 2 left, 3 right — from the body-space push direction.</summary>
        public static int HitDirectionIndex(Vector3 localPush)
        {
            if (Mathf.Abs(localPush.z) >= Mathf.Abs(localPush.x)) return localPush.z < 0f ? 0 : 1;
            return localPush.x < 0f ? 2 : 3;
        }

        /// <summary>WP-032C: a Special (Runner) stumble — the base-layer hit_special_stumble clip (stagger_end closes it).</summary>
        public void PlayStumble()
        {
            if (!_authored || _dying) return;
            _windowOpen = false;
            _animator.ResetTrigger(AttackId);
            _animator.SetTrigger(StumbleId);
        }

        /// <summary>WP-032C: armour break by region — the full-body armorbreak_<region> clip; held like the heavy stagger until its end (0.6 s clip).</summary>
        public void PlayArmorBreak(int region)
        {
            if (!_authored || _dying) return;
            _windowOpen = false;
            _heavyStaggerUntil = Time.time + Mathf.Min(HeavyStaggerCapSeconds, ArmorBreakHoldSeconds);
            _animator.ResetTrigger(AttackId);
            _animator.SetInteger(ArmorBreakRegionId, Mathf.Clamp(region, 0, 2));
            _animator.SetTrigger(ArmorBreakId);
        }

        [PrototypeOnly(PendingDecisions.None, "WP-032C: seconds the body is held (no walk / attack) by an armorbreak clip = the clip length (v005b: 21 frames, peak hold). Clip length.", true)]
        public const float ArmorBreakHoldSeconds = 0.7f;

        /// <summary>Full-body heavy stagger (armour break, ADR-018-2). Cancels the attack clip.</summary>
        public void PlayHeavyStagger()
        {
            if (!_authored || _dying) return;
            _windowOpen = false;
            _heavyStaggerUntil = Time.time + HeavyStaggerCapSeconds;   // WP-040: held until stagger_end (or the cap)
            _animator.ResetTrigger(AttackId);
            _animator.SetTrigger(StaggerId);
        }

        /// <summary>One of the four death clips; the body holds the last frame until death_end and the pool takes it.</summary>
        public void PlayDeath(int deathStyle)
        {
            if (!_authored) return;
            _special = ZombieSpecialVisual.None;   // WP-032E: a netted / dazed body still dies at full speed
            _animator.speed = 1f;
            _dying = true;
            _deathEnded = false;
            _windowOpen = false;
            _rising = false;
            _animator.ResetTrigger(AttackId);
            _animator.ResetTrigger(StaggerId);
            _animator.ResetTrigger(HitLeftId);
            _animator.ResetTrigger(HitRightId);
            _animator.SetBool(AliveId, false);
            _animator.SetInteger(DeathStyleId, Mathf.Clamp(deathStyle, 0, CharacterAnimatorContract.ZombieDeathClips.Length - 1));
            _animator.SetTrigger(DieId);
            BeginDeathClock(deathStyle);   // WP-071: onset clip → ragdoll hand-off
        }

        /// <summary>Armour binder hook (ArmorVisualBinder): a broken plate plays the region's armorbreak clip (WP-032C; the WP-018 heavy stagger stays the REQ-124 gameplay stagger's full-body read).</summary>
        public void NotifyArmorBroken(DD.Prototype.Damage.BodyRegion region)
        {
            PlayArmorBreak(region == DD.Prototype.Damage.BodyRegion.Head ? 0 : region == DD.Prototype.Damage.BodyRegion.LowerBody ? 2 : 1);
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
