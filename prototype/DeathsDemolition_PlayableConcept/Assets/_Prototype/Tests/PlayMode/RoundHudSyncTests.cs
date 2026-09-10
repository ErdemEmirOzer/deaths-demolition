using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Damage;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// G-14 · KUSUR 1 regresyon testi — "sol üstte tur her zaman 1 gösteriyor".
    ///
    /// NEYİ ÇÜRÜTÜYOR
    /// --------------
    /// İki ihtimal vardı: (a) tur gerçekten ilerlemiyor, (b) HUD ilk kareden sonra
    /// tazelenmiyor. Bu test (b)'yi doğrudan çürütür: turu DOĞAL yoldan kapatır
    /// (havuzu öldürerek, F1 hata ayıklama düğmesiyle değil) ve
    /// <see cref="RunEvents.Current"/>.Round ile <see cref="RunDirector.Round"/>
    /// arasındaki uyuşmazlığı HER KARE sayar.
    ///
    /// Mevcut duman testi turları F1 panelindeki "Complete this round" düğmesiyle
    /// atlatıyor; bu yüzden havuzun doğal boşalma yolu ve HUD'un onu izleyişi
    /// otomatik kapının kör noktasındaydı. Bu test o boşluğu kapatır.
    ///
    /// 2026-08-18 ölçümü: uyuşmazlık = 0 kare, tur 1 -> 2 -> 3 doğal yoldan kapandı.
    /// Yani HUD kablolaması sağlamdı; kullanıcının gördüğü "1" dürüst bir 1'di —
    /// oyuncu duvara yapıştığı için (KUSUR 2) turu hiç temizleyemiyordu.
    /// </summary>
    [TestFixture]
    public sealed class RoundHudSyncTests
    {
        private const float SahneKurulumSaniye = 12f;

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator TurIlerleyinceHudRakamiDaIlerler()
        {
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < SahneKurulumSaniye)
            {
                yield return null;
            }

            RunDirector direktor = RunDirector.Instance;
            EnemySpawnDirector doguran = EnemySpawnDirector.Instance;
            Assert.IsNotNull(direktor, "Run sahnesinde RunDirector yok.");
            Assert.IsNotNull(doguran, "Run sahnesinde EnemySpawnDirector yok.");

            yield return Bekle(() => direktor.Phase == RunPhase.Fighting, 25f,
                "Kosu Fighting fazina girmedi.");

            Assert.AreEqual(1, direktor.Round, "Kosu 1. turdan baslamali.");
            Assert.AreEqual(1, RunEvents.Current.Round,
                "HUD anlik goruntusu ilk turda 1 gostermiyor.");

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] tur.hud senkron olcumu (dogal havuz bosaltma)");

            int uyusmazlikKare = 0;
            int olculenKare = 0;
            var gorulenTurlar = new List<int>();
            var isabetler = new List<DamageTarget>(64);

            gorulenTurlar.Add(direktor.Round);

            float baslangic = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - baslangic < 120f && direktor.Round < 3)
            {
                olculenKare++;

                if (RunEvents.Current.Round != direktor.Round)
                {
                    uyusmazlikKare++;
                }

                // Havuzu DOGAL yoldan bosalt: gercek hasar yolu, gercek olum olayi.
                isabetler.Clear();
                DamageTargetRegistry.Query(Vector3.zero, 400f, isabetler,
                    PlayerRegistry.Primary != null ? PlayerRegistry.Primary.transform : null);

                for (int i = 0; i < isabetler.Count; i++)
                {
                    if (!isabetler[i].IsValid)
                    {
                        continue;
                    }

                    isabetler[i].Receiver.ApplyHit(new DamageHit
                    {
                        Amount = 100000f,
                        Region = BodyRegion.Head,
                        Point = isabetler[i].Position,
                        Direction = Vector3.forward,
                        ImpactForce = 1f,
                        Source = DamageSourceKind.Bullet
                    });
                }

                // Yukseltme ekrani oyuncunun DEVAM dugmesini bekler; testte onu biz basariz.
                if (direktor.Phase == RunPhase.Upgrading)
                {
                    Assert.AreEqual(RunEvents.Current.Round, direktor.Round,
                        "Yukseltme ekrani acikken HUD tur rakami direktorden ayrildi.");
                    direktor.CloseUpgradeScreen();
                }

                if (gorulenTurlar[gorulenTurlar.Count - 1] != direktor.Round)
                {
                    gorulenTurlar.Add(direktor.Round);
                    rapor.AppendLine("  tur ilerledi -> " + direktor.Round +
                                     "  HUD=" + RunEvents.Current.Round +
                                     "  havuz=" + doguran.RoundPoolTotal);
                }

                yield return null;
            }

            // Dongu kosulu 'Round < 3' oldugu icin 3. tur ACILDIGI kare dongu biter ve
            // eski hali onu diziye hic yazmadan cikardi. Cikistaki turu burada kaydet.
            if (gorulenTurlar[gorulenTurlar.Count - 1] != direktor.Round)
            {
                gorulenTurlar.Add(direktor.Round);
            }

            rapor.AppendLine("  dongu cikis: gecen=" +
                             (Time.realtimeSinceStartup - baslangic).ToString("F1") + " sn" +
                             " tur=" + direktor.Round + " faz=" + direktor.Phase +
                             " havuzKalan=" + doguran.RemainingInPool +
                             " aktif=" + doguran.ActiveCount +
                             " dt=" + Time.deltaTime.ToString("F5") +
                             " captureDt=" + Time.captureDeltaTime);
            rapor.AppendLine("  gorulen tur dizisi = " + string.Join(" -> ", gorulenTurlar));
            rapor.AppendLine("  olculen kare = " + olculenKare +
                             "  HUD/direktor uyusmazlik = " + uyusmazlikKare);
            Debug.Log(rapor.ToString());

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, gorulenTurlar,
                "Havuz dogal yoldan bosaltildiginda tur 1->2->3 ilerlemedi.\n" + rapor);

            Assert.AreEqual(0, uyusmazlikKare,
                "HUD anlik goruntusu " + uyusmazlikKare + " karede direktorun tur " +
                "rakamindan ayrildi; HUD tazelenmiyor demektir.\n" + rapor);

            Assert.AreEqual(3, RunEvents.Current.Round,
                "Kosu 3. turda ama HUD anlik goruntusu " + RunEvents.Current.Round +
                " diyor.\n" + rapor);
        }

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string aciklama)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t0 > saniye)
                {
                    Assert.Fail("ZAMAN ASIMI (" + saniye + " sn): " + aciklama);
                }

                yield return null;
            }
        }
    }
}
