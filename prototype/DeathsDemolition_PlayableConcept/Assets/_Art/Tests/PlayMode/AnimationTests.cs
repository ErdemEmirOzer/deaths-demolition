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

        // ======================================================================== WP-071 B4 grip

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SolEl_SilahKabzasinda_ReloadtaBirakir_SonraTutar()
        {
            yield return RunAP005();
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv.GripIk, "WeaponGripIk bağlı değil");
            Assert.AreEqual(3, pv.HeadRenderers.Length, "baş her LOD'da ayrı renderer (P-06 rig v002, S-01 FP)");
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

        // S-06: one normal boot/selection/run observation; no reload, pose reset or aim writer.
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator S06_LegacyAr1_ImportedSurface_OnePostHumanoidFrame()
        {
            string path = S06PalmSurfaceObserver.Argument("-ddS06PalmProof");
            if (string.IsNullOrEmpty(path))
                Assert.Ignore("S-06 imported-surface diagnostic not requested (-ddS06PalmProof absent)");
            UiDataPaths.SetRootOverride(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "ui-sandbox"));
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, SahneZamanAsimi, "normal MainMenu");
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "normal Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "normal Run/player/weapon");
            PlayerSkinnedVisual pv = PlayerRegistry.Primary.GetComponentInChildren<PlayerSkinnedVisual>(true);
            Assert.IsNotNull(pv, "Actual loaded skinned player unavailable");
            yield return CharacterTests.Bekle(() =>
            {
                var an = pv.Animator; int layer = an != null ? an.GetLayerIndex(C.OverrideUpperLayer) : -1;
                return layer >= 0 && !an.IsInTransition(layer) && an.GetCurrentAnimatorStateInfo(layer).IsName(C.AimState + "_0")
                    && WeaponRegistry.ActiveController.Classic.IsConfigured && !WeaponRegistry.ActiveController.SpecialWeaponActive;
            }, OyuncuZamanAsimi, "actual legacy Aim_0, no pose forcing");
            var observer = pv.gameObject.AddComponent<S06PalmSurfaceObserver>();
            observer.Visual = pv; observer.OutputPath = path;
            yield return CharacterTests.Bekle(() => observer.Completed, OyuncuZamanAsimi, "ONE postHumanoid record");
            bool measured = observer.ImportedSurfaceObserved;
            UnityEngine.Object.Destroy(observer);
            Assert.IsTrue(System.IO.File.Exists(path), "Actual observer proof output missing");
            Assert.IsTrue(measured, "Imported surface or actual identity unavailable; preserve JSON UNKNOWN, no retake");
        }

        // ================================================================================ rise

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
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SilahV3_AOC_ar1ar2_TacEmpty_BurstTekVurus_LocomotionSifirlanmaz()
        {
            // WP-071 B4b (K8): the AR pair on the generic v3 states through one runtime override controller — equip swaps only the
            // weapon slots (no rebind: the base layer keeps its state and clock), reload picks tac / empty from the duration, the
            // burst weapon's fire clip starts once per burst.
            yield return RunAP005();
            PlayerSkinnedVisual.V3WeaponSets = true;   // C-00 25.09: TP v3 is off in the game until P-06's gripOffset re-export; this test turns it on
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

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator FpKollar_VarsayilanKapali_FpdeGizli_SilahFpViewda()
        {
            // C-00 26.09: the FP arms are OFF by default until P-06's FP arms v2 + finger clips — in first person no arms copy shows and the
            // weapon keeps S-02's fpView (the FirstPersonWeaponSocket hook stays empty).
            Assert.IsFalse(PlayerSkinnedVisual.FirstPersonV3Arms, "FP kolları varsayılan kapalı");
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

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator FpKollar_Viewmodel_fpCameraCapada_AtesReloadGecis_TPyeDonuste_Gizli()
        {
            // WP-071 B4b-3: in first person the FP arms copy sits on the viewmodel layer with fp_camera on the anchor, plays the
            // FP twins (fire additive, tac / empty reload, switch), shows only the LOD0 body (no head), and hides back in TP.
            PlayerSkinnedVisual.FirstPersonV3Arms = true;   // the feature under test (default OFF, C-00 26.09)
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
                PlayerSkinnedVisual.FirstPersonV3Arms = false;
            }

            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsFalse(pv.FirstPersonArms.Visible, "TP'de FP kolları gizli");
            Assert.IsNull(DD.Prototype.Weapons.WeaponController.FirstPersonWeaponSocket, "TP'de kanca boş (fpView yedeği)");
            Assert.IsNull(FirstPersonArms.Shown);
        }

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


    /// <summary>One opt-in read-only observation after the existing runtime writers. It never defines an anatomical palm from a bone.</summary>
    [DefaultExecutionOrder(20000)]
    public sealed class S06PalmSurfaceObserver : MonoBehaviour
    {
        public PlayerSkinnedVisual Visual;
        public string OutputPath;
        public bool Completed { get; private set; }
        public bool ImportedSurfaceObserved { get; private set; }
        [Serializable] public sealed class AssetId
        {
            public string name, path, guid, fileId, fileSha256, dependencyHash;
        }
        [Serializable] public sealed class TransformId
        {
            public string name, hierarchy;
            public Vector3 localPosition, worldPosition, localScale, lossyScale;
            public Quaternion localRotation, worldRotation;
            public float[] localToWorldRowMajor, worldToLocalRowMajor;
            public float localToWorldDeterminant;
        }
        [Serializable] public sealed class ClipId
        {
            public AssetId asset;
            public float weight, lengthSeconds, animatorNormalizedTime, observedLoopSeconds, observedFrameAt30fps;
        }
        [Serializable] public sealed class Surface
        {
            public string landmarkName = "ImportedLOD0_SurfaceClosestToAuthoredRightGrip";
            public string anatomicalIdentity = "UNKNOWN: no sealed source anatomical palm CP/polygon/barycentric landmark";
            public string sourceControlpointMapping = "UNKNOWN: importer welding/split/reorder; source CP index is not Unity vertex index";
            public string selection = "Minimum world-space squared distance over all actual baked LOD0 skin triangles; exact float ties counted; no anatomical mask or acceptance threshold";
            public AssetId mesh;
            public TransformId renderer;
            public int bakedVertexCount, importedVertexCount, triangleOrdinal, submeshIndex, submeshTriangleOrdinal, exactFloatTies;
            public int[] orderedBakedVertexIndices;
            public string bakedToImportedVertexCorrespondence = "UNKNOWN: imported mesh unreadable; vertex ordinal equivalence not independently verified";
            public bool rendererEnabled, rendererActiveInHierarchy, rendererIsVisible;
            public string scaleConversion = "BakeMesh(useScale:false), then renderer.localToWorld once; Unity renderer-local to world metres. Source FBX/Blender to Unity correspondence UNKNOWN";
            public Vector3[] orderedBakedRendererLocalVertices, orderedBakedWorldVertices;
            public Vector3 barycentric, rendererLocalPoint, worldPoint, worldWindingNormal, gripMinusSurfaceWorld;
            public float distanceToAuthoredGripMetres;
            public string originalMeshReadable, restWeightsAccess, bakedSnapshotSha256;
            public TransformId[] boundBones;
        }
        [Serializable] public sealed class HumanMap { public string humanName, boneName; public bool useDefaultLimits; public Vector3 minimum, maximum, center; public float axisLength; }
        [Serializable] public sealed class RestBone { public string name; public Vector3 position, scale; public Quaternion rotation; }
        [Serializable] public sealed class MuscleScalar { public string name; public float normalizedValue; }
        [Serializable] public sealed class Proof
        {
            public string schema = "s06.imported-skin-grip-observer.v1";
            public string status = "UNKNOWN_ANATOMICAL_PALM_AND_SOURCE_VERTEX_MATCH";
            public string observerSourceSha, utc, stage = "LateUpdate order20000, after Animator/Humanoid and runtime mount900/left-grip910 writers";
            public string domain = "Unity world metres; vectors are Unity axes. Triangle normal from ordered baked world geometry, not bone rotation. Source Blender units are not subtracted.";
            public int frameCount;
            public float timeSeconds, deltaTimeSeconds;
            public string expectedWeapon = "ar_1 / HINAWA, legacy Aim_0", observedWeaponAnimSet, observedState, error;
            public string testIdentity = "DD.Art.Tests.PlayMode.AnimationTests.S06_LegacyAr1_ImportedSurface_OnePostHumanoidFrame", unityVersion, observedWeaponId, observedDisplayName, observedFireMode, observedWorldSource, observedWorldScene, classicModelSource, libraryRosterKey, libraryModelPackage;
            public AssetId[] candidateLod0SkinMeshes;
            public bool actualLoadedLegacyIdentityKnown, specialWeaponActive, actualModelMeshSourcesMatchLibrary;
            public AssetId[] actualModelMeshes;
            public string activeClipTimeLimit = "Fractional state normalized time times clip length is a state-clock estimate, not source animation sample/correspondence proof";
            public int observedWeaponParameter, activeAuthoredClassicModelCount;
            public bool fpArmsEnabled, tpSpecialDefaultEnabled, stateTransition, mountAligned, mountDeltaApplied;
            public float mountDeltaDegrees, leftGripWeight, leftGripErrorMetres;
            public bool leftGripErrorFinite;
            public AssetId playerPrefab, avatar, baseController, weaponDefinition, weaponLibraryModel;
            public ClipId[] activeUpperClips;
            public HumanMap[] avatarHumanMapping;
            public RestBone[] avatarRestSkeleton;
            public MuscleScalar[] rightArmHandMuscles;
            public TransformId root, rightHandBoneReference, middleProximalBoneReference, socket, weaponMount, actualModelRoot, actualModelFirstChild, authoredGripMarker, leftGripMarker;
            public Vector3 authoredGripOffsetModelMetres, authoredGripOffsetWorldPoint;
            public string actualGripMarkerName;
            public Vector3 observedGripWorldPoint;
            public float socketToObservedGripMetres;
            public Surface surface;
            public string counterPart = "WAIT: f44 FP diagnostic inputs absent in frozen legacy TP candidate; FP remains OFF; no extra intake/sample";
            public string proofLimit = "Loaded source/Avatar/mesh identities and observed imported surface can be proven; source CP equivalence/anatomical palm/contact/cause/fix cannot. Historical bone-midpoint proxies are not surface landmarks.";
        }
        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
        private static string Hierarchy(Transform t)
        {
            if (t == null) return null;
            string path = t.name; while (t.parent != null) { t = t.parent; path = t.name + "/" + path; } return path;
        }
        private static TransformId TransformOf(Transform t)
        {
            if (t == null) return null;
            var m = t.localToWorldMatrix; var inv = t.worldToLocalMatrix; var values = new float[16]; var inverseValues = new float[16];
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) { values[r * 4 + c] = m[r, c]; inverseValues[r * 4 + c] = inv[r, c]; }
            return new TransformId { name = t.name, hierarchy = Hierarchy(t), localPosition = t.localPosition,
                worldPosition = t.position, localScale = t.localScale, lossyScale = t.lossyScale, localRotation = t.localRotation, worldRotation = t.rotation, localToWorldRowMajor = values, worldToLocalRowMajor = inverseValues, localToWorldDeterminant = m.determinant };
        }
        private static AssetId Identity(UnityEngine.Object obj)
        {
            if (obj == null) return null;
            var id = new AssetId { name = obj.name };
#if UNITY_EDITOR
            id.path = UnityEditor.AssetDatabase.GetAssetPath(obj);
            if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long fileId)) { id.guid = guid; id.fileId = fileId.ToString(CultureInfo.InvariantCulture); }
            if (!string.IsNullOrEmpty(id.path))
            {
                id.dependencyHash = UnityEditor.AssetDatabase.GetAssetDependencyHash(id.path).ToString();
                if (System.IO.File.Exists(id.path))
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        id.fileSha256 = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(id.path))).Replace("-", "").ToLowerInvariant();
            }
