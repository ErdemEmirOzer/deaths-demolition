using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
    /// Attached under the M1 rig's <c>Visual</c> node by <see cref="Attach"/>: the block rig
    /// keeps driving attack / stagger / flinch / death / tier lean through
    /// <see cref="ProceduralPoseBridge"/>, the hitboxes and the armour anchors move onto the
    /// bones so a bullet lands where the body is drawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZombieSkinnedVisual : MonoBehaviour
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
        [SerializeField] private ProceduralPoseBridge _bridge;
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
        public ProceduralPoseBridge Bridge => _bridge;
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
            Transform anchorHelmet, Transform anchorTorso, Transform anchorLegs, CharacterAnimationSet animation, ProceduralPoseBridge bridge)
        {
            _animator = animator;
            _lodGroup = lodGroup;
            _slots = slots;
            _skeletonRoot = skeletonRoot;
            _anchorHelmet = anchorHelmet;
            _anchorTorso = anchorTorso;
            _anchorLegs = anchorLegs;
            _animation = animation;
            _bridge = bridge;
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
        /// Parent under the rig's Visual node and wire the bridge: block Hips/Spine/Chest/Head
        /// and both arms (upper + forearm) drive the bones; hips also carry the bob/sink
        /// position delta. Legs stay with the clip. Rest is captured now — the block rig must
        /// be at its rest pose (spawn resets it) and the Animator must not have run yet or be
        /// in its bind pose.
        /// </summary>
        public void Attach(ZombieRig rig, ZombieAnimator animator)
        {
            if (rig == null || rig.Visual == null) return;
            transform.SetParent(rig.Visual, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            if (animator != null) animator.SkinnedLocomotion = true;

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
                _bridge.Configure(rig.Visual);
                Transform hips = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Hips);
                Transform spine = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Spine);
                Transform chest = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Chest);
                Transform head = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Head);
                Transform uaL = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.LeftUpperArm);
                Transform uaR = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.RightUpperArm);
                Transform laL = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.LeftLowerArm);
                Transform laR = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.RightLowerArm);
                Transform handL = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.LeftHand);
                Transform handR = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.RightHand);
                _bridge.Add("hips", rig.Hips, hips, positionDelta: true);
                _bridge.Add("spine", rig.Spine, spine);
                _bridge.Add("chest", rig.Chest, chest);
                _bridge.Add("head", rig.Head, head);
                _bridge.Add("upperArmL", rig.UpperArmL, uaL, rig.ForearmL, laL, conjugateLimb: true);
                _bridge.Add("upperArmR", rig.UpperArmR, uaR, rig.ForearmR, laR, conjugateLimb: true);
                _bridge.Add("forearmL", rig.ForearmL, laL, null, handL, conjugateLimb: true);
                _bridge.Add("forearmR", rig.ForearmR, laR, null, handR, conjugateLimb: true);
                _bridge.CaptureRest();
                _bridge.Active = true;
            }

            Attached = true;
        }

        /// <summary>Drive the locomotion blend from the enemy's ground speed (called by ZombieEnemy each tick).</summary>
        public void SetSpeed(float metersPerSecond, float dt)
        {
            if (_animator == null || _animation == null) return;
            // Own smoothing (the damped SetFloat overload measured as a no-op on these animators).
            float target = _animation.SpeedParameterFor(metersPerSecond);
            _speed = dt > 0f ? Mathf.MoveTowards(_speed, target, dt / SpeedSmoothing) : target;
            _animator.SetFloat(SpeedId, _speed);
        }

        private const float SpeedSmoothing = 0.12f;   // seconds per unit of Speed
        private float _speed;

        /// <summary>
        /// Move the rig's three hitboxes and the armour placeholder roots onto the bones /
        /// anchors, so they follow the drawn body. Positions are the AP-007 anchor origins
        /// (head 1.60 m, chest 1.17 m, hips 0.93 m) plus the M1 hitbox offsets in metres.
        /// </summary>
        public void RehomeGameplayNodes(ZombieRig rig)
        {
            EnsureBones();
            Transform head = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Head);
            Transform chest = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Chest);
            Transform hips = ProceduralPoseBridge.Bone(_animator, HumanBodyBones.Hips);
            if (head == null || chest == null || hips == null) return;

            Rehome(rig.HeadHitbox != null ? rig.HeadHitbox.transform : null, head, new Vector3(0f, 0.10f, 0f));
            Rehome(rig.UpperHitbox != null ? rig.UpperHitbox.transform : null, chest, new Vector3(0f, 0.02f, 0f));
            Rehome(rig.LowerHitbox != null ? rig.LowerHitbox.transform : null, hips, new Vector3(0f, -0.40f, 0f));
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
            Transform b = ProceduralPoseBridge.Bone(_animator, bone);
            Transform c = ProceduralPoseBridge.Bone(_animator, child);
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
