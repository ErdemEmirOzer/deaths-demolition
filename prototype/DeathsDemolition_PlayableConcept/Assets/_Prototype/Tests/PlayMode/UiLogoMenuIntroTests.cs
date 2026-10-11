using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10, K7/K12) · the main menu's logo intro: off in batch unless forced, with FastTransitions
    /// and under reduced motion; ≤ 2.0 s; one BootLogo bell at the impact; never a frame where the logo (≥ 0.95) and the menu
    /// title (≥ 0.95) are both fully opaque; a skip ends it at once (0.14 s fade) and a direct API skip never dispatches a
    /// menu action in this test; navigation stays enabled and the menu owns input; once per session.
    /// </summary>
    [Category("AP035")]
    public sealed class UiLogoMenuIntroTests
    {
        private bool _fast;
        private bool _reduced;
        private GameObject _host;
        private string _settingsPath;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            _reduced = UiMotion.ReducedMotion;
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-ap035-menu-ui-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
        }

        [TearDown]
        public void TearDown()
        {
            UiLogoMenuIntro.ForceInBatch = false;
            PrototypeUiFlow.FastTransitions = _fast;
            UiMotion.ReducedMotion = _reduced;
            if (_host != null)
            {
                Object.Destroy(_host);
            }

            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        private IEnumerator MenuReady()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "menu revealed");
            yield return null;
            PrototypeUiFlow.FastTransitions = false;
            UiMotion.ReducedMotion = false;
            UiLogoMenuIntro.ForceInBatch = true;
            UiLogoMenuIntro.ResetSessionForTests();
        }

        private UiLogoMenuIntro StartIntro(System.Action onDone = null)
        {
            if (_host != null)
            {
                Object.Destroy(_host);
            }

            _host = new GameObject("test.UiLogoMenuIntro");
            UiLogoMenuIntro intro = _host.AddComponent<UiLogoMenuIntro>();
            intro.Play(onDone);
            return intro;
        }

        /// <summary>R4 WP-105: the menu's title is the wordmark + seal lockup ("Title", MainMenuScreen.BuildTitle).</summary>
        private static RectTransform Title()
        {
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu, "the menu screen");
            foreach (RectTransform t in menu.GetComponentsInChildren<RectTransform>(true))
            {
                if (t.name == "Title") return t;
            }

            Assert.Fail("the menu title");
            return null;
        }

        /// <summary>The lockup's least drawn opacity: each part's colour × renderer alpha (the logo intro's group is read apart).</summary>
        private static float Drawn(RectTransform title)
        {
            float least = 1f;
            foreach (Image part in title.GetComponentsInChildren<Image>(true))
            {
                least = Mathf.Min(least, part.color.a * part.canvasRenderer.GetAlpha());
            }

            return least;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Izin_BatchHizliGecisAzHareketteKapali()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            UiLogoMenuIntro.ForceInBatch = false;
            PrototypeUiFlow.FastTransitions = false;
            Assert.IsFalse(UiLogoMenuIntro.Allowed, "batch: off unless forced (the menu suites never see it)");
            UiLogoMenuIntro.ForceInBatch = true;
            Assert.IsTrue(UiLogoMenuIntro.Allowed);
            PrototypeUiFlow.FastTransitions = true;
            Assert.IsFalse(UiLogoMenuIntro.Allowed, "FastTransitions: off");
            PrototypeUiFlow.FastTransitions = false;
            UiMotion.ReducedMotion = true;
            Assert.IsFalse(UiLogoMenuIntro.Allowed, "reduced motion: no logo, the menu at once (K6)");
            Assert.IsNull(UiLogoMenuIntro.Current, "nothing runs in the batch menu");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Giris_En2Saniye_TekCan_BaslikVeLogoAyniAndaTamOpakDegil()
        {
            yield return MenuReady();
            int bells = 0;
            System.Action<UiEvent, string> count = (e, d) => { if (e == UiEvent.BootLogo) bells++; };
            UiEvents.Raised += count;
            try
            {
                StartIntro();
                yield return Bekle(() => UiLogoMenuIntro.Current != null && UiLogoMenuIntro.Current.Running, 5f, "the intro starts");
                UiLogoMenuIntro intro = UiLogoMenuIntro.Current;
                RectTransform title = Title();
                float t0 = intro.Logo.Timeline.IntroStart;
                Assert.IsTrue(EventSystem.current == null || EventSystem.current.sendNavigationEvents, "menu input stays enabled; the menu owns skipping");
                int both = 0, frames = 0;
                float maxLogo = 0f;
                while (UiLogoMenuIntro.Current == intro && UiFxGlobals.Now - t0 < 5f)
                {
                    float logo = intro.Logo != null ? intro.Logo.Alpha : 0f;
                    float titleAlpha = intro.TitleAlpha * Drawn(title);
                    maxLogo = Mathf.Max(maxLogo, logo);
                    if (logo >= 0.95f && titleAlpha >= 0.95f) both++;
                    frames++;
                    yield return null;
                }

                float seconds = UiFxGlobals.Now - t0;
                Debug.Log("[DD-TEST] ap035.menuIntro seconds=" + seconds.ToString("F3") + " frames=" + frames + " bells=" + bells + " bothOpaque=" + both + " maxLogo=" + maxLogo.ToString("F2"));
                Assert.Greater(maxLogo, 0.95f, "the logo showed");
                Assert.AreEqual(0, both, "never the logo and the title both fully opaque (C-00 08.10)");
                Assert.LessOrEqual(seconds, UiLogoTimeline.MenuIntroDuration + 0.1f, "K7: ≤ 2.0 s (+ a frame)");
                Assert.LessOrEqual(UiLogoTimeline.MenuIntroDuration, 2.0f);
                Assert.AreEqual(1, bells, "K12: one BootLogo bell at the impact");
                yield return null;
                yield return null;
                Assert.IsTrue(EventSystem.current == null || EventSystem.current.sendNavigationEvents, "navigation remains enabled after it");
                Assert.IsNull(title.GetComponent<CanvasGroup>(), "the title's temporary group is gone");
                Assert.AreEqual(1f, Drawn(title), 0.01f, "the title stands");
            }
            finally
            {
                UiEvents.Raised -= count;
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Atlama_HemenBiter_MenuDegismez_OturumdaBirKez()
        {
            yield return MenuReady();
            int completions = 0;
            StartIntro(() => completions++);
            yield return Bekle(() => UiLogoMenuIntro.Current != null && UiLogoMenuIntro.Current.Running, 5f, "the intro starts");
            UiLogoMenuIntro intro = UiLogoMenuIntro.Current;
            yield return new WaitForSecondsRealtime(0.3f);
            float at = UiFxGlobals.Now;
            intro.RequestSkip();
            intro.RequestSkip();   // twice: harmless
            yield return Bekle(() => UiLogoMenuIntro.Current == null, 2f, "the skip ends it");
            float took = UiFxGlobals.Now - at;
            Debug.Log("[DD-TEST] ap035.menuIntro.skip seconds=" + took.ToString("F3"));
            Assert.LessOrEqual(took, UiLogoTimeline.SkipFadeSeconds + 0.1f, "a skip: the last frame, gone in 0.14 s");
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "the skip changed nothing in the menu");
            Assert.IsFalse(PrototypeUiFlow.IsLoading, "the skipping press opened nothing");

            // once per session: a second menu arrival plays nothing
            Assert.IsTrue(UiLogoMenuIntro.PlayedThisSession);
            Assert.AreEqual(1, completions, "two skip requests complete the menu hook exactly once");
            StartIntro();
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsNull(UiLogoMenuIntro.Current, "once per session");

            // reduced motion: never
            UiLogoMenuIntro.ResetSessionForTests();
            UiMotion.ReducedMotion = true;
            StartIntro();
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsNull(UiLogoMenuIntro.Current, "RM: no logo intro");
        }
    }
}
