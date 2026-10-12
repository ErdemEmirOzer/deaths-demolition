using System;
using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-101 (R4, AP-041) · AP-041 v001 kapıları: içe alma tablosu ↔ kırpılmış manifest (33 satır, sha256 Codex'inki, lisans
    /// satırı), katalog satırları manifestle birebir ve premultiplied, materyaller DD/VFX Flipbook PM yolu (One /
    /// OneMinusSrcAlpha, _Premultiplied 1), doku sözleşmesi (Alpha Is Transparency kapalı, sRGB, mip, Clamp, BC7, 1024 / 512)
    /// ve kendi bütçe satırı; AP-010 materyallerinde PM bayrağı 0 (bugünkü davranış birebir). İçe alma yoksa ilgili testler
    /// Ignore (çekirdek sözleşme testleri her checkout'ta koşar).
    /// </summary>
    [TestFixture]
    [Category("WP101")]
    public sealed class Wp101Ap041CatalogTests
    {
        private static void IceAlmaGerekli()
        {
            if (!ArtAp041VfxIntake.Active) Assert.Ignore("AP-041 v001 içe alınmamış (" + ArtAp041VfxIntake.ManifestPath + " yok): apply_ap041.py + ArtAp041VfxIntake.Run.");
        }

        [Test]
        public void Sozlesme_Ap041Yolu_PmVeBoyutlar()
        {
            const string impact = ArtAp041VfxIntake.Root + "/impacts/H_impact_stone_srgb_pm.png";
            const string muzzle = ArtAp041VfxIntake.Root + "/muzzle/H_muzzle_rifle_TD_var01_srgb_pm.png";
            const string hole = ArtAp041VfxIntake.Root + "/decals/H_decal_wood_var03_srgb_pm.png";
            foreach (string p in new[] { impact, muzzle, hole })
            {
                Assert.IsTrue(ArtVfxContract.IsVfxAsset(p), p);
                Assert.IsTrue(ArtVfxContract.IsVfxTexture(p), p);
                Assert.IsTrue(ArtVfxContract.IsPremultipliedTexture(p), p);
                Assert.IsFalse(ArtVfxContract.IsSupplementAsset(p), "AP-041 AP-010B kan satırına sayılmaz: " + p);
                Assert.AreEqual(ArtVfxContract.VfxTextureRole.Sheet, ArtVfxContract.RoleOf(p));
            }

            Assert.AreEqual(1024, ArtVfxContract.MaxSizeOf(impact));
            Assert.AreEqual(512, ArtVfxContract.MaxSizeOf(muzzle));
            Assert.AreEqual(512, ArtVfxContract.MaxSizeOf(hole));
            // AP-010 rows untouched.
            Assert.IsFalse(ArtVfxContract.IsPremultipliedTexture("Assets/_Art/VFX/AP010/flipbooks/AP010_fb_impact_stone_8x8_v003.png"));
            Assert.AreEqual(1024, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010/flipbooks/AP010_fb_impact_stone_8x8_v003.png"));
            Assert.AreEqual(256, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010/decals/AP010_decal_impact_stone_1024.png"));
            Assert.IsFalse(ArtVfxContract.IsVfxAsset("Assets/_Art/VFX/AP0411/x.png"));
        }

        [Test]
        public void Tablo_33Satir_KimliklerVfxIdsIleBirebir()
        {
            Assert.AreEqual(ArtAp041VfxIntake.ExpectedTextures, ArtAp041VfxIntake.Table.Length);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArtAp041VfxIntake.Entry e in ArtAp041VfxIntake.Table)
            {
                Assert.IsTrue(ids.Add(e.Id), "çift id " + e.Id);
                Assert.IsTrue(files.Add(e.File), "çift dosya " + e.File);
                Assert.IsTrue(e.File.EndsWith("_srgb_pm.png", StringComparison.Ordinal), e.File);
                Assert.AreEqual(e.Kind == VfxKind.Decal, e.Id.StartsWith("decal_ap041_", StringComparison.Ordinal), e.Id);
            }

            foreach (string[] cls in VfxIds.Ap041MuzzleTd) foreach (string id in cls) Assert.IsTrue(ids.Contains(id), id);
            foreach (string[] set in VfxIds.Ap041Decals) foreach (string id in set) Assert.IsTrue(ids.Contains(id), id);
            foreach (string id in new[] { VfxIds.Ap041MuzzleSmoke, VfxIds.Ap041ImpactStone, VfxIds.Ap041ImpactWood, VfxIds.Ap041ImpactEarth, VfxIds.Ap041ImpactMetal, VfxIds.Ap041ImpactFlesh })
                Assert.IsTrue(ids.Contains(id), id);
        }

        [Test]
        public void Shader_PremultipliedOzelligi_Varsayilan0_AP010MateryalleriDegismez()
        {
            Shader flipbook = Shader.Find(ArtVfxImporter.FlipbookShaderName);
            Assert.IsNotNull(flipbook);
            Assert.IsFalse(ShaderUtil.ShaderHasError(flipbook), "DD/VFX Flipbook derleme hatası");
            int index = flipbook.FindPropertyIndex("_Premultiplied");
            Assert.GreaterOrEqual(index, 0, "_Premultiplied özelliği yok");
            Assert.AreEqual(0f, flipbook.GetPropertyDefaultFloatValue(index), "varsayılan 0 = bugünkü straight-alpha yolu");

            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog);
            var hatalar = new List<string>();
            foreach (VfxCatalogEntry e in catalog.Entries)
            {
                if (e.Material == null || e.Material.shader != flipbook || e.Premultiplied) continue;
                if (e.Material.GetFloat("_Premultiplied") != 0f) hatalar.Add(e.Id);
            }

            Assert.IsEmpty(hatalar, "AP-010 materyalinde PM bayrağı açık: " + string.Join(", ", hatalar));
        }

        [Test]
        public void Manifest_TabloyaBirebir_ShaVeLisans()
        {
            IceAlmaGerekli();
            Dictionary<string, Dictionary<string, object>> items = ArtAp041VfxIntake.ManifestItems();
            Assert.AreEqual(ArtAp041VfxIntake.ExpectedTextures, items.Count, "kırpılmış manifest yalnız WP-101 satırlarını taşır");
            var hatalar = new List<string>();
            foreach (ArtAp041VfxIntake.Entry t in ArtAp041VfxIntake.Table)
            {
                if (!items.TryGetValue(t.File, out Dictionary<string, object> a)) { hatalar.Add(t.File + ": manifestte yok"); continue; }
                if (!ArtJson.AsBool(a["premultiplied"])) hatalar.Add(t.File + ": premultiplied değil");
                if (ArtJson.AsString(a["color_space"]) != "sRGB") hatalar.Add(t.File + ": color_space");
                if (ArtJson.AsString(a["unity_id"]) != t.Id) hatalar.Add(t.File + ": unity_id");
                List<object> grid = ArtJson.AsList(a["grid"]);
                int cells = ArtJson.AsInt(grid[0]) * ArtJson.AsInt(grid[1]);
                int frames = ArtJson.AsInt(a["frames_used"]);
                if (frames < 1 || frames > cells) hatalar.Add(t.File + ": frames_used " + frames + " / " + cells);
                if (t.Kind == VfxKind.Decal && cells != 1) hatalar.Add(t.File + ": delik tek hücre olmalı");
                if (Ap041Vfx.IsMuzzleFlashId(t.Id) && (frames != 6 || Mathf.Abs(ArtJson.AsFloat(a["duration_s"]) - 0.05f) > 1e-4f))
                    hatalar.Add(t.File + ": TD namlu 6 kare / 0,05 s değil");
            }

            hatalar.AddRange(ArtAp041VfxIntake.VerifySources());   // sha256 = Codex manifest; one licence row per sheet
            Assert.IsEmpty(hatalar, "AP-041 manifest / kaynak ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }

        [Test]
        public void Katalog_33PmSatir_ManifestleBirebir_Materyaller()
        {
            IceAlmaGerekli();
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog, "DD_VfxCatalog yok (ArtAp041VfxIntake.Run).");
            Dictionary<string, Dictionary<string, object>> items = ArtAp041VfxIntake.ManifestItems();
            Shader flipbook = Shader.Find(ArtVfxImporter.FlipbookShaderName);
            var hatalar = new List<string>();
            int n = 0;
            foreach (ArtAp041VfxIntake.Entry t in ArtAp041VfxIntake.Table)
            {
                VfxCatalogEntry e = catalog.Find(t.Id);
                if (e == null) { hatalar.Add(t.Id + ": katalogda yok"); continue; }
                n++;
                Dictionary<string, object> a = items[t.File];
                List<object> grid = ArtJson.AsList(a["grid"]);
                List<object> ws = ArtJson.AsList(a["world_size_m"]);
                List<object> uv = ArtJson.AsList(a["origin_uv"]);
                if (!e.Premultiplied || e.Additive) hatalar.Add(t.Id + ": PM / additive bayrağı");
                if (e.ManifestFile != ArtAp041VfxIntake.ManifestName) hatalar.Add(t.Id + ": ManifestFile " + e.ManifestFile);
                if (e.Kind != t.Kind || e.Family != t.Family) hatalar.Add(t.Id + ": kind / family");
                if (e.Columns != ArtJson.AsInt(grid[0]) || e.Rows != ArtJson.AsInt(grid[1])) hatalar.Add(t.Id + ": grid");
                if (e.FramesUsed != ArtJson.AsInt(a["frames_used"])) hatalar.Add(t.Id + ": frames_used");
                if (Mathf.Abs(e.Fps - ArtJson.AsFloat(a["fps"])) > 0.01f) hatalar.Add(t.Id + ": fps");
                if (Mathf.Abs(e.DurationSeconds - ArtJson.AsFloat(a["duration_s"])) > 0.001f) hatalar.Add(t.Id + ": duration");
                if (Mathf.Abs(e.WorldSizeMetres - ArtJson.AsFloat(ws[0])) > 0.001f || Mathf.Abs(e.WorldHeightMetres - ArtJson.AsFloat(ws[1])) > 0.001f) hatalar.Add(t.Id + ": [w, h]");
                if ((e.OriginUv - new Vector2(ArtJson.AsFloat(uv[0]), ArtJson.AsFloat(uv[1]))).sqrMagnitude > 1e-8f) hatalar.Add(t.Id + ": origin_uv");
                if (e.Sha256 != ArtJson.AsString(a["sha256"])) hatalar.Add(t.Id + ": sha256");
                if (e.Texture == null || AssetDatabase.GetAssetPath(e.Texture) != ArtAp041VfxIntake.Root + "/" + t.File) hatalar.Add(t.Id + ": doku bağı");

                Material m = e.Material;
                if (m == null || m.shader != flipbook) { hatalar.Add(t.Id + ": materyal / shader"); continue; }
                if (m.GetTexture("_Sheet") != e.Texture) hatalar.Add(t.Id + ": _Sheet");
                if (m.GetFloat("_Premultiplied") != 1f) hatalar.Add(t.Id + ": _Premultiplied ≠ 1");
                if (m.GetFloat("_SrcBlend") != (float)BlendMode.One || m.GetFloat("_DstBlend") != (float)BlendMode.OneMinusSrcAlpha) hatalar.Add(t.Id + ": blend");
                if (m.GetFloat("_UseEmissive") != 0f) hatalar.Add(t.Id + ": emissive");
                if (Mathf.Abs(m.GetFloat("_Cols") - e.Columns) > 0.01f || Mathf.Abs(m.GetFloat("_Rows") - e.Rows) > 0.01f) hatalar.Add(t.Id + ": materyal grid");
                int expectedQueue = (int)RenderQueue.Transparent + (t.Kind == VfxKind.Decal ? -10 : Ap041Vfx.IsMuzzleFlashId(t.Id) ? 10 : 0);
                if (m.renderQueue != expectedQueue) hatalar.Add(t.Id + ": kuyruk " + m.renderQueue);
                if (!m.enableInstancing) hatalar.Add(t.Id + ": instancing");
            }

            Assert.IsEmpty(hatalar, "AP-041 katalog ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.AreEqual(ArtAp041VfxIntake.ExpectedTextures, n);
            Debug.Log("[DD-TEST] wp101.ap041.catalog rows=" + n + " total=" + catalog.Entries.Count);
        }

        [Test]
        public void Dokular_PmImportSozlesmesi_KendiButceSatirinda()
        {
            IceAlmaGerekli();
            List<string> ihlaller = ArtAp041VfxIntake.ValidateImports(out double mb);
            foreach (string path in ArtAp041VfxIntake.TextureFiles())
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                // Spelled out as well: the four settings that make a premultiplied sheet render right.
                if (importer.alphaIsTransparency) ihlaller.Add(path + ": alphaIsTransparency açık (PM'de hale)");
                if (!importer.sRGBTexture) ihlaller.Add(path + ": sRGB kapalı");
                if (!importer.mipmapEnabled) ihlaller.Add(path + ": mip kapalı");
                if (importer.wrapMode != TextureWrapMode.Clamp) ihlaller.Add(path + ": wrap " + importer.wrapMode);
                if (importer.streamingMipmaps) ihlaller.Add(path + ": streaming");
            }

            Debug.Log("[DD-TEST] wp101.ap041.textures count=" + ArtAp041VfxIntake.TextureFiles().Count + " estMB=" + mb.ToString("F1") + "/" + ArtVfxContract.ShippedAp041BudgetMb);
            Assert.IsEmpty(ihlaller, "AP-041 doku sözleşmesi (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            Assert.LessOrEqual(mb, ArtVfxContract.ShippedAp041BudgetMb, "AP-041 sevk tahmini kendi satırını aşıyor");
            Assert.Greater(mb, 10.0, "tahmin 33 doku için fazla küçük: boyut tavanı / mip okunmuyor");
        }
    }
}
