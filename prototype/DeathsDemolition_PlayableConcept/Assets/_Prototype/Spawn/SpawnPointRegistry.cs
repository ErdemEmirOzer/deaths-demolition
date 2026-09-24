using System.Collections.Generic;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// Holds the runtime spawn points and merges the two sources of truth:
    ///
    ///   1. the authored layout (<see cref="DefaultSpawnPointLayout"/> or a
    ///      <see cref="SpawnPointLayoutAsset"/>) - always present, map-independent;
    ///   2. <see cref="SpawnPointMarker"/> components placed in the map scene by task
    ///      G-06 - they override the position of the Id they name.
    ///
    /// Markers may register before or after the registry is built; early ones are queued
    /// and applied on build. A marker whose Id is not in the layout is appended as a new
    /// point, so G-06 can add a thirteenth entrance without touching this code.
    /// </summary>
    public static class SpawnPointRegistry
    {
        private static readonly List<SpawnPointDefinition> s_definitions = new List<SpawnPointDefinition>(16);
        private static readonly List<SpawnPointRuntime> s_points = new List<SpawnPointRuntime>(16);
        private static readonly List<SpawnPointMarker> s_pendingMarkers = new List<SpawnPointMarker>(16);
        private static bool s_built;

        /// <summary>Runtime points in layout order. Never null, never reordered mid-run.</summary>
        public static IReadOnlyList<SpawnPointRuntime> Points => s_points;

        public static int Count => s_points.Count;

        public static bool IsBuilt => s_built;

        /// <summary>How many points the map has actually bound with a marker.</summary>
        public static int MarkerBoundCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < s_points.Count; i++)
                {
                    if (s_points[i].HasMarker)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Builds once. Pass null to use the authored default layout.</summary>
        public static void EnsureBuilt(SpawnPointLayoutAsset layout)
        {
            if (s_built)
            {
                return;
            }

            Rebuild(layout);
        }

        /// <summary>Rebuilds from the given layout and re-applies every live marker.</summary>
        public static void Rebuild(SpawnPointLayoutAsset layout)
        {
            s_definitions.Clear();
            s_points.Clear();

            if (layout != null && layout.Count > 0)
            {
                IReadOnlyList<SpawnPointDefinition> source = layout.Points;
                for (int i = 0; i < source.Count; i++)
                {
                    if (source[i] != null && !string.IsNullOrEmpty(source[i].Id))
                    {
                        s_definitions.Add(source[i].Clone());
                    }
                }
            }

            if (s_definitions.Count == 0)
            {
                DefaultSpawnPointLayout.Fill(s_definitions);
            }

            for (int i = 0; i < s_definitions.Count; i++)
            {
                s_points.Add(new SpawnPointRuntime(i, s_definitions[i]));
            }

            s_built = true;

            for (int i = 0; i < s_pendingMarkers.Count; i++)
            {
                Apply(s_pendingMarkers[i]);
            }
        }

        /// <summary>Called by a marker in OnEnable.</summary>
        public static void RegisterMarker(SpawnPointMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            if (!s_pendingMarkers.Contains(marker))
            {
                s_pendingMarkers.Add(marker);
            }

            if (s_built)
            {
                Apply(marker);
            }
        }

        /// <summary>Called by a marker in OnDisable.</summary>
        public static void UnregisterMarker(SpawnPointMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            s_pendingMarkers.Remove(marker);
        }

        public static SpawnPointRuntime FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < s_points.Count; i++)
            {
                if (string.Equals(s_points[i].Id, id, System.StringComparison.Ordinal))
                {
                    return s_points[i];
                }
            }

            return null;
        }

        /// <summary>Clears per-round state on every point. Called at BeginRound.</summary>
        public static void ResetRuntimeState()
        {
            for (int i = 0; i < s_points.Count; i++)
            {
                s_points[i].ResetForRound();
            }
        }

        private static void Apply(SpawnPointMarker marker)
        {
            if (marker == null || string.IsNullOrEmpty(marker.PointId))
            {
                return;
            }

            SpawnPointRuntime point = FindById(marker.PointId);

            if (point == null)
            {
                // A marker the layout does not know about: accept it as a new point rather
                // than dropping it silently.
                var definition = new SpawnPointDefinition
                {
                    Id = marker.PointId,
                    Region = marker.Region,
                    Position = marker.transform.position,
                    BaseWeight = marker.BaseWeightOverride >= 0f ? marker.BaseWeightOverride : 1f,
                    Enabled = marker.PointEnabled,
                    FaceMapCenter = true,
                    Notes = "Added at runtime by a scene marker.",
                    Kind = marker.Kind   // WP-076 B6: the v6 inner gates arrive as scene markers
                };

                s_definitions.Add(definition);
                point = new SpawnPointRuntime(s_points.Count, definition);
                s_points.Add(point);

                NavigationDiagnostics.WarnThrottled(
                    "spawn.marker.unknown", 5f,
                    "[Spawn] SpawnPointMarker '" + marker.PointId +
                    "' is not part of the authored layout. It was added as an extra point.");
            }

            point.ApplyMarker(
                marker.PointId,
                marker.transform.position,
                marker.Region,
                marker.OverrideRegion,
                marker.BaseWeightOverride,
                marker.PointEnabled);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_definitions.Clear();
            s_points.Clear();
            s_pendingMarkers.Clear();
            s_built = false;
        }
    }
}
