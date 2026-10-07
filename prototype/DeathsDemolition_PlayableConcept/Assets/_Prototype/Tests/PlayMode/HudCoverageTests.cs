using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 8-R · the coverage cap. The user found the panel HUD "too big, covers the screen" (10.48 % of 1080p);
    /// the ink HUD they chose (concept A) measured ≈ 1 %. This test keeps it there: in calm combat, the persistent HUD
    /// covers at most <see cref="PersistentCapPercent"/> of a 1920×1080 frame.
    /// The measure is the same as the capture meter (UiCaptureAgent.Coverage), made headless:
    /// - the HUD canvas renders through its own camera into a 1920×1080 target, with no world in it;
    /// - it renders once over black and once over white;
    /// - a pixel is covered when either render differs from its background by more than 3 % in any channel.
    /// That counts translucent ink over white and pale strokes over black alike.
    /// WP-094 (S-09, 08.10): the round's brush plate grows with its numeral, so the cap holds at three rounds - I (the
    /// shortest), XIV (a mid plate) and XXXVIII (the longest under 40, its cap shrunk to fit the 288 px plate - HudArt.FitCap).
    /// The levers that keep XIV / XXXVIII under the cap are the cap 44 → 40 and the plate's height (cap / 0.46); the plate's
    /// alpha is no lever (its mask is near-binary: a covered pixel stays covered at any α the AD accepts).
    /// </summary>
    public sealed class HudCoverageTests
    {
        public const float PersistentCapPercent = 1.5f;

        /// <summary>WP-094 (S-09, 08.10): the rounds the cap is held at - the shortest, a mid and the longest plate under 40.</summary>
        private static readonly int[] Rounds = { 1, 14, 38 };
        private const int Width = 1920;
        private const int Height = 1080;
        private const int Tolerance = 8;   // 3 % of 255

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator KaliciHud_1080p_Ui1_Metin1_EkraninYuzde1BucugunuGecmez()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return new WaitForSecondsRealtime(3f);   // past the round banner (transient, not the persistent HUD)

            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD yok");
            Transform canvasTransform = hud.transform.Find(hud.GetType().Name + ".Canvas");
            Canvas canvas = canvasTransform != null ? canvasTransform.GetComponent<Canvas>() : null;
            Assert.IsNotNull(canvas, "HUD canvas'ı yok");

            var result = new float[1];
            foreach (int round in Rounds)
            {
                // The numeral through the production path (the capture seam), then past its transition and the seal's ink.
                hud.CaptureRound(round);
                float until = Time.realtimeSinceStartup + 5f;
                while (hud.RoundTransitionLive && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }

                Assert.IsFalse(hud.RoundTransitionLive, "raund geçişi 5 sn'de bitmedi (" + round + ")");
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.AreEqual(round, hud.RoundNumeral.Value, "HUD raundu göstermiyor");

                yield return Measure(canvas, result);
                string name = RomanNumerals.Cached(round);
                Debug.Log("[DD-TEST] wp068.8r.coverage." + name + "=" + result[0].ToString("0.00") + "% cap="
                    + hud.RoundCap.ToString("0.0") + " numeral=" + hud.RoundNumeral.Width.ToString("0.0"));
                Assert.Greater(result[0], 0.2f, "ölçüm HUD'ı görmedi (pozitif kontrol: dört küme en az %0,2)");
                Assert.LessOrEqual(result[0], PersistentCapPercent,
                    name + ": kalıcı HUD 1080p ekranın %" + PersistentCapPercent + "'ini geçmemeli (UI ölçeği 1,0, metin ölçeği 1,0; kullanıcı" +
                    " kararı 24.09; paneller %10,48 idi). Mac 1,2 × 1,25 değeri yalnız bilgi - yakalama ölçeri raporlar.");
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>The HUD drawn alone through its own camera, over black and over white; the covered share in %.</summary>
        private static IEnumerator Measure(Canvas canvas, float[] result)
        {
            int layer = LayerMask.NameToLayer("UI");
            Transform[] all = canvas.GetComponentsInChildren<Transform>(true);
            var oldLayers = new int[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                oldLayers[i] = all[i].gameObject.layer;
                all[i].gameObject.layer = layer;
            }

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("[HudCoverageCamera]");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << layer;
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;

            RenderMode oldMode = canvas.renderMode;
            Camera oldCamera = canvas.worldCamera;
            float oldPlane = canvas.planeDistance;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            // Two frames: the CanvasScaler re-fits the canvas to the camera's 1920×1080 target in its own Update.
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Color32[] overBlack = Render(camera, target, Color.black);
            Color32[] overWhite = Render(camera, target, Color.white);

            canvas.renderMode = oldMode;
            canvas.worldCamera = oldCamera;
            canvas.planeDistance = oldPlane;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                {
                    all[i].gameObject.layer = oldLayers[i];
                }
            }

            camera.targetTexture = null;
            Object.Destroy(cameraObject);
            target.Release();
            Object.Destroy(target);

            int covered = 0;
            for (int i = 0; i < overBlack.Length; i++)
            {
                Color32 a = overBlack[i], b = overWhite[i];
                if (a.r > Tolerance || a.g > Tolerance || a.b > Tolerance ||
                    b.r < 255 - Tolerance || b.g < 255 - Tolerance || b.b < 255 - Tolerance)
                {
                    covered++;
                }
            }

            result[0] = 100f * covered / overBlack.Length;
        }

        private static Color32[] Render(Camera camera, RenderTexture target, Color background)
        {
            camera.backgroundColor = background;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = read.GetPixels32();
            Object.Destroy(read);
            return pixels;
        }
    }
}
