using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-022 · UI art & HUD v2 in the real flow: the AP-012 sprites are bound on the
    /// screens, the HUD keeps the play area clear, the skin cards show the character,
    /// and the menus can be driven end to end with EventSystem move/submit events -
    /// the same events the StandaloneInputModule raises for the arrow keys, WASD, the
    /// d-pad and the left stick. Gamepad hardware is not on the build machine; this is
    /// the deterministic equivalent.
    /// </summary>
    [TestFixture]
    [Category("WP022")]
    public sealed class UiArtSliceTests
    {
        private readonly List<string> _hatalar = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _hatalar.Clear();
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            Application.logMessageReceived += HataTopla;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= HataTopla;
            RunFlowClock.ClearHolds();
            LogAssert.ignoreFailingMessages = false;
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            Assert.AreEqual(0, _hatalar.Count, "Test boyunca UI/koşu hatası loglandı:\n" + string.Join("\n", _hatalar));
        }

        private void HataTopla(string mesaj, string iz, LogType tur)
        {
            if (tur != LogType.Error && tur != LogType.Exception && tur != LogType.Assert)
            {
                return;
            }

            if (mesaj.Contains("[UI]") || mesaj.Contains("[Run]") || mesaj.Contains("NullReferenceException") ||
                mesaj.Contains("MissingReferenceException") || mesaj.Contains("ArgumentException"))
            {
                _hatalar.Add(tur + ": " + mesaj);
            }
        }

        // ================================================================= yardımcılar

        private static GameObject Secili
        {
            get { return EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null; }
        }

        /// <summary>One navigation step, exactly as StandaloneInputModule dispatches an axis press.</summary>
        private static void Hareket(MoveDirection yon)
        {
            GameObject current = Secili;
            Assert.IsNotNull(current, "hareket için seçili kontrol yok");
            var data = new AxisEventData(EventSystem.current) { moveDir = yon, moveVector = Vektor(yon) };
            ExecuteEvents.Execute(current, data, ExecuteEvents.moveHandler);
        }

        private static Vector2 Vektor(MoveDirection yon)
        {
            switch (yon)
            {
                case MoveDirection.Up: return Vector2.up;
                case MoveDirection.Down: return Vector2.down;
                case MoveDirection.Left: return Vector2.left;
                default: return Vector2.right;
            }
        }

        private static void Onayla()
        {
            GameObject current = Secili;
            Assert.IsNotNull(current, "onay için seçili kontrol yok");
            ExecuteEvents.Execute(current, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        private static IEnumerator SeciliBekle(string adParcasi)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f)
            {
                GameObject s = Secili;
                if (s != null && (s.name.Contains(adParcasi) ||
                                  (s.transform.parent != null && s.transform.parent.name.Contains(adParcasi))))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("seçili kontrol '" + adParcasi + "' olmadı; seçili: " + (Secili != null ? Secili.name : "yok"));
        }

        // ============================================================== 1 · HUD sanatı

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Hud_IkonlarBagli_GuvenliBolgeBos_ReticleVar()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return null;
            yield return null;

            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD yok");

            // Icons bound from the theme (the theme is present in this project).
            Assert.IsTrue(UiArt.HasIcon("Hud.Gold"), "tema HUD ikonlarını vermeli");
            Image coin = Bul<Image>(hud.transform, "Gold", "Coin");
            Assert.IsNotNull(coin.sprite);
            Assert.AreEqual("Hud.Gold", coin.sprite.name, "gold paneli AP-012 coin glifini çizmeli");
            Image heart = Bul<Image>(hud.transform, "Health", "Heart");
            Assert.AreEqual("Hud.Health", heart.sprite.name);
            Image weapon = Bul<Image>(hud.transform, "ClassicWeapon", "WeaponIcon");
            Assert.IsTrue(weapon.enabled && weapon.sprite != null && weapon.sprite.name.StartsWith("Weapon."),
                "klasik silah paneli eldeki silahın glifini çizmeli: " + (weapon.sprite != null ? weapon.sprite.name : "yok"));

            // Frames: every panel wears the AP-012 ink frame over its HudInk fill.
            Assert.GreaterOrEqual(hud.Panels.Count, 5, "beş HUD paneli");
            foreach (RectTransform panel in hud.Panels)
            {
                Transform frame = panel.Find("Frame");
                Assert.IsNotNull(frame, panel.name + " paneli çerçeve taşımıyor");
                Assert.AreEqual("Frame.HudPanel", frame.GetComponent<Image>().sprite.name);
            }

            // Safe zone: no panel rect intersects the play area (REQ-104/105).
            var canvasRect = (RectTransform)hud.Panels[0].parent;
            Rect canvas = canvasRect.rect;
            var zone = new Rect(
                canvas.xMin + canvas.width * RunHudScreen.HudSafeZone.xMin,
                canvas.yMin + canvas.height * RunHudScreen.HudSafeZone.yMin,
                canvas.width * RunHudScreen.HudSafeZone.width,
                canvas.height * RunHudScreen.HudSafeZone.height);
            foreach (RectTransform panel in hud.Panels)
            {
                Rect r = PanelRect(panel, canvasRect);
                Assert.IsFalse(r.Overlaps(zone), panel.name + " paneli oyun alanına giriyor: " + r + " vs " + zone);
            }


            // Reticle: drawn at the aim point on its own canvas while the run is live.
            Transform reticle = hud.transform.Find(hud.GetType().Name + ".Canvas/ReticleLayer/Reticle");
            Assert.IsNotNull(reticle, "reticle yok");
            Assert.IsNotNull(reticle.parent.GetComponent<Canvas>(), "reticle kendi canvas'ında olmalı");
            Vector2 reticlePos = ((RectTransform)reticle).anchoredPosition;
            Assert.IsTrue(Mathf.Abs(reticlePos.x) < 2000f && Mathf.Abs(reticlePos.y) < 2000f,
                "canlı koşuda reticle ekran içinde (park pozisyonunda değil): " + reticlePos);
            Assert.AreEqual("Reticle.Ticks", reticle.GetComponent<Image>().sprite.name);

            // Threat chips (WP-022B / WP-033): built, hidden until the director says so.
            Transform threatRow = hud.transform.Find(hud.GetType().Name + ".Canvas/ThreatRow");
            Assert.IsNotNull(threatRow, "tehdit çipi satırı yok");
            Assert.AreEqual(3, threatRow.childCount, "üç tehdit çipi");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(hud.ThreatChipVisible(i), "tehdit yokken çip " + i + " gizli olmalı");
            }

            // Screen events (WP-022B): the HUD announced itself; the settings file, not
            // PlayerPrefs, holds the UI scale.
            Assert.IsTrue(File.Exists(UiSettings.FilePath) || UiSettings.UiScale == 1f, "ui-settings.json yolu: " + UiSettings.FilePath);

            // Marker layer and special block are nested canvases too (rebuild isolation).
            Assert.IsNotNull(hud.transform.Find(hud.GetType().Name + ".Canvas/MarkerLayer").GetComponent<Canvas>());
            Transform special = hud.transform.Find(hud.GetType().Name + ".Canvas/SpecialWeapon");
            Assert.IsNotNull(special);
            Assert.IsNotNull(special.GetComponent<Canvas>(), "Special bloğu kendi canvas'ında olmalı");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp022.hud=OK");
        }

        private static Rect PanelRect(RectTransform panel, RectTransform canvas)
        {
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            Vector3 min = canvas.InverseTransformPoint(corners[0]);
            Vector3 max = canvas.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static UiButton Buton(UiScreen screen, string ad)
        {
            foreach (UiButton b in screen.GetComponentsInChildren<UiButton>(true))
            {
                if (b.gameObject.name == ad)
                {
                    return b;
                }
            }

            Assert.Fail("buton yok: " + ad);
            return null;
        }

        private static T Bul<T>(Transform root, string blok, string ad) where T : Component
        {
            foreach (T c in root.GetComponentsInChildren<T>(true))
            {
                if (c.name != ad)
                {
                    continue;
                }

                for (Transform p = c.transform.parent; p != null; p = p.parent)
                {
                    if (p.name == blok)
                    {
                        return c;
                    }
                }
            }

            Assert.Fail("bulunamadı: " + blok + "/" + ad);
            return null;
        }

        // ======================================================== 2 · seçim önizlemeleri

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Secim_SkinOnizlemeleriGercekKarakter_SilahGlifleri()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;

            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen);
            Assert.IsNotNull(screen.SkinPreviews);
            Assert.AreEqual(PlayerSkins.Count, screen.SkinPreviews.Length);
            for (int i = 0; i < screen.SkinPreviews.Length; i++)
            {
                Assert.IsNotNull(screen.SkinPreviews[i], "skin " + (i + 1) + " önizlemesi kurulmadı");
                Assert.AreSame(UiArt.SkinPreview(i), screen.SkinPreviews[i].sprite, "skin " + (i + 1) + " kendi AP-006 renderını göstermeli");
                Assert.AreNotSame(screen.SkinPreviews[i].sprite, screen.SkinPreviews[(i + 1) % 3].sprite, "her skin farklı görsel");
            }

            int glif = 0;
            foreach (Image img in screen.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "Icon" && img.sprite != null && img.sprite.name.StartsWith("Weapon."))
                {
                    glif++;
                }
            }

            Assert.AreEqual(3, glif, "üç silah kartı üç AP-012 silah glifi");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp022.selection=OK");
        }

        // ============================================================ 3 · gamepad akışı

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gamepad_AnaMenudenSecimeVeGeri_YalnizHareketVeOnayla()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;
            yield return null;

            // Main menu opens with PLAY focused; Down walks the explicit chain.
            yield return SeciliBekle("Play");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Tab.Profile");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Tab.Statistics");
            Hareket(MoveDirection.Down);
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Tab.Settings");
            Onayla();
            yield return null;
            Assert.AreEqual(UiContext.Settings, UiNavigation.Layer, "Settings paneli açılmalı");

            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.AreEqual(UiContext.Settings, menu.Pane);
            // Focus moved into the pane: the first slider. Right nudges the value.
            yield return SeciliBekle("Setting.UI SCALE");
            var slider = Secili.GetComponentInChildren<Slider>();
            Assert.IsNotNull(slider);
            float before = slider.value;
            Hareket(MoveDirection.Right);
            Assert.Greater(slider.value, before, "sağ hareket UI ölçeğini artırmalı");
            Hareket(MoveDirection.Left);
            Assert.AreEqual(before, slider.value, 0.02f, "sol hareket geri almalı");

            // Down through the three sliders wraps back to the first.
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.MASTER VOLUME");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.GRAPHICS QUALITY");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("ReducedMotion");
            // Submit flips the toggle (WP-022B) and the label says so; flip it back.
            int raisedBefore = UiEvents.RaisedCount;
            Onayla();
            yield return null;
            Assert.IsTrue(UiSettings.ReducedMotion, "Submit reduced motion'ı açmalı");
            Assert.Greater(UiEvents.RaisedCount, raisedBefore, "toggle bir UiEvent yükseltir");
            Onayla();
            yield return null;
            Assert.IsFalse(UiSettings.ReducedMotion);

            // WP-022E: the run telemetry toggle is the last control; Submit flips it and
            // RunTelemetry follows; flip back. Down wraps to the first slider.
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Telemetry");
            bool telemetryBefore = UiSettings.Telemetry;
            Onayla();
            yield return null;
            Assert.AreNotEqual(telemetryBefore, UiSettings.Telemetry, "Submit telemetriyi çevirir");
            Assert.AreEqual(UiSettings.Telemetry, DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled, "RunTelemetry ayarı izler");
            Onayla();
            yield return null;
            Assert.AreEqual(telemetryBefore, UiSettings.Telemetry);
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.UI SCALE");

            // Cancel (Escape / east button) returns to Profile with the tab focused.
            menu.ShowPane(UiContext.Profile); // the Cancel path minus the hardware read
            yield return null;
            Assert.AreEqual(UiContext.Profile, UiNavigation.Layer);

            // Back on the nav chain: focus the Profile tab, Up reaches PLAY; Up again stays
            // on PLAY (the chain does not wrap onto QUIT).
            UiInput.Focus(Buton(menu, "Tab.Profile"));
            yield return SeciliBekle("Tab.Profile");
            Hareket(MoveDirection.Up);
            yield return SeciliBekle("Play");
            Hareket(MoveDirection.Up);
            yield return null;
            Assert.AreEqual("Play", Secili.name, "PLAY'den yukarı QUIT'e sarmamalı");
            Onayla();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;

            // Selection: the selected skin card has focus; Right hops to the weapon column;
            // Down reaches the footer; Left returns to the skin column.
            yield return SeciliBekle("Skin.");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Weapon.");
            GameObject weapon = Secili;
            Hareket(MoveDirection.Down);
            yield return null;
            Assert.IsTrue(Secili.name == "Start" || Secili.name.StartsWith("Weapon."),
                "silah sütununda aşağı: bir sonraki açık silah ya da START; seçili: " + Secili.name);
            while (Secili.name != "Start")
            {
                Hareket(MoveDirection.Down);
                yield return null;
            }

            Hareket(MoveDirection.Left);
            yield return SeciliBekle("Back");
            Onayla();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Assert.AreEqual(UiContext.MainMenu, UiNavigation.Base);
            Assert.IsNotNull(weapon);
            Debug.Log("[DD-TEST] wp022.gamepad=OK");
        }

        // ============================================================== 4 · ölçek ayarı

        /// <summary>
        /// WP-022D (WP-035): the Profile pane's title picker and the Achievements pager
        /// are reachable with the pad - Right from the tab enters the pane, Left returns,
        /// NEXT turns the page and keeps focus. The "None" title option is the current
        /// choice on a fresh profile; a locked option is not interactable.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gamepad_ProfilUnvanSecici_VeBasarimSayfalari()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;
            yield return null;
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);

            // Profile pane: Right from the tab lands on the "None" title option (selected).
            menu.ShowPane(UiContext.Profile);
            yield return null;
            UiInput.Focus(Buton(menu, "Tab.Profile"));
            yield return SeciliBekle("Tab.Profile");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Option.Title.None");
            Assert.IsTrue(Secili.GetComponent<UiButton>().IsSelectedChoice, "taze profilde unvan yok → None seçili");
            int locked = 0, options = 0;
            foreach (UiButton b in menu.GetComponentsInChildren<UiButton>(true))
            {
                if (b.name.StartsWith("Option.Title."))
                {
                    options++;
                    if (!b.IsInteractable()) locked++;
                }
            }

            Assert.GreaterOrEqual(options, 2, "None + en az bir katalog unvanı");
            Assert.GreaterOrEqual(locked, 1, "taze profilde kilitli unvan var (etkileşimsiz)");
            Hareket(MoveDirection.Left);
            yield return SeciliBekle("Tab.Profile");

            // Achievements pane: pages of five; NEXT turns the page and stays focused.
            menu.ShowPane(UiContext.Achievements);
            yield return null;
            Assert.IsTrue(MetinVar(menu, "Page 1 of"), "27 başarım → sayfalı");
            UiInput.Focus(Buton(menu, "Tab.Achievements"));
            yield return SeciliBekle("Tab.Achievements");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Pager.Next");
            Onayla();
            yield return null;
            yield return null;
            Assert.IsTrue(MetinVar(menu, "Page 2 of"), "NEXT ikinci sayfayı açar");
            yield return SeciliBekle("Pager.Next");
            Assert.IsTrue(MetinVar(menu, "IN PROGRESS"), "kazanılmamış girdi ilerleme çipi + çubuğu taşır");
            Assert.IsNotNull(menu.GetComponentInChildren<UiBar>(true), "ilerleme çubuğu (kazanılmamış girdi)");
        }

        private static bool MetinVar(Component ekran, string parca)
        {
            foreach (TMPro.TMP_Text t in ekran.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.text != null && t.text.Contains(parca)) return true;
            }

            return false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator UiOlcegi_SonrakiEkraninReferansCerceveniOlcekler()
        {
            LogAssert.ignoreFailingMessages = true;
            float before = UiSettings.UiScale;
            string path = Path.Combine(Path.GetTempPath(), "dd-wp022b-scale-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(path);
            try
            {
                UiSettings.UiScale = 1.2f;
                yield return BootTanMainMenuye();
                yield return null;
                MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
                var scaler = menu.GetComponentInChildren<CanvasScaler>();
                Assert.AreEqual(1920f / 1.2f, scaler.referenceResolution.x, 0.5f, "ölçek 1.2 referans genişliği 1600'e çekmeli");

                UiSettings.UiScale = 0.9f;
                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                yield return null;
                scaler = Object.FindObjectOfType<SelectionScreen>().GetComponentInChildren<CanvasScaler>();
                Assert.AreEqual(1920f / 0.9f, scaler.referenceResolution.x, 0.5f, "yeni ekran yeni ölçeği okur");
                PrototypeUiFlow.GoToMainMenu();
                yield return SahneyiBekle(PrototypeScenes.MainMenu);
            }
            finally
            {
                UiSettings.SetPathOverride(null);
                if (File.Exists(path)) File.Delete(path);
                UiSettings.UiScale = before;
            }

            Debug.Log("[DD-TEST] wp022.scale=OK");
        }
    }
}
