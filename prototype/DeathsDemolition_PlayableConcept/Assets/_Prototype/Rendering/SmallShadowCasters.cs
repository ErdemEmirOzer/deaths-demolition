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
    /// FINAL c4093280 perf (C-00 10.10) · Shadow casters the top-down camera cannot see the shadow of. TD High read 9.6 ms (budget
    /// 8.0) after the 09.10 world v6 + living-ground merges took the window's draws 1950 → 2625 (bisect d69e4984 → 1b9c0fa8); S-01's
    /// static census (K/wp-099/evidence/perf675-1010/PERF675-STATIK.md): Layout v5 added +674 shadow-casting LOD0 slots, ~390 of them
    /// flat ground pieces (ishidatami, drain_channel, pond_stepstone, Ground_Stones, boardwalk, pond_shore_*). Every caster inside
    /// the shadow distance is drawn again per cascade.
    /// Two sets, collected once per world scene (the scene of <see cref="WorldBakedProbes.Current"/>, MeshRenderers casting On):
    /// · flat — world bounds lower than <see cref="MaxFlatHeight"/>, or under a pond_shore piece: a piece lying on the ground, its
    ///   shadow invisible from above → no shadow in top-down at every tier (C-00 10.10: no visible change);
    /// · small — bounds diagonal under <see cref="MaxDiagonal"/> (pebbles, small stones, props) → no shadow in High top-down.
    /// Switched on <see cref="QualityModeApplier"/>'s events (tier, camera mode, scene), never per frame; TP and FP keep every
    /// shadow (top-down only, user 06.10), and leaving top-down puts them back On.
    /// </summary>
    public static class SmallShadowCasters
    {
        /// <summary>World-space bounds diagonal (m) under which a world renderer counts as small.</summary>
        public const float MaxDiagonal = 1.5f;

        /// <summary>World bounds height (m) under which a world renderer lies on the ground.</summary>
        public const float MaxFlatHeight = 0.25f;

        /// <summary>Name fragment of the pond shore modules (SM_pond_shore_*), flat in use whatever their bounds.</summary>
        public const string PondShoreName = "pond_shore";

        private static readonly List<MeshRenderer> s_small = new List<MeshRenderer>(2048);
        private static readonly List<MeshRenderer> s_flat = new List<MeshRenderer>(1024);
        private static Scene s_scene;
        private static bool s_smallOff, s_flatOff;

        /// <summary>Small casters collected in the current world scene (readout).</summary>
        public static int Count => s_small.Count;

        /// <summary>Flat ground casters collected in the current world scene (readout).</summary>
        public static int FlatCount => s_flat.Count;

        /// <summary>True while the small casters are switched off (High top-down).</summary>
        public static bool Off => s_smallOff;

        /// <summary>True while the flat casters are switched off (top-down).</summary>
        public static bool FlatOff => s_flatOff;

        public static void Apply(QualityTier tier, CameraMode mode)
        {
            WorldBakedProbes world = WorldBakedProbes.Current;
            Scene scene = world != null ? world.gameObject.scene : default;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                s_small.Clear();
                s_flat.Clear();
                s_smallOff = s_flatOff = false;
                s_scene = default;
                return;
            }

            if (scene != s_scene)
            {
                s_small.Clear();
                s_flat.Clear();
                s_smallOff = s_flatOff = false;
                s_scene = scene;
                Collect(scene);
            }

            bool topDown = mode == CameraMode.TopDown;
            Switch(s_flat, topDown, ref s_flatOff);
            Switch(s_small, topDown && tier == QualityTier.Yuksek, ref s_smallOff);
        }

        private static void Switch(List<MeshRenderer> set, bool off, ref bool state)
        {
            if (off == state) return;
            ShadowCastingMode value = off ? ShadowCastingMode.Off : ShadowCastingMode.On;
            for (int i = 0; i < set.Count; i++)
            {
                MeshRenderer r = set[i];
                if (r != null) r.shadowCastingMode = value;
            }

            state = off;
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
                    if (size.y < MaxFlatHeight || UnderPondShore(r.transform)) s_flat.Add(r);
                    else if (size.sqrMagnitude < max2) s_small.Add(r);
                }
            }
        }

        private static bool UnderPondShore(Transform t)
        {
            for (int up = 0; up < 4 && t != null; up++, t = t.parent)
            {
                if (t.name.IndexOf(PondShoreName, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_small.Clear();
            s_flat.Clear();
            s_scene = default;
            s_smallOff = s_flatOff = false;
        }
    }
}
