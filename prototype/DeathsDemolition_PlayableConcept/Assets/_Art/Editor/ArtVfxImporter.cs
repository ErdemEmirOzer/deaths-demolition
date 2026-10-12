using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Feedback.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-019 · Turns the AP-010 manifest (<see cref="ArtVfxContract.ManifestPath"/>) into the
    /// runtime catalog (<c>Assets/_Art/VFX/Resources/DD_VfxCatalog.asset</c>) and one material
    /// per playable row (<c>Assets/_Art/VFX/AP010/Materials/M_vfx_&lt;id&gt;.mat</c>). Idempotent:
    /// rows are matched by id, materials and the catalog keep their GUIDs, every property is
    /// rewritten from the manifest on each run so a hand edit never survives.
    ///
    /// Blend per kind: flipbooks/cards with an emissive layer or a light-like note (muzzle,
    /// shockwave, beacon, sparkle/trail) are additive; decals, smoke, impacts, death are alpha.
    /// The gold pack mesh gets a URP/Lit material with the AP-010 albedo + HDR emissive
    /// (intensity 2 so bloom's 1.25 threshold lifts the coins, manifest note).
    /// </summary>
    public static class ArtVfxImporter
    {
        public const string CatalogPath = ArtPipelinePaths.ArtRoot + "/VFX/Resources/" + VfxCatalog.ResourceName + ".asset";
        public const string MaterialsFolder = ArtVfxContract.VfxRoot + "/Materials";
        public const string FlipbookShaderName = "DD/VFX Flipbook";
        private const string LogPrefix = "[ArtVfx] ";

        private static readonly Regex SizeSuffix = new Regex(@"_(\d+x\d+|\d{3,4})(?=_|$)", RegexOptions.Compiled);
        private static readonly Regex VersionSuffix = new Regex(@"_v\d{3}[a-z]?(?=_|$)", RegexOptions.Compiled);

        private static readonly Regex PackagePrefix = new Regex(@"^AP010[A-Z]?_", RegexOptions.Compiled);

        /// <summary>Catalog id from a manifest file path: strip the AP010_/AP010B_ prefix, size and version suffixes (the GLB review copy gets "_review").</summary>
        public static string IdOf(string file)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            stem = PackagePrefix.Replace(stem, string.Empty);
            stem = VersionSuffix.Replace(stem, string.Empty);
            stem = SizeSuffix.Replace(stem, string.Empty);
            if (file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) stem += "_review";
            return stem;
        }

        public const string GameFeelPath = ArtPipelinePaths.ArtRoot + "/VFX/Resources/" + GameFeelSettings.ResourceName + ".asset";

        /// <summary>The game-feel asset (ADR-019-2) with its defaults; created once, never overwritten (it is the tuning surface).</summary>
        public static GameFeelSettings EnsureGameFeel()
        {
            EnsureFolder(Path.GetDirectoryName(GameFeelPath).Replace('\\', '/'));
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>(GameFeelPath);
            if (feel == null)
            {
                feel = ScriptableObject.CreateInstance<GameFeelSettings>();
                AssetDatabase.CreateAsset(feel, GameFeelPath);
                AssetDatabase.SaveAssets();
                Debug.Log(LogPrefix + "Game feel settings created: " + GameFeelPath);
            }

            return feel;
        }

        /// <summary>Re-imports every VFX texture whose importer disagrees with the contract (a contract edit after the first import). Returns the count.</summary>
        public static int ReimportOutOfContractTextures()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", ArtVfxContract.VfxFolders()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || !ArtVfxContract.IsVfxTexture(path) || ArtVfxContract.Validate(importer).Count == 0)
                {
                    continue;
                }

                ArtVfxContract.Apply(importer);
                importer.SaveAndReimport();
                n++;
            }

            if (n > 0)
            {
                Debug.Log(LogPrefix + "Re-imported " + n + " VFX texture(s) to the contract.");
            }

            return n;
        }

        /// <summary>Diagnostic: imported format / alpha of every VFX texture (batch: -executeMethod …ArtVfxImporter.DumpTextureInfo).</summary>
        public static void DumpTextureInfo()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", ArtVfxContract.VfxFolders()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Debug.Log("[ArtVfx] texinfo " + path + " format=" + (tex != null ? tex.format.ToString() : "null") + " mips=" + (tex != null ? tex.mipmapCount : 0) +
                          " alphaIsTransparency=" + (importer != null && importer.alphaIsTransparency) + " alphaSource=" + (importer != null ? importer.alphaSource.ToString() : "-") +
                          " srcHasAlpha=" + (importer != null && importer.DoesSourceTextureHaveAlpha()) + " sRGB=" + (importer != null && importer.sRGBTexture) +
                          " standalone=" + (importer != null ? importer.GetPlatformTextureSettings("Standalone").format.ToString() : "-") + " size=" + (tex != null ? tex.width + "x" + tex.height : "-"));
            }
        }

        public static VfxCatalog BuildCatalog()
        {
            EnsureGameFeel();
            ReimportOutOfContractTextures();
            if (!File.Exists(ArtVfxContract.ManifestPath))
            {
                throw new FileNotFoundException(LogPrefix + "Manifest missing: " + ArtVfxContract.ManifestPath);
            }

            var manifest = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ArtVfxContract.ManifestPath)), "manifest");
            List<object> assets = ArtJson.AsList(manifest["assets"], "assets");

            EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
            EnsureFolder(MaterialsFolder);

            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(CatalogPath);
            bool created = catalog == null;
            if (created)
            {
                catalog = ScriptableObject.CreateInstance<VfxCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.ManifestSchema = ArtJson.AsString(manifest["schema"], "schema");
            catalog.ManifestVersion = manifest.TryGetValue("version", out object ver) ? ArtJson.AsString(ver, "version") : "v002";

            Shader flipbook = Shader.Find(FlipbookShaderName);
            if (flipbook == null)
            {
                throw new InvalidOperationException(LogPrefix + "Shader '" + FlipbookShaderName + "' not found.");
            }

            var rows = new List<VfxCatalogEntry>(assets.Count + 32);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AppendRows(assets, ArtVfxContract.VfxRoot, Path.GetFileName(ArtVfxContract.ManifestPath), rows, seen, catalog, flipbook);

            // WP-019b · supplements: any AP010<letter> folder (AP010B blood set) with its own manifest, same schema, same rules.
            foreach (string supplement in SupplementManifests())
            {
                var extra = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(supplement)), "manifest");
                List<object> extraAssets = ArtJson.AsList(extra["assets"], "assets");
                AppendRows(extraAssets, Path.GetDirectoryName(supplement).Replace('\\', '/'), Path.GetFileName(supplement), rows, seen, catalog, flipbook);
                Debug.Log(LogPrefix + "Supplement manifest: " + supplement + " (" + extraAssets.Count + " assets)");
            }

            // WP-101 (R4, AP-041): the premultiplied photographic set from its trimmed manifest (no-op when it is not imported).
            ArtAp041VfxIntake.AppendRows(rows, seen, catalog, flipbook);
            // WP-104 B4 (S-07): the run tree's AP-041 v001 + v002 sheets (no-op when not imported).
            ArtAp041TreeIntake.AppendRows(rows, seen, catalog, flipbook);
            rows.Add(SynthesizeDropletRow(rows, catalog, flipbook));

            catalog.Entries.Clear();
            catalog.Entries.AddRange(rows);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log(LogPrefix + "Catalog " + (created ? "created" : "updated") + ": " + rows.Count + " rows, " + CatalogPath);
            return catalog;
        }

        /// <summary>
        /// One manifest's rows (the main AP010 one or a supplement rooted at <paramref name="root"/>).
        /// Schema v003 (AP-010 HQ pass + AP-010B): <c>file</c> may be relative to <c>Assets/_Art/VFX/</c>
        /// (starts with <c>AP010</c>), superseding rows may omit family/kind/size (inherited by id),
        /// <c>world_size_m</c> may be a number, a [w,h] pair or a string, <c>capsule_cover_s</c> replaces
        /// <c>readability_limit_s</c>, and <c>blend</c>/<c>tint_rgb</c>/<c>hdr</c>/<c>slot</c>/<c>supersedes</c> are read.
        /// </summary>
        private static void AppendRows(List<object> assets, string root, string manifestName, List<VfxCatalogEntry> rows, HashSet<string> seen, VfxCatalog catalog, Shader flipbook)
        {
            string vfxParent = Path.GetDirectoryName(ArtVfxContract.VfxRoot).Replace('\\', '/');
            foreach (object o in assets)
            {
                var a = ArtJson.AsObject(o, "asset");
                string file = ArtJson.AsString(a["file"], "file");
                string id = IdOf(file);
                if (!seen.Add(id))
                {
                    throw new InvalidOperationException(LogPrefix + "Duplicate catalog id '" + id + "' from " + file);
                }

                VfxCatalogEntry e = catalog.Find(id) ?? new VfxCatalogEntry();
                e.Id = id;
                e.SourceFile = file;
                e.Sha256 = OptString(a, "sha256");
                e.Slot = OptString(a, "slot");
                e.Supersedes = OptString(a, "supersedes");
                e.AliasTo = OptString(a, "alias_to");
                string familyText = OptString(a, "family");
                e.Family = familyText.Length > 0 ? ParseFamily(familyText) : GuessFamily(id);
                string kindText = OptString(a, "kind");
                e.Kind = file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ? VfxKind.Review
                    : file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ? VfxKind.Mesh
                    : kindText.Length > 0 ? ParseKind(kindText) : GuessKind(id);
                List<object> grid = a.TryGetValue("grid", out object g) && g != null ? ArtJson.AsList(g, "grid") : null;
                e.Columns = grid != null ? ArtJson.AsInt(grid[0], "grid[0]") : 1;
                e.Rows = grid != null ? ArtJson.AsInt(grid[1], "grid[1]") : 1;
                e.FramesUsed = Mathf.Max(1, OptInt(a, "frames_used", 1));
                e.Fps = OptFloat(a, "fps", 0f);
                e.DurationSeconds = OptFloat(a, "duration_s", 0f);
                e.Loop = a.TryGetValue("loop", out object lp) && lp != null && ArtJson.AsBool(lp, "loop");
                ParseWorldSize(a, out float ws, out float wh);
                e.WorldSizeMetres = ws;
                e.WorldHeightMetres = wh;
                e.ReadabilityLimitSeconds = a.ContainsKey("capsule_cover_s") ? OptFloat(a, "capsule_cover_s", 0f) : OptFloat(a, "readability_limit_s", 0f);
                string reserve = OptString(a, "colour_reserve");
                e.ColourReserve = reserve.Length > 0 ? ParseReserve(reserve) : VfxColourReserve.None;
                e.UsageNote = OptString(a, "usage_note");
                e.BlendNote = OptString(a, "blend");
                e.HdrIntensity = OptFloat(a, "hdr", 1f);
                e.DefaultTint = ParseTint(a);
                e.OriginUv = new Vector2(0.5f, 0.5f);
                if (a.TryGetValue("origin_uv", out object ouv) && ouv != null)
                {
                    List<object> uv = ArtJson.AsList(ouv, "origin_uv");
                    e.OriginUv = new Vector2(ArtJson.AsFloat(uv[0], "origin_uv[0]"), ArtJson.AsFloat(uv[1], "origin_uv[1]"));
                }

                string assetPath = ResolveAssetPath(file, root, vfxParent);
                e.Texture = null;
                e.Mesh = null;
                e.Emissive = null;
                e.Normal = null;
                if (e.Kind == VfxKind.Mesh)
                {
                    e.Mesh = LoadMesh(assetPath);
                }
                else if (e.Kind != VfxKind.Review)
                {
                    e.Texture = Load<Texture2D>(assetPath);
                    if (e.Texture == null)
                    {
                        throw new FileNotFoundException(LogPrefix + "Texture missing for " + id + ": " + assetPath);
                    }
                }

                string emissive = OptString(a, "emissive_file");
                if (emissive.Length > 0 && e.Kind != VfxKind.Mesh)
                {
                    e.Emissive = Load<Texture2D>(ResolveAssetPath(emissive, root, vfxParent));
                }

                string normal = OptString(a, "normal_file");
                if (normal.Length > 0)
                {
                    e.Normal = Load<Texture2D>(ResolveAssetPath(normal, root, vfxParent));
                }

                e.ManifestFile = manifestName;
                e.MeanOpaqueColour = e.Texture != null && e.Kind != VfxKind.Mesh ? MeanOpaqueColour(assetPath) : Color.white;
                e.Additive = IsAdditive(e);
                e.Material = BuildMaterial(e, flipbook, catalog);
                rows.Add(e);
            }
        }

        /// <summary>v003 paths start with the VFX folder (<c>AP010/…</c>, <c>AP010B/…</c>); v002 paths are relative to the manifest's root.</summary>
        private static string ResolveAssetPath(string file, string root, string vfxParent)
        {
            return file.StartsWith("AP010", StringComparison.Ordinal) ? vfxParent + "/" + file : root + "/" + file;
        }

        private static string OptString(Dictionary<string, object> a, string key)
        {
            return a.TryGetValue(key, out object v) && v != null && !(v is Dictionary<string, object>) && !(v is List<object>) ? ArtJson.AsString(v, key) : string.Empty;
        }

        private static float OptFloat(Dictionary<string, object> a, string key, float fallback)
        {
            return a.TryGetValue(key, out object v) && v != null && !(v is string) && !(v is List<object>) && !(v is Dictionary<string, object>) ? ArtJson.AsFloat(v, key) : fallback;
        }

        private static int OptInt(Dictionary<string, object> a, string key, int fallback)
        {
            return a.TryGetValue(key, out object v) && v != null && !(v is string) ? ArtJson.AsInt(v, key) : fallback;
        }

        /// <summary><c>world_size_m</c>: number, [w, h] (width = size, height kept), or a string ("full screen") → 0.</summary>
        private static void ParseWorldSize(Dictionary<string, object> a, out float size, out float height)
        {
            size = 0f; height = 0f;
            if (!a.TryGetValue("world_size_m", out object v) || v == null) return;
            if (v is List<object> list && list.Count > 0)
            {
                size = ArtJson.AsFloat(list[0], "world_size_m[0]");
                height = list.Count > 1 ? ArtJson.AsFloat(list[1], "world_size_m[1]") : size;
                return;
            }

            if (v is string) return;
            size = ArtJson.AsFloat(v, "world_size_m");
            height = size;
        }

        /// <summary><c>tint_rgb</c>: [r,g,b] or {name: [r,g,b], …} (the "default" entry, else the first) → colour; white when absent.</summary>
        private static Color ParseTint(Dictionary<string, object> a)
        {
            if (!a.TryGetValue("tint_rgb", out object v) || v == null) return Color.white;
            List<object> rgb = null;
            if (v is List<object> l) rgb = l;
            else if (v is Dictionary<string, object> d)
            {
                if (d.TryGetValue("default", out object def) && def is List<object> dl) rgb = dl;
                else foreach (KeyValuePair<string, object> kv in d) { if (kv.Value is List<object> fl) { rgb = fl; break; } }
            }

            if (rgb == null || rgb.Count < 3) return Color.white;
            return new Color(ArtJson.AsFloat(rgb[0], "tint[0]"), ArtJson.AsFloat(rgb[1], "tint[1]"), ArtJson.AsFloat(rgb[2], "tint[2]"), 1f);
        }

        private static VfxFamily GuessFamily(string id)
        {
            if (id.Contains("impact")) return VfxFamily.Impact;
            if (id.Contains("muzzle")) return VfxFamily.Muzzle;
            if (id.Contains("explosion") || id.Contains("shockwave") || id.Contains("smoke") || id.Contains("scorch")) return VfxFamily.Explosion;
            if (id.Contains("armorbreak") || id.Contains("splinter")) return VfxFamily.ArmorBreak;
            if (id.Contains("zombie")) return VfxFamily.Zombie;
            if (id.Contains("gold")) return VfxFamily.Gold;
            if (id.Contains("beacon") || id.Contains("hud_marker")) return VfxFamily.Beacon;
            if (id.Contains("vignette")) return VfxFamily.Vignette;
            if (id.Contains("blood")) return VfxFamily.Blood;
            return VfxFamily.World;
        }

        private static VfxKind GuessKind(string id)
        {
            if (id.StartsWith("fb_", StringComparison.Ordinal) || id.StartsWith("sheet_", StringComparison.Ordinal)) return VfxKind.Flipbook;
            if (id.Contains("_normal") || id.Contains("_height") || id.Contains("_distort") || id.Contains("overlay")) return VfxKind.Texture;
            if (id.StartsWith("decal_", StringComparison.Ordinal)) return VfxKind.Decal;
            if (id.StartsWith("card_", StringComparison.Ordinal) || id.StartsWith("atlas_", StringComparison.Ordinal)) return VfxKind.Card;
            return VfxKind.Texture;
        }

        /// <summary>Supplement manifests: <c>Assets/_Art/VFX/AP010&lt;letter&gt;/*-vfx-manifest-*.json</c> (AP010B blood set), sorted.</summary>
        public static List<string> SupplementManifests()
        {
            var found = new List<string>();
            string parent = Path.GetDirectoryName(ArtVfxContract.VfxRoot).Replace('\\', '/');
            if (!Directory.Exists(parent))
            {
                return found;
            }

            foreach (string dir in Directory.GetDirectories(parent))
            {
                string name = Path.GetFileName(dir);
                if (!Regex.IsMatch(name, "^AP010[A-Z]$"))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(dir, "*-vfx-manifest-*.json"))
                {
                    found.Add(file.Replace('\\', '/'));
                }
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        /// <summary>
        /// Alpha-weighted mean LINEAR colour of the texels (alpha &gt; 0.1) of the source PNG (sRGB → linear per
        /// texel, weighted by alpha, then averaged — what the sRGB sampler feeds the shader), decoded on the CPU (works
        /// under -nographics). Every 4th texel on each axis; white when the file cannot be read.
        /// </summary>
        public static Color MeanOpaqueColour(string assetPath)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(assetPath), false))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return Color.white;
                }

                Color32[] px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                UnityEngine.Object.DestroyImmediate(tex);
                // Alpha-weighted: what the eye averages on screen is coverage × colour, so a soft sheet's
                // dense core counts more than its fringe (texels under alpha 0.1 are ignored).
                double r = 0, g = 0, b = 0, wsum = 0; long n = 0;
                for (int y = 0; y < h; y += 4)
                {
                    for (int x = 0; x < w; x += 4)
                    {
                        Color32 c = px[y * w + x];
                        if (c.a > 25)
                        {
                            double wa = c.a / 255.0;
                            r += wa * Mathf.GammaToLinearSpace(c.r / 255f); g += wa * Mathf.GammaToLinearSpace(c.g / 255f); b += wa * Mathf.GammaToLinearSpace(c.b / 255f);
                            wsum += wa; n++;
                        }
                    }
                }

                return n == 0 || wsum <= 0 ? Color.white : new Color((float)(r / wsum), (float)(g / wsum), (float)(b / wsum), 1f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "MeanOpaqueColour failed for " + assetPath + ": " + ex.Message);
                return Color.white;
            }
        }

        /// <summary>
        /// WP-019b · The droplet row: its own material (tint baked in, instancing on — a property
        /// block would break the particle instancing) over the AP-010B droplet sheet when present,
        /// else the flesh decal. Recolour target <see cref="BloodPalette.Droplet"/>.
        /// </summary>
        private static VfxCatalogEntry SynthesizeDropletRow(List<VfxCatalogEntry> rows, VfxCatalog catalog, Shader flipbook)
        {
            VfxCatalogEntry source = null;
            foreach (VfxCatalogEntry r in rows)
            {
                if (r.Id == BloodSlots.Droplet[0] && r.Texture != null) { source = r; break; }
            }

            if (source == null)
            {
                foreach (VfxCatalogEntry r in rows)
                {
                    if (r.Id == VfxIds.DecalImpactFlesh && r.Texture != null) { source = r; break; }
                }
            }

            VfxCatalogEntry e = catalog.Find(VfxIds.BloodDroplet) ?? new VfxCatalogEntry();
            e.Id = VfxIds.BloodDroplet;
            e.Family = VfxFamily.Blood;
            e.Kind = VfxKind.Card;
            e.SourceFile = source != null ? source.SourceFile : string.Empty;
            e.Sha256 = source != null ? source.Sha256 : string.Empty;
            e.ManifestFile = "synthesized";
            e.Texture = source != null ? source.Texture : null;
            e.Columns = source != null ? source.Columns : 1;
            e.Rows = source != null ? source.Rows : 1;
            e.FramesUsed = source != null ? source.FramesUsed : 1;
            e.WorldSizeMetres = 0.04f;
            e.MeanOpaqueColour = source != null ? source.MeanOpaqueColour : Color.white;
            e.ColourReserve = VfxColourReserve.None;
            e.UsageNote = "blood droplets (BloodDropletEffect, mesh particles, GPU instanced)";
            e.Additive = false;
            if (e.Texture == null)
            {
                e.Material = null;
                return e;
            }

            string path = MaterialsFolder + "/M_vfx_" + e.Id + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(flipbook) { name = "M_vfx_" + e.Id };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != flipbook)
            {
                m.shader = flipbook;
            }

            m.SetTexture("_Sheet", e.Texture);
            m.SetTexture("_EmissiveTex", null);
            m.SetTexture("_MainTex", null);
            m.SetFloat("_UseEmissive", 0f);
            // AP-010B v003 droplet sheet is painted in the band (authored tint); the v002 flesh-decal fallback is recoloured.
            m.SetColor("_Tint", source != null && source.Family == VfxFamily.Blood ? Color.white : BloodPalette.TintFor(e, BloodPalette.Droplet, BloodPalette.DropletCalibration));
            m.SetFloat("_Cols", 1f);   // the particle system's texture-sheet module picks the cell (row.Columns/Rows drive it)
            m.SetFloat("_Rows", 1f);
            m.SetFloat("_Frame", 0f);
            m.SetFloat("_Alpha", 1f);
            m.SetFloat("_AlphaCap", 1f);
            m.SetFloat("_ScrollV", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            m.renderQueue = (int)RenderQueue.Transparent + 5;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            e.Material = m;
            return e;
        }

        // ------------------------------------------------------------------ materials

        private static bool IsAdditive(VfxCatalogEntry e)
        {
            if (!string.IsNullOrEmpty(e.BlendNote))
            {
                return e.BlendNote.StartsWith("additive", StringComparison.OrdinalIgnoreCase);
            }

            switch (e.Id)
            {
                case VfxIds.Muzzle:
                case VfxIds.Shockwave:
                case VfxIds.CardShockwave:
                case VfxIds.BeaconColumn:
                case VfxIds.BeaconRing:
                case VfxIds.BeaconRingAnim:
                case VfxIds.CardGoldSparkle:
                case VfxIds.CardGoldTrail:
                    return true;
                default:
                    return false;
            }
        }

        private static Material BuildMaterial(VfxCatalogEntry e, Shader flipbook, VfxCatalog catalog)
        {
            if (e.Kind == VfxKind.Review || e.Id == VfxIds.HudMarker)
            {
                return null;   // sprite for the HUD (S-09 API); the GLB has no asset
            }

            if (e.Kind == VfxKind.Texture && e.Id != VfxIds.BeaconColumn && e.Id != VfxIds.BeaconRing && e.Id != VfxIds.VignetteDamage && e.Id != VfxIds.PlayerDeathOverlay)
            {
                return null;   // data textures: gold albedo/emissive (bound by the mesh material), normals / heights / masks (no lit decal shader yet)
            }

            string path = MaterialsFolder + "/M_vfx_" + e.Id + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (e.Kind == VfxKind.Mesh)
            {
                Shader lit = ArtMaterialBuilder.ResolveLitShader();
                if (m == null)
                {
                    m = new Material(lit) { name = "M_vfx_" + e.Id };
                    AssetDatabase.CreateAsset(m, path);
                }
                else if (m.shader != lit)
                {
                    m.shader = lit;
                }

                VfxCatalogEntry albedo = FindRow(catalog, VfxIds.GoldPackAlbedo);
                VfxCatalogEntry emissive = FindRow(catalog, VfxIds.GoldPackEmissive);
                m.SetTexture("_BaseMap", albedo != null ? albedo.Texture : null);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0.35f);
                m.SetFloat("_Smoothness", 0.55f);
                Texture2D emissiveTex = emissive != null ? emissive.Texture : null;
                m.SetTexture("_EmissionMap", emissiveTex);
                // Manifest: "Unity HDR intensity ~2 so bloom (threshold 1.25) lifts the coins".
                m.SetColor("_EmissionColor", new Color(2f, 2f, 2f, 1f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                return m;
            }

            if (m == null)
            {
                m = new Material(flipbook) { name = "M_vfx_" + e.Id };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != flipbook)
            {
                m.shader = flipbook;
            }

            m.SetTexture("_Sheet", e.Texture);
            m.SetTexture("_EmissiveTex", e.Emissive);
            m.SetTexture("_MainTex", null);   // the pre-rename slot: a stale v002 texture GUID stayed serialized here (ProjectIntegrityTests, WP-019 r2)
            m.SetFloat("_UseEmissive", e.Emissive != null ? 1f : 0f);
            m.SetFloat("_EmissiveStrength", 1.5f);
            // v003 LA (greyscale) sheets carry their tint in the manifest (× hdr when additive); v002 rows stay white.
            Color tint = e.DefaultTint;
            if (e.Additive && e.HdrIntensity > 1f) tint *= e.HdrIntensity;
            tint.a = 1f;
            m.SetColor("_Tint", tint);
            m.SetFloat("_Cols", e.Columns);
            m.SetFloat("_Rows", e.Rows);
            m.SetFloat("_Frame", 0f);
            m.SetFloat("_Alpha", 1f);
            // Manifest readability caps: smoke alpha ≤ 0.45 after the fireball's first 13 frames (explosion sheet is baked).
            // Smoke alpha ≤ 0.45 (manifest); 0.40 after the v003 HQ pass (the denser plume took the 0.65 s explosion readability to 68.9 % of clear, gate 70 %).
            m.SetFloat("_AlphaCap", e.Id == VfxIds.CardSmoke ? 0.40f : 1f);
            m.SetFloat("_ScrollV", e.Id == VfxIds.BeaconColumn ? 0.2f : 0f);   // manifest: UV scroll 0.2/s up
            m.SetFloat("_SrcBlend", e.Additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", e.Additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            m.renderQueue = (int)RenderQueue.Transparent + (e.Kind == VfxKind.Decal ? -10 : e.Additive ? 10 : 0);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static VfxCatalogEntry FindRow(VfxCatalog catalog, string id)
        {
            return catalog.Find(id);
        }

        // ------------------------------------------------------------------ helpers

        private static VfxFamily ParseFamily(string s)
        {
            switch (s)
            {
                case "impact": return VfxFamily.Impact;
                case "muzzle": return VfxFamily.Muzzle;
                case "explosion": return VfxFamily.Explosion;
                case "armorbreak": return VfxFamily.ArmorBreak;
                case "zombie": return VfxFamily.Zombie;
                case "gold": return VfxFamily.Gold;
                case "beacon": return VfxFamily.Beacon;
                case "vignette": return VfxFamily.Vignette;
                case "blood": return VfxFamily.Blood;
                case "weapon": return VfxFamily.Weapon;
                case "world": return VfxFamily.World;
                case "enemy": return VfxFamily.Enemy;
                case "player": return VfxFamily.Player;
                case "player/enemy": return VfxFamily.Enemy;
                default: throw new InvalidOperationException(LogPrefix + "Unknown family '" + s + "'");
            }
        }

        /// <summary>v002 kinds are exact words; v003 kinds are descriptions ("A3 ground decal normal", "sprite sheet, 16 static…", "puff flipbook (alpha)") — classified by keyword.</summary>
        private static VfxKind ParseKind(string s)
        {
            switch (s)
            {
                case "flipbook": return VfxKind.Flipbook;
                case "decal": return VfxKind.Decal;
                case "card": return VfxKind.Card;
                case "texture": return VfxKind.Texture;
                case "mesh": return VfxKind.Mesh;
            }

            string k = s.ToLowerInvariant();
            if (k.Contains("mesh")) return VfxKind.Mesh;
            if (k.Contains("normal") || k.Contains("height") || k.Contains("distortion") || k.Contains("overlay") || k.Contains("data")) return VfxKind.Texture;
            if (k.Contains("decal") || k.Contains("pool albedo")) return VfxKind.Decal;
            if (k.Contains("flipbook") || k.Contains("sheet") || k.Contains("masks") || k.Contains("sprites") || k.Contains("cone") || k.Contains("fan") || k.Contains("burst") || k.Contains("strand") || k.Contains("mist") || k.Contains("arc") || k.Contains("crown")) return VfxKind.Flipbook;
            if (k.Contains("card")) return VfxKind.Card;
            return VfxKind.Texture;
        }

        private static VfxColourReserve ParseReserve(string s)
        {
            switch (s)
            {
                case "gold": return VfxColourReserve.Gold;
                case "beacon": return VfxColourReserve.Beacon;
                case "vermilion": return VfxColourReserve.Vermilion;
                case "data": return VfxColourReserve.Data;
                default: return VfxColourReserve.None;
            }
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(LogPrefix + typeof(T).Name + " missing: " + path, path);
            }

            return asset;
        }

        private static Mesh LoadMesh(string fbxPath)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (o is Mesh mesh)
                {
                    return mesh;
                }
            }

            throw new FileNotFoundException(LogPrefix + "No mesh in " + fbxPath, fbxPath);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
