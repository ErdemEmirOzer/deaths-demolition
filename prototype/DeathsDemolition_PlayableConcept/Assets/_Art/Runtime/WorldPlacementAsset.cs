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
            /// <summary>Blender yaw about +Z (CCW, degrees) — the dd-placement-v1 rotation; equals <c>rotationEuler.z</c>.</summary>
            public float yawDegrees;
            /// <summary>WP-017 · Full Blender XYZ euler (degrees, extrinsic X → Y → Z). AP-005B tilts eight gable panels about Y; every v001 record is (0, 0, yaw).</summary>
            public Vector3 rotationEuler;
            public Vector3 scale = Vector3.one;

            public bool IsTilted => Mathf.Abs(rotationEuler.x) > 1e-4f || Mathf.Abs(rotationEuler.y) > 1e-4f;
            public Quaternion Rotation => ToUnityRotation(rotationEuler);
            public string region;
            public string role;
            public string navMesh;
            public bool occluder;
            public bool fade;
            /// <summary>WP-017 · AP-005B world-master material name (<c>material_override</c>), empty = the prefab's own materials. Mapped by <c>PlacementImporter.MaterialOverrides</c>.</summary>
            public string materialOverride;
            /// <summary>WP-017 · "v002" for records added or changed by the AP-005B delta, "v003" by the AP-005C delta (WP-020); empty for v001 records.</summary>
            public string deltaVersion;
            /// <summary>WP-020 · AP-005C <c>material_override_parts</c>: world-master material per assembly part (part = renderer name prefix, e.g. <c>panel</c> → <c>panel_LOD0</c>); the other parts keep the prefab's materials. Empty = none.</summary>
            public PartOverride[] materialOverrideParts = new PartOverride[0];
        }

        [Serializable]
        public struct PartOverride
        {
            public string part;
            public string material;
        }

        /// <summary>WP-020 · One applied delta file (in order): file name, sha256, target and counts.</summary>
        [Serializable]
        public sealed class DeltaRecord
        {
            public string file;
            public string version;
            public string sha256;
            public string target;
            public int added;
            public int changed;
            public int removed;
            public List<string> removedIds = new List<string>();
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
        /// <summary>WP-017 · Delta chain (dd-placement-delta-v1) applied on top of the v001 source, in <see cref="ArtLibraryPaths.Ap005PlacementDeltaJsons"/> order: sha256 of the LAST delta file, its target; counts and removed ids are the totals over the chain (WP-020: v002 + v003). Empty sha = no delta. Per-file records in <see cref="deltas"/>.</summary>
        public string deltaSha256;
        public string deltaTarget;
        public int deltaAdded;
        public int deltaChanged;
        public int deltaRemoved;
        public List<string> deltaRemovedIds = new List<string>();
        public List<DeltaRecord> deltas = new List<DeltaRecord>();
        public List<Instance> instances = new List<Instance>();
        public List<Route> routes = new List<Route>();
        public List<Marker> markers = new List<Marker>();
        public List<Point> spawnPoints = new List<Point>();
        /// <summary>WP-074 B1b · v6 inner spawn gates (S-03 WP-076 B6: SpawnPointMarker Kind = Inner, ids from spawn-gates-v6.json); empty for AP-005.</summary>
        public List<Point> innerSpawnPoints = new List<Point>();
        /// <summary>WP-074 B1b · the world layout this placement lays out: "v6" (the ±55 greybox / World v5) or empty / "ap005" (±45) — <c>WorldLayout.Current</c>.</summary>
        public string layout = "";
        public List<Point> weaponPoints = new List<Point>();
        public Vector3 playerStart;
        public List<Region> regions = new List<Region>();

        public const string Schema = "dd-placement-v1";
        public const string OverridesSchema = "dd-placement-overrides-v1";
        public const string DeltaSchema = "dd-placement-delta-v1";
        public const string DeltaVersionTag = "v002";
        public const string DeltaVersionTagV3 = "v003";

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

        /// <summary>
        /// WP-017 · Blender XYZ euler (degrees; R = Rz·Ry·Rx, CCW positive, +Z up) → Unity. The
        /// axis map (x, y, z) → (x, z, y) is a reflection, so every angle flips sign and Blender's
        /// X / Y / Z axes become Unity's X / Z / Y; the application order (X first) is kept.
        /// (0, 0, yaw) equals <see cref="ToUnityYaw"/>.
        /// </summary>
        public static Quaternion ToUnityRotation(Vector3 blenderEulerDegrees)
        {
            return Quaternion.AngleAxis(-blenderEulerDegrees.z, Vector3.up)
                   * Quaternion.AngleAxis(-blenderEulerDegrees.y, Vector3.forward)
                   * Quaternion.AngleAxis(-blenderEulerDegrees.x, Vector3.right);
        }

        /// <summary>Rotate a Blender-space vector by a Blender XYZ euler (R = Rz·Ry·Rx), for parity tests.</summary>
        public static Vector3 RotateBlender(Vector3 blenderEulerDegrees, Vector3 v)
        {
            float a = blenderEulerDegrees.x * Mathf.Deg2Rad, b = blenderEulerDegrees.y * Mathf.Deg2Rad, g = blenderEulerDegrees.z * Mathf.Deg2Rad;
            // Rx
            v = new Vector3(v.x, v.y * Mathf.Cos(a) - v.z * Mathf.Sin(a), v.y * Mathf.Sin(a) + v.z * Mathf.Cos(a));
            // Ry
            v = new Vector3(v.x * Mathf.Cos(b) + v.z * Mathf.Sin(b), v.y, -v.x * Mathf.Sin(b) + v.z * Mathf.Cos(b));
            // Rz
            return new Vector3(v.x * Mathf.Cos(g) - v.y * Mathf.Sin(g), v.x * Mathf.Sin(g) + v.y * Mathf.Cos(g), v.z);
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
