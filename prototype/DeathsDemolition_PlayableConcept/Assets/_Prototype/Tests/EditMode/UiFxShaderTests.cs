using System.Collections.Generic;
using System.Linq;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 4 · DD/UI/Fx contract: one pass, only the CLIP_RECT + ALPHACLIP local keywords (4 variants — a mode
    /// never becomes a keyword), shipped from a Resources folder (Shader.Find works in a Player), the frozen-frame shader
    /// with its three passes, and the AP-012 v004 ink kit (BC7 linear, one mark per channel in its rect, tiles, masks).
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiFxShaderTests
    {
        [Test]
        public void UiFx_TekPass_DortVaryant_YalnizClipRectVeAlphaClip()
        {
            Shader shader = DD.Prototype.Rendering.PrototypeShaders.UiFx;
            Assert.IsNotNull(shader, "DD/UI/Fx bulunamadı");
            Assert.IsTrue(shader.isSupported, "DD/UI/Fx bu platformda desteklenmiyor (derleme hatası?)");
            Assert.AreEqual(1, shader.passCount, "tek pass");

            // Built-in stereo keywords (UnityCG instancing macros) are platform keywords, stripped without XR; the contract
            // is on the user-defined ones: exactly the two UI keywords → 2² = 4 variants.
            string[] user = UserKeywords(shader);
            CollectionAssert.AreEquivalent(new[] { "UNITY_UI_CLIP_RECT", "UNITY_UI_ALPHACLIP" }, user,
                "yalnız iki kullanıcı anahtarı → 2² = 4 varyant; mod bir anahtar olmamalı");

            string path = AssetDatabase.GetAssetPath(shader);
            StringAssert.Contains("/Resources/", path, "shader Resources altında olmalı (Player'da Shader.Find)");
        }

        [Test]
        public void FrozenFrame_UcPass_DownUpWash()
        {
            Shader shader = DD.Prototype.Rendering.PrototypeShaders.UiFrozenFrame;
            Assert.IsNotNull(shader, "Hidden/DD/UI/FrozenFrame bulunamadı");
            Assert.IsTrue(shader.isSupported);
            Assert.AreEqual(3, shader.passCount, "Kawase down / up + mürekkep yıkama");
            Assert.AreEqual(0, UserKeywords(shader).Length, "kullanıcı anahtarı yok (pass başına tek varyant)");
            StringAssert.Contains("/Resources/", AssetDatabase.GetAssetPath(shader));
        }

        [Test]
        public void MurekkepKiti_AP012v004_BC7Linear_KanalDikdortgenleri()
        {
            const string root = "Assets/_Prototype/UI/Resources/Fx/";
            string kitPath = root + "AP012_v004_ink_timemaps_1024.png";
            var kit = (TextureImporter)AssetImporter.GetAtPath(kitPath);
            Assert.IsNotNull(kit, "A8 mürekkep kiti içe alınmamış: " + kitPath);
            Assert.IsFalse(kit.sRGBTexture, "zaman haritası linear olmalı");
            Assert.AreEqual(TextureImporterFormat.BC7, kit.GetPlatformTextureSettings("Standalone").format, "tek RGBA BC7 (C-00)");
            Assert.AreEqual(TextureWrapMode.Clamp, kit.wrapMode);
            Assert.IsNotNull(Resources.Load<Texture2D>(UiFxGlobals.InkKitResource), "kit Resources'tan yüklenmeli (Player)");

            // Each channel: 1.0 (empty) everywhere outside its mark rect, inked (< 1) inside — the rects match the texture.
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.IsTrue(src.LoadImage(System.IO.File.ReadAllBytes(kitPath)));
                Assert.AreEqual(1024, src.width);
                Color32[] px = src.GetPixels32();
                for (int c = 0; c < 4; c++)
                {
                    Vector4 r = UiFxGlobals.InkRects[c];
                    int x0 = Mathf.RoundToInt(r.x * 1024), y0 = Mathf.RoundToInt(r.y * 1024);
                    int x1 = x0 + Mathf.RoundToInt(r.z * 1024), y1 = y0 + Mathf.RoundToInt(r.w * 1024);
                    int inked = 0, strayOutside = 0;
                    var times = new List<byte>();
                    for (int y = 0; y < 1024; y++)
                    {
                        for (int x = 0; x < 1024; x++)
                        {
                            Color32 p = px[y * 1024 + x];
                            byte v = c == 0 ? p.r : c == 1 ? p.g : c == 2 ? p.b : p.a;
                            bool inside = x >= x0 && x < x1 && y >= y0 && y < y1;
                            if (inside && v < 250) inked++;
                            if (inside && v < 254) times.Add(v);
                            if (!inside && v < 255) strayOutside++;
                        }
                    }

                    Assert.AreEqual(0, strayOutside, "kanal " + c + " dikdörtgeni dışında mürekkep var (rect yanlış?)");
                    Assert.Greater(inked, (x1 - x0) * (y1 - y0) / 10, "kanal " + c + " dikdörtgeninde işaret yok");

                    // The shader's per-channel time scale is the mark's 99th-percentile time (AD 4c MUST 1).
                    times.Sort();
                    float p99 = times[(int)(times.Count * 0.99f)] / 255f;
                    Assert.AreEqual(p99, UiFxGlobals.InkTimeMax[c], 0.02f, "kanal " + c + " InkTimeMax kitle uyuşmuyor");
                }
            }
            finally
            {
                Object.DestroyImmediate(src);
            }

            // Tiles: single channel, linear, repeat. Static masks: sprites, one per channel.
            foreach (string tile in new[] { "AP012_tile_kinpaku_128.png", "AP012_tile_washi_256.png" })
            {
                var t = (TextureImporter)AssetImporter.GetAtPath(root + tile);
                Assert.IsNotNull(t, tile);
                Assert.AreEqual(TextureImporterType.SingleChannel, t.textureType, tile);
                Assert.IsFalse(t.sRGBTexture, tile);
                Assert.AreEqual(TextureWrapMode.Repeat, t.wrapMode, tile);
            }

            foreach (UiInkChannel c in System.Enum.GetValues(typeof(UiInkChannel)))
            {
                Assert.IsNotNull(UiFxGlobals.InkMask(c), "statik maske yok: " + c);
            }
        }

        [Test]
        public void Token_v3_Baglama_UiTokensHlsl_UiLayer_UiMotion()
        {
            string hlsl = System.IO.File.ReadAllText("Assets/_Prototype/UI/Resources/Shaders/DD_UiFx.hlsl");
            string shader = System.IO.File.ReadAllText("Assets/_Prototype/UI/Resources/Shaders/DD_UiFx.shader");
            StringAssert.Contains("#include \"../../Art/Shaders/UiTokens.hlsl\"", hlsl, "S-09 dilim 1 token include'u");
            foreach (string token in new[] { "DD_UI_EASE_BRUSH", "DD_UI_EASE_SETTLE", "DD_UI_EASE_EXIT" }) StringAssert.Contains(token, hlsl);
            foreach (string token in new[] { "DD_UI_STAMP_PEAK", "DD_UI_STAMP_IN", "DD_UI_GLINT_MAX_BRIGHTNESS", "DD_UI_SHEEN_FIELD_ALPHA_MAX" }) StringAssert.Contains(token, shader);
            StringAssert.DoesNotContain("0.25, 0.10, 0.25", hlsl, "eğri sayıları elle yazılmamalı (token)");
            StringAssert.DoesNotContain("lerp(1.12", shader, "damga tepe değeri token'dan");
            Assert.AreEqual(UiLayer.FrozenFrame, UiFrozenFrame.Layer, "donmuş kare katmanı UiLayer'dan");
            Assert.AreEqual(UiMotionClass.Flash, UiFx.ClassOf(UiFxMode.Leaf));
            Assert.AreEqual(UiMotionClass.Data, UiFx.ClassOf(UiFxMode.SegmentBar));
            Assert.AreEqual(UiMotionClass.Decor, UiFx.ClassOf(UiFxMode.InkReveal));

            float? was = UiMotion.TestNow;
            try
            {
                UiMotion.TestNow = 123.5f;
                Assert.AreEqual(123.5f, UiFxGlobals.Now, 1e-5f, "Fx saati çekirdeğin test saatini izler");
            }
            finally
            {
                UiMotion.TestNow = was;
            }
        }

        private static string[] UserKeywords(Shader shader)
        {
            return shader.keywordSpace.keywords.Where(k => k.type == ShaderKeywordType.UserDefined).Select(k => k.name).ToArray();
        }

        [Test]
        public void ModSayilari_ShaderIleAyni()
        {
            // The shader's #defines (DD_UiFx.hlsl) and the C# enum must agree — the mode rides in uv1.x as a float.
            string hlsl = System.IO.File.ReadAllText("Assets/_Prototype/UI/Resources/Shaders/DD_UiFx.hlsl");
            var expected = new Dictionary<string, UiFxMode>
            {
                { "DD_FX_NONE", UiFxMode.None }, { "DD_FX_INK", UiFxMode.InkReveal }, { "DD_FX_LEAF", UiFxMode.Leaf },
                { "DD_FX_STAMP", UiFxMode.Stamp }, { "DD_FX_SHEEN", UiFxMode.Sheen }, { "DD_FX_SEGMENT", UiFxMode.SegmentBar },
                { "DD_FX_SDF", UiFxMode.SdfGlyph }, { "DD_FX_PARTICLES", UiFxMode.Particles }
            };
            foreach (KeyValuePair<string, UiFxMode> kv in expected)
            {
                StringAssert.IsMatch("#define " + kv.Key + @"\s+" + (int)kv.Value + @"\b", hlsl, kv.Key + " ↔ " + kv.Value);
            }
        }
    }
}
