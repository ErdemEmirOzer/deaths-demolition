using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-029 · The look reference set (ADR-029-1): eight frames of the real Run + World_AP005
    /// with the real characters — five region gameplay framings, the Presentation A overview,
    /// the 40-zombie crowd (natural armour roll) and a player close-up — each captured twice at
    /// 1920×1080: <c>raw</c> (no post-processing) and <c>post</c> (the Global Volume grade as the
    /// gameplay camera renders it). The <c>post</c> set is what Lightroom grades and what the LUT
    /// is measured against; the <c>raw</c> set is the A/B floor. Repeatable: same poses, same
    /// seeds, PNGs under <c>DD_ART_CAPTURE_DIR/look-ref/</c> (or the temp cache).
    /// </summary>
    [TestFixture]
    [Category("WP029")]
    public sealed class LookReferenceTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, DolusZamanAsimi = 90f, YatismaZamanAsimi = 30f;
        private const int W = 1920, H = 1080;
        private EnemySpawnDirector _director;

        public static readonly ArtVisualRegression.CameraPose[] WorldPoses =
        {
            new ArtVisualRegression.CameraPose("center", new Vector3(0f, 15f, -13.05f), new Vector3(0f, 0f, 0f), 42f),
            new ArtVisualRegression.CameraPose("north", new Vector3(0f, 15f, 11.95f), new Vector3(0f, 0f, 25f), 42f),
            new ArtVisualRegression.CameraPose("east", new Vector3(21.45f, 15f, 0.8f), new Vector3(34.5f, 0f, 0.8f), 42f),
            new ArtVisualRegression.CameraPose("south", new Vector3(4f, 15f, -15.95f), new Vector3(4f, 0f, -29f), 42f),
            new ArtVisualRegression.CameraPose("west", new Vector3(-36.95f, 15f, -13.05f), new Vector3(-36.95f, 0f, 0f), 42f),
            new ArtVisualRegression.CameraPose("presentation", new Vector3(-45f, 30f, -75f), new Vector3(5f, 0f, 0f), 42f)
        };

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _director = null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            if (_director != null) _director.ForceActiveTarget(0, false);
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
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

        public static string OutputDir()
        {
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp029-captures");
            dir = Path.Combine(dir, "look-ref");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator ReferansKareleri_8Kadraj_HamVePost_1080p()
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

            // The crowd: 40 through the director (natural armour roll — the mix the AD asked for).
            _director.ForceActiveTarget(40, true);
            yield return CharacterTests.Bekle(() => _director.ActiveCount >= 40, DolusZamanAsimi, "40 zombi doğmadı");
            // WP-030/040 spawn the crowd out of sight and let it rise/approach: wait until it has closed in on the
            // player (≥ 20 within 14 m) or 25 s, so the crowd framing shows a crowd, not an empty courtyard.
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 25f)
            {
                int near = 0;
                Vector3 pp = PlayerRegistry.Primary.transform.position;
                foreach (ZombieEnemy z in ZombieEnemy.Live) if (z != null && z.IsAlive && (z.transform.position - pp).sqrMagnitude < 14f * 14f) near++;
                if (near >= 20 && Time.realtimeSinceStartup - t1 > 6f) break;
                yield return null;
            }

            // The close-up frames the visual, not the controller root (the animated model can lead/lag the rig).
            Vector3 feet = PlayerRegistry.Primary.transform.position;
            var playerVisual = PlayerRegistry.Primary.GetComponentInChildren<SkinnedMeshRenderer>();
            if (playerVisual != null) { Bounds pb = playerVisual.bounds; feet = new Vector3(pb.center.x, pb.min.y, pb.center.z); }
            var poses = new List<ArtVisualRegression.CameraPose>(WorldPoses)
            {
                CharacterTests.GameplayPose("crowd40"),
                new ArtVisualRegression.CameraPose("player-closeup", feet + new Vector3(0f, 4.2f, -4.4f), feet + Vector3.up * 1.0f, 30f)
            };

            string dir = OutputDir();
            var manifest = new List<string> { "name,pose_position,pose_lookAt,fov,raw,post,warmFrames" };
            int armored = 0, alive = 0;
            foreach (ZombieEnemy z in ZombieEnemy.Live) { if (z != null && z.IsAlive) { alive++; if (z.Armor != null && z.Armor.EquippedCount > 0) armored++; } }

            // Freeze time for the whole set so every pose sees the same crowd frame.
            Time.captureDeltaTime = 1f / 60f;
            foreach (ArtVisualRegression.CameraPose pose in poses)
            {
                int warm = 0;
                yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);
                Time.timeScale = 0f;
                try
                {
                    string raw = Path.Combine(dir, pose.name + "-raw.png");
                    string post = Path.Combine(dir, pose.name + "-post.png");
                    Texture2D a = ArtVisualRegression.Capture(pose, W, H, false);
                    try { File.WriteAllBytes(raw, a.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(a); }
                    Texture2D b = ArtVisualRegression.Capture(pose, W, H, true);
                    try { File.WriteAllBytes(post, b.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(b); }
                    manifest.Add(pose.name + "," + V(pose.position) + "," + V(pose.lookAt) + "," + pose.verticalFov.ToString("F0", CultureInfo.InvariantCulture) + "," + Path.GetFileName(raw) + "," + Path.GetFileName(post) + "," + warm);
                }
                finally
                {
                    Time.timeScale = 1f;
                }
            }

            Time.captureDeltaTime = 0f;
            File.WriteAllLines(Path.Combine(dir, "look-ref-manifest.csv"), manifest);
            Debug.Log("[DD-TEST] wp029.lookref frames=" + poses.Count + "x2 alive=" + alive + " armored=" + armored + " -> " + dir);
            Assert.AreEqual(8, poses.Count);
            foreach (ArtVisualRegression.CameraPose pose in poses)
            {
                Assert.IsTrue(File.Exists(Path.Combine(dir, pose.name + "-raw.png")), pose.name + " raw");
                Assert.IsTrue(File.Exists(Path.Combine(dir, pose.name + "-post.png")), pose.name + " post");
            }

            _director.ForceActiveTarget(0, false);
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("F2", CultureInfo.InvariantCulture) + " " + v.y.ToString("F2", CultureInfo.InvariantCulture) + " " + v.z.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }
    }
}
