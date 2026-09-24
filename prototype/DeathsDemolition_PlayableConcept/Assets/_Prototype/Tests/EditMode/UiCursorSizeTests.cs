using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// M4-R1 N21 (user 24.09: "the in-game cursor is too big"): the pointer and the reticle at the ui-ux-designer's sizes.
    /// </summary>
    public sealed class UiCursorSizeTests
    {
        [Test]
        public void ImlecBoyKurali_MacNoktaSabit_DigerleriOlcekle24ile31Arasi()
        {
            Assert.AreEqual(28, UiCursor.ArrowHeightFor(RuntimePlatform.OSXPlayer, 220f, 1964), "macOS: 28 pt (bugün 63 pt)");
            Assert.AreEqual(28, UiCursor.ArrowHeightFor(RuntimePlatform.OSXPlayer, 72f, 1080), "macOS: nokta zaten normalize");
            Assert.AreEqual(24, UiCursor.ArrowHeightFor(RuntimePlatform.WindowsPlayer, 96f, 1080), "Windows %100");
            Assert.AreEqual(30, UiCursor.ArrowHeightFor(RuntimePlatform.WindowsPlayer, 120f, 1440), "Windows %125");
            Assert.AreEqual(31, UiCursor.ArrowHeightFor(RuntimePlatform.WindowsPlayer, 192f, 2160), "4K %200 → tavan 31");
            Assert.AreEqual(24, UiCursor.ArrowHeightFor(RuntimePlatform.WindowsPlayer, 0f, 1080), "dpi yok → yükseklik oranı");
            Assert.AreEqual(24, UiCursor.ArrowHeightFor(RuntimePlatform.LinuxPlayer, 96f, 1080), "Linux sabit 96 → yükseklik oranı");
            Assert.AreEqual(31, UiCursor.ArrowHeightFor(RuntimePlatform.LinuxPlayer, 96f, 2160), "Linux 4K → tavan");
        }

        [Test]
        public void ImlecDokusu_OkBoyu_Uc_Kontur_BasiliMurekkepTers()
        {
            foreach (int h in new[] { 24, 28, 30, 31 })
            {
                foreach (bool pressed in new[] { false, true })
                {
                    Texture2D t = UiCursor.Sized(pressed, h);
                    string id = "H" + h + (pressed ? " press" : " arrow");
                    Assert.IsNotNull(t, id + ": V1 ana görseli yok");
                    Assert.AreEqual(UiCursor.TextureSize, t.width, id);
                    Assert.AreEqual(UiCursor.TextureSize, t.height, id);
                    Assert.AreEqual(TextureFormat.RGBA32, t.format, id + " donanım imleci RGBA32");
                    Assert.IsTrue(t.isReadable, id + " Cursor.SetCursor okunabilir doku ister");

                    Color32[] px = t.GetPixels32();
                    int s = t.width;
                    int xMin = s, yMin = s, yMax = -1, dark = 0, light = 0;
                    for (int y = 0; y < s; y++)
                    {
                        for (int x = 0; x < s; x++)
                        {
                            Color32 c = px[(s - 1 - y) * s + x];   // top-down
                            if (c.a < 128)
                            {
                                continue;
                            }

                            xMin = Mathf.Min(xMin, x);
                            yMin = Mathf.Min(yMin, y);
                            yMax = Mathf.Max(yMax, y);
                            float l = (c.r + c.g + c.b) / (3f * 255f);
                            if (c.a > 240 && l < 0.25f) dark++;
                            if (c.a > 240 && l > 0.75f) light++;
                        }
                    }

                    Assert.AreEqual(h, yMax - yMin + 1, 1, id + ": ok yüksekliği");
                    Assert.LessOrEqual(xMin, 2, id + ": uç sol üstte");
                    Assert.LessOrEqual(yMin, 1, id + ": uç sol üstte");
                    Color32 hot = px[(s - 1 - (int)UiCursor.Hotspot.y) * s + (int)UiCursor.Hotspot.x];
                    Assert.Greater(hot.a, 128, id + ": sıcak nokta (2,1) okun ucunda, dolu");
                    Assert.Greater(dark, h, id + ": düz Sumi kontur (≥ 2 px) - krem tek başına sıvada 1,4:1");
                    Assert.Greater(light, h, id + ": krem gövde");

                    // Press: the same bounds, the inks swapped - the arrow's middle is dark.
                    int midY = (yMin + yMax) / 2;
                    int midX = xMin + (yMax - yMin) / 5;
                    Color32 mid = px[(s - 1 - midY) * s + midX];
                    float midL = (mid.r + mid.g + mid.b) / (3f * 255f);
                    Assert.Greater(mid.a, 240, id + ": gövde ortası dolu");
                    if (pressed)
                    {
                        Assert.Less(midL, 0.25f, id + ": basılı = Sumi gövde");
                    }
                    else
                    {
                        Assert.Greater(midL, 0.75f, id + ": ok = krem gövde");
                    }
                }
            }
        }

        [Test]
        public void Nisangah_TikUIOlcegindenBagimsiz_FizikselTaban2x6()
        {
            Vector2 tick, arc;
            HudReticle.TickGeometry(1f, 1f, out tick, out arc);
            Assert.AreEqual(new Vector2(2f, 7f), tick, "1080p UI 1: 2 × 7 (eski 3 × 10)");
            Assert.AreEqual(16f, arc.x, 0.001f, "doygun yay 16 (eski 22)");

            // Mac panel at UI 1.2: 1.89 px per unit (Expand) → ≈ 3 × 11 physical, not 6 × 19 as before.
            HudReticle.TickGeometry(1.2f, 1.89f, out tick, out arc);
            Assert.AreEqual(2f * 1.89f / 1.2f, tick.x * 1.89f, 0.01f);
            Assert.AreEqual(7f * 1.89f / 1.2f, tick.y * 1.89f, 0.01f);

            // 1080p at UI 1.2 (1.2 px per unit): the physical floor holds 2 × 6.
            HudReticle.TickGeometry(1.2f, 1.2f, out tick, out arc);
            Assert.GreaterOrEqual(tick.x * 1.2f, HudReticle.MinTickWidthPx - 0.001f);
            Assert.GreaterOrEqual(tick.y * 1.2f, HudReticle.MinTickLengthPx - 0.001f);

            // A small window (0.6 px per unit): floored, never thinner than 2 × 6 physical px.
            HudReticle.TickGeometry(1f, 0.6f, out tick, out arc);
            Assert.AreEqual(HudReticle.MinTickWidthPx, tick.x * 0.6f, 0.001f);
            Assert.GreaterOrEqual(tick.y * 0.6f, HudReticle.MinTickLengthPx - 0.001f);
        }
    }
}
