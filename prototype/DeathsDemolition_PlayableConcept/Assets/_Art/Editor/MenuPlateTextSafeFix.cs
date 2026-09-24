using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-068 slice 6 · T10 after the linear audit (slice 8a, 5af6703f: UiContrast composites in linear light like the GPU): the
    /// shipped plates' brightest left-third specks measured 3.87:1 (p1). Applies <see cref="MenuPlateMetrics.ClampTextSafe"/> to
    /// the far / mid / near layers in place — the same step the capture now runs before writing — and re-records the manifest's
    /// T10 line with the linear measure at breath peak. The threshold stays 4.5.
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.MenuPlateTextSafeFix.ApplyCli</c>. Idempotent (a clamped plate changes 0 px).
    /// </summary>
    public static class MenuPlateTextSafeFix
    {
        public static void ApplyCli()
        {
            Debug.Log("[MenuPlateT10] " + Apply());
        }

        public static string Apply()
        {
            string root = ArtImportContract.MenuPlateRoot;
            var report = new StringBuilder();
            var layers = new Texture2D[3];
            string[] names = { ArtImportContract.MenuPlateFar, ArtImportContract.MenuPlateMid, ArtImportContract.MenuPlateNear };
            for (int i = 0; i < names.Length; i++)
            {
                string path = root + "/" + names[i] + ".png";
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(path));
                int changed = MenuPlateMetrics.ClampTextSafe(t);
                if (changed > 0)
                {
                    File.WriteAllBytes(path, t.EncodeToPNG());
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }

                layers[i] = t;
                report.Append(names[i]).Append(' ').Append(changed).Append(" px; ");
            }

            var mask = new Texture2D(2, 2);
            mask.LoadImage(File.ReadAllBytes(root + "/" + ArtImportContract.MenuPlateLanternMask + ".png"));
            Texture2D rest = MenuPlateMetrics.Composite(layers[0], layers[1], layers[2], 0, 0, 0);
            Texture2D peak = MenuPlateMetrics.ApplyGlow(rest, mask, 1f);
            (float p1, float min, float below) = MenuPlateMetrics.TextSafe(peak);

            string json = root + "/menu_plates.json";
            string t10 = "\"t10\": {\"token\": \"TextSecondary\", \"threshold\": " + F(MenuPlateMetrics.Threshold) + ", \"percentile\": 1, \"measured\": \"composite at breath peak, linear UiContrast\", \"clamp\": "
                         + F(MenuPlateMetrics.TextSafeTarget) + ", \"p1\": " + F(p1) + ", \"min\": " + F(min) + ", \"below_pct\": " + F(below) + "}";
            string text = File.ReadAllText(json);
            string updated = Regex.Replace(text, "\"t10\": \\{[^}]*\\}", t10);
            if (updated == text && !text.Contains(t10)) throw new InvalidDataException("menu_plates.json: no t10 record");
            File.WriteAllText(json, updated);
            AssetDatabase.ImportAsset(json, ImportAssetOptions.ForceUpdate);

            foreach (Texture2D t in layers) Object.DestroyImmediate(t);
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(rest);
            Object.DestroyImmediate(peak);
            return report + "T10 at breath peak p1 " + F(p1) + " min " + F(min) + " below " + F(below) + " %";
        }

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
