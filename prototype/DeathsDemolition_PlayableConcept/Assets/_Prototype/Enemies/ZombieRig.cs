using System;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Transform references for one procedurally built zombie.
    ///
    /// Serialized on the ZombieEnemy component so that Instantiate remaps every reference
    /// into the clone automatically: the body is assembled exactly once, on the pool
    /// template, and the 40 pooled copies inherit a fully wired rig for free.
    ///
    /// Nothing here is a skinned mesh. Every "bone" is a plain Transform that also carries
    /// the block it draws, which is what makes the pseudo animation a matter of writing
    /// localRotation and localPosition and nothing else.
    /// </summary>
    [Serializable]
    public sealed class ZombieRig
    {
        [Header("Structure")]
        public Transform Visual;      // whole-body scale and world lean
        public Transform Hips;        // bob, sway, turn
        public Transform Spine;       // locomotion lean
        public Transform Chest;       // attack + flinch layer
        public Transform Head;        // look, flinch, head-shot target

        [Header("Arms")]
        public Transform ShoulderL;
        public Transform ShoulderR;
        public Transform UpperArmL;
        public Transform UpperArmR;
        public Transform ForearmL;
        public Transform ForearmR;

        [Header("Legs")]
        public Transform ThighL;
        public Transform ThighR;
        public Transform ShinL;
        public Transform ShinR;

        [Header("Cloth layers")]
        public Renderer PelvisBlock;
        public Renderer TorsoBlock;
        public Renderer HeadBlock;
        public Renderer SkirtBlock;
        public Renderer SashBlock;
        public Renderer[] LimbBlocks = Array.Empty<Renderer>();

        [Header("Armour - helmet (kabuto)")]
        public GameObject HelmetRoot;
        public GameObject[] HelmetPieces = Array.Empty<GameObject>();

        [Header("Armour - body (do / kote)")]
        public GameObject BodyArmorRoot;
        public GameObject[] BodyArmorPieces = Array.Empty<GameObject>();

        [Header("Armour - legs (haidate / suneate)")]
        public GameObject LegArmorRoot;
        public GameObject[] LegArmorPieces = Array.Empty<GameObject>();

        [Header("Hitboxes")]
        public Collider HeadHitbox;
        public Collider UpperHitbox;
        public Collider LowerHitbox;

        [Header("Sockets")]
        public Transform AttackSocket;   // where the attack overlap is centred
        public Transform ChestSocket;    // impact FX origin for torso hits
        public Transform FootSocket;     // dust / footstep origin

        // ---------------------------------------------------------------- rest pose cache
        // Captured once so every pooled respawn can snap back to a clean pose without the
        // previous life's flinch, stagger or death rotation leaking into it.

        [NonSerialized] private Quaternion[] _restRotations;
        [NonSerialized] private Vector3[] _restPositions;
        [NonSerialized] private Transform[] _joints;

        /// <summary>Every animated joint, in a stable order. Built lazily, once per instance.</summary>
        public Transform[] Joints
        {
            get
            {
                if (_joints == null)
                {
                    _joints = new[]
                    {
                        Visual, Hips, Spine, Chest, Head,
                        ShoulderL, ShoulderR, UpperArmL, UpperArmR, ForearmL, ForearmR,
                        ThighL, ThighR, ShinL, ShinR
                    };
                }

                return _joints;
            }
        }

        public bool IsComplete =>
            Visual != null && Hips != null && Spine != null && Chest != null && Head != null &&
            UpperArmL != null && UpperArmR != null && ForearmL != null && ForearmR != null &&
            ThighL != null && ThighR != null && ShinL != null && ShinR != null &&
            HeadHitbox != null && UpperHitbox != null && LowerHitbox != null;

        /// <summary>Snapshot the authored pose. Call once, right after the body is built or cloned.</summary>
        public void CaptureRestPose()
        {
            Transform[] joints = Joints;
            _restRotations = new Quaternion[joints.Length];
            _restPositions = new Vector3[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                {
                    continue;
                }

                _restRotations[i] = joints[i].localRotation;
                _restPositions[i] = joints[i].localPosition;
            }
        }

        /// <summary>Restore the authored pose. Called from OnSpawned so pooling cannot leak a pose.</summary>
        public void ResetToRestPose()
        {
            if (_restRotations == null)
            {
                CaptureRestPose();
                return;
            }

            Transform[] joints = Joints;
            for (int i = 0; i < joints.Length && i < _restRotations.Length; i++)
            {
                if (joints[i] == null)
                {
                    continue;
                }

                joints[i].localRotation = _restRotations[i];
                joints[i].localPosition = _restPositions[i];
            }
        }

        public Quaternion RestRotation(int jointIndex)
        {
            return _restRotations != null && jointIndex >= 0 && jointIndex < _restRotations.Length
                ? _restRotations[jointIndex]
                : Quaternion.identity;
        }

        public Vector3 RestPosition(int jointIndex)
        {
            return _restPositions != null && jointIndex >= 0 && jointIndex < _restPositions.Length
                ? _restPositions[jointIndex]
                : Vector3.zero;
        }

        /// <summary>Indices into <see cref="Joints"/>. Kept as constants so the animator never searches.</summary>
        public const int JointVisual = 0;
        public const int JointHips = 1;
        public const int JointSpine = 2;
        public const int JointChest = 3;
        public const int JointHead = 4;
        public const int JointShoulderL = 5;
        public const int JointShoulderR = 6;
        public const int JointUpperArmL = 7;
        public const int JointUpperArmR = 8;
        public const int JointForearmL = 9;
        public const int JointForearmR = 10;
        public const int JointThighL = 11;
        public const int JointThighR = 12;
        public const int JointShinL = 13;
        public const int JointShinR = 14;
    }
}
