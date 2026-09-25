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
        public void Atlas_ImportSozlesmesi_WeaponSinifi_RenkUzaylari()
        {
            // The cap is S-05's import contract (ADR-015-1 class table, derived masks capped) — read, never pinned here.
            foreach ((string file, bool srgb) in new[] {
                ("Textures/T_weapons_BaseColor.png", true), ("Textures/T_weapons_Normal.png", false),
                ("Textures/T_weapons_Packed.png", false), ("Derived/T_weapons_MetallicSmoothness.png", false) })
            {
                string path = Folder + "/" + file;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, path + " yok / import edilmemiş.");
                Assert.AreEqual(DD.Art.Pipeline.EditorTools.ArtImportContract.MaxTextureSizeOf(path), importer.maxTextureSize, path + " sözleşme tavanı (ArtImportContract).");
                Assert.AreEqual(srgb, importer.sRGBTexture, path + " renk uzayı.");
                if (file.Contains("Normal")) Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType, path);
            }
        }

        /// <summary>
        /// WP-069 B4e: since P-07's batch B every roster weapon carries its own AP-021 model (the AP-013 A/B/C entries gave
        /// way on the same ids; Ap021ClassicModelGateTests checks frame, LODs, budget and material). Here: rigid, no
        /// collider on the hand model, and every socket the definition names exists (attachments bind there).
        /// </summary>
        [Test]
        public void HerSilah_Ap021Model_Rijit_TanimSoketleri()
        {
            ClassicWeaponVisualLibrary lib = Library;
            Assert.IsNotNull(lib);
            int n = 0;
            foreach (ClassicWeaponDefinition d in WeaponRoster.Enabled)
            {
                ClassicWeaponVisualLibrary.Entry e = lib.Find(d.Id);
                Assert.IsNotNull(e, d.RosterKey + " girdisi yok.");
                Assert.IsTrue(e.HasModel && e.ownMaterials && e.modelPackage == "AP-021", d.RosterKey + ": AP-021 modeli bağlı değil.");
                GameObject model = e.model;
                Assert.IsNull(model.GetComponentInChildren<SkinnedMeshRenderer>(true), d.RosterKey + ": rijit olmalı.");
                Assert.IsNull(model.GetComponentInChildren<Collider>(true), d.RosterKey + ": el modelinde collider yok.");
                Assert.IsNotNull(WeaponSocketContract.Find(model.transform, d.MuzzleSocket), d.RosterKey + ": " + d.MuzzleSocket);
                Assert.IsNotNull(WeaponSocketContract.Find(model.transform, d.EjectionSocket), d.RosterKey + ": " + d.EjectionSocket);
                foreach (string socket in d.AttachmentSockets) Assert.IsNotNull(WeaponSocketContract.Find(model.transform, socket), d.RosterKey + ": " + socket);
                n++;
            }

            Assert.AreEqual(10, n, "WP-069: on klasik silah etkin.");
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

            Assert.AreEqual(0, ap013, "WP-069 B4e: on klasiğin hepsi AP-021; AP-013 A/B/C girdileri aynı kimliklerde yerini bıraktı (D için model yok, DEC-083).");
        }
    }
}
