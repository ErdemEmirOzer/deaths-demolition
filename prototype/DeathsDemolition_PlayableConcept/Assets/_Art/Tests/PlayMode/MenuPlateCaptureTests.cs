#if UNITY_EDITOR
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
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-068 slice 6 (§6.1, AD MUST 5) · The MainMenu / loading backdrop is captured in the engine under Look B from a fixed
    /// <c>MenuPlateCamera</c> pose in World_AP005 — no painted plate, no live 3D. Run over AP005 with the round stopped, no
    /// enemies, the player / particles hidden, the frame frozen; every pass is rendered by hand at 2× and box-filtered down.
    /// <c>DD_PLATE_MODE=candidates</c>: one Look B frame per pose + a guide copy (left-third SurfaceBase scrim α 0.55, the
    /// 2217×1080 art-safe centre) + the T10 text-safe contrast of TextSecondary over it, for the AD's choice.
    /// <c>DD_PLATE_MODE=layers</c>: three depth bands of the chosen pose — renderers are banded by view depth (ground,
    /// water and sky always far): far = the frame with the mid / near objects shadow-only (real content behind them, their
    /// shadows kept), opaque; mid / near = the full-context Look B colour with the alpha of a black / white matte pair (post off)
    /// of the band's own renderers. Poses from <c>DD_PLATE_POSES</c> (JSON), output to <c>DD_PLATE_OUT</c>. Run by name.
    /// </summary>
    [Explicit("WP-068 slice 6 capture: run by name (-testFilter DD.Art.Tests.PlayMode.MenuPlateCaptureTests)")]
    [Category("Capture")]
    [Category("Diagnostics")]   // Y6 rule 1: an [Explicit] capture is a diagnostic, not an unexpected skip in a full PlayMode run
    public sealed class MenuPlateCaptureTests
    {
        private const float SahneZamanAsimi = 120f, KurulumZamanAsimi = 60f;
        private const int Supersample = 2;
        public const float ScrimAlpha = MenuPlateMetrics.ScrimAlpha;

        [Serializable]
        public sealed class PoseFile
        {
            public int width = 2560, height = 1440;
            public float nearBand = 4f, farBand = 14f;
            /// <summary>Instance ids that carry the lantern-breath glow, with their amplitude (AD); empty = none.</summary>
            public List<string> lanterns = new List<string>();
            public List<float> lanternAmps = new List<float>();
            /// <summary>Glow outer radius per lantern in plate pixels (AD: 1.5× the lantern's visible height); missing = 1.5× its projected height.</summary>
            public List<float> lanternRadiusPx = new List<float>();
            /// <summary>Instance ids hidden for the capture (props the AD asked out of the frame).</summary>
            public List<string> hide = new List<string>();
            /// <summary>Decal projectors whose centre projects into one of these rects are switched off (pixels, top-left origin).</summary>
            public List<ScreenRect> hideDecals = new List<ScreenRect>();
            /// <summary>Decal projectors switched off by name (from a pick run).</summary>
            public List<string> hideDecalNames = new List<string>();
            /// <summary>Paint-over on the far plate (AD, §6.1 reserve, in-house): desaturatePink | towardFog over a rect.</summary>
            public List<PaintOp> paint = new List<PaintOp>();
            /// <summary>DD_PLATE_MODE=pick: what the AD's rects hit (instances, decals) and where the lanterns sit.</summary>
            public List<ScreenRect> picks = new List<ScreenRect>();
            public List<ArtVisualRegression.CameraPose> poses = new List<ArtVisualRegression.CameraPose>();
        }

        [Serializable]
        public sealed class ScreenRect { public string name; public float x0, y0, x1, y1; }

        [Serializable]
        public sealed class PaintOp { public string kind; public float x0, y0, x1, y1, amount = 1f; }

        private readonly List<Behaviour> _hiddenDecals = new List<Behaviour>();
        private readonly List<(Renderer r, bool enabled, ShadowCastingMode mode)> _hidden = new List<(Renderer, bool, ShadowCastingMode)>();

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; RunFlowClock.ClearHolds(); }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.timeScale = 1f;
            foreach ((Renderer r, bool enabled, ShadowCastingMode mode) h in _hidden) if (h.r != null) { h.r.enabled = h.enabled; h.r.shadowCastingMode = h.mode; }
            _hidden.Clear();
            foreach (Behaviour d in _hiddenDecals) if (d != null) d.enabled = true;
            _hiddenDecals.Clear();
            RunFlowClock.ClearHolds();
            Environment.SetEnvironmentVariable(WorldSourceSettings.EnvironmentVariable, null);
            CharacterAssetSet.ResetCache();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            }
        }

        [TearDown]
        public void TearDown() { LogAssert.ignoreFailingMessages = false; }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator MenuPlate_Yakala()
        {
            string posesPath = Environment.GetEnvironmentVariable("DD_PLATE_POSES");
            string outDir = Environment.GetEnvironmentVariable("DD_PLATE_OUT");
            string mode = Environment.GetEnvironmentVariable("DD_PLATE_MODE") ?? "candidates";
            Assert.IsTrue(!string.IsNullOrEmpty(posesPath) && File.Exists(posesPath), "DD_PLATE_POSES: " + posesPath);
            Assert.IsFalse(string.IsNullOrEmpty(outDir), "DD_PLATE_OUT");
            Directory.CreateDirectory(outDir);
            PoseFile file = JsonUtility.FromJson<PoseFile>(File.ReadAllText(posesPath));
            Assert.Greater(file.poses.Count, 0, "poz yok");

            yield return RunAp005();
            HideRunActors();
            Transform instancesRoot = WorldInstances();
            foreach (string id in file.hide)
            {
                Transform t = instancesRoot != null ? instancesRoot.Find(id) : null;
                Assert.IsNotNull(t, "gizlenecek örnek yok: " + id);
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true)) Hide(r);
            }

            foreach (DecalProjector d in UnityEngine.Object.FindObjectsOfType<DecalProjector>())
                if (d.enabled && file.hideDecalNames.Contains(d.name)) { d.enabled = false; _hiddenDecals.Add(d); }
            foreach (ScreenRect rect in file.hideDecals)
            foreach (DecalProjector d in UnityEngine.Object.FindObjectsOfType<DecalProjector>())
            {
                Vector2 px = ToPixels(file.poses[0], file, d.transform.position);
                if (d.enabled && px.x >= rect.x0 && px.x <= rect.x1 && px.y >= rect.y0 && px.y <= rect.y1) { d.enabled = false; _hiddenDecals.Add(d); }
            }

            // Warm every pose's mip streaming, then freeze: all passes of a pose render the same instant (wind, koi, fog noise).
            foreach (ArtVisualRegression.CameraPose pose in file.poses)
                yield return ArtVisualRegression.WarmStreaming(pose, file.width, file.height, 240, _ => { });
            for (int i = 0; i < 10; i++) yield return null;
            Time.timeScale = 0f;
            yield return null;

            var manifest = new StringBuilder();
            if (mode == "pick")
            {
                Pick(file.poses[0], file, manifest);
            }
            else if (mode == "layers")
            {
                ArtVisualRegression.CameraPose pose = file.poses[0];
                CaptureLayers(pose, file, outDir, manifest);
            }
            else
            {
                manifest.Append("name,position,lookAt,fov,textSafeContrastP1,textSafeContrastMin\n");
                foreach (ArtVisualRegression.CameraPose pose in file.poses)
                {
                    Texture2D frame = Render(pose, file.width, file.height, true, true, Color.black);
                    File.WriteAllBytes(Path.Combine(outDir, pose.name + ".png"), frame.EncodeToPNG());
                    (float p1, float min, _) = MenuPlateMetrics.TextSafe(frame);
                    Texture2D guide = MenuPlateMetrics.Guide(frame);
                    File.WriteAllBytes(Path.Combine(outDir, pose.name + "_guide.png"), guide.EncodeToPNG());
                    manifest.Append(pose.name + "," + V(pose.position) + "," + V(pose.lookAt) + "," + pose.verticalFov.ToString("F0", CultureInfo.InvariantCulture) + "," + p1.ToString("F2", CultureInfo.InvariantCulture) + "," + min.ToString("F2", CultureInfo.InvariantCulture) + "\n");
                    UnityEngine.Object.Destroy(frame);
                    UnityEngine.Object.Destroy(guide);
                }
            }

            File.WriteAllText(Path.Combine(outDir, mode + "-manifest.csv"), manifest.ToString());
            Debug.Log("[DD-TEST] wp068.plates mode=" + mode + " out=" + outDir + "\n" + manifest);
        }

        private IEnumerator RunAp005()
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
            yield return CharacterTests.Bekle(() => SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName).isLoaded, SahneZamanAsimi, "World_AP005");
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null, KurulumZamanAsimi, "oyuncu/kamera");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Fighting fazı");
            EnemySpawnDirector.Instance.StopRound();
            EnemySpawnDirector.Instance.ClearAllEnemies();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 30; i++)
            {
                if (EnemySpawnDirector.Instance.ActiveCount > 0) EnemySpawnDirector.Instance.ClearAllEnemies();
                yield return null;
            }
        }

        /// <summary>No player, no particles (the backdrop draws its own shader leaves / rain), no enemy leftovers.</summary>
        private void HideRunActors()
        {
            foreach (Renderer r in PlayerRegistry.Primary.GetComponentsInChildren<Renderer>(true)) Hide(r);
            foreach (Light l in PlayerRegistry.Primary.GetComponentsInChildren<Light>(true)) l.enabled = false;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (ParticleSystemRenderer p in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) Hide(p);
            }
        }

        private static Transform WorldInstances()
        {
            GameObject root = GameObject.Find(Ap005WorldBootstrap.RootName);
            return root != null ? root.transform.Find(Ap005WorldBootstrap.InstancesName) : null;
        }

        private void Hide(Renderer r)
        {
            _hidden.Add((r, r.enabled, r.shadowCastingMode));
            r.enabled = false;
        }

        // ------------------------------------------------------------------ layers

        private enum Band { Near, Mid, Far }

        private static bool AlwaysFar(Renderer r)
        {
            Bounds b = r.bounds;
            if (Mathf.Max(b.size.x, b.size.z) > 20f) return true;   // ground / surface plates, sky & horizon plates
            for (Transform t = r.transform; t != null; t = t.parent)
            {
                string n = t.name;
                if (n.IndexOf("Surfaces", StringComparison.Ordinal) >= 0 || n.IndexOf("Ground", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Sky", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("Horizon", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Grout", StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        /// <summary>
        /// Band per placed instance (AD: never split an object): its nearest point along the view — an instance that straddles a
        /// threshold goes whole into the nearer band; ground / water / sky and anything plate-sized are far by rule.
        /// </summary>
        private static List<(Renderer r, Band band)> BandRenderers(ArtVisualRegression.CameraPose pose, PoseFile file, StringBuilder log)
        {
            Scene world = SceneManager.GetSceneByName(ArtLibraryPaths.Ap005WorldSceneName);
            Transform instances = WorldInstances();
            Vector3 forward = (pose.lookAt - pose.position).normalized;
            var groups = new Dictionary<Transform, List<Renderer>>();
            var loose = new List<Renderer>();
            foreach (GameObject root in world.GetRootGameObjects())
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                Transform inst = InstanceOf(r.transform, instances);
                if (inst != null && !AlwaysFar(r))
                {
                    if (!groups.TryGetValue(inst, out List<Renderer> g)) groups[inst] = g = new List<Renderer>();
                    g.Add(r);
                }
                else loose.Add(r);
            }

            Band Of(float depth) => depth < file.nearBand ? Band.Near : depth < file.farBand ? Band.Mid : Band.Far;
            var list = new List<(Renderer, Band)>();
            int[] counts = new int[3];
            foreach (KeyValuePair<Transform, List<Renderer>> g in groups)
            {
                float min = float.MaxValue;
                foreach (Renderer r in g.Value) min = Mathf.Min(min, MinDepth(r.bounds, pose.position, forward));
                Band band = Of(min);
                foreach (Renderer r in g.Value) list.Add((r, band));
                counts[(int)band]++;
            }

            foreach (Renderer r in loose)
            {
                Band band = AlwaysFar(r) ? Band.Far : Of(MinDepth(r.bounds, pose.position, forward));
                list.Add((r, band));
            }

            log.Append("bands near<" + file.nearBand + " m: " + counts[0] + " instances, mid<" + file.farBand + " m: " + counts[1] + ", far: " + counts[2] + " (+" + loose.Count + " loose renderers)\n");
            return list;
        }

        private static Transform InstanceOf(Transform t, Transform instances)
        {
            if (instances == null) return null;
            for (; t != null; t = t.parent) if (t.parent == instances) return t;
            return null;
        }

        private static float MinDepth(Bounds b, Vector3 eye, Vector3 forward)
        {
            float min = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                min = Mathf.Min(min, Vector3.Dot(c - eye, forward));
            }

            return min;
        }

        private void CaptureLayers(ArtVisualRegression.CameraPose pose, PoseFile file, string outDir, StringBuilder log)
        {
            List<(Renderer r, Band band)> banded = BandRenderers(pose, file, log);
            var saved = new List<(Renderer r, bool enabled, ShadowCastingMode mode)>();
            foreach ((Renderer r, Band band) b in banded) saved.Add((b.r, b.r.enabled, b.r.shadowCastingMode));
            // Renderers outside the world scene (Run props) stay in the colour passes, never in a band's matte.
            var others = new List<Renderer>();
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                Scene scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded || scene.name == ArtLibraryPaths.Ap005WorldSceneName) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false)) if (r.enabled) { others.Add(r); saved.Add((r, true, r.shadowCastingMode)); }
            }
            void Restore() { foreach ((Renderer r, bool enabled, ShadowCastingMode mode) s in saved) { s.r.enabled = s.enabled; s.r.shadowCastingMode = s.mode; } }
            // Shadow-only keeps what the object casts on the layers behind it; the matte passes show one band's bodies only.
            void ShadowOnly(Func<Band, bool> which) { foreach ((Renderer r, Band band) b in banded) if (which(b.band) && b.r.shadowCastingMode != ShadowCastingMode.Off) b.r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; else if (which(b.band)) b.r.enabled = false; }
            void Only(Band keep) { foreach ((Renderer r, Band band) b in banded) if (b.band != keep) b.r.enabled = false; foreach (Renderer r in others) r.enabled = false; }
            int w = file.width, h = file.height;
            try
            {
                ShadowOnly(b => b != Band.Far);
                Texture2D far = Render(pose, w, h, true, true, Color.black);
                Restore();
                ShadowOnly(b => b == Band.Near);
                Texture2D midColour = Render(pose, w, h, true, true, Color.black);
                Restore();
                Only(Band.Mid);
                Texture2D midB = Render(pose, w, h, false, false, Color.black), midW = Render(pose, w, h, false, false, Color.white);
                Restore();
                Texture2D full = Render(pose, w, h, true, true, Color.black);
                Only(Band.Near);
                Texture2D nearB = Render(pose, w, h, false, false, Color.black), nearW = Render(pose, w, h, false, false, Color.white);
                Restore();

                Texture2D mid = Matte(midColour, midB, midW), near = Matte(full, nearB, nearW);
                Paint(far, file.paint, log);
                // WP-068 T10 after the linear audit: every layer's text-safe third is clamped to 4.6:1 (MenuPlateMetrics.ClampTextSafe).
                log.Append("text-safe clamp px: far " + MenuPlateMetrics.ClampTextSafe(far) + ", mid " + MenuPlateMetrics.ClampTextSafe(mid) + ", near " + MenuPlateMetrics.ClampTextSafe(near) + "\n");
                File.WriteAllBytes(Path.Combine(outDir, "plate_far.png"), far.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(outDir, "plate_mid.png"), mid.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(outDir, "plate_near.png"), near.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(outDir, "plate_full.png"), full.EncodeToPNG());
                // Parallax check: the composite at rest and at the extreme offsets (4 / 8 / 12 px).
                foreach ((string name, int k) c in new[] { ("rest", 0), ("left", -1), ("right", 1) })
                {
                    Texture2D comp = MenuPlateMetrics.Composite(far, mid, near, 4 * c.k, 8 * c.k, 12 * c.k);
                    File.WriteAllBytes(Path.Combine(outDir, "composite_" + c.name + ".png"), comp.EncodeToPNG());
                    if (c.k == 0)
                    {
                        (float p1, float min, _) = MenuPlateMetrics.TextSafe(comp);
                        log.Append("composite text-safe contrast p1 " + p1.ToString("F2", CultureInfo.InvariantCulture) + " min " + min.ToString("F2", CultureInfo.InvariantCulture) + "\n");
                        Texture2D guide = MenuPlateMetrics.Guide(comp);
                        File.WriteAllBytes(Path.Combine(outDir, "composite_rest_guide.png"), guide.EncodeToPNG());
                    }
                }

                var allWorld = new List<Renderer>();
                foreach ((Renderer r, Band band) b in banded) allWorld.Add(b.r);
                void OnlyThese(HashSet<Renderer> keep) { foreach (Renderer r in allWorld) if (!keep.Contains(r)) r.enabled = false; foreach (Renderer r in others) r.enabled = false; }
                string maskPath = Path.Combine(outDir, "plate_lantern_mask.png");
                (int lanternCount, string lanternIds) = LanternMask(pose, file, w / 2, h / 2, maskPath, InstanceDepths(pose), OnlyThese, Restore);
                var maskTex = new Texture2D(2, 2);
                maskTex.LoadImage(File.ReadAllBytes(maskPath));
                Texture2D rest = MenuPlateMetrics.Composite(far, mid, near, 0, 0, 0);
                Texture2D peak = MenuPlateMetrics.ApplyGlow(rest, maskTex, 1f);
                File.WriteAllBytes(Path.Combine(outDir, "composite_breath_peak.png"), peak.EncodeToPNG());
                (float t10p, float t10min, float failPct) = MenuPlateMetrics.TextSafe(peak);
                File.WriteAllText(Path.Combine(outDir, "menu_plates.json"), ManifestJson(pose, file, t10p, t10min, failPct, lanternIds));
                log.Append("lantern mask: " + lanternCount + " lanterns (" + lanternIds + ")\n");
                log.Append("pose " + pose.name + " " + V(pose.position) + " → " + V(pose.lookAt) + " fov " + pose.verticalFov + "; " + w + "×" + h + " (2× supersampled)\n");
            }
            finally
            {
                Restore();
            }
        }

        /// <summary>Straight-alpha layer: colour from the full-context Look B pass, alpha from the band's black / white matte.</summary>
        private static Texture2D Matte(Texture2D colour, Texture2D black, Texture2D white)
        {
            Color32[] c = colour.GetPixels32(), b = black.GetPixels32(), wh = white.GetPixels32();
            var o = new Color32[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                float diff = ((wh[i].r - b[i].r) + (wh[i].g - b[i].g) + (wh[i].b - b[i].b)) / (3f * 255f);
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - diff) * 255f);
                o[i] = new Color32(c[i].r, c[i].g, c[i].b, a);
            }

            var t = new Texture2D(colour.width, colour.height, TextureFormat.RGBA32, false);
            t.SetPixels32(o);
            t.Apply(false, false);
            return t;
        }

        private static Camera ProjectionCamera(ArtVisualRegression.CameraPose pose, PoseFile file, out GameObject go)
        {
            go = new GameObject("MenuPlate projection camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = pose.verticalFov;
            cam.aspect = (float)file.width / file.height;
            cam.nearClipPlane = 0.1f;
            cam.transform.position = pose.position;
            cam.transform.LookAt(pose.lookAt, Vector3.up);
            return cam;
        }

        /// <summary>World point → plate pixels (top-left origin, the AD's coordinates); behind the camera → far off-frame.</summary>
        private static Vector2 ToPixels(ArtVisualRegression.CameraPose pose, PoseFile file, Vector3 world)
        {
            Camera cam = ProjectionCamera(pose, file, out GameObject go);
            try
            {
                Vector3 vp = cam.WorldToViewportPoint(world);
                return vp.z <= 0f ? new Vector2(-1e6f, -1e6f) : new Vector2(vp.x * file.width, (1f - vp.y) * file.height);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>DD_PLATE_MODE=pick · For each AD rect: the instances a 5×5 ray grid hits (by count) and the decals inside; then every lantern in frame.</summary>
        private static void Pick(ArtVisualRegression.CameraPose pose, PoseFile file, StringBuilder log)
        {
            Camera cam = ProjectionCamera(pose, file, out GameObject go);
            Transform instances = WorldInstances();
            var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
            var family = new Dictionary<string, string>();
            foreach (WorldPlacementAsset.Instance inst in bootstrap.Placement.instances) family[inst.id] = inst.family;
            try
            {
                foreach (ScreenRect rect in file.picks)
                {
                    var hits = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 5; j++)
                    {
                        float x = Mathf.Lerp(rect.x0, rect.x1, (i + 0.5f) / 5f), y = Mathf.Lerp(rect.y0, rect.y1, (j + 0.5f) / 5f);
                        Ray ray = cam.ViewportPointToRay(new Vector3(x / file.width, 1f - y / file.height, 0f));
                        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        Transform inst = InstanceOf(hit.collider.transform, instances);
                        string key = inst != null ? inst.name + " (" + (family.TryGetValue(inst.name, out string f) ? f : "?") + ")" : "~" + hit.collider.name + " @" + hit.distance.ToString("F1");
                        hits[key] = hits.TryGetValue(key, out int n) ? n + 1 : 1;
                    }

                    log.Append("pick " + rect.name + " [" + rect.x0 + "," + rect.y0 + "," + rect.x1 + "," + rect.y1 + "]:");
                    foreach (KeyValuePair<string, int> h in hits) log.Append(" " + h.Key + "×" + h.Value);
                    foreach (DecalProjector d in UnityEngine.Object.FindObjectsOfType<DecalProjector>())
                    {
                        // The projector's box footprint on screen (a decal can cover the rect from a centre far away).
                        float dx0 = float.MaxValue, dy0 = float.MaxValue, dx1 = float.MinValue, dy1 = float.MinValue;
                        bool front = false;
                        for (int c = 0; c < 8; c++)
                        {
                            var local = d.pivot + Vector3.Scale(d.size, new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f));
                            Vector3 vp = cam.WorldToViewportPoint(d.transform.TransformPoint(local));
                            if (vp.z <= 0.1f) continue;
                            front = true;
                            dx0 = Mathf.Min(dx0, vp.x * file.width); dx1 = Mathf.Max(dx1, vp.x * file.width);
                            dy0 = Mathf.Min(dy0, (1f - vp.y) * file.height); dy1 = Mathf.Max(dy1, (1f - vp.y) * file.height);
                        }

                        if (front && d.enabled && dx1 >= rect.x0 && dx0 <= rect.x1 && dy1 >= rect.y0 && dy0 <= rect.y1)
                            log.Append(" decal:" + d.name + (d.material != null ? "[" + d.material.name + "]" : "") + " @" + Vector3.Distance(pose.position, d.transform.position).ToString("F1") + " box(" + dx0.ToString("F0") + "," + dy0.ToString("F0") + "-" + dx1.ToString("F0") + "," + dy1.ToString("F0") + ")");
                    }

                    // Renderers (collider or not: petal drifts, leaf piles) whose on-screen bounds overlap the rect, nearest first.
                    var overlap = new List<(float d, string key)>();
                    foreach (Transform inst in instances)
                    {
                        Renderer[] rs = inst.GetComponentsInChildren<Renderer>(false);
                        if (rs.Length == 0) continue;
                        Bounds b = rs[0].bounds;
                        foreach (Renderer r in rs) if (r.enabled) b.Encapsulate(r.bounds);
                        if (Mathf.Max(b.size.x, b.size.z) > 20f) continue;
                        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                        bool front = false;
                        for (int c = 0; c < 8; c++)
                        {
                            var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                            Vector3 vp = cam.WorldToViewportPoint(corner);
                            if (vp.z <= 0.1f) continue;
                            front = true;
                            x0 = Mathf.Min(x0, vp.x * file.width); x1 = Mathf.Max(x1, vp.x * file.width);
                            y0 = Mathf.Min(y0, (1f - vp.y) * file.height); y1 = Mathf.Max(y1, (1f - vp.y) * file.height);
                        }

                        if (!front || x1 < rect.x0 || x0 > rect.x1 || y1 < rect.y0 || y0 > rect.y1) continue;
                        overlap.Add((Vector3.Distance(pose.position, b.center), inst.name + "(" + (family.TryGetValue(inst.name, out string f2) ? f2 : "?") + ")"));
                    }

                    overlap.Sort((a, b) => a.d.CompareTo(b.d));
                    log.Append("\n   overlap:");
                    for (int i = 0; i < Mathf.Min(10, overlap.Count); i++) log.Append(" " + overlap[i].key + "@" + overlap[i].d.ToString("F1"));
                    log.Append("\n");
                }

                foreach (WorldPlacementAsset.Instance inst in bootstrap.Placement.instances)
                {
                    if (inst.family.IndexOf("lantern", StringComparison.Ordinal) < 0 && inst.family.IndexOf("ofuda", StringComparison.Ordinal) < 0) continue;
                    Transform t = instances.Find(inst.id);
                    if (t == null) continue;
                    (bool ok, Vector2 c, float heightPx) = FireBox(cam, file, t);
                    if (!ok || c.x < -50 || c.x > file.width + 50 || c.y < -50 || c.y > file.height + 50) continue;
                    Ray ray = cam.ViewportPointToRay(new Vector3(c.x / file.width, 1f - c.y / file.height, 0f));
                    string seen = Physics.Raycast(ray, out RaycastHit first, 200f, ~0, QueryTriggerInteraction.Ignore)
                        ? (first.collider.transform.IsChildOf(t) ? "VISIBLE" : "behind " + (InstanceOf(first.collider.transform, instances)?.name ?? first.collider.name)) : "no-hit";
                    log.Append("lantern " + inst.id + " " + inst.family + " firebox@(" + c.x.ToString("F0") + "," + c.y.ToString("F0") + ") h=" + heightPx.ToString("F0") + "px dist=" + Vector3.Distance(pose.position, t.position).ToString("F1") + " " + seen + "\n");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>A lantern's fire box (55–80 % of its height) in plate pixels, and its full on-screen height.</summary>
        private static (bool ok, Vector2 centre, float heightPx) FireBox(Camera cam, PoseFile file, Transform lantern) { (bool ok, Vector2 c, float hp, Vector3 _) = FireBoxWorld(cam, file, lantern); return (ok, c, hp); }

        private static (bool ok, Vector2 centre, float heightPx, Vector3 world) FireBoxWorld(Camera cam, PoseFile file, Transform lantern)
        {
            // Enabled renderers only: a breakable carries its broken / debris pieces switched off under the intact one.
            var rs = new List<Renderer>();
            foreach (Renderer r in lantern.GetComponentsInChildren<Renderer>(false)) if (r.enabled) rs.Add(r);
            if (rs.Count == 0) return (false, Vector2.zero, 0f, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            Vector3 c = new Vector3(b.center.x, b.min.y + b.size.y * 0.675f, b.center.z);
            Vector3 vc = cam.WorldToViewportPoint(c), vlo = cam.WorldToViewportPoint(new Vector3(b.center.x, b.min.y, b.center.z)), vhi = cam.WorldToViewportPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            if (vc.z <= 0.3f) return (false, Vector2.zero, 0f, c);
            return (true, new Vector2(vc.x * file.width, (1f - vc.y) * file.height), Mathf.Abs(vhi.y - vlo.y) * file.height, c);
        }

        /// <summary>
        /// The lantern-breath mask (AD post-capture round): per carrier a core + halo over its fire box — 1.0 inside 0.15 H,
        /// falling to 0.25 at 0.5 H and to 0 at 1.5 H (H = the lantern's visible height), times the AD amplitude; each disc is
        /// multiplied by (1 − the matte of everything standing in front of that lantern), so no glow lands on a post, the eave
        /// or a shrub; nothing left of x 853 (the text-safe third). Half resolution, linear single channel.
        /// </summary>
        private (int count, string ids) LanternMask(ArtVisualRegression.CameraPose pose, PoseFile file, int w, int h, string path, List<(Transform inst, float minDepth, List<Renderer> rs)> insts, Action<HashSet<Renderer>> onlyThese, Action restore)
        {
            Camera cam = ProjectionCamera(pose, file, out GameObject go);
            Transform instances = WorldInstances();
            Vector3 forward = (pose.lookAt - pose.position).normalized;
            var mask = new float[w * h];
            var used = new List<string>();
            float scale = (float)w / file.width;
            try
            {
                for (int k = 0; k < file.lanterns.Count; k++)
                {
                    Transform t = instances.Find(file.lanterns[k]);
                    Assert.IsNotNull(t, "fener örneği yok: " + file.lanterns[k]);
                    (bool ok, Vector2 c, float heightPx, Vector3 world) = FireBoxWorld(cam, file, t);
                    Assert.IsTrue(ok, "fener kameranın arkasında: " + file.lanterns[k]);
                    float amp = k < file.lanternAmps.Count ? file.lanternAmps[k] : 1f;
                    float hPx = (k < file.lanternRadiusPx.Count ? file.lanternRadiusPx[k] / 1.5f : heightPx) * scale;
                    float depth = Vector3.Dot(world - pose.position, forward);
                    var occluders = new HashSet<Renderer>();
                    foreach ((Transform inst, float minDepth, List<Renderer> rs) i in insts) if (i.inst != t && i.minDepth < depth - 0.5f) occluders.UnionWith(i.rs);
                    onlyThese(occluders);
                    Texture2D ob = Render(pose, w, h, false, false, Color.black), ow = Render(pose, w, h, false, false, Color.white);
                    restore();
                    Color32[] b = ob.GetPixels32(), wh = ow.GetPixels32();
                    float cx = c.x * scale, cy = (file.height - c.y) * scale, rc = 0.15f * hPx, r1 = 0.5f * hPx, r2 = 1.5f * hPx;
                    for (int y = Mathf.Max(0, (int)(cy - r2)); y < Mathf.Min(h, (int)(cy + r2) + 1); y++)
                    for (int x = Mathf.Max(0, (int)(cx - r2)); x < Mathf.Min(w, (int)(cx + r2) + 1); x++)
                    {
                        if (x < MenuPlateMetrics.GlowClipX * scale) continue;
                        float r = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        if (r >= r2) continue;
                        float v = r <= rc ? 1f : r <= r1 ? Mathf.Lerp(1f, 0.25f, (r - rc) / Mathf.Max(1e-3f, r1 - rc)) : 0.25f * (1f - Mathf.SmoothStep(0f, 1f, (r - r1) / Mathf.Max(1e-3f, r2 - r1)));
                        int i = y * w + x;
                        float front = 1f - ((wh[i].r - b[i].r) + (wh[i].g - b[i].g) + (wh[i].b - b[i].b)) / (3f * 255f);
                        mask[i] = Mathf.Max(mask[i], amp * v * (1f - Mathf.Clamp01(front)));
                    }

                    UnityEngine.Object.Destroy(ob);
                    UnityEngine.Object.Destroy(ow);
                    used.Add(file.lanterns[k] + ":" + amp.ToString("0.##", CultureInfo.InvariantCulture));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }

            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) { byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(mask[i]) * 255f); px[i] = new Color32(v, v, v, 255); }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            return (used.Count, string.Join(" ", used));
        }

        /// <summary>Paint-over on the far plate (static, opaque): the AD's in-house fixes, each confined to its rect.</summary>
        private static void Paint(Texture2D far, List<PaintOp> ops, StringBuilder log)
        {
            int w = far.width, h = far.height;
            Color32[] px = far.GetPixels32();
            float Lum(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            bool Pink(Color32 c) => c.r > c.g + 12 && c.r > c.b + 4;
            foreach (PaintOp op in ops)
            {
                // Rects in plate pixels (top-left origin); the texture's rows run bottom-up.
                int x0 = Mathf.Max(0, (int)op.x0), x1 = Mathf.Min(w - 1, (int)op.x1), yt0 = Mathf.Max(0, (int)op.y0), yt1 = Mathf.Min(h - 1, (int)op.y1);
                const int Ring = 30;
                double sr = 0, sg = 0, sb = 0; int n = 0;
                for (int yt = Mathf.Max(0, yt0 - Ring); yt <= Mathf.Min(h - 1, yt1 + Ring); yt++)
                for (int x = Mathf.Max(0, x0 - Ring); x <= Mathf.Min(w - 1, x1 + Ring); x++)
                {
                    bool inside = x >= x0 && x <= x1 && yt >= yt0 && yt <= yt1;
                    Color32 c = px[(h - 1 - yt) * w + x];
                    if (op.kind == "desaturatePink" ? Pink(c) : inside) continue;
                    sr += c.r; sg += c.g; sb += c.b; n++;
                }

                if (n == 0) continue;
                var mean = new Color32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n), 255);
                int changed = 0;
                for (int yt = yt0; yt <= yt1; yt++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = (h - 1 - yt) * w + x;
                    Color32 c = px[i];
                    float wgt;
                    Color target;
                    if (op.kind == "desaturatePink")
                    {
                        wgt = Mathf.Clamp01((c.r - c.g - 6) / 20f);
                        float k = Lum(c) / Mathf.Max(1f, Lum(mean));
                        target = new Color(mean.r * k / 255f, mean.g * k / 255f, mean.b * k / 255f);
                    }
                    else
                    {
                        float edge = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(yt - yt0, yt1 - yt));
                        wgt = op.amount * Mathf.Clamp01(edge / 8f);
                        target = (Color)mean;
                    }

                    if (wgt <= 0f) continue;
                    px[i] = Color.Lerp(c, target, wgt);
                    changed++;
                }

                log.Append("paint " + op.kind + " [" + op.x0 + "," + op.y0 + "," + op.x1 + "," + op.y1 + "] " + changed + " px toward (" + mean.r + "," + mean.g + "," + mean.b + ")\n");
            }

            far.SetPixels32(px);
            far.Apply(false, false);
        }

        /// <summary>World instances with their enabled renderers and nearest view depth (ground / plates excluded).</summary>
        private static List<(Transform inst, float minDepth, List<Renderer> rs)> InstanceDepths(ArtVisualRegression.CameraPose pose)
        {
            Transform instances = WorldInstances();
            Vector3 forward = (pose.lookAt - pose.position).normalized;
            var list = new List<(Transform, float, List<Renderer>)>();
            foreach (Transform inst in instances)
            {
                var rs = new List<Renderer>();
                float min = float.MaxValue;
                foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || r is ParticleSystemRenderer || AlwaysFar(r)) continue;
                    rs.Add(r);
                    min = Mathf.Min(min, MinDepth(r.bounds, pose.position, forward));
                }

                if (rs.Count > 0) list.Add((inst, min, rs));
            }

            return list;
        }

        private static string ManifestJson(ArtVisualRegression.CameraPose pose, PoseFile file, float t10p1, float t10min, float failPct, string lanternIds)
        {
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema\": \"dd.menu-plates/1\",\n  \"package\": \"WP-068 slice 6 (S-05)\",\n");
            sb.Append("  \"source\": \"World_AP005, Look B (in-game Global Volume), engine capture, 2x supersampled\",\n");
            sb.Append("  \"size\": [" + file.width + ", " + file.height + "],\n");
            sb.Append("  \"pose\": {\"name\": \"" + pose.name + "\", \"position\": [" + F(pose.position.x) + ", " + F(pose.position.y) + ", " + F(pose.position.z) + "], \"lookAt\": [" + F(pose.lookAt.x) + ", " + F(pose.lookAt.y) + ", " + F(pose.lookAt.z) + "], \"verticalFov\": " + F(pose.verticalFov) + "},\n");
            sb.Append("  \"bands_m\": {\"near\": " + F(file.nearBand) + ", \"far\": " + F(file.farBand) + "},\n");
            sb.Append("  \"layers\": [\n    {\"resource\": \"MenuPlates/plate_far\", \"band\": \"far\", \"alpha\": false, \"parallax_px\": 4},\n");
            sb.Append("    {\"resource\": \"MenuPlates/plate_mid\", \"band\": \"mid\", \"alpha\": true, \"parallax_px\": 8},\n");
            sb.Append("    {\"resource\": \"MenuPlates/plate_near\", \"band\": \"near\", \"alpha\": true, \"parallax_px\": 12}\n  ],\n");
            sb.Append("  \"follow_s\": 0.2,\n  \"lantern_mask\": {\"resource\": \"MenuPlates/plate_lantern_mask\", \"scale\": 0.5, \"blend\": \"over\", \"tint\": \"Amber300 #F7C766\", \"peak_alpha\": 0.35, \"period_s\": 2.4, \"breath_min\": 0.55, \"clip_x_px\": 853, \"shape\": \"core 1.0 to 0.15H, 0.25 at 0.5H, 0 at 1.5H; occluded by everything in front\", \"instances\": \"" + lanternIds + "\"},\n");
            sb.Append("  \"scrim\": {\"token\": \"SurfaceBase\", \"alpha\": " + F(ScrimAlpha) + "},\n");
            sb.Append("  \"text_safe\": {\"x0\": 0, \"x1\": 0.3333, \"y0\": 0, \"y1\": 1},\n  \"art_safe_px\": [2217, 1080],\n");
            sb.Append("  \"t10\": {\"token\": \"TextSecondary\", \"threshold\": 4.5, \"percentile\": 1, \"measured\": \"composite at breath peak\", \"p1\": " + F(t10p1) + ", \"min\": " + F(t10min) + ", \"below_pct\": " + F(failPct) + "}\n}\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ rendering

        private static Texture2D Render(ArtVisualRegression.CameraPose pose, int w, int h, bool post, bool skybox, Color background)
        {
            var go = new GameObject("MenuPlateCamera (" + pose.name + ")") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            RenderTexture big = null, small = null;
            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                camera.fieldOfView = pose.verticalFov;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 300f;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.transform.position = pose.position;
                camera.transform.LookAt(pose.lookAt, Vector3.up);
                UniversalAdditionalCameraData urp = camera.GetUniversalAdditionalCameraData();
                urp.renderType = CameraRenderType.Base;
                urp.renderPostProcessing = post;
                urp.antialiasing = AntialiasingMode.None;
                urp.renderShadows = true;
                go.SetActive(true);

                big = new RenderTexture(w * Supersample, h * Supersample, 24) { name = "MenuPlate 2x", filterMode = FilterMode.Bilinear };
                big.Create();
                camera.targetTexture = big;
                camera.Render();
                camera.targetTexture = null;
                // 2:1 bilinear blit samples each destination texel at the corner of four sources: an exact 2×2 box filter.
                small = new RenderTexture(w, h, 0) { name = "MenuPlate 1x" };
                small.Create();
                Graphics.Blit(big, small);

                var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = small;
                result.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                result.Apply(false, false);
                RenderTexture.active = previous;
                return result;
            }
            finally
            {
                if (big != null) { big.Release(); UnityEngine.Object.DestroyImmediate(big); }
                if (small != null) { small.Release(); UnityEngine.Object.DestroyImmediate(small); }
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static string V(Vector3 v) => v.x.ToString("F2", CultureInfo.InvariantCulture) + " " + v.y.ToString("F2", CultureInfo.InvariantCulture) + " " + v.z.ToString("F2", CultureInfo.InvariantCulture);
    }
}
#endif
