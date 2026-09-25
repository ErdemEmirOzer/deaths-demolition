using System;
using System.Collections.Generic;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B5b-2 · The World v5 plaza on the AP-023 4K slate (P-05 ground manifest v001, the HF-center target's wet flagstone
    /// court). The plaza runs wall to wall: the centre square up to the forecourt wall, the karamon axis, the chūmon threshold, the
    /// forecourt and the strips beside the hall. It is a mesh on a global grid of 2 m cells (the 4 m texture tile at half size —
    /// slabs 0.6–1.0 m, AD B5b MUST-3), odd rows shifted half a cell (SHOULD-2): each cell takes one of the three slate variants (a
    /// sub-mesh per variant material) and a 0 / 90 / 180 / 270° UV rotation, both from a hash of the cell (P-05 AD SHOULD). The
    /// tile edges are grout lines, so mixed cells read as one irregular flagstone court. The mesh lies 12 mm over the greybox ground and carries no collider, so the NavMesh is
    /// unchanged; its open edges get the kit's granite kerb (+0.15 m, a step under the 0.30 climb). Material: DD/GroundTiles
    /// (BaseColor + Normal 4K, the packed Mask R AO / G roughness / B wetness / A puddle). Each paved rect has a box-projected
    /// reflection probe, so the wet slate and the puddles reflect the court.
    /// </summary>
    public static class WorldV5Ground
    {
        public const string PlazaName = "Plaza_Slate";
        public const string KerbGroupName = "Plaza_Kerbs", ProbeGroupName = "Plaza_ReflectionProbes";
        public const int ProbeResolution = 512;
        public const string ShaderName = "DD/GroundTiles";
        public const float Cell = 2f, Lift = 0.012f;
        public const int Seed = 743;
        public static readonly string[] SlateSets = { "ap023_slate_flag_v01", "ap023_slate_flag_v02", "ap023_slate_flag_v03" };
        public static string MeshPath => GreyboxV6Builder.DataFolder + "/V5_Plaza_Slate.asset";

        /// <summary>
        /// The paved rectangles (world x0, z0, x1, z1), not overlapping (AD B5b MUST-1: wall to wall, one paved axis): the centre
        /// square up to the forecourt wall's south face (z 19.7), the karamon axis strip (8 m wide, z −18…−14), the chūmon threshold,
        /// the forecourt inside its walls (inner faces x ±25.7, z 20.3) up to the hall's plinth front, and the strips beside the hall.
        /// </summary>
        public static readonly Vector4[] PlazaRects =
        {
            new Vector4(-16f, -14f, 16f, 19.7f),
            new Vector4(-4f, -18f, 4f, -14f),
            new Vector4(-4f, 19.7f, 4f, 20.3f),
            new Vector4(-25.7f, 20.3f, 25.7f, WorldV5Dress.HallFrontZ),
            new Vector4(-25.7f, WorldV5Dress.HallFrontZ, -18f, WorldV5Dress.HallFrontZ + 13f),
            new Vector4(18f, WorldV5Dress.HallFrontZ, 25.7f, WorldV5Dress.HallFrontZ + 13f),
        };

        /// <summary>Box-projected reflection probes: the centre square (with the axis strip) and the forecourt.</summary>
        public static readonly Vector4[] ProbeRects =
        {
            new Vector4(-16f, -18f, 16f, 19.7f),
            new Vector4(-25.7f, 20.3f, 25.7f, WorldV5Dress.HallFrontZ),
        };

        /// <summary>Open plaza edges that get kerbs (world XZ segments): the square's sides around the axis strip, the strip, the hall-side strips' outer edges past the forecourt walls (z 36).</summary>
        public static readonly (Vector2 a, Vector2 b)[] KerbEdges =
        {
            (new Vector2(-16f, -14f), new Vector2(-4f, -14f)),
            (new Vector2(4f, -14f), new Vector2(16f, -14f)),
            (new Vector2(-16f, -14f), new Vector2(-16f, 19.7f)),
            (new Vector2(16f, -14f), new Vector2(16f, 19.7f)),
            (new Vector2(-4f, -18f), new Vector2(-4f, -14f)),
            (new Vector2(4f, -18f), new Vector2(4f, -14f)),
            (new Vector2(-4f, -18f), new Vector2(4f, -18f)),
            (new Vector2(-25.7f, 36f), new Vector2(-25.7f, WorldV5Dress.HallFrontZ + 13f)),
            (new Vector2(25.7f, 36f), new Vector2(25.7f, WorldV5Dress.HallFrontZ + 13f)),
        };

        /// <summary>1 m kerb modules per edge (Kerbs: the edge length rounded, each stretched to fit).</summary>
        public static int KerbCount()
        {
            int n = 0;
            foreach ((Vector2 a, Vector2 b) in KerbEdges) n += Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, b)));
            return n;
        }

        /// <summary>DD/GroundTiles values set on every ground material (AD B5b: MUST-2 clustered puddles and near-mirror water, MUST-4 grade).</summary>
        /// <summary>AD S1 cool tint (linear, R/B ≈ 0.72 against the 4900 K sun and LUT v2's warm highlights; 0.64 overshot to 226° / s 0.29, 0.77 gave 206° / s 0.07).</summary>
        public static readonly Color AlbedoTint = new Color(0.86f, 1.00f, 1.19f, 1f);

        /// <summary>DEC-101 sky sheen (linear): the bright overcast the courtyard mirrors (HF), added on wet stone (0.10) and in the
        /// puddles (0.35, no puddle darkening, softer 0.10 edges) — the top-down camera's planar sees only the deck overhead.</summary>
        public static readonly Color SkyReflColor = new Color(0.46f, 0.50f, 0.56f, 1f);

        public static readonly (string name, float value)[] MaterialValues =
        {
            // WP-075 AD S1: the slate read beige (hue ≈ 35°, V ≈ 0.70 in the sun) → value 2.5 → 2.0 with a cool tint (AlbedoTint), toward
            // #6C7680 (205–215°, V 0.42–0.52 sun / 0.30–0.38 shade); puddles only from the world field (3–6 m blobs, off the tile grid),
            // smoothness 0.72 in them and 0.55 on the wet body.
            // DEC-101 (HF: a mirror-wet courtyard under a bright overcast sky): the wet body 0.55 → 0.78, value 2.0 → 4.0 (C-00: slate under cloud V 0.30–0.38), puddles 0.72 → 0.86 and
            // larger (5.5 m, field 0.56 ≈ 40 % cover) — AD S1's ≤ 8 % specular area belonged to the sunny v3 key.
            ("_NormalStrength", 1f), ("_Occlusion", 1f), ("_Wetness", 1f), ("_WetAlbedo", 0.82f), ("_WetSmoothness", 0.78f),
            ("_PuddleThreshold", 0.45f), ("_PuddleRamp", 0.15f), ("_PuddleSmoothness", 0.86f), ("_PuddleDarken", 1f), ("_TilePuddle", 0f),
            ("_SkyReflWet", 0.10f), ("_SkyReflPuddle", 0.35f),
            ("_PuddleScale", 5.5f), ("_PuddleField", 0.56f), ("_PuddleFieldRamp", 0.10f), ("_AlbedoSaturation", 0.28f), ("_AlbedoValue", 4.0f),
            ("_PlanarStrength", 1f), ("_PlanarDistortion", 0.03f), ("_PlanarWet", 0.35f),
        };

        /// <summary>The ground-set textures under the contract (T_&lt;set&gt;_BaseColor / _Normal / _Mask for every AP-023 ground set).</summary>
        public static IEnumerable<string> TextureFiles()
        {
            foreach (string set in ArtImportContract.Ap023GroundSets)
            foreach (string map in new[] { "BaseColor", "Normal", "Mask" })
                yield return ArtPipelinePaths.SourceTexture(set, map);
        }

        /// <summary>Deterministic cell hash → (variant, quarter turns).</summary>
        public static (int variant, int quarter) CellPick(int cx, int cz, int variants)
        {
            unchecked
            {
                uint h = (uint)(cx * 73856093) ^ (uint)(cz * 19349663) ^ (uint)(Seed * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return ((int)(h % (uint)variants), (int)((h >> 8) & 3u));
            }
        }

        /// <summary>Builds the plaza (mesh asset, materials, renderer) and its kerbs under <paramref name="parent"/>. Returns the cell count.</summary>
        public static int Build(Transform parent, Func<Transform, string, int, Matrix4x4, string, GameObject> placeKit)
        {
            Mesh mesh = TilesMesh(PlazaRects, SlateSets.Length, out int cells);
            var go = new GameObject(PlazaName) { layer = PrototypeLayers.Environment };
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, Lift, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[SlateSets.Length];
            for (int i = 0; i < SlateSets.Length; i++) materials[i] = GroundMaterial(SlateSets[i]);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;   // a 12 mm skin over the ground casts nothing worth a draw
            go.AddComponent<PlanarReflection>().PlaneHeight = Lift;   // B8: the court's mirror (tier-sized; Cinematic 1024 with characters)
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);

            // The wet slate needs something to reflect: one box-projected probe per paved rect (realtime, rendered once on load — the
            // B8 bake replaces them), 512 px (QUALITY-TIERS-V2 r2 Cinematic), 14 m tall.
            var probes = new GameObject(ProbeGroupName).transform;
            probes.SetParent(parent, false);
            for (int i = 0; i < ProbeRects.Length; i++)
            {
                Vector4 r = ProbeRects[i];
                var probe = new GameObject("PlazaProbe_" + i).AddComponent<ReflectionProbe>();
                probe.transform.SetParent(probes, false);
                probe.transform.position = new Vector3((r.x + r.z) * 0.5f, 3f, (r.y + r.w) * 0.5f);
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
                probe.boxProjection = true;
                probe.size = new Vector3(r.z - r.x, 14f, r.w - r.y);
                probe.center = new Vector3(0f, 4f, 0f);
                probe.resolution = ProbeResolution;
                probe.hdr = true;
                probe.blendDistance = 2f;
            }

            var kerbs = new GameObject(KerbGroupName).transform;
            kerbs.SetParent(parent, false);
            Material kerb = KerbMaterial();
            foreach ((Vector2 a, Vector2 b) in KerbEdges) Kerbs(kerbs, a, b, placeKit, kerb);
            return cells;
        }

        /// <summary>1 m kerbs along an edge (the last one stretched to fit), centred 0.15 m outside the paving.</summary>
        private static void Kerbs(Transform parent, Vector2 a, Vector2 b, Func<Transform, string, int, Matrix4x4, string, GameObject> placeKit, Material kerb)
        {
            float len = Vector2.Distance(a, b);
            Vector2 d = (b - a) / len;
            // Outward = away from the paving: the edges run with the paving on their left (+90°) or right; pick the side away from the nearest rect centre.
            Vector2 side = new Vector2(-d.y, d.x);
            Vector2 mid = (a + b) * 0.5f;
            if (InsidePlaza(mid + side * 0.5f)) side = -side;
            int n = Mathf.Max(1, Mathf.RoundToInt(len));
            float scale = len / n, deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = a + d * ((i + 0.5f) * scale) + side * 0.15f;
                GameObject k = placeKit(parent, "granite_kerb", 1 + (i % 3 == 2 ? 2 : 0), Matrix4x4.TRS(new Vector3(p.x, p.y, 0f), Quaternion.AngleAxis(deg, Vector3.forward), new Vector3(scale, 1f, 1f)), "dressing");
                foreach (Renderer r in k.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials = Array.ConvertAll(r.sharedMaterials, m => m != null && m.name == KerbSourceMaterial ? kerb : m);
            }
        }

        public static bool InsidePlaza(Vector2 p)
        {
            foreach (Vector4 r in PlazaRects)
                if (p.x > r.x && p.x < r.z && p.y > r.y && p.y < r.w) return true;
            return false;
        }

        /// <summary>
        /// The grid mesh: every 4 m cell of the global grid clipped to each rect; UVs cell-local (0…1 across a whole cell, a clipped
        /// cell keeps its part of the tile) turned by the cell's quarter turns; one sub-mesh per variant; tangents from the UVs.
        /// </summary>
        public static Mesh TilesMesh(IList<Vector4> rects, int variants, out int cells)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[variants];
            for (int i = 0; i < variants; i++) tris[i] = new List<int>();
            cells = 0;
            foreach (Vector4 r in rects)
            {
                for (int cz = Mathf.FloorToInt(r.y / Cell); cz * Cell < r.w; cz++)
                for (int cx = Mathf.FloorToInt((r.x - RowShift(cz)) / Cell); cx * Cell + RowShift(cz) < r.z; cx++)
                {
                    float left = cx * Cell + RowShift(cz);
                    float x0 = Mathf.Max(r.x, left), x1 = Mathf.Min(r.z, left + Cell), z0 = Mathf.Max(r.y, cz * Cell), z1 = Mathf.Min(r.w, (cz + 1) * Cell);
                    if (x1 - x0 < 1e-3f || z1 - z0 < 1e-3f) continue;
                    (int variant, int quarter) = CellPick(cx, cz, variants);
                    int b = verts.Count;
                    foreach (Vector2 c in new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) })
                    {
                        verts.Add(new Vector3(c.x, 0f, c.y));
                        uvs.Add(Turn(new Vector2((c.x - left) / Cell, c.y / Cell - cz), quarter));
                    }

                    tris[variant].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    cells++;
                }
            }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = "V5_Plaza_Slate" };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = variants;
            for (int i = 0; i < variants; i++) mesh.SetTriangles(tris[i], i);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, MeshPath);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Odd rows run half a cell to the east (a running bond of cells: the grid stops reading).</summary>
        public static float RowShift(int cz)
        {
            return (cz & 1) != 0 ? Cell * 0.5f : 0f;
        }

        /// <summary>Quarter turns of a tile UV about the tile centre.</summary>
        public static Vector2 Turn(Vector2 uv, int quarter)
        {
            switch (quarter & 3)
            {
                case 1: return new Vector2(1f - uv.y, uv.x);
                case 2: return new Vector2(1f - uv.x, 1f - uv.y);
                case 3: return new Vector2(uv.y, 1f - uv.x);
                default: return uv;
            }
        }

        /// <summary>V5_Ground_&lt;set&gt;.mat on DD/GroundTiles with the set's three maps (rebuilt in place).</summary>
        public static Material GroundMaterial(string set)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException(ShaderName + " missing");
            string path = GreyboxV6Builder.DataFolder + "/Materials/V5_Ground_" + set + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            m.SetTexture("_BaseMap", Load(set, "BaseColor"));
            m.SetTexture("_NormalMap", Load(set, "Normal"));
            m.SetTexture("_MaskMap", Load(set, "Mask"));
            foreach ((string name, float value) in MaterialValues) m.SetFloat(name, value);
            m.SetColor("_AlbedoTint", AlbedoTint);
            m.SetColor("_SkyReflColor", SkyReflColor);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public const string KerbSourceMaterial = "M_ap023_granite";

        /// <summary>
        /// V5_Kerb.mat: the kit granite, wet (AD B5b MUST-4 — the kerbs read as near-white outlines): base colour × 0.5 (top value
        /// ≤ 0.40 linear) and the packed smoothness at full weight, the slate's wet read; the kit material itself is untouched.
        /// </summary>
        public static Material KerbMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.ArtRoot + "/Materials/" + KerbSourceMaterial + ".mat");
            if (source == null) throw new InvalidOperationException(KerbSourceMaterial + " missing");
            string path = GreyboxV6Builder.DataFolder + "/Materials/V5_Kerb.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(source);
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = source.shader;
                m.CopyPropertiesFromMaterial(source);
            }

            m.SetColor("_BaseColor", source.GetColor("_BaseColor") * 0.5f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D Load(string set, string map)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtPipelinePaths.SourceTexture(set, map));
            if (t == null) throw new InvalidOperationException("ground texture missing: " + ArtPipelinePaths.SourceTexture(set, map));
            return t;
        }
    }
}
