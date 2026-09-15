using System;
using System.Text;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.World;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · The single component in AP001_GoldenSlice.unity. Builds the pilot on
    /// Awake the way <see cref="WorldBootstrap"/> builds Run.unity, with the AP-001
    /// content in place of the procedural blockout:
    ///
    ///   1. <see cref="PilotGround"/>          stand-in floor + the slice's authored ground collider
    ///   2. <see cref="GoldenSliceSpawner"/>   823 family instances from the layout asset
    ///   3. <see cref="WorldMarkers"/>         the same 12 spawn points, 12 weapon points and player start
    ///   4. <see cref="WorldNavigation"/>      NavMesh bake + the reachability proof (12/12 + 12/12)
    ///   5. <see cref="RunSystemsInstaller"/>  the run's gameplay systems, after the markers exist
    ///   6. <see cref="PrototypeUiInstaller"/> HUD / upgrade / pause, requested as if this were "Run"
    ///
    /// Execution order −500 for the same reason WorldBootstrap has it: the markers and
    /// the PlayerSpawner must exist before any Start in the scene. Steps 5 and 6 are
    /// inside <see cref="BuildNow"/> so a test that calls it gets the whole sequence.
    ///
    /// Step 6 is explicit because the UI installer only reacts to a scene literally
    /// named "Run" (brief B-9). Asking it for the Run screens here makes the pilot
    /// measure the same HUD cost as the reference scene, which is the point of the
    /// pilot: every difference in the probe delta should be content, not scaffolding.
    ///
    /// Idempotent: an existing "[Pilot]" root (and any stray "[GoldenSlice]") is removed
    /// before building, so a rebuild or a domain reload never stacks two slices.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class PilotSceneBootstrap : MonoBehaviour
    {
        public const string RootName = "[Pilot]";

        public struct Result
        {
            public GameObject Root;
            public bool Ok;
            public string Report;
            public GoldenSliceSpawner.Stats Stats;
            public int NavMeshTriangles;
            public int Unreachable;
            /// <summary>The family library the slice was spawned from (tests measure LODs on its prefabs).</summary>
            public ArtFamilyLibrary Library;
        }

        [Header("Content")]
        // Explicit nulls rather than bare fields: the scene builder is the only writer
        // and without an initialiser every build reports CS0649. Null is handled below.
        [SerializeField] private GoldenSliceLayoutAsset _layout = null;
        [SerializeField] private ArtFamilyLibrary _library = null;
        [SerializeField] private Material _earthMaterial = null;
        [SerializeField] private Material _sakuraPurpleMaterial = null;

        [Tooltip("Mesh sub-asset of GoldenSlice_Ground_COLLIDER.fbx. Optional.")]
        [SerializeField] private Mesh _groundColliderMesh = null;

        [Header("Build")]
        [Tooltip("Create the run's gameplay systems after the slice is built. Off leaves a viewer, not a game.")]
        [SerializeField] private bool _installRunSystems = true;

        [Tooltip("Build the Run scene's HUD, upgrade and pause screens in this scene too.")]
        [SerializeField] private bool _buildHud = true;

        [Tooltip("Bake the NavMesh and verify every marker reaches the player start.")]
        [SerializeField] private bool _bakeNavMesh = true;

        [Tooltip("WP-013: wind driver + pollen/petal drift over the slice (the Run scene has the same).")]
        [SerializeField] private bool _buildAtmosphere = true;

        [Tooltip("Print the content and navigation report to the console.")]
        [SerializeField] private bool _logReport = true;

        private static Result s_last;

        /// <summary>The most recent build. Empty Root means nothing has been built yet.</summary>
        public static Result Last => s_last;

        private void Awake()
        {
            BuildNow();
        }

        /// <summary>Builds, or rebuilds, the pilot. Safe to call more than once.</summary>
        public Result BuildNow()
        {
            var report = new StringBuilder(1024);
            bool ok = true;

            Clear();

            var rootGo = new GameObject(RootName);
            rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform root = rootGo.transform;

            if (_layout == null)
            {
                ok = false;
                report.Append("[Pilot] Layout asset is not assigned.\n");
            }

            if (_library == null)
            {
                ok = false;
                report.Append("[Pilot] Family library is not assigned.\n");
            }

            if (_earthMaterial == null)
            {
                report.Append("[Pilot] Earth material is not assigned; the ground renders with no material.\n");
            }

            PilotGround.Build(root, _earthMaterial, _groundColliderMesh);

            GoldenSliceSpawner.Stats stats = GoldenSliceSpawner.Spawn(_layout, _library, root, _sakuraPurpleMaterial);
            if (stats.Missing > 0)
            {
                ok = false;
            }

            int expected = _layout != null && _layout.entries != null ? _layout.entries.Count : 0;
            report.Append("[Pilot] Content: ").Append(stats.Instances).Append('/').Append(expected)
                .Append(" instances (").Append(stats.Missing).Append(" missing), ")
                .Append(stats.Renderers).Append(" renderers, ")
                .Append(stats.LodGroups).Append(" LOD groups, ")
                .Append(stats.Colliders).Append(" colliders, ")
                .Append(stats.Triangles).Append(" triangles (all LODs), ground collider ")
                .Append(_groundColliderMesh != null ? "present" : "absent").Append(".\n");

            // WP-013: the same wind and drift fields as the Run map, placed from the slice's
            // own bounds (pollen over the footprint, petals over the sakura crowns only).
            int driftFields = 0;
            if (_buildAtmosphere)
            {
                WorldAtmosphere.EnsureWind(root);
                driftFields = WorldAtmosphere.BuildDrift(WorldPiece.Group(root, "Atmosphere"),
                    WorldAtmosphere.RegionsFor(SliceFootprint(root, out Bounds? sakura), sakura), null);
            }

            report.Append("[Pilot] Atmosphere: ").Append(_buildAtmosphere ? "wind + " + driftFields + " drift fields" : "off").Append(".\n");

            // Markers before navigation, because the bake's verification walks them; the
            // keep-out list they return is for procedural foliage and unused here.
            WorldMarkers.Build(root);

            int navTriangles = 0;
            int unreachable = 0;
            if (_bakeNavMesh)
            {
                WorldNavigation.Build(root);
                string nav = WorldNavigation.Verify(out bool navOk);
                ok &= navOk;
                report.Append(nav);
                navTriangles = ParseCount(nav, @"NavMesh:\s*(\d+)\s+triangles");
                unreachable = ParseCount(nav, @"(\d+)\s+unreachable");
                if (!navOk && nav.IndexOf("unreachable", StringComparison.Ordinal) < 0)
                {
                    // "NavMesh: EMPTY" reports no per-marker result at all; an empty mesh
                    // strands every marker, so that is the honest count.
                    unreachable = WorldMarkers.Spawns.Length + WorldMarkers.Weapons.Length;
                }
            }
            else
            {
                report.Append("[Pilot] NavMesh: skipped.\n");
            }

            // AFTER the markers and the bake, never before: the spawn director binds the
            // markers in its Awake and the spawn weighting queries the NavMesh.
            if (_installRunSystems)
            {
                RunSystemsInstaller.Install();
            }

            if (_buildHud)
            {
                PrototypeUiInstaller.BuildForScene(PrototypeScenes.Run);
            }

            s_last = new Result
            {
                Root = rootGo,
                Library = _library,
                Ok = ok,
                Report = report.ToString(),
                Stats = stats,
                NavMeshTriangles = navTriangles,
                Unreachable = unreachable
            };

            if (_logReport)
            {
                // One block, read after the fact (same reasoning as WorldBootstrap).
                if (ok)
                {
                    Debug.Log("[Pilot] Build complete.\n" + s_last.Report, this);
                }
                else
                {
                    // An error, not a warning: a missing prefab or a stranded marker is a
                    // defect the zero-errors gate must see.
                    Debug.LogError("[Pilot] Build completed WITH FAILURES.\n" + s_last.Report, this);
                }
            }

            return s_last;
        }

        /// <summary>Destroys an existing pilot root (and any orphaned slice root), if there is one.</summary>
        public static void Clear()
        {
            ClearNamed(RootName);
            ClearNamed(GoldenSliceSpawner.RootName);
        }

        private static void ClearNamed(string name)
        {
            GameObject existing = GameObject.Find(name);
            while (existing != null)
            {
                if (Application.isPlaying)
                {
                    // Destroy is deferred; deactivating keeps the old markers and spawner
                    // out of the FindObjectOfType guards the installers run this frame.
                    existing.SetActive(false);
                    Destroy(existing);
                    break;
                }

                DestroyImmediate(existing);
                existing = GameObject.Find(name);
            }
        }

        /// <summary>
        /// Renderer bounds of the placed slice, and separately of its sakura crowns (layout
        /// entries named after the sakura_foliage family) for the petal field. Null sakura
        /// when the layout has no crowns.
        /// </summary>
        public static Bounds SliceFootprint(Transform root, out Bounds? sakura)
        {
            var footprint = new Bounds(root.position, Vector3.zero);
            bool any = false, anySakura = false;
            var crowns = new Bounds();
            Transform slice = root.Find(GoldenSliceSpawner.RootName);
            Renderer[] renderers = (slice != null ? slice : root).GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (!any)
                {
                    footprint = r.bounds;
                    any = true;
                }
                else
                {
                    footprint.Encapsulate(r.bounds);
                }

                if (IsSakuraCrown(r))
                {
                    if (!anySakura)
                    {
                        crowns = r.bounds;
                        anySakura = true;
                    }
                    else
                    {
                        crowns.Encapsulate(r.bounds);
                    }
                }
            }

            sakura = anySakura ? crowns : (Bounds?)null;
            return footprint;
        }

        private static bool IsSakuraCrown(Renderer r)
        {
            // A crown is whatever wears a sakura material (M_sakura_pink / _purple in AP-001,
            // M_sakura_foliage_0n in AP-003) - the layout's instance names are export labels.
            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.StartsWith("M_sakura", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ParseCount(string text, string pattern)
        {
            Match m = Regex.Match(text ?? string.Empty, pattern);
            return m.Success && int.TryParse(m.Groups[1].Value, out int value) ? value : 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_last = default;
        }

        private void OnDrawGizmosSelected()
        {
            // The stand-in ground and the slice extent, so the pilot can be sanity-checked
            // in the Scene view without entering play mode.
            Gizmos.color = new Color(0.6f, 0.8f, 0.6f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(PilotGround.Size, 0.1f, PilotGround.Size));

            Gizmos.color = new Color(0.9f, 0.7f, 0.85f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(16f, 5f, 13.5f), new Vector3(60f, 10f, 57f));
        }
    }
}
