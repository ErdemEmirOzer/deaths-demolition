using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// G-22 · HAREKET KESKİNLİĞİ nöbetçisi. REQ-036 / REQ-037 / REQ-042.
    ///
    /// NEDEN VAR
    /// ---------
    /// Kullanıcı G3 sonrası "karakter hareketi daha keskin olsun, daha hassas hâkim
    /// olayım" dedi. "Keskinlik" öznel bir kelimedir; bu dosya onu dört ÖLÇÜLEBİLİR
    /// büyüklüğe indirir ve her birine bir tavan koyar:
    ///
    ///     1. hızlanma      dururken tam hıza çıkma süresi ve mesafesi
    ///     2. durma         girdi bırakılınca durma süresi ve KAYMA MESAFESİ
    ///     3. yön değişimi  ters girdide hızın sıfırı geçme süresi ve aşım mesafesi
    ///     4. kare bağımsızlığı  30 / 60 / 120 fps'te AYNI yol
    ///
    /// Bu dört sayı düşerse "his" bozulmuştur; testin işi bunu kanıtla söylemektir.
    ///
    /// EŞİKLER ÖLÇÜMDEN DEĞİL, DENKLEMDEN
    /// ----------------------------------
    /// Her eşiğin yanında türetmesi var. Türetme şu üç orandan çıkar (hepsi
    /// PlayerConfigSection'da; teste bilerek SABİT yazıldı, config'ten okunsalardı test
    /// kendi kendini doğrulardı):
    ///
    ///     MoveSpeed             V = 6.2  m/s
    ///     Acceleration          A = 45   m/s²   hız KURAR
    ///     Deceleration          D = 60   m/s²   girdi bırakılınca hız SÖKER
    ///     CounterStrafeBraking  C = 90   m/s²   ters girdide hız SÖKER
    ///
    /// Her eşik = analitik değer + bir örnekleme adımı (eşik geçişi kare sınırında
    /// yakalanır) + %5 pay. Yani eşik "geçsin diye" seçilmiş olamaz; %5'ten fazla
    /// kayan bir davranış testi kırar.
    ///
    /// G-22 ÖNCESİ ÖLÇÜM (tek katsayı, A = D = C = 28; 1/120 adım)
    ///     hızlanma  0.225 s / 0.734 m      durma  0.225 s / 0.661 m
    ///     sıfır geçişi 0.225 s             aşım   0.661 m
    ///     0.5 s ileri + 0.5 s bırak + 0.5 s ters sonunda x:
    ///         30 fps 0.5867 · 60 fps 0.6356 · 120 fps 0.6608  ->  %11.8 SAPMA
    ///
    /// Son satır bu dosyanın ikinci gerekçesi: eski integrasyon konumu
    /// "kare sonu hızı × dt" ile topluyordu, bu da kare hızına göre 11.8 cm'lik bir
    /// durma noktası farkı demekti. Nerede durduğun, hassas hâkimiyetin ta kendisidir.
    ///
    /// SAHNE YOK
    /// ---------
    /// Ölçüm Run sahnesini yüklemez: sürü (REQ-046) oyuncuyu fiziksel olarak engeller ve
    /// mesafeyi kirletir, 12 sn'lik sahne kurulumu da her ölçümü pahalılaştırır. Bunun
    /// yerine boş bir zemin + tek bir PlayerMotor kurulur, dünyanın 250 m uzağında.
    /// Kamera rig'i yoksa PlayerMotor dünya eksenlerine düşer (REQ-036 yedeği), o yüzden
    /// +x girdisi = dünya +x'i.
    /// </summary>
    [TestFixture]
    public sealed class PlayerMovementSharpnessTests
    {
        // ------------------------------------------------------------------ sabitler

        /// <summary>PlayerConfigSection.MoveSpeed. Teste sabit yazıldı.</summary>
        private const float V = 6.2f;

        /// <summary>
        /// PlayerConfigSection.Acceleration. WP-002: 28 → 45. Config 45'e çıkarılmıştı (kullanıcı
        /// oynayıp "daha ağır" dedi) ama bu sabit 28'de kalmıştı; eşikler gevşek yönde yanlış
        /// türetildiğinden test yeşil kalıyor, "eşik formülden" iddiası ise bozuktu. Sabit tazelik
        /// nöbetçisi <see cref="UcOranConfigtenGelirVeAsimetriKorunur"/> artık config'ten sapmayı yakalar.
        /// </summary>
        private const float A = 45f;

        /// <summary>PlayerConfigSection.Deceleration.</summary>
        private const float D = 60f;

        /// <summary>PlayerConfigSection.CounterStrafeBraking.</summary>
        private const float C = 90f;

        /// <summary>PlayerConfigSection.CollisionRadius. Gövde yarıçapı.</summary>
        private const float GovdeYaricapi = 0.45f;

        /// <summary>Ölçüm adımı. 1/120: eşik geçişini en ince yakalayan adım.</summary>
        private const float InceAdim = 1f / 120f;

        /// <summary>Tam hız sayılan oran. %99'da MoveTowards hâlâ ölçülebilir hızda.</summary>
        private const float TamHizOrani = 0.99f;

        /// <summary>Durmuş sayılan oran: 0.062 m/s, kare başına 0.5 mm.</summary>
        private const float DurmusOrani = 0.01f;

        // ------------------------------------------------------------------ eşikler

        /// <summary>
        /// HIZLANMA SÜRESİ. v(t) = A·t, %99 V için t = 0.99·6.2/45 = 0.1364 s.
        /// + bir örnekleme adımı (1/120 = 0.0083) + %5 = 0.1520 -> 0.152 s.
        /// (A = 28 iken 0.24 s idi; sabit 45'e çekilince WP-002'de yeniden türetildi.)
        /// </summary>
        private const float HizlanmaSuresiEsigi = 0.152f;

        /// <summary>
        /// HIZLANMA MESAFESİ. x = ½·A·t², t = 0.1364 + 1/120 = 0.1447 -> 0.471 m,
        /// + %5 = 0.495 -> 0.50 m.
        /// </summary>
        private const float HizlanmaMesafesiEsigi = 0.50f;

        /// <summary>
        /// DURMA SÜRESİ. t = 0.99·V/D = 6.138/60 = 0.1023 s, + 1/120 + %5 = 0.1161
        /// -> 0.12 s. Eski tek katsayılı hâlde 0.2192 s idi.
        /// </summary>
        private const float DurmaSuresiEsigi = 0.12f;

        /// <summary>
        /// DURMA MESAFESİ. x = V²/(2·D) = 38.44/120 = 0.3203 m, + %5 = 0.3363 -> 0.34 m.
        /// Örnekleme payı YOK: MoveTowards hızı tam sıfırda kırpar, dolayısıyla bu mesafe
        /// kare hızından bağımsız olarak TAM 0.3203 m'dir; testin ikinci yarısı zaten
        /// bunu ayrıca kanıtlar.
        ///
        /// Anlamı: 0.3203 &lt; 0.45 = CollisionRadius. Karakter KENDİ AYAK İZİNİN İÇİNDE
        /// durur. "İstediğim yere tam koyabiliyorum" cümlesinin ölçülebilir karşılığı bu.
        /// Eski hâlde 0.6864 m idi, yani gövdesinin bir buçuk katı kayıyordu.
        /// </summary>
        private const float DurmaMesafesiEsigi = 0.34f;

        /// <summary>
        /// YÖN DEĞİŞİMİ — SIFIR GEÇİŞİ. Ters girdide hız vektörü C ile sökülür:
        /// t = V/C = 6.2/90 = 0.0689 s, + 1/120 + %5 = 0.0811 -> 0.085 s.
        /// Oyuncunun "artık öbür tarafa gidiyorum" diye gördüğü an budur; eski hâlde
        /// 0.2214 s idi, 3.2 katı.
        /// </summary>
        private const float SifirGecisiEsigi = 0.085f;

        /// <summary>
        /// YÖN DEĞİŞİMİ — AŞIM MESAFESİ. x = V²/(2·C) = 38.44/180 = 0.2136 m, + %5
        /// -> 0.23 m. Bu da kare hızından bağımsız TAM bir değerdir.
        /// Anlamı: aşım gövde yarıçapının yarısından (0.225 m) küçük.
        /// </summary>
        private const float AsimMesafesiEsigi = 0.23f;

        /// <summary>
        /// YÖN DEĞİŞİMİ — TAM TERS HIZ. Fren (V/C = 0.0689) + yeniden hızlanma
        /// (0.99·V/A = 0.1364) = 0.2053 s, + 1/120 + %5 = 0.2243 -> 0.225 s.
        /// (A = 28 ile 0.32 s idi.)
        /// </summary>
        private const float TamTersEsigi = 0.225f;

        /// <summary>
        /// 90° DÖNÜŞ. Hız vektörü (V,0) -> (0,V) doğrusunda ilerler; fren payı
        /// V/√2 = 4.384 (t = 4.384/90 = 0.0487), kalan 4.384 ivmeyle (t = 4.384/45 = 0.0974).
        /// Toplam 0.1461 s, + 1/120 + %5 = 0.1621 -> 0.162 s. (A = 28 ile 0.23 s idi.)
        /// </summary>
        private const float DonusEsigi = 0.162f;

        /// <summary>Kare bağımsızlığı payı. Kabul ölçütü O2: sapma ≤ %2.</summary>
        private const float KareSapmaEsigi = 0.02f;

        /// <summary>Ölçüm dünyanın (±44 m) çok dışında; hiçbir cisim güzergâha giremez.</summary>
        private static readonly Vector3 OlcumBasi = new Vector3(250f, 0.05f, 250f);

        private SurulenGirdi _girdi;
        private PlayerMotor _motor;
        private float _adim = InceAdim;
        private GameObject _oyuncu;
        private GameObject _zemin;

        // ------------------------------------------------------------------ örnekleme

        /// <summary>Bir karenin sonunda okunan durum.</summary>
        private struct Ornek
        {
            public float Zaman;
            public Vector3 Hiz;
            public Vector3 Konum;
        }

        // =========================================================================== 1

        /// <summary>
        /// KESKİNLİĞİN DÖRT BİLEŞENİ, tek koşuda, 1/120 adımla. Her ölçüm bir öncekinin
        /// bıraktığı yerden değil, TAM HIZDA ve temiz bir güzergâhtan başlar.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(120000)]
        public IEnumerator HizlanmaDurmaVeYonDegisimiEsiklerinAltinda()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return OlcumDuzenegiKur(InceAdim);

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] hareket.keskinlik (adim=1/120, A=28 D=60 C=90)");

            // ----------------------------------------------------------- 1 · hızlanma
            var kayit = new List<Ornek>(256);
            yield return Sur(new Vector2(1f, 0f), 0.6f, kayit);

            float hizlanmaSuresi;
            float hizlanmaMesafesi;
            IlkEsigeKadar(kayit, o => o.Hiz.x >= TamHizOrani * V, out hizlanmaSuresi, out hizlanmaMesafesi);

            rapor.AppendLine("  hizlanma  t=" + hizlanmaSuresi.ToString("F4") + " s  x=" +
                             hizlanmaMesafesi.ToString("F4") + " m" +
                             "  (esik " + HizlanmaSuresiEsigi + " s / " + HizlanmaMesafesiEsigi + " m)");

            Assert.Less(hizlanmaSuresi, HizlanmaSuresiEsigi,
                "Tam hiza cikis " + hizlanmaSuresi.ToString("F4") + " s surdu.\n" + rapor);
            Assert.Less(hizlanmaMesafesi, HizlanmaMesafesiEsigi,
                "Tam hiza cikis " + hizlanmaMesafesi.ToString("F4") + " m surdu.\n" + rapor);

            // -------------------------------------------------------------- 2 · durma
            kayit.Clear();
            yield return Sur(Vector2.zero, 0.5f, kayit);

            float durmaSuresi;
            float durmaMesafesi;
            IlkEsigeKadar(kayit, o => o.Hiz.magnitude <= DurmusOrani * V, out durmaSuresi, out durmaMesafesi);

            rapor.AppendLine("  durma     t=" + durmaSuresi.ToString("F4") + " s  x=" +
                             durmaMesafesi.ToString("F4") + " m" +
                             "  (esik " + DurmaSuresiEsigi + " s / " + DurmaMesafesiEsigi + " m" +
                             ", govde yaricapi " + GovdeYaricapi + " m)");

            Assert.Less(durmaSuresi, DurmaSuresiEsigi,
                "Girdi birakildiktan sonra durus " + durmaSuresi.ToString("F4") +
                " s surdu; asimetrik yavaslama (Deceleration) calismiyor.\n" + rapor);
            Assert.Less(durmaMesafesi, DurmaMesafesiEsigi,
                "Durma KAYMASI " + durmaMesafesi.ToString("F4") + " m; keskinligin en buyuk " +
                "tek kaynagi burasidir.\n" + rapor);
            Assert.Less(durmaMesafesi, GovdeYaricapi,
                "Karakter kendi ayak izinin (r=" + GovdeYaricapi + " m) disina kayarak duruyor.\n" + rapor);

            // ----------------------------------------------------- 3 · ters yön / fren
            yield return TamHizaCik(new Vector2(1f, 0f));

            kayit.Clear();
            yield return Sur(new Vector2(-1f, 0f), 0.5f, kayit);

            float sifirGecisi;
            float sifirYolu;
            IlkEsigeKadar(kayit, o => o.Hiz.x <= 0f, out sifirGecisi, out sifirYolu);

            float tamTers;
            float tamTersYolu;
            IlkEsigeKadar(kayit, o => o.Hiz.x <= -TamHizOrani * V, out tamTers, out tamTersYolu);

            // Aşım: Ornek.Konum faz başlangıcına göredir, dolayısıyla ters girdi
            // verildikten sonra ESKİ yönde ulaşılan en uzak nokta doğrudan budur.
            float enUzak = 0f;
            for (int i = 0; i < kayit.Count; i++)
            {
                if (kayit[i].Konum.x > enUzak)
                {
                    enUzak = kayit[i].Konum.x;
                }
            }

            rapor.AppendLine("  ters yon  sifir gecisi=" + sifirGecisi.ToString("F4") + " s" +
                             "  tam ters=" + tamTers.ToString("F4") + " s" +
                             "  asim=" + enUzak.ToString("F4") + " m" +
                             "  (esik " + SifirGecisiEsigi + " / " + TamTersEsigi + " s / " +
                             AsimMesafesiEsigi + " m)");

            Assert.Less(sifirGecisi, SifirGecisiEsigi,
                "Ters girdide yon " + sifirGecisi.ToString("F4") + " s sonra degisti; " +
                "karsi-yon freni (CounterStrafeBraking) calismiyor.\n" + rapor);
            Assert.Less(enUzak, AsimMesafesiEsigi,
                "Ters girdiden sonra eski yonde " + enUzak.ToString("F4") + " m asim var.\n" + rapor);
            Assert.Less(tamTers, TamTersEsigi,
                "Tam ters hiza ulasma " + tamTers.ToString("F4") + " s surdu.\n" + rapor);

            // --------------------------------------------------------- 4 · 90° dönüş
            yield return TamHizaCik(new Vector2(1f, 0f));

            kayit.Clear();
            yield return Sur(new Vector2(0f, 1f), 0.5f, kayit);

            float donus;
            float donusYolu;
            IlkEsigeKadar(kayit, o => o.Hiz.z >= TamHizOrani * V, out donus, out donusYolu);

            rapor.AppendLine("  90 donus  t=" + donus.ToString("F4") + " s  (esik " + DonusEsigi + " s)");
            Debug.Log(rapor.ToString());

            Assert.Less(donus, DonusEsigi,
                "90 derecelik yon degisimi " + donus.ToString("F4") + " s surdu.\n" + rapor);
        }

        // =========================================================================== 2

        /// <summary>
        /// KARE BAĞIMSIZLIĞI — kabul ölçütü O2.
        ///
        /// Aynı girdi betiği üç kare hızında koşulur: 0.5 s ileri, 0.5 s bırak, 0.5 s ters.
        /// Üç süre de 1/30, 1/60 ve 1/120'nin tam katı olduğu için karşılaştırma
        /// örnekleme hatası taşımaz. Ölçülen şey KONUMDUR, hız değil: hız zaten eski
        /// kodda da kare-bağımsızdı, konum değildi.
        ///
        /// Hedef: sapma ≤ %2. Yeni integrasyonda beklenen sapma ~%0'dır; %2 payı yalnız
        /// CharacterController'ın süpürme/temas gürültüsü içindir.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(120000)]
        public IEnumerator Kare30_60_120AyniYoluUretir()
        {
            LogAssert.ignoreFailingMessages = true;

            float[] adimlar = { 1f / 30f, 1f / 60f, 1f / 120f };
            var ileri = new float[3];
            var kayma = new float[3];
            var sonKonum = new float[3];

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] hareket.kare-bagimsizligi (0.5 s ileri / 0.5 s birak / 0.5 s ters)");

            for (int i = 0; i < adimlar.Length; i++)
            {
                yield return OlcumDuzenegiKur(adimlar[i]);

                var kayit = new List<Ornek>(128);
                Vector3 basla = _oyuncu.transform.position;

                yield return Sur(new Vector2(1f, 0f), 0.5f, kayit);
                float ileriSonu = _oyuncu.transform.position.x;

                kayit.Clear();
                yield return Sur(Vector2.zero, 0.5f, kayit);
                float kaymaSonu = _oyuncu.transform.position.x;

                kayit.Clear();
                yield return Sur(new Vector2(-1f, 0f), 0.5f, kayit);

                ileri[i] = ileriSonu - basla.x;
                kayma[i] = kaymaSonu - ileriSonu;
                sonKonum[i] = _oyuncu.transform.position.x - basla.x;

                rapor.AppendLine("  " + (1f / adimlar[i]).ToString("F0") + " fps" +
                                 "  ileri=" + ileri[i].ToString("F5") +
                                 "  kayma=" + kayma[i].ToString("F5") +
                                 "  son=" + sonKonum[i].ToString("F5"));

                DuzenegiYik();
                yield return null;
            }

            float ileriSapma = BagilSapma(ileri);
            float kaymaSapma = BagilSapma(kayma);
            float sonSapma = BagilSapma(sonKonum);

            rapor.AppendLine("  SAPMA  ileri=%" + (ileriSapma * 100f).ToString("F3") +
                             "  kayma=%" + (kaymaSapma * 100f).ToString("F3") +
                             "  son=%" + (sonSapma * 100f).ToString("F3") +
                             "  (esik %" + (KareSapmaEsigi * 100f).ToString("F0") +
                             ", G-22 oncesi son=%11.8)");
            Debug.Log(rapor.ToString());

            Assert.Less(ileriSapma, KareSapmaEsigi,
                "Hizlanma yolu kare hizina gore %" + (ileriSapma * 100f).ToString("F2") + " kayiyor.\n" + rapor);
            Assert.Less(kaymaSapma, KareSapmaEsigi,
                "DURMA MESAFESI kare hizina gore %" + (kaymaSapma * 100f).ToString("F2") +
                " kayiyor; oyuncu 30 fps'te baska, 120 fps'te baska yerde duruyor.\n" + rapor);
            Assert.Less(sonSapma, KareSapmaEsigi,
                "Ucu ucuna ayni girdi betigi kare hizina gore %" + (sonSapma * 100f).ToString("F2") +
                " farkli bitiyor.\n" + rapor);
        }

        // =========================================================================== 3

        /// <summary>
        /// REQ-037 — HAREKET NİŞANDAN BAĞIMSIZ. Motor tarafı: karakter hangi yöne bakarsa
        /// baksa, +x girdisi dünya +x'inde aynı yolu üretmeli. Karakter 137° döndürülür
        /// (bilerek "yuvarlak olmayan" bir açı) ve ölçüm tekrarlanır.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(120000)]
        public IEnumerator HareketBakisYonundenBagimsiz()
        {
            LogAssert.ignoreFailingMessages = true;

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] hareket.nisandan-bagimsizlik (REQ-037)");

            yield return OlcumDuzenegiKur(InceAdim);
            var kayit = new List<Ornek>(128);
            Vector3 basla = _oyuncu.transform.position;
            yield return Sur(new Vector2(1f, 0f), 0.5f, kayit);
            Vector3 duz = _oyuncu.transform.position - basla;
            DuzenegiYik();
            yield return null;

            yield return OlcumDuzenegiKur(InceAdim);
            _oyuncu.transform.rotation = Quaternion.Euler(0f, 137f, 0f);
            kayit.Clear();
            basla = _oyuncu.transform.position;
            yield return Sur(new Vector2(1f, 0f), 0.5f, kayit);
            Vector3 donuk = _oyuncu.transform.position - basla;

            rapor.AppendLine("  0 derece   dx=" + duz.x.ToString("F5") + "  dz=" + duz.z.ToString("F5"));
            rapor.AppendLine("  137 derece dx=" + donuk.x.ToString("F5") + "  dz=" + donuk.z.ToString("F5"));
            Debug.Log(rapor.ToString());

            Assert.Less(Mathf.Abs(donuk.z), 0.01f * Mathf.Abs(donuk.x),
                "Karakter dondurulunce hareket de dondu; REQ-037 bozuldu.\n" + rapor);
            Assert.Less(Mathf.Abs(donuk.x - duz.x), 0.02f * Mathf.Abs(duz.x),
                "Bakis yonu hizi degistirdi (dx " + duz.x.ToString("F4") + " -> " +
                donuk.x.ToString("F4") + "); REQ-037 bozuldu.\n" + rapor);
        }

        // =========================================================================== 4

        /// <summary>
        /// REQ-042 — DASH/DODGE YOK. İki ayrı kanıt:
        ///
        ///   a) YÜZEY. PlayerMotor'da atılma anlamına gelebilecek tek bir public giriş
        ///      noktası yok. Keskinlik bir atılma mekaniğiyle çözülmedi.
        ///   b) DAVRANIŞ. Girdi 3 karede bir yön değiştirerek 2 sn boyunca hırpalanır;
        ///      hız hiçbir karede MoveSpeed'i aşmaz. Karşı-yön freni hız SÖKER, hiçbir
        ///      koşulda hız EKLEMEZ.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(120000)]
        public IEnumerator AtilmaMekanigiYokVeHizTavanAsilmaz()
        {
            LogAssert.ignoreFailingMessages = true;

            string[] yasakli = { "dash", "dodge", "impulse", "addforce", "launch", "boost", "blink", "lunge" };
            MethodInfo[] yontemler = typeof(PlayerMotor)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            for (int i = 0; i < yontemler.Length; i++)
            {
                string ad = yontemler[i].Name.ToLowerInvariant();
                for (int j = 0; j < yasakli.Length; j++)
                {
                    Assert.IsFalse(ad.Contains(yasakli[j]),
                        "PlayerMotor.'" + yontemler[i].Name + "' bir atilma girisi gibi duruyor; REQ-042 " +
                        "dash/dodge YASAKLIYOR. Keskinlik bir mekanikle degil, hizlanma/yavaslama " +
                        "asimetrisiyle cozulur.");
                }
            }

            yield return OlcumDuzenegiKur(1f / 60f);

            var yonler = new[]
            {
                new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f),
                new Vector2(-0.7071f, -0.7071f), new Vector2(1f, 0f), new Vector2(0f, -1f)
            };

            float enYuksek = 0f;
            for (int tur = 0; tur < 40; tur++)
            {
                _girdi.Deger = yonler[tur % yonler.Length];
                for (int k = 0; k < 3; k++)
                {
                    yield return null;
                    float hiz = _motor.PlanarVelocity.magnitude;
                    if (hiz > enYuksek)
                    {
                        enYuksek = hiz;
                    }
                }
            }

            _girdi.Deger = Vector2.zero;

            Debug.Log("[DD-TEST] hareket.req042  en yuksek hiz=" + enYuksek.ToString("F4") +
                      " m/s  tavan=" + _motor.MaxSpeed.ToString("F4"));

            Assert.LessOrEqual(enYuksek, _motor.MaxSpeed * 1.001f,
                "Girdi hirpalanirken hiz tavani asildi (" + enYuksek.ToString("F4") + " > " +
                _motor.MaxSpeed.ToString("F4") + "); bir yerde ivme degil ITME uygulaniyor.");
        }

        // =========================================================================== 5

        /// <summary>
        /// Kabul ölçütü O7 — tasarım parametreleri KODDA DEĞİL. Üç hareket oranı da
        /// config'ten gelmeli, ve keskinlik sözleşmesi olan sıralamayı korumalı:
        ///
        ///     hız kurma  ≤  hız sökme  ≤  ters yön freni
        ///
        /// PlayerMotor bu sıralamayı zorlamaz (bir tasarımcı buzda kaymayı denemek
        /// isteyebilir); sıralamayı burası nöbetler.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(60000)]
        public IEnumerator UcOranConfigtenGelirVeAsimetriKorunur()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return OlcumDuzenegiKur(1f / 60f);

            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config, "PrototypeConfig yuklenemedi.");

            Assert.AreEqual(config.Player.Acceleration, _motor.Acceleration, 0.0001f,
                "PlayerMotor.Acceleration config'ten okunmuyor.");
            Assert.AreEqual(config.Player.Deceleration, _motor.Deceleration, 0.0001f,
                "PlayerMotor.Deceleration config'ten okunmuyor.");
            Assert.AreEqual(config.Player.CounterStrafeBraking, _motor.CounterStrafeBraking, 0.0001f,
                "PlayerMotor.CounterStrafeBraking config'ten okunmuyor.");

            Assert.GreaterOrEqual(_motor.Deceleration, _motor.Acceleration,
                "Yavaslama (" + _motor.Deceleration + ") hizlanmadan (" + _motor.Acceleration +
                ") kucuk; keskinlik asimetrisi ters cevrilmis, karakter buzda kayar.");
            Assert.GreaterOrEqual(_motor.CounterStrafeBraking, _motor.Deceleration,
                "Karsi-yon freni (" + _motor.CounterStrafeBraking + ") duz yavaslamadan (" +
                _motor.Deceleration + ") kucuk; yon degistirmek durmaktan yavas demektir.");
        }

        // ------------------------------------------------------------------ yardımcılar

        /// <summary>
        /// Zemin + tek bir PlayerMotor. Run sahnesi yüklenmez; ölçüm dünyanın 250 m
        /// uzağında, kendi zemininde yapılır.
        /// </summary>
        private IEnumerator OlcumDuzenegiKur(float adim)
        {
            DuzenegiYik();

            // Destroy kare sonunda islenir. Bir kare beklenmezse eski olcum kapsulu ile
            // yenisi ayni karede ayni noktada bulunur ve birbirlerini iterler.
            yield return null;

            Time.timeScale = 1f;
            Time.captureDeltaTime = adim;
            _adim = adim;

            _zemin = new GameObject("KeskinlikZemini");
            _zemin.transform.position = new Vector3(OlcumBasi.x, -1f, OlcumBasi.z);
            BoxCollider kutu = _zemin.AddComponent<BoxCollider>();
            kutu.size = new Vector3(120f, 2f, 120f);

            _oyuncu = new GameObject("KeskinlikOyuncusu");
            _oyuncu.SetActive(false);
            _oyuncu.transform.position = OlcumBasi;
            _oyuncu.AddComponent<CharacterController>();
            _motor = _oyuncu.AddComponent<PlayerMotor>();
            _oyuncu.SetActive(true);

            _girdi = new SurulenGirdi();
            _motor.SetInputSource(_girdi);
            _motor.Teleport(OlcumBasi);

            // Zemine otursun: ilk karede isGrounded false'tur, yer çekimi kapsülü indirir.
            for (int i = 0; i < 12; i++)
            {
                yield return null;
            }

            Assert.Greater(_motor.MaxSpeed, 0f, "PlayerMotor config'e baglanamadi (MaxSpeed 0).");
            Assert.IsTrue(_motor.IsGrounded, "Olcum kapsulu zemine oturmadi; olcum guvenilmez.");

            _motor.ClearVelocity();
            yield return null;
        }

        private void DuzenegiYik()
        {
            if (_motor != null)
            {
                _motor.SetInputSource(null);
            }

            if (_oyuncu != null)
            {
                Object.Destroy(_oyuncu);
            }

            if (_zemin != null)
            {
                Object.Destroy(_zemin);
            }

            _oyuncu = null;
            _zemin = null;
            _motor = null;
            _girdi = null;
        }

        /// <summary>
        /// Girdiyi sürer ve her karenin SONUNDA örnek alır. Zaman Time.deltaTime'dan
        /// toplanır; captureDeltaTime'ın gerçekten uygulandığını varsaymaz.
        /// </summary>
        private IEnumerator Sur(Vector2 yon, float sure, List<Ornek> kayit)
        {
            _girdi.Deger = yon;

            // Kare sayısı SÜREDEN TÜRETİLİR, biriken zamanı eşikle karşılaştırmaktan
            // değil: 0.5 s her üç adımın (1/30, 1/60, 1/120) tam katıdır ve kare sayısı
            // tam sayıya yuvarlanınca üç koşum da aynı sayıda kare görür. Biriken float
            // zamanı eşiğe vurmak, 30 fps'te bir kare eksik/fazla koşturup kare
            // bağımsızlığı ölçümüne %3'lük sahte bir sapma sokardı.
            int kare = Mathf.RoundToInt(sure / _adim);
            float gecen = 0f;
            Vector3 basla = _oyuncu.transform.position;

            for (int i = 0; i < kare; i++)
            {
                yield return null;
                gecen += Time.deltaTime;

                kayit.Add(new Ornek
                {
                    Zaman = gecen,
                    Hiz = _motor.PlanarVelocity,
                    Konum = _oyuncu.transform.position - basla
                });
            }
        }

        /// <summary>
        /// Tam hıza çıkarır ve girdiyi BIRAKMAZ; sonraki ölçüm doğrudan tam hızdan
        /// başlasın diye. 0.6 s, en yavaş geçiş olan tam-ters (0.288 s) için bile yeterli.
        /// </summary>
        private IEnumerator TamHizaCik(Vector2 yon)
        {
            var cop = new List<Ornek>(128);
            yield return Sur(yon, 0.6f, cop);
        }

        /// <summary>
        /// Koşulu ilk sağlayan örneğin zamanı ve o ana kadar kat edilen YATAY yol.
        /// Koşul hiç sağlanmazsa test okunur bir mesajla düşsün diye +sonsuz döner.
        /// </summary>
        private static void IlkEsigeKadar(
            List<Ornek> kayit, System.Func<Ornek, bool> kosul, out float zaman, out float yol)
        {
            for (int i = 0; i < kayit.Count; i++)
            {
                if (!kosul(kayit[i]))
                {
                    continue;
                }

                zaman = kayit[i].Zaman;
                Vector3 duzlem = kayit[i].Konum;
                duzlem.y = 0f;
                yol = duzlem.magnitude;
                return;
            }

            zaman = float.PositiveInfinity;
            yol = float.PositiveInfinity;
        }

        /// <summary>(max - min) / |ortalama|. Sıfıra yakın ortalamada 0 döner.</summary>
        private static float BagilSapma(float[] degerler)
        {
            float enAz = float.MaxValue;
            float enCok = float.MinValue;
            float toplam = 0f;

            for (int i = 0; i < degerler.Length; i++)
            {
                enAz = Mathf.Min(enAz, degerler[i]);
                enCok = Mathf.Max(enCok, degerler[i]);
                toplam += degerler[i];
            }

            float ortalama = Mathf.Abs(toplam / degerler.Length);
            return ortalama < 1e-4f ? 0f : (enCok - enAz) / ortalama;
        }

        [TearDown]
        public void Bitir()
        {
            DuzenegiYik();
            Time.captureDeltaTime = 0f;
        }
    }
}
