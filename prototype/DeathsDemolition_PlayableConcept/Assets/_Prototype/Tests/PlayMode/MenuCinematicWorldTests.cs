using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-081 · the main menu over the real run world (the user's G5 addition; C-00 05.10 items 1-5): the world the player
    /// gets with no flag loads under the menu behind the doors, as a view only (no run systems, markers, player or HUD; no
    /// planar reflection); the menu goes over it as an overlay with the UI camera off and one listener; the plates step
    /// aside, the α 0.55 scrim stays; Reduced Motion and an open pane hold S01's start; the camera's 0.35 m sphere stays
    /// clear of the world's colliders along every path; leaving the menu takes the world, the viewer flag and the overlays
    /// with it.
    /// </summary>
    [TestFixture]
    [Category("WP081")]
    public sealed class MenuCinematicWorldTests
    {
        private readonly List<string> _errors = new List<string>();
        private bool _reduced;

        [SetUp]
        public void SetUp()
        {
            _errors.Clear();
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            MenuCinematic.ResetForTests();
            MenuCinematic.ForceInBatch = true;
            MenuCinematic.FocusForTests = true;
            UiNavigation.Model.Clear();
            Application.logMessageReceived += Collect;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Collect;
            UiMotion.ReducedMotion = _reduced;
            MenuCinematic.ResetForTests();
            Assert.AreEqual(0, _errors.Count, "Test boyunca hata loglandı:\n" + string.Join("\n", _errors));
        }

        private void Collect(string message, string trace, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) &&
                (message.Contains("[UI]") || message.Contains("[AP005]") || message.Contains("Exception")))
            {
                _errors.Add(type + ": " + message);
            }
        }

        [UnityTest]
        public IEnumerator AnaMenu_GercekDunyaUstunde_YalnizGorunum_Overlay_DurmaKurallari_CikistaTemiz()
        {
            yield return BootTanMainMenuye();
            yield return Bekle(() => MenuCinematic.Current != null && MenuCinematic.Current.Live, 90f, "menü dünyası canlanmadı");
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            MenuCinematic cinematic = MenuCinematic.Current;
            WorldSourceSettings.Resolve(out string worldScene);
            Assert.IsTrue(SceneManager.GetSceneByName(worldScene).isLoaded, worldScene + " menünün altında yüklü");
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "etkin sahne menü kalır");
            Assert.IsTrue(WorldViewer.Active);

            // A view only: none of the run.
            Assert.IsNull(GameObject.Find(RunSystemsInstaller.HostName), "run sistemleri kurulmadı");
            Assert.IsNull(PlayerRegistry.Primary, "oyuncu yok");
            Assert.IsNull(Object.FindObjectOfType<RunHudScreen>(), "HUD yok");
            int planar = 0;
            foreach (MonoBehaviour behaviour in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (behaviour.GetType().Name == "PlanarReflection" && behaviour.gameObject.scene.name == worldScene)
                {
                    Assert.IsFalse(behaviour.enabled, "menüde planar yansıma kapalı (AP-028 bütçe)");
                    planar++;
                }
            }

            // The menu over it: an overlay, the UI camera off, one listener; the cinematic camera never the main camera.
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);
            Canvas canvas = menu.GetComponentInChildren<Canvas>();
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.rootCanvas.renderMode, "kelimeler post-işleme girmez");
            Assert.IsTrue(UiFactory.OverlayScreens);
            GameObject uiCamera = GameObject.Find(UiFactory.UiCameraName);
            Assert.IsNotNull(uiCamera);
            Assert.IsFalse(uiCamera.GetComponent<Camera>().enabled, "UI kamerası dünyayı örtmez");
            int listeners = 0;
            foreach (AudioListener listener in Object.FindObjectsOfType<AudioListener>())
            {
                if (listener.isActiveAndEnabled) listeners++;
            }

            Assert.AreEqual(1, listeners, "tek ses dinleyicisi");
            Assert.IsNotNull(cinematic.Camera);
            Assert.AreNotEqual("MainCamera", cinematic.Camera.tag);
            Assert.IsTrue(cinematic.Camera.enabled);

            // The plates step aside; the scrim stays at 0.55.
            yield return new WaitForSecondsRealtime(MenuCinematic.Blend + 0.3f);
            UiBackdrop backdrop = menu.GetComponentInChildren<UiBackdrop>(true);
            Assert.IsNotNull(backdrop);
            Assert.IsTrue(backdrop.WorldShown);
            foreach (string band in UiBackdrop.WorldBands)
            {
                Transform t = backdrop.transform.Find(band);
                Assert.IsTrue(t == null || !t.gameObject.activeSelf, band + " çekildi");
            }

            // C-00 05.10 decision B + pads: the spine's scrim one wide soft profile, the small words on their own pads.
            MenuLook look = MenuLook.Current;
            Assert.IsTrue(look.GradientScrim && look.Pads, "shipped look: " + look);
            Assert.IsTrue(backdrop.HasWorldScrim, "the world scrim replaces the third's");
            Assert.IsFalse(backdrop.transform.Find("TextSafeScrim").gameObject.activeSelf);
            Assert.AreEqual(look.ScrimEdge, backdrop.WorldScrimAlphaAt(0f), 1e-3f);
            Assert.AreEqual(look.ScrimColumn, backdrop.WorldScrimAlphaAt(backdrop.WorldScrimColumn), 1e-3f);
            Assert.AreEqual(0f, backdrop.WorldScrimAlphaAt(look.ScrimEnd), 1e-3f);
            int padCount = 0;
            foreach (Image image in menu.GetComponentsInChildren<Image>(false))
            {
                if (image.name == MenuLook.ReadoutPadName)
                {
                    padCount++;
                    Assert.AreEqual(MenuLook.ReadoutPadAlpha, image.color.a, 1e-4f);
                }
            }

            Assert.AreEqual(Debug.isDebugBuild ? 3 : 2, padCount, "a pad behind the focused word, one behind the level readout (and one behind the dev build line)");

            // The clock runs; Reduced Motion and an open pane hold S01's start.
            float before = cinematic.Clock;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.Greater(cinematic.Clock, before, "döngü ilerler");
            UiMotion.ReducedMotion = true;
            yield return null;
            yield return null;
            Assert.IsTrue(cinematic.Still);
            Assert.AreEqual(0f, cinematic.Clock, 1e-4f);
            Assert.Less((cinematic.Camera.transform.position - MenuCinematic.Shots[0].Start).magnitude, 1e-3f, "hareketi azalt: S01 başlangıcı");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(0f, cinematic.Clock, 1e-4f, "hareketi azalt: sabit");
            UiMotion.ReducedMotion = false;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.Greater(cinematic.Clock, 0f);
            menu.ShowPane(UiContext.Settings);
            yield return null;
            yield return null;
            Assert.IsTrue(cinematic.Still, "panel açıkken durur");
            Assert.AreEqual(0f, cinematic.Clock, 1e-4f, "panel: S01 başlangıcı");
            menu.ShowPane(UiContext.MainMenu);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(cinematic.Still);
            Assert.Greater(cinematic.Clock, 0f, "panel kapanınca döngü S01'den sürer");

            // The camera's 0.35 m sphere stays clear of the world's colliders along every path.
            var hits = new List<string>();
            foreach (MenuCinematic.Shot shot in MenuCinematic.Shots)
            {
                for (int k = 0; k <= 60; k++)
                {
                    Vector3 p = Vector3.Lerp(shot.Start, shot.End, k / 60f);
                    foreach (Collider c in Physics.OverlapSphere(p, 0.35f, ~(1 << 5), QueryTriggerInteraction.Ignore))
                    {
                        string hit = shot.id + " " + c.name;
                        if (!hits.Contains(hit)) hits.Add(hit);
                    }
                }
            }

            Debug.Log("[Test] menu cinematic on " + worldScene + ": " + planar + " planar reflection(s) off, doors waited "
                + PrototypeUiFlow.LastMenuWorldWaitSeconds.ToString("F2") + " s, sphere hits: " + (hits.Count == 0 ? "none" : string.Join(", ", hits)));
            Assert.IsEmpty(hits, "kamera 0,35 m küresi dünyaya değmez");

            // Leaving: the world, the viewer flag and the overlays go with the menu.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            Assert.IsFalse(SceneManager.GetSceneByName(worldScene).isLoaded, "dünya menüyle gider");
            Assert.IsFalse(WorldViewer.Active);
            Assert.IsFalse(UiFactory.OverlayScreens);
            Assert.IsNull(MenuCinematic.Current);
        }
    }
}
