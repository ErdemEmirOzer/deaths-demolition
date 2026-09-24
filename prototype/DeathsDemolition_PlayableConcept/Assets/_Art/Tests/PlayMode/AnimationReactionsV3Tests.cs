using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-071 · B3 · reactions (K9, III A5, II A5.3): no override hit inside the DEC-029 window, an LMG-rate burst accumulates
    /// instead of stun-locking, the killing impulse's body state / weapon class pick the fall (electrocuted → arc_electro,
    /// launcher → legs out + lift), and the electrocution seizes the pose with a live spasm.
    /// </summary>
    [TestFixture]
    [Category("WP071")]
    public sealed class AnimationReactionsV3Tests
    {
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); RagdollProxyPool.ResetStatics(); CharacterAnimatorLod.ResetStatics(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            foreach (GameObject go in _temp) if (go != null) Object.Destroy(go);
            _temp.Clear();
            RagdollProxyPool.ResetStatics();
            CharacterAssetSet.ResetCache();
            yield return null;
        }

        private IEnumerator Stage()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
            yield return null;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(60f, 0.2f, 60f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
        }

        private ZombieSkinnedVisual Zombie(Vector3 at, uint seed)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP071_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);
            GameObject go = Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return v;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Vurus_PencereIcindeYalnizAdditive_LmgHiziBirikir()
        {
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 60u);
            for (int i = 0; i < 10; i++) yield return null;
            // inside the hit window: additive only
            v.PlayAttack(ZombieAttackStyle.Swipe, 0.55f);
            yield return CharacterTests.Bekle(() => v.HitWindowOpen, 3f, "hit window");
            int over0 = v.OverrideHits, sup0 = v.SuppressedHits;
            v.PlayFlinch(new Vector3(0f, 0f, -1f), BodyRegion.UpperBody, true);
            Assert.AreEqual(over0, v.OverrideHits, "pencere içinde override tepki");
            Assert.AreEqual(sup0 + 1, v.SuppressedHits);
            yield return CharacterTests.Bekle(() => !v.HitWindowOpen, 3f, "window closes");
            for (int i = 0; i < 40; i++) yield return null;
            // LMG-rate burst: 12 hits at 20 Hz → the override reads at most every 0.22 s
            over0 = v.OverrideHits;
            float t0 = Time.time;
            for (int k = 0; k < 12; k++)
            {
                v.PlayFlinch(new Vector3(0f, 0f, -1f), BodyRegion.UpperBody, true);
                float next = t0 + (k + 1) * 0.05f;
                while (Time.time < next) yield return null;
            }

            int reads = v.OverrideHits - over0;
            Debug.Log("[DD-TEST] wp071.hits burst=12@20Hz overrideReads=" + reads + " suppressed=" + v.SuppressedHits);
            Assert.That(reads, Is.InRange(2, 4), "LMG hızında override okumaları seyreltilmeli (stun-lock yok)");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Olum_ItkiDurumuVeSilahSinifi_AileyiSecer()
        {
            yield return Stage();
            ZombieSkinnedVisual e = Zombie(new Vector3(-3f, 0f, 0f), 61u);
            ZombieSkinnedVisual l = Zombie(new Vector3(3f, 0f, 0f), 62u);
            for (int i = 0; i < 10; i++) yield return null;
            e.SetDeathImpulse(new DeathImpulse(Vector3.back, 1.2f, DamageSourceKind.Bullet, WeaponClass.Special, true, BodyRegion.UpperBody, Vector3.zero, 0f, ZombieBodyState.Electrocuted));
            e.PlayDeath(1);
            l.SetDeathImpulse(new DeathImpulse(Vector3.back, 2.5f, DamageSourceKind.Explosion, WeaponClass.Launcher, true, BodyRegion.UpperBody, Vector3.zero, 0f, ZombieBodyState.Standing));
            Transform chest = l.Animator.GetBoneTransform(HumanBodyBones.Chest);
            l.PlayDeath(1);
            Assert.AreEqual(ZombieDeathFamily.ArcElectro, e.DeathFamily, "elektrik → arc_electro");
            Assert.AreEqual(ZombieDeathFamily.LegsOut, l.DeathFamily, "roketatar → legs_out");
            yield return CharacterTests.Bekle(() => l.RagdollActive, 1f, "launcher hand-off");
            float atHandoff = chest.position.y, peak = atHandoff;   // the lift is judged from the hand-off pose (the onset has already folded the body)
            float t0 = Time.time;
            while (Time.time - t0 < 1.0f) { yield return null; peak = Mathf.Max(peak, chest.position.y); }
            Debug.Log("[DD-TEST] wp071.impulse launcherChestRise=" + (peak - atHandoff).ToString("F2", CultureInfo.InvariantCulture) + " m ragdollE=" + e.DiedByRagdoll + " ragdollL=" + l.DiedByRagdoll);
            Assert.IsTrue(l.DiedByRagdoll && e.DiedByRagdoll);
            Assert.Greater(peak - atHandoff, 0.1f, "roketatar gövdeyi kaldırmalı");
            yield return CharacterTests.Bekle(() => e.DeathFinished && l.DeathFinished, 6f, "iki ölüm biter");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Elektrik_PozYavaslar_SpazmCanli_BitinceNormal()
        {
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 63u);
            for (int i = 0; i < 10; i++) yield return null;
            v.SetSpecialVisual(ZombieSpecialVisual.Electrocuted);
            Assert.AreEqual(ZombieSkinnedVisual.ElectroAnimatorSpeed, v.Animator.speed, 1e-4f);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 30; i++) { yield return null; min = Mathf.Min(min, v.LastSpasm); max = Mathf.Max(max, v.LastSpasm); }
            Debug.Log("[DD-TEST] wp071.electro spasmRange=" + (max - min).ToString("F1", CultureInfo.InvariantCulture) + "°");
            Assert.Greater(max - min, 5f, "spazm yok");
            v.SetSpecialVisual(ZombieSpecialVisual.None);
            Assert.AreEqual(1f, v.Animator.speed, 1e-4f);
        }
    }
}
