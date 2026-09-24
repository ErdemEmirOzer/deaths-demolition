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
    /// WP-022 · AP-012 UI art contract (ADR-022-1; WP-068 §7.1: the G1 SDF glyph atlas + A4 frames v3 replace the
    /// RGBA atlases): the atlases in Assets/_Prototype/UI/Art are sliced by the manifest, every key the screens ask for exists, frames carry a
    /// 9-slice border, nothing exceeds the UI texture class, the shipped UI art stays
    /// under its budget, the theme resolves them, and the HUD tokens keep WCAG AA over
    /// the five environment roles the camera can put behind a panel.
    /// </summary>
    [TestFixture]
    [Category("WP022")]
    public sealed class UiArtThemeTests
    {
        private const string ArtFolder = "Assets/_Prototype/UI/Art/Resources/UI";
        private const long UiArtBudgetBytes = 12L * 1024 * 1024; // ADR-015-1 UI class, package brief

        private static readonly string[] HudKeys =
        {
            "Hud.Gold", "Hud.Special", "Hud.Round", "Hud.Health", "Hud.Enemies", "Hud.Timer", "Hud.LowHealth"
        };

        private static readonly string[] MenuKeys =
        {
            "Menu.Settings", "Menu.Audio", "Menu.Graphics", "Menu.Gamepad", "Menu.Keyboard",
            "Menu.Profile", "Menu.Statistics", "Menu.Achievements", "Menu.Back", "Menu.Confirm",
            "Menu.Accessibility", "Menu.Controls", "Menu.Play", "Menu.Quit", "Menu.Resume", "Menu.EndRun"
        };

        /// <summary>G1 glyphs the v3 screens ask for besides the icon families (v004 A2/A3 + the moved reticle pair / divider).</summary>
        private static readonly string[] GlyphKeys =
        {
            "Status.Netted", "Status.Dazed", "Status.Pulled", "Threat.Surrounded", "Threat.Behind", "Threat.Elite", "Threat.Ultra",
            "Status.NettedOuter", "Status.DazedOuter", "Status.PulledOuter", "Threat.EliteOuter", "Threat.UltraOuter", "Threat.BehindOuter",
            "Medal.Common", "Medal.Rare", "Medal.Epic", "Medal.Locked", "Reticle.Ring", "Reticle.Ticks", "Reticle.Hit",
            "Reticle.SpreadArc", "Reticle.SpreadDot", "Marker.Edge", "Ornament.Divider", "Seal.Mark", "Petal.Single",
            "Pip.Full", "Pip.Empty", "Toast.Record", "Special.Ready", "Shop.Locked", "Shop.Purchased", "Shop.MaxTier",
            "Shop.Reroll", "Shop.Picks", "Tier.Special", "Unlock.Title", "Unlock.Badge", "Notice.Info", "Notice.Warning",
            "Notice.Error", "Empty.Scroll", "Empty.Bowl", "Stat.TrendUp", "Stat.TrendDown", "Tip.Hint"
        };

        /// <summary>V1 input shapes (26 + the pad disc).</summary>
        private static readonly string[] InputShapes =
        {
            "Key.Arrows", "Mouse.Left", "Mouse.Right", "Mouse.Middle", "Mouse.Wheel", "Mouse.Move", "Pad.Disc",
            "Pad.FaceSouth", "Pad.FaceEast", "Pad.FaceWest", "Pad.FaceNorth", "Pad.LB", "Pad.RB", "Pad.LT", "Pad.RT",
            "Pad.LS", "Pad.RS", "Pad.LSClick", "Pad.RSClick", "Pad.LSMove", "Pad.RSMove", "Pad.DUp", "Pad.DDown",
            "Pad.DLeft", "Pad.DRight", "Pad.Menu", "Pad.View"
        };

        /// <summary>A4 frames v3 + kit sprites + the V1 key caps.</summary>
        private static readonly string[] FrameKeys =
        {
            "Frame.HudPanel", "Frame.Card", "Frame.Chip", "Frame.ChipFill", "Frame.Modal", "Frame.Tooltip", "Frame.TooltipPointer",
            "Frame.ListRow", "Frame.Ribbon", "Focus.Brackets", "Ring.Progress132", "Ring.Progress64", "Slider.Rail",
            "Slider.Handle", "Slider.HandleFocus", "Shadow.Soft", "Bar.Segments", "Key.Cap", "Key.Wide"
        };

        [TearDown]
        public void TearDown()
        {
            UiTheme.Override(null);
        }

        /// <summary>
        /// WP-068 §7.1 G1 / A4: one SDF glyph atlas holds every single-colour glyph (the former AP-012 icon + status,
        /// AP-013, AP-014 atlases, the reticle pair and divider, and the v004 A1–A3 / V1 / M1 sets), one frames v3 atlas
        /// the frames and key caps; every key a screen asks for exists once.
        /// </summary>
        [Test]
        public void Atlaslar_ManifestteAdlandirilmisHerSpriteYuklenir()
        {
            Sprite[] glyphs = Resources.LoadAll<Sprite>(UiTheme.GlyphAtlas);
            Sprite[] frames = Resources.LoadAll<Sprite>(UiTheme.FrameAtlas);
            Assert.GreaterOrEqual(glyphs.Length, 148, "G1: 148 glif (53 eski + 3 taşınan + 92 v004)");
            Assert.AreEqual(FrameKeys.Length, frames.Length, "A4: 17 çerçeve/kit + 2 tuş kapağı");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Sprite s in glyphs) Assert.IsTrue(names.Add(s.name), "glif adı tekrarlı: " + s.name);
            foreach (Sprite s in frames) Assert.IsTrue(names.Add(s.name), "çerçeve adı tekrarlı: " + s.name);

            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                Assert.IsTrue(names.Contains("Upgrade." + branch), "eksik upgrade glifi: " + branch);
            }

            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                Assert.IsTrue(names.Contains("Weapon." + id), "eksik silah glifi: " + id);
            }

            foreach (SpecialWeaponId id in Enum.GetValues(typeof(SpecialWeaponId)))
            {
                Assert.IsTrue(names.Contains("Special." + id), "eksik special glifi: " + id);
            }

            foreach (string key in new[] { "Weapon.weapon_rifle", "Weapon.weapon_carbine", "Weapon.weapon_shotgun", "Sw.KaguraBell", "Sw.OfudaLoom" })
            {
                Assert.IsTrue(names.Contains(key), "AP-013/014 glifi G1'e taşınmadı: " + key);
            }

            foreach (string key in HudKeys) Assert.IsTrue(names.Contains(key), "eksik HUD glifi: " + key);
            foreach (string key in MenuKeys) Assert.IsTrue(names.Contains(key), "eksik menü glifi: " + key);
            foreach (string key in GlyphKeys) Assert.IsTrue(names.Contains(key), "eksik glif: " + key);
            foreach (string key in InputShapes) Assert.IsTrue(names.Contains(key), "eksik V1 giriş şekli: " + key);
            foreach (string key in FrameKeys) Assert.IsTrue(names.Contains(key), "eksik çerçeve/kit: " + key);

            int achievements = 0;
            foreach (Sprite s in glyphs)
            {
                achievements += s.name.StartsWith("Ach.", StringComparison.Ordinal) ? 1 : 0;
            }

            Assert.AreEqual(27, achievements, "A1/A2: 27 başarım glifi");
        }

        /// <summary>G1 cells are 64² with a 6 px spread (the sprite sits inside the 52² content box); A4 frames carry their 9-slice borders.</summary>
        [Test]
        public void Cerceveler_DokuzDilimKenariTasir_GlifHucresi64()
        {
            foreach (Sprite s in Resources.LoadAll<Sprite>(UiTheme.FrameAtlas))
            {
                if (!s.name.StartsWith("Frame.", StringComparison.Ordinal) && !s.name.StartsWith("Key.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (s.name == "Frame.TooltipPointer")
                {
                    continue; // a Simple pointer under the tooltip edge
                }

                Assert.Greater(s.border.x, 0f, s.name + " 9-slice sol kenarı sıfır");
                Assert.Greater(s.border.z, 0f, s.name + " 9-slice sağ kenarı sıfır");
                Assert.Less(s.border.x + s.border.z, s.rect.width, s.name + " kenar toplamı sprite genişliğini aşıyor");
                Assert.Less(s.border.y + s.border.w, s.rect.height + 1f, s.name + " kenar toplamı sprite yüksekliğini aşıyor");
            }

            foreach (Sprite s in Resources.LoadAll<Sprite>(UiTheme.GlyphAtlas))
            {
                Assert.LessOrEqual(s.rect.width, 52f, s.name + " 52² içerik kutusuna sığmalı (64² hücre, 6 px yayılım)");
                Assert.LessOrEqual(s.rect.height, 52f, s.name + " 52² içerik kutusuna sığmalı (64² hücre, 6 px yayılım)");
                Assert.AreEqual(Mathf.FloorToInt(s.rect.xMin / 64f), Mathf.FloorToInt((s.rect.xMax - 1f) / 64f), s.name + " hücre sınırını aşıyor");
                Assert.AreEqual(Mathf.FloorToInt(s.rect.yMin / 64f), Mathf.FloorToInt((s.rect.yMax - 1f) / 64f), s.name + " hücre sınırını aşıyor");
                Assert.AreEqual(Vector4.zero, s.border, s.name + " glifte 9-slice olmaz");
            }
        }

        /// <summary>
        /// Texture class: every UI texture ≤ 1024², no mipmaps; the G1 distance field and the A4 frames are Alpha8
        /// (white + alpha, the distance / coverage in .a); the V1 cursors are readable RGBA32 32 / 64 px; the kumiko
        /// tile repeats. The UI art stays under its source budget.
        /// </summary>
        [Test]
        public void DokuSinifi_1024VeButce()
        {
            string[] files = Directory.GetFiles(ArtFolder, "*.png", SearchOption.AllDirectories);
            Assert.Greater(files.Length, 0, "UI PNG'leri bulunamadı: " + ArtFolder);

            long total = 0;
            foreach (string file in files)
            {
                total += new FileInfo(file).Length;
            }

            Assert.LessOrEqual(total, UiArtBudgetBytes,
                "UI sanatı kaynak boyutu bütçeyi aşıyor: " + total + " B (sıkıştırma öncesi üst sınır)");

            foreach (string folder in new[] { UiTheme.AtlasFolder.TrimEnd('/'), "UI/G1", "UI/A4" })
            {
                Texture2D[] textures = Resources.LoadAll<Texture2D>(folder);
                Assert.Greater(textures.Length, 0, folder);
                foreach (Texture2D t in textures)
                {
                    Assert.LessOrEqual(Mathf.Max(t.width, t.height), 1024, t.name + " UI sınıfı 1024² tavanını aşıyor (ADR-015-1)");
                    Assert.AreEqual(1, t.mipmapCount, t.name + " UI dokusunda mipmap olmamalı");
                }
            }

            Texture2D glyphs = Resources.LoadAll<Sprite>(UiTheme.GlyphAtlas)[0].texture;
            Texture2D frames = Resources.LoadAll<Sprite>(UiTheme.FrameAtlas)[0].texture;
            Assert.AreEqual(TextureFormat.Alpha8, glyphs.format, "G1 mesafe alanı Alpha8");
            Assert.AreEqual(TextureFormat.Alpha8, frames.format, "A4 çerçeveleri Alpha8");
            Assert.AreEqual(1024, glyphs.width);
            Assert.AreEqual(1024, frames.width);

            foreach (bool retina in new[] { false, true })
            {
                foreach (bool pressed in new[] { false, true })
                {
                    Texture2D cursor = UiCursor.Texture(pressed, retina);
                    Assert.IsNotNull(cursor, "V1 imleci yok: pressed=" + pressed + " retina=" + retina);
                    Assert.AreEqual(retina ? 64 : 32, cursor.width, cursor.name);
                    Assert.AreEqual(TextureFormat.RGBA32, cursor.format, cursor.name + " donanım imleci RGBA32");
                    Assert.IsTrue(cursor.isReadable, cursor.name + " Cursor.SetCursor okunabilir doku ister");
                }
            }

            Texture kumiko = Resources.Load<Texture>(UiTheme.KumikoTileResource);
            Assert.IsNotNull(kumiko, "A6 kumiko karosu yok");
            Assert.AreEqual(TextureWrapMode.Repeat, kumiko.wrapMode, "kumiko karosu tekrarlar");
        }

        [Test]
        public void Tema_KaynaklardanCozulur_YedekKitAsilaKopmaz()
        {
            UiTheme theme = UiTheme.Active;
            Assert.IsNotNull(theme);
            Assert.IsNotNull(theme.Icon("Hud.Gold"), "tema glif vermedi");
            Assert.IsNotNull(theme.Frame("Frame.HudPanel"), "tema çerçeve vermedi");
            Assert.IsNotNull(theme.MenuPlate, "menü plakası yok");
            Assert.IsNotNull(theme.HudMarker, "AP-010 HUD işareti yok");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsNotNull(theme.Skin(i), "skin önizlemesi yok: " + (i + 1));
            }

            Assert.IsTrue(UiArt.HasFrame(UiFrame.Card));
            Assert.IsTrue(UiArt.HasFrame(UiFrame.Modal), "A4 Elev2 modal çerçevesi");
            Assert.IsFalse(UiArt.HasFrame(UiFrame.Button), "v3 düğmeleri çerçevesiz (A4'te Frame.Button yok) → prosedürel kontur");
            Assert.AreSame(UiSprites.Control, UiArt.Frame(UiFrame.Button));
            Assert.AreSame(theme.Icon("Upgrade.Piercing"), UiArt.UpgradeIcon(UpgradeBranch.Piercing));
            Assert.AreSame(theme.Icon("Weapon.AutoSmg"), UiArt.WeaponIcon(ClassicWeaponId.AutoSmg));
            Assert.AreSame(theme.Icon("Medal.Epic"), UiArt.Medal(UiMedal.Epic), "M1 madalyası G1'den");
            Assert.AreSame(theme.Icon("Reticle.Ring"), UiArt.Reticle(true), "reticle G1'e taşındı");
            Assert.IsNotNull(UiArt.KeyCap(true));
            Assert.IsNotNull(UiArt.KeyCap(false));
            Assert.IsTrue(UiArt.IsGlyph(theme.Icon("Hud.Gold")), "G1 glifi SDF olarak tanınır");
            Assert.IsFalse(UiArt.IsGlyph(theme.Frame("Frame.Card")), "A4 çerçevesi glif değildir");
            Assert.IsFalse(UiArt.IsGlyph(UiSprites.Ring), "prosedürel sprite glif değildir");
            Assert.AreEqual(2f, UiArt.FramePixelsPerUnitMultiplier, "A4 2× yazıldı");

            // Art absent: every lookup falls back to the procedural kit and nothing throws.
            var empty = ScriptableObject.CreateInstance<UiTheme>();
            UiTheme.Override(empty);
            Assert.IsNull(UiArt.Icon("Hud.Gold"));
            Assert.IsFalse(UiArt.HasFrame(UiFrame.HudPanel));
            Assert.AreSame(UiSprites.Card, UiArt.Frame(UiFrame.Card));
            Assert.AreSame(UiSprites.Control, UiArt.Frame(UiFrame.Button));
            Assert.AreSame(UiSprites.Ring, UiArt.Medal(UiMedal.Epic));
            Assert.AreSame(UiSprites.Disc, UiArt.HudMarker());
            Assert.IsNull(UiArt.KeyCap(true));
            Assert.IsFalse(UiArt.IsGlyph(UiSprites.Ring));
            Assert.IsNull(UiArt.SkinPreview(0));
            Assert.IsNull(UiArt.MenuPlate());
            UnityEngine.Object.DestroyImmediate(empty);
        }

        /// <summary>
        /// WP-068 V1 prompts: pad and mouse buttons draw their V1 shape (face buttons by position in the diamond, the
        /// letter stays the control's name for tests and screen readers); keys stay text on a cap. UiCursor: the pointer
        /// shows when the context wants it and the last device is not a pad.
        /// </summary>
        [Test]
        public void V1_GirdiSekilleri_VeImlecGorunurlugu()
        {
            UiInputGlyphs.Glyph submit = UiInputGlyphs.For(UiPromptAction.Submit, DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
            Assert.AreEqual("A", submit.Text);
            Assert.AreEqual("Pad.FaceSouth", submit.Shape);
            Assert.IsTrue(UiInputGlyphs.HasShape(submit), "G1'de V1 şekli var");
            Assert.AreEqual(UiInputGlyphs.DefaultSize, UiInputGlyphs.WidthOf(submit, UiInputGlyphs.DefaultSize), "şekil kare");
            Assert.AreEqual("Pad.Menu", UiInputGlyphs.For(UiPromptAction.Pause, DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad).Shape);
            Assert.IsNull(UiInputGlyphs.For(UiPromptAction.Submit, DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse).Shape, "tuş metin + kapak");
            Assert.AreEqual("Mouse.Left", UiInputGlyphs.FromControlName("Left Button", DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse).Shape);
            Assert.AreEqual("Pad.RB", UiInputGlyphs.FromControlName("Right Shoulder", DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad).Shape);
            Assert.AreEqual("Pad.DUp", UiInputGlyphs.FromControlName("D-Pad Up", DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad).Shape);
            foreach (UiPromptAction action in Enum.GetValues(typeof(UiPromptAction)))
            {
                UiInputGlyphs.Glyph pad = UiInputGlyphs.For(action, DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                Assert.IsTrue(pad.Shape == null || UiArt.HasIcon(pad.Shape), action + ": şekil anahtarı G1'de olmalı (" + pad.Shape + ")");
            }

            bool before = UiCursor.ContextVisible;
            try
            {
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse);
                UiCursor.SetContextVisible(true);
                Assert.IsTrue(UiCursor.WantVisible, "menü + fare → görünür");
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                Assert.IsFalse(UiCursor.WantVisible, "pad son konuşan cihazken gizli");
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse);
                Assert.IsTrue(UiCursor.WantVisible, "fare hareketi (şema dönüşü) geri getirir");
                UiCursor.SetContextVisible(false);
                Assert.IsFalse(UiCursor.WantVisible, "koşu / yükleme bağlamı gizler");
            }
            finally
            {
                UiCursor.SetContextVisible(before);
                UiInputScheme.Release();
            }
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

                // WP-022G accessibility values round-trip and apply to the tokens.
                UiSettings.ColorPalette = 1;
                UiSettings.TextScale = 1.2f;
                UiSettings.ReduceFlashes = true;
                Assert.AreEqual(UiPalette.ColorBlindSafe, UiTokens.Palette);
                Assert.AreEqual(Mathf.RoundToInt(18 * 1.2f), UiTokens.FontSize(UiType.Body), "Body metin ölçeğini izler");
                Assert.AreEqual(34, UiTokens.FontSize(UiType.H1), "başlıklar ölçeklenmez");
                Assert.AreEqual(UiSettings.ReducedFlashScale, UiTokens.FlashScale, 0.001f);
                UiSettings.SetPathOverride(path);
                Assert.AreEqual(1, UiSettings.ColorPalette);
                Assert.AreEqual(1.2f, UiSettings.TextScale, 1e-4f);
                Assert.IsTrue(UiSettings.ReduceFlashes);
                UiSettings.ColorPalette = 0;
                UiSettings.TextScale = 1f;
                UiSettings.ReduceFlashes = false;
                Assert.IsTrue(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled);

                // WP-022F (S-04 review): -ddTelemetry 0 owns the process - the toggle cannot
                // turn it on, so Enabled never claims a subscription that was skipped.
                DD.Prototype.Diagnostics.Telemetry.RunTelemetry.ParseArgs(new[] { "app", "-ddTelemetry", "0" });
                Assert.IsTrue(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.CommandLineOff);
                DD.Prototype.Diagnostics.Telemetry.RunTelemetry.SetEnabled(true);
                Assert.IsFalse(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled, "CLI kapatması ayar toggle'ından üstün");
                DD.Prototype.Diagnostics.Telemetry.RunTelemetry.ParseArgs(new[] { "app", "-ddTelemetry", "1" });
                Assert.IsFalse(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.CommandLineOff);
                DD.Prototype.Diagnostics.Telemetry.RunTelemetry.SetEnabled(true);
                Assert.IsTrue(DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled);

                // WP-022M: DISPLAY choice persists in ui-settings.json (applied by UiDisplayMode in play).
                UiSettings.Fullscreen = true;
                StringAssert.Contains("\"DisplayMode\": 1", File.ReadAllText(path));
                UiSettings.Fullscreen = false;
                StringAssert.Contains("\"DisplayMode\": 0", File.ReadAllText(path));
                Assert.AreEqual(1, new UiSettingsData { DisplayMode = 7 }.Clamp().DisplayMode);
                Assert.AreEqual(-1, new UiSettingsData { DisplayMode = -5 }.Clamp().DisplayMode);

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

        /// <summary>
        /// WP-022F: one vocabulary for prompts - every action has a glyph on both devices,
        /// gamepad face buttons are round single letters, keys are named caps; the scheme
        /// seam raises Changed once per flip and nothing on a repeat.
        /// </summary>
        [Test]
        public void GirdiGlifleri_HerEylemIkiCihaz_SemaDegisimiTekOlay()
        {
            foreach (UiPromptAction action in Enum.GetValues(typeof(UiPromptAction)))
            {
                UiInputGlyphs.Glyph pad = UiInputGlyphs.For(action, DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                UiInputGlyphs.Glyph key = UiInputGlyphs.For(action, DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse);
                Assert.IsFalse(string.IsNullOrEmpty(pad.Text) || string.IsNullOrEmpty(key.Text), action + ": iki cihazda da metin");
                Assert.AreNotEqual("?", pad.Text, action + ": pad glifi tanımlı");
                Assert.IsTrue(!pad.Round || pad.Text.Length == 1, action + ": yuvarlak pad glifi tek harf");
                Assert.IsFalse(key.Round, action + ": tuş kapağı yuvarlak değil");
                Assert.GreaterOrEqual(key.Width, 1f);
            }

            int raised = 0;
            Action<DD.Prototype.Core.InputAbstraction.InputSchemeKind> handler = _ => raised++;
            UiInputScheme.Changed += handler;
            try
            {
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse);
                Assert.AreEqual(2, raised, "yalnız gerçek geçişler olay yükseltir");
                Assert.AreEqual(DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse, UiInputScheme.Current);
            }
            finally
            {
                UiInputScheme.Changed -= handler;
                UiInputScheme.Release();
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

        /// <summary>
        /// WP-022G: the colour-blind safe palette passes the same WCAG table (every pair,
        /// both palettes), the green family really leaves green, and the palette is
        /// restored afterwards.
        /// </summary>
        [Test]
        public void KontrastTablosu_RenkKorlukPaleti_TumCiftlerGecer()
        {
            UiPalette before = UiTokens.Palette;
            try
            {
                UiTokens.Palette = UiPalette.ColorBlindSafe;
                var table = new System.Text.StringBuilder();
                int failures = 0;
                foreach (UiContrastCase c in UiContrast.Cases)
                {
                    float ratio = UiContrast.Measure(c);
                    bool ok = ratio >= c.Required;
                    if (!ok) failures++;
                    table.AppendLine("| " + c.Description + " | " + ratio.ToString("0.00") + ":1 | " + c.Required.ToString("0.0") + " | " + (ok ? "PASS" : "FAIL") + " |");
                }

                Debug.Log("[DD-TEST] wp022g.contrastTableColorBlind\n" + table);
                Assert.AreEqual(0, failures, "renk körlüğü paletinde FAIL:\n" + table);

                Color success = UiTokens.Get(UiColor.StateSuccess);
                Color health = UiTokens.Get(UiColor.BarHealth);
                Color danger = UiTokens.Get(UiColor.StateDanger);
                Assert.Greater(success.b, success.g, "başarı jade'den maviye taşındı");
                Assert.Greater(health.b, health.r, "sağlık çubuğu mavi aileye taşındı");
                Assert.Greater(danger.r, danger.b, "tehlike vermilion kalır");
                // Hue separation between health and low health is what the palette exists for.
                Color low = UiTokens.Get(UiColor.BarHealthLow);
                Assert.Greater(Mathf.Abs(low.r - health.r), 0.3f, "sağlık ↔ düşük sağlık kanal farkı belirgin");
            }
            finally
            {
                UiTokens.Palette = before;
            }
        }

        private static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
