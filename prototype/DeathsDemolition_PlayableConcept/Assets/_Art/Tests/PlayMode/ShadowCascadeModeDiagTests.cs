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
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rendering;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.Tests;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-070 · S-05 (C-00 06.10) · Shadow cascades per quality tier × camera mode as the running game sees them, with
    /// <see cref="QualityModeApplier"/> off (the assets' own counts = the behaviour before the fix) and on.
    /// <para>Table: the active URP asset's shadowCascadeCount / shadowDistance and QualitySettings.lodBias next to the contract
    /// value (QualityTiers.ShadowCascades / ShadowDistance / LodBias of the mode's CameraModeProfile).</para>
    /// <para>V6 crowd: the default world, 40 zombies (R2ZombieCrowd.GateMix 22/10/6/2) frozen in one frame; per Sinematik / Yüksek ×
    /// TD / TP / FP × applier off / on the render counters of the gameplay camera (load-independent, book §5.10) and an AD frame
    /// (ArtVisualRegression, post on).</para>
    /// Rows as [DD-OBS] lines and TSVs under <c>DD_S05_CASCADE_DIR</c>. Diagnostic, run by full name.
    /// </summary>
    [Explicit("WP-070 S-05 cascade diagnostic: run by full name")]
    [Category("Diagnostics")]
    public sealed class ShadowCascadeModeDiagTests
    {
        public const string DirVariable = "DD_S05_CASCADE_DIR";
        private const float SahneZamanAsimi = 120f, KurulumZamanAsimi = 60f;
        private const int W = 1920, H = 1080;

        private static readonly CameraMode[] Modes = { CameraMode.TopDown, CameraMode.ThirdPerson, CameraMode.FirstPerson };
        private static readonly QualityTier[] Tiers = { QualityTier.Performans, QualityTier.Yuksek, QualityTier.Sinematik };

        private int _levelBefore;
        private CameraMode _modeBefore;
        private int _frameRateBefore;
        private bool _applierBefore;
        private ClassicWeaponId _weaponBefore;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _levelBefore = QualitySettings.GetQualityLevel();
            _modeBefore = CameraModeService.Current;
            _frameRateBefore = Application.targetFrameRate;
            _applierBefore = QualityModeApplier.Enabled;
            _weaponBefore = PrototypeUiFlow.SelectedWeapon;
            Application.targetFrameRate = -1;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            ZombieEnemy.ResetRisePolicy();
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            QualityModeApplier.Enabled = _applierBefore;
            CameraModeService.Request(_modeBefore, instant: true);
            if (QualitySettings.GetQualityLevel() != _levelBefore) QualitySettings.SetQualityLevel(_levelBefore, true);
            QualityModeApplier.ApplyNow();
            PrototypeUiFlow.SelectedWeapon = _weaponBefore;
            Application.targetFrameRate = _frameRateBefore;
            if (SceneManager.GetActiveScene().name == PrototypeScenes.Run)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        public static string OutputDir()
        {
            string dir = Environment.GetEnvironmentVariable(DirVariable);
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "s05-cascades");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ------------------------------------------------------------------------------------------------ table

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Kaskad_KademeModTablosu_OlcumYazilir()
        {
            var sb = new StringBuilder("pass\ttier\tmode\tlevel\tasset\tcascades\tprofileCascades\tcontractCascades\tshadowDistance\tprofileShadowDistance\tcontractShadowDistance\tlodBias\tcontractLodBias\n");
            int[] rows = { 0 };

            // off: every asset back to its file value, nothing writes — what main rendered before the fix
            QualityModeApplier.Enabled = false;
            QualityModeApplier.RestoreOriginals();
            yield return Table("off", sb, rows);

            // on: one ApplyNow (re-arm), then the level / mode events alone
            QualityModeApplier.Enabled = true;
            QualityModeApplier.ApplyNow();
            yield return Table("on", sb, rows);

            string path = Path.Combine(OutputDir(), "cascade-table.tsv");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DD-OBS] s05.cascade.table rows=" + rows[0] + " -> " + path);
            // the source is in the record: every pass × tier × mode row read a URP asset
            Assert.AreEqual(2 * Tiers.Length * Modes.Length, rows[0], "kaynak yok: her geçiş × kademe × mod satırı URP varlığı okumadı");
        }

        /// <summary>One pass over every tier × mode: switch the level, then the mode (instant), read the active pipeline asset.</summary>
        private static IEnumerator Table(string pass, StringBuilder sb, int[] rows)
        {
            int count = QualitySettings.count;
            foreach (QualityTier tier in Tiers)
            {
                int level = QualityTiers.LevelOfTier(tier, count);
                if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
                yield return null;
                Assert.AreEqual(tier, QualityTiers.Current, "kademe geçişi");
                foreach (CameraMode mode in Modes)
                {
                    CameraModeService.Request(mode, instant: true);
                    yield return null;
                    Assert.AreEqual(mode, CameraModeService.Current, "mod geçişi (kilit?)");
                    var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    Assert.IsNotNull(asset, "etkin URP varlığı yok (" + tier + ")");
                    CameraModeProfile profile = CameraModeService.CurrentProfile;
                    string row = string.Join("\t", pass, tier.ToString(), mode.ToString(), level.ToString(CultureInfo.InvariantCulture), asset.name,
                        asset.shadowCascadeCount.ToString(CultureInfo.InvariantCulture),
                        profile.ShadowCascades.ToString(CultureInfo.InvariantCulture),
                        QualityTiers.ShadowCascades(tier, profile.ShadowCascades).ToString(CultureInfo.InvariantCulture),
                        asset.shadowDistance.ToString("0.##", CultureInfo.InvariantCulture),
                        profile.ShadowDistance.ToString("0.##", CultureInfo.InvariantCulture),
                        QualityTiers.ShadowDistance(tier, profile.ShadowDistance).ToString("0.##", CultureInfo.InvariantCulture),
                        QualitySettings.lodBias.ToString("0.##", CultureInfo.InvariantCulture),
                        QualityTiers.LodBias(tier, profile.LodBiasFactor).ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(row).Append('\n');
                    rows[0]++;
                    Debug.Log("[DD-OBS] s05.cascade " + row.Replace('\t', ' '));
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ V6 crowd

        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator Kaskad_V6_40Zombi_SayaclarVeKareler()
        {
            // the player's default path: no world override (V6, World_V6_Greybox), menu flow into a run
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            Assert.AreEqual(WorldSource.V6, WorldSourceSettings.Resolve(), "varsayılan dünya V6 değil");
            yield return Bekle(() => SceneManager.GetSceneByName(WorldSourceSettings.V6SceneName).isLoaded, SahneZamanAsimi, WorldSourceSettings.V6SceneName);
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, KurulumZamanAsimi, "oyuncu/kamera");
            yield return Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            EnemySpawnDirector director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;

            // the R2 crowd (Z3b): director stopped, fixed mix around the player, 4 s at a fixed step to close in, then frozen —
            // every tier × mode × applier state renders this same frame
            director.StopRound();
            director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 300; i++) yield return null;
            var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
            R2ZombieCrowd.Spawn(R2ZombieCrowd.GateMix, 12, crowd);
            Assert.AreEqual(R2ZombieCrowd.Size, crowd.Count, "kaynak yok: 40 zombi doğmadı");
            for (int i = 0; i < 240; i++) yield return null;
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;
            int alive = 0;
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) alive++;
            Assert.AreEqual(R2ZombieCrowd.Size, alive, "kaynak yok: donmadan önce 40 zombinin hepsi sahnede değil");

            string dir = OutputDir();
            string frames = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frames);
            var sb = new StringBuilder("tier\tmode\tapplier\tasset\tcascades\tshadowDistance\tsetPass\tdraws\tbatches\tshadowCasters\tvisibleZombieRenderers\tcamPos\tframe\n");
            int rows = 0;
            long minSetPass = long.MaxValue, minDraws = long.MaxValue;
            Transform player = PlayerRegistry.Primary.transform;
            int count = QualitySettings.count;
            var warmed = new HashSet<CameraMode>();
            foreach (QualityTier tier in new[] { QualityTier.Sinematik, QualityTier.Yuksek })
            {
                QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier(tier, count), true);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.AreEqual(tier, QualityTiers.Current, "kademe geçişi");
                foreach (CameraMode mode in Modes)
                {
                    CameraModeService.Request(mode, instant: true);
                    for (int i = 0; i < 5; i++) yield return null;
                    Assert.AreEqual(mode, CameraModeService.Current, "mod geçişi");
                    Camera cam = PrototypeCameraRig.Active.Camera;
                    // AD poses: TD the gameplay framing (15 m up, 13 m back, 42°) over the player; TP / FP the live rig camera
                    ArtVisualRegression.CameraPose pose = mode == CameraMode.TopDown
                        ? new ArtVisualRegression.CameraPose("td", player.position + new Vector3(0f, 15f, -13f), player.position, 42f)
                        : new ArtVisualRegression.CameraPose(mode == CameraMode.ThirdPerson ? "tp" : "fp", cam.transform.position, cam.transform.position + cam.transform.forward * 20f, cam.fieldOfView);
                    if (warmed.Add(mode)) yield return ArtVisualRegression.WarmStreaming(pose, W, H, 120, null);
                    int visible = VisibleZombieRenderers(cam);
                    foreach (bool on in new[] { false, true })
                    {
                        QualityModeApplier.Enabled = on;
                        if (on) QualityModeApplier.ApplyNow();
                        else QualityModeApplier.RestoreOriginals();
                        yield return null;
                        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                        Assert.IsNotNull(asset, "etkin URP varlığı yok");
                        var c = new long[4];
                        yield return Counters(cam, c);
                        string file = Path.Combine(frames, tier + "-" + mode + "-" + (on ? "on" : "off") + ".png");
                        ArtVisualRegression.CaptureToPng(pose, W, H, file, true);
                        string row = string.Join("\t", tier.ToString(), mode.ToString(), on ? "on" : "off", asset.name,
                            asset.shadowCascadeCount.ToString(CultureInfo.InvariantCulture), asset.shadowDistance.ToString("0.##", CultureInfo.InvariantCulture),
                            c[0].ToString(CultureInfo.InvariantCulture), c[1].ToString(CultureInfo.InvariantCulture), c[2].ToString(CultureInfo.InvariantCulture), c[3].ToString(CultureInfo.InvariantCulture),
                            visible.ToString(CultureInfo.InvariantCulture), cam.transform.position.ToString("F2"), Path.GetFileName(file));
                        sb.Append(row).Append('\n');
                        rows++;
                        minSetPass = Math.Min(minSetPass, c[0]);
                        minDraws = Math.Min(minDraws, c[1]);
                        Debug.Log("[DD-OBS] s05.cascade.v6 " + row.Replace('\t', ' '));
                    }
                }
            }

            string path = Path.Combine(dir, "cascade-v6-counters.tsv");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DD-OBS] s05.cascade.v6 rows=" + rows + " zombies=" + alive + " -> " + path);
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) z.Remove();
            // the source is in the record: the crowd was there and every frame was drawn
            Assert.AreEqual(12, rows, "satır sayısı");
            Assert.Greater(minSetPass, 0, "kaynak yok: SetPass 0 (kare render edilmedi)");
            Assert.Greater(minDraws, 0, "kaynak yok: draw 0");
        }

        private static int VisibleZombieRenderers(Camera cam)
        {
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
            int visible = 0;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null) continue;
                foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false)) if (r.enabled && GeometryUtility.TestPlanesAABB(frustum, r.bounds)) visible++;
            }

            return visible;
        }

        /// <summary>
        /// Render counters of one hand-rendered frame of the gameplay camera (the R2 Z3b method: 1080p, MSAA 2×, the camera's own
        /// render off, median of 12 frames after 2 warm-up). into = setPass, draws, batches, shadow casters (−1: no such counter).
        /// </summary>
        private static IEnumerator Counters(Camera cam, long[] into)
        {
            var rt = new RenderTexture(W, H, 24) { name = "S-05 cascade frame", antiAliasing = 2 };
            rt.Create();
            var sync = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "S-05 cascade sync" };
            sync.Create();
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            bool wasEnabled = cam.enabled;
            var sp = new List<long>(); var dr = new List<long>(); var ba = new List<long>(); var sc = new List<long>();
            try
            {
                cam.enabled = false;
                yield return null;
                for (int k = 0; k < 14; k++)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    Graphics.Blit(rt, sync);
                    AsyncGPUReadback.Request(sync, 0, TextureFormat.RGBA32).WaitForCompletion();
                    cam.targetTexture = null;
                    yield return null;
                    if (k < 2) continue;
                    sp.Add(setPass.LastValue); dr.Add(draws.LastValue); ba.Add(batches.LastValue);
                    if (casters.Valid) sc.Add(casters.LastValue);
                }
            }
            finally
            {
                setPass.Dispose(); draws.Dispose(); batches.Dispose(); casters.Dispose();
                cam.targetTexture = null;
                cam.enabled = wasEnabled;
                rt.Release(); sync.Release();
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(sync);
            }

            into[0] = Median(sp); into[1] = Median(dr); into[2] = Median(ba); into[3] = sc.Count > 0 ? Median(sc) : -1;
        }

        private static long Median(List<long> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t < saniye) yield return null;
            Assert.IsTrue(kosul(), aciklama + " (" + saniye + " s)");
        }
    }
}
#endif
