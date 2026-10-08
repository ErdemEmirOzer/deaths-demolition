using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU M1–M13) · the main menu's v5 motion in a real Boot → MainMenu flow (the plates, no world:
    /// MenuCinematicWorldTests owns the world, its freeze and the transition's renders):
    ///   arrival    the session's first arrival plays once from the reveal - the rail transparent and staggering in, the hero flag
    ///              up, the title shown only by the S-11 hook's onDone (logo ≤ 2 s; the rail lands by MenuIntroMax); a later arrival does not replay it;
    ///              a Cancel during it skips it and still acts in the same frame (focus on PLAY);
    ///   hover      a rail item's focus plays its lift track and its ink wave behind it (past the 80 ms gate);
    ///   click      pointer down → the press track; the click → the splash, the pane built at α 0 on its frame and moving from the
    ///              next; Cancel → the card fades and the frame is wiped off, the card goes after Fast, the motes come back.
    /// </summary>
    [TestFixture]
    [Category("WP098")]
    public sealed class MenuV5MotionPlayTests
    {
        private bool _reduced;
        private bool _logoForced;
        private bool _fastTransitions;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            _logoForced = UiLogoMenuIntro.ForceInBatch;
            _fastTransitions = PrototypeUiFlow.FastTransitions;
            UiMotion.ReducedMotion = false;
            MenuCinematic.ResetForTests();   // batch: no world under the menu (the plates)
            MenuCinematic.FocusForTests = true;   // the batch Editor never has focus: the motes would rest
            MainMenuScreen.ResetIntroForTests();
            UiNavigation.Model.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedMotion = _reduced;
            UiLogoMenuIntro.ForceInBatch = _logoForced;
            PrototypeUiFlow.FastTransitions = _fastTransitions;
            MenuCinematic.ResetForTests();
        }

        private static UiIconButton Item(MainMenuScreen menu, string name)
        {
            foreach (UiIconButton icon in menu.Rail)
            {
                if (icon.gameObject.name == name) return icon;
            }

            Assert.Fail("rail item " + name);
            return null;
        }

        private static PointerEventData Pointer(GameObject go)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, pointerPress = go, pointerEnter = go };
            var corners = new Vector3[4];
            ((RectTransform)go.transform).GetWorldCorners(corners);
            Canvas canvas = go.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            data.position = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[2]) * 0.5f);
            return data;
        }

        [UnityTest]
        public IEnumerator AnaMenu_Acilis_BirKez_Kademe_KancaBaslik_Atlama_AyniKaredeIsler()
        {
            UiLogoMenuIntro.ForceInBatch = true;
            // The real logo hook is intentionally disabled by batch fast transitions.
            PrototypeUiFlow.FastTransitions = false;
            UiLogoMenuIntro.ResetSessionForTests();
            yield return BootTanMainMenuye();
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            yield return null;

            // M3: the rail comes in staggered from the reveal; the hero flag holds the idle pass and the waves off.
            Assert.IsTrue(menu.IntroRunning, "ilk geliş: kahraman koreografisi");
            Assert.IsTrue(UiIconMotion.HeroActive);
            float last = Item(menu, "Quit").GetComponent<CanvasGroup>().alpha;
            Assert.Less(last, 1f, "ray kademeli gelir (QUIT henüz tam değil)");
            Assert.IsNotNull(UiLogoMenuIntro.Current, "S-11: installed logo owns the single completion hook");
            Assert.IsFalse(menu.TitleShown, "the title waits for the actual logo callback");
            float logoAt = UiLogoMenuIntro.Current.Logo.Timeline.IntroStart;
            yield return Bekle(() => UiLogoMenuIntro.Current == null, UiLogoTimeline.MenuIntroDuration + 1f, "logo hook did not finish");
            Assert.LessOrEqual(UiFxGlobals.Now - logoAt, UiLogoTimeline.MenuIntroDuration + 0.1f, "brand intro stays within 2 s (+ a frame)");
            Assert.IsTrue(menu.TitleShown, "the one logo callback revealed the title");
            yield return new WaitForSecondsRealtime(UiMotionTokens.Seconds(UiDuration.Reveal) + 0.1f);
            Assert.IsFalse(menu.IntroRunning, "1,2 s içinde biter");
            Assert.IsFalse(UiIconMotion.HeroActive);
            foreach (UiIconButton icon in menu.Rail)
            {
                Assert.AreEqual(1f, icon.GetComponent<CanvasGroup>().alpha, 1e-4f, icon.name + " yerinde");
            }

            RectTransform clip = null;
            foreach (RectTransform rect in menu.GetComponentsInChildren<RectTransform>(true))
            {
                clip = rect.name == "TitleClip" ? rect : clip;
            }

            Assert.IsNotNull(clip);
            TMPro.TMP_Text title = clip.GetComponentInChildren<TMPro.TMP_Text>();
            Assert.AreEqual(title.rectTransform.sizeDelta.x, clip.sizeDelta.x, 0.5f, "M2: başlık tam açıldı");
            Assert.IsTrue(menu.TitleStrokeMark.Visible, "M2: başlığın fırça çizgisi");

            // A later arrival this session (M6): no replay.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            yield return null;
            menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsFalse(menu.IntroRunning, "M6: oturumda ikinci geliş kısa");

            // A replayed arrival skipped by Cancel: everything lands, and the Cancel still acts in that frame (focus on PLAY).
            MainMenuScreen.ResetIntroForTests();
            UiLogoMenuIntro.ResetSessionForTests();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            menu = Object.FindObjectOfType<MainMenuScreen>();
            yield return Bekle(() => menu.IntroRunning, 2f, "geliş başlamadı");
            UiInput.Focus(Item(menu, "Tab.Settings").Button);
            UiInput.SimulateCancelNextFrame();
            yield return null;
            yield return null;
            Assert.IsFalse(menu.IntroRunning, "Cancel atlar");
            foreach (UiIconButton icon in menu.Rail)
            {
                Assert.AreEqual(1f, icon.GetComponent<CanvasGroup>().alpha, 1e-4f, icon.name + " atlamada yerine iner");
            }

            Assert.AreSame(Item(menu, "Play").gameObject, EventSystem.current.currentSelectedGameObject, "aynı karede Cancel işlendi: odak PLAY");
            yield return Bekle(() => UiLogoMenuIntro.Current == null, 1f, "the menu skip did not complete the logo");
            Assert.IsTrue(menu.TitleShown, "skip callback lands the title");
            Assert.IsTrue(menu.TitleStrokeMark.Drawn, "skip leaves the title stroke at rest");
        }

        [UnityTest]
        public IEnumerator AnaMenu_Hover_IzVeDalga_Tik_BasmaSicramaPane_Iptal_SolarakKapanir()
        {
            yield return BootTanMainMenuye();
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            yield return new WaitForSecondsRealtime(UiMenuMotionTokens.MenuIntroMax + 0.3f);
            Assert.IsFalse(menu.IntroRunning);

            // M7: focus by the axis - the lift track, then the ink wave behind the glyph past its 80 ms gate.
            UiIconButton profile = Item(menu, "Tab.Profile");
            EventSystem.current.SetSelectedGameObject(profile.gameObject, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down });
            Assert.AreEqual(UiIconTrack.Lift, UiIconMotion.TrackOf(profile), "M7: kalkış izi");
            Assert.AreEqual(profile.ShortLabel, menu.RailWord.text, "ray kelimesi");
            UiInkWave wave = profile.transform.Find(UiInkWave.ObjectName).GetComponent<UiInkWave>();
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.Greater(wave.Group.alpha, 0f, "M7: mürekkep dalgası kapıdan sonra");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(UiIconPose.Lifted.Scale, profile.ShownPose.Scale, 1e-3f, "kalkmış dinlenme");

            // M9 / M10 / M11: pointer down → the press; the click → the splash and the pane, built at α 0, moving from the next frame.
            int splashes = menu.Splash.Played;
            ExecuteEvents.Execute(profile.gameObject, Pointer(profile.gameObject), ExecuteEvents.pointerDownHandler);
            Assert.AreEqual(UiIconTrack.Press, UiIconMotion.TrackOf(profile), "M9: basma izi");
            yield return new WaitForSecondsRealtime(0.1f);
            PointerEventData up = Pointer(profile.gameObject);
            ExecuteEvents.Execute(profile.gameObject, up, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(profile.gameObject, up, ExecuteEvents.pointerClickHandler);
            Assert.AreEqual(splashes + 1, menu.Splash.Played, "M10: sıçrama");
            Assert.IsTrue(menu.PaneOpen);
            Assert.AreEqual(0f, menu.PaneContentGroup.alpha, 1e-4f, "§5-4: kurulum karesinde α 0");
            Assert.IsFalse(menu.Ambient.Shown, "M17: pane açıkken kül ve yaprak çekilir");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(1f, menu.PaneContentGroup.alpha, 1e-3f, "M11: içerik geldi");
            Assert.AreEqual(1f, menu.PaneCardGroup.alpha, 1e-3f);

            // M12 / WP-098B: Cancel fades the frameless carrier out, then deactivates it; the motes return.
            UiInput.SimulateCancelNextFrame();
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.04f);
            Assert.IsFalse(menu.PaneOpen, "Cancel pane'i kapatır");
            Assert.Less(menu.PaneCardGroup.alpha, 1f, "M12: frameless taşıyıcı solarak çıkar");
            Assert.IsTrue(menu.Ambient.Shown);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(0f, menu.PaneContentGroup.alpha, 1e-4f, "kapanış sonrası frameless içerik görünmez kalır");
            Assert.IsNotNull(menu.PaneCarrier, "S04 frameless taşıyıcı korunur");
            Assert.IsFalse(menu.PaneCarrier.Root.gameObject.activeSelf, "taşıyıcı Fast sonunda gider");
            Assert.AreSame(profile.gameObject, EventSystem.current.currentSelectedGameObject, "odak ikona döner");
        }
    }
}
