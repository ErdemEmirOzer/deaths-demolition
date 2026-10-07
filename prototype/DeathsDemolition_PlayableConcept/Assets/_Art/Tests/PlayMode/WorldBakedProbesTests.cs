#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rendering;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Tests;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// R3 #1 (S-05; C-00 07.10 decision B; measured 06.10, K/wp-038m/evidence-probe): the run loads the V6 world additively and the
    /// active scene stays "Run", so Unity leaves <c>LightmapSettings.lightProbes</c> null. The builder writes the bake's probes into the
    /// world root's <see cref="WorldBakedProbes"/>, which binds them while the world is loaded: the probes are the bake's (count), a
    /// sample differs from the ambient, the switch acts at once, leaving gives them back, a second run and the menu → run path (the
    /// menu backdrop loads the same world first) bind again with the same light.
    /// Top Down only (user 06.10): the AD frames are TD (Sinematik, Yüksek); the menu's own frames are S-09's capture.
    /// Categories sit on the methods (C-00 07.10: UTF 1.1.33 does not see a method category under a fixture category).
    /// </summary>
    public sealed class WorldBakedProbesTests
    {
        public const string DirVariable = "DD_ART_CAPTURE_DIR";
        private const float SahneZamanAsimi = 90f, KurulumZamanAsimi = 60f;
        private const int W = 1920, H = 1080;
        private static readonly Vector3 SamplePoint = new Vector3(0f, 1f, -4f);   // the run's spawn plaza
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>AD 07.10 M2 · the main hall's south threshold (R1_MainHall z 39–49 on the 1.2 m plinth): one body inside, one on the
        /// threshold, one outside, a TD pose over the threshold.</summary>
        private static readonly Vector3[] ThresholdBodies = { new Vector3(-1.5f, 1.2f, 43.5f), new Vector3(1.2f, 1.2f, 39.6f), new Vector3(-0.8f, 0f, 34.5f) };
        private static readonly Vector3 ThresholdLook = new Vector3(0f, 1f, 39f);

        private int _levelBefore;
        private CameraMode _modeBefore;
        private ClassicWeaponId _weaponBefore;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _levelBefore = QualitySettings.GetQualityLevel();
            _modeBefore = CameraModeService.Current;
            _weaponBefore = PrototypeUiFlow.SelectedWeapon;
            WorldBakedProbes.Enabled = true;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            ZombieEnemy.ResetRisePolicy();
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            WorldBakedProbes.Enabled = true;
            CameraModeService.Request(_modeBefore, instant: true);
            if (QualitySettings.GetQualityLevel() != _levelBefore) QualitySettings.SetQualityLevel(_levelBefore, true);
            PrototypeUiFlow.SelectedWeapon = _weaponBefore;
            if (SceneManager.GetActiveScene().name == PrototypeScenes.Run)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            }

            MenuCinematic.ResetForTests();
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(300000)]
        [Category("R3")]
        public IEnumerator Kosu_V6Additive_PisimisProblarBaglanir_CikistaGeriAlinir_IkinciKosuAyniIsik()
        {
            yield return RunaGir();
            Assert.AreEqual(WorldSource.V6, WorldSourceSettings.Resolve(), "varsayılan dünya V6 değil");
            Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name, "aktif sahne Run değil (kök neden koşulu değişti mi?)");

            WorldBakedProbes holder = WorldBakedProbes.Current;
            Assert.IsNotNull(holder, "V6 kökünde WorldBakedProbes yok");
            Assert.IsNotNull(holder.Probes, "pişmiş prob referansı boş (bake sonrası WriteProbeReference)");
            Assert.IsTrue(holder.IsBound, "V6'nın pişmiş probları bağlanmadı");
            Assert.AreSame(holder.Probes, LightmapSettings.lightProbes, "bağlanan nesne bileşeninki değil");
            Assert.AreEqual(holder.BakedCount, LightmapSettings.lightProbes.count, "bağlanan prob sayısı bake'inki değil");

            SphericalHarmonicsL2 ambient = RenderSettings.ambientProbe;
            float d = SampleDistance(ambient, out string sample, out Vector3 l0);
            Debug.Log("[DD-OBS] r3.probes.bound count=" + holder.Probes.count + " source=" + holder.Source + " " + sample + " |d|=" + d.ToString("0.###", Inv));
            Assert.Greater(d, 0.01f, "prob örneği ambient'ten ayırt edilemiyor (kaynak: bağlama gerçekten V6'nın mı?)");

            // the switch acts at once: off → the probes from before the world (none in the run), on → the world's again
            WorldBakedProbes.Enabled = false;
            Assert.IsNull(LightmapSettings.lightProbes, "anahtar kapalı: koşunun önceki probları (yok) geri gelmedi");
            Assert.IsFalse(holder.IsBound, "anahtar kapalı: IsBound");
            WorldBakedProbes.Enabled = true;
            Assert.IsTrue(holder.IsBound, "anahtar açık: dünyanın probları yeniden bağlanmadı");

            // the world unloaded additively (as the run's exit does) gives the previous probes (none) back
            LightProbes first = holder.Probes;
            yield return SceneManager.UnloadSceneAsync(WorldSourceSettings.V6SceneName);
            for (int i = 0; i < 2; i++) yield return null;
            Assert.IsNull(LightmapSettings.lightProbes, "dünya boşaltılınca bağ çözülmedi");
            Assert.IsNull(WorldBakedProbes.Current, "boşaltılan dünyanın bileşeni hâlâ güncel");

            PrototypeUiFlow.GoToMainMenu();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu, SahneZamanAsimi, "MainMenu");
            for (int i = 0; i < 3; i++) yield return null;

            // a second run binds again, with the same light (gt 07.10 M2)
            yield return RunaGir();
            Assert.IsNotNull(WorldBakedProbes.Current, "ikinci koşuda bileşen yok");
            Assert.IsTrue(WorldBakedProbes.Current.IsBound, "ikinci koşuda problar bağlanmadı");
            Assert.AreEqual(first.count, LightmapSettings.lightProbes.count, "ikinci koşu prob sayısı");
            float d2 = SampleDistance(RenderSettings.ambientProbe, out string sample2, out Vector3 l0b);
            Debug.Log("[DD-OBS] r3.probes.rebound count=" + LightmapSettings.lightProbes.count + " sameObject=" + ReferenceEquals(first, LightmapSettings.lightProbes) + " " + sample2);
            Assert.Greater(d2, 0.01f, "ikinci koşu: örnek ambient'ten ayırt edilemiyor");
            Assert.Less((l0 - l0b).magnitude, 0.002f, "ikinci koşunun ışığı ilkinden farklı");
        }

        /// <summary>gt 07.10 M1 · The player's path with the menu backdrop live (the menu loads the same world first): the menu binds
        /// it, the run binds its own load with the same light.</summary>
        [UnityTest]
        [Timeout(300000)]
        [Category("R3")]
        public IEnumerator MenuDunyasiAcik_SonraKosu_IkisindeDeBagli_AyniIsik()
        {
            MenuCinematic.ResetForTests();
            MenuCinematic.ForceInBatch = true;
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return Bekle(() => MenuCinematic.Current != null && MenuCinematic.Current.Live, SahneZamanAsimi, "menü dünyası canlanmadı");
            Assert.IsNotNull(WorldBakedProbes.Current, "menü dünyasında WorldBakedProbes yok");
            Assert.IsTrue(WorldBakedProbes.Current.IsBound, "menü dünyasının probları bağlanmadı");
            SampleDistance(RenderSettings.ambientProbe, out string menuSample, out Vector3 menuL0);
            Debug.Log("[DD-OBS] r3.probes.menu count=" + LightmapSettings.lightProbes.count + " " + menuSample);

            yield return RunaGir();
            Assert.IsTrue(WorldBakedProbes.Current != null && WorldBakedProbes.Current.IsBound, "menüden sonra koşuda problar bağlanmadı");
            float d = SampleDistance(RenderSettings.ambientProbe, out string runSample, out Vector3 runL0);
            Debug.Log("[DD-OBS] r3.probes.menu-run count=" + LightmapSettings.lightProbes.count + " " + runSample + " |d|=" + d.ToString("0.###", Inv));
            Assert.Greater(d, 0.01f, "menüden sonra koşu: örnek ambient'ten ayırt edilemiyor");
            Assert.Less((menuL0 - runL0).magnitude, 0.002f, "menü ve koşu aynı pişmiş ışığı görmüyor");
        }

        /// <summary>
        /// AD frames, before / after: the default V6 run with the 40-body crowd (R2ZombieCrowd.GateMix) frozen, TD only, Sinematik and
        /// Yüksek, the probes off and on — the gameplay framing (15 m up, 13 m back, 42°), a close TD view of the player and (AD M2) the
        /// main hall's threshold with a body inside, on it and outside; post on. The SH at the player, at each threshold body and at the
        /// large ground renderers' bounds centres (AD S1) goes to probe-ab.tsv / probe-points.tsv under <c>DD_ART_CAPTURE_DIR/probe-ab/</c>.
        /// </summary>
        [UnityTest]
        [Timeout(900000)]
        [Explicit("R3 #1 AD before / after frames: run by full name")]
        [Category("Diagnostics")]
        [Category("R3")]
        public IEnumerator ProbBaglama_AD_OnceSonra_TD_SinematikYuksek_SalonEsigi()
        {
            yield return RunaGir();
            Assert.IsNotNull(WorldBakedProbes.Current, "kaynak yok: WorldBakedProbes");
            EnemySpawnDirector director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            director.StopRound();
            director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 300; i++) yield return null;
            var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
            R2ZombieCrowd.Spawn(R2ZombieCrowd.GateMix, 12, crowd);
            Assert.AreEqual(R2ZombieCrowd.Size, crowd.Count, "kaynak yok: 40 zombi doğmadı");
            for (int i = 0; i < 240; i++) yield return null;

            // AD M2: three bodies at the hall threshold (the crowd's three farthest give their factory slots)
            for (int k = 0; k < ThresholdBodies.Length; k++)
            {
                ZombieEnemy farthest = null;
                float farthestSq = -1f;
                Vector3 pp = PlayerRegistry.Primary.transform.position;
                foreach (ZombieEnemy z in ZombieEnemy.Live)
                {
                    if (z == null || !z.IsSpawned) continue;
                    float sq = (z.transform.position - pp).sqrMagnitude;
                    if (sq > farthestSq) { farthestSq = sq; farthest = z; }
                }

                if (farthest != null) farthest.Remove();
            }

            yield return null;
            var threshold = new List<ZombieEnemy>();
            foreach (Vector3 at in ThresholdBodies)
            {
                Vector3 p = NavPathFollower.ClampToNavMesh(at, 1.5f);
                ZombieEnemy body = EnemyFactory.Spawn(EnemyTier.Standard, 12, p, Quaternion.LookRotation(Vector3.back, Vector3.up));
                Assert.IsNotNull(body, "kaynak yok: salon eşiği gövdesi doğmadı " + at);
                threshold.Add(body);
            }

            for (int i = 0; i < 2; i++) yield return null;
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;

            string dir = OutputDir();
            Transform player = PlayerRegistry.Primary.transform;
            Vector3 feet = player.position;
            var poses = new[]
            {
                new ArtVisualRegression.CameraPose("td", feet + new Vector3(0f, 15f, -13f), feet, 42f),
                new ArtVisualRegression.CameraPose("td-close", feet + new Vector3(0f, 4.2f, -4.4f), feet + Vector3.up, 30f),
                new ArtVisualRegression.CameraPose("td-threshold", ThresholdLook + new Vector3(0f, 15f, -13f), ThresholdLook, 42f),
            };

            var points = new List<(string name, Vector3 at)> { ("player", feet + Vector3.up) };
            for (int k = 0; k < threshold.Count; k++) points.Add(("threshold" + k, threshold[k].transform.position + Vector3.up));
            foreach (MeshRenderer r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                if (r.lightProbeUsage == LightProbeUsage.Off && (r.name == "Ground" || r.name == "Plaza_Slate" || r.name == "Karesansui_Gravel"))
                    points.Add(("bounds:" + r.name, r.bounds.center));

            var sb = new StringBuilder("tier\tstate\tpose\tframe\n");
            var pts = new StringBuilder("state\tpoint\tx\ty\tz\tL0r\tL0g\tL0b\tambientL0r\tambientL0g\tambientL0b\n");
            int frames = 0, count = QualitySettings.count;
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            foreach (QualityTier tier in new[] { QualityTier.Sinematik, QualityTier.Yuksek })
            {
                QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier(tier, count), true);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.AreEqual(tier, QualityTiers.Current, "kademe geçişi");
                foreach (string state in new[] { "off", "on" })
                {
                    WorldBakedProbes.Enabled = state == "on";
                    yield return null;
                    if (tier == QualityTier.Sinematik)
                    {
                        SphericalHarmonicsL2 amb = RenderSettings.ambientProbe;
                        foreach ((string name, Vector3 at) in points)
                        {
                            LightProbes.GetInterpolatedProbe(at, null, out SphericalHarmonicsL2 sh);
                            pts.Append(string.Join("\t", state, name, at.x.ToString("0.##", Inv), at.y.ToString("0.##", Inv), at.z.ToString("0.##", Inv),
                                sh[0, 0].ToString("0.###", Inv), sh[1, 0].ToString("0.###", Inv), sh[2, 0].ToString("0.###", Inv),
                                amb[0, 0].ToString("0.###", Inv), amb[1, 0].ToString("0.###", Inv), amb[2, 0].ToString("0.###", Inv))).Append('\n');
                        }
                    }

                    foreach (ArtVisualRegression.CameraPose pose in poses)
                    {
                        if (frames < poses.Length) yield return ArtVisualRegression.WarmStreaming(pose, W, H, 120, null);
                        string file = Path.Combine(dir, tier + "-" + pose.name + "-" + state + ".png");
                        ArtVisualRegression.CaptureToPng(pose, W, H, file, true);
                        sb.Append(string.Join("\t", tier.ToString(), state, pose.name, Path.GetFileName(file))).Append('\n');
                        frames++;
                        Assert.IsTrue(File.Exists(file), "kare yazılmadı: " + file);
                    }
                }
            }

            WorldBakedProbes.Enabled = true;
            File.WriteAllText(Path.Combine(dir, "probe-ab.tsv"), sb.ToString());
            File.WriteAllText(Path.Combine(dir, "probe-points.tsv"), pts.ToString());
            Debug.Log("[DD-OBS] r3.probes.ab frames=" + frames + " points=" + points.Count + " -> " + dir);
            Assert.AreEqual(12, frames, "kare sayısı (2 kademe × 2 durum × 3 poz)");
            foreach (ZombieEnemy z in threshold) if (z != null && z.IsSpawned) z.Remove();
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) z.Remove();
        }

        public static string OutputDir()
        {
            string dir = Environment.GetEnvironmentVariable(DirVariable);
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "r3-probe-ab");
            dir = Path.Combine(dir, "probe-ab");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>The menu → selection → run flow into the default V6 run, Fighting.</summary>
        private static IEnumerator RunaGir()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
                yield return null;
                yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
                yield return null;
            }

            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return Bekle(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded, SahneZamanAsimi, WorldSourceSettings.V6SceneName);
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, KurulumZamanAsimi, "oyuncu/kamera");
            yield return Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
        }

        /// <summary>L0 of the probe SH at the spawn plaza against the ambient's, summed over RGB; <paramref name="l0"/> is the sample's L0.</summary>
        private static float SampleDistance(SphericalHarmonicsL2 ambient, out string text, out Vector3 l0)
        {
            LightProbes.GetInterpolatedProbe(SamplePoint, null, out SphericalHarmonicsL2 sh);
            l0 = new Vector3(sh[0, 0], sh[1, 0], sh[2, 0]);
            text = "sampleL0=" + sh[0, 0].ToString("0.###", Inv) + "/" + sh[1, 0].ToString("0.###", Inv) + "/" + sh[2, 0].ToString("0.###", Inv)
                   + " ambientL0=" + ambient[0, 0].ToString("0.###", Inv) + "/" + ambient[1, 0].ToString("0.###", Inv) + "/" + ambient[2, 0].ToString("0.###", Inv);
            return Mathf.Abs(sh[0, 0] - ambient[0, 0]) + Mathf.Abs(sh[1, 0] - ambient[1, 0]) + Mathf.Abs(sh[2, 0] - ambient[2, 0]);
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string ne)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), ne + " " + saniye.ToString("0", Inv) + " s içinde gelmedi");
        }
    }
}
#endif
