using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-022 · AP-012 UI art contract (ADR-022-1): the atlases in Assets/_Prototype/UI/Art are
    /// sliced by the manifest, every key the screens ask for exists, frames carry a
    /// 9-slice border, nothing exceeds the UI texture class, the shipped UI art stays
    /// under its budget, the theme resolves them, and the HUD tokens keep WCAG AA over
    /// the five environment roles the camera can put behind a panel.
    /// </summary>
    [TestFixture]
    [Category("WP022")]
    public sealed class UiArtThemeTests
    {
        private const string ArtFolder = "Assets/_Prototype/UI/Art/Resources/UI/AP012";
        private const long UiArtBudgetBytes = 12L * 1024 * 1024; // ADR-015-1 UI class, package brief

        private static readonly string[] HudKeys =
        {
            "Hud.Gold", "Hud.Special", "Hud.Round", "Hud.Health", "Hud.Enemies", "Hud.Timer"
        };

        private static readonly string[] MenuKeys =
        {
            "Menu.Settings", "Menu.Audio", "Menu.Graphics", "Menu.Gamepad", "Menu.Keyboard",
            "Menu.Profile", "Menu.Statistics", "Menu.Achievements", "Menu.Back", "Menu.Confirm"
        };

        private static readonly string[] FrameKeys =
        {
            "Frame.HudPanel", "Frame.Button", "Frame.Card", "Frame.Chip",
            "Medal.Common", "Medal.Rare", "Medal.Epic", "Ornament.Divider",
            "Reticle.Ticks", "Reticle.Ring", "Cursor.Arrow", "Focus.Brackets"
        };

        [TearDown]
        public void TearDown()
        {
            UiTheme.Override(null);
        }

        [Test]
        public void Atlaslar_ManifestteAdlandirilmisHerSpriteYuklenir()
        {
            Sprite[] icons = Resources.LoadAll<Sprite>(UiTheme.IconAtlas);
            Sprite[] frames = Resources.LoadAll<Sprite>(UiTheme.FrameAtlas);
            Assert.IsNotNull(icons);
            Assert.IsNotNull(frames);
            Assert.AreEqual(32, icons.Length, "ikon atlası 32 sprite'a dilimlenmeli (16 upgrade/weapon + 16 HUD/menu)");
            Assert.AreEqual(12, frames.Length, "çerçeve atlası 12 sprite'a dilimlenmeli");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Sprite s in icons) Assert.IsTrue(names.Add(s.name), "ikon adı tekrarlı: " + s.name);
            foreach (Sprite s in frames) Assert.IsTrue(names.Add(s.name), "çerçeve adı tekrarlı: " + s.name);

            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                Assert.IsTrue(names.Contains("Upgrade." + branch), "eksik upgrade ikonu: " + branch);
            }

            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                Assert.IsTrue(names.Contains("Weapon." + id), "eksik silah ikonu: " + id);
            }

            foreach (SpecialWeaponId id in Enum.GetValues(typeof(SpecialWeaponId)))
            {
                Assert.IsTrue(names.Contains("Special." + id), "eksik special ikonu: " + id);
            }

            foreach (string key in HudKeys) Assert.IsTrue(names.Contains(key), "eksik HUD ikonu: " + key);
            foreach (string key in MenuKeys) Assert.IsTrue(names.Contains(key), "eksik menü ikonu: " + key);
            foreach (string key in FrameKeys) Assert.IsTrue(names.Contains(key), "eksik çerçeve: " + key);
        }

        [Test]
        public void Cerceveler_DokuzDilimKenariTasir_IkonlarBeyazGlif()
        {
            Sprite[] frames = Resources.LoadAll<Sprite>(UiTheme.FrameAtlas);
            foreach (Sprite s in frames)
            {
                if (!s.name.StartsWith("Frame.", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.Greater(s.border.x, 0f, s.name + " 9-slice kenarı sıfır");
                Assert.Greater(s.border.y, 0f, s.name + " 9-slice kenarı sıfır");
                Assert.Less(s.border.x * 2f, s.rect.width, s.name + " kenar toplamı sprite genişliğini aşıyor");
                Assert.Less(s.border.y * 2f, s.rect.height, s.name + " kenar toplamı sprite yüksekliğini aşıyor");
            }

            foreach (Sprite s in Resources.LoadAll<Sprite>(UiTheme.IconAtlas))
            {
                Assert.AreEqual(128f, s.rect.width, 0.5f, s.name + " ikon hücresi 128 px değil");
                Assert.AreEqual(128f, s.rect.height, 0.5f, s.name + " ikon hücresi 128 px değil");
            }
        }

        [Test]
        public void DokuSinifi_1024VeButce()
        {
            string[] files = Directory.GetFiles(ArtFolder, "*.png", SearchOption.TopDirectoryOnly);
            Assert.Greater(files.Length, 0, "AP-012 PNG'leri bulunamadı: " + ArtFolder);

            long total = 0;
            foreach (string file in files)
            {
                total += new FileInfo(file).Length;
            }

            Assert.LessOrEqual(total, UiArtBudgetBytes,
                "UI sanatı kaynak boyutu bütçeyi aşıyor: " + total + " B (sıkıştırma öncesi üst sınır)");

            var textures = Resources.LoadAll<Texture2D>(UiTheme.AtlasFolder.TrimEnd('/'));
            Assert.Greater(textures.Length, 0);
            foreach (Texture2D t in textures)
            {
                Assert.LessOrEqual(Mathf.Max(t.width, t.height), 1024, t.name + " UI sınıfı 1024² tavanını aşıyor (ADR-015-1)");
                Assert.AreEqual(1, t.mipmapCount, t.name + " UI dokusunda mipmap olmamalı");
            }
        }

        [Test]
        public void Tema_KaynaklardanCozulur_YedekKitAsilaKopmaz()
        {
            UiTheme theme = UiTheme.Active;
            Assert.IsNotNull(theme);
            Assert.IsNotNull(theme.Icon("Hud.Gold"), "tema ikon vermedi");
            Assert.IsNotNull(theme.Frame("Frame.HudPanel"), "tema çerçeve vermedi");
            Assert.IsNotNull(theme.MenuPlate, "menü plakası yok");
            Assert.IsNotNull(theme.HudMarker, "AP-010 HUD işareti yok");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsNotNull(theme.Skin(i), "skin önizlemesi yok: " + (i + 1));
            }

            Assert.IsTrue(UiArt.HasFrame(UiFrame.Card));
            Assert.AreSame(theme.Icon("Upgrade.Piercing"), UiArt.UpgradeIcon(UpgradeBranch.Piercing));
            Assert.AreSame(theme.Icon("Weapon.AutoSmg"), UiArt.WeaponIcon(ClassicWeaponId.AutoSmg));

            // Art absent: every lookup falls back to the procedural kit and nothing throws.
            var empty = ScriptableObject.CreateInstance<UiTheme>();
            UiTheme.Override(empty);
            Assert.IsNull(UiArt.Icon("Hud.Gold"));
            Assert.IsFalse(UiArt.HasFrame(UiFrame.HudPanel));
            Assert.AreSame(UiSprites.Card, UiArt.Frame(UiFrame.Card));
            Assert.AreSame(UiSprites.Control, UiArt.Frame(UiFrame.Button));
            Assert.AreSame(UiSprites.Ring, UiArt.Medal(UiMedal.Epic));
            Assert.AreSame(UiSprites.Disc, UiArt.HudMarker());
            Assert.IsNull(UiArt.SkinPreview(0));
            Assert.IsNull(UiArt.MenuPlate());
            UnityEngine.Object.DestroyImmediate(empty);
        }

        /// <summary>
        /// DEC-077 / REQ-105: the HUD panel is HudInk at alpha 0.92 over whatever the
        /// camera shows. The five roles below are the brightest and darkest opaque
        /// surfaces of the DEC-076 palette (style guide §2) plus the sky/mist colour;
        /// the panel text and bars must clear WCAG AA over every one of them.
        /// </summary>
        [Test]
        public void HudKontrasti_BesCevreRoluUzerinde_AA()
        {
            var world = new (string Name, Color Colour)[]
            {
                ("plaster", Hex(0xCD, 0xCB, 0xC1)),
                ("sky-mist", Hex(0xD0, 0xD7, 0xDE)),
                ("stone", Hex(0x6E, 0x6B, 0x66)),
                ("moss", Hex(0x4E, 0x5A, 0x3E)),
                ("earth", Hex(0x4D, 0x46, 0x39))
            };

            var text = new (UiColor Token, float Floor)[]
            {
                (UiColor.TextPrimary, 4.5f),
                (UiColor.TextSecondary, 4.5f),
                (UiColor.StateGold, 4.5f),
                (UiColor.StateDanger, 4.5f),
                (UiColor.BarHealth, 3f),
                (UiColor.BarReload, 3f),
                (UiColor.BarSpecial, 3f)
            };

            var report = new System.Text.StringBuilder();
            int failures = 0;
            foreach (var w in world)
            {
                Color panel = UiContrast.Composite(UiTokens.Get(UiColor.HudInk), w.Colour);
                foreach (var t in text)
                {
                    Color fg = UiContrast.Composite(UiTokens.Get(t.Token), panel);
                    float ratio = UiContrast.Ratio(fg, panel);
                    report.AppendLine(t.Token + " on HudInk over " + w.Name + ": " + ratio.ToString("0.00") + ":1 (floor " + t.Floor + ")");
                    if (ratio < t.Floor)
                    {
                        failures++;
                    }
                }
            }

            Debug.Log("[DD-TEST] wp022.hudContrast\n" + report);
            Assert.AreEqual(0, failures, "HUD kontrastı beş çevre rolünden birinde eşiğin altında:\n" + report);
        }

        [Test]
        public void Ayarlar_OlcekAraliginaKirpilir_JsonDosyasi_PlayerPrefsYok()
        {
            string path = Path.Combine(Path.GetTempPath(), "dd-wp022b-ui-settings-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(path);
            try
            {
                UiSettings.UiScale = 5f;
                Assert.AreEqual(UiTheme.UiScaleMax, UiSettings.UiScale, 1e-4f);
                UiSettings.UiScale = 0.1f;
                Assert.AreEqual(UiTheme.UiScaleMin, UiSettings.UiScale, 1e-4f);
                UiSettings.UiScale = 1.1f;
                UiSettings.ReducedMotion = true;
                UiSettings.Telemetry = false; // WP-022E
                Assert.IsTrue(File.Exists(path), "ayarlar dosyaya yazılmalı: " + path);
                string json = File.ReadAllText(path);
                StringAssert.Contains("\"UiScale\": 1.1", json);
                StringAssert.Contains("\"ReducedMotion\": true", json);
                StringAssert.Contains("\"Telemetry\": false", json);
                Assert.IsFalse(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled, "ayar RunTelemetry'ye uygulanır");

                // Reload from disk: the same values come back, clamped.
                UiSettings.SetPathOverride(path);
                Assert.AreEqual(1.1f, UiSettings.UiScale, 1e-4f, "dosyadan geri okunmalı");
                Assert.IsTrue(UiSettings.ReducedMotion);
                Assert.IsFalse(UiSettings.Telemetry);
                UiSettings.Telemetry = true;
                Assert.IsTrue(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled);

                Vector2 reference = UiTokens.ReferenceResolution / UiSettings.UiScale;
                Assert.AreEqual(1920f / 1.1f, reference.x, 0.01f, "ölçek 1.1 referans çerçeveyi 1/1.1 küçültmeli (UI büyür)");
            }
            finally
            {
                UiSettings.SetPathOverride(null);
                if (File.Exists(path)) File.Delete(path);
            }

            // WP-022B: PlayerPrefs is not a store for anything in the UI module (it lands in
            // the Player plist and breaks other sessions' user-data restore checks).
            string root = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime");
            var offenders = new System.Collections.Generic.List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string code = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), @"//[^\n]*|/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (code.Contains("PlayerPrefs."))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }

            Assert.IsEmpty(offenders, "UI/Runtime PlayerPrefs kullanmamalı: " + string.Join(", ", offenders));
        }

        /// <summary>
        /// WP-022C: the whole UiContrast audit (WP-005 pairs + the v2.x chips) as a
        /// WCAG 2.2 AA table, asserted and written to the log for the evidence folder.
        /// </summary>
        /// <summary>
        /// WP-022C: the 21:9 page cap converges in one pass. The first UiMaxWidth re-derived
        /// its inset inside its own dimension-change callback and overflowed the Player's
        /// stack at 2560 px (crash reports in wp-022/evidence/crash-reports); this drives a
        /// 2560- then 1920-wide parent through the component and checks width and re-entry.
        /// </summary>
        [Test]
        public void SayfaGenislikKapagi_UltraGenisTeYakinsar_YinelenmeYok()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                var parent = (RectTransform)canvasGo.transform;
                parent.sizeDelta = new Vector2(2560f, 1080f);

                var hostGo = new GameObject("PageHost", typeof(RectTransform));
                var host = (RectTransform)hostGo.transform;
                host.SetParent(parent, false);
                host.anchorMin = Vector2.zero;
                host.anchorMax = Vector2.one;
                host.pivot = new Vector2(0.5f, 0.5f);
                host.offsetMin = new Vector2((int)UiSpace.Page, (int)UiSpace.Section);
                host.offsetMax = new Vector2(-(int)UiSpace.Page, -(int)UiSpace.Section);

                UiMaxWidth cap = hostGo.AddComponent<UiMaxWidth>();
                cap.Configure(UiTokens.ContentMaxWidth);
                Assert.AreEqual(UiTokens.ContentMaxWidth, host.rect.width, 0.5f, "2560 px: sayfa kapağa kırpılır.");
                Assert.AreEqual(0f, host.anchoredPosition.x, 0.01f, "2560 px: sayfa ortalanır.");

                // Edit mode has no canvas driving the dimension-change callback (and
                // SendMessage asserts on a non-edit-mode behaviour); re-applying through
                // Configure is the same Apply the callback runs. Twice: it must be idempotent.
                parent.sizeDelta = new Vector2(1920f, 1080f);
                cap.Configure(UiTokens.ContentMaxWidth);
                cap.Configure(UiTokens.ContentMaxWidth);
                Assert.AreEqual(1920f - 2 * (int)UiSpace.Page, host.rect.width, 0.5f, "1920 px: sayfa kenar boşluğu dışında tam genişlik.");

                parent.sizeDelta = new Vector2(3440f, 1440f);
                cap.Configure(UiTokens.ContentMaxWidth);
                Assert.AreEqual(UiTokens.ContentMaxWidth, host.rect.width, 0.5f, "3440 px: yine kapak.");
                Assert.AreEqual((int)UiSpace.Section, host.offsetMin.y, 0.01f, "dikey ofset dokunulmaz.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void KontrastTablosu_WcagAA_TumCiftlerGecer()
        {
            var table = new System.Text.StringBuilder();
            table.AppendLine("| pair | ratio | floor | result |");
            table.AppendLine("|---|---|---|---|");
            int failures = 0;
            foreach (UiContrastCase c in UiContrast.Cases)
            {
                float ratio = UiContrast.Measure(c);
                bool ok = ratio >= c.Required;
                if (!ok) failures++;
                table.AppendLine("| " + c.Description + " | " + ratio.ToString("0.00") + ":1 | " + c.Required.ToString("0.0") + " | " + (ok ? "PASS" : "FAIL") + " |");
            }

            Debug.Log("[DD-TEST] wp022c.contrastTable\n" + table);
            Assert.AreEqual(0, failures, "kontrast tablosunda FAIL:\n" + table);
            Assert.GreaterOrEqual(UiContrast.Cases.Count, 30, "audit çift sayısı daraldı");
        }

        private static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
