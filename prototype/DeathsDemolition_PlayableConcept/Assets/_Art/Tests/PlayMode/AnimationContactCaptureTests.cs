using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
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
    /// WP-018 · Art-director evidence (§5 fidelity): six frames of each key AP-009 clip as the
    /// Unity Humanoid plays it, from the gameplay camera in the Run scene (AP-005 world, post-fx
    /// on), written to <c>DD_ART_CAPTURE_DIR</c>. The actors are the shipped prefabs on a stage
    /// next to the player; the Animator is stepped by hand to the same normalised times as the
    /// AP-009 contact sheets (0, 0.2, …, 1.0). Not a gate — run by filter for the capture.
    /// </summary>
    [TestFixture]
    [Category("WP018Capture")]
    public sealed class AnimationContactCaptureTests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
        }

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
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator KontakKareleri_AP009_OyunKamerasi()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && RunDirector.Instance != null, SahneZamanAsimi, "oyuncu");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, SahneZamanAsimi, "Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            // Nothing spawns or walks into the frame while we shoot the sheets: hold the run clock
            // (director + enemies freeze under the hold; the stage actors are stepped by hand).
            for (int i = 0; i < 5; i++) yield return null;
            RunFlowClock.AddHold("wp018-capture");
            foreach (DD.Prototype.Enemies.ZombieEnemy live in new List<DD.Prototype.Enemies.ZombieEnemy>(DD.Prototype.Enemies.ZombieEnemy.Live)) if (live != null && live.IsSpawned) live.Despawn();
            yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp018-captures");
            Directory.CreateDirectory(dir);

            CharacterAssetSet set = CharacterAssetSet.Load();
            Vector3 feet = PlayerRegistry.Primary.transform.position;
            Vector3 stage = feet + new Vector3(2.2f, 0f, 1.0f);   // beside the player, facing the camera
            Quaternion facing = Quaternion.LookRotation(new Vector3(0f, 0f, -1f));

            // ---- zombie stage actor (assembled look, fit A, no armour)
            var zroot = new GameObject("WP018_ZombieStage");
            _temp.Add(zroot);
            zroot.transform.SetPositionAndRotation(stage, facing);
            GameObject zgo = UnityEngine.Object.Instantiate(set.ZombiePrefab, zroot.transform, false);
            var zv = zgo.GetComponent<ZombieSkinnedVisual>();
            zv.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, 7u));
            zv.ApplyMaterial(set.ZombieLibrary, 0);
            var zlod = zgo.GetComponent<CharacterAnimatorLod>();
            if (zlod != null) zlod.enabled = false;

            // ---- player stage actor (skin 1)
            var proot = new GameObject("WP018_PlayerStage");
            _temp.Add(proot);
            proot.transform.SetPositionAndRotation(stage + new Vector3(-4.4f, 0f, 0f), facing);
            GameObject pgo = UnityEngine.Object.Instantiate(set.PlayerPrefab, proot.transform, false);
            var pv = pgo.GetComponent<PlayerSkinnedVisual>();
            pv.ApplySkin(0, set.PlayerSkinAlbedos != null && set.PlayerSkinAlbedos.Length > 0 ? set.PlayerSkinAlbedos[0] : null);
            yield return null;

            ArtVisualRegression.CameraPose pose = new ArtVisualRegression.CameraPose("animation-stage", stage + new Vector3(-2.2f, 15f, -13f), stage + new Vector3(-2.2f, 0.9f, 0f), 42f);
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, CharacterTests.W, CharacterTests.H, 60, f => warm = f);

            var sheets = new List<(string clip, Animator an, int layer, string state)>
            {
                (C.ZombieShambleA, zv.Animator, 0, null), (C.ZombieAttackSwipe, zv.Animator, 0, C.AttackSwipeState), (C.ZombieAttackLunge, zv.Animator, 0, C.AttackLungeState),
                (C.ZombieHitHeavyStagger, zv.Animator, 0, C.StaggerState), (C.ZombieDeathForward, zv.Animator, 0, C.DeathState + "_0"), (C.ZombieDeathKneel, zv.Animator, 0, C.DeathState + "_3"),
                (C.ZombieHitLightL, zv.Animator, 1, C.HitState + "_L"), (C.ZombieRise, zv.Animator, 0, C.RiseState),
                (C.PlayerWalkFwd, pv.Animator, 0, null), (C.PlayerFireRecoil, pv.Animator, 2, C.FireState), (C.PlayerHitLight, pv.Animator, 1, C.HitState),   // WP-032C: Fire = the v001 recoil state (Weapon 3 fallback)
                (C.PlayerDeathFwd, pv.Animator, 0, C.DeathState + "_0"), (C.PlayerPickupUpper, pv.Animator, 1, C.PickupState), (C.PlayerIdle, pv.Animator, 0, null)
            };

            zv.Animator.enabled = false;
            pv.Animator.enabled = false;
            // Second strip: a side view at 6 m so the hip drop of the deaths / rise reads (AD S3).
            ArtVisualRegression.CameraPose side = new ArtVisualRegression.CameraPose("animation-side", stage + new Vector3(-2.2f, 1.4f, -6.5f), stage + new Vector3(-2.2f, 0.9f, 0f), 42f);
            Transform zHips = zv.Animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform pHips = pv.Animator.GetBoneTransform(HumanBodyBones.Hips);
            var written = new List<string>();
            foreach ((string clip, Animator an, int layer, string state) sheet in sheets)
            {
                var hips = new List<string>();
                for (int k = 0; k < 6; k++)
                {
                    float t = k / 5f;
                    // Rest the OTHER layers; the target layer gets exactly one Play so Update(0) evaluates it (a
                    // second Play on the same layer in the same frame kept the first pose at t=0 — AD M1).
                    if (sheet.an == zv.Animator)
                    {
                        zv.Animator.SetFloat(C.Speed01, sheet.clip == C.ZombieShambleA ? 1f : 0f);
                        zv.Animator.SetFloat(C.Gait, 0f);
                        zv.Animator.SetFloat(C.IdleVariant, 0f);
                        zv.Animator.SetFloat(C.GaitRate, 1f);
                        zv.Animator.SetFloat(C.AttackRate, 1f);
                        if (sheet.layer != 0 || sheet.state == null) zv.Animator.Play(C.LocomotionState, 0, sheet.clip == C.ZombieShambleA ? t : 0f);
                        if (sheet.layer != 1) zv.Animator.Play(C.EmptyState, 1, 0f);
                        if (sheet.layer != 2) zv.Animator.Play(C.EmptyState, 2, 0f);
                    }
                    else
                    {
                        pv.Animator.SetFloat(C.MoveX, 0f);
                        pv.Animator.SetFloat(C.MoveY, sheet.clip == C.PlayerWalkFwd ? 1f : 0f);
                        pv.Animator.SetFloat(C.LocoRate, 1f);
                        if (sheet.layer != 0 || sheet.state == null) pv.Animator.Play(C.LocomotionState, 0, sheet.clip == C.PlayerWalkFwd || sheet.clip == C.PlayerIdle ? t : 0f);
                        if (sheet.layer != 1) pv.Animator.Play(C.EmptyState, 1, 0f);
                        if (sheet.layer != 2) pv.Animator.Play(C.EmptyState, 2, 0f);
                    }

                    if (sheet.state != null) sheet.an.Play(sheet.state, sheet.layer, Mathf.Min(t, 0.999f));
                    sheet.an.Update(0f);
                    // The skinning job runs once per frame: a mid-frame Camera.Render still draws the previous
                    // pose (AD M1, measured against the Hips readout). Let the frame turn, then capture.
                    yield return null;
                    Transform h = sheet.an == zv.Animator ? zHips : pHips;
                    hips.Add((h != null ? h.position.y - stage.y : -1f).ToString("F2", CultureInfo.InvariantCulture));
                    string png = Path.Combine(dir, sheet.clip + "_f" + k + ".png");
                    CharacterTests.Capture(pose, png);
                    CharacterTests.Capture(side, Path.Combine(dir, sheet.clip + "_side_f" + k + ".png"));
                    written.Add(png);
                    yield return null;
                }

                Debug.Log("[DD-TEST] wp018.capture.hips " + sheet.clip + " y=" + string.Join(" ", hips));
            }

            Debug.Log("[DD-TEST] wp018.capture warm=" + warm + " frames=" + written.Count + " dir=" + dir + " stage=" + stage.ToString("F1"));
            Assert.AreEqual(sheets.Count * 6, written.Count);
        }
    }
}
