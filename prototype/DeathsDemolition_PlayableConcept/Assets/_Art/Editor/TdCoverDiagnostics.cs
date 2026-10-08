using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using Object = UnityEngine.Object;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>Opt-in evidence only. Copies the actual forward depth attachment; never replaces a scene material.
    /// Category IDs are provisional depth matches, not renderer IDs or a replacement for the RGB gate.</summary>
    internal sealed class TdCoverDiagnostics : IDisposable
    {
        private const int W = 480, H = 270;
        private readonly Type _preview;
        private readonly IDisposable _previewScope;
        private readonly PropertyInfo _draw, _snapshot;
        private readonly string _dir;
        private bool _dumped;
        private Color32[] _lastRgb;

        public TdCoverDiagnostics(string dir)
        {
            _dir = dir;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                _preview = a.GetType(WorldTdCapture.VegetationPreviewType, false);
                if (_preview != null) break;
            }
            if (_preview == null) throw new InvalidOperationException("Diagnostic vegetation preview missing");
            _draw = _preview.GetProperty("DiagnosticDrawEnabled");
            _snapshot = _preview.GetProperty("LastDiagnostics");
            _previewScope = _preview.GetMethod("BeginDiagnostics")?.Invoke(null, null) as IDisposable;
            if (_draw == null || _snapshot == null || _previewScope == null)
                throw new InvalidOperationException("Diagnostic preview contract missing");
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            try { WriteJson("diagnostic-frames.json", new FrameRecords { frames = _frames.ToArray(), verifiedReplayRestores = _replayRestores, verifiedPoseRestores = _poseRestores }); }
            finally { _previewScope.Dispose(); }
        }

        private Camera _observedCamera;
        private SsaoEntropyBinding _entropyBinding;
        private SsaoEntropy _bareEntropy;
        private string _renderLabel;
        private readonly List<FrameRecord> _frames = new List<FrameRecord>();
        private FrameRecord _pendingFrame;
        private int _replayRestores, _poseRestores;

        // Called only by the opt-in entry point; baseline capture methods have no entropy hook.
        public void Observe(Camera camera)
        {
            if (_observedCamera != null) throw new InvalidOperationException("UNKNOWN: diagnostic observer already bound");
            // Camera.Render may create the pipeline and dispose pre-existing renderers.
            // Bind after the first existing beauty render, never in Observe or before bootstrap.
            _observedCamera = camera;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        public void Label(string label) => _renderLabel = label;
        public void MarkBareEntropy()
        {
            if (_entropyBinding == null) throw new InvalidOperationException("UNKNOWN: camera bootstrap did not bind SSAO");
            _entropyBinding.AssertCurrent(_observedCamera.GetUniversalAdditionalCameraData().scriptableRenderer);
            _bareEntropy = _entropyBinding.Snapshot();
        }

        [Serializable] private sealed class FrameState
        {
            public int frame, randomStateHash, noiseIndex;
            public float realtime, noiseOffsetX, noiseOffsetY;
            public Vector4 shaderTime, shaderTimeParameters, wind, vegWave;
            public bool materialInitialized, materialPersistent, bindingInitialized;
            public int materialInstanceId;
        }
        [Serializable] private sealed class FrameRecord
        {
            public string label;
            public FrameState before, after;
        }
        [Serializable] private sealed class FrameRecords
        {
            public string randomHashLimit = "Opaque Random.State GetHashCode is a diagnostic fingerprint; restoration uses full State.Equals.";
            public FrameRecord[] frames;
            public int verifiedReplayRestores, verifiedPoseRestores;
        }

        private FrameState FrameStateNow() => new FrameState
        {
            frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
            randomStateHash = UnityEngine.Random.state.GetHashCode(),
            noiseIndex = _entropyBinding?.Index ?? -1, noiseOffsetX = _entropyBinding?.OffsetX ?? 0f,
            noiseOffsetY = _entropyBinding?.OffsetY ?? 0f, materialInitialized = _entropyBinding?.MaterialReady ?? false,
            materialPersistent = _entropyBinding?.MaterialPersistent ?? false, materialInstanceId = _entropyBinding?.MaterialInstanceId ?? 0,
            bindingInitialized = _entropyBinding != null,
            shaderTime = Shader.GetGlobalVector("_Time"), shaderTimeParameters = Shader.GetGlobalVector("_TimeParameters"),
            wind = Shader.GetGlobalVector("_DD_WindVector"), vegWave = Shader.GetGlobalVector("_DD_VegWaveW"),
        };
        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _observedCamera) return;
            if (_entropyBinding != null)
                _entropyBinding.AssertCurrent(camera.GetUniversalAdditionalCameraData().scriptableRenderer);
            _pendingFrame = new FrameRecord { label = _renderLabel, before = FrameStateNow() };
        }
        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _observedCamera || _pendingFrame == null) return;
            ScriptableRenderer renderer = camera.GetUniversalAdditionalCameraData().scriptableRenderer;
            if (_entropyBinding == null) _entropyBinding = new SsaoEntropyBinding(renderer);
            _entropyBinding.AssertCurrent(renderer);
            _pendingFrame.after = FrameStateNow();
            _frames.Add(_pendingFrame);
            _pendingFrame = null;
        }

        // Pinned URP14.0.10 exact types. No Create/Dispose, settings, asset, cache or feature-list mutation here.
        private sealed class SsaoEntropyBinding
        {
            private readonly ScriptableRendererFeature _feature;
            private readonly ScriptableRenderer _renderer;
            private readonly FieldInfo _activePass;
            private readonly object _pass;
            private readonly FieldInfo _index, _x, _y, _passMaterial, _featureMaterial;
            public int Index => (int)_index.GetValue(_pass);
            public float OffsetX => (float)_x.GetValue(_pass);
            public float OffsetY => (float)_y.GetValue(_pass);
            public bool MaterialReady => _passMaterial.GetValue(_pass) is Material;
            public bool MaterialPersistent => _passMaterial.GetValue(_pass) is Material material && UnityEditor.AssetDatabase.Contains(material);
            public int MaterialInstanceId => (_passMaterial.GetValue(_pass) as Material)?.GetInstanceID() ?? 0;

            public SsaoEntropyBinding(ScriptableRenderer renderer)
            {
                _renderer = renderer;
                Assembly assembly = typeof(UniversalRenderPipelineAsset).Assembly;
                Type featureType = assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion", true);
                Type passType = assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion+ScreenSpaceAmbientOcclusionPass", true);
                Type settingsType = assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusionSettings", true);
                Type methodType = assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusionSettings+AOMethodOptions", true);
                PropertyInfo list = typeof(ScriptableRenderer).GetProperty("rendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
                if (list == null || list.PropertyType != typeof(List<ScriptableRendererFeature>))
                    throw new InvalidOperationException("UNKNOWN: URP14 live rendererFeatures API differs");
                foreach (ScriptableRendererFeature feature in (List<ScriptableRendererFeature>)list.GetValue(renderer))
                {
                    if (feature == null || !feature.isActive || feature.GetType() != featureType) continue;
                    if (_feature != null) throw new InvalidOperationException("UNKNOWN: multiple active SSAO features");
                    _feature = feature;
                }
                if (_feature == null) throw new InvalidOperationException("UNKNOWN: exact active URP14 SSAO feature missing");
                object settings = ExactField(featureType, "m_Settings", settingsType).GetValue(_feature);
                object method = ExactField(settingsType, "AOMethod", methodType).GetValue(settings);
                if (!methodType.IsEnum || Convert.ToInt32(method) != 0)
                    throw new InvalidOperationException("UNKNOWN: runtime SSAO is not pinned BlueNoise method");
                _activePass = ExactField(featureType, "m_SSAOPass", passType);
                _pass = _activePass.GetValue(_feature);
                if (_pass == null || _pass.GetType() != passType) throw new InvalidOperationException("UNKNOWN: SSAO runtime pass missing");
                _index = ExactField(passType, "m_BlueNoiseTextureIndex", typeof(int));
                _x = ExactField(passType, "m_BlurRandomOffsetX", typeof(float));
                _y = ExactField(passType, "m_BlurRandomOffsetY", typeof(float));
                _passMaterial = ExactField(passType, "m_Material", typeof(Material));
                _featureMaterial = ExactField(featureType, "m_Material", typeof(Material));
            }

            private static FieldInfo ExactField(Type type, string name, Type expected)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != expected)
                    throw new InvalidOperationException("UNKNOWN: pinned SSAO field differs: " + type.FullName + "." + name);
                return field;
            }

            public void AssertCurrent(ScriptableRenderer renderer)
            {
                if (!ReferenceEquals(renderer, _renderer))
                    throw new InvalidOperationException("UNKNOWN: actual renderer identity changed since SSAO binding");
                if (!_feature.isActive || !ReferenceEquals(_activePass.GetValue(_feature), _pass))
                    throw new InvalidOperationException("UNKNOWN: actual SSAO pass identity changed since binding");
            }

            private Material RuntimeMaterial()
            {
                AssertCurrent(_renderer);
                Material material = _passMaterial.GetValue(_pass) as Material;
                Material featureMaterial = _featureMaterial.GetValue(_feature) as Material;
                if (material == null)
                    throw new InvalidOperationException("UNKNOWN: active SSAO pass material=null; featureMaterialId=" +
                        (featureMaterial != null ? featureMaterial.GetInstanceID() : 0));
                if (material != featureMaterial)
                    throw new InvalidOperationException("UNKNOWN: SSAO pass/feature material identity differs; passId=" +
                        material.GetInstanceID() + "; featureId=" + (featureMaterial != null ? featureMaterial.GetInstanceID() : 0));
                if (UnityEditor.AssetDatabase.Contains(material))
                    throw new InvalidOperationException("UNKNOWN: actual SSAO material is persistent; id=" + material.GetInstanceID());
                if (material.shader == null || material.shader.name != "Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion")
                    throw new InvalidOperationException("UNKNOWN: actual SSAO shader unsupported: " +
                        (material.shader != null ? material.shader.name : "<null>"));
                return material;
            }

            public SsaoEntropy Snapshot()
            {
                Material material = RuntimeMaterial();
                Texture noise = material.GetTexture("_BlueNoiseTexture");
                if (!(noise is Texture2D))
                    throw new InvalidOperationException("UNKNOWN: actual SSAO BlueNoise property is not Texture2D");
                return new SsaoEntropy(this, UnityEngine.Random.state, Index, OffsetX, OffsetY, material,
                    noise, material.GetVector("_SSAOBlueNoiseParams"));
            }

            public void Restore(SsaoEntropy state)
            {
                Material material = RuntimeMaterial();
                if (material != state.Material) throw new InvalidOperationException("UNKNOWN: SSAO runtime material identity changed");
                _index.SetValue(_pass, state.Index);
                _x.SetValue(_pass, state.X);
                _y.SetValue(_pass, state.Y);
                material.SetTexture("_BlueNoiseTexture", state.Noise);
                material.SetVector("_SSAOBlueNoiseParams", state.Parameters);
                UnityEngine.Random.state = state.Random;
                if (Index != state.Index || OffsetX != state.X || OffsetY != state.Y ||
                    material.GetTexture("_BlueNoiseTexture") != state.Noise ||
                    !material.GetVector("_SSAOBlueNoiseParams").Equals(state.Parameters) ||
                    !UnityEngine.Random.state.Equals(state.Random) || UnityEditor.AssetDatabase.Contains(material))
                    throw new InvalidOperationException("UNKNOWN: SSAO/RNG entropy restoration failed");
            }
        }

        private sealed class SsaoEntropy
        {
            private readonly SsaoEntropyBinding _binding;
            public readonly UnityEngine.Random.State Random;
            public readonly int Index;
            public readonly float X, Y;
            public readonly Material Material;
            public readonly Texture Noise;
            public readonly Vector4 Parameters;
            public SsaoEntropy(SsaoEntropyBinding binding, UnityEngine.Random.State random, int index,
                float x, float y, Material material, Texture noise, Vector4 parameters)
            { _binding = binding; Random = random; Index = index; X = x; Y = y; Material = material; Noise = noise; Parameters = parameters; }
            public void Restore() => _binding.Restore(this);
        }

        private float[] ReplayDepth(SsaoEntropy replay, Camera camera, TdCoverDepthFeature feature, string label, bool rgb = false)
        {
            SsaoEntropy before = _entropyBinding.Snapshot();
            try { replay.Restore(); Label(label); return ReadDepth(camera, feature, label, rgb); }
            finally { before.Restore(); _replayRestores++; }
        }


        [Serializable] private sealed class ArrayRecord
        {
            public string file, owner, field, elementType, sha256;
            public int length;
        }
        [Serializable] private sealed class GridRecord
        {
            public string status = "SOURCE_ONLY; registration and depth equivalence require engine validation";
            public int width, height;
            public float cell, gridDefaultGroundY, fieldDefaultGroundY;
            public Vector2 origin;
            public string order = "index=z*width+x; little-endian; NaN preserved; density=byte*LayerScale/255";
            public string flags = "Ground=1,Path=2,Plaza=4,Water=8,Excluded=16,Roofed=32,Bamboo=64,MossCourt=128,Shrub=256";
            public string species = "SS,GM,GT,FE,LC,MO,HY,SL,KZ,ML; heightScale=byte/128; cap=byte*.005m";
            public ArrayRecord[] arrays;
        }

        // Dump the actual final zoner arrays, not a second evaluation or a reconstructed mask.
        private void DumpGrid()
        {
            if (_dumped) return;
            object snapshot = _snapshot.GetValue(null);
            if (snapshot == null) throw new InvalidOperationException("Zoner diagnostic snapshot missing");
            object grid = Field(snapshot, "Grid"), field = Field(snapshot, "Field");
            var rows = new List<ArrayRecord>();
            DumpArrays(snapshot, "snapshot", rows);
            DumpArrays(grid, "grid", rows);
            DumpArrays(field, "field", rows);
            WriteJson("vegetation-grid.json", new GridRecord { width = (int)Field(grid, "Width"),
                height = (int)Field(grid, "Height"), cell = (float)Field(grid, "Cell"),
                origin = (Vector2)Field(grid, "Origin"), gridDefaultGroundY = (float)Field(grid, "DefaultGroundY"),
                fieldDefaultGroundY = (float)Field(field, "DefaultGroundY"), arrays = rows.ToArray() });
            DumpProvenance(snapshot, rows);
            _dumped = true;
        }

        private void DumpArrays(object owner, string label, List<ArrayRecord> rows)
        {
            foreach (FieldInfo f in owner.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!(f.GetValue(owner) is Array values)) continue;
                if (values is byte[][] layers)
                {
                    for (int i = 0; i < layers.Length; i++) DumpArray(layers[i], label, f.Name + "-" + i, rows);
                }
                else DumpArray(values, label, f.Name, rows);
            }
        }

        private void DumpArray(Array values, string owner, string field, List<ArrayRecord> rows)
        {
            Type type = values.GetType().GetElementType();
            if (type != typeof(byte) && type != typeof(ushort) && type != typeof(float) && type != typeof(bool))
                throw new InvalidOperationException("Unsupported diagnostic array: " + type);
            string file = "vegetation-" + owner + "-" + field + ".bin";
            using (var writer = new BinaryWriter(File.Create(Path.Combine(_dir, file))))
                foreach (object value in values)
                {
                    if (value is byte b) writer.Write(b);
                    else if (value is ushort u) writer.Write(u);
                    else if (value is float v) writer.Write(v);
                    else writer.Write((bool)value);
                }
            rows.Add(new ArrayRecord { file = file, owner = owner, field = field, elementType = type.Name, length = values.Length, sha256 = FileHash(Path.Combine(_dir, file)) });
        }


        [Serializable] private sealed class ProvenanceRecord
        {
            public string status = "ACTUAL_PREVIEW_METADATA; visible/submitted counts and coverage NOTRECORDED";
            public string recipeName, recipeAssetPath, recipeGuid, recipeDependencyHash, recipeJsonFile;
            public string recipeJsonSha256, fieldGridIdentitySha256, layoutIdentitySha256;
            public bool recipePersistent, speciesCountMatchesTotal;
            public int recipeInstanceId, appliedTier, appliedSeed, actualTotalPlaced;
            public int[] actualPlacedPerSpecies;
            public float actualTierDensity;
            public string recipeIdentity = "Empty asset path/GUID means non-asset runtime recipe; recipeName and actual JSON identify it";
            public string hashContract = "SHA256 field: vegetation-grid.json UTF8 plus ordered array filename+contentSHA; layout: LE chunks/origin/chunksize,total,group type/variant/count then row-major matrix floats; recipe: actual JsonUtility.ToJson";
        }

        private void DumpProvenance(object snapshot, List<ArrayRecord> arrays)
        {
            var recipe = Field(snapshot, "AppliedRecipe") as Object;
            object layout = Field(snapshot, "AppliedLayout");
            if (recipe == null || layout == null)
            {
                WriteJson("vegetation-provenance.json", new ProvenanceRecord { status = "NOTRECORDED: applied preview recipe/layout unavailable" });
                return;
            }
            string recipeFile = "vegetation-applied-recipe.json";
            File.WriteAllText(Path.Combine(_dir, recipeFile), JsonUtility.ToJson(recipe, true));
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(recipe);
            int[] counts = (int[])((int[])Field(layout, "InstancesPerType")).Clone();
            int total = (int)Field(layout, "TotalInstances"), sum = 0;
            foreach (int count in counts) sum += count;
            string layoutHash;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((int)Field(layout, "ChunksX")); writer.Write((int)Field(layout, "ChunksZ"));
                    Vector2 origin = (Vector2)Field(layout, "Origin"); writer.Write(origin.x); writer.Write(origin.y);
                    writer.Write((float)Field(layout, "ChunkSize")); writer.Write(total);
                    foreach (object group in (Array)Field(layout, "Groups"))
                    {
                        writer.Write(Convert.ToInt32(Field(group, "Type"))); writer.Write((int)Field(group, "Variant"));
                        Matrix4x4[] matrices = (Matrix4x4[])Field(group, "Matrices"); writer.Write(matrices.Length);
                        foreach (Matrix4x4 matrix in matrices)
                            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) writer.Write(matrix[row, col]);
                    }
                }
                stream.Position = 0;
                using (var sha = System.Security.Cryptography.SHA256.Create()) layoutHash = HexHash(sha.ComputeHash(stream));
            }
            string fieldIdentity = File.ReadAllText(Path.Combine(_dir, "vegetation-grid.json"));
            foreach (ArrayRecord array in arrays) fieldIdentity += "\n" + array.file + ":" + array.sha256;
            string fieldHash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                fieldHash = HexHash(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fieldIdentity)));
            int tier = (int)Field(snapshot, "AppliedTier");
            WriteJson("vegetation-provenance.json", new ProvenanceRecord {
                recipeName = recipe.name, recipeInstanceId = recipe.GetInstanceID(), recipePersistent = UnityEditor.AssetDatabase.Contains(recipe),
                recipeAssetPath = assetPath, recipeGuid = string.IsNullOrEmpty(assetPath) ? "" : UnityEditor.AssetDatabase.AssetPathToGUID(assetPath),
                recipeDependencyHash = string.IsNullOrEmpty(assetPath) ? "" : UnityEditor.AssetDatabase.GetAssetDependencyHash(assetPath).ToString(),
                recipeJsonFile = recipeFile, recipeJsonSha256 = FileHash(Path.Combine(_dir, recipeFile)), fieldGridIdentitySha256 = fieldHash,
                layoutIdentitySha256 = layoutHash, appliedTier = tier, appliedSeed = (int)Field(snapshot, "AppliedSeed"),
                actualTierDensity = (float)recipe.GetType().GetMethod("TierDensity").Invoke(recipe, new object[] { tier }),
                actualTotalPlaced = total, actualPlacedPerSpecies = counts, speciesCountMatchesTotal = sum == total });
        }

        private static string HexHash(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static string FileHash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path)) return HexHash(sha.ComputeHash(stream));
        }

        private static object Field(object value, string name) => value.GetType().GetField(name).GetValue(value);

        [Serializable] private sealed class RendererRecord
        {
            public int instanceId, category;
            public string path;
            public string[] materials, shaders;
            public bool enabled, forceRenderingOff;
        }
        [Serializable] private sealed class HitRecord
        {
            public string collider;
            public Vector3 point;
            public float distance;
        }
        [Serializable] private sealed class ProbeRecord
        {
            public int index = -1, flags, rawZone, finalZone, clear, obstacle;
            public float gridGroundY, fieldGroundY;
            public bool gridGroundValid, fieldGroundValid;
            public float[] densityPerSquareMetre;
        }
        private ProbeRecord Probe(Vector2 feet)
        {
            object snapshot = _snapshot.GetValue(null), grid = Field(snapshot, "Grid"), field = Field(snapshot, "Field");
            Vector2 origin = (Vector2)Field(grid, "Origin");
            float cell = (float)Field(grid, "Cell");
            int width = (int)Field(grid, "Width"), height = (int)Field(grid, "Height");
            int x = Mathf.FloorToInt((feet.x - origin.x) / cell), z = Mathf.FloorToInt((feet.y - origin.y) / cell);
            var result = new ProbeRecord();
            if (x < 0 || z < 0 || x >= width || z >= height) return result;
            int i = z * width + x;
            byte[][] layers = (byte[][])Field(field, "Layers");
            float[] scales = (float[])Field(field, "LayerScale");
            result.index = i; result.flags = ((ushort[])Field(grid, "Flags"))[i];
            result.rawZone = ((byte[])Field(grid, "Zone"))[i];
            result.finalZone = ((byte[])Field(snapshot, "FinalZone"))[i];
            result.clear = ((byte[])Field(snapshot, "Clear"))[i];
            result.obstacle = ((byte[])Field(grid, "Obstacle"))[i];
            result.gridGroundY = ((float[])Field(grid, "GroundY"))[i];
            result.fieldGroundY = ((float[])Field(field, "GroundY"))[i];
            result.gridGroundValid = !float.IsNaN(result.gridGroundY) && !float.IsInfinity(result.gridGroundY);
            result.fieldGroundValid = !float.IsNaN(result.fieldGroundY) && !float.IsInfinity(result.fieldGroundY);
            if (!result.gridGroundValid) result.gridGroundY = 0f;
            if (!result.fieldGroundValid) result.fieldGroundY = 0f;
            result.densityPerSquareMetre = new float[layers.Length];
            for (int t = 0; t < layers.Length; t++) result.densityPerSquareMetre[t] = layers[t][i] * scales[t] / 255f;
            return result;
        }

        [Serializable] private sealed class PoseRecord
        {
            public string status = "UNKNOWN until two-pose depth/MSAA/alpha/occlusion/pixel-registration checks";
            public string pose, graphicsApi, unityVersion;
            public Vector2 feet;
            public float actualGroundY;
            public Vector3 actualChosenGround, cameraPosition, chosenGroundViewport;
            public Vector4 chosenGroundGpuClip, windBefore, windAfter, vegWaveBefore, vegWaveAfter, shaderTimeBefore, shaderTimeAfter;
            public Matrix4x4 view, projection, gpuProjection, inverseGpuViewProjection;
            public bool reversedZ, uvStartsAtTop, featureListRestored;
            public int width = W, height = H, requestedMsaa = 4, fullRepeatChanged, bareRepeatChanged, copiedFrames;
            public int[] fullCategoryPixels;
            public string legend = "R:0 depth-clear (ZWriteOff unknown),1 ground,2 opaque-queue,3 canopy,4 original instanced vegetation,5 other/mixed,6 fog/shaft;255 ambiguous/unstable. Provisional category IDs, not renderer IDs.";
            public string registration = "PNG and RFloat readback use ReadPixels lower-left origin. GPU projection is renderIntoTexture=true. Verify point registration before interpreting pixels.";
            public string limits = "Raw non-linear depth; CopyDepth resolves MSAA to farthest sample. ZWriteOff/transparent color, shadows and subpixel coverage cannot be identified from depth alone. No ForceLOD, material override, wind freeze or replacement draw.";
            public string sourceDepth;
            public int fullRgbChanged, bareRgbChanged, bareMaskMembershipChanged;
            public bool runtimeEntropyReplayed;
            public string entropyRestoreEvidence = "Per-render finally restore is verified; outer restore failure throws UNKNOWN. diagnostic-frames.json records all requested camera renders.";
            public ProbeRecord feetGridProbe;
            public RendererRecord[] renderers;
            public HitRecord[] groundRayHits;
        }

        public void Capture(Camera cam, string name, Vector2 feet, Vector3 ground)
        {
            DumpGrid();
            if (_entropyBinding == null || _bareEntropy == null)
                throw new InvalidOperationException("UNKNOWN: original bare entropy snapshot missing");
            SsaoEntropy entryEntropy = _entropyBinding.Snapshot();
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            var records = new RendererRecord[renderers.Length];
            var off = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                off[i] = r.forceRenderingOff;
                Material[] materials = r.sharedMaterials;
                var matNames = new string[materials.Length];
                var shaderNames = new string[materials.Length];
                for (int m = 0; m < materials.Length; m++)
                {
                    matNames[m] = materials[m] != null ? materials[m].name : "<null>";
                    shaderNames[m] = materials[m] != null && materials[m].shader != null ? materials[m].shader.name : "<null>";
                }
                records[i] = new RendererRecord { instanceId = r.GetInstanceID(), path = Hierarchy(r.transform),
                    materials = matNames, shaders = shaderNames, enabled = r.enabled,
                    forceRenderingOff = off[i], category = Category(materials) };
            }
            bool draw = (bool)_draw.GetValue(null);
            CameraClearFlags clear = cam.clearFlags;
            Color background = cam.backgroundColor;
            var data = cam.GetUniversalAdditionalCameraData();
            bool post = data.renderPostProcessing;
            var feature = ScriptableObject.CreateInstance<TdCoverDepthFeature>();
            feature.hideFlags = HideFlags.HideAndDontSave;
            try { feature.Initialize(cam, W, H); }
            catch { feature.Dispose(); Object.DestroyImmediate(feature); throw; }
            var renderer = data.scriptableRenderer;
            var getter = typeof(ScriptableRenderer).GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);
            if (getter == null || getter.PropertyType != typeof(List<ScriptableRendererFeature>))
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
                throw new InvalidOperationException("Pinned URP14 live feature-list API mismatch");
            }
            var live = (List<ScriptableRendererFeature>)getter.GetValue(renderer);
            if (!(renderer is UniversalRenderer) || live == null || live.Count == 0)
            {
                feature.Dispose();
                Object.DestroyImmediate(feature);
                throw new InvalidOperationException("UNKNOWN: diagnostic requires existing UniversalRenderer features; do not alter intermediate-target policy");
            }
            ScriptableRendererFeature[] original = live.ToArray();
            bool restored = false;
            Texture depthBindingBefore = Shader.GetGlobalTexture("_CameraDepthAttachment");
            string[] depthKeywords = { "_DEPTH_MSAA_2", "_DEPTH_MSAA_4", "_DEPTH_MSAA_8", "_OUTPUT_DEPTH" };
            var keywordStates = new bool[depthKeywords.Length];
            for (int i = 0; i < depthKeywords.Length; i++) keywordStates[i] = Shader.IsKeywordEnabled(depthKeywords[i]);
            Vector4 windBefore = Shader.GetGlobalVector("_DD_WindVector"),
                waveBefore = Shader.GetGlobalVector("_DD_VegWaveW"), timeBefore = Shader.GetGlobalVector("_Time");
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                data.renderPostProcessing = false;
                SsaoEntropy fullEntropy = _entropyBinding.Snapshot();
                Label(name + "-full-original-source");
                Color32[] originalFull = ReadOriginalRgb(cam);
                SaveIds(originalFull, Path.Combine(_dir, name + "-full-original-source.png"));
                live.Add(feature);
                float[] full = ReplayDepth(fullEntropy, cam, feature, name + "-depth-full", true);
                Color32[] fullRgb = _lastRgb;
                int fullRgbChanged = Changed(originalFull, fullRgb);
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].forceRenderingOff = off[i] || MetricHidden(renderers[i].sharedMaterial);
                float[] bare = ReplayDepth(_bareEntropy, cam, feature, name + "-depth-bare", true);
                Color32[] originalBare = LoadRgb(Path.Combine(_dir, name + "-bare-source.png"));
                int bareRgbChanged = Changed(originalBare, _lastRgb);
                int bareMembershipChanged = ChangedMembership(originalBare, _lastRgb);
                var isolated = new float[6][];
                for (int category = 1; category <= 6; category++)
                {
                    for (int i = 0; i < renderers.Length; i++)
                        renderers[i].forceRenderingOff = off[i] || records[i].category != category;
                    _draw.SetValue(null, draw && category == 4);
                    isolated[category - 1] = ReplayDepth(fullEntropy, cam, feature, name + "-depth-category-" + category);
                }
                for (int i = 0; i < renderers.Length; i++) renderers[i].forceRenderingOff = off[i];
                _draw.SetValue(null, draw);
                float[] fullAgain = ReplayDepth(fullEntropy, cam, feature, name + "-depth-full-repeat");
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].forceRenderingOff = off[i] || MetricHidden(renderers[i].sharedMaterial);
                float[] bareAgain = ReplayDepth(_bareEntropy, cam, feature, name + "-depth-bare-repeat");
                var ids = new Color32[W * H];
                var counts = new int[256];
                int fullChanged = 0, bareChanged = 0;
                for (int p = 0; p < ids.Length; p++)
                {
                    bool depthStable = Equal(full[p], fullAgain[p]);
                    if (!depthStable) fullChanged++;
                    bool stable = depthStable && SameRgb(originalFull[p], fullRgb[p]);
                    if (!Equal(bare[p], bareAgain[p])) bareChanged++;
                    int id = 255;
                    if (stable && ClearDepth(full[p])) id = 0;
                    else if (stable)
                    {
                        int matches = 0;
                        for (int c = 0; c < isolated.Length; c++)
                            if (!ClearDepth(isolated[c][p]) && Equal(full[p], isolated[c][p])) { id = c + 1; matches++; }
                        if (matches != 1) id = 255;
                    }
                    ids[p] = new Color32((byte)id, 0, 0, 255);
                    counts[id]++;
                }
                SaveIds(ids, Path.Combine(_dir, name + "-category-id-provisional.png"));
                RaycastHit[] hits = Physics.RaycastAll(new Vector3(feet.x, 30f, feet.y), Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                var hitRows = new HitRecord[hits.Length];
                for (int i = 0; i < hits.Length; i++)
                    hitRows[i] = new HitRecord { collider = Hierarchy(hits[i].collider.transform), point = hits[i].point, distance = hits[i].distance };
                Matrix4x4 gpu = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
                // Remove and verify before emitting success of this particular restoration.
                live.Remove(feature);
                restored = SameList(live, original);
                WriteJson(name + "-diagnostics.json", new PoseRecord { pose = name, feet = feet, actualChosenGround = ground, actualGroundY = ground.y,
                    cameraPosition = cam.transform.position, view = cam.worldToCameraMatrix, projection = cam.projectionMatrix,
                    chosenGroundViewport = cam.WorldToViewportPoint(ground),
                    chosenGroundGpuClip = gpu * cam.worldToCameraMatrix * new Vector4(ground.x, ground.y, ground.z, 1f),
                    windBefore = windBefore, windAfter = Shader.GetGlobalVector("_DD_WindVector"),
                    vegWaveBefore = waveBefore, vegWaveAfter = Shader.GetGlobalVector("_DD_VegWaveW"),
                    shaderTimeBefore = timeBefore, shaderTimeAfter = Shader.GetGlobalVector("_Time"),
                    gpuProjection = gpu, inverseGpuViewProjection = (gpu * cam.worldToCameraMatrix).inverse,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(), unityVersion = Application.unityVersion,
                    reversedZ = SystemInfo.usesReversedZBuffer, uvStartsAtTop = SystemInfo.graphicsUVStartsAtTop,
                    fullRepeatChanged = fullChanged, bareRepeatChanged = bareChanged, fullCategoryPixels = counts,
                    renderers = records, groundRayHits = hitRows, copiedFrames = feature.CopiedFrames,
                    sourceDepth = feature.SourceDescription, featureListRestored = restored,
                    fullRgbChanged = fullRgbChanged, bareRgbChanged = bareRgbChanged, feetGridProbe = Probe(feet),
                    bareMaskMembershipChanged = bareMembershipChanged, runtimeEntropyReplayed = true });
                if (!restored) throw new InvalidOperationException("Diagnostic renderer feature-list restoration failed");
            }
            finally
            {
                live.Remove(feature);
                for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].forceRenderingOff = off[i];
                _draw.SetValue(null, draw);
                cam.clearFlags = clear;
                cam.backgroundColor = background;
                data.renderPostProcessing = post;
                Shader.SetGlobalTexture("_CameraDepthAttachment", depthBindingBefore);
                for (int i = 0; i < depthKeywords.Length; i++)
                    if (keywordStates[i]) Shader.EnableKeyword(depthKeywords[i]); else Shader.DisableKeyword(depthKeywords[i]);
                feature.Dispose();
                Object.DestroyImmediate(feature);
                entryEntropy.Restore();
                _poseRestores++;
                _bareEntropy = null;
                if (!SameList(live, original)) throw new InvalidOperationException("Diagnostic feature-list drift");
            }
        }

        private static bool SameList(List<ScriptableRendererFeature> live, ScriptableRendererFeature[] original)
        {
            if (live.Count != original.Length) return false;
            for (int i = 0; i < original.Length; i++) if (!ReferenceEquals(live[i], original[i])) return false;
            return true;
        }

        private float[] ReadDepth(Camera cam, TdCoverDepthFeature feature, string name, bool rgb = false)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var read = new Texture2D(W, H, TextureFormat.RFloat, false, true);
            RenderTexture oldTarget = cam.targetTexture, oldActive = RenderTexture.active;
            float aspect = cam.aspect;
            int before = feature.CopiedFrames;
            try
            {
                if (!rt.Create() || !rt.IsCreated() || feature.Output == null || !feature.Output.IsCreated())
                    throw new InvalidOperationException("UNKNOWN: color/depth output allocation failed");
                cam.targetTexture = rt;
                cam.aspect = (float)W / H;
                cam.Render();
                if (!rt.IsCreated() || !feature.Output.IsCreated())
                    throw new InvalidOperationException("UNKNOWN: color/depth output lost during render");
                if (feature.CopiedFrames != before + 1) throw new InvalidOperationException("Actual camera depth copy did not execute");
                if (rgb)
                {
                    _lastRgb = ReadRgb(rt);
                    SaveIds(_lastRgb, Path.Combine(_dir, name + "-source.png"));
                }
                RenderTexture.active = feature.Output;
                read.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                read.Apply(false);
                var values = read.GetRawTextureData<float>().ToArray();
                if (values.Length != W * H) throw new InvalidOperationException("UNKNOWN: depth pixel count differs");
                foreach (float value in values)
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                        throw new InvalidOperationException("UNKNOWN: invalid raw depth readback");
                using (var writer = new BinaryWriter(File.Create(Path.Combine(_dir, name + ".rfloat"))))
                    foreach (float value in values) writer.Write(value);
                return values;
            }
            finally
            {
                cam.targetTexture = oldTarget;
                cam.aspect = aspect;
                RenderTexture.active = oldActive;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
            }
        }

        private static Color32[] ReadOriginalRgb(Camera cam)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture target = cam.targetTexture, active = RenderTexture.active;
            float aspect = cam.aspect;
            try
            {
                if (!rt.Create() || !rt.IsCreated()) throw new InvalidOperationException("UNKNOWN: original RGB allocation failed");
                cam.targetTexture = rt; cam.aspect = (float)W / H; cam.Render(); return ReadRgb(rt);
            }
            finally { cam.targetTexture = target; cam.aspect = aspect; RenderTexture.active = active; rt.Release(); Object.DestroyImmediate(rt); }
        }
        private static Color32[] ReadRgb(RenderTexture rt)
        {
            if (rt == null || !rt.IsCreated() || rt.width != W || rt.height != H)
                throw new InvalidOperationException("UNKNOWN: RGB attachment unavailable or wrong size");
            var texture = new Texture2D(W, H, TextureFormat.RGBA32, false);
            RenderTexture active = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                texture.Apply(false);
                Color32[] pixels = texture.GetPixels32();
                if (pixels.Length != W * H) throw new InvalidOperationException("UNKNOWN: RGB pixel count differs");
                return pixels;
            }
            finally { RenderTexture.active = active; Object.DestroyImmediate(texture); }
        }
        private static Color32[] LoadRgb(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)) || texture.width != W || texture.height != H)
                    throw new InvalidOperationException("Original metric RGB registration dimensions differ");
                return texture.GetPixels32();
            }
            finally { Object.DestroyImmediate(texture); }
        }
        private static bool SameRgb(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;
        private static int ChangedMembership(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) throw new InvalidOperationException("UNKNOWN: metric registration sizes differ");
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if ((a[i].r + a[i].g + a[i].b <= 6) != (b[i].r + b[i].g + b[i].b <= 6)) changed++;
            return changed;
        }
        private static int Changed(Color32[] a, Color32[] b)
        {
            if (a.Length != b.Length) throw new InvalidOperationException("RGB registration sizes differ");
            int changed = 0;
            for (int i = 0; i < a.Length; i++) if (!SameRgb(a[i], b[i])) changed++;
            return changed;
        }

        private static bool Equal(float a, float b) => !float.IsNaN(a) && !float.IsNaN(b) && Math.Abs(a - b) <= 0.000001f;
        private static bool ClearDepth(float v) => v == (SystemInfo.usesReversedZBuffer ? 0f : 1f);
        private static bool MetricHidden(Material m)
        {
            string s = m != null && m.shader != null ? m.shader.name : "";
            return s == "DD/GroundTiles" || s == "DD/GroundMoss" || s == "DD/GroundBlend" ||
                   s == "DD/GroundTerrain" || s == "DD/FogSheet" || s == "DD/ShaftCard";
        }
        private static int Category(Material[] materials)
        {
            int result = 0;
            foreach (Material m in materials)
            {
                int next = 5;
                if (m != null && m.shader != null)
                {
                    string s = m.shader.name;
                    if (s == "DD/FogSheet" || s == "DD/ShaftCard") next = 6;
                    else if (MetricHidden(m)) next = 1;
                    else if (s == "DD/Foliage Wind") next = 3;
                    else if (s == "DD/World Vegetation") next = 4;
                    else if (m.renderQueue <= 2500) next = 2;
                }
                if (result != 0 && next != result) return 5;
                result = next;
            }
            return result == 0 ? 5 : result;
        }
        private static string Hierarchy(Transform t) => t.parent != null ? Hierarchy(t.parent) + "/" + t.name : t.name;
        private void WriteJson(string name, object value) => File.WriteAllText(Path.Combine(_dir, name), JsonUtility.ToJson(value, true));
        private static void SaveIds(Color32[] values, string path)
        {
            var texture = new Texture2D(W, H, TextureFormat.RGBA32, false);
            try { texture.SetPixels32(values); texture.Apply(false); File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
    }

    // Transient live renderer feature. Only our camera is accepted. No renderer asset/cache/index mutation.
    internal sealed class TdCoverDepthFeature : ScriptableRendererFeature
    {
        private Camera _camera;
        private Material _material;
        private RTHandle _output;
        private DepthPass _pass;
        public RenderTexture Output => _output.rt;
        public int CopiedFrames;
        public string SourceDescription;

        public void Initialize(Camera camera, int width, int height)
        {
            _camera = camera;
            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, FormatUsage.Render))
                throw new InvalidOperationException("R32 float depth copy unsupported");
            Shader shader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("URP14 CopyDepth shader unavailable");
            _material = CoreUtils.CreateEngineMaterial(shader);
            var rt = new RenderTexture(new RenderTextureDescriptor(width, height) {
                graphicsFormat = GraphicsFormat.R32_SFloat, depthBufferBits = 0,
                depthStencilFormat = GraphicsFormat.None, msaaSamples = 1, sRGB = false
            }) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            // Take ownership first so Initialize's caller can dispose even a failed allocation.
            _output = RTHandles.Alloc(rt);
            if (!rt.Create() || !rt.IsCreated())
                throw new InvalidOperationException("UNKNOWN: R32 depth output allocation failed");
            _pass = new DepthPass(this) { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
        }
        public override void Create() { }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (data.cameraData.camera == _camera) renderer.EnqueuePass(_pass);
        }
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData data)
        {
            if (data.cameraData.camera == _camera) _pass.Setup(renderer.cameraDepthTargetHandle, data.cameraData.cameraTargetDescriptor);
        }
        protected override void Dispose(bool disposing)
        {
            if (_output != null)
            {
                _output.Release();
                _output = null;
            }
            CoreUtils.Destroy(_material);
            _material = null;
        }
        private sealed class DepthPass : ScriptableRenderPass
        {
            private readonly TdCoverDepthFeature _owner;
            private CopyDepthPass _copy;
            public DepthPass(TdCoverDepthFeature owner) => _owner = owner;
            public void Setup(RTHandle source, RenderTextureDescriptor cameraDescriptor)
            {
                if (source == null || source.rt == null)
                    throw new InvalidOperationException("UNKNOWN: actual depth attachment is not a texture");
                RenderTexture rt = source.rt;
                if (!rt.IsCreated() || _owner.Output == null || !_owner.Output.IsCreated())
                    throw new InvalidOperationException("UNKNOWN: actual source/output attachment is not created");
                if (rt.depthStencilFormat == GraphicsFormat.None && rt.depth == 0)
                    throw new InvalidOperationException("UNKNOWN: actual attachment has no depth");
                if (rt.width != _owner.Output.width || rt.height != _owner.Output.height ||
                    rt.antiAliasing != cameraDescriptor.msaaSamples)
                    throw new InvalidOperationException("UNKNOWN: attachment size/MSAA differs from camera descriptor");
                _owner.SourceDescription = rt.name + "; format=" + rt.depthStencilFormat + "; msaa=" + rt.antiAliasing +
                    "; bindMS=" + rt.bindTextureMS + "; cameraMSAA=" + cameraDescriptor.msaaSamples;
                // Public URP14 CopyDepthPass resolves actual bound MSAA when bindMS=true, resolved texture otherwise.
                _copy = new CopyDepthPass(renderPassEvent, _owner._material, false, false, !rt.bindTextureMS);
                _copy.Setup(source, _owner._output);
            }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data) => ConfigureTarget(_owner._output);
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                if (_copy == null) throw new InvalidOperationException("UNKNOWN: depth copy setup missing");
                _copy.Execute(context, ref data);
                _owner.CopiedFrames++;
            }
        }
    }
}
