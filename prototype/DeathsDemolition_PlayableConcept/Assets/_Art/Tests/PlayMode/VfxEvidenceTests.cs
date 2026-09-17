using System;
using System.Collections;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
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
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-019 · §5 fidelity evidence: close-up captures of every AP-010 family in the Run
    /// world at their peak frames, for the art-director's side-by-side with the AP-010
    /// previews. Asserts only that the frames were written and are not black.
    /// </summary>
    [TestFixture]
    [Category("WP019")]
    public sealed class VfxEvidenceTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;
        private const int W = 1280, H = 720;
        private EnemySpawnDirector _director;

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); }

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
        }

        [TearDown] public void TearDown() { LogAssert.ignoreFailingMessages = false; }

        private static EnemyHitFeedbackInfo Info(Vector3 point, Transform root, bool armored, bool broke, bool killed, float health)
        {
            var r = new DamageResult { HealthDamage = health, ArmorDamage = armored ? 5f : 0f, ArmorBroke = broke, Killed = killed, WasArmored = armored, Region = BodyRegion.UpperBody };
            return new EnemyHitFeedbackInfo { Point = point, Direction = Vector3.forward, Normal = Vector3.back, Result = r, Power = WeaponPower.Medium, TargetId = 4243, TargetRoot = root, Weight = FeedbackWeight.Standard, ImpactForce = 1f };
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Run_AP010_AileYakinPlanKareleri()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && HitFeedbackDirector.Instance != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;

            HitFeedbackDirector fx = HitFeedbackDirector.Instance;
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp019-captures");
            dir = Path.Combine(dir, "families"); Directory.CreateDirectory(dir);

            Vector3 p = PlayerRegistry.Primary.transform.position + new Vector3(0f, 0f, 4f);
            // Close pose: 4 m up, 5 m back, looking at the stage point (effects billboard to the gameplay camera; the
            // capture camera sits near it so the sheets face us).
            var pose = new ArtVisualRegression.CameraPose("vfx_closeup", p + new Vector3(0f, 2.6f, -4.5f), p + Vector3.up * 0.8f, 42f);
            var rootGo = new GameObject("wp019-stage"); rootGo.transform.position = p;

            // Impacts (five surfaces in a row), captured at ~frame 3.
            for (int i = 0; i < 5; i++)
            {
                SurfaceKind s = new[] { SurfaceKind.Stone, SurfaceKind.Wood, SurfaceKind.Dirt, SurfaceKind.Flesh, SurfaceKind.Metal }[i];
                fx.ReportSurfaceImpact(p + new Vector3(-1.6f + i * 0.8f, 0.9f, 0f), Vector3.back, s, 1f);
            }
            yield return Wait(0.1f);
            yield return Shoot(pose, Path.Combine(dir, "01-impacts-5-surfaces-0.10s.png"));
            yield return Wait(0.6f);
            yield return Shoot(pose, Path.Combine(dir, "01-impacts-decals-after.png"));

            // Muzzle at the stage point.
            var muzzle = new GameObject("wp019-muzzle").transform; muzzle.position = p + Vector3.up * 1.1f; muzzle.rotation = Quaternion.LookRotation(Vector3.back);
            fx.ReportMuzzleFlash(muzzle, 1f, 1f);
            yield return null;
            yield return Shoot(pose, Path.Combine(dir, "02-muzzle-0.03s.png"));
            yield return Wait(0.3f);

            // Explosion: peak fireball and plume.
            fx.ReportExplosion(p + Vector3.forward * 1.5f, 2.5f);
            yield return Wait(0.13f);
            yield return Shoot(pose, Path.Combine(dir, "03-explosion-0.13s.png"));
            yield return Wait(0.37f);
            yield return Shoot(pose, Path.Combine(dir, "03-explosion-0.50s.png"));
            yield return Wait(0.5f);
            yield return Shoot(pose, Path.Combine(dir, "03-explosion-1.00s-smoke-scorch.png"));
            yield return Wait(1f);

            // Armour break: sheet + splinter + fragments (plate hook).
            fx.ReportEnemyHit(Info(p + Vector3.up * 1.2f, rootGo.transform, true, true, false, 0f));
            fx.Vfx.PlayArmorFragments(p + Vector3.up * 1.2f, Vector3.back);
            yield return Wait(0.12f);
            yield return Shoot(pose, Path.Combine(dir, "04-armorbreak-0.12s.png"));
            yield return Wait(0.6f);

            // Zombie death: sheet at the feet + splat.
            fx.ReportEnemyHit(Info(p + Vector3.up * 1.0f, rootGo.transform, false, false, true, 30f));
            yield return Wait(0.2f);
            yield return Shoot(pose, Path.Combine(dir, "05-zombiedeath-0.20s.png"));
            yield return Wait(0.5f);
            yield return Shoot(pose, Path.Combine(dir, "05-zombiedeath-splat-after.png"));

            // Gold pack: kill a real zombie at the stage, its packet drops there.
            _director.ForceActiveTarget(2, true);
            yield return CharacterTests.Bekle(() => ZombieEnemy.Live.Count >= 1, 60f, "zombi");
            ZombieEnemy z = ZombieEnemy.Live[0];
            z.transform.position = p + Vector3.right * 1.2f;
            yield return null;
            z.Kill();
            yield return CharacterTests.Bekle(() => GoldService.Instance != null && GoldService.Instance.LiveOrbCount >= 1, 10f, "gold");
            yield return Wait(0.4f);
            yield return Shoot(pose, Path.Combine(dir, "06-goldpack-sparkle.png"));
            _director.ForceActiveTarget(0, false);

            // Beacon at the stage point.
            PrototypePool<BeaconEffect> beacons = PoolService.GetOrCreate(FeedbackEffectFactory.BeaconTemplate, 1, 2, PoolExhaustedPolicy.RecycleOldest);
            BeaconEffect beacon = beacons.Get(p + Vector3.left * 1.5f, Quaternion.identity);
            beacon.Begin(12f, 0.5f, VfxLibrary.BeaconTint, new Color(1f, 0.36f, 0.2f, 0.5f));
            yield return Wait(0.5f);
            var wide = new ArtVisualRegression.CameraPose("vfx_beacon", p + new Vector3(0f, 4f, -9f), p + Vector3.up * 2.5f, 42f);
            yield return Shoot(wide, Path.Combine(dir, "07-beacon-column-ring.png"));
            beacon.Despawn();

            // Vignette: a screen-space overlay canvas (no camera capture reaches it; WaitForEndOfFrame does not fire in
            // batchmode). Evidence = the alpha the layer reports after a hit + the offline composite in evidence/.
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            PlayerRegistry.Primary.Health.ApplyDamage(5f, p, "wp019-evidence");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            DamageVignetteLayer vignette = UnityEngine.Object.FindObjectOfType<DamageVignetteLayer>();
            float peak = 0f;
            for (int i = 0; i < 8; i++) { yield return null; if (vignette != null) peak = Mathf.Max(peak, vignette.ShownAlpha); }
            File.WriteAllText(Path.Combine(dir, "08-damage-vignette-alpha.txt"), "peak alpha after one 5-damage hit: " + peak.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " (tint Vermilion600, AP010_vignette_damage_1024)\n");

            // WP-019b · Blood (directive §10 quality gate: close-up 2 m and the gameplay camera 20 m, same events).
            // Stage a fresh spot 6 m to the right so the earlier families do not sit under the marks.
            Vector3 b = p + Vector3.right * 6f;
            b.y = Physics.Raycast(b + Vector3.up * 3f, Vector3.down, out RaycastHit bh, 10f, ~0, QueryTriggerInteraction.Ignore) ? bh.point.y : b.y;
            var near = new ArtVisualRegression.CameraPose("blood_near", b + new Vector3(0f, 1.4f, -2.0f), b + Vector3.up * 0.6f, 42f);
            var game = new ArtVisualRegression.CameraPose("blood_game", b + new Vector3(0f, 15f, -13f), b + Vector3.up * 1f, 42f);
            // Three normal hits, one critical, one armour break, then a death — 10 hits/s pacing across frames.
            for (int i = 0; i < 3; i++)
            {
                fx.ReportEnemyHit(Hit(b + new Vector3(-0.8f + i * 0.8f, 0.7f, 0.4f), Vector3.forward, false, 12f, 9100 + i, false));
                yield return null;
            }

            fx.ReportEnemyHit(Hit(b + new Vector3(0f, 1.1f, -0.4f), Vector3.forward, false, 45f, 9200, true));
            yield return Wait(0.10f);
            yield return Shoot(near, Path.Combine(dir, "09-blood-hits-near-0.10s.png"));
            yield return Shoot(game, Path.Combine(dir, "09-blood-hits-game-0.10s.png"));
            yield return Wait(0.5f);
            // AD M1: a hit from the side (shot travelling +X across the screen) so the splash axis can be read at 20 m; mid-sheet frame.
            fx.ReportEnemyHit(Hit(b + new Vector3(-0.6f, 0.9f, 0.6f), Vector3.right, false, 45f, 9250, true));
            yield return Wait(0.25f);
            yield return Shoot(near, Path.Combine(dir, "09-blood-hit-side-near-0.25s.png"));
            yield return Shoot(game, Path.Combine(dir, "09-blood-hit-side-game-0.25s.png"));
            yield return Wait(0.6f);
            fx.ReportEnemyHit(Info(b + Vector3.up * 1.2f, rootGo.transform, true, true, false, 0f));   // armour break (blood under the plate)
            yield return Wait(0.12f);
            yield return Shoot(near, Path.Combine(dir, "09-blood-armorbreak-near-0.12s.png"));
            yield return Wait(0.5f);
            BloodSystem blood = fx.Vfx.Blood;
            int live0 = blood.LiveMarks, merged0 = blood.Merged, evicted0 = blood.Evicted, pools0 = blood.Pools;
            fx.ReportEnemyHit(Hit(b + Vector3.up * 1.0f, Vector3.forward, true, 30f, 9300, false));   // death: sheet + cards + droplets + pool
            yield return Wait(0.15f);
            int live1 = blood.LiveMarks;
            yield return Shoot(near, Path.Combine(dir, "09-blood-death-near-0.15s.png"));
            yield return Shoot(game, Path.Combine(dir, "09-blood-death-game-0.15s.png"));
            yield return Wait(3.2f);
            yield return Shoot(near, Path.Combine(dir, "09-blood-marks-pool-near-3.4s.png"));
            yield return Shoot(game, Path.Combine(dir, "09-blood-marks-pool-game-3.4s.png"));
            FeedbackCameraRef.TryGetPosition(out Vector3 camPos);
            var poolDump = new System.Text.StringBuilder();
            foreach (DecalEffect dd in blood.DecalPool.Spawned)
            {
                var rr = dd.GetComponentInChildren<MeshRenderer>();
                var bl = new MaterialPropertyBlock(); if (rr != null) rr.GetPropertyBlock(bl);
                poolDump.Append(dd.EvictionClass).Append('@').Append(dd.transform.position.ToString("F2")).Append(" size=").Append(dd.CurrentSize.ToString("F2"))
                    .Append(" quadScale=").Append(dd.Quad != null ? dd.Quad.localScale.ToString("F2") : "-").Append(" rEnabled=").Append(rr != null && rr.enabled).Append(" visible=").Append(rr != null && rr.isVisible)
                    .Append(" alpha=").Append(bl.GetFloat("_Alpha").ToString("F2")).Append(" tint=").Append(bl.GetColor("_Tint").ToString("F2")).Append(" fading=").Append(dd.IsFading).Append(" age=").Append(dd.AgeSeconds.ToString("F1")).Append(" | ");
            }

            File.WriteAllText(Path.Combine(dir, "09-blood-readout.txt"),
                "splashes=" + blood.Splashes + " droplets=" + blood.DropletBursts + " marks=" + blood.Marks + " pools=" + blood.Pools + " (stage +" + (blood.Pools - pools0) + ") live " + live0 + "→" + live1 + "→" + blood.LiveMarks +
                " evicted +" + (blood.Evicted - evicted0) + " merged +" + (blood.Merged - merged0) + " skippedGold=" + blood.SkippedForGold + " camDist=" + Vector3.Distance(camPos, b).ToString("F1") + " round=" + RunDirector.Instance.Round +
                " dropletsGpuInstanced=" + (blood.DropletPool != null && blood.DropletPool.Spawned.Count > 0 ? blood.DropletPool.Spawned[0].GpuInstanced.ToString() : "n/a") + "\nlive marks at 3.4 s: " + poolDump + "\n");

            UnityEngine.Object.Destroy(rootGo); UnityEngine.Object.Destroy(muzzle.gameObject);
            Debug.Log("[DD-TEST] wp019.evidence dir=" + dir + " blood splashes=" + blood.Splashes + " marks=" + blood.Marks + " pools=" + blood.Pools);
        }

        private static EnemyHitFeedbackInfo Hit(Vector3 point, Vector3 direction, bool killed, float health, int id, bool head)
        {
            var r = new DamageResult { HealthDamage = health, Killed = killed, Region = head ? BodyRegion.Head : BodyRegion.UpperBody };
            return new EnemyHitFeedbackInfo
            {
                Point = point, Direction = direction, Normal = -direction, Result = r, Power = WeaponPower.Medium,
                TargetId = id, TargetRoot = null, Weight = FeedbackWeight.Standard, ImpactForce = 1f
            };
        }

        private static IEnumerator Wait(float seconds)
        {
            float t0 = Time.time; while (Time.time - t0 < seconds) yield return null;
        }

        private static IEnumerator Shoot(ArtVisualRegression.CameraPose pose, string path)
        {
            ArtVisualRegression.CaptureToPng(pose, W, H, path, true);
            Assert.IsTrue(File.Exists(path), path);
            yield return null;
        }
    }
}
