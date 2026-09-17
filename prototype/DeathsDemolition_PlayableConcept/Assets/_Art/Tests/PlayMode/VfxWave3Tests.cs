using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-047 · Wave 3 in the Run: the enemy beats (WP-040) and foot events (WP-032B) drive claw swipe /
    /// spawn dust / dust step through the binder; reload drops a magazine; the muzzle picks its A/B/C variant
    /// per weapon; Kagura ring and Ofuda collapse play the AP-014 sheets; lanterns get their flame loop;
    /// the event path allocates nothing (ADR-037-1). Evidence captures for the AD round.
    /// </summary>
    [TestFixture]
    [Category("WP047")]
    public sealed class VfxWave3Tests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;
        private const int W = 1280, H = 720;
        private EnemySpawnDirector _director;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.timeScale = 1f;
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
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null && HitFeedbackDirector.Instance != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static EnemyCombatEventInfo Beat(EnemyCombatEventKind kind, Transform body, EnemyTier tier, Vector3 pos, Vector3 dir)
        {
            return new EnemyCombatEventInfo(kind, body, tier, 12345, pos, dir, ArmorSlot.None, true, 0.5f);
        }

        private static float GroundY(Vector3 near)
        {
            return Physics.Raycast(near + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : near.y;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_Wave3_Kancalar_Varyantlar_AP014_Fener_GcSifir()
        {
            yield return RunAP005();
            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            VfxDressing vfx = fx.Vfx;
            Assert.IsTrue(vfx.IsReady);
            VfxEventBinder binder = vfx.Binder;
            Assert.IsNotNull(binder, "olay bağlayıcı yok");

            Vector3 origin = PlayerRegistry.Primary.transform.position;
            Vector3 p = origin + Vector3.forward * 3f;
            p.y = GroundY(p);
            var body = new GameObject("wave3-body").transform; body.position = p;

            // A · enemy beats → claw swipe (attack_commit) and spawn dust (spawn_rise); breaks / deaths stay on the director path.
            int claws0 = binder.ClawSwipes, dust0 = binder.SpawnDusts, w3 = vfx.Wave3Played;
            EnemyCombatEvents.Raise(Beat(EnemyCombatEventKind.AttackCommit, body, EnemyTier.Standard, p + Vector3.up * 1.1f, Vector3.back));
            EnemyCombatEvents.Raise(Beat(EnemyCombatEventKind.SpawnRise, body, EnemyTier.Elite, p + Vector3.right * 2f, Vector3.back));
            EnemyCombatEvents.Raise(Beat(EnemyCombatEventKind.AttackMiss, body, EnemyTier.Standard, p, Vector3.back));
            yield return null;
            Assert.AreEqual(1, binder.ClawSwipes - claws0, "attack_commit → claw_swipe");
            Assert.AreEqual(1, binder.SpawnDusts - dust0, "spawn_rise → spawn_dust");
            Assert.GreaterOrEqual(binder.BeatsSeen, 3);

            // A · foot events: the player's animation-event component; the second step inside 0.22 s is throttled.
            CharacterAnimationEvents feet = PlayerRegistry.Primary.GetComponentInChildren<CharacterAnimationEvents>(true);
            int steps0 = binder.FootSteps, thr0 = binder.FootStepsThrottled;
            if (feet != null)
            {
                feet.foot_l();
                feet.foot_r();
                yield return null;
                Assert.AreEqual(1, binder.FootSteps - steps0, "ilk adım → dust_step");
                Assert.AreEqual(1, binder.FootStepsThrottled - thr0, "0.22 s içindeki ikinci adım kısılır");
            }

            // B · reload → magazine card + bounce dust after the delay.
            int mags0 = binder.MagDrops;
            WeaponEvents.PublishReloadStarted(1.2f);
            yield return new WaitForSeconds(VfxEventBinder.MagDropDelaySeconds + 0.15f);
            Assert.AreEqual(1, binder.MagDrops - mags0, "reload → mag_drop");

            // B · muzzle A / B / C: the SMG variant plays at 60 % duration, the shotgun schedules the pump vent.
            WeaponController wc = WeaponRegistry.ActiveController;
            Transform muzzle = wc.Muzzle;
            fx.ReportMuzzleFlash(muzzle, 1f, 1f);
            yield return null;
            float lifeA = LastMuzzleLife(vfx);
            MuzzleVariant vA = vfx.LastMuzzleVariant;
            wc.EquipClassic(ClassicWeaponId.AutoSmg);
            yield return new WaitForSeconds(0.5f);   // past the A card's life; EquipClassic rebuilds the model, so the muzzle is re-read
            muzzle = wc.Muzzle;
            fx.ReportMuzzleFlash(muzzle, 1f, 1f);
            yield return null;
            float lifeB = LastMuzzleLife(vfx);
            MuzzleVariant vB = vfx.LastMuzzleVariant;
            Debug.Log("[DD-TEST] wp047.muzzle A=" + vA + "/" + lifeA + " B=" + vB + "/" + lifeB + " classic=" + wc.Classic.Id + " special=" + wc.SpecialWeaponActive + " plays=" + vfx.MuzzlePlays + " note=" + vfx.LastMuzzleNote + " active==wc " + (WeaponRegistry.ActiveController == wc));
            wc.EquipClassic(ClassicWeaponId.CombatShotgun);
            yield return new WaitForSeconds(0.5f);
            muzzle = wc.Muzzle;
            int pump0 = binder.PumpSmokes;
            fx.ReportMuzzleFlash(muzzle, 1f, 1f);
            yield return new WaitForSeconds(VfxEventBinder.PumpSmokeDelaySeconds + 0.1f);
            wc.EquipClassic(ClassicWeaponId.AssaultRifle);
            Assert.AreEqual(MuzzleVariant.A, vA); Assert.AreEqual(MuzzleVariant.B, vB);
            Assert.Less(lifeB, lifeA * 0.75f, "B (SMG) kısa flaş: " + lifeB + " vs A " + lifeA);
            Assert.AreEqual(1, binder.PumpSmokes - pump0, "C (pompalı) pump dumanı");

            // B · AP-014 sheets: Kagura ring on the shock ring, Ofuda collapse on the seal links (de-duplicated per frame).
            int w3b = vfx.Wave3Played;
            fx.ReportShockRing(p, 4f, 0.34f);
            yield return null;
            int kagura = vfx.Wave3Played - w3b;
            fx.ReportSealLink(p, p + Vector3.right * 2f);
            fx.ReportSealLink(p + Vector3.right * 2f, p);   // the closing link of a 2-body net: same bodies, no second burst
            yield return null;
            int ofuda = vfx.Wave3Played - w3b - kagura;
            Assert.AreEqual(1, kagura, "Kagura ring sheet");
            Assert.AreEqual(2, ofuda, "Ofuda collapse: 2 sealed bodies, 4 link ends → 2 bursts");
            Assert.GreaterOrEqual(vfx.SwSheets.MaterialsCreated, 2, "AP-014 runtime materials (kagura + collapse)");

            // D · lanterns: the scan lit the stone lanterns of the AP005 world.
            yield return new WaitForSeconds(0.2f);
            int placed = vfx.Lanterns.Scan();
            int lanterns = vfx.Lanterns.Placed;
            {
                int tagged = 0; foreach (SurfaceTag tag in UnityEngine.Object.FindObjectsOfType<SurfaceTag>()) if (tag.Family != null && tag.Family.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) >= 0) tagged++;
                Debug.Log("[DD-TEST] wp047.lanterns taggedLanterns=" + tagged + " placed=" + placed + " scans=" + vfx.Lanterns.Scans);
            }
            Assert.Greater(lanterns, 0, "AP005'te fener alevi yok (stone_lantern renderer'ları)");
            Assert.LessOrEqual(lanterns, LanternFlames.MaxFlames);

            // GC · 30 beats + 8 foot events + 4 links: nothing after warm-up.
            for (int i = 0; i < 10; i++) EnemyCombatEvents.Raise(Beat(EnemyCombatEventKind.AttackCommit, body, EnemyTier.Standard, p + Vector3.right * (i % 5), Vector3.back));
            yield return null; yield return null;
            // The bus has other subscribers (audio, HUD); the VFX handlers are measured directly with the used-heap delta
            // (WP-005 pattern: GC.GetAllocatedBytesForCurrentThread is always 0 in Unity Mono, the frame recorder misses
            // script allocations in the batchmode Editor). 100 × 40 calls synchronous: a per-call leak cannot hide in one 4 KB block.
            long control = GC.GetTotalMemory(false);
            { var keep = new object[4000]; for (int i = 0; i < keep.Length; i++) keep[i] = new byte[64]; control = GC.GetTotalMemory(false) - control; GC.KeepAlive(keep); }
            Assert.Greater(control, 0, "yığın sayacı 256 KB yemi görmedi — ölçüm geçersiz");
            long gcBytes = GC.GetTotalMemory(false);
            for (int rep = 0; rep < 100; rep++)
            {
                for (int i = 0; i < 30; i++)
                {
                    if (i % 3 == 0) vfx.PlaySpawnDust(p + Vector3.right * (i % 5) * 0.5f, Vector3.back);
                    else vfx.PlayClawSwipe(p + Vector3.right * (i % 5) * 0.5f + Vector3.up, Vector3.back, EnemyTier.Standard);
                }

                for (int i = 0; i < 8; i++) vfx.PlayDustStep(p + Vector3.right * (i % 4) * 0.3f, Vector3.up, Vector3.forward, i % 2 == 0 ? SurfaceKind.Stone : SurfaceKind.Dirt);
                vfx.PlayOfudaCollapse(p, p + Vector3.right * 2f);
                vfx.PlayKaguraRing(p, 4f, 0.34f);
            }
            gcBytes = GC.GetTotalMemory(false) - gcBytes;
            yield return null;

            Debug.Log("[DD-TEST] wp047.wave3 claws=" + binder.ClawSwipes + " spawnDust=" + binder.SpawnDusts + " steps=" + binder.FootSteps + "/" + binder.FootStepsThrottled +
                      " mags=" + binder.MagDrops + " pump=" + binder.PumpSmokes + " lifeA=" + lifeA.ToString("F2") + " lifeB=" + lifeB.ToString("F2") + " kagura=" + kagura + " ofuda=" + ofuda +
                      " lanterns=" + lanterns + " swMaterials=" + vfx.SwSheets.MaterialsCreated + " wave3=" + vfx.Wave3Played + " gcBytes=" + gcBytes);
            Assert.LessOrEqual(gcBytes, 4096, "Wave-3 olay yolu yönetilen bellek ayırdı: " + gcBytes + " B / 4000 çağrı (yığın deltası, kontrol " + control + " B).");

            // Evidence for the AD round (post-fx on): swipe, spawn dust, muzzle C, kagura, lantern.
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp047-captures");
            dir = Path.Combine(dir, "wave3"); Directory.CreateDirectory(dir);
            // AD round 1 (M1/M4): the GC-loop cards must be dead before the frame, the claw sits on a living body when the
            // round has one (its own chest / facing; the synthetic body otherwise), the ring starts at the player's feet
            // like the bell does (KaguraWaveField origin) — the first frames had staged cards side by side and a 3 m offset.
            yield return new WaitForSeconds(0.6f);
            Transform actor = body; Vector3 actorFacing = Vector3.back;
            float bestD = float.MaxValue;
            foreach (ZombieEnemy z in UnityEngine.Object.FindObjectsOfType<ZombieEnemy>())
            {
                if (!z.IsAlive) continue;
                float d = (z.transform.position - origin).sqrMagnitude;
                if (d < bestD) { bestD = d; actor = z.transform; actorFacing = z.transform.forward; }
            }
            Vector3 ap = actor.position;
            Vector3 camBack = actorFacing.sqrMagnitude > 1e-4f ? actorFacing.normalized : Vector3.back;
            var near = new ArtVisualRegression.CameraPose("wave3_near", ap + camBack * 2.6f + Vector3.up * 1.6f, ap + Vector3.up * 0.9f, 42f);
            EnemyCombatEvents.Raise(Beat(EnemyCombatEventKind.AttackCommit, actor, EnemyTier.Elite, ap + Vector3.up * 1.1f, actorFacing));
            Vector3 dustAt = ap + Vector3.Cross(Vector3.up, camBack) * 1.5f; dustAt.y = GroundY(dustAt);
            EnemyCombatEvents.Raise(Beat(EnemyCombatEventKind.SpawnRise, body, EnemyTier.Standard, dustAt, actorFacing));
            yield return new WaitForSeconds(0.12f);
            ArtVisualRegression.CaptureToPng(near, W, H, Path.Combine(dir, "01-claw-spawndust-near-0.12s.png"), true);
            Debug.Log("[DD-TEST] wp047.ad claw actor=" + (actor == body ? "synthetic" : actor.name) + " at " + ap.ToString("F2"));
            yield return new WaitForSeconds(0.5f);
            Vector3 ringAt = PlayerRegistry.Primary.transform.position;
            fx.ReportShockRing(ringAt, 4f, 0.34f);
            yield return new WaitForSeconds(0.2f);
            ArtVisualRegression.CaptureToPng(new ArtVisualRegression.CameraPose("wave3_ring", ringAt + new Vector3(0f, 6f, -8f), ringAt, 50f), W, H, Path.Combine(dir, "02-kagura-ring-0.20s.png"), true);
            yield return new WaitForSeconds(0.6f);
            if (lanterns > 0)
            {
                FlipbookEffect flame = null;
                foreach (FlipbookEffect f in vfx.FlipbookPool.Spawned) { if (f.Row != null && f.Row.Id == VfxIds.LanternFlame) { flame = f; break; } }
                Vector3 lp = p;
                float best = float.MaxValue;
                foreach (SurfaceTag tag in UnityEngine.Object.FindObjectsOfType<SurfaceTag>())
                {
                    if (tag.Family == null || tag.Family.IndexOf("lantern", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    MeshRenderer r = tag.GetComponentInChildren<MeshRenderer>();
                    if (r == null) continue;
                    float d = (r.bounds.center - origin).sqrMagnitude; if (d < best) { best = d; lp = r.bounds.center; }
                }
                ArtVisualRegression.CaptureToPng(new ArtVisualRegression.CameraPose("wave3_lantern", lp + new Vector3(0f, 0.9f, -2.2f), lp + Vector3.up * 0.5f, 40f), W, H, Path.Combine(dir, "03-lantern-flame-near.png"), true);
            }

            UnityEngine.Object.Destroy(body.gameObject);
            File.WriteAllText(Path.Combine(dir, "wave3-readout.txt"), "claws=" + binder.ClawSwipes + " spawnDust=" + binder.SpawnDusts + " steps=" + binder.FootSteps + " mags=" + binder.MagDrops + " pump=" + binder.PumpSmokes +
                " kagura=" + kagura + " ofuda=" + ofuda + " lanterns=" + lanterns + " gcBytes=" + gcBytes + "\n");
        }

        private static float LastMuzzleLife(VfxDressing vfx)
        {
            float life = -1f;
            foreach (FlipbookEffect f in vfx.FlipbookPool.Spawned)
            {
                if (f.Row != null && f.Row.Id == VfxIds.Muzzle) life = f.LifeSeconds;
            }

            return life;
        }
    }
}
