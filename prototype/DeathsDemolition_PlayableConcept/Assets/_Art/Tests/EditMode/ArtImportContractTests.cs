using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-011 · <see cref="ArtImportContract"/> birim nöbetçisi: rol adlandırması, kapsam
    /// sınırı ve Apply↔Validate gidiş-dönüşü.
    ///
    /// YAKALAMA GÜCÜ KANITI: "Validate boş" tek başına yalancı yeşil olabilir (hiçbir alanı
    /// kontrol etmeyen bir Validate de boş döner). O yüzden gerçek bir importer üzerinde tek bir
    /// alan BELLEKTE saptırılır, Validate'in tam o alanı adıyla raporladığı ve Apply'ın onu
    /// onardığı gösterilir. Hiçbir şey diske yazılmaz: SaveAndReimport yok; Apply sonrası
    /// bellekteki değerler .meta ile yeniden aynıdır.
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class ArtImportContractTests
    {
        // ------------------------------------------------------------------ RoleOf

        [TestCase("Assets/_Art/Textures/Source/T_stone_BaseColor.png", ArtImportContract.TextureRole.BaseColor)]
        [TestCase("Assets/_Art/Textures/Source/T_stone_Normal.png", ArtImportContract.TextureRole.Normal)]
        [TestCase("Assets/_Art/Textures/Source/T_bark_Packed.png", ArtImportContract.TextureRole.Packed)]
        [TestCase("Assets/_Art/Textures/Derived/T_earth_MetallicSmoothness.png", ArtImportContract.TextureRole.MetallicSmoothness)]
        [TestCase("T_sakura_pink_BaseColor.png", ArtImportContract.TextureRole.BaseColor)]
        [TestCase("T_wood_Normal.PNG", ArtImportContract.TextureRole.Normal)]
        [TestCase("Assets/_Art/VFX/Textures/T_vfx_sw_kagura_ring_4x4_Sheet.png", ArtImportContract.TextureRole.Sheet)]
        [TestCase("stone_BaseColor.png", ArtImportContract.TextureRole.Unknown)]
        [TestCase("T_BaseColor.png", ArtImportContract.TextureRole.Unknown)]
        [TestCase("T_stone_Roughness.png", ArtImportContract.TextureRole.Unknown)]
        [TestCase("T_stone_basecolor.png", ArtImportContract.TextureRole.Unknown)]
        [TestCase("T_stone.png", ArtImportContract.TextureRole.Unknown)]
        [TestCase("t_stone_BaseColor.png", ArtImportContract.TextureRole.Unknown)]
        public void RoleOf_AdlandirmaKurali(string yol, ArtImportContract.TextureRole beklenen)
        {
            Assert.AreEqual(beklenen, ArtImportContract.RoleOf(yol), "RoleOf(" + yol + ")");
        }

        [Test]
        public void RoleOf_RolAdiAltCizgiIcerebilir()
        {
            // "sakura_pink" gibi çok parçalı roller: son '_' harita adını ayırır, öncesi rol.
            Assert.AreEqual(ArtImportContract.TextureRole.MetallicSmoothness,
                ArtImportContract.RoleOf("Assets/_Art/Textures/Derived/T_sakura_pink_MetallicSmoothness.png"));
        }

        // ------------------------------------------------------------------ kapsam

        [TestCase("Assets/_Art/Environment/Families/curb/curb_LOD0.fbx", true)]
        [TestCase("Assets/_Art/x.FBX", true)]
        [TestCase("Assets/_Artful/x.fbx", false)]
        [TestCase("Assets/_Prototype/World/x.fbx", false)]
        [TestCase("Assets/_Art/Textures/Source/T_stone_BaseColor.png", false)]
        public void IsContractModel_YalnizArtAltindakiFbx(string yol, bool beklenen)
        {
            Assert.AreEqual(beklenen, ArtImportContract.IsContractModel(yol), "IsContractModel(" + yol + ")");
        }

        [TestCase("Assets/_Art/Textures/Source/T_stone_BaseColor.png", true)]
        [TestCase("Assets/_Art/Textures/Derived/T_stone_MetallicSmoothness.PNG", true)]
        [TestCase("Assets/_Prototype/T_stone_BaseColor.png", false)]
        [TestCase("Assets/_Art/Environment/Families/curb/curb_LOD0.fbx", false)]
        [TestCase("Assets/_Art/Resources/MenuPlates/plate_far.png", false)]   // WP-068: own contract (ApplyMenuPlate)
        public void IsContractTexture_YalnizArtAltindakiPng(string yol, bool beklenen)
        {
            Assert.AreEqual(beklenen, ArtImportContract.IsContractTexture(yol), "IsContractTexture(" + yol + ")");
        }

        [Test]
        public void IsUnderContract_NullVeKokDisi()
        {
            Assert.IsFalse(ArtImportContract.IsUnderContract(null));
            Assert.IsFalse(ArtImportContract.IsUnderContract(""));
            Assert.IsFalse(ArtImportContract.IsUnderContract("Assets/_Art"), "Kökün kendisi bir asset değildir.");
            Assert.IsTrue(ArtImportContract.IsUnderContract("Assets/_Art/a"));
        }

        // ------------------------------------------------------------------ Apply ↔ Validate (model)

        private static ModelImporter ModelImporterAl(string yol)
        {
            var importer = AssetImporter.GetAtPath(yol) as ModelImporter;
            Assume.That(importer, Is.Not.Null, "ModelImporter yok: " + yol + " (asset import edilmemiş).");
            return importer;
        }

        [Test]
        public void Model_UyumluAssetteApplyDegistirmez_ValidateBos()
        {
            // En küçük FBX (zemin collider'ı, ~13 KB): olası bir yeniden import bile ucuz kalsın.
            ModelImporter importer = ModelImporterAl(ArtPipelinePaths.GroundColliderFbx);

            List<string> once = ArtImportContract.ValidateModel(importer);
            Assert.IsEmpty(once, "Uyumlu olması gereken asset sözleşme dışı: " + string.Join("; ", once));
            Assert.IsFalse(ArtImportContract.Apply(importer), "Uyumlu asset'te Apply değişiklik bildirdi — idempotency kırık, her import .meta'yı kirletir.");
            Assert.IsEmpty(ArtImportContract.ValidateModel(importer));
        }

        [Test]
        public void Model_BellekteSaptirilanAlan_ValidateAdiylaYakalar_ApplyOnarir()
        {
            ModelImporter importer = ModelImporterAl(ArtPipelinePaths.GroundColliderFbx);
            Assume.That(ArtImportContract.ValidateModel(importer), Is.Empty, "Ön koşul: asset uyumlu değil; bu test anlamsız.");

            float asil = importer.globalScale;
            try
            {
                importer.globalScale = ArtImportContract.ModelGlobalScale * 2f;

                List<string> v = ArtImportContract.ValidateModel(importer);
                Assert.AreEqual(1, v.Count, "Tek alan saptırıldı, tek ihlal beklenir: " + string.Join("; ", v));
                StringAssert.StartsWith("globalScale:", v[0]);

                Assert.IsTrue(ArtImportContract.Apply(importer), "Apply sapmayı onarmalı ve bunu bildirmeli.");
                Assert.AreEqual(ArtImportContract.ModelGlobalScale, importer.globalScale, 0.0001f);
                Assert.IsEmpty(ArtImportContract.ValidateModel(importer), "Apply sonrası Validate boş olmalı.");
            }
            finally
            {
                // Ne olursa olsun bellekteki değer .meta ile aynı bırakılır; diske yazılmaz.
                importer.globalScale = asil;
            }
        }

        // ------------------------------------------------------------------ Apply ↔ Validate (doku)

        private static TextureImporter TextureImporterAl(string yol)
        {
            var importer = AssetImporter.GetAtPath(yol) as TextureImporter;
            Assume.That(importer, Is.Not.Null, "TextureImporter yok: " + yol + " (asset import edilmemiş).");
            return importer;
        }

        [Test]
        public void Doku_UyumluAssetteApplyDegistirmez_ValidateBos()
        {
            string yol = ArtPipelinePaths.SourceTexture("plaster", "BaseColor");
            TextureImporter importer = TextureImporterAl(yol);
            ArtImportContract.TextureRole rol = ArtImportContract.RoleOf(yol);
            Assert.AreEqual(ArtImportContract.TextureRole.BaseColor, rol);

            List<string> once = ArtImportContract.ValidateTexture(importer, rol);
            Assert.IsEmpty(once, "Uyumlu olması gereken doku sözleşme dışı: " + string.Join("; ", once));
            Assert.IsFalse(ArtImportContract.Apply(importer, rol), "Uyumlu dokuda Apply değişiklik bildirdi — idempotency kırık.");
            Assert.IsEmpty(ArtImportContract.ValidateTexture(importer, rol));
        }

        [Test]
        public void Doku_YanlisRolleValidate_TipVeSrgbFarkiniRaporlar()
        {
            // Hiçbir şeyi değiştirmeden pozitif kontrol: BaseColor dokusu Normal rolüyle
            // doğrulanınca tip (Default≠NormalMap), sRGB ve platform formatı (BC7≠BC5) sapar.
            string yol = ArtPipelinePaths.SourceTexture("plaster", "BaseColor");
            TextureImporter importer = TextureImporterAl(yol);

            List<string> v = ArtImportContract.ValidateTexture(importer, ArtImportContract.TextureRole.Normal);
            Assert.IsNotEmpty(v, "Yanlış rol hiç ihlal üretmedi — Validate rol farkını görmüyor.");
            string hepsi = string.Join("; ", v);
            StringAssert.Contains("textureType:", hepsi);
            StringAssert.Contains("sRGBTexture:", hepsi);
            StringAssert.Contains("Standalone.format:", hepsi);
        }

        [Test]
        public void Doku_UnknownRol_AdIhlaliRaporlar()
        {
            string yol = ArtPipelinePaths.SourceTexture("plaster", "BaseColor");
            TextureImporter importer = TextureImporterAl(yol);

            List<string> v = ArtImportContract.ValidateTexture(importer, ArtImportContract.TextureRole.Unknown);
            Assert.IsTrue(v.Count > 0 && v[0].StartsWith("name:"), "Unknown rol ilk satırda ad ihlali vermeli: " + string.Join("; ", v));
        }

        [Test]
        public void Doku_BellekteSaptirilanAlan_ValidateAdiylaYakalar_ApplyOnarir()
        {
            string yol = ArtPipelinePaths.SourceTexture("plaster", "BaseColor");
            TextureImporter importer = TextureImporterAl(yol);
            ArtImportContract.TextureRole rol = ArtImportContract.RoleOf(yol);
            Assume.That(ArtImportContract.ValidateTexture(importer, rol), Is.Empty, "Ön koşul: doku uyumlu değil; bu test anlamsız.");

            int asil = importer.anisoLevel;
            try
            {
                importer.anisoLevel = ArtImportContract.TextureAniso + 4;

                List<string> v = ArtImportContract.ValidateTexture(importer, rol);
                Assert.AreEqual(1, v.Count, "Tek alan saptırıldı, tek ihlal beklenir: " + string.Join("; ", v));
                StringAssert.StartsWith("anisoLevel:", v[0]);

                Assert.IsTrue(ArtImportContract.Apply(importer, rol), "Apply sapmayı onarmalı ve bunu bildirmeli.");
                Assert.AreEqual(ArtImportContract.TextureAniso, importer.anisoLevel);
                Assert.IsEmpty(ArtImportContract.ValidateTexture(importer, rol));
            }
            finally
            {
                importer.anisoLevel = asil;
            }
        }

        // ------------------------------------------------------------------ WP-015 · doku sınıf tablosu (ADR-015-1)

        [TestCase("earth", ArtImportContract.TextureClass.Ground, 2048)]
        [TestCase("gravel", ArtImportContract.TextureClass.Ground, 2048)]
        [TestCase("stone", ArtImportContract.TextureClass.Ground, 2048)]
        [TestCase("wood", ArtImportContract.TextureClass.Structure, 1024)]
        [TestCase("plaster_hf003", ArtImportContract.TextureClass.Structure, 1024)]
        [TestCase("earth_02", ArtImportContract.TextureClass.Structure, 1024)]
        [TestCase("green_foliage_03", ArtImportContract.TextureClass.Foliage, 1024)]
        [TestCase("bamboo_culm", ArtImportContract.TextureClass.Foliage, 1024)]
        [TestCase("bark", ArtImportContract.TextureClass.Foliage, 1024)]
        [TestCase("sky", ArtImportContract.TextureClass.Sky, 1024)]   // WP-038E (2): AP-019 band lat-long
        [TestCase("sw_atlas", ArtImportContract.TextureClass.Weapon, 1024)]
        [TestCase("weapons", ArtImportContract.TextureClass.Weapon, 1024)]
        public void DokuSinifi_RolTablosu_VeBoyut(string rol, ArtImportContract.TextureClass beklenen, int boyut)
        {
            Assert.AreEqual(beklenen, ArtImportContract.ClassOfRole(rol), "ClassOfRole(" + rol + ")");
            Assert.AreEqual(boyut, ArtImportContract.MaxSizeOf(beklenen));
            Assert.AreEqual(boyut, ArtImportContract.MaxTextureSizeOf("Assets/_Art/Textures/Source/T_" + rol + "_BaseColor.png"));
        }

        [Test]
        public void DokuSinifi_TablodaOlmayanRol_NullVeProp512()
        {
            Assert.IsNull(ArtImportContract.ClassOfRole("lantern_brass"), "tabloda olmayan rol null döner");
            Assert.IsNull(ArtImportContract.ClassOfRole(null));
            Assert.AreEqual(ArtImportContract.TextureClass.Prop, ArtImportContract.ClassOf("Assets/_Art/Textures/Source/T_lantern_brass_BaseColor.png"), "sınıfsız rol Prop boyutunda import edilir");
            Assert.AreEqual(512, ArtImportContract.MaxTextureSizeOf("Assets/_Art/Textures/Source/T_lantern_brass_BaseColor.png"));
            Assert.AreEqual(512, ArtImportContract.MaxSizeOf(ArtImportContract.TextureClass.Prop));
            Assert.AreEqual(512, ArtImportContract.MaxSizeOf(ArtImportContract.TextureClass.Breakable));
        }

        [Test]
        public void DokuSinifi_Sheet_VfxSinifi_1024_StreamYok_AlfaKorunur()
        {
            // WP-037 (AP-014): a T_*_Sheet.png is the Vfx class without a table row — 1024, never streamed, colour with alpha.
            const string yol = "Assets/_Art/VFX/Textures/T_vfx_sw_ofuda_card_1x2_Sheet.png";
            Assert.IsNull(ArtImportContract.ClassOfRole(ArtImportContract.RoleSlugOf(yol)), "sheet'in tablo satırı yoktur");
            Assert.AreEqual(ArtImportContract.TextureClass.Vfx, ArtImportContract.ClassifiedClassOf(yol));
            Assert.AreEqual(1024, ArtImportContract.MaxTextureSizeOf(yol));
            Assert.IsFalse(ArtImportContract.StreamsMipmaps(ArtImportContract.TextureClass.Vfx));
            Assert.IsFalse(ArtImportContract.IsLinearRole(ArtImportContract.TextureRole.Sheet), "sheet renk (sRGB)");
            Assert.IsTrue(ArtImportContract.KeepsAlpha(ArtImportContract.TextureRole.Sheet));
            Assert.AreEqual(ArtImportContract.FormatOf(ArtImportContract.TextureRole.BaseColor), ArtImportContract.FormatOf(ArtImportContract.TextureRole.Sheet));
        }

        [Test]
        public void DokuSinifi_HerMalzemeRolununDokuRolu_Tabloda()
        {
            var eksik = new List<string>();
            foreach (string rol in ArtLibraryMaterialRoles.TextureRoles())
                if (ArtImportContract.ClassOfRole(rol) == null) eksik.Add(rol);
            foreach (string rol in new[] { "stone", "wood", "bark", "earth", "plaster", "roof", "sky" })
                if (ArtImportContract.ClassOfRole(rol) == null) eksik.Add(rol);
            Assert.IsEmpty(eksik, "ADR-015-1 tablosunda sınıfı olmayan doku rolü: " + string.Join(", ", eksik));
        }

        [Test]
        public void DokuSinifi_Streaming_GokHaric()
        {
            Assert.IsTrue(ArtImportContract.StreamsMipmaps(ArtImportContract.TextureClass.Ground));
            Assert.IsTrue(ArtImportContract.StreamsMipmaps(ArtImportContract.TextureClass.Foliage));
            Assert.IsTrue(ArtImportContract.StreamsMipmaps(ArtImportContract.TextureClass.Prop));
            Assert.IsFalse(ArtImportContract.StreamsMipmaps(ArtImportContract.TextureClass.Sky), "skybox dokusunu hiçbir renderer örneklemez; streaming en alt mip'e düşürürdü");
        }

        [Test]
        public void SevkBoyutuTahmini_BlokSikistirma_MipZinciri()
        {
            // 2048² BC7 + mips = 4 MiB × 4/3 (4×4 blok tabanı ile son mipler 16 B).
            long tam = ArtImportContract.EstimatedShippedBytes(2048, 2048, ArtImportContract.TextureClass.Ground, true);
            Assert.AreEqual(5592432L, tam, "2048 mip zinciri: 2048²+…+4² + (2² ve 1² seviyeleri 4×4 bloğa yuvarlanır) = 5 592 432");
            Assert.AreEqual(4194304L, ArtImportContract.EstimatedShippedBytes(2048, 2048, ArtImportContract.TextureClass.Ground, false));
            // Sınıf 1024: kaynak 2048 → 1024'e iner.
            long yapi = ArtImportContract.EstimatedShippedBytes(2048, 2048, ArtImportContract.TextureClass.Structure, true);
            Assert.Less(yapi, tam / 3);
            Assert.AreEqual(1398128L, yapi);
            // Kaynak sınıf tavanından küçükse büyütülmez.
            Assert.AreEqual(ArtImportContract.EstimatedShippedBytes(4, 256, ArtImportContract.TextureClass.Sky, false), 4L * 256L);
        }

        [Test]
        public void Doku_BellekteSaptirilanMaxSize_ValidateAdiylaYakalar_ApplyOnarir()
        {
            string yol = ArtPipelinePaths.SourceTexture("plaster", "BaseColor");   // Structure → 1024
            TextureImporter importer = TextureImporterAl(yol);
            ArtImportContract.TextureRole rol = ArtImportContract.RoleOf(yol);
            Assume.That(ArtImportContract.ValidateTexture(importer, rol), Is.Empty, "Ön koşul: doku uyumlu değil; bu test anlamsız.");
            Assert.AreEqual(1024, importer.maxTextureSize, "plaster Structure sınıfı 1024");
            Assert.IsTrue(importer.streamingMipmaps, "Structure sınıfı streaming açık");

            int asil = importer.maxTextureSize;
            try
            {
                importer.maxTextureSize = 2048;
                List<string> v = ArtImportContract.ValidateTexture(importer, rol);
                Assert.AreEqual(1, v.Count, "Tek alan saptırıldı, tek ihlal beklenir: " + string.Join("; ", v));
                StringAssert.StartsWith("maxTextureSize:", v[0]);
                Assert.IsTrue(ArtImportContract.Apply(importer, rol), "Apply sapmayı onarmalı.");
                Assert.AreEqual(1024, importer.maxTextureSize);
                Assert.IsEmpty(ArtImportContract.ValidateTexture(importer, rol));
            }
            finally
            {
                importer.maxTextureSize = asil;
            }
        }

        // ------------------------------------------------------------------ sözleşme sabitleri

        [Test]
        public void GeometryRotation_TamArtiDoksanX()
        {
            // Prefab geometri çocuğu bu kuaterniyonu kullanır; importer düğümünün 89.98°'lik
            // Euler okuması yerine tam değer. Açı sapması sıfır, eksen +X.
            Quaternion q = ArtImportContract.GeometryRotation;
            Assert.AreEqual(90f, ArtImportContract.GeometryRotationEulerX, 0.0001f);
            Assert.Less(Quaternion.Angle(q, Quaternion.AngleAxis(90f, Vector3.right)), 0.0001f);

            // Rx(+90°)·(bx, by, −bz) = (bx, bz, by): Blender Z (yukarı) Unity Y'ye, Blender Y Unity Z'ye.
            Vector3 p = q * new Vector3(1f, 2f, -3f);
            Assert.AreEqual(1f, p.x, 0.0001f);
            Assert.AreEqual(3f, p.y, 0.0001f);
            Assert.AreEqual(2f, p.z, 0.0001f);
        }
    }
}
