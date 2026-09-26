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
            { "sakura_trunk", SurfaceKind.Wood }, { "sakura_foliage", SurfaceKind.Foliage }, { "strawbales", SurfaceKind.Foliage },
            // WP-038B · AP-017 Phase 1 derivatives (litter / piles) and world breakables (by their break material: stone / ceramic → Stone, wood / bamboo → Wood, paper → Wood frame, straw → Foliage)
            { "fallen_culm", SurfaceKind.Wood }, { "broken_fence", SurfaceKind.Wood }, { "stone_pile", SurfaceKind.Stone }, { "moss_patch", SurfaceKind.Foliage },
            { "leaf_pile", SurfaceKind.Foliage }, { "fallen_lantern", SurfaceKind.Stone }, { "broken_planter", SurfaceKind.Stone }, { "yard_stack", SurfaceKind.Wood },
            { "shore_debris", SurfaceKind.Wood },
            { "breakable_lantern_stone", SurfaceKind.Stone }, { "breakable_lantern_paper", SurfaceKind.Wood }, { "breakable_planter", SurfaceKind.Stone },
            { "breakable_fence_panel", SurfaceKind.Wood }, { "breakable_strawbale", SurfaceKind.Foliage }, { "breakable_bench", SurfaceKind.Wood },
            { "breakable_sign", SurfaceKind.Wood }, { "breakable_shoji", SurfaceKind.Wood },
            // WP-038I · AP-017 Phase 2: dominant blocking material (cloth/paper props follow their wood frame; ceramic follows stone).
            { "drying_rack_cloth", SurfaceKind.Wood }, { "well_head_tsurube", SurfaceKind.Wood },
            { "ofuda_post", SurfaceKind.Wood }, { "breakable_sake_jar", SurfaceKind.Stone },
            // WP-065C · AP-017 Phase 2b (WP-038L): same rule — ceramic → Stone, bamboo/wood frames → Wood, the koi shoal is pond water, the glass fūrin reads hard (Metal)
            { "kagura_mask_stand", SurfaceKind.Wood }, { "breakable_tea_set", SurfaceKind.Stone }, { "hishaku_ladle_rack", SurfaceKind.Wood },
            { "fish_trap", SurfaceKind.Wood }, { "koi_shoal", SurfaceKind.Water }, { "furin_wind_chime", SurfaceKind.Metal }
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

            // WP-072 · World v5 (V6, the default world since C-00 25.09): no placement instances — the AP-023 kit and the greybox.
            if (bootstrap.Placement.instances.Count == 0 && bootstrap.transform.Find(V6GreyboxName) != null)
            {
                return ApplyV6(bootstrap);
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

                SurfaceTag tag = t.gameObject.AddComponent<SurfaceTag>();
                tag.Surface = SurfaceFor(inst);
                tag.Family = inst.family;   // WP-047 (S-07): the family handle for effects (lantern flames); the instance name is AP005_iNNNN
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
            RegisterWindChimes(placement);
            RegisterBambooGroves(placement);
            AudioSurfaceHints.FootstepVariant = FootstepVariantFor;   // WP-058B: AP-015P plate variants
            return added;
        }

        // ---- WP-058B · AP-015P plates → footstep variants (WP-038 tags plates Dirt / Stone / Water only)

        private static readonly Dictionary<int, AudioSoundId> s_footVariant = new Dictionary<int, AudioSoundId>(256);

        /// <summary>
        /// Plate id (the SurfaceTag owner's name, ArtSurfaceImporter) → finer footstep: paving plates = cut-stone
        /// pavement, the centre ground plate = the gravel courtyard, water surfaces = shallow water (deep water waits
        /// for a wading-depth signal). Anything else → None (the kind's default clip). Cached per collider.
        /// </summary>
        public static AudioSoundId FootstepVariantFor(Collider collider, Vector3 point, SurfaceKind kind)
        {
            if (collider == null) return AudioSoundId.None;
            int key = collider.GetInstanceID();
            if (s_footVariant.TryGetValue(key, out AudioSoundId cached)) return cached;
            SurfaceTag tag = collider.GetComponentInParent<SurfaceTag>();
            AudioSoundId v = FootstepVariantForName(tag != null ? tag.gameObject.name : collider.gameObject.name, kind);
            s_footVariant[key] = v;
            return v;
        }

        /// <summary>Name rule (public for the EditMode test).</summary>
        public static AudioSoundId FootstepVariantForName(string plateName, SurfaceKind kind)
        {
            if (string.IsNullOrEmpty(plateName)) return AudioSoundId.None;
            if (plateName.StartsWith("AP015_paving", System.StringComparison.Ordinal)) return AudioSoundId.FootstepPavement;
            if (plateName.StartsWith("AP015_ground_center", System.StringComparison.Ordinal)) return AudioSoundId.FootstepGravel;
            if (kind == SurfaceKind.Water && plateName.StartsWith("AP015_", System.StringComparison.Ordinal)) return AudioSoundId.FootstepWaterShallow;
            return AudioSoundId.None;
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

        /// <summary>WP-065C: every AP-017 fūrin (hung under the eaves) → the ambience's wind-chime points.</summary>
        /// <summary>
        /// WP-072 · DEC-102: bamboo grove centres for the ambience's grove bed and gusts — the bamboo stalk / cluster
        /// instances clustered greedily within 10 m; a grove needs ≥ 4 plants (a lone fence post is not a grove).
        /// </summary>
        private static void RegisterBambooGroves(WorldPlacementAsset placement)
        {
            AudioSurfaceHints.BambooGroves.Clear();
            var sums = new System.Collections.Generic.List<Vector3>(16);
            var counts = new System.Collections.Generic.List<int>(16);
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                string key = Key(inst.family);
                if (key != "bamboo_stalk" && key != "bamboo_cluster") continue;
                int hit = -1;
                for (int g = 0; g < sums.Count; g++)
                {
                    Vector3 c = sums[g] / counts[g];
                    if ((new Vector3(c.x - inst.position.x, 0f, c.z - inst.position.z)).sqrMagnitude <= 100f) { hit = g; break; }
                }

                if (hit < 0) { sums.Add(inst.position); counts.Add(1); }
                else { sums[hit] += inst.position; counts[hit]++; }
            }

            for (int g = 0; g < sums.Count; g++)
            {
                if (counts[g] >= 4) AudioSurfaceHints.BambooGroves.Add(sums[g] / counts[g]);
            }
        }

        private static void RegisterWindChimes(WorldPlacementAsset placement)
        {
            AudioSurfaceHints.WindChimes.Clear();
            for (int i = 0; i < placement.instances.Count; i++)
            {
                WorldPlacementAsset.Instance inst = placement.instances[i];
                if (Key(inst.family) == FurinFamily)
                {
                    AudioSurfaceHints.WindChimes.Add(inst.position);
                }
            }
        }

        public const string FurinFamily = "furin_wind_chime";

        // ---- WP-072 · World v5 on the v6 layout (World_V6_Greybox: GreyboxV6Builder + WorldV5Dress + WorldBoundaryBuilder)

        /// <summary>GreyboxV6Builder.GreyboxRootName / WorldV5Dress.RoofOccluderName / WorldBoundaryBuilder names (editor assembly, not referenced here).</summary>
        public const string V6GreyboxName = "Greybox v6", V6RoofOccluderName = "RoofOccluder", V6BoundaryName = "World Boundary (WP-038M)", V6PondRimsName = "Pond Rims";

        /// <summary>WorldV5Ground.PlazaRects (world x0, z0, x1, z1): the wet slate plaza — the ground reads Stone inside, Dirt outside.</summary>
        public static readonly Vector4[] V6PlazaRects =
        {
            new Vector4(-16f, -14f, 16f, 19.7f),
            new Vector4(-4f, -18f, 4f, -14f),
            new Vector4(-4f, 19.7f, 4f, 20.3f),
            new Vector4(-25.7f, 20.3f, 25.7f, 37.5f),
            new Vector4(-25.7f, 37.5f, -18f, 50.5f),
            new Vector4(18f, 37.5f, 25.7f, 50.5f),
        };

        /// <summary>AP-023 kit family (SM_&lt;family&gt;_Vnn) → surface. Timber buildings, fences and trees = Wood; kerbs, rocks, lanterns, plaster walls = Stone.</summary>
        public static readonly Dictionary<string, SurfaceKind> V6FamilySurface = new Dictionary<string, SurfaceKind>
        {
            { "palace_kit", SurfaceKind.Wood }, { "kairo_open", SurfaceKind.Wood }, { "kairo_bay", SurfaceKind.Wood }, { "yagura", SurfaceKind.Wood },
            { "azumaya", SurfaceKind.Wood }, { "teahouse", SurfaceKind.Wood }, { "kura", SurfaceKind.Wood }, { "pavilion", SurfaceKind.Wood },
            { "gates", SurfaceKind.Wood }, { "plank_bridge", SurfaceKind.Wood }, { "fence_kenninji", SurfaceKind.Wood }, { "fence_yotsume", SurfaceKind.Wood },
            { "bamboo_v2", SurfaceKind.Wood }, { "sugi", SurfaceKind.Wood }, { "black_pine", SurfaceKind.Wood }, { "sakura_v2", SurfaceKind.Wood },
            { "momiji", SurfaceKind.Wood }, { "kairo_angle", SurfaceKind.Wood }, { "chumon", SurfaceKind.Wood },   // v007-B: angled kairō corners, the chūmon
            { "wall_kit", SurfaceKind.Stone }, { "granite_kerb", SurfaceKind.Stone }, { "planter", SurfaceKind.Stone }, { "lantern_kasuga", SurfaceKind.Stone },
            { "rock_large", SurfaceKind.Stone }, { "pond_rock", SurfaceKind.Stone }, { "slab_bridge", SurfaceKind.Stone }, { "pond_shore_pebble", SurfaceKind.Stone },
            { "pond_shore_curve", SurfaceKind.Stone }, { "waterfall_rock", SurfaceKind.Stone },
            { "pond_shore_moss", SurfaceKind.Foliage }, { "groundcover", SurfaceKind.Foliage }, { "satsuki", SurfaceKind.Foliage }, { "lotus", SurfaceKind.Foliage },
            { "pond_reeds", SurfaceKind.Foliage },
        };

        /// <summary>Greybox box material (Greybox_&lt;key&gt;) → surface, for the greybox boxes the dress keeps as collision.</summary>
        public static SurfaceKind V6GreyboxSurface(string materialKey, string name)
        {
            switch (materialKey)
            {
                case "ground": return SurfaceKind.Dirt;
                case "floor": return name.StartsWith("Planter_", System.StringComparison.Ordinal) ? SurfaceKind.Stone : SurfaceKind.Wood;
                case "tree": return SurfaceKind.Wood;
                case "water": return SurfaceKind.Water;
                case "wall": case "roof": case "prop": return SurfaceKind.Stone;
                default: return V6ByName(name);
            }
        }

        private static SurfaceKind V6ByName(string name)
        {
            if (name.IndexOf("Floor", System.StringComparison.Ordinal) >= 0 || name.IndexOf("Stair", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("Deck", System.StringComparison.Ordinal) >= 0 || name.StartsWith("Bamboo_", System.StringComparison.Ordinal)) return SurfaceKind.Wood;
            if (name == "Ground") return SurfaceKind.Dirt;
            return SurfaceKind.Stone;   // roofs (tile), walls (plaster), everything else hard
        }

        /// <summary>SM_palace_kit_V20 → palace_kit.</summary>
        public static string V6Family(string name)
        {
            if (!name.StartsWith("SM_", System.StringComparison.Ordinal)) return null;
            int v = name.LastIndexOf("_V", System.StringComparison.Ordinal);
            return v > 3 ? name.Substring(3, v - 3) : name.Substring(3);
        }

        public static bool InsideV6Plaza(Vector3 p)
        {
            for (int i = 0; i < V6PlazaRects.Length; i++)
            {
                Vector4 r = V6PlazaRects[i];
                if (p.x > r.x && p.x < r.z && p.z > r.y && p.z < r.w) return true;
            }

            return false;
        }

        public static SurfaceKind RefineV6(Vector3 point, SurfaceKind resolved)
        {
            return resolved == SurfaceKind.Dirt && InsideV6Plaza(point) ? SurfaceKind.Stone : resolved;
        }

        private static readonly List<Collider> s_colliders = new List<Collider>(4096);

        private static int ApplyV6(Ap005WorldBootstrap bootstrap)
        {
            Transform root = bootstrap.transform;
            int added = 0;

            // 1 · every kit instance root (SM_<family>_Vnn) by family; its colliders sit under it.
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                string family = V6Family(t.name);
                if (family != null)
                {
                    if (t.GetComponent<SurfaceTag>() == null)
                    {
                        if (!V6FamilySurface.TryGetValue(family, out SurfaceKind kind))
                        {
                            kind = SurfaceKind.Stone;
                            if (s_unknown.Add(family)) UnknownFamilies++;
                        }

                        SurfaceTag tag = t.gameObject.AddComponent<SurfaceTag>();
                        tag.Surface = kind;
                        tag.Family = family;
                        added++;

                        // WP-073 (C-00 26.09): a tile roof inside a timber kit piece (SM_gates' Roof/RoofOccluder) is tile, not
                        // the frame's wood — its own tag wins (the nearest SurfaceTag up the parents). Load-time only.
                        foreach (Collider roof in t.GetComponentsInChildren<Collider>(true))
                        {
                            if (roof.name != V6RoofOccluderName || roof.GetComponent<SurfaceTag>() != null) continue;
                            roof.gameObject.AddComponent<SurfaceTag>().Surface = SurfaceKind.Stone;
                            added++;
                        }
                    }

                    continue;
                }

                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }

            // 2 · the colliders left untagged: greybox boxes (by their Greybox_<key> material), the dress's own pieces (hall floor, roofs).
            // The world boundary (invisible walls, pond rims) stays untagged.
            Transform boundary = root.Find(V6BoundaryName);
            root.GetComponentsInChildren(true, s_colliders);
            for (int i = 0; i < s_colliders.Count; i++)
            {
                Collider c = s_colliders[i];
                if (c.isTrigger || (boundary != null && c.transform.IsChildOf(boundary)) || c.GetComponentInParent<SurfaceTag>() != null) continue;
                var renderer = c.GetComponent<Renderer>();
                string mat = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
                string key = mat.StartsWith("Greybox_", System.StringComparison.Ordinal) ? mat.Substring(8) : "";
                c.gameObject.AddComponent<SurfaceTag>().Surface = V6GreyboxSurface(key, c.name);
                added++;
            }

            Tagged += added;
            SurfaceResolver.Invalidate();
            s_routes = null;
            AudioSurfaceHints.Refine = RefineV6;
            AudioSurfaceHints.FootstepVariant = null;
            RegisterV6Hints(root, boundary);
            s_colliders.Clear();
            return added;
        }

        /// <summary>The ambience anchors on V6: bamboo groves (the grove band's greybox blocks), eave points (roof edges), pond centres (rim rings).</summary>
        private static void RegisterV6Hints(Transform root, Transform boundary)
        {
            AudioSurfaceHints.WindChimes.Clear();   // no fūrin in World v5
            AudioSurfaceHints.EavePoints.Clear();
            var grove = new List<Vector3>(128);
            var rims = new List<Vector3>(256);
            Transform pondRims = boundary != null ? boundary.Find(V6PondRimsName) : null;
            for (int i = 0; i < s_colliders.Count; i++)
            {
                Collider c = s_colliders[i];
                if (c.name.StartsWith("Bamboo_", System.StringComparison.Ordinal))
                {
                    grove.Add(c.bounds.center);
                }
                else if (c.name == V6RoofOccluderName || c.name == "Hall_Roof")
                {
                    AddEaves(c.bounds);
                }
                else if (pondRims != null && c.transform.IsChildOf(pondRims))
                {
                    rims.Add(c.bounds.center);
                }
            }

            Cluster(grove, 10f, 4, AudioSurfaceHints.BambooGroves);
            Cluster(rims, 25f, 4, AudioSurfaceHints.PondCenters);
        }

        /// <summary>A roof's eave line: its footprint's edge every ~3 m at the eave height.</summary>
        private static void AddEaves(Bounds b)
        {
            float y = b.min.y;
            Vector3 lo = b.min, hi = b.max;
            int nx = Mathf.Max(1, Mathf.RoundToInt((hi.x - lo.x) / 3f)), nz = Mathf.Max(1, Mathf.RoundToInt((hi.z - lo.z) / 3f));
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(lo.x, hi.x, i / (float)nx);
                AudioSurfaceHints.EavePoints.Add(new Vector3(x, y, lo.z));
                AudioSurfaceHints.EavePoints.Add(new Vector3(x, y, hi.z));
            }

            for (int i = 1; i < nz; i++)
            {
                float z = Mathf.Lerp(lo.z, hi.z, i / (float)nz);
                AudioSurfaceHints.EavePoints.Add(new Vector3(lo.x, y, z));
                AudioSurfaceHints.EavePoints.Add(new Vector3(hi.x, y, z));
            }
        }

        /// <summary>Greedy XZ clustering (as the AP005 groves): a point joins the first centre within <paramref name="radius"/>; centres of ≥ <paramref name="min"/> points.</summary>
        private static void Cluster(List<Vector3> points, float radius, int min, List<Vector3> into)
        {
            into.Clear();
            var sums = new List<Vector3>(16);
            var counts = new List<int>(16);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                int hit = -1;
                for (int g = 0; g < sums.Count; g++)
                {
                    Vector3 c = sums[g] / counts[g];
                    if (new Vector3(c.x - p.x, 0f, c.z - p.z).sqrMagnitude <= radius * radius) { hit = g; break; }
                }

                if (hit < 0) { sums.Add(p); counts.Add(1); }
                else { sums[hit] += p; counts[hit]++; }
            }

            for (int g = 0; g < sums.Count; g++)
            {
                if (counts[g] >= min) into.Add(sums[g] / counts[g]);
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
            s_footVariant.Clear();
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
                        Debug.Log("[AP005] SurfaceTags: " + added + " instances tagged from placement families (" + UnknownFamilies + " unknown families by role)."
                                  + (bootstrap.Placement != null && bootstrap.Placement.instances.Count == 0
                                      ? " V6: groves " + AudioSurfaceHints.BambooGroves.Count + ", eave points " + AudioSurfaceHints.EavePoints.Count + ", ponds " + AudioSurfaceHints.PondCenters.Count
                                      : ""));
                    }
                }
            }
        }
    }
}
