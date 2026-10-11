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
        private string _language;

        [SetUp]
        public void SetUp()
        {
            // The machine's saved UI settings may have Reduced Motion on; the motion tests set it themselves.
            _reducedMotion = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            _language = UiStrings.Language;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiMotion.ReducedMotion = _reducedMotion;
            UiStrings.Language = _language;   // WP-105: the TR case sets the mirror only (the settings file is never written)
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
            // WP-105 (V-42): the foot button ends the run and is named for it (was "MainMenu"; the old accessor stays).
            Assert.AreEqual("EndRun", pause.EndRunButton.name);
            Assert.AreSame(pause.EndRunButton, pause.MainMenuButton, "eski erişim adı aynı düğmeyi verir (çekim ajanları)");
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
                // WP-105 (NICE-3, V-04): the kit's percentage, in the language's order - never the invariant "1.00".
                Assert.AreEqual(UiStrings.Percent(100), slider.ValueText, "değer metni yüzde (kitin diğer kaydırıcıları gibi)");
                string dil = UiStrings.Language;
                try
                {
                    UiStrings.Language = UiStrings.English;
                    slider.SetValue(0.7f, false);
                    Assert.AreEqual("70%", slider.ValueText, "EN 70%");
                    UiStrings.Language = UiStrings.Turkish;
                    slider.SetValue(1f, false);
                    Assert.AreEqual("%100", slider.ValueText, "TR %100 (\"1.00\" değil)");
                    slider.SetValue(0.7f, false);
                    Assert.AreEqual("%70", slider.ValueText, "TR %70 (\"0.70\" değil)");
                }
                finally
                {
                    UiStrings.Language = dil;
                    slider.SetValue(1f, false);
                }
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
                // Default focus and the explicit chain, top to bottom as drawn: RESUME ↓ CAMERA ↓ SHAKE ↓ END RUN.
                Assert.AreSame(pause.ResumeButton.gameObject, events.currentSelectedGameObject, "varsayılan odak RESUME");
                AssertKosebentSarar((RectTransform)events.currentSelectedGameObject.transform, "AD M1 açılış");   // C7 ⊇ seçili (+8)
                UnityEngine.UI.Navigation resume = pause.ResumeButton.navigation;
                Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, resume.mode, "açık nav zinciri");
                Assert.IsNull(resume.selectOnUp, "üstten alta sarma yok (RESUME ↑ END RUN'a düşmez)");
                UiBrushChoiceGroup camera = pause.CameraSegments;
                Assert.AreSame(camera[camera.Selected], resume.selectOnDown, "RESUME ↓ mevcut kamera segmenti");
                for (int i = 0; i < camera.Count; i++)
                {
                    Assert.AreSame(pause.ResumeButton, camera[i].navigation.selectOnUp, "segment ↑ RESUME");
                }

                Selectable belowCamera = pause.ShakeSlider.interactable ? (Selectable)pause.ShakeSlider : pause.MainMenuButton;
                for (int i = 0; i < camera.Count; i++)
                {
                    Assert.AreSame(belowCamera, camera[i].navigation.selectOnDown, "segment ↓ kaydırıcı (yoksa END RUN)");
                }

                Assert.AreSame(belowCamera, pause.MainMenuButton.navigation.selectOnUp, "END RUN ↑");
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
                Assert.IsTrue(pause.IsConfirming, "END RUN onayı açar");
                Assert.AreEqual(1, opened);
                Assert.AreSame(pause.Confirmation.Secondary.gameObject, events.currentSelectedGameObject, "onayda varsayılan odak güvenli eylem (RESUME)");
                Assert.AreEqual(PauseScreen.ConfirmName, pause.Confirmation.name, "kök eski adını korur");
                Assert.AreEqual("Cancel", pause.Confirmation.Secondary.name, "güvenli eylem eski adıyla");
                Assert.AreEqual("Confirm", pause.Confirmation.Primary.name);
                Assert.AreNotEqual(UiButtonVariant.Danger, pause.Confirmation.Primary.Variant, "yıkıcı eylem renkle değil kelime + ikonla");
                Assert.AreEqual(UiBrushTokens.ScrimConfirm, ScrimAlpha(pause.Confirmation), 1e-3f, "onay = Confirm perdesi");
                Canvas layer = pause.Confirmation.GetComponent<Canvas>();
                Assert.IsTrue(layer != null && layer.overrideSorting, "onay kendi tuval katmanında (kapalı kurulsa da: em-1 #2)");
                Assert.AreEqual(UiTokens.SortingDialog, layer.sortingOrder, "diyalog sırası");

                // Cancel (pad B / Esc): the confirmation closes, the pause stays, focus goes back to END RUN.
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

        /// <summary>
        /// WP-105 (V-42 + NICE-4) in both languages: one action, one name. The foot button is END RUN (the confirmation's
        /// own word and the Menu.EndRun glyph - it read MAIN MENU / ANA MENÜ with the home glyph and hid the loss); the
        /// confirmation's safe action is the panel's RESUME / DEVAM ET (it read KEEP PLAYING / OYNAMAYA DEVAM) and it
        /// resumes play with one DialogClosed; the panel's RESUME and the safe action share their glyph too.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_KosuyuBitirDugmesi_TekAd_GuvenliEylemDevamEt_OyunaDoner_EN() { yield return OneNamePerAction(UiStrings.English); }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_KosuyuBitirDugmesi_TekAd_GuvenliEylemDevamEt_OyunaDoner_TR() { yield return OneNamePerAction(UiStrings.Turkish); }

        private static IEnumerator OneNamePerAction(string language)
        {
            yield return Open(1f, 1f, language);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.AreEqual(language, UiStrings.Language, "the run was built in " + language);
            Assert.IsTrue(PauseScreen.IsOpen, "IsOpen = the navigation model's Paused overlay (V-41's HUD reads it)");

            UiButton endRun = pause.EndRunButton;
            Assert.AreEqual(UiStrings.Get("pause.confirm.end", "END RUN"), endRun.LabelText.text, language + ": the button says END RUN");
            Assert.AreNotEqual(UiStrings.Get("common.main_menu", "MAIN MENU"), endRun.LabelText.text, language + ": not MAIN MENU");
            Transform glyph = endRun.transform.Find("Glyph");
            if (UiArt.HasIcon("Menu.EndRun"))
            {
                Assert.IsNotNull(glyph, language + ": END RUN has its glyph");
                Assert.AreSame(UiArt.Icon("Menu.EndRun"), glyph.GetComponent<Image>().sprite, language + ": the Menu.EndRun glyph (not Menu.Home)");
            }

            int closed = 0;
            System.Action<UiEvent, string> record = (e, detail) =>
            {
                if (e == UiEvent.DialogClosed && detail == PauseScreen.ConfirmName) closed++;
            };

            UiEvents.Raised += record;
            try
            {
                endRun.OnSubmit(null);
                yield return null;
                Assert.IsTrue(pause.IsConfirming, language + ": END RUN asks first");
                Assert.AreEqual(endRun.LabelText.text, pause.Confirmation.Primary.LabelText.text, language + ": the button and the destructive action share one name");
                Assert.AreEqual(UiStrings.Get("pause.resume", "RESUME"), pause.Confirmation.Secondary.LabelText.text, language + ": the safe action is RESUME / DEVAM ET");
                Assert.AreEqual(pause.ResumeButton.LabelText.text, pause.Confirmation.Secondary.LabelText.text, language + ": the panel's RESUME and the safe action share one name");
                Assert.AreNotEqual(UiStrings.Get("pause.confirm.keep", "KEEP PLAYING"), pause.Confirmation.Secondary.LabelText.text, language + ": no second name");

                pause.Confirmation.Secondary.OnSubmit(null);
                yield return null;
                Assert.IsFalse(pause.IsConfirming, language + ": RESUME closes the confirmation");
                Assert.IsFalse(pause.IsPaused, language + ": RESUME resumes play, as its word says");
                Assert.IsFalse(PauseScreen.IsOpen, language + ": the overlay is gone");
                Assert.AreEqual(1, closed, language + ": one DialogClosed");
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
            Assert.AreSame(pause.Confirmation.Secondary.gameObject, EventSystem.current.currentSelectedGameObject, "odak ilk karede RESUME");
            pause.Confirmation.Secondary.OnSubmit(null);
            Assert.IsFalse(pause.IsConfirming, "RESUME onayı kapatır");
            // WP-105 (NICE-4): the safe action is RESUME - it resumes play, as the panel's RESUME does (KEEP PLAYING only
            // went back to the panel). Cancel stays one step back (Duraklat_DialogClosed_…).
            Assert.IsFalse(pause.IsPaused, "RESUME oyuna döner");
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

        // ------------------------------------------------------------------ WP-105 · Ek 11.10 (§C2 settings, §C5 focus loss)

        private static readonly string[][] SettingsRows =
        {
            // object name (the main menu's), key (the main menu's), the screen's fallback
            new[] { "Setting.MASTER VOLUME", "settings.row.master_volume", "MASTER VOLUME" },
            new[] { "Setting.MUSIC VOLUME", "settings.row.music_volume", "MUSIC" },
            new[] { "Setting.EFFECTS VOLUME", "settings.row.effects_volume", "EFFECTS" },
            new[] { "Setting.UI SOUNDS", "settings.row.ui_sounds", "UI SOUNDS" },
            new[] { "Setting.TEXT SCALE", "access.row.text_scale", "TEXT SCALE" },
            new[] { "ReduceFlashes", "access.row.reduce_flashes", "REDUCE FLASHES" },
            new[] { "ReducedMotion", "access.row.reduced_motion", "REDUCED MOTION" },
        };

        /// <summary>
        /// WP-105 (§C2, V-40; C-00 11.10 decision): SETTINGS beside RESUME (the menu's word, Menu.Settings; RESUME → SETTINGS,
        /// its ↓ the camera row) opens the run's settings inside the pause: the menu's rows under the menu's keys and object
        /// names, one explicit chain from the C10 Back, no key binding. A slider and a toggle write UiSettings (a temp settings
        /// file - the player's is never written). Cancel, Esc (Pause + Cancel in one frame) and the C10 go one step back to the
        /// pause's own page, focus on SETTINGS; a pause closed on the settings page opens on its own page next time.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_Ayarlar_MenuSatirlariAcilir_IptalEscGeriTekAdim()
        {
            string settingsFile = UseTempSettings();
            IInputSource input = InputSourceRegistry.Current;
            try
            {
                yield return Open(1f, 1f);
                PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
                EventSystem events = EventSystem.current;
                UiButton settings = pause.SettingsButton;
                Assert.IsNotNull(settings, "SETTINGS");
                Assert.AreEqual("Settings", settings.name);
                Assert.AreEqual(UiStrings.Get("menu.tab.settings", "SETTINGS"), settings.LabelText.text, "the menu's word");
                Transform glyph = settings.transform.Find("Glyph");
                if (UiArt.HasIcon("Menu.Settings"))
                {
                    Assert.IsNotNull(glyph);
                    Assert.AreSame(UiArt.Icon("Menu.Settings"), glyph.GetComponent<Image>().sprite, "Menu.Settings");
                }

                UiBrushChoiceGroup camera = pause.CameraSegments;
                Assert.AreSame(settings, pause.ResumeButton.navigation.selectOnRight, "RESUME → SETTINGS");
                Assert.AreSame(pause.ResumeButton, settings.navigation.selectOnLeft, "SETTINGS ← RESUME");
                Assert.AreSame(camera[camera.Selected], settings.navigation.selectOnDown, "SETTINGS ↓ the camera row");
                Assert.AreSame(camera[camera.Selected], pause.ResumeButton.navigation.selectOnDown, "RESUME ↓ unchanged");
                pause.ResumeRect.GetWorldCorners(s_a);
                ((RectTransform)settings.transform).GetWorldCorners(s_b);
                Assert.AreEqual(s_a[1].y, s_b[1].y, 1f, "SETTINGS on RESUME's row");
                Assert.Greater(s_b[0].x, s_a[2].x, "SETTINGS right of RESUME");

                settings.OnSubmit(null);
                yield return null;
                Assert.IsTrue(pause.IsSettingsOpen, "SETTINGS opens the page");
                Assert.IsTrue(pause.IsPaused);
                Assert.IsTrue(pause.SettingsPage.gameObject.activeInHierarchy);
                Assert.IsFalse(pause.ResumeButton.gameObject.activeInHierarchy, "the pause's own page yields its column");
                Assert.IsFalse(pause.EndRunButton.gameObject.activeInHierarchy);
                Assert.IsFalse(pause.ShakeSlider.gameObject.activeInHierarchy);
                Assert.AreEqual(SettingsRows[0][0], events.currentSelectedGameObject.name, "focus on the first row");

                Selectable previous = pause.SettingsBackButton;
                Assert.IsNotNull(previous, "the page's C10 Back");
                foreach (string[] row in SettingsRows)
                {
                    Transform t = FindUnder(pause.SettingsPage, row[0]);
                    Assert.IsNotNull(t, row[0] + " (the menu's object name)");
                    Assert.IsTrue(HasText(t, UiStrings.Get(row[1], row[2])), row[0] + ": the menu's word (" + row[1] + ")");
                    Selectable control = t.GetComponent<Selectable>();
                    Assert.IsNotNull(control, row[0] + ": a pad stop");
                    Assert.AreSame(control, previous.navigation.selectOnDown, row[0] + ": ↓ from the row above");
                    Assert.AreSame(previous, control.navigation.selectOnUp, row[0] + ": ↑ back");
                    previous = control;
                }

                Assert.IsNull(previous.navigation.selectOnDown, "no wrap from the last row");
                foreach (UiButton button in pause.SettingsPage.GetComponentsInChildren<UiButton>(true))
                {
                    Assert.IsFalse(button.name.StartsWith("Bind.", System.StringComparison.Ordinal), "no key binding in the pause (it stays in the main menu)");
                }

                // The rows write UiSettings (the temp file).
                UiSlider music = FindUnder(pause.SettingsPage, "Setting.MUSIC VOLUME").GetComponent<UiSlider>();
                float musicBefore = UiSettings.MusicVolume;
                music.Step(musicBefore > 0.5f ? -1 : +1);
                Assert.AreNotEqual(musicBefore, UiSettings.MusicVolume, "MUSIC moves UiSettings.MusicVolume");
                Assert.AreEqual(music.Value, UiSettings.MusicVolume, 0.026f, "on the 5 % steps");
                UiSwitch flashes = FindUnder(pause.SettingsPage, "ReduceFlashes").GetComponent<UiSwitch>();
                bool flashesBefore = UiSettings.ReduceFlashes;
                flashes.Toggle();
                Assert.AreEqual(!flashesBefore, UiSettings.ReduceFlashes, "REDUCE FLASHES writes UiSettings.ReduceFlashes");
                flashes.Toggle();
                Assert.AreEqual(flashesBefore, UiSettings.ReduceFlashes);

                // Cancel: one step back - the page, not the pause.
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(pause.IsSettingsOpen, "Cancel closes the settings page");
                Assert.IsTrue(pause.IsPaused, "and only the page");
                Assert.IsTrue(pause.ResumeButton.gameObject.activeInHierarchy, "the pause's own page is back");
                Assert.AreSame(settings.gameObject, events.currentSelectedGameObject, "focus back on SETTINGS");

                // Esc = the Pause key + Cancel in one frame: still one step.
                settings.OnSubmit(null);
                yield return null;
                Assert.IsTrue(pause.IsSettingsOpen);
                var esc = new PauseTusuGirdi();
                InputSourceRegistry.SetCurrent(esc);
                esc.PauseFrame = Time.frameCount + 1;
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                Assert.IsFalse(pause.IsSettingsOpen, "Esc closes the settings page");
                Assert.IsTrue(pause.IsPaused, "the same press does not resume");
                InputSourceRegistry.SetCurrent(input);

                // The C10 Back.
                settings.OnSubmit(null);
                yield return null;
                pause.SettingsBackButton.OnSubmit(null);
                Assert.IsFalse(pause.IsSettingsOpen, "the C10 Back closes the page");
                Assert.IsTrue(pause.IsPaused);

                // Closed on the settings page, the next pause opens on its own page.
                settings.OnSubmit(null);
                yield return null;
                pause.SetPaused(false);
                yield return new WaitForSecondsRealtime(MotionSettle);
                pause.SetPaused(true);
                yield return null;
                Assert.IsFalse(pause.IsSettingsOpen, "a new pause opens on its own page");
                Assert.AreSame(pause.ResumeButton.gameObject, events.currentSelectedGameObject, "RESUME focused");
            }
            finally
            {
                InputSourceRegistry.SetCurrent(input);
                RestoreSettings(settingsFile);
            }
        }

        /// <summary>
        /// WP-105 (§C2) at the Mac setting in Turkish (UI 1.2 × text 1.25): the settings page stays inside the pane on the left
        /// column and none of its words is cut (the rows scroll inside the kit's C35 where they outgrow the window).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_Ayarlar_MacTR_Metin125_SayfaPanedeKelimelerButun()
        {
            string settingsFile = UseTempSettings();
            try
            {
                yield return Open(1.2f, 1.25f, UiStrings.Turkish);
                PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
                pause.SettingsButton.OnSubmit(null);
                yield return null;
                Assert.IsTrue(pause.IsSettingsOpen);
                pause.PanelRect.GetWorldCorners(s_b);
                pause.SettingsPage.GetWorldCorners(s_a);
                Assert.IsTrue(s_a[0].y >= s_b[0].y - 1f && s_a[1].y <= s_b[1].y + 1f && s_a[0].x >= s_b[0].x - 1f && s_a[2].x <= s_b[2].x + 1f,
                    "the page inside the pane");
                int words = 0;
                foreach (TMPro.TMP_Text text in pause.SettingsPage.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (string.IsNullOrEmpty(text.text))
                    {
                        continue;
                    }

                    text.ForceMeshUpdate();
                    Assert.IsFalse(text.isTextTruncated, "mac-tr: " + text.name + " is whole (" + text.text + ")");
                    words++;
                }

                Assert.Greater(words, SettingsRows.Length, "every row's word and its value are on the page");
                Debug.Log("[DD-TEST] wp105.pause-settings mac-tr page=" + pause.SettingsPage.rect.size + " words=" + words);
            }
            finally
            {
                RestoreSettings(settingsFile);
            }
        }

        /// <summary>
        /// WP-105 (§C5, KOD SHOULD-9; C-00 11.10 decision 5): a lost window focus pauses a live run; focus coming back resumes
        /// nothing (the player says RESUME); with the rule off (automation) nothing happens. The rule itself is
        /// UiPauseFocusRuleTests' (batch mode turns it off, so the suite forces it here).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_OdakKaybi_KosudaDuraklatir_OdakDonunceDevamEtmez_OtomasyondaKapali()
        {
            yield return Open(1f, 1f);
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            pause.SetPaused(false);
            yield return new WaitForSecondsRealtime(MotionSettle);
            Assert.IsFalse(pause.IsPaused);
            try
            {
                PauseScreen.FocusPauseOverride = true;
                pause.SendMessage("OnApplicationFocus", false);
                Assert.IsTrue(pause.IsPaused, "focus lost in a live run: paused");
                Assert.IsTrue(PauseScreen.IsOpen);
                pause.SendMessage("OnApplicationFocus", true);
                yield return null;
                Assert.IsTrue(pause.IsPaused, "focus back: still paused (no auto-resume)");

                pause.SetPaused(false);
                yield return new WaitForSecondsRealtime(MotionSettle);
                PauseScreen.FocusPauseOverride = false;
                pause.SendMessage("OnApplicationFocus", false);
                Assert.IsFalse(pause.IsPaused, "the rule off (automation): no pause");
            }
            finally
            {
                PauseScreen.FocusPauseOverride = null;
            }
        }

        /// <summary>A settings file of the test's own: the player's ui-settings.json is never written (§4.14).</summary>
        private static string UseTempSettings()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dd-wp105-pause-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(path);
            return path;
        }

        private static void RestoreSettings(string path)
        {
            UiSettings.SetPathOverride(null);
            UiSettings.EnsureLoaded();
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }

        private static Transform FindUnder(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
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
            // R4 WP-105 (r4-tests): the handler's method name stopped being RequestQuit when menu v5 (bc3a10b3) wrapped every rail
            // action in its ink splash (AddRail's lambda) - the rule is the behaviour: a press opens the confirm, the game stays.
            Assert.IsFalse(PrototypeUiFlow.QuitConfirmOpen);
            quit.OnSubmit(null);
            yield return null;
            Assert.IsTrue(PrototypeUiFlow.QuitConfirmOpen, "raydaki QUIT doğrudan çıkmaz, önce sorar (PrototypeUiFlow.RequestQuit)");
            Assert.IsTrue(Application.isPlaying, "çıkılmadı");
            PrototypeUiFlow.QuitConfirmation.Secondary.OnSubmit(null);   // NO - never its QUIT here
            yield return null;
            Assert.IsFalse(PrototypeUiFlow.QuitConfirmOpen, "HAYIR kapatır");
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

        private static IEnumerator Open(float uiScale, float textScale, string language = null)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            if (language != null)
            {
                UiStrings.Language = language;   // WP-105: the mirror only, before the run scene builds the pause (TearDown restores)
            }

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
