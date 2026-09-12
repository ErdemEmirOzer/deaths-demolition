using System.Collections.Generic;
using DD.Prototype.Navigation;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-002 · YUMUŞAK AYRIŞMA ve OYUNCU FRENİ nöbetçisi. REQ-046 / REQ-047 · DEC-032 / DEC-033.
    ///
    /// <see cref="CrowdSolver"/> düz C# olduğu için sahne, fizik veya NavMesh gerekmez:
    /// sahte <see cref="ICrowdMember"/> üyeleri kurulur, <c>Tick</c> çağrılır ve üyeler
    /// sözleşmenin dediği gibi <c>final = own + correction</c> ile ilerletilir.
    ///
    /// SAYILAR PROTOTYPE-ONLY — ÜRÜN KARARI DEĞİL
    /// -----------------------------------------
    /// CrowdTuning varsayılanları (SpacingScale 1.05, SeparationStiffness 7, MaxSeparation
    /// 2.2 m/s, MaxCorrection 2.8 m/s, PlayerBraceStrength 1, MaxPlayerDepenetration 0.9)
    /// teste sabit yazılmıştır; kilitlenen şey davranıştır:
    ///   1. aynı noktaya doğan iki gövde üst üste KALMAZ (REQ-047);
    ///   2. ayrışma sürüyü DAĞITMAZ — herkes hâlâ hedefe yaklaşır;
    ///   3. oyuncuya yürüyen üye oyuncunun İÇİNE girmez ama dışarı da FIRLATILMAZ
    ///      (fren, itme değil — REQ-046 "zombiler yolu kapatır");
    ///   4. dar koridorda kalıcı jitter/patlama yok (NaN yok, hız tavanı, sonlu varyans).
    ///
    /// EŞİKLER FORMÜLDEN
    /// -----------------
    /// İki Standard gövde (r = 0.45): minDist = (0.45 + 0.45) × 1.05 = 0.945 m. Ayrışma hızı
    /// overlap × 7, tavan 2.2 m/s; kütle payı ½. Tam üst üste başlayan çift, en kötü
    /// ihtimalle 0.945 / (2 × 2.2) = 0.215 s'de ayrılır; 1 s bol pay bırakır. Kabul:
    /// mesafe ≥ 0.9 × minDist = 0.85 m (stiffness'in denge noktasına yaklaşma payı).
    /// </summary>
    [TestFixture]
    public sealed class CrowdSeparationContractTests
    {
        private const float StandardYaricap = 0.45f;     // EnemyTierSettings.BodyRadius (Standard)
        private const float OyuncuYaricap = 0.45f;       // PlayerConfigSection.CollisionRadius
        private const float OyuncuHiz = 6.2f;            // PlayerConfigSection.MoveSpeed
        private const float ZombiHiz = 2.7f;             // EnemyTierSettings.MoveSpeed (Standard)
        private const float SpacingScale = 1.05f;        // CrowdTuning.SpacingScale
        private const float MaxCorrection = 2.8f;        // CrowdTuning.MaxCorrectionSpeed
        private const float Adim = 1f / 60f;

        /// <summary>Sahte üye: kendi lokomosyonu + solver düzeltmesi ile ilerler.</summary>
        private sealed class Uye : ICrowdMember
        {
            public Vector3 Konum;
            public Vector3 Hedef;
            public float Hiz;
            public float Yaricap = StandardYaricap;
            public float Kutle = 1f;
            public Vector3 SonDuzeltme;
            public int DuzeltmeSayisi;

            public Vector3 Position => Konum;
            public float Radius => Yaricap;
            public float Mass => Kutle;
            public Vector3 SteeringTarget => Hedef;

            public void ApplySteering(Vector3 velocity)
            {
                SonDuzeltme = velocity;
                DuzeltmeSayisi++;
            }

            public Vector3 KendiHizi()
            {
                Vector3 d = Hedef - Konum;
                d.y = 0f;
                return d.sqrMagnitude > 0.0001f ? d.normalized * Hiz : Vector3.zero;
            }

            public void Ilerle(float dt)
            {
                Vector3 v = KendiHizi() + SonDuzeltme;
                v.y = 0f;
                Konum += v * dt;
                SonDuzeltme = Vector3.zero;
            }
        }

        private static CrowdSolver YeniSolver()
        {
            var solver = new CrowdSolver();
            solver.Tuning = new CrowdTuning();
            solver.Tuning.Sanitize();
            return solver;
        }

        private static CrowdPlayerState OyuncuYok()
        {
            return new CrowdPlayerState { HasPlayer = false };
        }

        private static void Adimla(CrowdSolver solver, List<Uye> uyeler, in CrowdPlayerState oyuncu, float saniye)
        {
            int kare = Mathf.RoundToInt(saniye / Adim);
            for (int k = 0; k < kare; k++)
            {
                solver.Tick(Adim, oyuncu);
                for (int i = 0; i < uyeler.Count; i++)
                {
                    uyeler[i].Ilerle(Adim);
                }
            }
        }

        private static float YatayMesafe(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ================================================================ sabit tazelik (K3)

        [Test]
        public void SabitTazeligi_CrowdTuningVarsayilanlariylaAyni()
        {
            var t = new CrowdTuning();
            const string not = " — test sabiti CrowdTuning varsayılanından saptı; türetmeyi yenile (PrototypeOnly).";
            Assert.AreEqual(SpacingScale, t.SpacingScale, 0.0001f, "SpacingScale" + not);
            Assert.AreEqual(MaxCorrection, t.MaxCorrectionSpeed, 0.0001f, "MaxCorrectionSpeed" + not);
            Assert.AreEqual(0.45f, new DD.Prototype.Core.Config.EnemyConfigSection().Find(DD.Prototype.Core.Config.EnemyTier.Standard).BodyRadius, 0.0001f, "Standard.BodyRadius" + not);
            Assert.AreEqual(OyuncuYaricap, new DD.Prototype.Core.Config.PlayerConfigSection().CollisionRadius, 0.0001f, "Player.CollisionRadius" + not);
            Assert.AreEqual(OyuncuHiz, new DD.Prototype.Core.Config.PlayerConfigSection().MoveSpeed, 0.0001f, "Player.MoveSpeed" + not);
            Assert.AreEqual(ZombiHiz, new DD.Prototype.Core.Config.EnemyConfigSection().Find(DD.Prototype.Core.Config.EnemyTier.Standard).MoveSpeed, 0.0001f, "Standard.MoveSpeed" + not);
        }

        // ================================================================ REQ-047

        /// <summary>Tam aynı noktaya doğan iki gövde 1 sn içinde ayrışır; ikisi de hedefe yaklaşmaya devam eder.</summary>
        [Test]
        public void AyniNoktayaDoganIkiGovde_UstUsteKalmaz_VeHedefeYaklasmayiSurdurur()
        {
            CrowdSolver solver = YeniSolver();
            Vector3 hedef = new Vector3(0f, 0f, 10f);
            var a = new Uye { Konum = Vector3.zero, Hedef = hedef, Hiz = ZombiHiz };
            var b = new Uye { Konum = Vector3.zero, Hedef = hedef, Hiz = ZombiHiz };
            solver.Register(a);
            solver.Register(b);
            var uyeler = new List<Uye> { a, b };

            float minDist = (StandardYaricap + StandardYaricap) * SpacingScale;
            float baslangicHedefMesafeA = YatayMesafe(a.Konum, hedef);

            Adimla(solver, uyeler, OyuncuYok(), 1f);

            float mesafe = YatayMesafe(a.Konum, b.Konum);
            Assert.IsFalse(float.IsNaN(mesafe), "Ayrışma NaN üretti.");
            Assert.GreaterOrEqual(mesafe, 0.9f * minDist,
                "REQ-047: üst üste doğan iki gövde 1 sn sonra hâlâ " + mesafe.ToString("F3") +
                " m aralıkta; beklenen ≥ " + (0.9f * minDist).ToString("F3") + " m.");

            // Sürü dağılmadı: ikisi de hedefe yaklaştı ve birbirinden "kaçmadı".
            Assert.Less(YatayMesafe(a.Konum, hedef), baslangicHedefMesafeA - 1f,
                "Ayrışma A'nın hedefe yürümesini engelledi (sürü dağıtılmamalı).");
            Assert.Less(YatayMesafe(b.Konum, hedef), baslangicHedefMesafeA - 1f,
                "Ayrışma B'nin hedefe yürümesini engelledi.");
            Assert.Less(mesafe, 3f * minDist,
                "Ayrışma gövdeleri gereğinden fazla uzaklaştırdı: " + mesafe.ToString("F2") + " m.");
            Assert.AreEqual(60, a.DuzeltmeSayisi, "ApplySteering kare başına tam bir kez çağrılmalı.");
        }

        /// <summary>
        /// Kütle payı: ağır gövde (5) hafifi (1) iter, kendisi az kayar (share = mOther / (mA + mB)).
        /// Bu, sürünün akmasını sağlayan kuraldır; Standard-only pakette bile sözleşme korunur.
        /// </summary>
        [Test]
        public void KutlePayi_AgirGovdeAzKayar()
        {
            CrowdSolver solver = YeniSolver();
            var agir = new Uye { Konum = new Vector3(-0.2f, 0f, 0f), Hedef = new Vector3(-0.2f, 0f, 0f), Hiz = 0f, Kutle = 5f };
            var hafif = new Uye { Konum = new Vector3(0.2f, 0f, 0f), Hedef = new Vector3(0.2f, 0f, 0f), Hiz = 0f, Kutle = 1f };
            solver.Register(agir);
            solver.Register(hafif);

            Adimla(solver, new List<Uye> { agir, hafif }, OyuncuYok(), 0.5f);

            float agirKayma = Mathf.Abs(agir.Konum.x + 0.2f);
            float hafifKayma = Mathf.Abs(hafif.Konum.x - 0.2f);
            Assert.Greater(hafifKayma, agirKayma * 3f,
                "Hafif gövde ağırdan en az 3 kat fazla kaymalı (pay 5/6 vs 1/6); ağır " +
                agirKayma.ToString("F3") + " hafif " + hafifKayma.ToString("F3"));
        }

        // ================================================================ REQ-046

        /// <summary>
        /// Oyuncuya doğru yürüyen üye: solver FREN uygular, İTME uygulamaz. Sert çarpışma
        /// (CharacterController) bu testte üyenin kendi "yüzeye geri koy" adımıyla
        /// modellenir; solverın sözleşmesi ise şudur: temas hâlindeyken net dışa hız
        /// hiçbir karede itme kadar büyümez (üye fırlatılmaz), üye temasa ULAŞIR (fren
        /// teması engellemez) ve fren gerçekten çalışır (temas boyunca ortalama fren &gt; 0).
        /// </summary>
        [Test]
        public void OyuncuyaYuruyenUye_FrenlenirAmaItilmez()
        {
            CrowdSolver solver = YeniSolver();
            Vector3 oyuncuKonum = Vector3.zero;
            var uye = new Uye { Konum = new Vector3(0f, 0f, 3f), Hedef = oyuncuKonum, Hiz = ZombiHiz };
            solver.Register(uye);

            var oyuncu = new CrowdPlayerState
            {
                HasPlayer = true,
                Position = oyuncuKonum,
                Radius = OyuncuYaricap,
                Velocity = Vector3.zero
            };

            float sertSinir = StandardYaricap + OyuncuYaricap;
            float minDist = sertSinir + solver.Tuning.PlayerContactPadding;

            bool temasOldu = false;
            float enBuyukDisaHiz = float.MinValue;
            float frenToplam = 0f;
            int temasKare = 0;

            int kare = Mathf.RoundToInt(2f / Adim);
            for (int k = 0; k < kare; k++)
            {
                solver.Tick(Adim, oyuncu);

                Vector3 disari = uye.Konum - oyuncuKonum;
                disari.y = 0f;
                float mesafe = disari.magnitude;
                Vector3 n = mesafe > 0.0001f ? disari / mesafe : Vector3.forward;

                Vector3 net = uye.KendiHizi() + uye.SonDuzeltme;
                if (mesafe <= minDist + 0.05f)
                {
                    temasOldu = true;
                    temasKare++;
                    frenToplam += Vector3.Dot(uye.SonDuzeltme, n);
                    enBuyukDisaHiz = Mathf.Max(enBuyukDisaHiz, Vector3.Dot(net, n));
                }

                uye.Ilerle(Adim);

                // Sert çarpışma: kapsüller iç içe geçemez (CharacterController'ın işi).
                Vector3 d = uye.Konum - oyuncuKonum;
                d.y = 0f;
                if (d.magnitude < sertSinir)
                {
                    uye.Konum = oyuncuKonum + d.normalized * sertSinir;
                }
            }

            float son = YatayMesafe(uye.Konum, oyuncuKonum);
            Assert.IsFalse(float.IsNaN(son), "Oyuncu freni NaN üretti.");
            Assert.IsTrue(temasOldu, "Üye 2 sn'de oyuncuya ulaşamadı; fren temasın kendisini engelliyor.");
            Assert.Greater(frenToplam / Mathf.Max(1, temasKare), 0f,
                "Temas boyunca solver hiç fren uygulamadı; oyuncu freni çalışmıyor.");
            Assert.LessOrEqual(enBuyukDisaHiz, solver.Tuning.MaxPlayerDepenetrationSpeed + 0.05f,
                "REQ-046: temas hâlinde üye dışa " + enBuyukDisaHiz.ToString("F2") +
                " m/s fırlatıldı; sözleşme FREN, itme değil.");
            Assert.LessOrEqual(son, minDist + 0.35f,
                "Üye 2 sn sonra oyuncudan uzakta (" + son.ToString("F3") + " m); itilmiş.");
        }

        /// <summary>
        /// Zaten oyuncunun içinde doğan üye yavaşça dışarı alınır (MaxPlayerDepenetration
        /// 0.9 m/s tavanı), oyuncunun 6.2 m/s'sinin çok altında: oyuncu sürüyü yararak geçemez.
        /// Üyenin hedefi dışarıda (kendi niyeti içeri değil), böylece ölçülen yalnız depenetrasyondur.
        /// </summary>
        [Test]
        public void OyuncununIcindeDoganUye_YavasDepenetrasyonlaCikar()
        {
            CrowdSolver solver = YeniSolver();
            var uye = new Uye { Konum = new Vector3(0.05f, 0f, 0f), Hedef = new Vector3(5f, 0f, 0f), Hiz = 0f };
            solver.Register(uye);
            var oyuncu = new CrowdPlayerState { HasPlayer = true, Position = Vector3.zero, Radius = OyuncuYaricap };

            float enBuyukHiz = 0f;
            int kare = Mathf.RoundToInt(3f / Adim);
            for (int k = 0; k < kare; k++)
            {
                solver.Tick(Adim, oyuncu);
                enBuyukHiz = Mathf.Max(enBuyukHiz, uye.SonDuzeltme.magnitude);
                uye.Ilerle(Adim);
            }

            float son = YatayMesafe(uye.Konum, Vector3.zero);
            Assert.LessOrEqual(enBuyukHiz, solver.Tuning.MaxPlayerDepenetrationSpeed + 0.0001f,
                "Depenetrasyon tavanı aşıldı: " + enBuyukHiz.ToString("F3") + " m/s.");
            Assert.Less(enBuyukHiz, OyuncuHiz * 0.25f,
                "Depenetrasyon oyuncu hızına yaklaşıyor; oyuncu sürüyü yararak geçebilir.");
            Assert.GreaterOrEqual(son, StandardYaricap + OyuncuYaricap - 0.05f,
                "3 sn sonra üye hâlâ oyuncunun içinde: " + son.ToString("F3") + " m.");
        }

        // ================================================================ dar alan

        /// <summary>
        /// 8 gövde 1.2 m genişliğinde bir koridora üst üste doğar ve aynı hedefe yürür.
        /// 5 sn boyunca: NaN yok, düzeltme tavanı aşılmaz, kimse koridor dışına
        /// fırlamaz (koridor sınırı burada üyenin kendi kısıtı olarak modellenir),
        /// son 1 sn'de konum jitter'ı (kare başına ortalama geri-ileri titreme) sınırlı,
        /// ve herkes hedefe yaklaşmış durumda.
        /// </summary>
        [Test]
        public void DarKoridordaSekizGovde_PatlamazJitterlemezVeAkar()
        {
            CrowdSolver solver = YeniSolver();
            Vector3 hedef = new Vector3(0f, 0f, 30f);
            var uyeler = new List<Uye>(8);
            for (int i = 0; i < 8; i++)
            {
                var u = new Uye { Konum = new Vector3(0f, 0f, i * 0.05f), Hedef = hedef, Hiz = ZombiHiz };
                uyeler.Add(u);
                solver.Register(u);
            }

            const float yariGenislik = 0.6f;
            float enBuyukDuzeltme = 0f;
            var oncekiHiz = new Vector3[8];
            int yonDegisimi = 0;
            int olcumKare = 0;

            int kare = Mathf.RoundToInt(5f / Adim);
            for (int k = 0; k < kare; k++)
            {
                solver.Tick(Adim, OyuncuYok());
                for (int i = 0; i < uyeler.Count; i++)
                {
                    Uye u = uyeler[i];
                    enBuyukDuzeltme = Mathf.Max(enBuyukDuzeltme, u.SonDuzeltme.magnitude);

                    Vector3 v = u.KendiHizi() + u.SonDuzeltme;
                    if (k >= kare - 60)
                    {
                        // Son saniyede ileri (z) hızın işaret değişimi = geri-ileri titreme.
                        if (oncekiHiz[i].z * v.z < 0f)
                        {
                            yonDegisimi++;
                        }

                        olcumKare++;
                    }

                    oncekiHiz[i] = v;
                    u.Ilerle(Adim);

                    // Koridor duvarı: üyenin kendi NavMesh clamp'inin karşılığı.
                    u.Konum.x = Mathf.Clamp(u.Konum.x, -yariGenislik, yariGenislik);
                }
            }

            for (int i = 0; i < uyeler.Count; i++)
            {
                Assert.IsFalse(float.IsNaN(uyeler[i].Konum.x) || float.IsNaN(uyeler[i].Konum.z),
                    "Üye " + i + " NaN konuma düştü.");
                Assert.Greater(uyeler[i].Konum.z, 5f,
                    "Üye " + i + " 5 sn'de koridorda ilerleyemedi (z=" + uyeler[i].Konum.z.ToString("F2") +
                    "); ayrışma geçidi kilitledi.");
            }

            Assert.LessOrEqual(enBuyukDuzeltme, MaxCorrection + 0.0001f,
                "Düzeltme tavanı aşıldı: " + enBuyukDuzeltme.ToString("F3") + " m/s.");

            float titremeOrani = olcumKare > 0 ? (float)yonDegisimi / olcumKare : 0f;
            Assert.Less(titremeOrani, 0.05f,
                "Son saniyede ileri hız işaretini karelerin %" + (titremeOrani * 100f).ToString("F1") +
                "'inde değiştirdi; kalıcı jitter.");

            // Kimse üst üste değil.
            float minDist = 2f * StandardYaricap * SpacingScale;
            for (int i = 0; i < uyeler.Count; i++)
            {
                for (int j = i + 1; j < uyeler.Count; j++)
                {
                    Assert.Greater(YatayMesafe(uyeler[i].Konum, uyeler[j].Konum), 0.5f * minDist,
                        "Üye " + i + " ve " + j + " 5 sn sonra hâlâ iç içe.");
                }
            }
        }

        /// <summary>Unregister edilen üye bir daha düzeltme almaz; ölü zombi sürüyü etkilemez.</summary>
        [Test]
        public void UnregisterSonrasi_UyeDuzeltmeAlmaz()
        {
            CrowdSolver solver = YeniSolver();
            var a = new Uye { Konum = Vector3.zero, Hedef = Vector3.forward * 5f, Hiz = 0f };
            var b = new Uye { Konum = Vector3.zero, Hedef = Vector3.forward * 5f, Hiz = 0f };
            solver.Register(a);
            solver.Register(b);
            solver.Tick(Adim, OyuncuYok());
            Assert.AreEqual(1, b.DuzeltmeSayisi);

            solver.Unregister(b);
            solver.Tick(Adim, OyuncuYok());
            Assert.AreEqual(1, b.DuzeltmeSayisi, "Unregister sonrası ApplySteering çağrıldı.");
            Assert.AreEqual(1, solver.MemberCount);
            Assert.AreEqual(Vector3.zero, a.SonDuzeltme, "Tek başına kalan üye düzeltme almamalı.");
        }
    }
}
