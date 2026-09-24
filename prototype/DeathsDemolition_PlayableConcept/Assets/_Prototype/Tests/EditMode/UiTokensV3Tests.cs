using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using DD.Prototype.UI.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 1 · token v3 (UI v3 direction §2, tests T1/T2 + freshness). Locks the
    /// full colour table in both palettes (a refactor changes 0 values), the U4 warning
    /// switch in both positions, the ΔL* separation rules, the colour reserves by source
    /// scan, the motion / layer tokens, and that the exported HLSL / JSON / style-guide
    /// table match UiTokens.cs.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiTokensV3Tests
    {
        private UiPalette _palette;
        private bool _distinctWarning;

        [SetUp]
        public void SetUp()
        {
            _palette = UiTokens.Palette;
            _distinctWarning = UiTokens.DistinctWarningHue;
        }

        [TearDown]
        public void TearDown()
        {
            UiTokens.Palette = _palette;
            UiTokens.DistinctWarningHue = _distinctWarning;
        }

        // ------------------------------------------------------------------ T? TableEqualsV2

        /// <summary>(token, Default "#RRGGBB", alpha, ColorBlindSafe "#RRGGBB" or null = same as Default).</summary>
        private static readonly object[][] Table =
        {
            new object[] { UiColor.Transparent, "#000000", 0f, null },
            new object[] { UiColor.ScrimOverlay, "#12100F", 0.88f, null },
            new object[] { UiColor.SurfaceBase, "#1A1816", 1f, null },
            new object[] { UiColor.SurfacePanel, "#232120", 1f, null },
            new object[] { UiColor.SurfaceRaised, "#34322F", 1f, null },
            new object[] { UiColor.SurfaceInset, "#12100F", 1f, null },
            new object[] { UiColor.HudInk, "#12100F", UiTokens.HudInkAlpha, null },
            new object[] { UiColor.BorderHairline, "#4A4744", 0.85f, null },
            new object[] { UiColor.BorderStrong, "#F3EEE3", 0.45f, null },
            new object[] { UiColor.BorderFocus, "#F2D98C", 1f, null },
            new object[] { UiColor.BorderFocusHalo, "#12100F", 1f, null },
            new object[] { UiColor.TextPrimary, "#F3EEE3", 1f, null },
            new object[] { UiColor.TextSecondary, "#B9B2A6", 1f, null },
            new object[] { UiColor.TextMuted, "#9A948C", 1f, null },
            new object[] { UiColor.TextOnAccent, "#F3EEE3", 1f, null },
            new object[] { UiColor.TextAccent, "#E8806F", 1f, null },
            new object[] { UiColor.AccentPrimary, "#C4362B", 1f, null },
            new object[] { UiColor.AccentPrimaryPressed, "#9E2A21", 1f, null },
            new object[] { UiColor.AccentSakura, "#E8A0B8", 1f, null },
            new object[] { UiColor.AccentWisteria, "#A99BD4", 1f, null },
            new object[] { UiColor.AccentMoss, "#59714A", 1f, null },
            new object[] { UiColor.DamageEdge, "#C4362B", 1f, null },
            new object[] { UiColor.StateDanger, "#EE7063", 1f, null },
            new object[] { UiColor.StateWarning, "#F7C766", 1f, null },
            new object[] { UiColor.StateSuccess, "#8FC079", 1f, "#7FA6D9" },
            new object[] { UiColor.StateGold, "#D9A93C", 1f, null },
            new object[] { UiColor.BarTrack, "#12100F", 1f, null },
            new object[] { UiColor.BarHealth, "#748F60", 1f, "#5F8CC4" },
            new object[] { UiColor.BarHealthLow, "#EE7063", 1f, null },
            new object[] { UiColor.BarXp, "#E8A0B8", 1f, null },
            new object[] { UiColor.BarReload, "#F2D98C", 1f, null },
            new object[] { UiColor.BarSpecial, "#D9A93C", 1f, null },
            new object[] { UiColor.ShadowKey, "#12100F", 1f, null },
            new object[] { UiColor.SheenRim, "#F3EEE3", 0.10f, null },
            new object[] { UiColor.InkStroke, "#12100F", 0.90f, null },
            new object[] { UiColor.GoldLeaf, "#D9A93C", 1f, null },
            new object[] { UiColor.SilverLeaf, "#F3EEE3", 1f, null },
            new object[] { UiColor.HoldFill, "#F3EEE3", 0.15f, null },
            new object[] { UiColor.LanternGlow, "#F7C766", 1f, null }
        };

        [Test]
        public void RenkTablosu_V3_IkiPalet_BirebirKilitli()
        {
            UiTokens.DistinctWarningHue = true;
            Assert.AreEqual(Enum.GetValues(typeof(UiColor)).Length, Table.Length, "her UiColor tabloda: yeni token = tabloya satır");
            var failures = new List<string>();
            foreach (object[] row in Table)
            {
                var token = (UiColor)row[0];
                Color expected = Parse((string)row[1], (float)row[2]);
                Color expectedCb = row[3] != null ? Parse((string)row[3], (float)row[2]) : expected;
                UiTokens.Palette = UiPalette.Default;
                if (!Same(expected, UiTokens.Get(token))) failures.Add(token + " Default " + UiTokenExport.HexOf(UiTokens.Get(token)));
                UiTokens.Palette = UiPalette.ColorBlindSafe;
                if (!Same(expectedCb, UiTokens.Get(token))) failures.Add(token + " CB " + UiTokenExport.HexOf(UiTokens.Get(token)));
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ------------------------------------------------------------------ U4 switch (AD MUST 2)

        [Test]
        public void UyariAnahtari_IkiYol_AsliParaAltiniOlmaz()
        {
            Color gold = UiTokens.Get(UiColor.StateGold);
            foreach (UiPalette palette in new[] { UiPalette.Default, UiPalette.ColorBlindSafe })
            {
                UiTokens.Palette = palette;
                UiTokens.DistinctWarningHue = true;
                Assert.AreEqual("#F7C766", UiTokenExport.HexOf(UiTokens.Get(UiColor.StateWarning)), palette + " evet yolu Amber300");
                UiTokens.DistinctWarningHue = false;
                Color warning = UiTokens.Get(UiColor.StateWarning);
                Assert.AreEqual(UiTokenExport.HexOf(UiTokens.Get(UiColor.TextPrimary)), UiTokenExport.HexOf(warning), palette + " hayır yolu: uyarı renksiz (krem)");
                Assert.IsFalse(Same(gold, warning), palette + ": uyarı asla para altını hex'inde durmaz");
            }
        }

        // ------------------------------------------------------------------ T2 separation (ΔL*)

        [Test]
        public void Ayrim_DeltaL_IkiPalet_Onun_Ustunde()
        {
            UiTokens.DistinctWarningHue = true;
            var pairs = new[]
            {
                new[] { UiColor.StateWarning, UiColor.StateGold },
                new[] { UiColor.StateWarning, UiColor.StateDanger },
                new[] { UiColor.BorderFocus, UiColor.StateGold },
                new[] { UiColor.StateDanger, UiColor.AccentPrimary }
            };

            var report = new System.Text.StringBuilder();
            foreach (UiPalette palette in new[] { UiPalette.Default, UiPalette.ColorBlindSafe })
            {
                UiTokens.Palette = palette;
                foreach (UiColor[] pair in pairs)
                {
                    float delta = Mathf.Abs(LStar(UiTokens.Get(pair[0])) - LStar(UiTokens.Get(pair[1])));
                    report.Append(palette).Append(' ').Append(pair[0]).Append('↔').Append(pair[1]).Append(' ').Append(delta.ToString("0.0")).Append('\n');
                    Assert.GreaterOrEqual(delta, 10f, palette + " " + pair[0] + "↔" + pair[1] + " ΔL* " + delta.ToString("0.0"));
                }
            }

            Debug.Log("[DD-TEST] wp068.separation\n" + report);
        }

        // ------------------------------------------------------------------ T2 reserve registry (source scan)

        private static readonly Dictionary<string, string[]> Reserves = new Dictionary<string, string[]>
        {
            // Gold = currency counter, price, Special HUD (style guide §2.2 UI). UiKitSheet: the
            // capture-only component sheet shows the currency counter and the Special ring.
            // UiFxGlobals (slice 4): the Fx shader's gold-leaf global is StateGold; UiFxCaptureAgent:
            // capture-only Fx sample page (gold-leaf chips, sparks). WP-068 8b / 8-R: the HUD's own parts - HudReticle
            // (E15: Gold400 while a Special is live) and RunHudScreen.Ink.cs (the gold readout of the 8-R ink HUD).
            { "StateGold", new[] { "RunHudScreen.cs", "RunHudScreen.Ink.cs", "HudReticle.cs", "UpgradeScreen.cs", "ResultsViewModel.cs", "UiTokens.cs", "UiContrast.cs", "UiKitSheet.cs", "UiKitContrast.cs", "UiFxGlobals.cs", "UiFxCaptureAgent.cs" } },
            { "BarSpecial", new[] { "RunHudScreen.cs", "UiTokens.cs", "UiContrast.cs", "UiKitSheet.cs", "UiKitContrast.cs" } },
            { "GoldLeaf", new[] { "UiTokens.cs" } },
            // Vermilion = one primary CTA per screen/modal, heading tick, BUY pill, seal.
            // PauseScreen: the END RUN dialog frame - reviewed in slice 9 (modal v3).
            // UiFxCaptureAgent (slice 4): capture-only ink bleed / hanko stamp samples.
            // UiCurtain (10b-2): the boot logo seal - the one vermilion at boot, ink before loading (§15.3).
            { "AccentPrimary", new[] { "UiButton.cs", "UiFactory.cs", "UpgradeScreen.cs", "PauseScreen.cs", "UiTokens.cs", "UiContrast.cs", "UiFxCaptureAgent.cs", "UiCurtain.cs" } }
        };

        [Test]
        public void Rezervler_YalnizIzinliDosyalarda()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            var violations = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);
                foreach (KeyValuePair<string, string[]> reserve in Reserves)
                {
                    if (Regex.IsMatch(text, @"UiColor\." + reserve.Key + @"\b") && Array.IndexOf(reserve.Value, name) < 0)
                    {
                        violations.Add(reserve.Key + " in " + name);
                    }
                }
            }

            Assert.IsEmpty(violations, "rezerv rengi izinsiz yerde (stil kılavuzu §2.2 UI):\n" + string.Join("\n", violations));
        }

        // ------------------------------------------------------------------ motion / layer / grid tokens

        [Test]
        public void HareketTokenlari_EskiSabitlerleTutarli_EgrilerTasmaz()
        {
            Assert.AreEqual(UiTokens.MotionInstant, UiMotionTokens.Seconds(UiDuration.Instant), 1e-6f);
            Assert.AreEqual(UiTokens.MotionFast, UiMotionTokens.Seconds(UiDuration.Fast), 1e-6f);
            Assert.AreEqual(UiTokens.MotionBase, UiMotionTokens.Seconds(UiDuration.Base), 1e-6f);
            Assert.AreEqual(UiTokens.MotionScreen, UiMotionTokens.Seconds(UiDuration.Screen), 1e-6f);

            foreach (UiEase ease in Enum.GetValues(typeof(UiEase)))
            {
                Assert.AreEqual(0f, UiEaseCurve.Evaluate(ease, 0f), 1e-4f, ease + " 0");
                Assert.AreEqual(1f, UiEaseCurve.Evaluate(ease, 1f), 1e-4f, ease + " 1");
                float previous = 0f;
                for (int i = 1; i <= 100; i++)
                {
                    float v = UiEaseCurve.Evaluate(ease, i / 100f);
                    Assert.LessOrEqual(v, 1f + 1e-4f, ease + " taşma yok (overshoot)");
                    Assert.GreaterOrEqual(v, previous - 1e-4f, ease + " monoton");
                    previous = v;
                }
            }

            // CSS reference point: ease-out-ish Settle is past the midpoint early.
            Assert.Greater(UiEaseCurve.Evaluate(UiEase.Settle, 0.3f), 0.6f, "Settle erken varır");
            Assert.Less(UiEaseCurve.Evaluate(UiEase.Exit, 0.3f), 0.2f, "Exit yavaş başlar");

            for (float s = 0f; s <= 0.2f; s += 0.005f)
            {
                float k = UiEaseCurve.StampScale(s);
                Assert.GreaterOrEqual(k, 1f, "Stamp 1,0'ın altına inmez");
                Assert.LessOrEqual(k, UiMotionTokens.StampPeakScale + 1e-5f, "Stamp tepe 1,12");
            }

            Assert.AreEqual(1f, UiEaseCurve.StampScale(UiMotionTokens.StampInSeconds), 1e-5f);
            Assert.AreEqual(5f, UiEaseCurve.Follow(10f, 0f, 0.1f, 0.1f), 1e-4f, "bir yarı ömürde mesafe yarıya iner");

            Assert.AreEqual(UiLayer.Hud, UiTokens.SortingHud);
            Assert.AreEqual(UiLayer.Screen, UiTokens.SortingScreen);
            Assert.AreEqual(UiLayer.Pause, UiTokens.SortingPause);
            Assert.AreEqual(UiLayer.Modal, UiTokens.SortingDialog);

            Assert.AreEqual(UiGrid.BindingPageWidth, Mathf.RoundToInt(UiTokens.ReferenceResolution.x / UiTheme.UiScaleMax) - 2 * UiGrid.PageMargin,
                "bağlayıcı sayfa = en büyük UI ölçeğinde tuval − iki sayfa marjı");
            Assert.AreEqual(95.33f, UiGrid.ColumnWidth(UiGrid.BindingPageWidth), 0.01f);
        }

        // ------------------------------------------------------------------ exports are fresh

        [Test]
        public void Disa_Aktarimlar_Taze()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            UiTokens.Palette = UiPalette.Default;
            UiTokens.DistinctWarningHue = true;

            Assert.AreEqual(UiTokenExport.BuildHlsl(), Read(project, UiTokenExport.HlslPath), "UiTokens.hlsl bayat: DD/UI/Export Tokens");
            Assert.AreEqual(UiTokenExport.BuildJson(), Read(project, UiTokenExport.JsonPath), "ui-tokens.json bayat: DD/UI/Export Tokens");
            string guide = Read(project, UiTokenExport.StyleGuidePath);
            StringAssert.Contains(UiTokenExport.BuildStyleGuideBlock(), guide, "stil kılavuzu §2.2 UI tablosu bayat: DD/UI/Export Tokens");
            Assert.AreEqual(guide, UiTokenExport.MergeIntoStyleGuide(guide), "birleştirme idempotent");
        }

        private static string Read(string project, string relative)
        {
            string path = Path.Combine(project, relative);
            Assert.IsTrue(File.Exists(path), "yok: " + relative);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        // ------------------------------------------------------------------ helpers

        private static Color Parse(string hex, float alpha)
        {
            int v = Convert.ToInt32(hex.Substring(1), 16);
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, alpha);
        }

        private static bool Same(Color a, Color b)
        {
            const float e = 0.6f / 255f;
            return Mathf.Abs(a.r - b.r) < e && Mathf.Abs(a.g - b.g) < e && Mathf.Abs(a.b - b.b) < e && Mathf.Abs(a.a - b.a) < 0.002f;
        }

        /// <summary>CIE L* of an sRGB colour (D65).</summary>
        private static float LStar(Color c)
        {
            float y = 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
            float f = y > 0.008856f ? Mathf.Pow(y, 1f / 3f) : (7.787f * y + 16f / 116f);
            return 116f * f - 16f;
        }

        private static float Linear(float v)
        {
            return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        }
    }
}
