using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016 · Carries the M1 procedural animation (ZombieAnimator / PlayerProceduralAnimator:
    /// tier lean, arm carriage, attack lunges, stagger, flinch, death, recoil) onto the
    /// skinned Humanoid bones — the "existing placeholder behaviours applied to the skinned
    /// mesh" rule of WP-016 §2 — while the Animator keeps what it is good at (idle/walk
    /// cycle on the legs and hips from the rig-test clips).
    ///
    /// HOW: the M1 block rig keeps running, hidden, as the DRIVER skeleton. After the Animator
    /// has written the clip pose (this runs at execution order 1000, after every LateUpdate
    /// pseudo-animator), each mapped bone is set to
    ///     bone.rotation = S · Δ · boneRest      with   Δ = S⁻¹ · driver.rotation · driverRest⁻¹
    /// i.e. the driver joint's rotation relative to its own rest, expressed in the shared
    /// space S (the block <c>Visual</c> node — so the tier scale and the death fall on Visual
    /// carry over for free), applied onto the bone's rest orientation. Limb entries conjugate
    /// Δ by the rotation that takes the block limb's rest direction (hanging −Y) to the bone's
    /// rest direction (A-pose), so "bend the elbow" bends the elbow rather than a world axis.
    /// The write is ABSOLUTE (rest-relative), never accumulative, so a culled or absent Animator
    /// cannot make it drift; position deltas (hip bob / death sink) are additive on the
    /// Animator's own write and therefore need <c>AnimatorCullingMode.AlwaysAnimate</c>.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class ProceduralPoseBridge : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string name;
            public Transform driver;
            public Transform bone;
            /// <summary>Child joint on the driver side that gives the limb direction (null → the driver's −Y).</summary>
            public Transform driverChild;
            /// <summary>Child bone that gives the bone's limb direction (null → no conjugation).</summary>
            public Transform boneChild;
            /// <summary>Rotate Δ into the bone's limb frame (arms / legs).</summary>
            public bool conjugateLimb;
            /// <summary>Use identity as the driver rest so its authored rest offset (the player's raised weapon arm) is applied too.</summary>
            public bool identityRest;
            /// <summary>Also add the driver's position delta (hip bob, death sink).</summary>
            public bool positionDelta;

            [NonSerialized] public Quaternion DriverRest = Quaternion.identity;
            [NonSerialized] public Quaternion BoneRest = Quaternion.identity;
            [NonSerialized] public Quaternion Conj = Quaternion.identity;
            [NonSerialized] public Quaternion ConjInverse = Quaternion.identity;
            [NonSerialized] public Vector3 DriverRestPosition;
            [NonSerialized] public bool Captured;
        }

        [SerializeField] private Transform _space;
        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [SerializeField] private bool _active = true;

        public Transform Space => _space;
        public IReadOnlyList<Entry> Entries => _entries;
        public bool Active { get => _active; set => _active = value; }

        public void Configure(Transform space)
        {
            _space = space;
            _entries.Clear();
        }

        public Entry Add(string name, Transform driver, Transform bone, Transform driverChild = null, Transform boneChild = null,
            bool conjugateLimb = false, bool identityRest = false, bool positionDelta = false)
        {
            if (driver == null || bone == null) return null;
            var e = new Entry
            {
                name = name, driver = driver, bone = bone, driverChild = driverChild, boneChild = boneChild,
                conjugateLimb = conjugateLimb, identityRest = identityRest, positionDelta = positionDelta
            };
            _entries.Add(e);
            return e;
        }

        /// <summary>
        /// Snapshot both rigs' rest poses. Call once with the driver at its authored rest and
        /// the bones at their bind pose (before the first Animator update), and again after a
        /// pooled respawn resets the driver.
        /// </summary>
        public void CaptureRest()
        {
            if (_space == null) return;
            Quaternion sInv = Quaternion.Inverse(_space.rotation);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.driver == null || e.bone == null) continue;
                e.DriverRest = e.identityRest ? Quaternion.identity : sInv * e.driver.rotation;
                e.BoneRest = sInv * e.bone.rotation;
                e.DriverRestPosition = _space.InverseTransformPoint(e.driver.position);
                if (e.conjugateLimb)
                {
                    Vector3 driverDir = e.driverChild != null
                        ? _space.InverseTransformDirection(e.driverChild.position - e.driver.position)
                        : _space.InverseTransformDirection(e.driver.TransformDirection(Vector3.down));
                    Vector3 boneDir = e.boneChild != null
                        ? _space.InverseTransformDirection(e.boneChild.position - e.bone.position)
                        : driverDir;
                    e.Conj = driverDir.sqrMagnitude > 1e-8f && boneDir.sqrMagnitude > 1e-8f
                        ? Quaternion.FromToRotation(driverDir.normalized, boneDir.normalized)
                        : Quaternion.identity;
                }
                else
                {
                    e.Conj = Quaternion.identity;
                }

                e.ConjInverse = Quaternion.Inverse(e.Conj);
                e.Captured = true;
            }
        }

        private void LateUpdate()
        {
            if (!_active || _space == null) return;
            Quaternion s = _space.rotation;
            Quaternion sInv = Quaternion.Inverse(s);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (!e.Captured || e.driver == null || e.bone == null) continue;
                Quaternion delta = sInv * e.driver.rotation * Quaternion.Inverse(e.DriverRest);
                if (e.conjugateLimb)
                {
                    delta = e.Conj * delta * e.ConjInverse;
                }

                e.bone.rotation = s * delta * e.BoneRest;
                if (e.positionDelta)
                {
                    Vector3 dp = _space.InverseTransformPoint(e.driver.position) - e.DriverRestPosition;
                    if (dp.sqrMagnitude > 1e-10f)
                    {
                        e.bone.position += _space.TransformVector(dp);
                    }
                }
            }
        }

        /// <summary>Humanoid bone lookup that tolerates a missing Animator/avatar (returns null).</summary>
        public static Transform Bone(Animator animator, HumanBodyBones bone)
        {
            return animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;
        }
    }
}
