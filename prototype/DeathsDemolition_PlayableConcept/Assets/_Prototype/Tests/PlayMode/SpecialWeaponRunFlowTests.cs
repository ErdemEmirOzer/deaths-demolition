using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · Run yaşam döngüsü: ölüm / abort / yeni koşu / sahne teardown / ikinci koşu temizliği.
    /// FLOW-001 · AP-10 (TM-45, TM-46, TM-47, TM-48, TM-50). Olay ve abone sayıları koşular arasında
    /// eşit kalmalı; hiçbir Special durumu, timer'ı, loop sesi ya da callback'i ikinci koşuya taşınmamalı.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponRunFlowTests
    {
        private const float Pencere = 0.5f;
        private const int Kare = 12;

        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            RunFlowClock.ClearHolds();
            if (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu)
            {
                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        private static string AboneOzet(Dictionary<string, int> d)
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kv in d)
            {
                if (kv.Key.StartsWith("WeaponEvents.") || kv.Key.StartsWith("SpecialWeaponRoundBridge."))
                {
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                }
            }

            return sb.ToString();
        }

        /// <summary>Yeni koşuya kadar akış: Results → MainMenu → Selection → StartRun → Run hazır (sevkiyat config'iyle: ilk round uygun değil → Idle beklenir).</summary>
        private IEnumerator ResultstanYeniKosuya()
        {
            _s.ConfigGeriYukle();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Results);
            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
            yield return null;
            yield return null;
        }

        private static void IkinciKosuTemizOlmali(string etiket)
        {
            SpecialWeaponDirector director = Object.FindObjectOfType<SpecialWeaponDirector>();
            Assert.IsNotNull(director, etiket + ": ikinci koşuda director yok.");
            Assume.That(PrototypeConfig.Active.SpecialWeaponSpawnChance(RunDirector.Instance.Round), Is.EqualTo(0f),
                etiket + ": öncül — sevkiyat config'inde ilk round uygun değil (aksi hâlde Scheduled meşru).");
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, director.State, etiket + ": ikinci koşu Idle başlamadı.");
            Assert.AreEqual(-1f, director.ScheduledProgress, 0.0001f, etiket + ": eski schedule taşındı.");
            Assert.AreEqual(0, director.SpawnsThisRound, etiket + ": eski spawn sayacı taşındı.");
            Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, etiket + ": HUD snapshot taşındı.");
            Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), etiket + ": eski pickup yerde.");
            WeaponController wc = WeaponRegistry.ActiveController;
            Assert.IsNotNull(wc);
            Assert.IsFalse(wc.SpecialWeaponActive, etiket + ": eski aktif runtime taşındı.");
            Assert.AreEqual(0f, wc.Special.Remaining, 0.0001f, etiket + ": eski aktif timer taşındı.");
            Assert.AreEqual(0, SpecialImpacts.ActiveChains, etiket + ": eski kuyu taşındı.");
            Assert.IsFalse(WeaponEvents.Classic.TemporarilyDisabled, etiket + ": klasik silah devre dışı başladı.");
        }

        // ============================================================ TM-45 · ölüm (yerde)

        /// <summary>[TM-45][TM-47] Yerde pickup varken oyuncu ölür: koşu biter bitmez Special durumu temizlenir; ikinci koşu temiz başlar.</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Olum_YerdePickup_KosuBitinceTemizlenir_IkinciKosuTemiz()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                Assert.AreEqual(1, sayac.LoopBaslatSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop));

                _s.Rig.Health.DiagnosisInvulnerable = false;
                RunDirector director = RunDirector.Instance;
                _s.Rig.Health.ApplyDamage(_s.Rig.Health.Max * 10f, _s.Rig.transform.position + Vector3.forward, "wp007-death");
                yield return SpecialWeaponSahne.Bekle(() => director.Phase == RunPhase.Dead, SpecialWeaponSahne.FazZamanAsimi, "Dead fazı gelmedi.");
                yield return null;

                // Koşu bitti; sahne henüz değişmedi (Results gecikmesi ölçeksiz). Temizlik burada olmalı.
                Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name, "Öncül: sahne erken değişti.");
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Ölümde director Idle'a dönmedi (AP-10).");
                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), "Ölümde yerdeki pickup temizlenmedi (AP-10).");
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, "Ölümde HUD Special durumu kapanmadı (AP-10).");
                Assert.AreEqual(sayac.LoopBaslatSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop),
                    sayac.LoopDurdurSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop), "Ölümde beacon loop durmadı (AP-10 T7).");
                Assert.AreEqual(0, sayac.GroundExpired, "Ölümde iptal 'yerde süre doldu' olarak bildirildi.");
                Assert.AreEqual(0, sayac.PickedUp);
                Assert.AreEqual(0, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponGroundExpired), "İptal yolunda yerde-bitti cue'su çaldı.");
                Assert.AreEqual(1, sayac.Cancelled, "Cancelled tam bir kez.");
                Assert.AreEqual(SpecialWeaponCancelReason.RunEnded, sayac.Iptaller[0].Reason);
                Assert.AreEqual(SpecialWeaponPhase.OnGround, sayac.Iptaller[0].PhaseAtCancel);
                Assert.AreEqual(1, sayac.Cleared, "Cleared tam bir kez.");
                Debug.Log("[DD-TEST] wp007.tm45.ground " + sayac.Ozet());
            }

            yield return ResultstanYeniKosuya();
            IkinciKosuTemizOlmali("tm45-ground");
        }

        // ============================================================ TM-45 · ölüm (aktif)

        /// <summary>[TM-45] Special aktifken oyuncu ölür: runtime, loop, HUD ve alanlar koşu bitince temizlenir; ikinci koşu temiz.</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Olum_AktifSpecial_KosuBitinceTemizlenir_IkinciKosuTemiz()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
                Assert.AreEqual(1, sayac.LoopBaslatSayisi(CombatAudioEvent.HeavyWeaponSpinUp));

                _s.Rig.Health.DiagnosisInvulnerable = false;
                RunDirector director = RunDirector.Instance;
                _s.Rig.Health.ApplyDamage(_s.Rig.Health.Max * 10f, _s.Rig.transform.position + Vector3.forward, "wp007-death");
                yield return SpecialWeaponSahne.Bekle(() => director.Phase == RunPhase.Dead, SpecialWeaponSahne.FazZamanAsimi, "Dead fazı gelmedi.");
                yield return null;

                Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name, "Öncül: sahne erken değişti.");
                Assert.IsFalse(_s.Controller.SpecialWeaponActive, "Ölümde aktif runtime temizlenmedi (AP-10).");
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Ölümde director Idle'a dönmedi.");
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, "Ölümde HUD Special durumu kapanmadı.");
                Assert.AreEqual(sayac.LoopBaslatSayisi(CombatAudioEvent.HeavyWeaponSpinUp),
                    sayac.LoopDurdurSayisi(CombatAudioEvent.HeavyWeaponSpinUp), "Ölümde aktif loop durmadı (AP-10 T7).");
                Assert.AreEqual(0, SpecialImpacts.ActiveChains);
                Assert.IsFalse(WeaponEvents.Classic.TemporarilyDisabled, "Ölümde klasik silah 'devre dışı' kaldı.");
                // K-7 #1 (S-08 Ö1): iptal yolunda "kaybettin" haberi yok, nesne sesi bir kez, Ended bir kez.
                Assert.AreEqual(0, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponExpired), "İptal yolunda Expired haber cue'su çaldı.");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.HeavyWeaponSpinDown), "İptal yolunda SpinDown tam bir kez çalmalı.");
                Assert.AreEqual(1, sayac.Ended, "İptal yolunda Ended tam bir kez.");
                Assert.AreEqual(1, sayac.Cancelled, "Cancelled tam bir kez.");
                Assert.AreEqual(SpecialWeaponCancelReason.RunEnded, sayac.Iptaller[0].Reason);
                Assert.AreEqual(SpecialWeaponPhase.Active, sayac.Iptaller[0].PhaseAtCancel);
                // K-7 #8 kilidi (gt ek-1, C-00 kararı): ölüm vuruşunda "CLASSIC WEAPON RESTORED"
                // haberi ÜRETİLMEZ (HUD Dead fazında donar); pozitif kontrol doğal bitiş testinde.
                SpecialWeaponSahne.HudBanner(out string banner, out float bannerKalan);
                Assert.IsFalse(bannerKalan > 0f && banner == "CLASSIC WEAPON RESTORED",
                    "Ölüm vuruşunda 'CLASSIC WEAPON RESTORED' banner'ı gösterildi (HUD Dead fazında donmalı).");
                Debug.Log("[DD-TEST] wp007.tm45.active " + sayac.Ozet() + " banner='" + banner + "' kalan=" + bannerKalan.ToString("0.00"));
            }

            yield return ResultstanYeniKosuya();
            IkinciKosuTemizOlmali("tm45-active");
        }

        // ============================================================ TM-46 · abort (yerde)

        /// <summary>[TM-46] Yerde pickup varken pause→ana menü (abort): loop sesi taşınmaz, pickup kalmaz; sonraki koşu temiz.</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Abort_YerdePickup_LoopDurur_SonrakiKosuTemiz()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                Assert.AreEqual(1, sayac.LoopBaslatSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop));

                PrototypeUiFlow.AbortRunToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
                yield return SpecialWeaponSahne.Kareler(Kare);

                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), "Abort sonrası pickup yaşıyor.");
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, "Abort sonrası HUD snapshot OnGround kaldı.");
                Assert.AreEqual(sayac.LoopBaslatSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop),
                    sayac.LoopDurdurSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop), "Abort'ta beacon loop durmadı: ses ana menüye taşınır (AP-10 T7).");
                Debug.Log("[DD-TEST] wp007.tm46 " + sayac.Ozet());
            }

            _s.ConfigGeriYukle();
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
            yield return null;
            IkinciKosuTemizOlmali("tm46");
        }

        // ============================================================ TM-48 / TM-50 · ikinci koşu = birinci koşu

        /// <summary>
        /// [TM-50][TM-48] Aynı senaryo iki koşuda aynı olay sayılarını üretir; Special/Bridge abone sayıları
        /// koşular arasında ve menüye dönüşte değişmez (abonelik çoğalması yok, statik abone kalmaz).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator IkinciKosu_OlayVeAboneSayilari_BirinciKosuylaAyni()
        {
            var sayilar = new List<Dictionary<string, int>>(2);
            var aboneler = new List<Dictionary<string, int>>(2);

            for (int kosu = 0; kosu < 2; kosu++)
            {
                _s = new SpecialWeaponSahne();
                yield return _s.Kur();
                aboneler.Add(LifecycleTestKit.AboneAnlikGoruntu());

                using (var sayac = new SpecialOlaySayaci())
                {
                    yield return _s.SpawnEt(Pencere);
                    _s.PickupaGit(_s.CanliPickup());
                    yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
                    yield return SpecialWeaponSahne.Kareler(Kare);
                    _s.Controller.DeactivateSpecialWeapon();
                    yield return SpecialWeaponSahne.Bekle(() => _s.Director.State == SpecialWeaponDirector.SpecialWeaponState.Idle, SpecialWeaponSahne.FazZamanAsimi, "Director Idle olmadı.");
                    yield return SpecialWeaponSahne.Kareler(Kare);

                    sayilar.Add(new Dictionary<string, int>
                    {
                        { "spawned", sayac.Spawned }, { "pickedUp", sayac.PickedUp }, { "ended", sayac.Ended },
                        { "groundExpired", sayac.GroundExpired }, { "finalSeconds", sayac.FinalSeconds },
                        { "changedOnGround", sayac.ChangedOnGround }, { "changedActive", sayac.ChangedActive }, { "changedNone", sayac.ChangedNone },
                        { "cueSpawn", sayac.CueSayisi(CombatAudioEvent.SpecialWeaponSpawn) },
                        { "cuePickup", sayac.CueSayisi(CombatAudioEvent.SpecialWeaponPickup) },
                        { "loopBeaconStart", sayac.LoopBaslatSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop) },
                        { "loopBeaconStop", sayac.LoopDurdurSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop) },
                        { "loopSpinStart", sayac.LoopBaslatSayisi(CombatAudioEvent.HeavyWeaponSpinUp) },
                        { "loopSpinStop", sayac.LoopDurdurSayisi(CombatAudioEvent.HeavyWeaponSpinUp) },
                    });
                    Debug.Log("[DD-TEST] wp007.tm50 run" + (kosu + 1) + " " + sayac.Ozet());
                }

                yield return _s.Bitir();
                _s = null;

                PrototypeUiFlow.GoToMainMenu();
                yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
                yield return null;
            }

            Dictionary<string, int> menu = LifecycleTestKit.AboneAnlikGoruntu();
            Debug.Log("[DD-TEST] wp007.tm50 aboneler run1: " + AboneOzet(aboneler[0]) + "| run2: " + AboneOzet(aboneler[1]) + "| menu: " + AboneOzet(menu));

            foreach (KeyValuePair<string, int> kv in sayilar[0])
            {
                Assert.AreEqual(kv.Value, sayilar[1][kv.Key], "İkinci koşuda olay sayısı farklı: " + kv.Key);
            }

            Assert.AreEqual(1, sayilar[0]["spawned"]);
            Assert.AreEqual(1, sayilar[0]["pickedUp"]);
            Assert.AreEqual(1, sayilar[0]["ended"]);
            Assert.AreEqual(sayilar[0]["loopBeaconStart"], sayilar[0]["loopBeaconStop"], "Beacon loop start/stop dengesiz.");
            Assert.AreEqual(sayilar[0]["loopSpinStart"], sayilar[0]["loopSpinStop"], "Aktif loop start/stop dengesiz.");

            LifecycleTestKit.AboneleriKarsilastir("run1 vs run2", aboneler[0], aboneler[1]);
            foreach (KeyValuePair<string, int> kv in aboneler[0])
            {
                if (kv.Key.StartsWith("WeaponEvents.") || kv.Key.StartsWith("SpecialWeaponRoundBridge."))
                {
                    Assert.AreEqual(kv.Value, aboneler[1][kv.Key], "Abone sayısı ikinci koşuda değişti: " + kv.Key);
                }
            }

            // WP-059 (C-00 kararı): ana menüde kalan abone yalnız bilinen SÜREÇ-BOYU tüketiciler olabilir —
            // RunTelemetry (WP-045, ADR-045-1), VfxDressing (WP-047), GamepadRumble (WP-056). Beyaz liste dışı
            // her abone bir sızıntıdır (testin amacı korunur: teardown sonrası run'a ait abone kalmaz).
            var kalan = new List<string>();
            foreach ((System.Type tip, string ad) in new[] { (typeof(WeaponEvents), "SpecialWeaponSpawned"), (typeof(WeaponEvents), "SpecialChanged"), (typeof(SpecialWeaponRoundBridge), "RoundStarted") })
            {
                foreach (string hedef in LifecycleTestKit.AboneHedefleri(tip, ad))
                {
                    if (System.Array.IndexOf(SurecBoyuTuketiciler, hedef) < 0) kalan.Add(tip.Name + "." + ad + " ← " + hedef);
                }
            }

            Debug.Log("[DD-TEST] wp007.tm50 menü aboneleri: " + string.Join(", ", LifecycleTestKit.AboneHedefleri(typeof(WeaponEvents), "SpecialWeaponSpawned")) + " | " +
                      string.Join(", ", LifecycleTestKit.AboneHedefleri(typeof(WeaponEvents), "SpecialChanged")) + " | " + string.Join(", ", LifecycleTestKit.AboneHedefleri(typeof(SpecialWeaponRoundBridge), "RoundStarted")));
            CollectionAssert.IsEmpty(kalan, "Ana menüde Special/Bridge statik abonesi kaldı (teardown sonrası abone; beyaz liste dışı).");
        }

        /// <summary>WP-059: statik Special/Bridge event'lerine süreç boyu bağlı kalması TASARLANMIŞ tüketiciler (C-00 kararı 2026-09-17).</summary>
        private static readonly string[] SurecBoyuTuketiciler = { "RunTelemetry", "VfxDressing", "GamepadRumble" };
    }
}
