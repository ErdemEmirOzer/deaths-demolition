using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-081 · the main menu's cinematic backdrop (the user's G5 addition, AP-028 v002's five shots, C-00 05.10): the shot
    /// list as shipped, the 5 × 12 s timeline (1.2 s hold, 9.6 s smoothstep, the next start under the held end for the last
    /// 1.2 s), the seam at 60 s, the framing (the target at u 0.68 / v 0.48 at every aspect), the batch gate; the plates
    /// stepping aside for the world with the scrim kept; the menu canvases moving to overlays over it.
    /// </summary>
    [TestFixture]
    [Category("WP081")]
    public sealed class MenuCinematicTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private bool _reduced;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            MenuCinematic.ResetForTests();
            UiFactory.OverlayScreens = false;
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedMotion = _reduced;
            MenuCinematic.ResetForTests();
            UiFactory.OverlayScreens = false;
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static void Near(Vector3 expected, Vector3 actual, string what)
        {
            Assert.Less((expected - actual).magnitude, 1e-4f, what + ": " + expected.ToString("F4") + " ≠ " + actual.ToString("F4"));
        }

        [Test]
        public void CekimListesi_BesCekim_60s_AP028v002()
        {
            MenuCinematic.Shot[] shots = MenuCinematic.Shots;
            Assert.AreEqual(5, shots.Length, MenuCinematic.ShotResource + " (" + MenuCinematic.Schema + ")");
            CollectionAssert.AreEqual(new[] { "S01", "S02", "S03", "S04", "S05" }, System.Array.ConvertAll(shots, s => s.id));
            Assert.AreEqual(60f, MenuCinematic.Period, 1e-4f);
            Assert.AreEqual(MenuCinematic.Slice, MenuCinematic.Hold + MenuCinematic.Move + MenuCinematic.Blend, 1e-4f, "1.2 + 9.6 + 1.2 = 12");
            // S01's start is S-05's verified menu camera (menu_plates.json): the static frame Reduced Motion shows.
            Near(new Vector3(39.75f, 1.6f, 24.34f), shots[0].Start, "S01 başlangıç");
            Near(new Vector3(30.25f, 1.005f, 9.84f), shots[0].TargetStart, "S01 hedef");
            Assert.AreEqual(44f, shots[0].vfov, 1e-4f);
            foreach (MenuCinematic.Shot shot in shots)
            {
                Assert.AreEqual(new Vector2(0.68f, 0.48f), shot.ScreenTarget, shot.id + " screen_target_uv");
                Assert.Less((shot.End - shot.Start).magnitude, 1.5f, shot.id + ": yavaş hareket (≤ 1.2 m tasarım)");
            }
        }

        [Test]
        public void Dilim_Bekle_Kaydir_Bekle_SonrakiBaslangicAltinda()
        {
            MenuCinematic.Shot s1 = MenuCinematic.Shots[0];
            Near(s1.Start, MenuCinematic.PoseOf(s1, 0f).Position, "0 s");
            Near(s1.Start, MenuCinematic.PoseOf(s1, 1.2f).Position, "1.2 s: tutuş biter");
            Near(Vector3.Lerp(s1.Start, s1.End, 0.5f), MenuCinematic.PoseOf(s1, 6f).Position, "6 s: yolun yarısı (smoothstep simetrik)");
            Near(s1.End, MenuCinematic.PoseOf(s1, 10.8f).Position, "10.8 s: son poz");
            Near(s1.End, MenuCinematic.PoseOf(s1, 12f).Position, "12 s: son poz tutulur");
            // The ends are still: smoothstep's slope is 0, so a frame after the hold the camera has barely moved.
            float early = (MenuCinematic.PoseOf(s1, 1.2f + 1f / 60f).Position - s1.Start).magnitude;
            Assert.Less(early, 1e-3f * (s1.End - s1.Start).magnitude + 1e-5f, "tutuştan sonraki kare: hız ~0");

            Assert.IsFalse(MenuCinematic.Blending(10.79f));
            Assert.IsTrue(MenuCinematic.Blending(10.8f));
            Assert.AreEqual(1f, MenuCinematic.HeldWeight(10.8f), 1e-4f, "geçiş başında tutulan son kare tam");
            Assert.AreEqual(0.5f, MenuCinematic.HeldWeight(11.4f), 1e-4f);
            Assert.AreEqual(0f, MenuCinematic.HeldWeight(12f), 1e-4f, "dikişte sıfır");
            // Inside the blend the live camera already stands at the next shot's start.
            Near(MenuCinematic.Shots[1].Start, MenuCinematic.LivePose(11f).Position, "S01 geçişi altında S02 başlangıcı");
            Near(MenuCinematic.Shots[1].Start, MenuCinematic.LivePose(12f).Position, "12 s: S02 başlangıcı (atlama yok)");
        }

        [Test]
        public void Dongu_60s_Dikissiz_S05tenS01e()
        {
            int slice = MenuCinematic.SliceAt(59.5f, out float local);
            Assert.AreEqual(4, slice);
            Assert.AreEqual(11.5f, local, 1e-3f);
            Near(MenuCinematic.Shots[0].Start, MenuCinematic.LivePose(59.5f).Position, "S05 geçişi altında S01 başlangıcı");
            Near(MenuCinematic.LivePose(0f).Position, MenuCinematic.LivePose(60f).Position, "0 s = 60 s");
            Near(MenuCinematic.LivePose(0f).Target, MenuCinematic.LivePose(60f).Target, "0 s = 60 s hedef");
            Near(MenuCinematic.LivePose(7.25f).Position, MenuCinematic.LivePose(67.25f).Position, "periyot 60 s");
            for (int s = 0; s < 5; s++)
            {
                // Every seam: the live pose just before (the next start) is the live pose just after.
                float seam = (s + 1) * MenuCinematic.Slice;
                Near(MenuCinematic.LivePose(seam - 1e-3f).Position, MenuCinematic.LivePose(seam).Position, "dikiş " + seam + " s");
            }
        }

        [Test]
        public void Kadraj_Hedef_068_048_HerEkranOraninda()
        {
            var go = new GameObject("CineCamera", typeof(Camera));
            _created.Add(go);
            Camera camera = go.GetComponent<Camera>();
            foreach (float aspect in new[] { 16f / 9f, 16f / 10f, 21f / 9f, 4f / 3f, 3024f / 1964f })
            {
                camera.aspect = aspect;
                foreach (MenuCinematic.Shot shot in MenuCinematic.Shots)
                {
                    foreach (float local in new[] { 0f, 6f, 12f })
                    {
                        MenuCinematic.Pose pose = MenuCinematic.PoseOf(shot, local);
                        MenuCinematic.Place(camera, pose);
                        Vector3 v = camera.WorldToViewportPoint(pose.Target);
                        Assert.Greater(v.z, 0f, shot.id + " hedef önde");
                        Assert.AreEqual(0.68f, v.x, 2e-3f, shot.id + " u @" + aspect.ToString("F3") + " " + local + " s");
                        Assert.AreEqual(0.48f, v.y, 2e-3f, shot.id + " v @" + aspect.ToString("F3") + " " + local + " s");
                        Assert.AreEqual(0f, Mathf.DeltaAngle(0f, camera.transform.eulerAngles.z), 0.01f, shot.id + " yuvarlanma yok");
                    }
                }
            }
        }

        [Test]
        public void TopluKosuda_DunyaYok_TestIsteyinceVar()
        {
            Assert.IsTrue(Application.isBatchMode, "EditMode koşucusu batch");
            Assert.IsFalse(MenuCinematic.Allowed, "batch'te menü dünyası yüklenmez (testler, CI)");
            MenuCinematic.ForceInBatch = true;
            Assert.IsTrue(MenuCinematic.Allowed);
            Assert.IsFalse(MenuCinematic.HoldsReveal, "menü yokken perde beklemez");
            Assert.AreEqual(1f, MenuCinematic.LoadProgress, 1e-4f);
        }

        [Test]
        public void Plakalar_DunyayaYerVerir_PerdeVeSoluklasmaKalir()
        {
            var host = new GameObject("Root", typeof(RectTransform));
            _created.Add(host);
            var root = (RectTransform)host.transform;
            root.sizeDelta = new Vector2(1920f, 1080f);
            UiBackdrop backdrop = UiBackdrop.Create(root);
            Assert.IsNotNull(backdrop);
            Transform stack = backdrop.transform;

            backdrop.ShowWorld(1.2f, 10f);
            Assert.IsTrue(backdrop.WorldShown);
            foreach (string band in UiBackdrop.WorldBands)
            {
                Transform t = stack.Find(band);
                Assert.IsNotNull(t, band);
                Assert.AreEqual(1f, t.GetComponent<CanvasGroup>().alpha, 1e-4f, band + " başta tam");
            }

            backdrop.StepWorldFade(10.6f);
            Assert.AreEqual(0.5f, stack.Find("Far").GetComponent<CanvasGroup>().alpha, 1e-4f, "yarıda yarı (smoothstep)");
            backdrop.StepWorldFade(11.25f);   // past the 1.2 s (float: 11.2 - 10 lands a hair under it; the next frame ends it)
            foreach (string band in UiBackdrop.WorldBands)
            {
                Assert.IsFalse(stack.Find(band).gameObject.activeSelf, band + " sonunda çizilmez");
            }

            foreach (string kept in new[] { "TextSafeScrim", "TextSafeFade" })
            {
                Transform t = stack.Find(kept);
                Assert.IsTrue(t.gameObject.activeSelf, kept + " dünyanın üstünde kalır");
                Assert.AreEqual(UiBackdrop.ScrimAlpha, t.GetComponent<Image>().color.a, 1e-4f, kept + " α 0.55 (C-00 05.10 karar b)");
                Assert.IsNull(t.GetComponent<CanvasGroup>());
            }

            var host2 = new GameObject("Root2", typeof(RectTransform));
            _created.Add(host2);
            ((RectTransform)host2.transform).sizeDelta = new Vector2(1920f, 1080f);
            UiBackdrop cut = UiBackdrop.Create((RectTransform)host2.transform);
            cut.ShowWorld(0f, 0f);
            Assert.IsFalse(cut.transform.Find("Far").gameObject.activeSelf, "perde arkasında / RM: hemen");
        }

        [Test]
        public void DunyaPerdesi_GenisYumusak_KenardaCizgiYok_VarsayilanB()
        {
            MenuLook shipped = MenuLook.Shipped();
            Assert.IsTrue(shipped.GradientScrim && shipped.Pads, "C-00 05.10: B + pads ships");
            Assert.IsTrue(shipped.CornerLift, "S-09 06.10 (AD MUSTs 1-2): the title and the level readout stand on the top corners' lifts");
            Assert.GreaterOrEqual(MenuLook.TitleLiftAlpha, 0.85f, "AD MUST-2: α ≥ 0.85 over the title's box");
            Assert.GreaterOrEqual(MenuLook.TitleLiftMargin, 24f, "AD MUST-2: + 24 around the box");
            Assert.GreaterOrEqual(MenuLook.CornerFall, 0.12f, "AD MUST-1: a corner falls over at least 0.12 W");
            Assert.IsFalse(shipped.LookB, "Look B stays a dev option");

            var host = new GameObject("Root", typeof(RectTransform));
            _created.Add(host);
            var root = (RectTransform)host.transform;
            root.sizeDelta = new Vector2(1920f, 1080f);
            UiBackdrop backdrop = UiBackdrop.Create(root);
            backdrop.SetWorldScrim(shipped.ScrimEdge, shipped.ScrimColumn, shipped.ScrimEnd);
            float column = backdrop.WorldScrimColumn;
            Assert.AreEqual(shipped.ScrimEdge, backdrop.WorldScrimAlphaAt(0f), 1e-4f);
            Assert.AreEqual(shipped.ScrimColumn, backdrop.WorldScrimAlphaAt(column), 1e-4f);
            Assert.AreEqual(0f, backdrop.WorldScrimAlphaAt(shipped.ScrimEnd), 1e-4f);
            // No line: the profile falls monotonically and its steepest step is bounded (smoothstep: 1.5 × the mean slope),
            // and the two halves meet with no slope jump at the column's edge.
            float previous = 1f, steepest = 0f;
            const int Steps = 2000;
            for (int i = 0; i <= Steps; i++)
            {
                float x = shipped.ScrimEnd * i / Steps;
                float a = backdrop.WorldScrimAlphaAt(x);
                Assert.LessOrEqual(a, previous + 1e-5f, "monotone at " + x);
                if (i > 0) steepest = Mathf.Max(steepest, (previous - a) / (shipped.ScrimEnd / Steps));
                previous = a;
            }

            float mean = shipped.ScrimColumn / (shipped.ScrimEnd - column);
            Assert.LessOrEqual(steepest, 1.5f * mean + 1e-3f, "steepest fall per screen width");
            // Smoothstep's slope is 0 at both ends: right at the column's edge each side falls at a few % of the mean.
            float d = 1e-4f;
            float left = (backdrop.WorldScrimAlphaAt(column) - backdrop.WorldScrimAlphaAt(column - d)) / d;
            float right = (backdrop.WorldScrimAlphaAt(column + d) - backdrop.WorldScrimAlphaAt(column)) / d;
            Assert.Less(Mathf.Abs(left), 0.05f * mean, "flat into the column's edge");
            Assert.Less(Mathf.Abs(right), 0.05f * mean, "flat out of it");
            Assert.IsTrue(backdrop.HasWorldScrim);
            Assert.IsFalse(backdrop.transform.Find("TextSafeScrim").gameObject.activeSelf, "the third's solid scrim steps aside");
            // A resize re-cover-fits the plates, never the ramp (bfinal2: cropped to a sliver - a flat panel, a hard edge).
            // (the handler is private; SendMessage asserts on a component that does not run in edit mode)
            typeof(UiBackdrop).GetMethod("OnRectTransformDimensionsChange", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(backdrop, null);
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), backdrop.transform.Find("WorldScrim").GetComponent<RawImage>().uvRect, "the ramp stays whole");
        }

        [Test]
        public void DunyaCanli_KanvaslarOverlay_UiKamerasiCizmez_DinleyiciKalir()
        {
            // The camera EnsureUiCamera makes for a menu scene (built here: the Editor may hold other cameras).
            var camera = new GameObject(UiFactory.UiCameraName, typeof(Camera), typeof(AudioListener));
            _created.Add(camera);
            Camera ui = camera.GetComponent<Camera>();
            var other = new GameObject("Turntable", typeof(Camera));
            _created.Add(other);
            Canvas preview = UiFactory.CreateCanvas("PreviewCanvas", 0, null, other.GetComponent<Camera>());
            _created.Add(preview.gameObject);
            Canvas canvas = UiFactory.CreateCanvas("MenuCanvas", UiTokens.SortingScreen, null, ui);
            _created.Add(canvas.gameObject);
            Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);

            UiFactory.MoveScreensToOverlay();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode, "kelimeler dünyanın post-işlemine girmez");
            Assert.IsFalse(ui.enabled, "UI kamerası dünyayı örtmez");
            Assert.IsTrue(camera.GetComponent<AudioListener>().enabled, "ses dinleyicisi kalır");
            Assert.AreEqual(RenderMode.ScreenSpaceCamera, preview.renderMode, "başka bir kameranın kanvası (önizleme) dokunulmaz");
            Assert.IsTrue(other.GetComponent<Camera>().enabled);

            Assert.IsNull(UiFactory.EnsureUiCamera(), "dünya canlıyken yeni ekranlar overlay");
            UiFactory.OverlayScreens = false;
        }
    }
}
