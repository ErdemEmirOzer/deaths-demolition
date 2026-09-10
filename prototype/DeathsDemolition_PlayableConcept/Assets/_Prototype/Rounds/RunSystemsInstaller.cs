using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// THE RUN SCENE'S COMPOSITION ROOT. Everything that has to exist for the run to be a
    /// run - and nothing else - is created here, in one place, in one order.
    ///
    /// WHY IT EXISTS
    /// -------------
    /// Run.unity holds exactly one GameObject (see <see cref="World.WorldBootstrap"/>), on
    /// purpose: a scene file cannot be merged by four parallel tasks. That decision is
    /// sound, but it leaves a hole nobody owned. Each system was written to be
    /// self-installing *given that somebody creates it*, and for four of them nobody ever
    /// did:
    ///
    ///     RunDirector.EnsureExists()          defined, ZERO callers
    ///     EnemySpawnDirector                  never created, and its prefab slot is
    ///                                         unfillable from an inspector because the
    ///                                         enemy template is built in code
    ///     WeaponSystemInstaller               a component with no scene to sit in
    ///     EnemyFactory.TemplatePrefab  ->  EnemySpawnDirector.EnemyPrefab
    ///                                         a hand-off both sides documented and
    ///                                         neither side performed
    ///
    /// The observable result was a Run scene that built its map, spawned its player and
    /// drew its HUD while the round never started, no enemy ever spawned, no shot could be
    /// fired and the death -> Results transition could not be reached. Not one line of it
    /// logged an error: every system politely returned early on a null it was designed to
    /// tolerate. That is the failure mode this file removes.
    ///
    /// THE ORDER, AND WHY IT IS THIS ORDER
    /// -----------------------------------
    /// Each step below is a hard precondition of the next. Getting it wrong does not
    /// crash; it produces a silent null and a run that half-works, which is strictly worse.
    ///
    ///   0. CONFIG      PrototypeConfig.Active. Round pool size, target active count,
    ///                  the 40 ceiling and every enemy stat come from it. A director that
    ///                  wakes without it sizes round 1 at zero and the round completes
    ///                  instantly.
    ///   1. WORLD       Ground colliders, the twelve SpawnPointMarkers, the PlayerSpawner
    ///                  and the NavMesh bake. This is NOT done here - the caller
    ///                  (WorldBootstrap) has already done it, which is exactly why the
    ///                  call sits where it does.
    ///   2. NAVIGATION  NavigationSystemBehaviour. The crowd runtime holds the player
    ///                  position and heading that the spawn weighting reads; without it
    ///                  SelectPoint bails on "no player registered" every tick and no
    ///                  enemy ever leaves a point.
    ///   3. ENEMY HOST  EnemySystemHost, which forces EnemyFactory to build the procedural
    ///                  zombie template. The template must exist BEFORE step 4 hands it to
    ///                  the spawn director, and before the director's pool is created.
    ///   4. SPAWN       EnemySpawnDirector, then its prefab. Its Awake calls
    ///                  SpawnPointRegistry.EnsureBuilt, which binds the markers step 1
    ///                  created - so it cannot be created before the world.
    ///   5. WEAPONS     WeaponSystemInstaller. It subscribes to PlayerRegistry.RigRegistered
    ///                  and arms whatever rig appears, so it is created before the
    ///                  PlayerSpawner's Start runs later this frame. Without it nothing
    ///                  ever calls PlayerWeaponBinding.Attach, WeaponRegistry stays empty
    ///                  and the player cannot fire.
    ///   5b. TARGETING  <see cref="RunPlayerBinding"/>, same reason and same moment: the
    ///                  crowd and the enemy family are each told the player transform once,
    ///                  by the one component whose job that is.
    ///   6. RUN         RunDirector last, because it is the only one that starts anything.
    ///                  It auto-starts round 1 after 0.35 s and immediately resolves the
    ///                  spawn director, the wallet and the player rig - all of which the
    ///                  five steps above have now put in place.
    ///
    /// Idempotent from end to end: every step is an EnsureExists or a FindObjectOfType
    /// guard, so a scene that already contains one of these keeps its own, and installing
    /// twice changes nothing.
    /// </summary>
    public static class RunSystemsInstaller
    {
        /// <summary>Name of the GameObject that carries the components with no home of their own.</summary>
        public const string HostName = "[Run Systems]";

        private static bool s_reportedMissingConfig;

        /// <summary>
        /// Creates the run's gameplay systems. Call AFTER the world exists and the NavMesh
        /// is baked. Safe to call more than once.
        /// </summary>
        /// <returns>The host GameObject carrying the installed components.</returns>
        public static GameObject Install()
        {
            // ---------------------------------------------------------------- 0. config
            // Active self-loads from Resources, so a Run scene entered directly - which is
            // what the PlayMode tests and PROTOTYPE_DIRECT_TO_RUN both do - is as well
            // configured as one reached through Boot. Only a genuinely absent asset is
            // worth a word, and only once.
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null && !s_reportedMissingConfig)
            {
                s_reportedMissingConfig = true;
                Debug.LogWarning(
                    "[Run] No PrototypeConfig is active, so the round curve, the enemy stats and " +
                    "the safety ceiling all fall back to their defaults. Expected asset: " +
                    "Assets/_Prototype/Core/Resources/PrototypeConfig.asset");
            }

            GameObject host = ResolveHost();

            // ------------------------------------------------------------ 2. navigation
            NavigationSystemBehaviour.EnsureExists();

            // ------------------------------------------------------------ 3. enemy host
            // Also builds the procedural zombie template as a side effect of ApplyConfig,
            // which step 4 depends on.
            EnemySystemHost.EnsureExists();

            // ------------------------------------------------------------------ 4. spawn
            EnemySpawnDirector spawn = EnemySpawnDirector.EnsureExists();
            if (spawn != null && spawn.EnemyPrefab == null)
            {
                // THE HAND-OFF. EnemyFactory owns the template, EnemySpawnDirector owns the
                // pool, and this is the single line that connects them. One pool means one
                // safety ceiling, which is the only reason the 40 cap is a real number.
                spawn.EnemyPrefab = EnemyFactory.TemplatePrefab;
            }

            // ---------------------------------------------------------------- 5. weapons
            if (Object.FindObjectOfType<WeaponSystemInstaller>() == null)
            {
                host.AddComponent<WeaponSystemInstaller>();
            }

            // -------------------------------------------------------------- 5b. targeting
            if (Object.FindObjectOfType<RunPlayerBinding>() == null)
            {
                host.AddComponent<RunPlayerBinding>();
            }

            // -------------------------------------------------------------------- 6. run
            RunDirector.EnsureExists();

            return host;
        }

        private static GameObject ResolveHost()
        {
            GameObject existing = GameObject.Find(HostName);
            return existing != null ? existing : new GameObject(HostName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_reportedMissingConfig = false;
        }
    }

    /// <summary>
    /// Tells the crowd runtime and the enemy family who the player is.
    ///
    /// WHY A COMPONENT AND NOT ONE LINE IN THE PLAYER FACTORY
    /// -----------------------------------------------------
    /// Both consumers deliberately refuse to know the player type.
    /// <see cref="CrowdService.SetPlayer"/> takes a Transform and a radius;
    /// <see cref="EnemyTargeting.SetPlayer"/> takes a Transform, and its own remarks say
    /// so out loud: "the enemy family knows exactly one thing about the player: a
    /// Transform." Putting the call inside PlayerRigFactory would buy the player module a
    /// dependency on navigation AND on enemies to satisfy a contract that exists to
    /// prevent exactly that. The composition root is where a hand-off between two modules
    /// that must not see each other belongs.
    ///
    /// WHAT IT COST TO NOT HAVE IT
    /// ---------------------------
    /// Both sides shipped a fallback - GameObject.FindGameObjectWithTag("Player") - and
    /// both fallbacks fail on this project, because "Player" here is a LAYER and the rig
    /// carries no tag at all. So nobody ever registered, CrowdService.PlayerState.HasPlayer
    /// stayed false, EnemySpawnDirector.SelectPoint bailed on its first line every single
    /// tick, and the smoke run measured enemies.activeMax = 0: three full rounds fought
    /// against nothing, with only a throttled warning to show for it.
    ///
    /// Re-binds on every registration, so a respawn or a second run is handled, and clears
    /// on unregistration so a destroyed rig is never chased.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunPlayerBinding : MonoBehaviour
    {
        private void OnEnable()
        {
            PlayerRegistry.RigRegistered += Bind;
            PlayerRegistry.RigUnregistered += Unbind;

            // A rig that registered before this component woke up will not raise the
            // event again. Same guard WeaponSystemInstaller uses, for the same reason.
            if (PlayerRegistry.Primary != null)
            {
                Bind(PlayerRegistry.Primary);
            }
        }

        private void OnDisable()
        {
            PlayerRegistry.RigRegistered -= Bind;
            PlayerRegistry.RigUnregistered -= Unbind;
        }

        private static void Bind(PlayerRig rig)
        {
            if (rig == null)
            {
                return;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            float radius = config != null ? config.Player.CollisionRadius : 0.45f;

            CrowdService.SetPlayer(rig.transform, radius);
            EnemyTargeting.SetPlayer(rig.transform);
        }

        private static void Unbind(PlayerRig rig)
        {
            if (rig == null || !ReferenceEquals(CrowdService.PlayerTransform, rig.transform))
            {
                return;
            }

            CrowdService.SetPlayer(null, 0f);
            EnemyTargeting.Clear();
        }
    }
}
