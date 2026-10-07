using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 12b · D1 (AD 8a carried MUST for the closure) and D2 (AD 8b SHOULD-6, carried): every translucent token
    /// pair re-measured with the pinned composite - <see cref="UiContrast.Composite"/>, which blends in LINEAR light as
    /// the GPU does in this Linear project (8a's readback pinned it) - instead of the sRGB estimates the tokens were set
    /// with. A pair under its floor is fixed by raising the backing's α (or lowering a cream decoration's), never by
    /// moving text:
    ///   (a) T10, the menu plate: every word the plate carries, over the brightest plate texel under it (far / mid /
    ///       lantern at its breath peak / near, composited from the files; ±12 px parallax), through the real
    ///       UiBackdrop scrims (third, spine, header block and their fades), at the §12 matrix;
    ///   (b) the sheen field envelope and the 1 px SheenRim; (c) washi and kumiko over their surfaces, densest texel;
    ///   (d) the LanternGlow breath: T10 at its peak (in a) and outside the currency-gold band;
    ///   (e) the scrims under modal cards: the card's 3:1 boundary (fill or frame) over a grey-ramp world;
    ///   (f) HoldFill under the hold label, at rest and pressed.
    /// D2 (lane 2's files, test only): the rear hairline, the damage wedge and the reticle's Sumi twin; the gaps are
    /// logged for S-09 ("d2.gap"), the pairs that must hold are asserted.
    /// Every ratio is logged as "[DD-TEST] d1.* / d2.*".
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiTranslucentTokensTests
    {
        private const float Text = 4.5f;
        private const float Boundary = 3f;

        private const string PlateFolder = "Assets/_Art/Resources/MenuPlates/";
        private const string WashiTile = "Assets/_Prototype/UI/Resources/Fx/AP012_tile_washi_256.png";
        private const string KumikoTile = "Assets/_Prototype/UI/Art/Resources/UI/Tiles/AP012_tile_kumiko_asanoha_256.png";

        /// <summary>A long build stamp for the spine's foot (the "wp-…-g1234567 · release · date" form).</summary>
        private const string SampleBuildLabel = "wp-068-12b-closure-g0000000 · release · 2026-09-25";

        private readonly List<GameObject> _created = new List<GameObject>();
        private float _textScale;
        private UiPalette _palette;

        [SetUp]
        public void SetUp()
        {
            _textScale = UiTokens.TextScale;
            _palette = UiTokens.Palette;
            UiTokens.Palette = UiPalette.Default;
        }

        [OneTimeTearDown]
        public void Release()
        {
            PlateStack.Release();
        }

        [TearDown]
        public void TearDown()
        {
            UiTokens.TextScale = _textScale;
            UiTokens.Palette = _palette;
            foreach (GameObject go in _created)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        // ================================================================== helpers

        private static Color Opaque(UiColor token)
        {
            Color c = UiTokens.Get(token);
            c.a = 1f;
            return c;
        }

        private static Color Layer(UiColor token, float alpha)
        {
            Color c = UiTokens.Get(token);
            c.a = alpha;
            return c;
        }

        private static float Ratio(Color a, Color b)
        {
            return UiContrast.Ratio(a, b);
        }

        private static float Log(string key, float ratio, float floor)
        {
            Debug.Log("[DD-TEST] " + key + " " + ratio.ToString("F2") + " (floor " + floor.ToString("0.0") + ")");
            return ratio;
        }

        private static Texture2D LoadFile(string path)
        {
            Assert.IsTrue(File.Exists(path), path + " yok");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)), path);
            return texture;
        }

        private static float MaxRed(string path)
        {
            Texture2D t = LoadFile(path);
            byte max = 0;
            foreach (Color32 c in t.GetPixels32())
            {
                if (c.r > max) max = c.r;
            }

            UnityEngine.Object.DestroyImmediate(t);
            return max / 255f;
        }

        // UiContrast's own transfer functions (the composite below must be the GPU's, byte for byte of the formula).
        private static float Linear(float c)
        {
            return c <= 0.03928f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        private static float Encode(float l)
        {
            return l <= 0.0031308f ? l * 12.92f : 1.055f * Mathf.Pow(l, 1f / 2.4f) - 0.055f;
        }

        private static float Luminance(float r, float g, float b)
        {
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        // ================================================================== 0 · the pinned composite

        [Test]
        public void Kompozit_DogrusalIsikta_Sabitlendi()
        {
            Assert.IsTrue(UiContrast.BlendsInLinear, "proje Linear renk uzayında: UI GPU'da doğrusal ışıkta harmanlanır (8a okuması)");
            Color half = UiContrast.Composite(new Color(1f, 1f, 1f, 0.5f), Color.black);
            Assert.AreEqual(0.735f, half.r, 0.003f, "yarım beyaz siyah üstünde doğrusal ışıkta 0,735 (sRGB tahmini 0,5)");
        }

        // ================================================================== (b) (c) (f) · surfaces

        [Test]
        public void Zarflar_Parilti_Washi_Kumiko_Tutma_DogrusalIsikta()
        {
            var failures = new List<string>();
            Color muted = Opaque(UiColor.TextMuted);
            Color secondary = Opaque(UiColor.TextSecondary);
            Color primary = Opaque(UiColor.TextPrimary);

            // (b) the sheen field (the Fx Sheen band) at its envelope under TextMuted on both page surfaces.
            foreach (UiColor surface in new[] { UiColor.SurfacePanel, UiColor.SurfaceBase })
            {
                Color ground = UiContrast.Composite(Layer(UiColor.TextPrimary, UiMaterialSpec.SheenFieldAlphaMax), Opaque(surface));
                if (Log("d1.b.sheenField TextMuted/" + surface, Ratio(muted, ground), Text) < Text) failures.Add("sheen field on " + surface);
            }

            float past = Ratio(muted, UiContrast.Composite(Layer(UiColor.TextPrimary, UiMaterialSpec.SheenFieldAlphaMax + 0.005f), Opaque(UiColor.SurfacePanel)));
            Assert.Less(past, Text, "the field envelope is the measured edge (0.005 more drops TextMuted under 4.5)");

            // The 1 px rim is an edge (text ≥ 8 px away): a Decorative cue, measured for the record.
            Assert.AreEqual(0.10f, UiTokens.Get(UiColor.SheenRim).a, 1e-4f, "SheenRim 1 px α");
            foreach (UiColor surface in new[] { UiColor.SurfacePanel, UiColor.SurfaceBase })
            {
                Color rim = UiContrast.Composite(UiTokens.Get(UiColor.SheenRim), Opaque(surface));
                Log("d1.b.sheenRim/" + surface, Ratio(rim, Opaque(surface)), 1f);
            }

            // (c) washi (curtain card, tooltip, empty state) on SurfacePanel at its densest fibre texel.
            float washiTile = MaxRed(WashiTile);
            foreach (float alpha in new[] { UiMaterialSpec.WashiAlphaMin, UiMaterialSpec.WashiAlphaMax })
            {
                Color ground = UiContrast.Composite(Layer(UiColor.TextPrimary, alpha * washiTile), Opaque(UiColor.SurfacePanel));
                if (Log("d1.c.washi" + alpha.ToString("0.000") + " TextSecondary/Panel", Ratio(secondary, ground), Text) < Text) failures.Add("washi " + alpha + " TextSecondary");
                if (Log("d1.c.washi" + alpha.ToString("0.000") + " TextPrimary/Panel", Ratio(primary, ground), Text) < Text) failures.Add("washi " + alpha + " TextPrimary");
            }

            // Kumiko on the curtain doors (lacquer SurfaceInset) under the curtain's labels (eyebrow / subline TextSecondary, destination TextPrimary).
            float kumikoTile = MaxRed(KumikoTile);
            Color door = UiContrast.Composite(Layer(UiColor.TextPrimary, UiCurtain.KumikoAlpha * kumikoTile), Opaque(UiColor.SurfaceInset));
            if (Log("d1.c.kumiko TextSecondary/door", Ratio(secondary, door), Text) < Text) failures.Add("kumiko TextSecondary (UiCurtain.KumikoAlpha, lane 1)");
            if (Log("d1.c.kumiko TextPrimary/door", Ratio(primary, door), Text) < Text) failures.Add("kumiko TextPrimary (UiCurtain.KumikoAlpha, lane 1)");

            // (f) the hold label (TextPrimary) over the HoldFill sweep: at rest on SurfaceBase, pressed on SurfacePanel (a hold is a press).
            foreach (UiColor fill in new[] { UiColor.SurfaceBase, UiColor.SurfacePanel })
            {
                Color ground = UiContrast.Composite(UiTokens.Get(UiColor.HoldFill), Opaque(fill));
                if (Log("d1.f.holdFill TextPrimary/" + fill, Ratio(primary, ground), Text) < Text) failures.Add("HoldFill on " + fill);
            }

            Assert.IsEmpty(failures, "doğrusal ışıkta eşik altı:\n" + string.Join("\n", failures));
        }

        // ================================================================== (e) · scrims under modal cards

        [Test]
        public void ModalPerdeleri_KartSinirindeGriRampaUstunde_3e1()
        {
            var failures = new List<string>();
            Color fill = Opaque(UiColor.SurfacePanel);
            var scrims = new[]
            {
                new KeyValuePair<string, float>("ScrimOverlay (Pause, round-end)", UiTokens.Get(UiColor.ScrimOverlay).a),
                new KeyValuePair<string, float>("OpacityScrimUnderDialog (UiModal, END RUN)", UiTokens.OpacityScrimUnderDialog),
            };

            // Grey worlds from black up to the canonical white world (Cream100, as UiContrast's HUD and round-end cases).
            float whiteGrey = Encode(UiContrast.RelativeLuminance(Opaque(UiColor.TextPrimary)));
            foreach (KeyValuePair<string, float> scrim in scrims)
            {
                float worst = float.MaxValue;
                float worstWorld = 0f;
                for (int i = 0; i <= 64; i++)
                {
                    float g = whiteGrey * i / 63f;
                    Color world = i == 64 ? Opaque(UiColor.TextPrimary) : new Color(g, g, g, 1f);   // the ramp, then the cream-white world
                    Color ground = UiContrast.Composite(Layer(UiColor.SurfaceInset, scrim.Value), world);
                    Color frame = UiContrast.Composite(UiTokens.Get(UiColor.BorderStrong), ground);
                    float boundary = Mathf.Max(Ratio(fill, ground), Ratio(frame, ground));
                    if (boundary < worst)
                    {
                        worst = boundary;
                        worstWorld = g;
                    }
                }

                Log("d1.e.cardBoundary " + scrim.Key + " α" + scrim.Value.ToString("0.00") + " worst world " + worstWorld.ToString("0.00"), worst, Boundary);
                if (worst < Boundary) failures.Add(scrim.Key + " " + worst.ToString("F2") + " at world " + worstWorld.ToString("0.00"));
            }

            // The modal's words stand on its opaque card: the scrim does not touch them.
            Log("d1.e.modalBody TextSecondary/Panel", Ratio(Opaque(UiColor.TextSecondary), fill), Text);
            Assert.IsEmpty(failures, "modal kartının sınırı eşik altı:\n" + string.Join("\n", failures));
        }

        // ================================================================== (d) · lantern

        [Test]
        public void FenerNefesi_AltinBandininDisinda()
        {
            PlateStack plate = PlateStack.Instance;
            Color gold = Opaque(UiColor.StateGold);
            Vector3 goldLab = Lab(gold);
            float worst = float.MaxValue;
            foreach (float amount in new[] { 1f, UiBackdrop.BreathFloor })
            {
                float alpha = UiBackdrop.LanternPeak * amount;
                float d = plate.MinLanternDistance(alpha, goldLab);
                Log("d1.d.lantern α" + alpha.ToString("0.000") + " ΔE76 to StateGold (min over the glow core)", d, 10f);
                worst = Mathf.Min(worst, d);
            }

            // For the AD: the glow's on-screen chroma (max − min, sRGB) over SurfaceBase at the peak - the 6ab note's
            // "s < 0.30 after α" never named its metric; this is the number it would be judged on.
            Color glow = UiContrast.Composite(Layer(UiColor.LanternGlow, UiBackdrop.LanternPeak), Opaque(UiColor.SurfaceBase));
            float chroma = Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b)) - Mathf.Min(glow.r, Mathf.Min(glow.g, glow.b));
            Debug.Log("[DD-TEST] d1.d.lanternChroma peak/SurfaceBase " + chroma.ToString("F3"));
            Assert.GreaterOrEqual(worst, 10f, "fener parıltısı para altınından ayrı (ΔE76 ≥ 10, T2 ayrım eşiği)");
        }

        private static Vector3 Lab(Color c)
        {
            float r = Linear(c.r), g = Linear(c.g), b = Linear(c.b);
            float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.95047f;
            float y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            float z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.08883f;
            float fx = LabF(x), fy = LabF(y), fz = LabF(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float LabF(float t)
        {
            return t > 216f / 24389f ? Mathf.Pow(t, 1f / 3f) : (24389f / 27f * t + 16f) / 116f;
        }

        // ================================================================== (a) · T10 on the menu plate

        /// <summary>S-05's clamp holds the third at the fixed 0.55 (AD §14 MUST-5): TextSecondary ≥ 4.5 on its brightest texel.</summary>
        [Test]
        public void T10_SolUcteBir_Sabit055_KenetliPlakada()
        {
            Assert.AreEqual(0.55f, UiBackdrop.ScrimAlpha, 1e-4f, "AD §14 MUST-5: sol üçte bir scrim α 0,55 sabit");
            Assert.AreEqual(UiColor.TextSecondary, MainMenuScreen.PlateCaption, "plakadaki en soluk metin TextSecondary (T10)");
            PlateStack plate = PlateStack.Instance;
            Color brightest;
            float uMax;
            plate.Brightest(0f, 0f, plate.ClampU - 0.001f, 1f, out brightest, out uMax);   // the boundary texel straddles x1
            Color ground = UiContrast.Composite(Layer(UiColor.SurfaceBase, UiBackdrop.ScrimAlpha), brightest);
            float ratio = Log("d1.a.third TextSecondary (S-05 band, 16:9)", Ratio(Opaque(UiColor.TextSecondary), ground), Text);
            Assert.GreaterOrEqual(ratio, Text, "S-05'in kenetli bandı 0,55 ile TextSecondary taşır");
        }

        private static readonly float[][] Matrix = BuildMatrix();

        private static float[][] BuildMatrix()
        {
            var screens = new[] { new[] { 1920f, 1080f }, new[] { 2560f, 1080f }, new[] { 3024f, 1964f }, new[] { 2560f, 1600f } };
            var list = new List<float[]>();
            foreach (float[] s in screens)
            {
                foreach (float ui in new[] { 0.9f, 1f, 1.2f })
                {
                    foreach (float ts in new[] { 1f, 1.25f })
                    {
                        list.Add(new[] { s[0], s[1], ui, ts });
                    }
                }
            }

            return list.ToArray();
        }

        /// <summary>
        /// T10 at the §12 matrix (AD 6ab MUST-2: the title and subtitle, not only the third): the header words over the
        /// block, the spine's captions over the text-safe scrim, each over the brightest plate texel under it (±12 px).
        /// A spine caption whose texels reach past S-05's clamp band (menu_plates.json text_safe) is logged as pending the
        /// plate re-clamp - the fixed 0.55 cannot carry an unclamped plate - never counted as a pass.
        /// </summary>
        [Test]
        public void T10_MenuPlakasi_HerKelimeEnParlakTekselde_Matris()
        {
            PlateStack plate = PlateStack.Instance;
            var failures = new List<string>();
            var pending = new List<string>();
            foreach (float[] point in Matrix)
            {
                MeasureMenu(plate, point[0], point[1], point[2], point[3], failures, pending);
            }

            foreach (string p in pending)
            {
                Debug.LogWarning("[DD-TEST] d1.a.pendingS05 " + p);
            }

            Debug.Log("[DD-TEST] d1.a.pendingS05 count " + pending.Count);
            Assert.IsEmpty(failures, "T10 (doğrusal ışık) eşik altı:\n" + string.Join("\n", failures));
        }

        private struct Word
        {
            public string Name;
            public Rect Rect;          // canvas px, top-left origin
            public UiColor Colour;
            public bool Header;
        }

        private void MeasureMenu(PlateStack plate, float w, float h, float ui, float ts, List<string> failures, List<string> pending)
        {
            UiTokens.TextScale = ts;
            string id = w + "x" + h + " ui" + ui + " ts" + ts;
            Vector2 reference = new Vector2(1920f, 1080f) / ui;
            float fit = Mathf.Min(w / reference.x, h / reference.y);
            var canvas = new Vector2(w / fit, h / fit);

            var root = new GameObject("Canvas " + id, typeof(RectTransform));
            _created.Add(root);
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = canvas;

            // The page (UiScreen: PageHost 96 / 64, capped at ContentMaxWidth by UiMaxWidth).
            float width = Mathf.Min(canvas.x - (int)UiSpace.Page * 2f, UiTokens.ContentMaxWidth);
            float left = Mathf.Round(Mathf.Max(0f, (canvas.x - width) * 0.5f));
            float top = (int)UiSpace.Section;

            // WP-081 menu v4: the name on as many lines as the spine needs at the top of it, the rail under it (Column "Nav"
            // 0..0.26 from title + XXL to the page's foot): the focused item's word and PLAY's sub-line beside the icons,
            // the build line at the foot - every word inside the spine's scrim (no header block any more).
            float navWidth = width * 0.26f;
            float longest = 0f;
            foreach (string part in MainMenuScreen.HeaderTitle.Split(' '))
            {
                longest = Mathf.Max(longest, Mathf.Ceil(Width(rootRect, part, UiType.Display)));
            }

            float titleBox = Mathf.Max(navWidth, longest + 2f);   // MainMenuScreen.BuildTitle: words stay whole
            float titleW = Mathf.Min(titleBox, Mathf.Ceil(Width(rootRect, MainMenuScreen.HeaderTitle, UiType.Display)));
            float titleH = Mathf.Max(UiFactory.LineHeight(UiType.Display),
                Mathf.Ceil(WrappedHeight(rootRect, MainMenuScreen.HeaderTitle, UiType.Display, titleBox)));
            var words = new List<Word>();
            words.Add(new Word { Name = "title", Rect = new Rect(left, top, titleW, titleH), Colour = UiColor.TextPrimary });
            float navTop = top + titleH + (int)UiSpace.XXL;
            float navBottom = canvas.y - (int)UiSpace.Section;
            float wordLeft = left + UiIconTokens.Hit(MainMenuScreen.RailIcon) + (int)MainMenuScreen.RailWordGap;
            float wordRoom = Mathf.Max(0f, left + navWidth - wordLeft);
            float buildW = WrappedWidth(rootRect, MainMenuScreen.BuildNotePrefix + SampleBuildLabel, UiType.Caption, navWidth);
            words.Add(new Word { Name = "railWord", Rect = new Rect(wordLeft, navTop, Mathf.Min(wordRoom, Width(rootRect, MainMenuScreen.PlayWord, UiType.H2)), navBottom - navTop), Colour = UiColor.TextPrimary });
            words.Add(new Word { Name = "railSub", Rect = new Rect(wordLeft, navTop, Mathf.Min(wordRoom, Width(rootRect, MainMenuScreen.PlaySubline, UiType.Caption)), navBottom - navTop), Colour = MainMenuScreen.PlateCaption });
            words.Add(new Word { Name = "buildNote", Rect = new Rect(left, navTop, Mathf.Min(navWidth, buildW), navBottom - navTop), Colour = MainMenuScreen.PlateCaption });

            // The real scrims, placed as MainMenuScreen places them.
            UiBackdrop backdrop = UiBackdrop.Create(rootRect);
            Assert.IsNotNull(backdrop, "MenuPlates/plate_far Resources'ta");
            backdrop.CoverColumn(left + navWidth + UiBackdrop.NearParallax);
            List<Scrim> scrims = Scrims(rootRect);

            Rect uv = UiCurtain.CoverUv(canvas.x / canvas.y, plate.Aspect);
            foreach (Word word in words)
            {
                float alpha = MinAlpha(scrims, word.Rect);
                float p = UiBackdrop.NearParallax;
                float u0 = uv.x + (word.Rect.xMin - p) / canvas.x * uv.width;
                float u1 = uv.x + (word.Rect.xMax + p) / canvas.x * uv.width;
                float v0 = uv.y + (word.Rect.yMin - p) / canvas.y * uv.height;   // top-down; the cover crop is symmetric
                float v1 = uv.y + (word.Rect.yMax + p) / canvas.y * uv.height;
                Color brightest;
                float uMax;
                plate.Brightest(u0, v0, u1, v1, out brightest, out uMax);
                Color ground = UiContrast.Composite(Layer(UiColor.SurfaceBase, alpha), brightest);
                float ratio = Ratio(Opaque(word.Colour), ground);
                string line = id + " " + word.Name + " " + ratio.ToString("F2") + " (scrim " + alpha.ToString("0.00") + ", u ≤ " + uMax.ToString("0.000") + ")";
                Debug.Log("[DD-TEST] d1.a.t10 " + line);
                if (ratio + 0.005f >= Text)
                {
                    continue;
                }

                if (!word.Header && uMax > plate.ClampU)
                {
                    pending.Add(line + " - past S-05's clamp band u " + plate.ClampU.ToString("0.000"));
                }
                else
                {
                    failures.Add(line);
                }
            }
        }

        private float Width(RectTransform parent, string text, UiType type)
        {
            TMP_Text label = UiFactory.Label("Measure", parent, text, type, UiColor.TextPrimary);
            _created.Add(label.gameObject);
            return label.GetPreferredValues(text).x;
        }

        /// <summary>The height of <paramref name="text"/> wrapped at <paramref name="maxWidth"/> (the v4 title in the spine).</summary>
        private float WrappedHeight(RectTransform parent, string text, UiType type, float maxWidth)
        {
            TMP_Text label = UiFactory.Label("Measure", parent, text, type, UiColor.TextPrimary);
            _created.Add(label.gameObject);
            label.enableWordWrapping = true;
            return label.GetPreferredValues(text, maxWidth, 0f).y;
        }

        /// <summary>The widest line of <paramref name="text"/> wrapped at <paramref name="maxWidth"/> (TMP wraps on spaces, greedily).</summary>
        private float WrappedWidth(RectTransform parent, string text, UiType type, float maxWidth)
        {
            TMP_Text label = UiFactory.Label("Measure", parent, text, type, UiColor.TextPrimary);
            _created.Add(label.gameObject);
            string[] parts = text.Split(' ');
            string line = string.Empty;
            float widest = 0f;
            foreach (string part in parts)
            {
                string candidate = line.Length == 0 ? part : line + " " + part;
                float candidateWidth = label.GetPreferredValues(candidate).x;
                if (candidateWidth > maxWidth && line.Length > 0)
                {
                    widest = Mathf.Max(widest, label.GetPreferredValues(line).x);
                    line = part;
                }
                else
                {
                    line = candidate;
                }
            }

            return Mathf.Min(maxWidth, Mathf.Max(widest, label.GetPreferredValues(line).x));
        }

        private struct Scrim
        {
            public Rect Rect;   // canvas px, top-left origin
            public float Alpha;
            public int Ramp;    // 0 solid, 1 FadeLeft (opaque at the left), 2 FadeDown (opaque at the top)
        }

        private static List<Scrim> Scrims(RectTransform root)
        {
            var list = new List<Scrim>();
            var corners = new Vector3[4];
            Rect r = root.rect;
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                string name = image.gameObject.name;
                if (!name.StartsWith("TextSafe", StringComparison.Ordinal) && !name.StartsWith("Header", StringComparison.Ordinal))
                {
                    continue;
                }

                image.rectTransform.GetWorldCorners(corners);
                Vector3 min = root.InverseTransformPoint(corners[0]);
                Vector3 max = root.InverseTransformPoint(corners[2]);
                var rect = Rect.MinMaxRect(min.x - r.xMin, r.yMax - max.y, max.x - r.xMin, r.yMax - min.y);
                int ramp = image.sprite == UiSprites.FadeLeft ? 1 : image.sprite == UiSprites.FadeDown ? 2 : 0;
                list.Add(new Scrim { Rect = rect, Alpha = image.color.a, Ramp = ramp });
            }

            Assert.IsNotEmpty(list, "UiBackdrop scrims");
            return list;
        }

        /// <summary>The weakest scrim anywhere under the word (4 px grid): the layers stack as 1 − Π(1 − α).</summary>
        private static float MinAlpha(List<Scrim> scrims, Rect word)
        {
            float min = 1f;
            for (float y = word.yMin; y <= word.yMax + 0.01f; y += Mathf.Min(4f, Mathf.Max(0.5f, word.height)))
            {
                for (float x = word.xMin; x <= word.xMax + 0.01f; x += 4f)
                {
                    float clear = 1f;
                    foreach (Scrim s in scrims)
                    {
                        if (x < s.Rect.xMin || x > s.Rect.xMax || y < s.Rect.yMin || y > s.Rect.yMax)
                        {
                            continue;
                        }

                        float a = s.Alpha;
                        if (s.Ramp == 1) a *= 1f - Smooth((x - s.Rect.xMin) / Mathf.Max(1f, s.Rect.width));
                        else if (s.Ramp == 2) a *= 1f - Smooth((y - s.Rect.yMin) / Mathf.Max(1f, s.Rect.height));
                        clear *= 1f - a;
                    }

                    min = Mathf.Min(min, 1f - clear);
                }
            }

            return min;
        }

        /// <summary>UiSprites' fade ramp: 1 − smoothstep.</summary>
        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ================================================================== the plate

        /// <summary>
        /// The menu plate as the screen shows it at the lantern's breath peak: far, mid, the lantern mask in LanternGlow
        /// at <see cref="UiBackdrop.LanternPeak"/>, near - composited in linear light per texel from the source files,
        /// kept at half resolution as the brightest texel of each 2×2 block (a word never sits on less than 2 px).
        /// </summary>
        private sealed class PlateStack
        {
            private static PlateStack s_instance;

            public static PlateStack Instance
            {
                get { return s_instance ?? (s_instance = new PlateStack()); }
            }

            public static void Release()
            {
                s_instance = null;
            }

            private readonly int _w;
            private readonly int _h;
            private readonly Color[] _colour;   // sRGB, top-down rows
            private readonly float[] _lum;
            private readonly float[] _glow;     // lantern mask coverage per cell (top-down)
            private readonly Color[] _under;    // the stack without the lantern (for the glow's own colour)

            public float Aspect { get; private set; }

            /// <summary>menu_plates.json text_safe.x1: the band S-05 clamped for T10.</summary>
            public float ClampU { get; private set; }

            private PlateStack()
            {
                Texture2D far = LoadFile(PlateFolder + "plate_far.png");
                Texture2D mid = LoadFile(PlateFolder + "plate_mid.png");
                Texture2D near = LoadFile(PlateFolder + "plate_near.png");
                Texture2D mask = LoadFile(PlateFolder + "plate_lantern_mask.png");
                ClampU = ReadClamp(PlateFolder + "menu_plates.json");

                int fw = far.width, fh = far.height;
                Aspect = (float)fw / fh;
                _w = fw / 2;
                _h = fh / 2;
                Color32[] a = far.GetPixels32(), b = mid.GetPixels32(), c = near.GetPixels32(), m = mask.GetPixels32();
                int mw = mask.width, mh = mask.height;

                var lut = new float[256];
                for (int i = 0; i < 256; i++) lut[i] = Linear(i / 255f);
                Color glow = UiTokens.Get(UiColor.LanternGlow);
                float gr = Linear(glow.r), gg = Linear(glow.g), gb = Linear(glow.b);

                _colour = new Color[_w * _h];
                _lum = new float[_w * _h];
                _glow = new float[_w * _h];
                _under = new Color[_w * _h];
                for (int cy = 0; cy < _h; cy++)
                {
                    for (int cx = 0; cx < _w; cx++)
                    {
                        int topRow = cy;
                        int maskIndex = (mh - 1 - Mathf.Min(mh - 1, topRow * mh / _h)) * mw + Mathf.Min(mw - 1, cx * mw / _w);
                        float coverage = m[maskIndex].r / 255f;
                        float lantern = coverage * UiBackdrop.LanternPeak;
                        float best = -1f, br = 0f, bg = 0f, bb = 0f, ur = 0f, ug = 0f, ub = 0f;
                        for (int sy = 0; sy < 2; sy++)
                        {
                            int row = fh - 1 - (cy * 2 + sy);   // GetPixels32 rows run bottom-up
                            for (int sx = 0; sx < 2; sx++)
                            {
                                int i = row * fw + cx * 2 + sx;
                                float r = lut[a[i].r], g = lut[a[i].g], bl = lut[a[i].b];
                                float t = b[i].a / 255f;
                                r = lut[b[i].r] * t + r * (1f - t); g = lut[b[i].g] * t + g * (1f - t); bl = lut[b[i].b] * t + bl * (1f - t);
                                float r0 = r, g0 = g, b0 = bl;
                                r = gr * lantern + r * (1f - lantern); g = gg * lantern + g * (1f - lantern); bl = gb * lantern + bl * (1f - lantern);
                                t = c[i].a / 255f;
                                r = lut[c[i].r] * t + r * (1f - t); g = lut[c[i].g] * t + g * (1f - t); bl = lut[c[i].b] * t + bl * (1f - t);
                                r0 = lut[c[i].r] * t + r0 * (1f - t); g0 = lut[c[i].g] * t + g0 * (1f - t); b0 = lut[c[i].b] * t + b0 * (1f - t);
                                float l = Luminance(r, g, bl);
                                if (l > best)
                                {
                                    best = l; br = r; bg = g; bb = bl; ur = r0; ug = g0; ub = b0;
                                }
                            }
                        }

                        int cell = cy * _w + cx;
                        _lum[cell] = best;
                        _colour[cell] = new Color(Encode(br), Encode(bg), Encode(bb), 1f);
                        _under[cell] = new Color(Encode(ur), Encode(ug), Encode(ub), 1f);
                        _glow[cell] = coverage;
                    }
                }

                foreach (Texture2D t in new[] { far, mid, near, mask })
                {
                    UnityEngine.Object.DestroyImmediate(t);
                }
            }

            private static float ReadClamp(string path)
            {
                Assert.IsTrue(File.Exists(path), path + " yok");
                string json = File.ReadAllText(path);
                int at = json.IndexOf("\"text_safe\"", StringComparison.Ordinal);
                Assert.GreaterOrEqual(at, 0, "menu_plates.json text_safe");
                int key = json.IndexOf("\"x1\"", at, StringComparison.Ordinal);
                int colon = json.IndexOf(':', key);
                int end = json.IndexOfAny(new[] { ',', '}' }, colon);
                return float.Parse(json.Substring(colon + 1, end - colon - 1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }

            /// <summary>The brightest cell in [u0, u1] × [v0, v1] (0..1, v from the top) and the largest u actually sampled.</summary>
            public void Brightest(float u0, float v0, float u1, float v1, out Color colour, out float uMax)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt(u0 * _w), 0, _w - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(u1 * _w) - 1, 0, _w - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(v0 * _h), 0, _h - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(v1 * _h) - 1, 0, _h - 1);
                int best = y0 * _w + x0;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * _w + x;
                        if (_lum[i] > _lum[best]) best = i;
                    }
                }

                colour = _colour[best];
                uMax = (x1 + 1f) / _w;
            }

            /// <summary>The smallest ΔE76 to <paramref name="target"/> of the glow at <paramref name="alpha"/> over the plate where the mask is ≥ 0.5.</summary>
            public float MinLanternDistance(float alpha, Vector3 target)
            {
                Color glow = UiTokens.Get(UiColor.LanternGlow);
                float min = float.MaxValue;
                int cells = 0;
                for (int i = 0; i < _glow.Length; i++)
                {
                    if (_glow[i] < 0.5f)
                    {
                        continue;
                    }

                    glow.a = alpha * _glow[i];
                    Color onScreen = UiContrast.Composite(glow, _under[i]);
                    min = Mathf.Min(min, Vector3.Distance(Lab(onScreen), target));
                    cells++;
                }

                Assert.Greater(cells, 0, "fener maskesinin çekirdeği");
                return min;
            }
        }

        // ================================================================== D2 · HUD lines (lane 2's files: measured, gaps logged)

        /// <summary>
        /// R3 (the user 06.10: top-down only - its tests run alone): the top-down rear arc and the damage wedge in linear light;
        /// the first / third-person rows (HudRearEdge FP / TP, the reticle's Sumi twin) stand in the FPTP twin below.
        /// </summary>
        [Test]
        public void D2_ArkaKilCizgi_HasarKamasi_DogrusalIsikta()
        {
            var failures = new List<string>();
            Color white = Opaque(UiColor.TextPrimary);   // the cream-white world, as UiContrast's HUD cases
            Color black = Opaque(UiColor.SurfaceInset);  // the darkest world

            // The rear hairline: StateDanger at the top-down 0.6 against the world it lies on.
            D2RearArcs(failures, black, white, new KeyValuePair<string, float>("HudPlayerArcs rear (TD)", HudPlayerArcs.RearAlpha));

            // The damage wedge: DamageEdge at its peak × FlashScale.
            float peak = UiTokens.DamageEdgePeakAlpha;
            foreach (KeyValuePair<string, Color> world in new[] { new KeyValuePair<string, Color>("black", black), new KeyValuePair<string, Color>("white", white) })
            {
                float r = Log("d2.wedge peak /" + world.Key, Ratio(UiContrast.Composite(Layer(UiColor.DamageEdge, peak), world.Value), world.Value), Boundary);
                if (r < Boundary) failures.Add("damage wedge at peak over " + world.Key + " " + r.ToString("F2"));
                float reduced = Ratio(UiContrast.Composite(Layer(UiColor.DamageEdge, peak * UiSettings.ReducedFlashScale), world.Value), world.Value);
                Debug.Log("[DD-TEST] d2.wedge reduceFlashes /" + world.Key + " " + reduced.ToString("F2") + " (by design: Reduce Flashes trades the edge's contrast for flash safety)");
            }

            Assert.IsEmpty(failures, "D2 eşik altı:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// D2 on the first / third-person HUD lines: the rear-edge arc at FP 0.6 / TP 0.45, and the reticle's Sumi twin (only
        /// those modes draw it). Out of scope since 06.10, kept under FPTP.
        /// </summary>
        [Test]
        [Category("FPTP")]
        public void D2_ArkaKilCizgi_NisangahIkizi_FpTp_DogrusalIsikta()
        {
            var failures = new List<string>();
            Color white = Opaque(UiColor.TextPrimary);
            Color black = Opaque(UiColor.SurfaceInset);
            D2RearArcs(failures, black, white,
                new KeyValuePair<string, float>("HudRearEdge FP", HudRearEdge.FirstPersonAlpha),
                new KeyValuePair<string, float>("HudRearEdge TP", HudRearEdge.ThirdPersonAlpha));

            // The reticle's Sumi twin (HudReticle.Backing: HudInk at α 1, BackingEdgePx wider): the cream / gold core on
            // it, and the twin against a cream-white world.
            Color twin = Opaque(UiColor.HudInk);
            if (Log("d2.reticle cream/twin", Ratio(Opaque(UiColor.TextPrimary), twin), Boundary) < Boundary) failures.Add("reticle cream on its twin");
            if (Log("d2.reticle gold/twin", Ratio(Opaque(UiColor.StateGold), twin), Boundary) < Boundary) failures.Add("reticle gold on its twin");
            if (Log("d2.reticle twin/white", Ratio(twin, white), Boundary) < Boundary) failures.Add("reticle twin on a white world");
            Color reloadTwin = UiContrast.Composite(Layer(UiColor.HudInk, HudReticle.ReloadTickAlpha), white);
            float reload = Ratio(UiContrast.Composite(Layer(UiColor.TextPrimary, HudReticle.ReloadTickAlpha), reloadTwin), reloadTwin);
            Debug.Log("[DD-TEST] d2.reticle reload ticks (α " + HudReticle.ReloadTickAlpha + ") over white " + reload.ToString("F2") + " (by design: the Focus400 reload ring carries the state)");

            Assert.IsEmpty(failures, "D2 eşik altı:\n" + string.Join("\n", failures));
        }

        /// <summary>The rear hairline: StateDanger at each arc's α against the black and the cream-white world (a gap on cream is logged).</summary>
        private static void D2RearArcs(List<string> failures, Color black, Color white, params KeyValuePair<string, float>[] arcs)
        {
            foreach (KeyValuePair<string, float> arc in arcs)
            {
                float onBlack = Log("d2.rear " + arc.Key + " /black", Ratio(UiContrast.Composite(Layer(UiColor.StateDanger, arc.Value), black), black), Boundary);
                float onWhite = Log("d2.rear " + arc.Key + " /white", Ratio(UiContrast.Composite(Layer(UiColor.StateDanger, arc.Value), white), white), Boundary);
                if (onBlack < Boundary) failures.Add(arc.Key + " over black " + onBlack.ToString("F2"));
                if (onWhite < Boundary) Debug.LogWarning("[DD-TEST] d2.gap " + arc.Key + " over a cream-white world " + onWhite.ToString("F2") + ":1 - coral cannot reach 3:1 on cream by α (α 1 = 2.5:1); a Sumi twin as the reticle's would (lane 2)");
            }
        }
    }
}
