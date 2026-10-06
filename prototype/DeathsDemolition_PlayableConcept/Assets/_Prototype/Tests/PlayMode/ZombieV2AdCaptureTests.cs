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

            // (3)–(6), AD turn 1 MUST M1 / M2 (06.10): every target in frame and unobstructed — the player hidden, the figures in a row
            // across the sun's direction and turned to the camera, the camera on the sun's side (front light), framed on the
            // figures' own bounds; each actor's on-screen size and, for (6), the materials of every slot are logged.
            var playerRenderers = new List<Renderer>();
            foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(false)) if (r.enabled) { playerRenderers.Add(r); r.enabled = false; }
            Vector3 lightDir = SunForward(fwd);
            Vector3 across = Vector3.Cross(Vector3.up, lightDir);
            Vector3 spot = NavPathFollower.ClampToNavMesh(p0 + fwd * 6f, 6f);

            // (3) the three faces on one Standard look (no hair / headwear), one close-up each and the trio
            List<ZombieEnemy> row3 = SpawnRow(EnemyTier.Standard, 3, 11u, spot, across, 0.8f, lightDir);
            ZombieLook baseLook = row3[0].Skinned.Look;
            for (int i = 0; i < 3; i++)
            {
                ZombieLook look = baseLook;
                look.face = faces[i];
                look.accessory = null;
                look.head = ZombieModuleLibrary.HeadNone;
                Reassemble(row3[i], library, look, 1, 1, 1);
            }

            yield return Settle(row3, lightDir);
            Frame(game, row3, lightDir, 1.55f, 0.55f, 30f, Path.Combine(dir, "03-faces-trio.png"));
            for (int i = 0; i < 3; i++) Frame(game, new List<ZombieEnemy> { row3[i] }, lightDir, 1.62f, 0.32f, 30f, Path.Combine(dir, "03-face-" + faces[i] + ".png"));
            Time.timeScale = 1f;

            // (4) the masks on one look: (0,0,0) (1,1,1) (2,2,2); (5) blood only in the centre, full body and torso close-up
            int[][] combos = { new[] { 0, 0, 0 }, new[] { 1, 1, 1 }, new[] { 2, 2, 2 } };
            for (int i = 0; i < 3; i++)
            {
                ZombieLook look = baseLook;
                look.face = faces[0];
                look.accessory = null;
                look.head = ZombieModuleLibrary.HeadNone;
                Reassemble(row3[i], library, look, combos[i][0], combos[i][1], combos[i][2]);
            }

            yield return Settle(row3, lightDir);
            Frame(game, row3, lightDir, 1.0f, 1.05f, 34f, Path.Combine(dir, "04-masks-000-111-222.png"));
            Time.timeScale = 1f;
            for (int i = 0; i < 3; i++) row3[i].Skinned.ApplyVariation(0, 0, i == 1 ? 2 : 0);
            yield return Settle(row3, lightDir);
            Frame(game, row3, lightDir, 1.0f, 1.05f, 34f, Path.Combine(dir, "05-blood-only-centre.png"));
            Frame(game, new List<ZombieEnemy> { row3[1] }, lightDir, 1.25f, 0.45f, 30f, Path.Combine(dir, "05-blood-only-centre-torso.png"));
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in row3) if (z != null && z.IsSpawned) z.Remove();
            yield return null;

            // (6) one look seed, four versions side by side, front and from above; the material dump behind M4
            var versions = new List<ZombieEnemy>();
            for (int t = 0; t < Tiers.Length; t++)
            {
                EnemyFactory.ResetSeedForTests(7u);
                ZombieEnemy z = EnemyFactory.Spawn(Tiers[t], 8, NavPathFollower.ClampToNavMesh(spot + across * ((t - 1.5f) * 1.4f), 6f), Quaternion.LookRotation(-lightDir));
                Assert.IsNotNull(z, "spawn");
                versions.Add(z);
            }

            yield return Settle(versions, lightDir);
            Frame(game, versions, lightDir, 1.0f, 1.1f, 36f, Path.Combine(dir, "06-versions-standard-special-elite-ultra.png"));
            Vector3 mid = Centre(versions);
            Vector3 above = mid - lightDir * 2.5f + Vector3.up * 7f;
            CaptureFrom(game, above, Quaternion.LookRotation(mid + Vector3.up * 0.5f - above), 45f, Path.Combine(dir, "06-versions-topdown.png"));
            foreach (ZombieEnemy z in versions) DumpMaterials(z);
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in versions) if (z != null && z.IsSpawned) z.Remove();
            foreach (Renderer r in playerRenderers) if (r != null) r.enabled = true;
            yield return null;

            File.WriteAllText(Path.Combine(dir, "frames.txt"), "WP-084 AD frames: 01 crowd gameplay · 02 crowd top-down · 03 faces trio + one close-up each (no hair, front light) · 04 masks (0,0,0)/(1,1,1)/(2,2,2) · 05 blood only (centre) + torso close-up · 06 versions Standard/Special/Elite/Ultra, one look seed, front + top-down; player hidden for 03–06\n");
            Debug.Log("[DD-OBS] wp084.ad frames=11 dir=" + dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "06-versions-standard-special-elite-ultra.png")), "kareler yazılmadı");
        }

        /// <summary>
        /// WP-084 (C-00 06.10) · the before / after frames of a budget cut, three cameras on one fixed group (the four versions + four
        /// Standard, one look seed stream, the gameplay sun): top-down at 15 m, third person at 3 m, first person close (eye 1.65 m,
        /// 1.6 m from the front figure). PNGs to DD_WP084_AD_DIR with the DD_WP084_AD_TAG prefix (before / after).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AD_UcKamera_TD15_TP3_FP_Kareler()
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

            Camera game = PrototypeCameraRig.Active.Camera;
            string dir = Environment.GetEnvironmentVariable("DD_WP084_AD_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp084-ad");
            string tag = Environment.GetEnvironmentVariable("DD_WP084_AD_TAG") ?? "frame";
            Directory.CreateDirectory(dir);
            Vector3 fwd = Vector3.ProjectOnPlane(game.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 centre = NavPathFollower.ClampToNavMesh(rig.transform.position + fwd * 7f, 6f);
            var group = new List<ZombieEnemy>();
            EnemyFactory.ResetSeedForTests(21u);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = NavPathFollower.ClampToNavMesh(centre + right * ((i % 4 - 1.5f) * 1.3f) + fwd * ((i / 4) * 1.4f), 6f);
                ZombieEnemy z = EnemyFactory.Spawn(i < 4 ? Tiers[i] : EnemyTier.Standard, 8, p, Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, "spawn");
                if (z.Armor != null && z.Armor.EquippedCount < 3) z.FitArmorComposition(true, true, true);
                group.Add(z);
            }

            yield return Settle(group, fwd);
            Vector3 td = centre + Vector3.up * 15f - fwd * 2f;
            CaptureFrom(game, td, Quaternion.LookRotation(centre - td), 45f, Path.Combine(dir, tag + "-td15.png"));
            Vector3 tp = centre - fwd * 3.6f + Vector3.up * 2.4f;
            CaptureFrom(game, tp, Quaternion.LookRotation(centre + Vector3.up * 1.0f - tp), 55f, Path.Combine(dir, tag + "-tp3.png"));
            ZombieEnemy front = group[0];
            Vector3 fp = front.transform.position - fwd * 1.6f + Vector3.up * 1.65f;
            CaptureFrom(game, fp, Quaternion.LookRotation(front.transform.position + Vector3.up * 1.45f - fp), 60f, Path.Combine(dir, tag + "-fp.png"));
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in group) if (z != null && z.IsSpawned) z.Remove();
            yield return null;
            Debug.Log("[DD-OBS] wp084.ad3cam tag=" + tag + " dir=" + dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, tag + "-fp.png")), "kareler yazılmadı");
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

        /// <summary>The sun's horizontal travel direction (the camera stands on the sun's side: front light); the gameplay forward without a sun.</summary>
        private static Vector3 SunForward(Vector3 fallback)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                float best = -1f;
                foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                    if (l.type == LightType.Directional && l.isActiveAndEnabled && l.intensity > best) { best = l.intensity; sun = l; }
            }

            Vector3 d = sun != null ? Vector3.ProjectOnPlane(sun.transform.forward, Vector3.up) : Vector3.zero;
            return d.sqrMagnitude > 1e-4f ? d.normalized : fallback;
        }

        private static List<ZombieEnemy> SpawnRow(EnemyTier tier, int count, uint seed, Vector3 spot, Vector3 across, float spacing, Vector3 lightDir)
        {
            var row = new List<ZombieEnemy>(count);
            EnemyFactory.ResetSeedForTests(seed);
            for (int i = 0; i < count; i++)
            {
                ZombieEnemy z = EnemyFactory.Spawn(tier, 8, NavPathFollower.ClampToNavMesh(spot + across * ((i - (count - 1) * 0.5f) * spacing), 6f), Quaternion.LookRotation(-lightDir));
                Assert.IsNotNull(z, "spawn");
                row.Add(z);
            }

            return row;
        }

        private static Vector3 Centre(List<ZombieEnemy> bodies)
        {
            Vector3 c = Vector3.zero;
            foreach (ZombieEnemy z in bodies) c += z.transform.position;
            return c / Mathf.Max(1, bodies.Count);
        }

        /// <summary>The camera on the sun's side at <paramref name="height"/>, far enough that the figures' bounds fill the frame
        /// (half-height <paramref name="halfHeight"/> around the aim point); logs each actor's on-screen box in pixels.</summary>
        private static void Frame(Camera cam, List<ZombieEnemy> bodies, Vector3 lightDir, float height, float halfHeight, float fov, string png)
        {
            Bounds b = new Bounds(Centre(bodies) + Vector3.up * height, Vector3.zero);
            foreach (ZombieEnemy z in bodies)
                foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false))
                    if (r.enabled && r.gameObject.activeInHierarchy) { b.Encapsulate(new Vector3(r.bounds.min.x, b.center.y, r.bounds.min.z)); b.Encapsulate(new Vector3(r.bounds.max.x, b.center.y, r.bounds.max.z)); }
            float halfWidth = Mathf.Max(b.extents.x, b.extents.z) + 0.25f;
            float vfov = fov * Mathf.Deg2Rad * 0.5f;
            float hfov = Mathf.Atan(Mathf.Tan(vfov) * 1920f / 1080f);
            float dist = Mathf.Max(halfHeight / Mathf.Tan(vfov), halfWidth / Mathf.Tan(hfov)) * 1.1f;
            Vector3 aim = Centre(bodies) + Vector3.up * height;
            Vector3 eye = aim - lightDir * dist;
            CaptureFrom(cam, eye, Quaternion.LookRotation(aim - eye), fov, png);
            Transform t = cam.transform;
            Vector3 p = t.position; Quaternion q = t.rotation; float f = cam.fieldOfView;
            t.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            cam.fieldOfView = fov;
            var sizes = new System.Text.StringBuilder();
            foreach (ZombieEnemy z in bodies)
            {
                Renderer face = z.Skinned != null ? z.Skinned.Slot(0, ZombieSkinnedVisual.SlotFace) : null;
                sizes.Append(' ').Append(z.Tier).Append(":body=").Append(ScreenPx(cam, z.Skinned.Slot(0, ZombieSkinnedVisual.SlotBody)))
                     .Append(",face=").Append(ScreenPx(cam, face));
            }

            t.SetPositionAndRotation(p, q);
            cam.fieldOfView = f;
            Debug.Log("[DD-OBS] wp084.ad.frame " + Path.GetFileName(png) + " dist=" + dist.ToString("F2") + sizes);
        }

        /// <summary>A renderer's world bounds projected to the 1920×1080 frame: "w×h" pixels (0×0 when off screen).</summary>
        private static string ScreenPx(Camera cam, Renderer r)
        {
            if (r == null || !r.enabled) return "-";
            Bounds b = r.bounds;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int c = 0; c < 8; c++)
            {
                Vector3 v = cam.WorldToViewportPoint(new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z));
                if (v.z <= 0f) return "behind";
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }

            minX = Mathf.Clamp01(minX); maxX = Mathf.Clamp01(maxX); minY = Mathf.Clamp01(minY); maxY = Mathf.Clamp01(maxY);
            return Mathf.RoundToInt((maxX - minX) * 1920f) + "x" + Mathf.RoundToInt((maxY - minY) * 1080f);
        }

        /// <summary>AD M4: every LOD0 slot of a figure — renderer, mesh, materials and their base maps (after the version tint).</summary>
        private static void DumpMaterials(ZombieEnemy z)
        {
            var sb = new System.Text.StringBuilder("[DD-OBS] wp084.ad.materials " + z.Tier + " look=" + z.Skinned.Look.Key + " |");
            for (int s = 0; s < ZombieSkinnedVisual.SlotCount; s++)
            {
                SkinnedMeshRenderer r = z.Skinned.Slot(0, s);
                if (r == null || !r.enabled || r.sharedMesh == null) continue;
                sb.Append(' ').Append(r.name).Append('=').Append(r.sharedMesh.name).Append('[');
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Texture map = mats[i] != null && mats[i].HasProperty("_BaseMap") ? mats[i].GetTexture("_BaseMap") : null;
                    sb.Append(i > 0 ? ", " : "").Append(mats[i] != null ? mats[i].name : "null").Append(':').Append(map != null ? map.name : "-");
                }

                sb.Append(']');
            }

            Debug.Log(sb.ToString());
        }

        /// <summary>WP-084 (Z2 control pair 0.48 / 0.51, 06.10): one pose for every capture — the Animator rebound and held at the
        /// locomotion state's first frame at speed 0, so a frame differs by the version, not by the walk phase the spawn landed on.</summary>
        private static void PinPose(ZombieEnemy z)
        {
            Animator an = z != null && z.Skinned != null ? z.Skinned.Animator : null;
            if (an == null) return;
            an.Rebind();
            an.SetFloat(CharacterAnimationSet.SpeedParameter, 0f);
            an.Play(CharacterAnimationSet.LocomotionState, 0, 0f);
            an.Update(0f);
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
            foreach (ZombieEnemy z in bodies) PinPose(z);   // before / after frames compare the same pose
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
