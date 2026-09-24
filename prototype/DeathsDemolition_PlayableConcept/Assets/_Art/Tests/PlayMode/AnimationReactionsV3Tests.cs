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
using C = DD.Prototype.Characters.CharacterAnimatorContract;

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

        private sealed class MirrorSink : IAttackWindowSink
        {
            public bool SwipeMirror { get; set; }
            public void OnHitWindowStart() { }
            public void OnHitWindowEnd() { }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Saldiri_SwipeLAynalanir_BiteGrabLunge_ItmeVeBastirmaPozu()
        {
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 64u);
            for (int i = 0; i < 10; i++) yield return null;
            var sink = new MirrorSink { SwipeMirror = true };
            v.WindowSink = sink;
            v.PlayAttack(ZombieAttackStyle.Swipe, 0.55f);
            // AP-022 slice 1: swipe_L has its own clip (state 5); the mirror flag stays for sets without it
            Assert.AreEqual(5, v.Animator.GetInteger(CharacterAnimatorContract.AttackStyle), "swipe_L kendi klibinde");
            Assert.IsFalse(v.Animator.GetBool(CharacterAnimatorContract.AttackMirror));
            yield return CharacterTests.Bekle(() => !v.Animator.GetCurrentAnimatorStateInfo(0).IsName(CharacterAnimatorContract.LocomotionState), 1f, "saldırı başladı");
            for (int i = 0; i < 60; i++) yield return null;
            sink.SwipeMirror = false;
            v.PlayAttack(ZombieAttackStyle.BiteGrab, 0.7f);
            Assert.AreEqual(4, v.Animator.GetInteger(CharacterAnimatorContract.AttackStyle), "bite_grab kendi klibinde (AP-022 slice 1)");
            Assert.IsFalse(v.Animator.GetBool(CharacterAnimatorContract.AttackMirror));
            for (int i = 0; i < 90; i++) yield return null;

            // knockback: the read + the lean backwards along the shove (S-03 moves the body; here only the pose)
            int reads0 = v.KnockbackReads;
            Time.captureDeltaTime = 1f / 60f;
            float minPitch = 0f;
            for (int f = 0; f < 20; f++)
            {
                float t = f / 60f;
                var kb = new KnockbackState { Active = t < 0.25f, Direction = -v.transform.forward, Speed = t < 0.25f ? 4.8f * (1f - t / 0.25f) : 0f, Seconds = 0.25f, Progress01 = Mathf.Clamp01(t / 0.25f) };
                v.SetHitResponse(kb, false);
                v.SetSpeed(0f, Time.deltaTime);
                yield return null;
                minPitch = Mathf.Min(minPitch, v.Lean.Pitch);
            }

            // suppression: the continuous tremble
            int tremors0 = v.SuppressionTremors;
            for (int f = 0; f < 30; f++) { v.SetHitResponse(default, true); yield return null; }
            Time.captureDeltaTime = 0f;
            Debug.Log("[DD-TEST] wp071.b5 knockbackReads=" + (v.KnockbackReads - reads0) + " leanPitchMin=" + minPitch.ToString("F1", CultureInfo.InvariantCulture) + "° tremors=" + (v.SuppressionTremors - tremors0));
            Assert.AreEqual(1, v.KnockbackReads - reads0, "itmede tek okuma");
            Assert.Less(minPitch, -2f, "itmede gövde geriye eğilmeli");
            Assert.GreaterOrEqual(v.SuppressionTremors - tremors0, 3, "bastırmada sürekli titreme");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Elektrik_AP022Dongusu_TamHizda_BitinceNormal()
        {
            // WP-071 AP-022 s2a: seized = hit_electro_loop at speed 1 (the clip carries the spasm; the procedural one is the v003 fallback)
            yield return Stage();
            ZombieSkinnedVisual v = Zombie(Vector3.zero, 63u);
            for (int i = 0; i < 10; i++) yield return null;
            v.SetSpecialVisual(ZombieSpecialVisual.Electrocuted);
            Assert.IsTrue(v.ElectroLoopActive, "set AP-022 electro loop taşımalı");
            Assert.AreEqual(1f, v.Animator.speed, 1e-4f);
            yield return CharacterTests.Bekle(() => v.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.ElectroState), 1f, "Electro_Loop durumu");
            Transform head = v.Animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 p0 = head.localEulerAngles; float moved = 0f;
            for (int i = 0; i < 40; i++) { yield return null; moved = Mathf.Max(moved, Quaternion.Angle(Quaternion.Euler(p0), head.localRotation)); }
            Debug.Log("[DD-TEST] wp071.electro loop headSwing=" + moved.ToString("F1", CultureInfo.InvariantCulture) + "° spasmProcedural=" + v.LastSpasm.ToString("F1", CultureInfo.InvariantCulture));
            Assert.Greater(moved, 3f, "döngü baş sarsmalı");
            Assert.IsTrue(v.Animator.GetCurrentAnimatorStateInfo(0).loop, "döngü");
            v.SetSpecialVisual(ZombieSpecialVisual.None);
            Assert.AreEqual(1f, v.Animator.speed, 1e-4f);
            yield return CharacterTests.Bekle(() => v.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), 1f, "bırakınca Locomotion");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Olum_SpecialDurumu_ArcElectroVeGelSlip_KendiKlibi()
        {
            // WP-071 AP-022 s2a: a body seized by Raiju / slipping on Namekuji gel dies on its own clip (Death_8 / Death_9) even
            // with no DeathImpulse (the fix: PlayDeath used to clear the special state before the family read it).
            yield return Stage();
            ZombieSkinnedVisual e = Zombie(new Vector3(-3f, 0f, 0f), 64u);
            ZombieSkinnedVisual g = Zombie(new Vector3(3f, 0f, 0f), 65u);
            for (int i = 0; i < 10; i++) yield return null;
            e.SetSpecialVisual(ZombieSpecialVisual.Electrocuted);
            g.SetSpecialVisual(ZombieSpecialVisual.Slipping);
            for (int i = 0; i < 10; i++) yield return null;
            e.PlayDeath(0);
            g.PlayDeath(0);
            Assert.AreEqual(ZombieDeathFamily.ArcElectro, e.DeathFamily);
            Assert.AreEqual(ZombieDeathFamily.GelSlip, g.DeathFamily);
            yield return CharacterTests.Bekle(() => e.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.DeathState + "_" + C.DeathStyleArcElectro)
                && g.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.DeathState + "_" + C.DeathStyleGelSlip), 1f, "Death_8 / Death_9");
            yield return CharacterTests.Bekle(() => e.RagdollActive && g.RagdollActive, 1.5f, "olay hand-off");
            Debug.Log("[DD-TEST] wp071.specialDeath handoffE=" + e.RagdollHandoffSeconds.ToString("F2", CultureInfo.InvariantCulture) + " handoffG=" + g.RagdollHandoffSeconds.ToString("F2", CultureInfo.InvariantCulture));
            Assert.AreEqual(8f / 30f, e.RagdollHandoffSeconds, 0.08f, "arc_electro ragdoll_handoff@8 (olay; saat yalnız yedek)");
            Assert.AreEqual(9f / 30f, g.RagdollHandoffSeconds, 0.08f, "gel_slip ragdoll_handoff@9 (olay)");
            yield return CharacterTests.Bekle(() => e.DeathFinished && g.DeathFinished, 6f, "iki ölüm biter");
        }
    }
}
