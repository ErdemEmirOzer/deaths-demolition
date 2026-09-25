using System.IO;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 R1-C · the round-end wallpaper may not cost readability (user request 25.09): every pixel stays under the
    /// luminance ceiling the art was clamped to, so the dimmest text the screen sets on its backdrop (TextMuted) keeps
    /// 4.5:1 or better on the brightest pixel, and the texture imports as a UI backdrop (full size, no mips).
    /// </summary>
    public sealed class UpgradeWallpaperTests
    {
        private static readonly string[] SourcePaths =
        {
            "Assets/_Prototype/UI/Art/Resources/UI/Wallpapers/upgrade_wallpaper.png",
            "Assets/_Prototype/UI/Art/Resources/UI/Wallpapers/loading_run.png",       // R1-D (N20) key art
            "Assets/_Prototype/UI/Art/Resources/UI/Wallpapers/loading_results.png"
        };
        private const float LuminanceCeiling = 0.0245f;

        [Test]
        public void DuvarKagidi_EnParlakPikseldeBile_SolukMetinOkunur([ValueSource(nameof(SourcePaths))] string SourcePath)
        {
            var texture = new Texture2D(2, 2);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(SourcePath)), SourcePath);
            Color32[] pixels = texture.GetPixels32();
            float max = 0f;
            Color brightest = Color.black;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float l = UiContrast.RelativeLuminance(c);
                if (l > max)
                {
                    max = l;
                    brightest = c;
                }
            }

            Object.DestroyImmediate(texture);
            float ratio = UiContrast.Ratio(UiTokens.Get(UiColor.TextMuted), brightest);
            Debug.Log("[DD-TEST] r1c.wallpaper " + Path.GetFileName(SourcePath) + " maxL=" + max.ToString("F4") + " textMuted=" + ratio.ToString("F2"));
            Assert.LessOrEqual(max, LuminanceCeiling, "the wallpaper's brightest pixel stays under the ceiling");
            Assert.GreaterOrEqual(ratio, 4.5f, "TextMuted on the brightest pixel");
        }

        [Test]
        public void DuvarKagidi_ArkaPlanOlarakIceAlinir()
        {
            Texture2D art = Resources.Load<Texture2D>(UpgradeScreen.UpgradeWallpaper);
            Assert.IsNotNull(art, UpgradeScreen.UpgradeWallpaper);
            Assert.AreEqual(2560, art.width, "full size, never downsized");
            Assert.AreEqual(1440, art.height);
            Assert.AreEqual(1, art.mipmapCount, "a UI backdrop has no mips");
            foreach (string key in new[] { UiCurtain.KeyArtFor(DD.Prototype.Core.Boot.PrototypeScenes.Run), UiCurtain.KeyArtFor(DD.Prototype.Core.Boot.PrototypeScenes.Results) })
            {
                Texture2D keyArt = Resources.Load<Texture2D>(key);
                Assert.IsNotNull(keyArt, key);
                Assert.AreEqual(1, keyArt.mipmapCount, key + ": no mips");
            }
        }
    }
}
