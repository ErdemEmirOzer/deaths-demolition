using System.Collections;
using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-081 ikon v2 B5b · the O1 icon material (DD/UI/Fx Icon, 8) drawn by the GPU and read back (the UiGlyphAtlasTests
    /// pattern: cam.Render into a target texture). Every O1 glyph at S / M / L (24 / 32 / 40) in one grid. WP-098A (C-00 08.10):
    /// the material is AP-034 v002.1 row d, the AD default - its ink darkens the lit glyph within a ceiling, so T1 / T3's
    /// floors are the AD's (AP034-v002-AD §2, §3 MUST-1, §5-2), and its lob / glow are AP-034's (T2's reach):
    ///   T1  the colour floor - on every pixel the glyph covers, no channel of the lit glyph is under the same pose drawn
    ///       flat (light off) × (1 − (0.06 + 0.06 s)), s = sat((px − 24) / 16) - 0.94 at 24, 0.91 at 32, 0.88 at 40 (B5b:
    ///       × 1, light only) - for both tints, on transparent / #161D21 / white, in a linear half-float target: 0
    ///       violations, and the ink is live (some pixel darker than flat);
    ///   T2  the outer layers (glow at its peak, the ink lob) change no pixel the glyph covers; the lob is a real shadow
    ///       (on white ≥ 0.1, AP-034's Twin 0.24 at 40 px; B5b's cold sliver was 2e-4) and the glow a real halo (≥ 0.03 on
    ///       #161D21, the Twin 0.067; B5b 1e-3);
    ///   T3  contrast in the 8-bit sRGB target: every core pixel (α ≥ 0.8) of the lit glyph against #161D21 holds the AD's
    ///       guard - TextPrimary ≥ 7:1 (MUST-1 §1; AP-034 measured ≥ 10.73:1), TextSecondary ≥ 4.5:1 (B5b: ≥ the flat ratio);
    ///   T4  reduced motion: whatever the light channels hold, the frame is the rest frame (sheen off, no glow, no spread,
    ///       drawn) - and the relief itself, not being motion, stays;
    ///   plus the channel layout at rest (uv1 / uv2 / uv3).
    /// The tier switch is forced to the full material (UiFxGlobals.IconTierOverride) - a Performance machine would draw flat.
    /// </summary>
    [TestFixture]
    [Category("WP081")]
    public sealed class UiIconMaterialTests
    {
        private const int UiLayer = 5;
        private const int Cell = 48;
        private const int Columns = 16;
        private static readonly int[] Sizes = { (int)UiIconSize.S, (int)UiIconSize.M, (int)UiIconSize.L };
        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);
        private static readonly Color Ground = new Color32(0x16, 0x1D, 0x21, 0xFF);   // AP-028's measuring ground (sRGB)

        // Light channels (sheen, glow, spread) and parameters (draw, lit, opacity): the rest pose and AP-028's static
        // material states, plus the sheen sweep (D1 diagonal) and the motion keyframes' extremes the channels must carry.
        private static readonly Vector3 Rest = new Vector3(-1f, 0f, 0f);
        private static readonly Vector3 Drawn = new Vector3(1f, 1f, 1f);

        private readonly List<Object> _temp = new List<Object>();
        private readonly List<UiFx> _icons = new List<UiFx>();
        private readonly List<Image> _images = new List<Image>();

        [SetUp]
        public void SetUp()
        {
            UiFxGlobals.EnsureInstalled();
            UiFxGlobals.MotionOverride = true;     // motion on: T4 turns reduced motion on itself
            UiFxGlobals.FlashOverride = 1f;        // the machine's Reduce Flashes must not shrink the sheen under test
            UiFxGlobals.IconTierOverride = 1f;     // the full material (Cinematic / High) whatever the machine's tier
            UiFxGlobals.WriteIconTier();
        }

        [TearDown]
        public void TearDown()
        {
            UiFxGlobals.MotionOverride = null;
            UiFxGlobals.FlashOverride = null;
            UiFxGlobals.IconTierOverride = null;
            UiFxGlobals.WriteIconTier();
            UiFxGlobals.Write();
            foreach (Object o in _temp)
            {
                if (o != null) Object.Destroy(o);
            }

            _temp.Clear();
            _icons.Clear();
            _images.Clear();
        }

        // ------------------------------------------------------------------ T1 · colour floor

        [UnityTest]
        public IEnumerator T1_IkonMalzemesi_RenkTabani_IhlalSifir()
        {
            RenderTexture rt = NewTarget(true);
            Camera cam = Build(rt);
            var poses = new[]
            {
                new IconPose("rest", Rest, Drawn),
                new IconPose("sheen 0", new Vector3(0f, 0f, 0f), Drawn),
                new IconPose("sheen .25", new Vector3(0.25f, 0f, 0f), Drawn),
                new IconPose("sheen .5 + glow .08", new Vector3(0.5f, 0.08f, 0f), Drawn),
                new IconPose("sheen .75", new Vector3(0.75f, 0f, 0f), Drawn),
                new IconPose("sheen 1 + glow .04", new Vector3(1f, 0.04f, 0f), Drawn),
                new IconPose("pressed@50 spread .4", new Vector3(-1f, 0f, 0.4f), Drawn),
                new IconPose("selected@0 draw .75", Rest, new Vector3(0.75f, 1f, 1f)),
                new IconPose("disabled", Rest, new Vector3(1f, 0f, 1f)),
            };
            Color[] tints = { UiTokens.Get(UiColor.TextPrimary), UiTokens.Get(UiColor.TextSecondary) };
            Color[] grounds = { Clear, Ground, Color.white };

            int covered = 0, violations = 0;
            float lift = 0f, inkMin = 1f;
            string first = null;
            foreach (IconPose pose in poses)
            {
                foreach (Color tint in tints)
                {
                    bool[] glyph = null;
                    foreach (Color ground in grounds)
                    {
                        cam.backgroundColor = ground;
                        Apply(pose.Light, new Vector3(pose.Params.x, 0f, pose.Params.z), tint);   // the same pose, flat
                        yield return Render(cam);
                        Color[] flat = Read(rt, true);
                        Apply(pose.Light, pose.Params, tint);
                        yield return Render(cam);
                        Color[] lit = Read(rt, true);

                        glyph = glyph ?? Covered(flat);   // the transparent ground comes first: the glyph's own alpha
                        for (int i = 0; i < flat.Length; i++)
                        {
                            if (!glyph[i])
                            {
                                continue;
                            }

                            covered++;
                            // Row d's floor at this glyph's size (the grid's cells are laid size by size).
                            float floor = 1f - UiIconShade.Ceiling(UiIconShade.SizeRamp(SizeAt(i, rt.width)));
                            for (int c = 0; c < 3; c++)
                            {
                                float d = lit[i][c] - flat[i][c];
                                lift = Mathf.Max(lift, d);
                                if (flat[i][c] > 0.05f)
                                {
                                    inkMin = Mathf.Min(inkMin, lit[i][c] / flat[i][c]);
                                }

                                if (lit[i][c] < flat[i][c] * floor - 1e-3f)
                                {
                                    violations++;
                                    first = first ?? (pose.Name + " tint " + tint + " ground " + ground + " px " + i + " ch" + c + ": " + lit[i] + " < " + flat[i] + " × " + floor);
                                }
                            }
                        }
                    }
                }
            }

            Debug.Log("[DD-TEST] wp081.b5b.T1 covered " + covered + " violations " + violations + " maxLift " + lift.ToString("0.0000")
                + " inkMin " + inkMin.ToString("0.0000"));
            Assert.Greater(covered, 100000, "ızgarada glif pikseli bulunmalı (çizim boş mu?)");
            Assert.Greater(lift, 2e-3f, "malzeme açık: kabartma bir yerde ışık ekler (kademe anahtarı / dal çalışıyor mu?)");
            Assert.Less(inkMin, 0.99f, "d satırı: mürekkep bir yerde koyulaştırır (I1 / I2 bağlı mı?)");
            Assert.AreEqual(0, violations, "renk tabanı (AD §5-2): ışıklı glif düz × (1 − (0,06 + 0,06 s)) altına inmez - " + first);
        }

        // ------------------------------------------------------------------ T2 · outer layers stay outside the ink

        [UnityTest]
        public IEnumerator T2_IkonMalzemesi_DisKatman_GlifPikselindeSifir()
        {
            RenderTexture rt = NewTarget(true);
            Camera cam = Build(rt);
            Color tint = UiTokens.Get(UiColor.TextPrimary);
            Vector3 flat = new Vector3(1f, 0f, 1f);
            Vector3 glowPeak = new Vector3(-1f, UiIconShade.GlowChannelMax, 0f);

            cam.backgroundColor = Clear;
            Apply(Rest, flat, tint);
            yield return Render(cam);
            bool[] glyph = Covered(Read(rt, true));

            float glowReach = 0f, lobReach = 0f;
            int leaks = 0;
            string first = null;
            foreach (Color ground in new[] { Clear, Ground, Color.white })
            {
                cam.backgroundColor = ground;
                Apply(Rest, flat, tint);
                yield return Render(cam);
                Color[] bare = Read(rt, true);
                Apply(Rest, Drawn, tint);                // lit: the lob is on
                yield return Render(cam);
                Color[] lob = Read(rt, true);
                Apply(glowPeak, Drawn, tint);            // lit + the glow at its peak
                yield return Render(cam);
                Color[] glow = Read(rt, true);

                for (int i = 0; i < glow.Length; i++)
                {
                    if (glyph[i])
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            if (Mathf.Abs(glow[i][c] - lob[i][c]) > 1e-3f)
                            {
                                leaks++;
                                first = first ?? ("ground " + ground + " px " + i + ": " + glow[i] + " ≠ " + lob[i]);
                            }
                        }

                        continue;
                    }

                    if (ground == Ground)
                    {
                        glowReach = Mathf.Max(glowReach, glow[i].g - lob[i].g);
                    }
                    else if (ground == Color.white)
                    {
                        lobReach = Mathf.Max(lobReach, bare[i].g - lob[i].g);
                    }
                }
            }

            Debug.Log("[DD-TEST] wp081.b5b.T2 leaks " + leaks + " glowReach " + glowReach.ToString("0.00000") + " lobReach " + lobReach.ToString("0.00000"));
            Assert.AreEqual(0, leaks, "glow / lob glif pikseline girmez (IK-M5) - " + first);
            // WP-098A row d: the glow is α 0.07 + 0.05 s of the cream (AP-034's Twin: 0.019 / 0.042 / 0.067 at 24 / 32 / 40 on
            // #161D21); the lob is ink α 0.45 offset (1.6, −2.4) AP, soft 2.4 AP (the Twin on white: 0.095 / 0.17 / 0.24).
            Assert.Greater(glowReach, 0.03f, "glow yolu çalışıyor: glifin dışında zemini aydınlatır (d: görünür hale)");
            Assert.Greater(lobReach, 0.1f, "lob yolu çalışıyor: beyaz zeminde glifin dışında koyulaştırır (d: mürekkep gölge)");
        }

        // ------------------------------------------------------------------ T3 · contrast never below flat

        [UnityTest]
        public IEnumerator T3_IkonMalzemesi_Kontrast_AdBekcisinde()
        {
            RenderTexture rt = NewTarget(false);
            Camera cam = Build(rt);
            int core = 0, worse = 0;
            float minFlat = float.MaxValue, minLit = float.MaxValue;
            string first = null;
            foreach (Color tint in new[] { UiTokens.Get(UiColor.TextPrimary), UiTokens.Get(UiColor.TextSecondary) })
            {
                // The AD's guard (AP034-v002-AD §3 MUST-1 §1, §5-2): the cream core ≥ 7:1 on B3, the secondary tone ≥ 4.5:1 - lit,
                // and flat (the light off: disabled is TextSecondary flat, P-07 6.68:1; Performance draws the token flat).
                float guard = tint == UiTokens.Get(UiColor.TextPrimary) ? 7f : 4.5f;
                foreach (Vector3 light in new[] { Rest, new Vector3(0.5f, 0.08f, 0f) })
                {
                    cam.backgroundColor = Clear;
                    Apply(light, new Vector3(1f, 0f, 1f), tint);
                    yield return Render(cam);
                    Color[] coverage = Read(rt, false);

                    cam.backgroundColor = Ground;
                    yield return Render(cam);
                    Color[] flat = Read(rt, false);
                    Apply(light, Drawn, tint);
                    yield return Render(cam);
                    Color[] lit = Read(rt, false);

                    for (int i = 0; i < lit.Length; i++)
                    {
                        if (CoverageOf(coverage[i], tint) < 0.8f)
                        {
                            continue;   // the core only (α ≥ 0.8), as AP-028 measured
                        }

                        core++;
                        float rf = UiContrast.Ratio(Opaque(flat[i]), Ground), rl = UiContrast.Ratio(Opaque(lit[i]), Ground);
                        minFlat = Mathf.Min(minFlat, rf);
                        minLit = Mathf.Min(minLit, rl);
                        if (rl < guard || rf < guard)
                        {
                            worse++;
                            first = first ?? ("tint " + tint + " light " + light + " px " + i + ": lit " + rl.ToString("0.000") + " flat " + rf.ToString("0.000") + " < " + guard);
                        }
                    }
                }
            }

            Debug.Log("[DD-TEST] wp081.b5b.T3 core " + core + " minFlat " + minFlat.ToString("0.00") + " minLit " + minLit.ToString("0.00"));
            Assert.Greater(core, 20000, "çekirdek pikseller bulunmalı");
            Assert.AreEqual(0, worse, "çekirdek kontrastı AD bekçisinde (#161D21: krem ≥ 7, ikincil ≥ 4,5) - " + first);
        }

        // ------------------------------------------------------------------ T4 · reduced motion

        [UnityTest]
        public IEnumerator T4_IkonMalzemesi_HareketAzalt_Statik()
        {
            RenderTexture rt = NewTarget(true);
            Camera cam = Build(rt);
            Color tint = UiTokens.Get(UiColor.TextPrimary);
            Vector3 movingLight = new Vector3(0.5f, UiIconShade.GlowChannelMax, 0.4f);   // a track mid-flight
            Vector3 movingParams = new Vector3(0.75f, 1f, 1f);

            foreach (Color ground in new[] { Clear, Ground })
            {
                cam.backgroundColor = ground;
                UiFxGlobals.MotionOverride = true;
                Apply(movingLight, movingParams, tint);
                yield return Render(cam);
                Color[] movingLive = Read(rt, true);

                UiFxGlobals.MotionOverride = false;   // reduced motion
                Apply(Rest, Drawn, tint);
                yield return Render(cam);
                Color[] rest = Read(rt, true);
                Apply(movingLight, movingParams, tint);
                yield return Render(cam);
                Color[] moving = Read(rt, true);
                Apply(Rest, new Vector3(1f, 0f, 1f), tint);
                yield return Render(cam);
                Color[] flat = Read(rt, true);

                Assert.Less(MaxDifference(rest, moving), 1e-3f, "hareketi azalt: kanallarda iz kalsa da kare dinlenme karesi (T4), zemin " + ground);
                Assert.Greater(MaxDifference(rest, movingLive), 1e-2f, "hareket açıkken aynı poz farklı çizilir (test boş değil)");
                Assert.Greater(MaxDifference(rest, flat), 2e-3f, "kabartma hareket değildir: hareketi azaltta da kalır");
            }
        }

        // ------------------------------------------------------------------ channel layout

        /// <summary>uv1 = (8, sheen, glow, spread), uv2 = (draw, lit, opacity, −), uv3 = (sprite-box uv, rect w, h) - at rest and when set.</summary>
        [Test]
        public void Kanallar_Uv1Uv2Uv3_DinlenmeVeYazim()
        {
            RenderTexture rt = NewTarget(true);
            Build(rt);
            UiFx fx = _icons[0];
            var rect = (RectTransform)fx.transform;

            Vector4[] uv1, uv2, uv3;
            Channels(fx, out uv1, out uv2, out uv3);
            for (int v = 0; v < 4; v++)
            {
                Assert.AreEqual(new Vector4(8f, -1f, 0f, 0f), uv1[v], "uv1 dinlenme");
                Assert.AreEqual(1f, uv2[v].x, "çizili");
                Assert.AreEqual(1f, uv2[v].y, "ışıklı");
                Assert.AreEqual(1f, uv2[v].z, "opak");
                Assert.AreEqual(rect.rect.width, uv3[v].z, 1e-4f, "uv3.z = rect genişliği");
                Assert.AreEqual(rect.rect.height, uv3[v].w, 1e-4f, "uv3.w = rect yüksekliği");
            }

            Assert.AreEqual(new Vector4(0f, 0f, 0f, 0f), new Vector4(uv3[0].x, uv3[0].y, 0f, 0f), "sol alt köşe (0, 0)");
            Assert.AreEqual(new Vector4(1f, 1f, 0f, 0f), new Vector4(uv3[2].x, uv3[2].y, 0f, 0f), "sağ üst köşe (1, 1)");

            fx.SetIcon(new Vector3(0.5f, 0.08f, 0.4f), new Vector3(0.75f, 0f, 0.5f));
            Channels(fx, out uv1, out uv2, out uv3);
            Assert.AreEqual(new Vector4(8f, 0.5f, 0.08f, 0.4f), uv1[1], "uv1 yazım");
            Assert.AreEqual(new Vector4(0.75f, 0f, 0.5f, 0f), uv2[1], "uv2 yazım (w: kanal yok)");
        }

        // ------------------------------------------------------------------ helpers

        private readonly struct IconPose
        {
            public readonly string Name;
            public readonly Vector3 Light;
            public readonly Vector3 Params;

            public IconPose(string name, Vector3 light, Vector3 parameters)
            {
                Name = name;
                Light = light;
                Params = parameters;
            }
        }

        private RenderTexture NewTarget(bool linearHalf)
        {
            int width = Columns * Cell;
            int height = ((UiArt.O1IconKeys.Length * Sizes.Length + Columns - 1) / Columns) * Cell;
            var rt = linearHalf
                ? new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                : new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            _temp.Add(rt);
            return rt;
        }

        /// <summary>A UI camera into the target and every O1 glyph at S / M / L on a 48 px grid, pixel-aligned, through Icon.</summary>
        private Camera Build(RenderTexture rt)
        {
            var camGo = new GameObject("IconMaterialCamera");
            _temp.Add(camGo);
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Clear;
            cam.cullingMask = 1 << UiLayer;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = rt;
            cam.enabled = false;

            var canvasGo = new GameObject("IconMaterialCanvas", typeof(RectTransform), typeof(Canvas));
            _temp.Add(canvasGo);
            canvasGo.layer = UiLayer;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;

            _icons.Clear();
            _images.Clear();
            int index = 0;
            foreach (int size in Sizes)
            {
                foreach (string key in UiArt.O1IconKeys)
                {
                    Sprite sprite = UiArt.Icon(key);
                    Assert.IsNotNull(sprite, key + " G1'de");
                    Image image = UiFactory.NewImage(key, canvas.transform, UiColor.TextPrimary, sprite);
                    image.gameObject.layer = UiLayer;
                    image.preserveAspect = true;
                    var rect = (RectTransform)image.transform;
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.zero;
                    rect.pivot = Vector2.zero;
                    rect.sizeDelta = new Vector2(size, size);
                    rect.anchoredPosition = new Vector2((index % Columns) * Cell + (Cell - size) / 2, (index / Columns) * Cell + (Cell - size) / 2);
                    UiFx fx = UiArt.ApplyIconMaterial(image, key, true);
                    Assert.IsNotNull(fx, key + ": O1 → Icon");
                    _icons.Add(fx);
                    _images.Add(image);
                    index++;
                }
            }

            return cam;
        }

        /// <summary>The glyph size (px) of the grid cell a target pixel lies in: cells run size by size, key by key, 16 a row.</summary>
        private static int SizeAt(int pixel, int width)
        {
            int index = ((pixel / width) / Cell) * Columns + (pixel % width) / Cell;
            return Sizes[Mathf.Min(index / UiArt.O1IconKeys.Length, Sizes.Length - 1)];
        }

        private void Apply(Vector3 light, Vector3 parameters, Color tint)
        {
            for (int i = 0; i < _icons.Count; i++)
            {
                _icons[i].SetIcon(light, parameters);
                _images[i].color = tint;
            }
        }

        private static IEnumerator Render(Camera cam)
        {
            UiFxGlobals.Write();
            Canvas.ForceUpdateCanvases();
            yield return null;
            UiFxGlobals.Write();
            cam.Render();
        }

        /// <summary>The target's pixels: the half-float target as linear floats, the 8-bit one as its stored sRGB bytes / 255.</summary>
        private static Color[] Read(RenderTexture rt, bool linearHalf)
        {
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = linearHalf
                ? new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false, true)
                : new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            Color[] px = tex.GetPixels();
            Object.Destroy(tex);
            return px;
        }

        /// <summary>
        /// The pixels the glyph covers: the flat draw (no outer layer) on the transparent black ground is the premultiplied
        /// tint × coverage, so any light in it is coverage - read from rgb, not from the target's alpha.
        /// </summary>
        private static bool[] Covered(Color[] transparentFlat)
        {
            var covered = new bool[transparentFlat.Length];
            for (int i = 0; i < covered.Length; i++)
            {
                Color c = transparentFlat[i];
                covered[i] = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 1e-5f;
            }

            return covered;
        }

        /// <summary>Coverage of an 8-bit sRGB pixel of the flat draw on the transparent black ground: decode(stored) / tint (green).</summary>
        private static float CoverageOf(Color stored, Color tint)
        {
            return UiIconShade.SrgbToLinear(stored.g) / Mathf.Max(tint.linear.g, 1e-4f);
        }

        private static float MaxDifference(Color[] a, Color[] b)
        {
            float max = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    max = Mathf.Max(max, Mathf.Abs(a[i][c] - b[i][c]));
                }
            }

            return max;
        }

        private static Color Opaque(Color c)
        {
            c.a = 1f;
            return c;
        }

        /// <summary>Run the effect over the image's quad (a VertexHelper of its rect's corners) and return each vertex's uv1..uv3.</summary>
        private static void Channels(UiFx fx, out Vector4[] uv1, out Vector4[] uv2, out Vector4[] uv3)
        {
            Rect r = ((RectTransform)fx.transform).rect;
            var vh = new VertexHelper();
            try
            {
                Vector2[] corners = { new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin) };
                foreach (Vector2 p in corners)
                {
                    UIVertex v = UIVertex.simpleVert;
                    v.position = p;
                    vh.AddVert(v);
                }

                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
                fx.ModifyMesh(vh);
                uv1 = new Vector4[4];
                uv2 = new Vector4[4];
                uv3 = new Vector4[4];
                UIVertex read = default;
                for (int i = 0; i < 4; i++)
                {
                    vh.PopulateUIVertex(ref read, i);
                    uv1[i] = read.uv1;
                    uv2[i] = read.uv2;
                    uv3[i] = read.uv3;
                }
            }
            finally
            {
                vh.Dispose();
            }
        }
    }
}
