using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-012 · The per-library manifest (<c>&lt;AP&gt;_Library.json</c>, ADR-012-2) that drives
    /// prefab generation for AP-002 / AP-003 / AP-003B. Generated outside Unity
    /// (wp-012/tools/build-library-manifests.py) from the FBX files themselves (model
    /// names, material slot order, colour layers) and the production manifests (bounds,
    /// proxy classes, wind flags); never hand-edited.
    ///
    /// WHY A MANIFEST AND NOT THE FBX ALONE. Under the Import Contract materials are not
    /// imported (<c>materialImportMode = None</c>), so the material NAME of a slot is gone
    /// once the FBX is in Unity; only the sub-mesh order survives. The slot order recorded
    /// here (read from the FBX connections) is what binds sub-mesh <i>i</i> to
    /// <c>M_&lt;materials[i]&gt;.mat</c>. Everything else (which node is a LOD, a proxy, a
    /// roof, a broken state, a wind carrier) is naming convention the production packages
    /// state and the generator verified; repeating it in Unity code would be a second copy.
    ///
    /// Field names are the JSON keys (JsonUtility). Bounds are in Blender space (metres,
    /// Z up); <see cref="ArtAxis"/>-style conversion to the prefab space is (x, z, y).
    /// </summary>
    [Serializable]
    public sealed class ArtLibraryManifest
    {
        public string ap;
        public string version;
        public string derived;
        public List<Source> sources = new List<Source>();
        public List<Family> families = new List<Family>();

        [Serializable]
        public sealed class Source
        {
            public string path;
            public string sha256;
        }

        [Serializable]
        public sealed class Family
        {
            public string family;
            /// <summary>WP-038B/G · AP-017 family kind: <c>derivative</c> (litter / piles / stacks) or <c>breakable</c>; empty for the older libraries. Drives the short LOD cull and the no-shadow rule (WP-038G, M3 closure K1).</summary>
            public string kind;
            public bool IsDerivative => kind == "derivative";
            public bool IsBreakable => kind == "breakable";
            /// <summary>Root on the CameraOccluder layer (REQ-035 fade) — same policy as <see cref="ArtFamilies.OccluderCandidates"/>.</summary>
            public bool occluder;
            public List<Variation> variations = new List<Variation>();
        }

        [Serializable]
        public sealed class Variation
        {
            public string id;
            public string prefab;
            public float[] boundsMin;
            public float[] boundsMax;
            public List<Part> parts = new List<Part>();
            public List<Collider> colliders = new List<Collider>();
            /// <summary>WP-038B · AP-017 breakables: the break event id (BreakStone / BreakCeramic / BreakWood / BreakBamboo / BreakPaper / BreakStraw) for the VFX / SFX rows. Empty = the AP-003B default.</summary>
            public string breakEventId;
            /// <summary>WP-038B · AP-017 breakables: residue decal cell of the AP-010B 2×2 sheet (TL_ceramic / TR_wood / BL_straw / BR_cloth).</summary>
            public string residueCell;
            /// <summary>WP-038L · AP-017 koi shoal (FBX custom props AP017_swim = loop): circle radius range, speed, depth range. Absent = static prop.</summary>
            public Swim swim;
            /// <summary>WP-092 C · AP-030 EMPTY sockets (lamps: SOCKET_Flame, SOCKET_Light) — the FBX Null nodes in Blender space; empty for the older libraries.</summary>
            public List<Socket> sockets = new List<Socket>();

            public bool HasSwim => swim != null && swim.speedMps > 0f && swim.radiusM != null && swim.radiusM.Length == 2 && swim.radiusM[0] > 0f;
        }

        /// <summary>WP-092 C · A named attachment point (an FBX EMPTY at the model root): Blender-space metres, built as a child of the prefab root.</summary>
        [Serializable]
        public sealed class Socket
        {
            public string name;
            public float[] position;
            /// <summary>AP-030 v002 · CCW yaw about Blender +Z in degrees (road corner / curve exits: the flow leaves along the socket's +Z); 0 = unrotated.</summary>
            public float yawDeg;

            /// <summary>Prefab-root space (the AP world contract, <see cref="WorldPlacementAsset.ToUnity"/>).</summary>
            public Vector3 PrefabPosition => WorldPlacementAsset.ToUnity(position[0], position[1], position[2]);

            /// <summary>Prefab-root space rotation (<see cref="WorldPlacementAsset.ToUnityYaw"/>): its +Z is the Blender-space flow direction.</summary>
            public Quaternion PrefabRotation => WorldPlacementAsset.ToUnityYaw(yawDeg);
        }

        [Serializable]
        public sealed class Swim
        {
            public float[] radiusM;
            public float speedMps;
            public float[] depthM;
        }

        /// <summary>
        /// One renderer chain (LOD0/1/2). <see cref="role"/>: <c>lod</c> (default), <c>roof</c>
        /// (own LODGroup child that fades separately, REQ-111), <c>broken</c> (inactive
        /// broken-state child, REQ-113), <c>water</c> (placeholder material, shader is AP-015).
        /// </summary>
        [Serializable]
        public sealed class Part
        {
            public string name;
            public string role;
            public bool wind;
            public List<Lod> lods = new List<Lod>();

            public bool IsRoof => role == RoleRoof;
            public bool IsBroken => role == RoleBroken;
            public bool IsWater => role == RoleWater;
        }

        [Serializable]
        public sealed class Lod
        {
            public string fbx;
            /// <summary>FBX model node name = Unity Mesh sub-asset name.</summary>
            public string @object;
            /// <summary>Material roles in FBX slot order; sub-mesh <i>i</i> renders with <c>M_&lt;materials[i]&gt;</c>.</summary>
            public string[] materials;
            public int[] facesPerSlot;
            public int triangles;
            public string[] colourLayers;
            public bool windExpected;
        }

        /// <summary>
        /// A proxy/collider mesh: a union of axis-aligned box shells (occasionally a convex
        /// prism) in mesh space. The builder splits the shells and emits one primitive per
        /// shell. <see cref="class"/> is the production class (walkable, blocking, occluder,
        /// trigger/breakable); <see cref="broken"/> marks the broken-state trigger proxy.
        /// </summary>
        [Serializable]
        public sealed class Collider
        {
            public string fbx;
            public string @object;
            public string @class;
            public int shells;
            /// <summary>One entry per loose part of the proxy mesh, from the FBX's own vertex indices (Blender space).</summary>
            public List<ShellBox> shellBoxes = new List<ShellBox>();
            public string part;
            public bool broken;

            public bool IsTrigger => @class != null && @class.StartsWith(ClassTrigger, StringComparison.Ordinal);
            /// <summary>WP-038B · AP-017 flat litter: the proxy is a placement footprint only, no collider is built.</summary>
            public bool IsNone => @class == ClassNone;
        }

        /// <summary>
        /// Axis-aligned bounds of one proxy shell in Blender space and its distinct vertex
        /// count (8 = a box; more = a prism/hull). Read straight from the FBX geometry by the
        /// generator, so touching shells that Unity's vertex weld would merge stay separate.
        /// </summary>
        [Serializable]
        public sealed class ShellBox
        {
            public float[] min;
            public float[] max;
            public int positions;
            /// <summary>WP-074 B5 · A box shell that is not axis-aligned (sloped deck segments, angled doors): its oriented box in Blender space; null / empty otherwise.</summary>
            public Obb obb;

            public bool IsBox => positions <= 8;
            public bool HasObb => obb != null && obb.size != null && obb.size.Length == 3 && obb.center != null && obb.axisX != null && obb.axisY != null;

            /// <summary>
            /// The oriented box in MESH space: centre and axes through (bx, by, −bz) — a reflection, so the third axis is rebuilt as
            /// X × Y (a box is symmetric about its axes; the size along it is the recorded Z size).
            /// </summary>
            public void MeshSpaceObb(out Vector3 center, out Quaternion rotation, out Vector3 size)
            {
                center = new Vector3(obb.center[0], obb.center[1], -obb.center[2]);
                var x = new Vector3(obb.axisX[0], obb.axisX[1], -obb.axisX[2]);
                var y = new Vector3(obb.axisY[0], obb.axisY[1], -obb.axisY[2]);
                Vector3 z = Vector3.Cross(x, y);
                rotation = Quaternion.LookRotation(z, y);
                size = new Vector3(obb.size[0], obb.size[1], obb.size[2]);
            }

            /// <summary>
            /// The shell in Unity MESH space (the space of the Geometry child): the Import
            /// Contract's Mesh sub-asset holds (bx, by, −bz) of a Blender vertex.
            /// </summary>
            public Bounds MeshSpaceBounds()
            {
                var a = new Vector3(min[0], min[1], -max[2]);
                var b = new Vector3(max[0], max[1], -min[2]);
                var bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b);
                return bounds;
            }
        }

        /// <summary>WP-074 B5 · An oriented box (Blender space): centre, unit axes, full size along each axis.</summary>
        [Serializable]
        public sealed class Obb
        {
            public float[] center;
            public float[] axisX;
            public float[] axisY;
            public float[] axisZ;
            public float[] size;
        }

        public const string RoleLod = "lod";
        public const string RoleRoof = "roof";
        public const string RoleBroken = "broken";
        public const string RoleWater = "water";
        public const string ClassTrigger = "trigger";
        public const string ClassNone = "none";

        /// <summary>Blender-space bounds of a variation as a Unity prefab-space Bounds ((x, z, y), the AP world contract).</summary>
        public static Bounds PrefabBounds(Variation v)
        {
            var min = new Vector3(v.boundsMin[0], v.boundsMin[2], v.boundsMin[1]);
            var max = new Vector3(v.boundsMax[0], v.boundsMax[2], v.boundsMax[1]);
            var b = new Bounds(min, Vector3.zero);
            b.Encapsulate(max);
            return b;
        }

        public static ArtLibraryManifest FromJson(string json)
        {
            var m = JsonUtility.FromJson<ArtLibraryManifest>(json);
            if (m == null || string.IsNullOrEmpty(m.ap) || m.families == null)
            {
                throw new InvalidOperationException("[ArtLibrary] Manifest JSON is not a library manifest.");
            }

            return m;
        }

        public int VariationCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < families.Count; i++)
                {
                    n += families[i].variations.Count;
                }

                return n;
            }
        }
    }
}
