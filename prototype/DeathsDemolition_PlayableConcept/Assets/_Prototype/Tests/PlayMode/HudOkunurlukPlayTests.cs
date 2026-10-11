using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-105 (R4 UX audit, part D: HUD legibility - AUDIT-VISUAL §15) · the live half of HudOkunurlukTests (top-down only).
    /// - HudKelimeleri_…: a standalone HUD at 1920 × 1080 (UI 1.0, text 1.0) and the Mac 3024 × 1964 (UI 1.2, text 1.25), drawn
    ///   through a camera on a target of that size with the scaler's Expand by hand (HudV4Tests' frame): every alert word
    ///   (LOW, EMPTY / RELOAD / LOW AMMO, SUSPENDED, SURROUNDED / BEHIND YOU / ELITE NEARBY, SPECIAL WEAPON ENDING) is built at
    ///   AlertWordSize (≥ 20) in the halo, the reload key's chip encloses a letter at the word's size, every secondary line
    ///   (the weapon line, "/ 100", "/ 30", "recovery in", the gold) is ≥ 16 px as drawn in the halo, and the words stay in
    ///   their clusters / chips / rows at both frames (V-52 / V-53 "TextScale and UI 1.2 do not overflow").
    /// - Duraklat_…: in a real run, PauseScreen.SetPaused(true) takes the HUD to α 0 (V-41: the pause's lower-left button
    ///   sat on the health readout) and SetPaused(false) brings it back.
    /// A temp ui-settings file - the user's is never touched.
    /// </summary>
    [Category("WP105")]
    public sealed class HudOkunurlukPlayTests
    {
        private readonly List<Object> _owned = new List<Object>();
        private string _settingsPath;

        /// <summary>A word's ink may pass its text box by the halo (≈ 1–2 px) and TMP's rounding.</summary>
        private const float InkSlack = 2f;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp105-hud-settings-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null)
                {
                    Object.Destroy(_owned[i]);
                }
            }

            _owned.Clear();
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }

        // ================================================================== V-52 / V-53: sizes, halo, fit

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HudKelimeleri_1080pVeMac_BoyHaleVeKumeIcinde()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return Frame(1920, 1080, 1f, 1f);
            yield return Frame(3024, 1964, 1.2f, 1.25f);
        }

        private IEnumerator Frame(int width, int height, float uiScale, float textScale)
        {
            string frame = width + "x" + height + " ui" + uiScale.ToString("0.0") + " text" + textScale.ToString("0.00");
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "R3: top-down only");

            var root = new GameObject("Wp105.HudFrame");
            _owned.Add(root);
            RunHudScreen hud = root.AddComponent<RunHudScreen>();
            Canvas canvas = hud.transform.Find(typeof(RunHudScreen).Name + ".Canvas").GetComponent<Canvas>();

            var target = new RenderTexture(width, height, 0);
            target.Create();
            _owned.Add(target);
            var cameraObject = new GameObject("Wp105.Camera");
            _owned.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = Mathf.Min(width / (1920f / uiScale), height / (1080f / uiScale));
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var screen = (RectTransform)canvas.transform;
            var failures = new List<string>();

            // ---- V-52: the alert words - the size, the halo
            float alert = RunHudScreen.AlertWordSize(textScale);
            Assert.GreaterOrEqual(alert, 20f, frame);
            TMP_Text low = Find<TMP_Text>(hud.transform, "Health", "Low");
            TMP_Text caption = Find<TMP_Text>(hud.transform, "ClassicWeapon", "ReloadCaption");
            TMP_Text suspended = Find<TMP_Text>(hud.transform, "ClassicWeapon", "Suspended");
            TMP_Text ending = Find<TMP_Text>(hud.transform, "FinalSeconds", "Text");
            var alerts = new List<TMP_Text> { low, caption, suspended, ending };
            Transform threatRow = screen.Find("ThreatRow");
            Assert.IsNotNull(threatRow, "ThreatRow");
            for (int i = 0; i < threatRow.childCount; i++)
            {
                alerts.Add(threatRow.GetChild(i).GetComponentInChildren<TMP_Text>(true));
            }

            foreach (TMP_Text word in alerts)
            {
                if (word.fontSize + 1e-3f < 20f || Mathf.Abs(word.fontSize - alert) > 1e-3f)
                {
                    failures.Add(word.name + " (" + word.transform.parent.name + "): uyarı kelimesi " + word.fontSize + " px, beklenen " + alert);
                }

                if (!UiHudInk.HasHalo(word))
                {
                    failures.Add(word.name + " (" + word.transform.parent.name + "): hale yok");
                }
            }

            // The reload key: a chip round a letter at the word's size.
            RectTransform prompt = Find<RectTransform>(hud.transform, "ClassicWeapon", "ReloadPrompt");
            TMP_Text key = prompt.Find("Text").GetComponent<TMP_Text>();
            Assert.GreaterOrEqual(key.fontSize, alert - 1e-3f, frame + ": tuşun harfi kelimenin boyunda");
            Assert.GreaterOrEqual(prompt.sizeDelta.y, alert + 6f - 1e-3f, frame + ": tuş çipi harfi sarar");

            // ---- V-53: the secondary lines - the drawn size (the calm posture's scale), the halo
            var secondary = new[]
            {
                (Text: Find<TMP_Text>(hud.transform, "MaxCalm", "Max"), Calm: true),
                (Text: Find<TMP_Text>(hud.transform, "Health", "State"), Calm: true),
                (Text: Find<TMP_Text>(hud.transform, "WeaponCalm", "Name"), Calm: true),
                (Text: Find<TMP_Text>(hud.transform, "Ammo", "Max"), Calm: true),
                (Text: Find<TMP_Text>(hud.transform, "Gold", "Value"), Calm: false)
            };
            foreach (var line in secondary)
            {
                float drawn = line.Text.fontSize * (line.Calm ? RunHudScreen.CalmPostureScale : 1f);
                if (drawn + 0.05f < 16f)   // 17 × 0.94 = 15.98: the floor within half a pixel (HudCoverage cap)
                {
                    failures.Add(line.Text.name + " (" + line.Text.transform.parent.name + "): ikincil satır çizildiğinde " + drawn.ToString("0.0") + " px < 16");
                }

                if (!UiHudInk.HasHalo(line.Text))
                {
                    failures.Add(line.Text.name + " (" + line.Text.transform.parent.name + "): hale yok");
                }
            }

            // ---- the words stay where they live (TextScale 1.25 and UI 1.2 included)
            Rect health = CanvasRectOf(screen, ClusterNamed(hud, "Health"));
            Rect weapon = CanvasRectOf(screen, ClusterNamed(hud, "ClassicWeapon"));
            Rect round = CanvasRectOf(screen, ClusterNamed(hud, "Round"));
            Rect canvasRect = screen.rect;

            low.gameObject.SetActive(true);
            Inside(failures, frame + " LOW", InkRect(screen, low), health, InkSlack);
            low.gameObject.SetActive(false);

            string emptyWord = UiStrings.Get("hud.ammo.empty", "EMPTY");
            string reloadWord = UiStrings.Get("hud.ammo.reload_hint", "RELOAD");
            foreach (string word in new[] { emptyWord, reloadWord, UiStrings.Get("hud.ammo.low", "LOW AMMO") })
            {
                caption.text = word;
                Inside(failures, frame + " '" + word + "'", InkRect(screen, caption), weapon, InkSlack);
            }

            // EMPTY / RELOAD carry the key: the chip past the wider word, inside the cluster.
            prompt.gameObject.SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (string word in new[] { emptyWord, reloadWord })
            {
                caption.text = word;
                Rect ink = InkRect(screen, caption);
                Rect chip = CanvasRectOf(screen, prompt);
                Inside(failures, frame + " key chip", chip, weapon, 0.5f);
                if (chip.xMin < ink.xMax - 0.5f)
                {
                    failures.Add(frame + ": tuş çipi '" + word + "' kelimesine biniyor (" + chip + " vs " + ink + ")");
                }
            }

            prompt.gameObject.SetActive(false);
            caption.text = string.Empty;

            suspended.gameObject.SetActive(true);
            Inside(failures, frame + " SUSPENDED", InkRect(screen, suspended), weapon, InkSlack);
            suspended.gameObject.SetActive(false);

            // The calm weapon line: in the cluster's width (its box runs 16 px past the left edge since 8-R; the words do not), its
            // ink at most 4 px over the cluster's top at text 1.25 (nothing lives there).
            TMP_Text name = Find<TMP_Text>(hud.transform, "WeaponCalm", "Name");
            name.text = "HINAWA  ·  no upgrades";
            Rect nameInk = InkRect(screen, name);
            if (nameInk.xMin < weapon.xMin - InkSlack || nameInk.xMax > weapon.xMax + InkSlack || nameInk.yMax > weapon.yMax + 4f)
            {
                failures.Add(frame + ": silah satırı kümeden taşıyor " + nameInk + " vs " + weapon);
            }

            // "recovery in": the health cluster's width, clear of the screen's bottom edge.
            TMP_Text state = Find<TMP_Text>(hud.transform, "Health", "State");
            state.text = "recovery in 4.5s";
            Rect stateInk = InkRect(screen, state);
            if (stateInk.xMin < health.xMin - InkSlack || stateInk.xMax > health.xMax + InkSlack || stateInk.yMin < canvasRect.yMin + 16f)
            {
                failures.Add(frame + ": recovery satırı yerinde değil " + stateInk + " (küme " + health + ")");
            }

            state.text = string.Empty;

            // The threat chips: every word a chip can carry fits its chip; the row stays top-centre, on screen, off the round.
            Rect threats = CanvasRectOf(screen, (RectTransform)threatRow);
            Assert.AreEqual(canvasRect.yMax - 32f, threats.yMax, 0.5f, frame + ": tehdit satırı InkMargin'de");
            Assert.AreEqual(canvasRect.center.x, threats.center.x, 1f, frame + ": tehdit satırı ortada");
            Inside(failures, frame + " threat row", threats, canvasRect, 0f);
            if (threats.Overlaps(round))
            {
                failures.Add(frame + ": tehdit satırı raunda biniyor " + threats + " vs " + round);
            }

            for (int i = 0; i < threatRow.childCount; i++)
            {
                TMP_Text label = threatRow.GetChild(i).GetComponentInChildren<TMP_Text>(true);
                string original = label.text;
                foreach (string word in new[]
                         {
                             UiStrings.Get("hud.threat.surrounded", "SURROUNDED"), UiStrings.Get("hud.threat.behind", "BEHIND YOU"),
                             UiStrings.Get("hud.threat.elite", "ELITE NEARBY"), UiStrings.Get("hud.threat.ultra", "ULTRA NEARBY")
                         })
                {
                    float need = label.GetPreferredValues(word).x;
                    if (need > label.rectTransform.rect.width + 0.5f)
                    {
                        failures.Add(frame + ": tehdit çipi " + i + " '" + word + "' sığmıyor (" + need.ToString("0.0") + " > " + label.rectTransform.rect.width.ToString("0.0") + ")");
                    }
                }

                label.text = original;
            }

            float endingNeed = ending.GetPreferredValues(ending.text).x;
            if (endingNeed > ending.rectTransform.rect.width + 0.5f)
            {
                failures.Add(frame + ": son saniye satırı sığmıyor (" + endingNeed.ToString("0.0") + " > " + ending.rectTransform.rect.width + ")");
            }

            Debug.Log("[DD-TEST] wp105.hudWords " + frame + " alert=" + alert + " secondaryCalm=" + RunHudScreen.SecondaryTextSize(textScale, true) +
                      " chip=" + prompt.sizeDelta + " threats=" + threats + " failures=" + failures.Count);
            Assert.IsEmpty(failures, frame + ":\n" + string.Join("\n", failures));
            Object.Destroy(root);
            yield return null;
        }

        // ================================================================== V-41: the pause hides the HUD

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Duraklat_GercekKosuda_HudAlfaSifir_KapaninaGeriGelir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(hud, "HUD yok");
            Assert.IsNotNull(pause, "PauseScreen yok");
            CanvasGroup group = hud.HudGroup;
            Assert.IsNotNull(group, "HUD tuvalinin grubu");
            yield return Bekle(() => group.alpha > 0.99f, 10f, "HUD'un giriş solması bitmedi");
            Assert.IsFalse(hud.HiddenForPause, "oyunda HUD görünür");
            TMP_Text healthValue = Find<TMP_Text>(hud.transform, "Health", "Value");
            float fast = UiMotionTokens.Seconds(UiDuration.Fast);

            pause.SetPaused(true);
            Assert.IsTrue(pause.IsPaused, "Pause açılmadı");
            Assert.IsTrue(UiNavigation.IsPaused, "duraklatma bayrağı (UiNavigation)");
            yield return Bekle(() => hud.HiddenForPause, 2f, "HUD duraklatmayı okumadı");
            yield return new WaitForSecondsRealtime(fast + 0.2f);
            Canvas.ForceUpdateCanvases();
            Assert.LessOrEqual(group.alpha, 0.01f, "V-41: duraklat açıkken HUD α 0");
            // The health readout (the one the pause's lower-left button sat on) takes that α: no group between it and the HUD
            // canvas opts out of its parents (only the death beat's health group does, and the pause cannot open there).
            for (Transform p = healthValue.transform; p != null && p != group.transform; p = p.parent)
            {
                CanvasGroup own = p.GetComponent<CanvasGroup>();
                Assert.IsFalse(own != null && own.ignoreParentGroups, "V-41: can okuması HUD grubunun dışında kalıyor (" + p.name + ")");
            }

            pause.SetPaused(false);
            yield return Bekle(() => !hud.HiddenForPause, 2f, "HUD duraklatmanın kapandığını okumadı");
            yield return new WaitForSecondsRealtime(fast + 0.2f);
            Canvas.ForceUpdateCanvases();
            Assert.GreaterOrEqual(group.alpha, 0.99f, "duraklat kapanınca HUD geri gelir");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== helpers

        private static void Inside(List<string> failures, string what, Rect inner, Rect outer, float slack)
        {
            if (inner.xMin < outer.xMin - slack || inner.xMax > outer.xMax + slack || inner.yMin < outer.yMin - slack || inner.yMax > outer.yMax + slack)
            {
                failures.Add(what + " taşıyor: " + inner + " vs " + outer);
            }
        }

        /// <summary>A text's drawn letters (TMP's text bounds) in the canvas's space - not its layout box.</summary>
        private static Rect InkRect(RectTransform canvas, TMP_Text text)
        {
            text.ForceMeshUpdate();
            Bounds bounds = text.textBounds;
            Vector3 a = canvas.InverseTransformPoint(text.transform.TransformPoint(bounds.min));
            Vector3 b = canvas.InverseTransformPoint(text.transform.TransformPoint(bounds.max));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static RectTransform ClusterNamed(RunHudScreen hud, string name)
        {
            foreach (RectTransform cluster in hud.Clusters)
            {
                if (cluster.name == name)
                {
                    return cluster;
                }
            }

            Assert.Fail("no " + name + " cluster");
            return null;
        }

        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>The rect in the canvas's own space (centre origin), from its world corners.</summary>
        private static Rect CanvasRectOf(RectTransform canvas, RectTransform rect)
        {
            Assert.IsNotNull(rect, "rect");
            rect.GetWorldCorners(s_corners);
            Vector3 a = canvas.InverseTransformPoint(s_corners[0]);
            Vector3 b = canvas.InverseTransformPoint(s_corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static T Find<T>(Transform root, string block, string name) where T : Component
        {
            foreach (T c in root.GetComponentsInChildren<T>(true))
            {
                if (c.name != name)
                {
                    continue;
                }

                for (Transform p = c.transform.parent; p != null; p = p.parent)
                {
                    if (p.name == block)
                    {
                        return c;
                    }
                }
            }

            Assert.Fail("not found: " + block + "/" + name);
            return null;
        }
    }
}
