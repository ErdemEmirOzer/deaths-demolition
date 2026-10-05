using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// R2 zombie gates owned by S-03 (tools/qa/r2-gates.json; C-00 / S-10 05.10; intake note K/wp-088/R2-ZOMBI-V2-ALIM.md).
    /// Each test asserts its own threshold and hands its values to the acceptance chain through R2Olcum (DD_R2_MEASURE_DIR /
    /// DD_R2_COMMIT). Measurements, run by full name ([Explicit] + Diagnostics, Y6).
    /// Z2 — tier distinction: each version alone in one fixed frame; a pair is distinct by silhouette, luma or chroma.
    /// Z3b — the zombie share of the 40-zombie full-armour frame (the SetPassAttribution scenario, R1 6 SetPass / 310 draws)
    /// and the zombie texture budget.
    /// Z4 — face, eyes and teeth on every family (AP-025-B face modules; BLOCKED with a note until they are delivered).
    /// </summary>
    [TestFixture]
    [Explicit("R2 measurement: run by full name")]
    [Category("Diagnostics")]
    [Category("WP084")]
    public sealed class ZombieR2Tests
    {
        private const float SceneTimeout = 120f, SetupTimeout = 60f, FillTimeout = 90f;
        private static readonly EnemyTier[] Tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };

        // Z2 — the rule is OR: a pair of versions counts as distinct when ANY one cue (silhouette OR luma OR chroma) clears its
        // bar. Bars approved PROVISIONALLY (C-00 05.10); R1 today passes on silhouette alone (0.38–0.63; luma ≤ 0.027, chroma
        // ≤ 0.016). Re-measured and fixed on the AP-025-B skin/blood delivery (then reported to S-10).
        private const float MinSilhouetteDelta = 0.10f;   // 1 − IoU of the two silhouettes
        private const float MinLumaDelta = 0.04f;         // |mean luma| difference inside the silhouettes (0–1)
        private const float MinChromaDelta = 0.02f;       // distance of the mean rg-chromaticity inside the silhouettes

        // Z3b — r2-gates.json
        private const double MaxSharedTextureGpuMB = 96, MaxUniqueTexturesPerZombie = 0, MaxZombieSetPass = 14, MaxZombieDraws = 450, MaxFrameSetPass = 88;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            ZombieEnemy.ResetRisePolicy();
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------------------------------------------------ Z2

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Z2_TierCiftleri_Ayrisir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunaGir();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            Camera game = PrototypeCameraRig.Active.Camera;
            Vector3 fwd = Vector3.ProjectOnPlane(game.transform.forward, Vector3.up).normalized;
            Vector3 spot = NavPathFollower.ClampToNavMesh(PlayerRegistry.Primary.transform.position + fwd * 6f, 6f);
            Vector3 eye = spot - fwd * 4.2f + Vector3.up * 1.3f;
            Quaternion look = Quaternion.LookRotation(spot + Vector3.up * 1.0f - eye);

            Time.timeScale = 0f;
            yield return null;
            Color32[] empty = CaptureFrom(game, eye, look, 40f, 640, 480);
            Time.timeScale = 1f;

            var masks = new bool[Tiers.Length][];
            var luma = new float[Tiers.Length];
            var chroma = new Vector2[Tiers.Length];
            for (int t = 0; t < Tiers.Length; t++)
            {
                EnemyFactory.ResetSeedForTests(7u);   // the same look seed for every version: only the version differs
                ZombieEnemy z = EnemyFactory.Spawn(Tiers[t], 8, spot, Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, Tiers[t] + " spawn");
                for (int i = 0; i < 30; i++) { z.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd)); yield return null; }
                Time.timeScale = 0f;
                z.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd));
                yield return null;
                Color32[] frame = CaptureFrom(game, eye, look, 40f, 640, 480);
                Time.timeScale = 1f;
                masks[t] = Silhouette(frame, empty, out luma[t], out chroma[t]);
                z.Remove();
                yield return null;
            }

            int indistinct = 0, pairs = 0;
            for (int a = 0; a < Tiers.Length; a++)
            {
                for (int b = a + 1; b < Tiers.Length; b++)
                {
                    pairs++;
                    float sil = 1f - IoU(masks[a], masks[b]), dl = Mathf.Abs(luma[a] - luma[b]), dc = Vector2.Distance(chroma[a], chroma[b]);
                    bool bySil = sil >= MinSilhouetteDelta, byLuma = dl >= MinLumaDelta, byChroma = dc >= MinChromaDelta;
                    bool distinct = bySil || byLuma || byChroma;   // OR (see the bars above)
                    if (!distinct) indistinct++;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[DD-OBS] r2.z2 {0}-{1} rule=OR silhouetteDelta={2:0.000} lumaDelta={3:0.000} chromaDelta={4:0.000} distinct={5} by={6}",
                        Tiers[a], Tiers[b], sil, dl, dc, distinct, (bySil ? "silhouette " : "") + (byLuma ? "luma " : "") + (byChroma ? "chroma" : "")));
                }
            }

            R2Olcum.Yaz("Z2", "DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z2_TierCiftleri_Ayrisir",
                new Dictionary<string, double> { { "indistinctTierPairs", indistinct } }, pairs);
            Assert.AreEqual(0, indistinct, "ayırt edilemeyen tier çifti");
        }

        // ------------------------------------------------------------------------------------------------------------ Z3b

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Z3b_40Zombi_SetPassPayi_DokuButcesi()
        {
            LogAssert.ignoreFailingMessages = true;
            Application.targetFrameRate = -1;
            // The SetPassAttribution scenario (WP-038J; S-10's R1 baseline 84941661): AP005 world through the player's menu flow.
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SceneTimeout, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SceneTimeout, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SceneTimeout, "Run");
            yield return Bekle(() => SceneManager.GetSceneByName("World_AP005").isLoaded, SceneTimeout, "World_AP005");
            yield return Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, SetupTimeout, "oyuncu/kamera");
            yield return Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, SetupTimeout, "run sistemleri");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, SetupTimeout, "Fighting fazı");
            EnemySpawnDirector director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;

            // C-00 05.10: the gate is measured on the FIXED mix (R2ZombieCrowd.GateMix); the config's worst case is reported.
            var gate = new double[7];
            yield return MeasureCrowd(director, R2ZombieCrowd.GateMix, "gate", gate);
            int[] worstMix = R2ZombieCrowd.WorstMix();
            var worst = new double[7];
            yield return MeasureCrowd(director, worstMix, "worst", worst);

            // R2 standard "the source is in the record" (S-10 d3c15529): no artefact unless the crowd was really there and drawn.
            Assert.AreEqual(R2ZombieCrowd.Size, (int)gate[5], "kaynak yok: sabit karışım tam doğmadı");
            Assert.AreEqual(R2ZombieCrowd.Size, (int)worst[5], "kaynak yok: en kötü karışım tam doğmadı");
            Assert.Greater(gate[6], 0, "kaynak yok: kamerada görünür zombi renderer'ı yok");
            Assert.Greater(gate[2], 0, "kaynak yok: kare render edilmedi (sayaç 0)");
            Assert.Greater(gate[1], 0, "kaynak yok: zombiler çizilmedi (zombi draw 0)");
            Assert.Greater(worst[1], 0, "kaynak yok: en kötü karışım çizilmedi");
            Assert.Greater(gate[3], 0, "kaynak yok: zombi dokusu okunmadı");

            R2Olcum.Yaz("Z3b", "DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z3b_40Zombi_SetPassPayi_DokuButcesi", new Dictionary<string, double>
            {
                { "sharedTextureGpuMB", gate[3] }, { "uniqueTexturesPerZombie", gate[4] },
                { "zombieSetPass", gate[0] }, { "zombieDraws", gate[1] }, { "frameSetPass", gate[2] },
                { "zombieSetPassWorst", worst[0] }, { "zombieDrawsWorst", worst[1] }, { "sharedTextureGpuMBWorst", worst[3] },
            }, (int)gate[5]);

            Assert.LessOrEqual(gate[3], MaxSharedTextureGpuMB, "zombi ortak doku GPU MB");
            Assert.LessOrEqual(gate[4], MaxUniqueTexturesPerZombie, "zombiye özgü doku");
            Assert.LessOrEqual(gate[0], MaxZombieSetPass, "zombi SetPass payı");
            Assert.LessOrEqual(gate[1], MaxZombieDraws, "zombi draw payı");
            Assert.LessOrEqual(gate[2], MaxFrameSetPass, "kare SetPass");
        }

        /// <summary>
        /// One crowd: spawn the mix (R2ZombieCrowd), let it close in, freeze, then frame vs no-zombie counters and the zombie
        /// textures. result = [zombieSetPass, zombieDraws, frameSetPass, sharedTextureGpuMB, uniqueTexturesPerZombie, bodies,
        /// zombie renderers visible to the game camera].
        /// </summary>
        private static IEnumerator MeasureCrowd(EnemySpawnDirector director, int[] mix, string label, double[] result)
        {
            director.StopRound();
            director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            // let the previous crowd's effects (pooled spawn / hit flipbooks) run out at a fixed step — otherwise the first
            // measured crowd carried ~14 extra SetPass in its no-zombie frame (88 vs 74, repeatable)
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 300; i++) yield return null;
            Time.captureDeltaTime = 0f;
            // empty frame (diagnostic): the scene's renderers before the crowd
            Time.timeScale = 0f;
            yield return null;
            Camera cam0 = PrototypeCameraRig.Active.Camera;
            long[] empty = new long[3];
            yield return Counters(cam0, empty);
            var before = new HashSet<Renderer>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) before.Add(r);
            Time.timeScale = 1f;

            EnemyFactory.ResetSeedForTests(1u);   // the same looks for the same mix
            var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
            R2ZombieCrowd.Spawn(mix, 12, crowd);

            // A fixed simulation step while the crowd closes in: on a loaded machine 120 real frames covered a different distance
            // every run (zombie draws 420 vs 605 in two back-to-back runs). 4 s at 1/60 also outlasts the spawn effects.
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 240; i++) yield return null;
            Time.captureDeltaTime = 0f;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;

            Camera cam = PrototypeCameraRig.Active.Camera;
            long[] frame = new long[3], noZombies = new long[3];
            yield return Counters(cam, frame);
            var zombieRenderers = new List<Renderer>();
            foreach (ZombieEnemy z in ZombieEnemy.Live) if (z != null) foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false)) if (r.enabled) zombieRenderers.Add(r);
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
            int visible = 0;
            foreach (Renderer r in zombieRenderers) if (GeometryUtility.TestPlanesAABB(frustum, r.bounds)) visible++;
            foreach (Renderer r in zombieRenderers) r.enabled = false;
            yield return Counters(cam, noZombies);
            var extra = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || before.Contains(r)) continue;
                Transform root = r.transform; while (root.parent != null) root = root.parent;
                string key = root.name + "/" + r.GetType().Name + ":" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "-");
                extra[key] = extra.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            var ex = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kv in extra) ex.Append(kv.Key).Append(" ×").Append(kv.Value).Append(" | ");
            Debug.Log("[DD-OBS] r2.z3b.diag mix=" + label + " empty setPass=" + empty[0] + " draw=" + empty[1] + " | renderers new since empty, visible with zombies hidden: " + ex);
            foreach (Renderer r in zombieRenderers) r.enabled = true;

            // textures: every material on every zombie renderer (all LODs — they are resident either way). "Unique per zombie" =
            // per-instance data: a runtime-created texture (negative instance id) or a texture reached through an instanced
            // material ("(Instance)"); a shared asset used by only one zombie of this crowd (a lone Ultra) is not unique.
            var perZombie = new List<HashSet<Texture>>();
            var all = new HashSet<Texture>();
            var ids = new List<int>();
            int uniqueMax = 0;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null) continue;
                var set = new HashSet<Texture>();
                var own = new HashSet<Texture>();
                foreach (Renderer r in z.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        bool instanced = m.name.EndsWith("(Instance)", StringComparison.Ordinal) || m.GetInstanceID() < 0;
                        m.GetTexturePropertyNameIDs(ids);
                        foreach (int id in ids)
                        {
                            Texture tex = m.GetTexture(id);
                            if (tex == null) continue;
                            set.Add(tex);
                            if (instanced || tex.GetInstanceID() < 0) own.Add(tex);
                        }
                    }
                }
                perZombie.Add(set);
                uniqueMax = Math.Max(uniqueMax, own.Count);
                foreach (Texture tex in set) all.Add(tex);
            }

            double bytes = 0;
            var breakdown = new List<string>();
            foreach (Texture tex in all)
            {
                long b = Profiler.GetRuntimeMemorySizeLong(tex);
                bytes += b;
                breakdown.Add(tex.name + " " + tex.width + "x" + tex.height + " " + (b / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + "MB");
            }
            breakdown.Sort(StringComparer.Ordinal);
            Debug.Log("[DD-OBS] r2.z3b.textures mix=" + label + " " + string.Join(" | ", breakdown));

            double sharedMB = Math.Round(bytes / (1024.0 * 1024.0), 2);
            long zombieSetPass = frame[0] - noZombies[0], zombieDraws = frame[1] - noZombies[1];
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[DD-OBS] r2.z3b mix=" + label + " " + R2ZombieCrowd.Describe(mix) + " zombies={0} frame setPass={1} draw={2} batches={3} | no_zombies {4}/{5}/{6} | zombie setPass={7} draws={8} batches={9} | textures={10} sharedGpuMB={11:0.00} uniquePerZombieMax={12}",
                perZombie.Count, frame[0], frame[1], frame[2], noZombies[0], noZombies[1], noZombies[2], zombieSetPass, zombieDraws, frame[2] - noZombies[2], all.Count, sharedMB, uniqueMax));
            Time.timeScale = 1f;
            result[0] = zombieSetPass; result[1] = zombieDraws; result[2] = frame[0]; result[3] = sharedMB; result[4] = uniqueMax; result[5] = perZombie.Count; result[6] = visible;
            foreach (ZombieEnemy z in crowd) if (z != null && z.IsSpawned) z.Remove();
            yield return null;
        }

        // ------------------------------------------------------------------------------------------------------------ Z4

        [Test]
        public void Z4_HerAile_YuzGozDis()
        {
            const string source = "DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z4_HerAile_YuzGozDis";
            CharacterAssetSet set = CharacterAssetSet.Load();
            ZombieModuleLibrary library = set != null ? set.ZombieLibrary : null;
            Assert.IsNotNull(library, "zombie module library");

            // AP-025-B (intake note §2/§8): anatomical face modules, kind "face", a variant per body fit, 4 LODs, bound to the
            // shared skeleton with the jaw (teeth ride the jaw), eyes and teeth inside the face mesh.
            var faces = new List<ZombieModuleLibrary.Module>();
            if (library.modules != null) foreach (ZombieModuleLibrary.Module m in library.modules) if (m != null && m.kind == "face") faces.Add(m);
            // R2 standard "the source is in the record" (S-10 d3c15529): no artefact without the measured thing — the gate is
            // BLOCKED by name until AP-025-B delivers the face modules.
            Assert.IsTrue(library.bodies != null && library.bodies.Length > 0, "kaynak yok: kütüphanede gövde ailesi yok");
            Assert.Greater(faces.Count, 0, "kaynak yok: AP-025-B yüz modülleri henüz yok (kind \"face\") — Z4 BLOCKED");

            int missing = 0, families = 0;
            foreach (ZombieModuleLibrary.Body body in library.bodies)
            {
                if (body == null) continue;
                families++;
                bool ok = false;
                foreach (ZombieModuleLibrary.Module face in faces)
                {
                    ZombieModuleLibrary.Variant v = face.Find(ZombieModuleLibrary.NoColourway, body.fit);
                    if (v == null || v.lods == null || v.lods.Length < ZombieModuleLibrary.LodCount) continue;
                    bool lodsOk = true;
                    for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                    {
                        ZombieModuleLibrary.MeshRef r = v.lods[lod];
                        if (r == null || r.mesh == null || r.boneNames == null || Array.IndexOf(r.boneNames, "jaw") < 0) lodsOk = false;
                    }
                    if (lodsOk) { ok = true; break; }
                }
                if (!ok) missing++;
                Debug.Log("[DD-OBS] r2.z4 fit=" + body.fit + " face=" + (ok ? "ok" : "MISSING"));
            }

            R2Olcum.Yaz("Z4", source, new Dictionary<string, double> { { "familiesMissingFace", missing } }, families);
            Assert.AreEqual(0, missing, "yüzü olmayan aile");
        }

        // ------------------------------------------------------------------------------------------------------------ helpers

        private static IEnumerator RunaGir()
        {
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                                     && PrototypeCameraRig.Active != null && RunDirector.Instance.Phase == RunPhase.Fighting, SetupTimeout, "Run / Fighting");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t < saniye) yield return null;
            Assert.IsTrue(kosul(), aciklama + " (" + saniye + " s)");
        }

        /// <summary>
        /// Render counters of one hand-rendered frame of the game camera (1080p, MSAA 2×, the SetPassAttribution target): the
        /// camera's own per-frame render is switched off for the measurement so a profiler frame holds exactly one render, and
        /// the per-frame readings are reduced by their MEDIAN over 12 frames (the WP-038J max-of-14 caught two renders in one
        /// frame: an empty scene read 75 and 88 SetPass in the same session).
        /// </summary>
        private static IEnumerator Counters(Camera cam, long[] into)
        {
            var rt = new RenderTexture(1920, 1080, 24) { name = "R2 Z3b frame", antiAliasing = 2 };
            rt.Create();
            var sync = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "R2 Z3b sync" };
            sync.Create();
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            bool wasEnabled = cam.enabled;
            var sp = new List<long>(); var dr = new List<long>(); var ba = new List<long>();
            try
            {
                cam.enabled = false;
                yield return null;
                for (int k = 0; k < 14; k++)
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    Graphics.Blit(rt, sync);
                    UnityEngine.Rendering.AsyncGPUReadback.Request(sync, 0, TextureFormat.RGBA32).WaitForCompletion();
                    cam.targetTexture = null;
                    yield return null;
                    if (k < 2) continue;   // warm-up
                    sp.Add(setPass.LastValue); dr.Add(draws.LastValue); ba.Add(batches.LastValue);
                }
            }
            finally
            {
                setPass.Dispose(); draws.Dispose(); batches.Dispose();
                cam.targetTexture = null;
                cam.enabled = wasEnabled;
                rt.Release(); sync.Release();
            }

            into[0] = Median(sp); into[1] = Median(dr); into[2] = Median(ba);
            Debug.Log("[DD-OBS] r2.z3b.counters setPass=[" + string.Join(",", sp) + "] draw=[" + string.Join(",", dr) + "]");
        }

        private static long Median(List<long> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            return v[v.Count / 2];
        }

        /// <summary>The game camera (same post / exposure) moved for one render and put back; the ArtVisualRegression readable target.</summary>
        private static Color32[] CaptureFrom(Camera cam, Vector3 position, Quaternion rotation, float fov, int w, int h)
        {
            Transform t = cam.transform;
            Vector3 p = t.position; Quaternion q = t.rotation; float f = cam.fieldOfView;
            var rt = new RenderTexture(w, h, 24) { name = "R2 Z2 capture", antiAliasing = 1 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            try
            {
                t.SetPositionAndRotation(position, rotation);
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
                t.SetPositionAndRotation(p, q);
                cam.fieldOfView = f;
                rt.Release();
            }
        }

        /// <summary>Pixels that differ from the empty frame (the zombie), with the mean luma and rg-chromaticity inside them.</summary>
        private static bool[] Silhouette(Color32[] frame, Color32[] empty, out float luma, out Vector2 chroma)
        {
            var mask = new bool[frame.Length];
            double l = 0, cr = 0, cg = 0;
            int n = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                Color32 a = frame[i], b = empty[i];
                if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) <= 24) continue;
                mask[i] = true;
                n++;
                l += (0.2126 * a.r + 0.7152 * a.g + 0.0722 * a.b) / 255.0;
                double s = a.r + a.g + a.b + 1e-3;
                cr += a.r / s; cg += a.g / s;
            }

            Assert.Greater(n, 200, "silhouette too small (zombie not in frame)");
            luma = (float)(l / n);
            chroma = new Vector2((float)(cr / n), (float)(cg / n));
            return mask;
        }

        private static float IoU(bool[] a, bool[] b)
        {
            int inter = 0, union = 0;
            for (int i = 0; i < a.Length; i++) { if (a[i] && b[i]) inter++; if (a[i] || b[i]) union++; }
            return union > 0 ? (float)inter / union : 0f;
        }
    }
}
