using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 R1-D · the loading key arts are full and rich, so their readability is proven where the words sit (C-00 25.09,
    /// the round-end wallpaper's method: the brightest pixel under each word). For each screen the curtain meets -
    /// 16:9 1080, the Mac 3024×1964 at UI 1.2 × text 1.25, 16:10 and 21:9 - the ring, the label stack and the tip strip are
    /// laid out from UiCurtain's own numbers, mapped through its cover-fit at push-in 1.00 and 1.05, and sampled on the file:
    ///   ring    the cream stroke (TextPrimary) ≥ 3:1 on the brightest pixel (WCAG 1.4.11: a graphic);
    ///   labels  TextSecondary (the dimmest word there: eyebrow, subline) ≥ 4.5:1 on the brightest pixel;
    ///   tip     TextPrimary ≥ 7:1 on the strip (SurfaceBase 0.72, composited as the GPU does) over the brightest pixel -
    ///           not on the Results art: the way to the Results never shows a tip (PrototypeUiFlow.ShowsTip, asserted).
    /// WP-081 B1 (R2 U3): every art of the rotation passes the same bar, and the rotation itself is proven on fixed seeds -
    /// at least five arts, never the same one on two loading screens in a row, each at least twice in any twenty loads.
    /// </summary>
    public sealed class LoadingKeyArtTests
    {
        private const string ResourcesFolder = "Assets/_Prototype/UI/Art/Resources/";
        private static readonly string ResultsArt = FileOf(UiCurtain.ResultsKeyArt);

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

        /// <summary>The source file of a key art's Resources path.</summary>
        private static string FileOf(string resource)
        {
            return ResourcesFolder + resource + ".jpg";
        }

        private static IEnumerable<string> Arts()
        {
            foreach (string art in UiCurtain.LoadingKeyArts)
            {
                yield return FileOf(art);
            }

            yield return ResultsArt;
        }

        private static IEnumerable<object[]> Cases()
        {
            foreach (string art in Arts())
            {
                foreach (object[] screen in Screens)
                {
                    yield return new object[] { art, screen[0], screen[1], screen[2], screen[3], screen[4] };
                }
            }
        }

        private static readonly Dictionary<string, Texture2D> s_arts = new Dictionary<string, Texture2D>();
        private static Texture2D s_art;

        [OneTimeSetUp]
        public void Load()
        {
            foreach (string path in Arts())
            {
                var texture = new Texture2D(2, 2);
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), path);
                s_arts[path] = texture;
            }
        }

        [OneTimeTearDown]
        public void Unload()
        {
            foreach (Texture2D texture in s_arts.Values)
            {
                Object.DestroyImmediate(texture);
            }

            s_arts.Clear();
        }

        [TestCaseSource(nameof(Cases))]
        public void AnahtarSanat_KelimelerinAltindaEnParlakPikselde_Okunur(string art, string name, int width, int height, float uiScale, float textScale)
        {
            s_art = s_arts[art];
            bool results = art == ResultsArt;
            name = Path.GetFileNameWithoutExtension(art) + " " + name;
            if (results)
            {
                foreach (string from in new[] { PrototypeScenes.Boot, PrototypeScenes.MainMenu, PrototypeScenes.Selection, PrototypeScenes.Run })
                {
                    Assert.IsFalse(PrototypeUiFlow.ShowsTip(from, PrototypeScenes.Results), "the way to the Results shows no tip (from " + from + ")");
                }
            }

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
            if (!results)
            {
                Assert.GreaterOrEqual(tipRatio, 7f, name + ": TextPrimary on the tip strip over the brightest pixel under it");
            }
        }

        /// <summary>WP-081 B1: at least five arts in the rotation, each its own asset with its file in place; the Results' own art stays out.</summary>
        [Test]
        public void Rotasyon_EnAzBesAyriSanat_HepsiDosyada()
        {
            IReadOnlyList<string> arts = UiCurtain.LoadingKeyArts;
            Assert.GreaterOrEqual(arts.Count, 5, "R2 U3: at least five loading arts");
            Assert.AreEqual(arts.Count, new HashSet<string>(arts).Count, "every rotation art is its own asset");
            CollectionAssert.DoesNotContain(arts, UiCurtain.ResultsKeyArt, "the Results keep their own art, outside the rotation");
            foreach (string art in arts)
            {
                Assert.IsTrue(File.Exists(FileOf(art)), art + ": the source file");
            }
        }

        /// <summary>
        /// R2 U3 on fixed seeds: a session of 200 loading screens never shows one art twice in a row, and every 20 in a row
        /// (the first 20 too) show each art at least three times (r2-gates.json U3: minPerAny20 ≥ 3); also counts
        /// distinctSha256, the rotation files that differ (AP-028's five paintings since 06.10; the placeholder copies kept the
        /// gate red before) - this test asserts the rotation only. r2-U3.json through the shared R2Olcum (C-00 05.10, decision a;
        /// tools/qa/r2-measure-contract.md).
        /// </summary>
        [Test]
        public void Rotasyon_ArdisikTekrarYok_YirmiYuklemedeHerSanatEnAzIkiKez()
        {
            const int Seeds = 500;
            const int Loads = 200;
            const int Window = 20;
            int count = UiCurtain.LoadingKeyArts.Count;
            int fewest = int.MaxValue;
            int repeats = 0;
            string firstRepeat = null;
            var shown = new int[Loads];
            var tally = new int[count];
            for (int seed = 0; seed < Seeds; seed++)
            {
                var rotation = new KeyArtRotation(count, seed);
                for (int i = 0; i < Loads; i++)
                {
                    shown[i] = rotation.Upcoming;
                    rotation.Shown(shown[i]);
                    if (i > 0 && shown[i] == shown[i - 1])
                    {
                        repeats++;
                        firstRepeat = firstRepeat ?? "seed " + seed + ": load " + i + " shows art " + shown[i] + " again";
                    }
                }

                for (int start = 0; start + Window <= Loads; start++)
                {
                    System.Array.Clear(tally, 0, count);
                    for (int i = start; i < start + Window; i++)
                    {
                        tally[shown[i]]++;
                    }

                    for (int art = 0; art < count; art++)
                    {
                        fewest = Mathf.Min(fewest, tally[art]);
                    }
                }
            }

            // R2 contract (source in the record): the rotation really ran over real arts - every load counted, every art
            // file read and loadable - before anything is written.
            Assert.Greater(count, 1, "the rotation has arts to rotate");
            long showings = 0;
            for (int i = 0; i < Loads; i++)
            {
                Assert.That(shown[i], Is.InRange(0, count - 1), "load " + i + " shows a listed art");
                showings++;
            }

            Assert.AreEqual(Loads, showings);
            var shas = new HashSet<string>();
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (string art in UiCurtain.LoadingKeyArts)
                {
                    byte[] bytes = File.ReadAllBytes(FileOf(art));
                    Assert.Greater(bytes.Length, 0, art + ": the art file is empty");
                    Assert.IsNotNull(Resources.Load<Texture2D>(art), art + ": the art does not load");
                    shas.Add(System.BitConverter.ToString(sha.ComputeHash(bytes)));
                }
            }

            Debug.Log("[DD-TEST] wp081.u3 arts=" + count + " seeds=" + Seeds + " loads=" + Loads + " repeats=" + repeats
                      + " fewestPer" + Window + "=" + fewest + " distinctSha256=" + shas.Count);
            R2Olcum.Yaz("U3", "DD.Prototype.Tests.EditMode.LoadingKeyArtTests.Rotasyon_ArdisikTekrarYok_YirmiYuklemedeHerSanatEnAzIkiKez",
                new Dictionary<string, double>
                {
                    { "consecutiveRepeats", repeats },
                    { "minPerAny20", fewest },
                    { "distinctSha256", shas.Count }
                },
                Seeds * Loads);
            Assert.AreEqual(0, repeats, firstRepeat);
            Assert.GreaterOrEqual(fewest, 3, "every art at least three times in any twenty loads (r2-gates.json U3)");
        }

        /// <summary>
        /// A screen that shows an art off the turn (a test, a capture) takes that art out of the round: whatever was shown,
        /// the upcoming art is never the one just shown.
        /// </summary>
        [Test]
        public void Rotasyon_SiraDisiGosterim_SiradakiYineFarkli()
        {
            var random = new System.Random(81);
            for (int seed = 0; seed < 200; seed++)
            {
                var rotation = new KeyArtRotation(5, seed);
                for (int i = 0; i < 100; i++)
                {
                    int pick = random.Next(3) == 0 ? random.Next(5) : rotation.Upcoming;
                    rotation.Shown(pick);
                    if (rotation.Last != pick || rotation.Upcoming == pick)
                    {
                        Assert.Fail("seed " + seed + ": after art " + pick + " the upcoming is " + rotation.Upcoming + " (last " + rotation.Last + ")");
                    }
                }
            }
        }

        /// <summary>The Results ask for their own art; every other destination for the rotation's next, and asking spends no turn.</summary>
        [Test]
        public void KeyArtFor_SonuclarKendiSanati_DigerleriRotasyon()
        {
            Assert.AreEqual(UiCurtain.ResultsKeyArt, UiCurtain.KeyArtFor(PrototypeScenes.Results));
            string upcoming = UiCurtain.UpcomingKeyArt;
            CollectionAssert.Contains(UiCurtain.LoadingKeyArts, upcoming);
            foreach (string scene in new[] { PrototypeScenes.MainMenu, PrototypeScenes.Selection, PrototypeScenes.Run })
            {
                Assert.AreEqual(upcoming, UiCurtain.KeyArtFor(scene), scene);
            }
        }

        /// <summary>AP-035 R3: the rotation arts on every screen (the Results never show the logo: PrototypeUiFlow.ShowsLogo).</summary>
        private static IEnumerable<object[]> LogoCases()
        {
            foreach (string art in UiCurtain.LoadingKeyArts)
            {
                foreach (object[] screen in Screens)
                {
                    yield return new object[] { FileOf(art), screen[0], screen[1], screen[2], screen[3], screen[4] };
                }
            }
        }

        /// <summary>The wordmark's bone (AP-035 LOGO-KILAVUZU measured median #E9DECD).</summary>
        private static readonly Color LogoBone = new Color(0xE9 / 255f, 0xDE / 255f, 0xCD / 255f, 1f);

        /// <summary>
        /// AP-035 R3 (S-11 O2, C-00 08.10, K1 layout B, S3-16): with the logo on a loading screen the ring and its labels stand
        /// at <see cref="UiCurtain.LogoRingLift"/> and still pass the bar above (ring ≥ 3:1, labels ≥ 4.5:1 on the brightest
        /// pixel); the wordmark (bone) over its Sumi900 halo at <see cref="UiLogoAnimator.HaloAlpha"/>, composited on the
        /// brightest pixel under the wordmark's rect, ≥ 3:1 (a graphic, WCAG 1.4.11). The Results never show the logo.
        /// </summary>
        [TestCaseSource(nameof(LogoCases))]
        public void AnahtarSanat_LogoDuzeni_HalkaEtiketVeYaziOkunur(string art, string name, int width, int height, float uiScale, float textScale)
        {
            s_art = s_arts[art];
            name = Path.GetFileNameWithoutExtension(art) + " " + name + " (logo)";
            Assert.IsFalse(PrototypeUiFlow.ShowsLogo(PrototypeScenes.Run, PrototypeScenes.Results), "the way to the Results shows no logo");
            Assert.IsTrue(PrototypeUiFlow.ShowsLogo(PrototypeScenes.MainMenu, PrototypeScenes.Selection));

            Vector2 reference = new Vector2(1920f, 1080f) / uiScale;
            float fit = Mathf.Min(width / reference.x, height / reference.y);
            Vector2 canvas = new Vector2(width / fit, height / fit);
            Rect uv = UiCurtain.CoverUv(canvas.x / canvas.y, (float)s_art.width / s_art.height);

            Vector2 ring = new Vector2(canvas.x * 0.5f, canvas.y * 0.5f + UiCurtain.LogoRingLift);
            float half = UiCurtain.RingSize * 0.5f;
            Rect ringRect = new Rect(ring.x - half, ring.y - half, UiCurtain.RingSize, UiCurtain.RingSize);
            float labelsTop = ring.y - half - (int)UiSpace.XL;
            float labelsHeight = (UiFactory.LineHeight(UiType.Label) * 2f + (int)UiSpace.S + UiFactory.LineHeight(UiType.H2)) * textScale;
            float labelsHalf = LabelHalfWidth * textScale;
            Rect labelRect = new Rect(ring.x - labelsHalf, labelsTop - labelsHeight, labelsHalf * 2f, labelsHeight);

            // the logo: composition bottom-centre at (centre, centre + LogoBottom), scale LogoWidth / 960; the wordmark is its foot
            float scale = UiCurtain.LogoWidth / UiLogoAnimator.CompositionWidth;
            Rect word = new Rect(canvas.x * 0.5f - UiLogoAnimator.WordmarkRect.width * 0.5f * scale, canvas.y * 0.5f + UiCurtain.LogoBottom,
                UiLogoAnimator.WordmarkRect.width * scale, UiLogoAnimator.WordmarkRect.height * scale);
            float logoTop = canvas.y * 0.5f + UiCurtain.LogoBottom + UiLogoAnimator.CompositionHeight * scale;
            Assert.LessOrEqual(logoTop, canvas.y, name + ": the logo stays on the canvas");
            Assert.GreaterOrEqual(word.yMin, ringRect.yMax + UiCurtain.LogoGap - 0.5f, name + ": the logo stands clear of the ring");
            float tipTop = UiCurtain.TipMargin + TipLines * UiFactory.LineHeight(UiType.Body) * textScale + (int)UiSpace.M * 2f;
            float tipRight = UiCurtain.TipMargin + Mathf.Min(UiCurtain.TipMaxWidth, canvas.x - UiCurtain.TipMargin * 2f);
            Assert.IsTrue(labelRect.yMin >= tipTop || labelRect.xMin >= tipRight, name + ": the labels clear the tip strip");

            Color ringBg = Brightest(ringRect, canvas, uv);
            Color labelBg = Brightest(labelRect, canvas, uv);
            Color wordBg = Brightest(word, canvas, uv);
            Color halo = UiTokens.Get(UiColor.SurfaceInset);
            halo.a = UiLogoAnimator.HaloAlpha;
            float ringRatio = UiContrast.Ratio(UiTokens.Get(UiColor.TextPrimary), ringBg);
            float labelRatio = UiContrast.Ratio(UiTokens.Get(UiColor.TextSecondary), labelBg);
            float wordRatio = UiContrast.Ratio(LogoBone, UiContrast.Composite(halo, wordBg));
            Debug.Log("[DD-TEST] ap035.keyart.logo " + name + " ring=" + ringRatio.ToString("F2") + " labels=" + labelRatio.ToString("F2") + " wordmark=" + wordRatio.ToString("F2"));
            Assert.GreaterOrEqual(ringRatio, 3f, name + ": the ring's cream stroke on the brightest pixel under it");
            Assert.GreaterOrEqual(labelRatio, 4.5f, name + ": TextSecondary on the brightest pixel under the labels");
            Assert.GreaterOrEqual(wordRatio, 3f, name + ": the wordmark over its halo on the brightest pixel under it");
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

        /// <summary>
        /// R4 WP-105 (user note 6: "the logo sits inside the round progress ring, not above it") · the loading logo's seal lands
        /// on the ring's centre and its ink stays inside the ring's clear middle (the brush stroke's inner edge measured at 0.86 of
        /// the radius on the r4-audit-before frame: Ø 206 of 240); the wordmark keeps its place over the ring.
        /// </summary>
        [Test]
        public void LogoMuhru_HalkaninIcinde_YaziHalkaninUstunde()
        {
            const float InnerShare = 0.86f;
            float scale = UiCurtain.LogoScale;
            float sealCentre = UiCurtain.LogoBottom + (UiLogoAnimator.SealCentreAboveFoot - UiCurtain.LogoSealLanding) * scale;
            Assert.AreEqual(UiCurtain.LogoRingLift, sealCentre, 0.01f, "the seal lands on the ring's centre");

            float sealInk = UiLogoAnimator.SealSpriteSize * (738f / 768f) * scale;
            float clear = UiCurtain.RingSize * InnerShare;
            Debug.Log("[DD-TEST] wp105.loadingSeal ink=" + sealInk.ToString("F1") + " clear=" + clear.ToString("F1") + " landing=" + UiCurtain.LogoSealLanding.ToString("F1"));
            Assert.LessOrEqual(sealInk, clear - 2f * (int)UiSpace.M, "the seal's ink clears the ring's inner edge by ≥ 16 px a side");

            float wordmarkFoot = UiCurtain.LogoBottom;
            Assert.GreaterOrEqual(wordmarkFoot, UiCurtain.LogoRingLift + UiCurtain.RingSize * 0.5f + UiCurtain.LogoGap - 0.01f, "the wordmark stands over the ring");
        }

        private static Vector2 ToUv(Vector2 screen, float push, Rect uv)
        {
            Vector2 local = new Vector2(0.5f + (screen.x - 0.5f) / push, 0.5f + (screen.y - 0.5f) / push);
            return new Vector2(uv.xMin + local.x * uv.width, uv.yMin + local.y * uv.height);
        }
    }
}
