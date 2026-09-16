using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-032 · Art-director A/B evidence: the three attack wind-ups from the gameplay camera
    /// (19.9 m, −49°) with the telegraph emphasis layer OFF (WP-018) and ON (WP-032), at 40 / 70 /
    /// 95 % of the wind-up. Not a gate — run by filter, written to DD_ART_CAPTURE_DIR.
    /// </summary>
    [TestFixture]
    [Category("WP032Capture")]
    public sealed class AnimationTelegraphCaptureTests
    {
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            }
        }

        [TearDown]
        public void TearDown() { LogAssert.ignoreFailingMessages = false; }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator TelegrafAB_OyunKamerasi_UcSaldiri()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, 60f, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, 60f, "Run");
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, 60f, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, 60f, "Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
            RunFlowClock.AddHold("wp032-capture");
            foreach (ZombieEnemy live in new List<ZombieEnemy>(ZombieEnemy.Live)) if (live != null && live.IsSpawned) live.Despawn();
            yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp032-captures");
            Directory.CreateDirectory(dir);
            CharacterAssetSet set = CharacterAssetSet.Load();
            Vector3 feet = PlayerRegistry.Primary.transform.position;
            Vector3 stage = feet + new Vector3(2.2f, 0f, 1.0f);
            var zroot = new GameObject("WP032_ZombieStage");
            _temp.Add(zroot);
            zroot.transform.SetPositionAndRotation(stage, Quaternion.LookRotation(new Vector3(0f, 0f, -1f)));
            GameObject zgo = UnityEngine.Object.Instantiate(set.ZombiePrefab, zroot.transform, false);
            var zv = zgo.GetComponent<ZombieSkinnedVisual>();
            zv.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, 7u));
            zv.ApplyMaterial(set.ZombieLibrary, 0);
            var lod = zgo.GetComponent<CharacterAnimatorLod>();
            if (lod != null) lod.enabled = false;
            Animator an = zv.Animator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;
            int tele = zv.TelegraphLayerIndex;
            Assert.GreaterOrEqual(tele, 0);
            var pose = new ArtVisualRegression.CameraPose("telegraph-stage", stage + new Vector3(-2.2f, 15f, -13f), stage + new Vector3(-2.2f, 0.9f, 0f), 42f);
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, CharacterTests.W, CharacterTests.H, 60, f => warm = f);

            string[] attackStates = { C.AttackSwipeState, C.AttackLungeState, C.AttackSweepState };
            string[] names = { "swipe", "lunge", "sweep" };
            float[] windupFraction = { 16f / 33f, 10f / 29f, 24f / 50f };   // hit_window_start / (frames − 1)
            float[] samples = { 0.4f, 0.7f, 0.95f };
            an.enabled = false;
            int written = 0;
            for (int i = 0; i < 3; i++)
            for (int on = 0; on < 2; on++)
            for (int k = 0; k < samples.Length; k++)
            {
                float t = windupFraction[i] * samples[k];
                an.SetLayerWeight(tele, on);
                an.Play(C.EmptyState, 1, 0f); an.Play(C.EmptyState, 2, 0f);
                an.Play(attackStates[i], 0, t);
                if (on == 1) an.Play(C.TelegraphState + "_" + i, tele, samples[k]); else an.Play(C.EmptyState, tele, 0f);
                an.Update(0f);
                yield return null;
                CharacterTests.Capture(pose, Path.Combine(dir, "telegraph_" + names[i] + "_" + (on == 1 ? "on" : "off") + "_s" + k + ".png"));
                written++;
            }

            Debug.Log("[DD-TEST] wp032.capture telegraph frames=" + written + " dir=" + dir + " warm=" + warm);
            Assert.AreEqual(18, written);
        }
    }
}
