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
    /// WP-068 slice 7 · E9 v3 with the M4-R1 roster: every weapon row and the Specials strip fit the column (the v2
    /// cards ran 1 500 px into a 700 px column with ten weapons). The stage follows focus, and a focused locked row
    /// states its unlock and refuses a press. START RUN echoes the loadout.
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
        /// The Mac case (the r1b-caps frame that failed): UI scale 1.2 × TextScale 1.25 leaves the stage ~560 px. The
        /// measured layout keeps every block inside the card, and a focused PATH weapon still fits.
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
                foreach (string block in new[] { "State", "CoreBranches", "Stat2", "Stat3" })
                {
                    Assert.GreaterOrEqual(Bottom(Named(screen, block)), floor, (weapon ?? "seçili") + ": " + block + " kartın altına taşıyor");
                }

                // AD R1-B re-check: CORE never meets the last stat bar, and nothing on the stage is cut.
                RectTransform core = Named(screen, "CoreBranches");
                core.GetWorldCorners(s_corners);
                float coreTop = s_corners[1].y;
                Assert.Greater(Bottom(Named(screen, "Stat3")), coreTop, (weapon ?? "seçili") + ": son stat çubuğu CORE satırına giriyor");
                foreach (TMPro.TMP_Text t in screen.StageTexts)
                {
                    if (t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text))
                    {
                        t.ForceMeshUpdate();
                        Assert.IsFalse(t.isTextTruncated, (weapon ?? "seçili") + ": " + t.name + " kesiliyor: " + t.text);
                    }
                }
            }

            RectTransform column = Named(screen, "Weapons");
            Assert.GreaterOrEqual(Bottom(Named(screen, "Specials")), Bottom(column) - 0.5f, "Special şeridi kolona sığar (kompakt)");
            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        private static float Bottom(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            return s_corners[0].y;
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

            Assert.GreaterOrEqual(rows.Count, 9, "WP-069 kadrosu (rl_1 B3'e kadar kapalı)");

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

            // U1b MUST-1 (AD + ui-ux, 06.10): the card ends under its content - CORE and the state line follow the bars - and
            // never above the skins' last card; its foot stays put when focus walks to a locked row (the state line's place
            // is kept when it is empty).
            Canvas.ForceUpdateCanvases();
            RectTransform lastSkin = Named(screen, "Skin." + (PlayerSkin)(PlayerSkins.Count - 1));
            float cardFoot = Bottom(stageCard);
            Assert.LessOrEqual(cardFoot, Bottom(lastSkin) + 1f, "sahne kartı son kostüm kartının altına kadar iner");
            stageCard.GetWorldCorners(s_corners);
            float cardHeight = s_corners[1].y - s_corners[0].y;
            Assert.Less(Bottom(Named(screen, "Stat3")) - Bottom(Named(screen, "CoreBranches")), cardHeight * 0.2f, "CORE çubukların hemen altında (boş bant yok)");

            // Focus a locked row: the stage follows and names the unlock; a press changes nothing.
            UiButton locked = null;
            foreach (UiButton row in rows)
            {
                if (!ProfileServiceUnlocked(row) && !row.IsSelectedChoice)
                {
                    locked = row;
                    break;
                }
            }

            Assert.IsNotNull(locked, "yeni profilde kilitli silah satırı var");
            Assert.IsTrue(locked.IsInteractable(), "kilitli satır odaklanabilir (sahne kilidi açıklar)");
            EventSystem.current.SetSelectedGameObject(locked.gameObject);
            yield return null;
            yield return null;
            Assert.AreNotEqual(chosen, screen.StageIndex, "sahne odağı izler");
            string lockLine = screen.StageTexts[4].text;
            Assert.IsTrue(lockLine.StartsWith("Reach profile level") || lockLine.Contains("PATH"), "kilit satırı (merdiven ya da PATH): " + lockLine);
            StringAssert.DoesNotContain("{", lockLine, "kilit satırı biçimlendi");
            TMP_TextCheck(screen, "kilitli odak");
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(cardFoot, Bottom(stageCard), 1f, "kilitli satıra odakta kartın ayağı yerinde (±1 px)");

            ClassicWeaponId before = PrototypeUiFlow.SelectedWeapon;
            locked.OnSubmit(null);
            yield return null;
            Assert.AreEqual(before, PrototypeUiFlow.SelectedWeapon, "kilitli silah seçilemez");

            TMPro.TMP_Text loadout = Named(screen, "Loadout").GetComponent<TMPro.TMP_Text>();
            StringAssert.Contains(PlayerSkins.Get(PrototypeUiFlow.SelectedSkin).DisplayName, loadout.text, "START RUN yükü yankılar");

            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>
        /// R3 m12 (WP-098B): the page's weapon pictures are the AP-036 side views at one scale. In the column every row and
        /// both Specials draw px-per-metre alike (the real length reads: Tomi ~2 × Tetsui), each weapon's foot sits the same
        /// distance under its row's centre, and the rows' stocks stand on one vertical line left of the names. On the stage
        /// the scale holds across a focus change (Tomi → Tetsui), the picture stays inside the card and above the name.
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

            // The stage: one scale across a focus change, inside the card, above the name.
            var stageScales = new List<float>();
            foreach (string weapon in new[] { "Weapon.sr_1.Sr1", "Weapon.smg_2.Smg2" })
            {
                RectTransform row = Named(screen, weapon);
                Assert.IsNotNull(row, weapon);
                EventSystem.current.SetSelectedGameObject(row.gameObject);
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                string weaponId = weapon.Split('.')[1];
                Rect content = SideContent(Named(screen, "StageGlyph"), weaponId);
                stageScales.Add(content.width / SelectionScreen.WeaponSideView.Table[SelectionScreen.WeaponSideView.IndexOf(weaponId)].LengthM);
                Rect card = WorldRect(Named(screen, "StageCard"));
                Assert.IsTrue(card.Contains(content.min) && card.Contains(content.max), weapon + ": sahne görseli kartın içinde");
                Assert.GreaterOrEqual(content.yMin, WorldRect(screen.StageTexts[0].rectTransform).yMax - 0.5f, weapon + ": görsel adın üstünde");
            }

            Assert.AreEqual(stageScales[0], stageScales[1], stageScales[0] * 0.01f, "sahne odak değişiminde aynı ölçek");
            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>
        /// WP-098B (brush kit KILAVUZ §3 "Seçim", AP-028 §3.4): the window is the kit's - the world plate under the Window scrim
        /// at its linear α (no v2 veils, petals or lozenge), the C5 title without a glyph (one title brush, no C8, no
        /// vermilion heading tick); the skins and the weapons are C18 list rows - no card frame, no SELECTED / LOCKED chip -
        /// whose choice is the check glyph (exactly one skin and one weapon), a locked weapon the lock glyph and its level;
        /// Back is the kit's C10 and START RUN its C11, both ≥ 48 high; the names the PM tests use are kept.
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

            int checks = 0, skins = 0, weapons = 0;
            foreach (UiButton row in screen.GetComponentsInChildren<UiButton>(false))
            {
                bool skin = row.name.StartsWith("Skin.");
                bool weapon = row.name.StartsWith("Weapon.");
                if (!skin && !weapon)
                {
                    continue;
                }

                skins += skin ? 1 : 0;
                weapons += weapon ? 1 : 0;
                Assert.IsFalse(row.DrawsFocusRing, row.name + ": kit satırı (odak = köşebent, halka yok)");
                Assert.IsNull(row.transform.Find("Frame"), row.name + ": çerçevesiz satır");
                Transform status = row.transform.Find("Status");
                Assert.IsNotNull(status, row.name + ": C18 durum glifi");
                bool glyph = status.GetComponent<Image>().enabled;
                if (row.IsSelectedChoice)
                {
                    Assert.IsTrue(glyph, row.name + ": seçim = check glifi");
                    checks++;
                }
                else if (weapon && !ProfileServiceUnlocked(row))
                {
                    Assert.IsTrue(glyph, row.name + ": kilitli satırda kilit glifi");
                    TMPro.TMP_Text trailing = row.transform.Find("Trailing").GetComponent<TMPro.TMP_Text>();
                    Assert.IsFalse(string.IsNullOrEmpty(trailing.text), row.name + ": kilidin koşulu yazılı (L3 / PATH · L11)");
                }
                else
                {
                    Assert.IsFalse(glyph, row.name + ": seçilmemiş, açık satırda glif yok");
                }
            }

            Assert.AreEqual(3, skins, "üç kostüm satırı");
            Assert.GreaterOrEqual(weapons, 9, "silah satırları");
            Assert.AreEqual(2, checks, "tam iki check: bir kostüm + bir silah");

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
            Assert.IsFalse(string.IsNullOrEmpty(texts[0].text), when + ": ad");
            StringAssert.DoesNotContain("{", texts[1].text, when + ": sınıf satırı biçimlendi");
            Assert.IsFalse(string.IsNullOrEmpty(texts[2].text), when + ": rol (paket)");
            // WP-081 U1b (R2 U1): the paragraph left the card - the class line, the role and the bars say what the weapon is.
            Assert.IsEmpty(texts[3].text, when + ": açıklama paragrafı kartta yok (U1b)");
        }
    }
}
