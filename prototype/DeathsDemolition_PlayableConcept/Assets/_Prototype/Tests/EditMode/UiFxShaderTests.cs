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
    /// with its three passes, and the placeholder ink kit (deterministic, four distinct marks in RGBA).
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiFxShaderTests
    {
        [Test]
        public void UiFx_TekPass_DortVaryant_YalnizClipRectVeAlphaClip()
        {
            Shader shader = Shader.Find(UiFx.ShaderName);
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
            Shader shader = Shader.Find("Hidden/DD/UI/FrozenFrame");
            Assert.IsNotNull(shader, "Hidden/DD/UI/FrozenFrame bulunamadı");
            Assert.IsTrue(shader.isSupported);
            Assert.AreEqual(3, shader.passCount, "Kawase down / up + mürekkep yıkama");
            Assert.AreEqual(0, UserKeywords(shader).Length, "kullanıcı anahtarı yok (pass başına tek varyant)");
            StringAssert.Contains("/Resources/", AssetDatabase.GetAssetPath(shader));
        }

        [Test]
        public void MurekkepKiti_Placeholder_Deterministik_DortKanal()
        {
            Texture2D a = UiFxGlobals.BuildPlaceholderInkMap(64);
            Texture2D b = UiFxGlobals.BuildPlaceholderInkMap(64);
            try
            {
                Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
                CollectionAssert.AreEqual(pa, pb, "yer tutucu kit deterministik olmalı");

                // R brush: inks left → right (left column mean < right column mean).
                float left = 0f, right = 0f;
                for (int y = 0; y < 64; y++) { left += pa[y * 64 + 2].r; right += pa[y * 64 + 61].r; }
                Assert.Less(left, right * 0.5f, "fırça zaman haritası soldan sağa dolmalı");

                // G bleed: centre earlier than corners.
                Assert.Less(pa[32 * 64 + 32].g, pa[0].g, "sızma merkezden yayılmalı");

                // B hanko: centre early (≈ 0), rim late.
                Assert.Less(pa[32 * 64 + 32].b, 20, "mühür iç kısmı ilk mürekkeplenir");

                // A crack tile: mostly bright leaf, some dark cracks.
                int dark = pa.Count(p => p.a < 80), bright = pa.Count(p => p.a > 180);
                Assert.Greater(dark, 0, "varak çatlağı yok");
                Assert.Greater(bright, pa.Length / 2, "varak karosu çoğunlukla parlak olmalı");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
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
