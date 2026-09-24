using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DD.Prototype.PlayerSystem;
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
    /// WP-032E · Special Weapon animation: the player's use beat while holding a Special is the
    /// generated swing (SwFire_<hold>) over the SwHold pose, not the classic recoil; the enemy's
    /// Netted (frozen) and Dazed (stumble → sway) visuals and their release.
    /// </summary>
    [TestFixture]
    [Category("WP032E")]
    public sealed class AnimationSpecialTests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            CharacterAnimatorLod.ResetStatics();
            FootIkAdapter.ResetStatics();
            CharacterAnimationEvents.ResetStatics();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            RunFlowClock.ClearHolds();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        private IEnumerator MainMenu()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
        }

        private ZombieSkinnedVisual ZombieOnStage(Vector3 at, EnemyTier tier, uint seed, bool rise = false)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP032C_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);
            GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(tier, 2.7f, "A", 0, rise);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return v;
        }

        private PlayerSkinnedVisual PlayerOnStage(Func<Vector3> velocity)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP032C_PlayerStage");
            _temp.Add(stage);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(stage.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            pv.Attach(visual, velocity, () => 6.2f, () => Vector3.forward);
            Assert.IsTrue(pv.Authored, "oyuncu AP-009 seti değil");
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return pv;
        }

        private static bool In(Animator an, int layer, string state)
        {
            return an.GetCurrentAnimatorStateInfo(layer).IsName(state) || an.GetNextAnimatorStateInfo(layer).IsName(state);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_SpecialTutarken_Ates_SwingAdditive_ClassicDegil()
        {
            yield return MainMenu();
            Vector3 velocity = Vector3.zero;
            PlayerSkinnedVisual pv = PlayerOnStage(() => velocity);
            Animator an = pv.Animator;
            int over = ArtAnimatorLayers.Index(an, C.OverrideUpperLayer), add = ArtAnimatorLayers.Index(an, C.AdditiveLayer);
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_0"), 2f, "Aim_0");
            pv.PlayFire();
            yield return null; yield return null;
            Assert.IsTrue(In(an, add, C.FireState + "_0"), "klasik silahla Fire_0");
            yield return CharacterTests.Bekle(() => In(an, add, C.EmptyState) && !an.IsInTransition(add), 2f, "recoil bitti");
            pv.SetSpecialHold(1);
            yield return CharacterTests.Bekle(() => In(an, over, C.SwHoldState + "_1") && !an.IsInTransition(over), 2f, "SwHold_1");
            pv.PlayFire();
            yield return null; yield return null;
            Assert.IsTrue(In(an, add, C.SwFireState + "_1"), "bell tutarken SwFire_1 (swing) oynamalı");
            Assert.IsFalse(In(an, add, C.FireState + "_0"), "bell tutarken klasik recoil oynamamalı");
            Assert.IsTrue(In(an, over, C.SwHoldState + "_1"), "swing tutuş pozunu kesmemeli");
            yield return CharacterTests.Bekle(() => In(an, add, C.EmptyState) && !an.IsInTransition(add), 2f, "swing bitti");
            pv.SetSpecialHold(2);
            yield return CharacterTests.Bekle(() => In(an, over, C.SwHoldState + "_2") && !an.IsInTransition(over), 2f, "SwHold_2");
            pv.PlayFire();
            yield return null; yield return null;
            Assert.IsTrue(In(an, add, C.SwFireState + "_2"), "loom tutarken SwFire_2");
            pv.SetSpecialHold(0);
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_0") && !an.IsInTransition(over), 2f, "special bitince Aim_0");
            // WP-056 usage bus (ADR-056-1): Activated → hold pose of that Special, Deactivated → the classic Aim
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Namekuji, SpecialUsageKind.Activated, "test", Vector3.zero, Vector3.forward, 0f, 0f, 0);
            yield return CharacterTests.Bekle(() => In(an, over, C.SwHoldState + "_2") && !an.IsInTransition(over), 2f, "Activated(loom) → SwHold_2");
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Namekuji, SpecialUsageKind.Deactivated, "test", Vector3.zero, Vector3.forward, 0f, 0f, 0);
            yield return CharacterTests.Bekle(() => In(an, over, C.AimState + "_0") && !an.IsInTransition(over), 2f, "Deactivated → Aim_0");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Zombi_Netted_Donar_Dazed_StumbleSonraSway_Serbest()
        {
            yield return MainMenu();
            ZombieSkinnedVisual v = ZombieOnStage(new Vector3(0f, 0f, 0f), EnemyTier.Standard, 3u);
            Animator an = v.Animator;
            for (int i = 0; i < 3; i++) yield return null;
            for (int i = 0; i < 10; i++) { v.SetSpeed(2.7f, Time.deltaTime); yield return null; }
            // Netted: frozen where it stands — the base state's normalized time stops, the look-at gate closes
            v.SetSpecialVisual(ZombieSpecialVisual.Netted);
            yield return null;
            float t0 = an.GetCurrentAnimatorStateInfo(0).normalizedTime;
            for (int i = 0; i < 20; i++) { v.SetSpeed(2.7f, Time.deltaTime); yield return null; }
            Assert.AreEqual(0f, an.speed, 1e-4f, "netted: animator speed 0");
            Assert.AreEqual(t0, an.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-4f, "netted: poz ilerlemedi");
            Assert.AreEqual(ZombieSpecialVisual.Netted, v.SpecialVisual);
            // release
            v.SetSpecialVisual(ZombieSpecialVisual.None);
            for (int i = 0; i < 10; i++) { v.SetSpeed(2.7f, Time.deltaTime); yield return null; }
            Assert.AreEqual(1f, an.speed, 1e-4f, "serbest: speed 1");
            Assert.AreNotEqual(t0, an.GetCurrentAnimatorStateInfo(0).normalizedTime, "serbest: poz ilerledi");
            // Dazed: stumble once, then the queue sway until released; the wait flag S-03 set is restored afterwards
            v.SetWaiting(false);
            v.SetSpecialVisual(ZombieSpecialVisual.Dazed);
            yield return null; yield return null;
            Assert.IsTrue(In(an, 0, C.StumbleState), "dazed: stumble");
            yield return CharacterTests.Bekle(() => In(an, 0, C.WaitState) && !an.IsInTransition(0), 3f, "dazed: stumble sonrası sway (Wait)");
            Assert.IsTrue(an.GetBool(C.Waiting));
            v.SetSpecialVisual(ZombieSpecialVisual.None);
            yield return CharacterTests.Bekle(() => In(an, 0, C.LocomotionState) && !an.IsInTransition(0), 3f, "serbest: Locomotion");
            Assert.IsFalse(an.GetBool(C.Waiting), "serbest: bekleme bayrağı S-03'ün son değerine döndü (false)");
            Assert.AreEqual(ZombieSpecialVisual.None, v.SpecialVisual);
        }
    }
}
