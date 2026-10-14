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
    /// WP-104 B4 (S-07) · AP-041 run-tree intake gates: the table covers every TreeVfxRow once (PM colour sheets + the linear X8
    /// ensō mask), ship sizes reach ArtVfxContract (the mask linear, without mips), the budget line holds, the trimmed manifest
    /// and the sources match the delivery's (sha256, licence rows), the catalog rows are PM DD/VFX Flipbook materials and the ensō runs
    /// the <c>_RgMask</c> path, whose default (0) leaves every other material as it was. Import-dependent tests Ignore without the
    /// import (as Wp101Ap041CatalogTests); the contract tests run in every checkout.
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B4IntakeTests
    {
        private static void IceAlmaGerekli()
        {
            if (!ArtAp041TreeIntake.Active) Assert.Ignore("AP-041 ağaç satırları içe alınmamış (" + ArtAp041TreeIntake.ManifestPath + " yok): apply_b4.py + ArtAp041TreeIntake.Run.");
        }

        [Test]
        public void Tablo_HerSatirBirKez_KodlamaBoyutSozlesme()
        {
            Assert.AreEqual((int)TreeVfxRow.Count, ArtAp041TreeIntake.Table.Length);
            var rows = new HashSet<TreeVfxRow>();
            var files = new HashSet<string>(StringComparer.Ordinal);
            int masks = 0;
            foreach (ArtAp041TreeIntake.Entry e in ArtAp041TreeIntake.Table)
            {
                Assert.IsTrue(rows.Add(e.Row), "çift satır " + e.Row);
                Assert.IsTrue(files.Add(e.File), "çift dosya " + e.File);
                Assert.AreEqual(TreeVfxIds.Id(e.Row), e.Id);
                Assert.IsTrue(e.Ship == 256 || e.Ship == 512 || e.Ship == 1024 || e.Ship == 2048, e.File + ": sevk boyu " + e.Ship);
                Assert.AreEqual(e.Kind == VfxKind.Decal, e.Id.StartsWith(TreeVfxIds.DecalPrefix, StringComparison.Ordinal), e.Id);
                string path = ArtAp041TreeIntake.Root + "/" + e.File;
                string stem = System.IO.Path.GetFileNameWithoutExtension(e.File);
                Assert.AreEqual(e.Ship, ArtAp041TreeIntake.ShipSizeOf(stem), e.File);
                Assert.AreEqual(e.Ship, ArtVfxContract.MaxSizeOf(path), path + ": ArtVfxContract ağaç boyunu okumuyor");
                Assert.IsTrue(ArtVfxContract.IsVfxTexture(path), path);
                if (e.Mask)
                {
                    masks++;
                    Assert.AreEqual(TreeVfxRow.X8Enso, e.Row);
                    Assert.IsTrue(ArtVfxContract.IsLinearMaskTexture(path), path);
                    Assert.IsFalse(ArtVfxContract.IsPremultipliedTexture(path), path);
                }
                else
                {
                    Assert.IsTrue(e.File.EndsWith("_srgb_pm.png", StringComparison.Ordinal), e.File);
                    Assert.IsTrue(ArtVfxContract.IsPremultipliedTexture(path), path);
                    Assert.IsFalse(ArtVfxContract.IsLinearMaskTexture(path), path);
                }
            }

            Assert.AreEqual(1, masks, "tek veri maskesi: X8 ensō");
            Assert.AreEqual(0, ArtAp041TreeIntake.ShipSizeOf("H_impact_stone_srgb_pm"), "WP-101 satırları kendi kuralında");
            Assert.AreEqual(1024, ArtVfxContract.MaxSizeOf(ArtVfxContract.Ap041Root + "/v001/impacts/H_impact_stone_srgb_pm.png"), "WP-101 isabet 1024 değişmez");
        }

        [Test]
        public void Butce_UstSinir_SatirIcinde()
        {
            // upper bound: every source at least its ship size and square (the real sizes only shrink it; the import test re-sums)
            double mb = ArtAp041TreeIntake.EstimateShippedMb(f => new Vector2Int(2048, 2048));
            Assert.LessOrEqual(mb, ArtAp041TreeIntake.ShippedBudgetMb, "ağaç satırı bütçesi: " + mb.ToString("F1") + " MB");
        }

        [Test]
        public void Shader_RgMaskVarsayilan0_TintRightVar()
        {
            Shader flipbook = Shader.Find(ArtVfxImporter.FlipbookShaderName);
            Assert.IsNotNull(flipbook);
            Assert.IsFalse(ShaderUtil.ShaderHasError(flipbook), "DD/VFX Flipbook derleme hatası");
            int mask = flipbook.FindPropertyIndex("_RgMask");
            Assert.GreaterOrEqual(mask, 0, "_RgMask yok");
            Assert.AreEqual(0f, flipbook.GetPropertyDefaultFloatValue(mask), "varsayılan 0: renk sayfaları birebir");
            Assert.GreaterOrEqual(flipbook.FindPropertyIndex("_TintRight"), 0, "_TintRight yok");
        }

        [Test]
        public void Manifest_TabloyaBirebir_ShaLisansBoyut()
        {
            IceAlmaGerekli();
            Dictionary<string, Dictionary<string, object>> items = ArtAp041TreeIntake.ManifestItems();
            Assert.AreEqual(ArtAp041TreeIntake.Table.Length, items.Count, "kırpılmış manifest yalnız ağaç satırlarını taşır");
            var hatalar = new List<string>();
            foreach (ArtAp041TreeIntake.Entry t in ArtAp041TreeIntake.Table)
            {
                if (!items.TryGetValue(t.File, out Dictionary<string, object> a)) { hatalar.Add(t.File + ": manifestte yok"); continue; }
                if (ArtJson.AsString(a["unity_id"], "unity_id") != t.Id) hatalar.Add(t.File + ": unity_id");
                string space = ArtJson.AsString(a["color_space"], "color_space");
                bool pm = ArtJson.AsBool(a["premultiplied"], "premultiplied");
                if (t.Mask ? (pm || space != "linear") : (!pm || space != "sRGB")) hatalar.Add(t.File + ": kodlama " + space + " pm " + pm);
                if (ArtJson.AsInt(a["ship_max_size"], "ship_max_size") != t.Ship) hatalar.Add(t.File + ": ship_max_size");
                List<object> grid = ArtJson.AsList(a["grid"], "grid");
                int cells = ArtJson.AsInt(grid[0], "grid[0]") * ArtJson.AsInt(grid[1], "grid[1]");
                int frames = ArtJson.AsInt(a["frames_used"], "frames_used");
                if (frames < 1 || frames > cells) hatalar.Add(t.File + ": frames_used " + frames + " / " + cells);
            }

            hatalar.AddRange(ArtAp041TreeIntake.VerifySources());
            List<string> contract = ArtAp041TreeIntake.ValidateImports(out double mb);
            hatalar.AddRange(contract);
            Assert.IsEmpty(hatalar, "AP-041 ağaç manifest / kaynak / sözleşme ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
            Assert.LessOrEqual(mb, ArtAp041TreeIntake.ShippedBudgetMb, "ağaç satırı bütçesi (gerçek boyutlar)");
        }

        [Test]
        public void Katalog_SatirlarPm_EnsoRgMaske_DekalKuyrugu()
        {
            IceAlmaGerekli();
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog, "DD_VfxCatalog yok (ArtAp041TreeIntake.Run).");
            Shader flipbook = Shader.Find(ArtVfxImporter.FlipbookShaderName);
            var hatalar = new List<string>();
            foreach (ArtAp041TreeIntake.Entry t in ArtAp041TreeIntake.Table)
            {
                VfxCatalogEntry e = catalog.Find(t.Id);
                if (e == null) { hatalar.Add(t.Id + ": katalogda yok"); continue; }
                if (!e.Premultiplied || e.Additive) hatalar.Add(t.Id + ": PM / additive bayrağı");
                if (e.ManifestFile != ArtAp041TreeIntake.ManifestName) hatalar.Add(t.Id + ": ManifestFile " + e.ManifestFile);
                Material m = e.Material;
                if (m == null || m.shader != flipbook) { hatalar.Add(t.Id + ": materyal / shader"); continue; }
                if (m.GetFloat("_Premultiplied") < .5f) hatalar.Add(t.Id + ": _Premultiplied");
                if ((m.GetFloat("_RgMask") > .5f) != t.Mask) hatalar.Add(t.Id + ": _RgMask");
                if (m.GetFloat("_SrcBlend") != (float)BlendMode.One || m.GetFloat("_DstBlend") != (float)BlendMode.OneMinusSrcAlpha) hatalar.Add(t.Id + ": blend");
                if (t.Kind == VfxKind.Decal && m.renderQueue >= (int)RenderQueue.Transparent) hatalar.Add(t.Id + ": dekal saydamlardan önce çizilmeli");
                if (!m.enableInstancing) hatalar.Add(t.Id + ": instancing");
                var importer = AssetImporter.GetAtPath(ArtAp041TreeIntake.Root + "/" + t.File) as TextureImporter;
                if (importer == null) { hatalar.Add(t.File + ": importer"); continue; }
                if (importer.sRGBTexture == t.Mask) hatalar.Add(t.File + ": sRGB " + importer.sRGBTexture);
                if (importer.mipmapEnabled == t.Mask) hatalar.Add(t.File + ": mip " + importer.mipmapEnabled);
                if (importer.alphaIsTransparency) hatalar.Add(t.File + ": Alpha Is Transparency açık");
            }

            Assert.IsEmpty(hatalar, "Ağaç katalog / materyal ihlali (" + hatalar.Count + "):\n" + string.Join("\n", hatalar));
        }
    }
}
