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

        /// <summary>WP-084 (AD delta MD2): the share of the chest–belly plate pixels a top may cover per view — fit A / B measured
        /// 0.0–2.3 % (r8e 06.10: hem and seam slivers at the plate's edges); fit C 4–78 % (the H2 garments over an armour AP-008D refits).</summary>
        private const float MaxClothOverPlateShare = 0.03f;

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
            Roi(game, crowd, game.transform.position, game.transform.rotation, game.fieldOfView, "01-crowd-gameplay.png", new List<string>(), false, null);   // logged, not gated
            Vector3 centre = p0 + fwd * 8.5f;
            Vector3 topEye = centre + Vector3.up * 14f - fwd * 4f;
            CaptureFrom(game, topEye, Quaternion.LookRotation(centre - topEye), 45f, Path.Combine(dir, "02-crowd-topdown.png"));
            Roi(game, crowd, topEye, Quaternion.LookRotation(centre - topEye), 45f, "02-crowd-topdown.png", new List<string>(), false, null);
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
                (row3[i].VersionVisual as ZombieAppearance)?.HideAllArmor();   // AD delta MD1: no helmet / armour in 03–05
            }

            var roiFails = new List<string>();

            yield return Settle(row3, lightDir);
            Frame(game, row3, lightDir, 1.55f, 0.55f, 30f, Path.Combine(dir, "03-faces-trio.png"), roiFails, true);
            for (int i = 0; i < 3; i++) Frame(game, new List<ZombieEnemy> { row3[i] }, lightDir, 1.62f, 0.32f, 30f, Path.Combine(dir, "03-face-" + faces[i] + ".png"), roiFails, true);
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
            Frame(game, row3, lightDir, 1.0f, 1.05f, 34f, Path.Combine(dir, "04-masks-000-111-222.png"), roiFails, false);
            Time.timeScale = 1f;
            for (int i = 0; i < 3; i++) row3[i].Skinned.ApplyVariation(0, 0, i == 1 ? 2 : 0);
            // AD delta MD1: the blood target one step towards the camera (front row) so the side figures never cover it
            row3[1].transform.position = NavPathFollower.ClampToNavMesh(row3[1].transform.position - lightDir * 1.0f, 2f);
            yield return Settle(row3, lightDir);
            Frame(game, row3, lightDir, 1.0f, 1.05f, 34f, Path.Combine(dir, "05-blood-only-centre.png"), roiFails, false, row3[1]);
            Frame(game, new List<ZombieEnemy> { row3[1] }, lightDir, 1.25f, 0.45f, 30f, Path.Combine(dir, "05-blood-only-centre-torso.png"), roiFails, false);
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
            Frame(game, versions, lightDir, 1.0f, 1.1f, 36f, Path.Combine(dir, "06-versions-standard-special-elite-ultra.png"), roiFails, false);
            Vector3 mid = Centre(versions);
            Vector3 above = mid - lightDir * 2.5f + Vector3.up * 7f;
            Quaternion aboveRot = Quaternion.LookRotation(mid + Vector3.up * 0.5f - above);
            CaptureFrom(game, above, aboveRot, 45f, Path.Combine(dir, "06-versions-topdown.png"));
            Roi(game, versions, above, aboveRot, 45f, "06-versions-topdown.png", roiFails, false, null);
            foreach (ZombieEnemy z in versions) DumpMaterials(z);
            Time.timeScale = 1f;
            foreach (ZombieEnemy z in versions) if (z != null && z.IsSpawned) z.Remove();
            yield return null;

            // (7) AD delta MD3: the B4 tattsuke hakama cw1 on fit A, close on the waist and legs, armour off — after the sub-mesh / material
            // fix the hakama must show the cloth atlas (no skin-atlas wedges); its slot materials dumped with their base maps
            EnemyFactory.ResetSeedForTests(5u);
            ZombieEnemy hz = EnemyFactory.Spawn(EnemyTier.Standard, 8, spot, Quaternion.LookRotation(-lightDir));
            Assert.IsNotNull(hz, "spawn");
            ZombieLook hl = hz.Skinned.Look;
            hl.fit = "A";
            hl.bottom = "B4_tattsuke_hakama";
            hl.bottomColourway = "cw1";
            hl.head = ZombieModuleLibrary.HeadNone;
            hl.accessory = null;
            Reassemble(hz, library, hl, 1, 1, 0);
            (hz.VersionVisual as ZombieAppearance)?.HideAllArmor();
            var hakama = new List<ZombieEnemy> { hz };
            yield return Settle(hakama, lightDir);
            Frame(game, hakama, lightDir, 0.7f, 0.6f, 30f, Path.Combine(dir, "07-B4-hakama-cw1-A.png"), roiFails, false);
            DumpMaterials(hz);
            Time.timeScale = 1f;
            if (hz.IsSpawned) hz.Remove();
            foreach (Renderer r in playerRenderers) if (r != null) r.enabled = true;
            yield return null;

            File.WriteAllText(Path.Combine(dir, "frames.txt"), "WP-084 AD frames: 01 crowd gameplay · 02 crowd top-down · 03 faces trio + one close-up each (bald, no accessory, no armour, front light) · 04 masks (0,0,0)/(1,1,1)/(2,2,2), the same garments and colourways, no armour · 05 blood only (0,0,2) on the centre figure stepped 1 m towards the camera + torso close-up · 06 versions Standard/Special/Elite/Ultra, one look seed (their own armour / tier parts), front + top-down · 07 B4 hakama cw1 fit A close (MD3); player hidden for 03–07. ROI per actor = visible pixels / the actor drawn alone ([DD-OBS] wp084.ad.roi)\n");
            Debug.Log("[DD-OBS] wp084.ad frames=12 dir=" + dir + " roiFails=" + roiFails.Count);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "06-versions-standard-special-elite-ultra.png")), "kareler yazılmadı");
            Assert.IsEmpty(roiFails, "AD delta MD1 ROI: " + string.Join("; ", roiFails));
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

        /// <summary>
        /// WP-084 (AD delta MD2, C-00 06.10) · top garment over the body armour: every fit (A / B / C) × body-armour variant × top the
        /// game can draw under it (the variant's exclusions — AP-008 + the AP-025 intake's interim armourBodyExcludes — left out;
        /// the faded cw2 colourway an armoured spawn wears, else the only one). One body, the look forced, the dō Present, the pose
        /// pinned; front and back at 2.2 m, each figure part drawn ALONE (a free layer, black clear, no post, no shadows): armour-only
        /// → the plate mask and colour; the figure with and without the top. In the chest–belly band (hips → upper chest, the middle
        /// half of the shoulder span — the hanging sleeves stay outside) a pixel is cloth over the plate when the plate shows with the
        /// top hidden and the top changes it (a lamellar gap showing the layer beneath never counts). REPORTED per combination (TSV +
        /// sample frames to DD_WP084_AD_DIR); ASSERTED ≤ MaxClothOverPlateShare per view on fit A / B; fit C is REPORTED as the known
        /// open list — no top is clean on the H2 bodies (r8e 06.10), so the interim rule cannot fix it; AP-008D refits the armour.
        /// </summary>
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator MD2_ZirhUstundeKumas_HerFitVaryantUstGiysi()
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
            Camera game = PrototypeCameraRig.Active.Camera;
            string dir = Environment.GetEnvironmentVariable("DD_WP084_AD_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp084-ad");
            Directory.CreateDirectory(dir);
            foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(true)) r.enabled = false;   // the player out of every frame

            Vector3 fwd = Vector3.ProjectOnPlane(game.transform.forward, Vector3.up).normalized;
            Vector3 spot = NavPathFollower.ClampToNavMesh(rig.transform.position + fwd * 6f, 6f);
            EnemyFactory.ResetSeedForTests(84u);
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 8, spot, Quaternion.LookRotation(-fwd));
            Assert.IsNotNull(z, "spawn");
            var appearance = z.VersionVisual as ZombieAppearance;
            ArmorVisualBinder binder = appearance != null ? appearance.ArmorBinder : null;
            Assert.IsTrue(binder != null && binder.IsReady, "kaynak yok: zırh bağlayıcısı");
            ZombieSkinnedVisual v = z.Skinned;
            ZombieLook baseLook = v.Look;
            baseLook.head = ZombieModuleLibrary.HeadNone;
            baseLook.accessory = null;
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 20; i++) { z.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd)); yield return null; }
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;

            List<string> tops = library.Ids("top");
            var tsv = new System.Text.StringBuilder("fit\tvariant\ttop\tcolourway\tview\tchestBellyPlatePx\tclothOverPlatePx\tplateVisiblePx\n");
            var violations = new List<(int px, string key)>();
            var knownOpenC = new List<(int px, string key)>();
            var fitCReport = new SortedDictionary<string, float>(StringComparer.Ordinal);   // C-00 06.10: fit C, per top, front / back covered share
            int combos = 0;
            foreach (string fit in new[] { "A", "B", "C" })
            {
                foreach (ZombieArmorLibrary.Variant av in binder.Library.variants)
                {
                    if (av.slot != ArmorSlot.Body) continue;
                    foreach (string top in tops)
                    {
                        if (av.Excludes(top)) continue;   // never drawn under this dō
                        ZombieModuleLibrary.Module m = Array.Find(library.modules, x => x.id == top);
                        string cw = Array.IndexOf(m.colourways, "cw2") >= 0 ? "cw2" : m.colourways[0];
                        ZombieLook look = baseLook;
                        look.fit = fit;
                        look.top = top;
                        look.topColourway = cw;
                        Assert.IsTrue(v.Assemble(library, look), "assemble " + look.Key);
                        binder.SetLook(new ArmorLook { body = av.id }, fit);
                        binder.Apply(ArmorSlot.Body, ArmorVisualState.Present);
                        PinPose(z);
                        yield return null;
                        combos++;
                        foreach (bool front in new[] { true, false })
                        {
                            Vector3 face = front ? -fwd : fwd;   // the body faces -fwd (towards the camera side)
                            Vector3 aim = spot + Vector3.up * 1.25f;
                            Vector3 eye = aim + face * 2.2f;
                            Quaternion rot = Quaternion.LookRotation(aim - eye);
                            List<Renderer> plate = ArmourRenderers(z, ArmorSlot.Body);
                            List<Renderer> topR = SlotRenderers(z, ZombieSkinnedVisual.SlotTop);
                            List<Renderer> all = DrawnRenderers(z);
                            Color32[] plateAlone = AloneFrame(game, plate, eye, rot, 40f, 960, 540);
                            bool[] plateMask = Mask(plateAlone);
                            Color32[] with = AloneFrame(game, all, eye, rot, 40f, 960, 540);
                            all.RemoveAll(r => topR.Contains(r));
                            Color32[] without = AloneFrame(game, all, eye, rot, 40f, 960, 540);
                            // the chest–belly band (AD delta MD2): hips → upper chest vertically, the middle half of the shoulder span
                            // across — the hanging arms and their sleeves stay outside (r8c 06.10: over the whole plate, every view
                            // counted the sleeves in front of the dō's flanks and the kosode shoulders over its straps)
                            RectInt band = ChestBelly(z, eye, rot, 40f, 960, 540);
                            int platePx = 0, over = 0;
                            for (int y = band.yMin; y < band.yMax; y++)
                                for (int x = band.xMin; x < band.xMax; x++)
                                {
                                    int i = y * 960 + x;
                                    if (!plateMask[i]) continue;
                                    platePx++;
                                    // cloth over the plate = the plate shows when the top is hidden (that pixel matches the plate drawn alone)
                                    // and the top changes it; a lamellar gap showing the layer beneath is not the plate, so it never counts
                                    Color32 a = with[i], b = without[i], pl = plateAlone[i];
                                    bool plateShows = Mathf.Abs(b.r - pl.r) + Mathf.Abs(b.g - pl.g) + Mathf.Abs(b.b - pl.b) <= 24;
                                    if (plateShows && Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 24) over++;
                                }

                            string key = fit + "/" + av.id + "/" + top + ":" + cw + "/" + (front ? "front" : "back");
                            tsv.Append(fit).Append('\t').Append(av.id).Append('\t').Append(top).Append('\t').Append(cw).Append('\t').Append(front ? "front" : "back")
                               .Append('\t').Append(platePx).Append('\t').Append(over).Append('\t').Append(platePx - over).Append('\n');
                            float share = platePx > 0 ? (float)over / platePx : 0f;
                            if (fit == "C")
                            {
                                string reportKey = top + "/" + (front ? "front" : "back");   // the worse of the two dō variants
                                fitCReport[reportKey] = fitCReport.TryGetValue(reportKey, out float prev) ? Mathf.Max(prev, share) : share;
                            }

                            if (share > MaxClothOverPlateShare)
                            {
                                if (fit == "C") knownOpenC.Add((over, key + " " + (share * 100f).ToString("F0") + "%"));
                                else violations.Add((over, key + " " + (share * 100f).ToString("F1") + "%"));
                                if (violations.Count + knownOpenC.Count <= 12) SavePng(with, Path.Combine(dir, "md2-" + key.Replace('/', '_').Replace(':', '-') + ".png"));
                            }

                            Assert.Greater(platePx, 200, "kaynak yok: dō kadrajda değil (" + key + ")");
                        }
                    }
                }
            }

            binder.Reset();
            File.WriteAllText(Path.Combine(dir, "md2-zirh-ustu-kumas.tsv"), tsv.ToString());
            // C-00 06.10 (MD2 decision): fit C is a known open item until AP-008D refits the dō to the H2 bodies — reported per top and
            // view as report keys (the worse dō variant); after the refit these become checks at MaxClothOverPlateShare like A / B
            var json = new System.Text.StringBuilder("{\"schema\": \"wp084-md2-fitC-v1\", \"commit\": \"" + (Environment.GetEnvironmentVariable("DD_R2_COMMIT") ?? "")
                                                     + "\", \"limitLater\": " + MaxClothOverPlateShare.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                                                     + ", \"status\": \"known open - AP-008D refit\", \"coveredShare\": {");
            bool firstKey = true;
            foreach (KeyValuePair<string, float> kv in fitCReport)
            {
                json.Append(firstKey ? "" : ", ").Append('"').Append(kv.Key).Append("\": ").Append(kv.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                firstKey = false;
            }

            json.Append("}}\n");
            File.WriteAllText(Path.Combine(dir, "md2-fitC-report.json"), json.ToString());
            Debug.Log("[DD-OBS] wp084.md2.fitC " + string.Join(" ", new List<string>(fitCReport.Keys).ConvertAll(k => k + "=" + fitCReport[k].ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))));
            violations.Sort((x, y) => y.px.CompareTo(x.px));
            knownOpenC.Sort((x, y) => y.px.CompareTo(x.px));
            Debug.Log("[DD-OBS] wp084.md2 combos=" + combos + " views=" + combos * 2 + " maxShare=" + MaxClothOverPlateShare + " violationsAB=" + violations.Count
                      + " | " + string.Join(", ", violations.ConvertAll(x => x.key + "=" + x.px))
                      + " || knownOpenC (AP-008D refit, C-00 06.10)=" + knownOpenC.Count + " | " + string.Join(", ", knownOpenC.ConvertAll(x => x.key + "=" + x.px)));
            Assert.Greater(combos, 10, "kaynak yok: kombinasyon");
            Assert.IsEmpty(violations, "gövde zırhı üstünde kumaş (fit A/B): " + string.Join(", ", violations.ConvertAll(x => x.key + "=" + x.px + " px")));
        }

        /// <summary>
        /// WP-084 (MD2) · the chest–belly band of the figure in a <paramref name="w"/>×<paramref name="h"/> frame from <paramref name="eye"/>:
        /// vertically from the Hips bone to the upper chest, across the middle half of the shoulder span (humanoid bones; the body
        /// slot's bounds when the avatar has none). Pixel rows from the bottom, as GetPixels32 reads.
        /// </summary>
        private static RectInt ChestBelly(ZombieEnemy z, Vector3 eye, Quaternion rot, float fov, int w, int h)
        {
            Animator an = z.Skinned != null ? z.Skinned.Animator : null;
            Transform hips = null, chest = null, left = null, right = null;
            if (an != null && an.isHuman)
            {
                hips = an.GetBoneTransform(HumanBodyBones.Hips);
                chest = an.GetBoneTransform(HumanBodyBones.UpperChest) ?? an.GetBoneTransform(HumanBodyBones.Chest);
                left = an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                right = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }

            Vector3 lo, hi, l, r;
            if (hips != null && chest != null && left != null && right != null)
            {
                lo = hips.position; hi = chest.position; l = left.position; r = right.position;
            }
            else
            {
                Bounds b = z.Skinned.Slot(0, ZombieSkinnedVisual.SlotBody).bounds;
                lo = b.center - Vector3.up * b.extents.y * 0.1f; hi = b.center + Vector3.up * b.extents.y * 0.45f;
                l = b.center - z.transform.right * b.extents.x * 0.6f; r = b.center + z.transform.right * b.extents.x * 0.6f;
            }

            Matrix4x4 view = Matrix4x4.TRS(eye, rot, new Vector3(1f, 1f, -1f)).inverse;
            Matrix4x4 proj = Matrix4x4.Perspective(fov, (float)w / h, 0.05f, 100f);
            Vector2 P(Vector3 world)
            {
                Vector4 c = proj * (view * new Vector4(world.x, world.y, world.z, 1f));
                return new Vector2((c.x / c.w * 0.5f + 0.5f) * w, (c.y / c.w * 0.5f + 0.5f) * h);
            }

            Vector2 pl = P(lo), ph = P(hi), sl = P(l), sr = P(r);
            float cx = (sl.x + sr.x) * 0.5f, half = Mathf.Abs(sl.x - sr.x) * 0.25f;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(cx - half), 0, w), x1 = Mathf.Clamp(Mathf.RoundToInt(cx + half), 0, w);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(pl.y, ph.y)), 0, h), y1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(pl.y, ph.y)), 0, h);
            return new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }

        private static List<Renderer> DrawnRenderers(ZombieEnemy z)
        {
            var list = new List<Renderer>();
            foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false)) if (r.enabled && r.gameObject.activeInHierarchy) list.Add(r);
            return list;
        }

        private static List<Renderer> SlotRenderers(ZombieEnemy z, int slot)
        {
            var list = new List<Renderer>();
            for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++) { Renderer r = z.Skinned.Slot(lod, slot); if (r != null) list.Add(r); }
            return list;
        }

        private static List<Renderer> ArmourRenderers(ZombieEnemy z, ArmorSlot slot)
        {
            var list = new List<Renderer>();
            int s = (int)slot - 1;
            for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                for (int part = 0; part < ZombieSkinnedVisual.ArmorPartCount; part++)
                {
                    Renderer r = z.Skinned.ArmorRenderer(lod, s, part);
                    if (r != null && r.enabled && r.gameObject.activeInHierarchy) list.Add(r);
                }
            return list;
        }

        private static bool[] Mask(Color32[] px)
        {
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].r + px[i].g + px[i].b > 0;
            return m;
        }

        /// <summary>
        /// WP-084 (MD1 / MD2) · <paramref name="parts"/> drawn ALONE from <paramref name="eye"/>: moved to a free layer for one render that
        /// culls everything else, black clear, post-processing off (reflection: no URP reference here), no shadow casting — what is
        /// visible of these renderers and nothing else.
        /// </summary>
        private static Color32[] AloneFrame(Camera cam, List<Renderer> parts, Vector3 eye, Quaternion rot, float fov, int w, int h)
        {
            int layer = -1;
            for (int i = 31; i >= 8 && layer < 0; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) layer = i;
            Assert.GreaterOrEqual(layer, 0, "kaynak yok: boş katman");
            var layers = new List<int>(parts.Count);
            var shadows = new List<UnityEngine.Rendering.ShadowCastingMode>(parts.Count);
            foreach (Renderer r in parts) { layers.Add(r.gameObject.layer); shadows.Add(r.shadowCastingMode); r.gameObject.layer = layer; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            Transform t = cam.transform;
            Vector3 p = t.position; Quaternion q = t.rotation; float f = cam.fieldOfView;
            int cull = cam.cullingMask; CameraClearFlags clear = cam.clearFlags; Color bg = cam.backgroundColor;
            Component urp = cam.GetComponent("UniversalAdditionalCameraData");
            System.Reflection.PropertyInfo post = urp != null ? urp.GetType().GetProperty("renderPostProcessing") : null;
            object hadPost = post != null ? post.GetValue(urp) : null;
            var rt = new RenderTexture(w, h, 24) { name = "WP-084 alone", antiAliasing = 1 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            try
            {
                t.SetPositionAndRotation(eye, rot);
                cam.fieldOfView = fov;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                if (post != null) post.SetValue(urp, false);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.Destroy(tex);
                return px;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                rt.Release();
                t.SetPositionAndRotation(p, q);
                cam.fieldOfView = f;
                cam.cullingMask = cull; cam.clearFlags = clear; cam.backgroundColor = bg;
                if (post != null) post.SetValue(urp, hadPost);
                for (int i = 0; i < parts.Count; i++) { parts[i].gameObject.layer = layers[i]; parts[i].shadowCastingMode = shadows[i]; }
            }
        }

        private static void SavePng(Color32[] px, string path)
        {
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
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
        private static void Frame(Camera cam, List<ZombieEnemy> bodies, Vector3 lightDir, float height, float halfHeight, float fov, string png,
                                  List<string> roiFails, bool faceGate, ZombieEnemy target = null)
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
            Debug.Log("[DD-OBS] wp084.ad.frame " + Path.GetFileName(png) + " dist=" + dist.ToString("F2"));
            Roi(cam, bodies, eye, Quaternion.LookRotation(aim - eye), fov, Path.GetFileName(png), roiFails, faceGate, target);
        }

        /// <summary>
        /// WP-084 (AD delta MD1) · each actor's VISIBLE pixels: the actor drawn alone (AloneFrame) against the frame with and without it
        /// (what changes when it is hidden, at the same instant) — visible share = |alone ∧ changed| / |alone|; the face slot the same
        /// way on its own, with its on-screen height. Gates (AD proposal): every actor ≥ 90 % visible when it is the frame's subject
        /// (all of them, or <paramref name="target"/> alone), the face ≥ 120 px tall and ≥ 90 % visible on the face frames.
        /// </summary>
        private static void Roi(Camera cam, List<ZombieEnemy> bodies, Vector3 eye, Quaternion rot, float fov, string png, List<string> fails, bool faceGate, ZombieEnemy target)
        {
            const int w = 1920, h = 1080;
            Color32[] scene = AloneFrameScene(cam, eye, rot, fov, w, h, null);
            var line = new System.Text.StringBuilder("[DD-OBS] wp084.ad.roi " + png);
            foreach (ZombieEnemy z in bodies)
            {
                List<Renderer> body = DrawnRenderers(z);
                List<Renderer> face = SlotRenderers(z, ZombieSkinnedVisual.SlotFace).FindAll(r => r.enabled && r.gameObject.activeInHierarchy);
                float bodyShare = VisibleShare(cam, body, scene, eye, rot, fov, w, h, out int bodyPx, out _);
                int faceH = 0;
                float faceShare = 0f;
                if (face.Count > 0) faceShare = VisibleShare(cam, face, scene, eye, rot, fov, w, h, out _, out faceH);
                line.Append(' ').Append(z.Tier).Append(":visible=").Append(bodyShare.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append('(').Append(bodyPx).Append("px)")
                    .Append(",face=").Append(faceShare.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append("/h").Append(faceH);
                bool subject = target == null || z == target;
                if (subject && bodyShare < 0.90f) fails.Add(png + " " + z.Tier + " görünür %" + (bodyShare * 100f).ToString("F0"));
                if (faceGate && subject && (faceH < 120 || faceShare < 0.90f)) fails.Add(png + " " + z.Tier + " yüz h" + faceH + " görünür %" + (faceShare * 100f).ToString("F0"));
            }

            Debug.Log(line.ToString());
        }

        private static float VisibleShare(Camera cam, List<Renderer> parts, Color32[] scene, Vector3 eye, Quaternion rot, float fov, int w, int h, out int alonePx, out int height)
        {
            bool[] alone = Mask(AloneFrame(cam, parts, eye, rot, fov, w, h));
            Color32[] without = AloneFrameScene(cam, eye, rot, fov, w, h, parts);
            int both = 0, minY = int.MaxValue, maxY = int.MinValue;
            alonePx = 0;
            for (int i = 0; i < alone.Length; i++)
            {
                if (!alone[i]) continue;
                alonePx++;
                int y = i / w; minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                Color32 a = scene[i], b = without[i];
                if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 0) both++;
            }

            height = alonePx > 0 ? maxY - minY + 1 : 0;
            return alonePx > 0 ? (float)both / alonePx : 0f;
        }

        /// <summary>The game camera's frame from <paramref name="eye"/> with <paramref name="hidden"/> switched off for the one render (same instant).</summary>
        private static Color32[] AloneFrameScene(Camera cam, Vector3 eye, Quaternion rot, float fov, int w, int h, List<Renderer> hidden)
        {
            var off = new List<Renderer>();
            if (hidden != null) foreach (Renderer r in hidden) if (r.enabled) { r.enabled = false; off.Add(r); }
            Transform t = cam.transform;
            Vector3 p = t.position; Quaternion q = t.rotation; float f = cam.fieldOfView;
            var rt = new RenderTexture(w, h, 24) { name = "WP-084 roi scene", antiAliasing = 1 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            try
            {
                t.SetPositionAndRotation(eye, rot);
                cam.fieldOfView = fov;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.Destroy(tex);
                return px;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                rt.Release();
                t.SetPositionAndRotation(p, q);
                cam.fieldOfView = f;
                foreach (Renderer r in off) r.enabled = true;
            }
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
