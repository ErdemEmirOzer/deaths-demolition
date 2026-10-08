using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The AP-031 binding (P-05 exports/v001, intake under Assets/_Art/Environment/Vegetation/AP-031/v001): per species,
    /// the imported FBX models per variant (_A/_B/_C — each holds VEG_X_LOD0/1/2 children), the family material (M_VEG_Grass /
    /// M_VEG_Flora on DD/World Vegetation with the atlas) and, for the momiji litter, the atlas cards of the AP-031 momiji
    /// manifest. Resources/<see cref="ResourcesPath"/>; a species without an entry keeps its vc1 placeholders
    /// (<see cref="VegetationMeshFactory"/>), so the kit can land one family at a time.
    ///
    /// AXIS: the import contract bakes the axis conversion (ArtImportContract: Mesh sub-asset = (bx, by, −bz), node +90° X),
    /// so the raw Mesh is not Y-up. <see cref="ExtractLod"/> applies each LOD node's full transform chain (the model asset's
    /// root has no parent) and returns a Y-up copy — whatever the importer did, the copy matches what the model shows.
    /// </summary>
    [CreateAssetMenu(menuName = "Deaths Demolition/Vegetation Kit", fileName = "DD_VegetationKit")]
    public sealed class VegetationKit : ScriptableObject
    {
        public const string ResourcesPath = "DD_VegetationKit";

        [Serializable]
        public sealed class Card
        {
            [Tooltip("Atlas UV rect (u0, v0, u1, v1) — the content box (AP-031 momiji manifest content_uv).")]
            public Vector4 uv;
            [Tooltip("Real size (m) of the content box (size_m_content).")]
            public Vector2 size;
            [Tooltip("fresh | underside | old | cluster | cluster_wet")]
            public string tag = "fresh";
            public bool cluster;
        }

        [Serializable]
        public sealed class Entry
        {
            public VegetationType type;
            [Tooltip("Index = variant (_A, _B, _C): the imported FBX model roots.")]
            public GameObject[] models = Array.Empty<GameObject>();
            [Tooltip("Family material (DD/World Vegetation + atlas). Per-species parameters are set on a runtime copy.")]
            public Material material;
            [Tooltip("True: the atlas carries the colour (root / tip tints go white, no dry tint).")]
            public bool atlasCarriesColour = true;
            [Tooltip("Card species (momiji litter): no model, quads cut from the atlas.")]
            public Card[] cards = Array.Empty<Card>();

            public bool HasModels => models != null && models.Length > 0 && models[0] != null;
            public bool HasCards => cards != null && cards.Length > 0;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public static VegetationKit Load() => Resources.Load<VegetationKit>(ResourcesPath);

        /// <summary>The entry for <paramref name="type"/> that can draw (models or cards), or null.</summary>
        public Entry Find(VegetationType type)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e != null && e.type == type && (e.HasModels || e.HasCards)) return e;
            }

            return null;
        }

        /// <summary>LOD levels a model carries (children named *_LOD0, *_LOD1, *_LOD2 with a mesh).</summary>
        public static int LodCount(GameObject model)
        {
            int n = 0;
            while (n < 3 && FindLod(model, n) != null) n++;
            return n;
        }

        public static MeshFilter FindLod(GameObject model, int lod)
        {
            if (model == null) return null;
            string suffix = "_LOD" + lod;
            var filters = new List<MeshFilter>(4);
            model.GetComponentsInChildren(true, filters);
            for (int i = 0; i < filters.Count; i++)
            {
                MeshFilter f = filters[i];
                if (f != null && f.sharedMesh != null && f.name.EndsWith(suffix, StringComparison.Ordinal)) return f;
            }

            return null;
        }

        /// <summary>
        /// A Y-up copy of the model's LOD mesh in model-root space (the LOD node's rotation / scale / offset applied; pivot =
        /// model root). Keeps colours (vc1), UV0, normals and tangents. The source must be readable (the import contract makes
        /// vegetation models readable). Null when the LOD does not exist.
        /// </summary>
        public static Mesh ExtractLod(GameObject model, int lod)
        {
            MeshFilter f = FindLod(model, lod);
            if (f == null) return null;
            Mesh src = f.sharedMesh;
            // The asset root has no parent: localToWorld = the whole node chain the model shows (root rotation included, if any).
            Matrix4x4 m = f.transform.localToWorldMatrix;
            Matrix4x4 n = m.inverse.transpose;
            Vector3[] v = src.vertices;
            Vector3[] nr = src.normals;
            Vector4[] tg = src.tangents;
            bool mirror = m.determinant < 0f;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < nr.Length; i++) nr[i] = n.MultiplyVector(nr[i]).normalized;
            for (int i = 0; i < tg.Length; i++)
            {
                Vector3 t = m.MultiplyVector(new Vector3(tg[i].x, tg[i].y, tg[i].z)).normalized;
                tg[i] = new Vector4(t.x, t.y, t.z, mirror ? -tg[i].w : tg[i].w);
            }

            var mesh = new Mesh { name = src.name, indexFormat = src.indexFormat };
            mesh.vertices = v;
            if (nr.Length == v.Length) mesh.normals = nr;
            if (tg.Length == v.Length) mesh.tangents = tg;
            Color[] c = src.colors;
            if (c.Length == v.Length) mesh.colors = c;
            mesh.uv = src.uv;
            int[] tris = src.triangles;
            if (mirror)
            {
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int s = tris[i + 1];
                    tris[i + 1] = tris[i + 2];
                    tris[i + 2] = s;
                }
            }

            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
