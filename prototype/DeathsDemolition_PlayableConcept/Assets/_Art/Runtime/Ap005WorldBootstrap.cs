using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.World;
using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art
{
    /// <summary>
    /// WP-014 · Runtime side of the authored world (ADR-014-1). The instances, the colliders
    /// and the baked NavMesh are IN the scene (PlacementImporter); this component only adds
    /// what the procedural WorldBuilder adds at runtime and what must be built after the
    /// scene exists: the lighting/volume/wind/drift base (WorldAtmosphere.BuildFor with
    /// regions derived from placement.json), the twelve spawn and twelve weapon markers and
    /// the player start (WorldMarkers.Build with the placement set), the NavMesh verification
    /// and — when this is the run world — the run systems and, standalone, the HUD.
    ///
    /// Two ways in: additively under Run.unity (WorldSource.AP005; the active scene stays
    /// "Run", the HUD is the Run scene's) or standalone (tests, showcase) where it behaves
    /// like the pilot bootstrap and builds the HUD itself.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    public sealed class Ap005WorldBootstrap : MonoBehaviour
    {
        public const string RootName = "[AP005 World]";
        public const string InstancesName = "Instances";

        public struct Result
        {
            public bool Ok;
            public string Report;
            public int Instances;
            public int Renderers;
            public int LodGroups;
            public int Colliders;
            public int NavMeshTriangles;
            public int Unreachable;
            public int DriftFields;
            public bool UnderRun;
        }

        [Header("Content (written by PlacementImporter)")]
        [SerializeField] private WorldPlacementAsset _placement = null;
        [SerializeField] private VolumeProfile _volumeProfile = null;

        [Header("Build")]
        [Tooltip("Create the run's gameplay systems after the markers exist (the world scene owns this under Run too).")]
        [SerializeField] private bool _installRunSystems = true;

        [Tooltip("Build the Run HUD when this scene is the active scene (standalone); under Run the installer already did.")]
        [SerializeField] private bool _buildHudWhenStandalone = true;

        [Tooltip("Verify every marker reaches the player start on the baked NavMesh.")]
        [SerializeField] private bool _verifyNavMesh = true;

        [Tooltip("Sun, volume, wind and drift fields (WorldAtmosphere.BuildFor).")]
        [SerializeField] private bool _buildAtmosphere = true;

        [SerializeField] private bool _logReport = true;

        private static Result s_last;

        public static Result Last => s_last;

        public WorldPlacementAsset Placement => _placement;

        private void Awake()
        {
            BuildNow();
        }

        public Result BuildNow()
        {
            s_last = default;
            var report = new StringBuilder(1024);
            bool ok = true;
            bool underRun = SceneManager.GetActiveScene().name == PrototypeScenes.Run && gameObject.scene.name != PrototypeScenes.Run;
            // WP-081: under the main menu as its cinematic backdrop - the look only (atmosphere, sky, layout); none of the run.
            bool viewer = WorldViewer.Active;

            Transform root = transform;
            Transform instances = root.Find(InstancesName);
            int count = instances != null ? instances.childCount : 0;
            if (_placement == null)
            {
                ok = false;
                report.Append("[AP005] Placement asset is not assigned.\n");
            }
            else if (count != _placement.instances.Count)
            {
                ok = false;
                report.Append("[AP005] Scene holds ").Append(count).Append(" instances, placement lists ").Append(_placement.instances.Count).Append(".\n");
            }

            Renderer[] renderers = instances != null ? instances.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            int lodGroups = instances != null ? instances.GetComponentsInChildren<LODGroup>(true).Length : 0;
            int colliders = instances != null ? instances.GetComponentsInChildren<Collider>(true).Length : 0;
            report.Append("[AP005] Content: ").Append(count).Append(" instances, ").Append(renderers.Length).Append(" renderers, ")
                .Append(lodGroups).Append(" LOD groups, ").Append(colliders).Append(" colliders (").Append(viewer ? "menu viewer" : underRun ? "under Run" : "standalone").Append(").\n");

            // WP-074 B1b: the live layout before the markers register (S-03's SpawnWorldProfile refreshes on WorldLayout.Changed);
            // WP-075 C3: and before the atmosphere, whose skybox follows the layout (v6 → sky v2).
            WorldLayout.SetCurrent(LayoutOf(_placement));

            int driftFields = 0;
            if (_buildAtmosphere)
            {
                WorldAtmosphere.BuildFor(root, _volumeProfile, DriftRegions(_placement, instances));
                driftFields = root.GetComponentsInChildren<AtmosphereDrift>(true).Length;
            }

            report.Append("[AP005] Atmosphere: ").Append(_buildAtmosphere ? "sun + volume + wind + " + driftFields + " drift fields" : "off").Append(".\n");

            // WP-093: the living ground (V6 only; built on the vegetation system's next LateUpdate, menu backdrop included).
            VegetationWorldHookup.Ensure(this);

            if (viewer)
            {
                // AP-028 v002 menu budget: wet surfaces from the baked probes only - no real-time planar reflection.
                int planar = 0;
                foreach (PlanarReflection reflection in FindObjectsOfType<PlanarReflection>(true))
                {
                    if (reflection.gameObject.scene == gameObject.scene)
                    {
                        reflection.enabled = false;
                        planar++;
                    }
                }

                report.Append("[AP005] Menu viewer: no markers, NavMesh check, run systems or HUD; ").Append(planar).Append(" planar reflection(s) off.\n");
                s_last = new Result { Ok = ok, Report = report.ToString(), Instances = count, Renderers = renderers.Length, LodGroups = lodGroups, Colliders = colliders, DriftFields = driftFields };
                if (_logReport)
                {
                    Debug.Log("[AP005] World ready (menu viewer).\n" + s_last.Report, this);
                }

                return s_last;
            }

            WorldMarkers.Build(root, _placement != null ? MarkerSetFrom(_placement) : WorldMarkers.ProceduralSet());

            // The baked NavMesh is registered by NavMeshSurface.OnEnable, which runs AFTER this
            // Awake (execution order −500). Register it now, so the verification below and the
            // spawn director's weighting see the surface in the same frame; OnEnable's own
            // AddData is a no-op once the instance is valid.
            int surfaces = 0;
            foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
            {
                if (surface.navMeshData != null)
                {
                    surface.AddData();
                    surfaces++;
                }
            }

            report.Append("[AP005] NavMesh: ").Append(surfaces).Append(" baked surface(s) registered.\n");

            int navTriangles = 0;
            int unreachable = 0;
            if (_verifyNavMesh)
            {
                string nav = WorldNavigation.Verify(out bool navOk);
                ok &= navOk;
                report.Append(nav);
                navTriangles = ParseCount(nav, @"NavMesh:\s*(\d+)\s+triangles");
                unreachable = ParseCount(nav, @"(\d+)\s+unreachable");
                if (!navOk && nav.IndexOf("unreachable", StringComparison.Ordinal) < 0)
                {
                    unreachable = WorldMarkers.Spawns.Length + WorldMarkers.Weapons.Length;
                }
            }

            // AFTER the markers, never before: the spawn director binds the markers in its
            // Awake and the spawn weighting queries the NavMesh (same rule as WorldBootstrap).
            if (_installRunSystems)
            {
                RunSystemsInstaller.Install();
            }

            if (_buildHudWhenStandalone && !underRun)
            {
                PrototypeUiInstaller.BuildForScene(PrototypeScenes.Run);
            }

            s_last = new Result
            {
                Ok = ok,
                Report = report.ToString(),
                Instances = count,
                Renderers = renderers.Length,
                LodGroups = lodGroups,
                Colliders = colliders,
                NavMeshTriangles = navTriangles,
                Unreachable = unreachable,
                DriftFields = driftFields,
                UnderRun = underRun
            };

            if (_logReport)
            {
                if (ok)
                {
                    Debug.Log("[AP005] World ready.\n" + s_last.Report, this);
                }
                else
                {
                    Debug.LogError("[AP005] World ready WITH FAILURES.\n" + s_last.Report, this);
                }
            }

            return s_last;
        }

        /// <summary>
        /// The marker set from placement.json: ids must match the prototype tables (the
        /// spawn director and the weapon selector key on them); positions come from the
        /// placement, 5 cm above the authored ground like the procedural markers; the weapon
        /// off-route flag stays the design's (WorldMarkers.Weapons) since placement.json
        /// carries only the weight.
        /// </summary>
        /// <summary>WP-074 B1b · The layout a placement lays out ("v6" → V6; anything else, or no placement → the ±45 AP-005 layout).</summary>
        public static WorldLayoutKind LayoutOf(WorldPlacementAsset placement)
        {
            return placement != null && string.Equals(placement.layout, "v6", StringComparison.OrdinalIgnoreCase) ? WorldLayoutKind.V6 : WorldLayoutKind.Ap005;
        }

        public static WorldMarkers.MarkerSet MarkerSetFrom(WorldPlacementAsset placement)
        {
            int inner = placement.innerSpawnPoints != null ? placement.innerSpawnPoints.Count : 0;
            var set = new WorldMarkers.MarkerSet
            {
                Spawns = new WorldMarkers.SpawnPlacement[placement.spawnPoints.Count + inner],
                SpawnPositions = new Vector3[placement.spawnPoints.Count + inner],
                Weapons = new WorldMarkers.WeaponPlacement[placement.weaponPoints.Count],
                WeaponPositions = new Vector3[placement.weaponPoints.Count],
                PlayerStart = placement.playerStart
            };

            for (int i = 0; i < placement.spawnPoints.Count; i++)
            {
                WorldPlacementAsset.Point p = placement.spawnPoints[i];
                set.Spawns[i] = new WorldMarkers.SpawnPlacement(p.id, p.position.x, p.position.z, "AP-005 placement");
                set.SpawnPositions[i] = p.position + Vector3.up * 0.05f;
            }

            // WP-074 B1b · v6 inner gates (S-03 WP-076 B6: Kind = Inner; the registry adds ids its table does not list).
            for (int i = 0; i < inner; i++)
            {
                WorldPlacementAsset.Point p = placement.innerSpawnPoints[i];
                int k = placement.spawnPoints.Count + i;
                set.Spawns[k] = new WorldMarkers.SpawnPlacement(p.id, p.position.x, p.position.z, "v6 inner gate", DD.Prototype.Spawn.SpawnPointKind.Inner);
                set.SpawnPositions[k] = p.position + Vector3.up * 0.05f;
            }

            for (int i = 0; i < placement.weaponPoints.Count; i++)
            {
                WorldPlacementAsset.Point p = placement.weaponPoints[i];
                bool offRoute = false;
                for (int j = 0; j < WorldMarkers.Weapons.Length; j++)
                {
                    if (WorldMarkers.Weapons[j].Name == p.id)
                    {
                        offRoute = WorldMarkers.Weapons[j].OffRoute;
                        break;
                    }
                }

                set.Weapons[i] = new WorldMarkers.WeaponPlacement(p.id, p.position.x, p.position.z, p.weight, offRoute, "AP-005 placement");
                set.WeaponPositions[i] = p.position + Vector3.up * 0.05f;
            }

            return set;
        }

        /// <summary>
        /// Drift regions from the placement (ADR-014-1 seam): pollen over the whole map,
        /// sakura petals over the east garden's crowns and, thinner, over the courtyard —
        /// the same three roles the procedural map uses, with the boxes taken from the
        /// authored region polygons and the placed sakura crowns instead of constants.
        /// </summary>
        public static WorldAtmosphere.DriftRegion[] DriftRegions(WorldPlacementAsset placement, Transform instances)
        {
            if (placement == null)
            {
                return WorldAtmosphere.DefaultRunRegions();
            }

            var list = new List<WorldAtmosphere.DriftRegion>(3);
            var map = new Bounds(Vector3.zero, Vector3.zero);
            bool anyRegion = false;
            for (int i = 0; i < placement.regions.Count; i++)
            {
                Bounds b = WorldPlacementAsset.RegionBounds(placement.regions[i], 0f, 0f);
                if (!anyRegion) { map = b; anyRegion = true; } else { map.Encapsulate(b); }
            }

            if (!anyRegion)
            {
                map = new Bounds(Vector3.zero, new Vector3(90f, 0f, 90f));
            }

            list.Add(new WorldAtmosphere.DriftRegion(WorldAtmosphere.DriftKind.Pollen, AtmosphereDrift.Region.Global,
                new Vector3(map.center.x, 3.2f, map.center.z), new Vector3(map.size.x, 5f, map.size.z), 1f));

            Bounds? crowns = SakuraCrowns(placement, instances, "east");
            if (crowns.HasValue)
            {
                Bounds b = crowns.Value;
                list.Add(new WorldAtmosphere.DriftRegion(WorldAtmosphere.DriftKind.Petals, AtmosphereDrift.Region.EastGarden,
                    new Vector3(b.center.x, b.max.y + 2f, b.center.z), new Vector3(b.size.x + 8f, 1f, b.size.z + 8f), 1f));
            }

            WorldPlacementAsset.Region center = placement.FindRegion("center");
            if (center != null)
            {
                Bounds b = WorldPlacementAsset.RegionBounds(center, 0f, 0f);
                list.Add(new WorldAtmosphere.DriftRegion(WorldAtmosphere.DriftKind.Petals, AtmosphereDrift.Region.Courtyard,
                    new Vector3(b.center.x, 12f, b.center.z), new Vector3(b.size.x, 1f, b.size.z), 0.45f));
            }

            return list.ToArray();
        }

        private static Bounds? SakuraCrowns(WorldPlacementAsset placement, Transform instances, string region)
        {
            Bounds crowns = default;
            bool any = false;
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                if (inst.family != "sakura_foliage" || inst.region != region)
                {
                    continue;
                }

                Bounds b;
                Transform t = instances != null && i < instances.childCount ? instances.GetChild(i) : null;
                Renderer r = t != null ? t.GetComponentInChildren<Renderer>(false) : null;
                if (r != null)
                {
                    b = r.bounds;
                }
                else
                {
                    b = new Bounds(inst.position + Vector3.up * 5f, new Vector3(5f, 3.5f, 5f));
                }

                if (!any) { crowns = b; any = true; } else { crowns.Encapsulate(b); }
            }

            return any ? crowns : (Bounds?)null;
        }

        private static int ParseCount(string text, string pattern)
        {
            Match m = Regex.Match(text ?? string.Empty, pattern);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
        }
    }
}
