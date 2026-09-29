using System.Collections;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    public sealed class RunHudInitializationTests
    {
        [UnityTest]
        public IEnumerator ColdStrings_English_HudBuildsInAwake()
        {
            yield return ColdStringsBuild(UiStrings.English,
                "waiting for the round to start", "no upgrades", "LOW AMMO");
        }

        [UnityTest]
        public IEnumerator ColdStrings_Turkish_HudBuildsInAwake()
        {
            yield return ColdStringsBuild(UiStrings.Turkish,
                "roundun başlaması bekleniyor", "geliştirme yok", "AZ MERMİ");
        }

        private static IEnumerator ColdStringsBuild(string language, string waiting, string noUpgrades, string lowAmmo)
        {
            // Canvas creation loads settings, including language; settle that first.
            UiSettings.EnsureLoaded();
            string previousLanguage = UiStrings.Language;
            var root = new GameObject("HudInitializationProbe");
            root.SetActive(false);
            try
            {
                UiStrings.Language = language;
                UiStrings.ResetForTests(); // Keep Resources cold through AddComponent, not just activation.
                RunHudScreen hud = root.AddComponent<RunHudScreen>();
                root.SetActive(true);

                Assert.IsNotNull(hud.Model, "Awake must finish creating the localized presenter.");
                Assert.AreEqual(waiting, hud.Model.EnemiesBreakdown);
                Assert.AreEqual(noUpgrades, hud.Model.BuildText);
                Assert.AreEqual(lowAmmo, hud.AmmoLowWord);
                Assert.AreEqual(RunHudViewModel.Placeholder, hud.Model.RoundText);
                Assert.AreEqual("0", hud.Model.GoldText);
                Assert.IsFalse(hud.Model.HealthKnown);
                Assert.AreEqual(RunHudViewModel.Placeholder, hud.Model.HealthText);

                // A constructor exception used to leave the later ink lists null,
                // so Awake stopped at the first cluster's BackPlane.
                Assert.AreEqual(4, hud.Clusters.Count, "All four persistent readouts must finish building.");
                foreach (RectTransform cluster in hud.Clusters)
                {
                    Transform back = cluster.Find("Back");
                    Assert.IsNotNull(back, cluster.name + " back plane must exist.");
                    Assert.IsNotNull(back.GetComponent<Canvas>(), cluster.name + " back plane canvas must exist.");
                }
                yield return null;
            }
            finally
            {
                Object.Destroy(root); // Only this test's HUD and its children.
                UiStrings.Language = previousLanguage;
                UiStrings.ResetForTests();
            }
            yield return null;
        }
    }
}
