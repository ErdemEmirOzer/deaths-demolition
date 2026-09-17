#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine.Rendering.Universal;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-038J (M3 K1: setPass 61 → 138) · Attribution of the probe frame's render state on the world + post side: Run on
    /// World_AP005, 40 forced zombies in full armour, the gameplay rig rendered by hand into a 1080p MSAA-2× target with the
    /// simulation frozen, the Render-category ProfilerRecorders read per scenario (baseline, then one feature off at a time —
    /// renderers by shader, shadows, cascades, post, bloom, SSAO, decals, MSAA). Also written: the shader-variant census of
    /// the visible renderers (the SRP Batcher's batch keys), the UI canvas census (overlay UI never goes through
    /// Camera.Render, so it is outside the numbers) and a same-frame A/B PNG pair of the shipped post/SSAO settings against
    /// the WP-029 ones (the ΔE evidence for "no visible change"). Batch mode repaints no Game view, so the Editor Frame
    /// Debugger records nothing — this is the equivalent. Diagnostic, run by name; not part of the regression.
    /// </summary>
    [Explicit("WP-038J diagnostic: run by name (-testFilter DD.Art.Tests.PlayMode.SetPassAttributionTests)")]
    [Category("Diagnostics")]
    public sealed class SetPassAttributionTests
    {
        private const float SahneZamanAsimi = 120f, OyuncuZamanAsimi = 60f, KurulumZamanAsimi = 60f, DolusZamanAsimi = 90f;
        private EnemySpawnDirector _director;

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); _director = null; }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            if (_director != null) _director.ForceActiveTarget(0, false);
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            }

            _director = null;
        }

        [TearDown]
        public void TearDown() { LogAssert.ignoreFailingMessages = false; }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SetPass_Kaynaklari_40ZombiTamZirh()
        {
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, "AP005");
            CharacterAssetSet.ResetCache();
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Selection, SahneZamanAsimi, "Selection");
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "Run");
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, OyuncuZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "run sistemleri");
            yield return CharacterTests.Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            _director = EnemySpawnDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;

            _director.ForceActiveTarget(40, true);
            yield return CharacterTests.Bekle(() => _director.ActiveCount >= 40, DolusZamanAsimi, "40 zombi doğmadı");
            int fitted = 0;
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null || z.Armor == null || z.Armor.EquippedCount >= 3) continue;
                fitted += z.FitArmorComposition(true, true, true);
            }

            // Same 1080p game view the probe renders; let streaming and the crowd settle, then freeze the simulation so every
            // scenario renders the same frame (the crowd keeps closing in and hitting otherwise — draws drift by thousands).
            for (int i = 0; i < 120; i++) yield return null;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;

            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp038j-setpass");
            Directory.CreateDirectory(dir);

            // Batch mode repaints no Game view (the Frame Debugger never sees a frame end): the rig camera is rendered by hand
            // into a 1080p MSAA-2× target every frame (URP takes the MSAA count from the target) and the Render-category
            // ProfilerRecorders are read the frame after — the probe's own counters. Overlay UI does not go through
            // Camera.Render(), so the numbers are the world + post-fx side of the probe's setPass.
            Camera rigCamera = PrototypeCameraRig.Active.Camera;
            Assert.IsNotNull(rigCamera, "rig kamerası yok");
            var rt = new RenderTexture(1920, 1080, 24) { name = "WP-038J frame", antiAliasing = 2 };
            rt.Create();
            var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            var sb = new StringBuilder();
            sb.Append("scenario\tsetPass\tdraw\tbatches\trenderMs_median\trenderMs_p90\n");
            var rows = new List<string>();

            RenderTexture target = rt;
            // GPU fence: a tiny linear RGBA32 target the frame is blitted into, read back synchronously (the sRGB frame
            // target itself refuses ReadPixels on Metal); the in-order queue means the wait covers the whole render.
            var sync = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "WP-038J sync" };
            sync.Create();
            IEnumerator Measure(string label)
            {
                // The Editor's render counters are sampled irregularly without a Game view: render several frames, keep the
                // largest reading of CurrentValue (same frame) / LastValue (next frame). Time = Render() + a GPU readback
                // wait (CPU submit + GPU work, an upper bound; the deltas between scenarios are the attribution), median of 12.
                long sp = 0, dr = 0, ba = 0;
                var times = new List<double>();
                for (int k = 0; k < 14; k++)
                {
                    rigCamera.targetTexture = target;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    rigCamera.Render();
                    Graphics.Blit(target, sync);
                    UnityEngine.Rendering.AsyncGPUReadback.Request(sync, 0, TextureFormat.RGBA32).WaitForCompletion();
                    sw.Stop();
                    rigCamera.targetTexture = null;
                    if (k >= 2) times.Add(sw.Elapsed.TotalMilliseconds);
                    sp = Math.Max(sp, setPass.CurrentValue); dr = Math.Max(dr, draws.CurrentValue); ba = Math.Max(ba, batches.CurrentValue);
                    yield return null;
                    sp = Math.Max(sp, setPass.LastValue); dr = Math.Max(dr, draws.LastValue); ba = Math.Max(ba, batches.LastValue);
                }

                times.Sort();
                double median = times[times.Count / 2], p90 = times[(int)(times.Count * 0.9)];
                string row = label + "\t" + sp + "\t" + dr + "\t" + ba + "\t" + median.ToString("F2", CultureInfo.InvariantCulture) + "\t" + p90.ToString("F2", CultureInfo.InvariantCulture);
                rows.Add(row); sb.Append(row).Append('\n');
                Debug.Log("[DD-TEST] wp038j.setpass " + row.Replace('\t', ' '));
            }

            yield return Measure("baseline");

            // ---- toggles (each restored before the next)
            // Only renderers that are ON are toggled (the M1 block bodies under the skinned zombies are disabled on purpose —
            // re-enabling everything would put ~450 hidden block draws back, the artefact P-06 found in the QA gate).
            var zombieRenderers = new List<Renderer>();
            foreach (ZombieEnemy z in ZombieEnemy.Live) if (z != null) foreach (Renderer r in z.GetComponentsInChildren<Renderer>(false)) if (r.enabled) zombieRenderers.Add(r);
            SetRenderers(zombieRenderers, false); yield return Measure("no_zombies"); SetRenderers(zombieRenderers, true);

            var playerRenderers = new List<Renderer>();
            foreach (Renderer r in PlayerRegistry.Primary.GetComponentsInChildren<Renderer>(false)) if (r.enabled) playerRenderers.Add(r);
            SetRenderers(playerRenderers, false); yield return Measure("no_player"); SetRenderers(playerRenderers, true);

            var particles = new List<Renderer>();
            foreach (ParticleSystemRenderer r in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>()) if (r.enabled) particles.Add(r);
            SetRenderers(particles, false); yield return Measure("no_particles(" + particles.Count + ")"); SetRenderers(particles, true);

            var decals = new List<Behaviour>();
            foreach (UnityEngine.Rendering.Universal.DecalProjector d in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Universal.DecalProjector>()) if (d.enabled) decals.Add(d);
            foreach (Behaviour b in decals) b.enabled = false; yield return Measure("no_decals(" + decals.Count + ")"); foreach (Behaviour b in decals) b.enabled = true;

            yield return ToggleByShader("no_skyplates", "DD/SkyPlate", Measure);
            yield return ToggleByShader("no_water", "DD/Water", Measure);
            yield return ToggleByShader("no_groundblend", "DD/GroundBlend", Measure);
            yield return ToggleByShader("no_foliagewind", "DD/Foliage", Measure);
            yield return ToggleByShader("no_tierlit", "DD/TierLit", Measure);

            var lights = new List<Light>();
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>()) if (l.enabled && l.type != LightType.Directional) lights.Add(l);
            foreach (Light l in lights) l.enabled = false; yield return Measure("no_pointlights(" + lights.Count + ")"); foreach (Light l in lights) l.enabled = true;

            Light fill = null;
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>()) if (l.name.StartsWith("Fill (characters", StringComparison.Ordinal)) fill = l;
            if (fill != null) { fill.enabled = false; yield return Measure("no_filllight"); fill.enabled = true; }

            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null)
            {
                float shadowDistance = urp.shadowDistance;
                urp.shadowDistance = 0f; yield return Measure("no_shadows"); urp.shadowDistance = shadowDistance;
                int cascades = urp.shadowCascadeCount;
                urp.shadowCascadeCount = 1; yield return Measure("one_cascade"); urp.shadowCascadeCount = cascades;
            }

            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData camData = rigCamera.GetUniversalAdditionalCameraData();
            bool post = camData.renderPostProcessing;
            camData.renderPostProcessing = false; yield return Measure("no_postfx"); camData.renderPostProcessing = post;

            // Bloom alone (the Global Volume profile's override; restored after)
            foreach (UnityEngine.Rendering.Volume vol in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>())
            {
                if (vol.sharedProfile == null || !vol.sharedProfile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom)) continue;
                bool wasActive = bloom.active; bool hq = bloom.highQualityFiltering.value;
                bloom.active = false; yield return Measure("no_bloom(" + vol.sharedProfile.name + ")"); bloom.active = wasActive;
                bloom.highQualityFiltering.value = false; yield return Measure("bloom_hq_off"); bloom.highQualityFiltering.value = hq;
                break;
            }

            var rendererData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/DD_URP_Renderer.asset");
            var toggledFeatures = new List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();
            if (rendererData != null)
            {
                foreach (UnityEngine.Rendering.Universal.ScriptableRendererFeature f in rendererData.rendererFeatures)
                {
                    if (f == null || !f.isActive) continue;
                    f.SetActive(false); toggledFeatures.Add(f);
                    yield return Measure("no_feature:" + f.GetType().Name);
                    f.SetActive(true);
                }
            }

            var rt1 = new RenderTexture(1920, 1080, 24) { name = "WP-038J frame 1x", antiAliasing = 1 };
            rt1.Create(); target = rt1; yield return Measure("msaa_1x"); target = rt;
            rt1.Release(); UnityEngine.Object.DestroyImmediate(rt1);

            // ---- A/B frame on the SAME frozen scene: shipped post/SSAO settings vs the WP-029 ones (bloom 6 iterations + bicubic,
            // SSAO blur Medium) — the ΔE between the two PNGs is the "no visible change" evidence (planning/04 §WP-038J).
            {
                Texture2D Snap(string file)
                {
                    rigCamera.targetTexture = rt; rigCamera.Render(); rigCamera.targetTexture = null;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); tex.Apply(false, false);
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
                    return tex;
                }

                Texture2D a = Snap("ab-shipped.png");
                UnityEngine.Rendering.Universal.Bloom bloomAb = null; UnityEngine.Rendering.Volume volAb = null;
                foreach (UnityEngine.Rendering.Volume vol in UnityEngine.Object.FindObjectsOfType<UnityEngine.Rendering.Volume>())
                    if (vol.sharedProfile != null && vol.sharedProfile.TryGet(out bloomAb)) { volAb = vol; break; }
                int iterations = bloomAb != null ? bloomAb.maxIterations.value : -1; bool hqAb = bloomAb != null && bloomAb.highQualityFiltering.value;
                if (bloomAb != null) { bloomAb.maxIterations.value = 6; bloomAb.highQualityFiltering.value = true; }
                UnityEngine.Rendering.Universal.ScriptableRendererFeature ssaoAb = null; UnityEditor.SerializedObject ssaoSo = null; int blurBefore = -1, sourceBefore = -1;
                if (rendererData != null)
                    foreach (UnityEngine.Rendering.Universal.ScriptableRendererFeature f in rendererData.rendererFeatures)
                        if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") { ssaoAb = f; ssaoSo = new UnityEditor.SerializedObject(f); UnityEditor.SerializedProperty bq = ssaoSo.FindProperty("m_Settings.BlurQuality"); UnityEditor.SerializedProperty src = ssaoSo.FindProperty("m_Settings.Source"); if (bq != null && src != null) { blurBefore = bq.enumValueIndex; sourceBefore = src.enumValueIndex; bq.enumValueIndex = 1; src.enumValueIndex = 1; ssaoSo.ApplyModifiedPropertiesWithoutUndo(); ssaoAb.Create(); } }
                yield return null;
                Texture2D b = Snap("ab-wp029.png");
                if (bloomAb != null) { bloomAb.maxIterations.value = iterations; bloomAb.highQualityFiltering.value = hqAb; }
                if (ssaoSo != null && blurBefore >= 0) { ssaoSo.FindProperty("m_Settings.BlurQuality").enumValueIndex = blurBefore; ssaoSo.FindProperty("m_Settings.Source").enumValueIndex = sourceBefore; ssaoSo.ApplyModifiedPropertiesWithoutUndo(); ssaoAb.Create(); }
                yield return null;
                Debug.Log("[DD-TEST] wp038j.ab shipped(iterations=" + iterations + ", hq=" + hqAb + ", ssaoBlur=" + blurBefore + ", ssaoSource=" + sourceBefore + ") vs wp029(6, true, blur 1, DepthNormals) -> " + dir);
                UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
            }

            // ---- material / shader-variant census of the enabled renderers (the SRP Batcher's batch keys)
            var variants = new Dictionary<string, int>(StringComparer.Ordinal);
            var materialsByVariant = new Dictionary<string, HashSet<Material>>(StringComparer.Ordinal);
            int rendererCount = 0;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) continue;
                rendererCount++;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    string[] kw = (string[])m.shaderKeywords.Clone(); Array.Sort(kw, StringComparer.Ordinal);
                    string key = m.shader.name + " | " + string.Join(" ", kw) + (r.HasPropertyBlock() ? " | MPB" : "") + (m.enableInstancing ? " | inst" : "");
                    variants.TryGetValue(key, out int c); variants[key] = c + 1;
                    if (!materialsByVariant.TryGetValue(key, out HashSet<Material> set)) materialsByVariant[key] = set = new HashSet<Material>();
                    set.Add(m);
                }
            }

            // per material: visible renderer count + an example object path
            var perMaterial = new Dictionary<string, KeyValuePair<int, string>>(StringComparer.Ordinal);
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    perMaterial.TryGetValue(m.name, out KeyValuePair<int, string> e);
                    perMaterial[m.name] = new KeyValuePair<int, string>(e.Key + 1, e.Value ?? PathOf(r.transform) + " <" + r.GetType().Name + ">");
                }
            }

            var census = new StringBuilder();
            census.Append("renderers(visible)=").Append(rendererCount).Append(" variants=").Append(variants.Count).Append('\n');
            var keys = new List<string>(variants.Keys); keys.Sort((a, b) => variants[b].CompareTo(variants[a]));
            foreach (string k in keys)
            {
                var names = new List<string>(); foreach (Material m in materialsByVariant[k]) names.Add(m.name); names.Sort(StringComparer.Ordinal);
                census.Append(variants[k]).Append('\t').Append(materialsByVariant[k].Count).Append(" mats\t").Append(k).Append("\t").Append(string.Join(",", names)).Append('\n');
            }

            // ---- cameras and UI: what the Player frame renders besides this camera (overlay canvases never go through Camera.Render)
            var ui = new StringBuilder();
            foreach (Camera c in Camera.allCameras)
            {
                UniversalAdditionalCameraData d = c.GetUniversalAdditionalCameraData();
                ui.Append("camera\t").Append(c.name).Append("\tdepth ").Append(c.depth).Append("\t").Append(d != null ? d.renderType.ToString() : "?")
                  .Append("\tpost ").Append(d != null && d.renderPostProcessing).Append("\taa ").Append(d != null ? d.antialiasing.ToString() : "?").Append("\tmask ").Append(c.cullingMask).Append('\n');
            }

            int canvasCount = 0, uiBatches = 0, uiRenderers = 0;
            foreach (Canvas cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (!cv.isActiveAndEnabled) continue;
                canvasCount++;
                var pairs = new HashSet<string>(StringComparer.Ordinal);
                int active = 0;
                foreach (CanvasRenderer cr in cv.GetComponentsInChildren<CanvasRenderer>(false))
                {
                    if (cr.materialCount == 0 || cr.cull) continue;
                    var g = cr.GetComponent<UnityEngine.UI.Graphic>();
                    if (g != null && (!g.isActiveAndEnabled || g.color.a <= 0.001f || g.canvasRenderer.GetAlpha() <= 0.001f)) continue;
                    active++;
                    Material m = cr.GetMaterial(0);
                    Texture t = g != null ? g.mainTexture : null;
                    pairs.Add((m != null ? m.name : "null") + "|" + (t != null ? t.name : "null") + "|" + (cr.hasPopInstruction ? "pop" : ""));
                }

                uiBatches += pairs.Count; uiRenderers += active;
                ui.Append("canvas\t").Append(cv.name).Append("\t").Append(cv.renderMode).Append("\tsort ").Append(cv.sortingOrder).Append("\tactiveRenderers ").Append(active).Append("\tdistinct(material|texture) ").Append(pairs.Count).Append('\n');
                foreach (string k in pairs) ui.Append("\t\t").Append(k).Append('\n');
            }

            ui.Insert(0, "canvases=" + canvasCount + " uiRenderers=" + uiRenderers + " distinctMaterialTexturePairs=" + uiBatches + "\n");
            File.WriteAllText(Path.Combine(dir, "ui-census.tsv"), ui.ToString());
            Debug.Log("[DD-TEST] wp038j.ui canvases=" + canvasCount + " renderers=" + uiRenderers + " distinctPairs=" + uiBatches);

            File.WriteAllText(Path.Combine(dir, "setpass-toggles.tsv"), sb.ToString());
            // block-body audit: enabled MeshRenderers under each live zombie's rig that are not part of the skinned prefab
            int zombiesChecked = 0, blockEnabled = 0, blockVisible = 0, silhouettePlaceholders = 0;
            string blockExample = "";
            foreach (ZombieEnemy z in ZombieEnemy.Live)
            {
                if (z == null) continue;
                zombiesChecked++;
                foreach (MeshRenderer mr in z.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!mr.enabled) continue;
                    if (z.Skinned != null && mr.transform.IsChildOf(z.Skinned.transform)) continue;
                    if (mr.GetComponentInParent<TierSilhouetteSlot>() != null) { silhouettePlaceholders++; continue; }
                    blockEnabled++;
                    if (mr.isVisible) blockVisible++;
                    if (blockExample.Length == 0) blockExample = PathOf(mr.transform) + " mat=" + (mr.sharedMaterial != null ? mr.sharedMaterial.name : "null");
                }
            }

            census.Append("blockAudit\tzombies=").Append(zombiesChecked).Append("\tblockRenderersEnabled=").Append(blockEnabled).Append("\tvisible=").Append(blockVisible)
                  .Append("\tsilhouettePlaceholdersEnabled=").Append(silhouettePlaceholders).Append("\texample=").Append(blockExample).Append('\n');   // expected 0 / 0 / 0
            Debug.Log("[DD-TEST] wp038j.blockAudit zombies=" + zombiesChecked + " blockRenderersEnabled=" + blockEnabled + " visible=" + blockVisible + " silhouettePlaceholders=" + silhouettePlaceholders + " example=" + blockExample);

            var pm = new List<string>(perMaterial.Keys); pm.Sort((a, b) => perMaterial[b].Key.CompareTo(perMaterial[a].Key));
            census.Append("\nper material (visible renderers, example)\n");
            foreach (string k in pm) census.Append(perMaterial[k].Key).Append('\t').Append(k).Append('\t').Append(perMaterial[k].Value).Append('\n');
            File.WriteAllText(Path.Combine(dir, "variant-census.tsv"), census.ToString());
            setPass.Dispose(); draws.Dispose(); batches.Dispose();
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            sync.Release(); UnityEngine.Object.DestroyImmediate(sync);
            Debug.Log("[DD-TEST] wp038j.census " + census.ToString().Split('\n')[0] + " armorFitted=" + fitted + " -> " + dir);
            Assert.Greater(rows.Count, 5);
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void SetRenderers(List<Renderer> list, bool on) { foreach (Renderer r in list) if (r != null) r.enabled = on; }

        private static IEnumerator ToggleByShader(string label, string shaderPrefix, Func<string, IEnumerator> measure)
        {
            var list = new List<Renderer>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled) continue;
                foreach (Material m in r.sharedMaterials) if (m != null && m.shader != null && m.shader.name.StartsWith(shaderPrefix, StringComparison.Ordinal)) { list.Add(r); break; }
            }

            SetRenderers(list, false);
            yield return measure(label + "(" + list.Count + ")");
            SetRenderers(list, true);
        }

    }
}
#endif
