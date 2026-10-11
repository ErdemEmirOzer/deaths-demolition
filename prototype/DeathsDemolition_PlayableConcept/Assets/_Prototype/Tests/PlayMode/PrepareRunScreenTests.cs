using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Difficulty;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-106 · Prepare Run v2 on the real screen: no skin anywhere, the 3D preview shows the weapon on the stage and follows a
    /// focus change and a choice (ShowWeapon), the difficulty rows walk and choose with the pad's moves (WP-102's service holds
    /// the choice), a locked row's chip comes from the pack (WALL BUY / LEVEL n, never a bare "L3"), the preview's words show
    /// only on the pointer's hover, the page's letters stay inside R2 U1's room, and nothing is cut at text 1.25 on the Mac
    /// canvas (UI scale 1.2) in EN and TR. A temp ui-settings file: the user's is never touched.
    /// </summary>
    [Category("WP106")]
    public sealed class PrepareRunScreenTests
    {
        /// <summary>
        /// R2 U1 (UiTextInventoryTests, u1_same_content): the menu with Profile open (493 letters on the WP-105 run) + this page
        /// ≤ 864. The page's share is 371; the gate itself is the Explicit U1 instrument, this is its early warning.
        /// </summary>
        private const int LetterBudgetEn = 371;

        private static readonly Vector3[] s_corners = new Vector3[4];
        private string _settingsPath;
        private ClassicWeaponId _weapon;
        private PlayerSkin _skin;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-wp106-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            _weapon = PrototypeUiFlow.SelectedWeapon;
            _skin = PrototypeUiFlow.SelectedSkin;
            DifficultyService.ResetForTests();
            PrepareDifficulty.SetSourceForTests(null);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiSettings.Language = UiStrings.English;
            UiSettings.SetPathOverride(null);
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }

            PrototypeUiFlow.SelectedWeapon = _weapon;   // a global: never leak a test's choice
            DifficultyService.ResetForTests();
            PrepareDifficulty.SetSourceForTests(null);
        }

        // ================================================================== helpers

        private static IEnumerator OpenPrepareRun()
        {
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return Bekle(() => !UiCurtain.IsCovering, 10f, "perde açılmadı");
            yield return null;
            yield return null;
            yield return null;
        }

        private static SelectionScreen PrepareScreen()
        {
            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen, "Prepare Run ekranı yok");
            return screen;
        }

        private static GameObject Focused
        {
            get { return EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null; }
        }

        private static void Move(MoveDirection direction)
        {
            GameObject current = Focused;
            Assert.IsNotNull(current, "hareket için odak yok");
            Vector2 vector = direction == MoveDirection.Up ? Vector2.up : direction == MoveDirection.Down ? Vector2.down
                : direction == MoveDirection.Left ? Vector2.left : Vector2.right;
            ExecuteEvents.Execute(current, new AxisEventData(EventSystem.current) { moveDir = direction, moveVector = vector }, ExecuteEvents.moveHandler);
        }

        private static string WeaponIdOf(UiButton row)
        {
            string[] parts = row.name.Split('.');
            return parts.Length >= 3 ? parts[1] : null;
        }

        private static bool TryClassic(UiButton row, out ClassicWeaponId id)
        {
            id = default(ClassicWeaponId);
            string[] parts = row.name.Split('.');
            return parts.Length >= 3 && System.Enum.TryParse(parts[parts.Length - 1], out id);
        }

        private static int ChosenRow(SelectionScreen screen)
        {
            for (int i = 0; i < screen.WeaponRows.Length; i++)
            {
                if (screen.WeaponRows[i].IsSelectedChoice)
                {
                    return i;
                }
            }

            return -1;
        }

        private static Rect WorldRect(RectTransform rect)
        {
            rect.GetWorldCorners(s_corners);
            return new Rect(s_corners[0].x, s_corners[0].y, s_corners[2].x - s_corners[0].x, s_corners[2].y - s_corners[0].y);
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

        /// <summary>U2's rule on one screen: every visible text whole - not truncated, not overflowing, no word broken by a wrap.</summary>
        private static void CutTexts(SelectionScreen screen, string when, List<string> found)
        {
            foreach (TMP_Text text in screen.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.alpha <= 0.01f)
                {
                    continue;
                }

                text.ForceMeshUpdate();
                if (text.isTextTruncated || text.isTextOverflowing || BreaksInsideAWord(text))
                {
                    found.Add(when + " | " + text.transform.parent.name + "/" + text.name + " | " + text.GetParsedText().Replace('\n', ' '));
                }
            }
        }

        private static bool BreaksInsideAWord(TMP_Text text)
        {
            TMP_TextInfo info = text.textInfo;
            for (int l = 0; l < info.lineCount - 1; l++)
            {
                int last = info.lineInfo[l].lastCharacterIndex;
                if (last < 0 || last + 1 >= info.characterCount)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(info.characterInfo[last].character) && char.IsLetterOrDigit(info.characterInfo[last + 1].character))
                {
                    return true;
                }
            }

            return false;
        }

        // ================================================================== 1 · no skin, the preview, the focus

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PrepareRun_SkinYok_SeciliSilahOnizlemede_OdakSecilenSilahta()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return OpenPrepareRun();
            SelectionScreen screen = PrepareScreen();

            foreach (RectTransform rect in screen.GetComponentsInChildren<RectTransform>(true))
            {
                Assert.IsFalse(rect.name.StartsWith("Skin.", System.StringComparison.Ordinal) || rect.name == "Skins" || rect.name == "PreviewWell" || rect.name == "Swatch",
                    "skin kalmadı: " + rect.name);
            }

            Assert.AreEqual(_skin, PrototypeUiFlow.SelectedSkin, "ekran kostüme dokunmaz (oyun varsayılanı uygular)");
            foreach (string column in new[] { "Setup", "Stage", "Weapons", "StageCard", "Preview", "Stats", "State", "Map" })
            {
                Assert.IsNotNull(Named(screen, column), column + " bloğu");
            }

            int chosen = ChosenRow(screen);
            Assert.GreaterOrEqual(chosen, 0, "bir silah seçili");
            Assert.AreEqual(screen.WeaponRows[chosen].gameObject, Focused, "odak seçili silahta açılır");
            Assert.AreEqual(chosen, screen.StageIndex, "sahne seçili silahı gösterir");
            WeaponDefinition def = Arsenal.Get(PrototypeUiFlow.SelectedWeapon);
            Assert.AreEqual(UiStrings.Upper(UiStrings.Get("weapon." + def.weaponId + ".name", def.weaponId)), screen.StageTexts[0].text);

            Assert.IsNotNull(screen.Stage, "3B önizleme kuruldu");
            Assert.AreEqual(def.weaponId, screen.Stage.WeaponId, "önizleme seçili silahı gösterir");
            Canvas.ForceUpdateCanvases();
            Rect card = WorldRect((RectTransform)Named(screen, "StageCard"));
            Rect well = WorldRect(screen.PreviewWell);
            Assert.IsTrue(screen.PreviewWell.gameObject.activeInHierarchy, "önizlemenin yeri var");
            Assert.IsTrue(card.Contains(well.min) && card.Contains(well.max), "önizleme kartın içinde");
            Assert.Greater(well.height, 0f);

            yield return Bekle(() => screen.Stage.Renders > 0, 5f, "önizleme ilk karesini çizmedi");
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== 2 · the preview follows the focus and the choice

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahOdagi_VeSecim_OnizlemeShowWeapon_BaskaKontroldeSeciliSilah()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return OpenPrepareRun();
            SelectionScreen screen = PrepareScreen();
            Assert.IsNotNull(screen.Stage, "3B önizleme");

            UiButton other = null;
            int otherIndex = -1;
            for (int i = 0; i < screen.WeaponRows.Length; i++)
            {
                UiButton row = screen.WeaponRows[i];
                if (!row.IsSelectedChoice && row.ConfirmCue)
                {
                    other = row;
                    otherIndex = i;
                    break;
                }
            }

            Assert.IsNotNull(other, "ikinci açık silah");
            string otherId = WeaponIdOf(other);
            EventSystem.current.SetSelectedGameObject(other.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(otherIndex, screen.StageIndex, "sahne odağı izler");
            Assert.AreEqual(otherId, screen.Stage.WeaponId, "önizleme odaktaki silahı gösterir (ShowWeapon)");

            other.OnSubmit(null);
            yield return null;
            ClassicWeaponId id;
            Assert.IsTrue(TryClassic(other, out id));
            Assert.AreEqual(id, PrototypeUiFlow.SelectedWeapon, "seçim değişti");
            Assert.IsTrue(other.IsSelectedChoice);
            int checks = 0;
            foreach (UiButton row in screen.WeaponRows)
            {
                checks += row.IsSelectedChoice ? 1 : 0;
            }

            Assert.AreEqual(1, checks, "tam bir silah seçili");

            // Focus off the weapons (START RUN, a mode row): the stage shows the run's weapon, the new choice.
            EventSystem.current.SetSelectedGameObject(Named(screen, "Start").gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(otherId, screen.Stage.WeaponId, "START RUN'da önizleme koşunun silahı");
            EventSystem.current.SetSelectedGameObject(screen.DifficultyRows[0].gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(otherIndex, screen.StageIndex, "zorluk satırında sahne seçili silahta kalır");

            // A pointer on the preview leaves no focus: the stage keeps what it shows.
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            Assert.AreEqual(otherIndex, screen.StageIndex);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== 3 · difficulty: walk and choose

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zorluk_Satirlar_GezintiVeSecim_ServiseYazar_SatirOdaktaAyrinti()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsTrue(UiStrings.Has("difficulty.normal.name") && UiStrings.Has("difficulty.hard.desc") && UiStrings.Has("difficulty.demolition.detail"),
                "WP-106 metinleri pakette (strings-proposal r1-strings'e eklendi)");
            yield return BootTanMainMenuye();
            yield return OpenPrepareRun();
            SelectionScreen screen = PrepareScreen();
            UiButton[] modes = screen.DifficultyRows;
            Assert.AreEqual(3, modes.Length, "Normal / Hard / Demolition");
            Assert.AreEqual("Difficulty.normal", modes[0].name);
            Assert.AreEqual("Difficulty.demolition", modes[2].name);
            Assert.AreEqual(UiStrings.Get("difficulty.demolition.name", "DEMOLITION"), modes[2].LabelText.text, "ad paketten, büyük harfe çevrilmeden");
            Assert.IsTrue(modes[0].IsSelectedChoice && !modes[1].IsSelectedChoice && !modes[2].IsSelectedChoice, "varsayılan Normal");
            Assert.IsTrue(modes[0].transform.Find("Status").GetComponent<Image>().enabled, "seçim check glifi");
            for (int i = 0; i < modes.Length; i++)
            {
                int full = 0;
                for (int m = 0; m < 3; m++)
                {
                    Image mark = modes[i].transform.Find("Mark" + m).GetComponent<Image>();
                    full += mark.sprite != null && mark.sprite == UiArt.Icon(SelectionScreen.MarkFull) ? 1 : 0;
                }

                Assert.AreEqual(i + 1, full, modes[i].name + ": zorluk göstergesi " + (i + 1) + " dolu işaret");
            }

            // At rest the line under the rows is the chosen mode's short line.
            Assert.AreEqual(UiStrings.Get("difficulty.normal.desc", string.Empty), screen.ModeLine.text);

            // The pad: from the chosen weapon Left → a mode row, Down walks the modes, Right → the chosen weapon.
            int chosen = ChosenRow(screen);
            GameObject weapon = screen.WeaponRows[chosen].gameObject;
            Assert.AreEqual(weapon, Focused);
            Move(MoveDirection.Left);
            yield return null;
            Assert.IsTrue(Focused != null && Focused.name.StartsWith("Difficulty.", System.StringComparison.Ordinal), "sol → zorluk satırı; odak: " + (Focused != null ? Focused.name : "yok"));
            EventSystem.current.SetSelectedGameObject(modes[0].gameObject);
            yield return null;
            Move(MoveDirection.Down);
            yield return null;
            Assert.AreEqual(modes[1].gameObject, Focused, "aşağı → sonraki mod");
            Move(MoveDirection.Down);
            yield return null;
            Assert.AreEqual(modes[2].gameObject, Focused);
            yield return null;
            Assert.AreEqual(UiStrings.Get("difficulty.demolition.detail", string.Empty), screen.ModeLine.text, "odaktaki modun ayrıntısı yalnız odakta");
            Move(MoveDirection.Down);
            yield return null;
            Assert.AreEqual("Back", Focused.name, "son moddan aşağı → Geri");
            Move(MoveDirection.Right);
            yield return null;
            Assert.AreEqual("Start", Focused.name, "Geri ↔ Koşuyu başlat");

            // Choose Demolition: the service holds it, its row takes the check, the others lose it.
            int choices = 0;
            System.Action<UiEvent, string> count = (e, detail) => { if (e == UiEvent.ChoiceSelected && detail == "Difficulty") choices++; };
            UiEvents.Raised += count;
            try
            {
                EventSystem.current.SetSelectedGameObject(modes[2].gameObject);
                yield return null;
                modes[2].OnSubmit(null);
                yield return null;
            }
            finally
            {
                UiEvents.Raised -= count;
            }

            Assert.AreEqual(DifficultyMode.Demolition, DifficultyService.Selected, "seçim WP-102 servisine yazıldı");
            Assert.AreEqual(1, choices, "bir ChoiceSelected");
            Assert.IsTrue(modes[2].IsSelectedChoice && !modes[0].IsSelectedChoice && !modes[1].IsSelectedChoice);
            Move(MoveDirection.Right);
            yield return null;
            Assert.AreEqual(weapon, Focused, "sağ → seçili silah");
            yield return null;
            Assert.AreEqual(UiStrings.Get("difficulty.demolition.desc", string.Empty), screen.ModeLine.text, "odak gidince seçili modun kısa satırı");

            // Session memory: back to the menu and in again - Demolition is still the choice.
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return OpenPrepareRun();
            Assert.IsTrue(PrepareScreen().DifficultyRows[2].IsSelectedChoice, "oturum içinde hatırlanır");
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== 4 · the lock chip

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator KilitCipi_PakettenGelir_DuvardanSatinAlma_CiplakSeviyeYok()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return OpenPrepareRun();
            SelectionScreen screen = PrepareScreen();
            UiButton wallBuy = null;
            foreach (UiButton row in screen.WeaponRows)
            {
                ClassicWeaponId id;
                Assert.IsTrue(TryClassic(row, out id), row.name);
                string trailing = row.transform.Find("Trailing").GetComponent<TMP_Text>().text;
                if (ProfileService.IsWeaponUnlocked(id))
                {
                    Assert.IsEmpty(trailing, row.name + ": açık silahta çip yok");
                    continue;
                }

                MetaPathRules.WeaponLockInfo info = ProfileService.WeaponLock(id);
                Assert.AreEqual(SelectionScreen.LockChipFor(info), trailing, row.name + ": çip kilitten");
                Assert.IsFalse(Regex.IsMatch(trailing, @"^L\d"), row.name + ": çıplak 'L3' yok (selection.lock.chip)");
                if (PrepareRunLayout.IsWallBuyOnly(info))
                {
                    Assert.AreEqual(UiStrings.Get("selection.lock.wallbuy_chip", "WALL BUY"), trailing);
                    wallBuy = row;
                }
            }

            if (wallBuy == null)
            {
                Assert.Inconclusive("bu kadroda duvardan alınan silah yok (WP-107: Bohiya)");
            }

            ClassicWeaponId before = PrototypeUiFlow.SelectedWeapon;
            EventSystem.current.SetSelectedGameObject(wallBuy.gameObject);
            yield return null;
            yield return null;
            StringAssert.StartsWith(UiStrings.Get("selection.lock.wallbuy", "Bought at its wall").Substring(0, 8), screen.StateLine.text, "durum satırı nedenini söyler");
            wallBuy.OnSubmit(null);
            yield return null;
            Assert.AreEqual(before, PrototypeUiFlow.SelectedWeapon, "duvardan alınan silahla koşu başlamaz");
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== 5 · the preview's words on hover only

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator OnizlemeIpucu_GlifHepVar_SozcuklerYalnizImlecUzerindeyken()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse);
            try
            {
                yield return OpenPrepareRun();
                SelectionScreen screen = PrepareScreen();
                Assert.IsNotNull(screen.Stage);
                Assert.IsNotNull(screen.PreviewHover, "imleç bildirimi önizlemenin görüntüsünde");
                Assert.AreSame(screen.Stage.View.gameObject, screen.PreviewHover.gameObject, "aynı nesne: sahnenin sürükleme ve tekerleği yerinde kalır");
                Image glyph = screen.PreviewWell.Find("Hint").GetComponent<Image>();
                Assert.IsTrue(glyph.enabled, "köşe glifi dinlenmede");
                Assert.IsFalse(screen.PreviewHintText.gameObject.activeSelf, "sözcükler dinlenmede yok (R2 U1)");

                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(screen.Stage.View.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                Assert.IsTrue(screen.PreviewHintText.gameObject.activeSelf, "imleç üzerinde: sözcükler");
                ExecuteEvents.Execute(screen.Stage.View.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                Assert.IsFalse(screen.PreviewHintText.gameObject.activeSelf, "imleç gidince sözcükler gider");

                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                yield return null;
                Assert.AreSame(UiArt.Icon("Pad.RSMove"), glyph.sprite, "pad: sağ çubuk glifi");
            }
            finally
            {
                UiInputScheme.Release();
            }

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== 6 · R2 U1 early warning

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator U1_PrepareRun_En_HarfPayIcinde()
        {
            LogAssert.ignoreFailingMessages = true;
            UiSettings.Language = UiStrings.English;
            UiSettings.TextScale = 1f;
            yield return BootTanMainMenuye();
            yield return OpenPrepareRun();
            yield return new WaitForSecondsRealtime(1.5f);   // the columns' entrance settles (as the U1 instrument waits)
            int letters = 0;
            var lines = new List<string>();
            foreach (TMP_Text text in Object.FindObjectsOfType<TMP_Text>())
            {
                int n = UiTextInventoryTests.VisibleLetters(text);
                if (n > 0)
                {
                    letters += n;
                    lines.Add(text.transform.parent.name + "/" + text.name + "|" + n + "|" + text.GetParsedText().Replace('\n', ' '));
                }
            }

            lines.Sort(System.StringComparer.Ordinal);
            foreach (string line in lines)
            {
                Debug.Log("[DD-TEST] wp106.text en.prepare|" + line);
            }

            Debug.Log("[DD-TEST] wp106.u1 en.prepare letters=" + letters + " budget=" + LetterBudgetEn);
            Assert.LessOrEqual(letters, LetterBudgetEn, "Prepare Run EN harfleri R2 U1 payı içinde");
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== 7 · U2: nothing cut, Mac canvas, text 1.25, EN / TR

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Metin125_MacUi12_EnTr_HicbirMetinKesilmez_LobDahil()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            var found = new List<string>();
            foreach (string language in new[] { UiStrings.English, UiStrings.Turkish })
            {
                foreach (float ui in new[] { 1f, 1.2f })
                {
                    UiSettings.Language = language;
                    UiSettings.TextScale = 1.25f;
                    UiSettings.UiScale = ui;
                    yield return OpenPrepareRun();
                    yield return new WaitForSecondsRealtime(0.5f);
                    SelectionScreen screen = PrepareScreen();
                    string combo = language + ".t1.25.u" + ui.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    Canvas.ForceUpdateCanvases();
                    CutTexts(screen, combo + " rest", found);

                    // The longest words the page can show: the hardest mode's detail (on focus), a locked weapon's state line.
                    UiButton[] modes = screen.DifficultyRows;
                    EventSystem.current.SetSelectedGameObject(modes[modes.Length - 1].gameObject);
                    yield return null;
                    yield return null;
                    CutTexts(screen, combo + " mode-focus", found);
                    foreach (UiButton row in screen.WeaponRows)
                    {
                        if (!row.ConfirmCue)
                        {
                            EventSystem.current.SetSelectedGameObject(row.gameObject);
                            yield return null;
                            yield return null;
                            CutTexts(screen, combo + " locked-focus " + row.name, found);
                            break;
                        }
                    }

                    // The Specials' fire modes (R2 U2 red: "L…" at UI 1.2 × text 1.25) are whole and on screen.
                    foreach (string special in new[] { "Special.sw_arc", "Special.sw_gel" })
                    {
                        TMP_Text mode = Named(screen, special).Find("Mode").GetComponent<TMP_Text>();
                        mode.ForceMeshUpdate();
                        Assert.IsFalse(mode.isTextTruncated || mode.isTextOverflowing, combo + ": " + special + " modu kesik: " + mode.GetParsedText());
                        Assert.AreEqual(UiStrings.Get("weapon." + special.Substring(8) + ".firemode", string.Empty), mode.GetParsedText(), combo + ": " + special + " modu tam");
                    }

                    // Every block of the stage and the setup column stays inside its column.
                    Rect stage = WorldRect((RectTransform)Named(screen, "Stage"));
                    foreach (string block in new[] { "Identity", "Stats", "State" })
                    {
                        Rect r = WorldRect(Named(screen, block));
                        Assert.GreaterOrEqual(r.yMin, stage.yMin - 0.5f, combo + ": " + block + " sahne kolonunun altına taşıyor");
                    }

                    Rect setup = WorldRect((RectTransform)Named(screen, "Setup"));
                    foreach (string block in new[] { "Map", "ModeLine" })
                    {
                        Rect r = WorldRect(Named(screen, block));
                        Assert.GreaterOrEqual(r.yMin, setup.yMin - 0.5f, combo + ": " + block + " kurulum kolonunun altına taşıyor");
                    }

                    Rect weapons = WorldRect((RectTransform)Named(screen, "Weapons"));
                    Assert.GreaterOrEqual(WorldRect(Named(screen, "Specials")).yMin, weapons.yMin - 0.5f, combo + ": Special şeridi kolona sığar");
                    Debug.Log("[DD-TEST] wp106.u2 " + combo + " statColumns=" + screen.StatColumns + " specialsBeside=" + screen.SpecialsBeside);
                    PrototypeUiFlow.GoToMainMenu();
                    yield return SahneyiBekle(PrototypeScenes.MainMenu);
                }
            }

            foreach (string line in found)
            {
                Debug.Log("[DD-TEST] wp106.cut " + line);
            }

            Assert.IsEmpty(found, "Prepare Run: kesik / taşan metin yok (U2)");
        }
    }
}
