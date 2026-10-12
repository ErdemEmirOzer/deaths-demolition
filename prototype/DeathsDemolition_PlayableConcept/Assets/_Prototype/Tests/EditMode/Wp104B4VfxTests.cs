using System;
using System.Collections.Generic;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B4-1 (S-07) · the pure side of the run tree's VFX binding: every TreeProcKind has its AP-041 family and sheet (AD §3),
    /// the tracer ladder is AD-B4 §1 (the core no tier channel; the halo its WP-101 width + 0 / 5 / 10 / 15 / 20 px of max(Shot,
    /// Element), no ceiling; the trail × 1.0 / 1.0 / 1.1 / 1.25 / 1.45 of Shot, ≤ 10 m; V's halo HDR × 1.5) beside the AD §2 impact /
    /// emissive columns, the core ceiling (the bright core ≤ max(8 px, its WP-101 width) at 1080p) touches only a styled round
    /// (neutral = WP-101 byte-identical), Reduce Flashes dims the halo's HDR and keeps its α (M3), an element / marked halo covers the
    /// ground (M5), the marked round takes the V halo and a 0.85 sumi core (M4), S-02's SizeScale
    /// multiplies and a TD record merge widens like S-02's merged shot, the add-on emissive ladder (S-06 E4 / E5) is 0 / 0.3 / 0.6 /
    /// 1.0 / 1.5 × FlashScale and black while hidden, the element palette keeps A1 / A2 (ΔE ≥ 25 between trails, ≥ 35 from the gold
    /// orb) and the aura A4 / A17, K1 paints with S-04's TreeStats.DominantElement (owned, the resolver's "highest tier, tie to the
    /// later purchase" — no fixed order), the marked-round bit is S-04's TreeRoundMarkedChaos, the synergy ensō tints are
    /// synergy-map.json's, ExplosionSource.Tree is S-02's 2, and the presentation clock never runs backwards.
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B4VfxTests
    {
        [TearDown]
        public void TearDown()
        {
            TreeRuntime.ClearTestStats();
            TracerStyle.ResetForTests();
        }

        // ------------------------------------------------------------------ mapping

        [Test]
        public void ProcTuru_HerBiriAileVeSayfaTasir()
        {
            foreach (TreeProcKind kind in Enum.GetValues(typeof(TreeProcKind)))
            {
                if (kind == TreeProcKind.None)
                {
                    Assert.IsNull(TreeVfxIds.FamilyOf(kind));
                    continue;
                }

                string family = TreeVfxIds.FamilyOf(kind);
                Assert.IsNotNull(family, kind + ": AD §3 ailesi yok");
                if (family == "—")
                {
                    Assert.AreEqual(TreeVfxRow.Count, TreeVfxIds.PrimaryRow(kind), kind + ": '—' ailesinin B4 sayfası olmaz");
                    continue;
                }

                if (kind == TreeProcKind.EndlessWindowClose) continue;   // closes the open's ensō
                Assert.AreNotEqual(TreeVfxRow.Count, TreeVfxIds.PrimaryRow(kind), kind + ": ana sayfa yok");
            }

            // B0 24 + B1b ShieldUp 25 / ShieldHit 26 / Ricochet 27 / BarrageRocket 28 (TreeContract.cs on main)
            Assert.AreEqual(28, Enum.GetValues(typeof(TreeProcKind)).Length - 1, "sözleşmeye yeni tür eklendiyse eşleme tablosu güncellenmeli");
            Assert.AreEqual(25, (int)TreeProcKind.ShieldUp, "S-04 12.10: ShieldRestored yok, ShieldUp = 25");
            Assert.AreEqual(TreeVfxRow.B14Dome, TreeVfxIds.PrimaryRow(TreeProcKind.ShieldUp));
            Assert.AreEqual(TreeVfxRow.B14Hit, TreeVfxIds.PrimaryRow(TreeProcKind.ShieldHit), "emilen vuruş = B14 vuruş dalgası");
            Assert.AreEqual(TreeVfxRow.B14Break, TreeVfxIds.PrimaryRow(TreeProcKind.ShieldBreak));
            Assert.AreEqual(TreeVfxRow.B15Thread, TreeVfxIds.PrimaryRow(TreeProcKind.Leech), "can emme ipliği");
            Assert.AreEqual(TreeVfxRow.X8Enso, TreeVfxIds.PrimaryRow(TreeProcKind.SynergyFirstTrigger), "sinerji ensō");
        }

        [Test]
        public void Kimlikler_Benzersiz_SatirSayisiTutar()
        {
            Assert.AreEqual((int)TreeVfxRow.Count, TreeVfxIds.Count);
            Assert.AreEqual(45, TreeVfxIds.Count, "AP-041 v001 + v002 ağaç sayfaları (B1–B17, T3–T5, X8)");
            var seen = new HashSet<string>();
            for (int i = 0; i < (int)TreeVfxRow.Count; i++)
            {
                string id = TreeVfxIds.Id((TreeVfxRow)i);
                Assert.IsTrue(seen.Add(id), id + " çift");
                Assert.IsTrue(id.StartsWith(TreeVfxIds.Prefix, StringComparison.Ordinal) || id.StartsWith(TreeVfxIds.DecalPrefix, StringComparison.Ordinal), id);
            }

            Assert.AreEqual(TreeVfxRow.B8Chain1, TreeVfxIds.ChainRow(0));
            Assert.AreEqual(TreeVfxRow.B8Chain3, TreeVfxIds.ChainRow(2));
            Assert.AreEqual(TreeVfxRow.B8Chain1, TreeVfxIds.ChainRow(3), "üç varyant sırayla");
            Assert.AreEqual(TreeVfxRow.B11Ring2, TreeVfxIds.AuraRing(2));
            Assert.AreEqual(TreeVfxRow.B11Sparks3, TreeVfxIds.AuraSparks(7), "aura III tavan");
            Assert.AreEqual(TreeVfxRow.B1Ignition, TreeVfxIds.ElementImpact(TreeElement.Fire));
            Assert.AreEqual(TreeVfxRow.B4Cloud, TreeVfxIds.ElementImpact(TreeElement.Venom));
            Assert.AreEqual(TreeVfxRow.Count, TreeVfxIds.ElementImpact(TreeElement.None));
        }

        // ------------------------------------------------------------------ AD-B4 §1 tracer ladder + AD §2 impact / emissive

        [TestCase(1, 0f, 1f, .6f, 0f)]
        [TestCase(2, 5f, 1f, .75f, .3f)]
        [TestCase(3, 10f, 1.1f, .9f, .6f)]
        [TestCase(4, 15f, 1.25f, 1f, 1f)]
        [TestCase(5, 20f, 1.45f, 1.25f, 1.5f)]
        public void Merdiven_AdTablosu(int tier, float haloPx, float trail, float impact, float emissive)
        {
            Assert.AreEqual(haloPx, TreeVfxLadder.HaloStepPx(tier), 1e-5f, "AD-B4 §1 hale: WP-101 tabanı + 0/5/10/15/20 px (1080p)");
            Assert.AreEqual(trail, TreeVfxLadder.TrailScale(tier), 1e-5f, "AD-B4 §1 iz boyu ×1,0/1,0/1,1/1,25/1,45");
            Assert.AreEqual(impact, TreeVfxLadder.ImpactScale(tier), 1e-5f);
            Assert.AreEqual(emissive, TreeVfxLadder.Emissive(tier), 1e-5f);
        }

        [Test]
        public void Merdiven_KademeSifirIGibi_AltiVGibi()
        {
            Assert.AreEqual(0f, TreeVfxLadder.HaloStepPx(0), "dalda düğüm yok = I (WP-101 gerçekçi)");
            Assert.AreEqual(1f, TreeVfxLadder.TrailScale(0), "dalda düğüm yok = I (WP-101 boyu)");
            Assert.AreEqual(20f, TreeVfxLadder.HaloStepPx(6), 1e-5f);
            Assert.AreEqual(1.45f, TreeVfxLadder.TrailScale(6), 1e-5f);
            Assert.AreEqual(TreeVfxLadder.Sumi.None, TreeVfxLadder.SumiFor(2, false));
            Assert.AreEqual(TreeVfxLadder.Sumi.InkShock, TreeVfxLadder.SumiFor(3, false));
            Assert.AreEqual(TreeVfxLadder.Sumi.InkShock, TreeVfxLadder.SumiFor(4, false), "IV patlamada mürekkep şok");
            Assert.AreEqual(TreeVfxLadder.Sumi.Enso, TreeVfxLadder.SumiFor(4, true), "IV çekimde ensō");
            Assert.AreEqual(TreeVfxLadder.Sumi.Spatter, TreeVfxLadder.SumiFor(5, true));
        }

        [Test]
        public void ReduceFlashes_VEmissiveBloomEsiginAltina()
        {
            Assert.Greater(TreeVfxLadder.EmissiveFor(5, 1f), TreeVfxLadder.BloomThreshold, "V bloom üstünde (AD §2)");
            Assert.Less(TreeVfxLadder.EmissiveFor(5, .4f), TreeVfxLadder.BloomThreshold, "Reduce Flashes: V bloom altında");
            Assert.AreEqual(0f, TreeVfxLadder.EmissiveFor(1, 1f));
        }

        // ------------------------------------------------------------------ S-06 add-on glow ladder

        [TestCase(0, 0f)]
        [TestCase(1, 0f)]
        [TestCase(2, .3f)]
        [TestCase(3, .6f)]
        [TestCase(4, 1f)]
        [TestCase(5, 1.5f)]
        public void EkIsimasi_Merdiven_FlashScale_GizliykenKapali(int tier, float want)
        {
            Color on = TreeVfxLadder.AddOnEmission(tier, 1f, true);
            Assert.AreEqual(want, on.r, 1e-5f, "AD §2 emissive × FlashScale (beyaz; renk ışıma haritasından)");
            Assert.AreEqual(on.r, on.g, 1e-6f);
            Assert.AreEqual(on.r, on.b, 1e-6f);
            Assert.AreEqual(1f, on.a);
            Assert.AreEqual(want * .4f, TreeVfxLadder.AddOnEmission(tier, .4f, true).r, 1e-5f, "Reduce Flashes × FlashScale");
            Assert.AreEqual(0f, TreeVfxLadder.AddOnEmission(tier, 1f, false).r, "Shown = false: kapalı");
        }

        [TestCase(1, 0f, 0f)]
        [TestCase(2, .3f, .3f)]
        [TestCase(3, 1.4f, .6f)]
        [TestCase(4, 1.6f, 1.4f)]
        [TestCase(5, 2f, 2f)]
        public void EkIsimasi_M9_E4E5_FlashScale_E1E3Merdivende(int tier, float e4, float e5)
        {
            Assert.AreEqual(e4, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, tier, 1f, true).r, 1e-5f, "AD-B4 M9 E4");
            Assert.AreEqual(e5, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Ring, tier, 1f, true).r, 1e-5f, "AD-B4 M9 E5");
            Assert.AreEqual(e4 * .4f, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, tier, .4f, true).r, 1e-5f, "× FlashScale");
            Assert.AreEqual(e5 * .4f, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Ring, tier, .4f, true).r, 1e-5f, "× FlashScale");
            Assert.AreEqual(0f, TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Side, tier, 1f, false).r, "gizliyken kapalı");
            Assert.AreEqual(TreeVfxLadder.Emissive(tier), TreeVfxLadder.AddOnEmission(TreeVfxLadder.AddOnGlow.Silhouette, tier, 1f, true).r, 1e-5f, "E1–E3: §2 merdiveni");
            if (tier >= 3) Assert.Greater(TreeVfxLadder.AddOnEmissive(TreeVfxLadder.AddOnGlow.Side, tier), TreeVfxLadder.BloomThreshold, "E4 III+ bloom üstünde (§5 Q4)");
            if (tier >= 4) Assert.Greater(TreeVfxLadder.AddOnEmissive(TreeVfxLadder.AddOnGlow.Ring, tier), TreeVfxLadder.BloomThreshold, "E5 IV+ bloom üstünde");
        }

        // ------------------------------------------------------------------ ceilings (A5) and the halo step (AD-B4 §1)

        [Test]
        public void Tavan_Cekirdek8pxYaDaWp101Eni_HaleTavansiz_KademeAdimiPx()
        {
            Assert.AreEqual(8f, TracerStyle.CoreCapPx, "AD §2 / A5");
            Assert.AreEqual(TracerStyle.CoreCapPx, TracerStyle.CoreQuadCapPx * TracerStyle.CoreActiveShare, 1e-4f, "quad tavanının parlak kısmı = 8 px");
            float ar = TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle);
            float sr = TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Sniper);
            float body = TracerRibbonRenderer.TdBodyWidth;
            float cap = TracerStyle.CoreQuadCapPx;

            // neutral (capPx 0, step 0): WP-101 to the byte (_Extra.y, .z and .w stay 0)
            Assert.AreEqual(0f, TracerStyle.ShaderCap(0, true, false, 0f));
            Assert.AreEqual(0f, TracerStyle.ShaderCap(2, true, false, 0f));
            Assert.AreEqual(0f, TracerStyle.ShaderBase(0, true, false, 0f, ar));
            Assert.AreEqual(0f, TracerStyle.ShaderStep(2, true, 0f), "kademe I: hale adımı yok");
            // styled: the core / body ceiling (px at 1080p) and its floor (the instance's WP-101 width, −m); vapour and plain pellet bars none
            Assert.AreEqual(-cap, TracerStyle.ShaderCap(0, true, false, cap));
            Assert.AreEqual(-ar, TracerStyle.ShaderBase(0, true, false, cap, ar), 1e-6f);
            Assert.AreEqual(-cap, TracerStyle.ShaderCap(2, false, false, cap), "gövde izi çekirdek tavanında");
            Assert.AreEqual(-body, TracerStyle.ShaderBase(2, false, false, cap, body), 1e-6f);
            Assert.AreEqual(0f, TracerStyle.ShaderCap(1, true, false, cap), "sniper buharı tavan almaz");
            Assert.AreEqual(0f, TracerStyle.ShaderBase(1, true, false, cap, sr));
            Assert.AreEqual(0f, TracerStyle.ShaderCap(0, false, true, cap), "düz saçma çubuğu tavan almaz");
            // AD-B4 §1 / §5 soru 1: the fixed 64 px halo ceiling is gone; the tier step rides _Extra.y < 0 on the halo alone
            Assert.AreEqual(0f, TracerStyle.ShaderCap(2, true, false, cap), "hale tavansız");
            Assert.AreEqual(0f, TracerStyle.ShaderBase(2, true, false, cap, ar));
            Assert.AreEqual(-20f, TracerStyle.ShaderStep(2, true, 20f), "hale adımı: _Extra.y = −px");
            Assert.AreEqual(0f, TracerStyle.ShaderStep(0, true, 20f), "çekirdek adım almaz");
            Assert.AreEqual(0f, TracerStyle.ShaderStep(2, false, 20f), "gövde izi adım almaz");

            // the core at every tier = its WP-101 width (no tier channel: it neither grows nor thins, A5 / §5 soru 2) at three camera
            // scales (95 px/m 1080p ground, ≈ 100 at the AD frames' 1 m lane, ≈ 102 at the 1.39 m shot height); S-02's SizeScale grows
            // the AR quad only to max(11.1 px, its WP-101 width) - the bright part ≤ max(8 px, the class base)
            foreach (float px in new[] { 95f, 100f, 102f })
            {
                Assert.AreEqual(ar * px, TracerStyle.ClampedPx(ar * px, -cap, -ar, px), 1e-3f, "AR çekirdeği WP-101 eni (" + px + " px/m)");
                Assert.AreEqual(sr * px, TracerStyle.ClampedPx(sr * px, -cap, -sr, px), 1e-3f, "SR: WP-101 eni korunur (büyümez, incelmez)");
                Assert.AreEqual(Mathf.Max(cap, ar * px), TracerStyle.ClampedPx(ar * 1.25f * px, -cap, -ar, px), 1e-3f, "SizeScale çekirdeği tavanda durur");
                Assert.AreEqual(sr * px, TracerStyle.ClampedPx(sr * 1.25f * px, -cap, -sr, px), 1e-3f, "SR SizeScale: kendi WP-101 eni tavan");
            }

            Assert.AreEqual(12f, TracerStyle.ClampedPx(12f, 0f, 0f, 95f), "tavansız (nötr) genişlik değişmez");
            Assert.GreaterOrEqual(TracerRibbonRenderer.TdCoreMinPx, 3f, "A5 alt sınır");
        }

        /// <summary>AD-B4 M1 in px at the AD frames' scale (≈ 100 px/m at the 1 m lane, 1080p): AR I–V 44 / 49 / 54 / 59 / 64 (±2),
        /// an equal step ≥ 4 px; LMG / SR V = their WP-101 halo + 20 (SR ≈ 84: §5 soru 1's accepted exception); at 2160p the step
        /// doubles with the metre width (resolution-proportional).</summary>
        [Test]
        public void M1_HaleMerdiveni_Ar44_49_54_59_64_LmgSrV_TabanArti20()
        {
            const float ppm = 100f;
            float arHalo = TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle) * TracerRibbonRenderer.TdHaloScale;
            float[] want = { 44f, 49f, 54f, 59f, 64f };
            float previous = 0f;
            for (int tier = 1; tier <= 5; tier++)
            {
                float px = TracerStyle.HaloWidthPx(arHalo, TracerStyle.ShaderStep(2, true, TreeVfxLadder.HaloStepPx(tier)), ppm);
                Assert.AreEqual(want[tier - 1], px, 2f, "AR " + tier + " hale (±2 px)");
                if (tier > 1) Assert.GreaterOrEqual(px - previous, 4f, "komşu kademe farkı ≥ 4 px");
                previous = px;
            }

            foreach (VfxWeaponClass c in new[] { VfxWeaponClass.Lmg, VfxWeaponClass.Sniper })
            {
                float halo = TracerRibbonRenderer.TdCoreWidth(c) * TracerRibbonRenderer.TdHaloScale;
                Assert.AreEqual(halo * ppm + 20f, TracerStyle.HaloWidthPx(halo, TracerStyle.ShaderStep(2, true, TreeVfxLadder.HaloStepPx(5)), ppm), 1e-3f,
                    c + " V = WP-101 halesi + 20 px");
            }

            float srHalo = TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.Sniper) * TracerRibbonRenderer.TdHaloScale;
            Assert.AreEqual(84f, TracerStyle.HaloWidthPx(srHalo, -20f, ppm), 1f, "SR V ≈ 84 px");
            Assert.AreEqual(arHalo * ppm * 2f + 40f, TracerStyle.HaloWidthPx(arHalo, -20f, ppm * 2f, 2160f), 1e-3f, "çözünürlüğe oranlı: 2160p'de adım 40 px");
        }

        /// <summary>AD-B4 M1 length: the WP-101 one-exposure trail (the AD frames' AR at 260 m/s: 4.33 m) × the Shot tier — I–II as
        /// it is, III ≥ ×1.08, IV ≥ ×1.22, V ≥ ×1.40, never past 10 m (the sniper's own ceiling).</summary>
        [Test]
        public void M1_IzBoyu_AtistanKatsayi_I_IIWp101_V145_En10m()
        {
            float ar = Mathf.Clamp(260f * TracerRibbonRenderer.TdExposureSeconds, TracerRibbonRenderer.TdTailMin, TracerRibbonRenderer.TdTailMax);
            Assert.AreEqual(ar, TracerStyle.ScaledTrail(ar, TreeVfxLadder.TrailScale(1)), "I: WP-101 boyu");
            Assert.AreEqual(ar, TracerStyle.ScaledTrail(ar, TreeVfxLadder.TrailScale(2)), "II: WP-101 boyu (gerçekçilik I–II'de korunur)");
            Assert.GreaterOrEqual(TracerStyle.ScaledTrail(ar, TreeVfxLadder.TrailScale(3)) / ar, 1.08f, "III ≥ ×1,08");
            Assert.GreaterOrEqual(TracerStyle.ScaledTrail(ar, TreeVfxLadder.TrailScale(4)) / ar, 1.22f, "IV ≥ ×1,22");
            Assert.GreaterOrEqual(TracerStyle.ScaledTrail(ar, TreeVfxLadder.TrailScale(5)) / ar, 1.40f, "V ≥ ×1,40");
            Assert.AreEqual(TreeVfxLadder.TrailMaxMetres, TracerStyle.ScaledTrail(TracerRibbonRenderer.TdSniperTailMax, TreeVfxLadder.TrailScale(5)), 1e-5f, "≤ 10 m");
            Assert.AreEqual(3f, TracerStyle.ScaledTrail(3f, 0f), "stilsiz kayıt (0) WP-101 boyunda");
        }

        /// <summary>AD-B4 M3: a steady trail is no flash — Reduce Flashes keeps the halo's α (it was × 0.4: the streak lost its form)
        /// and dims its HDR × FlashScale (V's S1 boost first, never under the 0.4 floor: no dark veil); under it the neutral halo stays
        /// below white over the core and below the bloom threshold.</summary>
        [Test]
        public void ReduceFlashes_HaleAlfasiKalir_HdrKisilir_VArtisiOnceSoner()
        {
            Assert.GreaterOrEqual(TracerStyle.HaloAlphaFlashFactor, .7f, "M3: hale α çarpanı ≥ 0,70");
            foreach (int look in new[] { TracerStyle.NeutralLook, TracerStyle.FireLook, TracerStyle.ShockLook, TracerStyle.MarkedLook })
            {
                Assert.AreEqual(1f, TracerStyle.HaloFlash(look, .4f), "Reduce Flashes: hale α'sı kısılmaz (nötr WP-101 dahil, görünüm " + look + ")");
                Assert.AreEqual(1f, TracerStyle.HaloFlash(look, 1f));
            }
            Assert.AreEqual(1f, TracerStyle.HaloHdr(1f, 1f), "FlashScale 1: WP-101 HDR'ı birebir");
            Assert.AreEqual(TreeVfxLadder.HaloHdrV, TracerStyle.HaloHdr(TreeVfxLadder.HaloHdrV, 1f), 1e-6f, "S1: V ×1,5");
            Assert.AreEqual(.4f, TracerStyle.HaloHdr(1f, .4f), 1e-6f, "Reduce Flashes: HDR × FlashScale");
            Assert.AreEqual(.4f, TracerStyle.HaloHdr(TreeVfxLadder.HaloHdrV, .4f), 1e-6f, "Reduce Flashes: V'nin bloom artışı önce söner");
            Assert.AreEqual(TracerStyle.HaloHdrFlashFloor, TracerStyle.HaloHdr(1f, 0f), 1e-6f, "taban: hale koyu peçeye dönmez");
            Color rf = TracerRibbonRenderer.TdHaloColor * (TracerRibbonRenderer.TdHaloHdr * TracerStyle.HaloHdr(1f, .4f));
            Assert.Less(Mathf.Max(rf.g, rf.b), 1f, "Reduce Flashes: nötr hale merkezi beyaza kırpılmaz (tepe düşer)");
            Assert.Less(TracerRibbonRenderer.TdHaloHdr * TracerStyle.HaloHdr(TreeVfxLadder.HaloHdrV, .4f), TreeVfxLadder.BloomThreshold, "Reduce Flashes: hale bloom eşiğinin altında");
        }

        /// <summary>AD-B4 M5: an element (and the K3 violet) halo covers the ground it veils — the shader's premultiplied α grows outside
        /// the core while the colour it adds stays; the neutral WP-101 halo is untouched; every class's core lies inside |v| ≤ 0.25 of
        /// its halo (the halo is 4 × the core and the tier only adds), where the cover ramp (0.15–0.35) has barely begun.</summary>
        [Test]
        public void M5_ElementHalesiZeminiOrter_NotrWp101Degismez()
        {
            Assert.AreEqual(0f, TracerStyle.HaloCover(TracerStyle.NeutralLook), "nötr WP-101 halesi (AD kabulü)");
            foreach (int look in new[] { TracerStyle.FireLook, TracerStyle.IceLook, TracerStyle.ShockLook, TracerStyle.VenomLook, TracerStyle.MarkedLook })
                Assert.AreEqual(TracerStyle.ElementHaloCover, TracerStyle.HaloCover(look), "görünüm " + look);
            Assert.AreEqual(0f, TracerStyle.HaloCover(TracerStyle.LookCount), "geçersiz görünüm örtmez");
            Assert.GreaterOrEqual(TracerStyle.ElementHaloCover, 2f, "açık taşta elektrik renk payı ≥ .60 (AD kare modeli: ≥ 2)");
            foreach (VfxWeaponClass c in new[] { VfxWeaponClass.Smg, VfxWeaponClass.AssaultRifle, VfxWeaponClass.Lmg, VfxWeaponClass.Sniper, VfxWeaponClass.Shotgun })
            {
                float core = TracerRibbonRenderer.TdCoreWidth(c);
                Assert.LessOrEqual(core / (core * TracerRibbonRenderer.TdHaloScale), .25f + 1e-6f, c + ": çekirdek halenin |v| ≤ 0,25'inde");
            }
        }

        [Test]
        public void Birlesme_S02Kurali_KokIki_En125()
        {
            Assert.AreEqual(1f, TracerStyle.MergeFactor(0));
            Assert.AreEqual(TreeHooks.MergedSizeMax, TracerStyle.MergeFactor(1), 1e-5f, "√2 > 1.25: S-02 tavanı");
            Assert.AreEqual(TreeHooks.MergedSizeMax, TracerStyle.MergeFactor(9), 1e-5f);
            Assert.AreEqual(32, TracerStyle.VolleyRecordsMax, "ek mermiler 48 kaydın en çok 32'si");
            Assert.Less(TracerStyle.VolleyRecordsMax, TracerRibbonRenderer.TdCapacity, "ana mermiye yer kalır");
        }

        // ------------------------------------------------------------------ tracer style per run

        [Test]
        public void IzStili_Notr_Boy1_Wp101Birebir_SizeScaleTavanAcar()
        {
            TreeRuntime.SetTestStats(TreeStats.Neutral);
            Assert.AreEqual(1f, TracerStyle.CoreScale, "AD-B4 §1: çekirdek kademe kanalı değil");
            Assert.AreEqual(0f, TracerStyle.HaloPx);
            Assert.AreEqual(1f, TracerStyle.TrailScale);
            Assert.AreEqual(1f, TracerStyle.HaloHdrBoost);
            Assert.AreEqual(TracerStyle.NeutralLook, TracerStyle.Look);
            Assert.IsFalse(TracerStyle.Styled);
            float core = .11f, halo = .44f, cap = -1f, step = -1f, trail = -1f, hdr = -1f;
            int look = -1;
            TracerStyle.ApplyTd(ref core, ref halo, ref look, ref cap, ref step, ref trail, ref hdr, 1f, true);
            Assert.AreEqual(.11f, core);
            Assert.AreEqual(.44f, halo);
            Assert.AreEqual(TracerStyle.NeutralLook, look);
            Assert.AreEqual(0f, cap, "nötrde tavan yok: Normal birebir");
            Assert.AreEqual(0f, step, "nötrde hale adımı yok");
            Assert.AreEqual(1f, trail, "nötrde WP-101 iz boyu");
            Assert.AreEqual(1f, hdr);
            Assert.AreEqual(TracerRibbonRenderer.TdCoreHead, TracerStyle.CoreHead(look, TracerRibbonRenderer.TdCoreHead));
            Assert.AreEqual(TracerRibbonRenderer.TdHaloColor, TracerStyle.Halo(look, TracerRibbonRenderer.TdHaloColor));
            Assert.AreEqual(TracerRibbonRenderer.TdBodyColor, TracerStyle.Body(look, TracerRibbonRenderer.TdBodyColor));
            Assert.AreEqual(TracerRibbonRenderer.TdPelletColor, TracerStyle.Pellet(look, TracerRibbonRenderer.TdPelletColor));
            Assert.AreEqual(0f, TracerStyle.HaloCover(look), "nötr WP-101 halesi zemini örtmez");

            // S-02 B2: Big Bore / a merged shot (SizeScale 1.25) thickens a neutral round (core and halo, metres) and opens the core
            // ceiling; size is no tier: no halo step, the WP-101 trail
            core = .11f; halo = .44f;
            TracerStyle.ApplyTd(ref core, ref halo, ref look, ref cap, ref step, ref trail, ref hdr, 1.25f, true);
            Assert.AreEqual(.11f * 1.25f, core, 1e-6f);
            Assert.AreEqual(.44f * 1.25f, halo, 1e-6f);
            Assert.AreEqual(TracerStyle.CoreQuadCapPx, cap);
            Assert.AreEqual(0f, step, "boyut kademe değildir");
            Assert.AreEqual(1f, trail);
            float body = TracerRibbonRenderer.TdBodyWidth, none = 0f;
            TracerStyle.ApplyTd(ref body, ref none, ref look, ref cap, ref step, ref trail, ref hdr, 1.25f, false);
            Assert.AreEqual(TracerRibbonRenderer.TdBodyWidth * 1.25f, body, 1e-6f, "gövde izi yalnız mermi boyuyla");
            Assert.AreEqual(0f, none, "gövde izinin halesi yok");
            TracerStyle.ApplyTd(ref body, ref none, ref look, ref cap, ref step, ref trail, ref hdr, 0f, false);
            Assert.AreEqual(TracerRibbonRenderer.TdBodyWidth * 1.25f, body, 1e-6f, "SizeScale 0 = 1 (EffectiveSizeScale)");
        }

        [Test]
        public void IzStili_AtisVAtes_CekirdekWp101_HaleArti20_BoyX145_BirMermiBirRenk()
        {
            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Shot, 5);
            s.SetBranchTier(TreeBranch.Element, 1);
            s.Elements = TreeElement.Fire;
            s.DominantElement = TreeElement.Fire;
            TreeRuntime.SetTestStats(s);
            Assert.AreEqual(1f, TracerStyle.CoreScale, "AD-B4 §1: çekirdek kademe kanalı değil");
            Assert.AreEqual(20f, TracerStyle.HaloPx, 1e-5f, "hale: max(Atış, Element) = V → WP-101 + 20 px");
            Assert.AreEqual(1.45f, TracerStyle.TrailScale, 1e-5f, "iz boyu: Atış V ×1,45");
            Assert.AreEqual(TreeVfxLadder.HaloHdrV, TracerStyle.HaloHdrBoost, 1e-5f, "S1: V'te hale HDR ×1,5");
            Assert.IsTrue(TracerStyle.Styled);
            Assert.AreEqual(TracerStyle.FireLook, TracerStyle.Look);
            float core = .11f, halo = .44f, cap = 0f, step = 0f, trail = 0f, hdr = 0f;
            float body = .09f, bodyHalo = 0f, bodyCap = 0f, bodyStep = 0f, bodyTrail = 0f, bodyHdr = 0f;
            int look = 0, bodyLook = 0;
            TracerStyle.ApplyTd(ref core, ref halo, ref look, ref cap, ref step, ref trail, ref hdr, 1.2f, true);
            TracerStyle.ApplyTd(ref body, ref bodyHalo, ref bodyLook, ref bodyCap, ref bodyStep, ref bodyTrail, ref bodyHdr, 1.2f, false);
            Assert.AreEqual(.11f * 1.2f, core, 1e-5f, "× SizeScale yalnız: kademe çekirdeği büyütmez");
            Assert.AreEqual(.44f * 1.2f, halo, 1e-5f, "metre halesi × SizeScale; kademe px olarak eklenir");
            Assert.AreEqual(20f, step);
            Assert.AreEqual(1.45f, trail, 1e-5f);
            Assert.AreEqual(TreeVfxLadder.HaloHdrV, hdr, 1e-5f);
            Assert.AreEqual(TracerStyle.CoreQuadCapPx, cap);
            Assert.AreEqual(.09f * 1.2f, body, 1e-5f);
            Assert.AreEqual(0f, bodyStep, "gövde izi kademe almaz");
            Assert.AreEqual(1f, bodyTrail);
            Assert.AreEqual(1f, bodyHdr);
            Assert.AreEqual(TracerStyle.FireLook, bodyLook, "bir mermi bir renk (K1)");
            Assert.AreEqual(TracerStyle.ElementHaloCover, TracerStyle.HaloCover(look), "M5: element halesi zemini örter");

            Color head = TracerStyle.CoreHead(look, TracerRibbonRenderer.TdCoreHead);
            Color want = TreeVfxLadder.Linear(TreeVfxLadder.FireCore) * TracerStyle.CoreHdr;
            Assert.AreEqual(want.r, head.r, 1e-4f);
            Assert.AreEqual(want.b, head.b, 1e-4f);
            Color tail = TracerStyle.CoreTail(look, TracerRibbonRenderer.TdCoreTail);
            Assert.AreEqual(TreeVfxLadder.Linear(TreeVfxLadder.FireGlow).g * TracerStyle.TailHdr, tail.g, 1e-4f, "kuyruk element glow'una soğur");
            Color haloColour = TracerStyle.Halo(look, TracerRibbonRenderer.TdHaloColor);
            Assert.AreEqual(TreeVfxLadder.Linear(TreeVfxLadder.FireGlow).g, haloColour.g, 1e-4f);
            Color bodyColour = TracerStyle.Body(look, TracerRibbonRenderer.TdBodyColor);
            float neutralGain = Mathf.Max(TracerRibbonRenderer.TdBodyColor.r, Mathf.Max(TracerRibbonRenderer.TdBodyColor.g, TracerRibbonRenderer.TdBodyColor.b));
            Assert.AreEqual(neutralGain, Mathf.Max(bodyColour.r, Mathf.Max(bodyColour.g, bodyColour.b)), 1e-3f, "gövde parlaklığı WP-101'inki");
        }

        [TestCase(0, 1, 0f, 1f)]
        [TestCase(2, 0, 5f, 1f)]
        [TestCase(0, 4, 15f, 1f)]
        [TestCase(3, 4, 15f, 1.1f)]
        [TestCase(5, 2, 20f, 1.45f)]
        public void IzStili_Kademe_HaleMaxAtisElement_BoyAtistan_CekirdekSabit(int shot, int element, float haloPx, float trail)
        {
            TreeStats s = TreeStats.Neutral;
            s.SetBranchTier(TreeBranch.Shot, shot);
            if (element > 0)
            {
                s.SetBranchTier(TreeBranch.Element, element);
                s.Elements = TreeElement.Ice;
                s.DominantElement = TreeElement.Ice;
            }

            TreeRuntime.SetTestStats(s);
            Assert.AreEqual(1f, TracerStyle.CoreScale, "çekirdek kademe kanalı değil (AD-B4 §1: gerçekçilik çapası)");
            Assert.AreEqual(haloPx, TracerStyle.HaloPx, 1e-5f, "hale = max(Atış, Element)");
            Assert.AreEqual(trail, TracerStyle.TrailScale, 1e-5f, "iz boyu = yalnız Atış (biçim sahibi)");
            Assert.AreEqual(element > 0 ? TracerStyle.IceLook : TracerStyle.NeutralLook, TracerStyle.Look);
        }

        [Test]
        public void IzStili_Onizleme_BaskaYapiyiDogurur_SonraKosuyaDoner()
        {
            TreeRuntime.SetTestStats(TreeStats.Neutral);
            TreeStats after = TreeStats.Neutral;
            after.SetBranchTier(TreeBranch.Shot, 3);
            TracerStyle.SetPreview(in after);
            Assert.IsTrue(TracerStyle.PreviewActive);
            Assert.AreEqual(1.1f, TracerStyle.TrailScale, 1e-5f, "önizleme: sonra yarısı (Atış III iz ×1,1)");
            Assert.AreEqual(10f, TracerStyle.HaloPx, 1e-5f, "önizleme: sonra yarısı (hale +10 px)");
            TracerStyle.ClearPreview();
            Assert.IsFalse(TracerStyle.PreviewActive);
            Assert.AreEqual(1f, TracerStyle.TrailScale, "önizleme kapanınca koşunun stili");
            Assert.AreEqual(0f, TracerStyle.HaloPx);
        }

        // ------------------------------------------------------------------ K1 dominant element (S-04 B1b)

        [Test]
        public void K1_BaskinElement_S04Alani_SahipOlunan_SabitSiraYok()
        {
            TreeStats s = TreeStats.Neutral;
            Assert.AreEqual(TreeElement.None, TreeVfxLadder.DominantElement(in s));
            s.Elements = TreeElement.Fire | TreeElement.Ice;
            Assert.AreEqual(TreeElement.None, TreeVfxLadder.DominantElement(in s), "S-04 alanı boşken sabit sıra yok: nötr iz");
            s.DominantElement = TreeElement.Ice;
            Assert.AreEqual(TreeElement.Ice, TreeVfxLadder.DominantElement(in s), "S-04 TreeStats.DominantElement boyar");
            s.DominantElement = TreeElement.Fire | TreeElement.Ice;
            Assert.AreEqual(TreeElement.None, TreeVfxLadder.DominantElement(in s), "tek element biti değilse karışım yok");
            s.DominantElement = TreeElement.Venom;
            Assert.AreEqual(TreeElement.None, TreeVfxLadder.DominantElement(in s), "sahip olunmayan element boyamaz (S-06 E4 kuralı)");

            s.Elements |= TreeElement.Venom;
            s.SetBranchTier(TreeBranch.Element, 2);
            TreeRuntime.SetTestStats(s);
            Assert.AreEqual(TracerStyle.VenomLook, TracerStyle.Look);
            Assert.AreEqual(TreeElement.Venom, TracerStyle.DominantElement);
            Assert.AreEqual(TreeVfxLadder.HaloStepPx(2), TracerStyle.HaloPx, 1e-5f, "hale: max(Atış, Element) kademesi");
        }

        [Test]
        public void K1_Cozucuyle_EsitKademede_SonAlinanBoyar()
        {
            int fire = TreeCatalogV2.IndexOf("el_fire"), ice = TreeCatalogV2.IndexOf("el_ice"), ice2 = TreeCatalogV2.IndexOf("el_ice2");
            Assert.That(fire >= 0 && ice >= 0 && ice2 >= 0, "run-tree-v2.json: el_fire / el_ice / el_ice2");
            int n = TreeCatalogV2.Document.nodes.Length;
            var weapon = new TreeWeaponProfile { BaseRpm = 600f, BaseDamage = 10f, Pellets = 1, Magazine = 30 };

            var owned = new bool[n];
            var order = new int[n];
            owned[fire] = owned[ice] = true;
            order[ice] = 1;
            order[fire] = 2;
            TreeStats a = TreeResolver.Resolve(new TreeResolver.Input { Owned = owned, Order = order, Weapon = weapon });
            TreeRuntime.SetTestStats(a);
            Assert.AreEqual(TracerStyle.FireLook, TracerStyle.Look, "I = I: son alınan ateş boyar");

            order[fire] = 1;
            order[ice] = 2;
            TreeStats b = TreeResolver.Resolve(new TreeResolver.Input { Owned = owned, Order = order, Weapon = weapon });
            TreeRuntime.SetTestStats(b);
            Assert.AreEqual(TracerStyle.IceLook, TracerStyle.Look, "I = I: son alınan buz boyar");

            owned[ice2] = true;
            order[ice2] = 3;
            order[fire] = 4;
            TreeStats c = TreeResolver.Resolve(new TreeResolver.Input { Owned = owned, Order = order, Weapon = weapon });
            TreeRuntime.SetTestStats(c);
            Assert.AreEqual(TracerStyle.IceLook, TracerStyle.Look, "buz II > ateş I: yüksek kademe boyar, sonra alınsa da");
        }

        [Test]
        public void ElementSecimi_TekBit_BaskinIcerirse_YoksaIlkBit()
        {
            Assert.AreEqual(TreeElement.Shock, TreeVfxLadder.PickElement(TreeElement.Shock, TreeElement.Fire), "tek bit olduğu gibi");
            Assert.AreEqual(TreeElement.Venom, TreeVfxLadder.PickElement(TreeElement.Fire | TreeElement.Venom, TreeElement.Venom), "nova: baskın element");
            Assert.AreEqual(TreeElement.Fire, TreeVfxLadder.PickElement(TreeElement.Fire | TreeElement.Venom, TreeElement.Ice), "baskın yoksa ilk bit");
            Assert.AreEqual(TreeElement.None, TreeVfxLadder.PickElement(TreeElement.None, TreeElement.Ice), "element anı değilse None");
        }

        // ------------------------------------------------------------------ palette (A1 / A2 / A4 / A17)

        [Test]
        public void Renk_ElementIzleriAyrisir_A1_A2()
        {
            Color[] glows = { TreeVfxLadder.FireGlow, TreeVfxLadder.IceGlow, TreeVfxLadder.ShockGlow, TreeVfxLadder.VenomGlow };
            for (int i = 0; i < glows.Length; i++)
            {
                for (int j = i + 1; j < glows.Length; j++) Assert.GreaterOrEqual(DeltaE76(glows[i], glows[j]), 25f, "A1 " + i + "↔" + j);
                Assert.GreaterOrEqual(DeltaE76(glows[i], TreeVfxLadder.OrbGold), 35f, "A2 " + i + "↔orb");
            }

            Assert.AreEqual("FF5A1F", Hex(TreeVfxLadder.FireGlow));
            Assert.AreEqual("8CE6FF", Hex(TreeVfxLadder.IceGlow));
            Assert.AreEqual("ADC6FF", Hex(TreeVfxLadder.ShockGlow));
            Assert.AreEqual("A8E04A", Hex(TreeVfxLadder.VenomGlow));
            Assert.AreEqual("FFE2B0", Hex(TreeVfxLadder.FireCore));
            Assert.AreEqual("F0FCFF", Hex(TreeVfxLadder.IceCore));
            Assert.AreEqual("E8FFB0", Hex(TreeVfxLadder.VenomCore));
            Assert.AreEqual("9B5CFF", Hex(TreeVfxLadder.ChaosEdge));
            Assert.AreEqual("12100F", Hex(TreeVfxLadder.ChaosInk));
        }

        [Test]
        public void Aura_UcKademeAyrisir_A4_AltinRezerviDisinda()
        {
            Assert.AreEqual("5C8DFF", Hex(TreeVfxLadder.AuraTint(1)));
            Assert.AreEqual("FFE8B8", Hex(TreeVfxLadder.AuraTint(2)));
            Assert.AreEqual("E8302A", Hex(TreeVfxLadder.AuraTint(3)));
            for (int a = 1; a <= 3; a++)
            {
                for (int b = a + 1; b <= 3; b++) Assert.GreaterOrEqual(DeltaE76(TreeVfxLadder.AuraTint(a), TreeVfxLadder.AuraTint(b)), 30f, "A4 " + a + "↔" + b);
                Color.RGBToHSV(TreeVfxLadder.AuraTint(a), out float h, out float sat, out _);
                Assert.IsFalse(h * 360f >= 35f && h * 360f <= 65f && sat > .30f, "A17: aura " + a + " altın rezervinde");
            }

            Assert.AreEqual(1f, TreeVfxLadder.AuraRadius(1));
            Assert.AreEqual(1.3f, TreeVfxLadder.AuraRadius(2), 1e-5f);
            Assert.AreEqual(1.6f, TreeVfxLadder.AuraRadius(3), 1e-5f);
            Assert.AreEqual(.45f, TreeVfxLadder.AuraAlpha(1), 1e-5f);
            Assert.AreEqual(.55f, TreeVfxLadder.AuraAlpha(2), 1e-5f);
            Assert.AreEqual(.60f, TreeVfxLadder.AuraAlpha(3), 1e-5f);
            Assert.AreEqual(0f, TreeVfxLadder.AuraRadius(0));
            Assert.AreEqual(Color.clear, TreeVfxLadder.AuraTint(0));
            Assert.AreEqual(6, TreeVfxLadder.AuraSparksPerSecond(1));
            Assert.AreEqual(20, TreeVfxLadder.AuraSparksPerSecond(3));
            Assert.AreEqual(24, TreeVfxLadder.AuraSparksLive(3));
            // A4: ring radii differ ≥ 12 px at the TD camera (1080p)
            Assert.GreaterOrEqual((TreeVfxLadder.AuraRadius(2) - TreeVfxLadder.AuraRadius(1)) * TreeVfxLadder.TdPixelsPerMetre, 12f);
            Assert.GreaterOrEqual((TreeVfxLadder.AuraRadius(3) - TreeVfxLadder.AuraRadius(2)) * TreeVfxLadder.TdPixelsPerMetre, 12f);
        }

        [TestCase(TreeSynergy.Barrage, "C4362B", "7F8C99")]
        [TestCase(TreeSynergy.FrozenGravity, "3E9AAE", "5E3A8C")]
        [TestCase(TreeSynergy.ThunderCascade, "5B7FD1", "5E3A8C")]
        [TestCase(TreeSynergy.PinballStorm, "C4362B", "C4362B")]
        [TestCase(TreeSynergy.UnbrokenLine, "46567C", "7F8C99")]
        [TestCase(TreeSynergy.WildfireRounds, "E0702A", "C4362B")]
        [TestCase(TreeSynergy.ToxicRail, "6E9A2E", "C4362B")]
        [TestCase(TreeSynergy.CriticalMeltdown, "5E3A8C", "5E3A8C")]
        [TestCase(TreeSynergy.JudgmentLine, "C4362B", "C4362B")]
        [TestCase(TreeSynergy.GoldenGun, "FFC74D", "5E3A8C")]
        public void Sinerji_EnsoRenkleri_SynergyMap(TreeSynergy synergy, string left, string right)
        {
            TreeVfxLadder.SynergyTints(synergy, out Color l, out Color r);
            Assert.AreEqual(left, Hex(l));
            Assert.AreEqual(right, Hex(r));
            Assert.AreEqual(synergy, TreeVfxLadder.FirstSynergy(synergy));
        }

        // ------------------------------------------------------------------ B9 wedge helper (B4-2)

        [TestCase(40f, 30, 20f, 15f)]
        [TestCase(12f, 15, 6f, 7.5f)]
        [TestCase(60f, 60, 30f, 30f)]
        [TestCase(30f, 30, 15f, 15f)]
        public void FanKama_KaynakVeYukseklik(float arc, int source, float halfArc, float halfSource)
        {
            Assert.AreEqual(source, TracerStyle.FanWedgeSource(arc, out float scale));
            Assert.AreEqual(Mathf.Tan(halfArc * Mathf.Deg2Rad) / Mathf.Tan(halfSource * Mathf.Deg2Rad), scale, 1e-4f, "GD sh_fan 40°: 30° kaynak × tan20/tan15");
            Assert.AreEqual(5, TracerStyle.SeparateTracersMax, "K4: tetik başına ≤ 5 ayrı iz");
        }

        // ------------------------------------------------------------------ marked round, blast source, tiers, clock

        [Test]
        public void K3_IsaretliMermiBiti_MorKenar_SumiCekirdek()
        {
            Assert.AreEqual(16, RunTreeHooks.TreeRoundMarkedChaos, "S-04 12.10 §2: işaretli mermi biti 16");
            Assert.AreEqual(RunTreeHooks.TreeRoundMarkedChaos, TracerStyle.MarkedRoundFlag, "iz S-04'ün sabitini okur");
            Assert.IsTrue(TracerStyle.IsMarkedRound(TracerStyle.MarkedRoundFlag));
            const int capstoneTags = RunTreeHooks.TreeRoundIgnoresArmor | RunTreeHooks.TreeRoundFirstBodyIgnoresArmor
                | RunTreeHooks.TreeRoundUnlimitedPierce | RunTreeHooks.TreeRoundThirdEye;   // the retiring capstone round tags
            Assert.IsTrue(TracerStyle.IsMarkedRound(TracerStyle.MarkedRoundFlag | capstoneTags));
            Assert.IsFalse(TracerStyle.IsMarkedRound(capstoneTags), "eski kapstone bitleriyle çakışmaz");
            int look = 0;
            float cap = 0f, step = 0f, hdr = 0f;
            TracerStyle.MarkTd(ref look, ref cap, ref step, ref hdr);
            Assert.AreEqual(TracerStyle.MarkedLook, look);
            Assert.AreEqual(TracerStyle.CoreQuadCapPx, cap);
            Assert.AreEqual(TreeVfxLadder.HaloStepPx(TreeVfxLadder.TierMax), step, "M4: kademeden bağımsız V halesi (WP-101 + 20 px)");
            Assert.AreEqual(1f, hdr, "işaretin kendi HDR'ı (V'nin bloom artışı kademenin)");
            Color edge = TreeVfxLadder.Linear(TreeVfxLadder.ChaosEdge);
            Assert.AreEqual(edge.b, TracerStyle.Halo(look, Color.white).b, 1e-4f, "kenar #9B5CFF");
            Assert.AreEqual(edge.b * TracerStyle.MarkedEdgeHdr, TracerStyle.CoreHead(look, Color.white).b, 1e-4f, "eklenen çekirdek mor (Performance'ta da)");
            Color ink = TracerStyle.Ink;
            Assert.Less(Mathf.Max(ink.r, Mathf.Max(ink.g, ink.b)), .01f, "sumi çekirdek #12100F");
            Assert.AreEqual(.85f, TracerStyle.InkWidthShare, 1e-5f, "M4: sumi çekirdek 0,85 × çekirdek (0,55 kıl çizgisiydi)");
            Assert.Less(TracerStyle.InkWidthShare, 1f, "mor kenar sumi çekirdeğin iki yanında");
            Assert.GreaterOrEqual(TracerStyle.InkMinPx, 3f, "A5");
            Assert.GreaterOrEqual(TracerRibbonRenderer.TdCoreWidth(VfxWeaponClass.AssaultRifle) * TracerStyle.InkWidthShare * TreeVfxLadder.TdPixelsPerMetre, 4f,
                "M4: AR koyu çekirdeği ≥ 4 px (1080p)");
            Assert.AreEqual(TracerStyle.ElementHaloCover, TracerStyle.HaloCover(look), "işaretli hale de zemini örter (açık taşta okunur)");
        }

        [Test]
        public void AgacPatlamasi_KaynakS02B2_Iki()
        {
            Assert.AreEqual(2, (int)ExplosionSource.Tree, "S-02 B2: ExplosionSource.Tree = 2 (ExplosionDirector çizmez, TreeEvents.Proc sunar)");
        }

        [Test]
        public void KademeArtisi_DalBazinda()
        {
            TreeStats a = TreeStats.Neutral, b = TreeStats.Neutral;
            a.SetBranchTier(TreeBranch.Shot, 2);
            b.SetBranchTier(TreeBranch.Shot, 2);
            Assert.IsFalse(TreeVfxBinder.TierIncreased(a.BranchTiers, b.BranchTiers));
            b.SetBranchTier(TreeBranch.Economy, 1);
            Assert.IsTrue(TreeVfxBinder.TierIncreased(a.BranchTiers, b.BranchTiers), "yeni dal = kademe atlama");
            Assert.IsFalse(TreeVfxBinder.TierIncreased(b.BranchTiers, a.BranchTiers), "düşüş tören değil");
        }

        [Test]
        public void VfxClock_OnizlemeKapali_OyunSaati_OnizlemeGeriGitmez()
        {
            Assert.IsFalse(VfxClock.PreviewActive);
            Assert.AreEqual(Time.deltaTime, VfxClock.Delta);
            Assert.AreEqual(Time.deltaTime, VfxClock.DeltaFor(false));
            Assert.AreEqual(Time.time + VfxClock.Offset, VfxClock.Now, 1e-5f);
            // a 30 s preview at timeScale 0 started at t 100 (unscaled 200): its clock runs 100 → 130, the offset keeps the 30 s
            Assert.AreEqual(115f, VfxClock.PreviewNow(100f, 0f, 200f, 215f), 1e-4f);
            Assert.AreEqual(30f, VfxClock.OffsetAfter(100f, 0f, 200f, 100f, 230f), 1e-4f);
            // a preview at timeScale > 1 never pulls Now back
            Assert.AreEqual(5f, VfxClock.OffsetAfter(100f, 5f, 200f, 120f, 210f), 1e-4f);
        }

        // ------------------------------------------------------------------ helpers

        private static string Hex(Color c)
        {
            Color32 k = c;
            return k.r.ToString("X2") + k.g.ToString("X2") + k.b.ToString("X2");
        }

        /// <summary>CIE76 on sRGB → linear → XYZ (D65) → Lab, as the AD measured §1.2.</summary>
        private static float DeltaE76(Color a, Color b)
        {
            Vector3 la = Lab(a), lb = Lab(b);
            return (la - lb).magnitude;
        }

        private static Vector3 Lab(Color srgb)
        {
            float r = Mathf.GammaToLinearSpace(srgb.r), g = Mathf.GammaToLinearSpace(srgb.g), b = Mathf.GammaToLinearSpace(srgb.b);
            float x = (.4124f * r + .3576f * g + .1805f * b) / .95047f;
            float y = .2126f * r + .7152f * g + .0722f * b;
            float z = (.0193f * r + .1192f * g + .9505f * b) / 1.08883f;
            float fx = F(x), fy = F(y), fz = F(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float F(float t) => t > .008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
    }
}
