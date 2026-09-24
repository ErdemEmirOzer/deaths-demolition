using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Rounds;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-071 · B1 · death → ragdoll (K4/K5): a killed zombie falls along the killing push (not in place) through a pooled
    /// proxy, settles on the ground and keeps the fallen pose; the pool cap hands the overflow to the authored fall;
    /// a pooled body comes back animated.
    /// </summary>
    [TestFixture]
    [Category("WP071")]
    public sealed class AnimationRagdollTests
    {
        private const float SahneZamanAsimi = 60f;
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
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
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

        private static Transform Hips(ZombieSkinnedVisual v) => v.Animator.GetBoneTransform(HumanBodyBones.Hips);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Olum_ItkiYonundeDuser_Yerlesir_PozKalir_HavuzaDoner()
        {
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 11u);
            for (int i = 0; i < 20; i++) yield return null;
            Vector3 push = Vector3.back;                   // shot from the front: pushed back (the body faces +Z)
            Vector3 hips0 = Hips(v).position;
            v.SetDeathImpulse(push, 2f);
            v.PlayDeath(1);
            float t0 = Time.time;
            yield return CharacterTests.Bekle(() => v.RagdollActive, 1f, "ragdoll hand-off");
            Assert.AreEqual(ZombieDeathFamily.FallBack, v.DeathFamily);
            Assert.IsFalse(v.Animator.enabled, "ragdoll kemikleri sürerken Animator kapalı");
            yield return CharacterTests.Bekle(() => v.DeathFinished, 5f, "settle → DeathFinished");
            Vector3 hips1 = Hips(v).position;
            float along = Vector3.Dot(hips1 - hips0, push);
            Debug.Log("[DD-TEST] wp071.ragdoll handoff=" + v.RagdollHandoffSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s settle=" + (Time.time - t0).ToString("F2", CultureInfo.InvariantCulture)
                      + " s hipsAlongPush=" + along.ToString("F2", CultureInfo.InvariantCulture) + " m hipsY=" + hips1.y.ToString("F2", CultureInfo.InvariantCulture) + " active=" + RagdollProxyPool.ActiveCount);
            Assert.IsTrue(v.DiedByRagdoll);
            Assert.IsFalse(v.RagdollActive, "yerleşince proxy havuza döner");
            Assert.AreEqual(0, RagdollProxyPool.ActiveCount);
            Assert.Greater(along, 0.35f, "gövde itki yönünde düşmeli (yerinde çökme değil)");
            Assert.Less(hips1.y, 0.5f, "kalça yerde");
            Assert.Greater(hips1.y, -0.05f, "zeminden geçmedi");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.Less(Vector3.Distance(Hips(v).position, hips1), 0.01f, "düşmüş poz kalıcı (Animator/LOD ezmez)");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SahneDegisimi_DusenRagdoll_HatasizBirakilir()
        {
            // S-07 (main f52647f): a scene change while a proxy falls destroyed its Rigidbodies before the zombie's OnDisable
            // released it → MissingReferenceException in every later scene-loading test.
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 12u);
            _temp.Remove(v.transform.parent.gameObject);
            Object.DontDestroyOnLoad(v.transform.parent.gameObject);   // like a pooled body that outlives the scene
            for (int i = 0; i < 10; i++) yield return null;
            v.SetDeathImpulse(Vector3.back, 2f);
            v.PlayDeath(1);
            yield return CharacterTests.Bekle(() => v.RagdollActive, 1f, "ragdoll hand-off");
            int exceptions = 0;
            Application.LogCallback count = (msg, stack, type) => { if (type == LogType.Exception) exceptions++; };
            Application.logMessageReceived += count;
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null; yield return null;
            v.transform.parent.gameObject.SetActive(false);   // pool return → OnDisable → EndRagdoll
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            yield return null;
            Application.logMessageReceived -= count;
            Assert.AreEqual(0, exceptions, "sahne değişiminde ragdoll bırakma istisnası");
            Assert.IsFalse(v.RagdollActive);
            Assert.AreEqual(0, RagdollProxyPool.ActiveCount);
            Object.Destroy(v.transform.parent.gameObject);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Tavan_Dolunca_YaziliDusus_HepsiBiter_SonraYenidenDogar()
        {
            yield return Stage();
            RagdollProxyPool.Capacity = 2;
            var zs = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 4; i++) zs.Add(Zombie(new Vector3(i * 2.5f, 0f, 0f), (uint)(30 + i)));
            for (int i = 0; i < 20; i++) yield return null;
            for (int i = 0; i < zs.Count; i++) { zs[i].SetDeathImpulse(Vector3.forward, 1.5f); zs[i].PlayDeath(0); }
            yield return CharacterTests.Bekle(() => zs.TrueForAll(z => z.DeathFinished), 6f, "4 ölüm biter");
            Debug.Log("[DD-TEST] wp071.ragdoll.cap acquired=" + RagdollProxyPool.AcquiredCount + " refused=" + RagdollProxyPool.RefusedCount);
            Assert.AreEqual(2, RagdollProxyPool.AcquiredCount, "tavan 2");
            Assert.AreEqual(2, RagdollProxyPool.RefusedCount, "taşan 2 yazılı düşüşle");
            Assert.AreEqual(2, zs.FindAll(z => z.DiedByRagdoll).Count);

            ZombieSkinnedVisual r = zs[0];
            r.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            yield return null; yield return null;
            Assert.IsTrue(r.Animator.enabled, "yeniden doğan gövdede Animator açık");
            Assert.IsFalse(r.RagdollActive);
            Assert.IsTrue(r.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "Locomotion");
            Assert.Greater(Hips(r).position.y, 0.6f, "ayakta");
        }
    }
}
