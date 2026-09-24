using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-002 · HIT REACTION SÖZLEŞMESİ nöbetçisi. REQ-121 / REQ-122 / REQ-123 / REQ-124 /
    /// REQ-127 · DEC-015.
    ///
    /// NEDEN VAR
    /// ---------
    /// Vuruş hissi prototipten itibaren temel kalite hedefi (DEC-015); ama "hafif darbeler
    /// düşmanı her zaman durdurmaz" (REQ-123), "sürekli stun-lock önlenir" (REQ-124) ve
    /// "otomatik silahın her mermisi hit-stop üretmez" (REQ-127) kuralları şimdiye kadar
    /// yalnız kod yorumuyla korunuyordu. Bu dosya üçünü ölçülebilir hâle getirir:
    /// <see cref="EnemyHitReactionState"/> ve <see cref="StaggerGovernor"/> düz C# sınıfları
    /// olduğu için sahne gerekmez.
    ///
    /// SAYILAR PROTOTYPE-ONLY — ÜRÜN KARARI DEĞİL
    /// -----------------------------------------
    /// Aşağıdaki sabitler kodun bugünkü placeholder değerleridir (DamageForce.RifleBullet 1.0,
    /// ShotgunPellet 0.85, StaggerImpactThreshold 2.4, ImpactHalfLife 0.12 s, StaggerCooldown
    /// 1.1 s, FireRate 8.5/s). Teste SABİT yazılmışlardır ki test kendi kendini doğrulamasın;
    /// bir tasarımcı sayıyı değiştirirse test kırılır ve türetmenin yeniden yapılması gerekir.
    /// Hiçbir sayı bu testle "final" ilan edilmez; kilitlenen şey SÖZLEŞME'dir: sürekli
    /// otomatik ateş tek başına stagger üretmez, ağır tek darbe üretir, üst üste stagger
    /// cooldown'la engellenir, hit-stop yalnız ağır olaylarda önerilir.
    ///
    /// EŞİKLER FORMÜLDEN
    /// -----------------
    /// Akümülatör her karede 0.5^(dt / 0.12) ile söner. 8.5 atış/s ile aralık 0.1176 s;
    /// aralık başına kalan oran r = 0.5^(0.1176/0.12) = 0.5069. Kararlı durum tepe değeri
    /// A = impact / (1 − r) = 1.0 / 0.4931 = 2.028 &lt; 2.4 (eşik). Yani yürüyen Standard
    /// zombi AR ile hiç stagger olmaz; telegraf bonusu (×1.35) ile 2.74 &gt; 2.4 olur —
    /// bu da "hazırlanan saldırıyı kesmek" tasarımıdır ve ayrı sınanır.
    /// </summary>
    [TestFixture]
    public sealed class HitReactionContractTests
    {
        // ---------------------------------------------------------- PrototypeOnly sabitleri
        // WP-069 B2: Hinawa (ar_1) 600 RPM with impulse 0.13 on the rifle scale (0.15 = DamageForce.RifleBullet) —
        // the impulse was lowered so all three REQ-123 properties hold at the council's fire rate.
        private const float TufekMermisi = 1.0f * 0.13f / 0.15f;   // WeaponImpulse.ForClassic(ar_1)
        private const float PompaSacmasi = 0.85f;         // DamageForce.ShotgunPellet
        private const int SacmaSayisi = 8;                // ClassicWeaponSettings.PelletCount (shotgun)
        private const float StaggerEsigi = 2.4f;          // EnemyHitReactionState.StaggerImpactThreshold
        private const float YariOmur = 0.12f;             // EnemyHitReactionState.ImpactHalfLife
        private const float StaggerBekleme = 1.1f;        // FeedbackConfigSection.StaggerCooldown
        private const float TufekAtisHizi = 10f;          // ClassicWeaponSettings.FireRate (ar_1, 600 RPM)
        private const float AgirDarbeEsigi = 2.5f;        // EnemyHitReactionState.HeavyImpactThreshold
        private const float Adim = 1f / 60f;

        private static DamageHit Vurus(float impact, BodyRegion region = BodyRegion.UpperBody)
        {
            return new DamageHit
            {
                Amount = 11f,
                Region = region,
                Point = Vector3.zero,
                Direction = Vector3.forward,
                ImpactForce = impact,
                Source = DamageSourceKind.Bullet
            };
        }

        private static DamageResult AcikVurus(BodyRegion region = BodyRegion.UpperBody)
        {
            return new DamageResult
            {
                HealthDamage = 11f,
                ArmorDamage = 0f,
                ArmorBroke = false,
                Killed = false,
                WasArmored = false,
                Region = region
            };
        }

        private static EnemyHitReactionState YeniDurum()
        {
            // Config null: sınıf kendi fallback'lerini kullanır; bunlar
            // FeedbackConfigSection varsayılanlarını birebir yansıtır (dosya başlığı).
            var state = new EnemyHitReactionState();
            state.ResetForSpawn();
            return state;
        }

        // ================================================================ sabit tazelik (K3)

        /// <summary>Test sabitleri config/kod sabitlerinden sapınca kırılır: "türetmeyi yenile". Sayı kilitlemez.</summary>
        [Test]
        public void SabitTazeligi_ConfigVeKodSabitleriyleAyni()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config, "PrototypeConfig Resources'tan yüklenemedi.");
            const string not = " — test sabiti kaynaktan saptı; türetmeyi yenile (PrototypeOnly, ürün kararı değil).";
            Assert.AreEqual(1.0f, DamageForce.RifleBullet, 0.0001f, "DamageForce.RifleBullet" + not);
            Assert.AreEqual(TufekMermisi, DD.Prototype.Weapons.WeaponImpulse.ForClassic(config.Weapons.Find(ClassicWeaponId.AssaultRifle)), 0.0001f, "ar_1 mermi itkisi" + not);
            Assert.AreEqual(PompaSacmasi, DamageForce.ShotgunPellet, 0.0001f, "DamageForce.ShotgunPellet" + not);
            Assert.AreEqual(StaggerEsigi, EnemyHitReactionState.StaggerImpactThreshold, 0.0001f, "StaggerImpactThreshold" + not);
            Assert.AreEqual(StaggerBekleme, config.Feedback.StaggerCooldown, 0.0001f, "Feedback.StaggerCooldown" + not);
            Assert.AreEqual(StaggerBekleme, new FeedbackConfigSection().StaggerCooldown, 0.0001f, "FeedbackConfigSection varsayılanı" + not);
            Assert.AreEqual(TufekAtisHizi, config.Weapons.Find(ClassicWeaponId.AssaultRifle).FireRate, 0.0001f, "AssaultRifle.FireRate" + not);
            Assert.AreEqual(SacmaSayisi, config.Weapons.Find(ClassicWeaponId.CombatShotgun).PelletCount, "CombatShotgun.PelletCount" + not);
        }

        // ================================================================ REQ-123

        /// <summary>
        /// Sürekli AR ateşi (1.0 impact, 8.5/s), yürüyen Standard zombi: 3 saniye boyunca
        /// TEK BİR stagger bile olmamalı. Akümülatör tepe değeri formülle 2.03; test hem
        /// stagger sayısını hem de ölçülen tepe değerini formülün %5 payına vurur.
        /// </summary>
        [Test]
        public void SurekliTufekAtesi_YuruyenZombiyi_HicStaggerEtmez()
        {
            EnemyHitReactionState durum = YeniDurum();

            float aralik = 1f / TufekAtisHizi;
            float kalanOran = Mathf.Pow(0.5f, aralik / YariOmur);
            float beklenenTepe = TufekMermisi / (1f - kalanOran);

            Assert.Less(beklenenTepe, StaggerEsigi,
                "Türetme hatası: formül tepesi eşiğin üstünde; test önermesi geçersiz.");

            int stagger = 0;
            float olculenTepe = 0f;
            float sonrakiAtis = 0f;
            float zaman = 0f;

            while (zaman < 3f)
            {
                durum.Tick(Adim);
                zaman += Adim;

                if (zaman >= sonrakiAtis)
                {
                    sonrakiAtis += aralik;
                    HitReaction r = durum.Evaluate(
                        Vurus(TufekMermisi), AcikVurus(), false, false, false, false);

                    if (r.Stagger)
                    {
                        stagger++;
                    }

                    olculenTepe = Mathf.Max(olculenTepe, durum.ImpactCharge);
                    Assert.AreEqual(0f, r.SuggestedHitStop, 0.0001f,
                        "REQ-127: sıradan AR mermisi hit-stop önerdi.");
                }
            }

            Assert.AreEqual(0, stagger,
                "REQ-123: sürekli hafif otomatik ateş yürüyen zombiyi " + stagger +
                " kez staggerladı; akümülatör tepesi " + olculenTepe.ToString("F3") +
                ", eşik " + StaggerEsigi + ".");
            Assert.Less(olculenTepe, StaggerEsigi, "Akümülatör eşiği aştı ama stagger sayılmadı.");
            Assert.AreEqual(beklenenTepe, olculenTepe, beklenenTepe * 0.05f,
                "Akümülatör tepesi formülden (%5 pay) saptı: sönme yarı ömrü veya impact değişmiş.");
        }

        /// <summary>
        /// Gerçek kovalama durumu: tam hızda yürüyen Standard, ZombieAnimator'da Run pozundadır
        /// (speed01 &gt; 0.72) ve koşma bonusu ×1.15 uygulanır: 2.028 × 1.15 = 2.33 &lt; 2.4.
        /// Pay yalnız %3 — bu test o payın nöbetçisidir; FireRate/yarı ömür %3'ten fazla
        /// oynarsa kırılır ve türetme yenilenir (ürün kararı: ertelenen).
        /// </summary>
        [Test]
        public void SurekliTufekAtesi_KosanZombiyi_HicStaggerEtmez_PayUcYuzde()
        {
            EnemyHitReactionState durum = YeniDurum();
            float aralik = 1f / TufekAtisHizi;
            float kalanOran = Mathf.Pow(0.5f, aralik / YariOmur);
            float beklenenSkor = TufekMermisi / (1f - kalanOran) * 1.15f;   // RunningStaggerBonus
            Assert.Less(beklenenSkor, StaggerEsigi, "Türetme: koşma bonuslu skor eşiğin üstünde; önerme geçersiz.");

            int stagger = 0;
            float sonrakiAtis = 0f;
            float zaman = 0f;
            while (zaman < 3f)
            {
                durum.Tick(Adim);
                zaman += Adim;
                if (zaman >= sonrakiAtis)
                {
                    sonrakiAtis += aralik;
                    if (durum.Evaluate(Vurus(TufekMermisi), AcikVurus(), false, true, false, false).Stagger)
                    {
                        stagger++;
                    }
                }
            }

            Assert.AreEqual(0, stagger,
                "REQ-123: sürekli AR ateşi KOŞAN Standard'ı " + stagger + " kez staggerladı (skor " +
                beklenenSkor.ToString("F3") + " / eşik " + StaggerEsigi + ", pay %" +
                ((StaggerEsigi - beklenenSkor) / StaggerEsigi * 100f).ToString("F1") + ").");
        }

        /// <summary>
        /// Aynı ateş, ama zombi saldırı hazırlığında (isTelegraphing): telegraf bonusu ×1.35
        /// ile 2.03 × 1.35 = 2.74 &gt; 2.4 → hazırlanan saldırı KESİLİR. Bu, REQ-123'ün öbür
        /// yüzü: hafif silah yürüyeni durdurmaz ama savunma değeri taşır.
        /// </summary>
        [Test]
        public void SurekliTufekAtesi_TelegrafSirasinda_StaggerUretir()
        {
            EnemyHitReactionState durum = YeniDurum();
            float aralik = 1f / TufekAtisHizi;
            float sonrakiAtis = 0f;
            float zaman = 0f;
            bool staggerGoruldu = false;

            while (zaman < 2f && !staggerGoruldu)
            {
                durum.Tick(Adim);
                zaman += Adim;

                if (zaman >= sonrakiAtis)
                {
                    sonrakiAtis += aralik;
                    HitReaction r = durum.Evaluate(
                        Vurus(TufekMermisi), AcikVurus(), true, false, false, false);
                    staggerGoruldu = r.Stagger;
                }
            }

            Assert.IsTrue(staggerGoruldu,
                "Telegraf bonusu çalışmıyor: 2 sn sürekli ateş hazırlanan saldırıyı kesmedi.");
        }

        // ================================================================ REQ-124

        /// <summary>
        /// Tam pompa (8 × 0.85 = 6.8) tek karede staggerlar. Aynı zombiye 0.5 sn sonra
        /// ikinci tam pompa: cooldown (1.1 s) dolmadığı için İKİNCİ STAGGER YOK; cooldown
        /// dolunca üçüncü pompa yine staggerlar. Akümülatör stagger'da sıfırlanır.
        /// </summary>
        [Test]
        public void ArtArdaPompa_CooldownIcinde_IkinciStaggerYok_SonraYenidenMumkun()
        {
            EnemyHitReactionState durum = YeniDurum();

            int ilkStagger = PompaSalvosu(durum);
            Assert.AreEqual(1, ilkStagger,
                "İlk tam pompa (8 × " + PompaSacmasi + " = " + (SacmaSayisi * PompaSacmasi) +
                " ≥ " + StaggerEsigi + ") tam bir stagger üretmeli; " + ilkStagger + " üretti.");

            // Sacmalar aynı karede tek tek değerlendirilir: stagger k = ceil(2.4 / 0.85) = 3.
            // sacmada düşer ve akümülatörü sıfırlar; kalan 8 − 3 = 5 sacma cooldown altında
            // yalnız birikir (5 × 0.85 = 4.25). Yani "harcandı" = birikim, salvonun tamamı değil.
            int staggerSacmasi = Mathf.CeilToInt(StaggerEsigi / PompaSacmasi);
            float beklenenKalan = (SacmaSayisi - staggerSacmasi) * PompaSacmasi;
            Assert.AreEqual(beklenenKalan, durum.ImpactCharge, 0.001f,
                "Stagger akümülatörü harcamadı: kalan " + durum.ImpactCharge.ToString("F2") +
                ", beklenen (8 − " + staggerSacmasi + ") × " + PompaSacmasi + " = " + beklenenKalan.ToString("F2"));
            Assert.IsTrue(durum.StaggerOnCooldown, "Stagger sonrası cooldown başlamadı (REQ-124).");

            Ilerle(durum, 0.5f);
            int ikinciStagger = PompaSalvosu(durum);
            Assert.AreEqual(0, ikinciStagger,
                "REQ-124: cooldown içinde ikinci pompa yeniden staggerladı — stun-lock mümkün.");
            Assert.IsTrue(durum.StaggerOnCooldown, "0.5 sn sonra cooldown erken bitti.");

            // 0.5 + 0.7 = 1.2 s > 1.1 s cooldown
            Ilerle(durum, 0.7f);
            Assert.IsFalse(durum.StaggerOnCooldown, "Cooldown " + StaggerBekleme + " s sonra bitmedi.");

            int ucuncuStagger = PompaSalvosu(durum);
            Assert.AreEqual(1, ucuncuStagger,
                "Cooldown dolduktan sonra tam pompa yeniden staggerlamalı; kalıcı bağışıklık oluştu.");
        }

        /// <summary>
        /// Stagger süresi tavanla sınırlı (MaxStaggerDuration 0.45 s fallback); şiddet ve
        /// süre eşik aşımına göre artar ama sonsuz değildir — "süresiz tepkide tutmaz".
        /// </summary>
        [Test]
        public void StaggerSuresi_TavaniAsmaz()
        {
            EnemyHitReactionState durum = YeniDurum();
            HitReaction r = durum.Evaluate(Vurus(40f), AcikVurus(), false, false, false, false);

            Assert.IsTrue(r.Stagger, "40 impact stagger üretmedi.");
            Assert.LessOrEqual(r.StaggerDuration, 0.45f + 0.0001f,
                "Stagger süresi tavanı (0.45 s) aşıldı: " + r.StaggerDuration);
            Assert.LessOrEqual(r.StaggerIntensity, 2.4f + 0.0001f, "Stagger şiddeti tavanı aşıldı.");
        }

        // ================================================================ REQ-127

        /// <summary>Hit-stop önerisi: hafif/orta tek darbe 0; ağır tek darbe &gt; 0; ağır tier ölümü en büyük.</summary>
        [Test]
        public void HitStopOnerisi_YalnizAgirOlaylarda()
        {
            HitReaction hafif = YeniDurum().Evaluate(Vurus(TufekMermisi), AcikVurus(), false, false, false, false);
            Assert.AreEqual(0f, hafif.SuggestedHitStop, 0.0001f, "AR mermisi hit-stop önerdi (REQ-127).");

            HitReaction tekSacma = YeniDurum().Evaluate(Vurus(PompaSacmasi), AcikVurus(), false, false, false, false);
            Assert.AreEqual(0f, tekSacma.SuggestedHitStop, 0.0001f, "Tek sacma hit-stop önerdi.");

            HitReaction agir = YeniDurum().Evaluate(Vurus(AgirDarbeEsigi), AcikVurus(), false, false, false, false);
            Assert.Greater(agir.SuggestedHitStop, 0f, "Ağır darbe eşiğinde hit-stop önerilmedi.");

            HitReaction olumStandard = YeniDurum().Evaluate(Vurus(TufekMermisi), AcikVurus(), false, false, true, false);
            Assert.AreEqual(0f, olumStandard.SuggestedHitStop, 0.0001f,
                "Standard ölümü hit-stop önerdi; yalnız ağır tier ölümü önermeli.");

            HitReaction olumAgir = YeniDurum().Evaluate(Vurus(TufekMermisi), AcikVurus(), false, false, true, true);
            Assert.Greater(olumAgir.SuggestedHitStop, agir.SuggestedHitStop,
                "Ağır tier ölümü hiyerarşinin tepesi olmalı (REQ-125).");
        }

        // ================================================================ REQ-122

        /// <summary>
        /// Bağlam girdileri tepkiyi değiştirir: kafa &gt; gövde &gt; bacak flinch; zırhlı vuruş
        /// daha az flinch; tekrarlı darbe azalan getiri. Yön, hit.Direction ile animatöre
        /// olduğu gibi taşınır (knockback yönü ile kanıtlanır).
        /// </summary>
        [Test]
        public void Baglam_BolgeZirhVeTekrar_FlinchiDegistirir()
        {
            float kafa = YeniDurum().Evaluate(Vurus(1f, BodyRegion.Head), AcikVurus(BodyRegion.Head), false, false, false, false).FlinchIntensity;
            float govde = YeniDurum().Evaluate(Vurus(1f), AcikVurus(), false, false, false, false).FlinchIntensity;
            float bacak = YeniDurum().Evaluate(Vurus(1f, BodyRegion.LowerBody), AcikVurus(BodyRegion.LowerBody), false, false, false, false).FlinchIntensity;

            Assert.Greater(kafa, govde, "Kafa vuruşu gövdeden daha güçlü flinch üretmeli.");
            Assert.Greater(govde, bacak, "Gövde vuruşu bacaktan daha güçlü flinch üretmeli.");

            DamageResult zirhli = AcikVurus();
            zirhli.WasArmored = true;
            zirhli.HealthDamage = 0f;
            zirhli.ArmorDamage = 11f;
            float zirh = YeniDurum().Evaluate(Vurus(1f), zirhli, false, false, false, false).FlinchIntensity;
            Assert.Less(zirh, govde, "Zırhlı vuruş açık vuruştan daha az flinch üretmeli (REQ-125).");

            EnemyHitReactionState tekrar = YeniDurum();
            float ilk = tekrar.Evaluate(Vurus(1f), AcikVurus(), false, false, false, false).FlinchIntensity;
            float ikinci = tekrar.Evaluate(Vurus(1f), AcikVurus(), false, false, false, false).FlinchIntensity;
            Assert.Less(ikinci, ilk, "Tekrarlı darbede azalan getiri yok; SMG gövdeyi titretir.");

            // Yön: knockback eşiğini aşan ağır darbe, vuruş yönünde iter.
            DamageHit sagaDogru = Vurus(40f);
            sagaDogru.Direction = Vector3.right;
            HitReaction agir = YeniDurum().Evaluate(sagaDogru, AcikVurus(), false, false, false, false);
            Assert.Greater(Vector3.Dot(agir.KnockbackVelocity, Vector3.right), 0f,
                "Knockback vuruş yönünü (hit.Direction) taşımıyor.");
        }

        // ================================================================ StaggerGovernor

        /// <summary>
        /// Backstop: tam bir pompa (6.8) dokunulmadan geçer; tavan (9) aşılınca fazlası
        /// damped (%15). Clear() pencereyi unutur — havuzdan dönen id miras almaz.
        /// </summary>
        [Test]
        public void StaggerGovernor_TamPompayiGecirir_PatolojikBurstuKisar()
        {
            StaggerGovernor.Clear();
            StaggerGovernor.ResetCounters();

            const int hedef = 4242;
            float toplam = 0f;
            for (int i = 0; i < SacmaSayisi; i++)
            {
                float g = StaggerGovernor.Govern(hedef, PompaSacmasi);
                Assert.AreEqual(PompaSacmasi, g, 0.0001f, "Sacma " + i + " tavan altında kısıldı.");
                toplam += g;
            }

            Assert.AreEqual(SacmaSayisi * PompaSacmasi, toplam, 0.001f);
            Assert.AreEqual(0, StaggerGovernor.ThrottledCount, "Meşru pompa kısıldı sayıldı.");

            // 6.8 + 0.85 + 0.85 + 0.85 = 9.35 ≥ 9 → bundan sonraki impuls damped
            StaggerGovernor.Govern(hedef, PompaSacmasi);
            StaggerGovernor.Govern(hedef, PompaSacmasi);
            StaggerGovernor.Govern(hedef, PompaSacmasi);
            float fazla = StaggerGovernor.Govern(hedef, PompaSacmasi);

            Assert.AreEqual(PompaSacmasi * StaggerGovernor.DampedFraction, fazla, 0.0001f,
                "Tavan aşımında impuls DampedFraction ile kısılmalı.");
            Assert.AreEqual(1, StaggerGovernor.ThrottledCount);

            StaggerGovernor.Clear();
            float temiz = StaggerGovernor.Govern(hedef, PompaSacmasi);
            Assert.AreEqual(PompaSacmasi, temiz, 0.0001f, "Clear() sonrası pencere miras kaldı.");

            StaggerGovernor.Clear();
            StaggerGovernor.ResetCounters();
        }

        /// <summary>WeaponImpulse: sevkiyat trim değerleriyle AR tam 1.00, SMG 0.55, pompa sacması 0.85.</summary>
        [Test]
        public void WeaponImpulse_SevkiyatVarsayilanlari_ReaksiyonOlcegineTamOturur()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config, "PrototypeConfig Resources'tan yüklenemedi.");

            Assert.AreEqual(DamageForce.RifleBullet,
                WeaponImpulse.ForClassic(config.Weapons.Find(ClassicWeaponId.AssaultRifle)), 0.0001f);
            Assert.AreEqual(DamageForce.SmgBullet,
                WeaponImpulse.ForClassic(config.Weapons.Find(ClassicWeaponId.AutoSmg)), 0.0001f);
            Assert.AreEqual(DamageForce.ShotgunPellet,
                WeaponImpulse.ForClassic(config.Weapons.Find(ClassicWeaponId.CombatShotgun)), 0.0001f);
        }

        // ================================================================ yardımcı

        /// <summary>Bir karede 8 sacma; dönen değer o salvoda üretilen stagger sayısı.</summary>
        private static int PompaSalvosu(EnemyHitReactionState durum)
        {
            int stagger = 0;
            for (int i = 0; i < SacmaSayisi; i++)
            {
                HitReaction r = durum.Evaluate(Vurus(PompaSacmasi), AcikVurus(), false, false, false, false);
                if (r.Stagger)
                {
                    stagger++;
                }
            }

            return stagger;
        }

        private static void Ilerle(EnemyHitReactionState durum, float saniye)
        {
            int kare = Mathf.RoundToInt(saniye / Adim);
            for (int i = 0; i < kare; i++)
            {
                durum.Tick(Adim);
            }
        }
    }
}
