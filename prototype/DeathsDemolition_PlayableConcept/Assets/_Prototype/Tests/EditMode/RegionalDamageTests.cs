using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bağımsız doğrulama · bölgesel zırh + can modeli.
    ///
    /// İki ayırt edici kural burada sınanır:
    ///   1. Zırh taşması cana GEÇMEZ (REQ-083 doğrudan atış, REQ-090 patlama).
    ///   2. Bir patlama tek bir hasar bütçesidir; üç pay %25/%45/%30 ve toplamı tam 1.
    ///
    /// Beklenen sayılar SABİT (TS-201). Referans dayanıklılıklar 22/38/28,
    /// açık bölge çarpanları 2 / 1 / 0.8.
    /// </summary>
    [TestFixture]
    public sealed class RegionalDamageTests
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

        private static DamageHit Vurus(float amount, BodyRegion region)
        {
            return new DamageHit
            {
                Amount = amount,
                Region = region,
                Point = Vector3.zero,
                Direction = Vector3.forward,
                ImpactForce = 1f,
                Source = DamageSourceKind.Bullet
            };
        }

        // ================================================= sabit yapılandırma sayıları

        [Test]
        public void ZirhDayanikliliklari_Sabit()
        {
            Assert.AreEqual(22f, _armor.HelmetDurability, 0.0001f);
            Assert.AreEqual(38f, _armor.BodyDurability, 0.0001f);
            Assert.AreEqual(28f, _armor.LegDurability, 0.0001f);
            Assert.AreEqual(22f, _armor.DurabilityFor(ArmorSlot.Helmet), 0.0001f);
            Assert.AreEqual(38f, _armor.DurabilityFor(ArmorSlot.Body), 0.0001f);
            Assert.AreEqual(28f, _armor.DurabilityFor(ArmorSlot.Leg), 0.0001f);
            Assert.AreEqual(0f, _armor.DurabilityFor(ArmorSlot.None), 0.0001f);
        }

        [Test]
        public void AcikBolgeCarpanlari_Sabit()
        {
            Assert.AreEqual(2.0f, _armor.ExposedHeadMultiplier, 0.0001f, "Açık kafa çarpanı 2x olmalı.");
            Assert.AreEqual(1.0f, _armor.ExposedUpperBodyMultiplier, 0.0001f, "Açık gövde çarpanı 1x olmalı.");
            Assert.AreEqual(0.8f, _armor.ExposedLowerBodyMultiplier, 0.0001f, "Açık bacak çarpanı 0.8x olmalı.");
        }

        [Test]
        public void PatlamaPaylari_Yuzde25_45_30_ToplamTamBir()
        {
            Assert.AreEqual(0.25f, _explosion.HeadShare, 0.0001f);
            Assert.AreEqual(0.45f, _explosion.UpperBodyShare, 0.0001f);
            Assert.AreEqual(0.30f, _explosion.LowerBodyShare, 0.0001f);

            float toplam = _explosion.HeadShare + _explosion.UpperBodyShare + _explosion.LowerBodyShare;
            Assert.AreEqual(1.0f, toplam, 0.0001f, "Üç pay tam 1 etmiyor; patlama bütçesi kayıyor.");
        }

        // ================================================== doğrudan atış · açık bölge

        [Test]
        public void DogrudanAtis_AcikKafa_Iki_Kat()
        {
            var body = Beden(100f);
            DamageResult r = body.ApplyDirectHit(Vurus(10f, BodyRegion.Head), _armor);

            Assert.AreEqual(20f, r.HealthDamage, 0.0001f, "10 hasar açık kafaya 20 can hasarı olmalı.");
            Assert.AreEqual(80f, body.Health, 0.0001f);
            Assert.IsFalse(r.WasArmored);
            Assert.IsFalse(r.ArmorBroke);
        }

        [Test]
        public void DogrudanAtis_AcikGovde_BirKat()
        {
            var body = Beden(100f);
            DamageResult r = body.ApplyDirectHit(Vurus(10f, BodyRegion.UpperBody), _armor);
            Assert.AreEqual(10f, r.HealthDamage, 0.0001f);
            Assert.AreEqual(90f, body.Health, 0.0001f);
        }

        [Test]
        public void DogrudanAtis_AcikBacak_NoktaSekizKat()
        {
            var body = Beden(100f);
            DamageResult r = body.ApplyDirectHit(Vurus(10f, BodyRegion.LowerBody), _armor);
            Assert.AreEqual(8f, r.HealthDamage, 0.0001f);
            Assert.AreEqual(92f, body.Health, 0.0001f);
        }

        // ============================================ ZIRH TAŞMASI · asıl çürütme testi

        /// <summary>
        /// REQ-083. Kask 22 dayanıklılık. 1000 hasar gelse bile cana TEK BİR PUAN geçmez.
        /// Sabotaj S-5'in doğrudan-atış karşılığı.
        /// </summary>
        [Test]
        public void DogrudanAtis_ZirhTasmasi_CanaGECMEZ()
        {
            var body = Beden(100f);
            body.Armor.Equip(ArmorSlot.Helmet, 22f);

            DamageResult r = body.ApplyDirectHit(Vurus(1000f, BodyRegion.Head), _armor);

            Assert.AreEqual(0f, r.HealthDamage, 0.0001f, "Zırhlı bölgeye vuruş cana hasar vermemeli.");
            Assert.AreEqual(100f, body.Health, 0.0001f, "TAŞMA CANA GEÇTİ — REQ-083 ihlali.");
            Assert.AreEqual(22f, r.ArmorDamage, 0.0001f, "Emilen miktar kaskın dayanıklılığıyla sınırlı olmalı.");
            Assert.IsTrue(r.ArmorBroke, "22'yi aşan vuruş kaskı kırmalı.");
            Assert.IsTrue(r.WasArmored);
            Assert.IsFalse(body.IsDead);
        }

        [Test]
        public void DogrudanAtis_ZirhKirildiktanSonraBolgeAcilir()
        {
            var body = Beden(100f);
            body.Armor.Equip(ArmorSlot.Helmet, 22f);

            body.ApplyDirectHit(Vurus(1000f, BodyRegion.Head), _armor);
            Assert.AreEqual(100f, body.Health, 0.0001f);

            // İkinci vuruş artık açık kafaya gider: 10 * 2 = 20
            DamageResult r2 = body.ApplyDirectHit(Vurus(10f, BodyRegion.Head), _armor);
            Assert.AreEqual(20f, r2.HealthDamage, 0.0001f);
            Assert.AreEqual(80f, body.Health, 0.0001f);
            Assert.IsTrue(body.Armor.IsBroken(ArmorSlot.Helmet));
            Assert.IsFalse(body.Armor.Protects(ArmorSlot.Helmet));
            Assert.IsTrue(body.Armor.IsEquipped(ArmorSlot.Helmet), "Kırık parça 'hiç takılmamış'tan ayrı kalmalı.");
        }

        [Test]
        public void DogrudanAtis_ZirhiBirdenCokVurusla_KismiEmilim()
        {
            var body = Beden(100f);
            body.Armor.Equip(ArmorSlot.Body, 38f);

            DamageResult a = body.ApplyDirectHit(Vurus(20f, BodyRegion.UpperBody), _armor);
            Assert.AreEqual(20f, a.ArmorDamage, 0.0001f);
            Assert.IsFalse(a.ArmorBroke);
            Assert.AreEqual(18f, body.Armor.Current(ArmorSlot.Body), 0.0001f);

            DamageResult b = body.ApplyDirectHit(Vurus(50f, BodyRegion.UpperBody), _armor);
            Assert.AreEqual(18f, b.ArmorDamage, 0.0001f, "Kalan 18'den fazlası emilemez.");
            Assert.IsTrue(b.ArmorBroke);
            Assert.AreEqual(0f, b.HealthDamage, 0.0001f, "Kırılma vuruşunun taşması cana geçmemeli.");
            Assert.AreEqual(100f, body.Health, 0.0001f);
        }

        // =================================================== PATLAMA · üç-bölge dağılımı

        [Test]
        public void Patlama_ZirhsizBeden_UcPayTamOlarakDagilir()
        {
            var body = Beden(1000f);
            DamageResult r = body.ApplyExplosion(100f, _armor, _explosion);

            Assert.AreEqual(25f, body.LastHealthDamageIn(BodyRegion.Head), 0.0001f, "Kafa payı 25 olmalı.");
            Assert.AreEqual(45f, body.LastHealthDamageIn(BodyRegion.UpperBody), 0.0001f, "Gövde payı 45 olmalı.");
            Assert.AreEqual(30f, body.LastHealthDamageIn(BodyRegion.LowerBody), 0.0001f, "Bacak payı 30 olmalı.");
            Assert.AreEqual(100f, r.HealthDamage, 0.0001f, "Toplam patlama hasarı bütçeye eşit olmalı.");
            Assert.AreEqual(900f, body.Health, 0.0001f);
        }

        /// <summary>REQ-089: patlamada bölgesel kritik çarpan UYGULANMAZ.</summary>
        [Test]
        public void Patlama_BolgeselKritikCarpanUygulanmaz()
        {
            var body = Beden(1000f);
            body.ApplyExplosion(100f, _armor, _explosion);

            // Kritik uygulansaydı kafa 25*2=50 olurdu.
            Assert.AreEqual(25f, body.LastHealthDamageIn(BodyRegion.Head), 0.0001f,
                "Patlamada kafa payına 2x uygulanmış — REQ-089 ihlali.");
            Assert.AreEqual(30f, body.LastHealthDamageIn(BodyRegion.LowerBody), 0.0001f,
                "Patlamada bacak payına 0.8x uygulanmış — REQ-089 ihlali.");
        }

        /// <summary>
        /// REQ-088 + REQ-090. Sabotaj S-5'in asıl yakalayıcısı.
        /// Tam zırhlı bedene 1000 hasarlık patlama: üç pay da plakaya iner,
        /// toplam emilen 22+38+28 = 88, cana geçen SIFIR.
        /// </summary>
        [Test]
        public void Patlama_TamZirhliBeden_TASMA_CANA_GECMEZ()
        {
            var body = Beden(100f);
            body.Armor.Equip(ArmorSlot.Helmet, 22f);
            body.Armor.Equip(ArmorSlot.Body, 38f);
            body.Armor.Equip(ArmorSlot.Leg, 28f);
            Assert.IsTrue(body.Armor.FullyProtected);

            DamageResult r = body.ApplyExplosion(1000f, _armor, _explosion);

            Assert.AreEqual(0f, r.HealthDamage, 0.0001f, "Tam zırhlı bedene patlamadan can hasarı geçti.");
            Assert.AreEqual(100f, body.Health, 0.0001f, "PATLAMA TAŞMASI CANA GEÇTİ — REQ-090 ihlali.");
            Assert.AreEqual(88f, r.ArmorDamage, 0.0001f, "Emilen toplam 22+38+28 = 88 olmalı.");
            Assert.IsTrue(r.ArmorBroke);
            Assert.AreEqual(3, body.LastBreakCount, "Üç parça da kırılmalı.");
            Assert.IsFalse(body.IsDead);
            Assert.IsTrue(r.WasArmored);
        }

        /// <summary>REQ-087: kısmen zırhlı beden yalnız AÇIK paylarının toplamını yer.</summary>
        [Test]
        public void Patlama_YalnizKaskVar_AcikPaylarinToplami()
        {
            var body = Beden(1000f);
            body.Armor.Equip(ArmorSlot.Helmet, 22f);

            DamageResult r = body.ApplyExplosion(100f, _armor, _explosion);

            // Kafa payı 25 → 22 emilir, 3 atılır. Gövde 45 + bacak 30 = 75 cana.
            Assert.AreEqual(75f, r.HealthDamage, 0.0001f, "Can hasarı yalnız açık payların toplamı olmalı.");
            Assert.AreEqual(22f, r.ArmorDamage, 0.0001f);
            Assert.AreEqual(925f, body.Health, 0.0001f);
            Assert.AreEqual(0f, body.LastHealthDamageIn(BodyRegion.Head), 0.0001f,
                "Kaskın emmediği 3 puan cana sızdı.");
        }

        /// <summary>REQ-085: zırh eklemek toplam EMİLEN hasarı asla ARTIRMAZ.</summary>
        [Test]
        public void Patlama_ZirhEklemekToplamiArtirmaz()
        {
            var ciplak = Beden(10000f);
            DamageResult ciplakSonuc = ciplak.ApplyExplosion(100f, _armor, _explosion);

            var zirhli = Beden(10000f);
            zirhli.Armor.Equip(ArmorSlot.Helmet, 22f);
            zirhli.Armor.Equip(ArmorSlot.Body, 38f);
            DamageResult zirhliSonuc = zirhli.ApplyExplosion(100f, _armor, _explosion);

            Assert.AreEqual(100f, ciplakSonuc.HealthDamage, 0.0001f);
            Assert.LessOrEqual(zirhliSonuc.HealthDamage, ciplakSonuc.HealthDamage,
                "Zırh eklemek can hasarını artırdı — REQ-085 ihlali.");

            float zirhliToplam = zirhliSonuc.HealthDamage + zirhliSonuc.ArmorDamage;
            Assert.LessOrEqual(zirhliToplam, 100f + 0.0001f,
                "Zırhlı bedenin toplam emdiği patlama bütçesini aştı.");
        }

        [Test]
        public void Patlama_PaylarNormalizeEdilir_BozukConfigButceyiSismez()
        {
            var bozuk = new ExplosionConfigSection { HeadShare = 1f, UpperBodyShare = 1f, LowerBodyShare = 1f };
            var body = Beden(1000f);

            DamageResult r = body.ApplyExplosion(90f, _armor, bozuk);

            Assert.AreEqual(90f, r.HealthDamage, 0.001f, "Paylar normalize edilmedi; bütçe şişti.");
            Assert.AreEqual(30f, body.LastHealthDamageIn(BodyRegion.Head), 0.001f);
            Assert.AreEqual(30f, body.LastHealthDamageIn(BodyRegion.UpperBody), 0.001f);
            Assert.AreEqual(30f, body.LastHealthDamageIn(BodyRegion.LowerBody), 0.001f);
        }

        // ================================================================ havuz hijyeni

        [Test]
        public void Reset_HavuzdanGeriDonenBedenZirhSizdirmaz()
        {
            var body = Beden(100f);
            body.Armor.Equip(ArmorSlot.Helmet, 22f);
            body.ApplyDirectHit(Vurus(5f, BodyRegion.Head), _armor);

            body.Reset(50f);

            Assert.AreEqual(50f, body.MaxHealth, 0.0001f);
            Assert.AreEqual(50f, body.Health, 0.0001f);
            Assert.AreEqual(0, body.Armor.EquippedCount, "Havuzdan dönen beden eski zırhını taşıyor.");
            Assert.AreEqual(0, body.Armor.ProtectingCount);
            Assert.IsTrue(body.Armor.IsBare);
            Assert.AreEqual(0, body.LastBreakCount);
        }

        [Test]
        public void RescaleMaxHealth_OraniKorur_Iyilestirmez()
        {
            var body = Beden(100f);
            body.ApplyDirectHit(Vurus(60f, BodyRegion.UpperBody), _armor);
            Assert.AreEqual(40f, body.Health, 0.0001f);

            body.RescaleMaxHealth(200f);

            Assert.AreEqual(200f, body.MaxHealth, 0.0001f);
            Assert.AreEqual(80f, body.Health, 0.0001f, "%40 can oranı korunmalı.");
            Assert.AreEqual(0.4f, body.HealthRatio, 0.0001f);
        }

        [Test]
        public void OlduktenSonraHasarKabulEdilmez()
        {
            var body = Beden(10f);
            DamageResult olum = body.ApplyDirectHit(Vurus(50f, BodyRegion.UpperBody), _armor);
            Assert.IsTrue(olum.Killed);
            Assert.IsTrue(body.IsDead);

            DamageResult sonra = body.ApplyDirectHit(Vurus(50f, BodyRegion.UpperBody), _armor);
            Assert.IsFalse(sonra.Killed, "Ölü beden ikinci kez öldürülemez.");
            Assert.AreEqual(0f, sonra.HealthDamage, 0.0001f);

            DamageResult patlama = body.ApplyExplosion(500f, _armor, _explosion);
            Assert.IsFalse(patlama.Killed);
            Assert.AreEqual(0f, patlama.HealthDamage, 0.0001f);
        }

        [Test]
        public void DusmanCani_TierVeTurCarpanlariSabit()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                // 34 taban · Standard 1x · Elite 2.2x · Ultra 5x
                Assert.AreEqual(34f, config.EnemyHealth(EnemyTier.Standard, 1), 0.001f);
                Assert.AreEqual(28.9f, config.EnemyHealth(EnemyTier.Special, 1), 0.001f, "0.85x Runner");
                Assert.AreEqual(74.8f, config.EnemyHealth(EnemyTier.Elite, 1), 0.001f, "2.2x Guard");
                Assert.AreEqual(170f, config.EnemyHealth(EnemyTier.Ultra, 1), 0.001f, "5x Executioner");

                // R5 can çarpanı 1.48 · Elite: 34 * 2.2 * 1.48 = 110.704
                Assert.AreEqual(110.704f, config.EnemyHealth(EnemyTier.Elite, 5), 0.01f);
                // R8 can çarpanı 1.84 · Ultra: 34 * 5 * 1.84 = 312.8
                Assert.AreEqual(312.8f, config.EnemyHealth(EnemyTier.Ultra, 8), 0.01f);

                // Saldırı hasarı · R1 çarpanı 1
                Assert.AreEqual(10f, config.EnemyAttackDamage(EnemyTier.Standard, 1), 0.001f);
                Assert.AreEqual(18f, config.EnemyAttackDamage(EnemyTier.Elite, 1), 0.001f);
                Assert.AreEqual(34f, config.EnemyAttackDamage(EnemyTier.Ultra, 1), 0.001f);
                // R10 hasar çarpanı 1.72 · Standard: 10 * 1.72 = 17.2
                Assert.AreEqual(17.2f, config.EnemyAttackDamage(EnemyTier.Standard, 10), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void OlculenBulgu_UltraTierR8deTanitilir()
        {
            var enemies = new EnemyConfigSection();
            Assert.AreEqual(1, enemies.Find(EnemyTier.Standard).IntroductionRound);
            Assert.AreEqual(3, enemies.Find(EnemyTier.Special).IntroductionRound);
            Assert.AreEqual(5, enemies.Find(EnemyTier.Elite).IntroductionRound);
            Assert.AreEqual(8, enemies.Find(EnemyTier.Ultra).IntroductionRound,
                "Ultra R8'de tanıtılıyor — 10-15 dk'lık oturumda doğal görülmeyebilir (ölçülen bulgu).");

            // R1-R7 arasında Ultra ağırlığı sıfır olmalı.
            for (int r = 1; r <= 7; r++)
            {
                Assert.AreEqual(0f, enemies.Find(EnemyTier.Ultra).ResolveSpawnWeight(r), 0.0001f,
                    "R" + r + " Ultra ağırlığı sıfır olmalı.");
            }

            Assert.AreEqual(3f, enemies.Find(EnemyTier.Ultra).ResolveSpawnWeight(8), 0.0001f);
        }
    }
}
