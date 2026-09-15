using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · glTF (layout JSON) → Unity contract space, in one place (ADR-011-1 §Axis).
    ///
    /// The layout JSON is extracted from GoldenSlice.glb, so its transforms are glTF:
    /// right-handed, Y up, with the Blender exporter's mapping glTF = (bx, bz, −by) for a
    /// Blender vertex (bx, by, bz). The AP-001 world contract is Blender X = Unity X
    /// (east), Blender Y = Unity Z (north), Blender Z = Unity Y (up), i.e. Unity =
    /// (bx, bz, by). Composing the two: Unity = (gx, gy, −gz) - a single axis flip.
    ///
    /// A flip is a reflection (M = diag(1, 1, −1), det −1), so a rotation R becomes
    /// M·R·M⁻¹: same axis mapped through M, opposite sense. For a quaternion (x, y, z, w)
    /// that is (−x, −y, z, w) - verified numerically against all 823 entries of the
    /// layout (M(R·v) = R′(M·v) to 1e−15). Scale is per axis and unaffected. The AABB
    /// only swaps and negates the z pair.
    ///
    /// The contract-side geometry rotation (<see cref="ArtImportContract.GeometryRotation"/>)
    /// is a different thing: it corrects what the FBX importer does to the meshes, not
    /// what the glTF exporter did to the placements. Both end in the same contract space.
    /// </summary>
    public static class ArtAxis
    {
        public static Vector3 GltfToContractPosition(Vector3 g)
        {
            return new Vector3(g.x, g.y, -g.z);
        }

        public static Quaternion GltfToContractRotation(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, q.z, q.w);
        }

        public static Vector3 GltfToContractScale(Vector3 s)
        {
            return new Vector3(s.x, s.y, s.z);
        }

        public static Bounds GltfAabbToContract(Vector3 min, Vector3 max)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(
                new Vector3(min.x, min.y, -max.z),
                new Vector3(max.x, max.y, -min.z));
            return bounds;
        }
    }
}
