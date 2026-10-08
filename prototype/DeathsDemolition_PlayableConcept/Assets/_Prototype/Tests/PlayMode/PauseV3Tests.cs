using System.Collections;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-098B: Esc in one frame - the Pause action (Input System) on a chosen frame, nothing else. With
    /// <see cref="UiInput.SimulateCancelNextFrame"/> on the same frame it is the real keyboard Esc: Pause AND Cancel.
    /// </summary>
    internal sealed class PauseTusuGirdi : IInputSource
    {
        public int PauseFrame = -1;

        public bool IsEnabled => true;
        public Vector2 Move => Vector2.zero;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public Vector2 AimStick => Vector2.zero;
        public bool FireHeld => false;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => Time.frameCount == PauseFrame;
        public bool DebugPanelPressed => false;
#pragma warning disable 67
        public event System.Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// WP-068 R1-C · Pause (direction E25, AD R1-A SHOULD-3): the read-only "run so far" quotes the same producers as the
    /// HUD (round, kills, gold); the left column's slack sits in one band above MAIN MENU; at the Mac setting (UI 1.2 ×
    /// text 1.25) every word stays whole and on screen.
    /// WP-098B (brush kit KILAVUZ §3 / §5, AP-028 pause / pause-confirm): the panel is the kit's frameless pane on the
    /// Window scrim with title 1 + C8 1 + edge 1 = 3 traces and the three CAMERA modes visible; END THIS RUN? and QUIT THE
    /// GAME? are the kit's Confirm (Confirm scrim, the safe action focused, the destructive one in a word + glyph, never
    /// Danger); DialogClosed only for a dialog that was open; Cancel (pad B / Esc) closes the confirmation else resumes;
    /// Esc (Pause + Cancel) is one step; RESUME is focused first on an explicit chain; the panel enters / leaves inside
    /// 0.35 s (Reduced Motion: at once).
    /// </summary>
    public sealed class PauseV3Tests
    {
        private static readonly Vector3[] s_a = new Vector3[4];
        private static readonly Vector3[] s_b = new Vector3[4];

        /// <summary>The kit pane's entry (C1 140, ink 280, PaneIn 60 + 220 ms) has landed: measure the panel at rest.</summary>
        private const float MotionSettle = 0.4f;

        private bool _reducedMotion;

        [SetUp]
        public void SetUp()
        {
            // The machine's saved UI settings may have Reduced Motion on; the motion tests set it themselves.
            _reducedMotion = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiMotion.ReducedMotion = _reducedMotion;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            if (pause != null && pause.IsPaused)
            {
                pause.SetPaused(false);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_KosuSeridi_TekBoslukBandi_1080()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            RunDirector run = RunDirector.Instance;
            Assert.AreEqual(run.Round.ToString(), pause.RunValue(0), "ROUND = the director's round");
            Assert.AreEqual(RunEvents.Current.RunKills.ToString(), pause.RunValue(2), "KILLS = the HUD's producer");
            Assert.AreEqual(run.Wallet.Balance.ToString(), pause.RunValue(3), "GOLD = the wallet");
            Assert.IsFalse(string.IsNullOrEmpty(pause.RunBuild), "the build line is written");
            AssertStack(pause, "1080");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_Mac_KartIcerigeBuyur_EkrandaKalir()
        {
            yield return Open(1.2f, 1.25f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Debug.Log("[DD-TEST] r1c.pause mac panel=" + pause.PanelRect.rect.size);
            AssertStack(pause, "mac");
            RectTransform screen = (RectTransform)pause.PanelRect.GetComponentInParent<Canvas>().rootCanvas.transform;
            screen.GetWorldCorners(s_b);
            pause.PanelRect.GetWorldCorners(s_a);
            Assert.IsTrue(s_a[0].y >= s_b[0].y && s_a[1].y <= s_b[1].y, "mac: the pane stays on screen");
        }

        // ------------------------------------------------------------------ WP-098B

        /// <summary>
        /// KILAVUZ §3 Duraklat: Window scrim (the kit's level, not AP-028's sRGB α), the text ground ≥ TextGroundMin, and
        /// the trace budget title 1 + C8 1 + edge 1 = 3 (no divider, no wash; vermilion only in the one C8). FP/TP stay
        /// visible (C-00 07.10): three segments, the C8 under the current mode.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_FircaPerdesi_IzButcesi3_KameraSegmentleriGorunur()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            UiBrushPane pane = pause.Pane;
            Assert.IsNotNull(pane, "kitin pane'i");
            Assert.IsTrue(pane.IsOpen);
            Assert.AreEqual(UiBrushScrim.Window, pane.ScrimLevel, "Duraklat = Window perdesi");
            Assert.AreEqual(UiBrushTokens.Scrim(UiBrushScrim.Window), pane.Scrim.color.a, 1e-3f, "perde α kit token'ından");
            Assert.AreEqual(UiBrushTokens.ScrimWindow, UiBrushTokens.Scrim(UiBrushScrim.Window), 1e-4f);
            Assert.IsNull(pane.Wash, "Window perdesinde yıkama yok");
            Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin, "metin zemini ≥ 0,945");

            int title = 0, selection = 0, divider = 0, edge = 0;
            foreach (UiBrushMark mark in pause.GetComponentsInChildren<UiBrushMark>(true))
            {
                if (mark.transform.IsChildOf(pause.Confirmation.transform))
                {
                    continue;   // the confirmation is its own window with its own title
                }

                switch (mark.Kind)
                {
                    case UiBrushMarkKind.TitleStroke: title++; break;
                    case UiBrushMarkKind.Selection: selection++; break;
                    case UiBrushMarkKind.Divider: divider++; break;
                    case UiBrushMarkKind.Edge: edge++; break;
                }
            }

            Assert.AreEqual(1, title, "başlık fırçası 1");
            Assert.AreEqual(1, selection, "C8 1 (kamera segmenti)");
            Assert.AreEqual(1, edge, "kenar izi 1 (sol)");
            Assert.AreEqual(0, divider, "fırça ayırıcı yok");
            Assert.AreEqual(3, title + selection + divider + edge, "iz bütçesi: başlık 1 + C8 1 + kenar 1 = 3");
            Assert.LessOrEqual(title, UiBrushTokens.MaxTitle);
            Assert.LessOrEqual(selection, UiBrushTokens.MaxSelection);
            Assert.LessOrEqual(edge, UiBrushTokens.MaxEdge);
            Assert.LessOrEqual(title + selection + divider + edge, UiBrushTokens.MaxTraces);

            UiBrushChoiceGroup camera = pause.CameraSegments;
            Assert.AreEqual(3, camera.Count, "TD + TP + FP görünür (C-00 07.10)");
            for (int i = 0; i < camera.Count; i++)
            {
                Assert.IsTrue(camera[i].gameObject.activeInHierarchy, "segment " + i + " görünür");
                Assert.IsTrue(camera[i].interactable, "segment " + i + " seçilebilir");
            }

            Assert.AreEqual((int)DD.Prototype.CameraSystem.CameraModeService.Target, camera.Selected, "C8 mevcut modun altında");
            Assert.AreSame(camera.Mark, FirstSelectionMark(pause), "grubun tek C8'i");
            Assert.AreEqual("Setting.CAMERA SHAKE", pause.ShakeSlider.name, "kaydırıcı adı");
            Assert.AreEqual("Resume", pause.ResumeButton.name);
            Assert.AreEqual("MainMenu", pause.MainMenuButton.name);
        }

        /// <summary>
        /// WP-098B · K4 (C-00 07.10, WP-095 his sözleşmesi §6): kamera sarsıntısı kaydırıcısı 0–1, varsayılan 1. Aralık dışı
        /// canlı değer (eski 0–2 ayarı, F1 paneli) açılışta 1'e kırpılır ve kameraya geri yazılır; kaydırıcı 1'in üstüne çıkmaz.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_SarsintiKaydiricisi_0_1_Varsayilan1_EskiDegerKirpilir()
        {
            float once = DD.Prototype.CameraSystem.PrototypeCameraRig.GetShakeIntensity();
            try
            {
                Assert.AreEqual(1f, PauseScreen.ShakeMax, "K4: üst sınır 1");
                Assert.AreEqual(1f, new DD.Prototype.Core.Config.CameraConfigSection().ShakeIntensity, "K4: varsayılan 1");

                yield return Open(1f, 1f);
                PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
                UiSlider slider = pause.ShakeSlider;
                Assume.That(slider.interactable, "kamera/config yok: kaydırıcı kapalı (hata durumu), aralık ölçülemez");
                Assert.LessOrEqual(slider.Value, 1f, "açılış değeri aralıkta");
                Assert.AreEqual(slider.Value, DD.Prototype.CameraSystem.PrototypeCameraRig.GetShakeIntensity(), 0.0001f, "kaydırıcı = canlı değer");

                // Kaydırıcı uçları: 1'in üstü 1'e, altı 0'a.
                slider.SetValue(2f, false);
                Assert.AreEqual(1f, slider.Value, 0.0001f, "üst uç 1");
                slider.SetValue(-1f, false);
                Assert.AreEqual(0f, slider.Value, 0.0001f, "alt uç 0");

                // Eski 0–2 değeri: kapat, canlı değeri 2 yap, yeniden aç → 1'e kırpılır ve kameraya geri yazılır.
                pause.SetPaused(false);
                yield return new WaitForSecondsRealtime(MotionSettle);
                DD.Prototype.CameraSystem.PrototypeCameraRig.SetShakeIntensity(2f);
                Assume.That(DD.Prototype.CameraSystem.PrototypeCameraRig.GetShakeIntensity(), Is.GreaterThan(1f), "kamera tarafı 1'in üstünü artık kabul etmiyor (S-02 aralığı geldi)");
                pause.SetPaused(true);
                yield return null;
                Assert.AreEqual(1f, slider.Value, 0.0001f, "eski değer kaydırıcıda 1");
                Assert.AreEqual(1f, DD.Prototype.CameraSystem.PrototypeCameraRig.GetShakeIntensity(), 0.0001f, "eski değer kameraya 1 olarak geri yazıldı");
                Assert.AreEqual("1.00", slider.ValueText, "değer metni");
            }
            finally
            {
                DD.Prototype.CameraSystem.PrototypeCameraRig.SetShakeIntensity(once);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_DialogClosed_YalnizGercekKapanista_IptalOnceOnayiSonraDuraklatmayiKapatir()
        {
            int opened = 0, closed = 0;
            System.Action<UiEvent, string> record = (e, detail) =>
            {
                if (detail != PauseScreen.ConfirmName) return;
                if (e == UiEvent.DialogOpened) opened++;
                if (e == UiEvent.DialogClosed) closed++;
            };

            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            EventSystem events = EventSystem.current;
            UiEvents.Raised += record;
            try
            {
                // Default focus and the explicit chain, top to bottom as drawn: RESUME ↓ CAMERA ↓ SHAKE ↓ MAIN MENU.
                Assert.AreSame(pause.ResumeButton.gameObject, events.currentSelectedGameObject, "varsayılan odak RESUME");
                AssertKosebentSarar((RectTransform)events.currentSelectedGameObject.transform, "AD M1 açılış");   // C7 ⊇ seçili (+8)
                UnityEngine.UI.Navigation resume = pause.ResumeButton.navigation;
                Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, resume.mode, "açık nav zinciri");
                Assert.IsNull(resume.selectOnUp, "üstten alta sarma yok (RESUME ↑ MAIN MENU'ye düşmez)");
                UiBrushChoiceGroup camera = pause.CameraSegments;
                Assert.AreSame(camera[camera.Selected], resume.selectOnDown, "RESUME ↓ mevcut kamera segmenti");
                for (int i = 0; i < camera.Count; i++)
                {
                    Assert.AreSame(pause.ResumeButton, camera[i].navigation.selectOnUp, "segment ↑ RESUME");
                }

                Selectable belowCamera = pause.ShakeSlider.interactable ? (Selectable)pause.ShakeSlider : pause.MainMenuButton;
                for (int i = 0; i < camera.Count; i++)
                {
                    Assert.AreSame(belowCamera, camera[i].navigation.selectOnDown, "segment ↓ kaydırıcı (yoksa MAIN MENU)");
                }

                Assert.AreSame(belowCamera, pause.MainMenuButton.navigation.selectOnUp, "MAIN MENU ↑");
                Assert.IsNull(pause.MainMenuButton.navigation.selectOnDown, "alttan başa sarma yok");

                // The old CloseConfirm raised DialogClosed on every unpause, open dialog or not.
                pause.SetPaused(false);
                yield return null;
                Assert.AreEqual(0, closed, "onay hiç açılmadıysa unpause DialogClosed yaymaz");

                pause.SetPaused(true);
                yield return null;
                events.SetSelectedGameObject(pause.MainMenuButton.gameObject);   // as a pad / a click leaves it
                pause.MainMenuButton.OnSubmit(null);
                yield return null;
                Assert.IsTrue(pause.IsConfirming, "MAIN MENU onayı açar");
                Assert.AreEqual(1, opened);
                Assert.AreSame(pause.Confirmation.Secondary.gameObject, events.currentSelectedGameObject, "onayda varsayılan odak KEEP PLAYING");
                Assert.AreEqual(PauseScreen.ConfirmName, pause.Confirmation.name, "kök eski adını korur");
                Assert.AreEqual("Cancel", pause.Confirmation.Secondary.name, "güvenli eylem eski adıyla");
                Assert.AreEqual("Confirm", pause.Confirmation.Primary.name);
                Assert.AreNotEqual(UiButtonVariant.Danger, pause.Confirmation.Primary.Variant, "yıkıcı eylem renkle değil kelime + ikonla");
                Assert.AreEqual(UiBrushTokens.ScrimConfirm, ScrimAlpha(pause.Confirmation), 1e-3f, "onay = Confirm perdesi");
                Canvas layer = pause.Confirmation.GetComponent<Canvas>();
                Assert.IsTrue(layer != null && layer.overrideSorting, "onay kendi tuval katmanında (kapalı kurulsa da: em-1 #2)");
                Assert.AreEqual(UiTokens.SortingDialog, layer.sortingOrder, "diyalog sırası");

                // Cancel (pad B / Esc): the confirmation closes, the pause stays, focus goes back to MAIN MENU.
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(pause.IsConfirming, "iptal onayı kapatır");
                Assert.IsTrue(pause.IsPaused, "iptal önce yalnız onayı kapatır");
                Assert.AreEqual(1, closed, "gerçek kapanış: bir DialogClosed");
                Assert.AreSame(pause.MainMenuButton.gameObject, events.currentSelectedGameObject, "odak açan kontrole döner");
                Assert.IsTrue(pause.PanelGroup.interactable, "onay kapanınca panel yeniden girdi alır");

                // Cancel again: the pause resumes - and still no DialogClosed for the closed dialog.
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(pause.IsPaused, "onay kapalıyken iptal devam ettirir");
                Assert.AreEqual(1, closed, "unpause ikinci bir DialogClosed yaymaz");
            }
            finally
            {
                UiEvents.Raised -= record;
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_EscHemPauseHemCancel_TekAdim()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            IInputSource before = InputSourceRegistry.Current;
            var esc = new PauseTusuGirdi();
            InputSourceRegistry.SetCurrent(esc);
            try
            {
                pause.MainMenuButton.OnSubmit(null);
                yield return null;
                Assert.IsTrue(pause.IsConfirming);

                // Esc on a keyboard is the Pause action AND Cancel in the same frame: one step back, the confirmation only.
                esc.PauseFrame = Time.frameCount + 1;
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(pause.IsConfirming, "Esc onayı kapatır");
                Assert.IsTrue(pause.IsPaused, "aynı basış duraklatmayı da kapatmaz");

                // Again: the pause closes and is not reopened by the same press.
                esc.PauseFrame = Time.frameCount + 1;
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(pause.IsPaused, "Esc tek adımda devam ettirir (kapat-aç değil)");
            }
            finally
            {
                InputSourceRegistry.SetCurrent(before);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_AcilisKapanis_En035sn_AzHareketteSure0()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.AreEqual(1f, pause.PanelGroup.alpha, 1e-3f, "açık ve yerinde");
            Assert.LessOrEqual(UiMotionTokens.Seconds(UiBrushTokens.Open) + UiBrushTokens.ContentDelay, 0.35f, "kit açılışı ≤ 0,35 s");
            Assert.LessOrEqual(UiMotionTokens.Seconds(UiBrushTokens.Close), 0.35f, "kit kapanışı ≤ 0,35 s");

            pause.SetPaused(false);
            Assert.IsFalse(pause.IsPaused, "durum hemen kapanır");
            Assert.IsFalse(pause.PanelGroup.interactable, "girdi anında kapanır");
            Assert.IsTrue(pause.IsShowing, "panel solarak gider");
            yield return new WaitForSecondsRealtime(0.35f);
            yield return null;
            Assert.IsFalse(pause.IsShowing, "kapanış 0,35 s içinde");

            pause.SetPaused(true);
            Assert.IsTrue(pause.PanelGroup.interactable, "girdi ilk karede açılır");
            Assert.AreSame(pause.ResumeButton.gameObject, EventSystem.current.currentSelectedGameObject, "odak ilk karede RESUME");
            Assert.Less(pause.PanelGroup.alpha, 1f, "giriş hareketi oynar (PaneIn)");
            yield return new WaitForSecondsRealtime(0.35f);
            yield return null;
            Assert.AreEqual(1f, pause.PanelGroup.alpha, 1e-3f, "açılış 0,35 s içinde yerinde");

            UiMotion.ReducedMotion = true;
            pause.SetPaused(false);
            yield return null;
            Assert.IsFalse(pause.IsShowing, "azaltılmış hareket: kapanış süresi 0");
            pause.SetPaused(true);
            Assert.AreEqual(1f, pause.PanelGroup.alpha, 1e-3f, "azaltılmış hareket: açılış süresi 0");
            pause.MainMenuButton.OnSubmit(null);
            Assert.IsTrue(pause.IsConfirming, "azaltılmış hareket: onay ilk karede açık");
            Assert.AreSame(pause.Confirmation.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "odak ilk karede KEEP PLAYING");
            pause.Confirmation.Secondary.OnSubmit(null);
            Assert.IsFalse(pause.IsConfirming, "KEEP PLAYING kapatır");
            Assert.IsTrue(pause.IsPaused);
        }

        /// <summary>
        /// WP-098B · AD M1 (15-pause): varsayılan odak RESUME ve C7 köşebendi onu 8 px dıştan sarar. İlk açılışta köşebent
        /// RESUME yerleşmeden ölçülürdü (içeriğin ortası, "1.00"ın altındaki boş dikdörtgen); yeniden açılışta RESUME seçili
        /// kaldıysa seçim olayı hiç koşmaz, köşebent hiç gelmezdi. Ok ↓ ↓ ile kaydırıcıya inince köşebent satır kökünü -
        /// etiketten değere satırın tamamını - sarar (KILAVUZ C14 "C7 satırda").
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_VarsayilanOdakResume_KosebentSecileniSarar_KaydiriciSatiriniSarar()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            EventSystem events = EventSystem.current;

            // İlk açılış: seçili nesne RESUME, köşebent onu sarar.
            Assert.AreSame(pause.ResumeButton.gameObject, events.currentSelectedGameObject, "açılışta seçili nesne RESUME");
            AssertKosebentSarar(pause.ResumeRect, "ilk açılış");

            // Ok ↓ ↓: RESUME → mevcut kamera segmenti → kaydırıcı.
            Assume.That(pause.ShakeSlider.interactable, "kamera/config yok: kaydırıcı kapalı (hata durumu), zincir onu atlar");
            OkTusu(events, MoveDirection.Down);
            yield return null;
            UiBrushChoiceGroup camera = pause.CameraSegments;
            Assert.AreSame(camera[camera.Selected].gameObject, events.currentSelectedGameObject, "↓ mevcut kamera segmenti");
            OkTusu(events, MoveDirection.Down);
            yield return null;
            Assert.AreSame(pause.ShakeSlider.gameObject, events.currentSelectedGameObject, "↓ ↓ kaydırıcı");
            yield return new WaitForSecondsRealtime(KosebentUcusu);
            yield return null;

            var row = (RectTransform)pause.ShakeSlider.transform;
            Assert.AreEqual("Setting.CAMERA SHAKE", row.name, "kaydırıcının seçilebiri satır kökü");
            AssertKosebentSarar(row, "kaydırıcı satırı");
            pause.ResumeRect.GetWorldCorners(s_a);
            float resumeLeft = s_a[0].x;
            row.GetWorldCorners(s_a);
            Assert.AreEqual(resumeLeft, s_a[0].x, 1f * row.lossyScale.x, "satır sol sütunun kenarından başlar (etiket dahil)");

            // Satırın tamamı: etiket ve değer köşebendin içinde.
            UiFocusBrackets.Rect.GetWorldCorners(s_b);
            float tolerance = row.lossyScale.x;   // 1 px
            int words = 0;
            foreach (TMPro.TMP_Text text in row.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                text.rectTransform.GetWorldCorners(s_a);
                Assert.IsTrue(s_a[0].x >= s_b[0].x - tolerance && s_a[2].x <= s_b[2].x + tolerance &&
                              s_a[0].y >= s_b[0].y - tolerance && s_a[2].y <= s_b[2].y + tolerance,
                    "kaydırıcı: " + text.name + " (" + text.text + ") köşebendin içinde");
                words++;
            }

            Assert.GreaterOrEqual(words, 2, "satırda etiket + değer");

            // Yeniden açılış, RESUME seçili bırakılarak: SetSelectedGameObject aynı nesneyi yeniden seçmez (OnSelect yok).
            pause.SetPaused(false);
            yield return new WaitForSecondsRealtime(MotionSettle);
            yield return null;
            events.SetSelectedGameObject(pause.ResumeButton.gameObject);
            pause.SetPaused(true);
            Assert.AreSame(pause.ResumeButton.gameObject, events.currentSelectedGameObject, "yeniden açılış: seçili nesne RESUME");
            Assert.AreSame(pause.ResumeRect, UiFocusBrackets.Target, "yeniden açılış: köşebent ilk karede RESUME'da");
            yield return new WaitForSecondsRealtime(MotionSettle);
            yield return null;
            AssertKosebentSarar(pause.ResumeRect, "yeniden açılış (PaneIn bitti)");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Cikis_Onayi_VarsayilanOdakHayir_IptalKapatir_AzHareketteSure0()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu, "ana menü");
            UiButton quit = RailQuit(menu);
            EventSystem events = EventSystem.current;
            events.SetSelectedGameObject(quit.gameObject);

            int opened = 0, closed = 0;
            System.Action<UiEvent, string> record = (e, detail) =>
            {
                if (detail != PrototypeUiFlow.QuitConfirmName) return;
                if (e == UiEvent.DialogOpened) opened++;
                if (e == UiEvent.DialogClosed) closed++;
            };

            UiEvents.Raised += record;
            try
            {
                // Never press its QUIT here: confirming ends the play session.
                PrototypeUiFlow.RequestQuit();
                yield return null;
                UiModal confirm = PrototypeUiFlow.QuitConfirmation;
                Assert.IsNotNull(confirm, "QUIT onayı kuruldu");
                Assert.IsTrue(PrototypeUiFlow.QuitConfirmOpen);
                Assert.AreEqual(1, opened);
                Assert.AreEqual(PrototypeUiFlow.QuitConfirmName, confirm.name);
                Assert.IsTrue(confirm.transform.IsChildOf(menu.transform), "ana menünün tuvalinde");
                Assert.AreSame(confirm.Secondary.gameObject, events.currentSelectedGameObject, "varsayılan odak Hayır / No");
                Assert.AreEqual(UiStrings.Get("common.no", "NO"), confirm.Secondary.LabelText.text);
                Assert.AreEqual(UiStrings.Get("menu.quit", "QUIT"), confirm.Primary.LabelText.text);
                Assert.AreNotEqual(UiButtonVariant.Danger, confirm.Primary.Variant, "QUIT renkle değil kelime + ikonla");
                Assert.AreEqual(UiBrushTokens.ScrimConfirm, ScrimAlpha(confirm), 1e-3f, "QUIT onayı = Confirm perdesi");
                Canvas quitLayer = confirm.GetComponent<Canvas>();
                Assert.IsTrue(quitLayer != null && quitLayer.overrideSorting, "QUIT onayı kendi tuval katmanında");
                Assert.AreEqual(UiTokens.SortingDialog, quitLayer.sortingOrder, "diyalog sırası");
                Assert.IsTrue(HasText(confirm, UiStrings.Get("menu.quit.confirm", "QUIT THE GAME?")), "başlık");

                // Cancel (pad B / Esc) = NO: closed, still playing, focus back on QUIT.
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(PrototypeUiFlow.QuitConfirmOpen, "iptal = Hayır");
                Assert.IsTrue(Application.isPlaying, "çıkılmadı");
                Assert.AreEqual(1, closed, "bir DialogClosed");
                Assert.AreSame(quit.gameObject, events.currentSelectedGameObject, "odak QUIT'e döner");

                UiMotion.ReducedMotion = true;
                PrototypeUiFlow.RequestQuit();
                confirm = PrototypeUiFlow.QuitConfirmation;
                Assert.IsTrue(confirm.IsOpen, "azaltılmış hareket: onay ilk karede açık");
                Assert.AreSame(confirm.Secondary.gameObject, events.currentSelectedGameObject, "odak ilk karede Hayır");
                confirm.Secondary.OnSubmit(null);
                Assert.IsFalse(PrototypeUiFlow.QuitConfirmOpen, "HAYIR kapatır");
                Assert.AreEqual(2, closed);
            }
            finally
            {
                UiEvents.Raised -= record;
            }
        }

        /// <summary>WP-098B wiring: the rail's QUIT asks first (MainMenuScreen passes PrototypeUiFlow.RequestQuit, a method group).</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Cikis_RaydakiQuit_OnceSorar()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            UiButton quit = RailQuit(Object.FindObjectOfType<MainMenuScreen>());
            FieldInfo field = typeof(UiButton).GetField("_onClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "UiButton._onClick");
            var onClick = field.GetValue(quit) as System.Action;
            Assert.IsNotNull(onClick, "QUIT'in eylemi var");
            Assert.AreEqual(nameof(PrototypeUiFlow.RequestQuit), onClick.Method.Name,
                "raydaki QUIT doğrudan çıkmaz, önce sorar (MainMenuScreen: PrototypeUiFlow.Quit → PrototypeUiFlow.RequestQuit)");
        }

        private static UiButton RailQuit(MainMenuScreen menu)
        {
            foreach (UiButton button in menu.GetComponentsInChildren<UiButton>(true))
            {
                if (button.name == "Quit")
                {
                    return button;
                }
            }

            Assert.Fail("rayda QUIT yok");
            return null;
        }

        /// <summary>The confirmation's C1: the full-screen ink image named "Scrim" (the kit modal's own name for it).</summary>
        private static float ScrimAlpha(UiModal confirm)
        {
            foreach (UnityEngine.UI.Image image in confirm.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.name == "Scrim")
                {
                    return image.color.a;
                }
            }

            Assert.Fail(confirm.name + ": perde (Scrim) yok");
            return 0f;
        }

        private static bool HasText(Component root, string text)
        {
            foreach (TMPro.TMP_Text label in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (label.text == text)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The C7 flight between two controls (Fast 140, Move) has landed.</summary>
        private const float KosebentUcusu = 0.25f;

        /// <summary>AD M1: the brackets' allowed slack beyond <see cref="UiFocusBrackets.Outset"/> on any side.</summary>
        private const float KosebentTolerans = 8f;

        /// <summary>An arrow key / d-pad step as the EventSystem sends it to the focused control.</summary>
        private static void OkTusu(EventSystem events, MoveDirection direction)
        {
            var data = new AxisEventData(events)
            {
                moveDir = direction,
                moveVector = direction == MoveDirection.Down ? Vector2.down : direction == MoveDirection.Up ? Vector2.up :
                             direction == MoveDirection.Left ? Vector2.left : Vector2.right,
            };
            ExecuteEvents.Execute(events.currentSelectedGameObject, data, ExecuteEvents.moveHandler);
        }

        /// <summary>
        /// AD M1: the C7 brackets are on <paramref name="target"/> and their rect contains it (⊇), standing off each side by
        /// no more than Outset (8) + <see cref="KosebentTolerans"/> - canvas pixels, so the runner's window size does not matter.
        /// </summary>
        private static void AssertKosebentSarar(RectTransform target, string when)
        {
            Assert.AreSame(target, UiFocusBrackets.Target, when + ": köşebent seçili nesnede (" +
                (UiFocusBrackets.Target != null ? UiFocusBrackets.Target.name : "yok") + ")");
            RectTransform brackets = UiFocusBrackets.Rect;
            Assert.IsTrue(brackets != null && brackets.gameObject.activeInHierarchy, when + ": köşebent görünür");

            float scale = brackets.lossyScale.x;
            target.GetWorldCorners(s_a);
            brackets.GetWorldCorners(s_b);
            float left = (s_a[0].x - s_b[0].x) / scale;
            float bottom = (s_a[0].y - s_b[0].y) / scale;
            float right = (s_b[2].x - s_a[2].x) / scale;
            float top = (s_b[2].y - s_a[2].y) / scale;
            string sides = "(sol " + left.ToString("0.0") + ", alt " + bottom.ToString("0.0") + ", sağ " + right.ToString("0.0") +
                           ", üst " + top.ToString("0.0") + "; köşebent " + brackets.rect.size + ")";
            Assert.GreaterOrEqual(Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top)), -0.5f,
                when + ": köşebent ⊇ seçili nesne " + sides);
            Assert.LessOrEqual(Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top)), UiFocusBrackets.Outset + KosebentTolerans,
                when + ": köşebent seçili nesneyi dıştan sarar, uzakta değil (Outset + 8) " + sides);
        }

        private static UiBrushMark FirstSelectionMark(PauseScreen pause)
        {
            foreach (UiBrushMark mark in pause.GetComponentsInChildren<UiBrushMark>(true))
            {
                if (mark.Kind == UiBrushMarkKind.Selection && !mark.transform.IsChildOf(pause.Confirmation.transform))
                {
                    return mark;
                }
            }

            return null;
        }

        private static IEnumerator Open(float uiScale, float textScale)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => RunEvents.Current.Phase == RunPhase.Fighting, 20f, "fighting");
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause);
            // WP-098B (pm-1 #5): built closed - the pane's root is off until the first open, so no unplaced, zero-sized word
            // of the closed panel stands active under the run (UiTextScaleTests counted every one as truncated).
            Assert.IsFalse(pause.IsShowing, "kapalı kurulur: pane kökü ilk açılışa dek kapalı");
            Assert.IsFalse(pause.Confirmation.gameObject.activeSelf, "onay kapalı kurulur");
            pause.SetPaused(true);
            yield return null;
            yield return null;
            Assert.IsTrue(pause.IsPaused);
            yield return new WaitForSecondsRealtime(MotionSettle);   // WP-098B: the kit pane enters (scrim, ink, PaneIn)
        }

        /// <summary>
        /// Every word inside the pane and whole; the left column's one slack band sits between the shake slider and MAIN MENU
        /// (≥ XL), and the right column (run so far) does not cross into the left one.
        /// </summary>
        private static void AssertStack(PauseScreen pause, string when)
        {
            float scale = pause.PanelRect.lossyScale.y;
            pause.StackEnd.GetWorldCorners(s_a);
            ((RectTransform)pause.MainMenuButton.transform).GetWorldCorners(s_b);
            float gap = (s_a[0].y - s_b[1].y) / scale;
            Assert.GreaterOrEqual(gap, (int)UiSpace.XL - 0.5f, when + ": the shake slider clears MAIN MENU by at least XL (" + gap + ")");

            pause.ResumeRect.GetWorldCorners(s_a);
            float resumeTop = s_a[1].y;
            pause.StackEnd.GetWorldCorners(s_a);
            Assert.Less(s_a[0].y, resumeTop, when + ": RESUME heads the left column");

            pause.PanelRect.GetWorldCorners(s_b);
            foreach (TMPro.TMP_Text text in pause.PanelRect.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                text.rectTransform.GetWorldCorners(s_a);
                Assert.IsTrue(s_a[0].y >= s_b[0].y - 1f && s_a[1].y <= s_b[1].y + 1f, when + ": " + text.name + " inside the pane (vertical)");
                Assert.IsTrue(s_a[0].x >= s_b[0].x - 1f && s_a[2].x <= s_b[2].x + 1f, when + ": " + text.name + " inside the pane (horizontal)");
                Assert.IsFalse(text.isTextTruncated, when + ": " + text.name + " is whole (" + text.text + ")");
            }

            ((RectTransform)pause.CameraSegments.transform).GetWorldCorners(s_a);
            float leftRight = s_a[2].x;
            ((RectTransform)pause.ShakeSlider.transform).GetWorldCorners(s_a);
            leftRight = Mathf.Max(leftRight, s_a[2].x);
            Assert.IsTrue(pause.RunValue(0).Length > 0, when + ": the run column is written");
            foreach (TMPro.TMP_Text text in pause.PanelRect.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (text.name.StartsWith("Run.") || text.name == "RunBuild")
                {
                    text.rectTransform.GetWorldCorners(s_a);
                    Assert.Greater(s_a[0].x, leftRight, when + ": " + text.name + " sits right of the left column");
                }
            }
        }
    }
}
