using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-088 (C-00 05.10) · before / after frames and render counters for the garment atlas fix, in one frozen frame (same
    /// pose, same light). AFTER = the shipped assignment. BEFORE = the pre-fix final state, reproduced exactly: every body,
    /// top, bottom and head slot on the version's single body-tint material (the pre-fix measurement read exactly that on all
    /// 768 renderers). (1) The crowd: 12 zombies (3 per version) in front of the player through the game camera; (2) a
    /// close-up of a Standard and an Elite zombie. PNGs to DD_WP088_CMP_DIR; REPORTED ([DD-OBS] wp088.ap025.compare): crowd
    /// setPass / draw / batches before and after (max over 14 frames rendered by hand, the SetPassAttribution method).
    /// </summary>
    [TestFixture]
    [Explicit("WP-088 measurement: run by full name")]
    [Category("Diagnostics")]
    public sealed class ZombieMaterialCompareCaptureTests
    {
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Once_Sonra_Kalabalik_VeYakin_Kareler_RenderSayaclari()
        {
            LogAssert.ignoreFailingMessages = true;
            Application.targetFrameRate = -1;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (!(RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                     && RunDirector.Instance.Phase == RunPhase.Fighting) && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            Assert.IsTrue(RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, "Run sahnesi / Fighting");
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            ZombieTierLibrary tierLib = ZombieTierLibrary.Active;
            Assert.IsTrue(tierLib != null && tierLib.IsReady, "tier library");
            Camera game = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.Camera : null;
            Assert.IsNotNull(game, "game camera");

            // the crowd: 3 rows of 4 in front of the player, every version in every row
            Vector3 fwd = Vector3.ProjectOnPlane(game.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 p0 = rig.transform.position;
            var crowd = new List<ZombieEnemy>(12);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    Vector3 p = NavPathFollower.ClampToNavMesh(p0 + fwd * (6f + row * 2.5f) + right * ((col - 1.5f) * 2f), 6f);
                    ZombieEnemy z = EnemyFactory.Spawn(Tiers[(col + row) % 4], 8, p, Quaternion.LookRotation(-fwd));
                    Assert.IsNotNull(z, "spawn");
                    crowd.Add(z);
                }
            }

            // settle into a stance, then freeze the frame (render by hand; time stops for both states)
            for (int i = 0; i < 40; i++) yield return null;
            Time.timeScale = 0f;
            yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_WP088_CMP_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp088-compare");
            Directory.CreateDirectory(dir);

            // close-up camera: a Standard and an Elite of the front row, 3 m off, chest height
            ZombieEnemy std = crowd.Find(z => z.Tier == EnemyTier.Standard), elite = crowd.Find(z => z.Tier == EnemyTier.Elite);
            Vector3 mid = (std.transform.position + elite.transform.position) * 0.5f;
            Vector3 eye = mid - fwd * 3.6f + Vector3.up * 1.45f;
            Quaternion look = Quaternion.LookRotation(mid + Vector3.up * 1.1f - eye);
            yield return null;

            long[] after = new long[3];
            Capture(game, Path.Combine(dir, "crowd-after.png"));
            CaptureFrom(game, eye, look, 35f, Path.Combine(dir, "closeup-after.png"));
            yield return CountersOverFrames(game, after);

            // BEFORE: the pre-fix final state on every slot
            foreach (ZombieEnemy z in crowd)
            {
                Material body = tierLib.BodyMaterial(z.Tier);
                var bodyOnly = new[] { body };
                ZombieSkinnedVisual v = z.Skinned;
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                {
                    for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
                    {
                        SkinnedMeshRenderer r = v.Slot(lod, s);
                        if (r != null) r.sharedMaterials = bodyOnly;
                    }
                }
            }

            yield return null;
            long[] before = new long[3];
            Capture(game, Path.Combine(dir, "crowd-before.png"));
            CaptureFrom(game, eye, look, 35f, Path.Combine(dir, "closeup-before.png"));
            yield return CountersOverFrames(game, before);

            string line = string.Format(CultureInfo.InvariantCulture,
                "[DD-OBS] wp088.ap025.compare crowd=12 (3 per version) before setPass={0} draw={1} batches={2} | after setPass={3} draw={4} batches={5} | delta setPass={6:+0;-0;0} draw={7:+0;-0;0} batches={8:+0;-0;0} | png={9}",
                before[0], before[1], before[2], after[0], after[1], after[2], after[0] - before[0], after[1] - before[1], after[2] - before[2], dir);
            Debug.Log(line);
            File.WriteAllText(Path.Combine(dir, "counters.txt"), line + "\n");

            Time.timeScale = 1f;
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) z.Remove();
            Assert.Greater(after[0], 0, "no render counters read");
        }

        /// <summary>The game camera itself (same post-processing / exposure), moved for one render within the frame and put back.</summary>
        private static void CaptureFrom(Camera cam, Vector3 position, Quaternion rotation, float fov, string png)
        {
            Transform t = cam.transform;
            Vector3 p = t.position; Quaternion r = t.rotation; float f = cam.fieldOfView;
            try
            {
                t.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = fov;
                Capture(cam, png);
            }
            finally
            {
                t.SetPositionAndRotation(p, r);
                cam.fieldOfView = f;
            }
        }

        /// <summary>The SetPassAttribution method: render by hand 14 frames, keep the largest CurrentValue / LastValue.</summary>
        private static IEnumerator CountersOverFrames(Camera cam, long[] into)
        {
            var rt = new RenderTexture(1920, 1080, 24) { name = "WP-088 counters", antiAliasing = 2 };
            rt.Create();
            var sync = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "WP-088 sync" };
            sync.Create();
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            try
            {
                for (int k = 0; k < 14; k++)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    Graphics.Blit(rt, sync);
                    UnityEngine.Rendering.AsyncGPUReadback.Request(sync, 0, TextureFormat.RGBA32).WaitForCompletion();
                    cam.targetTexture = null;
                    into[0] = Math.Max(into[0], setPass.CurrentValue); into[1] = Math.Max(into[1], draws.CurrentValue); into[2] = Math.Max(into[2], batches.CurrentValue);
                    yield return null;
                    into[0] = Math.Max(into[0], setPass.LastValue); into[1] = Math.Max(into[1], draws.LastValue); into[2] = Math.Max(into[2], batches.LastValue);
                }
            }
            finally
            {
                setPass.Dispose(); draws.Dispose(); batches.Dispose();
                cam.targetTexture = null;
                rt.Release(); sync.Release();
            }
        }

        private static void Capture(Camera cam, string png)
        {
            var rt = new RenderTexture(1920, 1080, 24) { name = "WP-088 capture", antiAliasing = 1 };   // ArtVisualRegression's readable target
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply();
                File.WriteAllBytes(png, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                rt.Release();
            }
        }
    }
}
