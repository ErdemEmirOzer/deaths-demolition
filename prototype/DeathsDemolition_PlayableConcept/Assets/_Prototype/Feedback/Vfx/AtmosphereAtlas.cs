using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B5 · The atmosphere grain atlas, drawn in code at start-up (direction §6 #6: no credits in this package, no new art
    /// files; the AP-010 pollen / petal sheets stay with the legacy drift prefabs). 512 × 256 RGBA32, 4 × 2 cells of 128², mip
    /// chain, linear (the RGB is a luminance shading multiplied by the particle's colour, the alpha is the shape):
    /// <list type="table">
    /// <item><term>0 <see cref="Mote"/></term><description>soft round dot (gaussian): dust, near-field mote</description></item>
    /// <item><term>1 <see cref="Pollen"/></term><description>brighter core with a faint halo ring: pollen, shaft motes</description></item>
    /// <item><term>2 <see cref="PetalYoung"/></term><description>sakura petal, broad with the notched tip, darker toward the base</description></item>
    /// <item><term>3 <see cref="PetalAged"/></term><description>curled petal: narrower, darker rim (the "yaş" variant, #E8A9BF tint)</description></item>
    /// <item><term>4 <see cref="LeafSliver"/></term><description>bamboo leaf: lanceolate along x with a mid-rib (quad aspect 10:3)</description></item>
    /// <item><term>5 <see cref="LeafBroad"/></term><description>five-lobed broad leaf (momiji / garden trees)</description></item>
    /// <item><term>6 <see cref="Wisp"/></term><description>steam wisp: two-octave value noise inside a soft radial falloff</description></item>
    /// <item><term>7 <see cref="Drip"/></term><description>water drip: capsule along u with a bright core (stretched billboard)</description></item>
    /// </list>
    /// Built once per process (≈ 0.7 MB with mips, DontSave); every value is deterministic (hash noise, no Random), so captures
    /// compare frame to frame. The single start-up allocation (the Color32 buffer) is released after Apply.
    /// </summary>
    public static class AtmosphereAtlas
    {
        public const int Width = 512, Height = 256, Cell = 128, Columns = 4, Rows = 2;
        public const byte Mote = 0, Pollen = 1, PetalYoung = 2, PetalAged = 3, LeafSliver = 4, LeafBroad = 5, Wisp = 6, Drip = 7;

        private static Texture2D s_texture;

        /// <summary>The shared atlas (built on first use).</summary>
        public static Texture2D Texture
        {
            get
            {
                if (s_texture == null) s_texture = Build();
                return s_texture;
            }
        }

        /// <summary>Builds a fresh atlas (tests call it directly; the game uses <see cref="Texture"/>).</summary>
        public static Texture2D Build()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, true, true)
            {
                name = "DD_AtmoAtlas (WP-073 B5, procedural)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[Width * Height];
            for (int frame = 0; frame < Columns * Rows; frame++)
            {
                int ox = (frame % Columns) * Cell, oy = (frame / Columns) * Cell;
                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    // Cell-local coordinates −1..1, a 3 px transparent border so mips never bleed a neighbour in.
                    float u = (x + 0.5f) / Cell * 2f - 1f, v = (y + 0.5f) / Cell * 2f - 1f;
                    bool border = x < 3 || y < 3 || x >= Cell - 3 || y >= Cell - 3;
                    Shade(frame, u, v, out float lum, out float alpha);
                    if (border) alpha = 0f;
                    byte l = (byte)Mathf.RoundToInt(Mathf.Clamp01(lum) * 255f);
                    pixels[(oy + y) * Width + ox + x] = new Color32(l, l, l, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);   // mips, then non-readable: the CPU copy goes
            return tex;
        }

        /// <summary>Luminance and alpha of one atlas cell at (u, v) ∈ [−1, 1]². Public for the EditMode shape test.</summary>
        public static void Shade(int frame, float u, float v, out float lum, out float alpha)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            switch (frame)
            {
                case Mote:
                    lum = 1f;
                    alpha = Mathf.Exp(-r * r * 5.5f);
                    return;

                case Pollen:
                {
                    float core = Mathf.Exp(-r * r * 14f);
                    float halo = 0.28f * Mathf.Exp(-Sq((r - 0.55f) * 7f));
                    lum = Mathf.Lerp(0.85f, 1f, core);
                    alpha = Mathf.Clamp01(core + halo);
                    return;
                }

                case PetalYoung:
                case PetalAged:
                {
                    // Teardrop along +v: widest at v ≈ 0.25, pointed base at v = −0.9, a notch cut into the tip.
                    bool aged = frame == PetalAged;
                    float halfWidth = (aged ? 0.42f : 0.55f) * Mathf.Sqrt(Mathf.Clamp01((v + 0.9f) / 1.7f)) * Mathf.Clamp01((0.95f - v) * 2.2f);
                    float inside = Smooth(halfWidth - Mathf.Abs(u), 0.06f);
                    float notch = 1f - Smooth(0.14f - Mathf.Abs(u) - (0.95f - v) * 0.9f, 0.04f);
                    alpha = inside * notch * Smooth(0.95f - v, 0.05f);
                    float edge = Mathf.Clamp01(Mathf.Abs(u) / Mathf.Max(halfWidth, 1e-3f));
                    lum = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01((v + 0.9f) / 1.2f)) * (aged ? Mathf.Lerp(1f, 0.8f, edge * edge) : Mathf.Lerp(1f, 0.93f, edge));
                    return;
                }

                case LeafSliver:
                {
                    // Lanceolate along u (the quad is 10:3), pointed both ends, a darker mid-rib.
                    float t = Mathf.Clamp01(1f - Mathf.Abs(u) / 0.94f);
                    float halfWidth = 0.78f * Mathf.Sqrt(t) * t;
                    alpha = Smooth(halfWidth - Mathf.Abs(v), 0.08f);
                    lum = Mathf.Lerp(0.78f, 1f, Smooth(Mathf.Abs(v) - 0.05f, 0.05f)) * Mathf.Lerp(0.9f, 1f, t);
                    return;
                }

                case LeafBroad:
                {
                    // Five lobes: a polar rose on a round body, a short stem at the bottom.
                    float a = Mathf.Atan2(u, v);
                    float lobes = 0.52f + 0.30f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2.5f)), 0.7f);
                    float body = Smooth(lobes - r, 0.05f) * Smooth(0.9f - r, 0.05f);
                    float stem = Smooth(0.05f - Mathf.Abs(u), 0.03f) * Smooth(-v - 0.2f, 0.03f) * Smooth(0.95f + v, 0.03f);
                    alpha = Mathf.Max(body, stem);
                    lum = Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(r * 1.6f));
                    return;
                }

                case Wisp:
                {
                    float n = 0.65f * Noise(u * 2.2f + 3.1f, v * 2.2f + 1.7f) + 0.35f * Noise(u * 5.1f + 9.4f, v * 5.1f + 4.2f);
                    alpha = Mathf.Clamp01(Mathf.Exp(-r * r * 2.4f) * Mathf.Lerp(0.35f, 1.25f, n));
                    lum = Mathf.Lerp(0.9f, 1f, n);
                    return;
                }

                default:   // Drip
                {
                    float du = Mathf.Max(Mathf.Abs(u) - 0.55f, 0f), dv = Mathf.Abs(v);
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    alpha = Smooth(0.42f - d, 0.12f);
                    lum = Mathf.Lerp(0.75f, 1f, Mathf.Exp(-dv * dv * 18f));
                    return;
                }
            }
        }

        private static float Sq(float x) => x * x;

        /// <summary>0 below −w, 1 above +w, smooth between (a signed-distance edge of half-width w).</summary>
        private static float Smooth(float d, float w)
        {
            float t = Mathf.Clamp01((d + w) / (2f * w));
            return t * t * (3f - 2f * t);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        private static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx);
            float b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_texture = null;   // DontSave: survives a domain-reload-free play session; rebuilt on demand
        }
    }
}
