using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B6 · The Special VFX's procedural art, built once in C# at start-up (direction §6 items 4 and 5 — no
    /// external source, no imported PNG): the Lichtenberg burn atlas (3 variants, a dielectric-breakdown style branching
    /// walk — dense trunk, thinning forks, soft scorch halo), the gel splat atlas (3 variants: R = coverage / height with a
    /// noisy rim and satellite droplets, G = the drying crack mask — Voronoi edges inside the splat, B = thickness noise),
    /// and three meshes: a 1-subdivision icosphere (42 vertices / 80 triangles — the orb core, the plasma shell, the gel
    /// blob and every droplet), a flat XZ quad (puddles, splats, burn marks) and the DD/VFX Ribbon quad (arcs, streaks).
    /// Deterministic (fixed seeds): the same frames in every capture. Every object is HideFlags.DontSave and is owned by
    /// this class for the process (the systems never destroy them). Start-up cost ≈ 10–20 ms, once, at VfxDressing
    /// configure (boot / menu), never inside a run frame; nothing here runs per frame.
    /// </summary>
    public static class SpecialVfxAssets
    {
        public const int CellSize = 256;
        public const int Variants = 3;

        private static Texture2D s_burn;
        private static Texture2D s_splat;
        private static Mesh s_sphere;
        private static Mesh s_ground;
        private static Mesh s_ribbon;

        /// <summary>Lichtenberg burn marks, 3 cells side by side (768 × 256, R8: 1 = charred core).</summary>
        public static Texture2D BurnAtlas => s_burn != null ? s_burn : (s_burn = BuildLichtenbergAtlas(CellSize, Variants, 7301));

        /// <summary>Gel splats, 3 cells side by side (768 × 256, RGBA32: R coverage / height, G cracks, B thickness).</summary>
        public static Texture2D SplatAtlas => s_splat != null ? s_splat : (s_splat = BuildSplatAtlas(CellSize, Variants, 5519));

        public static Mesh Sphere => s_sphere != null ? s_sphere : (s_sphere = BuildIcosphere());

        public static Mesh GroundQuad => s_ground != null ? s_ground : (s_ground = BuildGroundQuad());

        public static Mesh RibbonQuad => s_ribbon != null ? s_ribbon : (s_ribbon = BuildRibbonQuad());

        /// <summary>Builds everything now (configure time), so the first shot never pays for it.</summary>
        public static void Warm()
        {
            _ = BurnAtlas;
            _ = SplatAtlas;
            _ = Sphere;
            _ = GroundQuad;
            _ = RibbonQuad;
        }

        // ------------------------------------------------------------------ Lichtenberg (§2.1 ground burn, §6-4)

        private struct Walker
        {
            public float X, Y, Angle, Energy;
        }

        /// <summary>
        /// A branching random walk from the cell centre: 4–6 trunks leave at even angles (± jitter), each step turns by
        /// ± 25°, spends energy and sometimes forks (the fork takes 55 % of the energy, the parent keeps 85 %); the brush
        /// width follows the energy, so the trunks are thick and the tips hair-thin. Two box blurs make the scorch halo;
        /// the result is the core + 0.35 × halo, faded to 0 before the cell edge (no hard square in the decal).
        /// </summary>
        public static Texture2D BuildLichtenbergAtlas(int cell, int variants, int seed)
        {
            int w = cell * variants, h = cell;
            var core = new float[w * h];
            var stack = new Walker[1024];
            for (int v = 0; v < variants; v++)
            {
                var rng = new System.Random(seed + v * 7919);
                float cx = v * cell + cell * 0.5f, cy = cell * 0.5f;
                float limit = cell * 0.46f;
                int top = 0;
                int trunks = 4 + rng.Next(3);
                for (int t = 0; t < trunks; t++)
                {
                    float a = t * Mathf.PI * 2f / trunks + (float)(rng.NextDouble() - 0.5) * 0.6f;
                    stack[top++] = new Walker { X = cx, Y = cy, Angle = a, Energy = 0.85f + (float)rng.NextDouble() * 0.15f };
                }

                while (top > 0)
                {
                    Walker k = stack[--top];
                    while (k.Energy > 0.05f)
                    {
                        k.Angle += (float)(rng.NextDouble() - 0.5) * 0.9f;
                        k.X += Mathf.Cos(k.Angle) * 1.5f;
                        k.Y += Mathf.Sin(k.Angle) * 1.5f;
                        float dx = k.X - cx, dy = k.Y - cy;
                        if (dx * dx + dy * dy > limit * limit) break;
                        Stamp(core, w, h, v * cell, (v + 1) * cell, k.X, k.Y, 0.6f + 2.2f * k.Energy, 0.4f + 0.6f * k.Energy);
                        k.Energy -= 0.004f + (float)rng.NextDouble() * 0.003f;
                        if (top < stack.Length && rng.NextDouble() < 0.045 * k.Energy)
                        {
                            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                            stack[top++] = new Walker
                            {
                                X = k.X, Y = k.Y, Energy = k.Energy * 0.55f,
                                Angle = k.Angle + side * (0.5f + (float)rng.NextDouble() * 0.5f)
                            };
                            k.Energy *= 0.85f;
                        }
                    }
                }
            }

            float[] halo = BoxBlur(BoxBlur(core, w, h, 3), w, h, 3);
            var bytes = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float lx = (x % cell) - cell * 0.5f, ly = y - cell * 0.5f;
                    float r = Mathf.Sqrt(lx * lx + ly * ly) / cell;
                    float fade = 1f - Mathf.SmoothStep(0.30f, 0.48f, r);
                    float value = Mathf.Clamp01(core[i] + 0.35f * halo[i] * 2.5f) * fade;
                    bytes[i] = (byte)(value * 255f);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.R8, true, true)
            {
                name = "DD_SpecialBurnAtlas (procedural)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixelData(bytes, 0);   // mip 0 only; Apply builds the chain (LoadRawTextureData would want every level)
            tex.Apply(true, true);
            return tex;
        }

        private static void Stamp(float[] buf, int w, int h, int x0, int x1, float x, float y, float radius, float value)
        {
            int r = Mathf.CeilToInt(radius);
            int ix = Mathf.RoundToInt(x), iy = Mathf.RoundToInt(y);
            for (int oy = -r; oy <= r; oy++)
            {
                int py = iy + oy;
                if (py < 0 || py >= h) continue;
                for (int ox = -r; ox <= r; ox++)
                {
                    int px = ix + ox;
                    if (px < x0 || px >= x1) continue;
                    float d = Mathf.Sqrt(ox * ox + oy * oy);
                    if (d > radius) continue;
                    float a = value * (1f - d / (radius + 0.5f));
                    int i = py * w + px;
                    if (a > buf[i]) buf[i] = a;
                }
            }
        }

        private static float[] BoxBlur(float[] src, int w, int h, int r)
        {
            var tmp = new float[w * h];
            var dst = new float[w * h];
            float n = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += src[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                    tmp[y * w + x] = s / n;
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                    dst[y * w + x] = s / n;
                }
            }

            return dst;
        }

        // ------------------------------------------------------------------ gel splat (§2.2 puddle, §6-5)

        /// <summary>
        /// Per cell: a main blob whose rim radius wanders with the angle (three sine octaves with random phases, ± 12 %),
        /// 5–8 satellite droplets beyond it, a dome height profile (R), Voronoi crack lines inside the blob (G: F2 − F1 &lt;
        /// 0.03, 14 sites — the drying phase's "kenar çatlak maskesi", strongest near the rim) and a low-frequency
        /// thickness noise (B). Soft 3 % edge so the shader's coverage threshold never aliases.
        /// </summary>
        public static Texture2D BuildSplatAtlas(int cell, int variants, int seed)
        {
            int w = cell * variants, h = cell;
            var px = new Color32[w * h];
            var sites = new Vector2[14];
            var sat = new Vector4[8];
            for (int v = 0; v < variants; v++)
            {
                var rng = new System.Random(seed + v * 104729);
                float p1 = (float)rng.NextDouble() * 6.283f, p2 = (float)rng.NextDouble() * 6.283f, p3 = (float)rng.NextDouble() * 6.283f;
                int satCount = 5 + rng.Next(4);
                for (int s = 0; s < satCount; s++)
                {
                    float a = (float)rng.NextDouble() * 6.283f;
                    float d = 0.70f + (float)rng.NextDouble() * 0.22f;
                    sat[s] = new Vector4(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.035f + (float)rng.NextDouble() * 0.06f, 0f);
                }

                for (int s = 0; s < sites.Length; s++)
                {
                    sites[s] = new Vector2((float)rng.NextDouble() * 1.6f - 0.8f, (float)rng.NextDouble() * 1.6f - 0.8f);
                }

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < cell; x++)
                    {
                        float ux = (x + 0.5f) / cell * 2f - 1f, uy = (y + 0.5f) / cell * 2f - 1f;
                        float r = Mathf.Sqrt(ux * ux + uy * uy);
                        float ang = Mathf.Atan2(uy, ux);
                        float rim = 0.62f * (1f + 0.07f * Mathf.Sin(ang * 3f + p1) + 0.035f * Mathf.Sin(ang * 7f + p2) + 0.02f * Mathf.Sin(ang * 13f + p3));
                        float cover = Mathf.Clamp01((rim - r) / 0.03f + 0.5f);
                        float height = cover * Mathf.Sqrt(Mathf.Clamp01(1f - (r / rim) * (r / rim)));
                        for (int s = 0; s < satCount; s++)
                        {
                            float dx = ux - sat[s].x, dy = uy - sat[s].y;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float c = Mathf.Clamp01((sat[s].z - d) / 0.02f + 0.5f);
                            if (c > cover) cover = c;
                            float hh = c * Mathf.Sqrt(Mathf.Clamp01(1f - (d / sat[s].z) * (d / sat[s].z))) * 0.6f;
                            if (hh > height) height = hh;
                        }

                        float f1 = 9f, f2 = 9f;
                        for (int s = 0; s < sites.Length; s++)
                        {
                            float dx = ux - sites[s].x, dy = uy - sites[s].y;
                            float d = dx * dx + dy * dy;
                            if (d < f1) { f2 = f1; f1 = d; }
                            else if (d < f2) f2 = d;
                        }

                        float crackLine = 1f - Mathf.Clamp01((Mathf.Sqrt(f2) - Mathf.Sqrt(f1)) / 0.03f);
                        float crack = crackLine * cover * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(r / rim));
                        float thick = 0.5f + 0.5f * Mathf.PerlinNoise(ux * 2.3f + v * 11f, uy * 2.3f + v * 7f);
                        // R carries coverage and height together: coverage saturates the first 0.1, the dome fills the rest.
                        float rch = cover * (0.12f + 0.88f * height);
                        px[y * w + v * cell + x] = new Color32((byte)(rch * 255f), (byte)(crack * 255f), (byte)(thick * 255f), 255);
                    }
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
            {
                name = "DD_GelSplatAtlas (procedural)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Icosahedron + one subdivision, unit radius 0.5 (a 1 m diameter at scale 1): 42 vertices, 80 triangles.</summary>
        public static Mesh BuildIcosphere()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var mid = new Dictionary<long, int>();
            var tris = new List<int>(80 * 3);
            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(verts, mid, a, b), bc = Mid(verts, mid, b, c), ca = Mid(verts, mid, c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            var normals = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                normals[i] = verts[i].normalized;
                verts[i] = normals[i] * 0.5f;
            }

            var mesh = new Mesh { name = "DD_SpecialSphere", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts);
            mesh.normals = normals;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static int Mid(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int i)) return i;
            verts.Add((verts[a].normalized + verts[b].normalized) * 0.5f);
            i = verts.Count - 1;
            cache[key] = i;
            return i;
        }

        /// <summary>A 1 × 1 quad in the XZ plane facing +Y, uv 0..1 (the puddle / splat / burn card; scale = diameter).</summary>
        public static Mesh BuildGroundQuad()
        {
            var mesh = new Mesh { name = "DD_SpecialGroundQuad", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>The DD/VFX Ribbon quad (u 0 tail → 1 head, v −1..1 across); world placement is the vertex stage's.</summary>
        public static Mesh BuildRibbonQuad()
        {
            var mesh = new Mesh { name = "DD_SpecialRibbonQuad", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(1f, -1f, 0f) };
            mesh.uv = new[] { new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // identity-matrix instances must never be culled
            return mesh;
        }

        // ------------------------------------------------------------------ colour helpers (linear, HDR)

        /// <summary>A direction hex (sRGB) as a linear HDR Vector4 for instanced arrays (MPB arrays are not colour-converted).</summary>
        public static Vector4 Hdr(uint rgb, float intensity, float alpha = 1f)
        {
            var c = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f).linear;
            return new Vector4(c.r * intensity, c.g * intensity, c.b * intensity, alpha);
        }

        /// <summary>A direction hex (sRGB) as a Color (for Material.SetColor / Light.color, which convert themselves).</summary>
        public static Color Srgb(uint rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        /// <summary>Cheap integer hash → 0..1 (stateless jitter for arcs, tendrils and bubbles).</summary>
        public static float Hash01(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return (x & 0x00FFFFFF) / 16777216f;
        }

        /// <summary>A unit vector from a hash (stateless: the same seed gives the same direction).</summary>
        public static Vector3 HashDir(uint seed)
        {
            float z = Hash01(seed) * 2f - 1f;
            float a = Hash01(seed * 747796405u + 2891336453u) * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
    }
}
