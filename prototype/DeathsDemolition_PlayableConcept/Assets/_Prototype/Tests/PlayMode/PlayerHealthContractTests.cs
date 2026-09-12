using System.Collections;
using DD.Prototype.PlayerSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-002 · SAĞLIK, REGEN ve ÖLÜM nöbetçisi. REQ-041 / REQ-044 / REQ-048 · DEC-027 / DEC-030 / DEC-034.
    ///
    /// SAHNE YOK: yalnız bir <see cref="PlayerHealth"/> bileşeni, PrototypeConfig
    /// Resources'tan bağlanır. Zaman <c>Time.captureDeltaTime</c> ile 1/60 sabittir; ölçüm
    /// kare hızından bağımsızdır.
    ///
    /// SAYILAR PROTOTYPE-ONLY — ÜRÜN KARARI DEĞİL
    /// -----------------------------------------
    /// MaxHealth 100, HealthRegenDelay 4.5 s, HealthRegenRate 12 HP/s, tavan 1.0 bugünkü
    /// placeholder değerlerdir (PlayerConfigSection, PendingDecisions.HealthRegen). Teste
    /// SABİT yazılmışlardır ki test kendi kendini doğrulamasın. Kilitlenen şey SÖZLEŞME'dir:
    ///   * hasarsız gecikme dolmadan regen yok, dolunca var, yeni hasar sayacı sıfırlar;
    ///   * regen tavanı aşmaz;
    ///   * aynı karede iki hasar ikisi de tam uygulanır (i-frame yok);
    ///   * can sıfırda TAM BİR ölüm olayı; sonrasında hasar, iyileşme ve regen yok (revive yok).
    /// </summary>
    [TestFixture]
    public sealed class PlayerHealthContractTests
    {
        private const float MaxCan = 100f;            // PlayerConfigSection.MaxHealth
        private const float RegenGecikme = 4.5f;      // PlayerConfigSection.HealthRegenDelay
        private const float RegenHiz = 12f;           // PlayerConfigSection.HealthRegenRate
        private const float Adim = 1f / 60f;

        private GameObject _go;
        private PlayerHealth _can;

        [UnitySetUp]
        public IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = Adim;

            _go = new GameObject("PlayerHealthContractTests");
            _can = _go.AddComponent<PlayerHealth>();
            yield return null;

            Assert.AreEqual(MaxCan, _can.Max, 0.01f,
                "Maksimum can " + _can.Max + "; test sabitleri " + MaxCan + " üzerinden türetildi.");
            Assert.AreEqual(RegenGecikme, _can.RegenDelaySeconds, 0.001f, "Regen gecikmesi test sabitinden farklı.");
            Assert.AreEqual(RegenHiz, _can.RegenRatePerSecond, 0.001f, "Regen hızı test sabitinden farklı.");
            Assert.IsFalse(_can.IsDead);
        }

        [TearDown]
        public void Yik()
        {
            Time.captureDeltaTime = 0f;
            if (_go != null)
            {
                Object.Destroy(_go);
            }

            _go = null;
            _can = null;
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Bekle(float saniye)
        {
            int kare = Mathf.RoundToInt(saniye / Adim);
            for (int i = 0; i < kare; i++)
            {
                yield return null;
            }
        }

        // ================================================================ REQ-041

        /// <summary>
        /// 30 hasar → gecikmenin %90'ında can hâlâ 70 (regen yok) → gecikme + 1 s'de can
        /// 70 + 12 × 1 = 82 (±1 kare) → yeni hasar sayacı sıfırlar: 1 s sonra hâlâ değişmez.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Regen_GecikmedenSonraBaslar_HasarSayaciSifirlar()
        {
            Assert.IsTrue(_can.ApplyDamage(30f, Vector3.zero, "Test.Hit"));
            Assert.AreEqual(70f, _can.Current, 0.01f);

            yield return Bekle(RegenGecikme * 0.9f);
            Assert.AreEqual(70f, _can.Current, 0.01f,
                "REQ-041: hasarsız süre dolmadan regen başladı (can " + _can.Current + ").");
            Assert.IsFalse(_can.IsRegenerating);

            yield return Bekle(RegenGecikme * 0.1f + 1f);
            float beklenen = 70f + RegenHiz * 1f;
            Assert.IsTrue(_can.IsRegenerating, "Gecikme dolduktan sonra regen başlamadı.");
            Assert.AreEqual(beklenen, _can.Current, RegenHiz * Adim * 2f + 0.01f,
                "Regen hızı formülden saptı: beklenen ≈ " + beklenen + ", ölçülen " + _can.Current);

            // Yeni hasar: sayaç sıfırlanır, regen durur.
            float onceki = _can.Current;
            _can.ApplyDamage(5f, Vector3.zero, "Test.Hit");
            Assert.AreEqual(onceki - 5f, _can.Current, 0.01f);
            Assert.IsFalse(_can.IsRegenerating, "Hasar regen'i durdurmadı.");
            Assert.AreEqual(RegenGecikme, _can.SecondsUntilRegen, 0.05f, "Hasar sonrası bekleme yeniden başlamadı.");

            float hasarSonrasi = _can.Current;
            yield return Bekle(1f);
            Assert.AreEqual(hasarSonrasi, _can.Current, 0.01f,
                "Yeni hasardan 1 sn sonra can değişti; regen gecikmesi yeniden başlamamış.");
        }

        /// <summary>Regen maksimum canı aşmaz; tam canda regen yoktur.</summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Regen_MaksimumCaniAsmaz()
        {
            _can.ApplyDamage(6f, Vector3.zero, "Test.Hit");
            // 6 HP, 12 HP/s ile 0.5 s'de dolar; 2 s fazladan beklenir.
            yield return Bekle(RegenGecikme + 2.5f);

            Assert.AreEqual(MaxCan, _can.Current, 0.01f, "Regen tam cana ulaşmadı.");
            Assert.LessOrEqual(_can.Current, _can.Max, "Regen maksimum canı aştı.");
            Assert.IsFalse(_can.IsRegenerating, "Tam canda regen bayrağı açık.");

            yield return Bekle(1f);
            Assert.AreEqual(MaxCan, _can.Current, 0.01f, "Tam canda can değişti.");
        }

        // ================================================================ REQ-044

        /// <summary>Aynı karede iki farklı kaynaktan hasar: ikisi de tam uygulanır, iki Damaged olayı.</summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AyniKaredeIkiHasar_IkisiDeTamUygulanir()
        {
            int olay = 0;
            _can.Damaged += _ => olay++;

            bool a = _can.ApplyDamage(new PlayerDamageRequest(10f, Vector3.zero, Vector3.left, "Zombie.A"));
            bool b = _can.ApplyDamage(new PlayerDamageRequest(15f, Vector3.zero, Vector3.right, "Zombie.B"));

            Assert.IsTrue(a && b, "İkinci hasar reddedildi (REQ-044: dokunulmazlık yok).");
            Assert.AreEqual(MaxCan - 25f, _can.Current, 0.01f, "Eşzamanlı hasarlar üst üste uygulanmadı.");
            Assert.AreEqual(2, olay, "İki hasar için iki Damaged olayı beklenir.");
            Assert.AreEqual(0f, _can.TimeSinceDamage, 0.0001f);
            yield return null;
        }

        // ================================================================ REQ-048

        /// <summary>
        /// Öldürücü hasar + aynı karede fazladan hasar: Died ve AnyPlayerDied TAM BİR kez;
        /// ikinci hasar false döner; Heal/HealToFull etkisiz; regen gecikmesi dolsa bile can 0.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator CanSifirda_TekOlumOlayi_ReviveYok()
        {
            int died = 0;
            int anyDied = 0;
            int damaged = 0;
            PlayerDeathInfo son = default;
            System.Action<PlayerDeathInfo> anyDinle = _ => anyDied++;
            PlayerHealthSnapshot sonSnapshot = default;
            int olumSnapshot = 0;
            _can.Died += info => { died++; son = info; };
            PlayerHealth.AnyPlayerDied += anyDinle;
            _can.Damaged += _ => damaged++;
            _can.HealthChanged += sn => { sonSnapshot = sn; if (sn.IsDead) olumSnapshot++; };

            try
            {
                bool ilk = _can.ApplyDamage(new PlayerDamageRequest(250f, Vector3.zero, Vector3.forward, "Zombie.Standard.Swipe"));
                bool ikinci = _can.ApplyDamage(50f, Vector3.zero, "Zombie.Standard.Swipe");

                Assert.IsTrue(ilk);
                Assert.IsTrue(sonSnapshot.IsDead, "Ölüm vuruşundan sonra yayınlanan son HealthChanged snapshot'ı IsDead=false; HUD ölümü göremez.");
                Assert.AreEqual(0f, sonSnapshot.Current, 0.0001f);
                Assert.AreEqual(1, olumSnapshot, "IsDead=true snapshot'ı tam 1 kez yayınlanmalı.");
                Assert.IsFalse(ikinci, "Ölü oyuncuya hasar 'uygulandı' döndü.");
                Assert.IsTrue(_can.IsDead);
                Assert.AreEqual(0f, _can.Current, 0.0001f);
                Assert.AreEqual(1, died, "Died " + died + " kez yayınlandı; tam 1 beklenir (REQ-048).");
                Assert.AreEqual(1, anyDied, "AnyPlayerDied " + anyDied + " kez yayınlandı.");
                Assert.AreEqual(1, damaged, "Ölümden sonraki hasar Damaged olayı üretti.");
                Assert.AreEqual("Zombie.Standard.Swipe", son.KilledBy);
                Assert.AreEqual(150f, son.Overkill, 0.01f, "Overkill = 250 − 100 olmalı.");

                Assert.AreEqual(0f, _can.Heal(50f), 0.0001f, "Ölüyken Heal iyileştirdi (revive yolu).");
                _can.HealToFull();
                Assert.AreEqual(0f, _can.Current, 0.0001f, "Ölüyken HealToFull can verdi (revive yolu).");

                yield return Bekle(RegenGecikme + 1f);
                Assert.AreEqual(0f, _can.Current, 0.0001f, "Ölüyken regen can verdi (revive yolu).");
                Assert.IsFalse(_can.IsRegenerating);
                Assert.IsTrue(_can.IsDead);
                Assert.AreEqual(1, died, "Bekleme sırasında ikinci Died olayı.");
            }
            finally
            {
                PlayerHealth.AnyPlayerDied -= anyDinle;
            }
        }
    }
}
