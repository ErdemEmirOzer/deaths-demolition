using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-107B (R4) · the wall buy art into the game: AP-040 v001 (the D1 boards V01 / V02, the D3 shrine, the shared
    /// prop atlas, the ten D2 weapon silhouette decals) and the AP-042 wall buy HUD sprites (v001 frames / price tag / icons,
    /// v002 discount seal, plus the 64 px icon layers derived in WP-107B).
    ///
    /// Source files are copied verbatim (sha256 against the delivery SHA256SUMS; listed in <see cref="ProvenancePath"/> and
    /// <see cref="HudIntakePath"/>, which this builder re-checks first). They sit outside the <c>Assets/_Art</c> import contract,
    /// so this builder owns every importer setting:
    ///   1. shared atlas (<see cref="SharedRoot"/>, also read by S-05's WP-108): BaseColor / Emission sRGB, MaskMap linear,
    ///      Normal as an OpenGL normal map; mips + streaming mips, Clamp, ≤ 2048, Standalone BC7 (the normal map BC5);
    ///   2. M_AP040_Shared (URP Lit: base, normal, MaskMap → metallic R / smoothness A and occlusion G) and M_AP040_Glow (the same
    ///      plus the emission atlas) — the integration JSON's "props" / "glow" materials;
    ///   3. the station FBX: file scale, no axis bake, hierarchy preserved (the SOCKET_ empties), no collider / animation /
    ///      camera / light, the FBX slots M_AP040_Shared / M_AP040_Glow remapped by name onto (2) — the proxy's
    ///      AP040_work_stone slot onto M_AP040_Shared too, so no material is embedded (the proxy never renders);
    ///   4. decals: sRGB, alpha is transparency, Clamp, Bilinear, mips, ≤ 512, BC7; one URP Unlit transparent material each;
    ///   5. HUD sprites: <c>UiArtImporter</c> keeps the UI class (Sprite, no mips, ≤ 1024 …); this adds the AP-042 nine-slice
    ///      borders, a Full Rect mesh and the Standalone BC7 override;
    ///   6. <see cref="WallBuyStationLibrary"/> at <see cref="LibraryPath"/>: standard = board V02, premium = shrine, wallMount =
    ///      board V01, the measured front depth, and one decal entry per classic weapon (runtime id from the Arsenal).
    /// Idempotent: an importer is reimported only when a setting differs. Throws on any contract break (missing socket,
    /// proxy, LOD level, decal, material, or a sha256 that no longer matches the provenance).
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtAp040WallBuyBuilder.BuildForBatch</c>.
    /// </summary>
    public static class ArtAp040WallBuyBuilder
    {
        public const string LogPrefix = "[ArtAp040WallBuyBuilder] ";
        public const string Package = "AP-040";
        public const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        public const string SharedRoot = "Assets/_Prototype/Art/AP040/Shared";
        public const string Root = "Assets/_Prototype/Weapons/WallBuy/Art/AP040";
        public const string ProvenancePath = Root + "/AP040-wallbuy.provenance.json";
        public const string LibraryPath = "Assets/_Prototype/Weapons/WallBuy/Resources/" + WallBuyStationLibrary.ResourcePath + ".asset";
        public const string HudFolder = "Assets/_Prototype/UI/Art/Resources/UI/Hud";
        public const string HudIntakePath = "Assets/_Prototype/UI/Art/AP042/ap042_wallbuy.json";

        /// <summary>FBX material slot names (AP-040 build_stations): the props slot, the emissive slot, the collision proxy's slot.</summary>
        public const string SharedSlot = "M_AP040_Shared", GlowSlot = "M_AP040_Glow", ProxySlot = "AP040_work_stone";

        public const string StandardKey = "board_V02", PremiumKey = "shrine_V01", WallMountKey = "board_V01";
        public const string LodSuffix = "_LOD";
        public const int LodCount = 3;
        public const int SharedMaxSize = 2048, DecalMaxSize = 512, HudMaxSize = 1024;

        /// <summary>Glow slot emission: the emission atlas × this (modest; S-07's bloom decides the final read).</summary>
        public const float GlowIntensity = 1.5f;

        /// <summary>AP-040 display scales: D1 boards 0.68 (weapon) / 0.85 m (decal quad); D3 shrine 0.80 / 0.82 m.</summary>
        public const float BoardWeaponScale = 0.68f, BoardDecalSize = 0.85f, ShrineWeaponScale = 0.80f, ShrineDecalSize = 0.82f;

        public static readonly float[] LodScreenHeights = { 0.40f, 0.15f, 0.02f };
        public static readonly string[] SharedMaps = { "BaseColor", "Normal", "MaskMap", "Emission" };

        /// <summary>The ten classic keys with a D2 decal (the two Specials have none).</summary>
        public static readonly string[] DecalWeapons = { "ar_1", "smg_1", "sg_1", "ar_2", "smg_2", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1" };

        public static string ModelPath(string key) => Root + "/Models/AP040_wallbuy_" + key + ".fbx";
        public static string SharedTexturePath(string map) => SharedRoot + "/Textures/T_AP040_Shared_" + map + ".png";
        public static string SharedMaterialPath => SharedRoot + "/Materials/" + SharedSlot + ".mat";
        public static string GlowMaterialPath => SharedRoot + "/Materials/" + GlowSlot + ".mat";
        public static string DecalTexturePath(string weapon) => Root + "/Decals/T_wallbuy_" + weapon + "_Decal.png";
        public static string DecalMaterialPath(string weapon) => Root + "/Materials/M_WallBuyDecal_" + weapon + ".mat";
        public static string HudSpritePath(string name) => HudFolder + "/" + name + ".png";

        // ------------------------------------------------------------------ provenance DTOs (JsonUtility; the gate test reads them too)

        [Serializable]
        public sealed class ProvenanceFile
        {
            public string repo, source, sha256, role;
            public long bytes;
        }

        [Serializable]
        public sealed class Provenance
        {
            public string schema, package, version, manifest_sha256;
            public ProvenanceFile[] files;
        }

        [Serializable]
        public sealed class HudIntakeFile
        {
            public string name, repo, source, sha256, role, derived_from;
            public long bytes;
            public int[] size, border_lbrt, text_safe_top_left;
        }

        [Serializable]
        public sealed class HudIntake
        {
            public string schema, package;
            public HudIntakeFile[] files;
        }

        public static Provenance ReadProvenance() => JsonUtility.FromJson<Provenance>(File.ReadAllText(ProvenancePath));
        public static HudIntake ReadHudIntake() => JsonUtility.FromJson<HudIntake>(File.ReadAllText(HudIntakePath));

        /// <summary>Lower-case hex sha256 of a file (project-relative path).</summary>
        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ entry

        [MenuItem("DD/Arsenal/Import AP-040 Wall Buy Stations (WP-107B)", priority = 333)]
        public static void BuildForBatch()
        {
            Provenance provenance = ReadProvenance();
            HudIntake hud = ReadHudIntake();
            var errors = new List<string>();
            CheckSources(provenance, hud, errors);
            Fail(errors, "source files");

            EnsureImported(provenance, hud);

            foreach (string map in SharedMaps)
            {
                bool normal = map == "Normal";
                ImportTexture(SharedTexturePath(map), normal ? TextureImporterType.NormalMap : TextureImporterType.Default,
                    map == "BaseColor" || map == "Emission", false, SharedMaxSize, true, normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7);
            }

            foreach (string weapon in DecalWeapons)
            {
                ImportTexture(DecalTexturePath(weapon), TextureImporterType.Default, true, true, DecalMaxSize, false, TextureImporterFormat.BC7);
            }

            Shader lit = FindShader(LitShaderName);
            Shader unlit = FindShader(UnlitShaderName);
            Material shared = BuildPropMaterial(SharedMaterialPath, SharedSlot, lit, false);
            Material glow = BuildPropMaterial(GlowMaterialPath, GlowSlot, lit, true);
            var decals = new Dictionary<string, Material>();
            foreach (string weapon in DecalWeapons)
            {
                decals[weapon] = BuildDecalMaterial(weapon, unlit);
            }

            AssetDatabase.SaveAssets();

            foreach (string key in new[] { StandardKey, PremiumKey, WallMountKey })
            {
                ImportModel(key, shared, glow);
            }

            foreach (HudIntakeFile f in hud.files)
            {
                ImportHudSprite(f);
            }

            WallBuyStationLibrary library = BuildLibrary(decals, errors);
            Fail(errors, "library");

            Validate(library, shared, glow, hud, errors);
            Fail(errors, "validation");

            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "OK standard=" + StandardKey + " (front " + library.standard.frontDepth.ToString("F2") + " m) premium=" + PremiumKey +
                      " (front " + library.premium.frontDepth.ToString("F2") + " m) wallMount=" + WallMountKey + " (front " +
                      library.wallMount.frontDepth.ToString("F2") + " m) decals=" + library.decals.Length + " hud=" + hud.files.Length +
                      " provenance=" + provenance.files.Length + " files");
        }

        private static void Fail(List<string> errors, string stage)
        {
            if (errors.Count == 0) return;
            throw new InvalidOperationException(LogPrefix + stage + " failed (" + errors.Count + "):\n  " + string.Join("\n  ", errors));
        }

        /// <summary>Every listed file exists with its recorded byte count and sha256 (a changed or LFS-pointer file stops the build here).</summary>
        private static void CheckSources(Provenance provenance, HudIntake hud, List<string> errors)
        {
            if (provenance?.files == null || provenance.files.Length == 0) errors.Add("no files in " + ProvenancePath);
            if (hud?.files == null || hud.files.Length == 0) errors.Add("no files in " + HudIntakePath);
            if (errors.Count > 0) return;

            var entries = new List<(string repo, long bytes, string sha)>();
            foreach (ProvenanceFile f in provenance.files) entries.Add((f.repo, f.bytes, f.sha256));
            foreach (HudIntakeFile f in hud.files) entries.Add((f.repo, f.bytes, f.sha256));
            foreach ((string repo, long bytes, string sha) in entries)
            {
                if (!File.Exists(repo))
                {
                    errors.Add("missing " + repo);
                    continue;
                }

                long size = new FileInfo(repo).Length;
                if (size != bytes) errors.Add(repo + ": " + size + " bytes, provenance " + bytes);
                else if (!string.Equals(Sha256(repo), sha, StringComparison.OrdinalIgnoreCase)) errors.Add(repo + ": sha256 differs from the provenance");
            }
        }

        /// <summary>New copies the AssetDatabase has not seen yet (an editor that skipped the refresh) are imported before any importer is touched.</summary>
        private static void EnsureImported(Provenance provenance, HudIntake hud)
        {
            bool missing = false;
            foreach (ProvenanceFile f in provenance.files) missing |= AssetImporter.GetAtPath(f.repo) == null;
            foreach (HudIntakeFile f in hud.files) missing |= AssetImporter.GetAtPath(f.repo) == null;
            if (missing) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Shader FindShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) throw new InvalidOperationException(LogPrefix + "shader missing: " + name);
            return shader;
        }

        // ------------------------------------------------------------------ textures

        private static TextureImporter TextureImporterAt(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null) throw new FileNotFoundException(LogPrefix + "texture missing", path);
            return importer;
        }

        /// <summary>
        /// World texture (shared atlas / decal): mips on, Clamp, not readable, CompressedHQ + a Standalone override at
        /// <paramref name="maxSize"/> - BC7, the normal map BC5 (two channels; URP's UnpackNormal reads RG or AG).
        /// </summary>
        private static void ImportTexture(string path, TextureImporterType type, bool srgb, bool alphaIsTransparency, int maxSize, bool streaming,
            TextureImporterFormat standaloneFormat)
        {
            TextureImporter importer = TextureImporterAt(path);
            bool changed = false;
            Set(ref changed, importer.textureType, type, v => importer.textureType = v);
            if (type == TextureImporterType.NormalMap)
            {
                Set(ref changed, importer.flipGreenChannel, false, v => importer.flipGreenChannel = v);   // OpenGL Y+ (AP-040: do not invert)
            }
            else
            {
                Set(ref changed, importer.sRGBTexture, srgb, v => importer.sRGBTexture = v);
            }

            Set(ref changed, importer.alphaSource, TextureImporterAlphaSource.FromInput, v => importer.alphaSource = v);
            Set(ref changed, importer.alphaIsTransparency, alphaIsTransparency, v => importer.alphaIsTransparency = v);
            Set(ref changed, importer.mipmapEnabled, true, v => importer.mipmapEnabled = v);
            Set(ref changed, importer.streamingMipmaps, streaming, v => importer.streamingMipmaps = v);
            Set(ref changed, importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
            Set(ref changed, importer.filterMode, FilterMode.Bilinear, v => importer.filterMode = v);
            Set(ref changed, importer.maxTextureSize, maxSize, v => importer.maxTextureSize = v);
            Set(ref changed, importer.textureCompression, TextureImporterCompression.CompressedHQ, v => importer.textureCompression = v);
            Set(ref changed, importer.isReadable, false, v => importer.isReadable = v);
            changed |= SetStandaloneOverride(importer, maxSize, standaloneFormat);
            if (changed) importer.SaveAndReimport();
        }

        /// <summary>The Standalone override at <paramref name="maxSize"/> in <paramref name="format"/>. True when it had to change.</summary>
        private static bool SetStandaloneOverride(TextureImporter importer, int maxSize, TextureImporterFormat format)
        {
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (standalone.overridden && standalone.format == format && standalone.maxTextureSize == maxSize) return false;
            standalone.overridden = true;
            standalone.format = format;
            standalone.maxTextureSize = maxSize;
            importer.SetPlatformTextureSettings(standalone);
            return true;
        }

        /// <summary>
        /// AP-042 HUD sprite. <c>UiArtImporter.OnPreprocessTexture</c> re-applies the UI class on every import (Sprite, Single,
        /// 100 ppu, no mips, Clamp, Bilinear, sRGB, alpha is transparency, ≤ 1024, CompressedHQ); the same values are set here so the
        /// first reimport agrees with it. Added on top (it never touches them): the nine-slice border (L, B, R, T px from the
        /// intake JSON), a Full Rect mesh (sliced Image) and the Standalone BC7 override.
        /// </summary>
        private static void ImportHudSprite(HudIntakeFile f)
        {
            if (f.border_lbrt == null || f.border_lbrt.Length != 4) throw new InvalidOperationException(LogPrefix + f.name + ": border_lbrt needs 4 values");
            TextureImporter importer = TextureImporterAt(f.repo);
            var border = new Vector4(f.border_lbrt[0], f.border_lbrt[1], f.border_lbrt[2], f.border_lbrt[3]);
            bool changed = false;
            Set(ref changed, importer.textureType, TextureImporterType.Sprite, v => importer.textureType = v);
            Set(ref changed, importer.spriteImportMode, SpriteImportMode.Single, v => importer.spriteImportMode = v);
            Set(ref changed, importer.spritePixelsPerUnit, 100f, v => importer.spritePixelsPerUnit = v);
            Set(ref changed, importer.mipmapEnabled, false, v => importer.mipmapEnabled = v);
            Set(ref changed, importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
            Set(ref changed, importer.filterMode, FilterMode.Bilinear, v => importer.filterMode = v);
            Set(ref changed, importer.sRGBTexture, true, v => importer.sRGBTexture = v);
            Set(ref changed, importer.alphaIsTransparency, true, v => importer.alphaIsTransparency = v);
            Set(ref changed, importer.maxTextureSize, HudMaxSize, v => importer.maxTextureSize = v);
            Set(ref changed, importer.textureCompression, TextureImporterCompression.CompressedHQ, v => importer.textureCompression = v);
            Set(ref changed, importer.spriteBorder, border, v => importer.spriteBorder = v);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteMeshType != SpriteMeshType.FullRect)
            {
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                changed = true;
            }

            changed |= SetStandaloneOverride(importer, HudMaxSize, TextureImporterFormat.BC7);
            if (changed) importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// The prop material (integration JSON "props"): URP Lit, opaque, metallic workflow. MaskMap: R metallic, G AO, A smoothness
        /// (smoothness source = metallic alpha), B unused. <paramref name="emissive"/> adds the shared emission atlas (the glow slot).
        /// </summary>
        private static Material BuildPropMaterial(string path, string name, Shader lit, bool emissive)
        {
            Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(path, lit, name);
            Texture2D mask = Load<Texture2D>(SharedTexturePath("MaskMap"));
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = (int)RenderQueue.Geometry;

            m.SetTexture("_BaseMap", Load<Texture2D>(SharedTexturePath("BaseColor")));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", Load<Texture2D>(SharedTexturePath("Normal")));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);   // scales MaskMap.a
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // 0 = metallic alpha
            m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap", mask);   // URP reads _OcclusionMap.g
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");

            if (emissive)
            {
                m.SetTexture("_EmissionMap", Load<Texture2D>(SharedTexturePath("Emission")));
                m.SetColor("_EmissionColor", new Color(GlowIntensity, GlowIntensity, GlowIntensity, 1f));
                m.EnableKeyword("_EMISSION");
                // None, not EmissiveIsBlack: URP's material validation turns _EMISSION off under EmissiveIsBlack (ArtCharacterPrefabBuilder).
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.SetTexture("_EmissionMap", null);
                m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            m.enableInstancing = true;   // static station props: many of the same board per map
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// A weapon silhouette decal: URP Unlit, transparent alpha blend, ZWrite off, back faces culled, queue Transparent, no depth /
        /// shadow pass (the renderer's shadow casting is set by <c>WallBuyStation</c>). The quad's front must face the station front.
        /// </summary>
        private static Material BuildDecalMaterial(string weapon, Shader unlit)
        {
            Material m = ArtCharacterPrefabBuilder.LoadOrCreateMaterialPublic(DecalMaterialPath(weapon), unlit, "M_WallBuyDecal_" + weapon);
            m.SetTexture("_BaseMap", Load<Texture2D>(DecalTexturePath(weapon)));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_BlendModePreserveSpecular", 0f);   // straight alpha (see ArtAp021WeaponBuilder.SetTransparent)
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetFloat("_CastShadows", 0f);   // URP's material validation re-derives the ShadowCaster pass from it
            m.SetFloat("_ReceiveShadows", 0f);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ models

        /// <summary>The FBX importer (the ArtAp021WeaponBuilder settings) and the by-name remap of its three material slots.</summary>
        private static void ImportModel(string key, Material shared, Material glow)
        {
            string path = ModelPath(key);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }

            if (importer == null) throw new FileNotFoundException(LogPrefix + "model importer missing", path);

            bool changed = false;
            Set(ref changed, importer.globalScale, 1f, v => importer.globalScale = v);
            Set(ref changed, importer.useFileScale, true, v => importer.useFileScale = v);
            Set(ref changed, importer.bakeAxisConversion, false, v => importer.bakeAxisConversion = v);   // as the AP-021 models (ADR-042-1)
            Set(ref changed, importer.importNormals, ModelImporterNormals.Import, v => importer.importNormals = v);
            Set(ref changed, importer.importTangents, ModelImporterTangents.CalculateMikk, v => importer.importTangents = v);
            Set(ref changed, importer.meshCompression, ModelImporterMeshCompression.Off, v => importer.meshCompression = v);
            Set(ref changed, importer.isReadable, false, v => importer.isReadable = v);
            Set(ref changed, importer.addCollider, false, v => importer.addCollider = v);   // the *_COL proxy becomes a BoxCollider at runtime
            Set(ref changed, importer.importAnimation, false, v => importer.importAnimation = v);
            Set(ref changed, importer.animationType, ModelImporterAnimationType.None, v => importer.animationType = v);
            Set(ref changed, importer.importBlendShapes, false, v => importer.importBlendShapes = v);
            Set(ref changed, importer.importCameras, false, v => importer.importCameras = v);
            Set(ref changed, importer.importLights, false, v => importer.importLights = v);
            Set(ref changed, importer.importVisibility, false, v => importer.importVisibility = v);
            Set(ref changed, importer.preserveHierarchy, true, v => importer.preserveHierarchy = v);   // keeps the SOCKET_ empties
            Set(ref changed, importer.generateSecondaryUV, false, v => importer.generateSecondaryUV = v);
            Set(ref changed, importer.materialImportMode, ModelImporterMaterialImportMode.ImportStandard, v => importer.materialImportMode = v);
            Set(ref changed, importer.materialLocation, ModelImporterMaterialLocation.InPrefab, v => importer.materialLocation = v);

            Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> map = importer.GetExternalObjectMap();
            foreach ((string slot, Material target) in new[] { (SharedSlot, shared), (GlowSlot, glow), (ProxySlot, shared) })
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), slot);
                if (map.TryGetValue(id, out UnityEngine.Object current) && current == target) continue;
                importer.AddRemap(id, target);
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
        }

        /// <summary>
        /// Metres from the pivot to the station front (+Z): the frontmost point of the LOD0 mesh and the collision proxy, rounded up
        /// to the centimetre. The proxy alone would put the use zone inside the D1 V01 lantern.
        /// </summary>
        public static float MeasureFrontDepth(GameObject model)
        {
            float front = 0f;
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                if (!f.name.EndsWith(WallBuyStationLibrary.ColliderSuffix, StringComparison.Ordinal) &&
                    !f.name.EndsWith(LodSuffix + "0", StringComparison.Ordinal))
                {
                    continue;
                }

                front = Mathf.Max(front, RootBounds(model.transform, f.transform, f.sharedMesh.bounds).max.z);
            }

            return Mathf.Ceil(front * 100f - 1e-3f) / 100f;
        }

        /// <summary>A child mesh's bounds in the model root's space (all eight corners: the AP-040 meshes carry a −90° X node rotation).</summary>
        public static Bounds RootBounds(Transform root, Transform child, Bounds local)
        {
            var b = new Bounds(root.InverseTransformPoint(child.TransformPoint(local.min)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x, (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z);
                b.Encapsulate(root.InverseTransformPoint(child.TransformPoint(corner)));
            }

            return b;
        }

        /// <summary>A named descendant (depth first), or null.</summary>
        public static Transform FindChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindChild(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }

        /// <summary>The collision proxy child (<c>*_COL</c> with a mesh), or null.</summary>
        public static MeshFilter FindProxy(GameObject model)
        {
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh != null && f.name.EndsWith(WallBuyStationLibrary.ColliderSuffix, StringComparison.Ordinal)) return f;
            }

            return null;
        }

        // ------------------------------------------------------------------ library

        private static WallBuyStationLibrary BuildLibrary(Dictionary<string, Material> decals, List<string> errors)
        {
            var library = AssetDatabase.LoadAssetAtPath<WallBuyStationLibrary>(LibraryPath);
            if (library == null)
            {
                EnsureFolder(Path.GetDirectoryName(LibraryPath).Replace('\\', '/'));
                library = ScriptableObject.CreateInstance<WallBuyStationLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            library.standard = Station(StandardKey, BoardWeaponScale, BoardDecalSize);
            library.premium = Station(PremiumKey, ShrineWeaponScale, ShrineDecalSize);
            library.wallMount = Station(WallMountKey, BoardWeaponScale, BoardDecalSize);

            var entries = new List<WallBuyStationLibrary.DecalEntry>();
            foreach (string weapon in DecalWeapons)
            {
                WeaponDefinition def = Arsenal.Get(weapon);
                if (def == null || def.slot != WeaponSlotKind.Classic)
                {
                    Debug.LogWarning(LogPrefix + "decal " + weapon + " skipped: no classic weapon with that key in the Arsenal");
                    continue;
                }

                if (entries.Exists(e => e.weapon == def.id))
                {
                    errors.Add("two decals resolve to " + def.id + " (" + weapon + ")");
                    continue;
                }

                entries.Add(new WallBuyStationLibrary.DecalEntry { weapon = def.id, material = decals[weapon] });
            }

            library.decals = entries.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static WallBuyStationLibrary.StationModel Station(string key, float weaponScale, float decalSize)
        {
            GameObject model = Load<GameObject>(ModelPath(key));
            return new WallBuyStationLibrary.StationModel
            {
                model = model,
                weaponScale = weaponScale,
                decalSize = decalSize,
                frontDepth = MeasureFrontDepth(model),
                lodScreenHeights = (float[])LodScreenHeights.Clone()
            };
        }

        // ------------------------------------------------------------------ validation

        private static void Validate(WallBuyStationLibrary library, Material shared, Material glow, HudIntake hud, List<string> errors)
        {
            ValidateStation("standard", library.standard, true, shared, glow, errors);
            ValidateStation("premium", library.premium, true, shared, glow, errors);
            ValidateStation("wallMount", library.wallMount, false, shared, glow, errors);

            foreach (string weapon in DecalWeapons)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(DecalTexturePath(weapon)) == null) errors.Add("decal texture missing: " + DecalTexturePath(weapon));
                Material m = AssetDatabase.LoadAssetAtPath<Material>(DecalMaterialPath(weapon));
                if (m == null || m.GetTexture("_BaseMap") == null) errors.Add("decal material missing or empty: " + DecalMaterialPath(weapon));
            }

            foreach (WeaponDefinition def in Arsenal.All)
            {
                if (def != null && def.slot == WeaponSlotKind.Classic && def.enabled && library.DecalFor(def.id) == null)
                {
                    errors.Add("no decal for the classic weapon " + def.weaponId + " (" + def.id + ")");
                }
            }

            foreach (HudIntakeFile f in hud.files)
            {
                var importer = AssetImporter.GetAtPath(f.repo) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
                {
                    errors.Add("HUD sprite not in the UI class: " + f.repo);
                }
            }
        }

        private static void ValidateStation(string role, WallBuyStationLibrary.StationModel station, bool needsDecal, Material shared, Material glow, List<string> errors)
        {
            if (station == null || station.model == null)
            {
                errors.Add(role + ": model missing");
                return;
            }

            GameObject model = station.model;
            string label = role + " (" + model.name + ")";
            Transform root = model.transform;
            Transform weaponSocket = FindChild(root, WallBuyStationLibrary.WeaponSocket);
            Transform decalSocket = FindChild(root, WallBuyStationLibrary.DecalSocket);
            Transform priceSocket = FindChild(root, "SOCKET_Price");
            MeshFilter proxy = FindProxy(model);
            if (weaponSocket == null) errors.Add(label + ": no " + WallBuyStationLibrary.WeaponSocket);
            if (needsDecal && decalSocket == null) errors.Add(label + ": no " + WallBuyStationLibrary.DecalSocket);
            if (proxy == null) errors.Add(label + ": no *" + WallBuyStationLibrary.ColliderSuffix + " proxy child");

            // The station front is +Z (WallBuyStationLibrary contract); the price socket sits on the front face of every AP-040 station.
            if (priceSocket != null && root.InverseTransformPoint(priceSocket.position).z <= 0f)
            {
                errors.Add(label + ": SOCKET_Price at z " + root.InverseTransformPoint(priceSocket.position).z.ToString("F3") + " - the front is not +Z");
            }

            LODGroup group = model.GetComponent<LODGroup>();
            LOD[] lods = group != null ? group.GetLODs() : new LOD[0];
            if (lods.Length != LodCount) errors.Add(label + ": " + lods.Length + " LOD levels, wanted " + LodCount + " (SM_*_LOD0..2)");
            var tris = new int[LodCount];
            for (int i = 0; i < lods.Length && i < LodCount; i++)
            {
                foreach (Renderer r in lods[i].renderers)
                {
                    if (r == null) continue;
                    if (r.name.EndsWith(WallBuyStationLibrary.ColliderSuffix, StringComparison.Ordinal)) errors.Add(label + ": proxy in LOD" + i);
                    MeshFilter f = r.GetComponent<MeshFilter>();
                    if (f != null && f.sharedMesh != null) tris[i] += Triangles(f.sharedMesh);
                }
            }

            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != shared && m != glow) errors.Add(label + ": " + r.name + " slot " + (m != null ? m.name : "null") + " is not remapped");
                }
            }

            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)))
            {
                if (sub is Material || sub is Texture2D) errors.Add(label + ": embedded " + sub.GetType().Name + " " + sub.name);
            }

            if (station.frontDepth <= 0.05f) errors.Add(label + ": front depth " + station.frontDepth.ToString("F3") + " m");

            var report = new StringBuilder(LogPrefix + label + " tris " + string.Join("/", tris) + " front " + station.frontDepth.ToString("F2") + " m");
            if (proxy != null) report.Append(" proxy " + RootBounds(root, proxy.transform, proxy.sharedMesh.bounds).size.ToString("F3"));
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("SOCKET_", StringComparison.Ordinal)) continue;
                report.Append("\n    " + t.name + " pos " + root.InverseTransformPoint(t.position).ToString("F3") + " euler " + t.localEulerAngles.ToString("F1") +
                              " fwd " + root.InverseTransformDirection(t.forward).ToString("F2") + " up " + root.InverseTransformDirection(t.up).ToString("F2"));
            }

            Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------ helpers

        private static void Set<T>(ref bool changed, T current, T wanted, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
            apply(wanted);
            changed = true;
        }

        private static int Triangles(Mesh mesh)
        {
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
            return (int)(indices / 3);
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException(LogPrefix + "asset missing", path);
            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
