using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
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
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-018 · The AP-009 set running (Metal): the DEC-029 hit window opened and closed by
    /// the clip's own events (never by the timer), the hit reaction that does not stop the
    /// walk (REQ-044/123), the four deaths ending on death_end and going back to the pool,
    /// the rise, the player's 2-D locomotion blend / aim layer / fire / hit / pickup / death
    /// on the prefab, and an allocation-free animation path over a 40-body crowd.
    /// </summary>
    [TestFixture]
    [Category("WP018")]
    public sealed class AnimationTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private readonly List<GameObject> _temp = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            EnemyAttackState.ResetStatics();
            CharacterAnimatorLod.ResetStatics();
            ZombieEnemy.RiseOnSpawn = false;
            _director = null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            ZombieEnemy.RiseOnSpawn = false;
            AttackSelector.Enabled = true;
            CharacterAnimatorLod.ForceFar = -1;
            foreach (GameObject go in _temp) if (go != null) UnityEngine.Object.Destroy(go);
            _temp.Clear();
            if (_director != null) _director.ForceActiveTarget(0, false);
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CharacterAssetSet.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }

            _director = null;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator RunAP005()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static ZombieEnemy Spawn(EnemyTier tier, Vector3 at, Vector3 facing)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 1, at, Quaternion.LookRotation(facing.sqrMagnitude > 1e-6f ? facing : Vector3.forward));
            Assert.IsNotNull(z, "zombi doğmadı");
            Assert.IsNotNull(z.Skinned, "skinned yok");
            Assert.IsTrue(z.Skinned.Authored, "AP-009 seti sürmüyor (rig-test controller?)");
            return z;
        }

        private static DamageHit Hit(float amount, BodyRegion region, Vector3 direction, float force)
        {
            return new DamageHit { Amount = amount, Region = region, Point = Vector3.zero, Direction = direction.normalized, ImpactForce = force, Source = DamageSourceKind.Bullet };
        }

        // ============================================================== DEC-029 saldırı penceresi

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SaldiriPenceresi_DEC029_YalnizKlipOlaylariArasinda_ZamanlayiciYedegi0()
        {
            AttackSelector.Enabled = false;   // WP-076 B1: the selector varies the style (telegraph ×0.85…2.1); this test times the signature swipe
            yield return RunAP005();
            Transform player = PlayerRegistry.Primary.transform;
            Vector3 p = player.position;
            ZombieEnemy z = Spawn(EnemyTier.Standard, p + player.forward * 1.3f, -player.forward);
            Assert.IsTrue(z.AttackAnimationDriven, "saldırı penceresi klibe bağlı değil");
            CharacterAnimationEvents events = z.Skinned.Events;
            Assert.IsNotNull(events);

            int activeFrames = 0, activeOutsideWindow = 0, swings = 0, windowStarts = 0, windowEnds = 0;
            int lastPhaseWindupFrame = -1;
            var telegraphSeconds = new List<float>();
            float windupStart = -1f;
            Action<CharacterAnimationEvent> onEvent = e =>
            {
                if (e == CharacterAnimationEvent.HitWindowStart) { windowStarts++; if (windupStart >= 0f) telegraphSeconds.Add(Time.time - windupStart); }
                if (e == CharacterAnimationEvent.HitWindowEnd) windowEnds++;
            };
            events.Raised += onEvent;
            ZombieAttackPhase previous = ZombieAttackPhase.None;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                while (Time.realtimeSinceStartup - t0 < 9f && swings < 3)
                {
                    ZombieAttackPhase phase = z.AttackPhase;
                    if (phase == ZombieAttackPhase.Windup && previous != ZombieAttackPhase.Windup) { windupStart = Time.time; lastPhaseWindupFrame = Time.frameCount; }
                    if (phase == ZombieAttackPhase.Active)
                    {
                        activeFrames++;
                        if (!z.Skinned.HitWindowOpen) activeOutsideWindow++;
                    }

                    if (previous == ZombieAttackPhase.Active && phase != ZombieAttackPhase.Active) swings++;
                    previous = phase;
                    yield return null;
                }
            }
            finally
            {
                events.Raised -= onEvent;
            }

            float telegraph = z.Stats.TelegraphTime;
            string tele = "";
            foreach (float t in telegraphSeconds) tele += t.ToString("F3", CultureInfo.InvariantCulture) + " ";
            Debug.Log("[DD-TEST] wp018.window swings=" + swings + " activeFrames=" + activeFrames + " outsideWindow=" + activeOutsideWindow + " starts=" + windowStarts + " ends=" + windowEnds
                      + " fallback=" + EnemyAttackState.WindowFallbackCount + " events=" + EnemyAttackState.WindowEventCount + " telegraphConfig=" + telegraph.ToString("F3", CultureInfo.InvariantCulture)
                      + " telegraphMeasured=" + tele + " lastConnectFrame=" + z.LastAttackConnectFrame + " windowFrames=" + z.Skinned.LastWindowStartFrame + ".." + z.Skinned.LastWindowEndFrame);
            Assert.GreaterOrEqual(swings, 1, "zombi hiç saldırmadı");
            Assert.Greater(activeFrames, 0);
            Assert.AreEqual(0, activeOutsideWindow, "Active faz klibin penceresi dışında kaldı");
            Assert.AreEqual(0, EnemyAttackState.WindowFallbackCount, "zamanlayıcı yedeği devreye girdi (olay gelmedi)");
            Assert.GreaterOrEqual(windowStarts, swings); Assert.GreaterOrEqual(windowEnds, swings);
            foreach (float t in telegraphSeconds) Assert.AreEqual(telegraph, t, 0.09f, "hit_window_start telegraph zamanına oturmalı");
            if (z.LastAttackConnectFrame >= 0)
            {
                Assert.GreaterOrEqual(z.LastAttackConnectFrame, z.Skinned.LastWindowStartFrame, "vuruş pencere başından önce");
                Assert.LessOrEqual(z.LastAttackConnectFrame, z.Skinned.LastWindowEndFrame, "vuruş pencere sonundan sonra");
            }
        }

        // ================================================================ hit reaction / stagger

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HitReaction_YurumeyiKesmez_UstGovdeKatmani_ZirhKirilmasi_AgirSendeleme()
        {
            yield return RunAP005();
            Vector3 p = PlayerRegistry.Primary.transform.position;
            ZombieEnemy z = Spawn(EnemyTier.Standard, p + new Vector3(0f, 0f, 14f), Vector3.back);
            Animator an = z.Skinned.Animator;
            int over = ArtAnimatorLayers.Index(an, C.OverrideUpperLayer);
            int add = ArtAnimatorLayers.Index(an, C.AdditiveLayer);
            Assert.IsTrue(over > 0 && add > 0, "katmanlar");

            // Let it walk, then a light hit from the left: the legs keep walking, the upper body reacts.
            yield return CharacterTests.Bekle(() => z.Skinned.Animator.GetFloat(Animator.StringToHash(C.Speed01)) > 0.5f, 6f, "yürümeye başlamadı");
            Vector3 before = z.transform.position;
            DamageResult r = z.ApplyHit(Hit(1f, BodyRegion.UpperBody, z.transform.right, 1.2f));
            Assert.IsFalse(r.Killed);
            yield return null; yield return null;
            AnimatorStateInfo overState = an.GetCurrentAnimatorStateInfo(over);
            AnimatorStateInfo addState = an.GetCurrentAnimatorStateInfo(add);
            AnimatorStateInfo baseState = an.GetCurrentAnimatorStateInfo(0);
            // WP-032C: the upper read is the v003 directional × tier clip (a Standard shoved along +right → Hit_right_std / left by the impulse)
            bool overHit = false;
            foreach (string d in C.HitDirections) { string n = C.HitState + "_" + d + "_std"; overHit |= overState.IsName(n) || an.GetNextAnimatorStateInfo(over).IsName(n); }
            bool addFlinch = addState.IsName(C.FlinchState + "_Torso") || an.GetNextAnimatorStateInfo(add).IsName(C.FlinchState + "_Torso");
            Debug.Log("[DD-TEST] wp018.hit overHit=" + overHit + " addFlinch=" + addFlinch + " base=" + baseState.IsName(C.LocomotionState));
            Assert.IsTrue(addFlinch, "bölgesel additive flinch oynamadı");
            Assert.IsTrue(overHit, "üst gövde hit klibi oynamadı");
            Assert.IsTrue(baseState.IsName(C.LocomotionState), "hafif vuruş taban katmanını (yürüme) kesti");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
            float moved = Vector3.Distance(before, z.transform.position);
            Assert.Greater(moved, 0.4f, "hafif vuruş yürümeyi durdurdu (REQ-123): " + moved.ToString("F2"));

            // Armour break → heavy stagger clip (ADR-018-2), one per break.
            Assert.AreEqual(3, z.FitArmorComposition(true, true, true));
            int stagger = 0;
            for (int i = 0; i < 90; i++)
            {
                if (i == 0) z.ApplyHit(Hit(1000f, BodyRegion.Head, Vector3.back, 1f));
                AnimatorStateInfo s = an.GetCurrentAnimatorStateInfo(0);
                // WP-032C: a broken helmet plays the v003 armorbreak_head clip (ArmorBreak_0) — the WP-018 heavy stagger stays for REQ-124
                if (s.IsName(C.ArmorBreakState + "_0") || an.GetNextAnimatorStateInfo(0).IsName(C.ArmorBreakState + "_0")) { stagger++; break; }
                yield return null;
            }

            Assert.IsTrue(z.Armor.IsBroken(ArmorSlot.Helmet), "kask kırılmadı");
            Assert.Greater(stagger, 0, "kırılmada armorbreak_head klibi oynamadı");
        }

        // ============================================================================ ölüm ×4

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Olum_DortKlip_DeathEnd_HavuzaDonus()
        {
            yield return RunAP005();
            Vector3 p = PlayerRegistry.Primary.transform.position;
            // Direction / intensity → M1 style → AP-009 clip: front push = Death_0 (fwd), back push = Death_1, heavy = Death_2 (side), light head = Death_3 (kneel).
            var cases = new[]
            {
                (dir: Vector3.forward, region: BodyRegion.UpperBody, force: 1f, expected: 0),
                (dir: Vector3.back, region: BodyRegion.UpperBody, force: 1f, expected: 1),
                (dir: Vector3.right, region: BodyRegion.UpperBody, force: 2.5f, expected: 4),   // WP-032C: a push to the right = death_side_R (4); to the left = death_side (2)
                (dir: Vector3.right, region: BodyRegion.Head, force: 1f, expected: 3)
            };
            var zombies = new List<ZombieEnemy>();
            for (int i = 0; i < cases.Length; i++)
            {
                ZombieEnemy z = Spawn(EnemyTier.Standard, p + new Vector3(-6f + i * 4f, 0f, 16f), Vector3.forward);
                zombies.Add(z);
            }

            yield return null;
            for (int i = 0; i < cases.Length; i++)
            {
                ZombieEnemy z = zombies[i];
                z.FitArmorComposition(false, false, false);   // bare: a helmet would swallow the head hit (WP-006 overflow rule)
                Vector3 dir = z.transform.TransformDirection(cases[i].dir);
                DamageResult r = z.ApplyHit(Hit(10000f, cases[i].region, dir, cases[i].force));
                Debug.Log("[DD-TEST] wp018.death case=" + i + " alive=" + z.IsAlive + " dying=" + z.IsDying + " health=" + z.Health + " killed=" + r.Killed + " active=" + z.gameObject.activeInHierarchy + " pos=" + z.transform.position.ToString("F1") + " state=" + z.Skinned.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
                Assert.IsTrue(r.Killed, "vaka " + i + " ölmedi (alive=" + z.IsAlive + " dying=" + z.IsDying + " health=" + z.Health + ")");
            }

            yield return null; yield return null;
            for (int i = 0; i < cases.Length; i++)
            {
                Animator an = zombies[i].Skinned.Animator;
                string expected = C.DeathState + "_" + cases[i].expected;
                bool ok = an.GetCurrentAnimatorStateInfo(0).IsName(expected) || an.GetNextAnimatorStateInfo(0).IsName(expected);
                Assert.IsTrue(ok, "vaka " + i + ": beklenen " + expected);
            }

            float t0 = Time.realtimeSinceStartup;
            var ended = new bool[cases.Length];
            var endedAt = new float[cases.Length];
            // WP-076 B2 (DEC-096): corpse linger v2 is 4–6 s × quality after DeathFinished — the window covers death + linger
            while (Time.realtimeSinceStartup - t0 < 14f && !zombies.TrueForAll(z => !z.gameObject.activeInHierarchy || !z.IsDying))
            {
                for (int i = 0; i < cases.Length; i++) if (zombies[i].gameObject.activeInHierarchy && zombies[i].Skinned.DeathFinished && !ended[i]) { ended[i] = true; endedAt[i] = Time.realtimeSinceStartup - t0; }
                yield return null;
            }

            for (int i = 0; i < cases.Length; i++)
            {
                Debug.Log("[DD-TEST] wp018.death.pool case=" + i + " endedAt=" + endedAt[i].ToString("F2", CultureInfo.InvariantCulture) + " s active=" + zombies[i].gameObject.activeInHierarchy + " linger=" + zombies[i].LastCorpseLinger.ToString("F2", CultureInfo.InvariantCulture)
                          + " timeScale=" + Time.timeScale.ToString("F2", CultureInfo.InvariantCulture) + " animSpeed=" + zombies[i].Skinned.Animator.speed.ToString("F2", CultureInfo.InvariantCulture) + " dying=" + zombies[i].IsDying);
                Assert.IsTrue(ended[i], "vaka " + i + ": death_end gelmedi");
                // WP-032F: the Run scene's spawn director may check the pooled body out again inside the window — "left the corpse
                // state" (inactive, or active in a new life) is the pool return, not activeInHierarchy alone.
                Assert.IsTrue(!zombies[i].gameObject.activeInHierarchy || !zombies[i].IsDying, "vaka " + i + ": ceset havuza dönmedi");
            }

            Assert.AreEqual(0, EnemyAttackState.WindowFallbackCount);
        }

        // ================================================================================ rise

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Rise_SpawnKlibi_RiseEndeKadarYurumez_Saymaz()
        {
            yield return RunAP005();
            ZombieEnemy.RiseOnSpawn = true;
            Vector3 p = PlayerRegistry.Primary.transform.position;
            ZombieEnemy z = Spawn(EnemyTier.Standard, p + new Vector3(0f, 0f, 12f), Vector3.back);
            Assert.IsTrue(z.Skinned.IsRising, "rise başlamadı");
            Vector3 start = z.transform.position;
            yield return null;
            Animator ra = z.Skinned.Animator;
            Debug.Log("[DD-TEST] wp018.rise enabled=" + ra.enabled + " init=" + ra.isInitialized + " cur=" + ra.GetCurrentAnimatorStateInfo(0).IsName(C.RiseState) + " next=" + ra.GetNextAnimatorStateInfo(0).IsName(C.RiseState) + " loco=" + ra.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) + " trigger=" + ra.GetBool(C.Rise) + " alive=" + ra.GetBool(C.Alive) + " far=" + z.Skinned.GetComponent<CharacterAnimatorLod>().Far + " culled=" + ra.cullingMode + " visible=" + z.Skinned.Slot(0, 0).isVisible);
            yield return null;
            // WP-032C: the rise is one of Rise / Rise_1 / Rise_2 (v003 rise_B / rise_C variants, deterministic per spawn)
            bool inRise = false;
            for (int v = 0; v < C.ZombieRiseClips.Length; v++) { string n = v == 0 ? C.RiseState : C.RiseState + "_" + v; inRise |= ra.GetCurrentAnimatorStateInfo(0).IsName(n) || ra.GetNextAnimatorStateInfo(0).IsName(n); }
            Assert.IsTrue(inRise, "Rise durumu yok");
            float t0 = Time.realtimeSinceStartup;
            float movedWhileRising = 0f;
            while (z.Skinned.IsRising && Time.realtimeSinceStartup - t0 < 4f)
            {
                movedWhileRising = Mathf.Max(movedWhileRising, Vector3.Distance(start, z.transform.position));
                yield return null;
            }

            Assert.IsFalse(z.Skinned.IsRising, "rise_end gelmedi");
            Assert.Less(movedWhileRising, 0.15f, "rise sırasında yürüdü");
            Assert.IsTrue(z.IsAlive);
            yield return CharacterTests.Bekle(() => Vector3.Distance(start, z.transform.position) > 1f, 6f, "rise sonrası yürümedi");
        }

        // ============================================================================ oyuncu prefab

        private PlayerSkinnedVisual PlayerOnStage(Func<Vector3> velocity, Func<Vector3> aim, out Transform visual)
        {
            CharacterAssetSet set = CharacterAssetSet.Load();
            Assert.IsNotNull(set, "DD_Characters");
            var root = new GameObject("WP018_PlayerStage");
            _temp.Add(root);
            visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            GameObject go = UnityEngine.Object.Instantiate(set.PlayerPrefab, visual, false);
            var pv = go.GetComponent<PlayerSkinnedVisual>();
            Assert.IsNotNull(pv);
            pv.Attach(visual, velocity, () => 6.2f, aim);
            Assert.IsTrue(pv.Authored, "oyuncu AP-009 seti değil");
            return pv;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Oyuncu_2DBlend_Yonler_Nisan_Ates_Hit_Pickup_Olum()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, SahneZamanAsimi, "MainMenu");
            Vector3 velocity = Vector3.zero;
            Vector3 aim = Vector3.forward;
            PlayerSkinnedVisual pv = PlayerOnStage(() => velocity, () => aim, out Transform visual);
            Animator an = pv.Animator;
            int over = ArtAnimatorLayers.Index(an, C.OverrideUpperLayer), add = ArtAnimatorLayers.Index(an, C.AdditiveLayer), aimLayer = ArtAnimatorLayers.Index(an, C.AimLayer);
            Assert.IsTrue(over > 0 && add > 0 && aimLayer > 0);
            Assert.IsTrue(pv.AimLayer != null && pv.AimLayer.Active, "aim layer pasif");

            // Forward walk → MoveY ≈ 1, rate ≈ 1; strafe → MoveX; back → MoveY ≈ −1; stop → idle.
            velocity = Vector3.forward * 6.2f;
            yield return CharacterTests.Bekle(() => pv.Move.y > 0.9f, 2f, "ileri blend");
            Assert.Less(Mathf.Abs(pv.Move.x), 0.1f);
            Assert.Greater(pv.LocoRate, 0.9f);
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState));
            velocity = Vector3.right * 3.1f;
            yield return CharacterTests.Bekle(() => pv.Move.x > 0.45f && pv.Move.y < 0.1f, 2f, "sağ strafe blend");
            velocity = Vector3.back * 6.2f;
            yield return CharacterTests.Bekle(() => pv.Move.y < -0.9f, 2f, "geri blend");
            velocity = Vector3.zero;
            yield return CharacterTests.Bekle(() => pv.Move.magnitude < 0.05f, 2f, "idle");

            // Aim layer: the spine turns toward the aim (yaw sign), pitches with it, and the bones actually move.
            Transform spine = an.GetBoneTransform(HumanBodyBones.Spine);
            Quaternion restSpine = spine.localRotation;
            aim = new Vector3(1f, 0f, 1f).normalized;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
            float yawRight = pv.AimLayer.Yaw;
            Quaternion rightSpine = spine.localRotation;
            aim = new Vector3(-1f, 0.3f, 1f).normalized;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
            float yawLeft = pv.AimLayer.Yaw;
            Debug.Log("[DD-TEST] wp018.aim yawRight=" + yawRight.ToString("F1", CultureInfo.InvariantCulture) + " yawLeft=" + yawLeft.ToString("F1", CultureInfo.InvariantCulture) + " pitch=" + pv.AimLayer.Pitch.ToString("F1", CultureInfo.InvariantCulture) + " spineDelta=" + Quaternion.Angle(restSpine, rightSpine).ToString("F1", CultureInfo.InvariantCulture));
            Assert.Greater(yawRight, 10f, "sağa nişan → pozitif yaw");
            Assert.Less(yawLeft, -10f, "sola nişan → negatif yaw");
            Assert.Less(pv.AimLayer.Pitch, -3f, "yukarı nişan → negatif pitch (başı kaldırır)");
            Assert.Greater(Quaternion.Angle(restSpine, rightSpine), 2f, "omurga kemiği nişanla dönmedi");
            aim = Vector3.forward;

            // Fire → additive layer state; Hit → override layer state; both leave the base layer alone.
            pv.PlayFire();
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(add).IsName(C.FireState + "_0") || an.GetNextAnimatorStateInfo(add).IsName(C.FireState + "_0"), "WP-032C: Fire_0 (A rifle recoil) additive katmanda değil");
            pv.PlayHit();
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(over).IsName(C.HitState) || an.GetNextAnimatorStateInfo(over).IsName(C.HitState), "hit_light override katmanda değil");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "ateş/hit taban katmanını değiştirdi");

            // Pickup through the WP-007 event → WP-032B (DEC-082): the grab on the upper-body override layer while the
            // legs keep the locomotion blend (walking forward the whole time), then back to the layer's Empty.
            velocity = Vector3.forward * 6.2f;
            yield return CharacterTests.Bekle(() => pv.Move.y > 0.9f, 2f, "pickup öncesi ileri blend");
            WeaponEvents.PublishSpecialPickedUp(10f);
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(over).IsName(C.PickupState) || an.GetNextAnimatorStateInfo(over).IsName(C.PickupState), "pickup klibi üst gövde katmanında başlamadı");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !an.IsInTransition(0), "pickup taban katmanını kesti");
            Assert.Greater(pv.Move.y, 0.9f, "pickup sırasında yürüyüş blend'i düştü");
            // WP-032C: the armed rest is Aim_<Weapon> (A rifle by default), not Empty
            yield return CharacterTests.Bekle(() => an.GetCurrentAnimatorStateInfo(over).IsName(C.AimState + "_0") && !an.IsInTransition(over), 3f, "pickup sonrası üst katman Aim_0'a dönmedi");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "pickup boyunca taban katmanı locomotion kalmalı");
            velocity = Vector3.zero;
            yield return CharacterTests.Bekle(() => pv.Move.magnitude < 0.05f, 2f, "pickup sonrası idle");

            // Death: back push → Death_1; death_end arrives; no way back.
            pv.PlayDeath(Vector3.back, false);
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.DeathState + "_1") || an.GetNextAnimatorStateInfo(0).IsName(C.DeathState + "_1"), "death_back");
            yield return CharacterTests.Bekle(() => pv.DeathFinished, 3f, "death_end gelmedi");
            pv.PlayFire(); pv.PlayHit();
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.DeathState + "_1"), "ölü oyuncu ölümden çıktı");
        }

        // ================================================================== kalabalık: GC 0 + uzak adım

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kalabalik40_AnimasyonYolu_GcSifir_UzakAdim15Hz()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, SahneZamanAsimi, "MainMenu");
            CharacterAssetSet set = CharacterAssetSet.Load();
            var root = new GameObject("WP018_Crowd");
            _temp.Add(root);
            var visuals = new List<ZombieSkinnedVisual>();
            for (int i = 0; i < 40; i++)
            {
                GameObject go = UnityEngine.Object.Instantiate(set.ZombiePrefab, root.transform, false);
                go.transform.localPosition = new Vector3(i % 8 * 1.5f, 0f, i / 8 * 1.5f);
                var v = go.GetComponent<ZombieSkinnedVisual>();
                v.Assemble(set.ZombieLibrary, ZombieLookSelector.Select(set.ZombieLibrary, (uint)(i + 1)));
                v.ResetForSpawn(EnemyTier.Standard, 2.7f, i % 3 == 0 ? "A" : i % 3 == 1 ? "B" : "C", i, false);
                visuals.Add(v);
            }

            Assert.IsTrue(visuals[0].Authored);
            // Warm-up: every reaction once, so first-use allocations are paid before the window.
            for (int frame = 0; frame < 120; frame++)
            {
                Drive(visuals, frame);
                yield return null;
            }

            GC.Collect();
            yield return null; yield return null;
            long monoBefore = Profiler.GetMonoUsedSizeLong();
            int gen0Before = GC.CollectionCount(0);
            for (int frame = 0; frame < 240; frame++)
            {
                Drive(visuals, frame);
                yield return null;
            }

            long delta = Profiler.GetMonoUsedSizeLong() - monoBefore;
            int gen0 = GC.CollectionCount(0) - gen0Before;
            // Positive control: the measurement sees a known allocation.
            long ctrlBefore = Profiler.GetMonoUsedSizeLong();
            var bait = new byte[256 * 1024];
            long ctrl = Profiler.GetMonoUsedSizeLong() - ctrlBefore;
            GC.KeepAlive(bait);
            Debug.Log("[DD-TEST] wp018.gc crowd=40 frames=240 monoDelta=" + delta + " gen0=" + gen0 + " control=" + ctrl);
            Assert.Greater(ctrl, 128 * 1024, "pozitif kontrol: ölçüm ayırmayı görmüyor");
            Assert.AreEqual(0, gen0, "kalabalık animasyon yolu gen0 toplama tetikledi");
            Assert.LessOrEqual(delta, 4096, "animasyon yolu kare başına ayırıyor: " + delta + " B / 240 kare");

            // Far step: forced far → the Animator is stepped by hand at ≤ 15 Hz, events keep coming.
            CharacterAnimatorLod.ForceFar = 1;
            yield return null; yield return null;
            Assert.AreEqual(40, CharacterAnimatorLod.FarCount, "uzak sayacı");
            Assert.IsFalse(visuals[0].Animator.enabled, "uzak gövdenin Animator'ı elle adımlanmalı");
            int eventsBefore = CharacterAnimationEvents.RaisedCount;
            float before = visuals[0].Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float t0 = Time.realtimeSinceStartup;
            int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 1f) { frames++; yield return null; }
            float after = visuals[0].Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            Debug.Log("[DD-TEST] wp018.far frames=" + frames + " normalizedTime " + before.ToString("F2", CultureInfo.InvariantCulture) + "→" + after.ToString("F2", CultureInfo.InvariantCulture) + " events=" + (CharacterAnimationEvents.RaisedCount - eventsBefore));
            Assert.Greater(after, before, "uzak gövdenin saati ilerlemedi");
            CharacterAnimatorLod.ForceFar = 0;
            yield return null; yield return null;
            Assert.AreEqual(0, CharacterAnimatorLod.FarCount);
            Assert.IsTrue(visuals[0].Animator.enabled);
        }

        private static void Drive(List<ZombieSkinnedVisual> visuals, int frame)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                ZombieSkinnedVisual v = visuals[i];
                v.SetSpeed(2.7f * ((frame / 30 + i) % 2), Time.deltaTime);
                int beat = (frame + i) % 120;
                if (beat == 0) v.PlayAttack(ZombieAttackStyle.Swipe, 0.55f);
                else if (beat == 40) v.PlayFlinch(i % 2 == 0 ? Vector3.left : Vector3.right, BodyRegion.UpperBody, true);
                else if (beat == 60) v.PlayFlinch(Vector3.right, BodyRegion.Head, false);
                else if (beat == 80) v.PlayHeavyStagger();
            }
        }
    }

    internal static class ArtAnimatorLayers
    {
        public static int Index(Animator animator, string layerName)
        {
            for (int i = 0; i < animator.layerCount; i++) if (animator.GetLayerName(i) == layerName) return i;
            return -1;
        }
    }
}
