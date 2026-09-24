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
            // AP-022 slice 1: fall_back names its hand-off at frame 8 (0.27 s) — the clip event drives it, not the clock
            Assert.That(v.RagdollHandoffSeconds, Is.InRange(0.2f, 0.42f), "hand-off klip olayında");
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
            Assert.IsFalse(v.RagdollActive);
            Assert.AreEqual(0, RagdollProxyPool.ActiveCount);
            // review R1-A #3: the next run draws the same pooled body — it spawns clean and dies into a ragdoll again
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            v.transform.parent.gameObject.SetActive(true);
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(v.Animator.enabled, "yeniden doğan gövde animasyonlu");
            v.SetDeathImpulse(Vector3.forward, 1.5f);
            v.PlayDeath(0);
            yield return CharacterTests.Bekle(() => v.RagdollActive, 1f, "ikinci koşuda ragdoll");
            yield return CharacterTests.Bekle(() => v.DeathFinished, 5f, "ikinci ölüm biter");
            Application.logMessageReceived -= count;
            Assert.AreEqual(0, exceptions, "sahne değişiminde / sonraki koşuda ragdoll istisnası");
            Assert.AreEqual(0, RagdollProxyPool.ActiveCount);
            Object.Destroy(v.transform.parent.gameObject);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Havuz_200Dongu_SizintiYok()
        {
            // WP-078 R3 (S-10): 3 × 200 acquire → release cycles leave nothing behind (acquired = released, 0 active); the allocation
            // per cycle is measured with the WP-044 referee method (cycles in ONE frame vs a control frame + 64 KB positive control) and logged
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 13u);
            for (int i = 0; i < 5; i++) yield return null;
            Animator an = v.Animator;
            HumanBodyBones[] tipBones =
            {
                HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
            };
            var bones = new Transform[RagdollProxyPool.BodyCount];
            var tips = new Transform[RagdollProxyPool.BodyCount];
            var vel = new Vector3[RagdollProxyPool.BodyCount];
            for (int i = 0; i < bones.Length; i++) { bones[i] = an.GetBoneTransform(RagdollProxyPool.Bones[i]); tips[i] = an.GetBoneTransform(tipBones[i]); }
            RagdollProxyPool.Capacity = 2;
            RagdollProxyPool.Release(RagdollProxyPool.Acquire(1, bones, tips, vel, Vector3.back, 0f));   // warm
            int baseAcq = RagdollProxyPool.AcquiredCount, baseRel = RagdollProxyPool.ReleasedCount;
            int savedRate = Application.targetFrameRate;
            Application.targetFrameRate = -1;   // WP-052: the recorder reads 0 while capped at 60 in the Editor runner
            using var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count");
            using var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null; yield return null;
            long controlMax = 0, cycleMin = long.MaxValue;
            for (int pass = 0; pass < 3; pass++)
            {
                int sink = 0;
                for (int k = 0; k < 200; k++) sink += bones[0] != null ? 1 : 0;
                yield return null;
                controlMax = System.Math.Max(controlMax, rec.LastValue);
                for (int k = 0; k < 200; k++) RagdollProxyPool.Release(RagdollProxyPool.Acquire(2, bones, tips, vel, Vector3.back, 0f));
                yield return null;
                cycleMin = System.Math.Min(cycleMin, rec.LastValue);
                if (sink < 0) Debug.Log(sink);
            }

            var keep = new object[64];
            for (int i = 0; i < keep.Length; i++) keep[i] = new byte[1024];
            yield return null;
            long posCount = rec.LastValue, posBytes = bytes.LastValue;
            keep = null;
            Application.targetFrameRate = savedRate;
            int acquired = RagdollProxyPool.AcquiredCount - baseAcq, released = RagdollProxyPool.ReleasedCount - baseRel;
            Debug.Log("[DD-TEST] wp071.ragdoll.cycles acquired=" + acquired + " released=" + released + " active=" + RagdollProxyPool.ActiveCount + " cycleFrameMin=" + cycleMin + " controlMax=" + controlMax + " positive=" + posCount + "/" + posBytes + " B");
            Assert.AreEqual(600, acquired);
            Assert.AreEqual(acquired, released, "sızıntı: alınan ≠ bırakılan");
            Assert.AreEqual(0, RagdollProxyPool.ActiveCount);
            // REPORTED, not gated (HIZLI HAT §7): an acquire → release cycle is a death event, not a frame path; the Editor reading swings
            // with background load (control 6 … 3 267 between runs). Clean run: 0.0 per cycle (the round-log re-arm that cost ≈ 4 moved to spawn).
            Debug.Log("[DD-TEST] wp071.ragdoll.cycleAllocs perCycle≈" + ((cycleMin - controlMax) / 200f).ToString("F1", CultureInfo.InvariantCulture) + " (positive control " + (posCount >= 64 ? "seen" : "NOT seen") + ")");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Tavan_Dolunca_YaziliDusus_HepsiBiter_SonraYenidenDogar()
        {
            yield return Stage();
            Assert.AreEqual(RagdollProxyPool.CapOf(QualityTiers.Current), RagdollProxyPool.Capacity, "tavan kademeden (2/6/10)");
            Assert.AreEqual(2, RagdollProxyPool.CapOf(QualityTier.Performans)); Assert.AreEqual(6, RagdollProxyPool.CapOf(QualityTier.Yuksek)); Assert.AreEqual(10, RagdollProxyPool.CapOf(QualityTier.Sinematik));
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
