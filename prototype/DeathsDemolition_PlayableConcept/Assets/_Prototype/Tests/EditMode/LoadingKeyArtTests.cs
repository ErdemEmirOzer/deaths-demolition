using System.IO;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 R1-D · the loading key art is full and rich, so its readability is proven where the words sit (C-00 25.09,
    /// the round-end wallpaper's method: the brightest pixel under each word). For each screen the curtain meets -
    /// 16:9 1080, the Mac 3024×1964 at UI 1.2 × text 1.25, 16:10 and 21:9 - the ring, the label stack and the tip strip are
    /// laid out from UiCurtain's own numbers, mapped through its cover-fit at push-in 1.00 and 1.05, and sampled on the file:
    ///   ring    the cream stroke (TextPrimary) ≥ 3:1 on the brightest pixel (WCAG 1.4.11: a graphic);
    ///   labels  TextSecondary (the dimmest word there: eyebrow, subline) ≥ 4.5:1 on the brightest pixel;
    ///   tip     TextPrimary ≥ 7:1 on the strip (SurfaceBase 0.72, composited as the GPU does) over the brightest pixel.
    /// </summary>
    public sealed class LoadingKeyArtTests
    {
        private const string SourcePath = "Assets/_Prototype/UI/Art/Resources/UI/Wallpapers/loading_keyart.jpg";

        /// <summary>Half the label stack's width at text 1.0 (reference px). The widest destination, BEFORE THE RUN, is ~130 at Mac × 1.25.</summary>
        private const float LabelHalfWidth = 180f;

        /// <summary>The tip strip's tint (UiCurtain.BuildTipStrip) and the most lines a tip wraps to (text 1.25).</summary>
        private const float TipStripAlpha = 0.72f;
        private const int TipLines = 3;

        private static readonly float[] PushScales = { 1f, UiCurtain.KeyArtPushScale };

        private static readonly object[] Screens =
        {
            new object[] { "1080", 1920, 1080, 1f, 1f },
            new object[] { "mac", 3024, 1964, 1.2f, 1.25f },
            new object[] { "16x10", 2560, 1600, 1f, 1f },
            new object[] { "21x9", 2560, 1080, 1f, 1f }
        };

        private static Texture2D s_art;

        [OneTimeSetUp]
        public void Load()
        {
            s_art = new Texture2D(2, 2);
            Assert.IsTrue(s_art.LoadImage(File.ReadAllBytes(SourcePath)), SourcePath);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            Object.DestroyImmediate(s_art);
        }

        [TestCaseSource(nameof(Screens))]
        public void AnahtarSanat_KelimelerinAltindaEnParlakPikselde_Okunur(string name, int width, int height, float uiScale, float textScale)
        {
            // The canvas the curtain draws on (UiFactory: reference 1920×1080 / UI scale, Expand).
            Vector2 reference = new Vector2(1920f, 1080f) / uiScale;
            float fit = Mathf.Min(width / reference.x, height / reference.y);
            Vector2 canvas = new Vector2(width / fit, height / fit);
            Rect uv = UiCurtain.CoverUv(canvas.x / canvas.y, (float)s_art.width / s_art.height);

            // UiCurtain's layout, canvas px from the bottom-left.
            Vector2 ring = new Vector2(canvas.x * 0.5f, canvas.y * 0.5f + UiCurtain.RingLift);
            float half = UiCurtain.RingSize * 0.5f;
            Rect ringRect = new Rect(ring.x - half, ring.y - half, UiCurtain.RingSize, UiCurtain.RingSize);
            float labelsTop = ring.y - half - (int)UiSpace.XL;
            float labelsHeight = (UiFactory.LineHeight(UiType.Label) * 2f + (int)UiSpace.S + UiFactory.LineHeight(UiType.H2)) * textScale;
            float labelsHalf = LabelHalfWidth * textScale;
            Rect labelRect = new Rect(ring.x - labelsHalf, labelsTop - labelsHeight, labelsHalf * 2f, labelsHeight);
            float tipWidth = Mathf.Min(UiCurtain.TipMaxWidth, canvas.x - UiCurtain.TipMargin * 2f);
            float tipHeight = TipLines * UiFactory.LineHeight(UiType.Body) * textScale + (int)UiSpace.M * 2f;
            Rect tipRect = new Rect(UiCurtain.TipMargin, UiCurtain.TipMargin, tipWidth, tipHeight);

            Color ringBg = Brightest(ringRect, canvas, uv);
            Color labelBg = Brightest(labelRect, canvas, uv);
            Color tipBg = Brightest(tipRect, canvas, uv);
            Color strip = UiTokens.Get(UiColor.SurfaceBase);
            strip.a = TipStripAlpha;
            Color tipComposite = UiContrast.Composite(strip, tipBg);

            float ringRatio = UiContrast.Ratio(UiTokens.Get(UiColor.TextPrimary), ringBg);
            float labelRatio = UiContrast.Ratio(UiTokens.Get(UiColor.TextSecondary), labelBg);
            float tipRatio = UiContrast.Ratio(UiTokens.Get(UiColor.TextPrimary), tipComposite);
            Debug.Log("[DD-TEST] r1d.keyart " + name + " ring=" + ringRatio.ToString("F2") + " labels=" + labelRatio.ToString("F2") + " tip=" + tipRatio.ToString("F2"));
            Assert.GreaterOrEqual(ringRatio, 3f, name + ": the ring's cream stroke on the brightest pixel under it");
            Assert.GreaterOrEqual(labelRatio, 4.5f, name + ": TextSecondary on the brightest pixel under the labels");
            Assert.GreaterOrEqual(tipRatio, 7f, name + ": TextPrimary on the tip strip over the brightest pixel under it");
        }

        /// <summary>The brightest texel under a canvas rect, through the cover-fit uvRect, at every push-in scale.</summary>
        private static Color Brightest(Rect rect, Vector2 canvas, Rect uv)
        {
            Color brightest = Color.black;
            float max = -1f;
            foreach (float push in PushScales)
            {
                // The art scales about its centre: a screen point p shows the image at 0.5 + (p - 0.5) / push.
                Vector2 min = ToUv(new Vector2(rect.xMin / canvas.x, rect.yMin / canvas.y), push, uv);
                Vector2 max2 = ToUv(new Vector2(rect.xMax / canvas.x, rect.yMax / canvas.y), push, uv);
                int x0 = Mathf.Clamp(Mathf.FloorToInt(min.x * s_art.width), 0, s_art.width - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(max2.x * s_art.width), x0 + 1, s_art.width);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(min.y * s_art.height), 0, s_art.height - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(max2.y * s_art.height), y0 + 1, s_art.height);
                Color[] block = s_art.GetPixels(x0, y0, x1 - x0, y1 - y0);
                for (int i = 0; i < block.Length; i++)
                {
                    float l = UiContrast.RelativeLuminance(block[i]);
                    if (l > max)
                    {
                        max = l;
                        brightest = block[i];
                    }
                }
            }

            return brightest;
        }

        private static Vector2 ToUv(Vector2 screen, float push, Rect uv)
        {
            Vector2 local = new Vector2(0.5f + (screen.x - 0.5f) / push, 0.5f + (screen.y - 0.5f) / push);
            return new Vector2(uv.xMin + local.x * uv.width, uv.yMin + local.y * uv.height);
        }
    }
}
