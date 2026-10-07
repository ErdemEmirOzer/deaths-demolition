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

        /// <summary>
        /// WP-068 12c · B12 (AD 8-R SHOULD-A2): the HUD's 16:9 frame - off in an older file and by default, kept once on;
        /// the inset is half the width past 16:9 (21:9 canvas 2560 × 1080 → 320 a side), 0 on 16:9 or narrower and when off.
        /// </summary>
        [Test]
        public void HudCercevesi169_VarsayilanKapali_Kalici_KenarPayi()
        {
            File.WriteAllText(Path.Combine(_dir, UiSettings.FileName), "{\"Version\":1,\"UiScale\":1.0}");
            UiSettings.SetPathOverride(Path.Combine(_dir, UiSettings.FileName));
            Assert.IsFalse(UiSettings.HudFrame169, "eski dosya: kapalı");
            UiSettings.HudFrame169 = true;
            UiSettings.SetPathOverride(Path.Combine(_dir, UiSettings.FileName));   // reload from disk
            Assert.IsTrue(UiSettings.HudFrame169, "kalıcı");

            Assert.AreEqual(320f, RunHudScreen.HudFrameInset(new Vector2(2560f, 1080f), true), 0.01f, "21:9");
            Assert.AreEqual(0f, RunHudScreen.HudFrameInset(new Vector2(2560f, 1080f), false), 0.01f, "kapalı");
            Assert.AreEqual(0f, RunHudScreen.HudFrameInset(new Vector2(1920f, 1080f), true), 0.01f, "16:9");
            Assert.AreEqual(0f, RunHudScreen.HudFrameInset(new Vector2(1920f, 1247f), true), 0.01f, "Mac 16:10 daha dar");
            Assert.AreEqual(440f, RunHudScreen.HudFrameInset(new Vector2(2800f, 1080f), true), 0.5f, "32:9'a yakın");
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
                float fpBottom = RunHudScreen.WeaponClusterPosition(new Vector2(-32f, 32f), true, new Vector2(c.W, c.H)).y;
                // AD FP cluster SHOULD-3: first person with the Special block (and its prompt row) up as well.
                foreach (string variant in new[] { "base", "special", "fp", "special-prompt", "fp-special", "fp-special-prompt" })
                {
                    var rects = new System.Collections.Generic.List<Rect>
                    {
                        Rect.MinMaxRect(-hw + 32f, hh - 32f - 36f, -hw + 32f + 170f, hh - 32f),                        // gold
                        Rect.MinMaxRect(-120f, hh - 24f - 56f, 120f, hh - 24f),                                          // seal
                        Rect.MinMaxRect(-320f, hh - 110f - 30f, 320f, hh - 110f),                                        // chip row
                        Rect.MinMaxRect(-hw + 32f, -hh + 32f, -hw + 32f + 380f, -hh + 32f + 110f * c.Tall),             // health
                        variant.StartsWith("fp")
                            ? Rect.MinMaxRect(-hw + 32f, -hh + fpBottom, -hw + 32f + 400f, -hh + fpBottom + 140f * c.Tall)  // weapon (FP: above the health block, C-00 25.09)
                            : Rect.MinMaxRect(hw - 32f - 400f, -hh + 32f, hw - 32f, -hh + 32f + 140f * c.Tall),         // weapon
                        new Rect(-HudRearEdge.PromptSlot.x * 0.5f, -hh + 24f, HudRearEdge.PromptSlot.x, HudRearEdge.PromptSlot.y),   // prompt slot
                    };
                    if (variant.Contains("special"))
                    {
                        rects.Add(Rect.MinMaxRect(-110f, -hh + 24f, 110f, -hh + 24f + 260f));                           // Special ring block (R1-B: 260)
                    }

                    if (variant.EndsWith("special-prompt"))
                    {
                        // AD R1-B MUST-3: the prompt / SLICK row closed onto the Special block at gap 8.
                        float row = -hh + 24f + 260f + 8f;
                        rects.Add(new Rect(-HudRearEdge.PromptSlot.x * 0.5f, row, HudRearEdge.PromptSlot.x, HudRearEdge.PromptSlot.y));
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

        /// <summary>
        /// WP-068 12b (AD R1-A SHOULD-10): a true point below a rect's centre resolves downward past the rect's bottom edge
        /// where the sprite fits under it (R1-A's 120° case), and the lift stands where it does not; the 64-bearing sweep above
        /// runs the same Place.
        /// </summary>
        [Test]
        public void ArkaYay_DogruNoktaDikdortgenOrtasininAltinda_AsagiCozulur_YerYoksaKalkar()
        {
            var canvas = new Vector2(1920f, 1080f);
            float hh = canvas.y * 0.5f;

            // R1-A's case: behind right (120°), a rect standing over the true point with room under it.
            const float bearing = 120f;
            Vector2 truth = HudRearEdge.EdgePoint(bearing, canvas);   // ≈ (748, −222)
            Rect over = Rect.MinMaxRect(truth.x - 200f, truth.y - 20f, truth.x + 200f, truth.y + 160f);
            Rect[] one = { over };
            Vector2 down = HudRearEdge.Place(bearing, canvas, one, 1);
            Vector2 lift = HudRearEdge.Resolve(bearing, canvas, one, 1, false);
            Rect downBox = HudRearEdge.StrokeBox(down, bearing);
            Rect liftBox = HudRearEdge.StrokeBox(lift, bearing);
            Assert.AreEqual(lift.x, down.x, 0.001f, "x değişmez: yön dürüst kalır");
            Assert.LessOrEqual(downBox.yMax, over.yMin - HudRearEdge.Clearance + 0.01f, "dikdörtgenin alt kenarının altına çözülür");
            Assert.GreaterOrEqual(liftBox.yMin, over.yMax + HudRearEdge.Clearance - 0.01f, "eski kural üstüne kaldırırdı");
            Assert.Less(Mathf.Abs(downBox.center.y - truth.y), Mathf.Abs(liftBox.center.y - truth.y) - 100f, "doğru noktaya ≥ 100 px daha yakın");
            Assert.GreaterOrEqual(down.y - HudRearEdge.Size * 0.5f, -hh + HudRearEdge.Clearance - 0.01f, "sprite ekran kenarı − 12 içinde");

            // No room: a rect near the bottom edge with the true point below its centre - the sprite would not fit under it.
            Rect low = Rect.MinMaxRect(-150f, -500f, 150f, -380f);
            Rect[] lowOnly = { low };
            Assert.AreEqual(HudRearEdge.Resolve(180f, canvas, lowOnly, 1, false), HudRearEdge.Place(180f, canvas, lowOnly, 1), "yer yoksa kaldırma kalır");

            // The live first-person layout (C-00 25.09): behind left at −146.25° the true point lies just under the weapon
            // cluster's centre (x 32–432, y 820–960 from the top); the stroke now sits under the cluster, above the health block's
            // column, instead of 210 px over it.
            var fp = new[]
            {
                Rect.MinMaxRect(-928f, -hh + RunHudScreen.FpWeaponClusterBottom, -528f, -hh + RunHudScreen.FpWeaponClusterBottom + RunHudScreen.WeaponClusterHeight),
                Rect.MinMaxRect(-928f, -hh + 32f, -548f, -hh + 142f),                                                  // health
                new Rect(-HudRearEdge.PromptSlot.x * 0.5f, -hh + 24f, HudRearEdge.PromptSlot.x, HudRearEdge.PromptSlot.y),   // prompt slot
            };
            Vector2 fpAt = HudRearEdge.Place(-146.25f, canvas, fp, fp.Length);
            Rect fpBox = HudRearEdge.StrokeBox(fpAt, -146.25f);
            Assert.LessOrEqual(fpBox.yMax, fp[0].yMin - HudRearEdge.Clearance + 0.01f, "FP: kümenin altında");
            for (int i = 0; i < fp.Length; i++)
            {
                Rect clear = Rect.MinMaxRect(fp[i].xMin - HudRearEdge.Clearance + 0.5f, fp[i].yMin - HudRearEdge.Clearance + 0.5f,
                    fp[i].xMax + HudRearEdge.Clearance - 0.5f, fp[i].yMax + HudRearEdge.Clearance - 0.5f);
                Assert.IsFalse(fpBox.Overlaps(clear), "FP: çizgi " + i + ". dikdörtgene girmez");
            }
        }

        /// <summary>R3 (the user 06.10: top-down only - its tests run alone): the weapon cluster stands in the corner in top-down.</summary>
        [Test]
        public void SilahKumesi_Tepeden_Kosede()
        {
            var home = new Vector2(-32f, 32f);
            var canvas = new Vector2(1920f, 1080f);
            Assert.AreEqual(home, RunHudScreen.WeaponClusterPosition(home, false, canvas), "TD: köşe");
            Assert.AreEqual(new Rect(1488f, 908f, 400f, 140f), RunHudScreen.WeaponClusterRect(false, canvas), "köşe rect (üst-sol köken)");
            Assert.AreEqual(RunHudScreen.WeaponClusterRect(false, canvas), RunHudScreen.WeaponClusterRect(DD.Prototype.CameraSystem.CameraMode.TopDown, canvas), "TD köşe");
        }

        /// <summary>The first-person / third-person half (out of scope since 06.10, kept under FPTP).</summary>
        [Test]
        [Category("FPTP")]
        public void SilahKumesi_FpTp_Parite()
        {
            // C-00 (25.09): S-02's FP viewmodel v2 owns the bottom right (x 1074–1918, y 502–1078 at 1080), so in first person
            // the cluster stands above the health block; third person keeps the corner.
            var home = new Vector2(-32f, 32f);
            var canvas = new Vector2(1920f, 1080f);
            Assert.AreEqual(new Vector2(-1488f, 120f), RunHudScreen.WeaponClusterPosition(home, true, canvas), "FP: sol alt, can bloğunun üstü");
            Rect fp = RunHudScreen.WeaponClusterRect(true, canvas);
            Assert.AreEqual(new Rect(32f, 820f, 400f, 140f), fp, "FP rect (üst-sol köken)");
            var viewmodel = Rect.MinMaxRect(1074f, 502f, 1918f, 1078f);   // S-02 v2 (235d167a): the ten weapons' union
            Assert.IsFalse(fp.Overlaps(viewmodel), "FP kümesi görüş modeline değmez");
            var health = Rect.MinMaxRect(32f, 1080f - 32f - 80f, 412f, 1048f);
            Assert.IsFalse(fp.Overlaps(health), "FP kümesi can bloğunun üstünde");
            Assert.AreEqual(fp, RunHudScreen.WeaponClusterRect(DD.Prototype.CameraSystem.CameraMode.FirstPerson), "mod imzası = FP (referans tuval)");
            Assert.AreEqual(RunHudScreen.WeaponClusterRect(false, canvas), RunHudScreen.WeaponClusterRect(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, canvas), "TP köşe");
        }

        /// <summary>R3 (top-down only): the top-down reticle - the minimum ring, no dot, no Sumi twin.</summary>
        [Test]
        public void Nisangah_Tepeden_HalkaNoktasizIkizsiz()
        {
            var host = new GameObject("ReticleHost", typeof(RectTransform));
            try
            {
                var reticle = new HudReticle((RectTransform)host.transform);
                reticle.SetMode(CameraMode.TopDown);
                Assert.AreEqual(HudReticle.MinRadius, reticle.MinimumRadius, 0.001f);
                Assert.IsFalse(reticle.DotShown, "tepeden: nokta yok");
                Assert.IsFalse(reticle.BackingShown, "tepeden: Sumi ikiz yok (bugünkü nişangâh)");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>The third-person / first-person reticle styles (out of scope since 06.10, kept under FPTP).</summary>
        [Test]
        [Category("FPTP")]
        public void Nisangah_ModStili_TP14NoktaIle_FP80()
        {
            var host = new GameObject("ReticleHost", typeof(RectTransform));
            try
            {
                var reticle = new HudReticle((RectTransform)host.transform);
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
