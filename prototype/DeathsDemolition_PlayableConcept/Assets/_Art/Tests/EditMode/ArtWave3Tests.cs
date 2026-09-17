using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-047 · Wave 3 data: the AP-017 break identities (six event ids ↔ residue cells / debris / dust, prefab-name
    /// mapping from the AP-017 table), the muzzle A/B/C table (DEC-083), the wave-2 rows the hooks consume and the
    /// AP-014 use sheets the Special Weapon adapter reads.
    /// </summary>
    [TestFixture]
    [Category("WP047")]
    public sealed class ArtWave3Tests
    {
        [Test]
        public void Kirilabilir_AltiOlayKimligi_AP017Tablosu()
        {
            Assert.AreEqual(6, BreakableVfx.Count);
            Assert.AreEqual(BreakableVfx.ResidueCeramic, BreakableVfx.Get(BreakEventId.BreakCeramic).ResidueCell);
            Assert.AreEqual(BreakableVfx.ResidueCeramic, BreakableVfx.Get(BreakEventId.BreakStone).ResidueCell);
            Assert.AreEqual(BreakableVfx.ResidueWood, BreakableVfx.Get(BreakEventId.BreakWood).ResidueCell);
            Assert.AreEqual(BreakableVfx.ResidueWood, BreakableVfx.Get(BreakEventId.BreakBamboo).ResidueCell);
            Assert.AreEqual(BreakableVfx.ResidueCloth, BreakableVfx.Get(BreakEventId.BreakPaper).ResidueCell);
            Assert.AreEqual(BreakableVfx.ResidueStraw, BreakableVfx.Get(BreakEventId.BreakStraw).ResidueCell);

            // AP017-breakable-event-table-v001: family → event_id.
            Assert.AreEqual(BreakEventId.BreakWood, BreakableVfx.EventIdFor("AP017_breakable_bench_V01"));
            Assert.AreEqual(BreakEventId.BreakBamboo, BreakableVfx.EventIdFor("AP017_breakable_fence_panel_V02"));
            Assert.AreEqual(BreakEventId.BreakPaper, BreakableVfx.EventIdFor("AP017_breakable_lantern_paper_V01"));
            Assert.AreEqual(BreakEventId.BreakStone, BreakableVfx.EventIdFor("AP017_breakable_lantern_stone_V02"));
            Assert.AreEqual(BreakEventId.BreakCeramic, BreakableVfx.EventIdFor("AP017_breakable_planter_V01"));
            Assert.AreEqual(BreakEventId.BreakPaper, BreakableVfx.EventIdFor("AP017_breakable_shoji_V01"));
            Assert.AreEqual(BreakEventId.BreakWood, BreakableVfx.EventIdFor("AP017_breakable_sign_V01"));
            Assert.AreEqual(BreakEventId.BreakStraw, BreakableVfx.EventIdFor("AP017_breakable_strawbale_V01"));
            // Today's AP-003B family and the WP-013 default.
            Assert.AreEqual(BreakEventId.BreakCeramic, BreakableVfx.EventIdFor("SM_small_breakable_V01(Clone)"));
            Assert.AreEqual(BreakEventId.BreakWood, BreakableVfx.EventIdFor("crate"));
            Assert.AreEqual("BreakStraw", BreakableVfx.Get(BreakEventId.BreakStraw).EventName);
        }

        [Test]
        public void NamluVaryanti_ABC_DEC083()
        {
            Assert.AreEqual(MuzzleVariant.A, VfxEventBinder.VariantFor(ClassicWeaponId.AssaultRifle));
            Assert.AreEqual(MuzzleVariant.B, VfxEventBinder.VariantFor(ClassicWeaponId.AutoSmg));
            Assert.AreEqual(MuzzleVariant.C, VfxEventBinder.VariantFor(ClassicWeaponId.CombatShotgun));
        }

        [Test]
        public void Wave3_KatalogSatirlari_VeAP014Sheetleri()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog);
            foreach (string id in new[] { VfxIds.ClawSwipe, VfxIds.SpawnDust, VfxIds.DustStep, VfxIds.MagDrop, VfxIds.MagDropDust, VfxIds.StrawPuff, VfxIds.StrawPuffCloud, VfxIds.LanternFlame, VfxIds.LeafBurst, VfxIds.PaperTear })
            {
                VfxCatalogEntry row = catalog.Find(id);
                Assert.IsNotNull(row, id + " katalogda yok");
                Assert.IsNotNull(row.Material, id + " materyali yok");
            }

            Assert.IsTrue(catalog.Find(VfxIds.LanternFlame).Loop, "lantern_flame seamless loop (manifest)");

            var library = Resources.Load<SpecialWeaponVisualLibrary>(SpecialWeaponVisualLibrary.ResourcePath);
            Assert.IsNotNull(library, "DD_SpecialWeapons (WP-037B) yok");
            foreach (string usage in new[] { VfxIds.SwKaguraWave, VfxIds.SwOfudaDart, VfxIds.SwOfudaCollapse, VfxIds.SwOfudaWeb })
            {
                SpecialWeaponVisualLibrary.Sheet sheet = library.FindSheet(usage);
                Assert.IsNotNull(sheet, usage + " sheet'i yok");
                Assert.IsNotNull(sheet.texture, usage + " dokusu yok");
            }

            Assert.AreEqual(19f, library.FindSheet(VfxIds.SwKaguraWave).worldSizeMetres, 0.01f, "Kagura ring 19 m (manifest)");
            Assert.AreEqual("sw_kagura_wave", SpecialWeaponUsage.For(SpecialWeaponId.KaguraBell).FireVfxFamily);
        }
    }
}
