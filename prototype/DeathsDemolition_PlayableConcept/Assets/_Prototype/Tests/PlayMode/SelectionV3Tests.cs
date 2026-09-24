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

            ClassicWeaponId before = PrototypeUiFlow.SelectedWeapon;
            locked.OnSubmit(null);
            yield return null;
            Assert.AreEqual(before, PrototypeUiFlow.SelectedWeapon, "kilitli silah seçilemez");

            TMPro.TMP_Text loadout = Named(screen, "Loadout").GetComponent<TMPro.TMP_Text>();
            StringAssert.Contains(PlayerSkins.Get(PrototypeUiFlow.SelectedSkin).DisplayName, loadout.text, "START RUN yükü yankılar");

            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
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
            Assert.IsFalse(string.IsNullOrEmpty(texts[3].text), when + ": açıklama (paket)");
        }
    }
}
