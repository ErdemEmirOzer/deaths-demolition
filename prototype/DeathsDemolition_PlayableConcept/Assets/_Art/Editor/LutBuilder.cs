using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-029 · The look recipe as a LUT (ADR-029-1). Lightroom edits an 8-bit sRGB identity
    /// strip exactly as it edits the reference frames; URP's Color Lookup (LDR grading) samples
    /// the strip with the tonemapped, graded frame's sRGB-coded colour as the coordinate and
    /// takes the texel's bytes as the sRGB-coded output — so a strip that Lightroom processed
    /// with the same global tone/colour recipe reproduces the recipe in-engine, with no
    /// gamma round-trip as long as the texture imports linear (sRGB off, ArtImportContract.ApplyLut).
    ///
    /// Layout = URP's (ApplyLut2D): width = size², height = size; blue selects the size-wide
    /// block along x, red runs across the block, green runs UP the image (v = 0 at the bottom row).
    ///
    ///   Assets/_Art/Lighting/LUT/DD_Neutral_32.png    1024×32 identity (pipeline LUT size 32)
    ///   Assets/_Art/Lighting/LUT/DD_Neutral_64.png    4096×64 identity (only if the pipeline moves to 64)
    ///   Assets/_Art/Lighting/LUT/DD_Look_v1.png       the chosen recipe applied to the 32 strip
    ///
    /// <see cref="Apply"/> samples a strip on the CPU the way the shader does (trilinear in the
    /// two nearest blue blocks) and <see cref="MeanDeltaE"/> scores a Lightroom frame against the
    /// LUT-processed one in CIE76 ΔE — the ADR-029-1 acceptance (mean ≤ 6).
    /// </summary>
    public static class LutBuilder
    {
        private const string LogPrefix = "[LutBuilder] ";
        public const string Folder = ArtImportContract.LutRoot;
        public const string Neutral32 = Folder + "/DD_Neutral_32.png";
        public const string Neutral64 = Folder + "/DD_Neutral_64.png";
        public const string Look = Folder + "/DD_Look_v1.png";
        public const string RecipeJson = Folder + "/DD_Look_v1.recipe.json";
        public const int PipelineLutSize = 32;
        /// <summary>ADR-029-1 acceptance: mean CIE76 ΔE between the Lightroom-graded frame and the LUT-graded frame.</summary>
        public const float MaxMeanDeltaE = 6f;

        // ================================================================== neutral strips

        public static void BuildNeutralStrips()
        {
            EnsureFolder(Folder);
            WriteNeutral(32, Neutral32);
            WriteNeutral(64, Neutral64);
            AssetDatabase.ImportAsset(Neutral32, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(Neutral64, ImportAssetOptions.ForceUpdate);
            Debug.Log(LogPrefix + "neutral strips written: " + Neutral32 + " (1024×32), " + Neutral64 + " (4096×64)");
        }

        /// <summary>The identity strip as 8-bit RGBA rows (row 0 = top of the PNG = green 1).</summary>
        public static Color32[] NeutralPixels(int size, out int width, out int height)
        {
            width = size * size;
            height = size;
            var px = new Color32[width * height];
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;   // PNG row 0 is the top; texture v = 1 there
                byte g = (byte)Mathf.RoundToInt(255f * y / (size - 1));
                for (int x = 0; x < width; x++)
                {
                    int block = x / size, r = x % size;
                    px[row * width + x] = new Color32((byte)Mathf.RoundToInt(255f * r / (size - 1)), g, (byte)Mathf.RoundToInt(255f * block / (size - 1)), 255);
                }
            }

            return px;
        }

        private static void WriteNeutral(int size, string path)
        {
            Color32[] px = NeutralPixels(size, out int w, out int h);
            // Texture2D rows run bottom → top; flip so the file's top row is the last texture row.
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var flipped = new Color32[px.Length];
            for (int row = 0; row < h; row++) Array.Copy(px, row * w, flipped, (h - 1 - row) * w, w);
            tex.SetPixels32(flipped);
            tex.Apply(false, false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ================================================================== CPU LUT

        /// <summary>A decoded strip: <c>size</c> and rows top-first (PNG order).</summary>
        public sealed class Strip
        {
            public int size;
            public int width;
            public int height;
            public Color32[] rows;   // PNG order (row 0 = top)

            public bool IsValid => rows != null && width == size * size && height == size && rows.Length == width * height;

            /// <summary>Texel at texture coordinates (x from the left, y from the BOTTOM).</summary>
            public Color32 Texel(int x, int y)
            {
                x = Mathf.Clamp(x, 0, width - 1);
                y = Mathf.Clamp(y, 0, height - 1);
                return rows[(height - 1 - y) * width + x];
            }
        }

        public static Strip Load(string pngPath)
        {
            byte[] bytes = File.ReadAllBytes(pngPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!tex.LoadImage(bytes, false)) throw new InvalidDataException(LogPrefix + "cannot decode " + pngPath);
                Color32[] px = tex.GetPixels32();   // bottom → top
                var strip = new Strip { width = tex.width, height = tex.height, size = tex.height, rows = new Color32[px.Length] };
                for (int row = 0; row < tex.height; row++) Array.Copy(px, (tex.height - 1 - row) * tex.width, strip.rows, row * tex.width, tex.width);
                return strip;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        public static Color32[] LoadPixels(string pngPath, out int width, out int height)
        {
            byte[] bytes = File.ReadAllBytes(pngPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!tex.LoadImage(bytes, false)) throw new InvalidDataException(LogPrefix + "cannot decode " + pngPath);
                width = tex.width;
                height = tex.height;
                return tex.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// URP ApplyLut2D on the CPU: input sRGB-coded colour c ∈ [0,1]³ → trilinear sample.
        /// uvw = c·(size−1); the blue slice picks the block, x/y inside the block interpolate
        /// bilinearly (the texture's bilinear filter), the two blue slices interpolate linearly.
        /// </summary>
        public static Color32 Sample(Strip lut, Color32 c)
        {
            int n = lut.size;
            float r = c.r / 255f * (n - 1), g = c.g / 255f * (n - 1), b = c.b / 255f * (n - 1);
            int b0 = Mathf.Clamp(Mathf.FloorToInt(b), 0, n - 1), b1 = Mathf.Min(b0 + 1, n - 1);
            float fb = b - b0;
            Vector3 s0 = Bilinear(lut, b0, r, g), s1 = Bilinear(lut, b1, r, g);
            Vector3 v = Vector3.Lerp(s0, s1, fb);
            return new Color32((byte)Mathf.RoundToInt(Mathf.Clamp(v.x, 0f, 255f)), (byte)Mathf.RoundToInt(Mathf.Clamp(v.y, 0f, 255f)), (byte)Mathf.RoundToInt(Mathf.Clamp(v.z, 0f, 255f)), 255);
        }

        private static Vector3 Bilinear(Strip lut, int block, float r, float g)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(r), 0, lut.size - 1), x1 = Mathf.Min(x0 + 1, lut.size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(g), 0, lut.size - 1), y1 = Mathf.Min(y0 + 1, lut.size - 1);
            float fx = r - x0, fy = g - y0;
            int bx = block * lut.size;
            Vector3 c00 = V(lut.Texel(bx + x0, y0)), c10 = V(lut.Texel(bx + x1, y0)), c01 = V(lut.Texel(bx + x0, y1)), c11 = V(lut.Texel(bx + x1, y1));
            return Vector3.Lerp(Vector3.Lerp(c00, c10, fx), Vector3.Lerp(c01, c11, fx), fy);
        }

        private static Vector3 V(Color32 c) => new Vector3(c.r, c.g, c.b);

        /// <summary>The frame with the strip applied (same pixel order as the input).</summary>
        public static Color32[] Apply(Strip lut, Color32[] frame)
        {
            var result = new Color32[frame.Length];
            var cache = new Dictionary<int, Color32>(65536);
            for (int i = 0; i < frame.Length; i++)
            {
                Color32 c = frame[i];
                int key = (c.r << 16) | (c.g << 8) | c.b;
                if (!cache.TryGetValue(key, out Color32 o))
                {
                    o = Sample(lut, c);
                    cache[key] = o;
                }

                result[i] = o;
            }

            return result;
        }

        // ================================================================== ΔE

        /// <summary>Mean CIE76 ΔE (sRGB → linear → XYZ D65 → Lab) over every pixel; also the 95th percentile and the max.</summary>
        public static float MeanDeltaE(Color32[] a, Color32[] b, out float p95, out float max)
        {
            if (a.Length != b.Length) throw new ArgumentException(LogPrefix + "frames differ in size");
            var d = new float[a.Length];
            double sum = 0;
            max = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                Vector3 la = Lab(a[i]), lb = Lab(b[i]);
                float e = Vector3.Distance(la, lb);
                d[i] = e;
                sum += e;
                if (e > max) max = e;
            }

            Array.Sort(d);
            p95 = d.Length > 0 ? d[Mathf.Clamp(Mathf.RoundToInt(0.95f * (d.Length - 1)), 0, d.Length - 1)] : 0f;
            return a.Length > 0 ? (float)(sum / a.Length) : 0f;
        }

        public static Vector3 Lab(Color32 c)
        {
            float r = SrgbToLinear(c.r / 255f), g = SrgbToLinear(c.g / 255f), b = SrgbToLinear(c.b / 255f);
            float x = (0.4124564f * r + 0.3575761f * g + 0.1804375f * b) / 0.95047f;
            float y = 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;
            float z = (0.0193339f * r + 0.1191920f * g + 0.9503041f * b) / 1.08883f;
            float fx = F(x), fy = F(y), fz = F(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
        private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        // ================================================================== CLI

        /// <summary>
        /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.LutBuilder.MeasureCli -ddLut &lt;strip.png&gt; -ddSource &lt;frame-post.png&gt; -ddTarget &lt;frame-lightroom.png&gt; [-ddOut &lt;json&gt;]</c>.
        /// Applies the strip to the source frame, scores it against the Lightroom frame, writes the LUT-processed PNG next to the JSON.
        /// </summary>
        public static void MeasureCli()
        {
            string lutPath = Arg("-ddLut"), source = Arg("-ddSource"), target = Arg("-ddTarget"), outJson = Arg("-ddOut");
            if (string.IsNullOrEmpty(lutPath) || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) throw new ArgumentException(LogPrefix + "-ddLut, -ddSource and -ddTarget are required");
            Result r = Measure(lutPath, source, target, string.IsNullOrEmpty(outJson) ? null : Path.ChangeExtension(outJson, ".lut.png"));
            string json = JsonUtility.ToJson(r, true);
            if (!string.IsNullOrEmpty(outJson)) File.WriteAllText(outJson, json + "\n", new UTF8Encoding(false));
            Debug.Log(LogPrefix + "ΔE " + r.meanDeltaE.ToString("F2", CultureInfo.InvariantCulture) + " (p95 " + r.p95DeltaE.ToString("F2", CultureInfo.InvariantCulture) + ", max " + r.maxDeltaE.ToString("F2", CultureInfo.InvariantCulture) + ") " + source + " ↔ " + target + " via " + lutPath + (r.pass ? " PASS" : " FAIL"));
        }

        [Serializable]
        public sealed class Result
        {
            public string lut;
            public string source;
            public string target;
            public int width;
            public int height;
            public int lutSize;
            public float meanDeltaE;
            public float p95DeltaE;
            public float maxDeltaE;
            public float sourceVsTargetDeltaE;   // how far the recipe moved the frame (context for the residual)
            public bool pass;
        }

        public static Result Measure(string lutPath, string sourcePng, string targetPng, string lutProcessedPngOut)
        {
            Strip lut = Load(lutPath);
            if (!lut.IsValid) throw new InvalidDataException(LogPrefix + lutPath + " is not a size²×size strip (" + lut.width + "×" + lut.height + ")");
            Color32[] src = LoadPixels(sourcePng, out int w, out int h);
            Color32[] tgt = LoadPixels(targetPng, out int tw, out int th);
            if (tw != w || th != h) throw new InvalidDataException(LogPrefix + "source " + w + "×" + h + " and target " + tw + "×" + th + " differ");
            Color32[] graded = Apply(lut, src);
            float mean = MeanDeltaE(graded, tgt, out float p95, out float max);
            float moved = MeanDeltaE(src, tgt, out _, out _);
            if (!string.IsNullOrEmpty(lutProcessedPngOut))
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                tex.SetPixels32(graded);
                tex.Apply(false, false);
                File.WriteAllBytes(lutProcessedPngOut, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }

            return new Result { lut = lutPath, source = sourcePng, target = targetPng, width = w, height = h, lutSize = lut.size, meanDeltaE = mean, p95DeltaE = p95, maxDeltaE = max, sourceVsTargetDeltaE = moved, pass = mean <= MaxMeanDeltaE };
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }
    }
}
