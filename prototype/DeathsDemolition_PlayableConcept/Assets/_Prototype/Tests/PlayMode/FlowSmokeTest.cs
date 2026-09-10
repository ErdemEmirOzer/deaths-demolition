using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// GT · PlayMode duman testi — kapının kalbi.
    ///
    /// Boot → MainMenu → Selection → Run(R1→R2→R3) → ölüm → Results → MainMenu
    /// akışının TAM SIRAYLA kapandığını doğrular.
    ///
    /// ÇÜRÜTME KOŞULU (TS-224): bu test hiçbir koşulda Assert.Pass() ile erken çıkmaz.
    /// Erken çıkış yalancı yeşildir. Her adım kendi kanıtını `_dizi` listesine yazar ve
    /// test sonunda dizi beklenen sırayla BİREBİR karşılaştırılır. Bir adım atlanırsa
    /// dizi kısa kalır ve test kırmızı yanar.
    /// </summary>
    [TestFixture]
    public sealed class FlowSmokeTest
    {
        private const float SahneZamanAsimi = 20f;
        private const float FazZamanAsimi = 25f;

        private readonly List<string> _dizi = new List<string>();

        // Konsol sayacı (kabul kriteri 27 · TS-309).
        private readonly List<string> _agirMesajlar = new List<string>();
        private readonly List<string> _bastirilmisHatalar = new List<string>();
        private int _uyariSayisi;

        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _dizi.Clear();
            _agirMesajlar.Clear();
            _bastirilmisHatalar.Clear();
            _uyariSayisi = 0;

            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();

            Application.logMessageReceived += MesajTopla;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= MesajTopla;

            // Test KALSA BİLE sayımlar basılır — ölçüm kaybolursa kanıt kaybolur.
            var ozet = new System.Text.StringBuilder();
            ozet.Append("[DD-TEST] console.heavy=").Append(_agirMesajlar.Count)
                .Append(" suppressed=").Append(_bastirilmisHatalar.Count)
                .Append(" warnings=").Append(_uyariSayisi);
            Debug.Log(ozet.ToString());

            var benzersiz = new Dictionary<string, int>();
            for (int i = 0; i < _agirMesajlar.Count; i++)
            {
                string k = _agirMesajlar[i];
                if (k.Length > 140)
                {
                    k = k.Substring(0, 140);
                }

                benzersiz[k] = benzersiz.TryGetValue(k, out int n) ? n + 1 : 1;
            }

            foreach (KeyValuePair<string, int> kv in benzersiz)
            {
                Debug.Log("[DD-TEST] console.kind x" + kv.Value + " :: " + kv.Key);
            }

            for (int i = 0; i < _bastirilmisHatalar.Count; i++)
            {
                Debug.Log("[DD-TEST] console.suppressed :: " + _bastirilmisHatalar[i]);
            }

            LogAssert.ignoreFailingMessages = false;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
        }

        private void MesajTopla(string mesaj, string yigin, LogType tur)
        {
            if (mesaj != null && mesaj.StartsWith("[DD-TEST]"))
            {
                return;
            }

            switch (tur)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    _agirMesajlar.Add(tur + " :: " + mesaj);
                    break;

                case LogType.Warning:
                    _uyariSayisi++;

                    // TS-309: Debug.LogWarning ile bastırılmış BAŞARISIZLIK bildirimleri
                    // Error sayımına girmez. Kapı bunları görmezse "konsol temiz" iddiası
                    // yalancı yeşildir. Bu yüzden ayrıca aranırlar.
                    if (mesaj != null &&
                        (mesaj.Contains("WITH FAILURES") ||
                         mesaj.Contains("-> FAILED") ||
                         mesaj.Contains("unreachable")))
                    {
                        _bastirilmisHatalar.Add("Warning :: " + mesaj);
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------- yardımcılar

        private void Adim(string ad)
        {
            _dizi.Add(ad);
            Debug.Log("[DD-TEST] flow.step=" + ad);
        }

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string aciklama)
        {
            float baslangic = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - baslangic > saniye)
                {
                    Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                }

                yield return null;
            }
        }

        private static IEnumerator SahneyiBekle(string ad)
        {
            yield return Bekle(
                () => SceneManager.GetActiveScene().name == ad && SceneManager.GetActiveScene().isLoaded,
                SahneZamanAsimi,
                "'" + ad + "' sahnesi etkin hale gelmedi. Şu an: '" +
                SceneManager.GetActiveScene().name + "'");
        }

        private static bool DebugButonunaBas(string etiketParcasi)
        {
            IReadOnlyList<DebugPanelEntry> girdiler = DebugPanelRegistry.Entries;
            for (int i = 0; i < girdiler.Count; i++)
            {
                if (girdiler[i] is DebugPanelButton buton &&
                    buton.Label != null &&
                    buton.Label.Contains(etiketParcasi) &&
                    buton.OnPressed != null)
                {
                    buton.OnPressed.Invoke();
                    return true;
                }
            }

            return false;
        }

        private static bool DebugAnahtariAyarla(string etiketParcasi, bool deger)
        {
            IReadOnlyList<DebugPanelEntry> girdiler = DebugPanelRegistry.Entries;
            for (int i = 0; i < girdiler.Count; i++)
            {
                if (girdiler[i] is DebugPanelToggle anahtar &&
                    anahtar.Label != null &&
                    anahtar.Label.Contains(etiketParcasi) &&
                    anahtar.Set != null)
                {
                    anahtar.Set.Invoke(deger);
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------- duman testi

        [UnityTest]
        [Category("Smoke")]
        [Timeout(300000)]
        public IEnumerator Akis_BootMainMenuSelectionRunOlumResultsMainMenu()
        {
            // Test çerçevesi ilk ağır mesajda testi keser; o zaman akışın geri kalanı
            // ÖLÇÜLEMEZ ve rapor eksik kalır. Mesajları kendimiz sayıp akışın sonunda
            // hüküm veriyoruz — böylece hem akış dizisi hem konsol hükmü elde edilir.
            // Bayrak test GÖVDESİNDE kurulmalı; [SetUp] içinde kurulan LogScope başka olur.
            LogAssert.ignoreFailingMessages = true;

            // ---------------------------------------------------------------- 1 · Boot
            // EditMode'da ölçülemeyen yüklenebilirlik burada ölçülür (yöntem düzeltmesi).
            string[] besSahne =
            {
                PrototypeScenes.Boot, PrototypeScenes.MainMenu, PrototypeScenes.Selection,
                PrototypeScenes.Run, PrototypeScenes.Results
            };

            for (int i = 0; i < besSahne.Length; i++)
            {
                Assert.IsTrue(Application.CanStreamedLevelBeLoaded(besSahne[i]),
                    besSahne[i] + " sahnesi yüklenebilir değil; akış bu sahnede kopar.");
            }

            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return SahneyiBekle(PrototypeScenes.Boot);
            Adim(PrototypeScenes.Boot);

            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config, "Boot, PrototypeConfig'i etkinleştirmedi.");
            Debug.Log("[DD-TEST] config.pool.r1=" + config.TotalEnemyPool(1) +
                      " target.r1=" + config.TargetActiveEnemies(1));

            // -------------------------------------------------------------- 2 · MainMenu
            // Boot kendi kendine devreder; elle sahne yüklemiyoruz.
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Adim(PrototypeScenes.MainMenu);

            // ------------------------------------------------------------- 3 · Selection
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            Adim(PrototypeScenes.Selection);

            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            Debug.Log("[DD-TEST] loadout=skin0/" + PrototypeUiFlow.SelectedWeapon);

            // -------------------------------------------------------------------- 4 · Run
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            Adim(PrototypeScenes.Run);

            yield return Bekle(() => RunDirector.Instance != null, FazZamanAsimi,
                "Run sahnesinde RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, FazZamanAsimi,
                "Run sahnesinde oyuncu doğmadı.");

            PlayerRig rig = PlayerRegistry.Primary;
            Assert.IsNotNull(rig.Health, "Oyuncunun PlayerHealth bileşeni yok.");
            Debug.Log("[DD-TEST] spawn.player=" + rig.transform.position.ToString("F2"));

            RunDirector director = RunDirector.Instance;
            yield return Bekle(() => director.Phase == RunPhase.Fighting, FazZamanAsimi,
                "Run, Fighting fazına girmedi. Faz: " + director.Phase);
            Assert.AreEqual(1, director.Round, "Koşu 1. turdan başlamalı.");
            Adim("Round1");
            Debug.Log("[DD-TEST] round.state=Fighting round=1 pool=" + director.RoundPoolTotal);

            // Tur sonu Upgrade ekranı akışın parçası; F1 anahtarıyla açık olduğundan emin ol.
            Assert.IsTrue(DebugAnahtariAyarla("Show upgrade screen at round end", true),
                "F1 panelinde 'Show upgrade screen at round end' anahtarı yok.");

            // Havuz büyüklüğü formülle uyuşuyor mu — canlı sahnede bağımsız kontrol.
            Assert.AreEqual(config.TotalEnemyPool(1), director.RoundPoolTotal,
                "R1 havuz büyüklüğü formülle uyuşmuyor.");

            // Aktif düşman sayısını R1 boyunca örnekle (K07 · TS-203).
            int enYuksekAktif = 0;
            float ornekBaslangic = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - ornekBaslangic < 3f)
            {
                if (DD.Prototype.Spawn.EnemySpawnDirector.Instance != null)
                {
                    enYuksekAktif = Mathf.Max(enYuksekAktif,
                        DD.Prototype.Spawn.EnemySpawnDirector.Instance.ActiveCount);
                }

                yield return null;
            }

            Debug.Log("[DD-TEST] enemies.activeMax=" + enYuksekAktif);
            Assert.LessOrEqual(enYuksekAktif, config.AbsoluteSafetyCeiling,
                "Aktif düşman sayısı mutlak güvenlik tavanı 40'ı aştı.");

            // ------------------------------------------------------ 5 · Pause sızdırmıyor
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.IsTrue(RunFlowClock.IsFrozen, "Pause tutamağı simülasyonu dondurmadı.");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Pause sırasında timeScale sıfır olmalı.");

            int pauseOncesiTur = director.Round;
            float pauseOncesiCan = rig.Health.Current;
            float pauseBaslangic = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - pauseBaslangic < 1f)
            {
                yield return null;
            }

            Assert.AreEqual(pauseOncesiTur, director.Round, "Pause sırasında tur ilerledi.");
            Assert.AreEqual(pauseOncesiCan, rig.Health.Current, 0.0001f,
                "Pause sırasında oyuncu canı değişti (rejenerasyon ya da hasar sızdı).");
            Debug.Log("[DD-TEST] pause.deltas=round:0,playerHealth:0 timeScale=" + Time.timeScale);

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.IsFalse(RunFlowClock.IsFrozen, "Pause tutamağı bırakılınca dünya çözülmeli.");
            Adim("PauseOK");

            // -------------------------------------------------- 6 · R1 → R2 → R3 kapanışı
            for (int hedefTur = 2; hedefTur <= 3; hedefTur++)
            {
                int oncekiTur = director.Round;

                yield return Bekle(() => director.Phase == RunPhase.Fighting, FazZamanAsimi,
                    "R" + oncekiTur + " Fighting fazında değil, kapatılamaz. Faz: " + director.Phase);

                Assert.IsTrue(DebugButonunaBas("Complete this round"),
                    "F1 panelinde 'Complete this round' butonu yok; tur kapatılamıyor.");

                yield return Bekle(() => director.Phase == RunPhase.Upgrading, FazZamanAsimi,
                    "R" + oncekiTur + " sonrası Upgrade ekranı açılmadı. Faz: " + director.Phase);
                Debug.Log("[DD-TEST] flow.step=Upgrade round=" + oncekiTur);
                Adim("Upgrade" + oncekiTur);

                Assert.IsTrue(director.Shop.IsOpen, "Upgrade fazında dükkân kapalı.");

                Assert.IsTrue(DebugButonunaBas("Close upgrade screen"),
                    "F1 panelinde 'Close upgrade screen' butonu yok; geri sayım başlatılamıyor.");

                yield return Bekle(() => director.Phase == RunPhase.Countdown ||
                                         (director.Phase == RunPhase.Fighting && director.Round == hedefTur),
                    FazZamanAsimi,
                    "Upgrade sonrası geri sayım başlamadı. Faz: " + director.Phase);

                yield return Bekle(() => director.Phase == RunPhase.Fighting && director.Round == hedefTur,
                    FazZamanAsimi,
                    "R" + hedefTur + " başlamadı. Faz: " + director.Phase + " tur: " + director.Round);

                Adim("Round" + hedefTur);
                Debug.Log("[DD-TEST] round.state=Complete round=" + oncekiTur +
                          " -> Fighting round=" + hedefTur +
                          " pool=" + director.RoundPoolTotal);

                Assert.AreEqual(config.TotalEnemyPool(hedefTur), director.RoundPoolTotal,
                    "R" + hedefTur + " havuz büyüklüğü formülle uyuşmuyor.");
            }

            Assert.AreEqual(3, director.Round, "Akış 3. tura ulaşmalıydı.");
            Assert.AreEqual(2, director.Stats.CompletedRounds, "İki tur tamamlanmış olmalı.");

            // ------------------------------------------------------------------ 7 · Ölüm
            rig = PlayerRegistry.Primary;
            Assert.IsNotNull(rig, "Ölüm adımından önce oyuncu kayboldu.");

            float maxCan = rig.Health.Max;
            rig.Health.ApplyDamage(maxCan * 10f, rig.transform.position + Vector3.forward, "gt-smoke");

            yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi,
                "Oyuncu öldürüldü ama koşu Dead fazına geçmedi. Faz: " + director.Phase);

            Assert.IsTrue(rig.Health.IsDead, "PlayerHealth ölü işaretlenmedi.");
            Adim("Death");
            Debug.Log("[DD-TEST] run.end=death revive=false");

            RunSummary ozet = RunEvents.LastRunSummary;
            Assert.AreEqual(RunEndReason.PlayerDeath, ozet.EndReason, "Koşu sonu sebebi ölüm olmalı.");
            Assert.AreEqual(3, ozet.ReachedRound, "Özet 3. tura ulaşıldığını bildirmeli.");
            Assert.AreEqual(2, ozet.CompletedRounds, "Özet iki tamamlanmış tur bildirmeli.");

            // -------------------------------------------------------------- 8 · Results
            yield return SahneyiBekle(PrototypeScenes.Results);
            Adim(PrototypeScenes.Results);

            // Results ekranının okuduğu 12 alan dolu mu (K19 · TS-108).
            int dolu = 0;
            if (ozet.ReachedRound > 0) dolu++;
            if (ozet.CompletedRounds >= 0) dolu++;
            if (ozet.TotalKills >= 0) dolu++;
            if (ozet.StandardKills >= 0) dolu++;
            if (ozet.SpecialKills >= 0) dolu++;
            if (ozet.EliteKills >= 0) dolu++;
            if (ozet.UltraKills >= 0) dolu++;
            if (ozet.GoldEarned >= 0) dolu++;
            if (ozet.CompletedRoundXp >= 0) dolu++;
            if (ozet.KillXp >= 0) dolu++;
            if (ozet.RunDuration > 0f) dolu++;
            if (!string.IsNullOrEmpty(ozet.BuildDescription)) dolu++;

            Debug.Log("[DD-TEST] results.fields=" + dolu + "/12");
            Assert.AreEqual(12, dolu, "Run Results'ta 12 alanın hepsi dolu olmalı.");

            // Tamamlanmış iki tur = 20 + 25 = 45 XP. Bitmemiş 3. tur XP vermez.
            Assert.AreEqual(45, ozet.CompletedRoundXp,
                "Tamamlanan tur XP'si 15+5R kuralıyla 20+25=45 olmalı; bitmemiş tur XP vermemeli.");

            // --------------------------------------------------------- 9 · MainMenu dönüşü
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Adim(PrototypeScenes.MainMenu);

            Assert.IsFalse(RunFlowClock.IsFrozen, "Ana menüye dönüşte simülasyon donmuş kaldı.");
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Ana menüye dönüşte timeScale 1 olmalı.");

            // ------------------------------------------------------- 10 · dizi karşılaştırma
            string[] beklenen =
            {
                "Boot",
                "MainMenu",
                "Selection",
                "Run",
                "Round1",
                "PauseOK",
                "Upgrade1",
                "Round2",
                "Upgrade2",
                "Round3",
                "Death",
                "Results",
                "MainMenu"
            };

            string gozlenen = string.Join(" -> ", _dizi);
            Debug.Log("[DD-TEST] flow.observed=" + gozlenen);

            CollectionAssert.AreEqual(beklenen, _dizi,
                "AKIŞ DİZİSİ BOZUK.\nbeklenen: " + string.Join(" -> ", beklenen) +
                "\ngözlenen: " + gozlenen);

            Debug.Log("[DD-TEST] flow.sequence=OK");

            // ------------------------------------------------ 11 · konsol hükmü (kriter 27)
            Debug.Log("[DD-TEST] console.heavy=" + _agirMesajlar.Count +
                      " suppressed=" + _bastirilmisHatalar.Count +
                      " warnings=" + _uyariSayisi);

            var rapor = new System.Text.StringBuilder();

            if (_agirMesajlar.Count > 0)
            {
                rapor.AppendLine("KONSOL AĞIR MESAJ (Error/Exception/Assert) = " + _agirMesajlar.Count);
                for (int i = 0; i < _agirMesajlar.Count; i++)
                {
                    rapor.AppendLine("  " + _agirMesajlar[i]);
                }
            }

            if (_bastirilmisHatalar.Count > 0)
            {
                rapor.AppendLine("BASTIRILMIŞ BAŞARISIZLIK (LogWarning ardına saklanmış) = " +
                                 _bastirilmisHatalar.Count);
                for (int i = 0; i < _bastirilmisHatalar.Count; i++)
                {
                    rapor.AppendLine("  " + _bastirilmisHatalar[i]);
                }
            }

            if (rapor.Length > 0)
            {
                rapor.AppendLine("toplam uyarı sayısı: " + _uyariSayisi);
                rapor.AppendLine("AKIŞ KAPANDI ama konsol temiz DEĞİL — kabul kriteri 27 karşılanmıyor.");
                Assert.Fail(rapor.ToString());
            }
        }
    }
}
