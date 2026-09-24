using System.Collections;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-070V (S-09 AD R1-A MUST-2): in the real Run scene, the FP viewmodel's resting silhouette must stay
    /// out of the HUD weapon cluster — bottom-right 420×150 plus a 24 px margin at 1920×1080 (16:9). The
    /// FirstPersonView layer is rendered alone with the viewmodel camera's pose and projection; no covered pixel
    /// may fall inside the rectangle. Positive control: with the screen
    /// transform switched off the silhouette DOES overlap (the finding this fixes), so the check is not blind.
    /// </summary>
    public class CameraViewmodelHudPlayTests
    {
        private const float RefW = 1920f, RefH = 1080f, ClusterW = 420f + 24f, ClusterH = 150f + 24f;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            CameraModeService.ResetForTests();
            yield return LifecycleTestKit.BootTanMainMenuye();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
        }

        [UnityTearDown]
        public IEnumerator Yik()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig != null && rig.Camera != null) rig.Camera.ResetAspect();
            if (rig != null && rig.ViewmodelCamera != null) rig.ViewmodelCamera.ResetAspect();
            CameraLayers.ViewmodelScreenScale = CameraLayers.DefaultViewmodelScreenScale;
            CameraLayers.ViewmodelScreenPivot = CameraLayers.DefaultViewmodelScreenPivot;
            CameraLayers.ViewmodelScreenShift = CameraLayers.DefaultViewmodelScreenShift;
            CameraModeService.ResetForTests();
            Cursor.lockState = CursorLockMode.None;
            yield return LifecycleTestKit.BootTanMainMenuye();
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FP_DinlenmeSiluetiHudSilahKumesineGirmez()
        {
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Assert.IsNotNull(rig);
            for (int i = 0; i < 90; i++) yield return null;   // settle into the idle rest pose
            Camera vm = rig.ViewmodelCamera;
            Assert.IsNotNull(vm, "FP'de görüntü modeli kamerası yok.");
            rig.Camera.aspect = RefW / RefH;
            vm.aspect = RefW / RefH;

            CameraLayers.ViewmodelScreenScale = 1f;
            CameraLayers.ViewmodelScreenShift = Vector2.zero;
            yield return null; yield return null;
            Olcum ham = Olc(vm, "identity");
            Debug.Log("[DD-TEST] wp070v.viewmodel identity " + ham);
            Assert.Greater(ham.Kapli, 0, "FirstPersonView katmanında çizilen piksel yok — ölçüm kör.");
            Assert.Greater(ham.Cakisan, 0, "Pozitif kontrol: dönüşümsüz dinlenme silüeti HUD kümesine girmeli (S-09 bulgusu).");

            CameraLayers.ViewmodelScreenScale = CameraLayers.DefaultViewmodelScreenScale;
            CameraLayers.ViewmodelScreenShift = CameraLayers.DefaultViewmodelScreenShift;
            yield return null; yield return null;
            Olcum son = Olc(vm, "default");
            Debug.Log("[DD-TEST] wp070v.viewmodel default " + son);
            Assert.AreEqual(0, son.Cakisan, "Dinlenme silüeti HUD silah kümesiyle çakışıyor: " + son);
        }

        private const int W = 960, H = 540, Tolerance = 8;

        private struct Olcum
        {
            public int Kapli, Cakisan;
            public RectInt Kutu;
            public string Png;
            public override string ToString()
            {
                float k = RefW / W;
                return "coveredPx=" + Kapli + " inCluster=" + Cakisan + " bbox(px@1080)=x " + (Kutu.xMin * k).ToString("0") + "–" + (Kutu.xMax * k).ToString("0")
                       + " y(top) " + ((H - Kutu.yMax) * k).ToString("0") + "–" + ((H - Kutu.yMin) * k).ToString("0") + " png=" + Png;
            }
        }

        // The viewmodel layer alone, drawn with the viewmodel camera's own pose and projection (incl. the WP-070V
        // screen transform), over black and over white: a pixel that differs from the background in either pass
        // is covered (HudCoverageTests pattern — independent of lighting and alpha).
        private static Olcum Olc(Camera vm, string ad)
        {
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("[WP070V-ViewmodelProbe]");
            go.transform.SetPositionAndRotation(vm.transform.position, vm.transform.rotation);
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = target;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 1 << CameraLayers.FirstPersonView;
            cam.nearClipPlane = vm.nearClipPlane;
            cam.farClipPlane = vm.farClipPlane;
            cam.aspect = RefW / RefH;
            cam.projectionMatrix = vm.projectionMatrix;
            Color32[] siyah = Ciz(cam, target, Color.black);
            Color32[] beyaz = Ciz(cam, target, Color.white);
            int kx = Mathf.CeilToInt(W * (1f - ClusterW / RefW)), ky = Mathf.FloorToInt(H * ClusterH / RefH);   // rows from the bottom
            var o = new Olcum { Kutu = new RectInt(W, H, -W, -H) };
            int xMin = W, yMin = H, xMax = -1, yMax = -1;
            var png = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var cik = new Color32[siyah.Length];
            for (int i = 0; i < siyah.Length; i++)
            {
                Color32 a = siyah[i], b = beyaz[i];
                bool kapli = a.r > Tolerance || a.g > Tolerance || a.b > Tolerance || b.r < 255 - Tolerance || b.g < 255 - Tolerance || b.b < 255 - Tolerance;
                int x = i % W, y = i / W;   // ReadPixels row 0 = bottom
                bool kumede = x >= kx && y < ky;
                cik[i] = kapli ? (kumede ? new Color32(255, 0, 0, 255) : a) : (kumede ? new Color32(60, 0, 60, 255) : new Color32(0, 0, 0, 255));
                if (!kapli) continue;
                o.Kapli++;
                if (kumede) o.Cakisan++;
                xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x); yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
            }
            if (xMax >= 0) o.Kutu = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
            png.SetPixels32(cik); png.Apply();
            o.Png = System.IO.Path.Combine(Application.temporaryCachePath, "wp070v-" + ad + ".png");
            System.IO.File.WriteAllBytes(o.Png, png.EncodeToPNG());
            Object.Destroy(png);
            cam.targetTexture = null;
            Object.Destroy(go);
            target.Release();
            Object.Destroy(target);
            return o;
        }

        private static Color32[] Ciz(Camera cam, RenderTexture target, Color zemin)
        {
            cam.backgroundColor = zemin;
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color32[] px = read.GetPixels32();
            Object.Destroy(read);
            return px;
        }
    }
}
