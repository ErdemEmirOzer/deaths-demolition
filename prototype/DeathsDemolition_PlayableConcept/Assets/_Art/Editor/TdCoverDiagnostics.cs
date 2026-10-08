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
        private Collider _groundCollider;
        private ColliderRecord _groundColliderRecord;
        private ScreenRayTarget _screenRayTarget;
        private ScreenRayRecord[] _screenRays;
        private PhysicalInventory _physicalInventory;


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
            try { WriteJson("diagnostic-frames.json", new FrameRecords { frames = _frames.ToArray(), verifiedReplayCpuStateRestores = _replayRestores, verifiedPoseCpuStateRestores = _poseRestores, verifiedRendererVisibilityRestores = _visibilityRestores }); }
            finally { _previewScope.Dispose(); }
        }

        private Camera _observedCamera;
        private SsaoEntropyBinding _entropyBinding;
        private SsaoEntropy _bareEntropy;
        private string _renderLabel;
        private readonly List<FrameRecord> _frames = new List<FrameRecord>();
        private FrameRecord _pendingFrame;
        private int _replayRestores, _poseRestores, _visibilityRestores;

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
            public int materialInstanceId, pixelWidth, pixelHeight, noiseArrayIdentity, selectedNoiseTextureId, selectedNoiseWidth, selectedNoiseHeight;
            public Vector4 expectedBlueNoiseParams;
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
            public int verifiedReplayCpuStateRestores, verifiedPoseCpuStateRestores, verifiedRendererVisibilityRestores;
            public string gpuMaterialPropertyReadback = "NOT_SUPPORTED: shader has no texture Properties; no Material.GetTexture/GetVector used";
            public string gpuRestoreWrite = "RECONSTRUCTED_WRITE from END actual render pixelWidth/Height + typed pass array/index/offsets; not GPU readback verified";
        }

        private FrameState FrameStateNow() => new FrameState
        {
            frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
            randomStateHash = UnityEngine.Random.state.GetHashCode(),
            noiseIndex = _entropyBinding?.Index ?? -1, noiseOffsetX = _entropyBinding?.OffsetX ?? 0f,
            noiseOffsetY = _entropyBinding?.OffsetY ?? 0f, materialInitialized = _entropyBinding?.MaterialReady ?? false,
            materialPersistent = _entropyBinding?.MaterialPersistent ?? false, materialInstanceId = _entropyBinding?.MaterialInstanceId ?? 0,
            bindingInitialized = _entropyBinding != null,
            pixelWidth = _observedCamera.pixelWidth, pixelHeight = _observedCamera.pixelHeight,
            noiseArrayIdentity = _entropyBinding?.NoiseArrayIdentity ?? 0,
            selectedNoiseTextureId = _entropyBinding?.Noise.GetInstanceID() ?? 0,
            selectedNoiseWidth = _entropyBinding?.Noise.width ?? 0, selectedNoiseHeight = _entropyBinding?.Noise.height ?? 0,
            expectedBlueNoiseParams = _entropyBinding != null ? _entropyBinding.ExpectedParams(_observedCamera.pixelWidth, _observedCamera.pixelHeight) : Vector4.zero,
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
            _entropyBinding.RecordRenderDimensions(camera.pixelWidth, camera.pixelHeight);
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
            private readonly FieldInfo _index, _x, _y, _passMaterial, _featureMaterial, _passTextures, _featureTextures;
            private readonly Texture2D[] _textures, _originalTextures;
            private int _lastPixelWidth, _lastPixelHeight;
            public int NoiseArrayIdentity => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_textures);
            public Texture2D Noise => _textures[Index];
            public Vector4 ExpectedParams(int width, int height) => new Vector4(width / (float)Noise.width, height / (float)Noise.height, OffsetX, OffsetY);
            public void RecordRenderDimensions(int width, int height)
            {
                if (width <= 0 || height <= 0) throw new InvalidOperationException("UNKNOWN: actual SSAO render dimensions invalid");
                _lastPixelWidth = width; _lastPixelHeight = height;
            }
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
                _passTextures = ExactField(passType, "m_BlueNoiseTextures", typeof(Texture2D[]));
                _featureTextures = ExactField(featureType, "m_BlueNoise256Textures", typeof(Texture2D[]));
                _textures = _passTextures.GetValue(_pass) as Texture2D[];
                if (_textures == null || _textures.Length == 0) throw new InvalidOperationException("UNKNOWN: actual SSAO noise array missing");
                _originalTextures = (Texture2D[])_textures.Clone();
                AssertCurrent(renderer);
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
                if (!ReferenceEquals(_passTextures.GetValue(_pass), _textures) || !ReferenceEquals(_featureTextures.GetValue(_feature), _textures))
                    throw new InvalidOperationException("UNKNOWN: actual SSAO noise array identity changed");
                for (int i = 0; i < _textures.Length; i++)
                    if (_textures[i] == null || !ReferenceEquals(_textures[i], _originalTextures[i]) || _textures[i].width <= 0 || _textures[i].height <= 0)
                        throw new InvalidOperationException("UNKNOWN: actual SSAO noise texture identity/size invalid");
                if (Index < 0 || Index >= _textures.Length || float.IsNaN(OffsetX) || float.IsInfinity(OffsetX) || float.IsNaN(OffsetY) || float.IsInfinity(OffsetY))
                    throw new InvalidOperationException("UNKNOWN: actual SSAO typed index/offset state invalid");
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
                if (_lastPixelWidth <= 0 || _lastPixelHeight <= 0)
                    throw new InvalidOperationException("UNKNOWN: last actual SSAO render dimensions unavailable");
                return new SsaoEntropy(this, UnityEngine.Random.state, Index, OffsetX, OffsetY, material,
                    Noise, _lastPixelWidth, _lastPixelHeight, ExpectedParams(_lastPixelWidth, _lastPixelHeight));
            }

            public void Restore(SsaoEntropy state)
            {
                Material material = RuntimeMaterial();
                if (material != state.Material || state.Noise != _textures[state.Index])
                    throw new InvalidOperationException("UNKNOWN: SSAO runtime material/noise identity changed");
                _index.SetValue(_pass, state.Index);
                _x.SetValue(_pass, state.X);
                _y.SetValue(_pass, state.Y);
                UnityEngine.Random.state = state.Random;
                // The pinned OnCameraSetup overwrites both on the NEXT existing render. These setters reconstruct the
                // previous END state using that actual target size; unsupported GPU getters are never verification.
                material.SetTexture("_BlueNoiseTexture", state.Noise);
                material.SetVector("_SSAOBlueNoiseParams", state.ExpectedParameters);
                RecordRenderDimensions(state.PixelWidth, state.PixelHeight);
                AssertCurrent(_renderer);
                if (Index != state.Index || OffsetX != state.X || OffsetY != state.Y ||
                    !UnityEngine.Random.state.Equals(state.Random) || UnityEditor.AssetDatabase.Contains(material))
                    throw new InvalidOperationException("UNKNOWN: SSAO/RNG typed CPU state restoration failed");
            }
        }

        private sealed class SsaoEntropy
        {
            private readonly SsaoEntropyBinding _binding;
            public readonly UnityEngine.Random.State Random;
            public readonly int Index, PixelWidth, PixelHeight;
            public readonly float X, Y;
            public readonly Material Material;
            public readonly Texture2D Noise;
            public readonly Vector4 ExpectedParameters;
            public SsaoEntropy(SsaoEntropyBinding binding, UnityEngine.Random.State random, int index,
                float x, float y, Material material, Texture2D noise, int width, int height, Vector4 expectedParameters)
            {
                _binding = binding; Random = random; Index = index; X = x; Y = y; Material = material; Noise = noise;
                PixelWidth = width; PixelHeight = height; ExpectedParameters = expectedParameters;
            }
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

        [Serializable] private sealed class ColliderRecord
        {
            public string status, path, type;
            public int instanceId, layer;
            public bool enabled, isTrigger;
            public Matrix4x4 localToWorld;
            public Vector3 boundsCenter, boundsSize, boxCenter, boxSize;
            public bool isBox;
        }
        [Serializable] private sealed class ScreenRayTarget
        {
            public string label;
            public int width, height;
            public float aspect, near, far;
            public float[] pixelRect;
            public bool renderIntoTexture;
            public Matrix4x4 view, projection, gpuProjection, inverseGpuViewProjection;
        }
        [Serializable] private sealed class ScreenRayRecord
        {
            public int[] pixelLowerLeft;
            public Vector3 origin, direction, point, normal;
            public bool hit;
            public int colliderInstanceId;
            public string colliderPath;
            public float distance;
        }
        private void SelectGroundCollider(Vector2 feet, Vector3 chosen)
        {
            _groundCollider = null; _screenRays = null; _screenRayTarget = null; _physicalInventory = null;
            // Exactly the legacy Ground query; exact hit-point equality, never a 2cm identity tolerance.
            if (Physics.Raycast(new Vector3(feet.x, 30f, feet.y), Vector3.down, out RaycastHit hit,
                60f, ~0, QueryTriggerInteraction.Ignore) && hit.point.Equals(chosen)) _groundCollider = hit.collider;
            if (_groundCollider == null)
            {
                _groundColliderRecord = new ColliderRecord { status = "NOTRECORDED: legacy chosen ground has no matching physics collider" };
                return;
            }
            Collider c = _groundCollider;
            _groundColliderRecord = new ColliderRecord { status = "ACTUAL_LEGACY_GROUND_HIT; independent registration only, not occlusion proof",
                instanceId = c.GetInstanceID(), path = Hierarchy(c.transform), type = c.GetType().FullName,
                enabled = c.enabled, isTrigger = c.isTrigger, layer = c.gameObject.layer,
                localToWorld = c.transform.localToWorldMatrix, boundsCenter = c.bounds.center, boundsSize = c.bounds.size };
            if (c is BoxCollider box)
            { _groundColliderRecord.isBox = true; _groundColliderRecord.boxCenter = box.center; _groundColliderRecord.boxSize = box.size; }
        }
        private void CaptureScreenRays(Camera camera, string label)
        {
            Rect rect = camera.pixelRect;
            Matrix4x4 gpu = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            _screenRayTarget = new ScreenRayTarget { label = label, width = camera.pixelWidth, height = camera.pixelHeight,
                aspect = camera.aspect, near = camera.nearClipPlane, far = camera.farClipPlane,
                pixelRect = new[] { rect.x, rect.y, rect.width, rect.height }, renderIntoTexture = camera.targetTexture != null,
                view = camera.worldToCameraMatrix, projection = camera.projectionMatrix, gpuProjection = gpu,
                inverseGpuViewProjection = (gpu * camera.worldToCameraMatrix).inverse };
            var rows = new List<ScreenRayRecord>();
            foreach (int y in new[] { 45, 135, 225 }) foreach (int x in new[] { 80, 240, 400 })
            {
                Ray ray = camera.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0f));
                var row = new ScreenRayRecord { pixelLowerLeft = new[] { x, y }, origin = ray.origin, direction = ray.direction,
                    colliderInstanceId = _groundCollider != null ? _groundCollider.GetInstanceID() : 0,
                    colliderPath = _groundCollider != null ? Hierarchy(_groundCollider.transform) : "NOTRECORDED" };
                if (_groundCollider != null && _groundCollider.Raycast(ray, out RaycastHit hit, camera.farClipPlane))
                { row.hit = true; row.point = hit.point; row.normal = hit.normal; row.distance = hit.distance; }
                rows.Add(row); // Keep misses, including absent chosen collider.
            }
            _screenRays = rows.ToArray();
        }

        [Serializable] private sealed class MaterialState
        {
            public string name, shader, depthPolicy;
            public int instanceId, shaderInstanceId, renderQueue;
            public bool hasZWrite, hasSurface;
            public float zWrite, surface;
        }
        [Serializable] private sealed class WorldBoundsRecord { public Vector3 center, size; }
        [Serializable] private sealed class CoverageRow
        {
            public int instanceId, drawIndex, instanceCount, meshInstanceId, materialInstanceId;
            public string path, shader, projectionStatus, boundsEnvelope;
            public WorldBoundsRecord worldBounds;
            public int[] projectedConservativeRectLowerLeft;
            public MaterialState[] materialStates;
        }
        [Serializable] private sealed class InstancedInventory
        {
            public bool complete, drawEnabled;
            public string status, knownVegetationShaderSha256;
            public int actualDrawCount, actualSubmittedInstances, cameraInstanceId;
            public Vector4 actualWave, actualPush, actualShape;
            public CoverageRow[] draws = new CoverageRow[0];
        }
        [Serializable] private sealed class PhysicalInventory
        {
            public string schema = "DD_PHYSICAL_COVERAGE_V1", producerPin, sourceSha256, status;
            public bool complete;
            public CoverageRow[] renderers;
            public InstancedInventory instanced;
            public string limits = "Conservative projected bounds only; material names alone do not prove screen pixels. Instanced counts are actual submitted LOD pages, not unique roots or visible fragments.";
        }
        private static readonly HashSet<string> GroundAir = new HashSet<string> {
            "DD/GroundTerrain", "DD/GroundTiles", "DD/GroundMoss", "DD/GroundBlend", "DD/FogSheet", "DD/SunDisc", "DD/Water v2", "DD/Water v1", "DD/ShaftCard" };
        private static string ProjectFileHash(string assetPath) => FileHash(Path.Combine(Application.dataPath, "..", assetPath));
        private static CoverageRow CoverageBounds(Camera camera, Bounds bounds)
        {
            var row = new CoverageRow { worldBounds = new WorldBoundsRecord { center = bounds.center, size = bounds.size } };
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
            float minX = W, minY = H, maxX = 0, maxY = 0;
            bool nearCrossing = false;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                Vector4 clip = vp * new Vector4(point.x, point.y, point.z, 1);
                if (float.IsNaN(clip.x) || float.IsNaN(clip.y) || float.IsNaN(clip.z) || float.IsNaN(clip.w)
                    || float.IsInfinity(clip.x) || float.IsInfinity(clip.y) || float.IsInfinity(clip.z) || float.IsInfinity(clip.w))
                    throw new InvalidOperationException("nonfinite actual bounds projection");
                // This inventory supports the actual Metal/D3D 0..1 clip depth convention only.
                if (clip.w <= 0f || clip.z < 0f || clip.z > clip.w) nearCrossing = true;
                if (clip.w > 0f)
                {
                    float px = (clip.x / clip.w + 1f) * W * 0.5f;
                    float py = (SystemInfo.graphicsUVStartsAtTop ? 1f - clip.y / clip.w : clip.y / clip.w + 1f) * H * 0.5f;
                    minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px); minY = Mathf.Min(minY, py); maxY = Mathf.Max(maxY, py);
                }
            }
            row.projectionStatus = nearCrossing ? "FULL_VIEWPORT" : "CONSERVATIVE_BOUNDS_RECT";
            row.projectedConservativeRectLowerLeft = nearCrossing ? new[] { 0, 0, W, H } : new[] {
                Mathf.Clamp(Mathf.FloorToInt(minX), 0, W), Mathf.Clamp(Mathf.FloorToInt(minY), 0, H),
                Mathf.Clamp(Mathf.CeilToInt(maxX), 0, W), Mathf.Clamp(Mathf.CeilToInt(maxY), 0, H) };
            return row;
        }
        private PhysicalInventory CapturePhysicalInventory(Camera camera)
        {
            var inventory = new PhysicalInventory { producerPin = Environment.GetEnvironmentVariable("DD_TD_SOURCE_PIN"),
                instanced = new InstancedInventory { drawEnabled = (bool)_draw.GetValue(null), cameraInstanceId = camera.GetInstanceID() } };
            try
            {
                inventory.sourceSha256 = ProjectFileHash("Assets/_Art/Editor/TdCoverDiagnostics.cs");
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11
                    && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12) throw new NotSupportedException("physical inventory clip-depth convention");
                var rows = new List<CoverageRow>();
                foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
                {
                    if (!renderer.enabled || renderer.forceRenderingOff) continue;
                    Material[] materials = renderer.sharedMaterials;
                    bool allGroundAir = materials.Length > 0;
                    foreach (Material material in materials) allGroundAir &= material != null && material.shader != null && GroundAir.Contains(material.shader.name);
                    if (allGroundAir) continue;
                    CoverageRow row = CoverageBounds(camera, renderer.bounds);
                    row.instanceId = renderer.GetInstanceID(); row.path = Hierarchy(renderer.transform);
                    row.materialStates = new MaterialState[materials.Length];
                    bool envelopeKnown = true;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material material = materials[i];
                        var state = new MaterialState { name = material != null ? material.name : "<null>",
                            shader = material != null && material.shader != null ? material.shader.name : "<null>", depthPolicy = "UNKNOWN" };
                        if (material != null)
                        {
                            state.instanceId = material.GetInstanceID(); state.shaderInstanceId = material.shader != null ? material.shader.GetInstanceID() : 0;
                            state.renderQueue = material.renderQueue; state.hasZWrite = material.HasProperty("_ZWrite"); state.hasSurface = material.HasProperty("_Surface");
                            if (state.hasZWrite) state.zWrite = material.GetFloat("_ZWrite");
                            if (state.hasSurface) state.surface = material.GetFloat("_Surface");
                            // UnderstorySkirt relies on ShaderLab default ZWrite On; consumer may conservatively keep it ambiguous.
                            if (state.shader == "DD/UnderstorySkirt") state.depthPolicy = "SHADER_DEFAULT_ON";
                        }
                        envelopeKnown &= GroundAir.Contains(state.shader) || state.shader == "Universal Render Pipeline/Lit" || state.shader == "DD/UnderstorySkirt"
                            || (state.shader == "DD/Foliage Wind" && Shader.GetGlobalVector("_DD_WindVector").z == 0f);
                        row.materialStates[i] = state;
                    }
                    row.boundsEnvelope = envelopeKnown ? "ACTUAL_RENDERER_BOUNDS; audited rigid shader or actual calm foliage" : "UNKNOWN_VERTEX_ENVELOPE";
                    if (!envelopeKnown) { row.projectionStatus = "FULL_VIEWPORT"; row.projectedConservativeRectLowerLeft = new[] { 0, 0, W, H }; }
                    rows.Add(row);
                }
                inventory.renderers = rows.ToArray();
                CaptureInstancedInventory(camera, inventory.instanced);
                inventory.complete = inventory.instanced.complete;
                inventory.status = inventory.complete ? "ACTUAL_BOUNDS_AND_MATERIALS_RECORDED" : "UNKNOWN: instanced inventory incomplete";
            }
            catch (Exception error) { inventory.complete = false; inventory.status = "NOTRECORDED: " + error.GetType().Name + ": " + error.Message; }
            return inventory;
        }
        private void CaptureInstancedInventory(Camera camera, InstancedInventory record)
        {
            record.actualWave = Shader.GetGlobalVector("_DD_VegWaveW"); record.actualPush = Shader.GetGlobalVector("_DD_VegPushOn");
            record.actualShape = Shader.GetGlobalVector("_DD_VegShape");
            if (!record.drawEnabled) { record.complete = true; record.status = "DRAW_DISABLED"; return; }
            if (record.actualWave.w != 0f || record.actualPush.x != 0f || float.IsNaN(record.actualShape.z) || float.IsInfinity(record.actualShape.z) || record.actualShape.z < 0f)
                throw new NotSupportedException("actual preview deformation is not calm/push-off with nonnegative arcK");
            object snapshot = _snapshot.GetValue(null), draw = Field(snapshot, "AppliedDrawSet");
            if (draw == null || (int)Field(snapshot, "LastDrawCameraId") != camera.GetInstanceID())
                throw new NotSupportedException("actual submitted preview camera/draw-set missing");
            Type type = draw.GetType();
            object Private(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(draw)
                ?? throw new NotSupportedException("pinned draw state missing: " + name);
            var counts = Private("_scratchCount") as int[][];
            var pages = Private("_pages") as Matrix4x4[][][][];
            var meshes = Private("_meshes") as Mesh[][][];
            var materials = Private("_materials") as Material[];
            var lods = Private("_lodCount") as int[];
            object layout = type.GetProperty("Layout").GetValue(draw);
            Array groups = (Array)Field(layout, "Groups");
            if (counts == null || pages == null || meshes == null || materials == null || lods == null)
                throw new NotSupportedException("actual draw state type mismatch");
            var rows = new List<CoverageRow>(); int submitted = 0;
            for (int g = 0; g < groups.Length; g++)
            {
                object group = groups.GetValue(g); int species = Convert.ToInt32(Field(group, "Type")), variant = (int)Field(group, "Variant");
                for (int lod = 0; lod < lods[g]; lod++) for (int page = 0, done = 0; done < counts[g][lod]; page++, done += 1023)
                {
                    int n = Mathf.Min(1023, counts[g][lod] - done); Mesh mesh = meshes[species][variant][lod]; Material material = materials[species];
                    if (mesh == null || material == null || material.shader == null || material.shader.name != "DD/World Vegetation")
                        throw new NotSupportedException("actual draw material/mesh is not the audited vegetation source");
                    Bounds local = mesh.bounds;
                    // Grow contracts towards root. With zero wind and push, deform Y is abs(base Y); union both root and reflected Y.
                    Vector3 lo = local.min, hi = local.max; lo = Vector3.Min(lo, Vector3.zero); hi = Vector3.Max(hi, Vector3.zero);
                    hi.y = Mathf.Max(hi.y, Mathf.Abs(lo.y)); lo.y = 0f;
                    Bounds bounds = new Bounds(); bool first = true;
                    for (int i = 0; i < n; i++)
                    {
                        Matrix4x4 matrix = pages[g][lod][page][i];
                        // Placer uses positive diagonal scales and yaw only; fail closed if that changes.
                        if (matrix.m11 <= 0f || matrix.m01 != 0f || matrix.m21 != 0f || matrix.m10 != 0f || matrix.m12 != 0f)
                            throw new NotSupportedException("actual preview transform no longer yaw/positive scale");
                        foreach (int x in new[] { 0, 1 }) foreach (int y in new[] { 0, 1 }) foreach (int z in new[] { 0, 1 })
                        {
                            Vector3 point = matrix.MultiplyPoint3x4(new Vector3(x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z));
                            if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                        }
                    }
                    CoverageRow row = CoverageBounds(camera, bounds); row.drawIndex = rows.Count; row.instanceCount = n;
                    row.meshInstanceId = mesh.GetInstanceID(); row.materialInstanceId = material.GetInstanceID(); row.shader = material.shader.name;
                    row.boundsEnvelope = "CALM_PUSH_OFF_GROW_ROOT_AND_ABS_Y_UNION";
                    rows.Add(row); submitted += n;
                }
            }
            record.actualDrawCount = (int)type.GetProperty("DrawCalls").GetValue(draw); record.actualSubmittedInstances = (int)type.GetProperty("VisibleInstances").GetValue(draw);
            if (record.actualDrawCount != rows.Count || record.actualSubmittedInstances != submitted) throw new NotSupportedException("actual draw counters/state mismatch");
            record.knownVegetationShaderSha256 = ProjectFileHash("Assets/_Prototype/World/Vegetation/Shaders/DD_Vegetation.shader");
            record.draws = rows.ToArray(); record.complete = true; record.status = "ACTUAL_LAST_FULL_RENDER_SUBMITTED_PAGES; calm source-derived conservative bounds";
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
            public string bareVisibilityApi = "Renderer.enabled=false; legacy !enabled/null sharedMaterial skip and exact six ground/air shaders; hidden enabled states restored before category isolates";
            public string priorBareVisibilitySourcePin = "83400db52a68260bd69af67064018fd9b2f776a8";
            public string priorBareVisibilityApi = "forceRenderingOff for bare/bare-repeat; historical v004 raw remains immutable";
            public string currentSourcePinEvidence = "Fresh run pin-start.json + machine-release.json; producerPin DD_TD_SOURCE_PIN; legacy-enabled-v006 preserved; full/category isolation API unchanged";
            public int fullRgbChanged, bareRgbChanged, bareMaskMembershipChanged;
            public bool runtimeEntropyReplayed;
            public string entropyRestoreEvidence = "Typed CPU index/offset/array identity/full RNG state restored+verified; GPU material property readback NOT_SUPPORTED. Material setters are RECONSTRUCTED_WRITE from actual END target dimensions. diagnostic-frames.json records all renders.";
            public ProbeRecord feetGridProbe;
            public RendererRecord[] renderers;
            public HitRecord[] groundRayHits;
            public ColliderRecord actualGroundCollider;
            public ScreenRayTarget cpuScreenRayTarget;
            public ScreenRayRecord[] cpuScreenRays;
            public PhysicalInventory physicalCoverageInventory;
        }

        public void Capture(Camera cam, string name, Vector2 feet, Vector3 ground)
        {
            DumpGrid();
            SelectGroundCollider(feet, ground);
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
                float[] bare = ReplayBareDepth(_bareEntropy, cam, feature, name + "-depth-bare", true);
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
                float[] bareAgain = ReplayBareDepth(_bareEntropy, cam, feature, name + "-depth-bare-repeat");
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
                    actualGroundCollider = _groundColliderRecord, cpuScreenRayTarget = _screenRayTarget, cpuScreenRays = _screenRays, physicalCoverageInventory = _physicalInventory,
                    sourceDepth = feature.SourceDescription, featureListRestored = restored,
                    fullRgbChanged = fullRgbChanged, bareRgbChanged = bareRgbChanged, feetGridProbe = Probe(feet),
                    bareMaskMembershipChanged = bareMembershipChanged, runtimeEntropyReplayed = true });
                feature.WriteSamples(_dir, name);
                if (!restored) throw new InvalidOperationException("Diagnostic renderer feature-list restoration failed");
            }
            finally
            {
                live.Remove(feature);
                bool visibilityRestored = true;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) { visibilityRestored = false; continue; }
                    renderers[i].enabled = records[i].enabled;
                    renderers[i].forceRenderingOff = off[i];
                    visibilityRestored &= renderers[i].enabled == records[i].enabled && renderers[i].forceRenderingOff == off[i];
                }
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
                if (!visibilityRestored) throw new InvalidOperationException("UNKNOWN: diagnostic renderer enabled/forceRenderingOff restoration failed");
                _visibilityRestores++;
                if (!SameList(live, original)) throw new InvalidOperationException("Diagnostic feature-list drift");
            }
        }

        // Same visibility API, enumeration, skip predicate and shader set as the unchanged BareGroundShare.
        private float[] ReplayBareDepth(SsaoEntropy entropy, Camera camera, TdCoverDepthFeature feature, string label, bool rgb = false)
        {
            var hidden = new List<Renderer>();
            try
            {
                foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
                {
                    if (!r.enabled || r.sharedMaterial == null) continue;
                    string shader = r.sharedMaterial.shader.name;
                    if (shader == "DD/GroundTiles" || shader == "DD/GroundMoss" || shader == "DD/GroundBlend" || shader == "DD/GroundTerrain" || shader == "DD/FogSheet" || shader == "DD/ShaftCard")
                    {
                        r.enabled = false;
                        hidden.Add(r);
                    }
                }
                return ReplayDepth(entropy, camera, feature, label, rgb);
            }
            finally
            {
                foreach (Renderer r in hidden) if (r != null) r.enabled = true;
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
                if (name.EndsWith("-depth-full", StringComparison.Ordinal)) CaptureScreenRays(cam, name);
                feature.PrepareSamples(name, _frames.Count);
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
                feature.ReadSamples(_dir, values);
                if (name.EndsWith("-depth-full", StringComparison.Ordinal)) _physicalInventory = CapturePhysicalInventory(cam);
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

        private const string ComputeAsset = "Assets/_Art/Editor/TdCoverMsaaReadback.compute";
        private ComputeShader _compute;
        private ComputeBuffer _samples;
        private int _kernel, _ordinal;
        private string _sampleLabel, _unsupported, _computeSha;
        private bool _requested, _dispatched;
        private SampleRecord _pendingSample;
        private readonly List<SampleRecord> _sampleRecords = new List<SampleRecord>();
        private MappingRecord _mapping;
        [Serializable] private sealed class MappingRecord
        {
            public string derivation = "URP14.CopyDepthPass.scaleBias";
            public float[] viewportScale;
            public bool yFlip, normalizedToReadPixelsLowerLeft = true;
            public float[] scaleBias;
            public string lowerLeftToBlitUv = "direct texel center / output size is raster-normalized; no extra platform UV flip; then actual CopyDepth scaleBias";
        }
        [Serializable] private sealed class SampleRecord
        {
            public string label, file, sha256, sourceDepthFormat;
            public int frameOrdinal, byteCount, writtenCount, sentinelFailures, sourceInstanceId, sourceWidth, sourceHeight, sourceMsaa;
            public int farthestBitwiseMismatchCount;
            public bool sameRenderDepthAttachment, sourceCreated, sourceBindMs, cameraRenderReturned, readbackCompleted;
            public string commandBufferSubmitPath = "URP14 ScriptableRenderer.ExecuteRenderPass: Execute->context.ExecuteCommandBuffer(same cmd)->Clear; camera context.Submit before Camera.Render returns";
            public string readbackMethod = "ComputeBuffer.GetData(Vector4[129600]) returned, then all-pixel sentinel/finite checks";
            public MappingRecord mapping;
        }
        [Serializable] private sealed class SampleSidecar
        {
            public string schema = "DD_MSAA4_SAMPLES_V1", status, reason, producerPin, pose;
            public int width = 480, height = 270, sampleCount = 4;
            public string byteOrder = "little", layout = "lower-left-y-x-float4";
            public string computeSourcePath = "prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Editor/TdCoverMsaaReadback.compute", computeSourceSha256;
            public bool supportsComputeShaders;
            public int supportsMultisampledTextures;
            public MappingRecord mapping;
            public SampleRecord[] records;
            public string limits = "Four actual depth samples only; mismatched reduction pixels UNKNOWN. ZWriteOff/transparent/mixed exclusion NOTRECORDED; no new physical bare claim.";
        }
        private void Unsupported(Exception error) => _unsupported = "NOT_SUPPORTED: " + error.GetType().Name + ": " + error.Message;
        public void PrepareSamples(string label, int ordinal)
        {
            _requested = label.EndsWith("-depth-full", StringComparison.Ordinal) || label.EndsWith("-depth-bare", StringComparison.Ordinal)
                || label.EndsWith("-depth-category-1", StringComparison.Ordinal) || label.EndsWith("-depth-full-repeat", StringComparison.Ordinal)
                || label.EndsWith("-depth-bare-repeat", StringComparison.Ordinal);
            _dispatched = false; _pendingSample = null; _sampleLabel = label; _ordinal = ordinal;
            if (!_requested || _unsupported != null) return;
            try
            {
                if (!BitConverter.IsLittleEndian || !SystemInfo.supportsComputeShaders || SystemInfo.supportsMultisampledTextures <= 0)
                    throw new NotSupportedException("actual compute/MS texture capability unavailable");
                if (_compute == null)
                {
                    ComputeShader asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAsset);
                    if (asset == null) throw new NotSupportedException("pinned compute asset missing or import failed");
                    _compute = Object.Instantiate(asset); _compute.hideFlags = HideFlags.HideAndDontSave;
                    _kernel = _compute.FindKernel("ReadMsaa4");
                    string path = Path.Combine(Application.dataPath, "..", ComputeAsset);
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        _computeSha = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                    _samples = new ComputeBuffer(480 * 270, 16, ComputeBufferType.Structured);
                }
                var sentinel = new Vector4[480 * 270];
                for (int i = 0; i < sentinel.Length; i++) sentinel[i] = new Vector4(-1f, -1f, -1f, -1f);
                _samples.SetData(sentinel);
            }
            catch (Exception error) { Unsupported(error); }
        }
        private void DispatchSamples(RTHandle source, ref RenderingData data)
        {
            if (!_requested || _unsupported != null) return;
            try
            {
                RenderTexture rt = source.rt;
                if (!rt.IsCreated() || rt.width != 480 || rt.height != 270 || rt.antiAliasing != 4 || !rt.bindTextureMS
                    || rt.depthStencilFormat != GraphicsFormat.D32_SFloat_S8_UInt)
                    throw new NotSupportedException("actual source is not created 480x270 bindMS D32S8 MSAA4");
                FieldInfo nativeField = typeof(ScriptableRenderer).GetField("useRenderPassEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
                if (nativeField == null || nativeField.FieldType != typeof(bool) || (bool)nativeField.GetValue(data.cameraData.renderer))
                    throw new NotSupportedException("actual renderer native render-pass mode unsupported for this compute append");
                // RenderingData.commandBuffer is internal in pinned URP14. Read the exact existing object, never substitute or execute/clear it.
                FieldInfo field = typeof(RenderingData).GetField("commandBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(CommandBuffer)) throw new NotSupportedException("pinned URP14 commandBuffer field differs");
                CommandBuffer cmd = field.GetValue(data) as CommandBuffer;
                if (cmd == null) throw new NotSupportedException("actual URP commandBuffer missing");
                Vector2 scale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
                bool flip = data.cameraData.IsHandleYFlipped(source) != data.cameraData.IsHandleYFlipped(_output);
                Vector4 bias = flip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);
                if (float.IsNaN(scale.x) || float.IsNaN(scale.y) || float.IsInfinity(scale.x) || float.IsInfinity(scale.y)
                    || scale.x <= 0f || scale.y <= 0f || scale.x > 1f || scale.y > 1f) throw new NotSupportedException("invalid actual viewport scale");
                _mapping = new MappingRecord { viewportScale = new[] { scale.x, scale.y }, yFlip = flip, scaleBias = new[] { bias.x, bias.y, bias.z, bias.w } };
                _pendingSample = new SampleRecord { label = _sampleLabel, frameOrdinal = _ordinal,
                    sourceInstanceId = rt.GetInstanceID(), sourceCreated = rt.IsCreated(), sourceWidth = rt.width, sourceHeight = rt.height,
                    sourceMsaa = rt.antiAliasing, sourceBindMs = rt.bindTextureMS, sourceDepthFormat = rt.depthStencilFormat.ToString(),
                    sameRenderDepthAttachment = true, mapping = _mapping };
                cmd.SetComputeVectorParam(_compute, "_ScaleBias", bias);
                cmd.SetComputeIntParam(_compute, "_UvStartsAtTop", SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
                cmd.SetComputeTextureParam(_compute, _kernel, "_SourceDepth", source.nameID, 0, RenderTextureSubElement.Depth);
                cmd.SetComputeBufferParam(_compute, _kernel, "_Samples", _samples);
                // Appended after CopyDepth to its SAME actual command buffer; ScriptableRenderer executes it after this pass.
                cmd.DispatchCompute(_compute, _kernel, 60, 34, 1);
                _dispatched = true;
            }
            catch (Exception error) { Unsupported(error); }
        }
        public void ReadSamples(string directory, float[] resolved)
        {
            if (!_requested || _unsupported != null) return;
            try
            {
                if (!_dispatched || _pendingSample == null) throw new NotSupportedException("same-render sample dispatch missing");
                _pendingSample.cameraRenderReturned = true;
                var samples = new Vector4[480 * 270]; _samples.GetData(samples);
                _pendingSample.readbackCompleted = true;
                int written = 0, failures = 0, mismatch = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    bool valid = true;
                    float farthest = SystemInfo.usesReversedZBuffer ? 1f : 0f;
                    for (int j = 0; j < 4; j++)
                    {
                        float value = samples[i][j];
                        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f) valid = false;
                        farthest = SystemInfo.usesReversedZBuffer ? Mathf.Min(farthest, value) : Mathf.Max(farthest, value);
                    }
                    if (valid) written++; else failures++;
                    // Strict raw bit comparison; no epsilon, fit, flip retry or resolved-value sample fabrication.
                    if (!valid || BitConverter.ToInt32(BitConverter.GetBytes(farthest), 0) != BitConverter.ToInt32(BitConverter.GetBytes(resolved[i]), 0)) mismatch++;
                }
                _pendingSample.writtenCount = written; _pendingSample.sentinelFailures = failures;
                _pendingSample.farthestBitwiseMismatchCount = mismatch;
                if (failures != 0 || written != 480 * 270) throw new NotSupportedException("sentinel/finite/sample-count proof failed");
                string file = _sampleLabel + ".msaa4-float4", path = Path.Combine(directory, file);
                using (var writer = new BinaryWriter(File.Create(path)))
                    foreach (Vector4 sample in samples) for (int j = 0; j < 4; j++) writer.Write(sample[j]);
                _pendingSample.file = file; _pendingSample.byteCount = checked((int)new FileInfo(path).Length);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var input = File.OpenRead(path)) _pendingSample.sha256 = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (_pendingSample.byteCount != 2073600) throw new NotSupportedException("actual sample byte count differs");
                _sampleRecords.Add(_pendingSample);
            }
            catch (Exception error) { Unsupported(error); }
        }
        public void WriteSamples(string directory, string pose)
        {
            string pin = Environment.GetEnvironmentVariable("DD_TD_SOURCE_PIN");
            bool pinValid = pin != null && System.Text.RegularExpressions.Regex.IsMatch(pin, "^[0-9a-f]{40}$");
            bool recorded = _unsupported == null && _sampleRecords.Count == 5 && pinValid;
            var sidecar = new SampleSidecar { status = recorded ? "RECORDED" : "NOT_SUPPORTED", reason = recorded ? "" : (_unsupported ?? "missing required records or DD_TD_SOURCE_PIN"),
                pose = pose, producerPin = pin, computeSourceSha256 = _computeSha,
                supportsComputeShaders = SystemInfo.supportsComputeShaders, supportsMultisampledTextures = SystemInfo.supportsMultisampledTextures,
                mapping = _mapping, records = _sampleRecords.ToArray() };
            File.WriteAllText(Path.Combine(directory, pose + "-msaa-samples.json"), JsonUtility.ToJson(sidecar, true));
        }

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
            if (_samples != null) { _samples.Dispose(); _samples = null; }
            CoreUtils.Destroy(_compute); _compute = null;
            CoreUtils.Destroy(_material);
            _material = null;
        }
        private sealed class DepthPass : ScriptableRenderPass
        {
            private readonly TdCoverDepthFeature _owner;
            private CopyDepthPass _copy;
            private RTHandle _source;
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
                _source = source;
            }
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data) => ConfigureTarget(_owner._output);
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                if (_copy == null) throw new InvalidOperationException("UNKNOWN: depth copy setup missing");
                _copy.Execute(context, ref data);
                _owner.DispatchSamples(_source, ref data);
                _owner.CopiedFrames++;
            }
        }
    }
}
