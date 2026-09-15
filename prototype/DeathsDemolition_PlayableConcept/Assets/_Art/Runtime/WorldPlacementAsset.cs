using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-014 · placement.json (dd-placement-v1) after import, in Unity space (ADR-014-1):
    /// every instance with its library/family/variation and a Unity TRS, the routes, the
    /// markers, the spawn/weapon points, the player start and the region polygons. The
    /// importer writes it; the world scene bootstrap (markers, drift regions) and the tests
    /// (parity, reachability, readability) read it. Blender → Unity: (x, y, z) → (x, z, y),
    /// yaw about +Z CCW → rotation about +Y by −yaw.
    /// </summary>
    public sealed class WorldPlacementAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Instance
        {
            public string id;
            public string ap;
            public string family;
            public string variation;
            public string lodGroup;
            public string[] assembly;
            public Vector3 position;
            public float yawDegrees;
            public Vector3 scale = Vector3.one;
            public string region;
            public string role;
            public string navMesh;
            public bool occluder;
            public bool fade;
        }

        [Serializable]
        public sealed class Route
        {
            public int index;
            public Vector3 a;
            public Vector3 b;
            public float halfWidth;
            public string surface;
        }

        [Serializable]
        public sealed class Marker
        {
            public string id;
            public string role;
            public Vector3 position;
        }

        [Serializable]
        public sealed class Point
        {
            public string id;
            public Vector3 position;
            public float weight;
        }

        [Serializable]
        public sealed class Region
        {
            public string id;
            public string label;
            public string identity;
            public string sightlineClass;
            /// <summary>Polygon in Unity (x, z) pairs, flattened.</summary>
            public float[] polygonXZ;
        }

        public string schema;
        public string space;
        public string package;
        public string version;
        public string created;
        public string sourceSha256;
        /// <summary>WP-015 · SHA-256 of the overrides file applied on top of the source (empty when there is none).</summary>
        public string overridesSha256;
        /// <summary>WP-015 · ids whose transform differs from the source by an override (in file order).</summary>
        public List<string> overriddenIds = new List<string>();
        public List<Instance> instances = new List<Instance>();
        public List<Route> routes = new List<Route>();
        public List<Marker> markers = new List<Marker>();
        public List<Point> spawnPoints = new List<Point>();
        public List<Point> weaponPoints = new List<Point>();
        public Vector3 playerStart;
        public List<Region> regions = new List<Region>();

        public const string Schema = "dd-placement-v1";
        public const string OverridesSchema = "dd-placement-overrides-v1";

        /// <summary>Blender (x, y, z) → Unity (x, z, y).</summary>
        public static Vector3 ToUnity(float bx, float by, float bz)
        {
            return new Vector3(bx, bz, by);
        }

        /// <summary>A CCW yaw about Blender +Z is a rotation by −yaw about Unity +Y (both map +X→+X, Blender +Y→Unity +Z).</summary>
        public static Quaternion ToUnityYaw(float yawDegreesCcw)
        {
            return Quaternion.Euler(0f, -yawDegreesCcw, 0f);
        }

        public Region FindRegion(string id)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                if (regions[i].id == id) return regions[i];
            }

            return null;
        }

        /// <summary>Axis-aligned XZ bounds of a region polygon, in Unity space, with the given height span.</summary>
        public static Bounds RegionBounds(Region region, float minY, float maxY)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i + 1 < region.polygonXZ.Length; i += 2)
            {
                minX = Mathf.Min(minX, region.polygonXZ[i]);
                maxX = Mathf.Max(maxX, region.polygonXZ[i]);
                minZ = Mathf.Min(minZ, region.polygonXZ[i + 1]);
                maxZ = Mathf.Max(maxZ, region.polygonXZ[i + 1]);
            }

            var b = new Bounds(new Vector3(minX, minY, minZ), Vector3.zero);
            b.Encapsulate(new Vector3(maxX, maxY, maxZ));
            return b;
        }
    }
}
