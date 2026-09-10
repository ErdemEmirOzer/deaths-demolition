using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// One designer-placed spawn point (REQ-065). Pure data, so the layout exists and can
    /// be reasoned about before the map does.
    ///
    /// HOW G-06 BINDS THE MAP TO THIS
    /// ------------------------------
    /// Every point has a stable <see cref="Id"/>. The map task drops a
    /// <see cref="SpawnPointMarker"/> on an empty GameObject, types the same Id, and the
    /// marker's world position replaces <see cref="Position"/> at runtime. Nothing else
    /// has to change: weights, regions and notes stay in the layout. Ids that never get a
    /// marker keep their authored coordinates, so the system also runs on a bare scene.
    /// </summary>
    [Serializable]
    public sealed class SpawnPointDefinition
    {
        [Tooltip("Stable identifier. A SpawnPointMarker with the same Id overrides the position.")]
        public string Id = "";

        [Tooltip("Ring region this point belongs to. Drives the region and heading weights.")]
        public RingRegion Region = RingRegion.North;

        [PrototypeOnly(PendingDecisions.None,
            "Authored position for a 90x90 m blockout centred on the origin. The map (G-06) " +
            "replaces it through a SpawnPointMarker with the same Id.", true)]
        public Vector3 Position = Vector3.zero;

        [PrototypeOnly(PendingDecisions.None,
            "Designer base weight - the first of the nine spawn weighting inputs.", true)]
        [Range(0f, 4f)] public float BaseWeight = 1f;

        [Tooltip("Point can be selected at all. A disabled point keeps its runtime state.")]
        public bool Enabled = true;

        [Tooltip("Spawn facing the courtyard. Uncheck to use FacingYaw instead.")]
        public bool FaceMapCenter = true;

        [Tooltip("Yaw in degrees when FaceMapCenter is off. 0 = +Z (north).")]
        public float FacingYaw;

        [Tooltip("Landmark note for the map task. Not used at runtime.")]
        [TextArea(1, 3)] public string Notes = "";

        public SpawnPointDefinition Clone()
        {
            return new SpawnPointDefinition
            {
                Id = Id,
                Region = Region,
                Position = Position,
                BaseWeight = BaseWeight,
                Enabled = Enabled,
                FaceMapCenter = FaceMapCenter,
                FacingYaw = FacingYaw,
                Notes = Notes
            };
        }
    }
}
