using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · Procedural PLACEHOLDER clumps until AP-031 (P-05) lands, built to the same "vc1" contract
    /// (AP-031-SPEC §1) so the runtime, the shader and the tests do not change when the real meshes arrive:
    ///   pivot at the base centre, +Y up, root vertices at y = 0, nothing below ground;
    ///   colour R = bend weight (y/h)² (low on stiff stems), G = per-blade phase (constant per blade), B = stiffness,
    ///   A = root AO 0.35 → 1; UV0 x across / y along (accents use x ≥ 2: 2..3 accent A, 3..4 accent B — a placeholder-only
    ///   code the atlas never produces); normals bent toward the clump's upper hemisphere; tip width ≥ 0.3 × base (bible
    ///   anti-shimmer) and blade width ≥ 1.8 cm. Triangle counts stay under the AP-031 §2 ceilings.
    /// </summary>
    public static class VegetationMeshFactory
    {
        public const float AccentA = 2.5f, AccentB = 3.5f;

        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>(512);
            public readonly List<Vector3> N = new List<Vector3>(512);
            public readonly List<Color> C = new List<Color>(512);
            public readonly List<Vector2> U = new List<Vector2>(512);
            public readonly List<int> T = new List<int>(1024);
            public readonly List<Vector4> Tangents = new List<Vector4>(64);
            public float Height = 1f;   // clump height for R / A

            public int Add(Vector3 p, Vector3 n, float phase, float stiff, float rScale, float u, float v)
            {
                float y01 = Mathf.Clamp01(p.y / Mathf.Max(1e-4f, Height));
                V.Add(p);
                N.Add(n.normalized);
                C.Add(new Color(Mathf.Clamp01(y01 * y01 * rScale), phase, stiff, Mathf.Lerp(0.35f, 1f, y01)));
                U.Add(new Vector2(u, v));
                return V.Count - 1;
            }

            public void Quad(int a, int b, int c, int d)
            {
                T.Add(a); T.Add(c); T.Add(b);
                T.Add(b); T.Add(c); T.Add(d);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(V);
                mesh.SetNormals(N);
                mesh.SetColors(C);
                mesh.SetUVs(0, U);
                mesh.SetTriangles(T, 0);
                mesh.RecalculateBounds();
                return mesh;   // stays readable: the vc1 contract test reads it back
            }
        }

        /// <summary>LOD count the species uses (2, or 3 with the cross card).</summary>
        public static int LodCount(VegetationRecipe.Species sp) => sp.hasLod2 ? 3 : 2;

        public static Mesh Build(VegetationRecipe.Species sp, int variant, int lod)
        {
            var b = new Builder();
            float h = (sp.heightMin + sp.heightMax) * 0.5f;
            b.Height = h;
            int salt = (int)sp.type * 977 + variant * 131 + 7;
            string name = VegetationTypes.AssetNames[(int)sp.type] + "_PH" + (char)('A' + variant) + "_LOD" + lod;
            if (lod >= 2)
            {
                CrossCard(b, sp, h, salt);
                return b.Build(name);
            }

            float detail = lod == 0 ? 1f : 0.6f;
            switch (sp.shape)
            {
                case VegetationRecipe.PlaceholderShape.Blades:
                    Blades(b, sp, h, salt, Mathf.Max(3, Mathf.RoundToInt(sp.placeholderBlades * detail)), lod == 0 ? sp.placeholderSegments : 2, 0.45f, sp.placeholderBladeWidth, 0.35f);
                    if (sp.type == VegetationType.GrassTall && variant == 1) Plumes(b, sp, h, salt, 4);
                    break;
                case VegetationRecipe.PlaceholderShape.Fronds:
                    Blades(b, sp, h, salt, Mathf.Max(3, Mathf.RoundToInt(sp.placeholderBlades * detail)), lod == 0 ? sp.placeholderSegments : 3, 0.9f, sp.placeholderBladeWidth, 0.3f);
                    break;
                case VegetationRecipe.PlaceholderShape.BroadLeaves:
                    Blades(b, sp, h, salt, Mathf.Max(3, Mathf.RoundToInt(sp.placeholderBlades * detail)), lod == 0 ? sp.placeholderSegments : 2, 1.2f, sp.placeholderBladeWidth, 0.5f);
                    break;
                case VegetationRecipe.PlaceholderShape.Patch:
                    Patch(b, sp, h, salt, lod == 0 ? sp.placeholderBlades : 6);
                    break;
                case VegetationRecipe.PlaceholderShape.FlowerShrub:
                    Blades(b, sp, h * 0.85f, salt, Mathf.Max(3, Mathf.RoundToInt(sp.placeholderBlades * detail)), lod == 0 ? 3 : 2, 0.8f, sp.placeholderBladeWidth, 0.45f, 0.5f);
                    Heads(b, h, salt, 3, 0.16f, variant == 0 ? AccentA : AccentB);
                    break;
                case VegetationRecipe.PlaceholderShape.FlowerStems:
                    Blades(b, sp, h * 0.9f, salt, sp.placeholderBlades, lod == 0 ? sp.placeholderSegments : 2, 0.15f, Mathf.Max(0.018f, sp.placeholderBladeWidth), 0.8f);
                    Heads(b, h, salt + 3, sp.placeholderBlades, 0.07f, AccentA);
                    break;
                case VegetationRecipe.PlaceholderShape.Bamboo:
                    Sasa(b, sp, h, salt, Mathf.Max(3, Mathf.RoundToInt(sp.placeholderBlades * detail)), lod);
                    break;
                case VegetationRecipe.PlaceholderShape.Litter:
                    Litter(b, sp, salt, lod == 0 ? sp.placeholderBlades : 6);
                    break;
            }

            return b.Build(name);
        }

        /// <summary>
        /// WP-093 · AP-031 momiji litter: a patch of flat atlas cards (the momiji manifest content boxes at their real size) lying
        /// 2–6 mm up, R = 0 (litter does not bend). Per leaf: 30 % clusters (variant 1 may draw the wet cluster), else singles —
        /// 20 % underside, 15 % old, the rest fresh (LOOK-BIBLE §1 ML). LOD0 10 cards, LOD1 5.
        /// </summary>
        public static Mesh BuildCards(VegetationRecipe.Species sp, VegetationKit.Card[] cards, int variant, int lod)
        {
            var b = new Builder { Height = 0.01f };
            var singles = new List<int>(8);
            var under = new List<int>(4);
            var old = new List<int>(4);
            var clusters = new List<int>(4);
            for (int i = 0; i < cards.Length; i++)
            {
                VegetationKit.Card c = cards[i];
                if (c == null) continue;
                bool wet = c.tag != null && c.tag.IndexOf("wet", StringComparison.Ordinal) >= 0;
                if (c.cluster) { if (!wet || variant == 1) clusters.Add(i); }
                else if (c.tag == "underside") under.Add(i);
                else if (c.tag == "old") old.Add(i);
                else singles.Add(i);
            }

            if (singles.Count == 0) singles.AddRange(under.Count > 0 ? under : clusters);
            int salt = (int)sp.type * 977 + variant * 131 + 991;
            int count = lod == 0 ? 10 : 5;
            float r = sp.footprint * 0.5f;
            for (int k = 0; k < count; k++)
            {
                float pick = H(salt, k + 10);
                List<int> set = pick < 0.3f && clusters.Count > 0 ? clusters : pick < 0.44f && under.Count > 0 ? under : pick < 0.545f && old.Count > 0 ? old : singles;
                if (set.Count == 0) continue;
                VegetationKit.Card card = cards[set[Mathf.Min(set.Count - 1, (int)(H(salt, k + 20) * set.Count))]];
                float a = H(salt, k) * Mathf.PI * 2f;
                float rr = r * Mathf.Sqrt(H(salt, k + 50));
                var c = new Vector3(Mathf.Cos(a) * rr, Mathf.Lerp(0.002f, 0.006f, H(salt, k + 60)), Mathf.Sin(a) * rr);
                float yaw = H(salt, k + 70) * Mathf.PI * 2f;
                var ax = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var az = new Vector3(-ax.z, 0f, ax.x);
                float hx = Mathf.Max(0.01f, card.size.x) * 0.5f, hz = Mathf.Max(0.01f, card.size.y) * 0.5f;
                float phase = H(salt, k + 90);
                int v0 = Card(b, c - ax * hx - az * hz, ax, phase, card.uv.x, card.uv.y);
                int v1 = Card(b, c + ax * hx - az * hz, ax, phase, card.uv.z, card.uv.y);
                int v2 = Card(b, c - ax * hx + az * hz, ax, phase, card.uv.x, card.uv.w);
                int v3 = Card(b, c + ax * hx + az * hz, ax, phase, card.uv.z, card.uv.w);
                b.Quad(v0, v1, v2, v3);
            }

            var mesh = b.Build(VegetationTypes.AssetNames[(int)sp.type] + "_Cards" + (char)('A' + variant) + "_LOD" + lod);
            var tangents = new Vector4[mesh.vertexCount];
            for (int i = 0; i < tangents.Length; i++) tangents[i] = b.Tangents.Count > i ? b.Tangents[i] : new Vector4(1f, 0f, 0f, 1f);
            mesh.tangents = tangents;
            return mesh;
        }

        private static int Card(Builder b, Vector3 p, Vector3 tangent, float phase, float u, float v)
        {
            b.V.Add(p); b.N.Add(Vector3.up); b.C.Add(new Color(0f, phase, 1f, 1f)); b.U.Add(new Vector2(u, v));
            b.Tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
            return b.V.Count - 1;
        }

        private static float H(int salt, int k) => VegetationHash.Cell01(salt, k, 4242);

        /// <summary>Curved blades fanning out of the base: lean = tip offset as a fraction of the height.</summary>
        private static void Blades(Builder b, VegetationRecipe.Species sp, float h, int salt, int count, int segments, float lean, float width, float tipWidthFrac, float stiffOverride = -1f)
        {
            float radius = sp.footprint * 0.25f;
            for (int k = 0; k < count; k++)
            {
                float a = (k + H(salt, k) * 0.8f) / count * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 root = dir * (radius * Mathf.Sqrt(H(salt, k + 100)));
                float bh = h * Mathf.Lerp(0.7f, 1f, H(salt, k + 200));
                if (k == 0) bh = h;   // one blade reaches the top so tip R reaches 1 (contract test)
                float bl = lean * Mathf.Lerp(0.6f, 1.2f, H(salt, k + 300));
                float phase = H(salt, k + 400);
                float stiff = stiffOverride >= 0f ? stiffOverride : sp.stiffness;
                AddBlade(b, root, dir, bh, bl, width, tipWidthFrac, segments, phase, stiff, 1f, 0f);
            }
        }

        private static void AddBlade(Builder b, Vector3 root, Vector3 dir, float height, float lean, float width, float tipWidthFrac, int segments, float phase, float stiff, float rScale, float uOffset)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 normal = (Vector3.up + dir * 0.45f).normalized;
            int prevL = -1, prevR = -1;
            for (int j = 0; j <= segments; j++)
            {
                float t = j / (float)segments;
                float w = width * Mathf.Lerp(1f, tipWidthFrac, t) * (tipWidthFrac > 0.45f ? 0.7f + 0.5f * Mathf.Sin(t * Mathf.PI) : 1f);
                Vector3 c = root + Vector3.up * (height * t) + dir * (lean * height * t * t);
                int l = b.Add(c - side * (w * 0.5f), normal, phase, stiff, rScale, uOffset, t);
                int r = b.Add(c + side * (w * 0.5f), normal, phase, stiff, rScale, uOffset + 1f, t);
                if (j > 0) b.Quad(prevL, prevR, l, r);
                prevL = l;
                prevR = r;
            }
        }

        private static void Plumes(Builder b, VegetationRecipe.Species sp, float h, int salt, int count)
        {
            for (int k = 0; k < count; k++)
            {
                float a = H(salt, k + 500) * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 baseP = dir * 0.08f + Vector3.up * (h * 0.78f);
                AddCardAt(b, baseP, dir, h * 0.22f, 0.05f, H(salt, k + 600), 0f, AccentA, h);
            }
        }

        private static void Heads(Builder b, float h, int salt, int count, float size, float accentU)
        {
            for (int k = 0; k < count; k++)
            {
                float a = (k + H(salt, k + 700)) / count * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 c = dir * (count > 3 ? 0.06f : 0.18f) + Vector3.up * (h - size * 0.5f);
                // Two crossed upward-facing quads per head (TD reads the top).
                for (int q = 0; q < 2; q++)
                {
                    Vector3 ax = q == 0 ? new Vector3(dir.x, 0f, dir.z) : new Vector3(-dir.z, 0f, dir.x);
                    Vector3 az = Vector3.Cross(ax, Vector3.up);
                    Vector3 n = Vector3.up;
                    float phase = H(salt, k + 800);
                    int v0 = b.Add(c - ax * size * 0.5f - az * size * 0.5f + Vector3.up * size * 0.3f, n, phase, 0.5f, 1f, accentU, 0f);
                    int v1 = b.Add(c + ax * size * 0.5f - az * size * 0.5f + Vector3.up * size * 0.3f, n, phase, 0.5f, 1f, accentU, 0f);
                    int v2 = b.Add(c - ax * size * 0.5f + az * size * 0.5f + Vector3.up * size * 0.5f, n, phase, 0.5f, 1f, accentU, 1f);
                    int v3 = b.Add(c + ax * size * 0.5f + az * size * 0.5f + Vector3.up * size * 0.5f, n, phase, 0.5f, 1f, accentU, 1f);
                    b.Quad(v0, v1, v2, v3);
                }
            }
        }

        private static void AddCardAt(Builder b, Vector3 basePos, Vector3 dir, float length, float width, float phase, float stiff, float accentU, float h)
        {
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 n = (Vector3.up + dir * 0.45f).normalized;
            Vector3 tip = basePos + Vector3.up * length + dir * (length * 0.3f);
            int a = b.Add(basePos - side * width * 0.5f, n, phase, stiff, 1f, accentU, 0f);
            int c = b.Add(basePos + side * width * 0.5f, n, phase, stiff, 1f, accentU, 0f);
            int d = b.Add(tip - side * width * 0.3f, n, phase, stiff, 1f, accentU, 1f);
            int e = b.Add(tip + side * width * 0.3f, n, phase, stiff, 1f, accentU, 1f);
            b.Quad(a, c, d, e);
        }

        /// <summary>Kumazasa: stiff stems (low R) with two broad leaves at the top (full R).</summary>
        private static void Sasa(Builder b, VegetationRecipe.Species sp, float h, int salt, int count, int lod)
        {
            float radius = sp.footprint * 0.3f;
            for (int k = 0; k < count; k++)
            {
                float a = (k + H(salt, k) * 0.7f) / count * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 root = dir * (radius * Mathf.Sqrt(H(salt, k + 100)));
                float sh = h * Mathf.Lerp(0.55f, 0.75f, H(salt, k + 200));
                float phase = H(salt, k + 300);
                AddBlade(b, root, dir, sh, 0.15f, 0.02f, 0.8f, lod == 0 ? 3 : 1, phase, 1f, 0.3f, 0f);
                Vector3 top = root + Vector3.up * sh + dir * (0.15f * sh);
                int leaves = lod == 0 ? 2 : 1;
                for (int l = 0; l < leaves; l++)
                {
                    float la = a + (l == 0 ? 0.6f : -0.6f);
                    var ld = new Vector3(Mathf.Cos(la), 0f, Mathf.Sin(la));
                    float leafLen = (k == 0 && l == 0) ? h - sh : (h - sh) * Mathf.Lerp(0.7f, 1f, H(salt, k * 3 + l + 900));
                    AddBlade(b, top, ld, Mathf.Max(0.05f, leafLen), 2.2f, sp.placeholderBladeWidth, 0.35f, 2, phase, 0.6f, 1f, 0f);
                }
            }
        }

        /// <summary>Moss: a low dome disc (R = 0: moss does not move).</summary>
        private static void Patch(Builder b, VegetationRecipe.Species sp, float h, int salt, int ring)
        {
            float r = sp.footprint * 0.5f;
            int centre = b.V.Count;
            b.V.Add(Vector3.up * h); b.N.Add(Vector3.up); b.C.Add(new Color(0f, 0.5f, 1f, 1f)); b.U.Add(new Vector2(0.5f, 0.5f));
            for (int k = 0; k < ring; k++)
            {
                float a = k / (float)ring * Mathf.PI * 2f;
                float rr = r * Mathf.Lerp(0.7f, 1f, H(salt, k));
                var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                b.V.Add(p);
                b.N.Add((Vector3.up + p.normalized * 0.3f).normalized);
                b.C.Add(new Color(0f, 0.5f, 1f, 0.6f));
                b.U.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }

            for (int k = 0; k < ring; k++)
            {
                b.T.Add(centre);
                b.T.Add(centre + 1 + (k + 1) % ring);
                b.T.Add(centre + 1 + k);
            }
        }

        /// <summary>Momiji litter: flat leaves lying 2–6 mm up, scattered over the footprint (R = 0).</summary>
        private static void Litter(Builder b, VegetationRecipe.Species sp, int salt, int count)
        {
            float r = sp.footprint * 0.5f;
            for (int k = 0; k < count; k++)
            {
                float a = H(salt, k) * Mathf.PI * 2f;
                float rr = r * Mathf.Sqrt(H(salt, k + 50));
                var c = new Vector3(Mathf.Cos(a) * rr, Mathf.Lerp(0.002f, 0.006f, H(salt, k + 60)), Mathf.Sin(a) * rr);
                float yaw = H(salt, k + 70) * Mathf.PI * 2f;
                var ax = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var az = new Vector3(-ax.z, 0f, ax.x);
                float s = sp.placeholderBladeWidth * Mathf.Lerp(0.8f, 1.2f, H(salt, k + 80)) * 0.5f;
                float phase = H(salt, k + 90);
                int v0 = Flat(b, c - ax * s - az * s, phase, 0f, 0f);
                int v1 = Flat(b, c + ax * s - az * s, phase, 1f, 0f);
                int v2 = Flat(b, c - ax * s + az * s, phase, 0f, 1f);
                int v3 = Flat(b, c + ax * s + az * s, phase, 1f, 1f);
                b.Quad(v0, v1, v2, v3);
            }
        }

        private static int Flat(Builder b, Vector3 p, float phase, float u, float v)
        {
            b.V.Add(p); b.N.Add(Vector3.up); b.C.Add(new Color(0f, phase, 1f, 1f)); b.U.Add(new Vector2(u, v));
            return b.V.Count - 1;
        }

        /// <summary>LOD2: two crossed vertical cards (4 triangles).</summary>
        private static void CrossCard(Builder b, VegetationRecipe.Species sp, float h, int salt)
        {
            float w = sp.footprint * 0.5f;
            for (int q = 0; q < 2; q++)
            {
                Vector3 ax = q == 0 ? Vector3.right : Vector3.forward;
                Vector3 n = (Vector3.up + Vector3.Cross(ax, Vector3.up) * 0.3f).normalized;
                float phase = H(salt, q);
                int a = b.Add(-ax * w, n, phase, sp.stiffness, 1f, 0f, 0f);
                int c = b.Add(ax * w, n, phase, sp.stiffness, 1f, 1f, 0f);
                int d = b.Add(-ax * w * 0.8f + Vector3.up * h, n, phase, sp.stiffness, 1f, 0f, 1f);
                int e = b.Add(ax * w * 0.8f + Vector3.up * h, n, phase, sp.stiffness, 1f, 1f, 1f);
                b.Quad(a, c, d, e);
            }
        }
    }
}
