using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.World
{
    /// <summary>
    /// Accumulates primitives into ONE mesh.
    ///
    /// WHY THIS EXISTS INSTEAD OF GameObject.CreatePrimitive
    /// -----------------------------------------------------
    /// The request asks for a layered environment built from primitives. Done naively
    /// that is a GameObject per box, which costs a draw call, a Transform and a
    /// MeshCollider each: a bamboo grove alone would be 200 objects. Here a whole
    /// grove, a whole lantern row or an entire palace is appended into a single mesh
    /// and becomes one renderer with one collider. The scene still reads as dozens of
    /// separate props, it just does not pay for them.
    ///
    /// The one deliberate exception is anything that has to fade independently for the
    /// camera (REQ-035): those get their own builder, so a single bamboo cluster can
    /// disappear without taking the whole grove with it.
    ///
    /// Every mesh produced here is created at runtime and is therefore readable, which
    /// is what lets NavMeshSurface voxelise it in a player build as well as in the
    /// editor.
    /// </summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices;
        private readonly List<Vector3> _normals;
        private readonly List<Vector2> _uvs;
        private readonly List<int> _triangles;

        public MeshBuilder(int expectedVertices = 256)
        {
            _vertices = new List<Vector3>(expectedVertices);
            _normals = new List<Vector3>(expectedVertices);
            _uvs = new List<Vector2>(expectedVertices);
            _triangles = new List<int>(expectedVertices * 3);
        }

        public int VertexCount => _vertices.Count;

        public int TriangleCount => _triangles.Count / 3;

        public bool IsEmpty => _triangles.Count == 0;

        public void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Quad in winding order a-b-c-d. Normal is derived from the corners.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale = 0.25f)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            AddQuad(a, b, c, d, normal, uvScale);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float uvScale)
        {
            int start = _vertices.Count;

            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);

            for (int i = 0; i < 4; i++)
            {
                _normals.Add(normal);
            }

            // Planar UVs from whichever pair of axes the normal is least aligned with,
            // so a wall and a floor both get sane tiling without per-call bookkeeping.
            _uvs.Add(PlanarUv(a, normal, uvScale));
            _uvs.Add(PlanarUv(b, normal, uvScale));
            _uvs.Add(PlanarUv(c, normal, uvScale));
            _uvs.Add(PlanarUv(d, normal, uvScale));

            _triangles.Add(start);
            _triangles.Add(start + 1);
            _triangles.Add(start + 2);

            _triangles.Add(start);
            _triangles.Add(start + 2);
            _triangles.Add(start + 3);
        }

        /// <summary>Axis-aligned box, optionally rotated about its own centre.</summary>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * 0.5f;

            Vector3 rx = rotation * new Vector3(h.x, 0f, 0f);
            Vector3 ry = rotation * new Vector3(0f, h.y, 0f);
            Vector3 rz = rotation * new Vector3(0f, 0f, h.z);

            Vector3 ppp = center + rx + ry + rz;
            Vector3 ppm = center + rx + ry - rz;
            Vector3 pmp = center + rx - ry + rz;
            Vector3 pmm = center + rx - ry - rz;
            Vector3 mpp = center - rx + ry + rz;
            Vector3 mpm = center - rx + ry - rz;
            Vector3 mmp = center - rx - ry + rz;
            Vector3 mmm = center - rx - ry - rz;

            // SARIM YÖNÜ — G-14 · KUSUR 2 ve KUSUR 3'ün ortak kök nedeni.
            // -----------------------------------------------------------
            // Bu altı yüzün eski köşe sırası (mpm,ppm,ppp,mpp ...) her yüzü İÇERİ
            // baktırıyordu: Cross(b-a, c-a) kutunun merkezine doğru çıkıyordu.
            // Unity'de Cross(b-a, c-a) ÖN yüz yönüdür — ölçüm: bir ışın kutunun yakın
            // yüzünü hiç görmeden içinden geçip uzak yüzünü ters normalle bildiriyordu
            // (Physics.queriesHitBackfaces = false). Sonuçları:
            //   fizik  · CharacterController'ın temas normali ters çıkıyor, kapsül
            //            duvarın içine itiliyor, sonra Move() her yönde kilitleniyordu;
            //   görsel · yakın yüzler kültenip iç/uzak yüzler çiziliyor, kutunun taban
            //            yüzü zeminle tam çakışık hale gelip z-fighting üretiyordu.
            // Düzeltme: her dörtgenin köşe sırasını ters çevir (a,b,c,d -> d,c,b,a).
            // Aynı dörtgen, aynı köşeler, aynı üçgen sayısı; yalnızca sarım ve türetilen
            // normal döner.
            AddQuad(mpp, ppp, ppm, mpm);   // üst   (+Y)
            AddQuad(mmm, pmm, pmp, mmp);   // alt   (-Y)
            AddQuad(mpm, ppm, pmm, mmm);   // -Z
            AddQuad(ppp, mpp, mmp, pmp);   // +Z
            AddQuad(ppm, ppp, pmp, pmm);   // +X
            AddQuad(mpp, mpm, mmm, mmp);   // -X
        }

        public void AddBox(Vector3 center, Vector3 size)
        {
            AddBox(center, size, Quaternion.identity);
        }

        /// <summary>
        /// Box given by its footprint and its height, which is how almost every piece of
        /// this blockout is actually authored (a wall from x1..x2, z1..z2, y0..y0+h).
        /// </summary>
        public void AddSlab(float x0, float z0, float x1, float z1, float y0, float y1)
        {
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            float cy = (y0 + y1) * 0.5f;
            AddBox(new Vector3(cx, cy, cz),
                new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0)));
        }

        /// <summary>Vertical cylinder or truncated cone standing on <paramref name="basePoint"/>.</summary>
        public void AddCylinder(Vector3 basePoint, float radiusBottom, float radiusTop, float height,
            int sides = 8, bool cap = true, Quaternion rotation = default)
        {
            if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
            {
                rotation = Quaternion.identity;
            }

            sides = Mathf.Max(3, sides);
            float step = Mathf.PI * 2f / sides;

            for (int i = 0; i < sides; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;

                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));

                Vector3 b0 = basePoint + rotation * (d0 * radiusBottom);
                Vector3 b1 = basePoint + rotation * (d1 * radiusBottom);
                Vector3 t0 = basePoint + rotation * (d0 * radiusTop + Vector3.up * height);
                Vector3 t1 = basePoint + rotation * (d1 * radiusTop + Vector3.up * height);

                // Sarım yönü — bkz. AddBox. Yan yüz de içeri bakıyordu; ters çevrildi.
                AddQuad(t0, t1, b1, b0, 0.35f);

                if (!cap)
                {
                    continue;
                }

                if (radiusTop > 0.001f)
                {
                    // Kapaklar ayrıca depolanan normalle ÇELİŞİYORDU: normal +Y idi ama
                    // sarım -Y'ye bakıyordu, yani yüz hem yanlış taraftan görünüyor hem
                    // ters aydınlanıyordu. t0/t1 sırası değişti.
                    AddTriangle(
                        basePoint + rotation * (Vector3.up * height),
                        t1, t0, rotation * Vector3.up);
                }

                if (radiusBottom > 0.001f)
                {
                    AddTriangle(basePoint, b0, b1, rotation * Vector3.down);
                }
            }
        }

        /// <summary>Low-poly ellipsoid. Used for tree canopies, rocks and koi.</summary>
        public void AddSphere(Vector3 center, Vector3 radii, int rings = 6, int segments = 10)
        {
            rings = Mathf.Max(2, rings);
            segments = Mathf.Max(3, segments);

            for (int r = 0; r < rings; r++)
            {
                float p0 = Mathf.PI * r / rings;
                float p1 = Mathf.PI * (r + 1) / rings;

                for (int s = 0; s < segments; s++)
                {
                    float t0 = Mathf.PI * 2f * s / segments;
                    float t1 = Mathf.PI * 2f * (s + 1) / segments;

                    Vector3 a = SpherePoint(center, radii, p0, t0);
                    Vector3 b = SpherePoint(center, radii, p0, t1);
                    Vector3 c = SpherePoint(center, radii, p1, t1);
                    Vector3 d = SpherePoint(center, radii, p1, t0);

                    if (r == 0)
                    {
                        AddTriangle(a, c, d, (a - center).normalized);
                        continue;
                    }

                    if (r == rings - 1)
                    {
                        AddTriangle(a, b, c, (b - center).normalized);
                        continue;
                    }

                    AddQuad(a, b, c, d, 0.3f);
                }
            }
        }

        /// <summary>
        /// Japanese hip-and-gable style roof, flattened to a blockout: a wide overhanging
        /// eave, a sloped body and a short ridge. Three parameters read clearly in the
        /// silhouette from the fixed camera, which is the only thing that matters here.
        /// </summary>
        public void AddRoof(Vector3 center, float sizeX, float sizeZ, float height, float ridgeRatio,
            float eaveDrop = 0.35f)
        {
            float hx = sizeX * 0.5f;
            float hz = sizeZ * 0.5f;
            float rx = hx * Mathf.Clamp01(ridgeRatio);
            float rz = hz * Mathf.Clamp01(ridgeRatio);

            float y0 = center.y - eaveDrop;
            float y1 = center.y + height;

            Vector3 e0 = new Vector3(center.x - hx, y0, center.z - hz);
            Vector3 e1 = new Vector3(center.x + hx, y0, center.z - hz);
            Vector3 e2 = new Vector3(center.x + hx, y0, center.z + hz);
            Vector3 e3 = new Vector3(center.x - hx, y0, center.z + hz);

            Vector3 r0 = new Vector3(center.x - rx, y1, center.z - rz);
            Vector3 r1 = new Vector3(center.x + rx, y1, center.z - rz);
            Vector3 r2 = new Vector3(center.x + rx, y1, center.z + rz);
            Vector3 r3 = new Vector3(center.x - rx, y1, center.z + rz);

            // Sarım yönü — bkz. AddBox. Beş yüz de içeri bakıyordu; ters çevrildi.
            AddQuad(r0, r1, e1, e0, 0.2f);
            AddQuad(r1, r2, e2, e1, 0.2f);
            AddQuad(r2, r3, e3, e2, 0.2f);
            AddQuad(r3, r0, e0, e3, 0.2f);
            AddQuad(r3, r2, r1, r0, 0.2f);

            // Eave underside, so the roof is not see-through from below.
            AddQuad(e0, e1, e2, e3, 0.2f);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            int start = _vertices.Count;

            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);

            _normals.Add(normal);
            _normals.Add(normal);
            _normals.Add(normal);

            _uvs.Add(PlanarUv(a, normal, 0.25f));
            _uvs.Add(PlanarUv(b, normal, 0.25f));
            _uvs.Add(PlanarUv(c, normal, 0.25f));

            _triangles.Add(start);
            _triangles.Add(start + 1);
            _triangles.Add(start + 2);
        }

        // ------------------------------------------------------------------ output

        /// <summary>
        /// Produces the mesh. Returns null when nothing was appended, so callers can
        /// build optimistically and simply skip empty groups.
        /// </summary>
        public Mesh Build(string name)
        {
            if (_triangles.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh { name = name };

            if (_vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0, true);
            mesh.RecalculateBounds();

            // The mesh is generated, never edited again and read by the NavMesh
            // voxeliser at build time only. Uploading it and keeping the CPU copy is
            // required: MarkDynamic or UploadMeshData(true) would make the NavMesh
            // collector fail on a MeshCollider.
            return mesh;
        }

        // ------------------------------------------------------------------ internals

        private static Vector3 SpherePoint(Vector3 center, Vector3 radii, float phi, float theta)
        {
            float sp = Mathf.Sin(phi);
            return new Vector3(
                center.x + radii.x * sp * Mathf.Cos(theta),
                center.y + radii.y * Mathf.Cos(phi),
                center.z + radii.z * sp * Mathf.Sin(theta));
        }

        private static Vector2 PlanarUv(Vector3 p, Vector3 n, float scale)
        {
            float ax = Mathf.Abs(n.x);
            float ay = Mathf.Abs(n.y);
            float az = Mathf.Abs(n.z);

            if (ay >= ax && ay >= az)
            {
                return new Vector2(p.x * scale, p.z * scale);
            }

            if (ax >= az)
            {
                return new Vector2(p.z * scale, p.y * scale);
            }

            return new Vector2(p.x * scale, p.y * scale);
        }
    }
}
