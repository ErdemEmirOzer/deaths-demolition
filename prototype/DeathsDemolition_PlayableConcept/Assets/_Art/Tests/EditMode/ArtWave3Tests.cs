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

        [Test]
        public void GeriBildirimPaleti_RenkKorlugu_UiTokens()
        {
            DD.Prototype.UI.UiPalette was = DD.Prototype.UI.UiTokens.Palette;
            try
            {
                DD.Prototype.UI.UiTokens.Palette = DD.Prototype.UI.UiPalette.Default;
                Color critDefault = DD.Prototype.Feedback.FeedbackPalette.Critical;
                Color armorDefault = DD.Prototype.Feedback.FeedbackPalette.Armor;
                DD.Prototype.UI.UiTokens.Palette = DD.Prototype.UI.UiPalette.ColorBlindSafe;
                Color critSafe = DD.Prototype.Feedback.FeedbackPalette.Critical;
                Color armorSafe = DD.Prototype.Feedback.FeedbackPalette.Armor;
                Assert.AreEqual(DD.Prototype.UI.UiTokens.Get(DD.Prototype.UI.UiColor.StateWarning), critSafe, "kritik = uyarı jetonu");
                Assert.AreNotEqual(critDefault, critSafe, "renk körlüğü paletinde kritik rengi değişmeli (altın → amber)");
                Assert.AreEqual(DD.Prototype.UI.UiTokens.Get(DD.Prototype.UI.UiColor.AccentWisteria), armorSafe, "zırh = wisteria (kırmızı–altın ekseninin dışı)");
                Assert.AreNotEqual(armorDefault, armorSafe);
                Color kill = DD.Prototype.Feedback.FeedbackPalette.Kill;
                // Kill (coral) and critical (amber) must stay apart in luminance, not only in hue.
                float lumKill = 0.299f * kill.r + 0.587f * kill.g + 0.114f * kill.b;
                float lumCrit = 0.299f * critSafe.r + 0.587f * critSafe.g + 0.114f * critSafe.b;
                Assert.Greater(Mathf.Abs(lumKill - lumCrit), 0.08f, "öldürme / kritik parlaklık farkı");
            }
            finally
            {
                DD.Prototype.UI.UiTokens.Palette = was;
            }
        }

        [Test]
        public void V004_AsimetrikZirhKirilmasi_GolLoblari_AtesTopuRengi()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog);
            Assert.AreEqual("v004", catalog.ManifestVersion, "AP-010B v004 manifesti (WP-047H)");
            VfxCatalogEntry brk = catalog.Find(VfxIds.ArmorBreak);
            Assert.IsNotNull(brk, "fb_armor_break yok"); Assert.IsNotNull(brk.Material); Assert.IsNotNull(brk.Emissive, "kırılma emissive katmanı");
            Assert.AreEqual(VfxFamily.ArmorBreak, brk.Family); Assert.AreEqual(VfxKind.Flipbook, brk.Kind);
            Assert.AreEqual(0.3f, brk.OriginUv.x, 0.01f, "kırılma +u yönünde: origin u 0.3");
            Assert.AreEqual(0.6f, brk.WorldSizeMetres, 0.01f, "quad 0.60 m (= zırh sheet'i × 1.3)");
            Assert.AreEqual(16, brk.FramesUsed); Assert.AreEqual(30f, brk.Fps, 0.01f);
            Assert.IsNotNull(catalog.Find(VfxIds.ImpactArmor), "düz zırh vuruşu sheet'i kalır");
            Assert.IsTrue(catalog.Find(VfxIds.Explosion).SourceFile.Contains("_v004"), "ateş topu retint (v004) sevk edilir");
            Assert.IsTrue(catalog.Find(VfxIds.BloodPoolAlbedo).SourceFile.Contains("_v004"), "göl lobları (v004) sevk edilir");
            Assert.IsTrue(catalog.Find(VfxIds.BloodPoolGrowth).SourceFile.Contains("_v004"), "göl büyüme maskeleri lob kenarını izler");
        }

        [Test]
        public void Wave3D_KatalogSatirlari_BarrelHeat_ChainArc()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog);
            VfxCatalogEntry heat = catalog.Find(VfxIds.BarrelHeat);
            Assert.IsNotNull(heat, "barrel_heat yok"); Assert.IsNotNull(heat.Material);
            Assert.Greater(heat.WorldHeightMetres, heat.WorldSizeMetres, "barrel_heat dikey şerit ([w,h] 0.08 × 0.45)");
            VfxCatalogEntry arc = catalog.Find(VfxIds.ChainArcSheet);
            Assert.IsNotNull(arc, "chain_arc_sheet yok"); Assert.IsNotNull(arc.Material);
            Assert.AreEqual(2.5f, arc.WorldSizeMetres, 0.01f);
            Assert.IsTrue(VfxDressing.ChainArcMinMetres < 2.5f && VfxDressing.ChainArcMaxMetres <= 2.5f * 1.4f + 0.01f, "arkın gerilme aralığı manifestin 0.6–1.4× bandında");
        }
    }
}
