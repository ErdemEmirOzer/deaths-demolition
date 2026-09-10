using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.World
{
    /// <summary>
    /// Builds the whole map. One entry point, called from <see cref="WorldBootstrap"/> at
    /// runtime and from the editor menu for a look-at-it preview.
    ///
    /// WHY THE WORLD IS CODE AND THE SCENE FILE IS ALMOST EMPTY
    /// -------------------------------------------------------
    /// Four tasks are authoring this project at the same time and a .unity file is the one
    /// artefact they cannot merge - a single hand-edited scene with a hundred thousand
    /// lines of YAML in it would serialise the four of them into a queue. So Run.unity
    /// holds settings and ONE GameObject, and every triangle, marker, light and particle
    /// in this map is produced by the code below. That also means the map is diffable,
    /// reviewable in a pull request, and cannot carry a broken serialized reference.
    ///
    /// ORDER IS LOAD-BEARING, in three places:
    ///   - markers before foliage, because foliage asks the marker list what to keep clear;
    ///   - structures before foliage, because foliage tests placement with
    ///     Physics.CheckSphere against the structures that already exist;
    ///   - everything before navigation, because the bake reads colliders.
    ///
    /// Three self-checks run every build and print to the console: topology (connected, no
    /// dead ends, a radial per region), palette (nothing trespassing on gold or beacon
    /// colour space) and navigation (every spawn and weapon point has a complete path to
    /// the player start). They are cheap and they turn silent map bugs into loud ones.
    /// </summary>
    public static class WorldBuilder
    {
        public const string RootName = "[World]";

        /// <summary>Result of a build, so callers can report without rebuilding.</summary>
        public struct Result
        {
            public GameObject Root;
            public NavMeshSurface Surface;
            public string Report;
            public bool Ok;
        }

        /// <summary>
        /// Creates the world under a fresh root, replacing any existing one.
        /// </summary>
        /// <param name="volumeProfile">Authored post-processing profile, or null to build
        /// an identical one in memory.</param>
        /// <param name="bakeNavMesh">False only for a fast editor preview.</param>
        public static Result Build(VolumeProfile volumeProfile, bool bakeNavMesh)
        {
            var report = new StringBuilder(2048);
            bool ok = true;

            Clear();

            // 1. Topology first, on the data alone. A broken map is caught before a single
            //    triangle is generated.
            report.Append(WorldLayout.ValidateConnectivity(out bool topologyOk));
            ok &= topologyOk;

            var rootGo = new GameObject(RootName);
            rootGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform root = rootGo.transform;

            var palette = new WorldPalette();

            WorldGround.Build(root, palette);
            WorldStructures.Build(root, palette);

            // 2. Gameplay positions, before anything is scattered around them.
            List<Vector2> keepOut = WorldMarkers.Build(root);

            WorldFoliage.Build(root, palette, keepOut);
            WorldAtmosphere.Build(root, palette, volumeProfile);

            report.Append(palette.Validate(out bool paletteOk));
            ok &= paletteOk;

            NavMeshSurface surface = null;
            if (bakeNavMesh)
            {
                surface = WorldNavigation.Build(root);
                report.Append(WorldNavigation.Verify(out bool navOk));
                ok &= navOk;
            }
            else
            {
                report.Append("[World] NavMesh: skipped (preview build).\n");
            }

            report.Append(DescribeContent(rootGo));

            return new Result
            {
                Root = rootGo,
                Surface = surface,
                Report = report.ToString(),
                Ok = ok
            };
        }

        /// <summary>Destroys an existing world root, if there is one.</summary>
        public static void Clear()
        {
            GameObject existing = GameObject.Find(RootName);
            while (existing != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(existing);
                    break;
                }

                Object.DestroyImmediate(existing);
                existing = GameObject.Find(RootName);
            }
        }

        /// <summary>
        /// Marks a preview world as never-save, so building one in the editor to look at
        /// it cannot accidentally be committed into the scene file. Without this the whole
        /// point of keeping Run.unity empty is lost the first time somebody hits Ctrl+S.
        /// </summary>
        public static void MarkTransient(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        private static string DescribeContent(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            var colliders = root.GetComponentsInChildren<MeshCollider>(true);

            int occluders = 0;
            int triangles = 0;
            int occluderLayer = DD.Prototype.Core.PrototypeLayers.CameraOccluder;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (occluderLayer >= 0 && renderers[i].gameObject.layer == occluderLayer)
                {
                    occluders++;
                }

                var filter = renderers[i].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.subMeshCount > 0)
                {
                    // GetIndexCount, not .triangles: the latter copies every index into a
                    // fresh managed array, and this runs over a few hundred meshes.
                    triangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
                }
            }

            return "[World] Content: " + renderers.Length + " renderers (" + occluders +
                   " on CameraOccluder), " + colliders.Length + " mesh colliders, " +
                   triangles + " triangles.\n";
        }
    }
}
