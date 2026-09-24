using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 4 · AD capture of the DD/UI/Fx modes in a Player: <c>-ddUiFxCapture &lt;folder&gt;</c> builds a
    /// 1920×1080 sheet over the boot scene (banner ink reveal at four points of its timeline + the bleed map, hanko
    /// stamp, kinpaku / ginpaku glint, segment bar with ghost, SDF glyph outline / glow, spark burst + petals) on a
    /// frozen Fx clock, and writes fx-sheet.png, fx-sheet-rm.png (reduced motion) and fx-sheet-flash.png (reduce
    /// flashes), then quits. Opt-in only; the frozen-frame blur is captured through the real Pause / shop screens
    /// (<c>-ddUiCapture pause</c>, the HUD observer's upgrade.png).
    /// </summary>
    public sealed class UiFxCaptureAgent : MonoBehaviour
    {
        private const string Flag = "-ddUiFxCapture";
        /// <summary><c>-ddUiFxCaptureShop &lt;png&gt;</c> (with <c>-ddDirectToRun</c>): open the round-end shop over a live run and capture it with the frozen-frame blur.</summary>
        private const string ShopFlag = "-ddUiFxCaptureShop";
        private const float T = 1000f;   // frozen Fx clock

        private string _folder;
        private string _shopPng;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string folder = Arg(Flag);
            string shop = Arg(ShopFlag);
            if (string.IsNullOrEmpty(folder) && string.IsNullOrEmpty(shop))
            {
                return;
            }

            var go = new GameObject("UiFxCaptureAgent");
            DontDestroyOnLoad(go);
            var agent = go.AddComponent<UiFxCaptureAgent>();
            agent._folder = folder;
            agent._shopPng = shop;
        }

        private IEnumerator Start()
        {
            if (!string.IsNullOrEmpty(_shopPng))
            {
                yield return ShopRoutine();
                yield break;
            }

            Directory.CreateDirectory(_folder);
            yield return new WaitForSecondsRealtime(1.5f);
            UiFxGlobals.EnsureInstalled();
            UiFxGlobals.TimeOverride = T;
            RectTransform sheet = BuildSheet();

            yield return Shoot("fx-sheet.png");
            UiFxGlobals.MotionOverride = false;
            yield return Shoot("fx-sheet-rm.png");
            UiFxGlobals.MotionOverride = null;
            UiFxGlobals.FlashOverride = 0.4f;
            yield return Shoot("fx-sheet-flash.png");
            UiFxGlobals.FlashOverride = null;

            Destroy(sheet.root.gameObject);
            Debug.Log("[UiFxCapture] done → " + _folder);
            Application.Quit(0);
        }

        private IEnumerator ShopRoutine()
        {
            string dir = Path.GetDirectoryName(_shopPng);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((DD.Prototype.Rounds.RunDirector.Instance == null || DD.Prototype.Rounds.RunDirector.Instance.Phase != DD.Prototype.Rounds.RunPhase.Fighting)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(4f);   // a few zombies on screen behind the blur
            DD.Prototype.Rounds.RunDirector.Instance?.OpenUpgradeScreen();
            float t0 = Time.realtimeSinceStartup;
            while (!UiFrozenFrame.IsShowing && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);   // the shop's unscaled fade-in
            yield return new WaitForEndOfFrame();
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(_shopPng, frame.EncodeToPNG());
            Debug.Log("[UiFxCapture] shop " + _shopPng + " blur=" + UiFrozenFrame.IsShowing + " captures=" + UiFrozenFrame.CaptureCount);
            Destroy(frame);

            // The frozen frame on its own (¼ resolution, what the layer shows before the scrim and the modal).
            RenderTexture rt = UiFrozenFrame.Target;
            if (rt != null)
            {
                RenderTexture was = RenderTexture.active;
                RenderTexture.active = rt;
                var only = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                only.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                only.Apply();
                RenderTexture.active = was;
                string blurPng = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, "frozen-frame.png");
                File.WriteAllBytes(blurPng, only.EncodeToPNG());
                Debug.Log("[UiFxCapture] frozen frame " + blurPng + " " + rt.width + "x" + rt.height);
                Destroy(only);
            }
            Application.Quit(0);
        }

        private IEnumerator Shoot(string file)
        {
            UiFxGlobals.Write();
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_folder, file), frame.EncodeToPNG());
            Debug.Log("[UiFxCapture] " + file + " " + frame.width + "x" + frame.height);
            Destroy(frame);
        }

        // ------------------------------------------------------------------ the sheet

        private RectTransform BuildSheet()
        {
            Canvas canvas = UiFactory.CreateCanvas("UiFxCaptureSheet", 1000);
            DontDestroyOnLoad(canvas.gameObject);
            var root = (RectTransform)canvas.transform;
            Image back = UiFactory.NewImage("Back", root, UiColor.SurfaceBase);
            UiFactory.Fill((RectTransform)back.transform);

            Label(root, "DD/UI/Fx — WP-068 slice 4c (AP-012 v004 ink kit)", 64, 28, UiType.H2, UiColor.TextPrimary);

            // Row 1 — banner ink reveal on washi (R underline, its static mask as the sprite): progress 0.2 / 0.45 / 0.7 / 1.0.
            float[] points = { 0.2f, 0.45f, 0.7f, 1f };
            for (int i = 0; i < points.Length; i++)
            {
                float x = 64f + i * 440f;
                Panel(root, x, 90f, 420f, 150f, UiColor.TextPrimary);
                Image stroke = Img(root, x + 20f, 118f, 380f, 95f, UiColor.SurfaceInset, UiFxGlobals.InkMask(UiInkChannel.Underline));
                Fx(stroke, UiFxMode.InkReveal, 0.6f, Vector3.zero, UiInkChannel.Underline, points[i]);
                Label(root, "ROUND 4", x + 140f, 145f, UiType.H2, UiColor.TextPrimary);
                Label(root, "InkReveal t=" + points[i].ToString("0.00"), x, 248f, UiType.Caption, UiColor.TextSecondary);
            }

            // Row 2 — blot (A), hanko stamp (B, mid-scale / landed), SDF glyph outline + glow, divider (G).
            float y2 = 290f;
            Image bleedA = Img(root, 64f, y2, 200f, 200f, UiColor.AccentPrimary, UiFxGlobals.InkMask(UiInkChannel.Blot));
            Fx(bleedA, UiFxMode.InkReveal, 0.6f, Vector3.zero, UiInkChannel.Blot, 0.3f);
            Label(root, "blot t=0.30", 64f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);
            Image bleedB = Img(root, 284f, y2, 200f, 200f, UiColor.AccentPrimary, UiFxGlobals.InkMask(UiInkChannel.Blot));
            Fx(bleedB, UiFxMode.InkReveal, 0.6f, Vector3.zero, UiInkChannel.Blot, 1f);
            Label(root, "blot t=1.00", 284f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);

            Image stampA = Img(root, 564f, y2 + 20f, 160f, 160f, UiColor.AccentPrimary, UiFxGlobals.InkMask(UiInkChannel.Hanko));
            Fx(stampA, UiFxMode.Stamp, 0.13f, Vector3.zero, UiInkChannel.Hanko, 0.35f, 0.13f);
            Label(root, "Stamp 30 ms", 564f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);
            Image stampB = Img(root, 764f, y2 + 20f, 160f, 160f, UiColor.AccentPrimary, UiFxGlobals.InkMask(UiInkChannel.Hanko));
            Fx(stampB, UiFxMode.Stamp, 0.13f, Vector3.zero, UiInkChannel.Hanko, 1f, 0.13f);
            Label(root, "Stamp landed", 764f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);

            Image sdfA = Img(root, 984f, y2 + 10f, 180f, 180f, UiColor.TextPrimary, SdfSprite());
            Fx(sdfA, UiFxMode.SdfGlyph, 1f, new Vector3(0.08f, 0f, 0f), UiInkChannel.Underline, 1f);
            Label(root, "SDF outline", 984f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);
            Image sdfB = Img(root, 1204f, y2 + 10f, 180f, 180f, UiColor.StateGold, SdfSprite());
            Fx(sdfB, UiFxMode.SdfGlyph, 1f, new Vector3(0.06f, 0.18f, 0f), UiInkChannel.Underline, 1f);
            Label(root, "SDF outline + glow", 1204f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);

            Image dividerA = Img(root, 1424f, y2 + 30f, 400f, 50f, UiColor.TextPrimary, UiFxGlobals.InkMask(UiInkChannel.Divider));
            Fx(dividerA, UiFxMode.InkReveal, 0.6f, Vector3.zero, UiInkChannel.Divider, 0.5f);
            Image dividerB = Img(root, 1424f, y2 + 120f, 400f, 50f, UiColor.TextPrimary, UiFxGlobals.InkMask(UiInkChannel.Divider));
            Fx(dividerB, UiFxMode.InkReveal, 0.6f, Vector3.zero, UiInkChannel.Divider, 1f);
            Label(root, "divider t=0.50 / 1.00", 1424f, y2 + 206f, UiType.Caption, UiColor.TextSecondary);

            // Row 3 — leaf glint: kinpaku (gold chip) at three phases, ginpaku (ceremony plate); segment bars.
            float y3 = 560f;
            float[] phases = { 0.3f, 0.5f, 0.7f };
            for (int i = 0; i < phases.Length; i++)
            {
                float x = 64f + i * 260f;
                Image chip = Img(root, x, y3, 240f, 72f, UiColor.StateGold);
                Fx(chip, UiFxMode.Leaf, 0.6f, new Vector3(1f, 0f, 0f), UiInkChannel.Underline, phases[i]);
                Label(root, "1 240", x + 70f, y3 + 18f, UiType.H2, UiColor.SurfaceInset);
                Label(root, "kinpaku glint t=" + phases[i].ToString("0.0"), x, y3 + 80f, UiType.Caption, UiColor.TextSecondary);
            }

            Image silver = Img(root, 864f, y3, 360f, 72f, UiColor.SurfacePanel);
            Fx(silver, UiFxMode.Leaf, 0.6f, new Vector3(0f, 0f, 0f), UiInkChannel.Underline, 0.5f);
            Label(root, "ginpaku (ceremony) t=0.5", 864f, y3 + 80f, UiType.Caption, UiColor.TextSecondary);

            Image track = Img(root, 1264f, y3 + 8f, 560f, 24f, UiColor.BarTrack);
            Image bar = Img(root, 1264f, y3 + 8f, 560f, 24f, UiColor.BarHealth);
            Fx(bar, UiFxMode.SegmentBar, 1f, new Vector3(0.62f, 10f, 0.8f), UiInkChannel.Underline, 1f);
            Label(root, "SegmentBar fill .62 ghost .80 ×10", 1264f, y3 + 40f, UiType.Caption, UiColor.TextSecondary);
            Img(root, 1264f, y3 + 90f, 560f, 12f, UiColor.BarTrack);
            Image solid = Img(root, 1264f, y3 + 90f, 560f, 12f, UiColor.BarReload);
            Fx(solid, UiFxMode.SegmentBar, 1f, new Vector3(0.4f, 0f, 0f), UiInkChannel.Underline, 1f);

            // Row 4 — particles: spark burst mid-life (0.12 s of 0.3), petal drift (loop 4 s); sheen on a menu plate.
            float y4 = 740f;
            Panel(root, 64f, y4, 420f, 260f, UiColor.SurfaceInset);
            RectTransform burst = UiFactory.NewRect("Burst", root);
            UiFactory.Place(burst, 64f, y4, 420f, 260f);
            var sparks = burst.gameObject.AddComponent<UiFxParticles>();
            sparks.color = UiTokens.Get(UiColor.StateGold);
            sparks.Configure(18, 7f, 420f, 700f, 0.3f);
            SetStart(sparks, T - 0.12f);
            Label(root, "Special-ready sparks (0.12 / 0.30 s)", 64f, y4 + 266f, UiType.Caption, UiColor.TextSecondary);

            Panel(root, 524f, y4, 420f, 260f, UiColor.SurfaceInset);
            RectTransform petals = UiFactory.NewRect("Petals", root);
            UiFactory.Place(petals, 524f, y4, 420f, 260f);
            var drift = petals.gameObject.AddComponent<UiFxParticles>();
            drift.color = UiTokens.Get(UiColor.AccentSakura);
            drift.Configure(24, 9f, 60f, 0f, 4f, 4f);
            Label(root, "petal drift (loop 4 s, RM: still)", 524f, y4 + 266f, UiType.Caption, UiColor.TextSecondary);

            Image plate = Img(root, 984f, y4, 840f, 260f, UiColor.SurfacePanel);
            Fx(plate, UiFxMode.Sheen, 1f, new Vector3(6f, 0f, 0f), UiInkChannel.Underline, 1f);
            Label(root, "Sheen (menu, α ≤ 0.05)", 984f, y4 + 266f, UiType.Caption, UiColor.TextSecondary);
            return root;
        }

        // Sets a one-shot's start so that, on the frozen clock T, its progress is <paramref name="progress"/>.
        private static void Fx(Graphic g, UiFxMode mode, float duration, Vector3 prm, UiInkChannel channel, float progress, float stampSeconds = 0f)
        {
            var fx = g.gameObject.AddComponent<UiFx>();
            fx.Configure(mode, duration, prm, channel);
            float elapsed = mode == UiFxMode.Stamp ? progress * 0.09f : progress * duration;
            SetStartField(fx, T - elapsed);
        }

        private static void SetStartField(UiFx fx, float start)
        {
            // Play() stamped the frozen clock; shift the start so the sample sits at the wanted point of the timeline.
            UiFxGlobals.TimeOverride = start;
            fx.Play();
            UiFxGlobals.TimeOverride = T;
        }

        private static void SetStart(UiFxParticles p, float start)
        {
            UiFxGlobals.TimeOverride = start;
            p.Play();
            UiFxGlobals.TimeOverride = T;
        }

        private static Image Img(RectTransform root, float x, float y, float w, float h, UiColor color, Sprite sprite = null)
        {
            Image img = UiFactory.NewImage("Fx", root, color, sprite);
            UiFactory.Place((RectTransform)img.transform, x, y, w, h);
            return img;
        }

        private static void Panel(RectTransform root, float x, float y, float w, float h, UiColor color)
        {
            Img(root, x, y, w, h, color);
        }

        private static void Label(RectTransform root, string text, float x, float y, UiType type, UiColor color)
        {
            TMP_Text t = UiFactory.Label("Label", root, text, type, color);
            UiFactory.Place(t.rectTransform, x, y, 1400f, 40f);
        }

        private static Sprite s_sdf;

        private static Sprite SdfSprite()
        {
            if (s_sdf == null)
            {
                // A diamond (the HUD marker shape) as a distance field: 0.5 at the edge, ±0.5 over 24 px.
                s_sdf = MakeSprite(128, (u, v) =>
                {
                    float d = (Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f)) - 0.3f;
                    return Mathf.Clamp01(0.5f - d * 128f / 48f);
                });
            }

            return s_sdf;
        }

        private static Sprite MakeSprite(int size, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha((x + 0.5f) / size, (y + 0.5f) / size) * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
