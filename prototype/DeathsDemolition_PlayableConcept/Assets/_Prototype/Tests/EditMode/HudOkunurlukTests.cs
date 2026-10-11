using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-105 (R4 UX audit, part D: HUD legibility - AUDIT-VISUAL §15) · the rules that need no scene. The live HUD half (the
    /// built labels at 1080p and on the Mac, the pause in a real run) is PlayMode HudOkunurlukPlayTests.
    /// - V-52: the alert words (RELOAD + key, EMPTY, LOW AMMO, LOW, SURROUNDED ...) ≥ 20 px at 1080p, growing with TextScale.
    /// - V-53: the secondary lines (the weapon line, "/ max", "recovery in") ≥ 16 px AS DRAWN - the calm posture's 0.94 included.
    /// - Halo: UiHudInk.ApplyHalo is the centred underlay in HudInk on the shipped keyword pair, wider than the base ink, and
    ///   the base ink the menus use is unchanged; every HUD word colour clears 4.5:1 against it over a white world (linear).
    /// - V-41: the pause hides the HUD, except in the death beat.
    /// </summary>
    [TestFixture]
    [Category("WP105")]
    public sealed class HudOkunurlukTests
    {
        private static readonly float[] TextScales = { 1f, 1.1f, 1.2f, 1.25f };

        // ------------------------------------------------------------------ V-52 / V-53: sizes

        [Test]
        public void UyariKelimeleri_1080pte20pxAltinaInmez_MetinOlcegiyleBuyur()
        {
            Assert.AreEqual(20f, RunHudScreen.AlertWordPx, "V-52 tabanı: Body 18 × 1,1 → 20");
            Assert.AreEqual(20f, RunHudScreen.AlertWordSize(1f));
            Assert.AreEqual(25f, RunHudScreen.AlertWordSize(1.25f), "metin ölçeği 1,25 → 25");
            Assert.AreEqual(20f, RunHudScreen.AlertWordSize(0.8f), "metin ölçeği tabanın altına çekemez");
            Assert.AreEqual(25f, RunHudScreen.AlertWordSize(2f), "kitin Label tavanı (1,25)");

            float previous = 0f;
            foreach (float scale in TextScales)
            {
                float size = RunHudScreen.AlertWordSize(scale);
                Assert.GreaterOrEqual(size, 20f, "uyarı kelimesi ≥ 20 px (ölçek " + scale + ")");
                Assert.GreaterOrEqual(size, previous, "ölçekle küçülmez");
                previous = size;

                // RELOAD's key chip encloses its letter at the word's size, and never shrinks under the 8c chip (22).
                float chip = RunHudScreen.AlertPromptSize(size);
                Assert.GreaterOrEqual(chip, size + 6f, "tuş çipi harfini sarar (ölçek " + scale + ")");
                Assert.GreaterOrEqual(chip, 22f);
                Assert.GreaterOrEqual(RunHudScreen.HudLineHeight(size), size, "satır kutusu kelimeyi taşır");
            }
        }

        [Test]
        public void IkincilMetinler_CizildigiBoyut16pxAltinaInmez_SakinDurusDahil()
        {
            Assert.AreEqual(16f, RunHudScreen.SecondaryTextPx);
            Assert.Less(RunHudScreen.CalmPostureScale, 1f, "sakin duruş kümeleri küçültür - boyut bunu karşılamalı");
            Assert.AreEqual(17f, RunHudScreen.SecondaryTextSize(1f, true), "16 / 0,94 → 17 (yuvarlanır: 15,98 px çizilir; HudCoverage tavanı)");
            Assert.AreEqual(16f, RunHudScreen.SecondaryTextSize(1f, false));

            foreach (float scale in TextScales)
            {
                float calm = RunHudScreen.SecondaryTextSize(scale, true) * RunHudScreen.CalmPostureScale;
                Assert.GreaterOrEqual(calm, 15.95f, "sakin kümede çizilen ikincil satır ≥ 16 px, yarım pikselin içinde (ölçek " + scale + ")");
                Assert.GreaterOrEqual(RunHudScreen.SecondaryTextSize(scale, false), 16f, "duruşsuz ikincil satır ≥ 16 px (ölçek " + scale + ")");
            }

            // The gold numeral keeps its NumericM (no posture scale): already over the floor, it takes the halo only.
            Assert.GreaterOrEqual(UiTokens.FontSize(RunHudScreen.GoldCounterType), 16, "altın sayısı ≥ 16 px");
        }

        // ------------------------------------------------------------------ the halo

        [Test]
        public void Hale_MerkezliAltKatman_GonderilenAnahtarCifti_MenuMurekkebiDegismez()
        {
            var host = new GameObject("HaloHost", typeof(RectTransform), typeof(Canvas));   // TMP lays text out only under a canvas
            try
            {
                TMP_Text halo = NewLabel(host.transform, "Halo", UiType.Label, RunHudScreen.AlertWordSize(1f));
                TMP_Text menu = NewLabel(host.transform, "Menu", UiType.Label, RunHudScreen.AlertWordSize(1f));
                Assume.That(halo.font != null && halo.font.material != null, "Text face yok (Resources/UI/Fonts)");

                UiHudInk.ApplyHalo(halo);
                UiHudInk.Apply(menu);
                Material h = halo.fontSharedMaterial;
                Material m = menu.fontSharedMaterial;
                Assert.IsTrue(UiHudInk.HasHalo(halo), "HUD kelimesi haleyi giyer");
                Assert.IsFalse(UiHudInk.HasHalo(menu), "Apply hale vermez");
                Assert.AreNotSame(h, m, "hale ayrı malzeme - menülerin mürekkebi aynı kalır");

                // The halo: the underlay, centred, dilated, in HudInk at the outline's alpha; the outline pass at width 0.
                Assert.IsTrue(h.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay), "UNDERLAY_ON");
                Assert.IsTrue(h.IsKeywordEnabled(ShaderUtilities.Keyword_Outline),
                    "OUTLINE_ON açık kalır: build'de yaşayan çift OUTLINE_ON + UNDERLAY_ON (shader_feature)");
                Assert.AreEqual(0f, h.GetFloat(ShaderUtilities.ID_OutlineWidth), 1e-6f, "dış çizgi genişliği 0 - yüz incelmez");
                Assert.AreEqual(0f, h.GetFloat(ShaderUtilities.ID_UnderlayOffsetX), 1e-6f, "hale merkezde (gölge değil)");
                Assert.AreEqual(0f, h.GetFloat(ShaderUtilities.ID_UnderlayOffsetY), 1e-6f);
                Assert.AreEqual(UiHudInk.HaloDilate, h.GetFloat(ShaderUtilities.ID_UnderlayDilate), 1e-6f);
                Color ink = h.GetColor(ShaderUtilities.ID_UnderlayColor);
                Color hudInk = UiTokens.Get(UiColor.HudInk);
                Assert.AreEqual(hudInk.r, ink.r, 1e-4f, "HudInk tonu");
                Assert.AreEqual(hudInk.g, ink.g, 1e-4f);
                Assert.AreEqual(hudInk.b, ink.b, 1e-4f);
                Assert.AreEqual(UiTokens.HudInkAlpha, ink.a, 1e-4f, "dış çizginin α'sı (0,98)");

                // The base ink is what it was: an outline and a contact shadow down-right.
                Assert.Greater(m.GetFloat(ShaderUtilities.ID_OutlineWidth), 0f, "menü mürekkebi: dış çizgi");
                Assert.Greater(Mathf.Abs(m.GetFloat(ShaderUtilities.ID_UnderlayOffsetX)), 0f, "menü mürekkebi: kayık gölge");

                // Wider than the base ink round the same 20 px word (TMP's own ratios; ink beyond the letter, SDF units → px).
                float haloReach = InkBeyondFace(h), baseReach = InkBeyondFace(m);
                float toPx = h.GetFloat(ShaderUtilities.ID_GradientScale) * halo.fontSize / Mathf.Max(1f, halo.font.faceInfo.pointSize);
                Debug.Log("[DD-TEST] wp105.halo text20 haloReach=" + haloReach.ToString("0.000") + " (" + (haloReach * toPx).ToString("0.00") +
                          " px) baseReach=" + baseReach.ToString("0.000") + " (" + (baseReach * toPx).ToString("0.00") + " px) ratioC=" +
                          h.GetFloat(ShaderUtilities.ID_ScaleRatio_C).ToString("0.000"));
                Assert.Greater(h.GetFloat(ShaderUtilities.ID_ScaleRatio_C), 0f, "atlas hale için yer bırakır (ScaleRatioC > 0)");
                Assert.Greater(haloReach, baseReach, "hale eski mürekkepten geniş");

                // The Numeric face (the gold, "/ 100") takes the halo the same way.
                TMP_Text numeral = NewLabel(host.transform, "Numeral", UiType.NumericM, UiTokens.FontSize(UiType.NumericM));
                UiHudInk.ApplyHalo(numeral);
                Assert.IsTrue(UiHudInk.HasHalo(numeral), "Numeric yüz de haleyi giyer");
                Assert.AreNotSame(h, numeral.fontSharedMaterial, "yüz başına bir malzeme");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// V-52 / V-53 "≥ 4.5:1 on a bright ground": every colour a HUD alert or secondary word draws in, against the halo
        /// (HudInk at UiHudInk.HaloAlpha) composited over a white world, the five world roles of HudKontrasti and the damage
        /// edge's peak under the health block - in LINEAR light (HudArt.GlyphContrast; the project blends after the sRGB decode).
        /// </summary>
        [Test]
        public void Hale_KelimeRenkleri_ParlakZemindeAA()
        {
            var world = new (string Name, Color Colour)[]
            {
                ("white", Color.white),
                ("plaster", Hex(0xCD, 0xCB, 0xC1)),
                ("sky-mist", Hex(0xD0, 0xD7, 0xDE)),
                ("stone", Hex(0x6E, 0x6B, 0x66)),
                ("moss", Hex(0x4E, 0x5A, 0x3E)),
                ("earth", Hex(0x4D, 0x46, 0x39)),
                ("damage-edge", EdgeOverWhite())
            };

            // LOW / SURROUNDED / BEHIND YOU / ENDING / DOWN; EMPTY / LOW AMMO / RELOAD / SUSPENDED / ELITE (both warning hues);
            // the reloading ring's caption colour; the weapon line, "/ max", "recovery in"; RECOVERING; the gold; the numbers.
            var words = new[]
            {
                UiColor.StateDanger, UiColor.StateWarning, UiColor.BarReload, UiColor.TextSecondary, UiColor.StateSuccess,
                UiColor.StateGold, UiColor.TextPrimary
            };

            bool hue = UiTokens.DistinctWarningHue;
            UiPalette palette = UiTokens.Palette;
            var report = new System.Text.StringBuilder("[DD-TEST] wp105.haloContrast");
            int failures = 0;
            try
            {
                foreach (UiPalette p in new[] { UiPalette.Default, UiPalette.ColorBlindSafe })
                {
                    foreach (bool distinct in new[] { true, false })
                    {
                        UiTokens.Palette = p;
                        UiTokens.DistinctWarningHue = distinct;
                        foreach (UiColor token in words)
                        {
                            foreach (var w in world)
                            {
                                float ratio = HudArt.GlyphContrast(UiTokens.Get(token), 1f, UiTokens.Get(UiColor.HudInk), UiHudInk.HaloAlpha, w.Colour);
                                if (ratio < 4.5f)
                                {
                                    failures++;
                                    report.Append("\nFAIL ");
                                }
                                else
                                {
                                    report.Append('\n');
                                }

                                report.Append(p).Append(distinct ? " amber " : " cream ").Append(token).Append(" on the halo over ").Append(w.Name)
                                      .Append(": ").Append(ratio.ToString("0.00")).Append(":1");
                            }
                        }
                    }
                }
            }
            finally
            {
                UiTokens.Palette = palette;
                UiTokens.DistinctWarningHue = hue;
            }

            Debug.Log(report.ToString());
            Assert.AreEqual(0, failures, "hale üstünde 4,5:1'in altında kalan HUD kelime rengi:\n" + report);
        }

        // ------------------------------------------------------------------ V-41: the pause

        [Test]
        public void Duraklat_HudGizlenir_OlumAniHaric()
        {
            Assert.IsTrue(RunHudScreen.HiddenUnderPause(true, RunPhase.Fighting), "savaşta duraklat → HUD gizli");
            Assert.IsTrue(RunHudScreen.HiddenUnderPause(true, RunPhase.Upgrading), "dükkânın üstünde duraklat → HUD gizli");
            Assert.IsTrue(RunHudScreen.HiddenUnderPause(true, RunPhase.Countdown));
            Assert.IsTrue(RunHudScreen.HiddenUnderPause(true, RunPhase.RoundComplete));
            Assert.IsFalse(RunHudScreen.HiddenUnderPause(false, RunPhase.Fighting), "duraklat kapalı → HUD görünür");
            Assert.IsFalse(RunHudScreen.HiddenUnderPause(true, RunPhase.Dead), "ölüm anında grubu ölüm karartması yönetir");
            Assert.IsFalse(RunHudScreen.HiddenUnderPause(false, RunPhase.Dead));
        }

        // ------------------------------------------------------------------ helpers

        private static TMP_Text NewLabel(Transform parent, string name, UiType type, float size)
        {
            TMP_Text text = UiFactory.Label(name, parent, "SURROUNDED 100", type, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            text.fontSize = size;
            return text;
        }

        /// <summary>
        /// The ink a material lays beyond the letter's own edge, in SDF alpha units (TMP_SDF-Mobile): an outline's outer half and
        /// its face dilate (ScaleRatioA), or a centred underlay's dilate (ScaleRatioC) less its offset - an offset underlay is a
        /// shadow on one side, not a ring. Ratios refreshed as TMP does before it pads a mesh.
        /// </summary>
        private static float InkBeyondFace(Material material)
        {
            ShaderUtilities.UpdateShaderRatios(material);
            float outline = 0f;
            if (material.IsKeywordEnabled(ShaderUtilities.Keyword_Outline))
            {
                float rA = material.GetFloat(ShaderUtilities.ID_ScaleRatio_A);
                outline = (material.GetFloat(ShaderUtilities.ID_OutlineWidth) + material.GetFloat(ShaderUtilities.ID_FaceDilate)) * rA * 0.5f;
            }

            float ring = 0f;
            if (material.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay))
            {
                float rC = material.GetFloat(ShaderUtilities.ID_ScaleRatio_C);
                float offset = Mathf.Max(Mathf.Abs(material.GetFloat(ShaderUtilities.ID_UnderlayOffsetX)), Mathf.Abs(material.GetFloat(ShaderUtilities.ID_UnderlayOffsetY)));
                ring = Mathf.Max(0f, (material.GetFloat(ShaderUtilities.ID_UnderlayDilate) * 0.5f - offset) * rC);
            }

            return Mathf.Max(outline, ring);
        }

        private static Color EdgeOverWhite()
        {
            Color edge = UiTokens.Get(UiColor.DamageEdge);
            float a = UiTokens.DamageEdgePeakAlpha * 0.49f;
            Color l = edge.linear;
            return new Color(l.r * a + (1f - a), l.g * a + (1f - a), l.b * a + (1f - a), 1f).gamma;
        }

        private static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
