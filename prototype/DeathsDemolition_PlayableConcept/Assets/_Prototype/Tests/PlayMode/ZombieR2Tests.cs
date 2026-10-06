using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// Z4 — face, eyes and teeth on every family (AP-025 face modules on the shared skeleton's Head). The jaw is C-00's 05.10
    /// decision (a): an empty jaw transform now, the face jaw weights with AP-025D — Z4b (EditMode ArtCharacterTests.Z4b_YuzCenesi_JawAgirligi_CullBack — the face meshes are not readable in play mode).
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

        // Z3b — r2-gates.json. C-00 05.10: the fixed-mix R1 (84941661, 22/10/6/2) measured 16 / 567 / 88 → zombie SetPass ≤ R1 + 8,
        // draws ≤ R1 × 1.45 (the old 450/310 ratio), frame ≤ R1 + 10; texture ≤ 96 MB unchanged. Reason: every version on screen
        // adds its own tint / part materials, so the pass count grows with the mix (evidence K/wp-088/evidence/r1).
        private const double MaxSharedTextureGpuMB = 96, MaxUniqueTexturesPerZombie = 0, MaxZombieSetPass = 24, MaxZombieDraws = 822, MaxFrameSetPass = 98;

        private static int? s_frameRate;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            if (s_frameRate.HasValue) { Application.targetFrameRate = s_frameRate.Value; s_frameRate = null; }   // Z3b uncaps it
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();   // Z3b loads the AP005 set; the next test of the session starts clean
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
            string pngDir = Environment.GetEnvironmentVariable("DD_WP084_Z2_DIR");   // WP-084: the frames behind the numbers (diagnosis)
            if (!string.IsNullOrEmpty(pngDir)) { Directory.CreateDirectory(pngDir); SavePng(empty, 640, 480, Path.Combine(pngDir, "z2-empty.png")); }
            Time.timeScale = 1f;

            var masks = new bool[Tiers.Length][];
            var luma = new float[Tiers.Length];
            var chroma = new Vector2[Tiers.Length];
            for (int t = 0; t < Tiers.Length; t++)
            {
                EnemyFactory.ResetSeedForTests(7u);   // the same look seed for every version: only the version differs
                ZombieEnemy z = EnemyFactory.Spawn(Tiers[t], 8, spot, Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(z, Tiers[t] + " spawn");
                Debug.Log("[DD-OBS] r2.z2 tier=" + Tiers[t] + " body=" + z.name + " look=" + (z.Skinned != null ? z.Skinned.Look.Key : "-"));
                // WP-084 (game-test-engineer 06.10): a fixed step, so the frozen pose is the same animation phase for every version
                Time.captureDeltaTime = 1f / 60f;
                for (int i = 0; i < 30; i++) { z.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd)); yield return null; }
                Time.captureDeltaTime = 0f;
                Time.timeScale = 0f;
                z.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd));
                PinPose(z);
                yield return null;
                Color32[] frame = CaptureFrom(game, eye, look, 40f, 640, 480);
                if (!string.IsNullOrEmpty(pngDir)) SavePng(frame, 640, 480, Path.Combine(pngDir, "z2-" + Tiers[t] + ".png"));
                Time.timeScale = 1f;
                masks[t] = SilhouetteMask(z, game, eye, look);
                Appearance(frame, masks[t], out luma[t], out chroma[t]);
                z.Remove();
                yield return null;
            }

            // control pair: Standard again with the same seed and step — the measurement's own noise must stay under every bar,
            // otherwise a "distinct" pair could be pose noise (the silhouette alone passes a pair under the OR rule)
            {
                EnemyFactory.ResetSeedForTests(7u);
                ZombieEnemy again = EnemyFactory.Spawn(Tiers[0], 8, spot, Quaternion.LookRotation(-fwd));
                Assert.IsNotNull(again, "kontrol doğuşu");
                Time.captureDeltaTime = 1f / 60f;
                for (int i = 0; i < 30; i++) { again.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd)); yield return null; }
                Time.captureDeltaTime = 0f;
                Time.timeScale = 0f;
                again.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-fwd));
                PinPose(again);
                yield return null;
                Color32[] repeat = CaptureFrom(game, eye, look, 40f, 640, 480);
                if (!string.IsNullOrEmpty(pngDir)) SavePng(repeat, 640, 480, Path.Combine(pngDir, "z2-Standard-control.png"));
                Debug.Log("[DD-OBS] r2.z2 control body=" + again.name + " pos=" + again.transform.position.ToString("F3") + " scale=" + again.transform.lossyScale.ToString("F3")
                          + " look=" + (again.Skinned != null ? again.Skinned.Look.Key : "-"));
                Time.timeScale = 1f;
                bool[] control = SilhouetteMask(again, game, eye, look);
                Appearance(repeat, control, out float controlLuma, out Vector2 controlChroma);
                again.Remove();
                yield return null;
                float silNoise = 1f - IoU(masks[0], control), lumaNoise = Mathf.Abs(luma[0] - controlLuma), chromaNoise = Vector2.Distance(chroma[0], controlChroma);
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[DD-OBS] r2.z2 control Standard-Standard silhouetteDelta={0:0.000} lumaDelta={1:0.000} chromaDelta={2:0.000}", silNoise, lumaNoise, chromaNoise));
                Assert.Less(silNoise, 0.02f, "ölçüm gürültüsü: aynı versiyonun iki karesi silüette ayrışıyor (poz sabit değil)");
                Assert.Less(lumaNoise, MinLumaDelta * 0.5f, "ölçüm gürültüsü: luma");
                Assert.Less(chromaNoise, MinChromaDelta * 0.5f, "ölçüm gürültüsü: kroma");
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
            s_frameRate = Application.targetFrameRate;
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
            var gate = new double[10];
            yield return MeasureCrowd(director, R2ZombieCrowd.GateMix, "gate", gate);
            int[] worstMix = R2ZombieCrowd.WorstMix();
            var worst = new double[10];
            yield return MeasureCrowd(director, worstMix, "worst", worst);

            // R2 standard "the source is in the record" (S-10 d3c15529): no artefact unless the crowd was really there and drawn.
            Assert.AreEqual(R2ZombieCrowd.Size, (int)gate[5], "kaynak yok: sabit karışım tam doğmadı");
            Assert.AreEqual(R2ZombieCrowd.Size, (int)worst[5], "kaynak yok: en kötü karışım tam doğmadı");
            Assert.Greater(gate[6], 0, "kaynak yok: kamerada görünür zombi renderer'ı yok");
            Assert.Greater(gate[2], 0, "kaynak yok: kare render edilmedi (sayaç 0)");
            Assert.Greater(gate[1], 0, "kaynak yok: zombiler çizilmedi (zombi draw 0)");
            Assert.Greater(worst[1], 0, "kaynak yok: en kötü karışım çizilmedi");
            Assert.Greater(gate[3], 0, "kaynak yok: zombi dokusu okunmadı");
            // WP-084 (game-test-engineer 06.10): the AP-025 content is what was measured — every body carries a bound face at LOD0,
            // the masks path is on, and the frame shows a crowd rather than one renderer
            Assert.AreEqual(R2ZombieCrowd.Size, (int)gate[7], "kaynak yok: LOD0 yüz yuvası bağlı olmayan gövde var");
            Assert.Greater(gate[8], 0, "kaynak yok: hiçbir zombi malzemesinde _ZOMBIE_MASKS açık değil");
            Assert.GreaterOrEqual(gate[9], 5, "kaynak yok: kamerada görünür zombi gövdesi 5'ten az (840e3768: 7 / 8)");

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
        /// zombie renderers visible to the game camera, bodies with a bound LOD0 face, zombie materials with _ZOMBIE_MASKS on,
        /// bodies visible to the game camera].
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
            // WP-084 (C-00 06.10): the AP-025 face slot's share on its own — the same frozen frame with only the SlotFace renderers off
            // (reported, not gated: the gate is the whole zombie share below).
            var faceRenderers = new List<Renderer>();
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || z.Skinned == null) continue;
                for (int lod = 0; lod < ZombieModuleLibrary.LodCount; lod++)
                {
                    Renderer fr = z.Skinned.Slot(lod, ZombieSkinnedVisual.SlotFace);
                    if (fr != null && fr.enabled && fr.gameObject.activeInHierarchy) faceRenderers.Add(fr);
                }
            }
            long[] noFaces = new long[3];
            foreach (Renderer r in faceRenderers) r.enabled = false;
            yield return Counters(cam, noFaces);
            foreach (Renderer r in faceRenderers) r.enabled = true;
            int facesLod0 = 0;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                SkinnedMeshRenderer f0 = z != null && z.Skinned != null ? z.Skinned.Slot(0, ZombieSkinnedVisual.SlotFace) : null;
                if (f0 != null && f0.enabled && f0.sharedMesh != null && z.Skinned.Look.HasFace) facesLod0++;
            }

            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[DD-OBS] r2.z3b.face mix={0} bodiesWithFace={1} faceRenderersAllLods={2} faceSetPass={3} faceDraws={4} faceBatches={5}",
                label, facesLod0, faceRenderers.Count, frame[0] - noFaces[0], frame[1] - noFaces[1], frame[2] - noFaces[2]));
            var zombieRenderers = new List<Renderer>();
            foreach (ZombieEnemy z in ZombieEnemy.Live) if (z != null) foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false)) if (r.enabled) zombieRenderers.Add(r);
            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
            int visible = 0, visibleBodies = 0;
            foreach (Renderer r in zombieRenderers) if (GeometryUtility.TestPlanesAABB(frustum, r.bounds)) visible++;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                SkinnedMeshRenderer b0 = z != null && z.Skinned != null ? z.Skinned.Slot(0, ZombieSkinnedVisual.SlotBody) : null;
                if (b0 != null && GeometryUtility.TestPlanesAABB(frustum, b0.bounds)) visibleBodies++;
            }

            // S-05 §17 A2 census (C-00 06.10): the crowd draws from shared materials only — distinct materials, (shader, keyword)
            // sets, property blocks, instanced materials and the distinct mask-index combinations riding renderingLayerMask
            var materialsSeen = new HashSet<Material>();
            var keywordSets = new HashSet<string>(StringComparer.Ordinal);
            var highBits = new HashSet<uint>();
            int withBlock = 0, instancedMaterials = 0, masksOn = 0;
            var odd = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null) continue;
                foreach (Renderer r in z.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;   // what draws (the hidden M1 block rig does not)
                    if (r.HasPropertyBlock()) withBlock++;
                    highBits.Add(r.renderingLayerMask & 0xFFFF0000u);
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || !materialsSeen.Add(m)) continue;
                        if (m.name.EndsWith("(Instance)", StringComparison.Ordinal) || m.GetInstanceID() < 0) { instancedMaterials++; odd.Add(r.name + ":" + m.name + "(" + m.shader.name + ")"); }
                        else if (m.shader.name != "DD/Character/Tier Lit") odd.Add(r.name + ":" + m.name + "(" + m.shader.name + ")");
                        if (m.IsKeywordEnabled("_ZOMBIE_MASKS")) masksOn++;
                        var kw = new List<string>(m.shaderKeywords);
                        kw.Sort(StringComparer.Ordinal);
                        keywordSets.Add(m.shader.name + "|" + string.Join(" ", kw));
                    }
                }
            }

            var sets = new List<string>(keywordSets);
            sets.Sort(StringComparer.Ordinal);
            Debug.Log("[DD-OBS] r2.z3b.a2 mix=" + label + " materials=" + materialsSeen.Count + " keywordSets=" + keywordSets.Count + " propertyBlocks=" + withBlock
                      + " instancedMaterials=" + instancedMaterials + " masksOnMaterials=" + masksOn + " maskCombos=" + highBits.Count + " visibleBodies=" + visibleBodies
                      + " | " + string.Join(" | ", sets) + " || not Tier Lit / runtime: " + string.Join(", ", odd));
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
            result[7] = facesLod0; result[8] = masksOn; result[9] = visibleBodies;
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

            // AP-025 (intake note §2/§8): anatomical face modules, kind "face", a variant per body fit, 4 LODs, bound to the shared
            // skeleton's Head, eyes and teeth inside the face mesh. WP-084: the jaw binding is Z4b (C-00 05.10 decision (a)).
            var faces = new List<ZombieModuleLibrary.Module>();
            if (library.modules != null) foreach (ZombieModuleLibrary.Module m in library.modules) if (m != null && m.kind == "face") faces.Add(m);
            // R2 standard "the source is in the record" (S-10 d3c15529): no artefact without the measured thing — the gate is
            // BLOCKED by name until AP-025-B delivers the face modules.
            Assert.IsTrue(library.bodies != null && library.bodies.Length > 0, "kaynak yok: kütüphanede gövde ailesi yok");
            Assert.Greater(faces.Count, 0, "kaynak yok: AP-025-B yüz modülleri henüz yok (kind \"face\") — Z4 BLOCKED");

            // WP-084: eyes and teeth are measured in EditMode (ArtCharacterTests.ZombiYuzleri_GozDis_TenDisiKoseler_AtlasBolgesi) — the
            // character meshes are not readable (isReadable 0), so their colours / UVs read empty while playing (840e3768: 0 / 0).
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
                        if (r == null || r.mesh == null || r.boneNames == null || Array.IndexOf(r.boneNames, "Head") < 0) lodsOk = false;
                    }
                    if (lodsOk) { ok = true; break; }
                }
                if (!ok) missing++;
                Debug.Log("[DD-OBS] r2.z4 fit=" + body.fit + " face=" + (ok ? "ok" : "MISSING"));
            }

            R2Olcum.Yaz("Z4", source, new Dictionary<string, double> { { "familiesMissingFace", missing } }, families);
            Assert.AreEqual(0, missing, "yüzü olmayan aile");
        }

        private static void SavePng(Color32[] pixels, int width, int height, string path)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
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

        /// <summary>
        /// WP-084 · the zombie's silhouette ALONE: its renderers moved to a free layer for one render that culls everything else, over
        /// a black clear with post-processing off. 06.10: the empty frame taken before the spawns measured the scene's drift (control
        /// pair 0.43, r6a2); a same-instant frame with the zombie hidden still let the rain, petals, lantern flicker and wet floor move
        /// the zombie's shadow / reflection pixels across the threshold (0.066, r6b3) — a silhouette has neither.
        /// </summary>
        private static bool[] SilhouetteMask(ZombieEnemy z, Camera cam, Vector3 eye, Quaternion look)
        {
            int layer = FreeLayer();
            var moved = new List<Renderer>();
            var layers = new List<int>();
            foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                moved.Add(r);
                layers.Add(r.gameObject.layer);
                r.gameObject.layer = layer;
            }

            int cull = cam.cullingMask;
            CameraClearFlags clear = cam.clearFlags;
            Color background = cam.backgroundColor;
            Component urp = cam.GetComponent("UniversalAdditionalCameraData");   // no URP reference in this assembly
            System.Reflection.PropertyInfo post = urp != null ? urp.GetType().GetProperty("renderPostProcessing") : null;
            object hadPost = post != null ? post.GetValue(urp) : null;
            try
            {
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                if (post != null) post.SetValue(urp, false);
                Color32[] px = CaptureFrom(cam, eye, look, 40f, 640, 480);
                var mask = new bool[px.Length];
                for (int i = 0; i < px.Length; i++) mask[i] = px[i].r + px[i].g + px[i].b > 0;
                return mask;
            }
            finally
            {
                cam.cullingMask = cull;
                cam.clearFlags = clear;
                cam.backgroundColor = background;
                if (post != null) post.SetValue(urp, hadPost);
                for (int i = 0; i < moved.Count; i++) moved[i].gameObject.layer = layers[i];
            }
        }

        private static int FreeLayer()
        {
            for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            Assert.Fail("kaynak yok: boş katman");
            return -1;
        }

        /// <summary>The mean luma and rg-chromaticity of the game frame inside the silhouette.</summary>
        private static void Appearance(Color32[] frame, bool[] mask, out float luma, out Vector2 chroma)
        {
            double l = 0, cr = 0, cg = 0;
            int n = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                if (!mask[i]) continue;
                Color32 a = frame[i];
                n++;
                l += (0.2126 * a.r + 0.7152 * a.g + 0.0722 * a.b) / 255.0;
                double s = a.r + a.g + a.b + 1e-3;
                cr += a.r / s; cg += a.g / s;
            }

            Assert.Greater(n, 200, "silhouette too small (zombie not in frame)");
            luma = (float)(l / n);
            chroma = new Vector2((float)(cr / n), (float)(cg / n));
        }

        private static float IoU(bool[] a, bool[] b)
        {
            int inter = 0, union = 0;
            for (int i = 0; i < a.Length; i++) { if (a[i] && b[i]) inter++; if (a[i] || b[i]) union++; }
            return union > 0 ? (float)inter / union : 0f;
        }
    }
}
