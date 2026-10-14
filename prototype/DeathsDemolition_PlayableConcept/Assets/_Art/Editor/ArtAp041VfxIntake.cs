using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DD.Prototype.Feedback.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-101 (R4, AP-041) · the AP-041 v001 premultiplied photographic set → catalog rows + one material per row.
    ///
    ///   Assets/_Art/VFX/AP041/v001/{muzzle,impacts,decals}/H_*_srgb_pm.png   33 sheets, copied byte-for-byte by
    ///                                                                      wp-101 apply_ap041.py (sha256 = delivery manifest)
    ///   Assets/_Art/VFX/AP041/v001/AP041-vfx-manifest-v001.json             the used rows of AP041-manifest-v001.json, trimmed
    ///   Assets/_Art/VFX/AP041/v001/AP041-v001-LICENSES.csv                  one register row per sheet (private ledger reference)
    ///   Assets/_Art/VFX/AP041/v001/Materials/M_vfx_&lt;id&gt;.mat                DD/VFX Flipbook, _Premultiplied 1, Blend One OneMinusSrcAlpha
    ///
    /// Only what WP-101 plays is imported: 15 top-down muzzle flashes (5 classes × 3 variants; the side views stay out — FP / TP
    /// keep the AP-010 sheet), the muzzle smoke, 5 impact sheets and 12 bullet holes. Tracers stay procedural; the B1–B13
    /// upgrade sheets are WP-104's. Ids are the <see cref="VfxIds"/> constants (<see cref="Table"/>); every number (grid,
    /// frames_used, fps, duration, [w, h], origin_uv) comes from the manifest — the flipbook grid is row-major, frame 0 top-left,
    /// which is what DD/VFX Flipbook already samples.
    ///
    /// <see cref="ArtVfxImporter.BuildCatalog"/> calls <see cref="AppendRows"/> after the AP-010 manifests, so a later
    /// "Run VFX Catalog" keeps the rows. Absent manifest = no rows, no error (a checkout without the import plays AP-010).
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtAp041VfxIntake.Run</c> (verifies sha256 / licence rows / import
    /// contract / budget, rebuilds the catalog, throws on any violation so the exit code tells).
    /// </summary>
    public static class ArtAp041VfxIntake
    {
        public const string Root = ArtVfxContract.Ap041Root + "/v001";
        public const string ManifestName = "AP041-vfx-manifest-v001.json";
        public const string ManifestPath = Root + "/" + ManifestName;
        public const string LicensesPath = Root + "/AP041-v001-LICENSES.csv";
        public const string MaterialsFolder = Root + "/Materials";
        public const int ExpectedTextures = 33;
        private const string LogPrefix = "[ArtAp041] ";

        public static bool Active => File.Exists(ManifestPath);

        /// <summary>One imported sheet: catalog id, path under <see cref="Root"/>, family and kind.</summary>
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly string File;
            public readonly VfxFamily Family;
            public readonly VfxKind Kind;

            public Entry(string id, string file, VfxFamily family, VfxKind kind)
            {
                Id = id;
                File = file;
                Family = family;
                Kind = kind;
            }
        }

        private static Entry Muzzle(string id, string cls, int variant) =>
            new Entry(id, "muzzle/H_muzzle_" + cls + "_TD_var0" + variant + "_srgb_pm.png", VfxFamily.Muzzle, VfxKind.Flipbook);

        private static Entry Impact(string id, string surface) =>
            new Entry(id, "impacts/H_impact_" + surface + "_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook);

        private static Entry Hole(string id, string surface, int variant) =>
            new Entry(id, "decals/H_decal_" + surface + "_var0" + variant + "_srgb_pm.png", VfxFamily.Impact, VfxKind.Decal);

        /// <summary>The 33 rows (ids are compile-time constants the runtime asks for).</summary>
        public static readonly Entry[] Table =
        {
            Muzzle(VfxIds.Ap041MuzzlePistolTd01, "pistol", 1), Muzzle(VfxIds.Ap041MuzzlePistolTd02, "pistol", 2), Muzzle(VfxIds.Ap041MuzzlePistolTd03, "pistol", 3),
            Muzzle(VfxIds.Ap041MuzzleRifleTd01, "rifle", 1), Muzzle(VfxIds.Ap041MuzzleRifleTd02, "rifle", 2), Muzzle(VfxIds.Ap041MuzzleRifleTd03, "rifle", 3),
            Muzzle(VfxIds.Ap041MuzzleLmgTd01, "lmg", 1), Muzzle(VfxIds.Ap041MuzzleLmgTd02, "lmg", 2), Muzzle(VfxIds.Ap041MuzzleLmgTd03, "lmg", 3),
            Muzzle(VfxIds.Ap041MuzzleSniperTd01, "sniper", 1), Muzzle(VfxIds.Ap041MuzzleSniperTd02, "sniper", 2), Muzzle(VfxIds.Ap041MuzzleSniperTd03, "sniper", 3),
            Muzzle(VfxIds.Ap041MuzzlePelletTd01, "pellet", 1), Muzzle(VfxIds.Ap041MuzzlePelletTd02, "pellet", 2), Muzzle(VfxIds.Ap041MuzzlePelletTd03, "pellet", 3),
            new Entry(VfxIds.Ap041MuzzleSmoke, "muzzle/H_muzzle_smoke_srgb_pm.png", VfxFamily.Muzzle, VfxKind.Flipbook),
            Impact(VfxIds.Ap041ImpactStone, "stone"), Impact(VfxIds.Ap041ImpactWood, "wood"), Impact(VfxIds.Ap041ImpactEarth, "earth"),
            Impact(VfxIds.Ap041ImpactMetal, "metal"), Impact(VfxIds.Ap041ImpactFlesh, "flesh"),
            Hole(VfxIds.Ap041DecalStone01, "stone", 1), Hole(VfxIds.Ap041DecalStone02, "stone", 2), Hole(VfxIds.Ap041DecalStone03, "stone", 3), Hole(VfxIds.Ap041DecalStone04, "stone", 4),
            Hole(VfxIds.Ap041DecalWood01, "wood", 1), Hole(VfxIds.Ap041DecalWood02, "wood", 2), Hole(VfxIds.Ap041DecalWood03, "wood", 3), Hole(VfxIds.Ap041DecalWood04, "wood", 4),
            Hole(VfxIds.Ap041DecalMetal01, "metal", 1), Hole(VfxIds.Ap041DecalMetal02, "metal", 2), Hole(VfxIds.Ap041DecalMetal03, "metal", 3), Hole(VfxIds.Ap041DecalMetal04, "metal", 4)
        };

        /// <summary>Asset paths of the imported sheets (empty when the set is not imported) — the PNG inventory gate's list.</summary>
        public static List<string> TextureFiles()
        {
            var list = new List<string>(ExpectedTextures);
            if (!Active) return list;
            foreach (Entry e in Table) list.Add(Root + "/" + e.File);
            return list;
        }

        /// <summary>The trimmed manifest's items by <c>file</c> (relative to <see cref="Root"/>).</summary>
        public static Dictionary<string, Dictionary<string, object>> ManifestItems()
        {
            var items = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            if (!Active) return items;
            var manifest = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ManifestPath)), "AP041 manifest");
            foreach (object o in ArtJson.AsList(manifest["items"], "items"))
            {
                var a = ArtJson.AsObject(o, "item");
                items[ArtJson.AsString(a["file"], "file")] = a;
            }

            return items;
        }

        // ------------------------------------------------------------------ catalog rows

        /// <summary>Appends the AP-041 rows (and writes their materials). Returns the rows added; 0 when not imported.</summary>
        public static int AppendRows(List<VfxCatalogEntry> rows, HashSet<string> seen, VfxCatalog catalog, Shader flipbook)
        {
            if (!Active)
            {
                Debug.Log(LogPrefix + "AP-041 v001 not imported (" + ManifestPath + " absent): AP-010 rows only.");
                return 0;
            }

            Dictionary<string, Dictionary<string, object>> items = ManifestItems();
            EnsureFolder(MaterialsFolder);
            int n = 0;
            foreach (Entry t in Table)
            {
                if (!items.TryGetValue(t.File, out Dictionary<string, object> a))
                {
                    throw new InvalidOperationException(LogPrefix + "Manifest has no item for " + t.File);
                }

                if (!seen.Add(t.Id))
                {
                    throw new InvalidOperationException(LogPrefix + "Duplicate catalog id '" + t.Id + "'");
                }

                string unityId = OptString(a, "unity_id");
                if (unityId.Length > 0 && !string.Equals(unityId, t.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(LogPrefix + t.File + ": manifest unity_id '" + unityId + "' ≠ table id '" + t.Id + "'");
                }

                if (!a.TryGetValue("premultiplied", out object pm) || pm == null || !ArtJson.AsBool(pm, "premultiplied"))
                {
                    throw new InvalidOperationException(LogPrefix + t.File + ": not premultiplied — the AP-041 path is PM only");
                }

                VfxCatalogEntry e = catalog.Find(t.Id) ?? new VfxCatalogEntry();
                e.Id = t.Id;
                e.Family = t.Family;
                e.Kind = t.Kind;
                e.SourceFile = "AP041/v001/" + t.File;
                e.Sha256 = OptString(a, "sha256");
                e.Slot = string.Empty;
                e.Supersedes = string.Empty;
                e.AliasTo = string.Empty;
                List<object> grid = ArtJson.AsList(a["grid"], "grid");
                e.Columns = ArtJson.AsInt(grid[0], "grid[0]");
                e.Rows = ArtJson.AsInt(grid[1], "grid[1]");
                e.FramesUsed = Mathf.Max(1, ArtJson.AsInt(a["frames_used"], "frames_used"));
                e.Fps = ArtJson.AsFloat(a["fps"], "fps");
                e.DurationSeconds = ArtJson.AsFloat(a["duration_s"], "duration_s");
                e.Loop = a.TryGetValue("loop", out object lp) && lp != null && ArtJson.AsBool(lp, "loop");
                List<object> ws = ArtJson.AsList(a["world_size_m"], "world_size_m");
                e.WorldSizeMetres = ArtJson.AsFloat(ws[0], "world_size_m[0]");
                e.WorldHeightMetres = ws.Count > 1 ? ArtJson.AsFloat(ws[1], "world_size_m[1]") : e.WorldSizeMetres;
                List<object> uv = ArtJson.AsList(a["origin_uv"], "origin_uv");
                e.OriginUv = new Vector2(ArtJson.AsFloat(uv[0], "origin_uv[0]"), ArtJson.AsFloat(uv[1], "origin_uv[1]"));
                e.ReadabilityLimitSeconds = 0f;
                e.ColourReserve = VfxColourReserve.None;
                e.UsageNote = "AP-041 v001 premultiplied (WP-101): " + t.File;
                e.BlendNote = OptString(a, "blend");
                e.HdrIntensity = 1f;
                e.DefaultTint = Color.white;
                e.Additive = false;
                e.Premultiplied = true;
                e.Mesh = null;
                e.Emissive = null;
                e.Normal = null;
                string assetPath = Root + "/" + t.File;
                e.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (e.Texture == null)
                {
                    throw new FileNotFoundException(LogPrefix + "Texture missing for " + t.Id + ": " + assetPath, assetPath);
                }

                // The blood recolour reads this; a premultiplied sheet is never recoloured (white = gain 1).
                e.MeanOpaqueColour = Color.white;
                e.ManifestFile = ManifestName;
                e.Material = BuildMaterial(e, flipbook);
                rows.Add(e);
                n++;
            }

            Debug.Log(LogPrefix + "Catalog rows: " + n + " (" + ManifestPath + ")");
            return n;
        }

        /// <summary>One DD/VFX Flipbook material per row on the premultiplied path: Blend One OneMinusSrcAlpha, <c>_Premultiplied</c> 1.</summary>
        private static Material BuildMaterial(VfxCatalogEntry e, Shader flipbook)
        {
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
            m.SetFloat("_EmissiveStrength", 1f);
            m.SetColor("_Tint", Color.white);
            m.SetFloat("_Cols", e.Columns);
            m.SetFloat("_Rows", e.Rows);
            m.SetFloat("_Frame", 0f);
            m.SetFloat("_Alpha", 1f);
            m.SetFloat("_AlphaCap", 1f);
            m.SetFloat("_ScrollV", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Premultiplied", 1f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            // Holes before the transparents (as AP-010 decals), flashes after (as the AP-010 additive muzzle), the rest in between.
            m.renderQueue = (int)RenderQueue.Transparent + (e.Kind == VfxKind.Decal ? -10 : Ap041Vfx.IsMuzzleFlashId(e.Id) ? 10 : 0);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ batch entry

        [MenuItem("Tools/Deaths Demolition/Art Pipeline/Run AP-041 VFX (WP-101)", priority = 304)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(LogPrefix + "Run cannot run in Play mode.");
            }

            Stopwatch total = Stopwatch.StartNew();
            AssetDatabase.Refresh();
            if (!Active)
            {
                throw new FileNotFoundException(LogPrefix + "AP-041 manifest missing (apply_ap041.py not run?): " + ManifestPath, ManifestPath);
            }

            List<string> problems = VerifySources();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + "Source check failed (" + problems.Count + "):\n" + string.Join("\n", problems));
            }

            int reimported = ArtVfxImporter.ReimportOutOfContractTextures();
            List<string> contract = ValidateImports(out double mb);
            if (contract.Count > 0)
            {
                throw new InvalidOperationException(LogPrefix + "Import contract violated (" + contract.Count + "):\n" + string.Join("\n", contract));
            }

            if (mb > ArtVfxContract.ShippedAp041BudgetMb)
            {
                throw new InvalidOperationException(LogPrefix + "Shipped estimate " + mb.ToString("F1") + " MB > " + ArtVfxContract.ShippedAp041BudgetMb + " MB line.");
            }

            VfxCatalog catalog = ArtVfxImporter.BuildCatalog();
            int rows = 0;
            foreach (VfxCatalogEntry e in catalog.Entries) if (e.ManifestFile == ManifestName) rows++;
            if (rows != Table.Length)
            {
                throw new InvalidOperationException(LogPrefix + "Catalog holds " + rows + " AP-041 rows, expected " + Table.Length);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run: " + rows + " rows, " + reimported + " re-imported, shipped ≈ " + mb.ToString("F1") + " / " + ArtVfxContract.ShippedAp041BudgetMb +
                      " MB, catalog " + catalog.Entries.Count + " rows, " + (total.ElapsedMilliseconds / 1000f).ToString("F1") + " s");
        }

        /// <summary>Every sheet on disk, its sha256 equal to the delivery manifest's, a licence row per sheet.</summary>
        public static List<string> VerifySources()
        {
            var v = new List<string>();
            Dictionary<string, Dictionary<string, object>> items = ManifestItems();
            string licences = File.Exists(LicensesPath) ? File.ReadAllText(LicensesPath) : null;
            if (licences == null) v.Add(LicensesPath + ": missing");
            foreach (Entry t in Table)
            {
                string path = Root + "/" + t.File;
                if (!File.Exists(path)) { v.Add(path + ": missing"); continue; }
                if (!items.TryGetValue(t.File, out Dictionary<string, object> a)) { v.Add(t.File + ": no manifest item"); continue; }
                string want = OptString(a, "sha256");
                string have = Sha256(path);
                if (!string.Equals(want, have, StringComparison.OrdinalIgnoreCase)) v.Add(path + ": sha256 " + have + " ≠ manifest " + want);
                if (licences != null && licences.IndexOf(Path.GetFileName(t.File), StringComparison.Ordinal) < 0) v.Add(Path.GetFileName(t.File) + ": no licence row");
            }

            return v;
        }

        /// <summary>The import contract of every sheet (ArtVfxContract) and the shipped estimate (PNG header size, ship cap, mips).</summary>
        public static List<string> ValidateImports(out double shippedMb)
        {
            var v = new List<string>();
            long bytes = 0;
            foreach (string path in TextureFiles())
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) { v.Add(path + ": importer missing"); continue; }
                List<string> c = ArtVfxContract.Validate(importer);
                if (c.Count > 0) v.Add(path + ": " + string.Join("; ", c));
                PngSize(path, out int w, out int h);
                bytes += ArtVfxContract.EstimatedShippedBytes(w, h, path, importer.mipmapEnabled);
            }

            shippedMb = bytes / 1048576.0;
            return v;
        }

        public static void PngSize(string path, out int width, out int height)
        {
            byte[] head = new byte[24];
            using (FileStream fs = File.OpenRead(path)) fs.Read(head, 0, 24);
            width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
        }

        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string OptString(Dictionary<string, object> a, string key)
        {
            return a.TryGetValue(key, out object v) && v != null && !(v is Dictionary<string, object>) && !(v is List<object>) ? ArtJson.AsString(v, key) : string.Empty;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
