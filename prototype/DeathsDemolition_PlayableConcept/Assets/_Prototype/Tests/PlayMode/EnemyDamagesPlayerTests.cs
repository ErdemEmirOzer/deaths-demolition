using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// Testin sürdüğü girdi kaynağı. <see cref="SurulenGirdi"/> ile aynı işi görür ama
    /// yönü her karede değiştirilebilsin diye ayrı durur; iki testin birbirinin girdi
    /// varsayımını bozması istenmiyor.
    /// </summary>
    internal sealed class SaldiriTestiGirdisi : IInputSource
    {
        public Vector2 Yon;

        public bool IsEnabled => true;
        public Vector2 Move => Yon;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public Vector2 AimStick => Vector2.zero;
        public bool FireHeld => false;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;
#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// G-15 · KUSUR 4 regresyon testi — "düşmanlar oyuncuya hiç hasar vermiyor".
    /// TEST-PLANI TS-223'ün otomatik karşılığı. REQ-043 / REQ-044 / REQ-045 / DEC-029.
    ///
    /// NEDEN VAR
    /// ---------
    /// Duman testi ölümü <c>PlayerHealth.ApplyDamage</c> ile ELLE tetikliyordu; düşman
    /// saldırı zincirinin tamamı hiç sınanmıyordu. Kusur tam da o sınanmayan halkadaydı:
    /// zombiler menzile giriyor, saldırı durum makinesi dönüyor, aktif karede vuruş alanı
    /// oyuncuyu buluyor, olay yayınlanıyor — ve hasarı kimse uygulamıyordu.
    ///
    /// 2026-08-18 ölçümü (oyuncu hareketsiz, 6 Standard zombi, 12 sn, 1/60 sabit adım):
    ///     en yakın planar mesafe                 0.929 m   (AttackRange 1.70)
    ///     windup / active / recovery kare        960 / 240 / 672
    ///     oyuncuyu bulan aktif kare              240 / 240
    ///     DamageEvents.PlayerAttacked            30 kez, 300 hasar taşıdı
    ///     bunlardan DamageApplied = true         0
    ///     PlayerHealth.Damaged                   0 kez
    ///     can                                    100.0 -> 100.0
    ///
    /// Bu dosyadaki hiçbir test ApplyDamage çağırmaz. Can yalnız zombinin kendi
    /// saldırısıyla düşer; düşmezse test kalır.
    ///
    /// EŞİKLER NEREDEN GELİYOR
    /// -----------------------
    /// Hepsi hasar ve zamanlama formülünden türetilir, ölçümden değil. Sayılar teste
    /// SABİT yazılmıştır; config'ten okunsalardı test kendi kendini doğrulardı.
    /// </summary>
    [TestFixture]
    public sealed class EnemyDamagesPlayerTests
    {
        // WP-076: this suite checks the WP-002/003 damage hand-off against the DEC-021 per-tier formula (every swing = the
        // tier's own attack). The five-style variety has its own suites (AttackSelectorTests / AttackVarietySliceTests), so
        // the selector is pinned to the signature style here — the same switch as `-ddAiV3 0`.
        [SetUp] public void Wp076ImzaStil() => AttackSelector.Enabled = false;
        [TearDown] public void Wp076ImzaStilBitir() => AttackSelector.Enabled = true;

        // ---------------------------------------------------- EnemyConfigSection · Standard
        /// <summary>AttackDamage 10 × EnemyDamageMultiplier(R1) = 1 + 0.08·(1−1) = 1.00.</summary>
        private const float TekVurusHasari = 10f;

        private const float Telegraf = 0.55f;
        private const float AktifPencere = 0.12f;
        private const float Toparlanma = 0.45f;
        private const float Bekleme = 1.5f;

        /// <summary>Bir saldırının başından ötekinin başına: 0.55 + 0.12 + 0.45 + 1.50.</summary>
        private const float SaldiriDongusu = Telegraf + AktifPencere + Toparlanma + Bekleme;

        private const float SaldiriMenzili = 1.7f;
        private const float ZombiHizi = 2.7f;
        private const float ZombiGovdeYaricapi = 0.45f;

        // -------------------------------------------------------------- PlayerConfigSection
        private const float BaslangicCani = 100f;

        /// <summary>C-00 30.09 M-20: the health the scene preparation left, before the shared full-health start (report only).</summary>
        private float _kurulumCani;
        private const float OyuncuYaricapi = 0.45f;
        private const float OyuncuHizi = 6.2f;

        /// <summary>
        /// 4.5 sn hasarsız geçmeden yenilenme başlamaz. Ölçümler bu yüzden EN DÜŞÜK canı
        /// izler: yenilenme bir minimumu geri alamaz, dolayısıyla eşikler regen'den etkilenmez.
        /// </summary>
        private const float RegenGecikmesi = 4.5f;

        private const float SahneKurulumSaniye = 12f;
        private const float Adim = 1f / 60f;

        private PlayerRig _rig;
        private PlayerHealth _can;
        private SaldiriTestiGirdisi _girdi;

        // =========================================================================== TEST 1

        /// <summary>
        /// DOĞAL YOLDAN HASAR. Oyuncu hareketsiz, üstüne 3 Standard zombi salınır,
        /// 6 sn ölçülür. Tek bir ApplyDamage çağrısı yoktur.
        ///
        /// BEKLENEN VURUŞ SAYISI — formülden
        ///   Zombi 3.00 m'den doğar, menzil 1.70 m, hız 2.70 m/s
        ///     yaklaşma  = (3.00 − 1.70) / 2.70 = 0.48 sn
        ///     ilk isabet = 0.48 + 0.55 (telegraf) = 1.03 sn
        ///     sonraki her isabet + 2.62 sn (döngü)
        ///   6.00 sn içinde zombi başına  1 + floor((6.00 − 1.03) / 2.62) = 2 isabet
        ///   3 zombi → 6 isabet → 6 × 10 = 60 hasar.
        ///
        /// ALT EŞİK = 2 isabet (20 hasar). Aritmetiğin üçte biri: kalabalık itişmesi bir
        /// zombiyi bir döngü boyunca menzil dışında tutabilir, ama üç zombinin altı
        /// salınımından yalnız biri bağlanırsa zaten sistem çalışmıyordur. Kusurlu hâlde
        /// ölçülen değer 0 isabet / 0 hasardı; eşik ikisinin arasında kapanmaz boşluk bırakır.
        ///
        /// ÜST EŞİK = oyuncu ölmemeli. Aritmetik tavan 6 isabet = 60 hasar &lt; 100 can.
        /// Ölüm görülürse ya hasar formülü ya da saldırı zamanlaması bozulmuştur.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator SuruDogalYollaOyuncununCaniniDusurur()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunSahnesiniHazirla();

            const int zombiSayisi = 3;
            const float dogumYaricapi = 3f;
            const float olcumSaniye = 6f;
            const int beklenenIsabet = 6;   // yukarıdaki türetme
            const int altEsikIsabet = 2;

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] dusman.hasar (dogal yol, elle ApplyDamage YOK)");
            rapor.AppendLine("  senaryo: " + zombiSayisi + " Standard zombi, " + dogumYaricapi +
                             " m halka, " + olcumSaniye + " sn, oyuncu hareketsiz");
            rapor.AppendLine("  formul : menzil " + SaldiriMenzili + " m, zombi hizi " + ZombiHizi +
                             " m/s, telegraf " + Telegraf + " sn, dongu " + SaldiriDongusu.ToString("F2") +
                             " sn -> ilk isabet 1.03 sn, beklenen " + beklenenIsabet + " isabet = " +
                             (beklenenIsabet * TekVurusHasari) + " hasar");

            SuruDogur(zombiSayisi, dogumYaricapi, rapor);

            var vuruslar = new List<PlayerDamageInfo>(16);
            var zamanOlcegi = new List<float>(16);
            var kontrolAcik = new List<bool>(16);

            Action<PlayerDamageInfo> dinle = info =>
            {
                vuruslar.Add(info);
                // REQ-045 aynı anda ölçülür: hasar KARESİNDE kontrol hâlâ açık mı.
                zamanOlcegi.Add(Time.timeScale);
                kontrolAcik.Add(_rig.Motor.enabled && _rig.Motor.ControlEnabled &&
                                _rig.Aim.enabled && _rig.Aim.ControlEnabled);
            };

            _can.Damaged += dinle;

            float baslangicCan = _can.Current;
            float enDusukCan = baslangicCan;

            int kareSayisi = Mathf.RoundToInt(olcumSaniye / Adim);
            for (int i = 0; i < kareSayisi; i++)
            {
                yield return null;
                if (_can.Current < enDusukCan)
                {
                    enDusukCan = _can.Current;
                }
            }

            _can.Damaged -= dinle;

            float dusus = baslangicCan - enDusukCan;
            rapor.AppendLine("  OLCUM  : isabet=" + vuruslar.Count +
                             "  en dusuk can=" + enDusukCan.ToString("F1") +
                             "  dusus=" + dusus.ToString("F1") +
                             "  (baslangic " + baslangicCan.ToString("F1") + ")");
            rapor.AppendLine("  canli zombi (olcum sonu) = " + CanliZombiSayisi());

            for (int i = 0; i < vuruslar.Count; i++)
            {
                PlayerDamageInfo v = vuruslar[i];
                rapor.AppendLine("    vurus[" + i + "] istenen=" + v.RequestedAmount.ToString("F2") +
                                 " uygulanan=" + v.AppliedAmount.ToString("F2") +
                                 " " + v.HealthBefore.ToString("F1") + "->" + v.HealthAfter.ToString("F1") +
                                 " kaynak=" + (v.SourceId ?? "<null>") +
                                 " timeScale=" + zamanOlcegi[i].ToString("F2") +
                                 " kontrol=" + kontrolAcik[i]);
            }

            Debug.Log(rapor.ToString());

            Assert.GreaterOrEqual(vuruslar.Count, altEsikIsabet,
                "Sürü oyuncuya DOĞAL YOLDAN hasar veremiyor. " + zombiSayisi + " zombi " +
                olcumSaniye + " sn boyunca " + vuruslar.Count + " isabet uyguladı; formül " +
                beklenenIsabet + " bekliyor, eşik " + altEsikIsabet + ". Kusurlu hâlde bu " +
                "sayı 0'dı: saldırı bağlanıyor, olay yayınlanıyor, hasarı kimse uygulamıyordu.\n" +
                rapor);

            Assert.GreaterOrEqual(dusus, altEsikIsabet * TekVurusHasari - 0.01f,
                "İsabet sayılıyor ama can düşmüyor.\n" + rapor);

            // REQ-043: her isabet, config'in tek vuruş hasarını TAM uygular. Bir kısıt,
            // bekleme ya da soğuma eklenirse bu eşitlik önce burada bozulur.
            for (int i = 0; i < vuruslar.Count; i++)
            {
                PlayerDamageInfo v = vuruslar[i];
                Assert.AreEqual(TekVurusHasari, v.RequestedAmount, 0.01f,
                    "vurus[" + i + "] hasarı " + v.RequestedAmount.ToString("F2") +
                    "; Standard/R1 formülü tam " + TekVurusHasari + " diyor.\n" + rapor);

                // REQ-044: dokunulmazlık yok — her çağrı tam uygulanır, kısmen değil.
                Assert.AreEqual(TekVurusHasari, v.HealthBefore - v.HealthAfter, 0.01f,
                    "vurus[" + i + "] canın yalnız " + (v.HealthBefore - v.HealthAfter).ToString("F2") +
                    " puanını götürdü. REQ-044 dokunulmazlık/soğuma yasağını çiğneyen bir " +
                    "kapı eklenmiş demektir.\n" + rapor);

                Assert.IsNotNull(v.SourceId,
                    "vurus[" + i + "] kaynaksız geldi; zincirin düşman ucundan gelmiyor olabilir.\n" + rapor);
                StringAssert.StartsWith("Zombie.", v.SourceId,
                    "vurus[" + i + "] kaynağı '" + v.SourceId + "'. Hasar düşman saldırı " +
                    "zincirinden değil başka bir yerden geliyor.\n" + rapor);

                // REQ-045: hasar karesinde hareket/nişan kontrolü kesilmemiş olmalı.
                Assert.AreEqual(1f, zamanOlcegi[i], 0.0001f,
                    "vurus[" + i + "] anında timeScale " + zamanOlcegi[i] +
                    "; hasar dünyayı donduruyor (REQ-045).\n" + rapor);
                Assert.IsTrue(kontrolAcik[i],
                    "vurus[" + i + "] anında oyuncunun hareket/nişan kontrolü KAPALI (REQ-045).\n" + rapor);
            }

            Assert.IsFalse(_can.IsDead,
                "Oyuncu bu senaryoda ölmemeliydi: aritmetik tavan " + beklenenIsabet + " × " +
                TekVurusHasari + " = " + (beklenenIsabet * TekVurusHasari) + " < " + BaslangicCani +
                ". Ölüm, hasarın formülden büyük olduğunu ya da saldırının çok sık " +
                "bağlandığını gösterir.\n" + rapor);
        }

        // =========================================================================== TEST 2

        /// <summary>
        /// TS-223 · REQ-043 / DEC-029 — TEMAS TEK BAŞINA HASAR VERMEZ.
        ///
        /// Saldırıyı bastırıp "hasar gelmedi" demek boş bir ölçüm olurdu: hiçbir şey
        /// olmayan bir sahnede hiçbir şey olmaması sınanmış sayılmaz. Bunun yerine
        /// saldırılar serbest bırakılır ve HER hasar olayının, ORTAYA ÇIKTIĞI ANDA
        /// aktif pencerede bir zombiyle çakıştığı doğrulanır. Ölçüm PlayerHealth.Damaged
        /// işleyicisinin İÇİNDE yapılır: o işleyici ApplyDamage'in içinden, o da
        /// TryConnect'in içinden, o da saldıran zombinin Tick'inin içinden çağrılır, yani
        /// saldıran zombi o anda tanım gereği Active fazındadır. Kare sırası varsayımı yok.
        ///
        /// ÖLÇÜMÜN BOŞ OLMADIĞININ KANITI: temas VAR ama hiçbir zombi aktif pencerede
        /// DEĞİL olan kare sayısı. Bir saldırı döngüsünün yalnız 0.12 / 2.62 = %4.6'sı
        /// aktif penceredir, yani temasın büyük çoğunluğu saldırısız geçer. 4 zombiyle
        /// ~4.1 sn temasın en az 1 sn'si (60 kare) saldırısız olmalı; eşik budur.
        ///
        /// SENARYO BOYU — ölümü aritmetikle dışarıda bırakır
        ///   2.60 m doğum, yaklaşma (2.60 − 1.70) / 2.70 = 0.33 sn, ilk isabet 0.88 sn,
        ///   ikinci 3.50 sn, üçüncü 6.12 sn. 4.50 sn'de zombi başına 2 isabet, 4 zombi →
        ///   8 isabet = 80 hasar &lt; 100 can.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator TemasTekBasinaHasarVermez()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunSahnesiniHazirla();

            const int zombiSayisi = 4;
            const float dogumYaricapi = 2.6f;
            const float olcumSaniye = 4.5f;
            const int temasEsigiKare = 60;

            // Kapsül yüzeyleri değiyor sayılacağı mesafe: iki yarıçap + 15 cm pay.
            const float temasMesafesi = OyuncuYaricapi + ZombiGovdeYaricapi + 0.15f;

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] dusman.temas (TS-223 · REQ-043)");
            rapor.AppendLine("  senaryo: " + zombiSayisi + " Standard zombi, " + dogumYaricapi +
                             " m halka, " + olcumSaniye + " sn, oyuncu hareketsiz");
            rapor.AppendLine("  temas esigi mesafe = " + temasMesafesi.ToString("F2") + " m");
            rapor.AppendLine("  kurulum cani=" + _kurulumCani.ToString("F1") +
                             " olcum baslangici=" + _can.Current.ToString("F1"));

            SuruDogur(zombiSayisi, dogumYaricapi, rapor);

            float baslangicCani = _can.Current;
            float istenenToplam = 0f;
            float uygulananToplam = 0f;
            var hasarMetni = new StringBuilder();
            int ihlal = 0;
            int hasarOlayi = 0;
            var ihlalMetni = new StringBuilder();

            Action<PlayerDamageInfo> dinle = info =>
            {
                hasarOlayi++;
                istenenToplam += info.RequestedAmount;
                uygulananToplam += info.AppliedAmount;
                hasarMetni.AppendLine("    hit " + hasarOlayi + " requested=" + info.RequestedAmount.ToString("F2") +
                                      " applied=" + info.AppliedAmount.ToString("F2") +
                                      " hp=" + info.HealthBefore.ToString("F2") + "->" + info.HealthAfter.ToString("F2") +
                                      " source=" + info.SourceId + " fatal=" + info.WasFatal);
                if (AktifPenceredeZombiVar())
                {
                    return;
                }

                ihlal++;
                ihlalMetni.AppendLine("    IHLAL: kare " + Time.frameCount + " · " +
                                      info.RequestedAmount.ToString("F1") + " hasar · kaynak " +
                                      (info.SourceId ?? "<null>") +
                                      " · o anda AKTIF PENCEREDE ZOMBI YOK");
            };

            _can.Damaged += dinle;

            int temasliKare = 0;
            int temasVarSaldiriYokKare = 0;
            int sessizPencereIhlali = 0;
            float enYakin = float.MaxValue;

            // Olay dinleyicisinden BAĞIMSIZ ikinci nöbetçi: art arda gelen ve ikisinde de
            // hiçbir zombinin aktif pencerede olmadığı iki karede can düşerse, hasarı
            // saldırıdan başka bir şey uygulamış demektir. Tek kare bakmak yetmez, çünkü
            // aktif karede uygulanan hasar bir sonraki karede hâlâ "yeni düşmüş" görünür.
            float oncekiCan = _can.Current;
            bool oncekiAktif = true;

            int kareSayisi = Mathf.RoundToInt(olcumSaniye / Adim);
            for (int i = 0; i < kareSayisi; i++)
            {
                yield return null;

                bool temas = false;
                bool aktif = false;
                IReadOnlyList<ZombieEnemy> canli = ZombieEnemy.Live;

                for (int z = 0; z < canli.Count; z++)
                {
                    ZombieEnemy zombi = canli[z];
                    if (zombi == null || !zombi.IsAlive)
                    {
                        continue;
                    }

                    Vector3 d = zombi.transform.position - _rig.transform.position;
                    d.y = 0f;
                    float mesafe = d.magnitude;
                    if (mesafe < enYakin)
                    {
                        enYakin = mesafe;
                    }

                    if (mesafe <= temasMesafesi)
                    {
                        temas = true;
                    }

                    if (zombi.AttackPhase == ZombieAttackPhase.Active)
                    {
                        aktif = true;
                    }
                }

                if (temas)
                {
                    temasliKare++;
                    if (!aktif)
                    {
                        temasVarSaldiriYokKare++;
                    }
                }

                if (!aktif && !oncekiAktif && _can.Current < oncekiCan - 0.001f)
                {
                    sessizPencereIhlali++;
                    ihlalMetni.AppendLine("    IHLAL: kare " + Time.frameCount +
                                          " · can " + oncekiCan.ToString("F2") + " -> " +
                                          _can.Current.ToString("F2") +
                                          " · art arda iki karede de aktif pencere YOK");
                }

                oncekiCan = _can.Current;
                oncekiAktif = aktif;
            }

            _can.Damaged -= dinle;

            rapor.AppendLine("  OLCUM  : temasli kare=" + temasliKare + " / " + kareSayisi +
                             "  temas var + saldiri yok kare=" + temasVarSaldiriYokKare +
                             "  en yakin mesafe=" + enYakin.ToString("F3") + " m");
            rapor.AppendLine("  hasar olayi=" + hasarOlayi + "  olay-ihlali=" + ihlal +
                             "  sessiz-pencere-ihlali=" + sessizPencereIhlali +
                             "  can " + baslangicCani.ToString("F1") + " -> " +
                             _can.Current.ToString("F1"));
            rapor.AppendLine("  requested toplam=" + istenenToplam.ToString("F2") +
                             " applied toplam=" + uygulananToplam.ToString("F2") +
                             " selector=" + AttackSelector.Enabled);
            rapor.Append(hasarMetni);
            if (ihlal > 0)
            {
                rapor.Append(ihlalMetni);
            }

            Debug.Log(rapor.ToString());

            Assert.Greater(temasliKare, 0,
                "Hiçbir karede fiziksel temas ölçülmedi; ölçüm boş. Zombiler oyuncuya " +
                "hiç yaklaşmamış (en yakın " + enYakin.ToString("F3") + " m).\n" + rapor);

            Assert.GreaterOrEqual(temasVarSaldiriYokKare, temasEsigiKare,
                "Temas VAR ama saldırı YOK durumunda yalnız " + temasVarSaldiriYokKare +
                " kare geçti; eşik " + temasEsigiKare + ". Bu kadar az örnekle 'temas hasar " +
                "vermiyor' iddiası boş ölçümdür.\n" + rapor);

            Assert.Greater(hasarOlayi, 0,
                "Ölçüm boyunca hiç hasar olayı olmadı; bu test o zaman hiçbir şey " +
                "denetlemiyor. Önce SuruDogalYollaOyuncununCaniniDusurur'a bakın.\n" + rapor);

            Assert.AreEqual(0, ihlal,
                "REQ-043 / DEC-029 ÇİĞNENDİ: " + ihlal + " hasar olayı, hiçbir zombi görünür " +
                "saldırının aktif penceresinde değilken uygulandı. Temas tek başına hasar " +
                "veremez.\n" + rapor);

            Assert.AreEqual(0, sessizPencereIhlali,
                "REQ-043 / DEC-029 ÇİĞNENDİ: " + sessizPencereIhlali + " kez, art arda iki " +
                "karenin ikisinde de hiçbir zombi aktif pencerede değilken can düştü. Bu, " +
                "PlayerHealth.Damaged'i hiç yükseltmeyen bir temas/yakınlık hasarı yolu " +
                "eklendiğini de yakalar.\n" + rapor);

            Assert.AreEqual(hasarOlayi * TekVurusHasari, istenenToplam, 0.01f,
                "Standard imza saldırısının istenen hasarı formülden farklı.\n" + rapor);
            Assert.AreEqual(baslangicCani - _can.Current, uygulananToplam, 0.01f,
                "Can düşüşü Damaged olaylarının AppliedAmount toplamından farklı.\n" + rapor);
            Assert.IsFalse(_can.IsDead,
                "Oyuncu bu senaryoda ölmemeliydi: aritmetik tavan 8 × " + TekVurusHasari +
                " = 80 < " + BaslangicCani + ".\n" + rapor);
        }

        // =========================================================================== TEST 3

        /// <summary>
        /// REQ-045 — HASAR ALIRKEN KONTROL KESİLMEZ.
        ///
        /// Oyuncu sabit değil: girdi saniyede bir tur dönen, büyüklüğü 0.5 olan bir vektör
        /// olarak sürülür. Böylece oyuncu yerinden ayrılmadan (yarıçap ≈ 3.1 / 2π ≈ 0.49 m)
        /// sürekli hareket eder, zombiler menzilde kalır ve hız hiçbir karede sıfıra
        /// düşmez — dönüş yönü değişse bile yol uzunluğu ölçülebilir kalır.
        ///
        /// EŞİK: bir hasar olayından sonraki 10 karede kat edilen YOL.
        ///   MoveSpeed 6.2 m/s × girdi 0.5 = 3.1 m/s; 10 kare = 0.1667 sn → ideal 0.517 m.
        ///   Eşik 0.05 m, idealin %10'u. Kalabalık oyuncuyu fiziksel olarak engelleyebilir
        ///   (REQ-046): CI/yerelde 0.000–0.010 m pencereler ölçüldü, yani yol tek başına
        ///   kilidi kalabalıktan ayıramaz. WP-069 B5: her pencerede motor girdiyi almalı
        ///   (ControlEnabled, RawInput = girdi); eşik altı pencere ancak CharacterController
        ///   yanlardan temas bildirdiyse (fiziksel engel) kabul edilir.
        ///
        /// SENARYO BOYU: 3 zombi × en çok 2 isabet = 60 hasar &lt; 100 can.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator HasarAlirkenKontrolKesilmiyor()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return RunSahnesiniHazirla();

            const int zombiSayisi = 3;
            const float dogumYaricapi = 2.8f;
            const float olcumSaniye = 6f;
            const int pencereKare = 10;
            const float yolEsigi = 0.05f;
            const float girdiBuyuklugu = 0.5f;
            const float turFrekansi = 1f;   // saniyede bir tur

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] dusman.hasar.kontrol (REQ-045)");
            rapor.AppendLine("  senaryo: " + zombiSayisi + " zombi, oyuncu " + girdiBuyuklugu +
                             " buyuklugunde donen girdiyle surekli hareketli");

            SuruDogur(zombiSayisi, dogumYaricapi, rapor);

            var hasarKaresi = new List<int>(16);
            var hasarBilgisi = new List<PlayerDamageInfo>(16);
            var konumlar = new List<Vector3>(512);
            // WP-069 B5 (C-00 30.09): per sample, whether the motor took this frame's input (ControlEnabled and RawInput
            // = the stick) and whether the CharacterController's last Move touched something at its sides. A crowd
            // can pin the player (REQ-046: measured 0.000–0.010 m windows in CI/local runs); a stun cannot hide behind
            // that — it shows as control closed, or as no movement with nothing in the way.
            var kontrolAcik = new List<bool>(512);
            var yanEngel = new List<bool>(512);
            CharacterController govde = _rig.GetComponent<CharacterController>();
            Assert.IsNotNull(govde, "Oyuncuda CharacterController yok.");

            Action<PlayerDamageInfo> dinle = info =>
            {
                hasarKaresi.Add(konumlar.Count - 1);
                hasarBilgisi.Add(info);
            };

            _can.Damaged += dinle;

            int kareSayisi = Mathf.RoundToInt(olcumSaniye / Adim);
            konumlar.Add(_rig.transform.position);
            kontrolAcik.Add(true);
            yanEngel.Add(false);

            for (int i = 0; i < kareSayisi; i++)
            {
                float t = i * Adim;
                float aci = t * turFrekansi * Mathf.PI * 2f;
                _girdi.Yon = new Vector2(Mathf.Cos(aci), Mathf.Sin(aci)) * girdiBuyuklugu;

                yield return null;
                konumlar.Add(_rig.transform.position);
                kontrolAcik.Add(_rig.Motor.ControlEnabled && (_rig.Motor.RawInput - _girdi.Yon).sqrMagnitude < 1e-6f);
                yanEngel.Add((govde.collisionFlags & CollisionFlags.Sides) != 0);
            }

            _can.Damaged -= dinle;
            _girdi.Yon = Vector2.zero;

            rapor.AppendLine("  OLCUM  : hasar olayi=" + hasarKaresi.Count +
                             "  can " + BaslangicCani.ToString("F1") + " -> " +
                             _can.Current.ToString("F1") +
                             "  ornek konum sayisi=" + konumlar.Count);

            Assert.Greater(hasarKaresi.Count, 0,
                "Hareketli oyuncu 6 sn boyunca hiç hasar almadı; REQ-045 ölçülemiyor.\n" + rapor);

            float enKisaYol = float.MaxValue;
            int kontrolKesik = 0, engelsizDurma = 0;
            for (int i = 0; i < hasarKaresi.Count; i++)
            {
                int basla = Mathf.Max(0, hasarKaresi[i]);
                int bitir = Mathf.Min(konumlar.Count - 1, basla + pencereKare);
                if (bitir <= basla)
                {
                    continue;   // ölçümün son karesinde gelen isabet; penceresi yok
                }

                float yol = 0f;
                int kontrolKare = 0, engelKare = 0;
                for (int k = basla; k < bitir; k++)
                {
                    Vector3 d = konumlar[k + 1] - konumlar[k];
                    d.y = 0f;
                    yol += d.magnitude;
                    if (kontrolAcik[k + 1]) kontrolKare++;
                    if (yanEngel[k + 1]) engelKare++;
                }

                bool kesik = kontrolKare < bitir - basla;
                bool engelsiz = yol <= yolEsigi && engelKare == 0;
                if (kesik) kontrolKesik++;
                if (engelsiz) engelsizDurma++;
                rapor.AppendLine("    hasar[" + i + "] sonrasi " + (bitir - basla) +
                                 " karede yol=" + yol.ToString("F4") + " m  (esik " + yolEsigi + ")" +
                                 "  kontrol " + kontrolKare + "/" + (bitir - basla) + "  yan-engel " + engelKare +
                                 (kesik ? "  KONTROL KESIK" : "") + (engelsiz ? "  ENGELSIZ DURMA" : ""));

                if (yol < enKisaYol)
                {
                    enKisaYol = yol;
                }
            }

            Debug.Log(rapor.ToString());

            Assert.Less(enKisaYol, float.MaxValue,
                "Hiçbir hasar olayının ölçülebilir bir pencereye denk gelmedi.\n" + rapor);

            // REQ-045: after every hit the motor keeps taking the stick, and a window under the path threshold is only
            // accepted when the controller was physically stopped at its sides (REQ-046 crowd), never standing free.
            Assert.AreEqual(0, kontrolKesik,
                "Hasar sonrası " + kontrolKesik + " pencerede motor girdiyi almadı (ControlEnabled / RawInput). " +
                "REQ-045: hasar hareketi, nişanı veya ateşi kesmez.\n" + rapor);
            Assert.AreEqual(0, engelsizDurma,
                "Hasar aldıktan sonraki " + pencereKare + " karede oyuncu yalnız " +
                enKisaYol.ToString("F4") + " m yol aldı (eşik " + yolEsigi + " m, ideal 0.517 m) ve önünde engel yoktu. " +
                "REQ-045: hasar hareketi, nişanı veya ateşi kesmez.\n" + rapor);

            Assert.IsFalse(_can.IsDead,
                "Oyuncu bu senaryoda ölmemeliydi: aritmetik tavan 6 × " + TekVurusHasari +
                " = 60 < " + BaslangicCani + ".\n" + rapor);
        }

        // ------------------------------------------------------------------------ yardımcı

        /// <summary>
        /// Ölçümün yalnız test için doğurulmuş zombileri görmesi için sahne süpürülür:
        /// spawn yöneticisi durdurulur, sahadaki herkes toplanır, sonra halka kurulur.
        /// </summary>
        private void SuruDogur(int adet, float yaricap, StringBuilder rapor)
        {
            Vector3 merkez = _rig.transform.position;
            int reddedilen = 0;

            for (int i = 0; i < adet; i++)
            {
                float aci = i * Mathf.PI * 2f / adet;
                Vector3 nokta = merkez + new Vector3(Mathf.Cos(aci), 0f, Mathf.Sin(aci)) * yaricap;
                Quaternion bakis = Quaternion.LookRotation((merkez - nokta).normalized, Vector3.up);

                ZombieEnemy zombi = EnemyFactory.Spawn(EnemyTier.Standard, 1, nokta, bakis);
                if (zombi == null)
                {
                    reddedilen++;
                }
            }

            rapor.AppendLine("  dogurulan zombi = " + (adet - reddedilen) + " / " + adet +
                             "  (sahnedeki canli toplam " + CanliZombiSayisi() + ")");

            Assert.AreEqual(0, reddedilen,
                reddedilen + " zombi doğurulamadı; ölçüm istenen sürü baskısını taşımıyor.");
        }

        private static int CanliZombiSayisi()
        {
            IReadOnlyList<ZombieEnemy> canli = ZombieEnemy.Live;
            int n = 0;
            for (int i = 0; i < canli.Count; i++)
            {
                if (canli[i] != null && canli[i].IsAlive)
                {
                    n++;
                }
            }

            return n;
        }

        private static bool AktifPenceredeZombiVar()
        {
            IReadOnlyList<ZombieEnemy> canli = ZombieEnemy.Live;
            for (int i = 0; i < canli.Count; i++)
            {
                ZombieEnemy z = canli[i];
                if (z != null && z.AttackPhase == ZombieAttackPhase.Active)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < SahneKurulumSaniye)
            {
                yield return null;
            }

            _rig = PlayerRegistry.Primary;
            Assert.IsNotNull(_rig, "Run sahnesinde oyuncu doğmadı.");

            _can = _rig.Health;
            Assert.IsNotNull(_can, "Oyuncuda PlayerHealth yok.");
            Assert.IsFalse(_can.DiagnosisInvulnerable,
                "Oyuncu tanı dokunulmazlığıyla açıldı; ölçüm geçersiz olurdu.");
            Assert.AreEqual(BaslangicCani, _can.Max, 0.01f,
                "Maksimum can " + _can.Max + "; test eşikleri " + BaslangicCani + " üzerinden türetildi.");

            // Zincirin oyuncu ucu: hasarın gideceği collider tek ve Player katmanında olmalı.
            Assert.AreEqual(PrototypeLayers.Player, _rig.gameObject.layer,
                "Oyuncu kökü Player katmanında değil; EnemyAttackMask onu bulamaz.");

            // Doğal koşu spawn'ı ölçümü kirletir: hem sayıyı hem tier'ı belirsiz yapar.
            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.StopRound();
                EnemySpawnDirector.Instance.ClearAllEnemies();
            }

            yield return null;

            // C-00 30.09 M-20: the natural enemies of the preparation window can land a hit before StopRound (measured
            // 100 → 90); their damage is outside every measurement here, whose bounds are derived from a full 100 —
            // one shared start: full health, asserted.
            _kurulumCani = _can.Current;
            Assert.IsFalse(_can.IsDead, "Oyuncu sahne hazırlığında öldü; ölçüm başlayamaz.");
            _can.HealToFull();
            Assert.AreEqual(BaslangicCani, _can.Current, 0.01f, "Ölçüm tam canla başlamıyor (hazırlık hasarı " + (BaslangicCani - _kurulumCani).ToString("F1") + ").");

            // Kare hızından bağımsız, tekrarlanabilir ölçüm: adım tam 1/60 sn.
            Time.captureDeltaTime = Adim;

            _girdi = new SaldiriTestiGirdisi();
            _rig.Motor.SetInputSource(_girdi);
            _rig.Aim.SetInputSource(_girdi);

            Assert.Less(Mathf.Abs(OyuncuHizi - _rig.Motor.MaxSpeed), 0.01f,
                "Oyuncu hızı " + _rig.Motor.MaxSpeed + " m/s; test eşikleri " + OyuncuHizi +
                " m/s üzerinden türetildi.");
        }

        [TearDown]
        public void Bitir()
        {
            Time.captureDeltaTime = 0f;

            if (_girdi != null)
            {
                _girdi.Yon = Vector2.zero;
            }

            if (_rig != null)
            {
                if (_rig.Motor != null)
                {
                    _rig.Motor.SetInputSource(null);
                }

                if (_rig.Aim != null)
                {
                    _rig.Aim.SetInputSource(null);
                }
            }

            EnemyFactory.ClearAll();

            _rig = null;
            _can = null;
            _girdi = null;
        }
    }
}
