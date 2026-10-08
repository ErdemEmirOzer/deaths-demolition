using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The one world → density-field function, registered by the world package (DD.Art VegetationWorldHookup for the
    /// V6 world) and used by BOTH the runtime (<see cref="VegetationSystem.SetWorld"/>) and the edit-mode preview
    /// (<see cref="VegetationPreview"/>): one code path for every position (probe + S-05 masks + roads + zoner).
    /// </summary>
    public static class VegetationWorldFields
    {
        /// <summary>(world scene, recipe) → field, or null when the scene is not a vegetated world.</summary>
        public static Func<Scene, VegetationRecipe, VegetationDensityField> Builder;

        public static VegetationDensityField Build(Scene world, VegetationRecipe recipe)
        {
            Func<Scene, VegetationRecipe, VegetationDensityField> b = Builder;
            return b != null && world.IsValid() ? b(world, recipe ?? VegetationRecipe.Load()) : null;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// WP-093 · EDITOR-ONLY deterministic preview for S-05's edit-mode TD capture (WorldTdCapture: OpenScene → hidden URP camera
    /// → Camera.Render; "bare ground" metric). <see cref="Populate(Scene, int)"/> builds the field with the same
    /// <see cref="VegetationWorldFields"/> function the game uses, places it with <see cref="VegetationDrawSet.Build"/> (same seed
    /// <see cref="VegetationSystem.Seed"/>, tier scale, kit mesh heights) and draws it for EVERY camera that renders
    /// (beginCameraRendering — no Camera.main / tag filter), with calm wind and no interaction, no Time / registries / DDOL.
    /// Dispose removes the draw hook and every object it made. Called by reflection:
    /// <c>DD.Prototype.World.Vegetation.VegetationPreview.Populate(Scene, int) → IDisposable</c>.
    /// </summary>
    public static class VegetationPreview
    {
        public const int TierPerformance = 0, TierHigh = 1, TierCinematic = 2;

        // Opt-in editor evidence only. Normal previews retain their original draw and allocate no snapshot.
        public sealed class DiagnosticSnapshot
        {
            public VegetationGrid Grid;
            public VegetationDensityField Field;
            public byte[] FinalZone, Clear;
            public bool[] Corridor;
            public VegetationRecipe AppliedRecipe;
            public VegetationLayout AppliedLayout;
            public int AppliedTier, AppliedSeed;
            public VegetationDrawSet AppliedDrawSet;
            public int LastDrawCameraId;
        }

        public static DiagnosticSnapshot LastDiagnostics { get; private set; }
        public static bool DiagnosticDrawEnabled { get; set; } = true;
        private static bool s_diagnosticsRequested;

        public static IDisposable BeginDiagnostics() => new DiagnosticScope();

        private sealed class DiagnosticScope : IDisposable
        {
            private readonly bool _requested = s_diagnosticsRequested, _draw = DiagnosticDrawEnabled;
            private readonly DiagnosticSnapshot _previous = LastDiagnostics;
            public DiagnosticScope() { s_diagnosticsRequested = true; LastDiagnostics = null; }
            public void Dispose()
            {
                s_diagnosticsRequested = _requested;
                DiagnosticDrawEnabled = _draw;
                LastDiagnostics = _previous;
            }
        }

        internal static void RecordDiagnostics(VegetationGrid grid, VegetationDensityField field, byte[] zone, bool[] corridor, byte[] clear)
        {
            if (!s_diagnosticsRequested) return;
            LastDiagnostics = new DiagnosticSnapshot { Grid = grid, Field = field, FinalZone = zone, Corridor = corridor, Clear = clear };
        }

        private static readonly int PushOnId = Shader.PropertyToID("_DD_VegPushOn");

        public sealed class Handle : IDisposable
        {
            private VegetationDrawSet _draw;
            private Action<ScriptableRenderContext, Camera> _hook;

            internal Handle(VegetationDrawSet draw)
            {
                _draw = draw;
                if (draw == null) return;
                _hook = (ctx, cam) =>
                {
                    if (DiagnosticDrawEnabled && _draw != null && cam != null)
                    {
                        _draw.Render(cam, 0);
                        if (s_diagnosticsRequested && LastDiagnostics != null && ReferenceEquals(LastDiagnostics.AppliedDrawSet, _draw))
                            LastDiagnostics.LastDrawCameraId = cam.GetInstanceID();
                    }
                };
                RenderPipelineManager.beginCameraRendering += _hook;
            }

            public VegetationDrawSet DrawSet => _draw;
            public VegetationLayout Layout => _draw != null ? _draw.Layout : null;
            public int TotalInstances => Layout != null ? Layout.TotalInstances : 0;

            public void Dispose()
            {
                if (_hook != null) RenderPipelineManager.beginCameraRendering -= _hook;
                _hook = null;
                _draw?.Dispose();
                _draw = null;
            }
        }

        /// <summary>The world's vegetation as the game places it at <paramref name="qualityTier"/> (0 Performance, 1 High, 2 Cinematic).</summary>
        public static IDisposable Populate(Scene world, int qualityTier)
        {
            VegetationRecipe recipe = VegetationRecipe.Load();
            VegetationDensityField field = VegetationWorldFields.Build(world, recipe);
            if (field == null)
            {
                Debug.LogWarning("[Vegetation] preview: '" + world.name + "' builds no vegetation field (not a V6 world, or no world builder registered).");
            }

            return Populate(field, qualityTier, recipe);
        }

        /// <summary>Preview of a ready field (tests, tools).</summary>
        public static Handle Populate(VegetationDensityField field, int qualityTier, VegetationRecipe recipe = null)
        {
            recipe = recipe != null ? recipe : VegetationRecipe.Load();
            int tier = Mathf.Clamp(qualityTier, TierPerformance, TierCinematic);
            if (field == null) return new Handle(null);
            var draw = new VegetationDrawSet(recipe, VegetationKit.Load());
            draw.Build(field, tier, VegetationSystem.Seed);
            if (s_diagnosticsRequested && LastDiagnostics != null && ReferenceEquals(LastDiagnostics.Field, field))
            {
                LastDiagnostics.AppliedRecipe = recipe;
                LastDiagnostics.AppliedLayout = draw.Layout;
                LastDiagnostics.AppliedDrawSet = draw;
                LastDiagnostics.AppliedTier = tier;
                LastDiagnostics.AppliedSeed = VegetationSystem.Seed;
            }
            // Deterministic stills: calm wind (no lean, no gust tint), no interaction map.
            VegetationWindSettings.Load().ApplyGlobals(tier, false, 0f, 0f);
            Shader.SetGlobalVector(PushOnId, Vector4.zero);
            return new Handle(draw);
        }
    }
#endif
}
