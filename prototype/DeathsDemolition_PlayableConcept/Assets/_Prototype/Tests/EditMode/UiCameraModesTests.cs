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
        public void KaliteKademesi_EskiAltiSeviyeUceEslenir_AyarBakisOlcegi()
        {
            Assert.AreEqual(0, UiSettings.MigrateQuality(1, 1, 3), "Low → Performance");
            Assert.AreEqual(1, UiSettings.MigrateQuality(2, 1, 3), "Medium → High");
            Assert.AreEqual(1, UiSettings.MigrateQuality(3, 1, 3), "High → High");
            Assert.AreEqual(2, UiSettings.MigrateQuality(5, 1, 3), "Ultra → Cinematic");
            Assert.AreEqual(2, UiSettings.MigrateQuality(2, 2, 3), "v2 dosya: aynen");
            Assert.AreEqual(4, UiSettings.MigrateQuality(4, 1, 6), "altı seviye düzeni: aynen");

            // WP-070 §3.1: LOOK SENSITIVITY 1–20 ↔ 0.02–0.30 °/px, round trip on the default 0.08.
            Assert.AreEqual(0.02f, MainMenuScreen.LookSensitivityOf(1f), 0.0001f);
            Assert.AreEqual(0.30f, MainMenuScreen.LookSensitivityOf(20f), 0.0001f);
            float step = MainMenuScreen.LookStepOf(0.08f);
            Assert.AreEqual(0.08f, MainMenuScreen.LookSensitivityOf(step), 0.01f, "varsayılan 0,08 bir adım içinde");
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
        public void ArkaYay_64Yonde_KumelereGirmez_CizgiIcKenarda_EkranIcinde()
        {
            var canvases = new (string Name, float W, float H, float Tall)[] { ("1080", 1920f, 1080f, 1f), ("21:9", 2560f, 1080f, 1f), ("mac", 1600f, 1039f, 1.25f) };
            var failures = new System.Collections.Generic.List<string>();
            foreach (var c in canvases)
            {
                float hw = c.W * 0.5f, hh = c.H * 0.5f;
                float fpLift = RunHudScreen.WeaponClusterPosition(new Vector2(-32f, 32f), true, c.H).y;
                foreach (string variant in new[] { "base", "special", "fp" })
                {
                    var rects = new System.Collections.Generic.List<Rect>
                    {
                        Rect.MinMaxRect(-hw + 32f, hh - 32f - 36f, -hw + 32f + 170f, hh - 32f),                        // gold
                        Rect.MinMaxRect(-120f, hh - 24f - 56f, 120f, hh - 24f),                                          // seal
                        Rect.MinMaxRect(-320f, hh - 110f - 30f, 320f, hh - 110f),                                        // chip row
                        Rect.MinMaxRect(-hw + 32f, -hh + 32f, -hw + 32f + 380f, -hh + 32f + 110f * c.Tall),             // health
                        variant == "fp"
                            ? Rect.MinMaxRect(hw - 32f - 400f, -hh + fpLift, hw - 32f, -hh + fpLift + 140f * c.Tall)     // weapon, FP lifted
                            : Rect.MinMaxRect(hw - 32f - 400f, -hh + 32f, hw - 32f, -hh + 32f + 140f * c.Tall),         // weapon
                        new Rect(-HudRearEdge.PromptSlot.x * 0.5f, -hh + 24f, HudRearEdge.PromptSlot.x, HudRearEdge.PromptSlot.y),   // prompt slot
                    };
                    if (variant == "special")
                    {
                        rects.Add(Rect.MinMaxRect(-110f, -hh + 24f, 110f, -hh + 24f + 216f));                           // Special ring block
                    }

                    Rect[] avoid = rects.ToArray();
                    var canvas = new Vector2(c.W, c.H);
                    for (int b = 0; b < 64; b++)
                    {
                        float bearing = b * 360f / 64f - 180f;
                        Vector2 centre = HudRearEdge.Place(bearing, canvas, avoid, avoid.Length);
                        Rect stroke = HudRearEdge.StrokeBox(centre, bearing);
                        for (int i = 0; i < avoid.Length; i++)
                        {
                            Rect clear = Rect.MinMaxRect(avoid[i].xMin - HudRearEdge.Clearance + 0.5f, avoid[i].yMin - HudRearEdge.Clearance + 0.5f,
                                avoid[i].xMax + HudRearEdge.Clearance - 0.5f, avoid[i].yMax + HudRearEdge.Clearance - 0.5f);
                            if (stroke.Overlaps(clear))
                            {
                                failures.Add(c.Name + "/" + variant + " " + bearing + "°: stroke " + stroke + " meets rect " + i);
                            }
                        }

                        float half = HudRearEdge.Size * 0.5f;
                        if (Mathf.Abs(centre.x) + half > hw - HudRearEdge.Clearance + 0.5f || Mathf.Abs(centre.y) + half > hh - HudRearEdge.Clearance + 0.5f)
                        {
                            failures.Add(c.Name + "/" + variant + " " + bearing + "°: sprite leaves the screen - 12 px at " + centre);
                        }
                    }
                }

                // Straight behind with nothing in the way... except the reserved prompt slot: the stroke's apex on the inset.
                Vector2 behind = HudRearEdge.Place(180f, new Vector2(c.W, c.H), new Rect[0], 0);
                float apex = behind.y - HudRearEdge.StrokeMidRadius;
                Assert.AreEqual(-(hh - HudRearEdge.EdgeInset), apex, 2f, c.Name + ": çizginin orta noktası iç kenarda (96 px), fiziksel kenarda değil");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void FpSilahKumesi_GorusModelininUstunde_TepedenVeTpYerinde()
        {
            var home = new Vector2(-32f, 32f);
            Assert.AreEqual(home, RunHudScreen.WeaponClusterPosition(home, false, 1080f), "TD / TP: köşe (parite)");
            Vector2 fp = RunHudScreen.WeaponClusterPosition(home, true, 1080f);
            Assert.AreEqual(home.x, fp.x, 0.001f);
            Assert.AreEqual(1080f - 728f, fp.y, 1f, "AD R1-A MUST-2: 1080'de küme altı y ≤ 728 (görüş modeli üstü + 12)");
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
