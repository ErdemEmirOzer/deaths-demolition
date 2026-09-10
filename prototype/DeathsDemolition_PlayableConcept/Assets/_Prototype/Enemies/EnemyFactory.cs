using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Builds the zombie body template and configures whatever comes out of the pool.
    ///
    /// POOL OWNERSHIP - READ THIS BEFORE ADDING A GetOrCreate CALL
    /// This class does NOT own the enemy pool and must never call
    /// PoolService.GetOrCreate on the enemy template. EnemySpawnDirector (G-05/G-06)
    /// owns it. PoolService keys pools by prefab instance id and refuses to serve one
    /// prefab as two different generic types: a second GetOrCreate here would make Core
    /// log an error, and acceptance criterion 27 - zero console errors - would fall.
    ///
    /// So the wiring is one-directional:
    ///     EnemyFactory.TemplatePrefab  ->  EnemySpawnDirector's Enemy Prefab slot
    /// and this class only ever LOOKS UP that pool, through PoolService.TryGet.
    ///
    /// WHAT THIS CLASS IS STILL FOR
    ///   * building the procedural body template exactly once (TemplatePrefab)
    ///   * fanning a live config edit into every enemy already on the map
    ///   * the F1 panel's spawn / clear / kill / break-armour actions
    ///   * a standalone tier schedule for those debug spawns, so R3 / R5 / R8 debuts can
    ///     be exercised without a spawn director in the scene
    /// In a real run the director owns which tier spawns where; see SpawnTierSelector.
    ///
    /// THE 40 CEILING
    /// The director creates the pool with capacity = PrototypeConfig.AbsoluteSafetyCeiling
    /// and policy ReturnNull. Because there is exactly one pool, that ceiling is global:
    /// nothing in this file can produce a 41st zombie, including the debug buttons.
    ///
    /// THE TEMPLATE IS BUILT IN CODE
    /// No .prefab asset and no editor step: the template GameObject is assembled on first
    /// use and kept inactive under DontDestroyOnLoad. Four parallel tasks can therefore
    /// work on the same project without fighting over a prefab file.
    /// </summary>
    public static class EnemyFactory
    {
        [PrototypeOnly(PendingDecisions.CombatScaling,
            "Round pool size assumed when no PrototypeConfig is active. Equals the request's " +
            "own formula at round 1: 12 + 6*(R-1) + floor(2.5*(R-1)^1.15) = 12.")]
        private const int FallbackRoundPoolSize = 12;

        private static ZombieEnemy s_template;
        private static PrototypePool<PooledBehaviour> s_pool;
        private static bool s_poolWarningLogged;

        private static readonly EnemyTierScheduler s_scheduler = new EnemyTierScheduler();
        private static EnemyRandom s_random = new EnemyRandom(0x5EED1234u);

        private static int s_capacity;
        private static uint s_seedCounter = 1u;

        /// <summary>
        /// The pool template, built on demand. Assign this to the spawn director's enemy
        /// prefab slot; both spawn routes then share ONE pool, which is the only way the
        /// safety ceiling can stay a single number.
        ///
        /// The pool is typed as PooledBehaviour on purpose: PoolService keys by prefab and
        /// refuses to serve the same prefab as two different generic types, and the spawn
        /// director asks for PrototypePool&lt;PooledBehaviour&gt;.
        /// </summary>
        public static ZombieEnemy Template
        {
            get
            {
                EnsureTemplate();
                return s_template;
            }
        }

        /// <summary>Same object as <see cref="Template"/>, typed for the spawn director's slot.</summary>
        public static PooledBehaviour TemplatePrefab => Template;

        /// <summary>
        /// Every checked-out zombie, whoever spawned it - this task's debug spawns and the
        /// spawn director's alike. Read straight off the enemy's own roster.
        /// </summary>
        public static IReadOnlyList<ZombieEnemy> Spawned => ZombieEnemy.Live;

        /// <summary>Instances checked out of the pool. This is what the ceiling limits.</summary>
        public static int SpawnedCount => ZombieEnemy.Live.Count;

        /// <summary>Enemies that can still act and still take damage. Corpses excluded.</summary>
        public static int LivingCount
        {
            get
            {
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                int count = 0;
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null && live[i].IsAlive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Tier selection, including the guaranteed R3 / R5 / R8 introductions.</summary>
        public static EnemyTierScheduler Scheduler => s_scheduler;

        public static int Capacity => s_capacity;

        public static bool IsAtCeiling => s_capacity > 0 && SpawnedCount >= s_capacity;

        // ==================================================================== lifecycle

        /// <summary>
        /// Re-read the live config. Called by EnemySystemHost on enable and on every config
        /// change; safe to call repeatedly.
        ///
        /// Note what this does NOT do: it does not create or resize the pool. That is the
        /// spawn director's job and calling GetOrCreate here would collide with it.
        /// </summary>
        public static void ApplyConfig(PrototypeConfig config)
        {
            if (config == null)
            {
                return;
            }

            EnsureTemplate();

            // The absolute technical safety ceiling from the request. The director builds
            // the pool with this same number; holding it here is only so the debug buttons
            // and the F1 readout can answer "are we full?" without touching the pool.
            s_capacity = Mathf.Max(1, config.AbsoluteSafetyCeiling);

            // Push the change into everything already walking the map, however it spawned.
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null)
                {
                    live[i].ApplyConfig(config);
                }
            }
        }

        /// <summary>
        /// Look up the shared enemy pool. Never creates it - see the class remarks.
        /// Returns false until EnemySpawnDirector has created it from this template.
        /// </summary>
        private static bool TryResolvePool()
        {
            if (s_pool != null)
            {
                return true;
            }

            EnsureTemplate();
            if (s_template == null)
            {
                return false;
            }

            if (PoolService.TryGet<PooledBehaviour>(s_template, out PrototypePool<PooledBehaviour> pool))
            {
                s_pool = pool;
                return true;
            }

            if (!s_poolWarningLogged)
            {
                s_poolWarningLogged = true;
                Debug.LogWarning(
                    "[Enemies] No enemy pool exists yet, so this task cannot spawn. The pool is " +
                    "owned by EnemySpawnDirector: assign EnemyFactory.TemplatePrefab to its " +
                    "Enemy Prefab slot. Until then the F1 spawn buttons are inert - this is a " +
                    "wiring gap, not a failure.");
            }

            return false;
        }

        private static void EnsureTemplate()
        {
            if (s_template != null)
            {
                return;
            }

            var go = new GameObject("Zombie_Template");
            go.SetActive(false);
            go.layer = PrototypeLayers.Enemy;
            Object.DontDestroyOnLoad(go);

            // CharacterController first: ZombieEnemy declares it as a RequireComponent, and
            // it is what makes the swarm a physical wall the player cannot walk through
            // (DEC-032) without any Rigidbody bookkeeping.
            var controller = go.AddComponent<CharacterController>();
            controller.radius = 0.45f;
            controller.height = ZombieBodyBuilder.StandHeight;
            controller.center = new Vector3(0f, ZombieBodyBuilder.StandHeight * 0.5f, 0f);
            controller.skinWidth = 0.03f;
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 55f;

            s_template = go.AddComponent<ZombieEnemy>();
            s_template.BuildProceduralBody();
        }

        // ======================================================================= rounds

        /// <summary>Start a round's tier schedule. Call once, before the first spawn of the round.</summary>
        public static void BeginRound(int round)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            int poolSize = config != null ? config.TotalEnemyPool(round) : FallbackRoundPoolSize;
            s_scheduler.BeginRound(round, poolSize, config, ref s_random);
        }

        // ======================================================================= spawns

        /// <summary>
        /// Spawn the next scheduled tier. This is the call the spawn system should use, so
        /// the guaranteed introductions actually happen.
        /// </summary>
        public static ZombieEnemy SpawnNext(Vector3 position, Quaternion rotation)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            EnemyTier tier = s_scheduler.Next(config, ref s_random);
            return Spawn(tier, s_scheduler.Round, position, rotation);
        }

        /// <summary>
        /// Spawn a specific tier. Returns null when the safety ceiling is reached; the
        /// caller must treat that as "not now", never as an error.
        /// </summary>
        public static ZombieEnemy Spawn(EnemyTier tier, int round, Vector3 position, Quaternion rotation)
        {
            PrototypeConfig config = PrototypeConfig.Active;

            if (s_capacity <= 0)
            {
                ApplyConfig(config);
            }

            if (!TryResolvePool())
            {
                return null;
            }

            // Belt and braces: the pool's ReturnNull policy already enforces this, but a
            // caller reading IsAtCeiling gets a cheaper answer than a failed Get.
            if (s_capacity > 0 && SpawnedCount >= s_capacity)
            {
                return null;
            }

            PooledBehaviour instance = s_pool.Get(position, rotation);
            if (instance == null)
            {
                return null;
            }

            var enemy = instance as ZombieEnemy;
            if (enemy == null)
            {
                instance.Despawn();
                Debug.LogError("[Enemies] The shared enemy pool returned something that is not a ZombieEnemy.");
                return null;
            }

            enemy.Initialise(tier, round, NextSeed(), config);
            return enemy;
        }

        private static uint NextSeed()
        {
            // Deterministic given the same spawn order, which is what makes a spawn
            // sequence reproducible without recording every random roll.
            unchecked
            {
                s_seedCounter = s_seedCounter * 1664525u + 1013904223u;
            }

            return s_seedCounter;
        }

        // ===================================================================== teardown

        /// <summary>
        /// Remove every enemy without crediting a kill. This is the round-cleanup and
        /// debug-panel path: no gold, no XP, no kill counter movement.
        /// </summary>
        public static int ClearAll()
        {
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            int count = live.Count;

            // Backwards: Remove() despawns, which pulls the entry out of the roster.
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] != null)
                {
                    live[i].Remove();
                }
            }

            return count;
        }

        /// <summary>Kill every living enemy through the normal death path. Debug only.</summary>
        public static int KillAll()
        {
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            int count = 0;

            for (int i = live.Count - 1; i >= 0; i--)
            {
                ZombieEnemy enemy = live[i];
                if (enemy != null && enemy.IsAlive)
                {
                    enemy.Kill(DamageSourceKind.Bullet);
                    count++;
                }
            }

            return count;
        }

        /// <summary>Shatter every plate on every living enemy. Debug only.</summary>
        public static int BreakAllArmor()
        {
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            int broken = 0;

            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null && live[i].IsAlive)
                {
                    broken += live[i].BreakAllArmor();
                }
            }

            return broken;
        }

        /// <summary>Armour and health summary of the current crowd, for the F1 readout.</summary>
        public static string DescribeCrowd()
        {
            int armoured = 0;
            int plates = 0;
            int helmets = 0;
            int bodies = 0;
            int legs = 0;
            int broken = 0;

            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy enemy = live[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                ArmorSet armor = enemy.Armor;
                if (armor == null || armor.IsBare)
                {
                    continue;
                }

                armoured++;
                plates += armor.ProtectingCount;
                broken += armor.EquippedCount - armor.ProtectingCount;

                if (armor.Protects(ArmorSlot.Helmet)) helmets++;
                if (armor.Protects(ArmorSlot.Body)) bodies++;
                if (armor.Protects(ArmorSlot.Leg)) legs++;
            }

            return string.Format(
                "spawned {0}/{1}  living {2}  armoured {3}  plates {4} (H{5} B{6} L{7})  broken {8}",
                SpawnedCount, s_capacity, LivingCount, armoured, plates, helmets, bodies, legs, broken);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_template = null;
            s_pool = null;
            s_poolWarningLogged = false;
            s_capacity = 0;
            s_seedCounter = 1u;
            s_random = new EnemyRandom(0x5EED1234u);
        }
    }
}