#endif
            return id;
        }
        private void LateUpdate()
        {
            if (Completed || Visual == null || string.IsNullOrEmpty(OutputPath)) return;
            var p = new Proof { observerSourceSha = Argument("-ddS06PalmSourceSha"), utc = DateTime.UtcNow.ToString("o"),
                frameCount = Time.frameCount, timeSeconds = Time.time, deltaTimeSeconds = Time.deltaTime,
                fpArmsEnabled = PlayerSkinnedVisual.FirstPersonV3Arms, tpSpecialDefaultEnabled = PlayerSkinnedVisual.TpV3On("sw_arc") };
            try { Observe(p); ImportedSurfaceObserved = p.actualLoadedLegacyIdentityKnown && p.surface != null; if (ImportedSurfaceObserved) p.status = "PARTIAL_IMPORTED_SURFACE_OBSERVED_ANATOMY_AND_SOURCE_MAPPING_UNKNOWN"; } catch (Exception e) { p.error = e.ToString(); p.status = "UNKNOWN_OBSERVER_ACCESS_OR_PLUMBING"; }
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OutputPath));
                System.IO.File.WriteAllText(OutputPath, JsonUtility.ToJson(p, true));
                Debug.Log("[S06-PalmProof] frame=" + p.frameCount + " status=" + p.status + " surface=" + (p.surface != null ? p.surface.landmarkName : "UNKNOWN"));
            }
            finally { Completed = true; }
        }
        private void Observe(Proof p)
        {
            var an = Visual.Animator;
            p.unityVersion = Application.unityVersion;
            p.observedWorldSource = WorldSourceSettings.Resolve(out string worldScene).ToString(); p.observedWorldScene = worldScene;
            var wc = WeaponRegistry.ActiveController;
            if (wc == null || !wc.Classic.IsConfigured) throw new InvalidOperationException("Actual configured weapon controller unavailable");
            var actualDef = wc.Classic.Definition;
            p.weaponDefinition = Identity(actualDef); p.observedWeaponId = actualDef != null ? actualDef.weaponId : null;
            p.observedDisplayName = wc.Classic.DisplayName; p.observedFireMode = wc.Classic.FireMode.ToString(); p.specialWeaponActive = wc.SpecialWeaponActive;
            int layer = an.GetLayerIndex(C.OverrideUpperLayer);
            if (layer < 0 || an.avatar == null || !an.avatar.isValid || !an.avatar.isHuman) throw new InvalidOperationException("Actual Humanoid Avatar or override layer unavailable");
            var state = an.GetCurrentAnimatorStateInfo(layer);
            p.observedState = state.IsName(C.AimState + "_0") ? "Aim_0" : state.shortNameHash.ToString(CultureInfo.InvariantCulture);
            p.stateTransition = an.IsInTransition(layer); p.observedWeaponParameter = an.GetInteger(C.Weapon); p.observedWeaponAnimSet = Visual.WeaponAnimSet;
            p.avatar = Identity(an.avatar);
            p.playerPrefab = Identity(CharacterAssetSet.Load().PlayerPrefab);
            p.baseController = Identity(an.runtimeAnimatorController);
                        var library = ClassicWeaponVisualLibrary.Current; var entry = library != null ? library.Find(wc.Classic.Id) : null;
            if (entry == null || entry.model == null) throw new InvalidOperationException("Loaded classic library entry unavailable");
            int entryMatches = 0; foreach (var candidate in library.entries) if (candidate != null && candidate.id == wc.Classic.Id) entryMatches++;
            if (entryMatches != 1) throw new InvalidOperationException("Classic library identity is ambiguous");
            p.weaponLibraryModel = Identity(entry.model); p.libraryRosterKey = entry.rosterKey; p.libraryModelPackage = entry.modelPackage;
            p.classicModelSource = wc.ClassicModelSource.ToString();
            var skinIds = new List<AssetId>(); foreach (var sr in Visual.Renderers) if (sr != null && sr.sharedMesh != null && sr.name.EndsWith("_LOD0", StringComparison.Ordinal)) skinIds.Add(Identity(sr.sharedMesh));
            p.candidateLod0SkinMeshes = skinIds.ToArray();
            var ci = an.GetCurrentAnimatorClipInfo(layer); p.activeUpperClips = new ClipId[ci.Length];
            for (int i = 0; i < ci.Length; i++)
            {
                float seconds = (state.normalizedTime - Mathf.Floor(state.normalizedTime)) * ci[i].clip.length;
                p.activeUpperClips[i] = new ClipId { asset = Identity(ci[i].clip), weight = ci[i].weight, lengthSeconds = ci[i].clip.length,
                    animatorNormalizedTime = state.normalizedTime, observedLoopSeconds = seconds, observedFrameAt30fps = seconds * 30f };
            }
            var hd = an.avatar.humanDescription; p.avatarHumanMapping = new HumanMap[hd.human.Length];
            for (int i = 0; i < hd.human.Length; i++) { var h = hd.human[i]; p.avatarHumanMapping[i] = new HumanMap { humanName = h.humanName, boneName = h.boneName,
                useDefaultLimits = h.limit.useDefaultValues, minimum = h.limit.min, maximum = h.limit.max, center = h.limit.center, axisLength = h.limit.axisLength }; }
            p.avatarRestSkeleton = new RestBone[hd.skeleton.Length];
            for (int i = 0; i < hd.skeleton.Length; i++) { var b = hd.skeleton[i]; p.avatarRestSkeleton[i] = new RestBone { name = b.name, position = b.position, rotation = b.rotation, scale = b.scale }; }
            using (var handler = new HumanPoseHandler(an.avatar, an.transform))
            {
                var hp = new HumanPose(); handler.GetHumanPose(ref hp); var muscles = new List<MuscleScalar>();
                for (int i = 0; i < HumanTrait.MuscleName.Length; i++) if (HumanTrait.MuscleName[i].StartsWith("Right", StringComparison.Ordinal))
                    muscles.Add(new MuscleScalar { name = HumanTrait.MuscleName[i], normalizedValue = hp.muscles[i] });
                p.rightArmHandMuscles = muscles.ToArray();
            }
            var rig = PlayerRegistry.Primary;
            p.root = TransformOf(rig.transform); p.rightHandBoneReference = TransformOf(an.GetBoneTransform(HumanBodyBones.RightHand));
            p.middleProximalBoneReference = TransformOf(an.GetBoneTransform(HumanBodyBones.RightMiddleProximal));
            p.weaponMount = TransformOf(rig.WeaponMount); p.socket = TransformOf(rig.WeaponMount.parent);
            var align = rig.WeaponMount.GetComponent<WeaponMountAlignment>();
            if (align != null) { p.mountAligned = align.AlignedThisFrame; p.mountDeltaApplied = align.DeltaApplied; p.mountDeltaDegrees = align.DeltaDegrees; }
            p.leftGripWeight = Visual.GripIk.Weight; p.leftGripErrorFinite = !float.IsNaN(Visual.GripIk.LastError) && !float.IsInfinity(Visual.GripIk.LastError);
            if (p.leftGripErrorFinite) p.leftGripErrorMetres = Visual.GripIk.LastError;
            p.leftGripMarker = TransformOf(Visual.GripIk.Grip);
            Transform model = null;
            var ownedRenderers = new List<Renderer>(); wc.CollectWeaponRenderers(ownedRenderers);
            var ownedRigs = new HashSet<WeaponVisualRig>();
            foreach (var wr in ownedRenderers) if (wr != null) { var vr = wr.GetComponentInParent<WeaponVisualRig>(); if (vr != null) ownedRigs.Add(vr); }
            foreach (var vr in ownedRigs)
                if (vr.HasAuthoredModel && vr.Body != null && vr.Body != wc.SpecialRigBody && vr.Body.gameObject.activeInHierarchy)
                    foreach (Transform child in vr.Body) if (child.name == "Model" && child.gameObject.activeInHierarchy) { p.activeAuthoredClassicModelCount++; model = child; }
            if (p.activeAuthoredClassicModelCount != 1) throw new InvalidOperationException("Actual classic model is not unique");
            var actualMeshIds = new List<AssetId>(); bool match = true;
            var expectedMeshes = new HashSet<Mesh>();
            foreach (var f in entry.model.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) expectedMeshes.Add(f.sharedMesh);
            foreach (var f in model.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null) { actualMeshIds.Add(Identity(f.sharedMesh)); match &= expectedMeshes.Contains(f.sharedMesh); }
            p.actualModelMeshes = actualMeshIds.ToArray(); p.actualModelMeshSourcesMatchLibrary = match && actualMeshIds.Count > 0;
            p.actualLoadedLegacyIdentityKnown = p.observedWeaponId == "ar_1" && wc.Classic.Id == ClassicWeaponId.AssaultRifle && entry.rosterKey == "ar_1"
                && wc.ClassicModelSource == WeaponController.ClassicModelKind.Authored && !p.specialWeaponActive && p.observedState == "Aim_0" && !p.stateTransition && p.observedWeaponParameter == 0
                && string.IsNullOrEmpty(p.observedWeaponAnimSet) && p.actualModelMeshSourcesMatchLibrary;
            if (!p.actualLoadedLegacyIdentityKnown) throw new InvalidOperationException("Actual loaded ar1 legacy pose/model identity does not match requested observation");
            p.actualModelRoot = TransformOf(model); p.actualModelFirstChild = model.childCount > 0 ? TransformOf(model.GetChild(0)) : null;
            Transform marker = null; int markerCount = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == "grip_r") { marker = t; markerCount++; }
            if (markerCount == 0) foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == DD.Prototype.Core.Weapons.WeaponSocketContract.MarkerGrip) { marker = t; markerCount++; }
            if (markerCount != 1) throw new InvalidOperationException("Authored right grip marker unavailable or ambiguous");
            p.authoredGripMarker = TransformOf(marker); p.actualGripMarkerName = marker != null ? marker.name : null;
            var def = actualDef; p.authoredGripOffsetModelMetres = def.GripOffset;
            p.authoredGripOffsetWorldPoint = model.TransformPoint(def.GripOffset);
            if (marker == null) throw new InvalidOperationException("No actual authored right-grip marker; geometry target not guessed");
            p.observedGripWorldPoint = marker.position; p.socketToObservedGripMetres = Vector3.Distance(rig.WeaponMount.parent.position, marker.position);
            p.surface = ClosestActualSkin(Visual.Renderers, marker.position);
        }
        private static Surface ClosestActualSkin(SkinnedMeshRenderer[] renderers, Vector3 grip)
        {
            Surface best = null; float bestD2 = float.PositiveInfinity; int ties = 0;
            foreach (var r in renderers)
            {
                if (r == null || r.sharedMesh == null || !r.name.EndsWith("_LOD0", StringComparison.Ordinal)) continue;
                var baked = new Mesh { name = "S06_TransientActualSkin" };
                try
                {
                    r.BakeMesh(baked, false); var verts = baked.vertices; string bakedSha = SnapshotHash(baked, verts); int globalTriangle = 0;
                    for (int sub = 0; sub < baked.subMeshCount; sub++)
                    {
                        var indices = baked.GetTriangles(sub);
                        for (int tri = 0; tri < indices.Length; tri += 3, globalTriangle++)
                        {
                            int ia = indices[tri], ib = indices[tri + 1], ic = indices[tri + 2];
                            var a = r.transform.TransformPoint(verts[ia]); var b = r.transform.TransformPoint(verts[ib]); var c = r.transform.TransformPoint(verts[ic]);
                            if (Vector3.Cross(b - a, c - a).sqrMagnitude == 0f) continue;
                            Vector3 bary; Vector3 q = ClosestPoint(grip, a, b, c, out bary); float d2 = (q - grip).sqrMagnitude;
                            if (float.IsNaN(d2) || float.IsInfinity(d2)) throw new InvalidOperationException("Non-finite baked geometry distance");
                            if (d2 == bestD2) { ties++; continue; } if (d2 > bestD2) continue;
                            bestD2 = d2; ties = 1;
                            var bones = new TransformId[r.bones.Length]; for (int k = 0; k < bones.Length; k++) bones[k] = TransformOf(r.bones[k]);
                            best = new Surface { bakedSnapshotSha256 = bakedSha, mesh = Identity(r.sharedMesh), renderer = TransformOf(r.transform), bakedVertexCount = verts.Length,
                                importedVertexCount = r.sharedMesh.vertexCount, triangleOrdinal = globalTriangle, submeshIndex = sub, submeshTriangleOrdinal = tri / 3,
                                orderedBakedVertexIndices = new[] { ia, ib, ic }, rendererEnabled = r.enabled, rendererActiveInHierarchy = r.gameObject.activeInHierarchy, rendererIsVisible = r.isVisible, orderedBakedRendererLocalVertices = new[] { verts[ia], verts[ib], verts[ic] },
                                orderedBakedWorldVertices = new[] { a, b, c }, barycentric = bary, rendererLocalPoint = bary.x * verts[ia] + bary.y * verts[ib] + bary.z * verts[ic],
                                worldPoint = q, worldWindingNormal = Vector3.Cross(b - a, c - a).normalized, gripMinusSurfaceWorld = grip - q,
                                distanceToAuthoredGripMetres = Mathf.Sqrt(d2), originalMeshReadable = r.sharedMesh.isReadable.ToString(),
                                restWeightsAccess = "No readability/importer mutation; source CP/skin-region identity remains UNKNOWN", boundBones = bones };
                        }
                    }
                }
                finally { UnityEngine.Object.Destroy(baked); }
            }
            if (best != null) best.exactFloatTies = ties;
            return best;
        }
        private static string SnapshotHash(Mesh baked, Vector3[] verts)
        {
            using (var memory = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(memory))
            {
                writer.Write(verts.Length); foreach (var v in verts) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                writer.Write(baked.subMeshCount);
                for (int sub = 0; sub < baked.subMeshCount; sub++) { var triangles = baked.GetTriangles(sub); writer.Write(triangles.Length); foreach (int index in triangles) writer.Write(index); }
                writer.Flush(); using (var sha = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(memory.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        private static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a; float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1, 0, 0); return a; }
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); bary = new Vector3(1 - v, v, 0); return a + v * ab; }
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); bary = new Vector3(1 - w, 0, w); return a + w * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); bary = new Vector3(0, 1 - w, w); return b + w * (c - b); }
            float inv = 1f / (va + vb + vc), v2 = vb * inv, w2 = vc * inv;
            bary = new Vector3(1 - v2 - w2, v2, w2); return a + ab * v2 + ac * w2;
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
