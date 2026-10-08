using System.Collections.Generic;
using DD.Prototype.World;
using DD.Prototype.World.Vegetation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Art
{
    /// <summary>
    /// WP-093 · Binds the living ground to the V6 world (World_V6_Greybox, the default world). The layout knowledge that lives in
    /// DD.Art is gathered here — the greybox "Ground" collider and its path mask (V5_GroundMoss _PathMask / _PathBounds), the plaza
    /// rects (<see cref="Ap005SurfaceTags.V6PlazaRects"/>), the pond surfaces (PondN_Water), the play-area extent
    /// (<see cref="WorldBounds"/> of the scene), the AP-023 family of each collider (SM_&lt;family&gt;_Vnn name parse), the
    /// spawn / weapon points of the placement asset, S-05's masks and roads (MASK-CONTRACT v1).
    ///
    /// <see cref="BuildFor"/> is THE world → field function (<see cref="VegetationWorldFields.Builder"/>): the runtime
    /// (<see cref="Ensure"/> from <see cref="Ap005WorldBootstrap.BuildNow"/>, built in the scene's sceneLoaded) and the editor
    /// preview (<see cref="VegetationPreview"/>, S-05's edit-mode TD capture) both call it, and it reads nothing that exists only
    /// in play mode (no SurfaceTags, no OnEnable-registered volumes, no WorldBounds.Active, no WorldMarkers.Current).
    /// AP005 / procedural worlds: no vegetation (Faz 1 is V6 only).
    /// </summary>
    public static class VegetationWorldHookup
    {
        public const string GroundName = "Ground";
        public const string PathMaskProperty = "_PathMask", PathBoundsProperty = "_PathBounds";

        /// <summary>
        /// The world is static per scene, so the RUNTIME field is built once per session (the menu backdrop usually pays it) and
        /// reused by every run. Keyed by scene name + recipe instance. The preview never uses the cache.
        /// </summary>
        private static readonly Dictionary<string, VegetationDensityField> s_cache = new Dictionary<string, VegetationDensityField>(2);

        public static void Ensure(Ap005WorldBootstrap bootstrap)
        {
            Register();
            if (bootstrap == null || VegetationSystem.DisabledByCommandLine || Ap005WorldBootstrap.LayoutOf(bootstrap.Placement) != WorldLayoutKind.V6) return;
            Scene scene = bootstrap.gameObject.scene;
            VegetationSystem.Ensure().SetWorld(scene, recipe => Cached(scene, recipe));
        }

        private static VegetationDensityField Cached(Scene scene, VegetationRecipe recipe)
        {
            string key = scene.name + "|" + (recipe != null ? recipe.GetInstanceID() : 0);
            if (s_cache.TryGetValue(key, out VegetationDensityField cached) && cached != null) return cached;
            VegetationDensityField field = VegetationWorldFields.Build(scene, recipe);
            if (field != null) s_cache[key] = field;
            return field;
        }

        /// <summary>Drops the cached fields (tools, tests).</summary>
        public static void ClearCache() => s_cache.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cache.Clear();
            Register();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterInEditor() => Register();
#endif

        public static void Register() => VegetationWorldFields.Builder = BuildFor;

        /// <summary>The V6 world's density field, or null when <paramref name="scene"/> holds no V6 world bootstrap.</summary>
        public static VegetationDensityField BuildFor(Scene scene, VegetationRecipe recipe)
        {
            Ap005WorldBootstrap bootstrap = FindBootstrap(scene);
            if (bootstrap == null || Ap005WorldBootstrap.LayoutOf(bootstrap.Placement) != WorldLayoutKind.V6) return null;
            WorldMarkers.MarkerSet markers = bootstrap.Placement != null ? Ap005WorldBootstrap.MarkerSetFrom(bootstrap.Placement) : default;
            return Build(bootstrap.transform, scene, recipe, markers);
        }

        private static Ap005WorldBootstrap FindBootstrap(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Ap005WorldBootstrap b = root.GetComponentInChildren<Ap005WorldBootstrap>(true);
                if (b != null) return b;
            }

            return null;
        }

        /// <summary>SM_palace_kit_V20 → palace_kit (the family Ap005SurfaceTags writes into SurfaceTag.Family at runtime).</summary>
        private static string V6FamilyOf(Transform t) => t != null ? Ap005SurfaceTags.V6Family(t.name) : null;

        private static VegetationDensityField Build(Transform root, Scene scene, VegetationRecipe recipe, WorldMarkers.MarkerSet markers)
        {
            if (root == null) return null;
            var options = new VegetationProbeOptions
            {
                SpawnPoints = markers.SpawnPositions,
                WeaponPoints = markers.WeaponPositions,
                FamilyOf = V6FamilyOf,
                Scene = scene,
            };
            WorldBounds bounds = root.GetComponentInChildren<WorldBounds>(true);
            float half = bounds != null ? bounds.HalfExtent : 55f;
            options.Min = new Vector2(-half, -half);
            options.Max = new Vector2(half, half);
            options.PlazaRects = Ap005SurfaceTags.V6PlazaRects;

            Transform greybox = root.Find(Ap005SurfaceTags.V6GreyboxName);
            Transform ground = greybox != null ? greybox.Find(GroundName) : null;
            if (ground != null)
            {
                Collider c = ground.GetComponent<Collider>();
                if (c != null) options.GroundColliders = new[] { c };
                Renderer r = ground.GetComponent<Renderer>();
                Material m = r != null ? r.sharedMaterial : null;
                if (m != null && m.HasProperty(PathMaskProperty) && m.HasProperty(PathBoundsProperty))
                {
                    options.PathMask = m.GetTexture(PathMaskProperty) as Texture2D;
                    options.PathBounds = m.GetVector(PathBoundsProperty);
                }
            }

            var water = new List<Transform>(4);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.EndsWith("_Water", System.StringComparison.Ordinal)) water.Add(r.transform);
            }

            options.WaterSurfaces = water.ToArray();
            options.Roads = VegetationMaskSet.TryLoadRoads(scene.name);

            VegetationGrid grid = VegetationWorldProbe.Probe(options, recipe);
            VegetationMaskSet masks = VegetationMaskSet.TryLoad(scene.name);
            masks?.ApplyTo(grid);
            Debug.Log("[Vegetation] V6 field: " + grid.Width + "×" + grid.Height + " cells @" + grid.Cell + " m, " + grid.Trees.Count + " trees, "
                      + grid.ClearPoints.Count + " clear points, " + water.Count + " ponds, roads " + (options.Roads != null ? options.Roads.Count.ToString() : "none (path mask)")
                      + ", S-05 masks " + (masks != null ? masks.Version : "none (fallback builder)") + ".");
            return VegetationZoner.Evaluate(grid, recipe);
        }
    }
}
