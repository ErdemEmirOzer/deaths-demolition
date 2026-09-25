using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 6 · the main menu's Look B backdrop (direction E2 / §6.1, S-05 menu_plates.json):
    /// band order far → mid → lanterns → near → rain → text-safe scrim; cover-fit; parallax
    /// 4 / 8 / 12 px followed over 0.20 s; lantern breath between 0.55 and 1 of 0.35; Reduced
    /// Motion holds everything still; the one-channel lantern mask never draws without its shader.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiBackdropTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private RectTransform _root;
        private bool _reduced;

        [SetUp]
        public void SetUp()
        {
            _reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;
            var go = new GameObject("Root", typeof(RectTransform));
            _created.Add(go);
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void TearDown()
        {
            UiMotion.ReducedMotion = _reduced;
            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void Plakalar_KatmanSirasi_KaplaSigdir_MetinGuvenliPerde()
        {
            UiBackdrop backdrop = UiBackdrop.Create(_root);
            Assert.IsNotNull(backdrop, "MenuPlates/plate_far Resources'ta");
            Assert.IsTrue(backdrop.HasPlates);

            Transform stack = backdrop.transform;
            var order = new List<string>();
            foreach (Transform child in stack) order.Add(child.name);
            CollectionAssert.AreEqual(new[] { "Far", "Mid", "Lanterns", "Near", "Rain", "TextSafeScrim", "TextSafeFade" }, order, "uzak → orta → fener → yakın → yağmur → perde");

            RawImage far = stack.Find("Far").GetComponent<RawImage>();
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), far.uvRect, "16:9 plaka 16:9 ekranda tam");
            Assert.AreEqual(-UiBackdrop.NearParallax, ((RectTransform)stack.Find("Near")).offsetMin.x, 0.01f, "yakın bant paralaksı kadar taşar (kenar görünmez)");

            Image scrim = stack.Find("TextSafeScrim").GetComponent<Image>();
            Assert.AreEqual(1f / 3f, scrim.rectTransform.anchorMax.x, 1e-4f, "sol üçte bir");
            Assert.AreEqual(UiBackdrop.ScrimAlpha, scrim.color.a, 1e-4f, "T10 perdesi 0,55");

            Shader mask = DD.Prototype.Rendering.PrototypeShaders.UiMaskTint;
            Assert.IsNotNull(mask, "DD/UI/MaskTint projede");
            GameObject lanterns = stack.Find("Lanterns").gameObject;
            if (mask.isSupported)
            {
                Assert.AreEqual(UiBackdrop.MaskShader, lanterns.GetComponent<RawImage>().material.shader.name, "tek kanallı maske kendi shader'ıyla");
            }
            else
            {
                Assert.IsFalse(lanterns.activeSelf, "shader yoksa maske çizilmez (kırmızı dikdörtgen yok)");
            }
        }

        /// <summary>
        /// AD 6ab MUST-2 + 12b · D1: the header block runs from x 0 to the title's right + 96 + 12 at HeaderScrimAlpha
        /// (0.90); the left third's scrim and its fade start under it (never doubled), and below the block over the
        /// third the fade adds only (H − T) / (1 − T), so it composites from 0.90 down to the third's fixed 0.55.
        /// </summary>
        [Test]
        public void BaslikBlogu_SoldanBaslar_UcteBirKatlanmaz_GecisKademesiz()
        {
            UiBackdrop backdrop = UiBackdrop.Create(_root);
            backdrop.CoverHeader(952f, 232f);
            Transform block = backdrop.transform.Find("HeaderScrim");
            Assert.IsNotNull(block, "başlık bloğu");
            Assert.AreEqual(952f, backdrop.HeaderRight, 0.5f, "başlığın sağı + 96 + 12");
            Assert.AreEqual(0f, ((RectTransform)block).anchoredPosition.x, 0.5f, "x 0'dan başlar (AD 6ab MUST-2; Mac kırpmasında alt başlığın solu)");
            Assert.AreEqual(UiBackdrop.HeaderScrimAlpha, block.GetComponent<Image>().color.a, 1e-4f, "0,90 (D1)");
            Assert.AreEqual(0.90f, UiBackdrop.HeaderScrimAlpha, 1e-4f);
            Assert.AreEqual(0.55f, UiBackdrop.ScrimAlpha, 1e-4f, "sol üçte bir 0,55 sabit (AD §14 MUST-5)");
            Assert.IsNotNull(backdrop.transform.Find("HeaderFadeRight"));
            Assert.IsNotNull(backdrop.transform.Find("HeaderFadeDown"));
            Assert.AreEqual(-232f, ((RectTransform)backdrop.transform.Find("TextSafeScrim")).offsetMax.y, 0.5f, "üçte birin perdesi bloğun altında başlar (katlanmaz)");
            Assert.AreEqual(-232f, ((RectTransform)backdrop.transform.Find("TextSafeFade")).offsetMax.y, 0.5f, "üçte birin geçişi bloğun altında başlar");
            Assert.AreEqual(UiSprites.FadeLeft, backdrop.transform.Find("TextSafeFade").GetComponent<Image>().sprite, "geçiş perdeye bitişik tarafta opak");

            Image safeFade = backdrop.transform.Find("HeaderFadeDownSafe").GetComponent<Image>();
            float t = UiBackdrop.ScrimAlpha;
            float top = 1f - (1f - t) * (1f - safeFade.color.a);
            Assert.AreEqual(UiBackdrop.HeaderScrimAlpha, top, 1e-3f, "üçte bir üstünde geçiş bloğun α'sından başlar (kademe yok)");
        }

        /// <summary>12b · D1: where the spine runs past the third (21:9 at UI 1.0 / 0.9) the text-safe scrim widens to it; never narrower than the third.</summary>
        [Test]
        public void MetinGuvenliPerde_OmurgayaGenisler_UcteBirdenDarAlmaz()
        {
            UiBackdrop backdrop = UiBackdrop.Create(_root);
            Assert.AreEqual(640f, backdrop.TextSafeRight, 0.5f, "üçte bir");
            backdrop.CoverColumn(600f);
            Assert.AreEqual(640f, backdrop.TextSafeRight, 0.5f, "üçte birin içindeki omurga bir şey değiştirmez");
            backdrop.CoverColumn(700f);
            Assert.AreEqual(700f, backdrop.TextSafeRight, 0.5f, "omurga + paralaks");
            RectTransform fade = (RectTransform)backdrop.transform.Find("TextSafeFade");
            Assert.AreEqual(700f / 1920f, fade.anchorMin.x, 1e-4f, "geçiş perdenin yeni kenarından başlar");
            backdrop.CoverHeader(952f, 232f);
            Assert.AreEqual(700f, ((RectTransform)backdrop.transform.Find("HeaderFadeDown")).anchoredPosition.x, 0.5f, "bloğun aşağı geçişi metin-güvenli kenardan");
        }

        [Test]
        public void MenuOmurgasi_KisaSutundaSikisir_SekmeAsla48AltinaInmez_AraliklarKalir()
        {
            const float fixedHeight = 8f + 50f + 25f + 48f + 8f + 25f;   // caption gap + PLAY caption + label + QUIT + gap + build line
            MainMenuScreen.NavRhythm full = MainMenuScreen.NavRhythmFor(900f, fixedHeight);
            Assert.AreEqual(72f, full.Play, "yeterli sütunda tam ritim");
            Assert.AreEqual(56f, full.Tab);
            Assert.AreEqual(8f, full.Gap);

            MainMenuScreen.NavRhythm compact = MainMenuScreen.NavRhythmFor(625f, fixedHeight);   // UI 1.2 × TextScale 1.25 at 1080p
            Assert.Less(compact.Need(fixedHeight), full.Need(fixedHeight), "kısa sütunda sıkışır");
            Assert.LessOrEqual(compact.Need(fixedHeight), 625f, "sekmeler QUIT'e değmez (T11)");
            Assert.Greater(compact.Gap, 0f, "sekmeler birbirine değmez");

            MainMenuScreen.NavRhythm tight = MainMenuScreen.NavRhythmFor(300f, fixedHeight);
            Assert.GreaterOrEqual(tight.Tab, UiTokens.TargetPreferred, "sekme 48'in altına inmez");
            Assert.Greater(tight.Gap, 0f);
            Assert.AreEqual(full.Tab, MainMenuScreen.NavRhythmFor(0f, fixedHeight).Tab, "yerleşmemiş sütun: tam ritim");
        }

        [Test]
        public void Paralaks_TakipEder_FenerNefesAlir_AzHareketteSabit()
        {
            UiBackdrop backdrop = UiBackdrop.Create(_root);
            backdrop.Tick(UiBackdrop.FollowSeconds * 0.5f, 0f, Vector2.right);   // a frame is capped at 0.1 s (hitches)
            backdrop.Tick(UiBackdrop.FollowSeconds * 0.5f, 0.1f, Vector2.right);
            float expected = -UiBackdrop.NearParallax * (1f - Mathf.Exp(-1f));
            Assert.AreEqual(expected, backdrop.NearOffset.x, 0.05f, "0,20 s'de aradaki farkın ~%63'ü");
            for (int i = 0; i < 60; i++) backdrop.Tick(0.05f, i * 0.05f, Vector2.right);
            Assert.AreEqual(-UiBackdrop.NearParallax, backdrop.NearOffset.x, 0.05f, "yakın bant 12 px'e oturur (bakışın tersine)");

            if (backdrop.LanternAlpha > 0f)
            {
                float min = float.MaxValue, max = 0f;
                for (int i = 0; i < 48; i++)
                {
                    backdrop.Tick(0.05f, i * 0.05f, Vector2.zero);
                    min = Mathf.Min(min, backdrop.LanternAlpha);
                    max = Mathf.Max(max, backdrop.LanternAlpha);
                }

                Assert.AreEqual(UiBackdrop.LanternPeak, max, 0.01f, "nefes tepesi 0,35");
                Assert.AreEqual(UiBackdrop.LanternPeak * UiBackdrop.BreathFloor, min, 0.01f, "nefes tabanı 0,55 × 0,35");
            }

            UiMotion.ReducedMotion = true;
            backdrop.Tick(0.05f, 1.2f, Vector2.one);
            Assert.AreEqual(Vector2.zero, backdrop.NearOffset, "RM: bantlar ortada");
            if (backdrop.LanternAlpha > 0f)
            {
                Assert.AreEqual(UiBackdrop.LanternPeak, backdrop.LanternAlpha, 1e-4f, "RM: fener tepede sabit");
            }
        }
    }
}
