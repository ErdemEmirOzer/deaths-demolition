using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-098B · the main menu's pane carrier on the brush kit (KIT-KILAVUZU §3 "Menü pane'leri": C1 Pane scrim, C2 wash
    /// under the content column, C37 edge trace, C5 title, ≤ 2 C6 dividers, one C8 on the settings strip - no framed card).
    /// The kit's scrim level and the text ground (≥ TextGroundMin), the trace budget (≤ 6 per window, each kind within its
    /// cap), the open / close motion against the 0.35 s ceiling and Reduced Motion (0 s), the row stagger, the source lock
    /// (brush traces only through UiBrushKit), and the statistics body laid out at the pane's widths in EN / TR × TextScale
    /// 1.0 / 1.25: no text cut, nothing below the height the scroller is given. Driven by the motion core's test clock.
    /// </summary>
    [TestFixture]
    [Category("WP098")]
    public sealed class MainMenuPaneTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private float _textScale;
        private bool _reduced;
        private string _language;

        [SetUp]
        public void SetUp()
        {
            _textScale = UiTokens.TextScale;
            _reduced = UiMotion.ReducedMotion;
            _language = UiStrings.Language;
            UiMotion.ReducedMotion = false;
            UiMotion.ResetForTests();
            UiMotion.TestNow = 200f;
        }

        [TearDown]
        public void TearDown()
        {
            UiTokens.TextScale = _textScale;
            UiMotion.ReducedMotion = _reduced;
            UiStrings.Language = _language;
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

        private RectTransform NewScreen()
        {
            var go = new GameObject("Screen", typeof(RectTransform), typeof(Canvas));
            _created.Add(go);
            var screen = (RectTransform)go.transform;
            screen.sizeDelta = new Vector2(1920f, 1080f);
            UiFactory.NewRect("Backdrop", screen);
            UiFactory.NewRect("PageHost", screen);
            return screen;
        }

        private static RectTransform Column(RectTransform screen, float width, float height)
        {
            RectTransform column = UiFactory.NewRect("Pane", screen.Find("PageHost"));
            column.sizeDelta = new Vector2(width, height);
            return column;
        }

        /// <summary>The scrim's drawn ink: its colour alpha (the level) × the renderer alpha (the open / close fade).</summary>
        private static float Drawn(Graphic graphic)
        {
            return graphic.color.a * graphic.canvasRenderer.GetAlpha();
        }

        private static int CountMarks(Transform root, UiBrushMarkKind kind)
        {
            int count = 0;
            foreach (UiBrushMark mark in root.GetComponentsInChildren<UiBrushMark>(true))
            {
                if (mark.Kind == kind) count++;
            }

            return count;
        }

        [Test]
        public void Tasiyici_KitPane_PaneCard_PerdeYikamaKenar()
        {
            RectTransform screen = NewScreen();
            UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, Column(screen, 1200f, 760f));

            Assert.AreEqual("PaneCard", pane.Root.name, "ad korunur (PM testleri pane'i bu adla bulur)");
            Assert.IsNull(pane.Root.GetComponent<Image>(), "çerçevesiz: kökün dolgusu yok");
            Assert.IsNull(pane.Root.Find("Fill"), "v3 kart dolgusu yok");
            Assert.IsNull(pane.Root.Find("Frame"), "A4 mürekkep çerçevesi yok");
            Assert.AreEqual(UiBrushScrim.Pane, pane.ScrimLevel, "menü pane'i: C1 Pane seviyesi (KILAVUZ §3)");
            Assert.IsNotNull(pane.Wash, "C2 yıkama içerik sütununun altında");
            Assert.IsNotNull(pane.Content.GetComponent<RectMask2D>(), "içerik kırpılır, raya taşmaz");
            Assert.IsFalse(pane.IsOpen, "Open'a dek kapalı");

            Image scrim = pane.Scrim;
            Assert.IsNotNull(scrim, "C1 perde");
            Assert.AreSame(screen, scrim.transform.parent, "C1 ekran kökünde");
            Assert.Less(scrim.transform.GetSiblingIndex(), screen.Find("PageHost").GetSiblingIndex(), "perde sayfanın (ray, başlık) altında");
            Assert.AreEqual(Vector2.zero, scrim.rectTransform.anchorMin);
            Assert.AreEqual(Vector2.one, scrim.rectTransform.anchorMax, "tam ekran");
            Assert.IsFalse(scrim.raycastTarget, "Pane seviyesi: ray tıklanır kalır");

            Assert.AreEqual(1, CountMarks(screen, UiBrushMarkKind.Edge), "C37 kenar izi tek");
            Assert.AreEqual(0, CountMarks(screen, UiBrushMarkKind.Selection), "taşıyıcıda C8 yok");
        }

        [Test]
        public void Perde_KitTokenindan_MetinZeminiEnAz0945()
        {
            Assert.AreEqual(UiBrushScrim.Pane, MainMenuScreen.PaneLook.Scrim, "taşıyıcının kendi perdesi Pane; okunan ekranın Window zemini ayrıdır (T1)");
            Assert.AreEqual(UiBrushWash.Column, MainMenuScreen.PaneLook.Wash, "Pane perdesi tek başına metin taşımaz: C2 şart");
            Assert.AreEqual(UiBrushTokens.ScrimPane, UiBrushTokens.Scrim(MainMenuScreen.PaneLook.Scrim), 1e-6f, "perde α kitin token'ı");
            Assert.AreEqual(UiBrushTokens.Combined(UiBrushTokens.ScrimPane, UiBrushTokens.WashCore), MainMenuScreen.PaneLook.TextGround, 1e-6f,
                "metin zemini = perde ⊕ yıkama çekirdeği");
            Assert.GreaterOrEqual(MainMenuScreen.PaneLook.TextGround, UiBrushTokens.TextGroundMin, "metin altında birleşik α ≥ 0,945");

            RectTransform screen = NewScreen();
            UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, Column(screen, 1200f, 760f));
            Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin, "kurulmuş taşıyıcı da eşikte");
            Assert.AreEqual(MainMenuScreen.PaneLook.TextGround, pane.TextGroundAlpha, 1e-4f, "ekranın beyanı kitin ölçümüyle aynı");

            // WP-098B K1: every reading pane uses Window; the real C2 plateau must not be assumed to be 1.
            Assert.AreEqual(UiBrushScrim.None, MainMenuScreen.PaneLook.ScrimOf(UiContext.MainMenu), "pane yokken perde yok");
            foreach (UiContext window in new[] { UiContext.Profile, UiContext.Settings, UiContext.Controls, UiContext.Accessibility, UiContext.Achievements, UiContext.Statistics })
            {
                Assert.AreEqual(UiBrushScrim.Window, MainMenuScreen.PaneLook.ScrimOf(window), window + ": Window (KILAVUZ §2.2 tam ekran pencere)");
                Assert.GreaterOrEqual(UiBrushTokens.Scrim(MainMenuScreen.PaneLook.ScrimOf(window)), UiBrushTokens.TextGroundMin, window + ": Window perdesi tek başına metin taşır");
                Assert.GreaterOrEqual(MainMenuScreen.PaneLook.TextGroundOf(window), UiBrushTokens.TextGroundMin, window + ": perde ⊕ yıkama ≥ 0,945");
            }

            UiBrushPane ground = MainMenuScreen.PaneLook.BuildGround(screen, (RectTransform)pane.Root.parent);
            Assert.AreEqual(UiBrushScrim.Window, ground.ScrimLevel, "zemin taşıyıcısı Window seviyesinde");
            Assert.AreEqual(UiBrushTokens.ScrimWindow, ground.Scrim.color.a, 1e-6f, "Window α kitin token'ı (elle α yok)");
            Assert.IsNull(ground.Wash, "zeminde ikinci C2 yok (yıkama bütçesi aynı)");
            Assert.AreEqual(0, CountMarks(ground.Root, UiBrushMarkKind.Edge), "zeminde kenar izi yok");
            Assert.IsFalse(ground.IsOpen, "zemin bir pane isteyene dek kapalı");
        }

        /// <summary>
        /// K1 regression: Profile's text stays on at least TextGroundMin even if its C2 mask is fully transparent.
        /// Open Profile first, switch through the reading panes, then close; inspect the rendered C1 fades rather than
        /// the kit's theoretical C1 + opaque C2 alpha. Repeat Profile with Reduced Motion. No pixel or token override.
        /// </summary>
        [Test]
        public void Perde_ProfilDahilWindow_MaskeSaydamkenMetinZeminiKorunur()
        {
            RectTransform screen = NewScreen();
            RectTransform column = Column(screen, 1227f, 809f);
            UiBrushPane carrier = MainMenuScreen.PaneLook.BuildCarrier(screen, column);
            UiBrushPane ground = MainMenuScreen.PaneLook.BuildGround(screen, column);

            carrier.Open(null);
            MainMenuScreen.PaneLook.ApplyGround(carrier, ground, MainMenuScreen.PaneLook.ScrimOf(UiContext.Profile));
            Assert.IsTrue(ground.IsOpen, "Profil doğrudan açılınca Window zemini açılır (K1)");
            Step(MainMenuScreen.PaneLook.MaxSeconds);
            Assert.AreEqual(0f, Drawn(carrier.Scrim), 1e-3f, "taşıyıcının Pane perdesi Window altında görünmez");
            Assert.AreEqual(UiBrushTokens.ScrimWindow, Ink(carrier, ground, false), 1e-3f, "Profil: çizilen C1 tek başına Window 0,95");
            Assert.GreaterOrEqual(Ink(carrier, ground, false), UiBrushTokens.TextGroundMin,
                "C2'nin gerçek maskesi tamamen saydam olsa bile metin zemini ≥ 0,945");

            foreach (UiContext window in new[] { UiContext.Settings, UiContext.Controls, UiContext.Accessibility, UiContext.Achievements, UiContext.Statistics, UiContext.Profile })
            {
                MainMenuScreen.PaneLook.ApplyGround(carrier, ground, MainMenuScreen.PaneLook.ScrimOf(window));
                float least = SweepLeast(carrier, ground, MainMenuScreen.PaneLook.MaxSeconds);
                Assert.GreaterOrEqual(least, UiBrushTokens.TextGroundMin, window + ": geçişte C2 olmadan da metin zemini eşikte");
                Assert.IsTrue(ground.IsOpen, window + ": Window zemini açık kalır");
                Assert.AreEqual(UiBrushTokens.ScrimWindow, Ink(carrier, ground, false), 1e-3f, window + ": 0,35 s içinde Window'a oturur");
            }

            MainMenuScreen.PaneLook.ApplyGround(carrier, ground, UiBrushScrim.None);
            carrier.Close();
            Step(MainMenuScreen.PaneLook.MaxSeconds);
            Assert.IsFalse(ground.IsOpen);
            Assert.IsFalse(carrier.Scrim.gameObject.activeSelf || ground.Scrim.gameObject.activeSelf, "kapanınca iki perde de kalkar");

            UiMotion.ReducedMotion = true;
            carrier.Open(null);
            MainMenuScreen.PaneLook.ApplyGround(carrier, ground, MainMenuScreen.PaneLook.ScrimOf(UiContext.Profile));
            Assert.AreEqual(UiBrushTokens.ScrimWindow, Ink(carrier, ground, false), 1e-3f, "AH: Profil Window zemini aynı karede 0,95");
        }

        /// <summary>The C1 ink as drawn (both scrims; with the wash when <paramref name="wash"/>).</summary>
        private static float Ink(UiBrushPane carrier, UiBrushPane ground, bool wash)
        {
            if (wash)
            {
                return MainMenuScreen.PaneLook.DrawnGround(carrier, ground);
            }

            float ink = 0f;
            if (carrier.Scrim.gameObject.activeInHierarchy) ink = UiBrushTokens.Combined(ink, Drawn(carrier.Scrim));
            if (ground.Scrim.gameObject.activeInHierarchy) ink = UiBrushTokens.Combined(ink, Drawn(ground.Scrim));
            return ink;
        }

        /// <summary>Steps the motion clock 10 ms at a time for <paramref name="seconds"/> and returns the least C1 ink seen.</summary>
        private static float SweepLeast(UiBrushPane carrier, UiBrushPane ground, float seconds)
        {
            float least = Ink(carrier, ground, false);
            for (float t = 0f; t < seconds; t += 0.01f)
            {
                Step(0.01f);
                least = Mathf.Min(least, Ink(carrier, ground, false));
            }

            return least;
        }

        /// <summary>
        /// WP-098B AD M2 · an open pane's content column - the title band, the help line, the rows down to the last, the
        /// preview column at the right edge - lies in the plateau of one active C2 (not on its soft edge), at the kit's linear
        /// WashCore α (no AP-028 sRGB α, T1), and scrim ⊕ wash as drawn is ≥ TextGroundMin. Pane sizes: the 1080p menu pane
        /// (1227 × 809), the fixture's 1200 × 760, 16:10 (square mask), an ultrawide page and a tall column (tall mask). Where
        /// AP-034's PNG is on disk, its own α is read under the column (≥ 0.88 = the plateau; the falloff is 0–0.5 there) and the
        /// drawn ground logged with it.
        /// </summary>
        [Test]
        public void Yikama_IcerikSutunuCekirdekte_DogrusalWashCore_BirlesikEnAz0945()
        {
            var sizes = new[] { new Vector2(1227f, 809f), new Vector2(1200f, 760f), new Vector2(1227f, 929f), new Vector2(1600f, 809f), new Vector2(700f, 1200f) };
            foreach (Vector2 size in sizes)
            {
                string at = size.x + "x" + size.y + ": ";
                RectTransform screen = NewScreen();
                RectTransform nav = UiFactory.NewRect("Nav", screen.Find("PageHost"));
                RectTransform column = Column(screen, size.x, size.y);
                UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, column);
                Assert.Less(column.GetSiblingIndex(), nav.GetSiblingIndex(), at + "pane sütunu omurganın altında çizilir: yıkamanın taşan kısmı rayı örtmez");

                RectTransform[] parts = LayContentColumn(pane);
                pane.Open(null);
                Step(MainMenuScreen.PaneLook.MaxSeconds + 0.1f);

                Image wash = pane.Wash;
                Assert.IsNotNull(wash, at + "C2 var");
                Assert.IsTrue(wash.isActiveAndEnabled, at + "C2 açık pane'de etkin");
                Assert.AreSame(pane.Root, wash.transform.parent, at + "C2 pane kökünde");
                Assert.Less(wash.transform.GetSiblingIndex(), pane.Content.GetSiblingIndex(), at + "C2 metnin altında");
                Assert.AreEqual(1f, wash.canvasRenderer.GetAlpha(), 1e-4f, at + "C2 açıkken tam çizilir");
                Assert.AreEqual(UiBrushTokens.WashCore, wash.color.a, 1e-4f, at + "C2 α'sı kitin doğrusal WashCore'u (sRGB α yazılmaz, T1)");
                Assert.GreaterOrEqual(Drawn(wash), UiBrushTokens.WashCore - 1e-4f, at + "çizilen C2 α ≥ WashCore");

                float ground = UiBrushTokens.Combined(Drawn(pane.Scrim), Drawn(wash));
                Assert.AreEqual(UiBrushTokens.ScrimPane, Drawn(pane.Scrim), 1e-3f, at + "perde Pane α'sında");
                Assert.GreaterOrEqual(ground, UiBrushTokens.TextGroundMin, at + "çizilen perde ⊕ yıkama ≥ 0,945");
                Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin, at + "kitin TextGroundAlpha'sı ≥ 0,945");

                Rect plateau = MainMenuScreen.PaneLook.WashPlateau(wash);
                Assert.IsTrue(Inside(plateau, WorldRect(pane.Content)), at + "içerik sütununun tamamı C2 çekirdeğinde: " + WorldRect(pane.Content) + " ⊄ " + plateau);
                foreach (RectTransform part in parts)
                {
                    Assert.IsTrue(Inside(plateau, WorldRect(part)), at + part.name + " C2 çekirdeğinde (yumuşak kenarda değil)");
                }

                AssertMaskPlateau(wash, pane.Content, at);
            }
        }

        [Test]
        public void Yikama_DilimliMaskede_KitinWashReachGeometrisiKalir()
        {
            RectTransform screen = NewScreen();
            UiBrushPane pane = UiBrushKit.Pane(screen, Column(screen, 1200f, 760f), MainMenuScreen.PaneLook.CardName,
                MainMenuScreen.PaneLook.Scrim, MainMenuScreen.PaneLook.Wash);
            Image wash = pane.Wash;
            wash.type = Image.Type.Sliced;   // a 9-sliced mask: the kit's soft edge is WashReach already
            MainMenuScreen.PaneLook.FitWash(pane);
            Assert.AreEqual(Vector2.zero, wash.rectTransform.anchorMin, "dilimli maskede kitin geometrisi");
            Assert.AreEqual(Vector2.one, wash.rectTransform.anchorMax);
            Assert.AreEqual(-UiBrushTokens.WashReach, wash.rectTransform.offsetMin.x, 1e-3f, "C2 içerikten WashReach taşar");

            wash.type = Image.Type.Simple;   // AP-034's sliceless masks: the soft edge grows with the rect
            MainMenuScreen.PaneLook.FitWash(pane);
            float spill = MainMenuScreen.PaneLook.WashSpill;
            Assert.AreEqual(new Vector2(-spill, -spill), wash.rectTransform.anchorMin, "pane'e bağlı taşma (yeniden boyutlanınca izler)");
            Assert.AreEqual(new Vector2(1f + spill, 1f + spill), wash.rectTransform.anchorMax);
            Assert.AreEqual(MainMenuScreen.PaneLook.WashFalloff, spill / (1f + 2f * spill), 1e-5f, "taşma tam maskenin yumuşak kenarı kadar");
        }

        /// <summary>The four edges of a menu pane's content: the title band, the help line, the last row at the foot, the preview column at the right.</summary>
        private static RectTransform[] LayContentColumn(UiBrushPane pane)
        {
            RectTransform content = pane.Content;
            var stack = new UiStack(content);
            UiBrushTitle title = MainMenuScreen.PaneLook.Title(ref stack, content, UiContext.Settings, null);
            Assert.IsNotNull(title);
            RectTransform heading = (RectTransform)content.Find(MainMenuScreen.PaneLook.TitleName);
            TMP_Text help = UiFactory.Label("Help", content, "Device preferences. They are saved on this machine and never touch your profile.",
                UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            stack.Add(help.rectTransform, UiFactory.LineHeight(UiType.MenuSmall));

            RectTransform last = UiFactory.NewRect("LastRow", content);
            last.anchorMin = Vector2.zero;
            last.anchorMax = new Vector2(1f, 0f);
            last.pivot = new Vector2(0.5f, 0f);
            last.sizeDelta = new Vector2(0f, UiBrushTokens.ControlHeight);

            RectTransform preview = UiFactory.NewRect("Preview", content);
            preview.anchorMin = new Vector2(1f, 0f);
            preview.anchorMax = Vector2.one;
            preview.pivot = new Vector2(1f, 0.5f);
            preview.offsetMin = new Vector2(-240f, 0f);
            preview.offsetMax = new Vector2(0f, -stack.Cursor);
            return new[] { heading, help.rectTransform, last, preview };
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static bool Inside(Rect outer, Rect inner)
        {
            const float Slack = 0.5f;
            return inner.xMin >= outer.xMin - Slack && inner.yMin >= outer.yMin - Slack && inner.xMax <= outer.xMax + Slack && inner.yMax <= outer.yMax + Slack;
        }

        /// <summary>
        /// AP-034's own α under the column, from the PNG on disk (the imported texture is not readable): a 17 × 17 grid over
        /// the content must sit on the mask's plateau (≥ 0.88; the old "pane + WashReach" geometry put the column's edges on
        /// 0–0.5). Logs the drawn ground there: 0.72 ⊕ 0.85 × mask α - the kit's sprite core (≈ 0.94, not 1) is its own note.
        /// </summary>
        private static void AssertMaskPlateau(Image wash, RectTransform content, string at)
        {
            if (wash.type == Image.Type.Sliced || wash.sprite == null)
            {
                return;
            }

            string path = Path.Combine(Application.dataPath, "_Prototype", "UI", "Art", "Resources", UiBrushArt.Folder + wash.sprite.name + ".png");
            if (!File.Exists(path))
            {
                Debug.Log("[DD-TEST] wp098b.paneWash " + at + "no AP-034 PNG for " + wash.sprite.name + " (code fallback): geometry only");
                return;
            }

            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(mask.LoadImage(File.ReadAllBytes(path)), at + "AP-034 maskesi okunur");
                Rect area = WorldRect(wash.rectTransform);
                Rect column = WorldRect(content);
                float least = 1f, sum = 0f;
                int count = 0;
                for (int i = 0; i <= 16; i++)
                {
                    for (int j = 0; j <= 16; j++)
                    {
                        Vector2 p = new Vector2(Mathf.Lerp(column.xMin, column.xMax, i / 16f), Mathf.Lerp(column.yMin, column.yMax, j / 16f));
                        float a = mask.GetPixelBilinear((p.x - area.xMin) / area.width, (p.y - area.yMin) / area.height).a;
                        least = Mathf.Min(least, a);
                        sum += a;
                        count++;
                    }
                }

                float drawnLeast = UiBrushTokens.Combined(UiBrushTokens.ScrimPane, UiBrushTokens.WashCore * least);
                float drawnMean = UiBrushTokens.Combined(UiBrushTokens.ScrimPane, UiBrushTokens.WashCore * sum / count);
                Debug.Log("[DD-TEST] wp098b.paneWash " + at + wash.sprite.name + " mask α under the column min=" + least.ToString("0.000") +
                          " mean=" + (sum / count).ToString("0.000") + " → drawn ground min=" + drawnLeast.ToString("0.0000") + " mean=" + drawnMean.ToString("0.0000"));
                Assert.GreaterOrEqual(least, 0.88f, at + wash.sprite.name + ": içerik sütunu maskenin platosunda (yumuşak kenar 0–0,5)");
            }
            finally
            {
                Object.DestroyImmediate(mask);
            }
        }

        [Test]
        public void Acilis_En035Saniye_AzHarekette0()
        {
            Assert.LessOrEqual(MainMenuScreen.PaneLook.OpenSeconds, MainMenuScreen.PaneLook.MaxSeconds, "açılış (perde + yıkama/izler + PaneIn + satır kademesi) ≤ 0,35 s");
            Assert.LessOrEqual(MainMenuScreen.PaneLook.CloseSeconds, MainMenuScreen.PaneLook.MaxSeconds, "kapanış ≤ 0,35 s");

            RectTransform screen = NewScreen();
            UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, Column(screen, 1200f, 760f));
            RectTransform content = pane.Content;
            Selectable first = UiFactory.NewRect("First", content).gameObject.AddComponent<Selectable>();
            var rows = new List<CanvasGroup>();
            for (int i = 0; i < 10; i++)
            {
                RectTransform row = UiFactory.NewRect("Row" + i, content);
                rows.Add(UiFactory.Group(row.gameObject));
            }

            pane.Open(first);
            for (int i = 0; i < rows.Count; i++)
            {
                MainMenuScreen.PaneLook.FadeRowIn(rows[i], i, 0);
            }

            CanvasGroup contentGroup = content.GetComponent<CanvasGroup>();
            Assert.IsTrue(pane.IsOpen, "girdi ilk karede açık");
            Assert.IsTrue(contentGroup == null || contentGroup.interactable, "içerik aynı karede etkileşimli");
            Assert.Greater(UiMotion.ActiveCount, 0, "hareket UiMotion'da (elle Lerp yok)");

            Step(0.12f);
            Assert.Less(Drawn(pane.Scrim), UiBrushTokens.ScrimPane - 1e-4f, "0,12 s'de perde yolda (ScrimIn 140)");
            if (contentGroup != null)
            {
                Assert.Less(contentGroup.alpha, 1f, "0,12 s'de içerik yolda (PaneIn)");
            }

            Step(MainMenuScreen.PaneLook.MaxSeconds - 0.12f + 1e-3f);
            Assert.AreEqual(UiBrushTokens.ScrimPane, Drawn(pane.Scrim), 1e-3f, "0,35 s'de perde kitin Pane α'sında");
            if (contentGroup != null)
            {
                Assert.AreEqual(1f, contentGroup.alpha, 1e-4f, "0,35 s'de içerik yerinde");
            }

            foreach (CanvasGroup row in rows)
            {
                Assert.AreEqual(1f, row.alpha, 1e-4f, row.name + ": satır kademesi 0,35 s içinde biter");
            }

            Assert.AreEqual(0, UiMotion.ActiveCount, "0,35 s'den sonra açık tween kalmaz");

            // Reduced Motion: everything at its end state at once, no tween opened.
            UiMotion.ReducedMotion = true;
            Assert.AreEqual(0f, MainMenuScreen.PaneLook.OpenSeconds, "AH: açılış 0 s");
            Assert.AreEqual(0f, MainMenuScreen.PaneLook.CloseSeconds, "AH: kapanış 0 s");
            RectTransform screen2 = NewScreen();
            UiBrushPane still = MainMenuScreen.PaneLook.BuildCarrier(screen2, Column(screen2, 1200f, 760f));
            Selectable focus = UiFactory.NewRect("First", still.Content).gameObject.AddComponent<Selectable>();
            CanvasGroup row0 = UiFactory.Group(UiFactory.NewRect("Row0", still.Content).gameObject);
            still.Open(focus);
            MainMenuScreen.PaneLook.FadeRowIn(row0, 0, 0);
            CanvasGroup stillGroup = still.Content.GetComponent<CanvasGroup>();
            Assert.AreEqual(UiBrushTokens.ScrimPane, Drawn(still.Scrim), 1e-3f, "AH: perde anında");
            Assert.IsTrue(stillGroup == null || Mathf.Approximately(stillGroup.alpha, 1f), "AH: içerik anında");
            Assert.AreEqual(1f, row0.alpha, 1e-4f, "AH: satır anında");
            Assert.AreEqual(0, UiMotion.ActiveCount, "AH: hiç tween açılmadı");
            foreach (UiBrushMark mark in screen2.GetComponentsInChildren<UiBrushMark>(true))
            {
                Assert.IsTrue(mark.Drawn, "AH: " + mark.Kind + " izi çizili");
            }
        }

        [Test]
        public void Kapanis_En035Saniye_AzHarekette0()
        {
            RectTransform screen = NewScreen();
            UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, Column(screen, 1200f, 760f));
            Selectable first = UiFactory.NewRect("First", pane.Content).gameObject.AddComponent<Selectable>();
            pane.Open(first);
            Step(MainMenuScreen.PaneLook.MaxSeconds + 1e-3f);

            pane.Close();
            Assert.IsFalse(pane.IsOpen, "kapatınca hemen kapalı (solma 'açık' değil)");
            CanvasGroup group = pane.Content.GetComponent<CanvasGroup>();
            Assert.IsTrue(group == null || !group.interactable, "girdi anında kapanır");
            Step(MainMenuScreen.PaneLook.CloseSeconds + 1e-3f);
            Step(1e-3f);
            Assert.IsFalse(pane.Root.gameObject.activeSelf, "solma bitince kök kapandı (kitin sessiz cue'su)");
            Assert.IsFalse(pane.Scrim.gameObject.activeSelf, "perde kalktı");

            UiMotion.ReducedMotion = true;
            pane.Open(first);
            pane.Close();
            Step(1e-3f);
            Assert.IsFalse(pane.IsOpen);
            Assert.IsFalse(pane.Root.gameObject.activeSelf, "AH: kök aynı anda kapanır");
            Assert.IsFalse(pane.Scrim.gameObject.activeSelf, "AH: perde aynı anda kalkar");
        }

        [Test]
        public void Firca_YapisalIzler_KittenGelir_PencereButcesinde()
        {
            RectTransform screen = NewScreen();
            UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, Column(screen, 1200f, 760f));
            pane.Root.gameObject.SetActive(true);   // as the screen builds a pane: the carrier live, Open in the same frame
            RectTransform content = UiFactory.NewRect("Body", pane.Content);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(1100f, 0f);

            // Statistics: the pane with the most traces (title + two dividers) besides the carrier's edge and wash.
            var marks = new List<UiBrushMark>();
            MainMenuScreen.BuildStatisticsBody(content, StatisticsViewModel.From(new ProfileData()), marks, null);
            Assert.AreEqual(3, marks.Count, "başlık fırçası + iki ayırıcı (pane değişiminde ekran bunları çizer)");

            int title = CountMarks(screen, UiBrushMarkKind.TitleStroke);
            int divider = CountMarks(screen, UiBrushMarkKind.Divider);
            int edge = CountMarks(screen, UiBrushMarkKind.Edge);
            int selection = CountMarks(screen, UiBrushMarkKind.Selection);
            int wash = pane.Wash != null ? 1 : 0;
            Assert.AreEqual(1, title, "başlık fırçası 1");
            Assert.LessOrEqual(divider, UiBrushTokens.MaxDivider, "ayırıcı ≤ 2");
            Assert.LessOrEqual(edge, UiBrushTokens.MaxEdge, "kenar ≤ 1");
            Assert.LessOrEqual(selection, UiBrushTokens.MaxSelection, "C8 ≤ 2");
            Assert.LessOrEqual(wash, UiBrushTokens.MaxWash, "yıkama ≤ 2");
            Assert.LessOrEqual(title + divider + edge + selection + wash, UiBrushTokens.MaxTraces, "pencere başına iz ≤ 6");

            Transform heading = content.Find(MainMenuScreen.PaneLook.TitleName);
            Assert.IsNotNull(heading, "C5 başlık kendi bandında");
            Color vermilion = UiTokens.Get(UiColor.AccentPrimary);
            foreach (Image image in content.GetComponentsInChildren<Image>(true))
            {
                bool isVermilion = Mathf.Abs(image.color.r - vermilion.r) < 1e-3f && Mathf.Abs(image.color.g - vermilion.g) < 1e-3f
                                   && Mathf.Abs(image.color.b - vermilion.b) < 1e-3f;
                Assert.IsFalse(isVermilion, image.name + ": başlıkta vermilion çentik yok (vermilion yalnız C8)");
            }
        }

        [Test]
        public void Ayarlar_SekmeSeridi_KitSekmeleri_AdlarVeTekC8()
        {
            int count = MainMenuScreen.PaneLook.SettingsTabIds.Length;
            Assert.AreEqual(count, MainMenuScreen.PaneLook.SettingsTabIcons.Length);
            Assert.AreEqual(count, MainMenuScreen.PaneLook.SettingsTabKeys.Length);
            UiBrushTab[] tabs = MainMenuScreen.PaneLook.SettingsTabs();
            Assert.AreEqual(count, tabs.Length);
            var icons = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(MainMenuScreen.PaneLook.SettingsTabPrefix + MainMenuScreen.PaneLook.SettingsTabIds[i], tabs[i].ObjectName, "ad çağırandan gelir");
                Assert.IsTrue(UiArt.HasIcon(tabs[i].IconKey), tabs[i].IconKey + ": G1 atlasında glif");
                Assert.IsTrue(icons.Add(tabs[i].IconKey), "her sekmenin kendi glifi");
                Assert.IsTrue(UiStrings.Has(tabs[i].NameKey), tabs[i].NameKey + ": okuyucu adı tabloda (yeni metin yok)");
                foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
                {
                    UiStrings.Language = language;
                    Assert.IsFalse(string.IsNullOrEmpty(UiStrings.Get(tabs[i].NameKey, string.Empty)), language + ": " + tabs[i].NameKey);
                }

                StringAssert.DoesNotContain("Display", tabs[i].ObjectName, "sekme adı 'Display' satırının adını taklit etmez (PM testleri ada göre arar)");
            }

            RectTransform screen = NewScreen();
            UiBrushPane pane = MainMenuScreen.PaneLook.BuildCarrier(screen, Column(screen, 1200f, 760f));
            RectTransform strip = UiFactory.NewRect(MainMenuScreen.PaneLook.SettingsStripName, pane.Content);
            UiBrushChoiceGroup group = UiBrushKit.Tabs(strip, MainMenuScreen.PaneLook.SettingsTabsName, tabs, 0, _ => { }, MainMenuScreen.PaneLook.SettingsTabWords);
            Assert.AreEqual(count, group.Count);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(tabs[i].ObjectName, group[i].gameObject.name, "sekme nesnesi 'SettingsTab.*' adını taşır");
            }

            Assert.AreEqual(1, CountMarks(strip, UiBrushMarkKind.Selection), "şeritte tek C8 (vermilion ≤ 2)");
            Assert.AreEqual(UiBrushMarkKind.Selection, group.Mark.Kind);
            Assert.GreaterOrEqual(MainMenuScreen.PaneLook.SettingsStripHeight, UiBrushTokens.ControlHeight + UiBrushTokens.SelectionGap,
                "şerit sekmeyi ve altındaki C8'i taşır");
        }

        [Test]
        public void Kaynak_FircaIzleriYalnizKittenGelir_CagriButcesi()
        {
            string path = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Screens", "MainMenuScreen.cs");
            string code = UiOrnamentCensusTests.StripComments(File.ReadAllText(path));

            Assert.AreEqual(0, Regex.Matches(code, @"UiInkChannel\.|InkMask\(|UiSprites\.(BrushRibbon|EnsoBrush)").Count,
                "fırça izi ekranda elle çizilmez: yalnız UiBrushKit (FircaIzleri_YalnizKittenGelir)");
            Assert.AreEqual(0, Regex.Matches(code, @"UiFactory\.Heading\(").Count, "pane başlığı UiBrushKit.Title (vermilion çentikli kit başlığı yok)");
            Assert.AreEqual(0, Regex.Matches(code, @"UiFxMode\.InkReveal|AddComponent<UiFx>").Count, "mürekkep çizimi kitin UiBrushMark'ında");
            Assert.AreEqual(1, Regex.Matches(code, @"UiBrushKit\.Title\(").Count, "tek başlık kurucusu (pane başına başlık 1)");
            Assert.AreEqual(1, Regex.Matches(code, @"UiBrushKit\.EdgeTrace\(").Count, "kenar izi tek (≤ 1)");
            Assert.LessOrEqual(Regex.Matches(code, @"UiBrushKit\.Divider\(").Count, 1, "ayırıcı tek kurucudan (pane başına ≤ 2)");
            Assert.LessOrEqual(Regex.Matches(code, @"UiBrushWash\.Column|UiBrushKit\.Wash\(").Count, UiBrushTokens.MaxWash, "yıkama ≤ 2");
            Assert.LessOrEqual(Regex.Matches(code, @"UiBrushKit\.(Tabs|Segments|SelectionMark)\(").Count, UiBrushTokens.MaxSelection, "C8 ≤ 2 (vermilion rezervi)");
        }

        /// <summary>
        /// WP-098B 2a · pane content rows on the kit: every value row is UiBrushKit Slider / Toggle / Cycle, every list row
        /// (picker, binding slot, achievement) a UiBrushKit ListRow, every scroller UiBrushKit.Scroll - no legacy uGUI slider,
        /// no Outline, no V3 Create, no width from a character count (KILAVUZ §4.4, §6). The C18 statuses carry state as
        /// glyph shape + word (G1: the choice is the check), each glyph in the G1 atlas, the words in the string table.
        /// </summary>
        [Test]
        public void Satirlar_KitBilesenleri_DurumGlifVeKelimeTasir()
        {
            string path = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Screens", "MainMenuScreen.cs");
            string code = UiOrnamentCensusTests.StripComments(File.ReadAllText(path));
            Assert.AreEqual(0, Regex.Matches(code, @"\bOutline\b|UiFactory\.NewSlider\(|\bSlider\s+\w+\s*=").Count, "eski satır yok: legacy Slider / Outline (envanter #7)");
            Assert.AreEqual(0, Regex.Matches(code, @"\b(UiSlider|UiSwitch|UiStepper|UiScrollView)\.Create\(").Count, "satır ve kaydırıcı kitten (UiBrushKit), V3 Create değil");
            Assert.AreEqual(0, Regex.Matches(code, @"\.Length\s*\*\s*\d").Count, "genişlik karakter sayısından tahmin edilmez (GetPreferredValues)");
            Assert.GreaterOrEqual(Regex.Matches(code, @"UiBrushKit\.(Slider|Toggle|Cycle)\(").Count, 20, "ayar / erişilebilirlik / kontrol değer satırları C14–C16");
            Assert.GreaterOrEqual(Regex.Matches(code, @"UiBrushKit\.ListRow\(").Count, 3, "unvan/rozet, bağ yuvası, başarım satırları C18");
            Assert.GreaterOrEqual(Regex.Matches(code, @"UiBrushKit\.Scroll\(").Count, 3, "kaydırma C35");

            foreach (string glyph in new[] { MainMenuScreen.PaneLook.CheckGlyph, MainMenuScreen.PaneLook.LockGlyph, MainMenuScreen.PaneLook.EarnedGlyph,
                         MainMenuScreen.PaneLook.ProgressGlyph, MainMenuScreen.PaneLook.ConflictGlyph, MainMenuScreen.PaneLook.ChangedGlyph })
            {
                Assert.IsTrue(UiArt.HasIcon(glyph), glyph + ": G1 atlasında");
            }

            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                UiStrings.Language = language;
                MainMenuScreen.PaneLook.OptionStatus(true, true, false, null, out string icon, out string word);
                Assert.AreEqual(MainMenuScreen.PaneLook.CheckGlyph, icon, language + ": seçim = check glifi (G1)");
                Assert.IsTrue(string.IsNullOrEmpty(word), "seçili açık satır kelime eklemez (U1)");
                MainMenuScreen.PaneLook.OptionStatus(true, false, false, null, out icon, out word);
                Assert.IsNull(icon, "seçili olmayan açık satırda glif yok");
                MainMenuScreen.PaneLook.OptionStatus(false, false, false, "LOCKED · ROUND 10", out icon, out word);
                Assert.AreEqual(MainMenuScreen.PaneLook.LockGlyph, icon, "kilitli = kilit glifi");
                Assert.AreEqual("LOCKED · ROUND 10", word, "kilit koşulu sözcükle (renk değil)");
                MainMenuScreen.PaneLook.OptionStatus(false, false, false, null, out icon, out word);
                Assert.AreEqual(UiStrings.Get("tree.state.locked", "LOCKED"), word, "koşulsuz kilit yine sözcük taşır");

                MainMenuScreen.PaneLook.AchievementStatus(true, false, out string earnedIcon, out string earned);
                MainMenuScreen.PaneLook.AchievementStatus(false, false, out string progressIcon, out string progress);
                Assert.AreNotEqual(earnedIcon, progressIcon, "kazanıldı / sürüyor ayrı biçim");
                Assert.AreEqual(UiStrings.Get("ach.earned", "EARNED"), earned);
                Assert.AreEqual(UiStrings.Get("ach.in_progress", "IN PROGRESS"), progress);

                MainMenuScreen.PaneLook.BindStatus(true, true, true, "W", out icon, out word);
                Assert.AreEqual(UiStrings.Get("controls.listening", "PRESS A KEY"), word, "dinlerken sözcük");
                MainMenuScreen.PaneLook.BindStatus(true, true, false, false, "A", out icon, out word);
                Assert.AreEqual(UiStrings.Get("controls.listening.pad", "PRESS A BUTTON · MENU CANCELS"), word, "R4 WP-105: pad yuvası düğme ister, Menü iptal eder");
                MainMenuScreen.PaneLook.BindStatus(false, true, true, "W", out icon, out word);
                Assert.AreEqual(MainMenuScreen.PaneLook.ConflictGlyph, icon, "çakışma uyarı glifinde (bildirim sözcüklerle)");
                Assert.AreEqual("W", word, "yuvanın değeri tuşun adı");
                MainMenuScreen.PaneLook.BindStatus(false, false, true, "W", out icon, out word);
                Assert.AreEqual(MainMenuScreen.PaneLook.ChangedGlyph, icon, "değişen yuva işaretli glif (lejant sözcükle)");
            }
        }

        /// <summary>
        /// WP-098B 2a · C13 measures from the kit: a setting row is the kit's hit (56) or MenuBody's line box + 8 - never a
        /// fixed band - and grows with TextScale 1.25; the list is the kit's 1008 where the preview well (≥ 240) still fits
        /// beside it, narrower down to 720, and takes the pane (no well) below that.
        /// </summary>
        [Test]
        public void Ayarlar_SatirOlcusuMetinden_ListeGenisligiKitten()
        {
            UiTokens.TextScale = 1f;
            float row = MainMenuScreen.PaneLook.SettingRowHeight;
            Assert.GreaterOrEqual(row, UiBrushTokens.RowHit, "satır isabeti ≥ 56");
            Assert.GreaterOrEqual(row, UiFactory.LineHeight(UiType.MenuBody) + 8f - 0.01f, "satır metinden");
            Assert.AreEqual(UiBrushTokens.RowStep, row + MainMenuScreen.PaneLook.SettingRowGap, 0.01f, "1,0'da adım 64");
            UiTokens.TextScale = 1.25f;
            Assert.GreaterOrEqual(MainMenuScreen.PaneLook.SettingRowHeight, UiFactory.LineHeight(UiType.MenuBody) + 8f - 0.01f, "1,25'te satır metinle büyür");
            Assert.GreaterOrEqual(MainMenuScreen.PaneLook.ListRowHeight, UiFactory.LineHeight(UiType.MenuSmall) + 8f - 0.01f, "yoğun liste satırı da metinden");
            Assert.GreaterOrEqual(MainMenuScreen.PaneLook.ListRowHeight, UiBrushTokens.RowHitCompact, "yoğun satır isabeti ≥ 48");

            bool well;
            float gutter = UiScrollView.Gutter;
            Assert.AreEqual(UiBrushTokens.ListWidth, MainMenuScreen.PaneLook.SettingsList(UiBrushTokens.ListWidth + gutter + 240f, out well), 0.01f);
            Assert.IsTrue(well, "1008 + kuyu sığıyor");
            float shared = MainMenuScreen.PaneLook.SettingsList(1100f, out well);
            Assert.IsTrue(well, "1100: kuyu en az 240 ile kalır");
            Assert.That(shared, Is.InRange(MainMenuScreen.PaneLook.SettingsListMin, (float)UiBrushTokens.ListWidth), "liste 720–1008");
            Assert.GreaterOrEqual(1100f - shared - gutter, 240f - 0.01f, "kuyu ≥ 240");
            float narrow = MainMenuScreen.PaneLook.SettingsList(900f, out well);
            Assert.IsFalse(well, "dar pane: kuyu çekilir");
            Assert.AreEqual(900f - gutter, narrow, 0.01f, "kuyusuz liste pane'i alır (oluk dışında boş şerit yok)");
            Assert.AreEqual(UiBrushTokens.ListWidth, MainMenuScreen.PaneLook.SettingsList(0f, out well), 0.01f, "yerleşmemiş pane: kitin genişliği");
        }

        [Test]
        public void Istatistik_TasmaYok_EnTr_Metin1ve125()
        {
            // The scroller's content width on the three hosts the U2 matrix runs: the batch Editor (4:3, 814), the Mac at
            // UI 1.2 (928), 1080p (1155) - pane column 71 % of the page, minus the card's 2 × 24 and the scroll gutter.
            float[] widths = { 814f, 928f, 1155f };
            var dense = new ProfileData
            {
                RunsPlayed = 48, HighestRound = 23, RoundsCompleted = 310, TotalKills = 98765, StandardKills = 90000,
                SpecialKills = 5000, EliteKills = 3000, UltraKills = 700, UnattributedKills = 65, ArmorPiecesBroken = 12345,
                ProfileLevel = 37, TotalXpEarned = 1234567, BestRunXp = 45678
            };
            var profiles = new[] { new ProfileData(), dense };
            var failures = new List<string>();
            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                UiStrings.Language = language;
                foreach (float scale in new[] { 1f, 1.25f })
                {
                    UiTokens.TextScale = scale;
                    foreach (float width in widths)
                    {
                        foreach (ProfileData profile in profiles)
                        {
                            string combo = language + "×" + scale + "@" + width + (profile.RunsPlayed > 0 ? "/dense" : "/empty");
                            RectTransform content = UiFactory.NewRect("Content", NewScreen());
                            content.anchorMin = new Vector2(0f, 1f);
                            content.anchorMax = new Vector2(0f, 1f);
                            content.pivot = new Vector2(0f, 1f);
                            content.sizeDelta = new Vector2(width, 0f);

                            StatisticsViewModel model = StatisticsViewModel.From(profile);
                            var rows = new List<RectTransform>();
                            float height = MainMenuScreen.BuildStatisticsBody(content, model, null, rows);
                            content.sizeDelta = new Vector2(width, height);

                            Assert.AreEqual(model.Runs.Count + model.Kills.Count + model.Progress.Count, rows.Count, combo + ": every row staggers");
                            foreach (TMP_Text text in content.GetComponentsInChildren<TMP_Text>(true))
                            {
                                if (string.IsNullOrEmpty(text.text)) continue;
                                text.ForceMeshUpdate();
                                if (text.isTextTruncated || text.isTextOverflowing)
                                {
                                    failures.Add(combo + " · " + text.name + " cut: " + text.text);
                                }
                            }

                            foreach (RectTransform child in content)
                            {
                                float bottom = -child.anchoredPosition.y + child.rect.height;
                                if (child.anchorMin.y > 0.99f && bottom > height + 0.5f)
                                {
                                    failures.Add(combo + " · " + child.name + " below the scroller's extent: " + bottom + " > " + height);
                                }
                            }

                            Debug.Log("[DD-TEST] wp098b.stats " + combo + " height=" + height.ToString("0"));
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, "WP-098B taşma (İstatistik):\n" + string.Join("\n", failures));
        }
    }
}
