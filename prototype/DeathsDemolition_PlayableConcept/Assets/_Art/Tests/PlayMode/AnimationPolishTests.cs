using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-032 · Look-at (threat / aim), foot IK on real ground, stride ↔ speed (measured foot
    /// slip), the telegraph emphasis bridge (DEC-029 window untouched), the crowd-wait state,
    /// the Rise / pickup flags and the IK cost + allocation on a 40-body crowd.
    /// </summary>
    [TestFixture]
    [Category("WP032")]
    public sealed class AnimationPolishTests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();
        private EnemySpawnDirector _director;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            EnemyAttackState.ResetStatics();
            CharacterAnimatorLod.ResetStatics();
            FootIkAdapter.ResetStatics();
            ZombieEnemy.RiseOnSpawn = false;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            AttackSelector.Enabled = true;
            ZombieEnemy.RiseOnSpawn = false;
            CharacterAnimatorLod.ForceFar = -1;
            FootIkAdapter.Force = -1;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            if (_director != null) _director.ForceActiveTarget(0, false);
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

            _director = null;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator MainMenu()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
        }

        private IEnumerator RunAP005()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            yield return MainMenu();
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && EnemySpawnDirector.Instance != null && RunDirector.Instance != null, SahneZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, SahneZamanAsimi, "Fighting");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        private ZombieSkinnedVisual ZombieOnStage(Vector3 at, Quaternion rot, uint seed)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP032_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, rot);
            GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", 0, false);
            // Stage actors are off-camera in the menu scene: CullUpdateTransforms would skip the IK pass and the bone
            // write (that is the production rule); the measurement needs the pass, so animate always here.
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return v;
        }

        // ============================================================ look-at (zombie → threat)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator LookAt_Zombi_OyuncuyaBakar_ArkadaBakmaz_BeklemedeKapanir()
        {
            yield return RunAP005();
            Vector3 p = PlayerRegistry.Primary.transform.position;
            // In view of the gameplay camera (an off-screen body is culled: no IK pass — by design).
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + PlayerRegistry.Primary.transform.forward * 3.5f, Quaternion.LookRotation(-PlayerRegistry.Primary.transform.forward));
            Assert.IsNotNull(z); Assert.IsTrue(z.Skinned.Authored);
            CharacterLookAt look = z.Skinned.LookAt;
            z.Skinned.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Assert.IsNotNull(look, "zombi LookAt");
            yield return CharacterTests.Bekle(() => look.Weight > 0.8f, 3f, "önündeki oyuncuya bakmadı");
            Assert.Greater(Vector3.Distance(look.Point, p), 0.5f);
            Assert.AreEqual(1.5f, look.Point.y - p.y, 0.2f, "bakış noktası oyuncu başı");
            // Waiting (crowd queue) turns the tracking off and the idle to twitch.
            z.Skinned.SetWaiting(true);
            yield return CharacterTests.Bekle(() => look.Weight < 0.05f, 3f, "beklemede bakış sönmedi");
            Assert.IsTrue(z.Skinned.Animator.GetBool(C.Waiting), "bekleme bayrağı (WP-032B: Wait durumu = idle_queue)");
            z.Skinned.SetWaiting(false);
            yield return CharacterTests.Bekle(() => look.Weight > 0.8f, 3f, "bekleme bitince bakış dönmedi");
            // A body facing away from the player (stage actor, no steering): the look stays 0 (angle > 80°).
            ZombieSkinnedVisual away = ZombieOnStage(p + PlayerRegistry.Primary.transform.forward * 3f + PlayerRegistry.Primary.transform.right * 2f, Quaternion.LookRotation(PlayerRegistry.Primary.transform.forward), 9u);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Less(away.LookAt.Weight, 0.05f, "arkadaki oyuncuya baktı");
            Debug.Log("[DD-TEST] wp032.lookat weight=" + look.Weight.ToString("F2", CultureInfo.InvariantCulture) + " active=" + CharacterLookAt.ActiveCount);
        }

        // ============================================================ foot IK on a step

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator FootIk_BasamakUstundeAyakKalkar_PelvisIner_ButceVeLodKapisi()
        {
            yield return MainMenu();
            // Ground + a 12 cm step under the right foot, on the Environment layer (the IK ray mask).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(20f, 0.2f, 20f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(step);
            step.transform.localScale = new Vector3(0.6f, 0.12f, 1.2f);
            step.transform.position = new Vector3(0.25f, 0.06f, 0f);   // right side of the body (+x)
            step.layer = PrototypeLayers.Environment;
            yield return new WaitForFixedUpdate();

            ZombieSkinnedVisual v = ZombieOnStage(Vector3.zero, Quaternion.identity, 3u);
            FootIkAdapter feet = v.FootIk;
            Assert.IsNotNull(feet);
            CharacterAnimatorLod.ForceFar = 0;
            Transform rightFoot = v.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftFoot = v.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform hips = v.Animator.GetBoneTransform(HumanBodyBones.Hips);
            FootIkAdapter.Force = 0;
            for (int i = 0; i < 20; i++) yield return null;
            float rightOff = rightFoot.position.y, leftOff = leftFoot.position.y, hipsOff = hips.position.y;
            FootIkAdapter.Force = -1;
            yield return CharacterTests.Bekle(() => feet.Weight > 0.95f, 3f, "ayak IK ağırlığı yükselmedi");
            for (int i = 0; i < 10; i++) yield return null;
            float rightOn = rightFoot.position.y, leftOn = leftFoot.position.y, hipsOn = hips.position.y;
            Debug.Log("[DD-TEST] wp032.footik right " + rightOff.ToString("F3", CultureInfo.InvariantCulture) + "→" + rightOn.ToString("F3", CultureInfo.InvariantCulture)
                      + " left " + leftOff.ToString("F3", CultureInfo.InvariantCulture) + "→" + leftOn.ToString("F3", CultureInfo.InvariantCulture)
                      + " hips " + hipsOff.ToString("F3", CultureInfo.InvariantCulture) + "→" + hipsOn.ToString("F3", CultureInfo.InvariantCulture)
                      + " lifts L/R=" + feet.LeftLift.ToString("F3", CultureInfo.InvariantCulture) + "/" + feet.RightLift.ToString("F3", CultureInfo.InvariantCulture)
                      + " pelvis=" + feet.PelvisOffset.ToString("F3", CultureInfo.InvariantCulture) + " served=" + FootIkAdapter.ServedThisFrame + " rays=" + FootIkAdapter.RaycastsThisFrame);
            float lifted = Mathf.Max(rightOn - rightOff, leftOn - leftOff), stayed = Mathf.Min(Mathf.Abs(rightOn - rightOff), Mathf.Abs(leftOn - leftOff));
            Assert.Greater(lifted, 0.06f, "basamak üstündeki ayak kalkmadı");
            Assert.Less(stayed, 0.04f, "yerdeki ayak yerinde kalmalı");
            Assert.AreEqual(2, FootIkAdapter.RaycastsThisFrame, "gövde başına 2 ışın");
            // Far body → the adapter fades out (LOD gate).
            CharacterAnimatorLod.ForceFar = 1;
            yield return CharacterTests.Bekle(() => feet.Weight < 0.05f, 3f, "uzak gövdede IK kapanmadı");
            CharacterAnimatorLod.ForceFar = 0;
        }

        // ============================================================ stride ↔ speed (measured slip)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Stride_YuruyenZombi_AyakKaymasi_2cmAlti()
        {
            yield return MainMenu();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(60f, 0.2f, 60f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
            ZombieSkinnedVisual v = ZombieOnStage(new Vector3(-15f, 0f, 0f), Quaternion.LookRotation(Vector3.right), 5u);
            FootIkAdapter.Force = 0;   // measure the clip's own contact, not the IK's
            Transform root = v.transform.parent;
            Transform lf = v.Animator.GetBoneTransform(HumanBodyBones.LeftFoot), rf = v.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            const float speed = 2.7f;   // Standard tier speed over shamble_A → rate ≈ 1.74 with the Unity calibration (inside the band)
            Time.captureDeltaTime = 1f / 60f;   // fixed step: the measurement must not depend on the machine's frame jitter
            for (int i = 0; i < 30; i++) { v.SetSpeed(speed, Time.deltaTime); yield return null; }

            // Stance = the contact phase (ankle at its ground height; the AP-009 ankle sits at 0.08–0.10 m in
            // Blender, ~0.11–0.13 m on the Unity rig). The shuffle moves the planted foot back in a burst, not
            // uniformly, so the honest measure is the NET world displacement of the foot over one contact
            // phase — zero when the mean stance speed matches the body speed (the manifest speed_mps).
            // Stance = from the clip's own contact event (foot_l / foot_r, AP-009 manifest) to the other foot's
            // contact: the authored definition of "planted", independent of ankle heights. Skating = how far
            // the planted foot travelled in the world over that window along the travel axis.
            var slips = new List<float>();
            var slipsPerFoot = new[] { new List<float>(), new List<float>() };
            Transform[] feet = { lf, rf };
            // At every contact event both feet are sampled; at the next contact the foot that travelled least in the
            // world over the window is the planted one (its travel = skating). The manifest names the landing foot
            // (foot_r at frame 0) but the Unity rig lands the LEFT foot at frame 0 — noted for P-06 (marker naming);
            // the measurement does not depend on the name.
            float[] sampleX = { float.NaN, float.NaN };
            float sampleTime = -1f;
            int[] landedFoot = { 0 };
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e != CharacterAnimationEvent.FootLeft && e != CharacterAnimationEvent.FootRight) return;
                if (!float.IsNaN(sampleX[0]) && Time.time - sampleTime > 0.05f)
                {
                    // signed along the travel axis: + = the planted foot is dragged with the body (clip too slow), − = it moves back (too fast)
                    float sl = feet[0].position.x - sampleX[0], sr = feet[1].position.x - sampleX[1];
                    int planted = Mathf.Abs(sl) <= Mathf.Abs(sr) ? 0 : 1;
                    slips.Add(planted == 0 ? sl : sr);
                    slipsPerFoot[planted].Add(Mathf.Abs(planted == 0 ? sl : sr));
                }

                sampleX[0] = feet[0].position.x;
                sampleX[1] = feet[1].position.x;
                sampleTime = Time.time;
            };
            v.Events.Raised += onEvent;
            float t0 = Time.time;
            try
            {
                while (Time.time - t0 < 5f)
                {
                    root.position += Vector3.right * speed * Time.deltaTime;
                    v.SetSpeed(speed, Time.deltaTime);
                    yield return null;
                }
            }
            finally
            {
                v.Events.Raised -= onEvent;
            }

            Time.captureDeltaTime = 0f;
            // WP-032B: the v002 events sit on the landing frame (shamble_A 0 / 7 instead of v001's 0 / 8), which splits the
            // shuffle's authored burst unevenly between the two windows (1.96 vs 1.19 m/s at rate 1); per-window drift
            // cancels over one stride cycle, a rate mismatch does not — the gate is the slip per step over a full cycle.
            // (The v002 shuffle drags the planted foot forward in one window and back in the other — authored, byte-identical to
            // v001 — so the per-window numbers are signed and the cycle mean is the rate error; |window| stays in the log.)
            var perCycle = new List<float>();
            for (int i = 0; i + 1 < slips.Count; i += 2) perCycle.Add(Mathf.Abs(0.5f * (slips[i] + slips[i + 1])));
            for (int i = 0; i < slips.Count; i++) slips[i] = Mathf.Abs(slips[i]);
            slips.Sort();
            perCycle.Sort();
            float median = perCycle.Count > 0 ? perCycle[perCycle.Count / 2] : 0f;
            float[] footMedian = new float[2];
            for (int i = 0; i < 2; i++) { slipsPerFoot[i].Sort(); footMedian[i] = slipsPerFoot[i].Count > 0 ? slipsPerFoot[i][slipsPerFoot[i].Count / 2] : 0f; }
            float planted = median;
            Assert.GreaterOrEqual(slips.Count, 6, "yeterli adım ölçülmedi");
            float sum = 0f, max = 0f;
            foreach (float s in slips) { sum += s; max = Mathf.Max(max, s); }
            float mean = sum / slips.Count;
            Debug.Log("[DD-TEST] wp032.slip steps=" + slips.Count + " cycleMedian=" + (median * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm mean=" + (mean * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm max=" + (max * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm rate=" + v.Animator.GetFloat(C.GaitRate).ToString("F2", CultureInfo.InvariantCulture));
            Debug.Log("[DD-TEST] wp032.slip.feet plantedL=" + slipsPerFoot[0].Count + " (" + (footMedian[0] * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm) plantedR=" + slipsPerFoot[1].Count + " (" + (footMedian[1] * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm)");
            // WP-032B: with the v002 landing-frame events the shuffle's dragged foot reads 3–4.5 cm per window in BOTH windows
            // (v001's 0/8 split put one foot at 0.3 cm and hid it) — the drag is authored (byte-identical keys); the band
            // for the shamble is 5 cm/step, the run cycles keep 2 cm on their contact frames (AnimationV002Tests).
            // WP-071 K3 (N5): the Standard's 2.7 m/s is past the shamble's cadence band — the clip plays at ≤ ×1.2 and the body
            // slides the rest by design (≈ 30–43 % until AP-022's shamble_fast); the gate is the band, the slip is reported.
            Assert.LessOrEqual(v.Animator.GetFloat(C.GaitRate), CadenceBand.Max + 1e-3f, "shamble kadans bandını aştı");
            Assert.GreaterOrEqual(v.GaitRate, CadenceBand.Min - 1e-3f);
        }


        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Yonelim_AP009Govdesi_ModelIleAyniYone_SagKolSwipe()
        {
            // WP-018 shipped the AP-009 clips 180° off the model (measured −0.96): the crowd walked and struck
            // facing away from its own transform. ArtAnimationClipContract.RootRotationOffsetDegrees fixes it.
            yield return MainMenu();
            ZombieSkinnedVisual v = ZombieOnStage(Vector3.zero, Quaternion.identity, 5u);
            FootIkAdapter.Force = 0;
            Animator an = v.Animator;
            an.SetFloat(C.Speed01, 1f); an.SetFloat(C.GaitRate, 1f); an.SetFloat(C.Gait, 0f);
            for (int i = 0; i < 20; i++) yield return null;
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot);
            float bodyDot = Vector3.Dot(an.bodyRotation * Vector3.forward, v.transform.forward);
            float toes = v.transform.InverseTransformPoint(an.GetBoneTransform(HumanBodyBones.LeftToes).position).z - v.transform.InverseTransformPoint(lf.position).z;
            // WP-071 AP-022 s1: the swipe is AP022 swipe_R now — a right-to-left sweep whose right hand ends across the body, so a
            // single pose no longer proves the arm. Not mirrored = the right hand travels further than the left from wind-up to the
            // window's end; swipe_L (same authoring, mirror off) is the control and must read the other way.
            an.enabled = false;
            float swipeR = HandTravel(v, an, C.AttackSwipeState, out float swipeRLeft);
            float swipeL = HandTravel(v, an, C.AttackSwipeLState, out float swipeLLeft);
            an.enabled = true;
            Debug.Log("[DD-TEST] wp032.orient bodyFwd·rootFwd=" + bodyDot.ToString("F2", CultureInfo.InvariantCulture) + " toesAhead=" + toes.ToString("F3", CultureInfo.InvariantCulture) + " swipeR travel R/L=" + swipeR.ToString("F2", CultureInfo.InvariantCulture) + "/" + swipeRLeft.ToString("F2", CultureInfo.InvariantCulture) + " swipeL travel R/L=" + swipeL.ToString("F2", CultureInfo.InvariantCulture) + "/" + swipeLLeft.ToString("F2", CultureInfo.InvariantCulture));
            Assert.Greater(bodyDot, 0.9f, "klip gövdesi modelin ileri yönüne bakmalı (180° kaydırma)");
            Assert.Greater(toes, 0.05f, "ayak parmakları topuğun önünde");
            Assert.Greater(swipeR, swipeRLeft * 1.3f, "swipe sağ kolla (ayna yok)");
            Assert.Greater(swipeLLeft, swipeL * 1.3f, "swipe_L sol kolla (kontrol)");
            yield return null;
        }

        static float HandTravel(ZombieSkinnedVisual v, Animator an, string state, out float leftTravel)
        {
            // Model-local hand path over wind-up → hit_window_end (frames 0..19 of 36), 12 samples.
            Transform lh = an.GetBoneTransform(HumanBodyBones.LeftHand), rh = an.GetBoneTransform(HumanBodyBones.RightHand);
            float right = 0f; leftTravel = 0f;
            Vector3 pr = default, pl = default;
            for (int i = 0; i <= 12; i++)
            {
                an.Play(state, 0, i / 12f * (19f / 36f)); an.Update(0f);
                Vector3 r = v.transform.InverseTransformPoint(rh.position), l = v.transform.InverseTransformPoint(lh.position);
                if (i > 0) { right += Vector3.Distance(r, pr); leftTravel += Vector3.Distance(l, pl); }
                pr = r; pl = l;
            }

            return right;
        }





        // ============================================================ jaw (AP-022 zombie rig v002)

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Cene_RigV002_BosktaAz_IsirmadaAcilir_CeneAsagiIner()
        {
            // WP-071 K10: the zombie's only rig v002 bone. Idle a few degrees, bite_grab wide, the chin goes DOWN in head space.
            yield return MainMenu();
            CharacterAnimatorLod.ForceFar = 0;
            ZombieSkinnedVisual v = ZombieOnStage(Vector3.zero, Quaternion.identity, 7u);
            Assert.IsNotNull(v.Jaw, "rig v002 jaw kemiği yok");
            for (int i = 0; i < 40; i++) yield return null;
            float idle = v.JawDegrees;
            Transform head = v.Animator.GetBoneTransform(HumanBodyBones.Head), jaw = v.Jaw;
            Vector3 chinJaw = jaw.InverseTransformPoint(jaw.position + v.transform.forward * 0.1f);
            Vector3 chinIdle = head.InverseTransformPoint(jaw.TransformPoint(chinJaw));
            Vector3 downHead = head.InverseTransformDirection(Vector3.down).normalized;
            v.PlayAttack(ZombieAttackStyle.BiteGrab, 0.55f);
            float peak = 0f; Vector3 chinPeak = chinIdle;
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                yield return null;
                if (v.JawDegrees > peak) { peak = v.JawDegrees; chinPeak = head.InverseTransformPoint(jaw.TransformPoint(chinJaw)); }
            }

            float drop = Vector3.Dot(chinPeak - chinIdle, downHead) * head.lossyScale.y;
            Debug.Log("[DD-TEST] wp071.jaw idle=" + idle.ToString("F1", CultureInfo.InvariantCulture) + "° bitePeak=" + peak.ToString("F1", CultureInfo.InvariantCulture) + "° chinDrop=" + (drop * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm");
            Assert.That(idle, Is.InRange(0.5f, ZombieSkinnedVisual.JawIdle + ZombieSkinnedVisual.JawBreath + 0.1f), "boşta az açık");
            Assert.Greater(peak, ZombieSkinnedVisual.JawBite - 2f, "ısırmada geniş açık");
            Assert.Greater(drop, 0.01f, "çene baş uzayında aşağı iner (eksen işareti)");
        }

        // ============================================================ telegraph bridge (DEC-029 untouched)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Telegraf_AdditiveVurgu_WindupIcinde_PencereOlaylariAynen()
        {
            AttackSelector.Enabled = false;   // WP-076 B1: the selector's new styles get their emphasis in WP-071 B5; this test reads the signature swipe
            yield return RunAP005();
            Transform player = PlayerRegistry.Primary.transform;
            Vector3 p = player.position;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, p + player.forward * 1.3f, Quaternion.LookRotation(-player.forward));
            Assert.IsNotNull(z); Assert.IsTrue(z.Skinned.Authored);
            Animator an = z.Skinned.Animator;
            int tele = z.Skinned.TelegraphLayerIndex;
            Assert.GreaterOrEqual(tele, 0, "telegraph katmanı");
            Assert.AreEqual(1f, z.Skinned.TelegraphEmphasis, 1e-3f, "Standard vurgu 1 (telegraf ölçeği 1)");
            Assert.AreEqual(1f, an.GetLayerWeight(tele), 1e-3f);
            int windupWithTelegraph = 0, windupFrames = 0, activeOutside = 0, swings = 0;
            ZombieAttackPhase prev = ZombieAttackPhase.None;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 8f && swings < 2)
            {
                ZombieAttackPhase phase = z.AttackPhase;
                if (phase == ZombieAttackPhase.Windup)
                {
                    windupFrames++;
                    AnimatorStateInfo s = an.GetCurrentAnimatorStateInfo(tele);
                    if (s.IsName(C.TelegraphState + "_0") || an.GetNextAnimatorStateInfo(tele).IsName(C.TelegraphState + "_0")) windupWithTelegraph++;
                }

                if (phase == ZombieAttackPhase.Active && !z.Skinned.HitWindowOpen) activeOutside++;
                if (prev == ZombieAttackPhase.Active && phase != ZombieAttackPhase.Active) swings++;
                prev = phase;
                yield return null;
            }

            Debug.Log("[DD-TEST] wp032.telegraph swings=" + swings + " windupFrames=" + windupFrames + " withEmphasis=" + windupWithTelegraph + " activeOutside=" + activeOutside + " fallback=" + EnemyAttackState.WindowFallbackCount);
            Assert.GreaterOrEqual(swings, 1);
            Assert.Greater(windupWithTelegraph, windupFrames / 2, "wind-up karelerinin çoğunda vurgu klibi oynamalı");
            Assert.AreEqual(0, activeOutside, "vurgu köprüsü pencereyi kaydırdı");
            Assert.AreEqual(0, EnemyAttackState.WindowFallbackCount);
        }

        // ============================================================ rise / pickup flags

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Bayraklar_DEC082_PickupMotoruKilitlemez_UstGovde_RiseVarsayilan()
        {
            yield return RunAP005();
            PlayerRig rig = PlayerRegistry.Primary;
            var pv = rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv); Assert.IsTrue(pv.Authored);
            Animator an = pv.Animator;
            int over = ArtAnimatorLayers.Index(an, C.OverrideUpperLayer);
            // WP-032B · DEC-082 (user, 2026-09-17): the Special Weapon grab never locks the motor — the clip is an
            // upper-body beat over the legs; the base layer stays in Locomotion for the whole grab.
            WeaponEvents.PublishSpecialPickedUp(10f);
            yield return null; yield return null;
            Assert.IsTrue(rig.Motor.ControlEnabled, "DEC-082: pickup motoru kilitlemez");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(over).IsName(C.PickupState) || an.GetNextAnimatorStateInfo(over).IsName(C.PickupState), "pickup üst gövde katmanında değil");
            float t0 = Time.time;
            while (Time.time - t0 < 0.5f)
            {
                Assert.IsTrue(rig.Motor.ControlEnabled, "pickup sırasında motor kapandı");
                Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "pickup taban katmanını kesti");
                yield return null;
            }

            Assert.IsFalse(ZombieEnemy.RiseOnSpawn, "RiseOnSpawn (Always) varsayılan kapalı — DEC-081 melez politika S-03/WP-040");
        }

        // ============================================================ crowd: IK cost + GC

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kalabalik40_IkAcik_GcSifir_Butce12Govde_Sure()
        {
            yield return MainMenu();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(60f, 0.2f, 60f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
            var target = new GameObject("WP032_Threat");
            _temp.Add(target);
            target.transform.position = new Vector3(6f, 0f, 20f);
            EnemyTargeting.SetPlayer(target.transform);
            var visuals = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 40; i++) visuals.Add(ZombieOnStage(new Vector3(i % 8 * 1.5f, 0f, i / 8 * 1.5f), Quaternion.identity, (uint)(i + 1)));
            CharacterAnimatorLod.ForceFar = 0;
            for (int f = 0; f < 90; f++) { foreach (var v in visuals) v.SetSpeed(2.7f * ((f / 30 + visuals.IndexOf(v)) % 2), Time.deltaTime); yield return null; }
            Assert.AreEqual(FootIkAdapter.MaxBodiesPerFrame, FootIkAdapter.ServedThisFrame, "kare başına 12 gövde bütçesi");
            Assert.AreEqual(FootIkAdapter.MaxBodiesPerFrame * 2, FootIkAdapter.RaycastsThisFrame);
            Assert.GreaterOrEqual(CharacterLookAt.ActiveCount, 30, "tehdide bakan gövdeler");

            int savedRate = Application.targetFrameRate;
            Application.targetFrameRate = -1;   // WP-052 note: the Editor runner keeps the recorder's LastValue at 0 when capped at 60
            yield return null; yield return null;
            GC.Collect();
            yield return null; yield return null;
            long monoBefore = Profiler.GetMonoUsedSizeLong();
            int gen0Before = GC.CollectionCount(0);
            int frames = 0;
            // WP-032D (WP-053 bisect): count the managed allocations per frame with the profiler recorders — the mono-heap
            // delta and gen0 count missed a 40 B/body/frame params-array in the foot IK (Mathf.Min(a, b, c)).
            long allocCount = 0, allocBytes = 0;
            long[] phaseCount = new long[3];   // 0: locomotion with the foot IK, 1: locomotion with the foot IK forced off, 2: + attacks (IK on again)
            using var recCount = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count");
            using var recBytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            for (int f = 0; f < 240; f++)
            {
                if (f >= 60) { allocCount += recCount.LastValue; allocBytes += recBytes.LastValue; }
                // LastValue here is the frame just completed (f − 1), which ran under the Force set on the previous pass: attribute first, then set.
                // IK on / off in alternating 10-frame windows for frames 60..179 (the scene baseline drifts in bursts), attacks after.
                if (f > 60 && f <= 180) { if (FootIkAdapter.Force == -1) phaseCount[0] += recCount.LastValue; else phaseCount[1] += recCount.LastValue; }
                else if (f > 180) phaseCount[2] += recCount.LastValue;
                if (f >= 60 && f < 180) { bool ikOn = ((f - 60) / 10) % 2 == 0; FootIkAdapter.Force = ikOn ? -1 : 0; }
                else FootIkAdapter.Force = -1;
                for (int i = 0; i < visuals.Count; i++)
                {
                    ZombieSkinnedVisual v = visuals[i];
                    v.SetSpeed(2.7f * ((f / 30 + i) % 2), Time.deltaTime);
                    if (f >= 180 && (f + i) % 120 == 0) v.PlayAttack(ZombieAttackStyle.Swipe, 0.55f);
                }

                yield return null;
                frames++;
            }

            long delta = Profiler.GetMonoUsedSizeLong() - monoBefore;
            int gen0 = GC.CollectionCount(0) - gen0Before;
            Debug.Log("[DD-TEST] wp032.crowd frames=" + frames + " monoDelta=" + delta + " gen0=" + gen0 + " allocPerFrame=" + (allocCount / 180f).ToString("F2", CultureInfo.InvariantCulture) + " bytesPerFrame=" + (allocBytes / 180f).ToString("F0", CultureInfo.InvariantCulture)
                      + " served=" + FootIkAdapter.ServedThisFrame + " rays=" + FootIkAdapter.RaycastsThisFrame + " lookActive=" + CharacterLookAt.ActiveCount);
            FootIkAdapter.Force = -1;
            Application.targetFrameRate = savedRate;
            Debug.Log("[DD-TEST] wp032.crowd.phases locoIk=" + (phaseCount[0] / 60f).ToString("F2", CultureInfo.InvariantCulture) + " locoNoIk=" + (phaseCount[1] / 60f).ToString("F2", CultureInfo.InvariantCulture) + " attacksIk=" + (phaseCount[2] / 60f).ToString("F2", CultureInfo.InvariantCulture) + " allocs/frame");
            // WP-032F: with the recorder actually reading (targetFrameRate −1) the MainMenu scene under the stage costs ~4 allocations a
            // frame on its own; the gate is the foot-IK path's own delta (IK on − IK off).
            Assert.AreEqual(0, gen0, "IK yolu gen0 tetikledi");
            Assert.LessOrEqual(delta, 4096, "IK/look-at yolu kare başına ayırıyor: " + delta);
            // WP-071 B2: the interleaved windows read ±1.2 a frame between runs (scene noise); a real per-body allocation in the
            // served path adds ≥ 12 a frame (12 bodies served) — the gate sits between the two.
            Assert.LessOrEqual((phaseCount[0] - phaseCount[1]) / 60f, 3f, "ayak IK yolu kare başına ayırıyor (IK açık − kapalı): " + ((phaseCount[0] - phaseCount[1]) / 60f));

            // WP-071 B3 (C-00): positive control — the same interleaved windows with ONE deliberate allocation per served body must read
            // ≥ 12 a frame over the IK-off baseline, i.e. the ≤ 3 gate above catches a per-body leak.
            Application.targetFrameRate = -1;
            long probeOn = 0, probeOff = 0;
            for (int f = 0; f < 121; f++)
            {
                if (f > 0) { if (FootIkAdapter.ProbeAllocPerServe) probeOn += recCount.LastValue; else probeOff += recCount.LastValue; }
                FootIkAdapter.ProbeAllocPerServe = f < 120 && (f / 10) % 2 == 0;
                for (int i = 0; i < visuals.Count; i++) visuals[i].SetSpeed(2.7f * ((f / 30 + i) % 2), Time.deltaTime);
                yield return null;
            }

            FootIkAdapter.ProbeAllocPerServe = false;
            Application.targetFrameRate = savedRate;
            float probeDelta = (probeOn - probeOff) / 60f;
            Debug.Log("[DD-TEST] wp071.ikgate.positive perFrame=" + probeDelta.ToString("F2", CultureInfo.InvariantCulture) + " (served " + FootIkAdapter.MaxBodiesPerFrame + ")");
            Assert.GreaterOrEqual(probeDelta, FootIkAdapter.MaxBodiesPerFrame - 1f, "pozitif kontrol: gövde başı ayırma görülmedi");
            Assert.Greater(probeDelta, 3f, "kapı (≤ 3) gövde başı ayırmayı yakalamalı");
            EnemyTargeting.Clear();
        }
    }
}
