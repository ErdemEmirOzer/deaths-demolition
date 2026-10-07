using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension (WP-051C)
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-098A (KIT-API §6) · the brush kit's P0 contract. This file: P0a, tests 1–5 (pane grounds, the frameless button and
    /// the brackets, the one C8 of a choice group, the pane's open / close / Reduced Motion / 0 GC, the brush confirm).
    /// Tests 6–8 and the linear-light ground test (P0b: rows at 1.25, no Update / Input System in kit types, the AP-034
    /// fallbacks, FircaZeminleri_MetinDogrusalIsikta) are UiBrushKitControlsTests. EditMode: the test clock drives UiMotion
    /// and the Fx clock (UiMotion.TestNow).
    /// </summary>
    [TestFixture]
    [Category("WP098")]
    public sealed class UiBrushKitTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private float _textScale;
        private bool _reduced;
        private RectTransform _root;
        private EventSystem _events;

        [SetUp]
        public void SetUp()
        {
            _textScale = UiTokens.TextScale;
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;   // the machine's saved UI settings may have it on; each test turns it on itself
            UiMotion.ResetForTests();
            UiMotion.TestNow = 50f;
            var go = new GameObject("Screen", typeof(RectTransform), typeof(Canvas));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_events != null)
            {
                // The EventSystem was enabled by hand (EditMode runs no OnEnable for it): take it out of the static list again.
                Invoke(_events, "OnDisable");
                _events = null;
            }

            UiFocusBrackets.HideNow();
            UiTokens.TextScale = _textScale;
            UiMotion.ReducedMotion = _reduced;
            UiMotion.ResetForTests();
            UiMotion.TestNow = null;
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        private static void Step(float seconds)
        {
            UiMotion.TestNow += seconds;
            UiMotion.Tick();
        }

        private RectTransform Host(string name)
        {
            RectTransform host = UiFactory.NewRect(name, _root);
            UiFactory.Place(host, 400f, 120f, 1008f, 720f);
            return host;
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(info, method);
            info.Invoke(target, null);
        }

        /// <summary>A live EventSystem in EditMode (its OnEnable, which registers EventSystem.current, only runs in play mode).</summary>
        private EventSystem Events()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            _created.Add(go);
            _events = go.GetComponent<EventSystem>();
            Invoke(_events, "OnEnable");
            Assert.AreSame(_events, EventSystem.current, "EditMode EventSystem.current");
            return _events;
        }

        // ------------------------------------------------------------------ 1

        [Test]
        public void Pane_MetinZemini_EnAz0945_TumSeviyeler()
        {
            var levels = new[]
            {
                new KeyValuePair<UiBrushScrim, UiBrushWash>(UiBrushScrim.Window, UiBrushWash.None),
                new KeyValuePair<UiBrushScrim, UiBrushWash>(UiBrushScrim.Confirm, UiBrushWash.None),
                new KeyValuePair<UiBrushScrim, UiBrushWash>(UiBrushScrim.Pane, UiBrushWash.Column),
                new KeyValuePair<UiBrushScrim, UiBrushWash>(UiBrushScrim.Wallpaper, UiBrushWash.Column),
            };

            foreach (KeyValuePair<UiBrushScrim, UiBrushWash> level in levels)
            {
                UiBrushPane pane = UiBrushKit.Pane(_root, Host("Host." + level.Key), "PaneCard", level.Key, level.Value);
                Assert.AreEqual("PaneCard", pane.Root.name, "ad çağırandan gelir");
                Assert.AreEqual(level.Key, pane.ScrimLevel);
                Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin - 1e-5f, level.Key + " metin zemini (perde ⊕ yıkama)");
                Assert.AreEqual(UiBrushTokens.Scrim(level.Key), pane.Scrim.color.a, 1e-5f, level.Key + " perde α'sı doğrusal token");
                Assert.AreSame(_root, pane.Scrim.transform.parent, "C1 ekran kökünde");
                Assert.Less(pane.Scrim.transform.GetSiblingIndex(), pane.Root.parent.GetSiblingIndex(), "C1 pane'in altında");
                Assert.AreEqual(level.Key != UiBrushScrim.Pane, pane.Scrim.raycastTarget, "Pane perdesi tıklamayı geçirir, diğerleri tutar");
                Assert.AreEqual(level.Value == UiBrushWash.Column, pane.Wash != null, "C2 yalnız istenince");
                if (pane.Wash != null)
                {
                    Assert.Less(pane.Wash.transform.GetSiblingIndex(), pane.Content.GetSiblingIndex(), "C2 içeriğin altında (kırpılmaz)");
                    Assert.AreEqual(-UiBrushTokens.WashReach, pane.Wash.rectTransform.offsetMin.x, 1e-3f, "C2 içerikten WashReach taşar");
                }

                Assert.IsNotNull(pane.Content.GetComponent<RectMask2D>(), "içerik kırpılır");
                Assert.IsFalse(pane.IsOpen, "Open'a kadar kapalı");
                Assert.IsFalse(pane.Group.interactable);
                Assert.IsFalse(pane.Scrim.gameObject.activeSelf);
            }

            // Pane / Wallpaper without the wash: not a text ground - logged once at build.
            LogAssert.Expect(LogType.Warning, new Regex("without the C2 wash"));
            UiBrushPane bare = UiBrushKit.Pane(_root, Host("Host.Bare"), "Bare", UiBrushScrim.Pane, UiBrushWash.None);
            Assert.Less(bare.TextGroundAlpha, UiBrushTokens.TextGroundMin, "yıkamasız Pane metin taşımaz");
        }

        // ------------------------------------------------------------------ 2

        [Test]
        public void FircaDugmesi_DolguCerceveHalkaYok_OdagiParantezTasir()
        {
            UiButton button = UiBrushKit.Button(_root, "Resume", "pause.resume", "RESUME", "Menu.Resume", null);
            Assert.AreEqual(UiButtonVariant.Brush, button.Variant);
            Assert.IsFalse(button.DrawsFocusRing, "fırça düğmesi kendi halkasını çizmez");
            Assert.IsNull(button.transform.Find("FocusRing"), "halka yok");
            Assert.IsNull(button.transform.Find("FocusHalo"), "hale yok");
            Assert.IsNull(button.transform.Find("Sheen"), "parıltı yok");
            foreach (string part in new[] { "Fill", "Border", "Wash" })
            {
                Assert.IsFalse(button.transform.Find(part).GetComponent<Image>().enabled, part + " kapalı (overdraw yok)");
            }

            var rect = (RectTransform)button.transform;
            Assert.AreEqual(UiBrushTokens.ControlHeight, rect.sizeDelta.y, 1e-3f, "C11 64 yüksek");
            Assert.GreaterOrEqual(rect.sizeDelta.x, UiBrushTokens.LabelButtonMin, "C11 ≥ 232");
            Assert.AreEqual(UiTokens.FontSize(UiType.MenuBody), (int)button.LabelText.fontSize, "kelime MenuBody");
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), button.LabelText.color);

            UiFocusBrackets.HideNow();
            button.OnSelect(new BaseEventData(null));
            Assert.AreSame(button.transform, UiFocusBrackets.Target, "odağı köşebent taşır");
            Assert.AreEqual(1f, UiFocusBrackets.Arms.canvasRenderer.GetAlpha(), 1e-4f, "kesme karesinde kollar α 1");
            Assert.AreEqual(rect.sizeDelta.x + UiFocusBrackets.Outset * 2f, UiFocusBrackets.Rect.sizeDelta.x, 0.5f, "köşebent 8 dışarıda");

            button.SetInteractable(false);
            Assert.IsTrue(button.DisabledRuleVisible, "devre dışı: kıl çizgi (biçim)");
            Assert.AreEqual(UiTokens.Get(UiColor.TextSecondary), button.LabelText.color, "devre dışı: TextSecondary (okunur)");
            button.SetInteractable(true);
            Assert.IsFalse(button.DisabledRuleVisible);
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), button.LabelText.color);
        }

        // ------------------------------------------------------------------ 3

        [Test]
        public void SecimIzi_GruptaTek_SecilenAltinaTasinir_AzHareketteCizili()
        {
            int chosen = -1;
            UiBrushChoiceGroup group = UiBrushKit.Segments(_root, "Camera", new[] { "Camera.TopDown", "Camera.First", "Camera.Third" },
                new string[] { null, null, null }, new[] { "TOP-DOWN", "FIRST PERSON", "THIRD PERSON" }, 0, i => chosen = i);
            Assert.AreEqual(3, group.Count);
            Assert.AreEqual("Camera.Third", group[2].name, "ad çağırandan gelir");

            UiBrushMark[] marks = group.GetComponentsInChildren<UiBrushMark>(true);
            Assert.AreEqual(1, marks.Length, "grupta tek C8");
            Assert.AreEqual(UiBrushMarkKind.Selection, marks[0].Kind);
            Assert.AreSame(group.Mark, marks[0]);
            Assert.IsTrue(group.Mark.Drawn, "kurulumda seçili altında çizili (kesme)");

            UiEvents.ResetCounters();
            group.Select(2, true);
            Assert.AreEqual(2, chosen, "onSelect");
            Assert.AreEqual(2, group.Selected);
            Assert.AreEqual(UiEvent.ChoiceSelected, UiEvents.Last);
            Assert.AreEqual("Camera", UiEvents.LastDetail);

            var item = new Vector3[4];
            var mark = new Vector3[4];
            ((RectTransform)group[2].transform).GetWorldCorners(item);
            group.Mark.Rect.GetWorldCorners(mark);
            Assert.AreEqual(item[0].x + UiBrushTokens.SelectionInset, mark[0].x, 0.5f, "C8 16 içeride başlar");
            Assert.AreEqual(item[2].x - UiBrushTokens.SelectionInset, mark[2].x, 0.5f, "C8 16 içeride biter");
            Assert.AreEqual(item[0].y - UiBrushTokens.SelectionGap, mark[1].y, 0.5f, "C8 8 altta");
            Assert.AreEqual(UiTokens.Get(UiColor.TextPrimary), group[2].LabelText.color, "seçilen kelime TextPrimary");
            Assert.AreEqual(UiTokens.Get(UiColor.TextSecondary), group[0].LabelText.color, "diğerleri TextSecondary (renk tek başına değil: C8 biçimi)");

            Assert.IsFalse(group.Mark.Drawn, "seçim değişince yeniden çizilir (Reselect 400)");
            Step(UiMotionTokens.Seconds(UiBrushTokens.Reselect) + 0.01f);
            Assert.IsTrue(group.Mark.Drawn);

            UiMotion.ReducedMotion = true;
            group.Select(1, false);
            Assert.IsTrue(group.Mark.Drawn, "AH: anında çizili");
            Assert.AreEqual(2, chosen, "notify false: geri çağırma yok");
        }

        // ------------------------------------------------------------------ 4

        [Test]
        public void Pane_AcKapa_GirdiAnindaAcilir_AzHareketteSonDurum_0GC()
        {
            UiBrushPane pane = UiBrushKit.Pane(_root, Host("Host"), "PaneCard", UiBrushScrim.Pane, UiBrushWash.Column);
            UiBrushMark edge = UiBrushKit.EdgeTrace(pane);
            UiBrushTitle title = UiBrushKit.Title(pane.Content, "menu.tab.settings", "SETTINGS", "Menu.Settings");
            UiButton row = UiBrushKit.Button(pane.Content, "Row", null, "ROW", null, null);
            Assert.Greater(title.Height, UiFactory.LineHeight(UiType.Display), "başlık bandı + çizgi + fırça");
            Assert.IsFalse(edge.Visible, "kapalı pane izleri göstermez");
            Assert.IsFalse(title.Stroke.Visible);

            UiEvents.ResetCounters();
            pane.Open(row);
            Assert.IsTrue(pane.IsOpen);
            Assert.IsTrue(pane.Group.interactable && pane.Group.blocksRaycasts, "girdi aynı karede açık");
            Assert.IsTrue(pane.Scrim.gameObject.activeSelf);
            Assert.IsTrue(edge.Visible && title.Stroke.Visible, "izler mürekkeplenir");
            Assert.IsFalse(title.Stroke.Drawn, "Screen 280 boyunca çiziliyor");
            Assert.AreEqual(720f, edge.Rect.sizeDelta.x, 0.5f, "kenar izi pane boyu");
            Assert.AreEqual(UiEvent.PaneOpened, UiEvents.Last);
            Step(0.4f);
            Assert.AreEqual(1f, pane.Group.alpha, 1e-4f);
            Assert.IsTrue(title.Stroke.Drawn);

            pane.Close();
            Assert.IsFalse(pane.Group.interactable, "girdi anında kapanır");
            Assert.IsTrue(pane.Root.gameObject.activeSelf, "solarken açık");
            Step(0.07f);
            Assert.IsTrue(pane.Root.gameObject.activeSelf);
            Step(0.1f);
            Assert.IsFalse(pane.Root.gameObject.activeSelf, "kapanış cue'su kökü kapatır");
            Assert.IsFalse(pane.Scrim.gameObject.activeSelf, "C1 de");
            Assert.IsFalse(pane.HandleCancel(), "kapalı pane iptali almaz");

            UiMotion.ReducedMotion = true;
            pane.Open(row);
            Assert.AreEqual(1f, pane.Group.alpha, 1e-4f, "AH: son durum");
            Assert.AreEqual(1f, pane.Scrim.canvasRenderer.GetAlpha(), 1e-4f);
            Assert.IsTrue(title.Stroke.Drawn, "AH: izler çizili");
            Assert.IsTrue(pane.HandleCancel(), "iptal açık pane'i kapatır");
            Assert.IsFalse(pane.Root.gameObject.activeSelf, "AH: kapanış anında");

            // 0 GC once warm: open / close (a re-open mid-fade, so no activation churn) and the C8's select / under.
            UiMotion.ReducedMotion = false;
            UiBrushChoiceGroup group = UiBrushKit.Segments(_root, "Seg", new[] { "A", "B", "C" }, null, new[] { "A", "B", "C" }, 0, null);
            var target = (RectTransform)group[1].transform;
            pane.Open(null);
            for (int i = 0; i < 3; i++)
            {
                pane.Close();
                pane.Open(null);
                group.Select(1 + (i & 1), false);
                group.Mark.Under(target, true);
            }

            byte[] control = null;
            Assert.That(() => { control = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: ölçüm ayırmayı görmeli");
            Assert.IsNotNull(control);
            Assert.That(() =>
            {
                pane.Close();
                pane.Open(null);
                group.Select(2, false);
                group.Select(1, false);
                group.Mark.Under(target, true);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "Open / Close / Select / Under ısındıktan sonra ayırmaz");
        }

        // ------------------------------------------------------------------ 5

        [Test]
        public void Onay_Firca_GuvenliVarsayilan_IptalGuvenli_OdakIadesi()
        {
            int confirmed = 0, cancelled = 0;
            var spec = new UiBrushConfirm
            {
                TitleFallback = "END THIS RUN?",
                BodyFallback = "Your run so far is recorded. This cannot be undone.",
                ConfirmFallback = "END RUN",
                ConfirmIcon = "Menu.EndRun",
                CancelKey = "pause.confirm.keep",
                CancelFallback = "KEEP PLAYING",
                CancelIcon = "Menu.Resume",
                Destructive = true
            };

            UiModal modal = UiBrushKit.Confirm("EndRunConfirm", _root, spec, () => confirmed++, () => cancelled++);
            Assert.AreEqual(UiControlStyle.Brush, modal.Style);
            Assert.IsFalse(modal.IsOpen);
            Assert.AreEqual(UiBrushTokens.ScrimConfirm, modal.ScrimImage.color.a, 1e-5f, "perde α = ScrimConfirm (doğrusal)");
            Assert.AreEqual(UiTokens.Get(UiColor.SurfaceInset).r, modal.ScrimImage.color.r, 1e-5f, "perde mürekkep");
            Assert.IsNull(modal.ShadowRect, "kart yok, gölge yok");
            Assert.AreEqual(UiBrushTokens.ConfirmMeasure, modal.CardSize.x, 1e-3f, "ölçü 1088");
            Assert.AreEqual(UiButtonVariant.Brush, modal.Primary.Variant, "yıkıcı eylem renkle değil kelimeyle");
            Assert.AreEqual(UiButtonVariant.Brush, modal.Secondary.Variant);
            Assert.AreSame(modal.Secondary, modal.DefaultFocus, "varsayılan odak güvenli eylem");

            var safe = (RectTransform)modal.Secondary.transform;
            var act = (RectTransform)modal.Primary.transform;
            Assert.Less(safe.anchoredPosition.x, act.anchoredPosition.x, "soldan önce güvenli eylem");
            Assert.AreEqual(UiBrushTokens.ConfirmActionGap, act.anchoredPosition.x - (safe.anchoredPosition.x + safe.sizeDelta.x), 0.5f, "eylemler arası 64");
            UnityEngine.UI.Navigation p = modal.Primary.navigation;
            Assert.AreSame(modal.Secondary, p.selectOnLeft, "odak tuzağı");
            Assert.IsNull(p.selectOnUp);

            EventSystem events = Events();
            UiButton opener = UiBrushKit.Button(_root, "MainMenu", null, "MAIN MENU", null, () => modal.Open());
            events.SetSelectedGameObject(opener.gameObject);
            modal.Open();
            Assert.IsTrue(modal.IsOpen);
            Assert.AreSame(modal.Secondary.gameObject, events.currentSelectedGameObject, "açılışta odak güvenli eylemde");

            Assert.IsTrue(modal.HandleCancel(), "iptal modalca alınır");
            Assert.AreEqual(1, cancelled, "iptal = güvenli eylem");
            Assert.AreEqual(0, confirmed);
            Assert.IsFalse(modal.IsOpen);
            Assert.AreSame(opener.gameObject, events.currentSelectedGameObject, "kapanışta odak açan kontrole döner");

            // One action (a notice): no safe action to fall back to - the only action is the default.
            var notice = new UiBrushConfirm { TitleFallback = "LOAD FAILED", BodyFallback = "The save could not be read.", ConfirmFallback = "OK" };
            UiModal single = UiBrushKit.Confirm("LoadError", _root, notice, null, null);
            Assert.IsNull(single.Secondary, "CancelKey null = tek eylem");
            Assert.AreSame(single.Primary, single.DefaultFocus);
        }
    }
}
