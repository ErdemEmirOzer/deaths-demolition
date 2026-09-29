using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using UnityEngine.EventSystems;
using DD.Prototype.Inputs;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>Narrow regression: reachable controls footer and existing Profile/Results copy, at the Mac frame.</summary>
    public sealed class UiControlsProfileResultsTests
    {
        private const int Width = 3024;
        private const int Height = 1964;
        private readonly List<GameObject> _owned = new List<GameObject>();
        private PrototypeConfig _previousConfig;
        private string _settingsPath;
        private RenderTexture _target;
        private Camera _camera;
        private RenderTexture _previousTarget;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-s09-ui-followup-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiSettings.EnsureLoaded();
            UiSettings.UiScale = 1.2f;
            UiSettings.TextScale = 1.25f;
            UiSettings.ReducedMotion = true;
            UiNavigation.Model.Clear();
            _previousConfig = PrototypeConfig.Active;
            PrototypeConfig config = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(config, "The shipped config is required for this target fixture.");
            PrototypeConfig.SetActive(config);
            ProfileService.EnsureLoaded();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Assembly profile isolation must be active.");
            bool installerExisted = PrototypeInputInstaller.Instance != null;
            PrototypeInputInstaller installer = PrototypeInputInstaller.EnsureExists();
            if (!installerExisted) _owned.Add(installer.gameObject);
            Assert.IsNotNull(installer.Remap);
            bool eventSystemExisted = EventSystem.current != null;
            UiFactory.EnsureEventSystem();
            if (!eventSystemExisted) _owned.Add(EventSystem.current.gameObject);
            var previousCameras = new HashSet<Camera>(Object.FindObjectsOfType<Camera>());
            _camera = UiFactory.EnsureUiCamera();
            if (!previousCameras.Contains(_camera)) _owned.Add(_camera.gameObject);
            _previousTarget = _camera.targetTexture;
            _target = new RenderTexture(Width, Height, 24);
            _target.Create();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            if (_camera != null) _camera.targetTexture = _previousTarget;
            if (_target != null) { _target.Release(); Object.DestroyImmediate(_target); }
            UiNavigation.Model.Clear();
            PrototypeConfig.SetActive(_previousConfig);
            UiSettings.SetPathOverride(null);
            UiSettings.EnsureLoaded();
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        }

        [UnityTest]
        public IEnumerator Controls_Profile_Results_Mac_TR() { yield return Prove(UiStrings.Turkish); }

        [UnityTest]
        public IEnumerator Controls_Profile_Results_Mac_EN() { yield return Prove(UiStrings.English); }

        private T Create<T>() where T : UiScreen
        {
            // Screens use the normal camera while Awake builds; the owned render target then supplies exact Mac geometry.
            _camera.targetTexture = null;
            var go = new GameObject("S09Target." + typeof(T).Name);
            _owned.Add(go);
            T screen = go.AddComponent<T>();
            _camera.targetTexture = _target;
            Canvas canvas = screen.GetComponentInChildren<Canvas>();
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            // Same Expand computation as UiFactory for 3024x1964 / UI1.2, independent of the Editor GameView window.
            canvas.scaleFactor = Mathf.Min(Width / (1920f / 1.2f), Height / (1080f / 1.2f));
            Canvas.ForceUpdateCanvases();
            Assert.That(((RectTransform)canvas.transform).rect.width, Is.EqualTo(1600f).Within(1f));
            Assert.That(((RectTransform)canvas.transform).rect.height, Is.EqualTo(Height / canvas.scaleFactor).Within(1f));
            return screen;
        }

        private IEnumerator Prove(string language)
        {
            UiSettings.Language = language;
            Assert.IsNotNull(PrototypeInputInstaller.Instance?.Remap, "Real remap entries must be present; an empty-column test does not cover the defect.");
            MainMenuScreen menu = Create<MainMenuScreen>();
            yield return null;
            menu.ShowPane(UiContext.Controls);
            yield return null;
            Canvas.ForceUpdateCanvases();
            UiScrollView scroll = menu.GetComponentInChildren<UiScrollView>();
            Assert.IsNotNull(scroll);
            Transform columns = scroll.Content.Find("Columns");
            UiButton reset = scroll.Content.Find("Footer/ResetBindings").GetComponent<UiButton>();
            UiButton rumble = columns.Find("Gamepad/Rumble").GetComponent<UiButton>();
            Assert.Greater(columns.Find("Keyboard").childCount, 5, "Use the actual binding rows.");
            Assert.GreaterOrEqual(ReferenceRect((RectTransform)rumble.transform).yMin, ReferenceRect((RectTransform)columns).yMin - 0.5f, "Right options fit the measured columns band.");
            Assert.Less(ReferenceRect((RectTransform)reset.transform.parent).yMax, ReferenceRect((RectTransform)rumble.transform).yMin, "Footer is below Rumble; it cannot overlay the right column.");
            UiInput.Focus(rumble);
            yield return null;
            scroll.Reveal((RectTransform)rumble.transform, false);
            AssertInside(scroll.Viewport, (RectTransform)rumble.transform, "Rumble after focus reveal");
            UiInput.Focus(reset);
            yield return null;
            scroll.Reveal((RectTransform)reset.transform, false);
            AssertInside(scroll.Viewport, (RectTransform)reset.transform, "Reset after focus reveal");
            AssertInside(scroll.Viewport, (RectTransform)rumble.transform, "Rumble remains visible beside the footer");
            reset.LabelText.ForceMeshUpdate();
            Debug.Log("[S09-UI] resetMeasurement language=" + language + " preferredWidth=" + reset.LabelText.GetPreferredValues(reset.LabelText.text).x +
                " labelRectWidth=" + reset.LabelText.rectTransform.rect.width + " buttonWidth=" + ((RectTransform)reset.transform).rect.width +
                " truncated=" + reset.LabelText.isTextTruncated + " viewportRefPx=" + ReferenceRect(scroll.Viewport));
            TMP_Text file = scroll.Content.Find("Footer/File").GetComponent<TMP_Text>();
            Assert.Greater(ReferenceRect(file.rectTransform).xMin, ReferenceRect((RectTransform)reset.transform).xMax,
                "File text band follows the measured reset width with the existing gap.");
            AssertInside(scroll.Viewport, file.rectTransform, "Footer File after reset focus");
            file.ForceMeshUpdate();
            Debug.Log("[S09-UI] fileMeasurement language=" + language + " preferredWidth=" + file.GetPreferredValues(file.text).x +
                " labelRectWidth=" + file.rectTransform.rect.width + " truncated=" + file.isTextTruncated);
            AssertFits(file);
            AssertFits(reset.LabelText);
            AssertFits(rumble.LabelText);
            Assert.That(reset.LabelText.text, Is.EqualTo(UiStrings.Get("controls.reset")));
            Transform pane = scroll.transform.parent;
            foreach (UiButton slot in columns.GetComponentsInChildren<UiButton>())
            {
                if (!slot.name.StartsWith("Bind.", StringComparison.Ordinal)) continue;
                string id = slot.name.Substring(5);
                string path = (string)typeof(MainMenuScreen).GetMethod("FindSlotPath", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, new object[] { id });
                Assert.AreSame(slot.transform, pane.Find(path), "Completion refocus path still reaches its original binding button.");
            }
            Debug.Log("[S09-UI] controls language=" + language + " frame=" + Width + "x" + Height + " canvas=" + ((RectTransform)menu.GetComponentInChildren<Canvas>().transform).rect.size + " scrollOffset=" + scroll.Offset + " reset=" + ReferenceRect((RectTransform)reset.transform) + " rumble=" + ReferenceRect((RectTransform)rumble.transform));
            yield return Capture(language + "-controls-footer");
            menu.ShowPane(UiContext.Profile);
            yield return null;
            UiCosmeticLine title = new UiCosmeticLine(ProfileService.CosmeticsSource.Titles[0]);
            string expectedTitle = language == UiStrings.Turkish ? "Unvan: Blooded" : "Title: Blooded";
            Assert.AreEqual(expectedTitle, title.Title);
            Assert.AreEqual(language == UiStrings.Turkish ? "Profil unvanı - profil bölmesi unvanları desteklediğinde adının yanında gösterilir" : UnlockCatalog.Find(title.Id).Detail, title.Detail);
            Assert.IsTrue(Contains(menu, "Blooded"), "Profile retains the existing cosmetic identity name.");
            UiUnlockLine branch = ProfileViewModel.From(ProfileService.Current, ProfileServiceLookup.Instance).Unlocks.Find(x => x.Title != null && x.Title.Contains(language == UiStrings.Turkish ? "Zincir" : "Chain"));
            Assert.IsNotNull(branch.Title, "The existing Chain unlock branch uses the language key.");
            Object.DestroyImmediate(menu.gameObject);
            ResultsScreen results = Create<ResultsScreen>();
            yield return null;
            // Copy proof uses the real reward-card builder at the current card width; no run or transaction is fabricated.
            RectTransform page = (RectTransform)results.GetType().BaseType.GetProperty("Content", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(results);
            RectTransform host = UiFactory.NewRect("S09RewardCopyProof", page);
            UiFactory.Fill(host);
            host.anchorMin = new Vector2(0.60f, 0f);
            host.offsetMax = new Vector2(0f, -200f);
            host.offsetMin = new Vector2(0f, 100f);
            var cards = new UiStack(host);
            MethodInfo builder = typeof(ResultsScreen).GetMethod("BuildRewardEntry", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (ProfileUnlockEntry entry in new[]
            {
                new ProfileUnlockEntry("Yabusame", "Classic weapon - selectable before your next run"),
                new ProfileUnlockEntry("First Blood", "Defeat your first enemy.")
            })
            {
                RectTransform card = (RectTransform)builder.Invoke(results, new object[] { host, entry, entry.Title == "First Blood" ? UiColor.StateSuccess : UiColor.TextSecondary });
                cards.Add(card, UiFactory.ScaledBand(92f));
                cards.Gap(UiSpace.S);
            }
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(Contains(results, language == UiStrings.Turkish ? "Klasik silah - sonraki koşudan önce seçilebilir" : "Classic weapon - selectable before your next run"));
            Assert.IsTrue(Contains(results, language == UiStrings.Turkish ? "İlk düşmanını yen." : "Defeat your first enemy."));
            foreach (TMP_Text text in host.GetComponentsInChildren<TMP_Text>())
                if (text.name == "Detail") AssertFits(text);
            CheckAllCatalogCopy(language);
            Debug.Log("[S09-UI] localized catalog descriptions language=" + language + " unlocks=" + UnlockCatalog.Entries.Count + " achievements=" + AchievementCatalog.Definitions.Count + " keys=" + UiStrings.Count);
        }

        private static void CheckAllCatalogCopy(string language)
        {
            Type copy = typeof(ProfileViewModel).Assembly.GetType("DD.Prototype.UI.ProfileContentCopy");
            MethodInfo method = copy.GetMethod("RewardDetail", BindingFlags.Static | BindingFlags.Public);
            foreach (UnlockDefinition entry in UnlockCatalog.Entries) CheckCopy(method, entry.DisplayName, entry.Detail, language);
            foreach (AchievementDefinition entry in AchievementCatalog.Definitions) CheckCopy(method, entry.DisplayName, entry.Description, language);
            Assert.AreEqual("Future detail {unknown}", method.Invoke(null, new object[] { "Unknown future identity", "Future detail {unknown}" }), "Unknown content remains unchanged.");
        }

        private static void CheckCopy(MethodInfo method, string title, string source, string language)
        {
            string value = (string)method.Invoke(null, new object[] { title, source });
            if (language == UiStrings.English) Assert.AreEqual(source, value, title);
            else
            {
                Assert.AreNotEqual(source, value, title + " description localized");
                Assert.IsFalse(value.Contains("{") || value.Contains("}"), title + " has no unresolved placeholders");
            }
        }

        private static bool Contains(UiScreen screen, string value)
        {
            foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>()) if (text.text.Contains(value)) return true;
            return false;
        }

        private static Rect ReferenceRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Transform reference = rect.GetComponentInParent<Canvas>().rootCanvas.transform;
            Vector3 min = reference.InverseTransformPoint(corners[0]);
            Vector3 max = reference.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void AssertInside(RectTransform viewport, RectTransform rect, string why)
        {
            Assert.AreSame(viewport.GetComponentInParent<Canvas>().rootCanvas, rect.GetComponentInParent<Canvas>().rootCanvas,
                "Containment uses one common canvas reference frame.");
            Rect outer = ReferenceRect(viewport), inner = ReferenceRect(rect);
            Debug.Log("[S09-UI] viewportContainment " + why + " viewportRefPx=" + outer + " targetRefPx=" + inner);
            Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - 0.5f), why);
            Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + 0.5f), why);
            Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - 0.5f), why);
            Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + 0.5f), why);
        }

        private static void AssertFits(TMP_Text text)
        {
            text.ForceMeshUpdate();
            Assert.IsFalse(text.isTextTruncated, text.text);
            Assert.That(text.textBounds.size.x, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 1f), text.text);
            Assert.That(text.textBounds.size.y, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1f), text.text);
        }

        private IEnumerator Capture(string name)
        {
            yield return null;
            yield return null; // The ordinary pipeline has rendered the camera's owned target, including screen UI.
            string folder = Environment.GetEnvironmentVariable("S09_TARGET_UI_PROOF_OUT");
            if (string.IsNullOrEmpty(folder)) yield break;
            Directory.CreateDirectory(folder);
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = _target;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
        }
    }
}
