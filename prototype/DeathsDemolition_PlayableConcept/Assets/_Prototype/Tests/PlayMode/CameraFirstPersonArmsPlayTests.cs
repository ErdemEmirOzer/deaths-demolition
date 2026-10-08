using System.Collections;
using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-070J (C-00 25.09, after S-06 B4b): first person shows the FP viewmodel arms and never the head. In the real Run
    /// scene: every renderer of the world player (body + head, all LODs) is ShadowsOnly — the body is drawn by S-06's FP arms
    /// copy, so a visible world body would double the arms; the FP arms copy is shown, draws at least one renderer and none of
    /// them is a head. Evidence frames (ahead, 60° up, 60° down) go to temporaryCachePath for the art review.
    /// </summary>
    [Category("FPTP")]
    public class CameraFirstPersonArmsPlayTests
    {
        private bool _armsBefore;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            CameraModeService.ResetForTests();
            _armsBefore = DD.Prototype.Characters.PlayerSkinnedVisual.FirstPersonV3Arms;
            DD.Prototype.Characters.PlayerSkinnedVisual.FirstPersonV3Arms = true;   // the FP arms feature under test, whatever the shipped default
            yield return LifecycleTestKit.BootTanMainMenuye();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
        }

        [UnityTearDown]
        public IEnumerator Yik()
        {
            CameraModeService.ResetForTests();
            DD.Prototype.Characters.PlayerSkinnedVisual.FirstPersonV3Arms = _armsBefore;
            Cursor.lockState = CursorLockMode.None;
            yield return LifecycleTestKit.BootTanMainMenuye();
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FP_BasGorunmez_KollarGorunur_DunyaGovdesiYalnizGolge()
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Assert.IsNotNull(rig);
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            for (int i = 0; i < 60; i++) yield return null;

            int vm = CameraLayers.FirstPersonView;
            PlayerSkinnedVisual world = null;
            foreach (PlayerSkinnedVisual v in Object.FindObjectsOfType<PlayerSkinnedVisual>())
                if (v.gameObject.layer != vm) world = v;
            Assert.IsNotNull(world, "dünya oyuncu görseli yok");
            var gorunen = new List<string>();
            foreach (SkinnedMeshRenderer r in world.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (r.enabled && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) gorunen.Add(r.name);
            Assert.IsEmpty(gorunen, "FP'de dünya gövdesi/başı görünür (FP kolları kopyası gövdeyi çiziyor; çift kol ya da baş): " + string.Join(", ", gorunen));

            FirstPersonArms arms = FirstPersonArms.Shown;
            Assert.IsNotNull(arms, "FP kolları gösterilmiyor (silahın FP seti yok mu?)");
            Assert.IsTrue(arms.Visible, "FP kolları kopyası kapalı");
            var kol = new List<string>();
            foreach (Renderer r in arms.Animator.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                kol.Add(r.name);
                Assert.AreEqual(-1, r.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase), "FP kolları bir baş renderer'ı çiziyor: " + r.name);
                Assert.AreEqual(vm, r.gameObject.layer, "FP kolları renderer'ı görüntü modeli katmanında değil: " + r.name);
            }
            Assert.IsNotEmpty(kol, "FP kolları kopyası hiçbir renderer çizmiyor");
            Debug.Log("[DD-TEST] wp070j.fp arms set=" + arms.WeaponSet + " renderers=" + string.Join(",", kol) + " anchorErr=" + arms.LastAnchorError.ToString("0.000"));

            Kanit(rig, "ahead");
            rig.SetViewAngles(rig.ViewYaw, -60f); for (int i = 0; i < 10; i++) yield return null; Kanit(rig, "up60");
            rig.SetViewAngles(rig.ViewYaw, 60f); for (int i = 0; i < 10; i++) yield return null; Kanit(rig, "down60");
        }

        // Evidence only: the world camera with its URP stack (FP arms overlay included), 960×540.
        private static void Kanit(PrototypeCameraRig rig, string ad)
        {
            Camera cam = rig.Camera;
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            RenderTexture eski = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = eski;
            RenderTexture onceki = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = onceki;
            string yol = System.IO.Path.Combine(Application.temporaryCachePath, "wp070j-" + ad + ".png");
            System.IO.File.WriteAllBytes(yol, tex.EncodeToPNG());
            Debug.Log("[DD-TEST] wp070j.frame " + ad + " " + yol);
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }
    }
}
