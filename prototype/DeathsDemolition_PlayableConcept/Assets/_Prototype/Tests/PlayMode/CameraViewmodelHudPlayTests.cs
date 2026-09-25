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
    /// WP-070V → WP-070I (C-00 25.09): in the real Run scene, the FP viewmodel's resting silhouette must stay out of
    /// the HUD weapon cluster as S-09 lays it out for first person — RunHudScreen.WeaponClusterRect(firstPerson: true,
    /// canvas), 1920×1080 canvas, 16:9 (no fixed numbers here). The FirstPersonView layer is rendered alone with the
    /// viewmodel camera's pose and projection; no covered pixel may fall inside the rectangle. Positive control: a
    /// pixel the weapon really covers is moved onto the rectangle's centre with the viewmodel screen shift — the
    /// overlap must then be seen, so the check is not blind whatever pose S-02 ships.
    /// </summary>
    public class CameraViewmodelHudPlayTests
    {
        private const float RefW = 1920f, RefH = 1080f;

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

            Rect kume = DD.Prototype.UI.RunHudScreen.WeaponClusterRect(true, new Vector2(RefW, RefH));   // canvas px, top-left
            yield return null; yield return null;
            Olcum son = Olc(vm, kume, "default");
            Debug.Log("[DD-TEST] wp070i.viewmodel default cluster=" + kume + " " + son);
            Assert.Greater(son.Kapli, 0, "FirstPersonView katmanında çizilen piksel yok — ölçüm kör.");
            Assert.AreEqual(0, son.Cakisan, "Dinlenme silüeti FP HUD silah kümesiyle çakışıyor: " + son);

            // Positive control: move a pixel the weapon covers onto the cluster's centre (screen shift, fraction of the screen).
            Vector2 hedef = new Vector2(kume.center.x / RefW, 1f - kume.center.y / RefH);
            Vector2 ornek = new Vector2((son.Ornek.x + 0.5f) / W, (son.Ornek.y + 0.5f) / H);
            CameraLayers.ViewmodelScreenShift = hedef - ornek;
            yield return null; yield return null;
            Olcum kontrol = Olc(vm, kume, "control");
            Debug.Log("[DD-TEST] wp070i.viewmodel control shift=" + CameraLayers.ViewmodelScreenShift + " " + kontrol);
            Assert.Greater(kontrol.Cakisan, 0, "Pozitif kontrol: kümeye kaydırılan silüet çakışma olarak görülmeli — ölçüm kör.");
            CameraLayers.ViewmodelScreenShift = CameraLayers.DefaultViewmodelScreenShift;
        }

        private const int W = 960, H = 540, Tolerance = 8;

        private struct Olcum
        {
            public int Kapli, Cakisan;
            public RectInt Kutu;
            public Vector2Int Ornek;   // a covered pixel near the silhouette's centre (render-texture px, row 0 = bottom)
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
        private static Olcum Olc(Camera vm, Rect kume, string ad)
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
            // cluster rect (canvas px, origin top-left) → render-texture px (row 0 = bottom)
            int x0 = Mathf.FloorToInt(kume.xMin * W / RefW), x1 = Mathf.CeilToInt(kume.xMax * W / RefW);
            int y0 = Mathf.FloorToInt(H - kume.yMax * H / RefH), y1 = Mathf.CeilToInt(H - kume.yMin * H / RefH);
            var o = new Olcum { Kutu = new RectInt(W, H, -W, -H) };
            int xMin = W, yMin = H, xMax = -1, yMax = -1;
            var png = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var cik = new Color32[siyah.Length];
            for (int i = 0; i < siyah.Length; i++)
            {
                Color32 a = siyah[i], b = beyaz[i];
                bool kapli = a.r > Tolerance || a.g > Tolerance || a.b > Tolerance || b.r < 255 - Tolerance || b.g < 255 - Tolerance || b.b < 255 - Tolerance;
                int x = i % W, y = i / W;   // ReadPixels row 0 = bottom
                bool kumede = x >= x0 && x < x1 && y >= y0 && y < y1;
                cik[i] = kapli ? (kumede ? new Color32(255, 0, 0, 255) : a) : (kumede ? new Color32(60, 0, 60, 255) : new Color32(0, 0, 0, 255));
                if (!kapli) continue;
                o.Kapli++;
                if (kumede) o.Cakisan++;
                xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x); yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
            }
            if (xMax >= 0) o.Kutu = new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
            // the covered pixel nearest the bounding box centre (a guaranteed-covered anchor for the positive control)
            float cx = (xMin + xMax) * 0.5f, cy = (yMin + yMax) * 0.5f, best = float.MaxValue;
            for (int i = 0; xMax >= 0 && i < siyah.Length; i++)
            {
                Color32 a = siyah[i], b = beyaz[i];
                if (!(a.r > Tolerance || a.g > Tolerance || a.b > Tolerance || b.r < 255 - Tolerance || b.g < 255 - Tolerance || b.b < 255 - Tolerance)) continue;
                float d = (i % W - cx) * (i % W - cx) + (i / W - cy) * (i / W - cy);
                if (d < best) { best = d; o.Ornek = new Vector2Int(i % W, i / W); }
            }
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
