using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>M4-R1 N2 (WP-070 §3–§7): the camera rows persist, reach the camera, lock the pointer only in play, and the HUD
    /// reads the mode (reticle style, rear-edge bearing).</summary>
    public sealed class UiCameraModesTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "dd-ui-camera-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            UiSettings.SetPathOverride(Path.Combine(_dir, UiSettings.FileName));
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.SetPathOverride(null);
            CameraModeService.ResetForTests();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [Test]
        public void KameraSatirlari_Sinirlanir_Kalici_EskiDosyaVarsayilan()
        {
            // An older file without the camera keys keeps the defaults.
            File.WriteAllText(Path.Combine(_dir, UiSettings.FileName), "{\"Version\":1,\"UiScale\":1.0,\"MasterVolume\":0.5}");
            UiSettings.SetPathOverride(Path.Combine(_dir, UiSettings.FileName));
            Assert.AreEqual(0, UiSettings.CameraMode, "eski dosya: tepeden");
            Assert.AreEqual(90f, UiSettings.FirstPersonFov, 0.001f);
            Assert.AreEqual(60f, UiSettings.ThirdPersonFov, 0.001f);
            Assert.AreEqual(1f, UiSettings.HeadBobScale, 0.001f);
            Assert.IsFalse(UiSettings.CentreDot);

            UiSettings.CameraMode = 7;
            UiSettings.FirstPersonFov = 400f;
            UiSettings.ThirdPersonFov = 10f;
            UiSettings.Shoulder = 1;
            UiSettings.HeadBobScale = 0.25f;
            UiSettings.CentreDot = true;
            Assert.AreEqual(2, UiSettings.CameraMode, "mod 0–2");
            Assert.AreEqual(CameraModeSettings.MaxFirstPersonFov, UiSettings.FirstPersonFov, 0.001f);
            Assert.AreEqual(CameraModeSettings.MinThirdPersonFov, UiSettings.ThirdPersonFov, 0.001f);

            UiSettings.SetPathOverride(Path.Combine(_dir, UiSettings.FileName));   // reload from disk
            Assert.AreEqual(2, UiSettings.CameraMode, "kalıcı");
            Assert.AreEqual(1, UiSettings.Shoulder);
            Assert.AreEqual(0.25f, UiSettings.HeadBobScale, 0.001f);
            Assert.IsTrue(UiSettings.CentreDot);
        }

        [Test]
        public void Kopru_KendiAlanlariniYazar_BakisAlanlarinaDokunmaz()
        {
            UiSettings.CameraMode = 1;
            UiSettings.FirstPersonFov = 85f;
            UiSettings.Shoulder = 1;
            UiSettings.ReducedMotion = true;
            var settings = new CameraModeSettings { LookSensitivity = 0.21f, InvertY = true, StickLookDegreesPerSecond = 300f };
            UiCameraSettingsBridge.CopyInto(settings);
            Assert.AreEqual(CameraMode.ThirdPerson, settings.Mode);
            Assert.AreEqual(85f, settings.FirstPersonFov, 0.001f);
            Assert.AreEqual(CameraShoulder.Left, settings.Shoulder);
            Assert.IsTrue(settings.ReducedMotion, "WP-070 D-10: tek anahtar kamerayı da durultur");
            Assert.AreEqual(0.21f, settings.LookSensitivity, 0.0001f, "bakış alanları S-01 köprüsünün");
            Assert.IsTrue(settings.InvertY);
            Assert.AreEqual(300f, settings.StickLookDegreesPerSecond, 0.001f);
            UiSettings.ReducedMotion = false;
        }

        [Test]
        public void Imlec_YalnizOyundaKilitlenir_MenuVePauseOnce()
        {
            Assert.IsTrue(UiCursor.WantLocked(CameraCursorIntent.Locked, false, true), "TP/FP oyunda kilitli");
            Assert.IsFalse(UiCursor.WantLocked(CameraCursorIntent.Locked, true, true), "menü / Pause / dükkân imleci ister → serbest");
            Assert.IsFalse(UiCursor.WantLocked(CameraCursorIntent.Locked, false, false), "odak yok → serbest");
            Assert.IsFalse(UiCursor.WantLocked(CameraCursorIntent.Free, false, true), "tepeden / donmuş / bakış bloğu → serbest");
        }

        [Test]
        public void KenarYayi_Yonu_ArkaAlttaSolSolda()
        {
            Assert.AreEqual(180f, Mathf.Abs(HudRearEdge.BearingFrom(Vector3.forward, Vector3.back)), 0.01f);
            Assert.AreEqual(-90f, HudRearEdge.BearingFrom(Vector3.forward, Vector3.left), 0.01f, "sol = eksi");
            Assert.IsNaN(HudRearEdge.BearingFrom(Vector3.up, Vector3.back), "dik kamera: yön yok");

            var canvas = new Vector2(1920f, 1080f);
            Vector2 behind = HudRearEdge.EdgePoint(180f, canvas);
            Assert.AreEqual(0f, behind.x, 0.01f);
            Assert.AreEqual(-(540f - HudRearEdge.EdgeInset), behind.y, 0.01f, "tam arka = alt kenar");
            Vector2 behindLeft = HudRearEdge.EdgePoint(-135f, canvas);
            Assert.Less(behindLeft.x, 0f);
            Assert.Less(behindLeft.y, 0f);
        }

        [Test]
        public void Nisangah_ModStili_TP14NoktaIle_FP80_TepedenEski()
        {
            var host = new GameObject("ReticleHost", typeof(RectTransform));
            try
            {
                var reticle = new HudReticle((RectTransform)host.transform);
                reticle.SetMode(CameraMode.TopDown);
                Assert.AreEqual(HudReticle.MinRadius, reticle.MinimumRadius, 0.001f);
                Assert.IsFalse(reticle.DotShown, "tepeden: nokta yok");
                Assert.IsFalse(reticle.BackingShown, "tepeden: Sumi ikiz yok (bugünkü nişangâh)");

                reticle.SetMode(CameraMode.ThirdPerson);
                Assert.AreEqual(14f, reticle.MinimumRadius, 0.001f, "WP-070 §4.5: halka en az 14 px");
                Assert.IsTrue(reticle.DotShown, "TP: merkez nokta");
                Assert.IsTrue(reticle.BackingShown, "TP: göz hizasında sıva üstünde okunur - düz Sumi ikiz");

                reticle.SetMode(CameraMode.FirstPerson);
                Assert.AreEqual(14f * 0.8f, reticle.MinimumRadius, 0.001f, "FP: TP'nin %80'i");
                Assert.IsTrue(reticle.DotShown);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
