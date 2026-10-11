using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using TMPro;
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

            Assert.AreEqual(Debug.isDebugBuild ? 2 : 1, padCount, "a pad behind the focused word (and one behind the dev build line); the level readout stands on the corner lift");
            Assert.IsTrue(backdrop.HasWorldCorner(true), "AD MUST-1: the top-right corner's lift");
            Assert.IsTrue(backdrop.HasWorldCorner(false), "AD MUST-2: the top-left corner's lift");
            RectTransform progress = null;
            foreach (RectTransform rect in menu.GetComponentsInChildren<RectTransform>(false))
            {
                progress = rect.name == "Progress" && rect.parent != null && rect.parent.name == "Page" ? rect : progress;
            }

            Assert.IsNotNull(progress, "the level readout");
            Assert.IsNull(progress.Find(MenuLook.ReadoutPadName), "no pad behind the level readout");
            var corners = new Vector3[4];
            progress.GetWorldCorners(corners);
            RectTransform root = (RectTransform)backdrop.transform;
            foreach (Vector3 corner in corners)
            {
                Vector2 local = root.InverseTransformPoint(corner);
                float alpha = backdrop.WorldCornerAlphaAt(true, root.rect.xMax - local.x, root.rect.yMax - local.y);
                Assert.GreaterOrEqual(alpha, MenuLook.CornerAlpha - 1e-3f, "the readout's corner " + corner + " stands on the full lift");
            }
            RectTransform title = null;   // R4 WP-105: the wordmark + seal lockup
            foreach (RectTransform rect in menu.GetComponentsInChildren<RectTransform>(false))
            {
                title = rect.name == "Title" ? rect : title;
            }

            Assert.IsNotNull(title, "the menu's title");
            title.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = root.InverseTransformPoint(corner);
                float alpha = backdrop.WorldCornerAlphaAt(false, local.x - root.rect.xMin, root.rect.yMax - local.y);
                Assert.GreaterOrEqual(alpha, MenuLook.TitleLiftAlpha - 1e-3f, "the title's corner " + corner + " stands on the full lift");
            }

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
            // WP-098A (HAREKET-MENU §5-3): the world freezes Screen 280 after the pane opens, outside the pane's own motion.
            yield return null;
            Assert.IsFalse(cinematic.Still, "donma pane girişinden 280 ms sonra başlar");
            yield return new WaitForSecondsRealtime(UiMenuMotionTokens.FreezeDelay + 0.05f);
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

        /// <summary>
        /// R3 (C-00 07.10; gt MUST-1 07.10): the cut's held frame is the cut frame itself - for one frame the camera stays on the
        /// slice's end (where it came to rest, the world settled around it) and draws into the held target, which covers the
        /// screen at full weight; the frame after, the camera draws the screen again at the next shot's start under it. One world
        /// render per frame across the cut (b31: 85 ms at each cut when the end was rendered a second time there), the held
        /// target opaque (URP 14's post writes alpha 1: nothing under it shows), and it goes with the menu (none during a run).
        /// </summary>
        [UnityTest]
        public IEnumerator AnaMenu_Kesim_KendiKaresiniTutar_KareBasinaTekRender_KosudaSifir()
        {
            yield return BootTanMainMenuye();
            yield return Bekle(() => MenuCinematic.Current != null && MenuCinematic.Current.Live, 90f, "menü dünyası canlanmadı");
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            MenuCinematic cinematic = MenuCinematic.Current;
            Camera cam = cinematic.Camera;
            var renders = new Dictionary<int, int>();
            var targets = new Dictionary<int, RenderTexture>();
            var positions = new Dictionary<int, Vector3>();
            System.Action<UnityEngine.Rendering.ScriptableRenderContext, Camera> count = (context, c) =>
            {
                if (c != cam)
                {
                    return;
                }

                int f = Time.frameCount;
                renders[f] = renders.TryGetValue(f, out int n) ? n + 1 : 1;
                targets[f] = c.targetTexture;
                positions[f] = c.transform.position;
            };
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += count;
            var lines = new List<string>();
            try
            {
                for (int s = 0; s < 2; s++)   // two cuts: S01 → S02 and S02 → S03
                {
                    float cut = (s + 1) * MenuCinematic.Slice - MenuCinematic.Blend;
                    MenuCinematic.TimeOverride = cut - 0.5f;   // the move's tail: the camera comes to rest on the end
                    for (int i = 0; i < 3; i++)
                    {
                        yield return null;
                    }

                    int captures = cinematic.CutCaptures;
                    MenuCinematic.TimeOverride = cut + 0.001f;   // the cut
                    yield return null;                            // this frame's Update has set the capture up; it renders next
                    int cutFrame = Time.frameCount;
                    Assert.AreEqual(captures + 1, cinematic.CutCaptures, "kesim kendi karesini tuttu (" + s + ")");
                    RenderTexture held = cam.targetTexture;
                    Assert.IsNotNull(held, "kesim karesinde kamera tutulan hedefe çiziyor");
                    Assert.AreSame(held, cinematic.HeldTexture, "ekrandaki tutulan kare, kameranın bu kare çizdiği hedef");
                    Assert.AreEqual(1f, cinematic.HeldWeightNow, 1e-4f, "kesim karesinde tutulan kare tam ağırlıkta ekranı kaplar");

                    MenuCinematic.TimeOverride = cut + 0.02f;
                    yield return null;                            // the cut frame has rendered; this Update gave the camera back
                    Assert.IsNull(cam.targetTexture, "kesimden sonraki kare kamera ekrana çiziyor");
                    Assert.AreSame(held, cinematic.HeldTexture, "geçiş başında aynı tutulan kare");
                    Assert.Greater(cinematic.HeldWeightNow, 0.95f, "geçiş başında tutulan kare neredeyse tam");
                    yield return null;
                    yield return null;

                    Assert.IsTrue(renders.TryGetValue(cutFrame, out int atCut), "kesim karesinde render yok");
                    Assert.AreEqual(1, atCut, "kesim karesinde tek dünya render'ı");
                    Assert.AreSame(held, targets[cutFrame], "kesim karesinin render'ı tutulan hedefe");
                    Vector3 end = MenuCinematic.PoseOf(MenuCinematic.Shots[s], MenuCinematic.Slice).Position;
                    Assert.Less(Vector3.Distance(end, positions[cutFrame]), 1e-3f, "kesim karesi çekimin son pozunda");
                    float alphaMin = AlphaMin(held);
                    Assert.GreaterOrEqual(alphaMin, 0.99f, "tutulan kare opak: altında bir şey görünmez");
                    lines.Add(MenuCinematic.Shots[s].id + " cutFrame=" + cutFrame + " renders=" + atCut + " alphaMin=" + alphaMin.ToString("F3"));
                }

                foreach (KeyValuePair<int, int> r in renders)
                {
                    Assert.LessOrEqual(r.Value, 1, "karede birden fazla dünya render'ı (kare " + r.Key + ")");
                }
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= count;
                MenuCinematic.TimeOverride = null;
            }

            Debug.Log("[DD-TEST] wp081.cutCapture frames=" + renders.Count + " heldTargets=" + LiveHeldTargets() + " · " + string.Join(" · ", lines));
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return null;
            Assert.AreEqual(0, LiveHeldTargets(), "koşuda tutulan kare hedefi yok (0 MB)");
        }

        /// <summary>
        /// WP-098A (C-00 08.10; HAREKET-MENU §5, b31's ~85 ms pane-open frame): the freeze's held frame is the freeze frame itself, as
        /// Faz 0's cut - on the frame the still begins the camera stays where it stood and draws into the held target (one render of
        /// the cinematic camera, into the held target), the held image covers the screen at weight 1; the frame after, the camera
        /// draws the screen at S01's start under the fading held frame. One world render per frame across the open; the still
        /// begins Screen 280 after the pane (§5-3); the content is built at α 0 on the open's frame (§5-4).
        /// </summary>
        [UnityTest]
        public IEnumerator AnaMenu_Donma_KendiKaresiniTutar_KareBasinaTekRender_IcerikIkiAsamali()
        {
            yield return BootTanMainMenuye();
            yield return Bekle(() => MenuCinematic.Current != null && MenuCinematic.Current.Live, 90f, "menü dünyası canlanmadı");
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            MenuCinematic cinematic = MenuCinematic.Current;
            Camera cam = cinematic.Camera;
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);
            yield return new WaitForSecondsRealtime(0.6f);   // the loop moves past S01's start: the freeze has a view to hold
            Assert.Greater(cinematic.Clock, 0.1f, "döngü ilerliyor");
            Assert.IsFalse(cinematic.Still);

            var renders = new Dictionary<int, int>();
            var targets = new Dictionary<int, RenderTexture>();
            var positions = new Dictionary<int, Vector3>();
            System.Action<UnityEngine.Rendering.ScriptableRenderContext, Camera> count = (context, c) =>
            {
                if (c != cam)
                {
                    return;
                }

                int f = Time.frameCount;
                renders[f] = renders.TryGetValue(f, out int n) ? n + 1 : 1;
                targets[f] = c.targetTexture;
                positions[f] = c.transform.position;
            };
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += count;
            try
            {
                int freezes = cinematic.FreezeCaptures;
                menu.ShowPane(UiContext.Settings);
                Assert.AreEqual(0f, menu.PaneContentGroup.alpha, 1e-4f, "§5-4: içerik açılış karesinde α 0 kurulur");
                Assert.AreEqual(0f, menu.PaneCardGroup.alpha, 1e-4f, "frameless taşıyıcı da açılış karesinde görünmez");
                int openFrame = Time.frameCount;
                float openAt = Time.realtimeSinceStartup;
                // R4 WP-105: frame by frame up to the freeze - a fixed 2 frames + 0.12 s wait could step over the freeze
                // (Screen 280 after the pane) when one batch frame runs long, and then the freeze frame was never seen.
                float slowest = 0f;
                while (cinematic.FreezeCaptures == freezes)
                {
                    Assert.Less(Time.realtimeSinceStartup - openAt, 5f, "donma kendi karesini tutmadı");
                    yield return null;
                    slowest = Mathf.Max(slowest, Time.unscaledDeltaTime);
                }

                Assert.AreEqual(freezes + 1, cinematic.FreezeCaptures, "tek donma");
                Assert.Greater(Time.frameCount, openFrame + 1, "donma açılış karesinden sonra");
                Assert.Greater(menu.PaneContentGroup.alpha + menu.PaneCardGroup.alpha, 0f, "hareket sonraki karelerde başladı");
                int freezeFrame = Time.frameCount;   // this frame's Update set the capture up; it renders next
                Debug.Log("[DD-TEST] wp105.freezeWait ms=" + ((Time.realtimeSinceStartup - openAt) * 1000f).ToString("F0")
                          + " frames=" + (freezeFrame - openFrame) + " slowestMs=" + (slowest * 1000f).ToString("F0"));
                RenderTexture held = cam.targetTexture;
                Vector3 standing = cam.transform.position;
                Assert.IsNotNull(held, "donma karesinde kamera tutulan hedefe çiziyor");
                Assert.AreSame(held, cinematic.HeldTexture, "ekrandaki tutulan kare, kameranın bu kare çizdiği hedef");
                Assert.AreEqual(1f, cinematic.HeldWeightNow, 1e-4f, "donma karesinde tutulan kare tam ağırlıkta ekranı kaplar");
                Assert.IsTrue(cinematic.Still);

                yield return null;   // the freeze frame has rendered; this Update gave the camera back at S01's start
                Assert.IsNull(cam.targetTexture, "donmadan sonraki kare kamera ekrana çiziyor");
                Assert.AreSame(held, cinematic.HeldTexture, "aynı tutulan kare solar");
                Assert.Greater(cinematic.HeldWeightNow, 0.9f, "geçiş başında tutulan kare neredeyse tam");
                Assert.Less((cam.transform.position - MenuCinematic.Shots[0].Start).magnitude, 1e-3f, "kamera S01 başlangıcında");
                yield return null;
                yield return null;

                Assert.IsTrue(renders.TryGetValue(freezeFrame, out int atFreeze), "donma karesinde render yok");
                Assert.AreEqual(1, atFreeze, "donma karesinde tek dünya render'ı");
                Assert.AreSame(held, targets[freezeFrame], "donma karesinin render'ı tutulan hedefe");
                Assert.Less(Vector3.Distance(standing, positions[freezeFrame]), 1e-3f, "donma karesi kameranın durduğu yerden");
                Assert.GreaterOrEqual(AlphaMin(held), 0.99f, "tutulan kare opak");
                foreach (KeyValuePair<int, int> r in renders)
                {
                    Assert.LessOrEqual(r.Value, 1, "karede birden fazla dünya render'ı (kare " + r.Key + ")");
                }

                Debug.Log("[DD-TEST] wp098.freezeCapture openFrame=" + openFrame + " freezeFrame=" + freezeFrame + " frames=" + renders.Count
                          + " freezeCaptures=" + cinematic.FreezeCaptures);
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= count;
            }

            // Reduced Motion stays a cut: no capture, S01's start at once.
            menu.ShowPane(UiContext.MainMenu);
            yield return new WaitForSecondsRealtime(0.5f);
            int before = cinematic.FreezeCaptures;
            UiMotion.ReducedMotion = true;
            yield return null;
            yield return null;
            Assert.AreEqual(before, cinematic.FreezeCaptures, "hareketi azalt: kesme, yakalama yok");
            Assert.Less((cinematic.Camera.transform.position - MenuCinematic.Shots[0].Start).magnitude, 1e-3f);
        }

        /// <summary>
        /// WP-098A (HAREKET-MENU M14 / §5-5): PLAY → Selection under the brush curtain - ScreenExiting at t0, the brush covers (the
        /// M3 wipe), the cinematic camera stops drawing while the cover is whole (UiCurtain.Shut), never more than one world
        /// render a frame across the transition, and the Selection screen opens under the brush's erase.
        /// </summary>
        [UnityTest]
        public IEnumerator AnaMenu_FircaGecisi_KareBasinaTekDunyaRenderi_KapaliykenCizimYok()
        {
            yield return BootTanMainMenuye();
            yield return Bekle(() => MenuCinematic.Current != null && MenuCinematic.Current.Live, 90f, "menü dünyası canlanmadı");
            yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
            Camera cam = MenuCinematic.Current.Camera;
            var renders = new Dictionary<int, int>();
            System.Action<UnityEngine.Rendering.ScriptableRenderContext, Camera> count = (context, c) =>
            {
                if (c == cam)
                {
                    int f = Time.frameCount;
                    renders[f] = renders.TryGetValue(f, out int n) ? n + 1 : 1;
                }
            };

            bool fast = PrototypeUiFlow.FastTransitions;
            bool sawShut = false, sawCameraOff = false, sawBrush = false, sawExiting = false;
            System.Action<UiEvent, string> events = (e, d) => sawExiting |= e == UiEvent.ScreenExiting;
            UiEvents.Raised += events;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += count;
            try
            {
                PrototypeUiFlow.FastTransitions = false;
                PrototypeUiFlow.GoToSelection();
                Assert.IsTrue(sawExiting, "ScreenExiting t0'da");
                UiCurtain curtain = UiCurtain.Instance;
                float deadline = Time.realtimeSinceStartup + 60f;
                while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering) && Time.realtimeSinceStartup < deadline)
                {
                    sawBrush |= curtain.Style == UiCurtainStyle.Brush && curtain.BrushVisible && !curtain.DoorsShut;
                    if (UiCurtain.Shut)
                    {
                        sawShut = true;
                        if (cam != null)
                        {
                            sawCameraOff |= !cam.enabled;
                        }
                    }

                    yield return null;
                }
            }
            finally
            {
                PrototypeUiFlow.FastTransitions = fast;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= count;
                UiEvents.Raised -= events;
            }

            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
            Assert.IsTrue(sawBrush, "fırça perde örttü (kapılar değil)");
            Assert.IsTrue(sawShut, "örtü tamamlandı");
            Assert.IsTrue(sawCameraOff, "§5-5: örtü tamken sinematik kamera çizmez");
            foreach (KeyValuePair<int, int> r in renders)
            {
                Assert.LessOrEqual(r.Value, 1, "geçişte karede birden fazla dünya render'ı (kare " + r.Key + ")");
            }

            Debug.Log("[DD-TEST] wp098.brushTransition frames=" + renders.Count + " shut=" + sawShut + " cameraOff=" + sawCameraOff);
        }

        /// <summary>The held frame targets alive right now (<see cref="MenuCinematic.HeldName"/>).</summary>
        private static int LiveHeldTargets()
        {
            int n = 0;
            foreach (RenderTexture rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (rt != null && rt.name == MenuCinematic.HeldName)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>The lowest alpha over a 32 px grid of <paramref name="source"/> (MSAA resolved through a plain copy).</summary>
        private static float AlphaMin(RenderTexture source)
        {
            var copy = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var read = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, copy);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = copy;
                read.ReadPixels(new Rect(0, 0, copy.width, copy.height), 0, 0);
                read.Apply(false);
                RenderTexture.active = previous;
                Color32[] pixels = read.GetPixels32();
                byte least = 255;
                for (int y = 0; y < copy.height; y += 32)
                {
                    for (int x = 0; x < copy.width; x += 32)
                    {
                        byte a = pixels[y * copy.width + x].a;
                        least = a < least ? a : least;
                    }
                }

                return least / 255f;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(copy);
                Object.Destroy(read);
            }
        }

        /// <summary>
        /// S-05 06.10 (S05-MENU-ISIK-MALIYET §16): the world loads additively under the menu and MainMenu stays the active scene;
        /// LightmapSettings.lightProbes stays null there (b19-b21: Tetrahedralize, sync or async, leaves it so), so every
        /// probe-lit renderer - no lightmap, probes on (b26c: the ground, the plaza slate, the hall floor, the ponds) - samples
        /// the menu's ambient SH, not the world's probes. Measured before any fix (S-05: no SetActiveScene - it swaps the
        /// AD-approved RenderSettings): per shot, the probe-lit renderers in the frustum and the share of the camera's own frame
        /// they change, rendered with them and without (an upper bound: their shadows count too). Only pixels that hold still
        /// over three renders of the same pose count (b26c: with post-processing off a frame still moved 1-12 % between two
        /// renders, S01 and S04 most); the stable share must reach 75 %. Hiding the whole world is the positive control. The
        /// probe state and the retetrahedralization events are logged for S-05.
        /// </summary>
        [UnityTest]
        public IEnumerator MenuDunyasi_ProbIsikliRendererPayi_BesCekim_Olcum()
        {
            int needs = 0, done = 0;
            System.Action onNeeds = () => needs++;
            System.Action onDone = () => done++;
            System.Reflection.EventInfo needsEvent = typeof(LightProbes).GetEvent("needsRetetrahedralization");
            System.Reflection.EventInfo doneEvent = typeof(LightProbes).GetEvent("tetrahedralizationCompleted");
            needsEvent?.AddEventHandler(null, onNeeds);
            doneEvent?.AddEventHandler(null, onDone);
            RenderTexture rt = null;
            Texture2D read = null;
            var hidden = new List<Renderer>();
            try
            {
                yield return BootTanMainMenuye();
                yield return Bekle(() => MenuCinematic.Current != null && MenuCinematic.Current.Live, 90f, "menü dünyası canlanmadı");
                yield return Bekle(() => !UiCurtain.IsCovering, 30f, "perde açılmadı");
                MenuCinematic cinematic = MenuCinematic.Current;
                WorldSourceSettings.Resolve(out string worldScene);
                Scene world = SceneManager.GetSceneByName(worldScene);
                Assert.IsTrue(world.isLoaded);
                string withWorld = ProbeState();

                var worldRenderers = new List<Renderer>();
                var probeLit = new List<Renderer>();
                foreach (GameObject root in world.GetRootGameObjects())
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!r.enabled)
                        {
                            continue;
                        }

                        worldRenderers.Add(r);
                        bool lightmapped = r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE;
                        if (!lightmapped && r.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off)
                        {
                            probeLit.Add(r);
                        }
                    }
                }

                const int W = 960, H = 540;
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                read = new Texture2D(W, H, TextureFormat.RGBA32, false);
                Camera cam = cinematic.Camera;
                var lines = new List<string>();
                float worst = 0f, control = -1f, noiseMax = 0f;
                // Coverage, not the grade: post-processing off while measuring (b26: LookV3Post's grain changed 1-16 % of a
                // frame between two renders of the same pose), back on after; what still moves (b26c) is masked out.
                bool post = SetPostProcessing(cam, false);
                var settled = new List<string>();
                for (int i = 0; i < MenuCinematic.Shots.Length; i++)
                {
                    // gt MUST-2 (07.10): settle on the pose first. The cinematic parks there (TimeOverride), the world streams
                    // its mips (>= 2 frames, then no load pending - ArtVisualRegression's rule) and the LOD cross-fades finish;
                    // the first renders after a jump were what moved between the three (S04 41 %).
                    MenuCinematic.TimeOverride = i * MenuCinematic.Slice + MenuCinematic.Hold + MenuCinematic.Move * 0.5f;
                    float settleFrom = Time.unscaledTime;
                    int settleFrames = Time.frameCount;
                    yield return Settle();
                    settled.Add(MenuCinematic.Shots[i].id + " " + (Time.frameCount - settleFrames) + "f/" + (Time.unscaledTime - settleFrom).ToString("F2") + "s");

                    // Mid-move of the shot, rendered at once into a 16:9 target (Update cannot move the camera in between).
                    cam.targetTexture = rt;
                    MenuCinematic.Place(cam, MenuCinematic.PoseOf(MenuCinematic.Shots[i], MenuCinematic.Hold + MenuCinematic.Move * 0.5f));
                    Color32[] a = Render(cam, rt, read);
                    bool[] stable = Stable(a, Render(cam, rt, read), Render(cam, rt, read));
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    var inView = new List<Renderer>();
                    foreach (Renderer r in probeLit)
                    {
                        if (GeometryUtility.TestPlanesAABB(planes, r.bounds))
                        {
                            inView.Add(r);
                        }
                    }

                    Hide(probeLit, hidden);
                    Color32[] without = Render(cam, rt, read);
                    Show(hidden);
                    float noise = 1f - Fraction(stable);
                    float share = Changed(a, without, stable);
                    if (i == 0)
                    {
                        Hide(worldRenderers, hidden);
                        control = Changed(a, Render(cam, rt, read), stable);
                        Show(hidden);
                    }

                    // Each one alone, the largest first: which renderers the share is.
                    var each = new List<KeyValuePair<float, string>>();
                    foreach (Renderer r in inView)
                    {
                        Hide(new List<Renderer> { r }, hidden);
                        float own = Changed(a, Render(cam, rt, read), stable);
                        Show(hidden);
                        Material m = r.sharedMaterial;
                        each.Add(new KeyValuePair<float, string>(own, r.name + "[" + (m != null && m.shader != null ? m.shader.name : "-") + "] " + (own * 100f).ToString("F2") + "%"));
                    }

                    each.Sort((x, y) => y.Key.CompareTo(x.Key));
                    var top = new List<string>();
                    for (int k = 0; k < Mathf.Min(5, each.Count); k++)
                    {
                        top.Add(each[k].Value);
                    }

                    cam.targetTexture = null;
                    worst = Mathf.Max(worst, share);
                    noiseMax = Mathf.Max(noiseMax, noise);
                    lines.Add(MenuCinematic.Shots[i].id + " inView=" + inView.Count + " share=" + (share * 100f).ToString("F2") + "% noise=" + (noise * 100f).ToString("F3") + "% top: " + string.Join(", ", top));
                }

                SetPostProcessing(cam, post);
                MenuCinematic.TimeOverride = null;
                Debug.Log("[DD-TEST] wp081.menuProbes.settle " + string.Join(" · ", settled));
                var kinds = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
                foreach (Renderer r in probeLit)
                {
                    Material m = r.sharedMaterial;
                    string kind = r.GetType().Name + " " + (m != null && m.shader != null ? m.shader.name : "-") + " " + r.lightProbeUsage;
                    kinds[kind] = kinds.TryGetValue(kind, out int c) ? c + 1 : 1;
                }

                var kindList = new List<string>();
                foreach (KeyValuePair<string, int> k in kinds)
                {
                    kindList.Add(k.Value + "× " + k.Key);
                }

                Debug.Log("[DD-TEST] wp081.menuProbes.kinds " + string.Join(" · ", kindList));

                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                yield return null;
                string withoutWorld = ProbeState();
                Debug.Log("[DD-TEST] wp081.menuProbes world=" + worldScene + " renderers=" + worldRenderers.Count + " probeLit=" + probeLit.Count
                    + " | probes with world: " + withWorld + " | after it left: " + withoutWorld
                    + " | events needsRetetrahedralization=" + (needsEvent == null ? "n/a" : needs.ToString())
                    + " tetrahedralizationCompleted=" + (doneEvent == null ? "n/a" : done.ToString())
                    + " | control (world hidden) " + (control * 100f).ToString("F1") + "% | " + string.Join(" · ", lines)
                    + " | worst " + (worst * 100f).ToString("F2") + "%");
                Assert.Greater(control, 0.3f, "pozitif kontrol: dünya gizlenince karenin çoğu değişir (ölçüm kör değil)");
                Assert.Less(noiseMax, 0.25f, "aynı poz üç kez: piksellerin en az %75'i kararlı (ölçüm geçerli)");
            }
            finally
            {
                Show(hidden);
                needsEvent?.RemoveEventHandler(null, onNeeds);
                doneEvent?.RemoveEventHandler(null, onDone);
                if (MenuCinematic.Current != null && MenuCinematic.Current.Camera != null)
                {
                    MenuCinematic.Current.Camera.targetTexture = null;
                }

                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }

                if (read != null)
                {
                    Object.Destroy(read);
                }
            }
        }

        private static string ProbeState()
        {
            LightProbes probes = LightmapSettings.lightProbes;
            string state = probes == null ? "lightProbes=null" : "lightProbes count=" + probes.count + " cells=" + probes.cellCount;
            System.Reflection.MethodInfo getCount = typeof(LightProbes).GetMethod("GetCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (getCount != null && getCount.GetParameters().Length == 1 && getCount.GetParameters()[0].IsOut)
            {
                object[] args = { 0 };
                getCount.Invoke(null, args);
                state += " GetCount=" + args[0];
            }

            return state + " active=" + SceneManager.GetActiveScene().name;
        }

        /// <summary>URP's per-camera post-processing switch, by reflection (the test assembly does not reference URP). Returns the old value.</summary>
        private static bool SetPostProcessing(Camera cam, bool on)
        {
            foreach (Component c in cam.GetComponents<Component>())
            {
                System.Reflection.PropertyInfo p = c != null ? c.GetType().GetProperty("renderPostProcessing") : null;
                if (p != null && p.PropertyType == typeof(bool) && p.CanWrite)
                {
                    bool was = (bool)p.GetValue(c);
                    p.SetValue(c, on);
                    return was;
                }
            }

            Assert.Fail("UniversalAdditionalCameraData.renderPostProcessing bulunamadı");
            return on;
        }

        /// <summary>
        /// gt MUST-2 (07.10): >= 2 frames, then until no mip load is pending, and at least the LOD cross-fade's length + 0.1 s in
        /// both clocks (the batch runner may step game time at a fixed rate); at most 10 s.
        /// </summary>
        private static IEnumerator Settle()
        {
            float started = Time.unscaledTime;
            float least = LODGroup.crossFadeAnimationDuration + 0.1f;
            int minFrames = Mathf.Max(2, Mathf.CeilToInt(least * 60f));
            int frames = 0;
            while (Time.unscaledTime - started < 10f)
            {
                yield return null;
                frames++;
                if (frames >= minFrames && Time.unscaledTime - started >= least &&
                    Texture.streamingTexturePendingLoadCount == 0 && Texture.streamingTextureLoadingCount == 0)
                {
                    yield break;
                }
            }
        }

        private static Color32[] Render(Camera cam, RenderTexture rt, Texture2D read)
        {
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            read.Apply(false);
            RenderTexture.active = previous;
            return read.GetPixels32();
        }

        private static void Hide(List<Renderer> renderers, List<Renderer> hidden)
        {
            foreach (Renderer r in renderers)
            {
                if (r != null && !r.forceRenderingOff)
                {
                    r.forceRenderingOff = true;
                    hidden.Add(r);
                }
            }
        }

        private static void Show(List<Renderer> hidden)
        {
            foreach (Renderer r in hidden)
            {
                if (r != null)
                {
                    r.forceRenderingOff = false;
                }
            }

            hidden.Clear();
        }

        /// <summary>True where two colours differ by more than 2/255 in any channel.</summary>
        private static bool Differs(Color32 a, Color32 b)
        {
            return Mathf.Abs(a.r - b.r) > 2 || Mathf.Abs(a.g - b.g) > 2 || Mathf.Abs(a.b - b.b) > 2;
        }

        /// <summary>The pixels that hold still over three renders of the same pose.</summary>
        private static bool[] Stable(Color32[] a, Color32[] b, Color32[] c)
        {
            var stable = new bool[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                stable[i] = !Differs(a[i], b[i]) && !Differs(a[i], c[i]);
            }

            return stable;
        }

        private static float Fraction(bool[] mask)
        {
            int n = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i]) n++;
            }

            return n / (float)mask.Length;
        }

        /// <summary>The share of the frame's pixels, stable ones only, whose colour moves by more than 2/255 in any channel.</summary>
        private static float Changed(Color32[] a, Color32[] b, bool[] stable)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (stable[i] && Differs(a[i], b[i]))
                {
                    n++;
                }
            }

            return n / (float)a.Length;
        }
    }
}
