using System.Collections;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 §7.1 G1 / A4 · the Alpha8 atlases draw in their token colour. The two ways an Alpha8 UI texture fails
    /// are both visible in a pixel: without uGUI's _TextureSampleAdd whitening the rgb is black, and a one-channel
    /// (BC4 / R8) texture reads (r, 0, 0) - a red rectangle. A G1 glyph through DD/UI/Fx SdfGlyph and an A4 pill
    /// through the default UI material are rendered to a texture and read back: the fill is the tint, the SDF edge
    /// is crisp (outside the shape is background), and a glyph image carries the SdfGlyph mode.
    /// </summary>
    [Category("WP068")]
    public sealed class UiGlyphAtlasTests
    {
        [UnityTest]
        public IEnumerator G1GlifSdfIleTonundaCizilir_A4CerceveBeyazAlfa()
        {
            var rt = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("GlyphCam");
            var canvasGo = new GameObject("GlyphCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Camera cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.orthographic = true;
                cam.targetTexture = rt;
                cam.enabled = false;

                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;

                Sprite heart = UiArt.Icon("Hud.Health");
                Sprite pill = UiArt.Kit("Frame.ChipFill");
                Assert.IsNotNull(heart, "G1 Hud.Health");
                Assert.IsNotNull(pill, "A4 Frame.ChipFill");

                Image glyph = UiFactory.NewImage("Glyph", canvas.transform, UiColor.StateGold, heart);
                glyph.preserveAspect = true;
                var glyphRect = (RectTransform)glyph.transform;
                glyphRect.sizeDelta = new Vector2(112f, 112f);
                glyphRect.anchoredPosition = new Vector2(-64f, 0f);
                var fx = glyph.GetComponent<UiFx>();
                Assert.IsNotNull(fx, "G1 glifi DD/UI/Fx ile çizilir");
                Assert.AreEqual(UiFxMode.SdfGlyph, fx.Mode);

                Image frame = UiFactory.NewImage("Pill", canvas.transform, UiColor.StateGold, pill);
                frame.pixelsPerUnitMultiplier = UiArt.FramePixelsPerUnitMultiplier;
                var frameRect = (RectTransform)frame.transform;
                frameRect.sizeDelta = new Vector2(100f, 48f);
                frameRect.anchoredPosition = new Vector2(64f, 0f);
                Assert.IsNull(frame.GetComponent<UiFx>(), "A4 çerçevesi varsayılan UI materyaliyle");

                yield return null;
                Canvas.ForceUpdateCanvases();
                cam.Render();

                var read = new Texture2D(256, 128, TextureFormat.RGBA32, false);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 256, 128), 0, 0);
                read.Apply();
                RenderTexture.active = previous;

                Color glyphCentre = read.GetPixel(64, 64);   // the heart centre (distance 1.0 in the atlas)
                Color glyphCorner = read.GetPixel(12, 12);   // outside the heart, inside its image
                Color pillCentre = read.GetPixel(192, 64);
                string report = "glyph " + glyphCentre + " corner " + glyphCorner + " pill " + pillCentre;
                Debug.Log("[DD-TEST] wp068.g1render " + report);
                Object.Destroy(read);

                // StateGold is warm and bright: red and green both well above zero (black = no whitening, g ≈ 0 = red channel only).
                Assert.Greater(glyphCentre.r, 0.5f, "G1 glifi tonunda (siyah değil): " + report);
                Assert.Greater(glyphCentre.g, 0.3f, "G1 glifi tonunda (kırmızı dikdörtgen değil): " + report);
                Assert.Greater(pillCentre.r, 0.5f, "A4 çerçevesi tonunda (siyah değil): " + report);
                Assert.Greater(pillCentre.g, 0.3f, "A4 çerçevesi tonunda (kırmızı değil): " + report);
                Assert.Less(glyphCorner.r + glyphCorner.g + glyphCorner.b, 0.1f, "SDF kenarı dışında zemin (bulanık hale yok): " + report);
            }
            finally
            {
                Object.Destroy(canvasGo);
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
            }
        }
    }
}
