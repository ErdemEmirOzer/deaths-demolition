using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Weapons
{
    /// <summary>What a weapon hands the projectile system besides the shot itself (from the v3 definition).</summary>
    public struct ProjectileLaunch
    {
        public ClassicWeaponId WeaponId;
        public ProjectileKind Kind;
        public WeaponCaliber Caliber;
        public float Speed;
        public float Gravity;
        public float MaxRange;
        public float MaxLifetime;
        public float SweepRadius;
        public int Penetration;
        public float PenetrationDamageKeep;
        public float FalloffStart;
        public float FalloffEnd;
        public float FalloffMinShare;
        public int PelletIndex;
        public float HeadMultiplier;
        public float ArmorMultiplier;

        // WP-069 B3 · rocket flight (DIRECTION §5): boost to the top speed, gravity per phase, a small yaw / pitch walk.
        public float RocketAcceleration;
        public float RocketMaxSpeed;
        public float RocketBoostSeconds;
        public float GravityCoast;
        public float WobbleDegrees;
        public float WobbleStepSeconds;

        /// <summary>WP-069 B3: Cluster bomblets at the blast (0 = none; 2 / 3 / 4 by tier) and the Explosion "Blast" tier (rl_1).</summary>
        public int ClusterBomblets;
        public int BlastTier;

        /// <summary>The v3 projectile block of <paramref name="d"/>, or a hitscan-like fast bullet when null.</summary>
        public static ProjectileLaunch From(WeaponDefinition d, ClassicWeaponId id, float fallbackRange)
        {
            if (d == null)
            {
                return new ProjectileLaunch
                {
                    WeaponId = id, Kind = ProjectileKind.Bullet, Caliber = WeaponCaliber.Medium,
                    Speed = 260f, Gravity = 0f, MaxRange = fallbackRange, MaxLifetime = 2f,
                    FalloffStart = fallbackRange, FalloffEnd = fallbackRange, FalloffMinShare = 1f, PenetrationDamageKeep = 1f,
                    HeadMultiplier = 1f, ArmorMultiplier = 1f
                };
            }

            WeaponProjectileBlock p = d.projectile;
            return new ProjectileLaunch
            {
                WeaponId = id,
                Kind = p.kind,
                Caliber = p.caliber,
                Speed = Mathf.Max(1f, p.speed),
                Gravity = Mathf.Max(0f, p.gravity),
                MaxRange = Mathf.Max(0.5f, p.maxRange),
                MaxLifetime = Mathf.Max(0.05f, p.maxLifetimeSeconds),
                SweepRadius = Mathf.Max(0f, p.sweepRadius),
                Penetration = Mathf.Max(0, p.penetration),
                PenetrationDamageKeep = Mathf.Clamp01(p.penetrationDamageKeep),
                FalloffStart = p.falloffStart,
                FalloffEnd = p.falloffEnd,
                FalloffMinShare = p.falloffMinShare,
                HeadMultiplier = p.headMultiplier > 0f ? p.headMultiplier : 1f,
                ArmorMultiplier = p.armorDamageMultiplier > 0f ? p.armorDamageMultiplier : 1f
            }.WithRocket(d);
        }

        /// <summary>WP-069 B3: a Rocket kind takes its flight from the rocket block (launch speed, boost gravity first).</summary>
        private ProjectileLaunch WithRocket(WeaponDefinition d)
        {
            if (Kind != ProjectileKind.Rocket || d.rocket == null)
            {
                return this;
            }

            WeaponRocketBlock r = d.rocket;
            ProjectileLaunch l = this;
            l.Speed = Mathf.Max(1f, r.launchSpeed);
            l.Gravity = Mathf.Max(0f, r.gravityBoost);
            l.RocketAcceleration = Mathf.Max(0f, r.acceleration);
            l.RocketMaxSpeed = Mathf.Max(l.Speed, r.maxSpeed);
            l.RocketBoostSeconds = Mathf.Max(0f, r.boostSeconds);
            l.GravityCoast = Mathf.Max(0f, r.gravityCoast);
            l.WobbleDegrees = Mathf.Max(0f, r.wobbleDegreesPerStep);
            l.WobbleStepSeconds = Mathf.Max(0.01f, r.wobbleStepSeconds);
            if (r.selfDetonateSeconds > 0f) l.MaxLifetime = r.selfDetonateSeconds;
            return l;
        }
    }

    /// <summary>One live projectile. Pooled in a flat array; no GameObject, no component, no coroutine.</summary>
    public struct ProjectileState
    {
        /// <summary>
        /// ADR-034-1 "one projectile damages one body once": bodies remembered per projectile. Review R1-A #6 — as
        /// large as one blast can touch (HitscanEngine's overlap buffer), so a branch explosion over a big crowd
        /// can never push a body out of the set and let a penetrating round hit it twice. Kept in a flat side
        /// table (<see cref="ProjectileSystem"/>), not inline.
        /// </summary>
        public const int TouchedCapacity = 192;

        public bool Alive;
        /// <summary>The projectile's key in every ProjectileEvents payload (unique per spawn in a session).</summary>
        public int Generation;
        /// <summary>Pool slot of this state (its row in the touched side table).</summary>
        public int Slot;
        public ShotRequest Shot;
        public ClassicWeaponId WeaponId;
        public ProjectileKind Kind;
        public WeaponCaliber Caliber;
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Origin;
        public float Gravity;
        public float Travelled;
        public float MaxRange;
        public float Age;
        public float MaxLifetime;
        public float SweepRadius;
        public int PelletIndex;
        public int TransactionId;
        public int TargetsLeft;
        public float DamageScale;
        public float PenetrationKeep;
        public float FalloffStart;
        public float FalloffEnd;
        public float FalloffMinShare;
        public float HeadMultiplier;
        public float ArmorMultiplier;
        public bool BranchesResolved;

        // WP-069 B3 · rocket flight and contact (RocketImpacts reads them when the rocket stops or expires).
        public float RocketAcceleration;
        public float RocketMaxSpeed;
        public float RocketBoostSeconds;
        public float GravityBoost;
        public float GravityCoast;
        public float WobbleDegrees;
        public float WobbleStepSeconds;
        public float WobbleTimer;
        public int ClusterBomblets;
        public int BlastTier;
        public bool ContactArmed;
        public int ContactTargetId;
        public Vector3 ContactPoint;
        public Vector3 ContactNormal;

        // WP-077 K10b · the run tree tags this round at spawn (RunTreeHooks.OnProjectileSpawned); the hit path reads them.
        /// <summary>RunTreeHooks.TreeRound* bits (armour bypass, unlimited pierce).</summary>
        public int TreeFlags;
        /// <summary>cap.ar_2: the round of its burst (1-based); cap.sg_2: its barrel (0 left, 1 right).</summary>
        public int TreeOrdinal;
        /// <summary>cap.sg_2: the barrel pair (left + right) this round belongs to.</summary>
        public int TreeVolley;

        // "Already damaged" set (ADR-034-1), rows of ProjectileSystem's side table.
        public int TouchedCount;

        public int GetTouched(int i)
        {
            return i >= 0 && i < TouchedCount ? ProjectileSystem.TouchedAt(Slot, i) : 0;
        }

        public void AddTouched(int id)
        {
            if (TouchedCount >= TouchedCapacity || HasTouched(id)) return;
            ProjectileSystem.SetTouched(Slot, TouchedCount, id);
            TouchedCount++;
        }

        public bool HasTouched(int id)
        {
            for (int i = 0; i < TouchedCount; i++) if (GetTouched(i) == id) return true;
            return false;
        }

        /// <summary>Unit direction of travel (forward when at rest).</summary>
        public Vector3 Direction => Velocity.sqrMagnitude > 1e-8f ? Velocity.normalized : Vector3.forward;

        public float Speed => Velocity.magnitude;
    }

    /// <summary>
    /// WP-069 ADR-069-1 · the projectile system. Live projectiles are structs in one pooled
    /// array (<see cref="Capacity"/>); the driver advances them on a fixed step (1/120 s, at most
    /// <see cref="MaxStepsPerTick"/> steps a tick, the remainder swept at once so a projectile
    /// never stalls) and every step SWEEPS the whole segment (ray, or sphere when the projectile
    /// has a radius), so no speed tunnels. Hits resolve through the same code as the hitscan
    /// engine (targets, regions, StaggerGovernor, feedback, props, surfaces, branches) inside the
    /// projectile's own damage transaction, resumed each step. Zero allocation per frame.
    ///
    /// Scaled time: pause and hit-stop freeze projectiles with the weapons. Tests drive the clock
    /// directly with <see cref="Step"/> / <see cref="SimulateFor"/>.
    /// </summary>
    public static class ProjectileSystem
    {
        [PrototypeOnly(PendingDecisions.None, "WP-069 ADR-069-1: live projectile ceiling (worst realistic case ≈ 60; the rest is headroom). Full = the oldest projectile is retired and counted.", true)]
        public const int Capacity = 256;

        [PrototypeOnly(PendingDecisions.None, "WP-069 ADR-069-1: simulation step, seconds (120 Hz).", true)]
        public const float FixedStep = 1f / 120f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 ADR-069-1: steps per tick before the remainder is swept in one go (a long frame never stalls projectiles).", true)]
        public const int MaxStepsPerTick = 8;

        private static readonly ProjectileState[] s_live = new ProjectileState[Capacity];
        private static readonly int[] s_order = new int[Capacity];   // live indices in spawn order
        private static readonly int[] s_touched = new int[Capacity * ProjectileState.TouchedCapacity];   // review R1-A #6
        private static int s_liveCount;
        private static float s_accumulator;
        private static int s_generation;
        private static int s_overflowCount;
        private static int s_spawnedTotal;
        private static bool s_sceneHooked;
        private static bool s_runHooked;

        // Review R1-A #13: a Spawn / Clear raised from inside a step (an impact / death listener) is applied after the step.
        private const int PendingCapacity = 64;
        private static readonly ShotRequest[] s_pendingShots = new ShotRequest[PendingCapacity];
        private static readonly ProjectileLaunch[] s_pendingLaunches = new ProjectileLaunch[PendingCapacity];
        private static int s_pendingCount;
        private static bool s_pendingClear;
        private static bool s_stepping;

        // WP-078 (S-10) run counters.
        private static int s_peakLive;
        private static int s_runOverflow;
        private static int s_runSpawned;
        private static readonly float[] s_lastLaunchSpeed = new float[16];

        /// <summary>Projectiles alive right now.</summary>
        public static int LiveCount => s_liveCount;

        /// <summary>Projectiles retired early because the pool was full, this run (reset at RunStarted). Should stay 0.</summary>
        public static int OverflowCount => s_runOverflow;

        /// <summary>Pool-full retirements since load (never reset by a run).</summary>
        public static int OverflowCountTotal => s_overflowCount;

        /// <summary>WP-078: the most projectiles alive at once this run (reset at RunStarted).</summary>
        public static int PeakLiveCount => s_peakLive;

        /// <summary>Projectiles spawned since load (tests / telemetry).</summary>
        public static int SpawnedTotal => s_spawnedTotal;

        /// <summary>WP-078: projectiles spawned this run.</summary>
        public static int SpawnedThisRun => s_runSpawned;

        /// <summary>WP-078: launch speed (m/s) of the last projectile this weapon fired this run; 0 when it has not fired.</summary>
        public static float LastLaunchSpeed(ClassicWeaponId id)
        {
            int i = (int)id;
            return i >= 0 && i < s_lastLaunchSpeed.Length ? s_lastLaunchSpeed[i] : 0f;
        }

        /// <summary>Projectiles waiting to spawn after the current step (re-entrant Spawn from a listener).</summary>
        public static int PendingSpawnCount => s_pendingCount;

        internal static int TouchedAt(int slot, int i) => s_touched[slot * ProjectileState.TouchedCapacity + i];

        internal static void SetTouched(int slot, int i, int id) => s_touched[slot * ProjectileState.TouchedCapacity + i] = id;

        /// <summary>The i-th live projectile in spawn order (0 ≤ i &lt; <see cref="LiveCount"/>). Read-only view for renderers.</summary>
        public static ref readonly ProjectileState Get(int i) => ref s_live[s_order[i]];

        /// <summary>
        /// Launch one projectile for <paramref name="shot"/> (one bullet or one pellet). Opens its damage
        /// transaction; the first step happens on the next <see cref="Tick"/>. Returns the pool index.
        /// </summary>
        public static int Spawn(in ShotRequest shot, in ProjectileLaunch launch)
        {
            EnsureHooks();

            if (s_stepping)
            {
                // Review R1-A #13: a listener spawning mid-step must not evict / shift the order list under the step.
                if (s_pendingCount < PendingCapacity)
                {
                    s_pendingShots[s_pendingCount] = shot;
                    s_pendingLaunches[s_pendingCount] = launch;
                    s_pendingCount++;
                }
                else
                {
                    s_overflowCount++;
                    s_runOverflow++;
                }

                return -1;
            }

            int index;
            if (s_liveCount >= Capacity)
            {
                // Retire the oldest (spawn order): it is the one nearest the end of its flight.
                index = s_order[0];
                ref ProjectileState old = ref s_live[index];
                old.Alive = false;
                RemoveOrderAt(0);
                s_overflowCount++;
                s_runOverflow++;
                ProjectileEvents.RaiseExpired(new ProjectileExpireInfo(old.Generation, index, old.WeaponId, old.Position, ProjectileEndReason.Evicted));
            }
            else
            {
                index = FindFreeSlot();
            }

            Vector3 direction = shot.Direction.sqrMagnitude > 1e-8f ? shot.Direction.normalized : Vector3.forward;
            int transaction = DamageTransaction.NextId();

            ref ProjectileState p = ref s_live[index];
            p = default;
            p.Alive = true;
            p.Generation = ++s_generation;
            if (p.Generation <= 0) p.Generation = s_generation = 1;
            p.Slot = index;
            p.Shot = shot;
            p.WeaponId = launch.WeaponId;
            p.Kind = launch.Kind;
            p.Caliber = launch.Caliber;
            p.Position = shot.Origin;
            p.Origin = shot.Origin;
            p.Velocity = direction * launch.Speed;
            p.Gravity = launch.Gravity;
            p.MaxRange = Mathf.Min(launch.MaxRange, shot.Range > 0f ? shot.Range : launch.MaxRange);
            p.MaxLifetime = launch.MaxLifetime;
            p.SweepRadius = launch.SweepRadius;
            p.PelletIndex = launch.PelletIndex;
            p.TransactionId = transaction;
            p.TargetsLeft = 1 + Mathf.Max(0, shot.PierceTargets) + Mathf.Max(0, launch.Penetration);
            p.DamageScale = 1f;
            p.PenetrationKeep = launch.Penetration > 0 ? launch.PenetrationDamageKeep : Mathf.Clamp(shot.PierceDamageFalloff, 0.1f, 1f);
            p.FalloffStart = launch.FalloffStart;
            p.FalloffEnd = launch.FalloffEnd;
            p.FalloffMinShare = launch.FalloffMinShare;
            p.HeadMultiplier = launch.HeadMultiplier > 0f ? launch.HeadMultiplier : 1f;
            p.ArmorMultiplier = launch.ArmorMultiplier > 0f ? launch.ArmorMultiplier : 1f;
            p.RocketAcceleration = launch.RocketAcceleration;
            p.RocketMaxSpeed = launch.RocketMaxSpeed;
            p.RocketBoostSeconds = launch.RocketBoostSeconds;
            p.GravityBoost = launch.Gravity;
            p.GravityCoast = launch.GravityCoast;
            p.WobbleDegrees = launch.WobbleDegrees;
            p.WobbleStepSeconds = launch.WobbleStepSeconds;
            p.ClusterBomblets = launch.ClusterBomblets;
            p.BlastTier = launch.BlastTier;
            RunTreeHooks.OnProjectileSpawned(ref p);   // WP-077 K10b: cap.ar_1 / cap.sr_1 / cap.ar_2 / cap.sg_2 round tags

            s_order[s_liveCount++] = index;
            s_spawnedTotal++;
            s_runSpawned++;
            if (s_liveCount > s_peakLive) s_peakLive = s_liveCount;
            int w = (int)launch.WeaponId;
            if (w >= 0 && w < s_lastLaunchSpeed.Length) s_lastLaunchSpeed[w] = p.Velocity.magnitude;

            ProjectileEvents.RaiseSpawned(new ProjectileSpawnInfo(p.Generation, index, launch.WeaponId, launch.Kind, launch.Caliber,
                shot.Origin, direction, launch.Speed, launch.PelletIndex));
            return index;
        }

        /// <summary>Driver entry: accumulate scaled time and advance in fixed steps.</summary>
        public static void Tick(float deltaTime)
        {
            // WP-069 B4: the Specials' chains / effects run on the same scaled clock as the projectiles.
            SpecialImpacts.Tick(deltaTime);
            RunTreeGroundFire.Tick(deltaTime);   // WP-077 K10b burning ground (Ash Cloud, Hibana)

            if (deltaTime <= 0f || s_liveCount == 0)
            {
                if (s_liveCount == 0) s_accumulator = 0f;
                return;
            }

            s_accumulator += deltaTime;
            int steps = 0;
            while (s_accumulator >= FixedStep && steps < MaxStepsPerTick)
            {
                Step(FixedStep);
                s_accumulator -= FixedStep;
                steps++;
            }

            if (steps == MaxStepsPerTick && s_accumulator >= FixedStep)
            {
                // A long frame: sweep the rest in one segment rather than letting projectiles fall behind.
                Step(s_accumulator);
                s_accumulator = 0f;
            }
        }

        /// <summary>Tests: advance every projectile by exactly <paramref name="dt"/> seconds (one sweep).</summary>
        public static void Step(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            s_stepping = true;
            try
            {
                int i = 0;
                while (i < s_liveCount)
                {
                    int index = s_order[i];
                    if (StepOne(index, dt))
                    {
                        i++;
                        continue;
                    }

                    // Died this step: remove from the order list (keep spawn order), do not advance i.
                    RemoveOrderAt(i);
                }
            }
            finally
            {
                s_stepping = false;
            }

            RunTreeHooks.FlushTreeBlasts();   // WP-077 cap.sg_2: blasts queued by this step, each in its own transaction
            ApplyPending();
        }

        /// <summary>Review R1-A #13: the clears and spawns listeners asked for during the step, in order.</summary>
        private static void ApplyPending()
        {
            if (s_pendingClear)
            {
                s_pendingClear = false;
                Clear();
            }

            int n = s_pendingCount;
            s_pendingCount = 0;
            for (int k = 0; k < n; k++)
            {
                Spawn(s_pendingShots[k], s_pendingLaunches[k]);
            }
        }

        /// <summary>Tests: advance by <paramref name="seconds"/> in fixed steps (deterministic test clock).</summary>
        public static void SimulateFor(float seconds)
        {
            float t = 0f;
            while (t + 1e-6f < seconds)
            {
                float dt = Mathf.Min(FixedStep, seconds - t);
                Step(dt);
                t += dt;
            }
        }

        /// <summary>
        /// Retire every projectile (scene change, run end, tests). Review R1-A #5: each raises Expired with
        /// <see cref="ProjectileEndReason.Cleared"/> so per-projectile consumers close their state. Called from
        /// inside a step it is deferred to the end of that step.
        /// </summary>
        public static void Clear()
        {
            if (s_stepping)
            {
                s_pendingClear = true;
                s_pendingCount = 0;
                return;
            }

            int n = s_liveCount;
            s_liveCount = 0;
            s_accumulator = 0f;
            s_pendingCount = 0;
            for (int i = 0; i < n; i++)
            {
                int index = s_order[i];
                ref ProjectileState p = ref s_live[index];
                if (!p.Alive) continue;
                p.Alive = false;
                ProjectileEvents.RaiseExpired(new ProjectileExpireInfo(p.Generation, index, p.WeaponId, p.Position, ProjectileEndReason.Cleared));
            }
        }

        // ------------------------------------------------------------------ one step

        /// <summary>Advance one projectile; false when it died (impact that stops it, range, lifetime).</summary>
        private static bool StepOne(int index, float dt)
        {
            ref ProjectileState p = ref s_live[index];

            Vector3 from = p.Position;
            Vector3 v0 = p.Velocity;
            Vector3 v1 = v0;
            if (p.Kind == ProjectileKind.Rocket)
            {
                v1 = RocketImpacts.Advance(ref p, v0, dt);
            }
            else
            {
                v1.y -= p.Gravity * dt;
            }
            Vector3 delta = (v0 + v1) * (0.5f * dt);
            float length = delta.magnitude;
            float remaining = p.MaxRange - p.Travelled;
            bool rangeEnds = length >= remaining;
            if (rangeEnds)
            {
                length = Mathf.Max(0f, remaining);
            }

            p.Velocity = v1;
            p.Age += dt;

            if (length > 1e-5f)
            {
                Vector3 direction = delta / delta.magnitude;
                bool stopped = HitscanEngine.ResolveProjectileSegment(ref p, from, direction, length, out float travelledThisStep, out Vector3 end);
                p.Travelled += travelledThisStep;
                p.Position = end;
                if (stopped)
                {
                    p.Alive = false;
                    if (p.Kind == ProjectileKind.Rocket) RocketImpacts.OnContact(ref p);   // after the hit's transaction closed
                    return false;
                }
            }

            if (rangeEnds || p.Age >= p.MaxLifetime)
            {
                p.Alive = false;
                if (p.Kind == ProjectileKind.Rocket) RocketImpacts.OnSelfDetonate(ref p);   // DIRECTION §5: 70 m / 2.2 s → air burst
                ProjectileEvents.RaiseExpired(new ProjectileExpireInfo(p.Generation, index, p.WeaponId, p.Position,
                    rangeEnds ? ProjectileEndReason.Range : ProjectileEndReason.Lifetime));
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ pool bookkeeping

        private static int FindFreeSlot()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (!s_live[i].Alive) return i;
            }

            return 0;   // unreachable while s_liveCount < Capacity
        }

        private static void RemoveOrderAt(int orderIndex)
        {
            for (int k = orderIndex; k < s_liveCount - 1; k++) s_order[k] = s_order[k + 1];
            s_liveCount--;
        }

        private static void EnsureHooks()
        {
            // Review R1-A #14: every spawn makes sure the driver is alive (cheap early-out), not only the first one.
            ProjectileDriver.EnsureExists();
            EnsureRunHooks();
            if (s_sceneHooked)
            {
                return;
            }

            s_sceneHooked = true;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>WP-078: run-scoped counters and the round-end line. Installed at load, so round 1 reports too.</summary>
        private static void EnsureRunHooks()
        {
            if (s_runHooked)
            {
                return;
            }

            s_runHooked = true;
            RunEvents.RunStarted += OnRunStarted;
            RunEvents.RoundCompleted += OnRoundCompleted;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallRunHooks()
        {
            EnsureRunHooks();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            Clear();
            SpecialImpacts.Clear();
            RunTreeGroundFire.Clear();   // WP-077 K10b
        }

        private static void OnRunStarted(int startingRound)
        {
            s_peakLive = s_liveCount;
            s_runOverflow = 0;
            s_runSpawned = 0;
            for (int i = 0; i < s_lastLaunchSpeed.Length; i++) s_lastLaunchSpeed[i] = 0f;
        }

        private static void OnRoundCompleted(int round)
        {
            Debug.Log(RoundReport(round));
        }

        /// <summary>
        /// WP-078 (S-10): <c>[Projectile] round=N active=A peak=P overflow=O spawned=S speed ar_1=260.0/260.0 …</c> —
        /// every weapon fired this run: its measured launch speed / the Arsenal catalog value. Built once per round.
        /// </summary>
        public static string RoundReport(int round)
        {
            var sb = new StringBuilder(160);
            sb.Append("[Projectile] round=").Append(round)
              .Append(" active=").Append(s_liveCount)
              .Append(" peak=").Append(s_peakLive)
              .Append(" overflow=").Append(s_runOverflow)
              .Append(" spawned=").Append(s_runSpawned)
              .Append(" speed");
            for (int i = 0; i < s_lastLaunchSpeed.Length; i++)
            {
                if (s_lastLaunchSpeed[i] <= 0f) continue;
                WeaponDefinition d = Arsenal.Get((ClassicWeaponId)i);
                sb.Append(' ').Append(d != null ? d.weaponId : i.ToString()).Append('=')
                  .Append(s_lastLaunchSpeed[i].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('/')
                  .Append(d != null ? d.projectile.speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-");
            }

            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < Capacity; i++) s_live[i] = default;
            s_liveCount = 0;
            s_accumulator = 0f;
            s_generation = 0;
            s_overflowCount = 0;
            s_spawnedTotal = 0;
            s_runOverflow = 0;
            s_runSpawned = 0;
            s_peakLive = 0;
            s_pendingCount = 0;
            s_pendingClear = false;
            s_stepping = false;
            for (int i = 0; i < s_lastLaunchSpeed.Length; i++) s_lastLaunchSpeed[i] = 0f;
            if (s_sceneHooked) SceneManager.sceneUnloaded -= OnSceneUnloaded;
            s_sceneHooked = false;
            s_runHooked = false;   // RunEvents clears its own handlers on this reset
        }
    }
}
