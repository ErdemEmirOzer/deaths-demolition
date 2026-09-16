using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// The custom crowd layer: soft mass-weighted separation between members, a
    /// deterministic tangential slide that stops head-on deadlocks, and a player contact
    /// constraint that brakes members without ever pushing them away.
    ///
    /// ALGORITHM (one tick)
    /// --------------------
    ///  1. GATHER    read Position / Radius / Mass / SteeringTarget from every member,
    ///               measure each member's real velocity from its own position delta and
    ///               keep a decaying peak of its free-running speed;
    ///  2. HASH      insert every member into a uniform XZ grid (cell ~ 2x max radius);
    ///  3. PAIRS     for each member scan the 3x3 neighbouring cells, handle each pair
    ///               once (j &gt; i):
    ///                   overlap  = (rA + rB) * SpacingScale - distance
    ///                   share    = mOther / (mSelf + mOther)          <- mass weighting
    ///                   speed    = min(overlap * Stiffness, MaxSep) + closingSpeed*Damping
    ///                   normal   : both members pushed apart by speed * share
    ///                   tangent  : the member with the lower index slides +90 deg, the
    ///                              other -90 deg, scaled by how head-on the pair is.
    ///                              THIS is the anti-lock term - a symmetric push alone
    ///                              is exactly what jams a narrow bridge;
    ///  4. PLAYER    inside (rMember + rPlayer + padding):
    ///                   brace = max(measured inward speed, desired inward speed) and the
    ///                           correction cancels it -> the member stops dead against
    ///                           the player and keeps standing in its way (REQ-046);
    ///                   plus a weak, capped depenetration used only for real overlap.
    ///               There is NO outward impulse: an impulse would let the player plough
    ///               through the swarm and DEC-032 would be dead;
    ///  5. DISPATCH  clamp to MaxCorrectionSpeed and call ApplySteering once per member.
    ///
    /// COST at 40 members (measured through <see cref="LastSolveMilliseconds"/>, shown
    /// live in the F1 panel under Perf): the pair stage is O(members * neighbours), i.e.
    /// roughly 150-350 pair tests instead of the 780 of a naive all-pairs loop, all in
    /// pre-allocated arrays with no managed allocation, no GetComponent and no physics
    /// query. Expected well under 0.15 ms of the 16.6 ms frame at 60 FPS.
    ///
    /// ALLOCATION: zero per tick once the member arrays have grown to their high-water
    /// mark (they start at 64, the safety ceiling is 40).
    /// </summary>
    public sealed class CrowdSolver
    {
        private struct MemberRecord
        {
            public ICrowdMember Member;
            public Object UnityRef;
            public bool IsUnityObject;

            public Vector3 Position;
            public Vector3 PrevPosition;
            public Vector3 Velocity;
            public Vector3 Desired;
            public Vector3 Correction;

            public float Radius;
            public float Mass;
            public float SpeedEstimate;
            public bool HasPrev;
        }

        /// <summary>
        /// PROTOTYPE-ONLY, invented: how fast the remembered free-running speed of a member
        /// decays while it is blocked. It only feeds the player brace, so it never changes
        /// how fast anything actually moves.
        /// </summary>
        private const float SpeedEstimateDecayPerSecond = 1.5f;

        private MemberRecord[] _records = new MemberRecord[64];
        private int _count;

        private readonly CrowdSpatialGrid _grid = new CrowdSpatialGrid();

        /// <summary>Live tuning. Owned by <see cref="NavigationSystemBehaviour"/>.</summary>
        public CrowdTuning Tuning = new CrowdTuning();

        // ------------------------------------------------------------------ diagnostics

        public int MemberCount => _count;
        public int LastPairTests { get; private set; }
        public float LastSolveMilliseconds { get; private set; }
        public float AverageSolveMilliseconds { get; private set; }
        public float PeakSolveMilliseconds { get; private set; }
        public int LastContactCount { get; private set; }

        public void ResetPeaks()
        {
            PeakSolveMilliseconds = 0f;
            AverageSolveMilliseconds = 0f;
        }

        // ------------------------------------------------------------------ membership

        public void Register(ICrowdMember member)
        {
            if (member == null)
            {
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_records[i].Member, member))
                {
                    return;
                }
            }

            if (_count == _records.Length)
            {
                System.Array.Resize(ref _records, _records.Length * 2);
            }

            var unityRef = member as Object;

            _records[_count] = new MemberRecord
            {
                Member = member,
                UnityRef = unityRef,
                IsUnityObject = unityRef != null,
                Position = member.Position,
                PrevPosition = member.Position,
                Velocity = Vector3.zero,
                Desired = Vector3.zero,
                Correction = Vector3.zero,
                Radius = Mathf.Max(0.05f, member.Radius),
                Mass = Mathf.Max(0.01f, member.Mass),
                SpeedEstimate = 0f,
                HasPrev = false
            };

            _count++;
        }

        public void Unregister(ICrowdMember member)
        {
            if (member == null)
            {
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_records[i].Member, member))
                {
                    RemoveAt(i);
                    return;
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
            {
                _records[i] = default;
            }

            _count = 0;
        }

        /// <summary>
        /// Living members inside a radius. Used by the spawn layer for local density, so
        /// the spawn director never needs to know the enemy type. Allocation free.
        /// </summary>
        /// <summary>WP-030: members whose position lies in the XZ box (chokepoint occupancy). Allocation-free.</summary>
        public int CountMembersInBox(Vector3 center, Vector3 size)
        {
            float hx = size.x * 0.5f, hz = size.z * 0.5f;
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                float dx = _records[i].Position.x - center.x;
                float dz = _records[i].Position.z - center.z;
                if (dx >= -hx && dx <= hx && dz >= -hz && dz <= hz) count++;
            }

            return count;
        }

        public int CountMembersWithin(Vector3 position, float radius)
        {
            float sqr = radius * radius;
            int count = 0;

            for (int i = 0; i < _count; i++)
            {
                float dx = _records[i].Position.x - position.x;
                float dz = _records[i].Position.z - position.z;
                if (dx * dx + dz * dz <= sqr)
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ tick

        /// <summary>
        /// One solver step. Does nothing when dt is 0, which is what makes pause
        /// (Time.timeScale = 0) freeze the crowd along with everything else.
        /// </summary>
        public void Tick(float dt, in CrowdPlayerState player)
        {
            if (dt <= 0f || _count == 0)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();

            Gather(dt);
            Solve(player);
            Dispatch();

            float ms = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            LastSolveMilliseconds = ms;
            AverageSolveMilliseconds = AverageSolveMilliseconds <= 0f ? ms : AverageSolveMilliseconds * 0.9f + ms * 0.1f;
            if (ms > PeakSolveMilliseconds)
            {
                PeakSolveMilliseconds = ms;
            }
        }

        private void Gather(float dt)
        {
            float smoothing = Tuning.VelocitySmoothing;
            float decay = Mathf.Clamp01(dt * SpeedEstimateDecayPerSecond);

            for (int i = _count - 1; i >= 0; i--)
            {
                ref MemberRecord r = ref _records[i];

                if (r.Member == null || (r.IsUnityObject && r.UnityRef == null))
                {
                    NavigationDiagnostics.WarnThrottled(
                        "crowd.orphan", 10f,
                        "[Crowd] A crowd member was destroyed without calling CrowdService.Unregister. " +
                        "Dropping it. Enemies must unregister in OnDisable.");
                    RemoveAt(i);
                    continue;
                }

                Vector3 position = r.Member.Position;
                position.y = 0f;

                if (r.HasPrev)
                {
                    Vector3 raw = (position - r.PrevPosition) / dt;
                    raw.y = 0f;
                    r.Velocity = Vector3.Lerp(raw, r.Velocity, smoothing);
                }
                else
                {
                    r.Velocity = Vector3.zero;
                    r.HasPrev = true;
                }

                r.PrevPosition = position;
                r.Position = position;
                r.Radius = Mathf.Max(0.05f, r.Member.Radius);
                r.Mass = Mathf.Max(0.01f, r.Member.Mass);
                r.Correction = Vector3.zero;

                float speed = Mathf.Sqrt(r.Velocity.x * r.Velocity.x + r.Velocity.z * r.Velocity.z);
                r.SpeedEstimate = Mathf.Max(speed, r.SpeedEstimate * (1f - decay));

                Vector3 toTarget = r.Member.SteeringTarget - position;
                toTarget.y = 0f;
                float sqr = toTarget.x * toTarget.x + toTarget.z * toTarget.z;
                r.Desired = sqr > 0.0001f ? toTarget / Mathf.Sqrt(sqr) : Vector3.zero;
            }
        }

        private void Solve(in CrowdPlayerState player)
        {
            int pairTests = 0;
            int contacts = 0;

            _grid.Configure(_count, Tuning.CellSize);
            _grid.Clear(_count);
            for (int i = 0; i < _count; i++)
            {
                _grid.Insert(i, _records[i].Position);
            }

            float spacing = Tuning.SpacingScale;
            float stiffness = Tuning.SeparationStiffness;
            float maxSep = Tuning.MaxSeparationSpeed;
            float damping = Tuning.ApproachDamping;
            float tangentialBias = Tuning.TangentialBias;
            int maxNeighbors = Tuning.MaxNeighborsPerMember;

            for (int i = 0; i < _count; i++)
            {
                int cx = _grid.CellCoordinate(_records[i].Position.x);
                int cz = _grid.CellCoordinate(_records[i].Position.z);
                int neighbors = 0;

                for (int oz = -1; oz <= 1 && neighbors < maxNeighbors; oz++)
                {
                    for (int ox = -1; ox <= 1 && neighbors < maxNeighbors; ox++)
                    {
                        int j = _grid.First(cx + ox, cz + oz, out int key);

                        while (j >= 0 && neighbors < maxNeighbors)
                        {
                            if (j > i)
                            {
                                pairTests++;
                                if (ResolvePair(i, j, spacing, stiffness, maxSep, damping, tangentialBias))
                                {
                                    neighbors++;
                                    contacts++;
                                }
                            }

                            j = _grid.Next(j, key);
                        }
                    }
                }

                if (player.HasPlayer)
                {
                    ResolvePlayerContact(i, player);
                }
            }

            LastPairTests = pairTests;
            LastContactCount = contacts;
        }

        /// <summary>Returns true when the pair actually overlapped.</summary>
        private bool ResolvePair(int i, int j, float spacing, float stiffness, float maxSep,
            float damping, float tangentialBias)
        {
            ref MemberRecord a = ref _records[i];
            ref MemberRecord b = ref _records[j];

            float dx = b.Position.x - a.Position.x;
            float dz = b.Position.z - a.Position.z;
            float minDist = (a.Radius + b.Radius) * spacing;
            float sqr = dx * dx + dz * dz;

            if (minDist <= 0f || sqr >= minDist * minDist)
            {
                return false;
            }

            float dist = Mathf.Sqrt(sqr);
            float nx;
            float nz;

            if (dist > 0.0001f)
            {
                nx = dx / dist;
                nz = dz / dist;
            }
            else
            {
                // Perfectly stacked (two spawns in the same tick). Deterministic golden
                // angle split so the pair never resolves the same way twice in a row.
                float angle = i * 2.399963f;
                nx = Mathf.Cos(angle);
                nz = Mathf.Sin(angle);
                dist = 0.0001f;
            }

            float overlap = minDist - dist;
            float shareA = b.Mass / (a.Mass + b.Mass);
            float shareB = 1f - shareA;

            float speed = overlap * stiffness;

            float relVx = b.Velocity.x - a.Velocity.x;
            float relVz = b.Velocity.z - a.Velocity.z;
            float closing = -(relVx * nx + relVz * nz);
            if (closing > 0f)
            {
                speed += closing * damping;
            }

            if (speed > maxSep)
            {
                speed = maxSep;
            }

            // Anti-deadlock slide. The lower index always takes +90 degrees and the higher
            // index -90, so a head-on pair deterministically picks opposite sides and
            // walks past each other instead of shoving along one axis forever.
            float tx = -nz;
            float tz = nx;
            float headOnA = Mathf.Clamp01(a.Desired.x * nx + a.Desired.z * nz);
            float headOnB = Mathf.Clamp01(-(b.Desired.x * nx + b.Desired.z * nz));
            float tangential = speed * tangentialBias;

            a.Correction.x -= nx * speed * shareA;
            a.Correction.z -= nz * speed * shareA;
            a.Correction.x += tx * tangential * headOnA * shareA;
            a.Correction.z += tz * tangential * headOnA * shareA;

            b.Correction.x += nx * speed * shareB;
            b.Correction.z += nz * speed * shareB;
            b.Correction.x -= tx * tangential * headOnB * shareB;
            b.Correction.z -= tz * tangential * headOnB * shareB;

            return true;
        }

        /// <summary>
        /// REQ-046 / DEC-032. Brake, never push. See the class remarks.
        /// </summary>
        private void ResolvePlayerContact(int i, in CrowdPlayerState player)
        {
            ref MemberRecord a = ref _records[i];

            float dx = a.Position.x - player.Position.x;
            float dz = a.Position.z - player.Position.z;
            float minDist = a.Radius + player.Radius + Tuning.PlayerContactPadding;
            float sqr = dx * dx + dz * dz;

            if (sqr >= minDist * minDist)
            {
                return;
            }

            float dist = Mathf.Sqrt(sqr);
            float nx;
            float nz;

            if (dist > 0.0001f)
            {
                nx = dx / dist;
                nz = dz / dist;
            }
            else
            {
                nx = 0f;
                nz = 1f;
                dist = 0.0001f;
            }

            // Inward speed the member is committing to this frame. The measured value goes
            // to zero once the brace works, so the DESIRED direction times the member's
            // remembered free speed keeps the brace alive; otherwise the member would sink
            // into the player and only the weak depenetration would fight it.
            float measuredInward = -(a.Velocity.x * nx + a.Velocity.z * nz);
            float desiredInward = -(a.Desired.x * nx + a.Desired.z * nz) * a.SpeedEstimate;
            float inward = Mathf.Max(measuredInward, desiredInward);

            if (inward > 0f)
            {
                float brace = inward * Tuning.PlayerBraceStrength;
                a.Correction.x += nx * brace;
                a.Correction.z += nz * brace;
            }

            float depth = minDist - dist;
            if (depth > 0f)
            {
                float push = Mathf.Min(depth * Tuning.PlayerDepenetrationStiffness,
                    Tuning.MaxPlayerDepenetrationSpeed);
                a.Correction.x += nx * push;
                a.Correction.z += nz * push;
            }
        }

        private void Dispatch()
        {
            float maxCorrection = Tuning.MaxCorrectionSpeed;
            float deadzone = Tuning.CorrectionDeadzone;

            for (int i = 0; i < _count; i++)
            {
                ref MemberRecord r = ref _records[i];

                Vector3 c = r.Correction;
                c.y = 0f;

                float sqr = c.x * c.x + c.z * c.z;
                if (sqr < deadzone * deadzone)
                {
                    c = Vector3.zero;
                }
                else if (sqr > maxCorrection * maxCorrection)
                {
                    float scale = maxCorrection / Mathf.Sqrt(sqr);
                    c.x *= scale;
                    c.z *= scale;
                }

                try
                {
                    r.Member.ApplySteering(c);
                }
                catch (System.Exception e)
                {
                    NavigationDiagnostics.WarnThrottled(
                        "crowd.applysteering", 10f,
                        "[Crowd] ApplySteering threw on a member and was skipped: " + e.Message);
                }
            }
        }

        private void RemoveAt(int index)
        {
            int last = _count - 1;
            if (index != last)
            {
                _records[index] = _records[last];
            }

            _records[last] = default;
            _count--;
        }

        /// <summary>Editor-only gizmo helper; never called at runtime.</summary>
        internal bool TryGetDebugMember(int index, out Vector3 position, out float radius, out Vector3 correction)
        {
            if (index < 0 || index >= _count)
            {
                position = Vector3.zero;
                radius = 0f;
                correction = Vector3.zero;
                return false;
            }

            position = _records[index].Position;
            radius = _records[index].Radius;
            correction = _records[index].Correction;
            return true;
        }
    }
}
