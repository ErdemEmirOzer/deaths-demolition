using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Difficulty;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-102 (S-03 R4; S-09 UI): a Hard run shows its mode under the round (one ink word, the secondary size in the halo)
    /// and a Normal run shows nothing there - the classic HUD keeps its four clusters and its coverage.
    /// </summary>
    public sealed class DifficultyHudResultsTests
    {
        [TearDown]
        public void TearDown()
        {
            DifficultyService.Selected = DifficultyMode.Normal;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Hud_ZorKosuda_ModSozcugu_NormaldeYok()
        {
            yield return BootTanMainMenuye();
            DifficultyService.Selected = DifficultyMode.Hard;
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            Assert.AreEqual(DifficultyMode.Hard, DifficultyService.RunMode, "the run took the selected mode");

            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD yok");
            Assert.IsNotNull(hud.ModeWord, "Hard: the mode word shows under the round");
            Assert.AreEqual(UiStrings.Get("difficulty.hard.name", "HARD"), hud.ModeWord.text);
            Assert.IsTrue(UiHudInk.HasHalo(hud.ModeWord), "the HUD halo (V-53)");
            Assert.AreEqual(4, hud.Clusters.Count, "the word is not a fifth cluster");
            // AD S8: the word's right edge is the seal's - the round plate's right end, not the margin 88 px past it.
            var corners = new Vector3[4];
            hud.ModeWord.rectTransform.GetWorldCorners(corners);
            float wordRight = corners[2].x;
            hud.RoundSeal.GetWorldCorners(corners);
            Assert.AreEqual(corners[2].x, wordRight, 1.5f, "the mode word ends where the seal ends");
            Assert.AreEqual(UiStrings.Get("difficulty.hard.name", "HARD"), ResultsViewModel.DescribeMode(DifficultyService.RunMode),
                "Results names the same mode on its reason line");

            DifficultyService.Selected = DifficultyMode.Normal;
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud);
            Assert.IsNull(hud.ModeWord, "Normal: nothing under the round");
        }
    }
}
