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
    /// WP-032B · Art-director evidence for the AP-009B v002 set from the gameplay camera: the Ultra
    /// overhead wind-up / strike, the player run ring at 6.2 m/s, the pickup over the walk (DEC-082)
    /// and the queue idle. Not a gate — run by filter, written to DD_ART_CAPTURE_DIR.
    /// </summary>
    [TestFixture]
    [Category("WP032BCapture")]
    public sealed class AnimationV002CaptureTests
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
        public IEnumerator V002_OyunKamerasi_Overhead_Kosu_Pickup_KuyrukIdle()
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
            RunFlowClock.AddHold("wp032b-capture");
            foreach (ZombieEnemy live in new List<ZombieEnemy>(ZombieEnemy.Live)) if (live != null && live.IsSpawned) live.Despawn();
            yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp032b-captures");
            Directory.CreateDirectory(dir);
            CharacterAssetSet set = CharacterAssetSet.Load();
            Vector3 feet = PlayerRegistry.Primary.transform.position;
            Vector3 stage = feet + new Vector3(2.2f, 0f, 1.0f);
            var zroot = new GameObject("WP032B_ZombieStage");
            _temp.Add(zroot);
            zroot.transform.SetPositionAndRotation(stage, Quaternion.LookRotation(new Vector3(0f, 0f, -1f)));
            GameObject zgo = UnityEngine.Object.Instantiate(set.ZombiePrefab, zroot.transform, false);
            var zv = zgo.GetComponent<ZombieSkinnedVisual>();
            zv.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, 7u));
            zv.ApplyMaterial(set.ZombieLibrary, 3);   // Ultra tint
            var lod = zgo.GetComponent<CharacterAnimatorLod>();
            if (lod != null) lod.enabled = false;
            Animator an = zv.Animator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;
            int tele = zv.TelegraphLayerIndex;
            var pose = new ArtVisualRegression.CameraPose("v002-stage", stage + new Vector3(-2.2f, 15f, -13f), stage + new Vector3(-2.2f, 0.9f, 0f), 42f);
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, CharacterTests.W, CharacterTests.H, 60, f => warm = f);

            int written = 0;
            an.enabled = false;
            // 1) Ultra overhead: wind-up 40 / 70 / 95 % (hit_window_start 35 of 63), strike (frame 38), recovery hold (frame 63)
            float windup = 35f / 63f;
            float[] overheadT = { windup * 0.4f, windup * 0.7f, windup * 0.95f, 38f / 63f, 1f };
            string[] overheadN = { "windup40", "windup70", "windup95", "strike", "hold" };
            for (int k = 0; k < overheadT.Length; k++)
            {
                an.Play(C.EmptyState, 1, 0f); an.Play(C.EmptyState, 2, 0f); if (tele >= 0) an.Play(C.EmptyState, tele, 0f);
                an.Play(C.AttackOverheadState, 0, overheadT[k]);
                an.Update(0f);
                yield return null;
                CharacterTests.Capture(pose, Path.Combine(dir, "overhead_" + overheadN[k] + ".png"));
                written++;
            }

            // 2) Queue idle at the shift_l / shift_r beats and mid-cycle
            float[] queueT = { 22f / 90f, 45f / 90f, 67f / 90f };
            for (int k = 0; k < queueT.Length; k++)
            {
                an.Play(C.WaitState, 0, queueT[k]);
                an.Update(0f);
                yield return null;
                CharacterTests.Capture(pose, Path.Combine(dir, "idle_queue_" + k + ".png"));
                written++;
            }

            // 3) Player: run ring at 6.2 m/s (three phases) and the pickup over the walk (DEC-082)
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv);
            Animator pa = pv.Animator;
            int over = -1;
            for (int i = 0; i < pa.layerCount; i++) if (pa.GetLayerName(i) == C.OverrideUpperLayer) over = i;
            pv.enabled = false;
            pa.enabled = false;
            pa.SetFloat(C.MoveX, 0f); pa.SetFloat(C.MoveY, 1f); pa.SetFloat(C.LocoRate, 1f);
            var ppose = new ArtVisualRegression.CameraPose("v002-player", feet + new Vector3(0f, 15f, -13f), feet + new Vector3(0f, 0.9f, 0f), 42f);
            float[] runT = { 0.0f, 0.33f, 0.66f };
            for (int k = 0; k < runT.Length; k++)
            {
                if (over >= 0) pa.Play(C.EmptyState, over, 0f);
                pa.Play(C.LocomotionState, 0, runT[k]);
                pa.Update(0f);
                yield return null;
                CharacterTests.Capture(ppose, Path.Combine(dir, "player_run_" + k + ".png"));
                written++;
            }

            pa.SetFloat(C.MoveY, 0.5f);   // walk ring
            float[] pickT = { 0.2f, 0.46f, 0.8f };   // grab at frame 11 of 23 ≈ 0.48
            for (int k = 0; k < pickT.Length; k++)
            {
                pa.Play(C.LocomotionState, 0, 0.3f * k);
                if (over >= 0) pa.Play(C.PickupState, over, pickT[k]);
                pa.Update(0f);
                yield return null;
                CharacterTests.Capture(ppose, Path.Combine(dir, "player_pickup_walk_" + k + ".png"));
                written++;
            }

            Debug.Log("[DD-TEST] wp032b.capture frames=" + written + " dir=" + dir + " warm=" + warm);
            Assert.AreEqual(14, written);
        }
    }
}
