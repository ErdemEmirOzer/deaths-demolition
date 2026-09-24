using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-042B · AP-013 classic weapon asset gate (ADR-042-1): the library is built in Resources
    /// with the atlas material, every enabled roster weapon has an imported model whose LOD0–2
    /// objects sit inside the WP-034 triangle budget, whose sockets match the contract measures
    /// (±3 cm), rigid, no collider, with an import-generated LODGroup; the atlas imports at the
    /// Weapon class size (1024) with the right colour spaces. D has no entry.
    /// </summary>
    [TestFixture]
    [Category("WP042")]
    public sealed class WeaponModelGateTests
    {
        private const string Folder = "Assets/_Art/Weapons/Classic";

        private static ClassicWeaponVisualLibrary Library => Resources.Load<ClassicWeaponVisualLibrary>(ClassicWeaponVisualLibrary.ResourcePath);

        [TearDown]
        public void TearDown()
        {
            WeaponRoster.SetForTests(null);
        }

        [Test]
        public void Kutuphane_Resources_ta_SemaVeMateryal()
        {
            ClassicWeaponVisualLibrary lib = Library;
            Assert.IsNotNull(lib, "DD_ClassicWeapons.asset Resources'ta yok (ArtWeaponBuilder koşmadı).");
            Assert.AreEqual(ClassicWeaponVisualLibrary.Schema, lib.schema);
            Assert.AreEqual("AP-013", lib.package);
            Assert.IsNotNull(lib.material, "M_weapons bağlı değil.");
            Assert.IsNotNull(lib.material.GetTexture("_BaseMap"), "BaseColor bağlı değil.");
            Assert.IsNotNull(lib.material.GetTexture("_BumpMap"), "Normal bağlı değil.");
            Assert.IsNotNull(lib.material.GetTexture("_MetallicGlossMap"), "MetallicSmoothness bağlı değil.");
            var baseMap = lib.material.GetTexture("_BaseMap") as Texture2D;
            Assert.IsNotNull(baseMap);
            Assert.AreEqual(1024, baseMap.width, "silah atlası 1K (Weapon sınıfı).");
            Assert.AreEqual(1024, baseMap.height);
        }

        [Test]
        public void Atlas_ImportSozlesmesi_WeaponSinifi_1024_RenkUzaylari()
        {
            foreach ((string file, bool srgb) in new[] {
                ("Textures/T_weapons_BaseColor.png", true), ("Textures/T_weapons_Normal.png", false),
                ("Textures/T_weapons_Packed.png", false), ("Derived/T_weapons_MetallicSmoothness.png", false) })
            {
                string path = Folder + "/" + file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, path + " yok / import edilmemiş.");
                Assert.AreEqual(1024, importer.maxTextureSize, path + " Weapon sınıfı 1024.");
                Assert.AreEqual(srgb, importer.sRGBTexture, path + " renk uzayı.");
                if (file.Contains("Normal")) Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType, path);
            }
        }

        [TestCase(ClassicWeaponId.AssaultRifle, "A", "rifle")]
        [TestCase(ClassicWeaponId.AutoSmg, "B", "carbine")]
        [TestCase(ClassicWeaponId.CombatShotgun, "C", "shotgun")]
        public void Silah_Model_LOD0_2_Butce_Soketler_Rijit(ClassicWeaponId id, string key, string stem)
        {
            ClassicWeaponVisualLibrary lib = Library;
            Assert.IsNotNull(lib);
            ClassicWeaponVisualLibrary.Entry e = lib.Find(id);
            Assert.IsNotNull(e, key + " girdisi yok.");
            Assert.IsTrue(e.HasModel, key + " modeli bağlı değil.");
            Assert.AreEqual(key, e.rosterKey);

            GameObject model = e.model;
            Assert.IsNull(model.GetComponentInChildren<SkinnedMeshRenderer>(true), key + ": rijit olmalı.");
            Assert.IsNull(model.GetComponentInChildren<Collider>(true), key + ": el modelinde collider yok.");
            var lodGroup = model.GetComponentInChildren<LODGroup>(true);
            Assert.IsNotNull(lodGroup, key + ": import LODGroup (AP013_*_LOD0..2 adlandırması) yok.");
            Assert.AreEqual(ClassicWeaponVisualLibrary.LodCount, lodGroup.lodCount, key + ": 3 LOD.");

            for (int lod = 0; lod < ClassicWeaponVisualLibrary.LodCount; lod++)
            {
                Transform t = WeaponSocketContract.Find(model.transform, "AP013_" + key + "_" + stem + "_LOD" + lod);
                Assert.IsNotNull(t, key + ": LOD" + lod + " nesnesi yok.");
                var filter = t.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, key + " LOD" + lod + " MeshFilter yok.");
                Assert.IsNotNull(filter.sharedMesh);
                int tris = filter.sharedMesh.triangles.Length / 3;
                Assert.LessOrEqual(tris, WeaponSocketContract.TriangleBudget[lod], key + " LOD" + lod + " " + tris + " tri > bütçe " + WeaponSocketContract.TriangleBudget[lod]);
                Assert.AreEqual(tris, e.lodTriangles[lod], key + " LOD" + lod + " kütüphane tri sayısı");
                Assert.Greater(tris, 0);
            }

            ClassicWeaponDefinition d = WeaponRoster.Find(id);
            Assert.IsNotNull(d);
            List<string> faults = WeaponSocketContract.Validate(model.transform, d.TypeClass);
            Assert.IsEmpty(faults, key + " (AP-013): " + string.Join("; ", faults));

            // Every socket the definition names exists on the model (attachments bind here later).
            Assert.IsNotNull(WeaponSocketContract.Find(model.transform, d.MuzzleSocket), key + ": " + d.MuzzleSocket);
            Assert.IsNotNull(WeaponSocketContract.Find(model.transform, d.EjectionSocket), key + ": " + d.EjectionSocket);
            foreach (string s in d.AttachmentSockets) Assert.IsNotNull(WeaponSocketContract.Find(model.transform, s), key + ": " + s);
            Debug.Log("[DD-TEST] wp042b.model " + key + " tris=" + string.Join("/", e.lodTriangles));
        }

        [Test]
        public void D_KutuphanedeYok()
        {
            ClassicWeaponVisualLibrary lib = Library;
            Assert.IsNotNull(lib);
            int ap013 = 0;
            foreach (ClassicWeaponVisualLibrary.Entry e in lib.entries)
            {
                Assert.AreNotEqual("D", e.rosterKey);
                if (!e.ownMaterials) ap013++;
            }

            Assert.AreEqual(3, ap013, "AP-013 A/B/C — D için model üretilmez (DEC-083); AP-021 klasikleri ayrı girdiler (WP-069 B4d).");
        }
    }
}
