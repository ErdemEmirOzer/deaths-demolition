using DD.Prototype.Core;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art
{
    /// <summary>
    /// WP-011 · The stand-in ground of the pilot scene.
    ///
    /// The golden slice is the north-east quarter of the map (x −14..46, z −15..42) and
    /// fourteen of the gameplay markers lie outside it (brief B-5). A flat 96×96 m quad
    /// at y = 0 gives every marker, the NavMesh bake and the player a floor, so the
    /// spawn/weapon parity check (12/12 + 12/12) runs against the same volume Run.unity
    /// uses. It is built with the prototype's <see cref="MeshBuilder"/>, which makes it
    /// readable - the runtime NavMesh voxeliser needs that, as WorldNavigation explains.
    ///
    /// Under it sits <c>GoldenSlice_Ground_COLLIDER</c>: the AP-001 ground collider of
    /// the slice itself (a 2-triangle quad), so the slice keeps its own authored
    /// collision footprint even though the stand-in already covers it.
    /// </summary>
    public static class PilotGround
    {
        public const float Size = 96f;

        /// <summary>One repeat of the earth texture per this many metres.</summary>
        public const float TileMetres = 4f;

        public const string GroundName = "Pilot Ground";
        public const string SourceColliderName = "GoldenSlice_Ground_COLLIDER";

        /// <summary>
        /// Rotation applied to any Mesh sub-asset imported under the Import Contract so
        /// it lands in contract space: the importer delivers (bx, by, −bz) for a Blender
        /// vertex (bx, by, bz) and the contract wants (bx, bz, by) = Rx(+90°)·mesh
        /// (measured, study2). Must equal <c>ArtImportContract.GeometryRotationEulerX</c>
        /// in the editor assembly; it is repeated here because that type is editor-only.
        /// </summary>
        public const float SourceMeshRotationEulerX = 90f;

        /// <summary>The exact quaternion for <see cref="SourceMeshRotationEulerX"/>.</summary>
        public static Quaternion SourceMeshRotation => Quaternion.Euler(SourceMeshRotationEulerX, 0f, 0f);

        /// <summary>
        /// Builds the ground under <paramref name="parent"/> and returns it.
        /// </summary>
        /// <param name="earth">M_earth. May be null in a headless test; the quad is then unlit-pink but still solid.</param>
        /// <param name="sourceGroundCollider">The Mesh sub-asset of GoldenSlice_Ground_COLLIDER.fbx, or null to skip that child.</param>
        public static GameObject Build(Transform parent, Material earth, Mesh sourceGroundCollider)
        {
            var go = new GameObject(GroundName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            var builder = new MeshBuilder(4);
            float h = Size * 0.5f;

            // Corner order chosen so Cross(b − a, c − a) points +Y (see MeshBuilder.AddBox
            // for why winding decides both the visible face and the physics normal).
            // uvScale = 1 / TileMetres: MeshBuilder's planar UV is position × scale, so
            // one texture repeat spans TileMetres metres and the material keeps tiling 1.
            builder.AddQuad(
                new Vector3(-h, 0f, -h),
                new Vector3(-h, 0f, h),
                new Vector3(h, 0f, h),
                new Vector3(h, 0f, -h),
                1f / TileMetres);

            Mesh mesh = builder.Build(GroundName);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = earth;

            // Nothing is ever below y = 0, so the ground has nothing to cast onto; it
            // only receives. Same renderer settings as WorldPiece otherwise.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;

            int layer = PrototypeLayers.Environment;
            if (layer >= 0)
            {
                go.layer = layer;
            }

            if (sourceGroundCollider != null)
            {
                BuildSourceCollider(go.transform, sourceGroundCollider, layer);
            }

            return go;
        }

        /// <summary>
        /// The authored ground collider, rotated into contract space (source Blender
        /// x −14..44, y −14..42, z 0 → mesh (x, y, 0) → contract x −14..44, y 0, z −14..42).
        ///
        /// The contract imports the FBX non-readable. A MeshCollider given a non-readable
        /// mesh at runtime works in the Editor, where the data is still resident, but a
        /// Player cannot cook it ("CollisionMeshData couldn't be created because the mesh
        /// has been marked as non-accessible"). The source is a flat two-triangle quad,
        /// so when it is not readable an identical quad is rebuilt from its bounds, in
        /// mesh space under the same rotation - the collision surface is the same either
        /// way and the pilot behaves the same in the Editor and in the measured Player.
        /// </summary>
        private static void BuildSourceCollider(Transform parent, Mesh source, int layer)
        {
            var go = new GameObject(SourceColliderName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = SourceMeshRotation;

            Mesh physicsMesh = source;
            if (!source.isReadable)
            {
                // The quad lies in the mesh's XY plane. Its normal must be mesh −Z, which
                // Rx(+90°) turns into world +Y; this corner order gives Cross(b−a, c−a) = −Z.
                Bounds b = source.bounds;
                var builder = new MeshBuilder(4);
                builder.AddQuad(
                    new Vector3(b.min.x, b.min.y, b.center.z),
                    new Vector3(b.min.x, b.max.y, b.center.z),
                    new Vector3(b.max.x, b.max.y, b.center.z),
                    new Vector3(b.max.x, b.min.y, b.center.z),
                    1f / TileMetres);
                physicsMesh = builder.Build(SourceColliderName + " (from source bounds)");
            }

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = physicsMesh;
            collider.convex = false;

            if (layer >= 0)
            {
                go.layer = layer;
            }
        }
    }
}
