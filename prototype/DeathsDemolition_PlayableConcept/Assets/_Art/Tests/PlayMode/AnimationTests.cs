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
using DD.Prototype.Tests;
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
            // C-00 05.10 (main-ci 285b7fe8): every test starts from the default camera mode — PlayerSkinnedVisual mutes the TP fire additive in
            // first person (1418b443), so a mode left over by an earlier FP test must never reach a TP additive check
            DD.Prototype.CameraSystem.CameraModeService.ResetForTests();
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
            // R3 (C-00 07.10): the next test starts in the process default, TopDown (user 06.10: tests run top-down only) — the FP
            // tests below leave first or third person in their finally blocks
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.TopDown, instant: true);
        }

        // WP-071 (C-00 26.09): the FP arms tests set the switch for their own run and restore it — the shipped default is one commit either way.
        private static bool? s_armsBefore;

        private static void ArmsForTest(bool on)
        {
            if (s_armsBefore == null) s_armsBefore = PlayerSkinnedVisual.FirstPersonV3Arms;
            PlayerSkinnedVisual.FirstPersonV3Arms = on;
        }

        [TearDown]
        public void RestoreArmsSwitch()
        {
            if (!s_armsBefore.HasValue) return;
            PlayerSkinnedVisual.FirstPersonV3Arms = s_armsBefore.Value;
            s_armsBefore = null;
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

        [Category("WP018")]
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

        [Category("WP018")]
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
            // WP-096 R3 (CI e8fc3f70): a body setting off plays the AP-033 p3 start (a base-layer state of its own) before it settles
            // into Locomotion — the light hit lands on the settled walk, the read this test makes
            yield return CharacterTests.Bekle(() => z.Skinned.Animator.GetFloat(Animator.StringToHash(C.Speed01)) > 0.5f && !z.Skinned.TransitionActive
                                                    && an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !an.IsInTransition(0), 6f, "yürümeye başlamadı (Locomotion'a oturmadı)");
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

        // ============================================================================ WP-096 R3: p3 near the player

        /// <summary>
        /// WP-096 R3 (REQ-046 0841b7dd; gt S1 / S2): a body engaging its target (≤ AttackRange × 1.6) plays no p3 start / stop / turn
        /// through a whole swing — Recovery → walk is the edge that opened the live wall — while a far body sets off with one (control).
        /// A Runner engaging the player still plays the stumble_recover reaction to its end: the clip AND its authored attach-out to the
        /// gait, with no CancelTransition (gt S1: the clip name alone let the attach-out be cut). The tree stagger holds the Runner's
        /// swing for the whole reaction, so the motor's only reason to cancel is the engaging rule.
        /// </summary>
        [Category("WP096")]
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator P3_MesgulGovdeBaslaDurDonOynamaz_UzakOynar_StumbleRecoverAttachOutKesilmez()
        {
            yield return RunAP005();
            Vector3 p = PlayerRegistry.Primary.transform.position;
            ZombieEnemy far = Spawn(EnemyTier.Standard, p + new Vector3(0f, 0f, 14f), Vector3.back);
            ZombieEnemy near = Spawn(EnemyTier.Standard, p + new Vector3(1.4f, 0f, 0f), Vector3.left);
            float engageNear = near.Stats.AttackRange * 1.6f;
            int nearStarts0 = near.Skinned.TransitionStarts, farStarts0 = far.Skinned.TransitionStarts;
            bool recovery = false, recoveryEnded = false;
            float maxNear = 0f, t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f && !(recoveryEnded && far.Skinned.TransitionStarts > farStarts0))
            {
                maxNear = Mathf.Max(maxNear, Flat(near.transform.position, p));
                if (near.AttackPhase == ZombieAttackPhase.Recovery) recovery = true;
                else if (recovery) recoveryEnded = true;
                yield return null;
            }

            // the edge after the swing: a few more frames for a start the motor would have begun
            for (int i = 0; i < 20; i++) { maxNear = Mathf.Max(maxNear, Flat(near.transform.position, p)); yield return null; }
            Debug.Log("[DD-TEST] wp096.p3.engage nearStarts=" + (near.Skinned.TransitionStarts - nearStarts0) + " farStarts=" + (far.Skinned.TransitionStarts - farStarts0)
                      + " recoveryEnded=" + recoveryEnded + " maxNear=" + maxNear.ToString("F2", CultureInfo.InvariantCulture) + " engage=" + engageNear.ToString("F2", CultureInfo.InvariantCulture));
            Assert.IsTrue(recoveryEnded, "yakın gövde 6 s içinde bir saldırıyı (Recovery → yürüyüş) bitirmedi — ölçülen kenar yok");
            Assert.LessOrEqual(maxNear, engageNear, "yakın gövde meşgul bandından çıktı — ölçüm meşgul durumu değil");
            Assert.AreEqual(0, near.Skinned.TransitionStarts - nearStarts0, "meşgul gövde p3 başla/dur/dön oynadı (REQ-046)");
            Assert.Greater(far.Skinned.TransitionStarts - farStarts0, 0, "uzak gövde p3 kalkışıyla yola çıkmadı (kontrol)");

            // the Runner's stumble_recover while engaging: clip + attach-out, never cancelled
            ZombieEnemy runner = Spawn(EnemyTier.Special, p + new Vector3(-1.4f, 0f, 0f), Vector3.right);
            float engageRunner = runner.Stats.AttackRange * 1.6f;
            yield return null; yield return null;
            int cancels0 = runner.Skinned.TransitionCancels, done0 = runner.Skinned.TransitionCompletions;
            Assert.IsTrue(runner.ApplyTreeStagger(3f, Vector3.left), "tree stagger reddedildi");
            yield return null;
            Assert.IsTrue(runner.Skinned.StumbleRecoverActive && runner.Skinned.TransitionClip == "AP033_zombie_stumble_recover", "p3 stumble_recover başlamadı: " + runner.Skinned.TransitionClip);
            bool attach = false; float maxRunner = 0f; t0 = Time.realtimeSinceStartup;
            while (runner.Skinned.StumbleRecoverActive && Time.realtimeSinceStartup - t0 < 4f)
            {
                attach |= !runner.Skinned.TransitionActive;
                maxRunner = Mathf.Max(maxRunner, Flat(runner.transform.position, p));
                yield return null;
            }

            Debug.Log("[DD-TEST] wp096.p3.stumbleEngaged completions+" + (runner.Skinned.TransitionCompletions - done0) + " cancels+" + (runner.Skinned.TransitionCancels - cancels0)
                      + " attachSeen=" + attach + " maxDist=" + maxRunner.ToString("F2", CultureInfo.InvariantCulture) + " engage=" + engageRunner.ToString("F2", CultureInfo.InvariantCulture));
            Assert.IsFalse(runner.Skinned.StumbleRecoverActive, "stumble_recover 4 s içinde bitmedi");
            Assert.LessOrEqual(maxRunner, engageRunner, "Runner meşgul bandından çıktı — ölçüm meşgul durumu değil");
            Assert.AreEqual(1, runner.Skinned.TransitionCompletions - done0, "stumble_recover klibi sonuna kadar oynamadı");
            Assert.IsTrue(attach, "attach-out penceresi görülmedi");
            Assert.AreEqual(0, runner.Skinned.TransitionCancels - cancels0, "meşgulken stumble_recover ya da attach-out'u kesildi (gt S1)");
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ============================================================================ ölüm ×4

        [Category("WP018")]
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

        // ======================================================================== WP-071 B4 grip

        [Category("WP018")]
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SolEl_SilahKabzasinda_ReloadtaBirakir_SonraTutar()
        {
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv.GripIk, "WeaponGripIk bağlı değil");
            // WP-082 R3: hero v2 carries its head in the body mesh — four body LOD renderers, no head renderer (FP stays on the pre-hero
            // body); the P-06 rig v002 body had three LODs with a separate head renderer each
            if (pv.Renderers.Length == 4) Assert.AreEqual(0, pv.HeadRenderers.Length, "kahraman v2: baş gövde mesh'inde (4 LOD, ayrı baş renderer'ı yok)");
            else Assert.AreEqual(3, pv.HeadRenderers.Length, "baş her LOD'da ayrı renderer (P-06 rig v002, S-01 FP)");
            yield return CharacterTests.Bekle(() => pv.GripIk.Grip != null && pv.GripIk.Weight >= 0.999f, 3f, "sol el kabzada");
            for (int i = 0; i < 10; i++) yield return null;
            float heldError = pv.GripIk.LastError;
            pv.PlayReload(1.6f);
            int over = -1; for (int i = 0; i < pv.Animator.layerCount; i++) if (pv.Animator.GetLayerName(i) == C.OverrideUpperLayer) over = i;
            for (int i = 0; i < 12; i++) yield return null;
            AnimatorStateInfo st = pv.Animator.GetCurrentAnimatorStateInfo(over);
            Debug.Log("[DD-TEST] wp071.grip.reload over=" + over + " reloadState=" + st.IsName(C.ReloadState + "_0") + " aim0=" + st.IsName(C.AimState + "_0") + " inTransition=" + pv.Animator.IsInTransition(over)
                      + " gate=" + pv.GripIk.GateOpen + " atRest=" + pv.UpperAtRestForTest + " visLayer=" + pv.OverrideLayerIndex + " hash=" + st.shortNameHash + " reloadHash=" + Animator.StringToHash(C.ReloadState + "_0") + " visuals=" + UnityEngine.Object.FindObjectsOfType<PlayerSkinnedVisual>().Length + " weight=" + pv.GripIk.Weight.ToString("F2", CultureInfo.InvariantCulture) + " weaponParam=" + pv.Animator.GetInteger(C.Weapon));
            yield return CharacterTests.Bekle(() => pv.GripIk.Weight <= 0.001f, 1f, "reload'da sol el serbest");
            yield return CharacterTests.Bekle(() => pv.GripIk.Weight >= 0.999f, 4f, "reload sonrası yeniden kabzada");
            Debug.Log("[DD-TEST] wp071.grip target=" + pv.GripIk.Grip.name + " heldError=" + (heldError * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm");
            Assert.LessOrEqual(heldError, 0.03f, "sol el kabzaya ulaşmadı (≤ 3 cm)");
        }

        // ================================================================================ rise

        [Category("WP018")]
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahV3_B4c_TpAcikSetler_SniperSurguDongusu_ArSmgLegacy()
        {
            // WP-071 B4c (C-00 26.09): TP v3 is ON for smg_2 / sr_1 / rl_1 / lmg_1 / lmg_2 in the game (PlayerSkinnedVisual.TpV3Sets) and
            // stays on the legacy pose for ar_1 / ar_2 / smg_1; the sniper's shot starts its bolt cycle (Cycle_V3).
            Assert.IsFalse(PlayerSkinnedVisual.V3WeaponSets, "global TP v3 kapalı (AR/SMG 3,7 cm)");
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            int weaponParam = Animator.StringToHash(C.Weapon);
            foreach (string key in new[] { "smg_2", "sr_1", "rl_1", "lmg_1", "lmg_2", "ar_1", "smg_1" })
            {
                var def = DD.Prototype.Core.Weapons.Arsenal.Get(key);
                pv.EquipWeapon(def, def.id);
                yield return null;
                bool on = System.Array.IndexOf(PlayerSkinnedVisual.TpV3Sets, key) >= 0;
                Assert.AreEqual(on ? key : null, pv.WeaponAnimSet, key + " TP v3 seti");
                Assert.AreEqual(on, pv.Animator.GetInteger(weaponParam) == C.WeaponPoseV3, key + " Weapon parametresi");
            }

            var sr = DD.Prototype.Core.Weapons.Arsenal.Get("sr_1");
            pv.EquipWeapon(sr, sr.id);
            for (int i = 0; i < 40; i++) yield return null;   // switch settles
            int over = pv.Animator.GetLayerIndex(C.OverrideUpperLayer);
            pv.PlayFire();
            yield return CharacterTests.Bekle(() => pv.Animator.GetCurrentAnimatorStateInfo(over).IsName(C.CycleV3State), 1.5f, "sr_1 Cycle_V3");
            AnimatorClipInfo[] ci = pv.Animator.GetCurrentAnimatorClipInfo(over);
            StringAssert.Contains("tp_sr_1_bolt_cycle", ci.Length > 0 ? ci[0].clip.name : "-");

            // WP-071 r2: an LMG string (shots within 1.6 fire intervals) loops fire_loop on the additive layer; it ends after the last shot
            var lmg = DD.Prototype.Core.Weapons.Arsenal.Get("lmg_1");
            pv.EquipWeapon(lmg, lmg.id);
            for (int i = 0; i < 40; i++) yield return null;
            int add = pv.Animator.GetLayerIndex(C.AdditiveLayer);
            for (int shot = 0; shot < 6; shot++)
            {
                pv.PlayFire();   // one shot per frame: the gap is a frame, inside the chain window (1.6 × 60 / rpm) on any machine load
                yield return null;
            }

            Assert.IsTrue(pv.Animator.GetBool(Animator.StringToHash(C.FireHeld)), "LMG dizisi FireHeld");
            yield return CharacterTests.Bekle(() => pv.Animator.GetCurrentAnimatorStateInfo(add).IsName(C.FireLoopV3State), 1f, "lmg_1 FireLoop_V3");
            yield return CharacterTests.Bekle(() => !pv.Animator.GetBool(Animator.StringToHash(C.FireHeld)), 2f, "dizi bitince FireHeld kapanır");
        }

        [Category("WP018")]
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahV3_SinifGecisi_smg2ar2_SpecialV3_swArc_Donus_VarsayilanKapali()
        {
            // WP-071 M-49 / M-48 (C-00 30.09): on the one runtime override controller smg_2 → ar_2 puts the ar class back on the class
            // slots; a Special's TP v3 set (EquipSpecialV3) plays on SwHold 0 through sw_arc and every end path (hold 0) returns to the
            // classic set; with the switch off (the shipped default) the Special keeps the SwHold interim pose.
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsTrue(pv.AnimationSet != null && pv.AnimationSet.WeaponSets.Count > 0, "v3 set yok");
            var smg2 = DD.Prototype.Core.Weapons.Arsenal.Get("smg_2");
            var ar2 = DD.Prototype.Core.Weapons.Arsenal.Get("ar_2");
            Assert.IsFalse(pv.EquipSpecialV3(SpecialWeaponId.Raiju), "varsayılan kapalı: Special v3 seti devreye girmez");
            Assert.IsFalse(pv.SpecialV3Active);
            PlayerSkinnedVisual.V3WeaponSets = true;
            try
            {
                var aoc = pv.Animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.IsNotNull(aoc, "runtime override controller");
                string idle = C.PlayerWeaponClipV3("ar_1", "idle_aim");
                pv.EquipWeapon(smg2, smg2.id);
                yield return null;
                StringAssert.Contains("tp_smg", aoc[idle].name, "smg_2 → smg sınıf dinlenmesi");
                pv.EquipWeapon(ar2, ar2.id);
                yield return null;
                StringAssert.Contains("tp_ar_idle_aim", aoc[idle].name, "smg_2 → ar_2: sınıf yuvası ar'a döner (M-49)");

                Assert.IsTrue(pv.EquipSpecialV3(SpecialWeaponId.Raiju), "Raiju v3 seti");
                yield return null;
                Assert.AreEqual("sw_arc", pv.WeaponAnimSet);
                Assert.AreEqual(0, pv.SpecialHold, "v3 Special SwHold 0'da");
                StringAssert.Contains("tp_sw_idle_aim", aoc[idle].name, "sw_arc → AP022_tp_sw_idle_aim");
                pv.SetSpecialHold(1);   // the HUD-state path's hold index while the v3 Special is out
                Assert.AreEqual(0, pv.SpecialHold, "v3 Special açıkken ara SwHold yok sayılır");
                Assert.AreEqual("sw_arc", pv.WeaponAnimSet);
                pv.SetSpecialHold(0);   // Deactivated / Ended / Cleared / Phase None
                yield return null;
                Assert.IsFalse(pv.SpecialV3Active);
                Assert.AreEqual("ar_2", pv.WeaponAnimSet, "bitişte klasik sete dönüş");
                StringAssert.Contains("tp_ar_idle_aim", aoc[idle].name, "klasik dinlenme geri");
            }
            finally { PlayerSkinnedVisual.V3WeaponSets = false; }
        }

        [Category("WP018")]
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahV3_AOC_ar1ar2_TacEmpty_BurstTekVurus_LocomotionSifirlanmaz()
        {
            // WP-071 B4b (K8): the AR pair on the generic v3 states through one runtime override controller — equip swaps only the
            // weapon slots (no rebind: the base layer keeps its state and clock), reload picks tac / empty from the duration, the
            // burst weapon's fire clip starts once per burst.
            yield return RunAP005();
            PlayerSkinnedVisual.V3WeaponSets = true;
            try
            {
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            if (pv.AnimationSet == null || pv.AnimationSet.WeaponSets.Count == 0) Assert.Ignore("AP-022 slice 4a (AR, 30 fps) henüz içe alınmadı — v3 set yok");
            var ar1 = DD.Prototype.Core.Weapons.Arsenal.Get("ar_1");
            var ar2 = DD.Prototype.Core.Weapons.Arsenal.Get("ar_2");
            Assert.IsNotNull(ar1); Assert.IsNotNull(ar2);
            int over = -1; for (int i = 0; i < pv.Animator.layerCount; i++) if (pv.Animator.GetLayerName(i) == C.OverrideUpperLayer) over = i;
            pv.EquipWeapon(ar1, ar1.id);
            Assert.AreEqual("ar_1", pv.WeaponAnimSet);
            Assert.AreEqual(C.WeaponPoseV3, pv.Weapon);
            yield return CharacterTests.Bekle(() => pv.Animator.GetCurrentAnimatorStateInfo(over).IsName(C.AimV3State), 2f, "Aim_V3");
            for (int i = 0; i < 20; i++) yield return null;
            AnimatorStateInfo b0 = pv.Animator.GetCurrentAnimatorStateInfo(0);
            pv.EquipWeapon(ar2, ar2.id);
            yield return null;
            AnimatorStateInfo b1 = pv.Animator.GetCurrentAnimatorStateInfo(0);
            Assert.AreEqual("ar_2", pv.WeaponAnimSet);
            Assert.AreEqual(b0.fullPathHash, b1.fullPathHash, "AOC değişimi base katmanı sıfırlamamalı (durum)");
            Assert.Greater(b1.normalizedTime, b0.normalizedTime, "AOC değişimi base saatini sıfırlamamalı");
            Assert.IsTrue(pv.Animator.GetCurrentAnimatorStateInfo(over).IsName(C.AimV3State), "üst katman Aim_V3'te kalır");

            string[] seen = new string[2];
            float[] seconds = { ar2.ammo.reloadTacSeconds, ar2.ammo.reloadEmptySeconds };
            string[] states = { C.ReloadTacState, C.ReloadEmptyState };
            for (int k = 0; k < 2; k++)
            {
                pv.PlayReload(seconds[k]);
                yield return CharacterTests.Bekle(() => pv.Animator.GetCurrentAnimatorStateInfo(over).IsName(states[k]), 1f, states[k]);
                AnimatorClipInfo[] ci = pv.Animator.GetCurrentAnimatorClipInfo(over);
                seen[k] = ci.Length > 0 ? ci[0].clip.name : "-";
                pv.CancelReload();
                yield return CharacterTests.Bekle(() => pv.Animator.GetCurrentAnimatorStateInfo(over).IsName(C.AimV3State), 1f, "reload iptal → Aim_V3");
            }

            StringAssert.Contains("ar_2_reload_tac", seen[0], "tac süresi → ar_2 reload_tac (AOC)");
            StringAssert.Contains("ar_2_reload_empty", seen[1], "empty süresi → ar_2 reload_empty (AOC)");

            // WP-083 (05.10): fixed 1/60 s frames — the burst window is counted in game time, and a loaded batch machine's long frame let
            // a real-time wait run past it (b2f8: 2 fire beats in one burst; ders-batch-kare-suresi)
            Time.captureDeltaTime = 1f / 60f;
            int f0 = pv.FireTriggers;
            for (int i = 0; i < 3; i++) { pv.PlayFire(); yield return new WaitForSeconds(ar2.fire.burstIntervalSeconds); }
            int burst = pv.FireTriggers - f0;
            yield return new WaitForSeconds(ar2.fire.burstDelaySeconds + 0.1f);
            pv.PlayFire();
            int second = pv.FireTriggers - f0 - burst;
            pv.EquipWeapon(ar1, ar1.id);
            int a0 = pv.FireTriggers;
            for (int i = 0; i < 3; i++) { pv.PlayFire(); yield return new WaitForSeconds(0.05f); }
            int auto = pv.FireTriggers - a0;
            Debug.Log("[DD-TEST] wp071.b4b tac=" + seen[0] + " empty=" + seen[1] + " burstFireBeats=" + burst + "+" + second + " autoBeats=" + auto
                      + " baseT " + b0.normalizedTime.ToString("F3", CultureInfo.InvariantCulture) + "→" + b1.normalizedTime.ToString("F3", CultureInfo.InvariantCulture));
            Assert.AreEqual(1, burst, "ar_2 burst: 3 mermi → 1 fire klibi");
            Assert.AreEqual(1, second, "sonraki burst yeniden başlar");
            Assert.AreEqual(3, auto, "ar_1 auto: mermi başına kick");
            // smg_1: another class — its AOC also swaps the class slots (idle_aim / switch) over the ar base
            var smg1 = DD.Prototype.Core.Weapons.Arsenal.Get("smg_1");
            Assert.IsNotNull(smg1);
            pv.EquipWeapon(smg1, smg1.id);
            Assert.AreEqual("smg_1", pv.WeaponAnimSet);
            yield return CharacterTests.Bekle(() => pv.Animator.GetCurrentAnimatorStateInfo(over).IsName(C.AimV3State), 1f, "smg Aim_V3");
            for (int i = 0; i < 5; i++) yield return null;
            AnimatorClipInfo[] sci = pv.Animator.GetCurrentAnimatorClipInfo(over);
            StringAssert.Contains("tp_smg_idle_aim", sci.Length > 0 ? sci[0].clip.name : "-", "smg_1 AOC sınıf slotunu da değiştirir");
            }
            finally { PlayerSkinnedVisual.V3WeaponSets = false; }
        }

        [Category("WP018")]
        [UnityTest]
        [Category("FPTP")]   // R3 (C-00 07.10): first person only — out of scope (user 06.10: top-down only); main-ci runs !FPTP
        [Timeout(300000)]
        public IEnumerator FpKollar_Kapaliyken_FpdeGizli_SilahFpViewda()
        {
            // C-00 26.09: with the FP arms switch off (the release stop-gap afabd44f) no arms copy shows in first person and the weapon keeps
            // S-02's fpView (the FirstPersonWeaponSocket hook stays empty). Independent of the shipped default (PlayerSkinnedVisual.FirstPersonV3Arms).
            ArmsForTest(false);
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            var ar1 = DD.Prototype.Core.Weapons.Arsenal.Get("ar_1");
            pv.EquipWeapon(ar1, ar1.id);
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            try
            {
                for (int i = 0; i < 20; i++) yield return null;
                Assert.IsTrue(pv.FirstPersonArms == null || !pv.FirstPersonArms.Visible, "FP kol kopyası görünür");
                Assert.IsNull(FirstPersonArms.Shown, "FP kolları gösteriliyor");
                Assert.IsNull(DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket, "kanca boş olmalı (silah fpView'da)");
            }
            finally { DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true); }
        }

        [Category("WP018")]
        [UnityTest]
        [Category("FPTP")]   // R3 (C-00 07.10): first person only — out of scope (user 06.10: top-down only); main-ci runs !FPTP
        [Timeout(300000)]
        public IEnumerator FpKollar_Viewmodel_fpCameraCapada_AtesReloadGecis_TPyeDonuste_Gizli()
        {
            // WP-071 B4b-3: in first person the FP arms copy sits on the viewmodel layer with fp_camera on the anchor, plays the
            // FP twins (fire additive, tac / empty reload, switch), shows only the LOD0 body (no head), and hides back in TP.
            ArmsForTest(true);   // the feature under test, whatever the shipped default
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv.FirstPersonArms, "FirstPersonArms bağlı değil");
            var ar1 = DD.Prototype.Core.Weapons.Arsenal.Get("ar_1");
            var smg1 = DD.Prototype.Core.Weapons.Arsenal.Get("smg_1");
            pv.EquipWeapon(ar1, ar1.id);
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            try
            {
                FirstPersonArms fp = pv.FirstPersonArms;
                yield return CharacterTests.Bekle(() => fp.Visible, 2f, "FP kolları görünür");
                for (int i = 0; i < 10; i++) yield return null;
                Transform anchor = DD.Prototype.CameraSystem.CameraView.ViewmodelAnchor;
                Assert.IsNotNull(anchor);
                Assert.Less(fp.LastAnchorError, 0.001f, "fp_camera çapada (m)");
                Assert.AreEqual(DD.Prototype.CameraSystem.CameraLayers.FirstPersonView, fp.Animator.gameObject.layer, "FirstPersonView katmanı");
                int bodies = 0;
                foreach (SkinnedMeshRenderer r in fp.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!r.enabled) continue;
                    bodies++;
                    StringAssert.DoesNotContain("Head", r.name, "baş FP'de çizilmez");
                    StringAssert.Contains("LOD0", r.name, "yalnız LOD0 gövde");
                }

                Assert.AreEqual(1, bodies, "tek gövde renderer");
                Assert.AreSame(fp, FirstPersonArms.Shown);
                yield return CharacterTests.Bekle(() => fp.FrameCalibrated, 5f, "FP el → silah çerçevesi dinlenmede kalibre");
                yield return null;
                Assert.AreSame(fp.WeaponFrame, DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket, "S-02 kancası FP silah çerçevesi");
                // WP-082 / WP-083 B2b: the FP left hand carries the same magazine socket as the third-person body
                Assert.IsNotNull(fp.MagazineSocket, "FP socket_mag_l yok");
                Assert.AreSame(fp.Animator.GetBoneTransform(HumanBodyBones.LeftHand), fp.MagazineSocket.parent, "FP socket_mag_l sol elin çocuğu");
                for (int i = 0; i < 12; i++) yield return null;   // the hand IK blends in (0.1 s)
                Camera vmCam = anchor.GetComponentInParent<Camera>();
                Vector3 rhView = vmCam != null ? vmCam.WorldToViewportPoint(fp.Animator.GetBoneTransform(HumanBodyBones.RightHand).position) : Vector3.zero;
                Debug.Log("[DD-TEST] wp071.fpik rightErr=" + (fp.LastRightError * 100f).ToString("F2", CultureInfo.InvariantCulture) + " cm leftErr=" + (fp.LastLeftError * 100f).ToString("F2", CultureInfo.InvariantCulture)
                          + " cm rightHandViewport=" + rhView.ToString("F2"));
                Assert.Less(fp.LastRightError, 0.01f, "FP sağ el kabzada (≤ 1 cm)");
                Assert.Less(fp.LastLeftError, 0.01f, "FP sol el ön el korumasında (≤ 1 cm)");
                // every FP set on its fpView (S-02 v4): the IK residual per set at rest — S-02 reshapes a pose the arms cannot reach
                var sb = new System.Text.StringBuilder("[DD-TEST] wp071.fpikSets");
                foreach (var def in new[] { DD.Prototype.Core.Weapons.Arsenal.Get("ar_2"), smg1, ar1 })
                {
                    // C-00 30.09 option C (fpArms gate e0c0755b): a weapon with presentation.fpArms off stays on the fpView pose
                    // without arms (ar_2 / lmg_* / rl_1) — the arms expectation holds only where fpArms is on (H5: rule change)
                    if (!def.FpArms) { sb.Append(' ').Append(def.AnimSet).Append(" fpArms=0 (kolsuz)"); continue; }
                    pv.EquipWeapon(def, def.id);
                    yield return CharacterTests.Bekle(() => fp.FrameCalibrated, 5f, def.AnimSet + " kalibre");
                    for (int i = 0; i < 12; i++) yield return null;
                    Vector3 v = vmCam != null ? vmCam.WorldToViewportPoint(fp.Animator.GetBoneTransform(HumanBodyBones.RightHand).position) : Vector3.zero;
                    sb.Append(' ').Append(def.AnimSet).Append(" r=").Append((fp.LastRightError * 100f).ToString("F2", CultureInfo.InvariantCulture))
                      .Append(" l=").Append((fp.LastLeftError * 100f).ToString("F2", CultureInfo.InvariantCulture)).Append("cm rhView=").Append(v.ToString("F2"));
                    Debug.Log(sb.ToString());
                    Assert.Less(fp.LastRightError, 0.01f, def.AnimSet + ": sağ el kabzada (≤ 1 cm)");
                    Assert.Less(fp.LastLeftError, 0.03f, def.AnimSet + ": sol el hedefte (≤ 3 cm, S-02 kapısı)");
                }

                Debug.Log(sb.ToString());
                {
                    Transform rhB = fp.Animator.GetBoneTransform(HumanBodyBones.RightHand), lhB = fp.Animator.GetBoneTransform(HumanBodyBones.LeftHand), hdB = fp.Animator.GetBoneTransform(HumanBodyBones.Head);
                    Debug.Log("[DD-TEST] wp071.fphands RH=" + anchor.InverseTransformPoint(rhB.position).ToString("F3") + " LH=" + anchor.InverseTransformPoint(lhB.position).ToString("F3")
                              + " Head=" + anchor.InverseTransformPoint(hdB.position).ToString("F3") + " fpCam=" + anchor.InverseTransformPoint(fp.FpCamera.position).ToString("F3") + " rigScale=" + fp.RigScale.ToString("F3")
                              + " rootInAnchor=" + anchor.InverseTransformPoint(fp.Animator.transform.position).ToString("F3") + " rootFwd=" + anchor.InverseTransformDirection(fp.Animator.transform.forward).ToString("F2")
                              + " bodyFwd=" + anchor.InverseTransformDirection(fp.Animator.bodyRotation * Vector3.forward).ToString("F2"));
                }


                // S-02 hand-off measurement (fpView band): the weapon root the FP rig implies = frame − R·gripOffset, in anchor space
                Vector3 rootWorld = fp.WeaponFrame.position - fp.WeaponFrame.rotation * (ar1.GripOffset * ar1.FpViewPose.scale);   // S-02's rule
                Vector3 rootInAnchor = anchor.InverseTransformPoint(rootWorld);
                var pose = ar1.FpViewPose;
                float posErr = Vector3.Distance(rootInAnchor, pose.position);
                float rotErr = Quaternion.Angle(Quaternion.Inverse(anchor.rotation) * fp.WeaponFrame.rotation, Quaternion.Euler(pose.euler));
                int fpBase = 0, fpAdd = 1;
                pv.PlayFire();
                yield return CharacterTests.Bekle(() => fp.Animator.GetCurrentAnimatorStateInfo(fpAdd).IsName(C.FireV3State), 1f, "FP Fire_V3");
                Vector3 frameRest = anchor.InverseTransformPoint(fp.WeaponFrame.position);
                pv.PlayReload(ar1.ammo.reloadEmptySeconds, true);
                yield return CharacterTests.Bekle(() => fp.Animator.GetCurrentAnimatorStateInfo(fpBase).IsName(C.ReloadEmptyState), 1f, "FP ReloadEmpty");
                float frameTravel = 0f;
                for (int i = 0; i < 40; i++) { yield return null; frameTravel = Mathf.Max(frameTravel, Vector3.Distance(frameRest, anchor.InverseTransformPoint(fp.WeaponFrame.position))); }
                Assert.Greater(frameTravel, 0.02f, "FP silah çerçevesi reload'da elle hareket eder");
                AnimatorClipInfo[] ci = fp.Animator.GetCurrentAnimatorClipInfo(fpBase);
                StringAssert.Contains("fp_ar_1_reload_empty", ci.Length > 0 ? ci[0].clip.name : "-");
                pv.CancelReload();
                pv.EquipWeapon(smg1, smg1.id);
                Assert.AreEqual("smg_1", fp.WeaponSet);
                pv.PlaySwitchIn();
                yield return CharacterTests.Bekle(() => fp.Animator.GetCurrentAnimatorStateInfo(fpBase).IsName(C.SwitchInV3State), 1f, "FP SwitchIn_V3");
                AnimatorClipInfo[] si = fp.Animator.GetCurrentAnimatorClipInfo(fpBase);
                Debug.Log("[DD-TEST] wp071.fp anchorErr=" + (fp.LastAnchorError * 1000f).ToString("F2", CultureInfo.InvariantCulture) + " mm ar1 fpView root Δ=" + (posErr * 100f).ToString("F1", CultureInfo.InvariantCulture)
                          + " cm rotΔ=" + rotErr.ToString("F1", CultureInfo.InvariantCulture) + "° reloadFrameTravel=" + (frameTravel * 100f).ToString("F1", CultureInfo.InvariantCulture) + " cm (pose " + pose.position.ToString("F3") + " / rig " + rootInAnchor.ToString("F3") + ", scale " + pose.scale.ToString("F2", CultureInfo.InvariantCulture) + ") smgSwitch=" + (si.Length > 0 ? si[0].clip.name : "-"));
                StringAssert.Contains("fp_smg_1_switch_in", si.Length > 0 ? si[0].clip.name : "-", "smg_1 FP AOC");
                // C-00 25.09: fpView owns FP placement — P-06's 5b rest lands the weapon on it (W13 ≤ 1.9 mm / 0.23°)
                Assert.Less(posErr, 0.01f, "FP dinlenmede silah kökü fpView'da (≤ 1 cm)");
                Assert.Less(rotErr, 1.5f, "FP dinlenmede silah dönüşü fpView'da (≤ 1,5°)");
            }
            finally
            {
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
            }

            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsFalse(pv.FirstPersonArms.Visible, "TP'de FP kolları gizli");
            Assert.IsNull(DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket, "TP'de kanca boş (fpView yedeği)");
            Assert.IsNull(FirstPersonArms.Shown);
        }

        // R2 H4a (C-00 05.10: split from H4, thresholds unchanged) — the source name the acceptance chain runs this test by.
        private const string H4aName = "DD.Art.Tests.PlayMode.AnimationTests.FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta";

        [Category("WP018")]
        [UnityTest]
        [Category("FPTP")]   // R3 (C-00 07.10): first person only — out of scope (user 06.10: top-down only); main-ci runs !FPTP
        [Timeout(300000)]
        public IEnumerator FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta()
        {
            // C-00 25.09 acceptance for the FP arms, per armed weapon at rest: arm pixels ≤ 10 % of 1080p, the arm geometry ≥ 15 cm from
            // the camera, both hands on the weapon ≤ 1 cm, rest on fpView ≤ 0.3 cm; a frame per weapon (FP layer, arms + weapon).
            // R2 H4a writes two frame counts over those frames: armOffscreenFrames (a hand outside the viewport, or no arm pixel
            // drawn) and armInCameraFrames (arm geometry inside the 15 cm camera clearance) — gate == 0 each. Measured with the FP
            // arms on (ArmsForTest), the only mode they show in; the default path keeps them off (C-00 06.10: the gate is the arms-on mode's).
            ArmsForTest(true);
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            FirstPersonArms fp = pv.FirstPersonArms;
            Assert.IsNotNull(fp);
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            var report = new System.Text.StringBuilder("[DD-TEST] wp071.fpframe");
            var wrong = new List<string>();
            var noSource = new List<string>();
            int frames = 0, offscreenFrames = 0, inCameraFrames = 0;
            try
            {
                foreach (string key in new[] { "ar_1", "ar_2", "smg_1", "smg_2" })
                {
                    var def = DD.Prototype.Core.Weapons.Arsenal.Get(key);
                    // C-00 30.09 option C (fpArms gate e0c0755b): fpArms off → the fpView pose without arms; only armed weapons here (H5)
                    if (!def.FpArms) { Debug.Log("[DD-TEST] wp071.fparms skip " + key + " fpArms=0 (kolsuz fpView)"); continue; }
                    pv.EquipWeapon(def, def.id);
                    yield return CharacterTests.Bekle(() => fp.Visible && fp.FrameCalibrated, 5f, key + " kollar");
                    for (int i = 0; i < 15; i++) yield return null;   // the hand IK blends in (0.1 s)
                    Transform anchor = DD.Prototype.CameraSystem.CameraView.ViewmodelAnchor;
                    Camera vm = anchor.GetComponentInParent<Camera>();
                    var pose = def.FpViewPose;
                    Vector3 root = fp.WeaponFrame.position - fp.WeaponFrame.rotation * (def.GripOffset * pose.scale);
                    float rest = Vector3.Distance(anchor.InverseTransformPoint(root), pose.position);
                    // arms-only pixels: a temporary base camera at the overlay camera's pose, FP layer, weapon renderers hidden
                    int armPx = RenderFp(vm, key, fp.Animator.transform, hideWeapon: true, save: false);
                    int fpPx = RenderFp(vm, key, fp.Animator.transform, hideWeapon: false, save: true);
                    // the arm geometry's clearance from the camera (baked skinned vertices)
                    float minDist = float.MaxValue, minFront = float.MaxValue;   // minFront: vertices in front of the lens only
                    int armVerts = 0;
                    foreach (SkinnedMeshRenderer smr in fp.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if (!smr.enabled) continue;
                        var baked = new Mesh();
                        smr.BakeMesh(baked, true);
                        Matrix4x4 m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        armVerts += baked.vertexCount;
                        foreach (Vector3 v in baked.vertices)
                        {
                            Vector3 cv = vm.transform.InverseTransformPoint(m.MultiplyPoint3x4(v));
                            minDist = Mathf.Min(minDist, cv.magnitude);
                            if (cv.z > 0f) minFront = Mathf.Min(minFront, cv.magnitude);
                        }
                        UnityEngine.Object.Destroy(baked);
                    }

                    Vector3 rh = vm.WorldToViewportPoint(fp.Animator.GetBoneTransform(HumanBodyBones.RightHand).position), lh = vm.WorldToViewportPoint(fp.Animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
                    bool rIn = rh.x > 0f && rh.x < 1f && rh.y > 0f && rh.y < 1f, lIn = lh.x > 0f && lh.x < 1f && lh.y > 0f && lh.y < 1f, handsIn = rIn && lIn;
                    // the elbows off screen with a margin (C-00 (1): the forearm enters from the edge; the v2 sleeve end sits above the elbow)
                    Vector3 le = vm.WorldToViewportPoint(fp.Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position), re = vm.WorldToViewportPoint(fp.Animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position);
                    const float margin = 40f / 1080f;
                    bool elbowsOff = (le.y < -margin || le.x < -margin || le.x > 1f + margin) && (re.y < -margin || re.x < -margin || re.x > 1f + margin);
                    float share = armPx / (1920f * 1080f) * 100f;
                    string line = key + " armPx=" + share.ToString("F1", CultureInfo.InvariantCulture) + "% minCam=" + (minDist * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm r=" + (fp.LastRightError * 100f).ToString("F2", CultureInfo.InvariantCulture)
                                  + " l=" + (fp.LastLeftError * 100f).ToString("F2", CultureInfo.InvariantCulture) + "cm rest=" + (rest * 100f).ToString("F2", CultureInfo.InvariantCulture) + "cm hands=" + (handsIn ? "in" : "OUT" + (rIn ? "" : "(R)") + (lIn ? "" : "(L)")) + " rhPx=(" + (rh.x * 1920f).ToString("F0", CultureInfo.InvariantCulture) + "," + ((1f - rh.y) * 1080f).ToString("F0", CultureInfo.InvariantCulture)
                                  + ") lhPx=(" + (lh.x * 1920f).ToString("F0", CultureInfo.InvariantCulture) + "," + ((1f - lh.y) * 1080f).ToString("F0", CultureInfo.InvariantCulture) + ") minFront=" + (minFront * 100f).ToString("F1", CultureInfo.InvariantCulture) + "cm elbowL=("
                                  + (le.x * 1920f).ToString("F0", CultureInfo.InvariantCulture) + "," + ((1f - le.y) * 1080f).ToString("F0", CultureInfo.InvariantCulture) + ") elbowR=(" + (re.x * 1920f).ToString("F0", CultureInfo.InvariantCulture) + "," + ((1f - re.y) * 1080f).ToString("F0", CultureInfo.InvariantCulture) + ")" + (elbowsOff ? "" : " ELBOW-ON-SCREEN");
                    report.Append(" | ").Append(line);
                    if (share > 10f || minDist < 0.15f || fp.LastRightError > 0.01f || fp.LastLeftError > 0.01f || rest > 0.003f || !handsIn || !elbowsOff) wrong.Add(line);
                    // H4a source (R2 standard): the arms copy shown and skinned, the FP layer frame actually rendered
                    if (!fp.Visible || !ReferenceEquals(FirstPersonArms.Shown, fp) || armVerts == 0 || fpPx == 0)
                        noSource.Add(key + " visible=" + fp.Visible + " shown=" + ReferenceEquals(FirstPersonArms.Shown, fp) + " armVerts=" + armVerts + " fpPx=" + fpPx);
                    frames++;
                    if (!handsIn || armPx == 0) offscreenFrames++;
                    if (minDist < 0.15f) inCameraFrames++;
                }
            }
            finally { DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true); }

            Debug.Log(report.ToString());
            Debug.Log("[DD-TEST] wp083.h4a frames=" + frames + " armOffscreenFrames=" + offscreenFrames + " armInCameraFrames=" + inCameraFrames);
            // the source is in the record before the artefact (S-10 R2 standard): armed weapons measured, the arms drawn on the FP layer
            Assert.Greater(frames, 0, "kaynak yok: kollu silahta ölçülen kare yok");
            Assert.IsEmpty(noSource, "kaynak yok: FP kolları gösterilmiyor / skinned gövde yok / FP katmanı çizilmedi");
            R2Olcum.Yaz("H4a", H4aName, new Dictionary<string, double> { { "armOffscreenFrames", offscreenFrames }, { "armInCameraFrames", inCameraFrames } }, frames);
            Assert.AreEqual(0, offscreenFrames, "R2 H4a: FP kolu kadraj dışında (el ekran dışında ya da kol pikseli yok)\n" + report);
            Assert.AreEqual(0, inCameraFrames, "R2 H4a: FP kol geometrisi kameraya 15 cm'den yakın\n" + report);
            Assert.IsEmpty(wrong, "FP kol kabulü (C-00: kol ≤ %10, kamera ≥ 15 cm, el ≤ 1 cm, dinlenme ≤ 0,3 cm, eller ekranda, dirsekler ekran dışında)");
        }

        [Category("WP018")]
        [UnityTest]
        [Category("FPTP")]   // R3 (C-00 07.10): first person only — out of scope (user 06.10: top-down only); main-ci runs !FPTP
        [Timeout(300000)]
        public IEnumerator FpKollar_V6Kareleri_4Silah()
        {
            // C-00 25.09: FP frames with the arms for the visual sign-off — the default world (V6), the full camera stack (world +
            // viewmodel overlay) at 1080p with the HUD on (the screen-space canvases drawn by a second camera into the same frame),
            // one per weapon at rest.
            ArmsForTest(true);
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "V6");
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
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded, SahneZamanAsimi, "World V6");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            FirstPersonArms fp = pv.FirstPersonArms;
            DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.FirstPerson, instant: true);
            var saved = new List<string>();
            var adsLines = new List<string>();
            var adsWrong = new List<string>();
            try
            {
                foreach (string key in new[] { "ar_1", "ar_2", "smg_1", "smg_2" })
                {
                    var def = DD.Prototype.Core.Weapons.Arsenal.Get(key);
                    // C-00 30.09 option C (fpArms gate e0c0755b): fpArms off → the fpView pose without arms; only armed weapons here (H5)
                    if (!def.FpArms) { Debug.Log("[DD-TEST] wp071.fparms skip " + key + " fpArms=0 (kolsuz fpView)"); continue; }
                    pv.EquipWeapon(def, def.id);
                    yield return CharacterTests.Bekle(() => fp.Visible && fp.FrameCalibrated, 5f, key + " kollar");
                    for (int i = 0; i < 20; i++) yield return null;
                    saved.Add(CaptureV6("wp071-v6-" + key));
                    fp.SetAds(true);   // the ADS frame: the arms follow S-02's ADS view pose (sight_ads on the anchor) at weight 1
                    for (int i = 0; i < 25; i++) yield return null;
                    saved.Add(CaptureV6("wp071-v6ads-" + key));
                    adsLines.Add(key + " ads r=" + (fp.LastRightError * 100f).ToString("F2", CultureInfo.InvariantCulture) + " l=" + (fp.LastLeftError * 100f).ToString("F2", CultureInfo.InvariantCulture) + "cm");
                    if (!(fp.LastRightError <= 0.01f) || !(fp.LastLeftError <= 0.01f)) adsWrong.Add(adsLines[adsLines.Count - 1]);
                    fp.SetAds(false);
                    for (int i = 0; i < 15; i++) yield return null;
                }
            }
            finally
            {
                DD.Prototype.CameraSystem.CameraModeService.Request(DD.Prototype.CameraSystem.CameraMode.ThirdPerson, instant: true);
                Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            }

            Debug.Log("[DD-TEST] wp071.v6frames " + string.Join(" ", saved));
            Debug.Log("[DD-TEST] wp071.ads " + string.Join(" | ", adsLines));
            Assert.AreEqual(6, saved.Count, "kollu 3 silah × (kare + ADS) — ar_2 fpArms=0 (C-00 30.09 kapı)");
            Assert.IsEmpty(adsWrong, "ADS'de eller silahta ≤ 1 cm");
        }

        // The full camera stack (world + viewmodel overlay) at 1080p with the HUD composited over it; saved to the temp cache.
        private static string CaptureV6(string name)
        {
            Camera cam = Camera.main;
            Assert.IsNotNull(cam, "ana kamera");
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();   // a URP base camera renders its stack (the viewmodel overlay) into its target
            cam.targetTexture = prevTarget;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            RenderTexture.active = prev;
            Color32[] world = tex.GetPixels32(), hud = RenderHud(1920, 1080);
            for (int i = 0; i < world.Length && hud != null; i++)
            {
                // UI blends SrcAlpha / OneMinusSrcAlpha over a clear (0,0,0,0) target: rgb = c·a, alpha = a² → composite over the frame
                float a = Mathf.Sqrt(hud[i].a / 255f);
                world[i] = new Color32((byte)Mathf.Min(255f, hud[i].r + world[i].r * (1f - a)), (byte)Mathf.Min(255f, hud[i].g + world[i].g * (1f - a)), (byte)Mathf.Min(255f, hud[i].b + world[i].b * (1f - a)), 255);
            }

            tex.SetPixels32(world);
            tex.Apply();
            string path = System.IO.Path.Combine(Application.temporaryCachePath, name + ".png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            return path;
        }

        // The HUD alone: every active screen-space-overlay root canvas is drawn, for this one render, by a camera into a transparent
        // 1080p target (the canvases lay out at its size), then restored; the caller composites it over the frame.
        private static Color32[] RenderHud(int w, int h)
        {
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("[WP071-HudCapture]");
            go.transform.position = new Vector3(0f, -5000f, 0f);   // nothing of the world near it
            Camera ui = go.AddComponent<Camera>();
            ui.enabled = false;
            ui.clearFlags = CameraClearFlags.SolidColor;
            ui.backgroundColor = new Color(0f, 0f, 0f, 0f);
            ui.orthographic = true;
            ui.nearClipPlane = 0.01f; ui.farClipPlane = 10f;
            ui.targetTexture = target;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(ui);
            data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
            data.renderPostProcessing = false;
            var switched = new List<Canvas>();
            int mask = 0;
            foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (!c.isRootCanvas || !c.enabled || !c.gameObject.activeInHierarchy || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = ui;
                c.planeDistance = 1f;
                mask |= 1 << c.gameObject.layer;
                switched.Add(c);
            }

            ui.cullingMask = mask;
            Canvas.ForceUpdateCanvases();
            ui.Render();
            foreach (Canvas c in switched) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            Color32[] px = read.GetPixels32();
            UnityEngine.Object.Destroy(read);
            ui.targetTexture = null;
            UnityEngine.Object.Destroy(go);
            target.Release();
            UnityEngine.Object.Destroy(target);
            Debug.Log("[DD-TEST] wp071.hudcapture canvases=" + switched.Count);
            return px;
        }

        // The viewmodel layer alone with the viewmodel camera's own pose and projection (CameraViewmodelHudPlayTests pattern), over
        // black and over white: a pixel that differs from the background in either pass is covered. hideWeapon: every FP-layer renderer
        // outside the arms rig is off for the pass (the weapon), so the count is the arms'.
        private static int RenderFp(Camera vm, string key, Transform arms, bool hideWeapon, bool save)
        {
            const int W = 1920, H = 1080, Tol = 6;
            var hidden = new List<Renderer>();
            if (hideWeapon)
                foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                    if (r.enabled && r.gameObject.layer == DD.Prototype.CameraSystem.CameraLayers.FirstPersonView && !r.transform.IsChildOf(arms)) { r.enabled = false; hidden.Add(r); }
            var target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("[WP071-FpProbe]");
            go.transform.SetPositionAndRotation(vm.transform.position, vm.transform.rotation);
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = target;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 1 << DD.Prototype.CameraSystem.CameraLayers.FirstPersonView;
            cam.nearClipPlane = vm.nearClipPlane;
            cam.farClipPlane = vm.farClipPlane;
            cam.aspect = (float)W / H;
            cam.projectionMatrix = vm.projectionMatrix;
            Color32[] black = FpPass(cam, target, Color.black, W, H), white = FpPass(cam, target, Color.white, W, H);
            foreach (Renderer r in hidden) r.enabled = true;
            int n = 0;
            for (int i = 0; i < black.Length; i++)
            {
                Color32 a = black[i], b = white[i];
                if (a.r > Tol || a.g > Tol || a.b > Tol || b.r < 255 - Tol || b.g < 255 - Tol || b.b < 255 - Tol) n++;
            }

            if (save)
            {
                var png = new Texture2D(W, H, TextureFormat.RGBA32, false);
                png.SetPixels32(black); png.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath, "wp071-fp-" + key + ".png"), png.EncodeToPNG());
                UnityEngine.Object.Destroy(png);
            }

            cam.targetTexture = null;
            UnityEngine.Object.Destroy(go);
            target.Release();
            UnityEngine.Object.Destroy(target);
            return n;
        }

        private static Color32[] FpPass(Camera cam, RenderTexture target, Color bg, int w, int h)
        {
            cam.backgroundColor = bg;
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color32[] px = read.GetPixels32();
            UnityEngine.Object.Destroy(read);
            return px;
        }

        [Category("WP018")]
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Twist_RigV002_SurucuBurulmasi_AgirlikKadarDagitilir()
        {
            // WP-071 · AP-022 rig v002: the eight twist bones follow their driver's twist × P-06's weight (−0.5 / +0.4 / +0.8 / −0.5).
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            TwistCorrector tc = pv.Animator.GetComponent<TwistCorrector>();
            Assert.IsNotNull(tc, "TwistCorrector");
            Assert.AreEqual(8, tc.LinkCount);
            for (int f = 0; f < 30; f++) yield return null;
            for (int i = 0; i < tc.LinkCount; i++)
            {
                float a = tc.AngleOf(i);
                Assert.IsFalse(float.IsNaN(a) || Mathf.Abs(a) > 90f, tc.BoneOf(i).name + " oyunda makul burulma: " + a);
            }

            // Same frame, no yield: the grip IK / mount alignment also write these arms in LateUpdate.
            var sb = new System.Text.StringBuilder("[DD-TEST] wp071.twist");
            for (int i = 0; i < tc.LinkCount; i++)
            {
                tc.PoseDriverForTest(i, 60f);
                tc.Apply();
                sb.Append(' ').Append(tc.BoneOf(i).name).Append('=').Append(tc.AngleOf(i).ToString("F1", CultureInfo.InvariantCulture));
                Assert.AreEqual(tc.WeightOf(i) * 60f, tc.AngleOf(i), 0.5f, tc.BoneOf(i).name + " ağırlık × 60°");
            }

            Debug.Log(sb.ToString());
        }

        [Category("WP018")]
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

        [Category("WP018")]
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Oyuncu_2DBlend_Yonler_Nisan_Ates_Hit_Pickup_Olum()
        {
            // WP-082 R3: "standing" — on the AP-026 8-way tree MoveX / MoveY stay the unit direction, LocoSpeed (m/s) goes to 0
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
            yield return CharacterTests.Bekle(() => Durgun(pv), 2f, "idle");

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
            // WP-071 MUST-3 (C-00 01.10): standing, the base layer is in Idle_Breath (the idle breath's own state) — "alone" = unchanged
            int baseBefore = an.GetCurrentAnimatorStateInfo(0).shortNameHash;
            // C-00 05.10 (main-ci 285b7fe8): the one-shot checks count frames — fixed 1/60 s frames, so a loaded CI machine's long frames
            // cannot play the short recoil / hit through before the state is read (ders: batch frame time)
            Time.captureDeltaTime = 1f / 60f;
            Assert.AreEqual(1f, pv.TpAdditiveWeight, "TP ateş additive'i TP'de açık olmalı (FP susturması yalnız birinci şahıs)");
            pv.PlayFire();
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(add).IsName(C.FireState + "_0") || an.GetNextAnimatorStateInfo(add).IsName(C.FireState + "_0"), "WP-032C: Fire_0 (A rifle recoil) additive katmanda değil");
            pv.PlayHit();
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(over).IsName(C.HitState) || an.GetNextAnimatorStateInfo(over).IsName(C.HitState), "hit_light override katmanda değil");
            Assert.AreEqual(baseBefore, an.GetCurrentAnimatorStateInfo(0).shortNameHash, "ateş/hit taban katmanını değiştirdi");

            // Pickup through the WP-007 event → WP-032B (DEC-082): the grab on the upper-body override layer while the
            // legs keep the locomotion blend (walking forward the whole time), then back to the layer's Empty.
            velocity = Vector3.forward * 6.2f;
            yield return CharacterTests.Bekle(() => pv.Move.y > 0.9f, 2f, "pickup öncesi ileri blend");
            // WP-071 MUST-3: from standing the base layer leaves Idle_Breath over 0.12 s — the pickup starts once the legs are on Locomotion
            yield return CharacterTests.Bekle(() => an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !an.IsInTransition(0), 2f, "pickup öncesi locomotion");
            WeaponEvents.PublishSpecialPickedUp(10f);
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(over).IsName(C.PickupState) || an.GetNextAnimatorStateInfo(over).IsName(C.PickupState), "pickup klibi üst gövde katmanında başlamadı");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState) && !an.IsInTransition(0), "pickup taban katmanını kesti");
            Assert.Greater(pv.Move.y, 0.9f, "pickup sırasında yürüyüş blend'i düştü");
            // WP-032C: the armed rest is Aim_<Weapon> (A rifle by default), not Empty
            yield return CharacterTests.Bekle(() => an.GetCurrentAnimatorStateInfo(over).IsName(C.AimState + "_0") && !an.IsInTransition(over), 3f, "pickup sonrası üst katman Aim_0'a dönmedi");
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.LocomotionState), "pickup boyunca taban katmanı locomotion kalmalı");
            velocity = Vector3.zero;
            yield return CharacterTests.Bekle(() => Durgun(pv), 2f, "pickup sonrası idle");

            // Death: back push → Death_1; death_end arrives; no way back.
            pv.PlayDeath(Vector3.back, false);
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.DeathState + "_1") || an.GetNextAnimatorStateInfo(0).IsName(C.DeathState + "_1"), "death_back");
            yield return CharacterTests.Bekle(() => pv.DeathFinished, 3f, "death_end gelmedi");
            pv.PlayFire(); pv.PlayHit();
            yield return null; yield return null;
            Assert.IsTrue(an.GetCurrentAnimatorStateInfo(0).IsName(C.DeathState + "_1"), "ölü oyuncu ölümden çıktı");
        }

        /// <summary>WP-082 R3: the player stands — LocoSpeed ≈ 0 on the 8-way tree (MoveX / MoveY keep the unit direction), Move ≈ 0 on the AP-022 4-way tree.</summary>
        private static bool Durgun(PlayerSkinnedVisual pv) =>
            pv.Locomotion8 ? pv.Animator.GetFloat(C.LocoSpeed) < 0.05f : pv.Move.magnitude < 0.05f;

        // ================================================================== kalabalık: GC 0 + uzak adım

        [Category("WP018")]
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
#if UNITY_EDITOR
            // WP-071 (C-00 30.09, Kalabalik40 root cause): the old monoDelta read the heap in 8 KB pages, and the editor allocates on
            // its own every frame (URP SceneViewDrawMode.UpdateSceneViewStates 48 B/frame after a V6 run) — 0 or 8192 by test order.
            // Now exact and owner-attributed: a profiler binary log over the window, read back here; counted = every GC.Alloc under
            // the PlayerLoop root, except the test runner's subtree (PlaymodeTestsController — it holds this test's body). Positive
            // control: GcProbe64 (a DD.* behaviour, 64 B in Update) is in the window and must be seen; everything else must be 0.
            var probeGo = new GameObject("WP071_GcProbe64");
            _temp.Add(probeGo);
            GcProbe64 gcProbe = probeGo.AddComponent<GcProbe64>();
            string rawBase = System.IO.Path.Combine(Application.temporaryCachePath, "kalabalik40-gc-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            string raw = rawBase + ".raw";
            if (System.IO.File.Exists(raw)) System.IO.File.Delete(raw);
            Profiler.logFile = rawBase;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            yield return null;
            Assert.IsTrue(Profiler.enabled, "profiler kaydı açılamadı — ölçüm yok, geçme yok");
#endif
            long monoBefore = Profiler.GetMonoUsedSizeLong();
            int gen0Before = GC.CollectionCount(0);
            for (int frame = 0; frame < 240; frame++)
            {
                Drive(visuals, frame);
                yield return null;
            }

            long delta = Profiler.GetMonoUsedSizeLong() - monoBefore;
            int gen0 = GC.CollectionCount(0) - gen0Before;
#if UNITY_EDITOR
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.logFile = "";
            int probeFrames = gcProbe.Frames;
            UnityEngine.Object.Destroy(probeGo);
            yield return null;
            GcOwners owners = GcOwners.Read(raw);
            try { System.IO.File.Delete(raw); } catch (System.IO.IOException) { }
            Debug.Log("[DD-TEST] wp018.gc crowd=40 frames=240 profilerFrames=" + owners.Frames + " phaseFrames=" + owners.PhaseFrames + " historyRaised=" + owners.HistoryRaised + " playerLoopBytes=" + owners.CountedBytes + " probeBytes=" + owners.ProbeBytes + " (probe frames " + probeFrames + ") excluded=" + owners.ExcludedBytes + " roots=[" + owners.Roots + "] excludedProbeChains:\n    " + owners.ExcludedProbeChains + "\n monoDelta(info)=" + delta + " gen0=" + gen0 + (owners.CountedBytes > 0 ? "\n" + owners.CountedOwners : ""));
            Assert.IsTrue(owners.HistoryRaised, "profiler geçmişi büyütülemedi (ProfilerDriver.SetMaxFrameHistoryLength yok) — pencere tam okunamaz, geçme yok");
            Assert.IsTrue(owners.Loaded, "profiler kaydı okunamadı: " + raw + " — ölçüm yok, geçme yok");
            Assert.IsTrue(owners.SawPlayerLoop, "kayıtta oyuncu döngüsü evresi (Update.* …) yok — ölçüm yok, geçme yok (kökler: " + owners.Roots + ")");
            Assert.GreaterOrEqual(owners.ProbeBytes, (long)probeFrames * 32 * 9 / 10, "pozitif kontrol: yöntem bilinçli 64 B/kare ayırmayı görmüyor");
            Assert.AreEqual(0, gen0, "kalabalık animasyon yolu gen0 toplama tetikledi");
            Assert.AreEqual(0L, owners.CountedBytes, "oyun döngüsü pencerede ayırıyor (DEC-088 GC 0):\n" + owners.CountedOwners);
#else
            Assert.Fail("GC ölçümü yalnız editörde (profiler okuyucu) — tanı politikası: atlama yok");
#endif

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

#if UNITY_EDITOR
        /// <summary>Kalabalik40's positive control: a DD.* behaviour that allocates ~64 B every Update (byte[32]).</summary>
        private sealed class GcProbe64 : MonoBehaviour
        {
            public int Frames;
            private static object s_sink;
            private void Update() { s_sink = new byte[32]; Frames++; }
        }

        /// <summary>GC.Alloc owners in a profiler binary log (main thread), by root: PlayerLoop counted, the test runner's subtree excluded.</summary>
        private sealed class GcOwners
        {
            public bool Loaded, SawPlayerLoop;
            public int Frames, PhaseFrames;
            public bool HistoryRaised;
            public long CountedBytes, ProbeBytes, ExcludedBytes;
            public string CountedOwners = "", Roots = "", ExcludedProbeChains = "";
            private const string ThreadRoot = "Main Thread";
            // r16: in batchmode there is no "PlayerLoop" / "EditorLoop" marker at the top (roots: Application.Message / Application.Tick /
            // Profiler.*); the player loop shows as Unity's phase samples — counted = under a phase sample, not under the test runner
            private static readonly string[] PlayerLoopPhases = { "Initialization.", "EarlyUpdate.", "FixedUpdate.", "PreUpdate.", "Update.", "PreLateUpdate.", "PostLateUpdate." };
            private static bool IsPhase(string name) { foreach (string p in PlayerLoopPhases) if (name.StartsWith(p, StringComparison.Ordinal)) return true; return false; }
            private const string TestRunner = "PlaymodeTestsController";   // the test runner's coroutine — this test's body lives under it
            private const string Probe = "GcProbe64";

            public static GcOwners Read(string raw)
            {
                var r = new GcOwners();
                // r18: the editor keeps a 300-frame profiler history and the log holds many editor frames per player frame — only ~6
                // of the 241 probe frames were in view. Raise the history before loading (internal API, by reflection); unavailable = FAIL.
                System.Reflection.MethodInfo setHistory = typeof(UnityEditorInternal.ProfilerDriver).GetMethod("SetMaxFrameHistoryLength",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (setHistory != null) { setHistory.Invoke(null, new object[] { 20000 }); r.HistoryRaised = true; }
                if (!System.IO.File.Exists(raw) || !UnityEditorInternal.ProfilerDriver.LoadProfile(raw, false)) return r;
                r.Loaded = true;
                var counted = new Dictionary<string, long>(StringComparer.Ordinal);
                var roots = new HashSet<string>(StringComparer.Ordinal);
                var probeChains = new HashSet<string>(StringComparer.Ordinal);
                for (int f = UnityEditorInternal.ProfilerDriver.firstFrameIndex; f <= UnityEditorInternal.ProfilerDriver.lastFrameIndex; f++)
                {
                    using (UnityEditor.Profiling.RawFrameDataView v = UnityEditorInternal.ProfilerDriver.GetRawFrameDataView(f, 0))
                    {
                        if (v == null || !v.valid) continue;
                        r.Frames++;
                        bool phaseInFrame = false;
                        var stack = new List<(string name, int left)>();
                        for (int i = 0; i < v.sampleCount; i++)
                        {
                            while (stack.Count > 0 && stack[stack.Count - 1].left == 0) stack.RemoveAt(stack.Count - 1);
                            string name = v.GetSampleName(i);
                            int children = v.GetSampleChildrenCount(i);
                            if (stack.Count > 0) { var top = stack[stack.Count - 1]; stack[stack.Count - 1] = (top.name, top.left - 1); }
                            bool atTop = stack.Count == 0 || (stack.Count == 1 && stack[0].name == ThreadRoot);
                            if (atTop && name != ThreadRoot) roots.Add(name);
                            if (IsPhase(name)) { r.SawPlayerLoop = true; phaseInFrame = true; }
                            if (name == "GC.Alloc")
                            {
                                long bytes = v.GetSampleMetadataCount(i) > 0 ? v.GetSampleMetadataAsLong(i, 0) : 0;
                                bool underPlayerLoop = stack.Exists(e => IsPhase(e.name));
                                bool testRunner = stack.Exists(e => e.name.Contains(TestRunner));
                                if (!underPlayerLoop || testRunner)
                                {
                                    r.ExcludedBytes += bytes;
                                    // r16: most probe allocations landed here — record their chains (diagnosis of the hierarchy)
                                    if (probeChains.Count < 6 && stack.Exists(e => e.name.Contains(Probe))) probeChains.Add(string.Join(" > ", stack.ConvertAll(e => e.name + "(" + e.left + ")")));
                                }
                                else if (stack.Exists(e => e.name.Contains(Probe))) r.ProbeBytes += bytes;
                                else
                                {
                                    r.CountedBytes += bytes;
                                    string owner = stack[stack.Count - 1].name;
                                    counted[owner] = counted.TryGetValue(owner, out long b) ? b + bytes : bytes;
                                }
                            }

                            if (children > 0) stack.Add((name, children));
                        }

                        if (phaseInFrame) r.PhaseFrames++;
                    }
                }

                foreach (var kv in counted) r.CountedOwners += "  " + kv.Value + " B  " + kv.Key + "\n";
                r.Roots = string.Join(", ", roots);
                r.ExcludedProbeChains = string.Join("\n    ", probeChains);
                return r;
            }
        }
#endif

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
}
