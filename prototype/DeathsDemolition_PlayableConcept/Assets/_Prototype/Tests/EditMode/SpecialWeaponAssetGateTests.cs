using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-037 · AP-014 Special Weapon asset gate (Import Contract v1, rigid mesh): the
    /// library is built, both authored weapons carry pickup + hand LOD0–2 meshes, LOD0 is
    /// inside the AP-014 budget (pickup 1 500 / hand 3 000), the atlas material carries
    /// the three maps, textures are 1K, the four use sheets are bound. LOD0–2 budgets are
    /// enforced (AP-014B v002 brought LOD1/2 inside them; WP-056).
    /// </summary>
    [TestFixture]
    [Category("WP037")]
    public sealed class SpecialWeaponAssetGateTests
    {
        private const int PickupLod0Budget = 1500;
        private const int HandLod0Budget = 3000;

        private static SpecialWeaponVisualLibrary Library => Resources.Load<SpecialWeaponVisualLibrary>(SpecialWeaponVisualLibrary.ResourcePath);

        [Test]
        public void Kutuphane_Resources_ta_VeSemaDogru()
        {
            SpecialWeaponVisualLibrary lib = Library;
            Assert.IsNotNull(lib, "DD_SpecialWeapons.asset Resources'ta yok (ArtSpecialWeaponBuilder koşmadı).");
            Assert.AreEqual(SpecialWeaponVisualLibrary.Schema, lib.schema);
            Assert.AreEqual("AP-014", lib.package);
            Assert.IsNotNull(lib.material, "M_sw_atlas materyali bağlı değil.");
            Assert.IsNotNull(lib.material.GetTexture("_BaseMap"), "BaseColor bağlı değil.");
            Assert.IsNotNull(lib.material.GetTexture("_BumpMap"), "Normal bağlı değil.");
            Assert.IsNotNull(lib.material.GetTexture("_MetallicGlossMap"), "MetallicSmoothness bağlı değil.");
            var baseMap = lib.material.GetTexture("_BaseMap") as Texture2D;
            Assert.IsNotNull(baseMap);
            Assert.AreEqual(1024, baseMap.width, "SW atlas 1K olmalı.");
            Assert.AreEqual(1024, baseMap.height);
        }

        [TestCase(SpecialWeaponId.KaguraBell)]
        [TestCase(SpecialWeaponId.OfudaLoom)]
        [TestCase(SpecialWeaponId.TideJewel)]   // WP-059C · AP-014D
        public void Silah_PickupVeElModeli_LOD0_2_Butce(SpecialWeaponId id)
        {
            SpecialWeaponVisualLibrary.Entry e = Library.Find(id);
            Assert.IsNotNull(e, id + " için kütüphane girdisi yok.");
            Assert.IsTrue(e.HasPickup && e.HasHand);
            for (int lod = 0; lod < SpecialWeaponVisualLibrary.LodCount; lod++)
            {
                Assert.IsNotNull(e.pickupLods[lod], id + " pickup LOD" + lod + " mesh yok.");
                Assert.IsNotNull(e.handLods[lod], id + " hand LOD" + lod + " mesh yok.");
                Assert.Greater(e.pickupTriangles[lod], 0);
                Assert.Greater(e.handTriangles[lod], 0);
                Assert.AreEqual(e.pickupLods[lod].triangles.Length / 3, e.pickupTriangles[lod], "Kayıtlı tri sayısı mesh ile aynı olmalı.");
            }

            Assert.LessOrEqual(e.pickupTriangles[0], PickupLod0Budget, id + " pickup LOD0 bütçe dışı.");
            Assert.LessOrEqual(e.handTriangles[0], HandLod0Budget, id + " hand LOD0 bütçe dışı.");
            Assert.LessOrEqual(e.pickupTriangles[2], e.pickupTriangles[0], "LOD2 LOD0'dan ağır olamaz.");
            Assert.LessOrEqual(e.handTriangles[2], e.handTriangles[0]);

            // WP-056 (AP-014B v002): the LOD1/2 budgets are met (kagura 510/147, ofuda 322/200 · 322/300) — enforced from here on.
            int[] pickupBudget = { 1500, 600, 200 };
            int[] handBudget = { 3000, 1000, 300 };
            for (int lod = 1; lod < 3; lod++)
            {
                Assert.LessOrEqual(e.pickupTriangles[lod], pickupBudget[lod], id + " pickup LOD" + lod + " " + e.pickupTriangles[lod] + " tri > " + pickupBudget[lod] + " (AP-014B bütçesi).");
                Assert.LessOrEqual(e.handTriangles[lod], handBudget[lod], id + " hand LOD" + lod + " " + e.handTriangles[lod] + " tri > " + handBudget[lod] + " (AP-014B bütçesi).");
            }

            // Rigid meshes: no bind poses (Import Contract v1 for props).
            Assert.AreEqual(0, e.handLods[0].bindposes.Length, id + " el modeli skinned görünüyor (rijit olmalı).");
        }

        /// <summary>WP-059C · AP-014D: the Tide Jewel joined the library (hand 1800/600/200, pickup 1450/510/147 — under the AP-014B budgets); the WP-007 placeholder is gone for all three.</summary>
        [Test]
        public void TideJewel_AP014D_ModeliVar_UcSilahTam()
        {
            SpecialWeaponVisualLibrary.Entry e = Library.Find(SpecialWeaponId.TideJewel);
            Assert.IsNotNull(e, "AP-014D Tide Jewel girdisi yok.");
            Assert.IsTrue(e.HasPickup && e.HasHand);
            Assert.LessOrEqual(e.handTriangles[0], 1800, "AP-014D hand LOD0 1800");
            Assert.LessOrEqual(e.pickupTriangles[0], 1450, "AP-014D pickup LOD0 1450");
            Assert.AreEqual(3, Library.entries.Length, "üç Special Weapon da kütüphanede");
        }

        [Test]
        public void KullanimSheetleri_DortAile_Bagli()
        {
            SpecialWeaponVisualLibrary lib = Library;
            foreach (string usage in new[] { "sw_kagura_wave", "sw_ofuda_dart", "sw_ofuda_collapse", "sw_ofuda_web" })
            {
                SpecialWeaponVisualLibrary.Sheet s = lib.FindSheet(usage);
                Assert.IsNotNull(s, usage + " sheet'i yok.");
                Assert.IsNotNull(s.texture, usage + " dokusu bağlı değil.");
                Assert.Greater(s.columns * s.rows, 0);

                // Import contract Sheet role (WP-037): T_*_Sheet.png, Vfx class — 1K cap, alpha kept as coverage, never streamed.
                string path = UnityEditor.AssetDatabase.GetAssetPath(s.texture);
                StringAssert.EndsWith("_Sheet.png", path, usage + " sheet adı sözleşme dışı.");
                var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
                Assert.IsNotNull(importer, path);
                Assert.AreEqual(1024, importer.maxTextureSize, path + " Vfx sınıfı 1024.");
                Assert.AreEqual(UnityEditor.TextureImporterAlphaSource.FromInput, importer.alphaSource, path + " alfa (kaplama) korunur.");
                Assert.IsTrue(importer.alphaIsTransparency, path + " alfa saydamlık.");
                Assert.IsFalse(importer.streamingMipmaps, path + " efekt dokusu stream edilmez.");
                Assert.IsTrue(importer.sRGBTexture, path + " renk (sRGB).");
            }

            SpecialWeaponVisualLibrary.Sheet ring = lib.FindSheet("sw_kagura_wave");
            Assert.AreEqual(1024, ring.texture.width);
            Assert.AreEqual(16, ring.columns * ring.rows, "Kagura ring 4×4.");
            Assert.AreEqual(1.0f, ring.durationSeconds, 1e-4f);

            // The usage ids the runtime table names exist as sheets (the VFX layer binds by these).
            Assert.AreEqual("sw_kagura_wave", SpecialWeaponUsage.For(SpecialWeaponId.KaguraBell).FireVfxFamily);
            Assert.AreEqual("sw_ofuda_web", SpecialWeaponUsage.For(SpecialWeaponId.OfudaLoom).FieldVfxFamily);
        }

        [Test]
        public void Manifest_EventCoverage_IkiSilah()
        {
            string path = "Assets/_Art/Weapons/Special/AP014-sw-manifest.json";
            Assert.IsTrue(File.Exists(path), "AP014-sw-manifest.json yok.");
            string json = File.ReadAllText(path);
            Assert.IsTrue(json.Contains("\"SW Kagura Bell\"") && json.Contains("\"SW Ofuda Loom\""), "EVENT-COVERAGE satırları eksik.");
            Assert.IsTrue(json.Contains("socket_weapon_r"), "El modeli soket çerçevesi manifestte yok.");
        }
    }
}
