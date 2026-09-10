using DD.Prototype.Core.Config;
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
        // ============================================================ 1. kimlik

        [Test]
        public void UcSpecialWeaponTanimliVeAdlari_Sabit()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.AreEqual("Kagura Bell", s.DisplayName(SpecialWeaponId.KaguraBell));
            Assert.AreEqual("Ofuda Loom", s.DisplayName(SpecialWeaponId.OfudaLoom));
            Assert.AreEqual("Tide Jewel", s.DisplayName(SpecialWeaponId.TideJewel));
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

        /// <summary>
        /// Ucu de "daha cok mermi" olmamali. Fiil ayrimini olculebilir hale getiren sey
        /// ates hizi ve etki yaricapinin birbirinden ayri olmasi: ayni sayilarsa ayni
        /// silahtir.
        /// </summary>
        [Test]
        public void UcSilahin_AtesHizlariBirbirindenFarkli()
        {
            var s = new SpecialWeaponConfigSection();

            float bell = s.Common(SpecialWeaponId.KaguraBell).FireRate;
            float loom = s.Common(SpecialWeaponId.OfudaLoom).FireRate;
            float jewel = s.Common(SpecialWeaponId.TideJewel).FireRate;

            Assert.Greater(Mathf.Abs(bell - loom), 0.5f, "Zil ile dokuma ayni tempoda olmamali.");
            Assert.Greater(Mathf.Abs(bell - jewel), 0.5f, "Zil ile mucevher ayni tempoda olmamali.");
            Assert.Greater(Mathf.Abs(loom - jewel), 0.5f, "Dokuma ile mucevher ayni tempoda olmamali.");
        }

        /// <summary>
        /// Kagura Bell yasadigi tek sey: KALABALIGI ITMEK. Bu yuzden tepki kuvveti
        /// dusmanin 4.5 birimlik geri itme esiginin uzerinde olmali - altinda kalirsa
        /// silah sadece bir alan hasari olur ve fiil kaybolur.
        /// </summary>
        [Test]
        public void KaguraBell_GeriItmeEsiginiAsar()
        {
            var s = new SpecialWeaponConfigSection();
            Assert.Greater(s.KaguraBell.Common.ImpactForce, 4.5f,
                "Can 4.5 = dusmanin geri itme kapisi. Altinda kalirsa 'supurme' fiili yok olur.");
        }

        /// <summary>
        /// Ofuda Loom'un darti KASITLI olarak zayif: 2.4 sersemletme esiginin altinda
        /// kalmali, yoksa muhur asamasi zaten odul gibi hissettirir ve "bagla sonra
        /// harca" dongusu anlamsizlasir.
        /// </summary>
        [Test]
        public void OfudaLoom_DartiSersemletmeEsiginin_Altinda()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.Less(s.OfudaLoom.Common.ImpactForce, 2.4f,
                "Talisman darti sersemletmemeli.");
            Assert.Greater(s.OfudaLoom.CollapseImpactForce, 2.4f,
                "Ag cokusu sersemletmeli.");
            Assert.Less(s.OfudaLoom.CollapseImpactForce, 4.5f,
                "Ag cokusu dagitmamali - 'bagla' fiili 'savur' fiiline donusmemeli.");
        }

        /// <summary>
        /// Cokus, dartin cok uzerinde olmali; aksi halde oyuncunun ag kurmak icin
        /// harcadigi zamanin karsiligi yok demektir.
        /// </summary>
        [Test]
        public void OfudaLoom_CokusHasari_DartinCokUzerinde()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.Greater(s.OfudaLoom.CollapseDamage, s.OfudaLoom.Common.Damage * 3f,
                "Ag cokusu, tek dartin en az 3 katini vermeli.");
            Assert.GreaterOrEqual(s.OfudaLoom.MinSealsToCollapse, 2,
                "Tek muhurluk bir ag kazara harcanabilmemeli.");
            Assert.Greater(s.OfudaLoom.SealArmSeconds, 0f,
                "Taze muhur, ayni saniye icinde patlatilamamali.");
        }

        /// <summary>
        /// Tide Jewel'in cekim vurusu geri itme esigini asmali, YOKSA 'topla' fiili
        /// hicbir sey yapmaz. Ayrica nabiz araligi dusmanin 1.1 sn sersemletme
        /// bekleme suresinden kisa olmali ki her nabiz bosa gitmesin, ama o kadar da
        /// kisa olmamali ki REQ-124 stun-lock kapisini zorlasin.
        /// </summary>
        [Test]
        public void TideJewel_CekimGercektenCeker()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.Greater(s.TideJewel.Common.ImpactForce, 4.5f,
                "Cekim, geri itme esigini asmali; asmazsa kimse yerinden oynamaz.");
            Assert.Greater(s.TideJewel.PullInterval, 0.3f,
                "Cok sik nabiz sadece butce harcar - dusman zaten 1.1 sn sersemletme bekler.");
            Assert.LessOrEqual(s.TideJewel.MaxWells, 3,
                "Ayni anda cok kuyu, kalabalik butcesini bu silaha kaptirir.");
        }

        [Test]
        public void RollWeapon_UcununDeCikisiMumkun_VeSinirlardaCokmez()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.AreEqual(SpecialWeaponId.KaguraBell, s.RollWeapon(0f));
            Assert.AreEqual(SpecialWeaponId.TideJewel, s.RollWeapon(1f));
            Assert.AreEqual(SpecialWeaponId.OfudaLoom, s.RollWeapon(0.5f));

            // Sinir disi girdiler kirpilmali, istisna atmamali.
            Assert.AreEqual(SpecialWeaponId.KaguraBell, s.RollWeapon(-5f));
            Assert.AreEqual(SpecialWeaponId.TideJewel, s.RollWeapon(5f));
        }

        [Test]
        public void RollWeapon_TumAgirliklarSifirsa_CokmezVeBirSilahDoner()
        {
            var s = new SpecialWeaponConfigSection();
            s.KaguraBell.Common.SpawnWeight = 0f;
            s.OfudaLoom.Common.SpawnWeight = 0f;
            s.TideJewel.Common.SpawnWeight = 0f;

            Assert.AreEqual(SpecialWeaponId.KaguraBell, s.RollWeapon(0.5f),
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
        /// Aktif sure sayaci DEGISMEDI. Kullanici yalnizca YERDE kalma suresinden
        /// sikayet etti; aktif sure farkli bir soruya cevap veriyor ve REQ-058 son
        /// saniye alarmiyla zaten dolu. Bu test o karari kilitler.
        /// </summary>
        [Test]
        public void AktifSure_HalaSayisal_SonSaniyeUyarisiDokunulmamis()
        {
            var s = new SpecialWeaponConfigSection();

            Assert.AreEqual(11f, s.ActiveDuration, 0.0001f, "Aktif sure degismedi.");
            Assert.AreEqual(3f, s.FinalSecondsWarning, 0.0001f, "Son saniye penceresi degismedi.");
            Assert.Less(s.FinalSecondsWarning, s.ActiveDuration,
                "Uyari penceresi aktif sureden kisa olmali.");
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

            // Alan tasiyan yardimci nesneler de ayni kurala tabi.
            AssertNoModifierField(typeof(KaguraWaveField));
            AssertNoModifierField(typeof(OfudaSealWeb));
            AssertNoModifierField(typeof(TideWell));
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

        /// <summary>
        /// Kagura dalgasinin hasar cephesi ile ekrandaki halkanin AYNI egriyi kullandigini
        /// dogrular. Ikisi ayrisirsa, govdeler halka onlara varmadan once ucar ve oyuncu
        /// bunu fark eder.
        /// </summary>
        [Test]
        public void KaguraDalgasi_CepheEgrisi_HalkaylaAyni()
        {
            const float yaricap = 9.5f;
            const float sure = 0.34f;

            // ShockRingEffect'in kullandigi ease-out ile birebir ayni ifade.
            for (int i = 0; i <= 10; i++)
            {
                float t = i / 10f;
                float beklenen = yaricap * (1f - (1f - t) * (1f - t));
                float gercek = KaguraWaveField.FrontRadius(yaricap, sure, t * sure);

                Assert.AreEqual(beklenen, gercek, 0.001f,
                    "Hasar cephesi ile gorsel halka ayni egride olmali.");
            }
        }

        [Test]
        public void KaguraDalgasi_CepheHicGerileMez()
        {
            float onceki = -1f;

            for (float t = 0f; t <= 0.4f; t += 0.005f)
            {
                float r = KaguraWaveField.FrontRadius(9.5f, 0.34f, t);
                Assert.GreaterOrEqual(r, onceki, "Cephe geriye gitmemeli.");
                onceki = r;
            }

            Assert.AreEqual(9.5f, KaguraWaveField.FrontRadius(9.5f, 0.34f, 0.34f), 0.001f,
                "Cephe tam surede tam yaricapa varmali.");
        }
    }
}
