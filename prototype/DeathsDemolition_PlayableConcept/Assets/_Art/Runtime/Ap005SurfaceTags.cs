using System.Collections.Generic;
using DD.Prototype.Audio;
using DD.Prototype.Feedback;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Art
{
    /// <summary>
    /// WP-021C · ADR-021-1 v2 · Surface tags for the authored world, from data, at scene load.
    ///
    /// The World_AP005 scene holds 2 664 placement instances (PlacementImporter) and none of
    /// them carries a <see cref="SurfaceTag"/>: a footstep or a bullet on the veranda read as
    /// "Default" (stone). Rather than touching the scene file or 40 family prefabs, this maps
    /// the placement FAMILY (the record the importer wrote, index-aligned with the "Instances"
    /// children) to a <see cref="SurfaceKind"/> and adds the tag to each instance root when the
    /// scene loads. One pass, ~2.7 k AddComponent at load, nothing per frame. The same table is
    /// the rule for AP-015P and any future family: add the family here (README §SurfaceTag) and
    /// the world is tagged; an unknown family falls back by role and is counted.
    ///
    /// The ground is one collider, so its Dirt is refined by the placement ROUTES (a stone route
    /// strip = paving) through <see cref="AudioSurfaceHints.Refine"/>.
    /// </summary>
    public static class Ap005SurfaceTags
    {
        /// <summary>Placement family → surface. Gravel/earth = Dirt, bridge/decking = Wood (SurfaceKind has no finer grain; the clips do).</summary>
        public static readonly Dictionary<string, SurfaceKind> FamilySurface = new Dictionary<string, SurfaceKind>
        {
            // ground & paving
            { "ground", SurfaceKind.Dirt }, { "hero_earth", SurfaceKind.Dirt }, { "quiet_paving", SurfaceKind.Stone },
            { "ring_transition", SurfaceKind.Stone }, { "gate_transition", SurfaceKind.Stone }, { "shore_path_module", SurfaceKind.Stone },
            { "curb", SurfaceKind.Stone }, { "planter_boundary", SurfaceKind.Stone },
            // water
            { "pond_water_surface", SurfaceKind.Water }, { "koi_pond_basin", SurfaceKind.Stone }, { "tsukubai", SurfaceKind.Stone },
            // wood: decking, bridge, corridors, timber, bamboo, sheds
            { "bridge_segment", SurfaceKind.Wood }, { "palace_veranda", SurfaceKind.Wood }, { "west_corridor_module", SurfaceKind.Wood },
            { "open_corridor", SurfaceKind.Wood }, { "timber_post", SurfaceKind.Wood }, { "timber_beam", SurfaceKind.Wood },
            { "palace_east_timber", SurfaceKind.Wood }, { "palace_gable_timber", SurfaceKind.Wood }, { "palace_rafters", SurfaceKind.Wood },
            { "palace_screen", SurfaceKind.Wood }, { "palace_clerestory", SurfaceKind.Wood }, { "palace_gable", SurfaceKind.Wood },
            { "palace_return", SurfaceKind.Wood }, { "bamboo_fence", SurfaceKind.Wood }, { "bamboo_stalk", SurfaceKind.Wood },
            { "bamboo_cluster", SurfaceKind.Wood }, { "shed", SurfaceKind.Wood }, { "handcart", SurfaceKind.Wood }, { "barrels", SurfaceKind.Wood },
            { "garden_bench_low_wall_cap", SurfaceKind.Stone },
            // stone / plaster / roof tile
            { "rock_prop", SurfaceKind.Stone }, { "mossrock", SurfaceKind.Stone }, { "stone_lantern", SurfaceKind.Stone },
            { "plaster_wall", SurfaceKind.Stone }, { "low_rise", SurfaceKind.Stone }, { "roof_segment", SurfaceKind.Stone },
            { "palace_roof_bay", SurfaceKind.Stone }, { "small_breakable", SurfaceKind.Stone },
            // vegetation & straw
            { "creeping_groundcover", SurfaceKind.Foliage }, { "greenery_cluster", SurfaceKind.Foliage }, { "understory_shrub", SurfaceKind.Foliage },
            { "sakura_trunk", SurfaceKind.Wood }, { "sakura_foliage", SurfaceKind.Foliage }, { "strawbales", SurfaceKind.Foliage }
        };

        /// <summary>Fallback when a family is not in the table (counted in <see cref="UnknownFamilies"/>).</summary>
        public static SurfaceKind ByRole(string role)
        {
            switch (role)
            {
                case "vegetation": return SurfaceKind.Foliage;
                case "ground": return SurfaceKind.Dirt;
                default: return SurfaceKind.Stone;
            }
        }

        /// <summary>Family with the AP prefix stripped (placement records carry "AP005_palace_screen", the asset "palace_screen").</summary>
        public static string Key(string family)
        {
            if (string.IsNullOrEmpty(family)) return string.Empty;
            int us = family.IndexOf('_');
            return us > 0 && family.Length > 2 && family[0] == 'A' && family[1] == 'P' && char.IsDigit(family[2]) ? family.Substring(us + 1) : family;
        }

        public static SurfaceKind SurfaceFor(WorldPlacementAsset.Instance inst)
        {
            return FamilySurface.TryGetValue(Key(inst.family), out SurfaceKind kind) ? kind : ByRole(inst.role);
        }

        /// <summary>The importer's ground object name (PlacementImporter.GroundName; the editor assembly is not referenced here).</summary>
        public const string GroundStandInName = "Ground (stand-in)";

        /// <summary>Tags added by the last pass. Test / F1 readout.</summary>
        public static int Tagged { get; private set; }
        public static int UnknownFamilies { get; private set; }
        private static readonly HashSet<string> s_unknown = new HashSet<string>();
        private static WorldPlacementAsset s_routes;

        /// <summary>Tags every instance under the bootstrap's Instances node. Idempotent per root (skips instances already tagged).</summary>
        public static int Apply(Ap005WorldBootstrap bootstrap)
        {
            if (bootstrap == null || bootstrap.Placement == null)
            {
                return 0;
            }

            Transform instances = bootstrap.transform.Find(Ap005WorldBootstrap.InstancesName);
            if (instances == null)
            {
                return 0;
            }

            WorldPlacementAsset placement = bootstrap.Placement;
            int n = Mathf.Min(instances.childCount, placement.instances.Count);
            int added = 0;
            for (int i = 0; i < n; i++)
            {
                Transform t = instances.GetChild(i);
                if (t.GetComponent<SurfaceTag>() != null)
                {
                    continue;
                }

                WorldPlacementAsset.Instance inst = placement.instances[i];
                string key = Key(inst.family);
                if (!FamilySurface.ContainsKey(key) && s_unknown.Add(key))
                {
                    UnknownFamilies++;
                }

                t.gameObject.AddComponent<SurfaceTag>().Surface = SurfaceFor(inst);
                added++;
            }

            // The courtyard ground stand-in (PlacementImporter.GroundName): one earth collider under
            // the root, outside Instances. Dirt, refined to Stone inside the paved routes below.
            Transform ground = bootstrap.transform.Find(GroundStandInName);
            if (ground != null && ground.GetComponent<SurfaceTag>() == null)
            {
                ground.gameObject.AddComponent<SurfaceTag>().Surface = SurfaceKind.Dirt;
                added++;
            }

            Tagged += added;
            SurfaceResolver.Invalidate();
            s_routes = placement;
            AudioSurfaceHints.Refine = RefineByRoute;
            RegisterPond(placement);
            return added;
        }

        /// <summary>
        /// WP-048 (Look B): the koi pond's centre from the water-surface instances, for the ambience
        /// director's 3D pond bed. One pond in AP-005 → one centroid; a second cluster (> 25 m apart)
        /// would register separately.
        /// </summary>
        private static void RegisterPond(WorldPlacementAsset placement)
        {
            AudioSurfaceHints.PondCenters.Clear();
            Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                if (Key(inst.family) == "pond_water_surface")
                {
                    sum += inst.position; n++;
                }
            }

            if (n > 0)
            {
                AudioSurfaceHints.PondCenters.Add(sum / n);
            }
        }

        /// <summary>Inside a stone route's strip the ground (Dirt) reads as Stone. Only the ground's own answer is refined.</summary>
        public static SurfaceKind RefineByRoute(Vector3 point, SurfaceKind resolved)
        {
            WorldPlacementAsset placement = s_routes;
            if (placement == null || resolved != SurfaceKind.Dirt)
            {
                return resolved;
            }

            List<WorldPlacementAsset.Route> routes = placement.routes;
            for (int i = 0; i < routes.Count; i++)
            {
                WorldPlacementAsset.Route r = routes[i];
                if (r.surface != "Stone")
                {
                    continue;
                }

                Vector3 ab = r.b - r.a; ab.y = 0f;
                Vector3 ap = point - r.a; ap.y = 0f;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / len2) : 0f;
                Vector3 closest = r.a + ab * t; closest.y = 0f;
                Vector3 p = point; p.y = 0f;
                if ((p - closest).sqrMagnitude <= r.halfWidth * r.halfWidth)
                {
                    return SurfaceKind.Stone;
                }
            }

            return resolved;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Tagged = 0;
            UnknownFamilies = 0;
            s_unknown.Clear();
            s_routes = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                TagScene(SceneManager.GetSceneAt(i));
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TagScene(scene);
        }

        private static void TagScene(Scene scene)
        {
            if (!scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Ap005WorldBootstrap bootstrap = roots[i].GetComponent<Ap005WorldBootstrap>();
                if (bootstrap != null)
                {
                    int added = Apply(bootstrap);
                    if (added > 0)
                    {
                        Debug.Log("[AP005] SurfaceTags: " + added + " instances tagged from placement families (" + UnknownFamilies + " unknown families by role).");
                    }
                }
            }
        }
    }
}
