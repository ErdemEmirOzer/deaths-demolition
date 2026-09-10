using DD.Prototype.Core;
using DD.Prototype.Feedback;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.World
{
    /// <summary>
    /// Turns a finished <see cref="MeshBuilder"/> into a scene object with the right
    /// layer, collider and surface tag.
    ///
    /// This is the single place where the three cross-system contracts of the world task
    /// are honoured, so none of them can be forgotten on a piece:
    ///
    ///   layer      Environment for anything solid, CameraOccluder for anything big
    ///              enough to get between the camera and the player (REQ-035, G-02's
    ///              CameraOcclusionFader only ever looks at that layer);
    ///   collider   a MeshCollider, which is what NavMeshSurface voxelises (G-05) and
    ///              what the aim raycast and bullets hit;
    ///   SurfaceTag the impact sound and impact burst G-04 plays when something is shot.
    ///
    /// Anything created with <see cref="OccluderKind.Occluder"/> is deliberately its own
    /// GameObject: fading is per object, so a grove split into six clusters lets one
    /// cluster disappear while the rest stays solid.
    /// </summary>
    public static class WorldPiece
    {
        public enum Solidity
        {
            /// <summary>Visible, no collider. Water, painted detail, ground decals.</summary>
            Visual = 0,

            /// <summary>Visible and solid, on the Environment layer.</summary>
            Solid = 1,

            /// <summary>Visible, solid, and allowed to fade for the camera.</summary>
            Occluder = 2,

            /// <summary>Solid but invisible. Pond barrier, map edge backstop.</summary>
            Invisible = 3
        }

        /// <summary>
        /// Builds the object. Returns null when the builder produced nothing, so callers
        /// can emit optimistically and let empty groups vanish.
        /// </summary>
        public static GameObject Create(Transform parent, string name, MeshBuilder builder,
            Material material, Solidity solidity, SurfaceKind surface)
        {
            if (builder == null || builder.IsEmpty)
            {
                return null;
            }

            Mesh mesh = builder.Build(name);
            if (mesh == null)
            {
                return null;
            }

            return Create(parent, name, mesh, material, solidity, surface);
        }

        public static GameObject Create(Transform parent, string name, Mesh mesh,
            Material material, Solidity solidity, SurfaceKind surface)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            if (solidity != Solidity.Invisible)
            {
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                // A blockout with every piece casting shadows turns to mush under an
                // overcast key light. Only pieces with real volume cast; ground paint and
                // water only receive. This is also most of the shadow cost saved.
                renderer.shadowCastingMode = solidity == Solidity.Visual
                    ? ShadowCastingMode.Off
                    : ShadowCastingMode.On;

                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }

            if (solidity != Solidity.Visual)
            {
                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
            }

            int layer = solidity == Solidity.Occluder
                ? PrototypeLayers.CameraOccluder
                : PrototypeLayers.Environment;

            if (layer >= 0)
            {
                go.layer = layer;
            }

            if (surface != SurfaceKind.Default && solidity != Solidity.Visual)
            {
                go.AddComponent<SurfaceTag>().Surface = surface;
            }

            return go;
        }

        /// <summary>An empty parent, used to group a region's pieces in the hierarchy.</summary>
        public static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
