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
    /// </summary>
    public sealed class HudCoverageTests
    {
        public const float PersistentCapPercent = 1.5f;
        private const int Width = 1920;
        private const int Height = 1080;
        private const int Tolerance = 8;   // 3 % of 255

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator KaliciHud_1080pEkraninYuzde1BucugunuGecmez()
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
            yield return Measure(canvas, result);
            Debug.Log("[DD-TEST] wp068.8r.coverage=" + result[0].ToString("0.00") + "%");
            Assert.Greater(result[0], 0.2f, "ölçüm HUD'ı görmedi (pozitif kontrol: dört küme en az %0,2)");
            Assert.LessOrEqual(result[0], PersistentCapPercent,
                "kalıcı HUD 1080p ekranın %" + PersistentCapPercent + "'ini geçmemeli (kullanıcı kararı 24.09; paneller %10,48 idi)");

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
