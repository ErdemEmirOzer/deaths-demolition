using UnityEngine;
using DD.Prototype.Core;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 · K5 · Zombie death ragdolls as a PROXY POOL (WP-071-ANIMATION-V3-DIRECTION Bölüm I). The zombie prefab carries
    /// no Rigidbody (DEC-032 stays: bodies move by CharacterController, and 40 × 11 kinematic bodies would cost a transform
    /// sync every frame). Instead <see cref="Capacity"/> ragdoll skeletons (11 bodies, 10 joints) are built once and kept
    /// inactive; a dying body borrows one at its hand-off: the proxy takes the bones' world pose and their last-frame
    /// velocity plus the killing push, falls under physics against the world, and every LateUpdate its bodies' rotations
    /// (and the pelvis position) are written back onto the zombie's bones. When the bodies settle (or at the time cap)
    /// the pose is left on the bones, the proxy returns to the pool and the corpse lies where it fell. Pool full → the
    /// caller keeps its authored fall clip (no tier ever collapses in place — K4).
    /// </summary>
    [DefaultExecutionOrder(-50)]   // before SecondaryChainDriver (0): the tails swing off the fallen pose
    public sealed class RagdollProxyPool : MonoBehaviour
    {
        /// <summary>Concurrent ragdolls (quality tier: Sinematik 10 / Yüksek 6 / Performans 2 — K4). Changing it takes effect for new deaths.</summary>
        public static int Capacity = 6;
        /// <summary>Global switch (tests / A-B): false = every death keeps its authored clip.</summary>
        public static bool Enabled = true;

        public const int BodyCount = 11;
        public const float SettleSpeed = 0.15f, SettleAngular = 0.6f, SettleHold = 0.35f, MinActive = 0.5f, MaxActive = 3.0f;
        private const int MaxProxies = 10;

        // body order = parent before child; parent index per body (-1 = root)
        public static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg
        };
        private static readonly int[] Parent = { -1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9 };
        // the bone each body's length is measured to
        private static readonly HumanBodyBones[] Tip =
        {
            HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };
        private static readonly float[] Mass = { 12f, 16f, 5f, 2.5f, 2f, 2.5f, 2f, 8f, 5f, 8f, 5f };
        private static readonly float[] Radius = { 0.13f, 0.14f, 0.11f, 0.06f, 0.05f, 0.06f, 0.05f, 0.08f, 0.06f, 0.08f, 0.06f };
        // twist ±, swing Y, swing Z (degrees; relative to the pose at the hand-off)
        private static readonly Vector3[] Limit =
        {
            Vector3.zero, new Vector3(30f, 25f, 25f), new Vector3(40f, 40f, 40f),
            new Vector3(70f, 80f, 80f), new Vector3(70f, 10f, 10f), new Vector3(70f, 80f, 80f), new Vector3(70f, 10f, 10f),
            new Vector3(60f, 40f, 30f), new Vector3(70f, 10f, 10f), new Vector3(60f, 40f, 30f), new Vector3(70f, 10f, 10f)
        };

        public sealed class Proxy
        {
            public GameObject root;
            public readonly Rigidbody[] bodies = new Rigidbody[BodyCount];
            public readonly CapsuleCollider[] colliders = new CapsuleCollider[BodyCount];
            public readonly ConfigurableJoint[] joints = new ConfigurableJoint[BodyCount];
            public readonly Transform[] target = new Transform[BodyCount];
            public bool active;
            public float activeSeconds, stillSeconds;
            public int owner;   // instance id of the zombie visual (release checks)
        }

        private static RagdollProxyPool s_instance;
        private static readonly Proxy[] s_proxies = new Proxy[MaxProxies];
        private static int s_built;
        private static int s_active;
        private static bool s_layersSet;

        /// <summary>Ragdolls falling right now (tests / probes).</summary>
        public static int ActiveCount => s_active;
        /// <summary>Acquisitions refused because the pool was at capacity (tests / probes; reset by <see cref="ResetStatics"/>).</summary>
        public static int RefusedCount { get; private set; }
        public static int AcquiredCount { get; private set; }

        /// <summary>Build the proxies up to <see cref="Capacity"/> now (first spawn), so the first death allocates nothing.</summary>
        public static void Warm()
        {
            EnsureInstance();
            int want = Mathf.Clamp(Capacity, 0, MaxProxies);
            while (s_built < want) s_proxies[s_built] = Build(s_built++);
        }

        /// <summary>
        /// Borrow a proxy for <paramref name="bones"/> (the <see cref="Bones"/> order, taken from the zombie's Animator) — the
        /// bodies start at the bones' pose with <paramref name="velocity"/> (per body, world m/s) plus the push. Null when
        /// disabled or the pool is full.
        /// </summary>
        public static Proxy Acquire(int owner, Transform[] bones, Transform[] tips, Vector3[] velocity, Vector3 push, float spinRadPerSec)
        {
            if (!Enabled || bones == null || bones[0] == null) return null;
            Warm();
            int cap = Mathf.Clamp(Capacity, 0, MaxProxies);
            Proxy p = null;
            for (int i = 0; i < cap && i < s_built; i++) if (!s_proxies[i].active) { p = s_proxies[i]; break; }
            if (p == null) { RefusedCount++; return null; }

            p.root.SetActive(false);   // joints re-initialise on activation, taking the hand-off pose as their reference
            for (int b = 0; b < BodyCount; b++)
            {
                Transform bone = bones[b];
                Rigidbody rb = p.bodies[b];
                p.target[b] = bone;
                rb.transform.SetPositionAndRotation(bone.position, bone.rotation);
                Fit(p.colliders[b], bone, tips[b], b);
            }

            for (int b = 0; b < BodyCount; b++)
            {
                ConfigurableJoint j = p.joints[b];
                if (j == null) continue;
                Transform bone = bones[b];
                Vector3 twist = tips[b] != null ? bone.InverseTransformDirection(tips[b].position - bone.position) : Vector3.up;
                if (twist.sqrMagnitude < 1e-6f) twist = Vector3.up;
                twist.Normalize();
                j.axis = twist;
                j.secondaryAxis = Mathf.Abs(Vector3.Dot(twist, Vector3.forward)) < 0.9f ? Vector3.Cross(twist, Vector3.forward).normalized : Vector3.Cross(twist, Vector3.right).normalized;
                j.connectedBody = p.bodies[Parent[b]];
            }

            p.root.SetActive(true);
            for (int b = 0; b < BodyCount; b++)
            {
                Rigidbody rb = p.bodies[b];
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                float share = b == 1 ? 1f : b == 0 ? 0.6f : b == 2 ? 0.9f : 0.4f;   // the push lands high: the chest carries it, the legs trail
                rb.velocity = (velocity != null ? velocity[b] : Vector3.zero) + push * share;
                rb.angularVelocity = b <= 1 ? new Vector3(0f, spinRadPerSec, 0f) : Vector3.zero;
                rb.WakeUp();
            }

            p.active = true;
            p.activeSeconds = 0f;
            p.stillSeconds = 0f;
            p.owner = owner;
            s_active++;
            AcquiredCount++;
            return p;
        }

        /// <summary>Return <paramref name="p"/> now (settled, respawned or despawned); the bones keep the last written pose.</summary>
        public static void Release(Proxy p)
        {
            if (p == null || !p.active) return;
            if (p.root == null)
            {
                // the scene took the pool with it (OnDestroy already cleared the books): only drop the owner's handle
                p.active = false;
                p.owner = 0;
                return;
            }

            for (int b = 0; b < BodyCount; b++)
            {
                p.bodies[b].interpolation = RigidbodyInterpolation.None;
                p.bodies[b].isKinematic = true;
                p.target[b] = null;
            }

            p.root.SetActive(false);
            p.active = false;
            p.owner = 0;
            s_active--;
        }

        /// <summary>True once the proxy has come to rest (or hit <see cref="MaxActive"/>); the owner then releases it.</summary>
        public static bool Settled(Proxy p) => p != null && p.active && ((p.activeSeconds >= MinActive && p.stillSeconds >= SettleHold) || p.activeSeconds >= MaxActive);

        private void LateUpdate()
        {
            if (s_active == 0) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < s_built; i++)
            {
                Proxy p = s_proxies[i];
                if (!p.active) continue;
                if (p.target[0] == null) { Release(p); continue; }   // the owner was destroyed mid-fall
                p.activeSeconds += dt;
                bool still = true;
                for (int b = 0; b < BodyCount; b++)
                {
                    Rigidbody rb = p.bodies[b];
                    if (rb.velocity.sqrMagnitude > SettleSpeed * SettleSpeed || rb.angularVelocity.sqrMagnitude > SettleAngular * SettleAngular) still = false;
                    Transform t = p.target[b];
                    if (t == null) continue;
                    Transform src = rb.transform;
                    if (b == 0) t.SetPositionAndRotation(src.position, src.rotation);
                    else t.rotation = src.rotation;
                }

                p.stillSeconds = still ? p.stillSeconds + dt : 0f;
            }
        }

        private static void Fit(CapsuleCollider c, Transform bone, Transform tip, int b)
        {
            Vector3 local = tip != null ? bone.InverseTransformPoint(tip.position) : Vector3.up * 0.2f;
            float len = local.magnitude;
            if (b == 2) { local = Vector3.up * 0.16f; len = 0.16f; }                  // head: a ball over the skull
            if (b == 4 || b == 6) { local *= 1.35f; len *= 1.35f; }                   // forearm + hand
            if (b == 8 || b == 10) { local *= 1.12f; len *= 1.12f; }                  // shin + foot
            int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
            c.direction = axis;
            c.radius = Radius[b];
            c.height = Mathf.Max(len + Radius[b] * 2f, Radius[b] * 2f);
            c.center = local * 0.5f;
        }

        private static Proxy Build(int index)
        {
            var p = new Proxy { root = new GameObject("RagdollProxy_" + index) };
            p.root.transform.SetParent(s_instance.transform, false);
            int layer = PrototypeLayers.Ragdoll >= 0 ? PrototypeLayers.Ragdoll : 0;
            for (int b = 0; b < BodyCount; b++)
            {
                var go = new GameObject(Bones[b].ToString());
                go.layer = layer;
                go.transform.SetParent(p.root.transform, false);
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = Mass[b];
                rb.drag = 0.05f;
                rb.angularDrag = 0.8f;
                rb.isKinematic = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                p.bodies[b] = rb;
                p.colliders[b] = go.AddComponent<CapsuleCollider>();
                if (Parent[b] < 0) continue;
                ConfigurableJoint j = go.AddComponent<ConfigurableJoint>();
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
                j.lowAngularXLimit = new SoftJointLimit { limit = -Limit[b].x };
                j.highAngularXLimit = new SoftJointLimit { limit = Limit[b].x };
                j.angularYLimit = new SoftJointLimit { limit = Limit[b].y };
                j.angularZLimit = new SoftJointLimit { limit = Limit[b].z };
                j.enableCollision = false;
                j.enablePreprocessing = false;
                j.autoConfigureConnectedAnchor = true;
                j.anchor = Vector3.zero;
                j.projectionMode = JointProjectionMode.PositionAndRotation;
                p.joints[b] = j;
            }

            p.root.SetActive(false);
            return p;
        }

        private static void EnsureInstance()
        {
            if (s_instance != null) return;
            var go = new GameObject("RagdollProxyPool");
            s_instance = go.AddComponent<RagdollProxyPool>();
            SetLayers();
        }

        /// <summary>Ragdoll ↔ everything that is not world: off (runtime matrix; the DynamicsManager asset is untouched).</summary>
        private static void SetLayers()
        {
            if (s_layersSet) return;
            int r = PrototypeLayers.Ragdoll;
            if (r < 0) return;
            s_layersSet = true;
            int[] off = { PrototypeLayers.Player, PrototypeLayers.Enemy, PrototypeLayers.EnemyHitbox, PrototypeLayers.AttackHitbox, PrototypeLayers.Projectile, PrototypeLayers.Pickup };
            for (int i = 0; i < off.Length; i++) if (off[i] >= 0) Physics.IgnoreLayerCollision(r, off[i], true);
        }

        private void OnDestroy()
        {
            if (s_instance != this) return;
            // the scene went away with the proxies: forget them (owners still holding one see it inactive / a destroyed root)
            for (int i = 0; i < s_built; i++)
            {
                if (s_proxies[i] != null) { s_proxies[i].active = false; s_proxies[i].owner = 0; }
                s_proxies[i] = null;
            }

            s_built = 0;
            s_active = 0;
            s_instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Capacity = 6;
            Enabled = true;
            RefusedCount = 0;
            AcquiredCount = 0;
            s_layersSet = false;
        }
    }
}
