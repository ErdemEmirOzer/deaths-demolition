using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
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

        /// <summary>The v3 projectile block of <paramref name="d"/>, or a hitscan-like fast bullet when null.</summary>
        public static ProjectileLaunch From(WeaponDefinition d, ClassicWeaponId id, float fallbackRange)
        {
            if (d == null)
            {
                return new ProjectileLaunch
                {
                    WeaponId = id, Kind = ProjectileKind.Bullet, Caliber = WeaponCaliber.Medium,
                    Speed = 260f, Gravity = 0f, MaxRange = fallbackRange, MaxLifetime = 2f,
                    FalloffStart = fallbackRange, FalloffEnd = fallbackRange, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
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
                FalloffMinShare = p.falloffMinShare
            };
        }
    }

    /// <summary>One live projectile. Pooled in a flat array; no GameObject, no component, no coroutine.</summary>
    public struct ProjectileState
    {
        public const int TouchedCapacity = 8;

        public bool Alive;
        public int Generation;
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
        public bool BranchesResolved;

        // Inline "already damaged" set (ADR-034-1: one projectile damages one body once).
        public int TouchedCount;
        public int T0, T1, T2, T3, T4, T5, T6, T7;

        public int GetTouched(int i)
        {
            switch (i)
            {
                case 0: return T0; case 1: return T1; case 2: return T2; case 3: return T3;
                case 4: return T4; case 5: return T5; case 6: return T6; default: return T7;
            }
        }

        public void AddTouched(int id)
        {
            if (TouchedCount >= TouchedCapacity) return;
            switch (TouchedCount)
            {
                case 0: T0 = id; break; case 1: T1 = id; break; case 2: T2 = id; break; case 3: T3 = id; break;
                case 4: T4 = id; break; case 5: T5 = id; break; case 6: T6 = id; break; default: T7 = id; break;
            }

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
        private static int s_liveCount;
        private static float s_accumulator;
        private static int s_generation;
        private static int s_overflowCount;
        private static int s_spawnedTotal;
        private static bool s_sceneHooked;

        /// <summary>Projectiles alive right now.</summary>
        public static int LiveCount => s_liveCount;

        /// <summary>Projectiles retired early because the pool was full. Should stay 0.</summary>
        public static int OverflowCount => s_overflowCount;

        /// <summary>Projectiles spawned since load (tests / telemetry).</summary>
        public static int SpawnedTotal => s_spawnedTotal;

        /// <summary>The i-th live projectile in spawn order (0 ≤ i &lt; <see cref="LiveCount"/>). Read-only view for renderers.</summary>
        public static ref readonly ProjectileState Get(int i) => ref s_live[s_order[i]];

        /// <summary>
        /// Launch one projectile for <paramref name="shot"/> (one bullet or one pellet). Opens its damage
        /// transaction; the first step happens on the next <see cref="Tick"/>. Returns the pool index.
        /// </summary>
        public static int Spawn(in ShotRequest shot, in ProjectileLaunch launch)
        {
            EnsureHooks();

            int index;
            if (s_liveCount >= Capacity)
            {
                // Retire the oldest (spawn order): it is the one nearest the end of its flight.
                index = s_order[0];
                Vector3 last = s_live[index].Position;
                s_live[index].Alive = false;
                RemoveOrderAt(0);
                s_overflowCount++;
                ProjectileEvents.RaiseExpired(index, last);
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

            s_order[s_liveCount++] = index;
            s_spawnedTotal++;

            ProjectileEvents.RaiseSpawned(new ProjectileSpawnInfo(index, launch.WeaponId, launch.Kind, launch.Caliber,
                shot.Origin, direction, launch.Speed, launch.PelletIndex));
            return index;
        }

        /// <summary>Driver entry: accumulate scaled time and advance in fixed steps.</summary>
        public static void Tick(float deltaTime)
        {
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

        /// <summary>Retire every projectile silently (scene change, run end, tests).</summary>
        public static void Clear()
        {
            for (int i = 0; i < s_liveCount; i++) s_live[s_order[i]].Alive = false;
            s_liveCount = 0;
            s_accumulator = 0f;
        }

        // ------------------------------------------------------------------ one step

        /// <summary>Advance one projectile; false when it died (impact that stops it, range, lifetime).</summary>
        private static bool StepOne(int index, float dt)
        {
            ref ProjectileState p = ref s_live[index];

            Vector3 from = p.Position;
            Vector3 v0 = p.Velocity;
            Vector3 v1 = v0;
            v1.y -= p.Gravity * dt;
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
                    return false;
                }
            }

            if (rangeEnds || p.Age >= p.MaxLifetime)
            {
                p.Alive = false;
                ProjectileEvents.RaiseExpired(index, p.Position);
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
            if (s_sceneHooked)
            {
                return;
            }

            s_sceneHooked = true;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            ProjectileDriver.EnsureExists();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            Clear();
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
            if (s_sceneHooked) SceneManager.sceneUnloaded -= OnSceneUnloaded;
            s_sceneHooked = false;
        }
    }
}
