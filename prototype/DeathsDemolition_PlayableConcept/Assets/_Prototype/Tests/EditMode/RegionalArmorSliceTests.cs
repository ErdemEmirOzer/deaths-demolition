using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-006 · M2 bölgesel zırh &amp; patlama dilimi — saf sözleşme testleri (sahnesiz).
    /// REQ-080…090 · FLOW-007/008 · REQ-089 (patlamada kritik yok) · olay meta/transaction
    /// sözleşmesi · bölge çözümü fallback'i · kaynak taraması (kare başına arama yok).
    ///
    /// Ana prompt test matrisi numaraları köşeli parantezle: [#7] bir bölgenin zırhı
    /// başka bölgeyi korumaz, [#8] taşma cana geçmez, [#10] aynı kırılma iki kez üretilmez,
    /// [#12]/[#13] tek/iki parça, [#15] biri kırıkken, [#16] aynı patlama bütün parçaları,
    /// [#18] patlama taşması, [#20] enjekte oranlar deterministik, [#22] ölü düşman.
    ///
    /// SAYILAR PROTOTYPE-ONLY: dayanıklılık 22/38/28, açık çarpan 2/1/0.8, paylar
    /// 25/45/30 — sevkiyat config'inden okunur ve sabit tazelik nöbetçisiyle sınanır;
    /// hiçbiri ürün kararı değildir. (`RegionalDamageTests` D01–D06, E11–E17, L21/L22'nin
    /// saf yarısını zaten kapsar; burası boşlukları kapatır.)
    /// </summary>
    [TestFixture]
    [Category("WP006")]
    public sealed class RegionalArmorSliceTests
    {
        private ArmorConfigSection _armor;
        private ExplosionConfigSection _explosion;

        [SetUp]
        public void SetUp()
        {
            _armor = new ArmorConfigSection();
            _explosion = new ExplosionConfigSection();
        }

        private static RegionalDamageBody Beden(float maxHealth)
        {
            var body = new RegionalDamageBody();
            body.Reset(maxHealth);
            return body;
        }

        private static DamageHit Vurus(float amount, BodyRegion region, DamageSourceKind source = DamageSourceKind.Bullet)
        {
            return new DamageHit
            {
                Amount = amount,
                Region = region,
                Point = Vector3.zero,
                Direction = Vector3.forward,
                ImpactForce = 1f,
                Source = source
            };
        }

        // ======================================================== doğrudan isabet [#7] [#8] [#10]

        /// <summary>[#7] REQ-080: bir parça yalnız kendi bölgesini korur.</summary>
        [TestCase(ArmorSlot.Helmet, BodyRegion.UpperBody, BodyRegion.LowerBody)]
        [TestCase(ArmorSlot.Body, BodyRegion.Head, BodyRegion.LowerBody)]
        [TestCase(ArmorSlot.Leg, BodyRegion.Head, BodyRegion.UpperBody)]
        public void Parca_YalnizKendiBolgesiniKorur(ArmorSlot takili, BodyRegion acik1, BodyRegion acik2)
        {
            RegionalDamageBody body = Beden(1000f);
            body.Armor.Equip(takili, _armor.DurabilityFor(takili));

            DamageResult r1 = body.ApplyDirectHit(Vurus(10f, acik1), _armor);
            Assert.IsFalse(r1.WasArmored, takili + " " + acik1 + " bölgesini korudu.");
            Assert.Greater(r1.HealthDamage, 0f);
            Assert.AreEqual(10f * _armor.ExposedMultiplierFor(DamageRegions.ToCore(acik1)), r1.HealthDamage, 0.0001f);

            DamageResult r2 = body.ApplyDirectHit(Vurus(10f, acik2), _armor);
            Assert.IsFalse(r2.WasArmored, takili + " " + acik2 + " bölgesini korudu.");
            Assert.Greater(r2.HealthDamage, 0f);

            // Korunan bölge hâlâ dokunulmamış.
            Assert.AreEqual(_armor.DurabilityFor(takili), body.Armor.Current(takili), 0.0001f);

            DamageResult r3 = body.ApplyDirectHit(Vurus(5f, DamageRegions.RegionFor(takili)), _armor);
            Assert.IsTrue(r3.WasArmored);
            Assert.AreEqual(0f, r3.HealthDamage);
            Assert.AreEqual(5f, r3.ArmorDamage, 0.0001f);
        }

        /// <summary>[#8] REQ-083: dayanıklılığı çok aşan tek vuruşta kalan hasar cana geçmez; parça kırılır.</summary>
        [TestCase(ArmorSlot.Helmet)]
        [TestCase(ArmorSlot.Body)]
        [TestCase(ArmorSlot.Leg)]
        public void Tasma_HerSlottaCanaGecmez(ArmorSlot slot)
        {
            RegionalDamageBody body = Beden(100f);
            float d = _armor.DurabilityFor(slot);
            body.Armor.Equip(slot, d);

            DamageResult r = body.ApplyDirectHit(Vurus(d * 50f, DamageRegions.RegionFor(slot)), _armor);
            Assert.IsTrue(r.WasArmored);
            Assert.IsTrue(r.ArmorBroke);
            Assert.AreEqual(d, r.ArmorDamage, 0.0001f, "Emilen dayanıklılıkla sınırlı olmalı.");
            Assert.AreEqual(0f, r.HealthDamage);
            Assert.AreEqual(100f, body.Health, 0.0001f);
            Assert.IsTrue(body.Armor.IsBroken(slot));
        }

        /// <summary>[#10] Kırılma idempotent: aynı slot ikinci çağrıda bir daha kırılmaz, bir daha emmez.</summary>
        [Test]
        public void Kirilma_Idempotent_IkinciCagriKirmaz()
        {
            var armor = new ArmorSet();
            armor.Equip(ArmorSlot.Helmet, 22f);

            Assert.IsTrue(armor.Absorb(ArmorSlot.Helmet, 100f, out float a1, out bool k1));
            Assert.AreEqual(22f, a1, 0.0001f);
            Assert.IsTrue(k1);
            Assert.AreEqual(0, armor.ProtectingCount);

            Assert.IsFalse(armor.Absorb(ArmorSlot.Helmet, 100f, out float a2, out bool k2), "Kırık parça ikinci çağrıda 'koruyor' dedi.");
            Assert.AreEqual(0f, a2);
            Assert.IsFalse(k2, "Aynı kırılma iki kez raporlandı.");
            Assert.AreEqual(0, armor.ProtectingCount, "ProtectingCount ikinci kırılmada eksiye/yanlışa gitti.");
            Assert.AreEqual(1, armor.EquippedCount, "Kırık parça hâlâ takılı sayılmalı (REQ-063 alt-kumaş).");

            RegionalDamageBody body = Beden(100f);
            body.Armor.Equip(ArmorSlot.Body, 38f);
            DamageResult ilk = body.ApplyDirectHit(Vurus(200f, BodyRegion.UpperBody), _armor);
            Assert.IsTrue(ilk.ArmorBroke);
            Assert.AreEqual(1, body.LastBreakCount);

            DamageResult ikinci = body.ApplyDirectHit(Vurus(10f, BodyRegion.UpperBody), _armor);
            Assert.IsFalse(ikinci.ArmorBroke, "Kırık gövde zırhı ikinci vuruşta tekrar 'kırıldı'.");
            Assert.AreEqual(0, body.LastBreakCount);
            Assert.IsFalse(ikinci.WasArmored);
            Assert.AreEqual(10f * _armor.ExposedUpperBodyMultiplier, ikinci.HealthDamage, 0.0001f, "REQ-084: kırıldıktan sonra bölge açık.");
        }

        // ================================================================ patlama [#12] [#13] [#15] [#16] [#18] [#20]

        private float Pay(BodyRegion region)
        {
            float sum = _explosion.HeadShare + _explosion.UpperBodyShare + _explosion.LowerBodyShare;
            switch (region)
            {
                case BodyRegion.Head: return _explosion.HeadShare / sum;
                case BodyRegion.LowerBody: return _explosion.LowerBodyShare / sum;
                default: return _explosion.UpperBodyShare / sum;
            }
        }

        /// <summary>[#12] Tek parça: korunan pay o zırha, iki açık pay cana.</summary>
        [TestCase(ArmorSlot.Helmet)]
        [TestCase(ArmorSlot.Body)]
        [TestCase(ArmorSlot.Leg)]
        public void Patlama_TekParca_KorunanPayZirhaAcikPaylarCana(ArmorSlot slot)
        {
            const float toplam = 100f;
            RegionalDamageBody body = Beden(10000f);
            body.Armor.Equip(slot, 10000f);   // kırılmasın diye büyük (PrototypeOnly dayanıklılık değil, test düzeneği)
            BodyRegion korunan = DamageRegions.RegionFor(slot);

            DamageResult r = body.ApplyExplosion(toplam, _armor, _explosion);

            float beklenenZirh = toplam * Pay(korunan);
            float beklenenCan = toplam - beklenenZirh;
            Assert.AreEqual(beklenenZirh, r.ArmorDamage, 0.001f, "Korunan pay zırha gitmedi.");
            Assert.AreEqual(beklenenCan, r.HealthDamage, 0.001f, "Açık payların toplamı cana gitmedi.");
            Assert.AreEqual(toplam, r.ArmorDamage + r.HealthDamage, 0.001f, "Bütçe korunmadı.");
            Assert.AreEqual(0f, body.LastHealthDamageIn(korunan), "Korunan bölge cana pay verdi.");
            Assert.IsFalse(r.ArmorBroke);
        }

        /// <summary>[#13] İki parça: iki pay zırhlara, açık pay cana.</summary>
        [TestCase(ArmorSlot.Helmet, ArmorSlot.Body, BodyRegion.LowerBody)]
        [TestCase(ArmorSlot.Helmet, ArmorSlot.Leg, BodyRegion.UpperBody)]
        [TestCase(ArmorSlot.Body, ArmorSlot.Leg, BodyRegion.Head)]
        public void Patlama_IkiParca_IkiPayZirhaAcikPayCana(ArmorSlot a, ArmorSlot b, BodyRegion acik)
        {
            const float toplam = 100f;
            RegionalDamageBody body = Beden(10000f);
            body.Armor.Equip(a, 10000f);
            body.Armor.Equip(b, 10000f);

            DamageResult r = body.ApplyExplosion(toplam, _armor, _explosion);

            float beklenenCan = toplam * Pay(acik);
            Assert.AreEqual(beklenenCan, r.HealthDamage, 0.001f);
            Assert.AreEqual(toplam - beklenenCan, r.ArmorDamage, 0.001f);
            Assert.AreEqual(toplam * Pay(DamageRegions.RegionFor(a)), body.LastArmorDamageIn(DamageRegions.RegionFor(a)), 0.001f);
            Assert.AreEqual(toplam * Pay(DamageRegions.RegionFor(b)), body.LastArmorDamageIn(DamageRegions.RegionFor(b)), 0.001f);
            Assert.AreEqual(beklenenCan, body.LastHealthDamageIn(acik), 0.001f);
        }

        /// <summary>[#15] [#16] Üç parça; ilk patlama üçünü de etkiler, cana 0; bir parça kırıldıktan sonraki patlamada o bölgenin payı cana.</summary>
        [Test]
        public void Patlama_UcParca_SonraBiriKirik_AcikPayCana()
        {
            RegionalDamageBody body = Beden(10000f);
            body.Armor.Equip(ArmorSlot.Helmet, _armor.DurabilityFor(ArmorSlot.Helmet));   // 22 (PrototypeOnly)
            body.Armor.Equip(ArmorSlot.Body, 10000f);
            body.Armor.Equip(ArmorSlot.Leg, 10000f);

            // [#16] aynı patlama üç parçaya da kendi payıyla vurur (kask kırılmasın: 40 × 0.25 = 10 < 22).
            DamageResult r1 = body.ApplyExplosion(40f, _armor, _explosion);
            Assert.AreEqual(0f, r1.HealthDamage, "[#14] üç sağlam parça varken cana hasar.");
            Assert.AreEqual(40f * Pay(BodyRegion.Head), body.LastArmorDamageIn(BodyRegion.Head), 0.001f);
            Assert.AreEqual(40f * Pay(BodyRegion.UpperBody), body.LastArmorDamageIn(BodyRegion.UpperBody), 0.001f);
            Assert.AreEqual(40f * Pay(BodyRegion.LowerBody), body.LastArmorDamageIn(BodyRegion.LowerBody), 0.001f);
            Assert.AreEqual(40f, r1.ArmorDamage, 0.001f);

            // Kaskı doğrudan vuruşla kır (REQ-084 yolu).
            body.ApplyDirectHit(Vurus(1000f, BodyRegion.Head), _armor);
            Assert.IsTrue(body.Armor.IsBroken(ArmorSlot.Helmet));

            // [#15] sonraki patlamada kafa payı cana ulaşır; diğer paylar hâlâ zırhta.
            DamageResult r2 = body.ApplyExplosion(100f, _armor, _explosion);
            Assert.AreEqual(100f * Pay(BodyRegion.Head), r2.HealthDamage, 0.001f, "Kırık bölgenin payı cana gitmedi.");
            Assert.AreEqual(100f * Pay(BodyRegion.Head), body.LastHealthDamageIn(BodyRegion.Head), 0.001f);
            Assert.AreEqual(100f - r2.HealthDamage, r2.ArmorDamage, 0.001f);
            Assert.IsFalse(r2.ArmorBroke, "Kırık kask tekrar kırıldı.");
        }

        /// <summary>[#18] K-11: maxHealth ≥ 1000, yalnız kask (22); 1000 patlama → can 750 (450+300), zırh 22, kafa payı artanı 228 atılır.</summary>
        [Test]
        public void Patlama_KirilmaTasmasi_AyniPatlamadaCanaGecmez()
        {
            RegionalDamageBody body = Beden(2000f);
            float kask = _armor.DurabilityFor(ArmorSlot.Helmet);
            body.Armor.Equip(ArmorSlot.Helmet, kask);

            DamageResult r = body.ApplyExplosion(1000f, _armor, _explosion);

            float kafaPayi = 1000f * Pay(BodyRegion.Head);
            float acikPaylar = 1000f - kafaPayi;
            Assert.IsTrue(r.ArmorBroke);
            Assert.AreEqual(kask, r.ArmorDamage, 0.001f, "Zırh yalnız kalan dayanıklılığı kadar emer.");
            Assert.AreEqual(acikPaylar, r.HealthDamage, 0.001f, "Kafa payının artanı (" + (kafaPayi - kask) + ") cana sızdı.");
            Assert.AreEqual(2000f - acikPaylar, body.Health, 0.001f);
            Assert.Greater(kafaPayi - kask, 0f, "Test düzeneği: kafa payı kaskı aşmalı ki artan olsun.");
        }

        /// <summary>[#20] Enjekte edilen oranlar: toplam korunur, sonuç deterministik (aynı girdi aynı çıktı).</summary>
        [TestCase(0.25f, 0.45f, 0.30f)]
        [TestCase(0.10f, 0.60f, 0.30f)]
        [TestCase(1f, 1f, 1f)]
        [TestCase(0.5f, 0f, 0.5f)]
        public void Patlama_EnjekteOranlar_ToplamKorunurDeterministik(float kafa, float ust, float alt)
        {
            var cfg = new ExplosionConfigSection { HeadShare = kafa, UpperBodyShare = ust, LowerBodyShare = alt };
            float sum = kafa + ust + alt;

            RegionalDamageBody a = Beden(10000f);
            RegionalDamageBody b = Beden(10000f);
            DamageResult ra = a.ApplyExplosion(200f, _armor, cfg);
            DamageResult rb = b.ApplyExplosion(200f, _armor, cfg);

            Assert.AreEqual(200f, ra.HealthDamage + ra.ArmorDamage, 0.001f, "Toplam korunmadı.");
            Assert.AreEqual(200f * kafa / sum, a.LastHealthDamageIn(BodyRegion.Head), 0.001f);
            Assert.AreEqual(200f * ust / sum, a.LastHealthDamageIn(BodyRegion.UpperBody), 0.001f);
            Assert.AreEqual(200f * alt / sum, a.LastHealthDamageIn(BodyRegion.LowerBody), 0.001f);
            Assert.AreEqual(ra.HealthDamage, rb.HealthDamage, 0f, "Aynı girdi farklı sonuç (deterministik değil).");
            Assert.AreEqual(a.LastHealthDamageIn(BodyRegion.Head), b.LastHealthDamageIn(BodyRegion.Head), 0f);
        }

        // ================================================== ölü düşman [#22] ve olay meta sözleşmesi

        /// <summary>[#22] Ölü beden ikinci ölüm/kırılma üretmez (saf model).</summary>
        [Test]
        public void OluBeden_IkinciOlumVeKirilmaYok()
        {
            RegionalDamageBody body = Beden(10f);
            body.Armor.Equip(ArmorSlot.Body, 38f);
            DamageResult olum = body.ApplyDirectHit(Vurus(100f, BodyRegion.Head), _armor);
            Assert.IsTrue(olum.Killed);

            DamageResult gec = body.ApplyDirectHit(Vurus(100f, BodyRegion.UpperBody), _armor);
            Assert.IsFalse(gec.Killed);
            Assert.IsFalse(gec.ArmorBroke);
            Assert.AreEqual(0f, gec.ArmorDamage, "Ölü bedende zırh hasarı işlendi.");
            Assert.IsTrue(DamageResults.IsEmpty(in gec), "Ölü bedenin sonucu 'boş' sayılmalı (dressing/sayaç yok).");
            Assert.AreEqual(38f, body.Armor.Current(ArmorSlot.Body), 0.0001f);

            DamageResult gecPatlama = body.ApplyExplosion(500f, _armor, _explosion);
            Assert.IsTrue(DamageResults.IsEmpty(in gecPatlama));
        }

        /// <summary>DamageResults.IsEmpty: canlı sonuçların hiçbiri boş değildir.</summary>
        [Test]
        public void IsEmpty_YalnizHicbirSeyOlmadiginda()
        {
            Assert.IsTrue(DamageResults.IsEmpty(default));
            Assert.IsFalse(DamageResults.IsEmpty(new DamageResult { HealthDamage = 0.1f }));
            Assert.IsFalse(DamageResults.IsEmpty(new DamageResult { ArmorDamage = 0.1f }));
            Assert.IsFalse(DamageResults.IsEmpty(new DamageResult { ArmorBroke = true }));
            Assert.IsFalse(DamageResults.IsEmpty(new DamageResult { Killed = true }));
        }

        /// <summary>Transaction kimlikleri monoton, 0 asla; iç içe Begin sayılır; End 0'a döndürür.</summary>
        [Test]
        public void DamageTransaction_MonotonVeIcIceSayilir()
        {
            int nestedBefore = DamageTransaction.NestedBegins;
            DamageTransaction.End();

            int a = DamageTransaction.Begin();
            Assert.Greater(a, 0);
            Assert.AreEqual(a, DamageTransaction.Current);
            DamageTransaction.End();
            Assert.AreEqual(0, DamageTransaction.Current);

            int b = DamageTransaction.Begin();
            Assert.Greater(b, a, "Transaction id monoton artmalı.");
            int c = DamageTransaction.Begin();   // iç içe: bug sayacı
            Assert.Greater(c, b);
            Assert.AreEqual(nestedBefore + 1, DamageTransaction.NestedBegins);
            DamageTransaction.End();
            Assert.AreEqual(0, DamageTransaction.Current);

            int e1 = DamageEventIds.Next();
            int e2 = DamageEventIds.Next();
            Assert.Greater(e1, 0);
            Assert.AreEqual(e1 + 1, e2);
        }

        /// <summary>Olay meta: before/after tutarlılığı ve bölge kırılımı (yan tip, frozen yapılar değişmeden).</summary>
        [Test]
        public void DamageEventMeta_BeforeAfterVeKirilim()
        {
            var meta = new DamageEventMeta(
                lifeId: 7, transactionId: 3, eventId: 11, kind: DamageKind.Explosion, slot: ArmorSlot.None,
                healthBefore: 100f, healthAfter: 70f,
                helmetBefore: 22f, helmetAfter: 0f, bodyBefore: 38f, bodyAfter: 38f, legBefore: 0f, legAfter: 0f,
                headHealthDamage: 0f, upperHealthDamage: 0f, lowerHealthDamage: 30f,
                headArmorDamage: 22f, upperArmorDamage: 0f, lowerArmorDamage: 0f);

            Assert.IsTrue(meta.IsPresent);
            Assert.AreEqual(22f, meta.ArmorBefore(ArmorSlot.Helmet));
            Assert.AreEqual(0f, meta.ArmorAfter(ArmorSlot.Helmet));
            Assert.AreEqual(38f, meta.ArmorAfter(ArmorSlot.Body));
            Assert.AreEqual(22f, meta.ArmorAbsorbed, 0.0001f);
            Assert.AreEqual(30f, meta.HealthRemoved, 0.0001f);
            Assert.AreEqual(30f, meta.HealthBefore - meta.HealthAfter, 0.0001f);
            Assert.AreEqual(30f, meta.HealthDamageIn(BodyRegion.LowerBody));
            Assert.AreEqual(22f, meta.ArmorDamageIn(BodyRegion.Head));
            Assert.IsFalse(default(DamageEventMeta).IsPresent, "Eski ctor'la yayınlanan olayın meta'sı 'yok' olmalı.");

            // Eski ctor'lar hâlâ derleniyor ve Meta default bırakıyor (geriye uyumluluk).
            var eski = new EnemyDamageInfo(null, EnemyTier.Standard, new DamageResult(), Vector3.zero, Vector3.forward,
                DamageSourceKind.Explosion, 1f, 1f, false);
            Assert.IsFalse(eski.Meta.IsPresent);
            Assert.AreEqual(0, eski.LifeId);
            Assert.AreEqual(DamageKind.Explosion, eski.Kind, "Meta yokken Kind Source'tan türetilir.");

            var yeni = new EnemyDamageInfo(null, EnemyTier.Standard, new DamageResult(), Vector3.zero, Vector3.forward,
                DamageSourceKind.Explosion, 1f, 1f, false, in meta);
            Assert.AreEqual(7, yeni.LifeId);
            Assert.AreEqual(DamageKind.Explosion, yeni.Kind);

            var kirik = new ArmorBreakInfo(null, EnemyTier.Standard, ArmorSlot.Helmet, BodyRegion.Head, Vector3.zero,
                Vector3.forward, DamageSourceKind.Bullet, 0, 0f, in meta);
            Assert.AreEqual(3, kirik.TransactionId);
            Assert.AreEqual(7, kirik.LifeId);
        }

        /// <summary>On olay sınıfı: sınıflandırıcı tablo testi (yeni statik olay eklenmeden türetilir).</summary>
        [Test]
        public void DamageEventClassifier_OnSinifTablosu()
        {
            DamageEventMeta Meta(DamageKind kind, float hh, float uh, float lh, float ha, float ua, float la)
            {
                return new DamageEventMeta(1, 1, DamageEventIds.Next(), kind, ArmorSlot.None,
                    100f, 100f - hh - uh - lh, 22f, 22f - ha, 38f, 38f - ua, 28f, 28f - la, hh, uh, lh, ha, ua, la);
            }

            EnemyDamageInfo Info(DamageResult r, DamageSourceKind src, in DamageEventMeta m)
            {
                return new EnemyDamageInfo(null, EnemyTier.Standard, in r, Vector3.zero, Vector3.forward, src, 1f, 1f, false, in m);
            }

            // flesh hit + direct health damage
            var flesh = DamageEventClassifier.Classify(Info(new DamageResult { HealthDamage = 10f, Region = BodyRegion.UpperBody },
                DamageSourceKind.Bullet, Meta(DamageKind.Direct, 0, 10, 0, 0, 0, 0)));
            Assert.AreEqual(DamageEventClass.FleshHit | DamageEventClass.DirectHealthDamage, flesh);

            // helmet armor hit + armor damaged
            var helm = DamageEventClassifier.Classify(Info(new DamageResult { ArmorDamage = 5f, WasArmored = true, Region = BodyRegion.Head },
                DamageSourceKind.Bullet, Meta(DamageKind.Direct, 0, 0, 0, 5, 0, 0)));
            Assert.AreEqual(DamageEventClass.HelmetArmorHit | DamageEventClass.ArmorDamaged, helm);

            // torso armor hit + broken
            var torso = DamageEventClassifier.Classify(Info(new DamageResult { ArmorDamage = 38f, WasArmored = true, ArmorBroke = true, Region = BodyRegion.UpperBody },
                DamageSourceKind.Bullet, Meta(DamageKind.Direct, 0, 0, 0, 0, 38, 0)));
            Assert.AreEqual(DamageEventClass.TorsoArmorHit | DamageEventClass.ArmorDamaged | DamageEventClass.ArmorBroken, torso);

            // leg armor hit (meta yok → Region'dan slot türetilir)
            var leg = DamageEventClassifier.Classify(new EnemyDamageInfo(null, EnemyTier.Standard,
                new DamageResult { ArmorDamage = 3f, WasArmored = true, Region = BodyRegion.LowerBody },
                Vector3.zero, Vector3.forward, DamageSourceKind.Pellet, 1f, 1f, false));
            Assert.AreEqual(DamageEventClass.LegArmorHit | DamageEventClass.ArmorDamaged, leg);

            // explosion armor damage (üç slot) + explosion health damage (açık yok → 0)
            var expArmor = DamageEventClassifier.Classify(Info(new DamageResult { ArmorDamage = 40f, WasArmored = true, Region = BodyRegion.UpperBody },
                DamageSourceKind.Explosion, Meta(DamageKind.Explosion, 0, 0, 0, 10, 18, 12)));
            Assert.AreEqual(DamageEventClass.HelmetArmorHit | DamageEventClass.TorsoArmorHit | DamageEventClass.LegArmorHit |
                            DamageEventClass.ArmorDamaged | DamageEventClass.ExplosionArmorDamage, expArmor);

            // explosion health damage (flesh hit DEĞİL: patlama kan/kritik dressing'i ayrı sınıftır)
            var expHealth = DamageEventClassifier.Classify(Info(new DamageResult { HealthDamage = 75f, Region = BodyRegion.UpperBody },
                DamageSourceKind.Explosion, Meta(DamageKind.Explosion, 25, 20, 30, 0, 0, 0)));
            Assert.AreEqual(DamageEventClass.ExplosionHealthDamage, expHealth);

            // enemy death
            var death = DamageEventClassifier.Classify(Info(new DamageResult { HealthDamage = 200f, Killed = true, Region = BodyRegion.Head },
                DamageSourceKind.Bullet, Meta(DamageKind.Direct, 200, 0, 0, 0, 0, 0)));
            Assert.IsTrue((death & DamageEventClass.EnemyDeath) != 0);
            Assert.IsTrue((death & DamageEventClass.FleshHit) != 0);

            // Kagura/Tide: Source=Explosion ama Kind=Direct (?U-1 varsayımı görünür kılınır)
            var special = DamageEventClassifier.Classify(Info(new DamageResult { HealthDamage = 10f, Region = BodyRegion.UpperBody },
                DamageSourceKind.Explosion, Meta(DamageKind.Direct, 0, 10, 0, 0, 0, 0)));
            Assert.AreEqual(DamageEventClass.FleshHit | DamageEventClass.DirectHealthDamage, special,
                "Meta varken sınıf Source'tan değil Kind'dan gelmeli.");
        }

        // ============================================================ feedback / kritik [#19] (D08)

        /// <summary>D08 / REQ-089: patlamada CriticalHit tier yok; zırhlı kafa isabeti kritik değil; açık kafa doğrudan isabet kritik (mevcut PrototypeOnly 2x, ürün kararı değil).</summary>
        [Test]
        public void Kritik_YalnizAcikKafaDogrudan_PatlamadaYok()
        {
            var acikKafa = new EnemyHitFeedbackInfo
            {
                Result = new DamageResult { HealthDamage = 22f, Region = BodyRegion.Head },
                FromExplosion = false
            };
            Assert.AreEqual(HitFeedbackTier.CriticalHit, HitFeedbackDirector.ResolveTier(in acikKafa));
            Assert.IsTrue(DamageRegions.IsCritical(in acikKafa.Result, DamageSourceKind.Bullet));

            var kaskliKafa = new EnemyHitFeedbackInfo
            {
                Result = new DamageResult { ArmorDamage = 11f, WasArmored = true, Region = BodyRegion.Head },
                FromExplosion = false
            };
            Assert.AreEqual(HitFeedbackTier.ArmorHit, HitFeedbackDirector.ResolveTier(in kaskliKafa));
            Assert.IsFalse(DamageRegions.IsCritical(in kaskliKafa.Result, DamageSourceKind.Bullet));

            var patlamaKafa = new EnemyHitFeedbackInfo
            {
                Result = new DamageResult { HealthDamage = 25f, Region = BodyRegion.Head },
                FromExplosion = true
            };
            Assert.AreEqual(HitFeedbackTier.FleshHit, HitFeedbackDirector.ResolveTier(in patlamaKafa),
                "REQ-089: patlama kafa payı kritik tier üretmemeli.");
            Assert.IsFalse(DamageRegions.IsCritical(in patlamaKafa.Result, DamageSourceKind.Explosion));

            var patlamaOlum = new EnemyHitFeedbackInfo
            {
                Result = new DamageResult { HealthDamage = 25f, Region = BodyRegion.Head, Killed = true },
                FromExplosion = true
            };
            Assert.AreEqual(HitFeedbackTier.Death, HitFeedbackDirector.ResolveTier(in patlamaOlum));
        }

        // ============================================================ bölge çözümü fallback (D10 saf yarısı)

        /// <summary>Kayıtsız/adsız collider: bölge yükseklik kesrinden, sayaç artar, çözüm yolu 'Height' (sessiz değil).</summary>
        [Test]
        public void BolgeCozumu_KayitsizCollider_AcikFallback()
        {
            DamageTargetResolver.ResetRegionDiagnostics();
            var go = new GameObject("wp006-fallback-root");
            try
            {
                var target = new DD.Prototype.Weapons.DamageTarget { Root = go.transform, Height = 2f };

                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new Regex("not registered in HitboxRegistry"));
                BodyRegion kafa = DamageTargetResolver.ResolveRegion(null, in target, go.transform.position + Vector3.up * 1.8f);
                Assert.AreEqual(BodyRegion.Head, kafa);
                Assert.AreEqual(RegionResolution.Height, DamageTargetResolver.LastResolution);
                Assert.AreEqual(1, DamageTargetResolver.RegionFallbackCount);

                BodyRegion govde = DamageTargetResolver.ResolveRegion(null, in target, go.transform.position + Vector3.up * 1.2f);
                Assert.AreEqual(BodyRegion.UpperBody, govde);
                BodyRegion bacak = DamageTargetResolver.ResolveRegion(null, in target, go.transform.position + Vector3.up * 0.3f);
                Assert.AreEqual(BodyRegion.LowerBody, bacak);
                Assert.AreEqual(3, DamageTargetResolver.RegionFallbackCount, "Her fallback sayılmalı.");

                // İkinci ve sonraki fallback'ler uyarı loglamaz (kare başına log yok) — LogAssert beklenti dışı uyarı görürse FAIL.
                UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(go);
                DamageTargetResolver.ResetRegionDiagnostics();
            }
        }

        /// <summary>Kayıtlı collider: bölge HitboxRegistry'den, fallback sayacı artmaz, yol 'Registry'.</summary>
        [Test]
        public void BolgeCozumu_KayitliCollider_RegistryOnce()
        {
            DamageTargetResolver.ResetRegionDiagnostics();
            var root = new GameObject("wp006-registry-root");
            var col = new GameObject("Hitbox_Generic").AddComponent<SphereCollider>();
            col.transform.SetParent(root.transform, false);
            col.transform.localPosition = Vector3.up * 0.2f;   // yükseklik kesri 'alt vücut' derdi; kayıt 'Head' diyor
            var receiver = new SahteAlici();
            try
            {
                HitboxRegistry.Register(col, receiver, null, BodyRegion.Head, root.transform);
                var target = new DD.Prototype.Weapons.DamageTarget { Root = root.transform, Height = 2f, Receiver = receiver };

                BodyRegion bolge = DamageTargetResolver.ResolveRegion(col, in target, col.transform.position);
                Assert.AreEqual(BodyRegion.Head, bolge, "Kayıt geometriye/isme üstün olmalı.");
                Assert.AreEqual(RegionResolution.Registry, DamageTargetResolver.LastResolution);
                Assert.AreEqual(0, DamageTargetResolver.RegionFallbackCount);
            }
            finally
            {
                HitboxRegistry.Unregister(col);
                Object.DestroyImmediate(root);
                DamageTargetResolver.ResetRegionDiagnostics();
            }
        }

        private sealed class SahteAlici : IDamageReceiver
        {
            public DamageResult ApplyHit(in DamageHit hit) => default;
        }

        // ============================================================ kaynak taraması (L28 EditMode yarısı)

        /// <summary>
        /// Hasar/zırh yolunda kare başına sahne araması (FindObject*/GameObject.Find), tek Player kilidi
        /// (PlayerRegistry.Primary), string.Format, ToLowerInvariant ve LINQ yok (kaynak taraması; kapsam ölçülü —
        /// dokuz dosya). Gerekçeli istisnalar: DamageTargetResolver ad-fallback'ı (ilk karşılaşmada, önbellekli) ve
        /// ZombieEnemy.DescribeArmor (yalnız F1 tanı paneli okur, vuruş yolunda değil).
        /// </summary>
        [Test]
        public void KaynakTaramasi_HasarYolundaKareBasinaAramaYok()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype");
            string[] dosyalar =
            {
                Path.Combine(root, "Damage", "DamageEventMeta.cs"),
                Path.Combine(root, "Damage", "ArmorSet.cs"),
                Path.Combine(root, "Damage", "RegionalDamageBody.cs"),
                Path.Combine(root, "Damage", "HitboxRegistry.cs"),
                Path.Combine(root, "Weapons", "HitscanEngine.cs"),
                Path.Combine(root, "Weapons", "DamageTargetResolver.cs"),
                Path.Combine(root, "Enemies", "ArmorVisualState.cs"),
                Path.Combine(root, "Enemies", "ZombieEnemy.cs"),
                Path.Combine(root, "Enemies", "ZombieAppearance.cs"),
            };

            var yasak = new Regex(@"FindObjectOfType|FindObjectsOfType|GameObject\.Find\(|PlayerRegistry\.Primary|string\.Format|\.ToLowerInvariant\(\)|System\.Linq");
            var ihlaller = new List<string>();
            int taranan = 0;

            foreach (string f in dosyalar)
            {
                Assert.IsTrue(File.Exists(f), "Tarama kapsamı kırık: " + f);
                string[] satirlar = File.ReadAllLines(f);
                taranan++;
                for (int i = 0; i < satirlar.Length; i++)
                {
                    string s = satirlar[i].Trim();
                    if (s.StartsWith("//") || s.StartsWith("///") || s.StartsWith("*")) continue;
                    if (!yasak.IsMatch(s)) continue;

                    // Bilinen ve gerekçeli tek istisna: collider adı yalnız İLK karşılaşmada küçültülür ve önbelleklenir
                    // (DamageTargetResolver.TryRegionFromName — fallback yolu, vuruş başına değil).
                    if (f.EndsWith("DamageTargetResolver.cs") && s.Contains("ToLowerInvariant")) continue;
                    // F1 tanı satırı (DescribeArmor): yalnız panel açıkken, vuruş yolunda çağrılmaz.
                    if (f.EndsWith("ZombieEnemy.cs") && s.Contains("string.Format(\"{0} hp")) continue;
                    ihlaller.Add(Path.GetFileName(f) + ":" + (i + 1) + " " + s);
                }
            }

            Assert.AreEqual(dosyalar.Length, taranan);
            Assert.IsEmpty(ihlaller, "Hasar yolunda kare başına arama / tek Player kilidi / string üretimi:\n" + string.Join("\n", ihlaller));
        }
    }
}
