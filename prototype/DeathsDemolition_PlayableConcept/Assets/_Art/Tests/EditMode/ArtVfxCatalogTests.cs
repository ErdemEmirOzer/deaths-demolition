using System;
using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-019 · AP-010 kapıları: 34 satırlık katalog manifestle birebir, doku sözleşmesi
    /// (ArtVfxContract), materyaller ve blend'ler, VFX doku bütçesi, dönüştürücü idempotansı,
    /// yüzey eşleme tablosu, game-feel sınırları.
    /// </summary>
    [TestFixture]
    [Category("WP019")]
    public sealed class ArtVfxCatalogTests
    {
        private static Dictionary<string, Dictionary<string, object>> ManifestRows()
        {
            var rows = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            var paths = new List<string> { ArtVfxContract.ManifestPath };
            paths.AddRange(ArtVfxImporter.SupplementManifests());   // WP-047E: AP010C (AP-014C sheets) rides the same schema
            foreach (string path in paths)
            {
                var manifest = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(path)), "manifest");
                foreach (object o in ArtJson.AsList(manifest["assets"], "assets"))
                {
                    var a = ArtJson.AsObject(o, "asset");
                    rows[ArtVfxImporter.IdOf(ArtJson.AsString(a["file"], "file"))] = a;
                }
            }

            return rows;
        }

        [Test]
        public void Katalog_34Satir_ManifestleBirebir()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog, "DD_VfxCatalog yok (Run VFX Catalog koşmalı).");
            Dictionary<string, Dictionary<string, object>> manifest = ManifestRows();
            Assert.AreEqual(113, manifest.Count, "Manifest v004 109 varlık (v003 108 + fb_armor_break, WP-047H) + AP010C 4 (WP-047E, AP-014C).");
            // WP-019b: 108 v003 manifest rows + the synthesized blood_droplet row; WP-047E: + 4 AP010C rows; WP-047H: + fb_armor_break (v004).
            Assert.AreEqual(114, catalog.Entries.Count, "Katalog 113 manifest satırı + 1 sentez (blood_droplet).");

            var hatalar = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (VfxCatalogEntry e in catalog.Entries)
            {
                if (!ids.Add(e.Id)) hatalar.Add(e.Id + ": çift id");
                if (e.ManifestFile == "synthesized") { if (e.Id != VfxIds.BloodDroplet) hatalar.Add(e.Id + ": beklenmeyen sentez satırı"); continue; }
                if (!manifest.TryGetValue(e.Id, out Dictionary<string, object> a)) { hatalar.Add(e.Id + ": manifestte yok"); continue; }
                // Schema v003: grid may be null (1×1), world_size_m a number / [w, h] / string, capsule_cover_s replaces readability_limit_s.
                List<object> grid = a.TryGetValue("grid", out object gr) && gr != null ? ArtJson.AsList(gr, "grid") : null;
                int cols = grid != null ? ArtJson.AsInt(grid[0]) : 1, rws = grid != null ? ArtJson.AsInt(grid[1]) : 1;
                if (e.Columns != cols || e.Rows != rws) hatalar.Add(e.Id + ": grid " + e.Columns + "x" + e.Rows);
                if (e.FramesUsed != Mathf.Max(1, a["frames_used"] != null ? ArtJson.AsInt(a["frames_used"]) : 1)) hatalar.Add(e.Id + ": frames_used " + e.FramesUsed);
                if (Mathf.Abs(e.Fps - (a["fps"] != null ? ArtJson.AsFloat(a["fps"]) : 0f)) > 0.01f) hatalar.Add(e.Id + ": fps " + e.Fps);
                if (Mathf.Abs(e.DurationSeconds - (a["duration_s"] != null ? ArtJson.AsFloat(a["duration_s"]) : 0f)) > 0.001f) hatalar.Add(e.Id + ": duration " + e.DurationSeconds);
                if (e.Loop != (a["loop"] != null && ArtJson.AsBool(a["loop"]))) hatalar.Add(e.Id + ": loop");
                object wsv = a.TryGetValue("world_size_m", out object w0) ? w0 : null;
                float ws = wsv is List<object> wl ? ArtJson.AsFloat(wl[0]) : wsv is string || wsv == null ? 0f : ArtJson.AsFloat(wsv);
                if (Mathf.Abs(e.WorldSizeMetres - ws) > 0.001f) hatalar.Add(e.Id + ": world size " + e.WorldSizeMetres);
                float cover = a.TryGetValue("capsule_cover_s", out object cc) && cc != null ? ArtJson.AsFloat(cc) : a.TryGetValue("readability_limit_s", out object rl) && rl != null ? ArtJson.AsFloat(rl) : 0f;
                if (Mathf.Abs(e.ReadabilityLimitSeconds - cover) > 0.001f) hatalar.Add(e.Id + ": readability " + e.ReadabilityLimitSeconds);
                if (e.FramesUsed > e.Columns * e.Rows) hatalar.Add(e.Id + ": frames_used > cells");

                bool review = e.Kind == VfxKind.Review;
                bool mesh = e.Kind == VfxKind.Mesh;
                if (!review && !mesh && e.Texture == null) hatalar.Add(e.Id + ": doku yok");
                if (mesh && (e.Mesh == null || e.Material == null)) hatalar.Add(e.Id + ": mesh/materyal yok");
                if (mesh && e.Mesh != null && e.Mesh.triangles.Length / 3 != 480) hatalar.Add(e.Id + ": üçgen " + e.Mesh.triangles.Length / 3 + " ≠ 480 (manifest)");
                if ((e.Kind == VfxKind.Flipbook || e.Kind == VfxKind.Decal || e.Kind == VfxKind.Card) && e.Material == null) hatalar.Add(e.Id + ": oynatılabilir satırda materyal yok");
                if (a.TryGetValue("emissive_file", out object em) && em != null && e.Emissive == null) hatalar.Add(e.Id + ": emissive bağlı değil");
            }

            // The runtime ids the dressing layer asks for must all exist.
            foreach (string id in new[] { VfxIds.ImpactFlesh, VfxIds.ImpactArmor, VfxIds.ImpactStone, VfxIds.ImpactWood, VfxIds.ImpactEarth, VfxIds.Muzzle, VfxIds.Explosion,
                VfxIds.Shockwave, VfxIds.ZombieDeath, VfxIds.BeaconRingAnim, VfxIds.DecalImpactStone, VfxIds.DecalImpactWood, VfxIds.DecalImpactEarth, VfxIds.DecalScorch,
                VfxIds.DecalLacquerSplinter, VfxIds.DecalZombieSplat01, VfxIds.DecalZombieSplat02, VfxIds.CardSmoke, VfxIds.AtlasArmorBreak, VfxIds.CardGoldSparkle,
                VfxIds.CardGoldTrail, VfxIds.GoldPackMesh, VfxIds.BeaconColumn, VfxIds.BeaconRing, VfxIds.HudMarker, VfxIds.VignetteDamage })
            {
                if (catalog.Find(id) == null) hatalar.Add("VfxIds." + id + " katalogda yok");
            }

            Assert.IsEmpty(hatalar, "Katalog ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            int aile = 0; foreach (VfxFamily f in Enum.GetValues(typeof(VfxFamily))) { foreach (VfxCatalogEntry e in catalog.Entries) if (e.Family == f) { aile++; break; } }
            Assert.GreaterOrEqual(aile, 12, "8 AP-010 ailesi + Blood + wave-2 aileleri (weapon/world/enemy/player).");
            Debug.Log("[DD-TEST] wp019.catalog rows=" + catalog.Entries.Count + " families=" + aile + " version=" + catalog.ManifestVersion);
        }

        [Test]
        public void VfxDokulari_SozlesmeyeUyar_ButceIcinde()
        {
            var ihlaller = new List<string>();
            long bytes = 0, bytesMain = 0, bytesSupplement = 0; int n = 0;
            foreach (string yol in ArtAssetGateTests.ArtVfxManifestPngs())
            {
                var importer = AssetImporter.GetAtPath(yol) as TextureImporter;
                if (importer == null) { ihlaller.Add(yol + ": importer yok"); continue; }
                List<string> v = ArtVfxContract.Validate(importer);
                if (v.Count > 0) ihlaller.Add(yol + ": " + string.Join("; ", v));
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(yol);
                if (tex == null) { ihlaller.Add(yol + ": Texture2D yok"); continue; }
                // Source size from the PNG header (the importer may have shipped it smaller).
                byte[] head = new byte[24];
                using (FileStream fs = File.OpenRead(yol)) fs.Read(head, 0, 24);
                int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                long est = ArtVfxContract.EstimatedShippedBytes(w, h, yol, importer.mipmapEnabled);
                bytes += est;
                if (ArtVfxContract.IsSupplementAsset(yol)) bytesSupplement += est; else bytesMain += est;
                n++;
            }

            double mb = bytes / 1048576.0, mbMain = bytesMain / 1048576.0, mbBlood = bytesSupplement / 1048576.0;
            Debug.Log("[DD-TEST] wp019.vfxTextures count=" + n + " estMB=" + mb.ToString("F1") + " main=" + mbMain.ToString("F1") + "/" + ArtVfxContract.ShippedVfxBudgetMb +
                      " ap010b=" + mbBlood.ToString("F1") + "/" + ArtVfxContract.ShippedBloodBudgetMb);
            Assert.IsEmpty(ihlaller, "VFX doku sözleşme ihlali (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            Assert.AreEqual(115, n);   // PNGs (rows + emissive): WP-047E + AP010C 4; WP-047H + fb_armor_break sheet + emissive
            Assert.LessOrEqual(mbMain, ArtVfxContract.ShippedVfxBudgetMb, "VFX ana satırı bütçeyi aşıyor (ADR-019-1).");
            Assert.LessOrEqual(mbBlood, ArtVfxContract.ShippedBloodBudgetMb, "AP-010B satırı bütçeyi aşıyor (C-00 kuyruk notu ≤ 60 MB).");
        }

        [Test]
        public void Materyaller_FlipbookShader_BlendVeKuyruk()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog);
            Shader flipbook = Shader.Find(ArtVfxImporter.FlipbookShaderName);
            Assert.IsNotNull(flipbook, "DD/VFX Flipbook yok.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(flipbook), "DD/VFX Flipbook derleme hatası.");
            var hatalar = new List<string>();
            int additive = 0, alpha = 0;
            foreach (VfxCatalogEntry e in catalog.Entries)
            {
                Material m = e.Material;
                if (m == null) continue;
                if (e.Kind == VfxKind.Mesh)
                {
                    if (m.shader.name != ArtMaterialBuilder.LitShaderName) hatalar.Add(e.Id + ": Lit olmalı");
                    if (!m.IsKeywordEnabled("_EMISSION") || m.GetColor("_EmissionColor").r < 1.5f) hatalar.Add(e.Id + ": HDR emissive (~2) olmalı (bloom eşiği 1.25)");
                    continue;
                }

                if (m.shader != flipbook) { hatalar.Add(e.Id + ": shader " + m.shader.name); continue; }
                if (m.GetTexture("_Sheet") != e.Texture) hatalar.Add(e.Id + ": _Sheet satırın dokusu değil");
                bool particleAtlas = e.Id == VfxIds.BloodDroplet;   // the particle system picks the cell; the material grid is 1×1
                if (!particleAtlas && (Mathf.Abs(m.GetFloat("_Cols") - e.Columns) > 0.01f || Mathf.Abs(m.GetFloat("_Rows") - e.Rows) > 0.01f)) hatalar.Add(e.Id + ": grid");
                bool isAdditive = m.GetFloat("_SrcBlend") == (float)BlendMode.One;
                if (isAdditive != e.Additive) hatalar.Add(e.Id + ": blend " + isAdditive + " ≠ " + e.Additive);
                if (isAdditive) additive++; else alpha++;
                if ((e.Emissive != null) != (m.GetFloat("_UseEmissive") > 0.5f)) hatalar.Add(e.Id + ": emissive bayrağı");
                if (m.renderQueue < (int)RenderQueue.Transparent - 20 || m.renderQueue > (int)RenderQueue.Transparent + 20) hatalar.Add(e.Id + ": kuyruk " + m.renderQueue);
                if (e.Kind == VfxKind.Decal && m.renderQueue >= (int)RenderQueue.Transparent) hatalar.Add(e.Id + ": dekal saydamlardan önce çizilmeli");
                if (!m.enableInstancing) hatalar.Add(e.Id + ": instancing");
            }

            Assert.IsEmpty(hatalar, "Materyal ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.Greater(additive, 0); Assert.Greater(alpha, 0);
            Debug.Log("[DD-TEST] wp019.materials additive=" + additive + " alpha=" + alpha);
        }

        [Test]
        public void Donusturucu_Idempotan_IkinciKosuDiffSifir()
        {
            string once = File.ReadAllText(ArtVfxImporter.CatalogPath);
            var onceMat = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { ArtVfxImporter.MaterialsFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                onceMat[p] = EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(p));
            }

            ArtVfxImporter.BuildCatalog();
            AssetDatabase.SaveAssets();

            Assert.AreEqual(once, File.ReadAllText(ArtVfxImporter.CatalogPath), "Katalog ikinci koşuda değişti.");
            var farkli = new List<string>();
            foreach (KeyValuePair<string, string> kv in onceMat)
            {
                if (EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(kv.Key)) != kv.Value) farkli.Add(kv.Key);
            }

            Assert.IsEmpty(farkli, "İkinci koşuda değişen materyal: " + string.Join(", ", farkli));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameFeelSettings>(ArtVfxImporter.GameFeelPath), "DD_GameFeel yok.");
        }

        [Test]
        public void KimlikTuretme_BoyutVeSurumEkiDuser()
        {
            Assert.AreEqual("fb_impact_flesh", ArtVfxImporter.IdOf("flipbooks/AP010_fb_impact_flesh_8x8_v002.png"));
            Assert.AreEqual("decal_zombie_splat_01", ArtVfxImporter.IdOf("decals/AP010_decal_zombie_splat_01_1024.png"));
            Assert.AreEqual("beacon_column", ArtVfxImporter.IdOf("beacon/AP010_beacon_column_512x2048.png"));
            Assert.AreEqual("goldpack_V01", ArtVfxImporter.IdOf("gold/AP010_goldpack_V01.fbx"));
            Assert.AreEqual("goldpack_V01_review", ArtVfxImporter.IdOf("gold/AP010_goldpack_V01.glb"));
            Assert.AreEqual("decal_impact_stone_normal", ArtVfxImporter.IdOf("decals/AP010_decal_impact_stone_normal_1024.png"));
        }

        [Test]
        public void YuzeyEslemesi_MateryalRolundenYuzey()
        {
            Assert.AreEqual(SurfaceKind.Stone, SurfaceResolver.FromMaterialName("M_stone"));
            Assert.AreEqual(SurfaceKind.Stone, SurfaceResolver.FromMaterialName("M_stone_weathered"));
            Assert.AreEqual(SurfaceKind.Stone, SurfaceResolver.FromMaterialName("M_plaster_hf003"));
            Assert.AreEqual(SurfaceKind.Stone, SurfaceResolver.FromMaterialName("M_roof"));
            Assert.AreEqual(SurfaceKind.Wood, SurfaceResolver.FromMaterialName("M_wood"));
            Assert.AreEqual(SurfaceKind.Wood, SurfaceResolver.FromMaterialName("M_bark_02"));
            Assert.AreEqual(SurfaceKind.Wood, SurfaceResolver.FromMaterialName("M_bamboo_culm"));
            Assert.AreEqual(SurfaceKind.Wood, SurfaceResolver.FromMaterialName("M_prop_handcart"));
            Assert.AreEqual(SurfaceKind.Dirt, SurfaceResolver.FromMaterialName("M_earth_01"));
            Assert.AreEqual(SurfaceKind.Dirt, SurfaceResolver.FromMaterialName("M_gravel"));
            Assert.AreEqual(SurfaceKind.Dirt, SurfaceResolver.FromMaterialName("M_moss_03"));
            Assert.AreEqual(SurfaceKind.Foliage, SurfaceResolver.FromMaterialName("M_sakura_foliage_01"));
            Assert.AreEqual(SurfaceKind.Foliage, SurfaceResolver.FromMaterialName("M_green_foliage_02_solid"));
            Assert.AreEqual(SurfaceKind.Water, SurfaceResolver.FromMaterialName("M_pond_water"));
            Assert.AreEqual(SurfaceKind.Default, SurfaceResolver.FromMaterialName("World/PetalParticle"));
            Assert.AreEqual(SurfaceKind.Default, SurfaceResolver.FromMaterialName(null));
        }

        [Test]
        public void GameFeel_SinirlarKirpilir_KapaliSifir()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameFeelSettings>(ArtVfxImporter.GameFeelPath);
            Assert.IsNotNull(feel);
            Assert.LessOrEqual(feel.HitStopArmorBreak, GameFeelSettings.MaxHitStopSeconds);
            Assert.LessOrEqual(feel.HitStopDeath, GameFeelSettings.MaxHitStopSeconds);
            Assert.LessOrEqual(feel.ShakeExplosionAmplitude, GameFeelSettings.MaxShakeAmplitude);
            Assert.LessOrEqual(feel.ShakeExplosionSeconds, GameFeelSettings.MaxShakeSeconds);
            Assert.LessOrEqual(feel.ShakePlayerDamageAmplitude, GameFeelSettings.MaxShakeAmplitude);
            Assert.LessOrEqual(feel.VignetteFlashAlpha, GameFeelSettings.MaxVignetteAlpha);
            Assert.LessOrEqual(feel.VignetteLowHealthAlpha, GameFeelSettings.MaxVignetteAlpha);
            Assert.AreEqual(0.040f, GameFeelSettings.MaxHitStopSeconds, 1e-6f);
            Assert.AreEqual(0.15f, GameFeelSettings.MaxShakeAmplitude, 1e-6f);
            Assert.AreEqual(0.20f, GameFeelSettings.MaxShakeSeconds, 1e-6f);
            Assert.AreEqual(0.5f, GameFeelSettings.MaxVignetteAlpha, 1e-6f);

            // A disabled layer reports zero everywhere (A/B switch).
            var off = ScriptableObject.CreateInstance<GameFeelSettings>();
            try
            {
                var so = new SerializedObject(off); so.FindProperty("_enabled").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(0f, off.HitStopArmorBreak); Assert.AreEqual(0f, off.ShakeExplosionAmplitude); Assert.AreEqual(0f, off.VignetteFlashAlpha);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(off);
            }

            // WP-062H: the runtime master (FeelConfig `enabled`, the F1 "Game Feel (A/B)" toggle) switches the asset off too;
            // back on, the WP-062 values return unchanged.
            float onValue = feel.HitStopArmorBreak;
            Assert.Greater(onValue, 0f, "varsayılan his: zırh kırılma hit-stop'u > 0");
            try
            {
                FeelRuntime.SetEnabled(false);
                Assert.IsFalse(FeelRuntime.EnabledAfterCommit, "A/B kapalı sıraya girdi");
                FeelRuntime.BeginFrame(-101);
                Assert.IsFalse(feel.Enabled);
                Assert.AreEqual(0f, feel.HitStopArmorBreak); Assert.AreEqual(0f, feel.ShakeExplosionAmplitude); Assert.AreEqual(0f, feel.VignetteFlashAlpha);
                Assert.AreEqual(1f, feel.DeathSlowScale, "kapalı katmanda ölüm yavaşlaması yok");
                FeelRuntime.SetEnabled(true);
                FeelRuntime.BeginFrame(-102);
                Assert.IsTrue(feel.Enabled);
                Assert.AreEqual(onValue, feel.HitStopArmorBreak, 1e-6f, "A/B geri açılınca değerler aynen döner");
            }
            finally
            {
                FeelRuntime.ResetForTests();
            }
        }

        [Test]
        public void HavuzKapasiteleri_VeVfxKutuphanesi()
        {
            Assert.GreaterOrEqual(VfxDressing.FlipbookCapacity, 32);
            Assert.GreaterOrEqual(VfxDressing.DecalCapacity, 32);
            Assert.GreaterOrEqual(VfxDressing.FragmentBurstCapacity, 4);
            Assert.GreaterOrEqual(VfxDressing.SmokeBurstCapacity, 4);
            // The Resources door the runtime uses.
            var catalog = Resources.Load<VfxCatalog>(VfxCatalog.ResourceName);
            Assert.IsNotNull(catalog, "Resources.Load(DD_VfxCatalog) boş.");
            Assert.IsNotNull(Resources.Load<GameFeelSettings>(GameFeelSettings.ResourceName), "Resources.Load(DD_GameFeel) boş.");
        }
    }
}
