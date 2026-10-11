using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 7 · E9 v3 with the M4-R1 roster, WP-106 Prepare Run v2: every weapon row and the Specials strip fit the
    /// column, every block of the stage (identity, preview, stat cards, state line) and of the run setup (difficulty, map,
    /// tip) fits its column on the Mac canvas, the stage follows focus, and a focused locked row states its lock and refuses a
    /// press. The skins are gone (R4 user note 7).
    /// </summary>
    public sealed class SelectionV3Tests
    {
        private static readonly Vector3[] s_corners = new Vector3[4];

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
        }

        /// <summary>
        /// The Mac case (the r1b-caps frame that failed): UI scale 1.2 × TextScale 1.25. The measured layout keeps every block
        /// inside its column and nothing on the stage is cut - for the chosen weapon, the longest (Tomi) and a heavy one.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kompakt_UiOlcek12_Metin125_SahneKartaSigar()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            UiSettings.UiScale = 1.2f;
            UiSettings.TextScale = 1.25f;
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;
            yield return null;
            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen);

            foreach (string weapon in new[] { null, "Weapon.sr_1.Sr1", "Weapon.lmg_2.Lmg2" })
            {
                if (weapon != null)
                {
                    RectTransform row = Named(screen, weapon);
                    Assert.IsNotNull(row, weapon);
                    EventSystem.current.SetSelectedGameObject(row.gameObject);
                    yield return null;
                    yield return null;
                }

                Canvas.ForceUpdateCanvases();
                RectTransform card = Named(screen, "StageCard");
                float floor = Bottom(card) - 0.5f;
                foreach (string block in new[] { "Identity", "State", "Stats", "Stat4" })
                {
                    Assert.GreaterOrEqual(Bottom(Named(screen, block)), floor, (weapon ?? "seçili") + ": " + block + " kartın altına taşıyor");
                }

                // The cards come after the preview (when it has room), the state line after the cards: no block meets another.
                RectTransform stats = Named(screen, "Stats");
                RectTransform preview = Named(screen, "Preview");
                if (preview.gameObject.activeInHierarchy)
                {
                    Assert.GreaterOrEqual(Bottom(preview), Top(stats) - 0.5f, (weapon ?? "seçili") + ": önizleme kartlara giriyor");
                }

                Assert.GreaterOrEqual(Bottom(stats), Top(Named(screen, "State")) - 0.5f, (weapon ?? "seçili") + ": kartlar durum satırına giriyor");
                foreach (TMPro.TMP_Text t in screen.StageTexts)
                {
                    if (t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text))
                    {
                        t.ForceMeshUpdate();
                        Assert.IsFalse(t.isTextTruncated, (weapon ?? "seçili") + ": " + t.name + " kesiliyor: " + t.text);
                    }
                }

                for (int s = 0; s < PrepareRunLayout.StatCount; s++)
                {
                    RectTransform cell = Named(screen, "Stat" + s);
                    foreach (string part in new[] { "Label", "Value" })
                    {
                        TMPro.TMP_Text text = cell.Find(part).GetComponent<TMPro.TMP_Text>();
                        text.ForceMeshUpdate();
                        Assert.IsFalse(text.isTextTruncated || text.isTextOverflowing, (weapon ?? "seçili") + ": Stat" + s + "/" + part + " kesiliyor: " + text.text);
                    }
                }
            }

            RectTransform column = Named(screen, "Weapons");
            Assert.GreaterOrEqual(Bottom(Named(screen, "Specials")), Bottom(column) - 0.5f, "Special şeridi kolona sığar (kompakt)");
            RectTransform setup = Named(screen, "Setup");
            foreach (string block in new[] { "Difficulty.demolition", "ModeLine", "Map" })
            {
                Assert.GreaterOrEqual(Bottom(Named(screen, block)), Bottom(setup) - 0.5f, block + " kurulum kolonuna sığar (kompakt)");
            }

            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        private static float Bottom(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            return s_corners[0].y;
        }

        private static float Top(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            return s_corners[1].y;
        }

        private static RectTransform Named(Component root, string name)
        {
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == name)
                {
                    return rect;
                }
            }

            return null;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator OnSilah_KolonaSigar_SahneOdagiIzler_KilitliReddeder()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;

            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen, "SelectionScreen yok");
            Canvas.ForceUpdateCanvases();

            var rows = new List<UiButton>();
            foreach (UiButton b in screen.GetComponentsInChildren<UiButton>(true))
            {
                if (b.name.StartsWith("Weapon."))
                {
                    rows.Add(b);
                }
            }

            Assert.GreaterOrEqual(rows.Count, 9, "WP-069 kadrosu");

            RectTransform column = Named(screen, "Weapons");
            RectTransform specials = Named(screen, "Specials");
            Assert.IsNotNull(column);
            Assert.IsNotNull(specials, "Special bilgi karoları");
            float floor = Bottom(column) - 0.5f;
            foreach (UiButton row in rows)
            {
                Assert.GreaterOrEqual(Bottom((RectTransform)row.transform), floor, row.name + " kolonun altına taşıyor");
            }

            Assert.GreaterOrEqual(Bottom(specials), floor, "Special şeridi kolona sığar");
            RectTransform stageCard = Named(screen, "StageCard");
            RectTransform state = Named(screen, "State");
            Assert.GreaterOrEqual(Bottom(state), Bottom(stageCard) - 0.5f, "sahnenin durum satırı kartın içinde");

            // The stage shows the chosen weapon at rest, with the pack's texts.
            int chosen = screen.StageIndex;
            Assert.GreaterOrEqual(chosen, 0);
            TMP_TextCheck(screen, "açılış");
            Assert.IsEmpty(screen.StateLine.text, "başlangıç silahında durum satırı boş");
            float cardFoot = Bottom(stageCard);

            // Focus a locked row: the stage follows and names the lock; a press changes nothing.
            UiButton locked = null;
            foreach (UiButton row in rows)
            {
                if (!ProfileServiceUnlocked(row) && !row.IsSelectedChoice)
                {
                    locked = row;
                    break;
                }
            }

            if (locked != null)
            {
                Assert.IsTrue(locked.IsInteractable(), "kilitli satır odaklanabilir (sahne kilidi açıklar)");
                EventSystem.current.SetSelectedGameObject(locked.gameObject);
                yield return null;
                yield return null;
                Assert.AreNotEqual(chosen, screen.StageIndex, "sahne odağı izler");
                string lockLine = screen.StateLine.text;
                // WP-107 (R4): the only locked row of a new profile is the rocket launcher - bought at its wall during a run.
                Assert.IsTrue(lockLine.StartsWith("Reach profile level") || lockLine.Contains("PATH") || lockLine.StartsWith("Bought at its wall"),
                    "kilit satırı (merdiven, PATH ya da wall buy): " + lockLine);
                StringAssert.DoesNotContain("{", lockLine, "kilit satırı biçimlendi");
                TMP_TextCheck(screen, "kilitli odak");
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(cardFoot, Bottom(stageCard), 1f, "kilitli satıra odakta kartın ayağı yerinde (±1 px)");

                ClassicWeaponId before = PrototypeUiFlow.SelectedWeapon;
                locked.OnSubmit(null);
                yield return null;
                Assert.AreEqual(before, PrototypeUiFlow.SelectedWeapon, "kilitli silah seçilemez");
            }
            else
            {
                Debug.Log("[DD-TEST] selection.v3 no locked weapon row on this profile - the lock half is not exercised");
            }

            // WP-106: no loadout echo in the footer (the choices are the check glyphs on the page).
            Assert.IsNull(Named(screen, "Loadout"), "START RUN yanında yük yankısı yok (R2 U1)");
            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>
        /// R3 m12 (WP-098B): the column's weapon pictures are the AP-036 side views at one scale. Every row and both Specials draw
        /// px-per-metre alike (the real length reads: Tomi ~2 × Tetsui), each weapon's foot sits the same distance under its
        /// row's centre, and the rows' stocks stand on one vertical line left of the names. WP-106: the stage's picture is the 3D
        /// preview; the side view stands in only until its first render, inside the preview well.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator YanGorunum_OrtakOlcekVeTaban_KolondaVeSahnede()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;
            yield return null;
            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen);
            Canvas.ForceUpdateCanvases();

            var scales = new List<float>();
            var feet = new List<float>();
            var stocks = new List<float>();
            float tomi = 0f, tetsui = 0f;
            foreach (UiButton row in screen.GetComponentsInChildren<UiButton>(true))
            {
                if (!row.name.StartsWith("Weapon."))
                {
                    continue;
                }

                string weaponId = row.name.Split('.')[1];
                Rect content = SideContent(row.transform.Find("Icon"), weaponId);
                SelectionScreen.WeaponSideView.Entry e = SelectionScreen.WeaponSideView.Table[SelectionScreen.WeaponSideView.IndexOf(weaponId)];
                float scale = content.width / e.LengthM;
                scales.Add(scale);
                ((RectTransform)row.transform).GetWorldCorners(s_corners);
                feet.Add(content.yMin - (s_corners[0].y + s_corners[1].y) * 0.5f);
                stocks.Add(content.xMax);
                Assert.Less(content.xMax, WorldRect(row.LabelText.rectTransform).xMin + 0.5f, row.name + ": görsel adın solunda");
                if (weaponId == "sr_1") tomi = content.width;
                if (weaponId == "smg_2") tetsui = content.width;
            }

            Assert.GreaterOrEqual(scales.Count, 9, "silah satırları");
            foreach (string special in new[] { "sw_arc", "sw_gel" })
            {
                RectTransform tile = Named(screen, "Special." + special);
                Assert.IsNotNull(tile, special);
                Rect content = SideContent(tile.Find("Icon"), special);
                scales.Add(content.width / SelectionScreen.WeaponSideView.Table[SelectionScreen.WeaponSideView.IndexOf(special)].LengthM);
                tile.GetWorldCorners(s_corners);
                feet.Add(content.yMin - (s_corners[0].y + s_corners[1].y) * 0.5f);
            }

            for (int i = 1; i < scales.Count; i++)
            {
                Assert.AreEqual(scales[0], scales[i], scales[0] * 0.01f + 0.05f, "kolon tek ölçek (px/m) #" + i);
                Assert.AreEqual(feet[0], feet[i], 0.75f, "ortak taban #" + i);
            }

            for (int i = 1; i < stocks.Count; i++)
            {
                Assert.AreEqual(stocks[0], stocks[i], 0.75f, "satırların dipçik çizgisi ortak #" + i);
            }

            if (tomi > 0f && tetsui > 0f)
            {
                Assert.AreEqual(1.4495f / 0.72f, tomi / tetsui, 0.05f, "Tomi / Tetsui gerçek boy oranı");
            }

            // The stage: the stand-in side view (while it shows) sits inside the preview well, at the real length ratio.
            RectTransform glyph = Named(screen, "StageGlyph");
            Assert.IsNotNull(glyph, "sahnenin yan görünüm yedeği");
            if (glyph.gameObject.activeInHierarchy && screen.PreviewWell.gameObject.activeInHierarchy)
            {
                string weaponId = screen.WeaponRows[screen.StageIndex].name.Split('.')[1];
                Rect content = SideContent(glyph, weaponId);
                Rect well = WorldRect(screen.PreviewWell);
                Assert.IsTrue(well.Contains(content.min) && well.Contains(content.max), "yedek görsel önizleme kuyusunun içinde");
            }

            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>
        /// WP-098B (brush kit KILAVUZ §3 "Seçim", AP-028 §3.4): the window is the kit's - the world plate under the Window scrim
        /// at its linear α (no v2 veils, petals or lozenge), the C5 title without a glyph (one title brush, no C8, no vermilion
        /// heading tick); the difficulty modes and the weapons are C18 list rows - no card frame, no SELECTED / LOCKED chip -
        /// whose choice is the check glyph (exactly one mode and one weapon), a locked weapon the lock glyph and its chip; no
        /// skin row (WP-106); Back is the kit's C10 and START RUN its C11, both ≥ 48 high; the names the PM tests use are kept.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Secim_FircaPencere_PlakaPencerePerdesi_Baslik_ListeSatirlariCheckGlifi()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            yield return LifecycleTestKit.Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return new WaitForSecondsRealtime(0.5f);
            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen);

            UiBrushPane pane = screen.Pane;
            Assert.IsNotNull(pane, "kitin pane'i pencereyi taşır");
            Assert.IsTrue(pane.IsOpen);
            Assert.AreEqual(UiBrushScrim.Window, pane.ScrimLevel, "KILAVUZ §3: Window perdesi");
            Assert.AreEqual(UiBrushTokens.ScrimWindow, pane.Scrim.color.a * pane.Scrim.canvasRenderer.GetAlpha(), 0.01f, "perde kitin doğrusal 0,95'inde");
            Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin, "metin zemini ≥ 0,945");

            Assert.IsNotNull(Named(screen, "Backdrop"), "dünya plakası");
            foreach (string v2 in new[] { "Petal", "SkyVeil", "GroundVeil", "FootRule", "HeaderOrnament", "Badge", "Chip" })
            {
                Assert.IsNull(Named(screen, v2), "v2 süsü kalktı: " + v2);
            }

            int titles = 0, others = 0;
            foreach (UiBrushMark mark in screen.GetComponentsInChildren<UiBrushMark>(false))
            {
                if (mark.Kind == UiBrushMarkKind.TitleStroke) titles++;
                else others++;
            }

            Assert.AreEqual(UiBrushTokens.MaxTitle, titles, "tek başlık fırçası (ikonsuz başlık)");
            Assert.AreEqual(0, others, "seçimde C8 / ayırıcı / kenar izi yok (seçim check glifiyle)");
            foreach (RectTransform rect in screen.GetComponentsInChildren<RectTransform>(false))
            {
                Assert.IsFalse(rect.name == "Heading" && rect.Find("Tick") != null, "fırça penceresinde vermilyon başlık çentiği yok");
            }

            int weaponChecks = 0, modeChecks = 0, skins = 0, modes = 0, weapons = 0;
            foreach (UiButton row in screen.GetComponentsInChildren<UiButton>(false))
            {
                skins += row.name.StartsWith("Skin.") ? 1 : 0;
                bool mode = row.name.StartsWith("Difficulty.");
                bool weapon = row.name.StartsWith("Weapon.");
                if (!mode && !weapon)
                {
                    continue;
                }

                modes += mode ? 1 : 0;
                weapons += weapon ? 1 : 0;
                Assert.IsFalse(row.DrawsFocusRing, row.name + ": kit satırı (odak = köşebent, halka yok)");
                Assert.IsNull(row.transform.Find("Frame"), row.name + ": çerçevesiz satır");
                Transform status = row.transform.Find("Status");
                Assert.IsNotNull(status, row.name + ": C18 durum glifi");
                bool glyph = status.GetComponent<Image>().enabled;
                if (row.IsSelectedChoice)
                {
                    Assert.IsTrue(glyph, row.name + ": seçim = check glifi");
                    weaponChecks += weapon ? 1 : 0;
                    modeChecks += mode ? 1 : 0;
                }
                else if (weapon && !ProfileServiceUnlocked(row))
                {
                    Assert.IsTrue(glyph, row.name + ": kilitli satırda kilit glifi");
                    TMPro.TMP_Text trailing = row.transform.Find("Trailing").GetComponent<TMPro.TMP_Text>();
                    Assert.IsFalse(string.IsNullOrEmpty(trailing.text), row.name + ": kilidin koşulu yazılı (WALL BUY / LEVEL n)");
                }
                else if (weapon)
                {
                    Assert.IsFalse(glyph, row.name + ": seçilmemiş, açık satırda glif yok");
                }
            }

            Assert.AreEqual(0, skins, "kostüm satırı yok (WP-106)");
            Assert.AreEqual(PrepareDifficulty.Source.Count, modes, "her mod bir satır");
            Assert.GreaterOrEqual(weapons, 9, "silah satırları");
            Assert.AreEqual(1, weaponChecks, "tam bir silah seçili");
            Assert.AreEqual(1, modeChecks, "tam bir mod seçili");

            foreach (string name in new[] { "Back", "Start" })
            {
                RectTransform control = Named(screen, name);
                Assert.IsNotNull(control, name + " adı korunur");
                UiButton button = control.GetComponent<UiButton>();
                Assert.IsFalse(button.DrawsFocusRing, name + ": kit kontrolü");
                Assert.GreaterOrEqual(control.rect.height, UiTokens.TargetPreferred, name + ": isabet ≥ 48");
            }

            Assert.IsNotNull(Named(screen, "StageCard"), "StageCard adı korunur");
            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>The weapon's alpha box on screen (world space) from its side-view image and the intake table.</summary>
        private static Rect SideContent(Transform icon, string weaponId)
        {
            Assert.IsNotNull(icon, weaponId + ": görsel yok");
            var image = icon.GetComponent<UnityEngine.UI.Image>();
            Assert.IsNotNull(image.sprite, weaponId + ": sprite yok");
            Assert.AreEqual(SelectionScreen.WeaponSideView.FilePrefix + weaponId, image.sprite.name, weaponId + ": AP-036 yan görünümü");
            SelectionScreen.WeaponSideView.Entry e = SelectionScreen.WeaponSideView.Table[SelectionScreen.WeaponSideView.IndexOf(weaponId)];
            Rect r = WorldRect((RectTransform)icon);
            float kx = r.width / e.Width;
            float ky = r.height / e.Height;
            return new Rect(r.xMin + e.Content.x * kx, r.yMin + e.Content.y * ky, e.Content.width * kx, e.Content.height * ky);
        }

        private static Rect WorldRect(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            return new Rect(s_corners[0].x, s_corners[0].y, s_corners[2].x - s_corners[0].x, s_corners[2].y - s_corners[0].y);
        }

        private static bool ProfileServiceUnlocked(UiButton row)
        {
            string[] parts = row.name.Split('.');
            ClassicWeaponId id;
            return parts.Length >= 3 && System.Enum.TryParse(parts[parts.Length - 1], out id) && DD.Prototype.Profile.ProfileService.IsWeaponUnlocked(id);
        }

        private static void TMP_TextCheck(SelectionScreen screen, string when)
        {
            TMPro.TMP_Text[] texts = screen.StageTexts;
            Assert.AreEqual(4, texts.Length, "ad, sınıf, rol, durum");
            Assert.IsFalse(string.IsNullOrEmpty(texts[0].text), when + ": ad");
            StringAssert.DoesNotContain("{", texts[1].text, when + ": sınıf satırı biçimlendi");
            Assert.IsFalse(string.IsNullOrEmpty(texts[2].text), when + ": rol (paket)");
            for (int s = 0; s < PrepareRunLayout.StatCount; s++)
            {
                RectTransform cell = Named(screen, "Stat" + s);
                TMPro.TMP_Text label = cell.Find("Label").GetComponent<TMPro.TMP_Text>();
                TMPro.TMP_Text value = cell.Find("Value").GetComponent<TMPro.TMP_Text>();
                Assert.IsFalse(string.IsNullOrEmpty(label.text), when + ": Stat" + s + " etiketi");
                Assert.IsFalse(string.IsNullOrEmpty(value.text), when + ": Stat" + s + " değeri");
                StringAssert.DoesNotContain("{", value.text, when + ": Stat" + s + " biçimlendi");
            }
        }
    }
}
