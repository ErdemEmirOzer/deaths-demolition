using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// G-21 · Special Weapon kimligi, yer omru ve sonme egrisi.
    ///
    /// Bu dosya iki kullanici kararini kilitler:
    ///
    ///   1. "Special weaponlar fantastik / bilim-kurgu olacak, klasik mermili silah
    ///      olmayacak" -> uc silah, uc AYRI oynanis fiili. Testler her ucunun de var
    ///      oldugunu ve birbirinin kopyasi OLMADIGINI dogrular.
    ///
    ///   2. "Yerde kalma suresi cok az, uzasin; sayi gosterilmesin, hizlanarak yanip
    ///      sonerek anlasilsin" -> yer omru 12 -> 22 sn, ve
    ///      <see cref="GroundBlinkClock"/> geometrik hizlanan bir egri uretir.
    ///
    /// Sonme egrisi icin referans (SABIT):
    ///   f(r) = 0.8 * (6.5 / 0.8) ^ (1 - r)      r = kalan / toplam
    ///   f(1) = 0.80 Hz   ·   f(0) = 6.50 Hz   ·   f(0.5) = sqrt(0.8*6.5) = 2.280 Hz
    /// </summary>
    [TestFixture]
    public sealed class SpecialWeaponIdentityTests
    {
        // ============================================================ 1. kimlik (WP-069 B4: DEC-094 iki Special, DEC-097 sayılar)

        [Test]
        public void IkiSpecialWeaponTanimliVeAdlari_Sabit()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.AreEqual(2, SpecialWeaponConfigSection.All.Length, "DEC-094: yalnız iki Special");
            Assert.AreEqual(2, System.Enum.GetValues(typeof(SpecialWeaponId)).Length, "Kagura / Ofuda / Tide kalktı");
            Assert.AreEqual("Raiju", s.DisplayName(SpecialWeaponId.Raiju));
            Assert.AreEqual("Namekuji", s.DisplayName(SpecialWeaponId.Namekuji));
            Assert.AreEqual("sw_arc", SpecialWeaponKeys.Of(SpecialWeaponId.Raiju));
            Assert.AreEqual("sw_gel", SpecialWeaponKeys.Of(SpecialWeaponId.Namekuji));
        }

        [Test]
        public void EskiRotaryCannon_ArtikYok()
        {
            var s = new SpecialWeaponConfigSection();

            foreach (SpecialWeaponId id in System.Enum.GetValues(typeof(SpecialWeaponId)))
            {
                Assert.AreNotEqual("Heavy Rotary Cannon", s.DisplayName(id),
                    "Klasik mermili Special Weapon kaldirilmis olmali (kullanici karari).");
            }
        }

        /// <summary>İki silah iki ayrı fiil: tempo, şarjör ve süre tavanı ayrı (DIRECTION §7a/§7b, DEC-097).</summary>
        [Test]
        public void IkiSilah_TempoSarjorVeTavan_DEC097()
        {
            var s = new SpecialWeaponConfigSection();
            WeaponDefinition arc = SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Raiju);
            WeaponDefinition gel = SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Namekuji);
            Assert.IsNotNull(arc, "Resources/Arsenal/WD_sw_arc yok");
            Assert.IsNotNull(gel, "Resources/Arsenal/WD_sw_gel yok");

            Assert.AreEqual(3, arc.ammo.magazine); Assert.AreEqual(9, arc.ammo.reserve);
            Assert.AreEqual(8, gel.ammo.magazine); Assert.AreEqual(16, gel.ammo.reserve);
            Assert.AreEqual(40f, s.CapSeconds(SpecialWeaponId.Raiju), 1e-4f);
            Assert.AreEqual(45f, s.CapSeconds(SpecialWeaponId.Namekuji), 1e-4f);
            Assert.AreEqual(0.25f, arc.fire.chargeSeconds, 1e-4f, "Raiju şarjı");
            Assert.AreEqual(0f, gel.fire.chargeSeconds, 1e-4f);
            Assert.AreEqual(0.9f, 1f / s.Common(SpecialWeaponId.Raiju).FireRate, 1e-3f, "çevrim 0,9 s");
            Assert.AreEqual(0.45f, 1f / s.Common(SpecialWeaponId.Namekuji).FireRate, 1e-3f, "çevrim 0,45 s");
            Assert.AreEqual(ProjectileKind.Orb, arc.projectile.kind);
            Assert.AreEqual(ProjectileKind.Gel, gel.projectile.kind);
            Assert.AreEqual(14f, arc.projectile.speed, 1e-4f);
            Assert.AreEqual(22f, gel.projectile.speed, 1e-4f);
            Assert.AreEqual(12f, gel.projectile.gravity, 1e-4f, "jel yayı");
            Assert.AreEqual(SpecialLimit.AmmoOrTime, arc.specialLifecycle.limit);
        }

        /// <summary>Raiju zinciri: 8 hedef, 6 m sıçrama, 0,08 s; Ultra (Executioner) %60 + 2,5 s ve zincir sürer (🔴 #2 varsayılanı).</summary>
        [Test]
        public void Raiju_ZincirKurallari_ExecutionerDirenci()
        {
            WeaponArcChainBlock c = SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Raiju).arcChain;
            Assert.AreEqual(8, c.maxTargets);
            Assert.AreEqual(6f, c.jumpRadius, 1e-4f);
            Assert.AreEqual(0.08f, c.jumpDelaySeconds, 1e-4f);
            Assert.IsTrue(c.ignoresArmor, "elektrik zırhı yok sayar");
            Assert.IsTrue(SpecialImpacts.Kills(SpecialImpacts.RuleFor(c.tierRules, EnemyTier.Standard), 30));
            Assert.IsTrue(SpecialImpacts.Kills(SpecialImpacts.RuleFor(c.tierRules, EnemyTier.Elite), 30));
            SpecialTierRule ultra = SpecialImpacts.RuleFor(c.tierRules, EnemyTier.Ultra);
            Assert.IsFalse(SpecialImpacts.Kills(ultra, 1));
            Assert.AreEqual(0.60f, ultra.maxHpShare, 1e-4f);
            Assert.AreEqual(2.5f, ultra.stunSeconds, 1e-4f);
        }

        /// <summary>Namekuji doğrudan isabet: Guard R ≤ 10 ölür, sonra %60 + devrilme; Executioner %25 (DIRECTION §7b).</summary>
        [Test]
        public void Namekuji_DogrudanIsabet_GuardRound10Siniri()
        {
            WeaponGelPuddleBlock g = SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Namekuji).gelPuddle;
            SpecialTierRule guard = SpecialImpacts.RuleFor(g.directHitRules, EnemyTier.Elite);
            Assert.IsTrue(SpecialImpacts.Kills(guard, 10), "R10'da ölür");
            Assert.IsFalse(SpecialImpacts.Kills(guard, 11), "R11'de pay + devrilme");
            Assert.AreEqual(0.60f, guard.maxHpShare, 1e-4f);
            Assert.AreEqual(0.25f, SpecialImpacts.RuleFor(g.directHitRules, EnemyTier.Ultra).maxHpShare, 1e-4f);
            Assert.AreEqual(1.6f, g.puddleRadius, 1e-4f);
            Assert.AreEqual(12, g.maxPuddles, "birikinti tavanı oyun kuralı");
        }

        [Test]
        public void RollWeapon_IkisininDeCikisiMumkun_VeSinirlardaCokmez()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.AreEqual(SpecialWeaponId.Raiju, s.RollWeapon(0f));
            Assert.AreEqual(SpecialWeaponId.Namekuji, s.RollWeapon(1f));
            Assert.AreEqual(SpecialWeaponId.Raiju, s.RollWeapon(0.49f));
            Assert.AreEqual(SpecialWeaponId.Namekuji, s.RollWeapon(0.51f));

            // Sinir disi girdiler kirpilmali, istisna atmamali.
            Assert.AreEqual(SpecialWeaponId.Raiju, s.RollWeapon(-5f));
            Assert.AreEqual(SpecialWeaponId.Namekuji, s.RollWeapon(5f));
        }

        [Test]
        public void RollWeapon_TumAgirliklarSifirsa_CokmezVeBirSilahDoner()
        {
            var s = new SpecialWeaponConfigSection();
            s.Common(SpecialWeaponId.Raiju).SpawnWeight = 0f;
            s.Common(SpecialWeaponId.Namekuji).SpawnWeight = 0f;

            Assert.AreEqual(SpecialWeaponId.Raiju, s.RollWeapon(0.5f),
                "Sifir agirlikta bile bir silah donmeli; 'hicbiri' bir durum degil.");
        }

        // ============================================================ 2. yer omru

        /// <summary>
        /// Yer omrunun gerekcesi olculmus bir sayi, his degil.
        ///
        /// Yol agi uzerinde olculen yuruyus mesafeleri (oyuncu 12 dusman doguş
        /// noktasindan birinde, birakma noktasi yonetmenin >= 22 m kuralini gecen 12
        /// Special noktadan biri; rota disi noktalar 1.6x agirlikli):
        ///
        ///     p25   49.8 m   =  8.0 sn        p90   93.8 m  = 15.1 sn
        ///     p50   67.4 m   = 10.9 sn        p95   98.5 m  = 15.9 sn
        ///     p75   79.8 m   = 12.9 sn        p100 109.8 m  = 17.7 sn
        ///
        /// MoveSpeed 6.2 m/s ile, HIC CARPISMADAN, DURMADAN kosarak. Eski 12 sn deger
        /// medyani bile karsilamiyordu - kullanicinin "yetisemiyorum" sikayeti his
        /// degil aritmetik.
        /// </summary>
        [Test]
        public void YerOmru_MedyanYuruyusu_CarpismaPayiylaKarsilar()
        {
            var s = new SpecialWeaponConfigSection();

            const float medyanYuruyusSaniye = 10.9f;   // 67.4 m / 6.2 m/s
            const float carpismaCarpani = 1.35f;       // kalabalik icinde duz kosulamaz
            const float tepkiSuresi = 1f;              // isareti gor, karar ver, don

            float gereken = medyanYuruyusSaniye * carpismaCarpani + tepkiSuresi;

            Assert.Greater(s.GroundLifetime, gereken,
                "Medyan birakma noktasi carpisma payiyla ulasilabilir olmali (" +
                gereken.ToString("0.0") + " sn).");
        }

        [Test]
        public void YerOmru_P90Yuruyusunu_Karsilar()
        {
            var s = new SpecialWeaponConfigSection();

            const float p90YuruyusSaniye = 15.1f;      // 93.8 m / 6.2 m/s
            float gereken = p90YuruyusSaniye * 1.35f + 1f;

            Assert.GreaterOrEqual(s.GroundLifetime, gereken - 0.5f,
                "p90 birakma noktasi, oyuncu hemen taahhut ederse yetisilebilir olmali.");
        }

        /// <summary>
        /// REQ-054 hala gecerli: birakma, oyuncuyu guvenli turundan CIKARMAK icin var.
        /// Haritanin bir kosesinden digerine olan en kotu durum ulasilamaz kalmali,
        /// yoksa karar diye bir sey kalmaz.
        /// </summary>
        [Test]
        public void YerOmru_HaritaninIkiUcunu_HalaKarsilamaz()
        {
            var s = new SpecialWeaponConfigSection();

            const float enKotuYuruyusSaniye = 17.7f;   // 109.8 m / 6.2 m/s
            float carpismayla = enKotuYuruyusSaniye * 1.35f;   // 23.9 sn

            Assert.Less(s.GroundLifetime, carpismayla,
                "Her birakma ulasilabilir olursa REQ-054'un getiri/risk karari yok olur.");
        }

        // ============================================================ 3. sonme egrisi

        [Test]
        public void SonmeEgrisi_UclariSabit()
        {
            Assert.AreEqual(0.8f, GroundBlinkClock.FrequencyHz(1f), 0.001f,
                "Birakma anindaki hiz 0.8 Hz olmali.");
            Assert.AreEqual(6.5f, GroundBlinkClock.FrequencyHz(0f), 0.001f,
                "Sifir aninda hiz 6.5 Hz olmali.");
        }

        /// <summary>
        /// Egrinin DOGRUSAL OLMADIGINI kanitlar. Dogrusal olsaydi ortadaki deger iki
        /// ucun aritmetik ortalamasi (3.65 Hz) olurdu; geometrik oldugu icin geometrik
        /// ortalama (sqrt(0.8 * 6.5) = 2.280 Hz).
        /// </summary>
        [Test]
        public void SonmeEgrisi_DogrusalDegil_Geometrik()
        {
            float orta = GroundBlinkClock.FrequencyHz(0.5f);
            float geometrikOrtalama = Mathf.Sqrt(GroundBlinkClock.SlowHz * GroundBlinkClock.FastHz);
            float aritmetikOrtalama = (GroundBlinkClock.SlowHz + GroundBlinkClock.FastHz) * 0.5f;

            Assert.AreEqual(geometrikOrtalama, orta, 0.001f,
                "Yari yolda geometrik ortalama olmali (2.280 Hz).");
            Assert.Less(orta, aritmetikOrtalama - 1f,
                "Dogrusal egri bu testi gecerdi; gecmemeli.");
        }

        /// <summary>
        /// Geometrik egrinin asil kazandirdigi sey: ALGILANAN degisim orani sabit.
        /// Her esit zaman diliminde frekans ayni ORANDA artar.
        /// </summary>
        [Test]
        public void SonmeEgrisi_EsitDilimlerdeEsitOran()
        {
            float a = GroundBlinkClock.FrequencyHz(1f) / GroundBlinkClock.FrequencyHz(0.75f);
            float b = GroundBlinkClock.FrequencyHz(0.75f) / GroundBlinkClock.FrequencyHz(0.5f);
            float c = GroundBlinkClock.FrequencyHz(0.5f) / GroundBlinkClock.FrequencyHz(0.25f);

            Assert.AreEqual(a, b, 0.001f);
            Assert.AreEqual(b, c, 0.001f);
        }

        [Test]
        public void SonmeEgrisi_MonotonHizlanir()
        {
            float onceki = 0f;

            for (int i = 0; i <= 20; i++)
            {
                float kalan01 = 1f - i / 20f;
                float simdi = GroundBlinkClock.FrequencyHz(kalan01);

                Assert.Greater(simdi, onceki, "Frekans her adimda artmali.");
                onceki = simdi;
            }
        }

        /// <summary>
        /// Frekans hicbir zaman goz icin "ritim" olmaktan cikip gurultuye donusmemeli.
        /// ~8 Hz hem ritim algisinin hem de fotosensitivite rehberliginin sinirlari.
        /// </summary>
        [Test]
        public void SonmeEgrisi_UstSinir_8HzinAltinda()
        {
            for (float kalan01 = -0.5f; kalan01 <= 1.5f; kalan01 += 0.05f)
            {
                Assert.LessOrEqual(GroundBlinkClock.FrequencyHz(kalan01), 8f,
                    "Sinir disi girdi bile 8 Hz'i asmamali.");
            }
        }

        /// <summary>
        /// Faz ENTEGRE edilmeli. Duvar saatinden hesaplansaydi frekans her degistiginde
        /// faz siçrar ve sonme goz onunde takilirdi. Bu test, kucuk adimlarla ilerleyen
        /// fazin surekli oldugunu - hicbir karede geriye gitmedigini veya sicramadigini -
        /// dogrular.
        /// </summary>
        [Test]
        public void Faz_Surekli_VeGeriyeGitmez()
        {
            const float sure = 22f;
            const float dt = 1f / 60f;

            float faz = 0f;
            float onceki = 0f;
            float kalan = sure;

            while (kalan > 0f)
            {
                faz = GroundBlinkClock.Advance(faz, kalan / sure, dt);

                Assert.GreaterOrEqual(faz, onceki, "Faz geriye gitmemeli.");
                Assert.Less(faz - onceki, 0.2f, "Faz tek karede sicramamali.");

                onceki = faz;
                kalan -= dt;
            }

            // 22 sn boyunca 0.8 -> 6.5 Hz arasi bir egri, egrinin integrali kadar
            // cevrim uretir. Ust ve alt siniri kaba ama anlamli tut: sabit 0.8 Hz'den
            // fazla, sabit 6.5 Hz'den az olmali.
            Assert.Greater(faz, GroundBlinkClock.SlowHz * sure,
                "Toplam cevrim sayisi, sabit yavas hizdan fazla olmali.");
            Assert.Less(faz, GroundBlinkClock.FastHz * sure,
                "Toplam cevrim sayisi, sabit hizli hizdan az olmali.");
        }

        /// <summary>
        /// Zarf: her cevrimin basinda tam parlak, sonunda tabanda; ve ASLA sifir degil.
        /// Sifir alfa "silah yok" mesajidir, "az kaldi" degil.
        /// </summary>
        [Test]
        public void Zarf_TepedenTabanaIner_AmaSifirOlmaz()
        {
            Assert.AreEqual(1f, GroundBlinkClock.Intensity01(0f), 0.001f, "Cevrim basi tam parlak.");
            Assert.AreEqual(1f, GroundBlinkClock.Intensity01(7f), 0.001f, "Her cevrim basi tam parlak.");

            float cevrimSonu = GroundBlinkClock.Intensity01(0.999f);
            Assert.Less(cevrimSonu, 0.35f, "Cevrim sonu belirgin sekilde sonmus olmali.");

            for (float faz = 0f; faz < 3f; faz += 0.017f)
            {
                float i = GroundBlinkClock.Intensity01(faz);
                Assert.GreaterOrEqual(i, GroundBlinkClock.Floor - 0.001f, "Zarf tabanin altina inmemeli.");
                Assert.LessOrEqual(i, 1.001f, "Zarf 1'i asmamali.");
            }
        }

        /// <summary>
        /// WP-069 B4 (DEC-097): aktif süre artık silahın süre TAVANI (Raiju 40 s, Namekuji 45 s; cephane önce biterse
        /// o bitirir). REQ-058 son saniye uyarısı tavana bağlı kalır; 11 s yalnız tanımı olmayan Special'ın yedeği.
        /// </summary>
        [Test]
        public void AktifSure_TavanOldu_SonSaniyeUyarisiDokunulmamis()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.AreEqual(11f, s.ActiveDuration, 0.0001f, "tanımsız yedek");
            Assert.AreEqual(3f, s.FinalSecondsWarning, 0.0001f, "Son saniye penceresi degismedi.");
            foreach (SpecialWeaponId id in SpecialWeaponConfigSection.All)
            {
                Assert.Less(s.FinalSecondsWarning, s.CapSeconds(id), "Uyari penceresi tavandan kisa olmali: " + id);
            }
        }

        // ============================================================ 4. yapisal koruma

        /// <summary>
        /// REQ-059. Ucuncu silahi eklemek, gelistirmelerin Special Weapon'a sizmasi icin
        /// bir kapi acmamali. <see cref="SpecialWeaponRuntime"/> hala
        /// <see cref="WeaponModifierState"/> turunden HICBIR alan tasimamali - "yok
        /// sayiyor" degil, "goremiyor".
        /// </summary>
        [Test]
        public void REQ059_SpecialWeaponRuntime_ModifierStateGoremez()
        {
            System.Reflection.FieldInfo[] fields = typeof(SpecialWeaponRuntime).GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.AreNotEqual(typeof(WeaponModifierState), fields[i].FieldType,
                    "Special Weapon, tur ici gelistirmeleri GOREMEMELI (REQ-059).");
            }

            // WP-069 B4: the Specials' landing effects obey the same rule.
            AssertNoModifierField(typeof(SpecialImpacts));
        }

        private static void AssertNoModifierField(System.Type type)
        {
            System.Reflection.FieldInfo[] fields = type.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.AreNotEqual(typeof(WeaponModifierState), fields[i].FieldType,
                    type.Name + " gelistirme durumunu goremem" + "eli (REQ-059).");
            }
        }
    }
}
