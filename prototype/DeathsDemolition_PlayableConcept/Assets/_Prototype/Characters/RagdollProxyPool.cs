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
        /// <summary>
        /// Concurrent ragdolls: the quality tier's cap (Performans 2 / Yüksek 6 / Sinematik 10 — K4, QUALITY-TIERS-V2 §4.2), followed
        /// on <see cref="QualityTiers.Changed"/> (review R1-A #12); a set value overrides it (tests) until <see cref="ResetStatics"/>.
        /// Falling ragdolls finish under the old cap.
        /// </summary>
        public static int Capacity
        {
            get { if (s_tierCap < 0) s_tierCap = CapOf(QualityTiers.Current); return s_capOverride >= 0 ? s_capOverride : s_tierCap; }
            set => s_capOverride = value;
        }

        public static int CapOf(QualityTier tier) => QualityTiers.Pick(tier, 2, 6, 10);
        private static int s_capOverride = -1;
        private static int s_tierCap = -1;
        private static bool s_tierSubscribed;
        /// <summary>Global switch (tests / A-B): false = every death keeps its authored clip. `-ddRagdoll 0` starts a run with it off
        /// (the quality-tier contract's A/B flag, WP-078 A3; C-00 30.09).</summary>
        public static bool Enabled = true;

        /// <summary>`-ddRagdoll 0|1` (default on): the process's ragdoll A/B setting.</summary>
        public static bool ReadFlag(string[] args)
        {
            for (int i = 0; args != null && i < args.Length - 1; i++) if (args[i] == "-ddRagdoll") return args[i + 1] != "0";
            return true;
        }

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
        // WP-071 B3 · P-06 AP-022 ragdoll-limits.json (24.09, measured on the four death onsets' hand-off poses), degrees relative to the
        // pose at the hand-off. Hinge = 0: X = twist about the bone (±LowX/HighX), Y / Z = swings. Hinge = ±1: X = flex about the body's
        // lateral axis (+1 right: + bends the chest forward; −1 left: + swings the thigh forward), Y = twist about the bone, Z = side swing.
        private static readonly int[] Hinge = { 0, 1, 0, 0, 0, 0, 0, -1, 0, -1, 0 };
        private static readonly float[] LowX = { 0f, -20f, -40f, -60f, -70f, -60f, -70f, -25f, -70f, -25f, -70f };
        private static readonly float[] HighX = { 0f, 45f, 40f, 60f, 70f, 60f, 70f, 110f, 70f, 110f, 70f };
        private static readonly float[] LimY = { 0f, 20f, 40f, 100f, 10f, 100f, 10f, 25f, 10f, 25f, 10f };
        private static readonly float[] LimZ = { 0f, 25f, 40f, 90f, 10f, 90f, 10f, 40f, 10f, 40f, 10f };

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
        /// <summary>WP-078 (S-10 soak): proxies returned (a cycle = acquire → release; leak = acquired − released − active ≠ 0) and the active peak since reset.</summary>
        public static int ReleasedCount { get; private set; }
        public static int PeakActive { get; private set; }
        private static int s_roundPeak, s_roundBaseAcquired, s_roundBaseReleased, s_roundBaseRefused;
        private static readonly int[] s_roundBaseFamilies = new int[7];
        private static readonly System.Action<int> s_roundLog = OnRoundCompletedLog;

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
        public static Proxy Acquire(int owner, Transform[] bones, Transform[] tips, Vector3[] velocity, Vector3 push, float spinRadPerSec) =>
            Acquire(owner, bones, tips, velocity, push, spinRadPerSec, Vector3.zero);

        /// <summary>WP-071 B3: + <paramref name="uniform"/> — velocity every body gets in full (a launcher's lift, a running body's momentum).</summary>
        public static Proxy Acquire(int owner, Transform[] bones, Transform[] tips, Vector3[] velocity, Vector3 push, float spinRadPerSec, Vector3 uniform)
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

            Vector3 bodyRight = bones[0].parent != null ? bones[0].parent.right : bones[0].right;   // the character's lateral axis (root frame)
            for (int b = 0; b < BodyCount; b++)
            {
                ConfigurableJoint j = p.joints[b];
                if (j == null) continue;
                Transform bone = bones[b];
                Vector3 twist = tips[b] != null ? bone.InverseTransformDirection(tips[b].position - bone.position) : Vector3.up;
                if (twist.sqrMagnitude < 1e-6f) twist = Vector3.up;
                twist.Normalize();
                if (Hinge[b] != 0)
                {
                    // flex about the lateral axis (made perpendicular to the bone), twist about the bone
                    Vector3 lateral = bone.InverseTransformDirection(bodyRight * Hinge[b]);
                    lateral = Vector3.ProjectOnPlane(lateral, twist);
                    if (lateral.sqrMagnitude < 1e-6f) lateral = Vector3.Cross(twist, Vector3.forward);
                    j.axis = lateral.normalized;
                    j.secondaryAxis = twist;
                }
                else
                {
                    j.axis = twist;
                    j.secondaryAxis = Mathf.Abs(Vector3.Dot(twist, Vector3.forward)) < 0.9f ? Vector3.Cross(twist, Vector3.forward).normalized : Vector3.Cross(twist, Vector3.right).normalized;
                }

                j.connectedBody = p.bodies[Parent[b]];
            }

            p.root.SetActive(true);
            for (int b = 0; b < BodyCount; b++)
            {
                Rigidbody rb = p.bodies[b];
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                float share = b == 1 ? 1f : b == 0 ? 0.6f : b == 2 ? 0.9f : 0.4f;   // the push lands high: the chest carries it, the legs trail
                rb.velocity = (velocity != null ? velocity[b] : Vector3.zero) + push * share + uniform;
                rb.angularVelocity = b <= 1 ? new Vector3(0f, spinRadPerSec, 0f) : Vector3.zero;
                rb.WakeUp();
            }

            p.active = true;
            p.activeSeconds = 0f;
            p.stillSeconds = 0f;
            p.owner = owner;
            s_active++;
            AcquiredCount++;
            if (s_active > PeakActive) PeakActive = s_active;
            if (s_active > s_roundPeak) s_roundPeak = s_active;
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
            ReleasedCount++;
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
                j.lowAngularXLimit = new SoftJointLimit { limit = LowX[b] };
                j.highAngularXLimit = new SoftJointLimit { limit = HighX[b] };
                j.angularYLimit = new SoftJointLimit { limit = LimY[b] };
                j.angularZLimit = new SoftJointLimit { limit = LimZ[b] };
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
            if (!s_tierSubscribed) { s_tierSubscribed = true; QualityTiers.Changed += OnTierChanged; }
            if (s_instance != null) return;
            // review R1-A #3: the pooled zombies are DontDestroyOnLoad — the proxies share their lifetime, so a run that ends
            // mid-fall releases its proxies through the owners instead of losing them with the scene
            var go = new GameObject("RagdollProxyPool");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            s_instance = go.AddComponent<RagdollProxyPool>();
            SetLayers();
        }

        private static void OnTierChanged(QualityTier tier) => s_tierCap = CapOf(tier);

        /// <summary>
        /// Re-arm the round log (RunEvents drops its subscribers per run). Called at spawn, never on the death path: removing /
        /// adding on a shared multicast event allocates its invocation list.
        /// </summary>
        public static void ArmRoundLog()
        {
            // scene-local subscriber (S-01 LifecycleHardeningTests): leave RoundCompleted when the scene changes, re-arm at the next spawn
            if (!s_sceneHooked) { s_sceneHooked = true; UnityEngine.SceneManagement.SceneManager.activeSceneChanged += s_sceneChanged; }
            DD.Prototype.Rounds.RunEvents.RoundCompleted -= s_roundLog;
            DD.Prototype.Rounds.RunEvents.RoundCompleted += s_roundLog;
        }

        private static bool s_sceneHooked;
        private static readonly UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.Scene> s_sceneChanged = OnActiveSceneChanged;

        private static void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to) =>
            DD.Prototype.Rounds.RunEvents.RoundCompleted -= s_roundLog;

        /// <summary>WP-078 §3/§8 (S-10): one Player-log line per completed round — active now, the round's peak, overflow to the authored fall, cycles, and the death families.</summary>
        private static void OnRoundCompletedLog(int round)
        {
            int[] fam = ZombieDeathFamilies.Counts;
            Debug.Log("[Ragdoll] round=" + round + " tierCap=" + Capacity + " active=" + s_active + " peak=" + s_roundPeak + " overflow=" + (RefusedCount - s_roundBaseRefused)
                      + " cycles=" + (ReleasedCount - s_roundBaseReleased) + " acquired=" + (AcquiredCount - s_roundBaseAcquired)
                      + " families=back:" + (fam[0] - s_roundBaseFamilies[0]) + ",fwd:" + (fam[1] - s_roundBaseFamilies[1]) + ",spin:" + (fam[2] - s_roundBaseFamilies[2])
                      + ",legs:" + (fam[3] - s_roundBaseFamilies[3]) + ",ultra:" + (fam[4] - s_roundBaseFamilies[4]) + ",arc:" + (fam[5] - s_roundBaseFamilies[5]) + ",gel:" + (fam[6] - s_roundBaseFamilies[6])
                      + " totalAcquired=" + AcquiredCount + " totalReleased=" + ReleasedCount + " peakAll=" + PeakActive);
            s_roundPeak = s_active;
            s_roundBaseAcquired = AcquiredCount;
            s_roundBaseReleased = ReleasedCount;
            s_roundBaseRefused = RefusedCount;
            for (int i = 0; i < fam.Length && i < s_roundBaseFamilies.Length; i++) s_roundBaseFamilies[i] = fam[i];
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
            s_capOverride = -1;
            s_tierCap = -1;
            if (s_tierSubscribed) { QualityTiers.Changed -= OnTierChanged; s_tierSubscribed = false; }
            Enabled = ReadFlag(System.Environment.GetCommandLineArgs());   // -ddRagdoll 0|1 (A/B); tests set Enabled directly
            RefusedCount = 0;
            AcquiredCount = 0;
            ReleasedCount = 0;
            PeakActive = 0;
            s_roundPeak = s_roundBaseAcquired = s_roundBaseReleased = s_roundBaseRefused = 0;
            if (s_sceneHooked) { UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= s_sceneChanged; s_sceneHooked = false; }
            DD.Prototype.Rounds.RunEvents.RoundCompleted -= s_roundLog;
            System.Array.Clear(s_roundBaseFamilies, 0, s_roundBaseFamilies.Length);
            ZombieDeathFamilies.ResetCounts();
            s_layersSet = false;
        }
    }
}
