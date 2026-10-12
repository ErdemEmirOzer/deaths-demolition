using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DD.Prototype.Feedback.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-104 B4-1 (S-07) · Codex's AP-041 v001 + v002 (EK1) run-tree sheets → catalog rows + one material per row, on the same path
    /// as <see cref="ArtAp041VfxIntake"/> (WP-101):
    ///
    ///   Assets/_Art/VFX/AP041/tree/*_srgb_pm.png, G_X8_synergy_enso_mask_linear.png   45 sheets, copied byte-for-byte by
    ///                                                                              apply_b4.py (sha256 = the v002 manifest)
    ///   Assets/_Art/VFX/AP041/tree/AP041-vfx-manifest-tree-v002.json                 the used rows of AP041-manifest-v002.json
    ///   Assets/_Art/VFX/AP041/tree/AP041-tree-LICENSES.csv                           one register row per sheet (LICENSES-v002.csv)
    ///   Assets/_Art/VFX/AP041/tree/Materials/M_vfx_&lt;id&gt;.mat                       DD/VFX Flipbook, _Premultiplied 1
    ///
    /// Ids are <see cref="TreeVfxIds"/> (one per <see cref="TreeVfxRow"/>); grid, frames, fps, duration, [w, h] and origin come from the
    /// manifest. The X8 ensō is a LINEAR R/G half-coverage mask (not a colour sheet): sRGB off, no mips, and its material runs
    /// DD/VFX Flipbook's <c>_RgMask</c> path (rgb = tint × R + tint right × G, a = R + G — premultiplied by construction).
    /// Ship sizes per row (<see cref="ShipSizeOf"/>, ArtVfxContract reads them): what the TD camera needs (≈ 95 px/m at 1080p) — 8×8 nuke
    /// sheets 2048, the large rings / sumi / ensō 1024, the rest 512 / 256; own budget line <see cref="ShippedBudgetMb"/>.
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtAp041TreeIntake.Run</c> (AP-041 v001 is on main since WP-101: no other step first).
    /// Absent manifest = no rows, no error (the binder then plays nothing and says so: TreeVfxBinder.RowsReady false).
    /// </summary>
    public static class ArtAp041TreeIntake
    {
        public const string Root = ArtVfxContract.Ap041Root + "/tree";
        public const string ManifestName = "AP041-vfx-manifest-tree-v002.json";
        public const string ManifestPath = Root + "/" + ManifestName;
        public const string LicensesPath = Root + "/AP041-tree-LICENSES.csv";
        public const string MaterialsFolder = Root + "/Materials";
        /// <summary>Resident line of the tree rows: 2 × 2048² + 12 × 1024² + 25 × 512² + 6 × 256² BC7 with mips (the mask without) ≈ 34.2 MB.</summary>
        public const int ShippedBudgetMb = 36;
        private const string LogPrefix = "[ArtAp041Tree] ";

        public static bool Active => File.Exists(ManifestPath);

        public readonly struct Entry
        {
            public readonly TreeVfxRow Row;
            public readonly string File;
            public readonly VfxFamily Family;
            public readonly VfxKind Kind;
            public readonly int Ship;

            public Entry(TreeVfxRow row, string file, VfxFamily family, VfxKind kind, int ship)
            {
                Row = row;
                File = file;
                Family = family;
                Kind = kind;
                Ship = ship;
            }

            public string Id => TreeVfxIds.Id(Row);
            public bool Mask => File.EndsWith("_linear.png", StringComparison.Ordinal);
        }

        private static Entry E(TreeVfxRow row, string file, VfxFamily family, VfxKind kind, int ship) => new Entry(row, file, family, kind, ship);

        /// <summary>The 45 rows (one per <see cref="TreeVfxRow"/>).</summary>
        public static readonly Entry[] Table =
        {
            E(TreeVfxRow.B1Ignition, "H_B1_ignition_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B1GroundScorch, "G_B1_ground_scorch_srgb_pm.png", VfxFamily.Explosion, VfxKind.Decal, 256),
            E(TreeVfxRow.BlastScorch1, "H_decal_scorch_var01_srgb_pm.png", VfxFamily.Explosion, VfxKind.Decal, 256),
            E(TreeVfxRow.BlastScorch2, "H_decal_scorch_var02_srgb_pm.png", VfxFamily.Explosion, VfxKind.Decal, 256),
            E(TreeVfxRow.BlastScorch3, "H_decal_scorch_var03_srgb_pm.png", VfxFamily.Explosion, VfxKind.Decal, 256),
            E(TreeVfxRow.BlastScorch4, "H_decal_scorch_var04_srgb_pm.png", VfxFamily.Explosion, VfxKind.Decal, 256),
            E(TreeVfxRow.B2IceImpact, "H_B2_ice_impact_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B2Shards, "G_B2_crystal_shards_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B2Coating, "G_B2_crystal_coating_srgb_pm.png", VfxFamily.Enemy, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B3Impact, "H_B3_electric_impact_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B3Flash, "G_B3_electric_flash_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B3SparkRing, "G_B3_spark_ring_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B4Cloud, "H_B4_poison_cloud_srgb_pm.png", VfxFamily.World, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B5Explosive, "H_B5_explosive_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B5Shock, "G_B5_circular_shock_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B6Singularity, "H_B6_singularity_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B6Rings, "G_B6_attraction_rings_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B7Nuclear, "H_B7_nuclear_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 2048),
            E(TreeVfxRow.B7Shock, "G_B7_nuclear_shock_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 2048),
            E(TreeVfxRow.B7HeatGlint, "G_B7_heat_glint_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B8Chain1, "H_B8_chain_blast_var01_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B8Chain2, "H_B8_chain_blast_var02_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B8Chain3, "H_B8_chain_blast_var03_srgb_pm.png", VfxFamily.Explosion, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B10MiniFlame, "G_B10_mini_rocket_flame_srgb_pm.png", VfxFamily.Weapon, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B11Ring1, "G_B11_ground_ring_tier1_blue_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B11Ring2, "G_B11_ground_ring_tier2_gold_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B11Ring3, "G_B11_ground_ring_tier3_crimson_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B11Sparks1, "G_B11_rising_sparks_tier1_blue_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B11Sparks2, "G_B11_rising_sparks_tier2_gold_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B11Sparks3, "G_B11_rising_sparks_tier3_crimson_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B11Smoke3, "G_B11_sumi_smoke_tier3_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B12WhiteHot, "G_B12_muzzle_white_hot_speed_srgb_pm.png", VfxFamily.Weapon, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B13Crit, "G_B13_critical_strike_srgb_pm.png", VfxFamily.Impact, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B13LevelUp, "G_B13_levelup_radial_burst_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.B14Dome, "G_B14_shield_dome_edge_srgb_pm.png", VfxFamily.Player, VfxKind.Card, 512),
            E(TreeVfxRow.B14Hit, "G_B14_shield_hit_wave_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B14Break, "G_B14_shield_break_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B15Thread, "G_B15_life_drain_thread_srgb_pm.png", VfxFamily.Player, VfxKind.Card, 1024),
            E(TreeVfxRow.B16Ghost, "G_B16_dodge_ghost_srgb_pm.png", VfxFamily.Player, VfxKind.Card, 256),
            E(TreeVfxRow.B16Shadow, "G_B16_dodge_ink_shadow_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.B17Pulse, "G_B17_healing_ground_pulse_srgb_pm.png", VfxFamily.Player, VfxKind.Flipbook, 512),
            E(TreeVfxRow.T3InkShock, "G_T3_ink_shock_srgb_pm.png", VfxFamily.World, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.T4Enso, "G_T4_enso_attraction_srgb_pm.png", VfxFamily.World, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.T5Spatter, "G_T5_ink_spatter_srgb_pm.png", VfxFamily.World, VfxKind.Flipbook, 1024),
            E(TreeVfxRow.X8Enso, "G_X8_synergy_enso_mask_linear.png", VfxFamily.Player, VfxKind.Flipbook, 1024)
        };

        /// <summary>Ship size of a tree sheet by file stem (no extension); 0 when the file is not one of <see cref="Table"/>.</summary>
        public static int ShipSizeOf(string fileStem)
        {
            if (string.IsNullOrEmpty(fileStem)) return 0;
            foreach (Entry e in Table)
            {
                if (e.File.Length == fileStem.Length + 4 && e.File.StartsWith(fileStem, StringComparison.Ordinal)) return e.Ship;
            }

            return 0;
        }

        /// <summary>Asset paths of the imported sheets (empty when not imported) — the PNG inventory gate's list.</summary>
        public static List<string> TextureFiles()
        {
            var list = new List<string>(Table.Length);
            if (!Active) return list;
            foreach (Entry e in Table) list.Add(Root + "/" + e.File);
            return list;
        }

        /// <summary>The trimmed manifest's items by <c>file</c>.</summary>
        public static Dictionary<string, Dictionary<string, object>> ManifestItems()
        {
            var items = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            if (!Active) return items;
            var manifest = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(ManifestPath)), "AP041 tree manifest");
            foreach (object o in ArtJson.AsList(manifest["items"], "items"))
            {
                var a = ArtJson.AsObject(o, "item");
                items[ArtJson.AsString(a["file"], "file")] = a;
            }

            return items;
        }

        // ------------------------------------------------------------------ catalog rows

        /// <summary>Appends the tree rows (and writes their materials). Returns the rows added; 0 when not imported.</summary>
        public static int AppendRows(List<VfxCatalogEntry> rows, HashSet<string> seen, VfxCatalog catalog, Shader flipbook)
        {
            if (!Active)
            {
                Debug.Log(LogPrefix + "AP-041 tree rows not imported (" + ManifestPath + " absent): the run tree plays no AP-041 sheet.");
                return 0;
            }

            Dictionary<string, Dictionary<string, object>> items = ManifestItems();
            EnsureFolder(MaterialsFolder);
            int n = 0;
            foreach (Entry t in Table)
            {
                if (!items.TryGetValue(t.File, out Dictionary<string, object> a)) throw new InvalidOperationException(LogPrefix + "Manifest has no item for " + t.File);
                if (!seen.Add(t.Id)) throw new InvalidOperationException(LogPrefix + "Duplicate catalog id '" + t.Id + "'");
                string unityId = OptString(a, "unity_id");
                if (unityId.Length > 0 && !string.Equals(unityId, t.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(LogPrefix + t.File + ": manifest unity_id '" + unityId + "' ≠ table id '" + t.Id + "'");
                }

                if (!t.Mask && (!a.TryGetValue("premultiplied", out object pm) || pm == null || !ArtJson.AsBool(pm, "premultiplied")))
                {
                    throw new InvalidOperationException(LogPrefix + t.File + ": not premultiplied — the AP-041 colour path is PM only");
                }

                VfxCatalogEntry e = catalog.Find(t.Id) ?? new VfxCatalogEntry();
                e.Id = t.Id;
                e.Family = t.Family;
                e.Kind = t.Kind;
                e.SourceFile = "AP041/tree/" + t.File;
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
                e.UsageNote = "AP-041 run tree (WP-104 B4): " + t.File;
                e.BlendNote = OptString(a, "blend");
                e.HdrIntensity = 1f;
                e.DefaultTint = Color.white;
                e.Additive = false;
                e.Premultiplied = true;   // the mask's output is premultiplied by construction (coverage × tint)
                e.Mesh = null;
                e.Emissive = null;
                e.Normal = null;
                string assetPath = Root + "/" + t.File;
                e.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (e.Texture == null) throw new FileNotFoundException(LogPrefix + "Texture missing for " + t.Id + ": " + assetPath, assetPath);
                e.MeanOpaqueColour = Color.white;
                e.ManifestFile = ManifestName;
                e.Material = BuildMaterial(e, t, flipbook);
                rows.Add(e);
                n++;
            }

            Debug.Log(LogPrefix + "Catalog rows: " + n + " (" + ManifestPath + ")");
            return n;
        }

        /// <summary>One DD/VFX Flipbook material per row: Blend One OneMinusSrcAlpha, <c>_Premultiplied</c> 1; the ensō also <c>_RgMask</c> 1.</summary>
        private static Material BuildMaterial(VfxCatalogEntry e, Entry t, Shader flipbook)
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
            m.SetColor("_TintRight", Color.white);
            m.SetFloat("_RgMask", t.Mask ? 1f : 0f);
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
            // scorches before the transparents (as every decal); the rest with the AP-010 / AP-041 sheets
            m.renderQueue = (int)RenderQueue.Transparent + (e.Kind == VfxKind.Decal ? -10 : 0);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ batch entry

        [MenuItem("Tools/Deaths Demolition/Art Pipeline/Run AP-041 Tree VFX (WP-104 B4)", priority = 305)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException(LogPrefix + "Run cannot run in Play mode.");
            Stopwatch total = Stopwatch.StartNew();
            AssetDatabase.Refresh();
            if (!Active) throw new FileNotFoundException(LogPrefix + "manifest missing (apply_b4.py not run?): " + ManifestPath, ManifestPath);
            List<string> problems = VerifySources();
            if (problems.Count > 0) throw new InvalidOperationException(LogPrefix + "Source check failed (" + problems.Count + "):\n" + string.Join("\n", problems));
            int reimported = ArtVfxImporter.ReimportOutOfContractTextures();
            List<string> contract = ValidateImports(out double mb);
            if (contract.Count > 0) throw new InvalidOperationException(LogPrefix + "Import contract violated (" + contract.Count + "):\n" + string.Join("\n", contract));
            if (mb > ShippedBudgetMb) throw new InvalidOperationException(LogPrefix + "Shipped estimate " + mb.ToString("F1") + " MB > " + ShippedBudgetMb + " MB line.");
            VfxCatalog catalog = ArtVfxImporter.BuildCatalog();
            int rows = 0;
            foreach (VfxCatalogEntry e in catalog.Entries) if (e.ManifestFile == ManifestName) rows++;
            if (rows != Table.Length) throw new InvalidOperationException(LogPrefix + "Catalog holds " + rows + " tree rows, expected " + Table.Length);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Run: " + rows + " rows, " + reimported + " re-imported, shipped ≈ " + mb.ToString("F1") + " / " + ShippedBudgetMb +
                      " MB, catalog " + catalog.Entries.Count + " rows, " + (total.ElapsedMilliseconds / 1000f).ToString("F1") + " s");
        }

        /// <summary>Every sheet on disk, its sha256 equal to the manifest's (Codex's), a licence row per sheet.</summary>
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
                string have = ArtAp041VfxIntake.Sha256(path);
                if (!string.Equals(want, have, StringComparison.OrdinalIgnoreCase)) v.Add(path + ": sha256 " + have + " ≠ manifest " + want);
                if (licences != null && licences.IndexOf(t.File, StringComparison.Ordinal) < 0) v.Add(t.File + ": no licence row");
            }

            return v;
        }

        /// <summary>The import contract of every sheet (ArtVfxContract; the mask linear without mips) and the shipped estimate.</summary>
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
                ArtAp041VfxIntake.PngSize(path, out int w, out int h);
                bytes += ArtVfxContract.EstimatedShippedBytes(w, h, path, importer.mipmapEnabled);
            }

            shippedMb = bytes / 1048576.0;
            return v;
        }

        /// <summary>The budget estimate from the table alone (PNG sizes from the manifest; tests without the import).</summary>
        public static double EstimateShippedMb(Func<string, Vector2Int> sourceSize)
        {
            long bytes = 0;
            foreach (Entry t in Table)
            {
                Vector2Int s = sourceSize(t.File);
                int w = s.x, h = s.y;
                while (Mathf.Max(w, h) > t.Ship)
                {
                    w = Mathf.Max(1, w / 2);
                    h = Mathf.Max(1, h / 2);
                }

                while (true)
                {
                    bytes += (long)Mathf.Max(1, w) * Mathf.Max(1, h);
                    if (t.Mask || (w == 1 && h == 1)) break;
                    w = Mathf.Max(1, w / 2);
                    h = Mathf.Max(1, h / 2);
                }
            }

            return bytes / 1048576.0;
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
