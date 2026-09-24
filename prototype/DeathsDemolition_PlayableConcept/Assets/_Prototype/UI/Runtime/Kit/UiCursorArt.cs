using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// M4-R1 N21 · builds a hardware-cursor texture at a given arrow height from the V1 64 px master (ui-ux-designer
    /// ruling). Plain resampling would shrink the master's 4 px Sumi outline to a grey 1 px fringe, and the cream fill
    /// alone reads ≈ 1.4:1 on plaster in linear light, so the outline is drawn again:
    /// - the master's cream interior is scaled so the arrow is <c>arrowHeight</c> tall with its outline;
    /// - the outline is <c>max(2, ceil(0.07 × H))</c> px of solid ink around it (a disc dilation);
    /// - both are worked at 4× and area-averaged down, so the edges stay anti-aliased;
    /// - the arrow's tip lands on <see cref="UiCursor.Hotspot"/> at every size;
    /// - Press is the same shape with the inks swapped (Sumi fill, Cream outline): it never shrinks on press.
    /// Runs once per size and state (UiCursor caches the result); never per frame.
    /// </summary>
    public static class UiCursorArt
    {
        private const int Super = 4;

        /// <summary>The outline width in px for an arrow height.</summary>
        public static int OutlineFor(int arrowHeight)
        {
            return Mathf.Max(2, Mathf.CeilToInt(0.07f * arrowHeight));
        }

        public static Texture2D Build(Texture2D master, int arrowHeight, bool pressed)
        {
            int mw = master.width;
            int mh = master.height;
            Color32[] src = master.GetPixels32();

            // Master masks in top-down rows (GetPixels32 is bottom-up): the cream interior, and the two ink colours.
            var fill = new float[mw * mh];
            Color light = Color.clear, dark = Color.clear;
            float lightN = 0f, darkN = 0f;
            int xMin = mw, yMin = mh, yMax = -1;
            for (int y = 0; y < mh; y++)
            {
                for (int x = 0; x < mw; x++)
                {
                    Color32 c = src[(mh - 1 - y) * mw + x];
                    float a = c.a / 255f;
                    float l = (c.r + c.g + c.b) / (3f * 255f);
                    float f = a * Mathf.Clamp01((l - 0.3f) / 0.4f);
                    fill[y * mw + x] = f;
                    if (a > 0.98f && l > 0.8f)
                    {
                        light += (Color)c;
                        lightN++;
                    }
                    else if (a > 0.98f && l < 0.2f)
                    {
                        dark += (Color)c;
                        darkN++;
                    }

                    if (f > 0.5f)
                    {
                        if (x < xMin) xMin = x;
                        if (y < yMin) yMin = y;
                        if (y > yMax) yMax = y;
                    }
                }
            }

            if (yMax < 0)
            {
                return null;
            }

            Color cream = lightN > 0f ? light / lightN : Color.white;
            Color sumi = darkN > 0f ? dark / darkN : Color.black;
            cream.a = 1f;
            sumi.a = 1f;
            Color inner = pressed ? sumi : cream;
            Color outer = pressed ? cream : sumi;

            int size = UiCursor.TextureSize;
            int w = OutlineFor(arrowHeight);
            float scale = (arrowHeight - 2f * w) / (yMax - yMin + 1);
            // The shape's top-left (the tip) at (hotspot - 1): the fill's tip sits one outline further in.
            float originX = UiCursor.Hotspot.x - 1f + w;
            float originY = UiCursor.Hotspot.y - 1f + w;

            int n = size * Super;
            var fillHi = new bool[n * n];
            for (int hy = 0; hy < n; hy++)
            {
                for (int hx = 0; hx < n; hx++)
                {
                    float mx = ((hx + 0.5f) / Super - originX) / scale + xMin;
                    float my = ((hy + 0.5f) / Super - originY) / scale + yMin;
                    fillHi[hy * n + hx] = Sample(fill, mw, mh, mx, my) > 0.5f;
                }
            }

            // The outline: every hi-res cell within w px of the fill.
            int r = w * Super;
            int r2 = r * r;
            var shapeHi = new bool[n * n];
            for (int hy = 0; hy < n; hy++)
            {
                for (int hx = 0; hx < n; hx++)
                {
                    if (!fillHi[hy * n + hx])
                    {
                        continue;
                    }

                    int y0 = Mathf.Max(0, hy - r), y1 = Mathf.Min(n - 1, hy + r);
                    int x0 = Mathf.Max(0, hx - r), x1 = Mathf.Min(n - 1, hx + r);
                    for (int y = y0; y <= y1; y++)
                    {
                        int dy = y - hy;
                        for (int x = x0; x <= x1; x++)
                        {
                            int dx = x - hx;
                            if (dx * dx + dy * dy <= r2)
                            {
                                shapeHi[y * n + x] = true;
                            }
                        }
                    }
                }
            }

            var pixels = new Color32[size * size];
            float cell = Super * Super;
            for (int ty = 0; ty < size; ty++)
            {
                for (int tx = 0; tx < size; tx++)
                {
                    int f = 0, s = 0;
                    for (int sy = 0; sy < Super; sy++)
                    {
                        for (int sx = 0; sx < Super; sx++)
                        {
                            int i = (ty * Super + sy) * n + tx * Super + sx;
                            if (fillHi[i]) f++;
                            if (shapeHi[i] || fillHi[i]) s++;
                        }
                    }

                    float alpha = s / cell;
                    Color c = s > 0 ? Color.Lerp(outer, inner, (float)f / s) : Color.clear;
                    c.a = alpha;
                    pixels[(size - 1 - ty) * size + tx] = c;   // back to bottom-up rows
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Cursor." + (pressed ? "Press" : "Arrow") + "." + arrowHeight,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);   // stays readable: Cursor.SetCursor reads it
            return texture;
        }

        /// <summary>Bilinear sample of a top-down mask; 0 outside.</summary>
        private static float Sample(float[] mask, int w, int h, float x, float y)
        {
            x -= 0.5f;
            y -= 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = At(mask, w, h, x0, y0), b = At(mask, w, h, x0 + 1, y0);
            float c = At(mask, w, h, x0, y0 + 1), d = At(mask, w, h, x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float At(float[] mask, int w, int h, int x, int y)
        {
            return x < 0 || y < 0 || x >= w || y >= h ? 0f : mask[y * w + x];
        }
    }
}
