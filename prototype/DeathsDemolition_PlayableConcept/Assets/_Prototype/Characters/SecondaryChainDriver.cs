using System;
using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
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
    /// WP-082 R3 perf (C-00 11.10 · S-05 profile 456e1e21: TD High is main-thread bound, LateUpdate 1.29 ms over 40 bodies) ·
    /// crowd LOD on Yüksek + TopDown only: the owner marks a crowd body (<see cref="Bind(Func{bool},bool)"/>); once a frame the
    /// crowd is ranked by distance to the camera — the nearest <see cref="LodFullBodies"/> step every frame, the next
    /// <see cref="LodHalfBodies"/> every 2nd frame, the rest every 4th. The rank is also the phase, so every frame steps the same
    /// count (12 + 8 + 3 on a 40-body crowd); the skipped frames' time rides the next step (capped at <see cref="LodMaxStep"/>). The player's chains, Sinematik,
    /// Performans and the other camera modes run every body every frame as before.
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
        [PrototypeOnly(PendingDecisions.None, "Crowd LOD (Yüksek + TD): the bodies nearest the camera that step every frame.", true)]
        public const int LodFullBodies = 12;
        [PrototypeOnly(PendingDecisions.None, "Crowd LOD (Yüksek + TD): the next bodies, every 2nd frame; the rest every 4th.", true)]
        public const int LodHalfBodies = 16;
        /// <summary>Step cap for a body on the 2nd / 4th-frame cadence (s): two 30 fps frames. Verlet stays stable at the contract's
        /// stiffness (k·dt² = 400 × 1/225 ≤ 1.8 &lt; 4); the cap only bites below 30 fps (half) / 60 fps (quarter).</summary>
        public const float LodMaxStep = 2f * MaxStep;

        [SerializeField] private Chain[] _chains = new Chain[0];
        private Func<bool> _near;
        private bool _crowd;
        private int _crowdIndex = -1;
        private int _lodRank;
        private float _carry;
        private static readonly List<SecondaryChainDriver> s_crowd = new List<SecondaryChainDriver>(128);
        private static float[] s_key = new float[128];
        private static int[] s_order = new int[128];
        private static int s_lodFrame = -1;
        private static bool s_lodOn;
        private static int s_levelCount = -1;   // QualitySettings.names allocates: read once
        /// <summary>Tests: −1 = Yüksek + TD only (normal), 0 = crowd LOD off, 1 = crowd LOD in every tier and camera mode.</summary>
        public static int CrowdLodForce = -1;
        /// <summary>Tests: the point the crowd is ranked from (null = Camera.main).</summary>
        public static Transform LodEyeOverride;
        /// <summary>Crowd LOD this frame (tests / probes).</summary>
        public static bool CrowdLodActive => s_lodFrame == Time.frameCount && s_lodOn;
        /// <summary>Crowd bodies registered for the LOD ranking (tests / probes).</summary>
        public static int CrowdCount => s_crowd.Count;
        /// <summary>This body's distance rank in the last LOD pass (0 = nearest the eye; tests / probes).</summary>
        public int LodRank => _lodRank;
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
            CrowdLodForce = -1;
            LodEyeOverride = null;
            s_lodFrame = -1;
            s_lodOn = false;
            // the registry is rebuilt by the live bodies' OnEnable / Bind; a destroyed body's slot must not survive a test
            for (int i = s_crowd.Count - 1; i >= 0; i--) if (s_crowd[i] == null) s_crowd.RemoveAt(i); else s_crowd[i]._crowdIndex = i;
        }

        /// <summary>Editor (prefab builder): the chain table.</summary>
        public void Configure(Chain[] chains)
        {
            _chains = chains ?? new Chain[0];
        }

        /// <summary>Owner: the near / LOD gate (LOD0/1 bodies only). Null = always near.</summary>
        public void Bind(Func<bool> near)
        {
            Bind(near, false);
        }

        /// <summary>Owner: the near / LOD gate; <paramref name="crowdLod"/> = a crowd body the Yüksek + TD crowd LOD may slow (zombies, never the player).</summary>
        public void Bind(Func<bool> near, bool crowdLod)
        {
            _near = near;
            _crowd = crowdLod;
            if (_crowd && isActiveAndEnabled) Register(); else if (!_crowd) Unregister();
        }

        /// <summary>The crowd LOD's tier × camera-mode rule: Yüksek + TopDown only (C-00 11.10).</summary>
        public static bool CrowdLodApplies(QualityTier tier, CameraMode mode) => tier == QualityTier.Yuksek && mode == CameraMode.TopDown;

        /// <summary>Drop the simulated state (pool reset / teleport): the next frame starts from the animated pose.</summary>
        public void Reset()
        {
            for (int c = 0; c < _chains.Length; c++) _chains[c].initialised = false;
            _carry = 0f;
        }

        private void OnEnable()
        {
            if (_crowd) Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        private void Register()
        {
            if (_crowdIndex >= 0) return;
            _crowdIndex = s_crowd.Count;
            s_crowd.Add(this);
            _lodRank = 0;   // full rate until the next pass ranks it
            _carry = 0f;
        }

        private void Unregister()
        {
            if (_crowdIndex < 0) return;
            int last = s_crowd.Count - 1;
            if (_crowdIndex <= last && s_crowd[_crowdIndex] == this)
            {
                SecondaryChainDriver moved = s_crowd[last];
                s_crowd[_crowdIndex] = moved;
                if (moved != null) moved._crowdIndex = _crowdIndex;
                s_crowd.RemoveAt(last);
            }

            _crowdIndex = -1;
        }

        /// <summary>Once a frame: is the crowd LOD on, and every crowd body's distance rank (stable insertion sort, no allocation).</summary>
        private static void CrowdLodPass()
        {
            s_lodFrame = Time.frameCount;
            s_lodOn = false;
            int n = s_crowd.Count;
            if (CrowdLodForce == 0 || n <= LodFullBodies) return;
            if (CrowdLodForce != 1)
            {
                if (s_levelCount < 0) s_levelCount = QualitySettings.names.Length;
                if (!CrowdLodApplies(QualityTiers.TierOfLevel(QualitySettings.GetQualityLevel(), s_levelCount), CameraModeService.Current)) return;
            }

            Transform eye = LodEyeOverride;
            if (eye == null)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                eye = cam.transform;
            }

            Vector3 e = eye.position;
            if (s_key.Length < n)
            {
                s_key = new float[Mathf.NextPowerOfTwo(n)];   // grows once past 128 bodies
                s_order = new int[s_key.Length];
            }

            for (int i = 0; i < n; i++)
            {
                SecondaryChainDriver d = s_crowd[i];
                float sq = d != null ? (d.transform.position - e).sqrMagnitude : float.MaxValue;
                int j = i - 1;
                while (j >= 0 && s_key[j] > sq) { s_key[j + 1] = s_key[j]; s_order[j + 1] = s_order[j]; j--; }
                s_key[j + 1] = sq;
                s_order[j + 1] = i;
            }

            for (int r = 0; r < n; r++)
            {
                SecondaryChainDriver d = s_crowd[s_order[r]];
                if (d != null) d._lodRank = r;
            }

            s_lodOn = true;
        }

        private void LateUpdate()
        {
            if (_chains.Length == 0) return;
            if (s_frame != Time.frameCount) { s_lastServed = s_served; s_lastTicks = s_ticks; s_frame = Time.frameCount; s_served = 0; s_ticks = 0; }
            if (s_lodFrame != Time.frameCount) CrowdLodPass();
            bool allowed = Force == 1 || (Force != 0 && (_near == null || _near()));
            float cap = MaxStep;
            float step = Time.deltaTime;
            if (allowed && _crowd && Force == -1 && s_lodOn && _crowdIndex >= 0)
            {
                // the crowd LOD: rank → cadence 1 / 2 / 4, the rank as the phase (a level count a frame); the skipped frames' time rides the next step
                int stride = _lodRank < LodFullBodies ? 1 : _lodRank < LodFullBodies + LodHalfBodies ? 2 : 4;
                _carry += step;
                if (((Time.frameCount + _lodRank) & (stride - 1)) != 0) { _servedThisFrame = false; return; }
                step = _carry;
                if (stride > 1) cap = LodMaxStep;
            }

            _servedThisFrame = allowed && s_served < MaxBodiesPerFrame;
            if (!_servedThisFrame) return;
            _carry = 0f;
            s_served++;
            float dt = Mathf.Min(step, cap);
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
