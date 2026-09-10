using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.World
{
    /// <summary>
    /// The single component in Run.unity. It builds the map on Awake.
    ///
    /// Execution order is -500, which puts it ahead of everything else in the scene. That
    /// is required rather than tidy: the builder creates the PlayerSpawner and the twelve
    /// SpawnPointMarkers as it goes, and those must exist before any Start runs, or the
    /// player spawns into an empty scene and the spawn registry comes up with no points.
    ///
    /// If the map is already present - a preview left in the scene, or a second bootstrap
    /// - the existing one is thrown away first, so this is idempotent and a domain reload
    /// never leaves two worlds stacked on each other.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class WorldBootstrap : MonoBehaviour
    {
        [Header("Post-processing")]
        [Tooltip("Assets/Settings/DD_GlobalVolumeProfile.asset. Leave empty to build an " +
                 "identical profile in memory at runtime.")]
        // Explicitly null rather than bare: the Inspector is the only thing that ever
        // writes this, and without an initialiser the compiler reports CS0649 on every
        // build. The runtime path handles null by building the same profile in memory.
        [SerializeField] private VolumeProfile _volumeProfile = null;

        [Header("Build")]
        [PrototypeOnly(PendingDecisions.None,
            "Build the map on Awake. Off only when something else drives the build order.", true)]
        [SerializeField] private bool _buildOnAwake = true;

        [PrototypeOnly(PendingDecisions.None,
            "Bake the NavMesh right after building. Off leaves G-05's path service degraded.", true)]
        [SerializeField] private bool _bakeNavMesh = true;

        [PrototypeOnly(PendingDecisions.None,
            "Print the topology, palette, navigation and content self-checks to the console.", true)]
        [SerializeField] private bool _logReport = true;

        [PrototypeOnly(PendingDecisions.None,
            "Create the run's gameplay systems after the map is built. Off leaves a map with " +
            "no round, no enemies and no weapon - a viewer, not a game.", true)]
        [SerializeField] private bool _installRunSystems = true;

        private static WorldBuilder.Result s_last;

        /// <summary>The most recent build. Empty Root means nothing has been built yet.</summary>
        public static WorldBuilder.Result Last => s_last;

        private void Awake()
        {
            if (_buildOnAwake)
            {
                BuildNow();
            }

            // AFTER the build, never before. The spawn director binds the twelve
            // SpawnPointMarkers in its own Awake and the spawn weighting queries the
            // NavMesh, so both have to exist first; installing ahead of the build gives a
            // director bound to nothing and a run that spawns nowhere, silently.
            //
            // Still inside Awake, though, so every system is in place before the
            // PlayerSpawner this build created reaches its Start later in the same frame
            // and the weapon installer can arm the rig on the frame it appears.
            if (_installRunSystems)
            {
                RunSystemsInstaller.Install();
            }
        }

        /// <summary>Builds, or rebuilds, the map. Safe to call more than once.</summary>
        public WorldBuilder.Result BuildNow()
        {
            s_last = WorldBuilder.Build(_volumeProfile, _bakeNavMesh);

            if (_logReport)
            {
                // One block, not four lines: this is read after the fact, and interleaving
                // it with the other three tasks' startup logs makes it useless.
                if (s_last.Ok)
                {
                    Debug.Log("[World] Build complete.\n" + s_last.Report, this);
                }
                else
                {
                    // AN ERROR, NOT A WARNING. A failed self-check means the map broke one
                    // of its own rules - a stranded spawn point, a disconnected route, a
                    // colour trespassing on the gold reserved for pickups - and every one
                    // of those is a gameplay defect. Reported as a warning it did not
                    // count as a console error, so a map that was measurably broken could
                    // still pass a "zero errors" gate. It cannot now.
                    Debug.LogError("[World] Build completed WITH FAILURES.\n" + s_last.Report, this);
                }
            }

            return s_last;
        }

        private void OnDrawGizmosSelected()
        {
            // The map extents and the courtyard, so the blockout can be sanity-checked in
            // the Scene view without entering play mode.
            Gizmos.color = new Color(0.6f, 0.8f, 0.6f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(WorldLayout.MapSize, 0.1f, WorldLayout.MapSize));

            Gizmos.color = new Color(0.9f, 0.85f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(Vector3.zero, WorldLayout.CenterRadius);

            Gizmos.color = new Color(1f, 0.5f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(WorldLayout.PlayerStart, 0.6f);
        }
    }
}
