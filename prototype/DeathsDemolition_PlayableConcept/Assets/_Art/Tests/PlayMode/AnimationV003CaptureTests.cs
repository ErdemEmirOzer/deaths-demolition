using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// WP-032C · Art-director evidence for the AP-009C v003 set from the gameplay camera: the three
    /// weapon aim poses and a mid-reload, the Special Weapon holds, the tier hit reads, an armour
    /// break, the Ultra death hold frame and the rise_B landing. Not a gate — run by filter, written to DD_ART_CAPTURE_DIR.
    /// </summary>
    [TestFixture]
    [Category("WP032CCapture")]
    public sealed class AnimationV003CaptureTests
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
        public IEnumerator V003_OyunKamerasi_Silah_Reload_TierVurus_ZirhKirilma_UltraOlum_RiseB()
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
            RunFlowClock.AddHold("wp032c-capture");
            foreach (ZombieEnemy live in new List<ZombieEnemy>(ZombieEnemy.Live)) if (live != null && live.IsSpawned) live.Despawn();
            yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp032c-captures");
            Directory.CreateDirectory(dir);
            CharacterAssetSet set = CharacterAssetSet.Load();
            Vector3 feet = PlayerRegistry.Primary.transform.position;
            Vector3 stage = feet + new Vector3(2.2f, 0f, 1.0f);
            var zroot = new GameObject("WP032C_ZombieStage");
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
            var pose = new ArtVisualRegression.CameraPose("v003-stage", stage + new Vector3(-2.2f, 15f, -13f), stage + new Vector3(-2.2f, 0.9f, 0f), 42f);
            int warm = 0;
            yield return ArtVisualRegression.WarmStreaming(pose, CharacterTests.W, CharacterTests.H, 60, f => warm = f);

            int written = 0;
            an.enabled = false;
            // zombie: tier hits (front / right, std / ultra) at the peak (frame 4), armour break torso, ultra death hold + slam, rise_B landing
            (string state, int layer, float t, string name)[] zshots =
            {
                (C.HitState + "_front_std", 1, 4f / 13f, "hit_front_std"), (C.HitState + "_right_std", 1, 4f / 13f, "hit_right_std"),
                (C.HitState + "_front_ultra", 1, 3f / 7f, "hit_front_ultra"), (C.ArmorBreakState + "_1", 0, 6f / 17f, "armorbreak_torso"),
                (C.DeathState + "_7", 0, 2f / 59f, "death_ultra_hold"), (C.DeathState + "_7", 0, 30f / 59f, "death_ultra_slam"),
                (C.RiseState + "_1", 0, 26f / 35f, "rise_B_land"), (C.StumbleState, 0, 6f / 15f, "stumble")
            };
            foreach ((string state, int layer, float t, string name) sh in zshots)
            {
                an.Play(C.LocomotionState, 0, 0f); an.Play(C.EmptyState, 1, 0f); an.Play(C.EmptyState, 2, 0f); if (tele >= 0) an.Play(C.EmptyState, tele, 0f);
                an.Play(sh.state, sh.layer, sh.t);
                an.Update(0f);
                yield return null;
                CharacterTests.Capture(pose, Path.Combine(dir, "z_" + sh.name + ".png"));
                written++;
            }

            // player: LIVE (Animator running, the real API): aim A/B/C, reload B at mag_in, C fire kick, sw hold bell, hurt_B, death_side
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv);
            Animator pa = pv.Animator;
            pa.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            pa.updateMode = AnimatorUpdateMode.UnscaledTime;   // the run clock is held (timeScale 0) — the live poses need real time
            var ppose = new ArtVisualRegression.CameraPose("v003-player", feet + new Vector3(0f, 15f, -13f), feet + new Vector3(0f, 0.9f, 0f), 42f);
            IEnumerator Settle(float seconds) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < seconds) yield return null; }
            for (int w = 0; w < 3; w++)
            {
                pv.SetWeapon(w);
                yield return Settle(0.6f);
                int overL = -1; for (int i = 0; i < pa.layerCount; i++) if (pa.GetLayerName(i) == C.OverrideUpperLayer) overL = i;
                Transform rh = pa.GetBoneTransform(HumanBodyBones.RightHand), lh = pa.GetBoneTransform(HumanBodyBones.LeftHand), ch = pa.GetBoneTransform(HumanBodyBones.Chest);
                Debug.Log("[DD-TEST] wp032c.capture.aim w=" + w + " overWeight=" + (overL >= 0 ? pa.GetLayerWeight(overL).ToString("F2", CultureInfo.InvariantCulture) : "-")
                    + " aimState=" + (overL >= 0 && pa.GetCurrentAnimatorStateInfo(overL).IsName(C.AimState + "_" + w)) + " norm=" + (overL >= 0 ? pa.GetCurrentAnimatorStateInfo(overL).normalizedTime.ToString("F2", CultureInfo.InvariantCulture) : "-")
                    + " rhLocal=" + pa.transform.InverseTransformPoint(rh.position).ToString("F2") + " lhLocal=" + pa.transform.InverseTransformPoint(lh.position).ToString("F2") + " chest=" + pa.transform.InverseTransformPoint(ch.position).ToString("F2")
                    + " weaponMountActive=" + (pv.WeaponSocket != null && pv.WeaponSocket.childCount > 0 ? pv.WeaponSocket.GetChild(0).gameObject.activeInHierarchy.ToString() : "no-child"));
                CharacterTests.Capture(ppose, Path.Combine(dir, "p_aim_" + C.WeaponLetters[w] + ".png"));
                written++;
            }

            pv.SetWeapon(1);
            yield return Settle(0.4f);
            pv.PlayReload(1.7f);
            yield return Settle(1.7f * 34f / 50f);
            CharacterTests.Capture(ppose, Path.Combine(dir, "p_reload_B_mag_in.png"));
            written++;
            yield return Settle(1.2f);
            pv.SetWeapon(2);
            yield return Settle(0.4f);
            pv.PlayFire();
            yield return null; yield return null; yield return null;
            CharacterTests.Capture(ppose, Path.Combine(dir, "p_fire_C_kick.png"));
            written++;
            pv.SetWeapon(0);
            pv.SetSpecialHold(1);
            yield return Settle(0.6f);
            CharacterTests.Capture(ppose, Path.Combine(dir, "p_sw_hold_bell.png"));
            written++;
            pv.SetSpecialHold(0);
            yield return Settle(0.4f);
            pv.PlayHurt(false);
            yield return Settle(5f / 30f);
            CharacterTests.Capture(ppose, Path.Combine(dir, "p_hurt_B.png"));
            written++;
            yield return Settle(0.8f);
            pv.PlayDeath(Vector3.right, false);
            yield return Settle(30f / 30f);
            CharacterTests.Capture(ppose, Path.Combine(dir, "p_death_side.png"));
            written++;

            pa.updateMode = AnimatorUpdateMode.Normal;
            Debug.Log("[DD-TEST] wp032c.capture frames=" + written + " dir=" + dir + " warm=" + warm);
            Assert.AreEqual(16, written);
        }
    }
}
