using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Rendering
{
    /// <summary>
    /// FINAL c4093280 perf (C-00 10.10) · High top-down only: the world's small shadow casters stop casting. TD High read 9.6 ms
    /// (budget 8.0) after the 09.10 world v6 + living-ground merges took the window's draws 1950 → 2625 (bisect d69e4984 →
    /// 1b9c0fa8); every caster inside the shadow distance is drawn again per cascade. From the TD camera (15 m up) the shadow of
    /// an object under <see cref="MaxDiagonal"/> m (pebbles, small stones, buckets, bowls, small props) is a few pixels.
    /// Collected once per world scene (the scene of <see cref="WorldBakedProbes.Current"/>, MeshRenderers casting On, bounds
    /// diagonal &lt; <see cref="MaxDiagonal"/>, or lower than <see cref="MaxFlatHeight"/>: a piece lying on the ground); switched on <see cref="QualityModeApplier"/>'s events (tier, camera mode, scene),
    /// never per frame. Any other tier or mode puts them back On — Cinematic, Performance, TP and FP are unchanged.
    /// </summary>
    public static class SmallShadowCasters
    {
        /// <summary>World-space bounds diagonal (m) under which a world renderer counts as small.</summary>
        public const float MaxDiagonal = 1.5f;

        /// <summary>
        /// Step 2 (S-01 static census 10.10: Layout v5 added +674 shadow-casting slots, ~390 of them flat ground pieces — ishidatami,
        /// drain_channel, pond_stepstone, boardwalk, Ground_Stones, pond_shore_*): a renderer whose world bounds are lower than this
        /// (m) lies on the ground; its shadow is invisible from the TD camera whatever its footprint.
        /// </summary>
        public const float MaxFlatHeight = 0.25f;

        private static readonly List<MeshRenderer> s_renderers = new List<MeshRenderer>(2048);
        private static Scene s_scene;
        private static bool s_off;

        /// <summary>Small casters collected in the current world scene (readout).</summary>
        public static int Count => s_renderers.Count;

        /// <summary>True while they are switched off (High top-down).</summary>
        public static bool Off => s_off;

        public static void Apply(QualityTier tier, CameraMode mode)
        {
            bool want = tier == QualityTier.Yuksek && mode == CameraMode.TopDown;
            WorldBakedProbes world = WorldBakedProbes.Current;
            Scene scene = world != null ? world.gameObject.scene : default;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                s_renderers.Clear();
                s_off = false;
                s_scene = default;
                return;
            }

            if (scene != s_scene)
            {
                s_renderers.Clear();
                s_off = false;
                s_scene = scene;
                Collect(scene);
            }

            if (want == s_off) return;
            ShadowCastingMode value = want ? ShadowCastingMode.Off : ShadowCastingMode.On;
            for (int i = 0; i < s_renderers.Count; i++)
            {
                MeshRenderer r = s_renderers[i];
                if (r != null) r.shadowCastingMode = value;
            }

            s_off = want;
        }

        private static void Collect(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();   // once per world scene
            float max2 = MaxDiagonal * MaxDiagonal;
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (MeshRenderer r in roots[i].GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.shadowCastingMode != ShadowCastingMode.On) continue;
                    Vector3 size = r.bounds.size;
                    if (size.sqrMagnitude >= max2 && size.y >= MaxFlatHeight) continue;
                    s_renderers.Add(r);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_renderers.Clear();
            s_scene = default;
            s_off = false;
        }
    }
}
