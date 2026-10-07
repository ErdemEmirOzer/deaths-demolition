using System.Collections;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// G-19 regresyon testi — "oyuncu vuruluyor ama hangi yönden vurulduğunu göremiyor".
    /// REQ-045 (hasar geri bildirimi) · REQ-044 (hasarlar üst üste biner) · REQ-104
    /// (okunabilirlik). TEST-PLANI'nın sunum ayağı.
    ///
    /// YAKALADIĞI KUSUR SINIFI
    /// -----------------------
    /// Bu projede beş kez tekrarlanan imza: BELGELENMİŞ, BAĞLANMAMIŞ.
    /// PlayerDamageFeedback'in kendi başlığı "ekran kenarı vinyeti -> HUD görevi,
    /// PlayerHealth.AnyDamaged" diye yazıyordu; kimse devralmadı. Kamera sarsıntısı
    /// vardı, ses vardı, yön yoktu. Derleme temiz, konsol sessiz, oyuncu kör.
    ///
    /// Bu test tam olarak o boşluğa nöbetçi koyar ve ÜÇ ayrı iddiayı ayrı ayrı ölçer:
    ///   1. HUD göstergeyi GERÇEKTEN kuruyor mu (sahnede başıboş bir bileşen bulup
    ///      "demek ki biri istemiş" varsaymak yerine RunHudScreen'in kendi alanına bakar)
    ///   2. Gösterge DOĞRU kenarı seçiyor mu — dört zıt yön
    ///   3. Kamera EĞİMİ hesaba katılıyor mu — bkz. TEST 3, bu testin can alıcı yeri
    ///
    /// NEDEN ApplyDamage ÇAĞIRMAK BURADA MEŞRU
    /// ---------------------------------------
    /// Ölçülen şey hasar zinciri değil, SUNUM. Zincirin kendisi
    /// EnemyDamagesPlayerTests'in nöbetinde ve orada tek bir ApplyDamage çağrısı yok.
    /// Burada yön sentetik olarak verilir, çünkü zombinin nereden geleceğini test
    /// belirleyemez; belirleyemediği bir şeyle zıt yön sınanamaz.
    ///
    /// ÖLÇÜM KOŞULU
    /// ------------
    /// Sahne kurulduktan sonra tur durdurulur ve bütün düşmanlar silinir. Bu bir kolaylık
    /// değil zorunluluk: canlı bir zombi ölçüm penceresinde kendi yönünden vurursa "zıt
    /// kenar tam olarak 0" iddiası çürütülemez hâle gelir. Adım 1/60'a sabitlenir, böylece
    /// sönüm zarfı kare hızından bağımsız olarak aynı yerde ölçülür.
    /// </summary>
    [TestFixture]
    public sealed class PlayerHitDirectionHudTests
    {
        private const float SahneKurulumSaniye = 12f;
        private const float Adim = 1f / 60f;

        /// <summary>Saldırganın oyuncudan uzaklığı. Yön dışında hiçbir şeyi etkilemez.</summary>
        private const float SaldirganMesafesi = 3f;

        /// <summary>
        /// Maksimum canın yüzdesi olarak tek vuruş. UiTokens.DamageEdgeFullDamageFraction
        /// 0.25 olduğu için 0.30 kuvveti kesin olarak 1.0'a doyurur — yani ölçülen alfa
        /// yalnız YÖN AĞIRLIĞINI taşır, hasar büyüklüğünü değil. Ayrıca vuruştan sonra
        /// oyuncu %70 canda kalır, yani düşük can terfisi (eşik 0.30) devreye GİRMEZ;
        /// iki kural aynı ölçümde karışmasın diye böyle seçildi.
        /// </summary>
        private const float VurusOrani = 0.30f;

        /// <summary>
        /// Yanan kenar için alt eşik. Tepe değer 1.0 × UiTokens.DamageEdgePeakAlpha = 0.92.
        /// WP-094 (his sözleşmesi §4.6, 08.10): zarf 0.05 sn'de doğrusal yükselir, 0.25 sn
        /// tepede kalır, 0.3 sn'de easeInQuad ile söner (ömür 0.6 sn). Ölçüm bu yüzden tek
        /// karede değil, <see cref="ZirveyiOlc"/> ile yükseliş + bekleme penceresi boyunca her
        /// kenarın en yüksek alfasıdır: bekleme 0.25 sn sürdüğü için hangi saat (gerçek ya da
        /// 1/60 adım) işlerse işlesin en az bir kare tepeye düşer. 0.60 eşiği tepe değerin
        /// altında pay bırakır ve kusurlu hâlde ölçülen 0.00 ile arasında kapanmaz boşluk vardır.
        /// </summary>
        private const float YananKenarEsigi = 0.60f;

        /// <summary>
        /// Sönük kenar için üst eşik. Düşman kalmadığı için beklenen değer TAM 0'dır;
        /// 0.05 yalnız kayan nokta payıdır, gürültü payı değil.
        /// </summary>
        private const float SonukKenarEsigi = 0.05f;

        /// <summary>Son <see cref="ZirveyiOlc"/> penceresinde her kenarın en yüksek alfası (UiScreenEdge sırası).</summary>
        private readonly float[] _zirve = new float[4];

        private PlayerHealth _can;
        private RunHudScreen _hud;
        private UiHitDirectionLayer _katman;
        private PrototypeCameraRig _kamera;

        // =========================================================================== TEST 1

        /// <summary>
        /// DÖRT ZIT YÖN. Her biri için: doğru kenar yanıyor mu, karşı kenar sönük mü.
        ///
        /// Yönler dünya eksenlerinden DEĞİL, kameranın kendi tabanından türetilir:
        ///   ekran sağı  = kameranın sağ vektörü (eğim yalpayı değil, sağ vektörü hiç
        ///                 döndürmez; test bunu ayrıca ölçer)
        ///   ekran üstü  = rig.PlanarForward — kamera aşağı baktığı için ondan uzaklaşan
        ///                 nokta ekranda YUKARI çıkar
        /// Beklenti bu yüzden ölçülen kodun içinden değil, kameranın anlamından gelir.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator HasarGeldigindeDogruEkranKenariYanar()
        {
            yield return RunSahnesiniHazirla();

            Vector3 ekranSagi = EkranSagi();
            Vector3 ekranUstu = _kamera.PlanarForward;

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] hasar.yonu (REQ-045)");
            rapor.AppendLine("  kamera euler = " + _kamera.StableRotation.eulerAngles);
            rapor.AppendLine("  ekran sagi   = " + ekranSagi + "   ekran ustu = " + ekranUstu);
            rapor.AppendLine("  tepe alfa    = " + UiTokens.DamageEdgePeakAlpha +
                             "   vurus = maks canin %" + (VurusOrani * 100f));

            yield return YonSina(ekranSagi, UiScreenEdge.Right, UiScreenEdge.Left, rapor);
            yield return YonSina(-ekranSagi, UiScreenEdge.Left, UiScreenEdge.Right, rapor);
            yield return YonSina(ekranUstu, UiScreenEdge.Top, UiScreenEdge.Bottom, rapor);
            yield return YonSina(-ekranUstu, UiScreenEdge.Bottom, UiScreenEdge.Top, rapor);

            TestContext.WriteLine(rapor.ToString());
        }

        // =========================================================================== TEST 2

        /// <summary>
        /// REQ-044 — AYNI KAREDE İKİ ZIT YÖNDEN HASAR. Oyuncuda dokunulmazlık penceresi
        /// yok, dolayısıyla iki düşman aynı karede iki yandan vurabilir ve İKİSİ DE
        /// görünmek zorundadır.
        ///
        /// Bu, "son hasarın yönünü tut" biçiminde yazılmış tek değişkenli bir uygulamanın
        /// geçemeyeceği tek testtir: öyle bir uygulamada ikinci vuruş birincisini siler ve
        /// sol kenar 0 ölçülür.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator AyniKaredeIkiYondenHasarIkiKenariDaYakar()
        {
            yield return RunSahnesiniHazirla();

            Vector3 ekranSagi = EkranSagi();

            yield return KenarlarSonsun();

            int oncekiVurus = _katman.HitCount;

            _can.HealToFull();
            Vurdur(ekranSagi);
            Vurdur(-ekranSagi);
            yield return ZirveyiOlc();

            float sag = Zirve(UiScreenEdge.Right);
            float sol = Zirve(UiScreenEdge.Left);

            // İki AYRI olay tüketildi mi: tek bir olayı iki kez çizmek de aynı iki kenarı
            // yakardı, bu sayaç o ihtimali ayırır.
            Assert.AreEqual(2, _katman.HitCount - oncekiVurus,
                "Katman iki hasar olayini ayri ayri tuketmedi (sayac farki " +
                (_katman.HitCount - oncekiVurus) + ").");

            TestContext.WriteLine("[DD-TEST] hasar.esZamanli (REQ-044)  sag=" +
                                  sag.ToString("F3") + "  sol=" + sol.ToString("F3"));

            Assert.Greater(sag, YananKenarEsigi,
                "Sag kenar yanmadi (alfa " + sag.ToString("F3") + ").");
            Assert.Greater(sol, YananKenarEsigi,
                "Sol kenar yanmadi (alfa " + sol.ToString("F3") + "). Iki zit yonden ayni karede " +
                "gelen hasarin ikisi de gorunmeli; tek degiskenli bir uygulama burada kalir.");
        }

        // =========================================================================== TEST 3

        /// <summary>
        /// KAMERA EĞİMİ HESABA KATILIYOR MU. Bu testin asıl sebebi budur.
        ///
        /// Kamera sabit açılı: yalpası sahnede yazılmış, ayrıca yükseltilip geriye
        /// çekildiği için AŞAĞI EĞİK. Dünya yönünü ekran kenarına eşlemenin iki ayrı
        /// hatası vardır ve yalpayı düzeltmek yalnız birincisini kapatır:
        ///
        ///   Zemin düzlemindeki bir yön ekranda tam bir birim DİKEY üretmez; eğimin
        ///   sinüsü kadar üretir. Bu yüzden PlanarRight/PlanarForward izdüşümlerini
        ///   doğrudan kenar ağırlığı saymak dikey ekseni 1/sin(eğim) oranında ŞİŞİRİR.
        ///
        /// ÖLÇÜM: saldırgan tam köşegene konur, yani (ekranSagi + ekranUstu)/√2.
        ///   saf (yanlış) eşleme  -> sag/ust = 1.000   iki kenar eşit yanar
        ///   doğru eşleme         -> sag/ust = 1/sin(egim)
        /// Sahnenin gerçek eğimi kameranın ileri vektöründen okunur — yani beklenti
        /// ölçülen koddan değil, trigonometriden gelir. İki tahmin arasındaki mesafe
        /// kapanmaz: 15 m yukarı / 13 m geri çerçevelemede sin(49.1°) = 0.756 ve oran
        /// 1.32 çıkar, saf eşlemenin öngördüğü 1.00'dan uzaktır.
        ///
        /// ORAN SEÇİLDİ, MUTLAK DEĞER DEĞİL: oran sönüm zarfından bağımsızdır (iki kenar
        /// da aynı zarfı yaşar), dolayısıyla ölçümün hangi karede yapıldığı sonucu
        /// değiştiremez.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator KosegenHasarKameraEgimineGoreBolusturulur()
        {
            yield return RunSahnesiniHazirla();

            Vector3 ekranSagi = EkranSagi();
            Vector3 ekranUstu = _kamera.PlanarForward;

            // Kameranın ileri vektörünün aşağı bileşeni = sin(egim).
            float sinEgim = -(_kamera.StableRotation * Vector3.forward).y;
            Assert.Greater(sinEgim, 0.05f,
                "Kamera asagi egik degil (sin=" + sinEgim.ToString("F3") + "); bu testin " +
                "turetimi yalanlanmistir.");

            // BU TEST NE ZAMAN YALAN SOYLER: kamera neredeyse tepeden bakiyorsa sin(egim)
            // 1'e gider, dogru esleme ile saf esleme ayni sonucu verir ve ayirt edici iddia
            // sessizce bosalir. O yuzden ust sinir da olculur; carpismasi gereken iki tahmin
            // arasinda en az %10 mesafe kalmali.
            Assert.Less(sinEgim, 0.95f,
                "Kamera neredeyse tepeden bakiyor (sin=" + sinEgim.ToString("F3") + "). Bu " +
                "cerceveleme ile dogru esleme ile saf esleme ayni sayiyi verir; test ayirt " +
                "edici olmaktan cikar. Cerceveleme degistiyse bu testin turetimi yenilenmeli.");

            float beklenenOran = 1f / sinEgim;

            yield return KenarlarSonsun();

            _can.HealToFull();
            Vurdur((ekranSagi + ekranUstu).normalized);
            yield return ZirveyiOlc();

            // İki kenar aynı zarfı yaşar: tepedeki değerleri bekleme penceresinde eşittir, oran zarftan bağımsız kalır.
            float sag = Zirve(UiScreenEdge.Right);
            float ust = Zirve(UiScreenEdge.Top);
            float sol = Zirve(UiScreenEdge.Left);
            float alt = Zirve(UiScreenEdge.Bottom);

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] hasar.kosegen (kamera egimi)");
            rapor.AppendLine("  sin(egim) = " + sinEgim.ToString("F4") +
                             "   egim = " + (Mathf.Asin(sinEgim) * Mathf.Rad2Deg).ToString("F1") + " derece");
            rapor.AppendLine("  alfa: sag=" + sag.ToString("F3") + "  ust=" + ust.ToString("F3") +
                             "  sol=" + sol.ToString("F3") + "  alt=" + alt.ToString("F3"));
            TestContext.WriteLine(rapor.ToString());

            Assert.Greater(sag, 0.20f, "Sag kenar kosegen vurusta yanmadi.");
            Assert.Greater(ust, 0.20f, "Ust kenar kosegen vurusta yanmadi; kosegen tek kenara indirgenmis.");
            Assert.LessOrEqual(sol, SonukKenarEsigi, "Sol kenar kosegen vurusta yanmis.");
            Assert.LessOrEqual(alt, SonukKenarEsigi, "Alt kenar kosegen vurusta yanmis.");

            float olculenOran = sag / ust;
            rapor.AppendLine("  oran: olculen=" + olculenOran.ToString("F3") +
                             "  beklenen=" + beklenenOran.ToString("F3") + "  saf esleme=1.000");

            Assert.AreEqual(beklenenOran, olculenOran, 0.10f,
                "Kosegen hasarin kenarlara bolusumu kamera egimiyle uyusmuyor.\n" +
                "  olculen sag/ust = " + olculenOran.ToString("F3") + "\n" +
                "  beklenen        = " + beklenenOran.ToString("F3") + "  (1/sin(egim))\n" +
                "  saf esleme      = 1.000  (dunya yonunu dogrudan kenara esleyen hatali hal)\n" +
                "Olculen 1.00'a yakinsa dunya yonu ekran kenarina duz esleniyor demektir.");
        }

        // =========================================================================== TEST 4

        /// <summary>
        /// REQ-045'in "kontrolü kesme" yarısının bu modüle düşen kısmı.
        ///
        /// Gösterge bütün oyun alanının üzerine serilmiş dört tam ekran grafiktir. Bunlar
        /// tıklama yakalarsa nişan alma girdisi gösterge yandığı sürece ölür — yani tam da
        /// oyuncunun en çok ihtiyaç duyduğu anda. Bu, bileşenin yaratabileceği TEK kontrol
        /// kaybı yoludur, o yüzden ayrıca nöbete konur. timeScale de aynı yerde ölçülür.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator GostergeGirdiYutmazVeZamaniDurdurmaz()
        {
            yield return RunSahnesiniHazirla();

            float oncekiZamanOlcegi = Time.timeScale;

            _can.HealToFull();
            Vurdur(EkranSagi());
            yield return ZirveyiOlc();

            Assert.Greater(Zirve(UiScreenEdge.Right), YananKenarEsigi,
                "Olcum gecersiz: gosterge zaten yanmiyor.");

            for (int i = 0; i < 4; i++)
            {
                var kenar = (UiScreenEdge)i;
                UnityEngine.UI.Graphic g = _katman.EdgeGraphic(kenar);
                Assert.IsNotNull(g, kenar + " kenari kurulmamis.");
                Assert.IsFalse(g.raycastTarget,
                    kenar + " kenari raycastTarget = true. Tam ekran bir hasar grafigi girdi " +
                    "yakalarsa REQ-045 ihlal edilir: hasar aldigi an oyuncunun nisani olur.");
            }

            // WP-095 K1 (C-00 08.10) is the root cause of the wait below: this hit is 30 % of max health — HEAVY (≥ 25 %) — so
            // top-down asks the clock arbiter (FeelTimeScale) for a 2-frame global stop. That stop is the arbiter's, not the
            // indicator's; the indicator itself runs on unscaled time and never touches the clock. Let the stop end (≤ 4
            // frames, here 2), then the clock must be exactly what it was: the HUD left it alone (REQ-045 unchanged).
            // S-02 (f2a): the stop runs on REAL time (4 frames = 0.067 s at 60 fps) and batch frames are 1–3 ms, so frames are no
            // measure here (ders-batch-kare-suresi) — wait real time with a margin.
            float durmaT0 = Time.realtimeSinceStartup;
            while (DD.Prototype.Feedback.FeelTimeScale.GlobalStopActive && Time.realtimeSinceStartup - durmaT0 < 0.5f)
            {
                yield return null;
            }

            Assert.IsFalse(DD.Prototype.Feedback.FeelTimeScale.GlobalStopActive, "K1: küresel durma 4 karede (0,067 s) bitmedi.");
            Assert.Less(Time.realtimeSinceStartup - durmaT0, 0.25f, "K1: küresel durma 4 kareden (0,067 s) çok uzun sürdü.");
            Assert.AreEqual(oncekiZamanOlcegi, Time.timeScale, 0.0001f,
                "Hasar geri bildirimi timeScale'e dokundu.");

            TestContext.WriteLine("[DD-TEST] hasar.kontrol  timeScale=" + Time.timeScale +
                                  "  dort kenar da raycastTarget=false");
        }

        // ============================================================================ altyapı

        private IEnumerator YonSina(Vector3 saldirganYonu, UiScreenEdge yanmasiGereken,
            UiScreenEdge sonukKalmasiGereken, StringBuilder rapor)
        {
            yield return KenarlarSonsun();

            _can.HealToFull();
            Vurdur(saldirganYonu);
            yield return ZirveyiOlc();

            float yanan = Zirve(yanmasiGereken);
            float sonuk = Zirve(sonukKalmasiGereken);

            rapor.AppendLine("  yon " + saldirganYonu.ToString("F2") + " -> " +
                             yanmasiGereken + "=" + yanan.ToString("F3") + "  " +
                             sonukKalmasiGereken + "=" + sonuk.ToString("F3"));

            Assert.Greater(yanan, YananKenarEsigi,
                yanmasiGereken + " kenari yanmadi (alfa " + yanan.ToString("F3") + ", esik " +
                YananKenarEsigi + "). Saldirgan " + saldirganYonu.ToString("F2") + " yonundeydi.");

            Assert.LessOrEqual(sonuk, SonukKenarEsigi,
                sonukKalmasiGereken + " kenari yanmis (alfa " + sonuk.ToString("F3") +
                "). Sahnede dusman yok, yani bu kenari yakabilecek baska hicbir hasar yok: " +
                "gosterge yanlis kenari secmis.");
        }

        /// <summary>Oyuncuya, verilen yönde duran bir saldırgandan gelmiş gibi hasar uygular.</summary>
        private void Vurdur(Vector3 saldirganYonu)
        {
            Vector3 oyuncu = _can.transform.position;
            Vector3 kaynak = oyuncu + saldirganYonu.normalized * SaldirganMesafesi;

            bool uygulandi = _can.ApplyDamage(
                new PlayerDamageRequest(_can.Max * VurusOrani, oyuncu, kaynak, "Test.HitDirection"));

            Assert.IsTrue(uygulandi, "ApplyDamage hasari uygulamadi; olcum gecersiz.");
        }

        /// <summary>
        /// Bütün kenarlar sönene kadar bekler. Bir vuruşun ömrü 0.05 + 0.25 + 0.3 = 0.6 sn (WP-094, §4.6); kenar
        /// katmanı ölçeksiz saatle yaşlanır, o yüzden sınır kare değil gerçek zamandır (3 sn = ömrün beş katı).
        /// </summary>
        private IEnumerator KenarlarSonsun()
        {
            const float enFazlaSaniye = 3f;
            float basla = Time.realtimeSinceStartup;

            while (_katman.IsShowing && Time.realtimeSinceStartup - basla < enFazlaSaniye)
            {
                yield return null;
            }

            Assert.IsFalse(_katman.IsShowing,
                "Gosterge " + enFazlaSaniye + " sn sonunda hala yaniyor; sonum calismiyor " +
                "ya da olcum penceresine disaridan hasar giriyor.");
        }

        /// <summary>
        /// WP-094 (§4.6): vuruştan sonra yükseliş (0.05 sn) ve bekleme (0.25 sn) boyunca her kareyi okur ve her kenarın
        /// en yüksek alfasını <see cref="_zirve"/>'ye yazar. Pencere gerçek zamanla 0.3 sn ya da kenarlar sönünce biter.
        /// </summary>
        private IEnumerator ZirveyiOlc()
        {
            for (int i = 0; i < _zirve.Length; i++)
            {
                _zirve[i] = _katman.EdgeAlpha((UiScreenEdge)i);
            }

            float pencere = UiTokens.DamageEdgeRiseSeconds + UiTokens.DamageEdgeHoldSeconds;
            float basla = Time.realtimeSinceStartup;
            do
            {
                yield return null;
                for (int i = 0; i < _zirve.Length; i++)
                {
                    _zirve[i] = Mathf.Max(_zirve[i], _katman.EdgeAlpha((UiScreenEdge)i));
                }
            }
            while (_katman.IsShowing && Time.realtimeSinceStartup - basla < pencere);
        }

        private float Zirve(UiScreenEdge kenar)
        {
            return _zirve[(int)kenar];
        }

        /// <summary>
        /// Ekran sağına karşılık gelen YATAY dünya yönü. Kameranın sağ vektörüdür: yalpa
        /// ve eğim (yatma yok) bu vektörü zemin düzleminden çıkarmaz, test bunu ölçerek
        /// doğrular — çıkarsaydı bütün türetim düşerdi.
        /// </summary>
        private Vector3 EkranSagi()
        {
            Vector3 sag = _kamera.StableRotation * Vector3.right;

            Assert.Less(Mathf.Abs(sag.y), 0.01f,
                "Kameranin sag vektoru yatay degil (y=" + sag.y.ToString("F4") + "); kamera " +
                "yatmis demektir ve bu testin ekran-sagi turetimi gecersizdir.");

            sag.y = 0f;
            return sag.normalized;
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < SahneKurulumSaniye)
            {
                yield return null;
            }

            PlayerRig rig = PlayerRegistry.Primary;
            Assert.IsNotNull(rig, "Run sahnesinde oyuncu dogmadi.");

            _can = rig.Health;
            Assert.IsNotNull(_can, "Oyuncuda PlayerHealth yok.");
            Assert.IsFalse(_can.DiagnosisInvulnerable,
                "Oyuncu tani dokunulmazligiyla acildi; olcum gecersiz olurdu.");

            _kamera = PrototypeCameraRig.Active;
            Assert.IsNotNull(_kamera, "Run sahnesinde PrototypeCameraRig yok.");

            // KUSURUN KENDİSİNİ ÖLÇEN İKİ SATIR. Gösterge sahnede başıboş duruyor olabilir
            // ve yine de hiçbir şeye bağlı olmayabilir; bu yüzden HUD'un KENDİ alanına
            // bakılır. Bu alan boşsa kusur aynen geri gelmiş demektir.
            _hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(_hud, "Run sahnesinde RunHudScreen yok.");

            _katman = _hud.DamageEdges;
            Assert.IsNotNull(_katman,
                "RunHudScreen.DamageEdges bos. Yonlu hasar gostergesi kurulmamis - " +
                "'belgelenmis, baglanmamis' kusuru geri gelmis.");

            // Canlı düşman ölçümü kirletir: zıt kenarin TAM 0 olmasi iddiasi ancak
            // disaridan hasar gelemeyecegi kesinken kurulabilir.
            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.StopRound();
                EnemySpawnDirector.Instance.ClearAllEnemies();
            }

            yield return null;

            // Kare hızından bağımsız, tekrarlanabilir sönüm ölçümü.
            Time.captureDeltaTime = Adim;
            _can.HealToFull();

            yield return null;
        }

        [TearDown]
        public void Bitir()
        {
            Time.captureDeltaTime = 0f;

            _can = null;
            _hud = null;
            _katman = null;
            _kamera = null;
        }
    }
}
