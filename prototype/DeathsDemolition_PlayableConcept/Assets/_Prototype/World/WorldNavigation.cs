using System.Text;
using DD.Prototype.Core;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.World
{
    /// <summary>
    /// Bakes the NavMesh and then PROVES it, which is the part that actually matters.
    ///
    /// G-05's <see cref="DD.Prototype.Navigation.NavPathService"/> degrades to straight
    /// lines and a warning when no NavMesh exists, and the spawn director then accepts
    /// every point because it cannot tell which ones are reachable. Simply baking is not
    /// enough to fix that: a bake that succeeds but leaves a spawn point two metres off
    /// the mesh, or on an island cut off by the pond, produces exactly the same broken
    /// behaviour with none of the warnings.
    ///
    /// So <see cref="Verify"/> walks every spawn point and every Special Weapon point,
    /// projects it onto the mesh and computes a real path from it to the player start. A
    /// point that cannot produce a <see cref="NavMeshPathStatus.PathComplete"/> is a bug
    /// in the map, and it is reported as one.
    ///
    /// The bake runs at RUNTIME as well as in the editor. Every mesh in this world is
    /// generated in code and therefore readable, which is the precondition; it costs a
    /// fraction of a second on a 90x90 m blockout and means a player build is never
    /// missing its navigation because somebody forgot to press a button.
    /// </summary>
    public static class WorldNavigation
    {
        /// <summary>PROTOTYPE-ONLY, invented: vertical room the bake volume needs.</summary>
        private const float VolumeHeight = 26f;

        /// <summary>
        /// PROTOTYPE-ONLY, invented: smallest surface worth keeping, in square metres.
        /// Culls the slivers that appear on top of rocks, hedges and lantern caps, which
        /// are unreachable anyway and only cost the pathfinder tiles.
        /// </summary>
        private const float MinRegionArea = 4f;

        /// <summary>How far a gameplay marker may sit from the mesh and still count.</summary>
        private const float VerifySampleRadius = 2f;

        public static NavMeshSurface Build(Transform root)
        {
            var go = new GameObject("NavMesh Surface");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            int env = PrototypeLayers.Environment;
            int occ = PrototypeLayers.CameraOccluder;

            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, VolumeHeight * 0.5f - 4f, 0f);
            surface.size = new Vector3(WorldLayout.MapSize + 6f, VolumeHeight, WorldLayout.MapSize + 6f);

            // Colliders, not render meshes: the invisible pond barrier has a collider and
            // no renderer, and the grass and petals have renderers and no collider. Only
            // the collider set is the truth about what a body can occupy.
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            // Occluders are solid too. A palace you can walk through because it happened
            // to be tagged "may fade" would be a very confusing bug.
            surface.layerMask = (env >= 0 ? 1 << env : 0) | (occ >= 0 ? 1 << occ : 0);

            surface.defaultArea = 0;
            surface.minRegionArea = MinRegionArea;
            surface.buildHeightMesh = false;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;

            Physics.SyncTransforms();
            surface.BuildNavMesh();

            return surface;
        }

        /// <summary>
        /// Reports mesh size and checks that every gameplay position is on it and can
        /// reach the player start. Returns false if anything is stranded.
        /// </summary>
        public static string Verify(out bool ok)
        {
            var sb = new StringBuilder(1024);
            ok = true;

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            int triangles = tri.indices != null ? tri.indices.Length / 3 : 0;

            if (triangles == 0)
            {
                ok = false;
                sb.Append("[World] NavMesh: EMPTY. Nothing baked.\n");
                return sb.ToString();
            }

            Vector3 start = WorldLayout.PlayerStart;
            if (!NavMesh.SamplePosition(start, out NavMeshHit startHit, 4f, NavMesh.AllAreas))
            {
                ok = false;
                sb.Append("  PLAYER START is not on the NavMesh\n");
                startHit.position = start;
            }

            int failures = 0;

            for (int i = 0; i < WorldMarkers.Spawns.Length; i++)
            {
                WorldMarkers.SpawnPlacement p = WorldMarkers.Spawns[i];
                failures += CheckPoint(sb, p.Id, new Vector3(p.X, 0f, p.Z), startHit.position);
            }

            for (int i = 0; i < WorldMarkers.Weapons.Length; i++)
            {
                WorldMarkers.WeaponPlacement p = WorldMarkers.Weapons[i];
                failures += CheckPoint(sb, p.Name, new Vector3(p.X, 0f, p.Z), startHit.position);
            }

            ok = ok && failures == 0;

            sb.Insert(0, "[World] NavMesh: " + triangles + " triangles, " +
                         WorldMarkers.Spawns.Length + " spawn points, " +
                         WorldMarkers.Weapons.Length + " weapon points, " +
                         failures + " unreachable" + (ok ? " -> OK\n" : " -> FAILED\n"));

            return sb.ToString();
        }

        private static int CheckPoint(StringBuilder sb, string id, Vector3 position, Vector3 start)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, VerifySampleRadius, NavMesh.AllAreas))
            {
                sb.Append("  '").Append(id).Append("' is not within ")
                    .Append(VerifySampleRadius).Append(" m of the NavMesh\n");
                return 1;
            }

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(hit.position, start, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                // The sampled position is printed with the failure because the two ways a
                // point fails look identical without it: the point may be on the right
                // surface behind a wall, or - as NE_StonePathCorner was - snapped onto a
                // sealed island a few centimetres away that nobody knew existed. Reading
                // "sampled 0.18 m away" is what turns the second case from a mystery into
                // a coordinate.
                sb.Append("  '").Append(id).Append("' has no complete path to the player start (")
                    .Append(path.status).Append("); sampled at ")
                    .Append(hit.position.ToString("F2")).Append(", ")
                    .Append(Vector3.Distance(position, hit.position).ToString("F2"))
                    .Append(" m from the marker\n");
                return 1;
            }

            return 0;
        }
    }
}
