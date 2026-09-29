using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DD.Art;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-020 · Fidelity captures of world v3 (Higgsfield §5: Unity ↔ AP-005C render side by side): the four
    /// AP-005C fidelity cameras of <c>AP005-render-log-v006.json</c> (Blender pos/target/lens → Unity, 36 mm
    /// horizontal fit) plus the west-corridor gameplay framing and the WP-038 water close-up, 1920×1080 with the gameplay grade, written to
    /// <c>DD_ART_CAPTURE_DIR/world-v3/</c>. Standalone world (own bootstrap), no crowd.
    /// </summary>
    [TestFixture]
    [Category("WP020")]
    public sealed class WorldV3CaptureTests
    {
        private const float SahneZamanAsimi = 60f, KurulumZamanAsimi = 60f, OyuncuZamanAsimi = 30f, YatismaZamanAsimi = 30f;
        private const int W = 1920, H = 1080;

        /// <summary>Vertical FOV of a Blender camera with a 36 mm horizontal sensor fit at 16:9.</summary>
        private static float VerticalFov(float lensMm)
        {
            float halfH = Mathf.Atan(18f / lensMm);
            return 2f * Mathf.Atan(Mathf.Tan(halfH) * 9f / 16f) * Mathf.Rad2Deg;
        }

        private static ArtVisualRegression.CameraPose Blender(string name, Vector3 pos, Vector3 target, float lensMm)
        {
            return new ArtVisualRegression.CameraPose(name, WorldPlacementAsset.ToUnity(pos.x, pos.y, pos.z), WorldPlacementAsset.ToUnity(target.x, target.y, target.z), VerticalFov(lensMm));
        }

        public static readonly ArtVisualRegression.CameraPose[] Poses =
        {
            Blender("fid-palace", new Vector3(-20f, 4f, 30f), new Vector3(3f, 37f, 4f), 40f),
            Blender("fid-gate-north", new Vector3(0f, -4f, 20f), new Vector3(0f, 20f, 1f), 35f),
            Blender("fid-palace-gable-west", new Vector3(-38f, 24f, 16f), new Vector3(-14f, 37f, 6.5f), 45f),
            Blender("fid-pavilion-north", new Vector3(40.5f, 40f, 6f), new Vector3(27.7f, 36.6f, 2f), 40f),
            new ArtVisualRegression.CameraPose("gameplay-west", new Vector3(-36.95f, 15f, -13.05f), new Vector3(-36.95f, 0f, 0f), 42f),
            new ArtVisualRegression.CameraPose("gameplay-north", new Vector3(0f, 15f, 11.95f), new Vector3(0f, 0f, 25f), 42f),
            // WP-038: south koi ponds 02/03 (AP005_i0094 / i0093 basins) at a low angle for the DD/Water v2 sheen, foam band and caustics.
            new ArtVisualRegression.CameraPose("water-closeup", new Vector3(-3f, 4.5f, -19.5f), new Vector3(-3f, -0.15f, -29.2f), 42f)
        };

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            RunFlowClock.ClearHolds();
            if (EnemySpawnDirector.Instance != null) EnemySpawnDirector.Instance.ForceActiveTarget(0, false);
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < YatismaZamanAsimi) yield return null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// WP-074 · <c>DD_ART_CAPTURE_POSES</c> (optional): a CSV "name,px,py,pz,tx,ty,tz,vfov" (Unity space) replaces the fixed poses —
        /// the gap-analysis frames (target-style overview, gameplay framing, eye-level third / first person) with the same tool.
        /// </summary>
        public static ArtVisualRegression.CameraPose[] PosesFor()
        {
            string csv = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_POSES");
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return Poses;
            var list = new List<ArtVisualRegression.CameraPose>();
            foreach (string line in File.ReadAllLines(csv))
            {
                string[] f = line.Split(',');
                if (f.Length < 8 || f[0].StartsWith("#") || f[0] == "name") continue;
                float P(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);
                list.Add(new ArtVisualRegression.CameraPose(f[0].Trim(), new Vector3(P(1), P(2), P(3)), new Vector3(P(4), P(5), P(6)), P(7)));
            }

            return list.ToArray();
        }

        /// <summary>WP-075 C7 · an optional 9th CSV column: the camera mode (td / tp / fp) the post follows for that pose.</summary>
        public static bool ModeFor(string poseName, out CameraMode mode)
        {
            mode = CameraMode.TopDown;
            string csv = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_POSES");
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return false;
            foreach (string line in File.ReadAllLines(csv))
            {
                string[] f = line.Split(',');
                if (f.Length >= 9 && f[0].Trim() == poseName) return CameraModeService.TryParseMode(f[8], out mode);
            }

            return false;
        }

        /// <summary>WP-075 AD S2 · an optional 10th CSV column: the cloud phase for that pose — lit / shadow at its look-at point.</summary>
        public static string PhaseFor(string poseName)
        {
            string csv = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_POSES");
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return null;
            foreach (string line in File.ReadAllLines(csv))
            {
                string[] f = line.Split(',');
                if (f.Length >= 10 && f[0].Trim() == poseName) return f[9].Trim().ToLowerInvariant();
            }

            return null;
        }

        public static string OutputDir()
        {
            string dir = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Application.temporaryCachePath, "wp020-captures");
            dir = Path.Combine(dir, "world-v3");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SadakatKareleri_AP005C_4Kamera_2Oynanis_1080p()
        {
            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, "MainMenu");
            yield return null;
            Assert.AreEqual(ArtTestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            // WP-074 B1: DD_ART_CAPTURE_SCENE (asset path) captures another world scene, e.g. the dev-only World v5 greybox.
            string scenePath = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_SCENE");
            string sceneName = string.IsNullOrEmpty(scenePath) ? ArtLibraryPaths.Ap005WorldSceneName : Path.GetFileNameWithoutExtension(scenePath);
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(scenePath)) UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            else
#endif
                SceneManager.LoadScene(ArtLibraryPaths.Ap005WorldSceneName, LoadSceneMode.Single);
            yield return null;
            yield return CharacterTests.Bekle(() => SceneManager.GetActiveScene().name == sceneName && SceneManager.GetActiveScene().isLoaded, SahneZamanAsimi, sceneName);
            yield return CharacterTests.Bekle(() => !string.IsNullOrEmpty(Ap005WorldBootstrap.Last.Report), KurulumZamanAsimi, "bootstrap");   // WP-074 B1: the greybox has 0 instances
            yield return CharacterTests.Bekle(() => PlayerRegistry.Primary != null, OyuncuZamanAsimi, "oyuncu");
            yield return CharacterTests.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null, KurulumZamanAsimi, "run sistemleri");
            EnemySpawnDirector.Instance.ForceActiveTarget(0, false);
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            for (int i = 0; i < 5; i++) yield return null;

            string dir = OutputDir();
            string bindingOutput = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_BINDING_ONLY");
            bool bindingOnly = !string.IsNullOrEmpty(bindingOutput);
            if (bindingOnly) Assert.AreEqual(1, PosesFor().Length, "Binding observer requires exactly one pose.");
            var manifest = new List<string> { "name,position,lookAt,vfov,file,warmFrames" };
            bool recordSugi = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_SUGI_LOD") == "1";
            if (recordSugi) SugiCaptureLodRecorder.Install();
            var lodRows = new List<string> { "pose,variant,groupId,lod,rootDistance,referenceDistance,renderer,mesh,lodBias,vfov,rootX,rootY,rootZ,screenMinX,screenMinY,screenMaxX,screenMaxY,triangles,localAzimuthDeg,elevationDeg,groupYawDeg" };
            Time.captureDeltaTime = 1f / 60f;
            float? phaseBefore = CloudShadowDriver.ForcedPhase;
            foreach (ArtVisualRegression.CameraPose pose in PosesFor())
            {
                int warm = 0;
                bool look = ModeFor(pose.name, out CameraMode mode) && mode != CameraMode.TopDown;
                CameraModeService.Request(look ? mode : CameraMode.TopDown, true);
                // WP-075 C4 B: the screen shafts follow the cloud cookie at the camera — TP / FP frames are taken in a sun patch.
                string phase = PhaseFor(pose.name);
                if ((phase == "lit" || phase == "shadow") && CloudShadowDriver.Active != null)
                {
                    LookReferenceTests.CookiePhases(CloudShadowDriver.Active, pose.lookAt, out float litPhase, out float shadowPhase, out float _, out float _);
                    CloudShadowDriver.ForcedPhase = phase == "lit" ? litPhase : shadowPhase;
                }
                else if (look && CloudShadowDriver.Active != null)
                {
                    LookReferenceTests.CookiePhases(CloudShadowDriver.Active, pose.position, out float litPhase, out float _, out float _, out float _);
                    CloudShadowDriver.ForcedPhase = litPhase;
                }
                else CloudShadowDriver.ForcedPhase = phaseBefore;

                if (bindingOnly)
                {
                    SkyBindingMetadata.Write(bindingOutput, pose, W, H);
                    continue; // No warm-up RT, Camera.Render, ReadPixels or image output in this opt-in branch.
                }
                yield return ArtVisualRegression.WarmStreaming(pose, W, H, 90, f => warm = f);
                Time.timeScale = 0f;
                try
                {
                    string file = Path.Combine(dir, pose.name + ".png");
                    if (recordSugi) SugiCaptureLodRecorder.Begin(pose.name);
                    string renderedBinding = Environment.GetEnvironmentVariable("DD_ART_CAPTURE_BINDING_RENDERED");
                    int bindingEvents = 0, srpCallbacks = 0, rendererCallbacks = 0;
                    Action<UnityEngine.Rendering.ScriptableRenderContext, Camera> onRender = null;
                    if (!string.IsNullOrEmpty(renderedBinding))
                    {
                        Assert.AreEqual(1, PosesFor().Length, "Rendered binding observer requires one pose.");
                        void Observe(Camera camera, string hook)
                        {
                            if (camera == null || camera.name != "ArtVisualRegression Camera (" + pose.name + ")" || camera.targetTexture == null) return;
                            if (bindingEvents != 0) return;
                            SkyBindingMetadata.Write(renderedBinding, pose, W, H, camera, hook);
                            bindingEvents++;
                        }
                        onRender = (context, camera) =>
                        {
                            if (camera != null && camera.name == "ArtVisualRegression Camera (" + pose.name + ")") srpCallbacks++;
                            Observe(camera, "RenderPipelineManager.beginCameraRendering");
                        };
                        SkyBindingMetadata.CaptureObserver = camera => { rendererCallbacks++; Observe(camera, "SugiCaptureLodRecorder.OnWillRenderObject"); };
                        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += onRender;
                    }
                    Texture2D t;
                    try { t = ArtVisualRegression.Capture(pose, W, H, true); }
                    finally
                    {
                        if (onRender != null) UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= onRender;
                        SkyBindingMetadata.CaptureObserver = null;
                        if (recordSugi) SugiCaptureLodRecorder.Stop();
                    }
                    try { File.WriteAllBytes(file, t.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(t); }
                    if (!string.IsNullOrEmpty(renderedBinding))
                    {
                        Assert.AreEqual(1, bindingEvents, "No actual capture-camera binding event was observed.");
                        SkyBindingMetadata.AttachImage(renderedBinding, file, srpCallbacks, rendererCallbacks);
                    }
                    if (recordSugi) lodRows.AddRange(SugiCaptureLodRecorder.Rows);
                    if (look && ScreenAtmosphereFeature.WorldActive)
                    {
                        // WP-075 C4 B / C5: the same frame without the screen atmosphere (K9: shafts ≤ +0.08 V frame mean).
                        ScreenAtmosphereFeature.WorldActive = false;
                        try
                        {
                            Texture2D off = ArtVisualRegression.Capture(pose, W, H, true);
                            try { File.WriteAllBytes(Path.Combine(dir, pose.name + "-noatmos.png"), off.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(off); }
                        }
                        finally { ScreenAtmosphereFeature.WorldActive = true; }
                    }
                    manifest.Add(pose.name + "," + V(pose.position) + "," + V(pose.lookAt) + "," + pose.verticalFov.ToString("F1", CultureInfo.InvariantCulture) + "," + Path.GetFileName(file) + "," + warm);
                }
                finally
                {
                    Time.timeScale = 1f;
                }
            }

            Time.captureDeltaTime = 0f;
            CameraModeService.Request(CameraMode.TopDown, true);
            CloudShadowDriver.ForcedPhase = phaseBefore;
            if (bindingOnly)
            {
                Assert.IsTrue(File.Exists(bindingOutput), "Binding metadata was not written.");
                yield break;
            }
            File.WriteAllLines(Path.Combine(dir, "world-v3-manifest.csv"), manifest);
            if (recordSugi)
            {
                File.WriteAllLines(Path.Combine(dir, "sugi-rendered-lods.csv"), lodRows);
                Assert.Greater(lodRows.Count, 1, "No sugi render callbacks from capture camera; no actual LOD evidence");
            }
            Debug.Log("[DD-TEST] wp020.captures frames=" + PosesFor().Length + " instances=" + Ap005WorldBootstrap.Last.Instances + " -> " + dir);
            // The AP005 placement count; another scene (DD_ART_CAPTURE_SCENE: the v6 dev scene, dressed in-scene) has no placement instances.
            if (string.IsNullOrEmpty(scenePath)) Assert.AreEqual(3168, Ap005WorldBootstrap.Last.Instances);   // WP-038I: AP-017 Faz 2 delta v005 (+23); WP-038L: Faz 2b delta v006 (+25)
            foreach (ArtVisualRegression.CameraPose pose in PosesFor()) Assert.IsTrue(File.Exists(Path.Combine(dir, pose.name + ".png")), pose.name);
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("F2", CultureInfo.InvariantCulture) + " " + v.y.ToString("F2", CultureInfo.InvariantCulture) + " " + v.z.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>Read-only Editor PlayMode binding observation; its new event is not the old PNG's render event.</summary>
    internal static class SkyBindingMetadata
    {
        [Serializable] internal sealed class Payload { public string assetPath, guid, sha256; public long bytes; }
        [Serializable] internal sealed class Preflight { public string head, branch, utc; public Payload[] payloads; }
        [Serializable] internal sealed class Identity
        {
            public string name, type, assetPath, guid, payloadSha256, provenance;
            public int instanceId; public long localFileId;
        }
        [Serializable] internal sealed class Property
        {
            public string name, type, effectiveSource; public bool rendererBlockHas, indexBlockHas;
            public float[] value; public Identity texture;
        }
        [Serializable] internal sealed class Slot
        {
            public int index, queue; public Identity material, shader; public bool indexBlockEmpty;
            public Property[] properties;
        }
        [Serializable] internal sealed class Near
        {
            public string hierarchy; public int rendererInstanceId; public bool enabled, activeInHierarchy, rendererBlockEmpty;
            public Identity mesh; public Matrix4x4 localToWorld; public Slot[] slots;
        }
        [Serializable] internal sealed class ResourceIdentity { public string resource; public bool equalsActualSkybox; public Identity material; }
        [Serializable] internal sealed class Observation
        {
            public string schema = "S05.sky-binding-metadata.v1", utc, sourceHead, sourceBranch, preflightSha256, layout, sceneName, scenePath;
            public string eventKind = "NEW Editor PlayMode metadata event; configured disabled camera; no render/draw or old-PNG binding claim";
            public string milestone = "Boot/menu/world/bootstrap/player/spawn initialized; target0; five setup frames; pose mode/cloud setup applied; no streaming warm-up";
            public int frame, width, height, cameraInstanceId; public float time, realtime, timeScale, verticalFov, aspect, nearClip, farClip;
            public bool cameraActive, cameraEnabled, hdr, msaa, postProcessing; public string urpRenderType;
            public string observationHook, unityVersion, graphicsBackend, imagePath, imageSha256;
            public int targetTextureInstanceId, targetWidth, targetHeight, srpCallbacks, rendererCallbacks; public bool captureCompleted;
            public Vector3 position, forward; public Matrix4x4 projection; public Identity scene, actualSkybox, actualSkyShader;
            public Property[] skyProperties; public ResourceIdentity[] resourceCandidates; public Near[] near;
            public string unresolved = "Historical fallback branch; old PNG same-event binding; actual draw order/pixel contribution; GPU texture/mip contents; other backends remain UNKNOWN";
        }

        private static Preflight _preflight;
        public static Action<Camera> CaptureObserver;
        private static Identity Id(UnityEngine.Object obj)
        {
            if (obj == null) return new Identity { provenance = "UNKNOWN: null object" };
            var id = new Identity { name = obj.name, type = obj.GetType().FullName, instanceId = obj.GetInstanceID(), provenance = "UNKNOWN: no persistent asset/preflight match" };
#if UNITY_EDITOR
            id.assetPath = UnityEditor.AssetDatabase.GetAssetPath(obj);
            UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out id.guid, out id.localFileId);
            foreach (Payload p in _preflight.payloads)
                if (!string.IsNullOrEmpty(id.guid) && id.guid == p.guid && id.assetPath == p.assetPath)
                { id.payloadSha256 = p.sha256; id.provenance = "actual Editor GUID/path/localID joined to preflight file-byte SHA256; not GPU content"; break; }
#endif
            return id;
        }
        private static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;
        private static Property Read(Material m, MaterialPropertyBlock renderer, MaterialPropertyBlock indexed, string key, string type)
        {
            int id = Shader.PropertyToID(key);
            bool Has(MaterialPropertyBlock b) => b != null && (type == "texture" ? b.HasTexture(id) : type == "vector" ? b.HasVector(id) : type == "color" ? b.HasColor(id) : b.HasFloat(id));
            bool rh = Has(renderer), ih = Has(indexed);
            // A nonempty per-slot block replaces the entire renderer block, including properties absent from that slot block.
            MaterialPropertyBlock effective = indexed != null && !indexed.isEmpty ? indexed : renderer;
            bool overridden = Has(effective);
            var p = new Property { name = key, type = type, rendererBlockHas = rh, indexBlockHas = ih, effectiveSource = overridden ? (effective == indexed ? "indexPropertyBlock" : "rendererPropertyBlock") : "sharedMaterial" };
            if (!overridden && !m.HasProperty(key == "_BaseMap_ST" ? "_BaseMap" : key))
            { p.effectiveSource = "UNKNOWN: material property missing"; return p; }
            if (type == "texture") p.texture = Id(overridden ? effective.GetTexture(id) : m.GetTexture(key));
            else if (type == "color")
            { Color c = overridden ? effective.GetColor(id) : m.GetColor(key); p.value = new[] { c.r, c.g, c.b, c.a }; }
            else if (type == "vector")
            {
                Vector4 v;
                if (overridden) v = effective.GetVector(id);
                else if (key == "_BaseMap_ST")
                { Vector2 s = m.GetTextureScale("_BaseMap"), o = m.GetTextureOffset("_BaseMap"); v = new Vector4(s.x, s.y, o.x, o.y); }
                else v = m.GetVector(key);
                p.value = new[] { v.x, v.y, v.z, v.w };
            }
            else p.value = new[] { overridden ? effective.GetFloat(id) : m.GetFloat(key) };
            return p;
        }
        public static void Write(string output, ArtVisualRegression.CameraPose pose, int width, int height, Camera renderedCamera = null, string hook = null)
        {
            string sidecar = Environment.GetEnvironmentVariable("DD_ART_BINDING_PREFLIGHT");
            Assert.IsTrue(!string.IsNullOrEmpty(sidecar) && File.Exists(sidecar), "Binding preflight sidecar required.");
            byte[] bytes = File.ReadAllBytes(sidecar);
            _preflight = JsonUtility.FromJson<Preflight>(System.Text.Encoding.UTF8.GetString(bytes));
            Assert.IsNotNull(_preflight.payloads);
            string digest;
            using (var sha = System.Security.Cryptography.SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            GameObject go = null;
            if (renderedCamera == null)
            {
                go = new GameObject("S05 W60 metadata camera") { hideFlags = HideFlags.HideAndDontSave };
                go.SetActive(false);
            }
            try
            {
                Camera camera = renderedCamera;
                if (camera == null)
                {
                    camera = go.AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.Skybox;
                    camera.fieldOfView = pose.verticalFov; camera.aspect = (float)width / height; camera.nearClipPlane = 0.3f; camera.farClipPlane = 300f;
                    camera.allowHDR = true; camera.allowMSAA = false; camera.transform.position = pose.position; camera.transform.LookAt(pose.lookAt, Vector3.up);
                    var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base; data.renderPostProcessing = true;
                    data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None; data.renderShadows = true;
                }
                var urp = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                Material actual = RenderSettings.skybox; // Observe before loading comparison resources; never replace it.
                var scene = SceneManager.GetActiveScene();
                var ob = new Observation { utc = DateTime.UtcNow.ToString("o"), sourceHead = _preflight.head, sourceBranch = _preflight.branch, preflightSha256 = digest,
                    layout = WorldLayout.Current.ToString(), sceneName = scene.name, scenePath = scene.path, frame = Time.frameCount, time = Time.time, realtime = Time.realtimeSinceStartup,
                    timeScale = Time.timeScale, width = width, height = height, cameraInstanceId = camera.GetInstanceID(), verticalFov = camera.fieldOfView, aspect = camera.aspect,
                    cameraActive = camera.gameObject.activeInHierarchy, cameraEnabled = camera.enabled, hdr = camera.allowHDR, msaa = camera.allowMSAA, postProcessing = urp != null && urp.renderPostProcessing, urpRenderType = urp == null ? "UNKNOWN" : urp.renderType.ToString(),
                    nearClip = camera.nearClipPlane, farClip = camera.farClipPlane, position = camera.transform.position, forward = camera.transform.forward, projection = camera.projectionMatrix,
                    actualSkybox = Id(actual), actualSkyShader = Id(actual == null ? null : actual.shader) };
                ob.unityVersion = Application.unityVersion; ob.graphicsBackend = SystemInfo.graphicsDeviceType.ToString(); ob.observationHook = hook ?? "nonrendered metadata camera";
                if (renderedCamera != null)
                {
                    ob.eventKind = "NEW actual capture-camera render binding event; pixel/draw contribution remains UNKNOWN";
                    ob.milestone = "Existing capture setup and streaming warm-up complete; Camera.Render executing at frozen time";
                    ob.targetTextureInstanceId = camera.targetTexture == null ? 0 : camera.targetTexture.GetInstanceID();
                    ob.targetWidth = camera.targetTexture == null ? 0 : camera.targetTexture.width; ob.targetHeight = camera.targetTexture == null ? 0 : camera.targetTexture.height;
                }
#if UNITY_EDITOR
                ob.scene = Id(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scene.path));
#endif
                if (actual != null)
                {
                    var sky = new List<Property>();
                    if (actual.HasProperty("_MainTex")) sky.Add(Read(actual, null, null, "_MainTex", "texture"));
                    if (actual.HasProperty("_Tint")) sky.Add(Read(actual, null, null, "_Tint", "color"));
                    foreach (string key in new[] { "_Exposure", "_Rotation", "_Mapping", "_ImageType", "_Layout" })
                        if (actual.HasProperty(key)) sky.Add(Read(actual, null, null, key, "float"));
                    ob.skyProperties = sky.ToArray();
                }
                var resources = new List<ResourceIdentity>();
                foreach (string name in new[] { WorldAtmosphere.SkyV3ResourceName, WorldAtmosphere.SharedSkyResourceName, WorldAtmosphere.SkyV2ResourceName })
                { Material candidate = Resources.Load<Material>(name); resources.Add(new ResourceIdentity { resource = name, equalsActualSkybox = candidate != null && candidate == actual, material = Id(candidate) }); }
                ob.resourceCandidates = resources.ToArray();
                var near = new List<Near>();
                foreach (MeshRenderer mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
                {
                    if (mr.name != "Horizon_near") continue;
                    MeshFilter filter = mr.GetComponent<MeshFilter>();
                    var block = new MaterialPropertyBlock(); mr.GetPropertyBlock(block);
                    var item = new Near { hierarchy = Hierarchy(mr.transform), rendererInstanceId = mr.GetInstanceID(), enabled = mr.enabled, activeInHierarchy = mr.gameObject.activeInHierarchy,
                        rendererBlockEmpty = block.isEmpty, mesh = Id(filter == null ? null : filter.sharedMesh), localToWorld = mr.localToWorldMatrix };
                    var slots = new List<Slot>(); Material[] materials = mr.sharedMaterials; // Shared getters never clone a material.
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material m = materials[i]; var indexBlock = new MaterialPropertyBlock(); mr.GetPropertyBlock(indexBlock, i);
                        var slot = new Slot { index = i, material = Id(m), shader = Id(m == null ? null : m.shader), queue = m == null ? -1 : m.renderQueue, indexBlockEmpty = indexBlock.isEmpty };
                        var props = new List<Property>();
                        if (m != null)
                        {
                            props.Add(Read(m, block, indexBlock, "_BaseMap", "texture")); props.Add(Read(m, block, indexBlock, "_BaseMap_ST", "vector"));
                            props.Add(Read(m, block, indexBlock, "_ScrollUV", "vector"));
                            foreach (string key in new[] { "_BaseColor", "_HazeColor" }) if (m.HasProperty(key)) props.Add(Read(m, block, indexBlock, key, "color"));
                            foreach (string key in new[] { "_BottomEdgeAA", "_BottomPanoramaReveal", "_AlphaPower", "_Haze" }) if (m.HasProperty(key)) props.Add(Read(m, block, indexBlock, key, "float"));
                        }
                        slot.properties = props.ToArray(); slots.Add(slot);
                    }
                    item.slots = slots.ToArray(); near.Add(item);
                }
                ob.near = near.ToArray();
                File.WriteAllText(output, JsonUtility.ToJson(ob, true));
                Debug.Log("[S05-BINDING] events=1 renderedCamera=" + (renderedCamera != null) + " frame=" + ob.frame + " nearCount=" + ob.near.Length + " -> " + output);
            }
            finally { if (go != null) UnityEngine.Object.DestroyImmediate(go); _preflight = null; }
        }
        public static void AttachImage(string output, string image, int srpCallbacks, int rendererCallbacks)
        {
            Observation ob = JsonUtility.FromJson<Observation>(File.ReadAllText(output));
            using (var sha = System.Security.Cryptography.SHA256.Create())
                ob.imageSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(image))).Replace("-", "").ToLowerInvariant();
            ob.imagePath = image; ob.captureCompleted = true; ob.srpCallbacks = srpCallbacks; ob.rendererCallbacks = rendererCallbacks;
            File.WriteAllText(output, JsonUtility.ToJson(ob, true));
        }
    }

    /// <summary>Opt-in capture evidence: records meshes that receive OnWillRenderObject for the exact capture camera.
    /// This never forces a LOD or changes a production prefab. Other game/shadow/reflection cameras cannot enter the receipt.</summary>
    public sealed class SugiCaptureLodRecorder : MonoBehaviour
    {
        private static string _pose;
        private static readonly HashSet<int> Seen = new HashSet<int>();
        public static readonly List<string> Rows = new List<string>();
        private LODGroup _group;
        private int _lod;
        private string _variant, _mesh;

        public static void Install()
        {
            foreach (LODGroup group in UnityEngine.Object.FindObjectsOfType<LODGroup>())
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length < 3 || lods[0].renderers.Length == 0) continue;
                MeshFilter first = lods[0].renderers[0].GetComponent<MeshFilter>();
                string mesh = first != null && first.sharedMesh != null ? first.sharedMesh.name : "";
                string variant = null;
                foreach (string v in new[] { "V01", "V02", "V03" }) if (mesh.Contains("sugi_" + v)) variant = v;
                if (variant == null) continue;
                for (int i = 0; i < lods.Length; i++)
                foreach (Renderer renderer in lods[i].renderers)
                {
                    var recorder = renderer.gameObject.AddComponent<SugiCaptureLodRecorder>();
                    recorder._group = group; recorder._lod = i; recorder._variant = variant;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    recorder._mesh = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "";
                }
            }
        }

        public static void Begin(string pose) { _pose = pose; Seen.Clear(); Rows.Clear(); }
        public static void Stop() { _pose = null; }

        private void OnWillRenderObject()
        {
            Camera camera = Camera.current;
            if (_pose == null || camera == null || camera.name != "ArtVisualRegression Camera (" + _pose + ")" || !Seen.Add(GetInstanceID())) return;
            SkyBindingMetadata.CaptureObserver?.Invoke(camera);
            float rootDistance = Vector3.Distance(camera.transform.position, _group.transform.position);
            float referenceDistance = Vector3.Distance(camera.transform.position, _group.transform.TransformPoint(_group.localReferencePoint));
            MeshFilter filter = GetComponent<MeshFilter>();
            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 min = bounds.min, max = bounds.max;
            Vector2 screenMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 screenMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
                Vector3 screen = camera.WorldToViewportPoint(transform.TransformPoint(local));
                screenMin = Vector2.Min(screenMin, new Vector2(screen.x, screen.y));
                screenMax = Vector2.Max(screenMax, new Vector2(screen.x, screen.y));
            }
            uint triangleCount = 0;
            for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                if (filter.sharedMesh.GetTopology(submesh) == MeshTopology.Triangles) triangleCount += filter.sharedMesh.GetIndexCount(submesh) / 3;
            Vector3 root = _group.transform.position;
            Vector3 localCamera = _group.transform.InverseTransformPoint(camera.transform.position);
            float azimuth = Mathf.Repeat(Mathf.Atan2(localCamera.z, localCamera.x) * Mathf.Rad2Deg, 360f);
            Vector3 toCamera = camera.transform.position - root;
            float elevation = Mathf.Atan2(toCamera.y, new Vector2(toCamera.x, toCamera.z).magnitude) * Mathf.Rad2Deg;
            Rows.Add(string.Join(",", _pose, _variant, _group.GetInstanceID().ToString(CultureInfo.InvariantCulture), _lod.ToString(CultureInfo.InvariantCulture),
                rootDistance.ToString("F4", CultureInfo.InvariantCulture), referenceDistance.ToString("F4", CultureInfo.InvariantCulture), gameObject.name, _mesh,
                QualitySettings.lodBias.ToString("F4", CultureInfo.InvariantCulture), camera.fieldOfView.ToString("F4", CultureInfo.InvariantCulture),
                root.x.ToString("F4", CultureInfo.InvariantCulture), root.y.ToString("F4", CultureInfo.InvariantCulture), root.z.ToString("F4", CultureInfo.InvariantCulture),
                screenMin.x.ToString("F5", CultureInfo.InvariantCulture), screenMin.y.ToString("F5", CultureInfo.InvariantCulture),
                screenMax.x.ToString("F5", CultureInfo.InvariantCulture), screenMax.y.ToString("F5", CultureInfo.InvariantCulture),
                triangleCount.ToString(CultureInfo.InvariantCulture),
                azimuth.ToString("F3", CultureInfo.InvariantCulture), elevation.ToString("F3", CultureInfo.InvariantCulture),
                _group.transform.eulerAngles.y.ToString("F3", CultureInfo.InvariantCulture)));
        }
    }
}
