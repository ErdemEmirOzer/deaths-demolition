using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 12b · run HUD lane: the geometry rules that need no scene. The live HUD half of each item sits in the
    /// PlayMode suites (EnemyStatusStampsTests, UiArtSliceTests, CameraModesHudTests).
    /// </summary>
    public sealed class RunHud12bTests
    {
        private static readonly (string Name, float W, float H, float Tall)[] Canvases =
        {
            ("1080", 1920f, 1080f, 1f), ("21:9", 2560f, 1080f, 1f), ("mac", 1600f, 1039f, 1.25f)
        };

        /// <summary>
        /// B1 (AD 8c, new for 12b) + B2 (AD R1-B SHOULD-4): a stamp over any head point on the canvas never meets an ink
        /// cluster + 12 px or the player figure + 24 px, and it keeps its body's column (x unchanged). Top-down and third
        /// person carry a figure, first person does not; the Special block counts as a cluster while it shows.
        /// </summary>
        [Test]
        public void DurumDamgasi_KumelereVeFigureGirmez_SutunuKorur()
        {
            var failures = new List<string>();
            foreach (var c in Canvases)
            {
                float hw = c.W * 0.5f, hh = c.H * 0.5f;
                foreach (string variant in new[] { "td", "td-special", "tp", "tp-special", "fp", "fp-special" })
                {
                    var rects = new List<Rect>
                    {
                        Rect.MinMaxRect(-hw + 32f, hh - 32f - 36f, -hw + 32f + 170f, hh - 32f),                  // gold
                        Rect.MinMaxRect(-120f, hh - 24f - 56f, 120f, hh - 24f),                                    // seal
                        Rect.MinMaxRect(-hw + 32f, -hh + 32f, -hw + 32f + 380f, -hh + 32f + 110f * c.Tall),       // health
                        variant.StartsWith("fp")
                            ? Rect.MinMaxRect(-hw + 32f, -hh + RunHudScreen.FpWeaponClusterBottom, -hw + 432f, -hh + RunHudScreen.FpWeaponClusterBottom + 140f * c.Tall)
                            : Rect.MinMaxRect(hw - 432f, -hh + 32f, hw - 32f, -hh + 32f + 140f * c.Tall),         // weapon
                    };
                    if (variant.EndsWith("special"))
                    {
                        rects.Add(Rect.MinMaxRect(-110f, -hh + 24f, 110f, -hh + 24f + 260f));                   // Special block
                    }

                    bool hasFigure = !variant.StartsWith("fp");
                    Rect figure = variant.StartsWith("td")
                        ? Rect.MinMaxRect(-30f * c.Tall, -45f * c.Tall, 30f * c.Tall, 45f * c.Tall)             // top-down: the figure at the centre
                        : Rect.MinMaxRect(-hw * 0.30f, -hh - 40f, -hw * 0.08f, 60f);                             // third person: over the shoulder, cut by the bottom edge
                    Rect[] avoid = rects.ToArray();
                    foreach (Vector2 chip in new[] { new Vector2(110f, 28f), new Vector2(140f, 36f) })
                    {
                        for (float x = -hw; x <= hw; x += 24f)
                        {
                            for (float y = -hh; y <= hh; y += 24f)
                            {
                                Vector2 at = UiEnemyStatusMarkers.Resolve(new Vector2(x, y), chip, avoid, avoid.Length, hasFigure, figure);
                                if (at.x != x)
                                {
                                    failures.Add(c.Name + "/" + variant + " (" + x + ", " + y + "): x moved to " + at.x);
                                }

                                var rect = new Rect(at.x - chip.x * 0.5f, at.y, chip.x, chip.y);
                                for (int i = 0; i < avoid.Length; i++)
                                {
                                    if (rect.Overlaps(Grown(avoid[i], UiEnemyStatusMarkers.ClusterClearance - 0.5f)))
                                    {
                                        failures.Add(c.Name + "/" + variant + " (" + x + ", " + y + "): stamp " + rect + " meets cluster " + i);
                                    }
                                }

                                if (hasFigure && rect.Overlaps(Grown(figure, UiEnemyStatusMarkers.FigureClearance - 0.5f)))
                                {
                                    failures.Add(c.Name + "/" + variant + " (" + x + ", " + y + "): stamp " + rect + " meets the figure");
                                }
                            }
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, failures.Count + " failures:\n" + string.Join("\n", failures.GetRange(0, Mathf.Min(20, failures.Count))));

            // The Mac case AD R1-B SHOULD-4 measured (SHOCK across the figure): the stamp lifts to the band above it, x unchanged.
            var body = Rect.MinMaxRect(-30f, -45f, 30f, 46f);
            Vector2 lifted = UiEnemyStatusMarkers.Resolve(new Vector2(5f, 30f), new Vector2(174f, 33f), new Rect[0], 0, true, body);
            Assert.AreEqual(5f, lifted.x, 0.001f, "x stays over the body");
            Assert.AreEqual(body.yMax + UiEnemyStatusMarkers.FigureClearance, lifted.y, 0.001f, "the band above the figure + 24");

            // A stamp with nothing in the way stands where its body puts it.
            var free = new Vector2(400f, 120f);
            Assert.AreEqual(free, UiEnemyStatusMarkers.Resolve(free, new Vector2(110f, 28f), new Rect[0], 0, true, body), "no drift");
        }

        /// <summary>
        /// AD 12b SHOULD-10: HudBanner.GlyphInkRight holds every Special glyph's empty right margin as the atlas has it -
        /// re-measured from the SDF file (alpha ≥ 0.5 = ink), within one texel - so the ink-to-ink gap holds per glyph.
        /// </summary>
        [Test]
        public void OnlineAfisi_SpecialGlifleri_MurekkepPayi_AtlasIleEslesir()
        {
            foreach (DD.Prototype.Core.Config.SpecialWeaponId id in System.Enum.GetValues(typeof(DD.Prototype.Core.Config.SpecialWeaponId)))
            {
                Sprite sprite = UiArt.SpecialIcon(id);
                Assert.IsNotNull(sprite, id + ": glif");
                float share;
                Assert.IsTrue(HudBanner.GlyphInkRight.TryGetValue(sprite.name, out share), sprite.name + ": GlyphInkRight girdisi yok");

                string path = UnityEditor.AssetDatabase.GetAssetPath(sprite.texture);
                var file = new Texture2D(2, 2);
                try
                {
                    Assert.IsTrue(file.LoadImage(System.IO.File.ReadAllBytes(path)), path);
                    Rect cell = sprite.rect;
                    int x0 = (int)cell.x, y0 = (int)cell.y, w = (int)cell.width, h = (int)cell.height;
                    int inkRight = -1;
                    for (int x = w - 1; x >= 0 && inkRight < 0; x--)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            if (file.GetPixel(x0 + x, y0 + y).a >= 0.5f)
                            {
                                inkRight = x;
                                break;
                            }
                        }
                    }

                    Assert.GreaterOrEqual(inkRight, 0, sprite.name + ": hücrede mürekkep");
                    float measured = (w - 1 - inkRight) / (float)w;
                    Debug.Log("[DD-TEST] 12c.glyphInkRight " + sprite.name + " table=" + share.ToString("F3") + " atlas=" + measured.ToString("F3"));
                    Assert.AreEqual(measured, share, 1f / w, sprite.name + ": tablo atlasla aynı (± 1 teksel)");
                }
                finally
                {
                    Object.DestroyImmediate(file);
                }
            }
        }

        /// <summary>
        /// B4 (AD R1-B SHOULD-2): the "&lt;SW&gt; ONLINE" line carries the Special's glyph at 48 px in the tint it is given,
        /// inside the wipe's mask, 16 px left of the words, glyph and words centred as one line; the generic line and every
        /// other banner carry none.
        /// </summary>
        [Test]
        public void OnlineAfisi_Glif48_KelimelerinSolunda16_SatirOrtada_DigerAfislerdeYok()
        {
            float? clock = UiMotion.TestNow;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 10f;
            var host = new GameObject("BannerHost", typeof(RectTransform), typeof(Canvas));   // TMP lays text out only under a canvas (EditMode)
            try
            {
                var root = (RectTransform)host.transform;
                root.sizeDelta = new Vector2(1920f, 1080f);
                var banner = new HudBanner(root, 196f);
                banner.Show("SPECIAL WEAPON ONLINE", 10f);
                Assert.IsFalse(banner.Glyph.enabled, "genel satırda glif yok (Special henüz adlanmadı)");

                Color arc = UiTokens.Get(UiGlyphTints.ChainLightning);
                banner.Retitle("RAIJU ONLINE", UiSprites.Disc, arc);
                Image glyph = banner.Glyph;
                Assert.IsTrue(glyph.enabled, "ONLINE satırı glifi taşır");
                Assert.AreEqual(48f, HudBanner.GlyphSize);
                Assert.AreEqual(16f, HudBanner.GlyphGap);
                Assert.AreEqual(new Vector2(48f, 48f), glyph.rectTransform.sizeDelta, "48 px");
                Assert.AreEqual(arc, glyph.color, "kimlik tonunda");
                Assert.AreSame(banner.Clip, glyph.transform.parent, "silme maskesinin içinde: satır glifle açılır");

                TMP_Text text = banner.Text;
                float width = text.rectTransform.sizeDelta.x;
                text.ForceMeshUpdate();
                TMP_TextInfo info = text.textInfo;
                Assert.Greater(info.characterCount, 0, "metin dizildi");
                float wordsLeft = info.characterInfo[0].origin;
                float wordsRight = info.characterInfo[info.characterCount - 1].xAdvance;
                float glyphLeft = glyph.rectTransform.anchoredPosition.x - HudBanner.GlyphSize * 0.5f;
                float glyphRight = glyphLeft + HudBanner.GlyphSize;
                // AD 12b SHOULD-10: ink to ink - the cell's empty right margin and the first letter's bearing do not count.
                float bearing = HudBanner.LeadingBearing(text);
                Assert.GreaterOrEqual(bearing, 0f);
                Assert.Less(bearing, text.fontSize * 0.2f, "bir harf yan boşluğu, satır değil");
                float glyphInk = glyphRight - HudBanner.GlyphInkRightPx(glyph.sprite);
                Assert.AreEqual(glyphInk + HudBanner.GlyphGap, wordsLeft + bearing, 2f, "glif mürekkebi kelime mürekkebinin 16 px solunda (± TMP yuvarlaması)");
                Assert.AreEqual(width * 0.5f, (glyphLeft + wordsRight) * 0.5f, 2f, "glif + kelimeler tek satır olarak ortada");
                Assert.GreaterOrEqual(glyphLeft, 0f, "glif afişin içinde");
                Assert.LessOrEqual(wordsRight, width, "kelimeler afişin içinde");

                banner.Show("ROUND 3", 11f);
                Assert.IsFalse(banner.Glyph.enabled, "başka afiş glif taşımaz");
                Assert.AreEqual(0f, text.margin.x, 0.001f, "kelimeler yeniden tam ortada");
                banner.Show("SPECIAL WEAPON ONLINE", 12f);
                banner.Retitle("NAMEKUJI ONLINE");
                Assert.IsFalse(banner.Glyph.enabled, "glifsiz yeniden adlandırma glifi göstermez");
            }
            finally
            {
                Object.DestroyImmediate(host);
                UiMotion.ResetForTests();
                UiMotion.TestNow = clock;
            }
        }

        /// <summary>
        /// B5 (AD R1-A SHOULD-6, the shoulder half): the rear-edge arc wears ArcShouldered60, whose stroke holds ≥ 0.5 of its
        /// peak out to 80 % of the chord (ArcSoft60 fell to ≈ 0.38 there); the E17 damage wedge keeps ArcSoft60.
        /// </summary>
        [Test]
        public void KenarYayi_OmuzluSprite_Kirisin80inde_YariTepe_HasarKamasiEski()
        {
            var host = new GameObject("ArcHost", typeof(RectTransform));
            try
            {
                var layer = (RectTransform)host.transform;
                var edge = new HudRearEdge(layer);
                var arcs = new HudPlayerArcs(layer);
                Assert.AreSame(UiSprites.ArcShouldered60, edge.Arc.sprite, "kenar yayı omuzlu sprite");
                Assert.AreSame(UiSprites.ArcSoft60, arcs.DamageArc.sprite, "E17 hasar kaması ArcSoft60'ta kalır");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            // Along the stroke's mid radius: the peak at 12 o'clock, then the point at 80 % of the chord (sin θ = 0.4).
            float shouldered = ChordRatio(UiSprites.ArcShouldered60.texture, 0.4f);
            float soft = ChordRatio(UiSprites.ArcSoft60.texture, 0.4f);
            Assert.GreaterOrEqual(shouldered, 0.5f, "omuzlu yay kirişin %80'inde tepe değerinin ≥ yarısı");
            Assert.Less(soft, 0.5f, "ArcSoft60 orada yarının altındaydı (≈ 80 px çekirdek)");
        }

        /// <summary>Alpha at chord fraction <paramref name="halfChord"/> (of the radius) over alpha at the apex, on the band's mid radius.</summary>
        private static float ChordRatio(Texture2D texture, float halfChord)
        {
            float half = texture.width * 0.5f;
            float outer = half - 2f, inner = outer * 0.75f, mid = (inner + outer) * 0.5f;
            float theta = Mathf.Asin(halfChord);
            float peak = texture.GetPixelBilinear(0.5f, (half + mid) / texture.height).a;
            float at = texture.GetPixelBilinear((half + mid * Mathf.Sin(theta)) / texture.width, (half + mid * Mathf.Cos(theta)) / texture.height).a;
            Assert.Greater(peak, 0.5f, "tepe noktası çizginin üstünde");
            return at / peak;
        }

        /// <summary>
        /// B10 (AD FP cluster SHOULD-2): the ammo LOW word carries its noun - the pack's hud.ammo.low, read by the HUD in every
        /// mode (in first person it shares a column with the health LOW).
        /// </summary>
        [Test]
        public void MermiDusukKelimesi_IsminiTasir_PakettenOkunur()
        {
            Assert.IsTrue(UiStrings.Has("hud.ammo.low"), "R1 paketi hud.ammo.low");
            Assert.AreEqual("LOW AMMO", UiStrings.Get("hud.ammo.low"), "en: LOW AMMO");
        }

        private static Rect Grown(Rect r, float by)
        {
            return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
        }
    }
}
