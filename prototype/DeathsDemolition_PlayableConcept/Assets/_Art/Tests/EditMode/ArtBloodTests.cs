using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-019b · Kan seti sözleşmesi: slotlar v002 fallback'iyle çözülür, damlacık satırı
    /// sentezlenir (materyal + instancing), katalog ortalama renkleri yazılı ve recolour
    /// hedefe oturuyor, palet bandı/rezerv ayrımı, AP010B klasör/boyut/id kuralları, ve
    /// olay kapsam matrisinde boş VFX hücresi yok (direktif §12).
    /// </summary>
    [TestFixture]
    [Category("WP019")]
    public sealed class ArtBloodTests
    {
        private static VfxCatalog Katalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VfxCatalog>(ArtVfxImporter.CatalogPath);
            Assert.IsNotNull(catalog, "Katalog yok: " + ArtVfxImporter.CatalogPath);
            return catalog;
        }

        [Test]
        public void KanSlotlari_v002Fallback_IleCozulur_DamlacikSatiriSentezli()
        {
            VfxCatalog c = Katalog();
            Assert.IsNotNull(c.Find(VfxIds.ImpactFlesh), "fb_impact_flesh yok");
            Assert.IsNotNull(c.Find(VfxIds.DecalImpactFlesh));
            Assert.IsNotNull(c.Find(VfxIds.DecalZombieSplat01));

            VfxCatalogEntry droplet = c.Find(VfxIds.BloodDroplet);
            Assert.IsNotNull(droplet, "blood_droplet satırı sentezlenmedi (Run VFX Catalog).");
            Assert.AreEqual(VfxFamily.Blood, droplet.Family);
            Assert.IsNotNull(droplet.Material, "damlacık materyali yok");
            Assert.IsTrue(droplet.Material.enableInstancing, "damlacık materyali instancing kapalı");
            Assert.IsNotNull(droplet.Material.GetTexture("_Sheet"), "damlacık materyalinde _Sheet boş (ayrı batch dersi)");
            Color tint = droplet.Material.GetColor("_Tint");
            VfxCatalogEntry dropletSource = c.Find(VfxIds.BloodDropletSheet);
            if (dropletSource != null && dropletSource.Family == VfxFamily.Blood) Assert.AreEqual(Color.white, tint, "v003 damlacık sheet'i boyalı: tint yok");
            else Assert.Greater(tint.r, tint.g * 3f, "damlacık tint'i kızıl değil: " + tint);
            Assert.AreEqual("synthesized", droplet.ManifestFile);

            // Slot ids resolve in order; the v002 rows stay as the tail fallback, the AP-010B v003 rows lead.
            Assert.AreEqual(VfxIds.ImpactFlesh, BloodSlots.Splash[BloodSlots.Splash.Length - 1]);
            Assert.AreEqual(VfxIds.DecalImpactFlesh, BloodSlots.GroundDecal[BloodSlots.GroundDecal.Length - 1]);
            Assert.AreEqual(VfxIds.DecalImpactFlesh, BloodSlots.WallDecal[BloodSlots.WallDecal.Length - 1]);
            Assert.AreEqual(VfxIds.DecalZombieSplat02, BloodSlots.Pool[BloodSlots.Pool.Length - 1]);
            foreach (string[] slot in new[] { BloodSlots.Splash, BloodSlots.Heavy, BloodSlots.GroundDecal, BloodSlots.WallDecal, BloodSlots.Droplet, BloodSlots.Pool, BloodSlots.DeathSheet })
            {
                for (int i = 0; i < slot.Length; i++)
                {
                    if (!slot[i].Contains("_blood_")) continue;   // the v002 tail rows (impact flesh, splats, zombiedeath)
                    VfxCatalogEntry r = c.Find(slot[i]);
                    Assert.IsNotNull(r, "AP-010B v003 satırı yok: " + slot[i]);
                    Assert.AreEqual(VfxFamily.Blood, r.Family, slot[i] + " ailesi");
                    Assert.IsNotNull(r.Material, slot[i] + " materyali yok");
                    Assert.IsNotNull(r.Texture, slot[i] + " dokusu yok");
                }
            }

            // v003 contract points read from the manifest, not the name: A1 origin (0.12, 0.5), A6 (0.5, 0.08), muzzle 8×8 @ 60, explosion 48 f, shockwave 5.0 m.
            Assert.AreEqual(new Vector2(0.12f, 0.5f), c.Find(VfxIds.BloodSplash01).OriginUv);
            Assert.AreEqual(new Vector2(0.5f, 0.08f), c.Find(VfxIds.BloodDeath01).OriginUv);
            Assert.AreEqual(8, c.Find(VfxIds.Muzzle).Columns); Assert.AreEqual(24, c.Find(VfxIds.Muzzle).FramesUsed); Assert.AreEqual(60f, c.Find(VfxIds.Muzzle).Fps);
            Assert.AreEqual(48, c.Find(VfxIds.Explosion).FramesUsed);
            Assert.AreEqual(5f, c.Find(VfxIds.Shockwave).WorldSizeMetres);
            Assert.AreEqual(VfxIds.BloodSplash01, ArtVfxImporter.IdOf("AP010B/blood/AP010B_fb_blood_splash_01_8x8_v003.png"));
            Assert.AreEqual(VfxIds.BloodDeath01, ArtVfxImporter.IdOf("AP010B/blood/AP010B_fb_blood_death_01_8x8_v003b.png"));
            Assert.AreEqual(1.4f, c.Find("decal_blood_ground_02_albedo").WorldSizeMetres); Assert.AreEqual(0.5f, c.Find("decal_blood_ground_02_albedo").WorldHeightMetres);
            Assert.IsTrue(c.Find(VfxIds.Spark).Additive, "spark additive (manifest blend)");
            Assert.IsFalse(c.Find(VfxIds.WaterSplash).Additive);
            Assert.AreNotEqual(Color.white, c.Find(VfxIds.WaterSplash).DefaultTint, "LA sheet tint from the manifest");
        }

        [Test]
        public void KatalogOrtalamaRenk_Yazili_RecolourHedefeOturur()
        {
            VfxCatalog c = Katalog();
            foreach (string id in new[] { VfxIds.ImpactFlesh, VfxIds.DecalImpactFlesh, VfxIds.DecalZombieSplat01, VfxIds.DecalZombieSplat02, VfxIds.ZombieDeath })
            {
                VfxCatalogEntry row = c.Find(id);
                Assert.IsNotNull(row, id);
                Color mean = row.MeanOpaqueColour;   // linear
                Assert.AreNotEqual(Color.white, mean, id + ": ortalama renk yazılmamış");
                Color meanSrgb = new Color(Mathf.LinearToGammaSpace(mean.r), Mathf.LinearToGammaSpace(mean.g), Mathf.LinearToGammaSpace(mean.b), 1f);
                Color.RGBToHSV(meanSrgb, out _, out float s, out float v);
                Assert.Less(v, 0.45f, id + ": v002 sheet koyu-gri beklenirdi, v=" + v);
                Assert.Less(s, 0.30f, id + ": v002 sheet düşük doygunluk beklenirdi, s=" + s);

                // The shader multiplies the linear texel by the linear tint (Unity gamma→linear on upload): the mean lands on the target.
                Color tint = BloodPalette.TintFor(row, BloodPalette.DecalWet);
                Color landed = new Color(
                    Mathf.LinearToGammaSpace(mean.r * Mathf.GammaToLinearSpace(tint.r)),
                    Mathf.LinearToGammaSpace(mean.g * Mathf.GammaToLinearSpace(tint.g)),
                    Mathf.LinearToGammaSpace(mean.b * Mathf.GammaToLinearSpace(tint.b)), 1f);
                Assert.That(landed.r, Is.EqualTo(BloodPalette.DecalWet.r).Within(0.02f), id + " r");
                Assert.That(landed.g, Is.EqualTo(BloodPalette.DecalWet.g).Within(0.02f), id + " g");
                Assert.That(landed.b, Is.EqualTo(BloodPalette.DecalWet.b).Within(0.02f), id + " b");
                Assert.LessOrEqual(Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b)), BloodPalette.MaxGain + 1e-3f);
            }
        }

        [Test]
        public void KanPaleti_BantIcinde_RezervlerdenAyri()
        {
            foreach (var (name, colour) in new[] { ("fresh", BloodPalette.Fresh), ("wet", BloodPalette.DecalWet), ("dry", BloodPalette.DecalDry), ("droplet", BloodPalette.Droplet) })
            {
                Assert.IsTrue(BloodPalette.IsInBand(colour, out float h, out float s, out float v), name + " bant dışı: h=" + h + " s=" + s + " v=" + v);
                Assert.GreaterOrEqual(BloodPalette.DeltaE(colour, VfxLibrary.BeaconTint), 25f, name + " ↔ beacon");
                Assert.GreaterOrEqual(BloodPalette.DeltaE(colour, VfxLibrary.VermilionTint), 25f, name + " ↔ vermilion");
                Assert.GreaterOrEqual(BloodPalette.DeltaE(colour, VfxLibrary.GoldTint), 25f, name + " ↔ gold");
                Color.RGBToHSV(BloodPalette.SkinOneAccent, out _, out _, out float skinV);
                Assert.GreaterOrEqual(skinV - v, 0.15f, name + " ↔ skin1 değer ayrımı");
                Debug.Log("[DD-TEST] wp019b.palette " + name + " hsv=(" + h.ToString("F0") + "," + s.ToString("F2") + "," + v.ToString("F2") + ") dE skin1=" + BloodPalette.DeltaE(colour, BloodPalette.SkinOneAccent).ToString("F1") +
                          " beacon=" + BloodPalette.DeltaE(colour, VfxLibrary.BeaconTint).ToString("F1") + " vermilion=" + BloodPalette.DeltaE(colour, VfxLibrary.VermilionTint).ToString("F1") + " gold=" + BloodPalette.DeltaE(colour, VfxLibrary.GoldTint).ToString("F1"));
            }

            Assert.GreaterOrEqual(BloodPalette.DeltaE(BloodPalette.DecalWet, BloodPalette.DecalDry), 8f, "ıslak ↔ kuru");
            Assert.AreEqual(BloodPalette.DecalWet, BloodPalette.DecalColourAt(BloodPalette.DryHoldSeconds));
            Assert.AreEqual(BloodPalette.DecalDry, BloodPalette.DecalColourAt(BloodPalette.DrySeconds + 1f));
            Assert.LessOrEqual(BloodSystem.SplashSeconds, 0.3f);
            Assert.LessOrEqual(BloodSystem.DecalAlphaCap, 0.85f);
            Assert.AreEqual(0.6f, BloodSystem.GoldExclusionMetres);
            Assert.GreaterOrEqual(BloodSystem.DecalCapacity, 128);
        }

        [Test]
        public void AP010B_KlasorBoyutId_Kurallari()
        {
            Assert.IsTrue(ArtVfxContract.IsVfxAsset("Assets/_Art/VFX/AP010B/flipbooks/AP010B_fb_blood_splash_01_8x8_v001.png"));
            Assert.IsTrue(ArtVfxContract.IsSupplementAsset("Assets/_Art/VFX/AP010B/flipbooks/AP010B_fb_blood_splash_01_8x8_v001.png"));
            Assert.IsFalse(ArtVfxContract.IsSupplementAsset("Assets/_Art/VFX/AP010/flipbooks/AP010_fb_impact_flesh_8x8_v002.png"));
            Assert.IsFalse(ArtVfxContract.IsVfxAsset("Assets/_Art/VFX/Resources/DD_VfxCatalog.asset"));
            Assert.IsFalse(ArtVfxContract.IsVfxAsset("Assets/_Art/VFX/AP0101/x.png"));
            Assert.AreEqual(1024, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010B/flipbooks/AP010B_fb_blood_splash_01_8x8_v001.png"));
            Assert.AreEqual(512, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010B/blood/AP010B_decal_blood_ground_01_albedo_2048_v003.png"));
            Assert.AreEqual(128, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010B/blood/AP010B_decal_blood_ground_01_normal_2048_v003.png"));
            Assert.AreEqual(2048, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010/flipbooks/AP010_fb_explosion_8x8_v003.png"));
            Assert.AreEqual(512, ArtVfxContract.MaxSizeOf("Assets/_Art/VFX/AP010B/blood/AP010B_sheet_blood_droplets_4x4_v003b.png"));
            Assert.AreEqual("fb_blood_splash_01", ArtVfxImporter.IdOf("flipbooks/AP010B_fb_blood_splash_01_8x8_v001.png"));
            Assert.AreEqual("decal_blood_pool", ArtVfxImporter.IdOf("decals/AP010B_decal_blood_pool_2048_v001.png"));
            Assert.AreEqual("fb_impact_flesh", ArtVfxImporter.IdOf("flipbooks/AP010_fb_impact_flesh_8x8_v002.png"));
            Assert.AreEqual(60, ArtVfxContract.ShippedBloodBudgetMb);
            Assert.AreEqual(0, ArtVfxImporter.SupplementManifests().Count, "AP-010B v003 ana manifestte birleşik; ayrı ek manifest beklenmiyor.");
        }

        /// <summary>WP-037 finding (C-00): the per-number string in DamageNumberEffect was the whole hot-path regression; the table path allocates nothing after warm-up.</summary>
        [Test]
        public void HasarSayisiMetni_SifirAyirma()
        {
            for (int i = 0; i < 1200; i++) DD.Prototype.Feedback.DamageNumberEffect.NumberText(i);
            DD.Prototype.Feedback.DamageNumberEffect.NumberText(4242);
            // Used-heap delta (GC.GetAllocatedBytesForCurrentThread is always 0 in Unity Mono): 12 000 lookups would be ~300 KB as strings.
            long before = System.GC.GetTotalMemory(false);
            for (int r = 0; r < 10; r++) { for (int i = 0; i < 1200; i++) DD.Prototype.Feedback.DamageNumberEffect.NumberText(i); DD.Prototype.Feedback.DamageNumberEffect.NumberText(4242); }
            long after = System.GC.GetTotalMemory(false);
            Assert.LessOrEqual(after - before, 4096, "NumberText ısınma sonrası ayırdı: " + (after - before) + " B / 12 010 çağrı");
            Assert.AreSame(DD.Prototype.Feedback.DamageNumberEffect.NumberText(12), DD.Prototype.Feedback.DamageNumberEffect.NumberText(12));
        }

        [Test]
        public void OlayKapsamMatrisi_BosSatirYok()
        {
            // Shared file with S-08 (WP-021): section tables "| Olay | Kaynak | SFX | 2D/3D | Bus | Durum | VFX … |" — the last
            // column is the VFX cell; the UI table's last column is S-09's micro-animation cell (also must not be empty).
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../planning/work-packages/EVENT-COVERAGE-MATRIX.md"));
            Assert.IsTrue(File.Exists(path), "Matris yok: " + path);
            var problems = new List<string>();
            int rows = 0, na = 0, pending = 0, vfxTables = 0;
            bool inVfxTable = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (!line.StartsWith("|")) { inVfxTable = false; continue; }
                string[] cells = line.Trim('|').Split('|');
                if (cells.Length < 7) { inVfxTable = false; continue; }
                string last = cells[cells.Length - 1].Trim();
                if (line.StartsWith("|---")) continue;
                bool header = last.StartsWith("VFX (S-07)");   // S-09's UI micro-animation column is not this gate
                if (header) { inVfxTable = true; vfxTables++; continue; }
                if (!inVfxTable) continue;
                string evt = cells[0].Trim();
                rows++;
                if (last.Length == 0 || last == "—" || last == "-") problems.Add("VFX hücresi boş: " + evt);
                if (last.StartsWith("n/a"))
                {
                    na++;
                    if (last.Length < 12) problems.Add("n/a gerekçesiz: " + evt);
                }

                if (last.Contains("WP-047")) pending++;
            }

            Debug.Log("[DD-TEST] wp019b.matrix vfxTables=" + vfxTables + " rows=" + rows + " n/a=" + na + " wp047Pending=" + pending);
            Assert.GreaterOrEqual(vfxTables, 8, "bölüm tabloları (oyuncu, zombi, 2b olay yüzeyi, zırh, kırılabilir, çevre, silah, ekonomi)");
            Assert.GreaterOrEqual(rows, 80, "matris satır sayısı");
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
