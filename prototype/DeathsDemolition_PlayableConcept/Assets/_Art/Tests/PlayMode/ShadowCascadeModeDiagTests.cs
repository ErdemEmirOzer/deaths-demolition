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
    /// <para>AD MUST-2 (06.10): the cascade boundaries are read back from URP itself — the culling spheres
    /// (_CascadeShadowSplitSpheres0..3) the frame's camera rendered with — and written as columns (radius, texel, where the player
    /// and the ≤ 10 m bodies sit) and as a cascade map over the frame (ray per 4 px block; the ground shaders have no DEBUG_DISPLAY
    /// path, so URP's own Shadow Cascades view would leave the ground uncoloured). A ±15° TP orbit (Cinematic, High, applier on)
    /// shows the boundary under motion. AD MUST-3: High FP close contact — one body at 1.5–3 m, its shadow in the frame, the live
    /// gameplay camera (viewmodel included) at 2 / 3 / 4 cascades.</para>
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

        /// <summary>The AD's close band: bodies within this ground distance of the player.</summary>
        public const float NearBandMetres = 10f;
        public const int PanFrames = 60;
        public const float PanHalfDegrees = 15f;
        public const float ContactDistance = 2.5f;
        public const float ContactPitch = 12f;
        private const int MapStep = 4;
        private const string CascadeHeader = "res\ttile\tr0\tr1\tr2\tr3\ttexel0cm\ttexel1cm\ttexel2cm\ttexel3cm\tratio01\tratio12\tplayerCascade\tplayerEdgeM\tnear10\tnearCascades\tnearEdgeMinM";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly int[] SphereIds =
        {
            Shader.PropertyToID("_CascadeShadowSplitSpheres0"), Shader.PropertyToID("_CascadeShadowSplitSpheres1"),
            Shader.PropertyToID("_CascadeShadowSplitSpheres2"), Shader.PropertyToID("_CascadeShadowSplitSpheres3"),
        };
        private static readonly Color32[] CascadeTint =
        {
            new Color32(230, 60, 60, 255), new Color32(60, 200, 60, 255), new Color32(60, 110, 240, 255), new Color32(240, 210, 40, 255),
        };

        private int _levelBefore;
        private CameraMode _modeBefore;
        private int _frameRateBefore;
        private bool _applierBefore;
        private ClassicWeaponId _weaponBefore;
        private UniversalRenderPipelineAsset _contactAsset;
        private int _contactCascadesBefore = -1;

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
            // MUST-3 writes the cascade count itself: back to the value it found before the applier takes over again
            if (_contactAsset != null && _contactCascadesBefore > 0) _contactAsset.shadowCascadeCount = _contactCascadesBefore;
            _contactAsset = null;
            QualityModeApplier.Enabled = _applierBefore;
            QualityModeApplier.ApplyShadowDistance = true;
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
            // WP-081: the applier stands down under the menu backdrop — in the run the flag must be clear or every row is "off"
            Assert.IsFalse(WorldViewer.Active, "koşuda menü arka planı bayrağı (WorldViewer.Active) açık");
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
            string maps = Path.Combine(dir, "cascade-maps");
            Directory.CreateDirectory(frames);
            Directory.CreateDirectory(maps);
            // the cascade columns describe the AD frame's camera (TD: the gameplay framing pose; TP / FP: the rig camera's pose),
            // the counters the rig camera
            var sb = new StringBuilder("tier\tmode\tapplier\tasset\tcascades\tshadowDistance\tsetPass\tdraws\tbatches\tshadowCasters\tvisibleZombieRenderers\tcamPos\tframe\t" + CascadeHeader + "\tmap\n");
            int rows = 0;
            long minSetPass = long.MaxValue, minDraws = long.MaxValue, minCasters = long.MaxValue;
            int minVisible = int.MaxValue;
            Transform player = PlayerRegistry.Primary.transform;
            int count = QualitySettings.count;
            var warmed = new HashSet<CameraMode>();
            // Performans added (C-00 06.10): the AD had no frame of it
            foreach (QualityTier tier in new[] { QualityTier.Sinematik, QualityTier.Yuksek, QualityTier.Performans })
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
                    minVisible = Math.Min(minVisible, visible);
                    // three states (C-00 06.10 decision 3): off = the assets as shipped; casc = the profile's cascades only;
                    // on = cascades + the profile's shadow distance (Cinematic 120 m -> 60 / 60 / 70 m)
                    foreach (string state in new[] { "off", "casc", "on" })
                    {
                        bool on = state != "off";
                        QualityModeApplier.Enabled = on;
                        QualityModeApplier.ApplyShadowDistance = state == "on";
                        if (on) QualityModeApplier.ApplyNow();
                        else QualityModeApplier.RestoreOriginals();
                        yield return null;
                        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                        Assert.IsNotNull(asset, "etkin URP varlığı yok");
                        var c = new long[4];
                        yield return Counters(cam, c);
                        string file = Path.Combine(frames, tier + "-" + mode + "-" + state + ".png");
                        string map = Path.Combine(maps, tier + "-" + mode + "-" + state + "-cascades.png");
                        Vector4[] spheres = CaptureWithSpheres(pose, file, map, asset.shadowCascadeCount);
                        string row = string.Join("\t", tier.ToString(), mode.ToString(), state, asset.name,
                            asset.shadowCascadeCount.ToString(CultureInfo.InvariantCulture), asset.shadowDistance.ToString("0.##", CultureInfo.InvariantCulture),
                            c[0].ToString(CultureInfo.InvariantCulture), c[1].ToString(CultureInfo.InvariantCulture), c[2].ToString(CultureInfo.InvariantCulture), c[3].ToString(CultureInfo.InvariantCulture),
                            visible.ToString(CultureInfo.InvariantCulture), cam.transform.position.ToString("F2"), Path.GetFileName(file),
                            CascadeColumns(asset, spheres, player.position), Path.GetFileName(map));
                        sb.Append(row).Append('\n');
                        rows++;
                        minSetPass = Math.Min(minSetPass, c[0]);
                        minDraws = Math.Min(minDraws, c[1]);
                        if (c[3] >= 0) minCasters = Math.Min(minCasters, c[3]);
                        Debug.Log("[DD-OBS] s05.cascade.v6 " + row.Replace('\t', ' '));
                    }
                }
            }

            string path = Path.Combine(dir, "cascade-v6-counters.tsv");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DD-OBS] s05.cascade.v6 rows=" + rows + " zombies=" + alive + " -> " + path);
            // the source is in the record: the crowd was there and every frame was drawn — and in view, casting (gt Y-6)
            Assert.AreEqual(27, rows, "satır sayısı");
            Assert.Greater(minSetPass, 0, "kaynak yok: SetPass 0 (kare render edilmedi)");
            Assert.Greater(minDraws, 0, "kaynak yok: draw 0");
            Assert.Greater(minVisible, 0, "kaynak yok: kamerada görünür zombi yok");
            if (minCasters != long.MaxValue) Assert.Greater(minCasters, 0, "kaynak yok: gölge döken 0");

            // MUST-2: the boundary under motion
            yield return Pan(dir, player, count);
            // MUST-3: High FP close contact at 2 / 3 / 4 cascades
            yield return Contact(dir, player, count);
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) z.Remove();
        }

        // ------------------------------------------------------------------------------------------------ MUST-2 pan

        /// <summary>
        /// TP orbit of ±<see cref="PanHalfDegrees"/>° around the player in <see cref="PanFrames"/> frames (3 s at 20 fps), Cinematic
        /// and High, applier on, the world frozen. Every frame: the capture camera's own cascade spheres and where the player and the
        /// close band sit in them — a boundary that sweeps over them shows as a cascade / edge change between neighbouring rows.
        /// </summary>
        private static IEnumerator Pan(string dir, Transform player, int levelCount)
        {
            string panDir = Path.Combine(dir, "pan");
            Directory.CreateDirectory(panDir);
            var sb = new StringBuilder("tier\tframe\tyawOffset\tcascades\tshadowDistance\t" + CascadeHeader + "\tfile\n");
            int rows = 0;
            foreach (QualityTier tier in new[] { QualityTier.Sinematik, QualityTier.Yuksek })
            {
                QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier(tier, levelCount), true);
                for (int i = 0; i < 3; i++) yield return null;
                CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
                QualityModeApplier.Enabled = true;
                QualityModeApplier.ApplyShadowDistance = true;
                QualityModeApplier.ApplyNow();
                for (int i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(CameraMode.ThirdPerson, CameraModeService.Current, "mod geçişi");
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Assert.IsNotNull(asset, "etkin URP varlığı yok");
                Camera cam = PrototypeCameraRig.Active.Camera;
                Vector3 pivot = player.position;
                Vector3 offset = cam.transform.position - pivot;
                Vector3 aim = cam.transform.position + cam.transform.forward * 20f - pivot;
                for (int k = 0; k < PanFrames; k++)
                {
                    float yaw = -PanHalfDegrees + 2f * PanHalfDegrees * k / (PanFrames - 1);
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                    var pose = new ArtVisualRegression.CameraPose("tp-pan", pivot + q * offset, pivot + q * aim, cam.fieldOfView);
                    string file = Path.Combine(panDir, tier + "-ThirdPerson-pan-" + k.ToString("00", Inv) + ".png");
                    Vector4[] spheres = CaptureWithSpheres(pose, file, null, asset.shadowCascadeCount);
                    string row = string.Join("\t", tier.ToString(), k.ToString(Inv), yaw.ToString("0.##", Inv),
                        asset.shadowCascadeCount.ToString(Inv), asset.shadowDistance.ToString("0.##", Inv),
                        CascadeColumns(asset, spheres, player.position), Path.GetFileName(file));
                    sb.Append(row).Append('\n');
                    rows++;
                    if (k % 10 == 9) yield return null;   // the editor breathes; the world stays frozen
                }
            }

            string path = Path.Combine(dir, "cascade-pan.tsv");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DD-OBS] s05.cascade.pan rows=" + rows + " -> " + path);
            Assert.AreEqual(2 * PanFrames, rows, "pan satır sayısı");
        }

        // ------------------------------------------------------------------------------------------------ MUST-3 contact

        /// <summary>
        /// High FP close contact: the view turned off the sun's ground direction (so the shadow falls across the frame), one
        /// Standard body (full armour) at <see cref="ContactDistance"/> m on the NavMesh in clear sight, pitch
        /// <see cref="ContactPitch"/>° down; the live gameplay camera (viewmodel overlay included) at 2 / 3 / 4 cascades, the
        /// distance the applier gives High FP (60 m). The count is written here, not by the applier, and put back afterwards.
        /// </summary>
        private IEnumerator Contact(string dir, Transform player, int levelCount)
        {
            QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier(QualityTier.Yuksek, levelCount), true);
            for (int i = 0; i < 3; i++) yield return null;
            CameraModeService.Request(CameraMode.FirstPerson, instant: true);
            QualityModeApplier.Enabled = true;
            QualityModeApplier.ApplyShadowDistance = true;
            QualityModeApplier.ApplyNow();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(CameraMode.FirstPerson, CameraModeService.Current, "mod geçişi");
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(asset, "etkin URP varlığı yok");

            Light sun = CloudShadowDriver.Active != null ? CloudShadowDriver.Active.Sun : RenderSettings.sun;
            Assert.IsNotNull(sun, "kaynak yok: güneş ışığı");
            Vector3 toSun = -sun.transform.forward;
            Vector3 shadowDir = Vector3.ProjectOnPlane(sun.transform.forward, Vector3.up);
            Assert.Greater(shadowDir.sqrMagnitude, 1e-4f, "güneş tepede: zeminde gölge yönü yok");
            shadowDir.Normalize();
            float elevation = 90f - Vector3.Angle(toSun, Vector3.up);
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Camera cam = rig.Camera;
            Vector3 eye = cam.transform.position;

            float[] offsets = { 60f, -60f, 90f, -90f, 30f, -30f, 120f, -120f, 0f };
            Vector3 spot = Vector3.zero, facing = Vector3.zero;
            float chosen = float.NaN;
            foreach (float o in offsets)
            {
                Vector3 d = Quaternion.Euler(0f, o, 0f) * shadowDir;
                Vector3 p = NavPathFollower.ClampToNavMesh(player.position + d * ContactDistance, 0.6f);
                float flat = Vector3.ProjectOnPlane(p - player.position, Vector3.up).magnitude;
                if (flat < 1.5f || flat > 3.2f) continue;
                if (Physics.Linecast(eye, p + Vector3.up, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore)) continue;
                spot = p;
                facing = Vector3.ProjectOnPlane(p - player.position, Vector3.up).normalized;
                chosen = o;
                break;
            }

            Assert.IsFalse(float.IsNaN(chosen), "kaynak yok: oyuncunun çevresinde 1,5–3 m'de görünür NavMesh noktası yok");
            rig.SetViewAngles(Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y, ContactPitch);
            // the 40-body crowd fills the factory's ceiling: the body farthest from the player gives its slot to the contact body
            ZombieEnemy farthest = null;
            float farthestSq = -1f;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || !z.IsSpawned) continue;
                float sq = (z.transform.position - player.position).sqrMagnitude;
                if (sq > farthestSq) { farthestSq = sq; farthest = z; }
            }

            if (farthest != null) farthest.Remove();
            yield return null;
            ZombieEnemy body = EnemyFactory.Spawn(EnemyTier.Standard, 12, spot, Quaternion.LookRotation(-facing, Vector3.up));
            Assert.IsNotNull(body, "kaynak yok: yakın temas gövdesi doğmadı");
            if (body.Armor != null && body.Armor.EquippedCount < 3) body.FitArmorComposition(true, true, true);
            // two fixed steps so the new body's animator writes a pose and the rig takes the new view; the crowd moves ≤ 0.1 m
            Time.captureDeltaTime = 1f / 60f;
            Time.timeScale = 1f;
            for (int i = 0; i < 2; i++) yield return null;
            Time.timeScale = 0f;
            Time.captureDeltaTime = 0f;
            yield return null;
            var fpPose = new ArtVisualRegression.CameraPose("fp-contact", cam.transform.position, cam.transform.position + cam.transform.forward * 20f, cam.fieldOfView);
            yield return ArtVisualRegression.WarmStreaming(fpPose, W, H, 120, null);

            string contactDir = Path.Combine(dir, "contact");
            Directory.CreateDirectory(contactDir);
            Vector3 feet = body.transform.position;
            float bodyDist = Vector3.ProjectOnPlane(feet - player.position, Vector3.up).magnitude;
            // the middle of a 1.8 m body's shadow on flat ground
            Vector3 shadowMid = feet + shadowDir * (0.5f * 1.8f / Mathf.Max(0.1f, Mathf.Tan(elevation * Mathf.Deg2Rad)));
            var sb = new StringBuilder("tier\tmode\tcascades\tshadowDistance\tsetPass\tdraws\tbatches\tshadowCasters\tbodyDistM\tsunOffsetDeg\tsunElevationDeg\tbodyCascade\tbodyEdgeM\tbodyTexelCm\tshadowMidCascade\tshadowMidTexelCm\t" + CascadeHeader + "\tframe\tmap\n");
            _contactAsset = asset;
            _contactCascadesBefore = asset.shadowCascadeCount;
            QualityModeApplier.Enabled = false;   // the count below is the test's, not the applier's
            int rows = 0;
            foreach (int n in new[] { 2, 3, 4 })
            {
                asset.shadowCascadeCount = n;
                yield return null;
                var c = new long[4];
                yield return Counters(cam, c);
                string file = Path.Combine(contactDir, "Yuksek-FirstPerson-contact-c" + n + ".png");
                string map = Path.Combine(contactDir, "Yuksek-FirstPerson-contact-c" + n + "-cascades.png");
                Vector4[] spheres = CaptureLiveWithSpheres(cam, file, map, n);
                int tile = n > 1 ? asset.mainLightShadowmapResolution / 2 : asset.mainLightShadowmapResolution;
                int bc = CascadeOf(spheres, n, feet), mc = CascadeOf(spheres, n, shadowMid);
                string row = string.Join("\t", "Yuksek", "FirstPerson", n.ToString(Inv), asset.shadowDistance.ToString("0.##", Inv),
                    c[0].ToString(Inv), c[1].ToString(Inv), c[2].ToString(Inv), c[3].ToString(Inv),
                    bodyDist.ToString("0.##", Inv), chosen.ToString("0", Inv), elevation.ToString("0.#", Inv),
                    bc.ToString(Inv), EdgeText(spheres, n, feet), bc >= 0 ? Texel(spheres, bc, tile).ToString("0.##", Inv) : "out",
                    mc.ToString(Inv), mc >= 0 ? Texel(spheres, mc, tile).ToString("0.##", Inv) : "out",
                    CascadeColumns(asset, spheres, player.position), Path.GetFileName(file), Path.GetFileName(map));
                sb.Append(row).Append('\n');
                rows++;
                Debug.Log("[DD-OBS] s05.cascade.contact " + row.Replace('\t', ' '));
            }

            asset.shadowCascadeCount = _contactCascadesBefore;
            _contactAsset = null;
            QualityModeApplier.Enabled = true;
            QualityModeApplier.ApplyNow();
            body.Remove();
            string path = Path.Combine(dir, "cascade-contact.tsv");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[DD-OBS] s05.cascade.contact rows=" + rows + " sunOffset=" + chosen + " -> " + path);
            Assert.AreEqual(3, rows, "temas satır sayısı");
        }

        // ------------------------------------------------------------------------------------------------ cascade read-back

        /// <summary>
        /// AD frame plus the culling spheres URP rendered it with: the globals are zeroed, the capture renders (its shadow pass
        /// sets them), they are read before anything else renders. A zero first sphere = not this render's → the test fails.
        /// </summary>
        private static Vector4[] CaptureWithSpheres(ArtVisualRegression.CameraPose pose, string file, string map, int cascades)
        {
            ClearSpheres();
            Texture2D tex = ArtVisualRegression.Capture(pose, W, H, true);
            Vector4[] s = ReadSpheres();
            try
            {
                File.WriteAllBytes(file, tex.EncodeToPNG());
                if (map != null) WriteCascadeMap(tex, pose.position, pose.lookAt, pose.verticalFov, s, cascades, map);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            if (cascades > 1) Assert.Greater(s[0].w, 0f, "kaynak yok: kaskad küreleri bu render'dan okunmadı (" + Path.GetFileName(file) + ")");
            return s;
        }

        /// <summary>The live gameplay camera (its stack: the FP viewmodel overlay) into a 1080p frame, with its spheres.</summary>
        private static Vector4[] CaptureLiveWithSpheres(Camera cam, string file, string map, int cascades)
        {
            var rt = new RenderTexture(W, H, 24) { name = "S-05 cascade live", antiAliasing = 1 };
            rt.Create();
            Texture2D tex = null;
            Vector4[] s;
            try
            {
                ClearSpheres();
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                s = ReadSpheres();
                tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0, false);
                tex.Apply(false, false);
                RenderTexture.active = previous;
                File.WriteAllBytes(file, tex.EncodeToPNG());
                WriteCascadeMap(tex, cam.transform.position, cam.transform.position + cam.transform.forward, cam.fieldOfView, s, cascades, map);
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }

            if (cascades > 1) Assert.Greater(s[0].w, 0f, "kaynak yok: kaskad küreleri bu render'dan okunmadı (" + Path.GetFileName(file) + ")");
            return s;
        }

        private static void ClearSpheres()
        {
            foreach (int id in SphereIds) Shader.SetGlobalVector(id, Vector4.zero);
        }

        private static Vector4[] ReadSpheres()
        {
            var s = new Vector4[SphereIds.Length];
            for (int i = 0; i < s.Length; i++) s[i] = Shader.GetGlobalVector(SphereIds[i]);
            return s;
        }

        /// <summary>URP's ComputeCascadeIndex: the first sphere that holds the point; −1 beyond the last.</summary>
        private static int CascadeOf(Vector4[] s, int n, Vector3 p)
        {
            for (int i = 0; i < n && i < s.Length; i++)
            {
                if ((p - new Vector3(s[i].x, s[i].y, s[i].z)).sqrMagnitude < s[i].w * s[i].w) return i;
            }

            return -1;
        }

        /// <summary>Metres from the point to the nearest boundary of its cascade (leaving its sphere or entering the one before); NaN beyond the last.</summary>
        private static float Edge(Vector4[] s, int n, Vector3 p)
        {
            int ci = CascadeOf(s, n, p);
            if (ci < 0) return float.NaN;
            float edge = s[ci].w - (p - new Vector3(s[ci].x, s[ci].y, s[ci].z)).magnitude;
            if (ci > 0) edge = Mathf.Min(edge, (p - new Vector3(s[ci - 1].x, s[ci - 1].y, s[ci - 1].z)).magnitude - s[ci - 1].w);
            return edge;
        }

        private static string EdgeText(Vector4[] s, int n, Vector3 p)
        {
            float e = Edge(s, n, p);
            return float.IsNaN(e) ? "out" : e.ToString("0.##", Inv);
        }

        /// <summary>World size of one shadow-map texel in cascade i, cm (sphere diameter over the atlas tile).</summary>
        private static float Texel(Vector4[] s, int i, int tile)
        {
            return tile > 0 ? 200f * s[i].w / tile : 0f;
        }

        private static string CascadeColumns(UniversalRenderPipelineAsset asset, Vector4[] s, Vector3 playerFeet)
        {
            int n = asset.shadowCascadeCount;
            int res = asset.mainLightShadowmapResolution;
            int tile = n > 1 ? res / 2 : res;
            var cols = new List<string>(17) { res.ToString(Inv), tile.ToString(Inv) };
            for (int i = 0; i < 4; i++) cols.Add(i < n ? s[i].w.ToString("0.##", Inv) : "-");
            for (int i = 0; i < 4; i++) cols.Add(i < n ? Texel(s, i, tile).ToString("0.##", Inv) : "-");
            cols.Add(n > 1 && s[0].w > 0f ? (s[1].w / s[0].w).ToString("0.##", Inv) : "-");
            cols.Add(n > 2 && s[1].w > 0f ? (s[2].w / s[1].w).ToString("0.##", Inv) : "-");
            cols.Add(CascadeOf(s, n, playerFeet).ToString(Inv));
            cols.Add(EdgeText(s, n, playerFeet));
            int near = 0;
            float nearEdge = float.PositiveInfinity;
            var hist = new int[5];
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || !z.IsSpawned) continue;
                Vector3 f = z.transform.position;
                if (Vector3.ProjectOnPlane(f - playerFeet, Vector3.up).magnitude > NearBandMetres) continue;
                near++;
                int ci = CascadeOf(s, n, f);
                hist[ci < 0 ? 4 : ci]++;
                float e = Edge(s, n, f);
                if (!float.IsNaN(e)) nearEdge = Mathf.Min(nearEdge, e);
            }

            cols.Add(near.ToString(Inv));
            cols.Add("c0:" + hist[0] + ",c1:" + hist[1] + ",c2:" + hist[2] + ",c3:" + hist[3] + ",out:" + hist[4]);
            cols.Add(float.IsInfinity(nearEdge) ? "-" : nearEdge.ToString("0.##", Inv));
            return string.Join("\t", cols);
        }

        /// <summary>
        /// The frame tinted by cascade (0 red, 1 green, 2 blue, 3 yellow, beyond the last darkened; sky untouched) with white
        /// boundary lines: one ray per <see cref="MapStep"/> px block from the frame's camera, the hit point's cascade from the
        /// read-back spheres. Ray hits are colliders, not pixels: foliage without colliders shows the ground behind it.
        /// </summary>
        private static void WriteCascadeMap(Texture2D frame, Vector3 position, Vector3 lookAt, float fov, Vector4[] s, int n, string file)
        {
            int gw = W / MapStep, gh = H / MapStep;
            var index = new int[gw * gh];
            Vector3 forward = (lookAt - position).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);
            float ty = Mathf.Tan(0.5f * fov * Mathf.Deg2Rad), tx = ty * W / H;
            for (int gy = 0; gy < gh; gy++)
            {
                for (int gx = 0; gx < gw; gx++)
                {
                    float nx = (gx + 0.5f) * MapStep / W * 2f - 1f, ny = (gy + 0.5f) * MapStep / H * 2f - 1f;
                    Vector3 dir = (forward + right * (nx * tx) + up * (ny * ty)).normalized;
                    index[gy * gw + gx] = Physics.Raycast(position, dir, out RaycastHit hit, 300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        ? CascadeOf(s, n, hit.point)
                        : -2;
                }
            }

            Color32[] px = frame.GetPixels32();
            var white = new Color32(255, 255, 255, 255);
            var beyond = new Color32(20, 20, 20, 255);
            for (int y = 0; y < H; y++)
            {
                int gy = y / MapStep;
                for (int x = 0; x < W; x++)
                {
                    int gx = x / MapStep;
                    int ci = index[gy * gw + gx];
                    if (ci == -2) continue;
                    int i = y * W + x;
                    bool rightEdge = x % MapStep == MapStep - 1 && gx + 1 < gw && index[gy * gw + gx + 1] != ci && index[gy * gw + gx + 1] != -2;
                    bool topEdge = y % MapStep == MapStep - 1 && gy + 1 < gh && index[(gy + 1) * gw + gx] != ci && index[(gy + 1) * gw + gx] != -2;
                    if (rightEdge || topEdge)
                    {
                        px[i] = white;
                        continue;
                    }

                    px[i] = Color32.Lerp(px[i], ci >= 0 ? CascadeTint[ci] : beyond, 0.45f);
                }
            }

            var map = new Texture2D(W, H, TextureFormat.RGB24, false);
            try
            {
                map.SetPixels32(px);
                map.Apply(false, false);
                File.WriteAllBytes(file, map.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(map);
            }
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
