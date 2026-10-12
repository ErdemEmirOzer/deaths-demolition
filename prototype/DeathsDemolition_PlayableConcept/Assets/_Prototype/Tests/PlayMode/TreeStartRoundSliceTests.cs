using System;
using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 (C-00 12.10) · dev <c>-ddStartRound N</c>: the run scene's auto start opens round N (pool / health / spawn mix from N),
    /// the V2 tree is built, and the wallet holds the Normal gold banked before N (TreePresetsV2.StartGold, no preset).
    /// </summary>
    public sealed class TreeStartRoundSliceTests
    {
        private const float Timeout = 30f;

        [TearDown]
        public void TearDown()
        {
            TreePresetsV2.TestStartRound = null;
            RoundUpgradeShop.TreeV2TestOverride = null;
            TreeRuntime.Reset();
        }

        [UnityTest]
        public IEnumerator StartRound_OpensRoundN_WithBankedGold()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            TreePresetsV2.TestStartRound = 6;
            RoundUpgradeShop.TreeV2TestOverride = true;

            // A previous test's run scene may still be alive for a frame: wait for THIS load's director, not the old one.
            RunDirector previous = RunDirector.Instance;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while ((RunDirector.Instance == null || RunDirector.Instance == previous || GoldService.Instance == null
                    || PlayerRegistry.Primary == null || RunDirector.Instance.Phase != RunPhase.Fighting) && Time.realtimeSinceStartup - t0 < Timeout)
            {
                yield return null;
            }

            RunDirector run = RunDirector.Instance;
            Assert.IsNotNull(run, "RunDirector oluşmadı");
            Assert.AreEqual(RunPhase.Fighting, run.Phase, "koşu Fighting'e girmedi");
            Assert.AreEqual(6, run.Round, "-ddStartRound 6: koşu R6'da açılır");
            Assert.IsTrue(run.Shop.TreeV2.IsBuilt, "V2 ağaç kuruldu");
            int expected = TreePresetsV2.StartGold(6, 0);
            Assert.AreEqual(471, expected, "R1–R5 Normal altını (devCumulativeGold)");
            Assert.GreaterOrEqual(GoldService.Instance.Wallet.Balance, expected, "cüzdan R6 öncesi altını taşır");
        }

        /// <summary>WP-104 opening: the SHIPPED config runs the feature tree; the v1 node effects are off (no double count).</summary>
        [UnityTest]
        public IEnumerator ShippedConfig_RunsTheFeatureTree_V1EffectsOff()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            RunDirector previous = RunDirector.Instance;
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while ((RunDirector.Instance == null || RunDirector.Instance == previous || RunDirector.Instance.Phase != RunPhase.Fighting)
                   && Time.realtimeSinceStartup - t0 < Timeout)
            {
                yield return null;
            }

            RunDirector run = RunDirector.Instance;
            Assert.IsNotNull(run);
            Assert.IsTrue(DD.Prototype.Core.Config.PrototypeConfig.Active.Economy.RunTreeV2, "sevk config'i: RunTreeV2 açık");
            Assert.IsTrue(run.Shop.TreeV2.IsBuilt, "koşu V2 ağacını kurdu");
            Assert.AreSame(RunTreeEffectValues.None, RunTreeEffects.Active, "v1 düğüm etkileri kapalı");
            Assert.IsFalse(RunTreeEffects.Owned(RunTreeEffects.LinkArcConductor));
        }
    }
}
