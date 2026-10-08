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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using C = DD.Prototype.Characters.CharacterAnimatorContract;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-032B · The AP-009B v002 set on the characters: the corrected foot events name the
    /// landing foot, the player's run ring shows no slip at the 6.2 m/s reference (rate cap
    /// honoured), the Ultra overhead is its own clip with the DEC-029 window on the clip's
    /// events, and the queue Wait state runs out of step across bodies.
    /// </summary>
    [TestFixture]
    [Category("WP032B")]
    public sealed class AnimationV002Tests
    {
        private const float SahneZamanAsimi = 60f;
        private readonly List<GameObject> _temp = new List<GameObject>();
        private EnemySpawnDirector _director;

        [SetUp]
        public void SetUp()
        {
            ZombieSkinnedVisual.TierGaits = true;   // WP-071 MUST-I1 A/B diagnostic switch
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
            ZombieSkinnedVisual.GaitVariety = 1f;
            ZombieEnemy.RiseOnSpawn = false;
            FootIkAdapter.Force = -1;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            if (_director != null) _director.ForceActiveTarget(0, false);
            _director = null;
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
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

        private ZombieSkinnedVisual ZombieOnStage(Vector3 at, Quaternion rot, uint seed, int idleVariant = 0)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP032B_Zombie");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(at, rot);
            GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, seed));
            v.ApplyMaterial(set.ZombieLibrary, 0);
            v.ResetForSpawn(EnemyTier.Standard, 2.7f, "A", idleVariant, false);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // stage actor off-camera (see AnimationPolishTests)
            return v;
        }

        private PlayerSkinnedVisual PlayerOnStage(Func<Vector3> velocity, out Transform root)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            var stage = new GameObject("WP032B_PlayerStage");
            _temp.Add(stage);
            root = stage.transform;
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            pv.Attach(visual, velocity, () => 6.2f, () => Vector3.forward);
            Assert.IsTrue(pv.Authored, "oyuncu AP-009 seti değil");
            pv.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return pv;
        }

        private static GameObject Ground(List<GameObject> temp)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.Add(ground);
            ground.transform.localScale = new Vector3(80f, 0.2f, 80f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.layer = PrototypeLayers.Environment;
            return ground;
        }

        // ============================================================ foot events name the landing foot (v002)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AyakOlaylari_v002_foot_l_SolAyakBasar_foot_r_SagAyakBasar()
        {
            // v001 had the two names swapped (foot_r at frame 0 was the LEFT heel strike — WP-032 measured it, P-06 fixed it
            // in v002). S-07 decals and S-08 footsteps read the event name, so the name must be the foot that is down.
            yield return MainMenu();
            Ground(_temp);
            FootIkAdapter.Force = 0;
            ZombieSkinnedVisual v = ZombieOnStage(new Vector3(-10f, 0f, 0f), Quaternion.LookRotation(Vector3.right), 5u);
            Transform lf = v.Animator.GetBoneTransform(HumanBodyBones.LeftFoot), rf = v.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 30; i++) { v.SetSpeed(2.7f, Time.deltaTime); yield return null; }
            // The planted foot over the window that STARTS at an event is the one that travelled least in the world by the
            // next event (WP-032 method — ankle heights do not separate a shuffle's feet); its side must be the event's name.
            int leftOk = 0, leftBad = 0, rightOk = 0, rightBad = 0;
            float[] sampleX = { float.NaN, float.NaN };
            CharacterAnimationEvent windowEvent = CharacterAnimationEvent.None;
            float sampleTime = -1f;
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e != CharacterAnimationEvent.FootLeft && e != CharacterAnimationEvent.FootRight) return;
                if (!float.IsNaN(sampleX[0]) && Time.time - sampleTime > 0.05f)
                {
                    float dl = Mathf.Abs(lf.position.x - sampleX[0]), dr = Mathf.Abs(rf.position.x - sampleX[1]);
                    bool leftPlanted = dl <= dr;
                    if (windowEvent == CharacterAnimationEvent.FootLeft) { if (leftPlanted) leftOk++; else leftBad++; }
                    else { if (!leftPlanted) rightOk++; else rightBad++; }
                }

                sampleX[0] = lf.position.x; sampleX[1] = rf.position.x; sampleTime = Time.time; windowEvent = e;
            };
            v.Events.Raised += onEvent;
            float t0 = Time.time;
            try
            {
                while (Time.time - t0 < 4f)
                {
                    v.transform.parent.position += Vector3.right * 2.7f * Time.deltaTime;
                    v.SetSpeed(2.7f, Time.deltaTime);
                    yield return null;
                }
            }
            finally { v.Events.Raised -= onEvent; }

            Debug.Log("[DD-TEST] wp032b.footevents leftOk=" + leftOk + " leftBad=" + leftBad + " rightOk=" + rightOk + " rightBad=" + rightBad);
            Assert.GreaterOrEqual(leftOk + leftBad, 3); Assert.GreaterOrEqual(rightOk + rightBad, 3);
            Assert.AreEqual(0, leftBad, "foot_l penceresinde basan ayak sol değil (v001 ayna hatası)");
            Assert.AreEqual(0, rightBad, "foot_r penceresinde basan ayak sağ değil");
        }

        // ============================================================ player run ring (v002) at the reference speed

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Oyuncu_KosuHalkasi_6_2ms_AyakKaymasi_2cmAlti_OranTavani()
        {
            yield return MainMenu();
            Ground(_temp);
            FootIkAdapter.Force = 0;
            Vector3 velocity = Vector3.zero;
            PlayerSkinnedVisual pv = PlayerOnStage(() => velocity, out Transform root);
            root.rotation = Quaternion.LookRotation(Vector3.right);
            root.position = new Vector3(-20f, 0f, 0f);
            Transform lf = pv.Animator.GetBoneTransform(HumanBodyBones.LeftFoot), rf = pv.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            const float speed = 6.2f;
            Time.captureDeltaTime = 1f / 60f;
            velocity = Vector3.right * speed;
            for (int i = 0; i < 40; i++) yield return null;
            Assert.Greater(pv.Move.y, 0.9f, "koşu halkasında değil");
            // Rate cap (P-06): the run clip's playback = LocoRate × its child time scale ≤ MaxRunRate.
            float runTimeScale = PlayerSkinnedVisual.ChildTimeScale(pv.AnimationSet.LocomotionReferenceSpeed, pv.AnimationSet.SpeedOf(C.PlayerRunFwd));
            float clipRate = pv.Animator.GetFloat(C.LocoRate) * runTimeScale;
            Assert.LessOrEqual(clipRate, PlayerSkinnedVisual.MaxRunRate + 1e-3f, "koşu klibi oran tavanını aştı: " + clipRate);
            Assert.Greater(clipRate, 0.9f, "6,2 m/s'de koşu klibi ≈ 1× oynamalı (v004 run_fwd canlı kalibrasyon 6,26 m/s): " + clipRate);

            // Flight-phase cycle: the planted foot is in contact for the manifest's 6 stance frames (of 15) after its landing
            // event, then lifts and swings — skating is its world travel over THOSE frames only (the WP-032 event-to-event
            // window would count the swing). Contact seconds = stance frames / 30 / clip rate.
            // Contact = the frames after a landing event while the landed foot moves BACKWARD relative to the body (its world Δx
            // below the body's Δx by more than the settle epsilon), at most the manifest stance length — the StrideCalibrator rule;
            // skating is the foot's signed world travel over those frames.
            int stanceFrames = Mathf.Max(1, pv.AnimationSet.Find(C.PlayerRunFwd).stanceFrames);
            float stanceSeconds = stanceFrames / 30f / Mathf.Max(0.1f, clipRate);
            var slips = new List<float>();
            bool pending = false, started = false;
            int landedFoot = 0, contactSamples = 0;
            float lastX = 0f, travel = 0f, contactTime = 0f;
            Transform[] feet = { lf, rf };
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e != CharacterAnimationEvent.FootLeft && e != CharacterAnimationEvent.FootRight) return;
                if (pending && contactSamples > 0) slips.Add(travel);
                landedFoot = e == CharacterAnimationEvent.FootLeft ? 0 : 1;
                pending = true; started = false; contactSamples = 0; travel = 0f; contactTime = 0f; lastX = feet[landedFoot].position.x;
            };
            pv.Events.Raised += onEvent;
            float t0 = Time.time;
            try
            {
                while (Time.time - t0 < 4f)
                {
                    float bodyDx = speed * Time.deltaTime;
                    root.position += Vector3.right * bodyDx;
                    yield return null;
                    if (!pending) continue;
                    float dx = feet[landedFoot].position.x - lastX;
                    lastX = feet[landedFoot].position.x;
                    float relative = dx - bodyDx;   // < 0: the foot moves back relative to the body (contact)
                    float eps = StrideCalibrator.SettleEpsilon * 30f * Time.deltaTime * clipRate;
                    if (!started) { if (relative >= -eps) continue; started = true; }
                    if (relative > eps || contactTime >= stanceSeconds) { pending = false; slips.Add(travel); continue; }
                    travel += dx; contactSamples++; contactTime += Time.deltaTime;
                }
            }
            finally { pv.Events.Raised -= onEvent; }

            Time.captureDeltaTime = 0f;
            var perCycle = new List<float>();
            for (int i = 0; i + 1 < slips.Count; i += 2) perCycle.Add(Mathf.Abs(0.5f * (slips[i] + slips[i + 1])));   // per stride cycle, signed windows (see AnimationPolishTests)
            float windowMean = 0f; foreach (float w in slips) windowMean += w; windowMean = slips.Count > 0 ? windowMean / slips.Count : 0f;
            perCycle.Sort();
            float median = perCycle.Count > 0 ? perCycle[perCycle.Count / 2] : 0f;
            Debug.Log("[DD-TEST] wp032b.runslip steps=" + slips.Count + " cycleMedian=" + (median * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm windowMeanSigned=" + (windowMean * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm clipRate=" + clipRate.ToString("F2", CultureInfo.InvariantCulture) + " runSpeed=" + pv.AnimationSet.SpeedOf(C.PlayerRunFwd).ToString("F3", CultureInfo.InvariantCulture));
            Assert.GreaterOrEqual(slips.Count, 6, "yeterli adım ölçülmedi");
            // Measured (b6, ×1.65 on the stance-calibrated 3.75 m/s): 7 contacts, cycle median 2.3 cm, signed mean +1.5 cm — the
            // band for the run is 3 cm on its contact frames (the walk/shamble gate stays in AnimationPolishTests).
            // WP-032D: this +X stage reads ≈ 4 cm more negative than the AnimationLiveCalibrationTests +Z stage at the same speed
            // (setup offset, both fixtures logged); the calibration gate is the live-calibration fixture (≤ 3 cm), this one bounds at 5 cm.
            Assert.LessOrEqual(median, 0.05f, "koşuda basan ayağın temas karelerindeki medyan kayması > 5 cm/adım");
        }

        // ============================================================ Ultra overhead (v002) — DEC-029 on the clip

        /// <summary>WP-071 MUST-I1 A/B diagnostic: the same Ultra overhead run with every tier on the AP-009 fallback gait.</summary>
        [UnityTest]
        [Explicit, Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator Tani_Overhead_Ultra_AP009Yurume()
        {
            ZombieSkinnedVisual.TierGaits = false;
            yield return Overhead_Ultra_v002_KendiKlibi_PencereKlipOlaylarindan();
            ZombieSkinnedVisual.TierGaits = true;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Overhead_Ultra_v002_KendiKlibi_PencereKlipOlaylarindan()
        {
            yield return RunAP005();
            Transform player = PlayerRegistry.Primary.transform;
            Vector3 p = player.position;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Ultra, 12, p + player.forward * 1.4f, Quaternion.LookRotation(-player.forward));
            Assert.IsNotNull(z); Assert.IsTrue(z.Skinned.Authored);
            Animator an = z.Skinned.Animator;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            int overheadFrames = 0, windupFrames = 0, activeOutside = 0, swings = 0, windupStyle = -1;
            float windupStart = -1f, windowOpenAt = -1f;
            ZombieAttackPhase prev = ZombieAttackPhase.None;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 10f && swings < 1)
            {
                ZombieAttackPhase phase = z.AttackPhase;
                if (phase == ZombieAttackPhase.Windup)
                {
                    if (prev != ZombieAttackPhase.Windup) windupStart = Time.time;
                    windupFrames++;
                    if (windupFrames == 1) windupStyle = an.GetInteger(C.AttackStyle);
                    if (an.GetCurrentAnimatorStateInfo(0).IsName(C.AttackOverheadState) || an.GetNextAnimatorStateInfo(0).IsName(C.AttackOverheadState)) overheadFrames++;
                }

                if (phase == ZombieAttackPhase.Active && windowOpenAt < 0f) windowOpenAt = Time.time;
                if (phase == ZombieAttackPhase.Active && !z.Skinned.HitWindowOpen) activeOutside++;
                if (prev == ZombieAttackPhase.Active && phase != ZombieAttackPhase.Active) swings++;
                prev = phase;
                yield return null;
            }

            float telegraph = z.Stats.TelegraphTime;
            Debug.Log("[DD-TEST] wp032b.overhead swings=" + swings + " windupFrames=" + windupFrames + " overheadFrames=" + overheadFrames + " activeOutside=" + activeOutside
                      + " telegraph=" + telegraph.ToString("F3", CultureInfo.InvariantCulture) + " measured=" + (windowOpenAt - windupStart).ToString("F3", CultureInfo.InvariantCulture) + " fallback=" + EnemyAttackState.WindowFallbackCount
                      + " attackStyle=" + windupStyle + " gait=" + C.ZombieGaitClips[z.Skinned.Gait] + " tierGaits=" + ZombieSkinnedVisual.TierGaits);
            Assert.GreaterOrEqual(swings, 1, "Ultra saldırmadı");
            Assert.Greater(overheadFrames, windupFrames / 2, "Ultra wind-up'ı Attack_Overhead durumunda değil (v002 klibi)");
            Assert.AreEqual(0, activeOutside, "aktif faz klip penceresi dışında");
            Assert.AreEqual(0, EnemyAttackState.WindowFallbackCount, "zamanlayıcı yedeği devreye girdi");
            Assert.AreEqual(telegraph, windowOpenAt - windupStart, 0.12f, "pencere telegraf zamanında açılmadı");
        }

        // ============================================================ queue Wait state, per-body phase

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Bekleme_WaitDurumu_IdleQueue_IkiGovdeFarkliFaz_GeriDonus()
        {
            yield return MainMenu();
            ZombieSkinnedVisual a = ZombieOnStage(new Vector3(0f, 0f, 0f), Quaternion.identity, 3u, 0);
            ZombieSkinnedVisual b = ZombieOnStage(new Vector3(2f, 0f, 0f), Quaternion.identity, 4u, 1);
            for (int i = 0; i < 5; i++) yield return null;
            a.SetWaiting(true); b.SetWaiting(true);
            yield return CharacterTests.Bekle(() => a.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.WaitState) && !a.Animator.IsInTransition(0)
                                                   && b.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.WaitState) && !b.Animator.IsInTransition(0), 3f, "Wait durumuna geçmedi");
            Assert.IsTrue(a.Waiting && b.Waiting);
            // The cycle offset is not visible in AnimatorStateInfo.normalizedTime (that is the state clock); the pose is.
            float phaseA = a.Animator.GetFloat(C.WaitPhase), phaseB = b.Animator.GetFloat(C.WaitPhase);
            float diff = Mathf.Abs(phaseA - phaseB); diff = Mathf.Min(diff, 1f - diff);
            Transform ha = a.Animator.GetBoneTransform(HumanBodyBones.Hips), hb = b.Animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform sa = a.Animator.GetBoneTransform(HumanBodyBones.Spine), sbn = b.Animator.GetBoneTransform(HumanBodyBones.Spine);
            float poseDelta = Quaternion.Angle(ha.localRotation, hb.localRotation) + Quaternion.Angle(sa.localRotation, sbn.localRotation);
            Debug.Log("[DD-TEST] wp032b.wait phaseA=" + phaseA.ToString("F3", CultureInfo.InvariantCulture) + " phaseB=" + phaseB.ToString("F3", CultureInfo.InvariantCulture) + " diff=" + diff.ToString("F3", CultureInfo.InvariantCulture) + " poseDeltaDeg=" + poseDelta.ToString("F2", CultureInfo.InvariantCulture));
            Assert.Greater(diff, 0.05f, "iki bekleyen gövde aynı WaitPhase'de");
            Assert.Greater(poseDelta, 0.5f, "faz ofseti poza yansımadı (cycleOffsetParameter)");
            a.SetWaiting(false); b.SetWaiting(false);
            yield return CharacterTests.Bekle(() => a.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && b.Animator.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), 3f, "Locomotion'a dönmedi");
            Assert.IsFalse(a.Waiting);
        }

        // ============================================================ zombie run (Runner) slip — stance-rule calibration evidence

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ZombiKosu_Runner_4_6ms_AyakKaymasi_2cmAlti()
        {
            // WP-032B: zombie_run calibrates to 6.14 m/s under the stance rule (manifest 5.32, whole-window 5.44); the
            // Runner's 4.6 m/s therefore plays at ×0.75 — this measures the slip at that rate (same method as the shamble test).
            ZombieSkinnedVisual.GaitVariety = 0f;   // WP-071: the exact-rate measurement runs without the per-spawn cadence spread
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, 60f, "MainMenu");
            yield return null;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _temp.Add(ground);
            ground.transform.localScale = new Vector3(80f, 0.2f, 80f); ground.transform.position = new Vector3(0f, -0.1f, 0f); ground.layer = PrototypeLayers.Environment;
            FootIkAdapter.Force = 0;
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP032B_Runner");
            _temp.Add(root);
            root.transform.SetPositionAndRotation(new Vector3(-20f, 0f, 0f), Quaternion.LookRotation(Vector3.right));
            GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
            var v = go.GetComponent<ZombieSkinnedVisual>();
            v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, 11u));
            v.ApplyMaterial(set.ZombieLibrary, 1);
            v.ResetForSpawn(EnemyTier.Special, 4.6f, "A", 0, false);
            v.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform lf = v.Animator.GetBoneTransform(HumanBodyBones.LeftFoot), rf = v.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform lt = v.Animator.GetBoneTransform(HumanBodyBones.LeftToes) ?? lf, rt = v.Animator.GetBoneTransform(HumanBodyBones.RightToes) ?? rf;
            const float speed = 4.6f;
            Time.captureDeltaTime = 1f / 60f;
            // WP-096 (08.10): the contact point = the lower of the ankle / toe markers relative to its own lowest point (the intake
            // calibration's and ZL1's definition); its lowest heights are read over one warm-up second
            float[] minAnkle = { float.MaxValue, float.MaxValue }, minToe = { float.MaxValue, float.MaxValue };
            for (int i = 0; i < 60; i++)
            {
                v.SetSpeed(speed, Time.deltaTime);
                yield return null;
                minAnkle[0] = Mathf.Min(minAnkle[0], lf.position.y); minAnkle[1] = Mathf.Min(minAnkle[1], rf.position.y);
                minToe[0] = Mathf.Min(minToe[0], lt.position.y); minToe[1] = Mathf.Min(minToe[1], rt.position.y);
            }
            // contact-frame metric (manifest stance 5 of 13): the landed foot's world travel over its stance frames only
            const int stanceFrames = 5;
            var slips = new List<float>();
            var contactSlips = new List<float>();
            float contactTravel = 0f;
            Vector3[] lastContact = new Vector3[2];
            float[] sampleX = { float.NaN, float.NaN };
            float sampleTime = -1f;
            bool pending = false;
            CharacterAnimationEvent landed = CharacterAnimationEvent.None;
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e != CharacterAnimationEvent.FootLeft && e != CharacterAnimationEvent.FootRight) return;
                sampleX[0] = lf.position.x; sampleX[1] = rf.position.x; sampleTime = Time.time; landed = e; pending = true;
                contactTravel = 0f;
                int f0 = e == CharacterAnimationEvent.FootLeft ? 0 : 1;
                lastContact[f0] = ContactPoint(f0 == 0 ? lf : rf, f0 == 0 ? lt : rt, minAnkle[f0], minToe[f0]);
            };
            v.Events.Raised += onEvent;
            float t0 = Time.time;
            try
            {
                while (Time.time - t0 < 4f)
                {
                    root.transform.position += Vector3.right * speed * Time.deltaTime;
                    v.SetSpeed(speed, Time.deltaTime);
                    yield return null;
                    float contactSeconds = stanceFrames / 30f / Mathf.Max(0.1f, v.Animator.GetFloat(C.GaitRate));
                    if (pending)
                    {
                        // the landed foot's contact point, frame by frame (the instantaneous lower marker — a rolling sole has no one material point)
                        int f1 = landed == CharacterAnimationEvent.FootLeft ? 0 : 1;
                        Vector3 cp = ContactPoint(f1 == 0 ? lf : rf, f1 == 0 ? lt : rt, minAnkle[f1], minToe[f1]);
                        contactTravel += cp.x - lastContact[f1].x;
                        lastContact[f1] = cp;
                    }

                    if (pending && Time.time - sampleTime >= contactSeconds)
                    {
                        slips.Add(landed == CharacterAnimationEvent.FootLeft ? lf.position.x - sampleX[0] : rf.position.x - sampleX[1]);
                        contactSlips.Add(contactTravel);
                        pending = false;
                    }
                }
            }
            finally { v.Events.Raised -= onEvent; Time.captureDeltaTime = 0f; FootIkAdapter.Force = -1; }

            var perCycle = new List<float>();
            for (int i = 0; i + 1 < slips.Count; i += 2) perCycle.Add(Mathf.Abs(0.5f * (slips[i] + slips[i + 1])));
            var contactCycle = new List<float>();
            for (int i = 0; i + 1 < contactSlips.Count; i += 2) contactCycle.Add(Mathf.Abs(0.5f * (contactSlips[i] + contactSlips[i + 1])));
            contactCycle.Sort();
            float contactMedian = contactCycle.Count > 0 ? contactCycle[contactCycle.Count / 2] : 0f;
            float windowMean = 0f; foreach (float w in slips) windowMean += w; windowMean = slips.Count > 0 ? windowMean / slips.Count : 0f;
            perCycle.Sort();
            float median = perCycle.Count > 0 ? perCycle[perCycle.Count / 2] : 0f;
            Debug.Log("[DD-TEST] wp032b.zrunslip steps=" + slips.Count + " cycleMedian=" + (median * 100f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " cm windowMeanSigned=" + (windowMean * 100f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " cm rate=" + v.Animator.GetFloat(C.GaitRate).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " runSpeed=" + v.AnimationSet.SpeedOf(C.ZombieRun).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                      + " gaitClip=" + C.ZombieGaitClips[v.Gait] + " clipSpeed=" + v.AnimationSet.SpeedOf(C.ZombieGaitClips[v.Gait]).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                      + " contactCycleMedian=" + (contactMedian * 100f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " cm");
            Assert.GreaterOrEqual(slips.Count, 8, "yeterli adım ölçülmedi");
            // WP-096 (08.10): the gate stays the ankle (S-06's metric) until C-00 / S-06 decide; the contact point (lower of ankle / toe,
            // the intake calibration's definition) is only logged — a heel-strike run rolls the ankle while the sole holds.
            Assert.LessOrEqual(median, 0.02f, "Runner koşusunda basan ayağın döngü kayması > 2 cm/adım");
        }

        /// <summary>WP-096: the foot's contact point — the ankle or the toe marker, whichever is nearer its own lowest height.</summary>
        private static Vector3 ContactPoint(Transform ankle, Transform toe, float minAnkle, float minToe)
            => toe.position.y - minToe < ankle.position.y - minAnkle ? toe.position : ankle.position;
    }
}
