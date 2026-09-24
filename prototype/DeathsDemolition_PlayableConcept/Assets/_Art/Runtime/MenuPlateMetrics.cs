using System.Collections.Generic;
using DD.Prototype.UI;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-068 slice 6 · The menu / loading backdrop plates (Assets/_Art/Resources/MenuPlates): their composite and the T10
    /// text-safe measure (AD MUST 5) — TextSecondary over the left third under the SurfaceBase scrim at α 0.55, composited the
    /// way the UI draws it (sRGB source-over, UiContrast). Shared by the capture (MenuPlateCaptureTests) and the gate (MenuPlateTests).
    /// </summary>
    public static class MenuPlateMetrics
    {
        public const float ScrimAlpha = 0.55f;
        public const float TextSafeRight = 1f / 3f;
        public const float Threshold = 4.5f;
        /// <summary>T10 reads the 1st percentile: 99 % of the text-safe rectangle; the rest are sub-stroke specks (a fog line seen through a bay, a roof edge).</summary>
        public const float Percentile = 0.01f;

        /// <summary>Lantern breath (AD, post-capture round): Amber300 #F7C766 (WP-068 §2.1), normal "over" blend, peak α 0.35 × mask, 2.4 s between 0.55 and 1.0 of peak; the glow never enters the text-safe third.</summary>
        public static readonly Color GlowTint = new Color(0xF7 / 255f, 0xC7 / 255f, 0x66 / 255f, 1f);
        public const float GlowPeakAlpha = 0.35f, GlowBreathMin = 0.55f, GlowPeriod = 2.4f;
        public const int GlowClipX = 853;

        /// <summary>The frame with the lantern-breath mask drawn at breath peak (tint over, α = peak × mask; mask may be any resolution).</summary>
        public static Texture2D ApplyGlow(Texture2D frame, Texture2D mask, float breath)
        {
            int w = frame.width, h = frame.height, mw = mask.width, mh = mask.height;
            Color32[] px = frame.GetPixels32(), m = mask.GetPixels32();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = GlowPeakAlpha * breath * m[(y * mh / h) * mw + (x * mw / w)].r / 255f;
                if (a <= 0f) continue;
                Color32 c = px[y * w + x];
                px[y * w + x] = new Color32((byte)(GlowTint.r * 255f * a + c.r * (1f - a)), (byte)(GlowTint.g * 255f * a + c.g * (1f - a)), (byte)(GlowTint.b * 255f * a + c.b * (1f - a)), 255);
            }

            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>Far + mid + near (straight alpha) at the given horizontal parallax offsets, opaque RGB.</summary>
        public static Texture2D Composite(Texture2D far, Texture2D mid, Texture2D near, int dxFar, int dxMid, int dxNear)
        {
            int w = far.width, h = far.height;
            Color32[] f = far.GetPixels32(), m = mid.GetPixels32(), n = near.GetPixels32();
            var o = new Color32[f.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 a = f[y * w + Mathf.Clamp(x - dxFar, 0, w - 1)];
                Color32 bm = m[y * w + Mathf.Clamp(x - dxMid, 0, w - 1)];
                Color32 bn = n[y * w + Mathf.Clamp(x - dxNear, 0, w - 1)];
                o[y * w + x] = Over(bn, Over(bm, a));
            }

            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.SetPixels32(o);
            t.Apply(false, false);
            return t;
        }

        private static Color32 Over(Color32 top, Color32 under)
        {
            float a = top.a / 255f;
            return new Color32((byte)(top.r * a + under.r * (1f - a)), (byte)(top.g * a + under.g * (1f - a)), (byte)(top.b * a + under.b * (1f - a)), 255);
        }

        /// <summary>T10 over the left third: (1st-percentile ratio, worst ratio, % of pixels below 4.5:1).</summary>
        public static (float p1, float min, float belowPct) TextSafe(Texture2D frame)
        {
            Color scrim = UiTokens.Get(UiColor.SurfaceBase);
            scrim.a = ScrimAlpha;
            Color text = UiTokens.Get(UiColor.TextSecondary);
            Color32[] px = frame.GetPixels32();
            int w = frame.width, h = frame.height, right = Mathf.RoundToInt(w * TextSafeRight), below = 0;
            var ratios = new List<float>(right * h);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < right; x++)
            {
                float r = UiContrast.Ratio(text, UiContrast.Composite(scrim, px[y * w + x]));
                ratios.Add(r);
                if (r < Threshold) below++;
            }

            ratios.Sort();
            return (ratios[Mathf.FloorToInt(ratios.Count * Percentile)], ratios[0], 100f * below / ratios.Count);
        }

        /// <summary>The frame with the left-third scrim drawn and the 2217×1080 art-safe centre outlined (review aid).</summary>
        public static Texture2D Guide(Texture2D frame)
        {
            int w = frame.width, h = frame.height, third = Mathf.RoundToInt(w * TextSafeRight);
            Color scrim = UiTokens.Get(UiColor.SurfaceBase);
            scrim.a = ScrimAlpha;
            Color32[] px = frame.GetPixels32();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < third; x++) px[y * w + x] = UiContrast.Composite(scrim, px[y * w + x]);
            int sx = Mathf.RoundToInt(w * (1f - 2217f / 2560f) * 0.5f), sy = Mathf.RoundToInt(h * (1f - 1080f / 1440f) * 0.5f);
            var line = new Color32(215, 60, 50, 255);
            for (int x = sx; x < w - sx; x++) { px[sy * w + x] = line; px[(h - 1 - sy) * w + x] = line; }
            for (int y = sy; y < h - sy; y++) { px[y * w + sx] = line; px[y * w + w - 1 - sx] = line; px[y * w + third] = line; }
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }
    }
}
