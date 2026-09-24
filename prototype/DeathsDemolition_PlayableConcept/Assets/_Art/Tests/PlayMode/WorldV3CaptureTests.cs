using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-020 · Fidelity captures of world v3 (Higgsfield §5: Unity ↔ AP-005C render side by side): the four
    /// AP-005C fidelity cameras of <c>AP005-render-log-v006.json</c> (Blender pos/target/lens → Unity, 36 mm
    /// horizontal fit) plus the west-corridor gameplay framing and the WP-038 water close-up, 1920×1080 with the gameplay grade, written to
    /// <c>DD_ART_CAPTURE_DIR/world-v3/</c>. Standalone world (own bootstrap), no crowd.
    /// </summary>
    [TestFixture]
    [Category("WP020")]
    public sealed class WorldV3CaptureTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;
        private const int W = 1920, H = 1080;

        /// <summary>Vertical FOV of a Blender camera with a 36 mm horizontal sensor fit at 16:9.</summary>
        private static float VerticalFov(float lensMm)
        {
            float halfH = Mathf.Atan(18f / lensMm);
            return 2f * Mathf.Atan(Mathf.Tan(halfH) * 9f / 16f) * Mathf.Rad2Deg;
        }

        private static ArtVisualRegression.CameraPose Blender(string name, Vector3 pos, Vector3 target, float lensMm)
        {
            return new ArtVisualRegression.CameraPose(name, WorldPlacementAsset.ToUnity(pos.x, pos.y, pos.z), WorldPlacementAsset.ToUnity(target.x, target.y, target.z), VerticalFov(lensMm));
        }

        public static readonly ArtVisualRegression.CameraPose[] Poses =
        {
            Blender("fid-palace", new Vector3(-20f, 4f, 30f), new Vector3(3f, 37f, 4f), 40f),
            Blender("fid-gate-north", new Vector3(0f, -4f, 20f), new Vector3(0f, 20f, 1f), 35f),
            Blender("fid-palace-gable-west", new Vector3(-38f, 24f, 16f), new Vector3(-14f, 37f, 6.5f), 45f),
            Blender("fid-pavilion-north", new Vector3(40.5f, 40f, 6f), new Vector3(27.7f, 36.6f, 2f), 40f),
            new ArtVisualRegression.CameraPose("gameplay-west", new Vector3(-36.95f, 15f, -13.05f), new Vector3(-36.95f, 0f, 0f), 42f),
            new ArtVisualRegression.CameraPose("gameplay-north", new Vector3(0f, 15f, 11.95f), new Vector3(0f, 0f, 25f), 42f),
            // WP-038: south koi ponds 02/03 (AP005_i0094 / i0093 basins) at a low angle for the DD/Water v2 sheen, foam band and caustics.
            new ArtVisualRegression.CameraPose("water-closeup", new Vector3(-3f, 4.5f, -19.5f), new Vector3(-3f, -0.15f, -29.2f), 42f)
        };

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            RunFlowClock.ClearHolds();
            if (EnemySpawnDirector.Instance != null) EnemySpawnDirector.Instance.ForceActiveTarget(0, false);
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// WP-074 · <c>DD_ART_CAPTURE_POSES</c> (optional): a CSV "name,px,py,pz,tx,ty,tz,vfov" (Unity space) replaces the fixed poses —
        /// the gap-analysis frames (target-style overview, gameplay framing, eye-level third / first person) with the same tool.
        /// </summary>
        public static ArtVisualRegression.CameraPose[] PosesFor()
        {
            string csv = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_POSES");
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return Poses;
            var list = new List<ArtVisualRegression.CameraPose>();
            foreach (string line in File.ReadAllLines(csv))
            {
                string[] f = line.Split(',');
                if (f.Length < 8 || f[0].StartsWith("#") || f[0] == "name") continue;
                float P(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);
                list.Add(new ArtVisualRegression.CameraPose(f[0].Trim(), new Vector3(P(1), P(2), P(3)), new Vector3(P(4), P(5), P(6)), P(7)));
            }

            return list.ToArray();
        }

        public static string OutputDir()
        {
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp020-captures");
            dir = Path.Combine(dir, "world-v3");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SadakatKareleri_AP005C_4Kamera_2Oynanis_1080p()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            SceneManager.LoadScene(ArtLibraryPaths.Ap005WorldSceneName, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == ArtLibraryPaths.Ap005WorldSceneName && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => Ap005WorldBootstrap.Last.Instances > 0, KurulumZamanAsimi, "bootstrap");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null, OyuncuZamanAsimi, "oyuncu");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null, KurulumZamanAsimi, "run sistemleri");
            EnemySpawnDirector.Instance.ForceActiveTarget(0, false);
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;

            string dir = OutputDir();
            var manifest = new List<string> { "name,position,lookAt,vfov,file,warmFrames" };
            Time.captureDeltaTime = 1f / 60f;
            foreach (ArtVisualRegression.CameraPose pose in PosesFor())
            {
                int warm = 0;
                yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);
                Time.timeScale = 0f;
                try
                {
                    string file = Path.Combine(dir, pose.name + ".png");
                    Texture2D t = ArtVisualRegression.Capture(pose, W, H, true);
                    try { File.WriteAllBytes(file, t.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(t); }
                    manifest.Add(pose.name + "," + V(pose.position) + "," + V(pose.lookAt) + "," + pose.verticalFov.ToString("F1", CultureInfo.InvariantCulture) + "," + Path.GetFileName(file) + "," + warm);
                }
                finally
                {
                    Time.timeScale = 1f;
                }
            }

            Time.captureDeltaTime = 0f;
            File.WriteAllLines(Path.Combine(dir, "world-v3-manifest.csv"), manifest);
            Debug.Log("[DD-TEST] wp020.captures frames=" + PosesFor().Length + " instances=" + Ap005WorldBootstrap.Last.Instances + " -> " + dir);
            Assert.AreEqual(3168, Ap005WorldBootstrap.Last.Instances);   // WP-038I: AP-017 Faz 2 delta v005 (+23); WP-038L: Faz 2b delta v006 (+25)
            foreach (ArtVisualRegression.CameraPose pose in PosesFor()) Assert.IsTrue(File.Exists(Path.Combine(dir, pose.name + ".png")), pose.name);
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("F2", CultureInfo.InvariantCulture) + " " + v.y.ToString("F2", CultureInfo.InvariantCulture) + " " + v.z.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }
    }
}
