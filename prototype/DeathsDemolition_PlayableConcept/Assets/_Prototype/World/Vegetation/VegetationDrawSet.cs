using System;
using DD.Prototype.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The drawable vegetation of one world: species meshes (AP-031 kit, or vc1 placeholders), per-species materials,
    /// the placed layout and its per-camera cull / draw. One class for the runtime (<see cref="VegetationSystem"/>) and the
    /// edit-mode preview (<see cref="VegetationPreview"/>): both place with <see cref="Build"/> — same field, seed, tier scale
    /// and mesh heights → the same instances — and draw with <see cref="Render"/>.
    ///
    /// Render: CPU frustum culling per chunk (cached Plane[6]), LOD 0/1/2 by chunk distance (2 m dither cross-fade in the shader),
    /// distance density (first N of each hash-sorted chunk range; the shader shrinks each instance by the same hash), camera-height
    /// density, Graphics.DrawMeshInstanced per group × LOD from build-time 1023-matrix pages. 0 B per call.
    /// Build also writes the ground-cover globals (_DD_VegCoverTex: grass cover 0..1 per 0.5 m) the ground shader uses to darken
    /// the soil and occlude its specular under grass (LOOK-BIBLE principle 1, P-05 note §2: SS–Soil ΔL* ≥ 8).
    /// </summary>
    public sealed class VegetationDrawSet : IDisposable
    {
        public const int MaxPerDraw = 1023;
        /// <summary>Shader grow band: an instance scales 0 → 1 over this much hash above the density threshold.</summary>
        public const float FadeBand = 0.06f;

        public static readonly int FadeId = Shader.PropertyToID("_DD_VegFade");
        public static readonly int NoiseTexId = Shader.PropertyToID("_DD_VegNoiseTex");
        public static readonly int CoverTexId = Shader.PropertyToID("_DD_VegCoverTex");
        public static readonly int CoverRectId = Shader.PropertyToID("_DD_VegCoverRect");
        public static readonly int GroundId = Shader.PropertyToID("_DD_VegGround");
        private static readonly int MeshHeightId = Shader.PropertyToID("_VegMeshHeight");
        private static readonly int LodId = Shader.PropertyToID("_VegLod");

        private static Texture2D s_noise;

        private readonly VegetationRecipe _recipe;
        private readonly VegetationKit _kit;

        private Mesh[][][] _meshes;          // [type][variant][lod]
        private bool[][][] _ownedMesh;
        private float[][] _nominal;          // [type][variant]
        private Material[] _materials;       // [type]
        private float[] _farKeep;            // [type]
        private bool[] _fromKit;             // [type]

        private VegetationDensityField _field;
        private VegetationLayout _layout;
        private int _builtTier = -1;
        private int[] _lodCount;             // [group]
        private MaterialPropertyBlock[][] _blocks;   // [group][lod]
        private Matrix4x4[][][][] _pages;    // [group][lod][page][MaxPerDraw]
        private int[][] _scratchCount;
        private readonly Plane[] _planes = new Plane[6];
        private Texture2D _cover;

        public VegetationDrawSet(VegetationRecipe recipe, VegetationKit kit)
        {
            _recipe = recipe != null ? recipe : VegetationRecipe.Load();
            _kit = kit;
        }

        public VegetationRecipe Recipe => _recipe;
        public VegetationLayout Layout => _layout;
        public VegetationDensityField Field => _field;
        public int BuiltTier => _builtTier;
        public bool IsBuilt => _layout != null;
        public int VisibleInstances { get; private set; }
        public int DrawCalls { get; private set; }
        public int ChunksVisible { get; private set; }
        public Texture2D CoverTexture => _cover;

        /// <summary>[type][variant] LOD0 mesh heights (m) the placement scales against.</summary>
        public float[][] NominalHeights
        {
            get
            {
                EnsureRenderData();
                return _nominal;
            }
        }

        public Mesh MeshOf(VegetationType type, int variant, int lod)
        {
            EnsureRenderData();
            Mesh[][] t = _meshes[(int)type];
            Mesh[] v = t[Mathf.Clamp(variant, 0, t.Length - 1)];
            return v[Mathf.Clamp(lod, 0, v.Length - 1)];
        }

        public int LodLevels(VegetationType type)
        {
            EnsureRenderData();
            return _meshes[(int)type][0].Length;
        }

        public bool UsesKit(VegetationType type)
        {
            EnsureRenderData();
            return _fromKit[(int)type];
        }

        public Material MaterialOf(VegetationType type)
        {
            EnsureRenderData();
            return _materials[(int)type];
        }

        // ------------------------------------------------------------------ build (allocates; load / tier change only)

        public void Build(VegetationDensityField field, int tier, int seed)
        {
            _field = field;
            if (field == null)
            {
                Clear();
                return;
            }

            EnsureRenderData();
            EnsureNoise();
            _layout = VegetationPlacer.Place(field, _recipe, _recipe.TierDensity(tier), seed, _nominal);
            _builtTier = tier;
            BuildDrawData();
            BuildCover(tier);
        }

        public void Clear()
        {
            _field = null;
            _layout = null;
            _builtTier = -1;
            VisibleInstances = DrawCalls = ChunksVisible = 0;
            Shader.SetGlobalVector(GroundId, Vector4.zero);
            Kill(_cover);
            _cover = null;
        }

        public void Dispose()
        {
            Clear();
            if (_meshes != null)
            {
                // Null-safe: a build that threw half way (a broken kit reference) leaves later types unset.
                for (int t = 0; t < _meshes.Length; t++)
                {
                    if (_meshes[t] == null || _ownedMesh[t] == null) continue;
                    for (int v = 0; v < _meshes[t].Length; v++)
                    {
                        if (_meshes[t][v] == null || _ownedMesh[t][v] == null) continue;
                        for (int l = 0; l < _meshes[t][v].Length; l++)
                        {
                            if (_ownedMesh[t][v][l]) Kill(_meshes[t][v][l]);
                        }
                    }
                }
            }

            if (_materials != null)
            {
                for (int t = 0; t < _materials.Length; t++) Kill(_materials[t]);
            }

            _meshes = null;
            _materials = null;
        }

        public static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        private void EnsureRenderData()
        {
            if (_meshes != null) return;
            int types = VegetationTypes.Count;
            _meshes = new Mesh[types][][];
            _ownedMesh = new bool[types][][];
            _nominal = new float[types][];
            _materials = new Material[types];
            _farKeep = new float[types];
            _fromKit = new bool[types];
            for (int t = 0; t < types; t++)
            {
                VegetationRecipe.Species sp = _recipe.Get((VegetationType)t);
                _farKeep[t] = sp.farKeep;
                VegetationKit.Entry kit = _kit != null ? _kit.Find((VegetationType)t) : null;
                int variants = Mathf.Max(1, sp.variants);
                int lods = VegetationMeshFactory.LodCount(sp);
                if (kit != null && kit.HasModels)
                {
                    lods = 3;
                    for (int v = 0; v < kit.models.Length; v++) lods = Mathf.Min(lods, Mathf.Max(1, VegetationKit.LodCount(kit.models[v])));
                }
                else if (kit != null && kit.HasCards)
                {
                    lods = 2;
                }

                _fromKit[t] = kit != null;
                _meshes[t] = new Mesh[variants][];
                _ownedMesh[t] = new bool[variants][];
                _nominal[t] = new float[variants];
                for (int v = 0; v < variants; v++)
                {
                    _meshes[t][v] = new Mesh[lods];
                    _ownedMesh[t][v] = new bool[lods];
                    for (int l = 0; l < lods; l++)
                    {
                        Mesh m = null;
                        if (kit != null && kit.HasModels) m = VegetationKit.ExtractLod(kit.models[v % kit.models.Length], l);
                        else if (kit != null && kit.HasCards) m = VegetationMeshFactory.BuildCards(sp, kit.cards, v, l);
                        if (m == null) m = VegetationMeshFactory.Build(sp, v, l);
                        m.hideFlags = HideFlags.HideAndDontSave;
                        _ownedMesh[t][v][l] = true;
                        _meshes[t][v][l] = m;
                    }

                    _nominal[t][v] = Mathf.Max(1e-3f, _meshes[t][v][0].bounds.max.y);
                }

                _materials[t] = MakeMaterial(sp, kit);
            }
        }

        private Material MakeMaterial(VegetationRecipe.Species sp, VegetationKit.Entry kit)
        {
            bool atlas = kit != null && kit.material != null;
            Material mat = atlas ? new Material(kit.material) : new Material(PrototypeShaders.Vegetation);
            mat.name = "DD_Veg_" + VegetationTypes.Codes[(int)sp.type];
            mat.enableInstancing = true;
            mat.hideFlags = HideFlags.HideAndDontSave;
            bool atlasColour = atlas && kit.atlasCarriesColour;
            mat.SetColor("_RootColor", atlasColour ? Color.white : sp.rootColor);
            mat.SetColor("_TipColor", atlasColour ? Color.white : sp.tipColor);
            mat.SetColor("_DryColor", sp.dryColor);
            mat.SetColor("_AccentColor", sp.accentColor);
            mat.SetColor("_AccentColorB", sp.accentColorB);
            mat.SetColor("_TransColor", _recipe.translucencyTone);
            mat.SetFloat("_Cutoff", _recipe.alphaCutoff);
            mat.SetFloat("_Translucency", sp.translucency);
            mat.SetFloat("_DryFraction", atlasColour ? 0f : sp.dryFraction);   // the atlas carries its own dry blades
            mat.SetFloat("_HueJitter", sp.hueJitterDegrees);
            mat.SetFloat("_ValueJitter", sp.valueJitter);
            mat.SetFloat("_SatJitter", sp.saturationJitter);
            mat.SetFloat("_SatCap", sp.saturationCap);
            mat.SetFloat("_FarKeep", sp.farKeep);
            if (!atlas) mat.SetFloat("_BumpScale", 0f);   // placeholders: no normal map, no tangents
            return mat;
        }

        private void BuildDrawData()
        {
            VegetationLayout.Group[] groups = _layout.Groups;
            int g = groups.Length;
            _lodCount = new int[g];
            _blocks = new MaterialPropertyBlock[g][];
            _pages = new Matrix4x4[g][][][];
            _scratchCount = new int[g][];
            float d1 = _recipe.lod1Distance, d2 = _recipe.lod2Distance, hw = _recipe.lodCrossFade * 0.5f;
            for (int k = 0; k < g; k++)
            {
                VegetationLayout.Group group = groups[k];
                int t = (int)group.Type;
                int lods = _meshes[t][group.Variant].Length;
                _lodCount[k] = lods;
                _blocks[k] = new MaterialPropertyBlock[lods];
                _pages[k] = new Matrix4x4[lods][][];
                _scratchCount[k] = new int[lods];
                for (int l = 0; l < lods; l++)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetFloat(MeshHeightId, _nominal[t][group.Variant]);
                    float lo = l == 0 ? 0f : l == 1 ? d1 : d2;
                    float hi = l == lods - 1 ? 100000f : l == 0 ? d1 : d2;
                    block.SetVector(LodId, new Vector4(lo, hi, hw, l));
                    _blocks[k][l] = block;
                    // Every page a camera could ever need, allocated here: the per-frame path never allocates.
                    int pages = Mathf.Max(1, (group.Count + MaxPerDraw - 1) / MaxPerDraw);
                    _pages[k][l] = new Matrix4x4[pages][];
                    for (int p = 0; p < pages; p++) _pages[k][l][p] = new Matrix4x4[MaxPerDraw];
                }
            }
        }

        // ------------------------------------------------------------------ ground cover (principle 1)

        private void BuildCover(int tier)
        {
            VegetationDensityField f = _field;
            int w = Mathf.Max(1, (f.Width + 1) / 2), h = Mathf.Max(1, (f.Height + 1) / 2);
            var data = new byte[w * h];
            float tierScale = _recipe.TierDensity(tier);
            var area = new float[VegetationTypes.Count];
            for (int t = 0; t < area.Length; t++)
            {
                var type = (VegetationType)t;
                bool foliage = type != VegetationType.Moss && type != VegetationType.LeafLitter;
                float r = _recipe.Get(type).footprint * 0.5f;
                area[t] = foliage ? Mathf.PI * r * r * tierScale : 0f;
            }

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dz = 0; dz < 2; dz++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int cx = x * 2 + dx, cz = z * 2 + dz;
                        if (cx >= f.Width || cz >= f.Height) continue;
                        int i = cz * f.Width + cx;
                        float c = 0f;
                        for (int t = 0; t < area.Length; t++)
                        {
                            if (area[t] > 0f && f.Layers[t][i] != 0) c += f.Density(t, i) * area[t];
                        }

                        sum += Mathf.Clamp01(c);
                        n++;
                    }

                    data[z * w + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(sum / Mathf.Max(1, n) * 255f), 0, 255);
                }
            }

            Kill(_cover);
            _cover = new Texture2D(w, h, TextureFormat.R8, false, true)
            {
                name = "DD_VegGroundCover", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            _cover.SetPixelData(data, 0);
            _cover.Apply(false, false);
            float sx = w * f.Cell * 2f, sz = h * f.Cell * 2f;
            Shader.SetGlobalTexture(CoverTexId, _cover);
            Shader.SetGlobalVector(CoverRectId, new Vector4(f.Origin.x, f.Origin.y, 1f / sx, 1f / sz));
            Shader.SetGlobalVector(GroundId, new Vector4(_recipe.groundAlbedoUnderGrass, _recipe.groundOcclusionUnderGrass, _recipe.groundSmoothnessUnderGrass, 1f));
        }

        /// <summary>Cover 0..1 at a world point from the CPU data (tests / bare-ground readouts); 0 outside or unbuilt.</summary>
        public float CoverAt(float x, float z)
        {
            if (_cover == null || _field == null) return 0f;
            float sx = _cover.width * _field.Cell * 2f, sz = _cover.height * _field.Cell * 2f;
            float u = (x - _field.Origin.x) / sx, v = (z - _field.Origin.y) / sz;
            if (u < 0f || v < 0f || u > 1f || v > 1f) return 0f;
            return _cover.GetPixelBilinear(u, v).r;
        }

        // ------------------------------------------------------------------ wind noise (shared, deterministic)

        public static void EnsureNoise()
        {
            if (s_noise == null)
            {
                const int size = 64, period = 8;
                var data = new byte[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float v = 0.65f * PeriodicNoise(x, y, size, period, 31) + 0.35f * PeriodicNoise(x, y, size, period * 2, 32);
                        data[y * size + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                    }
                }

                s_noise = new Texture2D(size, size, TextureFormat.R8, false, true)
                {
                    name = "DD_VegWindNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
                };
                s_noise.SetPixelData(data, 0);
                s_noise.Apply(false, true);
            }

            Shader.SetGlobalTexture(NoiseTexId, s_noise);
        }

        /// <summary>Tileable value noise: a period × period lattice over the texture, smoothstep-blended.</summary>
        private static float PeriodicNoise(int x, int y, int size, int period, int salt)
        {
            float fx = x * period / (float)size, fy = y * period / (float)size;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = VegetationHash.Cell01(x0 % period, y0 % period, salt);
            float b = VegetationHash.Cell01((x0 + 1) % period, y0 % period, salt);
            float c = VegetationHash.Cell01(x0 % period, (y0 + 1) % period, salt);
            float d = VegetationHash.Cell01((x0 + 1) % period, (y0 + 1) % period, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        // ------------------------------------------------------------------ per camera: cull + draw (0 B)

        /// <summary>Culls the chunks against <paramref name="cam"/> and submits the visible instances to it. 0 B.</summary>
        public void Render(Camera cam, int layer)
        {
            VisibleInstances = 0;
            DrawCalls = 0;
            ChunksVisible = 0;
            if (_layout == null || _materials == null || cam == null) return;

            GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix, _planes);
            Vector3 cp = cam.transform.position;
            VegetationRecipe r = _recipe;
            float ground = _field != null ? _field.DefaultGroundY : 0f;
            float heightFactor = VegetationLayout.HeightFactor(cp.y - ground, r.referenceCameraHeight, r.minHeightDensity);
            Shader.SetGlobalVector(FadeId, new Vector4(r.fadeStart, r.fadeEnd, heightFactor, FadeBand));

            VegetationLayout.Group[] groups = _layout.Groups;
            for (int g = 0; g < groups.Length; g++)
            {
                int[] counts = _scratchCount[g];
                for (int l = 0; l < counts.Length; l++) counts[l] = 0;
            }

            float d1 = r.lod1Distance, d2 = r.lod2Distance, hw = r.lodCrossFade * 0.5f;
            int chunks = _layout.ChunkCount;
            for (int c = 0; c < chunks; c++)
            {
                if (!_layout.ChunkUsed[c]) continue;
                Bounds b = _layout.ChunkBounds[c];
                if (!GeometryUtility.TestPlanesAABB(_planes, b)) continue;
                ChunksVisible++;
                VegetationLayout.DistanceRange(b, cp, out float near, out float far);
                for (int g = 0; g < groups.Length; g++)
                {
                    VegetationLayout.Group group = groups[g];
                    int count = group.ChunkCount[c];
                    if (count == 0) continue;
                    float density = VegetationLayout.DensityAt(near, r.fadeStart, r.fadeEnd, _farKeep[(int)group.Type]) * heightFactor;
                    int n = VegetationLayout.DrawCount(count, density, FadeBand);
                    int start = group.ChunkStart[c];
                    int lods = _lodCount[g];
                    if (near < d1 + hw) Append(g, 0, group.Matrices, start, n);
                    if (lods >= 2 && far > d1 - hw && (lods == 2 || near < d2 + hw)) Append(g, 1, group.Matrices, start, n);
                    if (lods >= 3 && far > d2 - hw) Append(g, 2, group.Matrices, start, n);
                }
            }

            for (int g = 0; g < groups.Length; g++)
            {
                VegetationLayout.Group group = groups[g];
                Mesh[] meshes = _meshes[(int)group.Type][group.Variant];
                Material material = _materials[(int)group.Type];
                for (int l = 0; l < _lodCount[g]; l++)
                {
                    int count = _scratchCount[g][l];
                    if (count == 0) continue;
                    Matrix4x4[][] pages = _pages[g][l];
                    MaterialPropertyBlock block = _blocks[g][l];
                    for (int p = 0, done = 0; done < count; p++, done += MaxPerDraw)
                    {
                        int n = count - done < MaxPerDraw ? count - done : MaxPerDraw;
                        Graphics.DrawMeshInstanced(meshes[l], 0, material, pages[p], n, block, ShadowCastingMode.Off, true, layer, cam, LightProbeUsage.Off);
                        DrawCalls++;
                    }

                    VisibleInstances += count;
                }
            }
        }

        private void Append(int g, int lod, Matrix4x4[] source, int start, int n)
        {
            int count = _scratchCount[g][lod];
            Matrix4x4[][] pages = _pages[g][lod];
            int capacity = pages.Length * MaxPerDraw;
            if (count + n > capacity) n = capacity - count;   // cannot happen (pages cover the whole group); never allocate here
            while (n > 0)
            {
                int page = count / MaxPerDraw, offset = count - page * MaxPerDraw;
                int take = MaxPerDraw - offset < n ? MaxPerDraw - offset : n;
                Array.Copy(source, start, pages[page], offset, take);
                start += take;
                count += take;
                n -= take;
            }

            _scratchCount[g][lod] = count;
        }
    }
}
