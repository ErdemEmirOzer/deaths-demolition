using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-084 (C-00 06.10, KADRO gate (b)) · the AD review frames of the AP-025 v002 zombies in the Run scene, through the game
    /// camera (same post-processing / exposure), frozen: (1) the crowd — 12 zombies, every version in every row, the gameplay
    /// pose; (2) top-down over the same crowd; (3) the three faces (Gaunt / Swollen / Riven) on one Standard look, close; (4) the
    /// skin / dirt / blood masks on one look — (0,0,0), (1,1,1), (2,2,2) and blood only (0,0,2); (5) the four versions of one look
    /// seed, mid shot (the Z2 pair reading). PNGs (1920×1080) to DD_WP084_AD_DIR. Frames only: no threshold (the gates are Z2 /
    /// Z3b / Z4 / DEC-077).
    /// </summary>
    [TestFixture]
    [Explicit("WP-084 AD frames: run by full name")]
    [Category("Diagnostics")]
    [Category("WP084")]
    public sealed class ZombieV2AdCaptureTests
    {
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            ZombieEnemy.ResetRisePolicy();
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AD_Kalabalik_Tepeden_Yuzler_Maskeler_Versiyonlar_Kareler()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            while (!(RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null
                     && RunDirector.Instance.Phase == RunPhase.Fighting) && Time.realtimeSinceStartup - t0 < 40f) yield return null;
            Assert.IsTrue(RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, "Run sahnesi / Fighting");
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            CharacterAssetSet set = CharacterAssetSet.Load();
            ZombieModuleLibrary library = set != null ? set.ZombieLibrary : null;
            Assert.IsNotNull(library, "zombie module library");
            List<string> faces = library.Ids(ZombieModuleLibrary.KindFace);
            Assert.AreEqual(3, faces.Count, "kaynak yok: üç yüz modülü");
            Camera game = PrototypeCameraRig.Active.Camera;
            string dir = Environment.GetEnvironmentVariable("DD_WP084_AD_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp084-ad");
            Directory.CreateDirectory(dir);

            Vector3 fwd = Vector3.ProjectOnPlane(game.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 p0 = rig.transform.position;

            // (1) + (2) the crowd
            var crowd = new List<ZombieEnemy>(12);
            EnemyFactory.ResetSeedForTests(3u);
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 4; col++)
            {
                Vector3 p = NavPathFollower.ClampToNavMesh(p0 + fwd * (6f + row * 2.5f) + right * ((col - 1.5f) * 2f), 6f);
                ZombieEnemy z = EnemyFactory.Spawn(Tiers[(col + row) % 4], 8, p, Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, "spawn");
                crowd.Add(z);
            }

            yield return Settle(crowd, fwd);
            Capture(game, Path.Combine(dir, "01-crowd-gameplay.png"));
            Vector3 centre = p0 + fwd * 8.5f;
            CaptureFrom(game, centre + Vector3.up * 14f - fwd * 4f, Quaternion.LookRotation(centre - (centre + Vector3.up * 14f - fwd * 4f)), 45f, Path.Combine(dir, "02-crowd-topdown.png"));
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) z.Remove();
            yield return null;

            // (3) the three faces on one Standard look, (4) the masks, (5) the versions — a row of figures in front of the camera
            Vector3 spot = NavPathFollower.ClampToNavMesh(p0 + fwd * 5f, 6f);
            var row3 = new List<ZombieEnemy>();
            EnemyFactory.ResetSeedForTests(11u);
            for (int i = 0; i < 3; i++)
            {
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 8, spot + right * ((i - 1) * 0.9f), Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, "spawn");
                row3.Add(z);
            }

            ZombieLook baseLook = row3[0].Skinned.Look;
            for (int i = 0; i < 3; i++)
            {
                ZombieLook look = baseLook;
                look.face = faces[i];
                look.accessory = null;
                look.head = ZombieModuleLibrary.HeadNone;   // the bare face, no hair / headwear over it
                Reassemble(row3[i], library, look, 1, 1, 1);
            }

            yield return Settle(row3, fwd);
            Vector3 headHeight = Vector3.up * 1.62f;
            Vector3 eye = spot - fwd * 2.4f + headHeight;
            CaptureFrom(game, eye, Quaternion.LookRotation(spot + headHeight - eye), 28f, Path.Combine(dir, "03-faces-gaunt-swollen-riven.png"));
            Time.timeScale = 1f;

            int[][] combos = { new[] { 0, 0, 0 }, new[] { 1, 1, 1 }, new[] { 2, 2, 2 } };
            for (int i = 0; i < 3; i++)
            {
                ZombieLook look = baseLook;
                look.face = faces[0];
                Reassemble(row3[i], library, look, combos[i][0], combos[i][1], combos[i][2]);
            }

            yield return Settle(row3, fwd);
            Vector3 eyeBody = spot - fwd * 4.2f + Vector3.up * 1.2f;
            CaptureFrom(game, eyeBody, Quaternion.LookRotation(spot + Vector3.up * 1.0f - eyeBody), 34f, Path.Combine(dir, "04-masks-000-111-222.png"));
            Time.timeScale = 1f;
            for (int i = 0; i < 3; i++) row3[i].Skinned.ApplyVariation(0, 0, i == 1 ? 2 : 0);
            yield return Settle(row3, fwd);
            CaptureFrom(game, eyeBody, Quaternion.LookRotation(spot + Vector3.up * 1.0f - eyeBody), 34f, Path.Combine(dir, "05-blood-only-centre.png"));
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in row3) if (z != null && z.IsSpawned) z.Remove();
            yield return null;

            // (5) one look seed, four versions
            var versions = new List<ZombieEnemy>();
            for (int t = 0; t < Tiers.Length; t++)
            {
                EnemyFactory.ResetSeedForTests(7u);
                ZombieEnemy z = EnemyFactory.Spawn(Tiers[t], 8, NavPathFollower.ClampToNavMesh(spot + right * ((t - 1.5f) * 1.3f), 6f), Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, "spawn");
                versions.Add(z);
            }

            yield return Settle(versions, fwd);
            Vector3 eyeTiers = spot - fwd * 6.5f + Vector3.up * 1.6f;
            CaptureFrom(game, eyeTiers, Quaternion.LookRotation(spot + Vector3.up * 1.0f - eyeTiers), 38f, Path.Combine(dir, "06-versions-standard-special-elite-ultra.png"));
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in versions) if (z != null && z.IsSpawned) z.Remove();
            yield return null;

            File.WriteAllText(Path.Combine(dir, "frames.txt"), "WP-084 AD frames: 01 crowd gameplay · 02 crowd top-down · 03 faces Gaunt/Swollen/Riven (no hair) · 04 masks (0,0,0)/(1,1,1)/(2,2,2) · 05 blood only (centre) · 06 versions Standard/Special/Elite/Ultra, one look seed\n");
            Debug.Log("[DD-OBS] wp084.ad frames=6 dir=" + dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "06-versions-standard-special-elite-ultra.png")), "kareler yazılmadı");
        }

        /// <summary>A capture-only look: the visual re-assembled on <paramref name="look"/> with the version's materials and the given mask indices.</summary>
        private static void Reassemble(ZombieEnemy z, ZombieModuleLibrary library, ZombieLook look, int skin, int dirt, int blood)
        {
            ZombieSkinnedVisual v = z.Skinned;
            Assert.IsTrue(v.Assemble(library, look), "assemble " + look.Key);
            v.ApplyMaterial(library, (int)z.Tier);
            ZombieTierLibrary tierLib = ZombieTierLibrary.Active;
            Material tint = tierLib != null && tierLib.IsReady ? tierLib.BodyMaterial(z.Tier) : null;
            if (tint != null) v.ApplyMaterial(library, (int)z.Tier, tint);
            v.ApplyVariation(skin, dirt, blood);
        }

        /// <summary>A fixed step into the idle stance facing the camera, then the frame frozen.</summary>
        private static IEnumerator Settle(List<ZombieEnemy> bodies, Vector3 fwd)
        {
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 40; i++)
            {
                foreach (ZombieEnemy z in bodies) if (z != null) z.transform.rotation = Quaternion.LookRotation(-fwd);
                yield return null;
            }

            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            yield return null;
        }

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

        private static void Capture(Camera cam, string png)
        {
            var rt = new RenderTexture(1920, 1080, 24) { name = "WP-084 AD capture", antiAliasing = 1 };
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
