using System;
using UnityEngine;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-032F · AP-006C secondary-motion chains (SECONDARY-CHAINS-CONTRACT.md): the `sec_&lt;chain&gt;_&lt;i&gt;`
    /// deform bones the AP-009 clips never key are driven here after the Animator wrote the pose —
    /// per chain a verlet particle string hanging off a core bone: p0 follows the parent (kinematic),
    /// p1..pn integrate gravity + a spring toward the rest direction (stiffness 0..1 → 0..400 m/s²,
    /// damping per frame at 30 fps), then three passes of segment length → angle clamp from the rest
    /// direction → push-out from the parent-bone capsule (body radius + collision radius). Each bone
    /// is then aimed at its next particle. Zero allocation after Bind (arrays live on the component),
    /// LOD0/1 only (the gate is the owner's), and at most <see cref="MaxBodiesPerFrame"/> bodies a frame —
    /// a body that is not served keeps its last pose. Configured by ArtCharacterPrefabBuilder from the
    /// AP006C / AP007C chain JSON (rest directions captured from the FBX rest pose at build time).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SecondaryChainDriver : MonoBehaviour
    {
        [Serializable]
        public sealed class Chain
        {
            public string name;
            public Transform parent;
            /// <summary>The capsule's far end along the parent bone (its core child, or null → parent + up × capsuleLength).</summary>
            public Transform parentEnd;
            public Transform[] bones;
            /// <summary>Segment length in metres (the contract's per-segment value).</summary>
            public float segmentLength;
            [Range(0f, 1f)] public float stiffness;
            [Range(0f, 1f)] public float damping;
            public float gravity = 9.81f;
            public float maxAngleDegrees = 60f;
            public float collisionRadius = 0.02f;
            public float bodyRadius = 0.15f;
            public float capsuleLength = 0.25f;
            /// <summary>Rest direction of each segment in the parent bone's space (captured at build from the rest pose).</summary>
            public Vector3[] restLocal;
            /// <summary>The bone-local axis that points from a bone to its next particle at rest (captured at build).</summary>
            public Vector3[] boneAxisLocal;

            [NonSerialized] public Vector3[] pos;
            [NonSerialized] public Vector3[] prev;
            [NonSerialized] public bool initialised;
        }

        [PrototypeOnly(PendingDecisions.None, "Bodies served per frame across the crowd (LOD0/1 gate is the owner's). Budget.", true)]
        public const int MaxBodiesPerFrame = 40;
        [PrototypeOnly(PendingDecisions.None, "Constraint passes per frame (contract: 3).", true)]
        public const int Iterations = 3;
        [PrototypeOnly(PendingDecisions.None, "Simulation step cap so a hitch does not fling the tails (s).", true)]
        public const float MaxStep = 1f / 30f;
        private const float StiffnessScale = 400f;

        [SerializeField] private Chain[] _chains = new Chain[0];
        private Func<bool> _near;
        private static int s_frame = -1;
        private static int s_served;
        /// <summary>Bodies served this frame (tests / probes).</summary>
        public static int ServedThisFrame => s_frame == Time.frameCount ? s_served : 0;
        /// <summary>Tests: −1 = normal, 0 = every body off, 1 = every body on (ignores the gate).</summary>
        public static int Force = -1;
        private bool _servedThisFrame;
        public bool ServedLastFrame => _servedThisFrame;
        private static long s_ticks;
        /// <summary>Microseconds spent by every served body this frame (Stopwatch ticks; tests / probes).</summary>
        public static double MicrosThisFrame => s_frame == Time.frameCount ? s_ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency : 0.0;
        private static int s_lastServed;
        private static long s_lastTicks;
        /// <summary>Bodies served in the last completed frame — a test coroutine resumes before LateUpdate, so the "this frame" readouts are 0 there.</summary>
        public static int ServedLastCompletedFrame => s_frame == Time.frameCount ? s_lastServed : s_served;
        public static double MicrosLastCompletedFrame => (s_frame == Time.frameCount ? s_lastTicks : s_ticks) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
        public Chain[] Chains => _chains;

        public static void ResetStatics()
        {
            s_frame = -1;
            s_served = 0;
            s_ticks = 0;
            s_lastServed = 0;
            s_lastTicks = 0;
            Force = -1;
        }

        /// <summary>Editor (prefab builder): the chain table.</summary>
        public void Configure(Chain[] chains)
        {
            _chains = chains ?? new Chain[0];
        }

        /// <summary>Owner: the near / LOD gate (LOD0/1 bodies only). Null = always near.</summary>
        public void Bind(Func<bool> near)
        {
            _near = near;
        }

        /// <summary>Drop the simulated state (pool reset / teleport): the next frame starts from the animated pose.</summary>
        public void Reset()
        {
            for (int c = 0; c < _chains.Length; c++) _chains[c].initialised = false;
        }

        private void LateUpdate()
        {
            if (_chains.Length == 0) return;
            if (s_frame != Time.frameCount) { s_lastServed = s_served; s_lastTicks = s_ticks; s_frame = Time.frameCount; s_served = 0; s_ticks = 0; }
            bool allowed = Force == 1 || (Force != 0 && (_near == null || _near()));
            _servedThisFrame = allowed && s_served < MaxBodiesPerFrame;
            if (!_servedThisFrame) return;
            s_served++;
            float dt = Mathf.Min(Time.deltaTime, MaxStep);
            if (dt <= 0f) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int c = 0; c < _chains.Length; c++) Step(_chains[c], dt);
            s_ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }

        /// <summary>WP-032H probe: one body step through the same gate and integrator as LateUpdate, outside the frame budget
        /// (the allocation test calls it thousands of times inside one frame so a per-step allocation cannot hide in frame noise).</summary>
        public void StepForProbe(float dt)
        {
            if (_chains.Length == 0 || dt <= 0f) return;
            bool allowed = Force == 1 || (Force != 0 && (_near == null || _near()));
            if (!allowed) return;
            for (int c = 0; c < _chains.Length; c++) Step(_chains[c], Mathf.Min(dt, MaxStep));
        }

        private static readonly Vector3[] s_rest = new Vector3[MaxSegments];   // this chain's rest directions in world space (one parent.rotation read a step)
        public const int MaxSegments = 8;

        private static void Step(Chain ch, float dt)
        {
            int n = ch.bones != null ? ch.bones.Length : 0;
            if (n == 0 || n > MaxSegments || ch.parent == null) return;
            if (ch.pos == null || ch.pos.Length != n + 1)
            {
                ch.pos = new Vector3[n + 1];
                ch.prev = new Vector3[n + 1];
                ch.initialised = false;
            }

            Transform parent = ch.parent;
            // Transform reads are the cost here (native calls): the parent's frame once, the root once, the capsule end once.
            Quaternion parentRot = parent.rotation;
            Vector3 capA = parent.position;
            for (int i = 0; i < n; i++) s_rest[i] = (parentRot * ch.restLocal[i]).normalized;
            Vector3 root = ch.bones[0].position;
            if (!ch.initialised)
            {
                // start from the rest pose in the parent's current frame
                ch.pos[0] = root;
                for (int i = 0; i < n; i++) ch.pos[i + 1] = ch.pos[i] + s_rest[i] * ch.segmentLength;

                Array.Copy(ch.pos, ch.prev, n + 1);
                ch.initialised = true;
            }

            ch.prev[0] = ch.pos[0];
            ch.pos[0] = root;
            // damping is "per frame at 30 fps" in the contract: keep the same decay per unit time
            float keep = Mathf.Pow(1f - ch.damping, dt * 30f);
            float dt2 = dt * dt;
            Vector3 gravity = new Vector3(0f, -ch.gravity, 0f);
            float spring = ch.stiffness * StiffnessScale;
            for (int i = 1; i <= n; i++)
            {
                Vector3 p = ch.pos[i];
                Vector3 v = (p - ch.prev[i]) * keep;
                Vector3 restPoint = ch.pos[i - 1] + s_rest[i - 1] * ch.segmentLength;
                Vector3 a = gravity + (restPoint - p) * spring;
                ch.prev[i] = p;
                ch.pos[i] = p + v + a * dt2;
            }

            Vector3 capB = ch.parentEnd != null ? ch.parentEnd.position : capA + (parentRot * Vector3.up) * ch.capsuleLength;
            float capR = ch.bodyRadius + ch.collisionRadius;
            float cosMax = Mathf.Cos(ch.maxAngleDegrees * Mathf.Deg2Rad);
            for (int it = 0; it < Iterations; it++)
            {
                for (int i = 1; i <= n; i++)
                {
                    Vector3 from = ch.pos[i - 1];
                    Vector3 d = ch.pos[i] - from;
                    float len = d.magnitude;
                    Vector3 rest = s_rest[i - 1];
                    Vector3 dir = len > 1e-6f ? d / len : rest;
                    // angle clamp from the rest direction (rotate toward the rest until inside the cone)
                    float cos = Vector3.Dot(dir, rest);
                    if (cos < cosMax)
                    {
                        Vector3 axis = Vector3.Cross(rest, dir);
                        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(rest, Vector3.right);
                        dir = Quaternion.AngleAxis(ch.maxAngleDegrees, axis.normalized) * rest;
                    }

                    ch.pos[i] = from + dir * ch.segmentLength;
                    // push-out from the parent capsule
                    Vector3 q = ClosestOnSegment(capA, capB, ch.pos[i]);
                    Vector3 away = ch.pos[i] - q;
                    float dist = away.magnitude;
                    if (dist < capR)
                    {
                        Vector3 push = dist > 1e-6f ? away / dist : parentRot * Vector3.forward;
                        ch.pos[i] = q + push * capR;
                    }
                }
            }

            // aim each bone at its next particle around the axis captured at build
            for (int i = 0; i < n; i++)
            {
                Transform b = ch.bones[i];
                Vector3 target = ch.pos[i + 1] - ch.pos[i];
                if (target.sqrMagnitude < 1e-8f) continue;
                Vector3 current = b.rotation * ch.boneAxisLocal[i];
                b.rotation = Quaternion.FromToRotation(current, target.normalized) * b.rotation;
            }
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        /// <summary>Tests: the particle positions of a chain (world).</summary>
        public Vector3[] Particles(int chain) => chain >= 0 && chain < _chains.Length ? _chains[chain].pos : null;

        /// <summary>Tests: segment angle (deg) from the rest direction, largest over the chain.</summary>
        public float MaxAngleFromRest(int chain)
        {
            Chain ch = _chains[chain];
            if (ch.pos == null) return 0f;
            float max = 0f;
            for (int i = 1; i < ch.pos.Length; i++)
            {
                Vector3 rest = ch.parent.TransformDirection(ch.restLocal[i - 1]).normalized;
                Vector3 dir = (ch.pos[i] - ch.pos[i - 1]).normalized;
                max = Mathf.Max(max, Vector3.Angle(rest, dir));
            }

            return max;
        }

        /// <summary>Tests: smallest clearance (m) between any particle and the parent capsule surface (negative = inside).</summary>
        public float MinClearance(int chain)
        {
            Chain ch = _chains[chain];
            if (ch.pos == null) return 0f;
            Vector3 capA = ch.parent.position;
            Vector3 capB = ch.parentEnd != null ? ch.parentEnd.position : ch.parent.position + ch.parent.up * ch.capsuleLength;
            float min = float.MaxValue;
            for (int i = 1; i < ch.pos.Length; i++) min = Mathf.Min(min, Vector3.Distance(ch.pos[i], ClosestOnSegment(capA, capB, ch.pos[i])) - ch.bodyRadius);
            return min;
        }
    }
}
