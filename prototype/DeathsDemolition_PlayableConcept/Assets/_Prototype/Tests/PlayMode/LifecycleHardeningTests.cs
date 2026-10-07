using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Inputs;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-001 · Aşama 5 — iki ardışık koşu zincirinde event abonesi birikmesi, hold/timeScale
    /// temizliği, ikinci koşuda kalıntı ve DDOL servislerin tekilliği.
    ///
    /// Abone sayıları sabit sayı olarak assert edilmez (aynı Play oturumundaki başka
    /// fixture'lar abone bırakmış olabilir); zincirin başı, koşu-1 sonu ve koşu-2 sonu
    /// birbirine EŞİT olmalı, Run içi − MainMenu farkı ise sahne-yerel abonelerin bilinen
    /// tablosuyla eşleşmelidir. Her ölçüm iki bağımsız gözlemle yapılır.
    /// </summary>
    [TestFixture]
    public sealed class LifecycleHardeningTests
    {
        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
            HitStopService.Cancel();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            RunFlowClock.ClearHolds();
            HitStopService.Cancel();
            Time.timeScale = _eskiTimeScale;
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator IkiKosuZinciri_AboneHoldZamanVeServislerTemizKalir()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();
            // HitFeedbackDirector is an intentionally lazy, process-long DDOL service. Its
            // VfxDressing installs the reload and four Special Weapon observers once and
            // keeps them between runs. Establish that documented owner before the baseline
            // so this lifecycle contract is independent of which fixture touched feedback first.
            _ = HitFeedbackDirector.Instance;
            yield return null;
            Dictionary<string, int> menu0 = AboneAnlikGoruntu();
            Yazdir("menu0", menu0);
            Assert.AreEqual(0, AktifSayi<ZombieEnemy>(), "İlk Main Menu'de aktif düşman kalıntısı.");
            IInputSource girdi0 = InputSourceRegistry.Current;
            Assert.IsTrue(InputSourceRegistry.HasRealSource, "Main Menu'de gerçek girdi kaynağı kurulmamış.");
            UiFlowRunner runner0 = UiFlowRunner.Instance;
            Assert.IsNotNull(runner0, "UiFlowRunner yok.");
            Assert.AreEqual(1, AktifSayi<PrototypeInputInstaller>(), "Birden fazla input installer.");
            Assert.AreEqual(1, AktifSayi<PrototypeBootstrapper>(), "Birden fazla bootstrapper.");

            Dictionary<string, int> run1 = null;
            Dictionary<string, int> run2 = null;
            Dictionary<string, int> menu1 = null;
            Dictionary<string, int> menu2 = null;

            for (int kosu = 1; kosu <= 2; kosu++)
            {
                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                yield return SimulasyonSerbestOlmali("selection" + kosu);

                PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return SahneyiBekle(PrototypeScenes.Run);

                // LH-3: koşu başında (Fighting'den ÖNCE) kalıntı yok.
                yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                         GoldService.Instance != null, FazZamanAsimi, "Run servisleri kurulmadı.");
                int aktifBaslangic = EnemySpawnDirector.Instance.ActiveCount;
                int doganBaslangic = EnemySpawnDirector.Instance.SpawnedThisRound;
                int mesgulBaslangic = EnemySpawnDirector.Instance.OccupiedCount;
                int orbBaslangic = GoldService.Instance.LiveOrbCount;
                int cuzdanBaslangic = RunDirector.Instance.Wallet != null ? RunDirector.Instance.Wallet.Balance : -1;
                Debug.Log("[DD-TEST] run" + kosu + ".start active=" + aktifBaslangic + " orbs=" + orbBaslangic +
                          " gold=" + cuzdanBaslangic + " phase=" + RunDirector.Instance.Phase);
                // A fast runner can advance the new round's refill clock before this coroutine resumes.
                // Prove every body belongs to this run here; the MainMenu checks below prove the previous
                // run left no active body behind after scene teardown.
                Assert.AreEqual(doganBaslangic, aktifBaslangic,
                    "Koşu " + kosu + " başında bu turda doğmamış aktif düşman var.");
                Assert.AreEqual(mesgulBaslangic, aktifBaslangic,
                    "Koşu " + kosu + " başında önceki turdan ceset/işgal kalıntısı var.");
                Assert.AreEqual(0, orbBaslangic, "Koşu " + kosu + " başında gold orb kalıntısı.");
                Assert.AreEqual(0, cuzdanBaslangic, "Koşu " + kosu + " başında cüzdan sıfır değil.");

                yield return KosuHazirOlsun();
                yield return SimulasyonSerbestOlmali("run" + kosu);

                RunDirector director = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController silah = WeaponRegistry.ActiveController;

                // LH-1 (Run içi) + tekillik.
                Dictionary<string, int> runSnap = AboneAnlikGoruntu();
                Yazdir("run" + kosu, runSnap);
                Assert.AreEqual(1, AktifSayi<RunDirector>(), "Run içinde birden fazla RunDirector.");
                Assert.AreEqual(1, AktifSayi<EnemySpawnDirector>(), "Birden fazla EnemySpawnDirector.");
                Assert.AreEqual(1, AktifSayi<GoldService>(), "Birden fazla GoldService.");
                Assert.AreEqual(1, AktifSayi<RunHudScreen>(), "Birden fazla RunHudScreen.");
                Assert.AreEqual(1, AktifSayi<PauseScreen>(), "Birden fazla PauseScreen.");
                Assert.AreEqual(1, AktifSayi<PrototypeInputInstaller>(), "Birden fazla input installer (Run).");
                Assert.IsTrue(ReferenceEquals(InputSourceRegistry.Current, girdi0), "Girdi kaynağı koşu " + kosu + "'de değişti.");
                Assert.IsTrue(ReferenceEquals(UiFlowRunner.Instance, runner0), "UiFlowRunner koşu " + kosu + "'de yeniden yaratıldı.");
                Assert.AreEqual(0, silah.Modifiers.GetTier(UpgradeBranch.FireRate), "Koşu " + kosu + " başında yükseltme kalıntısı.");
                Assert.AreEqual(rig.Health.Max, rig.Health.Current, 0.0001f, "Koşu " + kosu + " başında can tam değil.");

                // Kirlet: gold, yükseltme, hit-stop; düşman doğsun.
                director.Wallet.Credit(50, rig.transform.position);
                silah.Modifiers.AddTier(UpgradeBranch.FireRate, 1, PrototypeConfig.Active);
                HitStopService.Request(0.2f, 0f);
                Debug.Log("[DD-TEST] run" + kosu + ".dirty gold=" + director.Wallet.Balance + " hitstop=" + HitStopService.IsActive);
                float t0 = Time.realtimeSinceStartup;
                yield return Bekle(() => EnemySpawnDirector.Instance.ActiveCount > 0 || Time.realtimeSinceStartup - t0 > 8f,
                    12f, "düşman beklenirken");
                Assert.Greater(EnemySpawnDirector.Instance.ActiveCount, 0,
                    "Koşu " + kosu + "'de hiç düşman doğmadı; ikinci koşu kalıntı ölçümü anlamsız olurdu.");
                Debug.Log("[DD-TEST] run" + kosu + ".enemies=" + EnemySpawnDirector.Instance.ActiveCount);

                // Pause → resume: hold ve timeScale geri gelmeli.
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                Assert.IsTrue(RunFlowClock.IsFrozen, "Pause dondurmadı.");
                Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Pause'da timeScale 0 değil.");
                yield return null;
                yield return null;
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                yield return SimulasyonSerbestOlmali("resume" + kosu);

                if (kosu == 1) run1 = runSnap; else run2 = runSnap;

                // Ölüm → Results.
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "lh-" + kosu);
                yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
                yield return SahneyiBekle(PrototypeScenes.Results);
                yield return null;
                Assert.IsFalse(HitStopService.IsActive, "Results'ta hit-stop hâlâ aktif.");
                yield return SimulasyonSerbestOlmali("results" + kosu);

                PrototypeUiFlow.GoToMainMenu();
                yield return SahneyiBekle(PrototypeScenes.MainMenu);
                yield return null;
                yield return SimulasyonSerbestOlmali("menu" + kosu);
                Dictionary<string, int> menuSnap = AboneAnlikGoruntu();
                Yazdir("menu" + kosu, menuSnap);
                if (kosu == 1) menu1 = menuSnap; else menu2 = menuSnap;

                Assert.IsNull(RunDirector.Instance, "Main Menu'de RunDirector.Instance hâlâ dolu (koşu " + kosu + ").");
                Assert.IsNull(EnemySpawnDirector.Instance, "Main Menu'de EnemySpawnDirector.Instance dolu.");
                Assert.IsNull(GoldService.Instance, "Main Menu'de GoldService.Instance dolu.");
                Assert.IsNull(WeaponRegistry.ActiveController, "Main Menu'de eski silah kontrolcüsü kayıtlı.");
                Assert.AreEqual(0, AktifSayi<RunDirector>(), "Main Menu'de RunDirector nesnesi kaldı.");
                Assert.AreEqual(0, AktifSayi<ZombieEnemy>(), "Main Menu'de aktif düşman nesnesi kaldı.");
                Assert.AreEqual(0, AktifSayi<GoldOrb>(), "Main Menu'de gold orb nesnesi kaldı.");
            }

            // LH-1: zincir başı = koşu-1 sonu = koşu-2 sonu; Run içi eşit; farklar bilinen tabloyla aynı.
            AboneleriKarsilastir("menu1 vs menu0", menu0, menu1);
            AboneleriKarsilastir("menu2 vs menu0", menu0, menu2);
            AboneleriKarsilastir("run2 vs run1", run1, run2);
            // Run içi − MainMenu farkı: yalnız sahne-yerel aboneler (kaynak koddan çıkarılan tablo;
            // code-02 §B1 / test-02 §2.0). MainMenuScreen profil abonesi Run'da yoktur (−1).
            // WP-005: RunHudScreen artık RunStarted (presenter sıfırlama) ve GoldChanged (aynı
            // karede gold sayacı) olaylarına da abone — ikisi de sahne-yerel, OnDestroy'da çıkar.
            var beklenenFark = new Dictionary<string, int>
            {
                // WP-069 B2 (4e448f41): WeaponController refills the reserve per round (OnEnable/OnDisable, scene-local).
                // WP-094 (08.10): RunHudScreen's round banner moved to FeelEvents.RoundStarted (2 → 1).
                { "RunEvents.RunStarted", 1 }, { "RunEvents.RoundStarted", 1 },
                { "RunEvents.RoundCompleted", 3 },   // WP-078: the camera rig (scene-local, OnDisable removes) logs its inside-collider counter; WP-071: the ragdoll round log (armed at spawn, leaves on scene change)
                { "RunEvents.RoundPoolExhausted", 1 }, { "RunEvents.GoldChanged", 1 }, { "RunEvents.PhaseChanged", 0 },
                { "RunEvents.CountdownTick", 0 }, { "RunEvents.RunEnded", 0 }, { "RunFlowClock.FrozenChanged", 0 },
                { "ProfileService.Changed", -1 }, { "InputSourceRegistry.CurrentChanged", 0 },
                { "WeaponRegistry.ActiveChanged", 2 },
                { "PlayerHealth.AnyPlayerDied", 2 },   // WP-068 8c: RunHudScreen names the killing blow (E22 death line)
                // WP-068 8b: RunHudScreen also takes AnyDamaged (the player-bound damage arc, E17).
                // DEC-113 RunDirector, scene-local, paired unsubscribe: AnyDamaged → the Round Report's health lost (+1).
                // WP-094 (C-00 07.10): the HUD's two listeners (RunHudScreen, UiHitDirectionLayer) now hear the hit through
                // HudFeelSource - ONE bridge, made with the first HUD listener and dropped with the last (3 → 2). 08.10: the
                // bridge sits on FeelEvents.PlayerDamaged, not AnyDamaged (2 → 1; the FeelEvents row below takes the +1).
                { "PlayerHealth.AnyHealthChanged", 1 }, { "PlayerHealth.AnyDamaged", 1 },
                // WP-006: hasar olay busu izlenir. Run-yerel aboneler: EnemyDied ← RunDirector (kill/gold
                // istatistiği) + GoldService (paket); ArmorBroken ← RunDirector (kırılma sayacı). EnemyDamaged /
                // PlayerAttacked / EnemyHitDressed'a Run'da kalıcı abone yok (feedback tek sahibi çekmez, iter).
                // DEC-113 RunDirector, scene-local, paired unsubscribe: EnemyDamaged → the Round Report's Elite first-hit clock (+1).
                { "DamageEvents.EnemyDied", 2 }, { "DamageEvents.ArmorBroken", 1 }, { "DamageEvents.EnemyDamaged", 1 },
                { "DamageEvents.PlayerAttacked", 0 }, { "HitFeedbackDirector.EnemyHitDressed", 0 },
                // WP-007/WP-032C: RunHudScreen owns five Special events; PlayerSkinnedVisual owns the
                // equipped/reload/special animation beats for one player life; the director owns the four
                // bridge subscriptions. Every one is scene-local and must disappear again in MainMenu.
                { "WeaponEvents.WeaponEquipped", 1 }, { "WeaponEvents.AmmoChanged", 0 }, { "WeaponEvents.ReloadStarted", 1 },
                { "WeaponEvents.ReloadCompleted", 0 }, { "WeaponEvents.ReloadInterrupted", 1 },
                // WP-068 8a: RunHudScreen's ladder kick on an empty trigger (E11) - WP-094 (08.10) hears it from FeelEvents.DryFire (1 → 0).
                { "WeaponEvents.DryFire", 0 },
                { "WeaponEvents.ClassicChanged", 0 },
                { "WeaponEvents.SpecialWeaponSpawned", 1 }, { "WeaponEvents.SpecialWeaponGroundExpired", 1 },
                // WP-018/WP-032C: PlayerSkinnedVisual follows the player life and unsubscribes with the rig.
                // Besides the pickup clip it owns the equipped/reload/special hold animation beats.
                { "WeaponEvents.SpecialWeaponPickedUp", 2 }, { "WeaponEvents.SpecialWeaponFinalSeconds", 1 },
                { "WeaponEvents.SpecialWeaponEnded", 2 }, { "WeaponEvents.SpecialChanged", 1 },
                { "WeaponEvents.SpecialWeaponScheduled", 0 }, { "WeaponEvents.SpecialWeaponCancelled", 0 },
                { "WeaponEvents.SpecialWeaponCleared", 1 },
                { "SpecialWeaponRoundBridge.RoundStarted", 1 }, { "SpecialWeaponRoundBridge.RoundEnded", 1 },
                { "SpecialWeaponRoundBridge.RunStarted", 1 }, { "SpecialWeaponRoundBridge.RunEnded", 1 },
                // WP-094 (08.10, K/wp-095/FEELEVENTS-LISTE.md): the FeelEvents bridge is process-long (0 in this difference);
                // the HUD's listeners are scene-local and paired in OnDestroy - HudFeelSource's one PlayerDamaged bridge (made
                // with the first HUD listener, dropped with the last) and RunHudScreen's eight moments, one each.
                { "FeelEvents.PlayerDamaged", 1 }, { "FeelEvents.RoundStarted", 1 }, { "FeelEvents.DryFire", 1 },
                { "FeelEvents.ReloadEnded", 1 }, { "FeelEvents.EnemyHit", 1 }, { "FeelEvents.EnemyKilled", 1 },
                { "FeelEvents.MultiKill", 1 }, { "FeelEvents.LowHealth", 1 }, { "FeelEvents.LastZombie", 1 },
            };
            // Tamlık nöbetçisi (WP-007): tabloya girmeyen izlenen olay sessizce geçemez.
            Assert.AreEqual(IzlenenEventler.Length, beklenenFark.Count,
                "beklenenFark tablosu IzlenenEventler ile aynı uzunlukta değil — eksik satır sessiz geçer.");
            Dictionary<string, int> fark = Fark(run1, menu1);
            Yazdir("run-minus-menu", fark);
            AboneleriKarsilastir("run1 − menu1 (sahne-yerel abone tablosu)", beklenenFark, fark);

            Debug.Log("[DD-TEST] lifecycle.chain=OK");
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator PauseResumeAbort_HoldSifirVeMenuTimeScaleBir()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();
            int runsBefore = Profile.ProfileService.Current.RunsPlayed;
            Dictionary<string, int> menu0 = AboneAnlikGoruntu();

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector director = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause, "PauseScreen yok.");

            // Gerçek pause ekranı yolu (Escape'in çağırdığı API).
            pause.SetPaused(true);
            yield return null;
            Assert.IsTrue(RunFlowClock.HasHold(RunFlowClock.PauseHold), "SetPaused(true) PauseHold eklemedi.");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Pause'da timeScale 0 değil.");
            int tur = director.Round;
            float can = rig.Health.Current;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1f)
            {
                yield return null;
            }

            Assert.AreEqual(tur, director.Round, "Pause sırasında tur ilerledi.");
            Assert.AreEqual(can, rig.Health.Current, 0.0001f, "Pause sırasında can değişti.");

            pause.SetPaused(false);
            yield return null;
            Assert.IsFalse(RunFlowClock.HasHold(RunFlowClock.PauseHold), "SetPaused(false) PauseHold'u kaldırmadı.");
            yield return SimulasyonSerbestOlmali("resume");

            // Pause → abort (pause menüsünden ana menüye): hold kalmamalı, dünya çözülmeli.
            pause.SetPaused(true);
            yield return null;
            Assert.IsTrue(RunFlowClock.IsFrozen, "İkinci pause dondurmadı.");
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            yield return SimulasyonSerbestOlmali("abort-menu");
            Assert.IsNull(RunDirector.Instance, "Abort sonrası RunDirector kaldı.");
            Assert.AreEqual(runsBefore, Profile.ProfileService.Current.RunsPlayed,
                "Abort edilen koşu profile işlendi — mevcut davranış (B5 açık karar) bu testte kilitlenmez, yalnız değişirse fark edilir.");
            Dictionary<string, int> menu1 = AboneAnlikGoruntu();
            Yazdir("abort.menu", menu1);
            AboneleriKarsilastir("abort menu vs menu0", menu0, menu1);

            // Abort sonrası yeni koşu temiz başlamalı.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            Assert.AreEqual(1, RunDirector.Instance.Round, "Abort sonrası koşu 1. turdan başlamadı.");
            Assert.AreEqual(0, RunDirector.Instance.Wallet.Balance, "Abort sonrası cüzdan sıfır değil.");
            yield return SimulasyonSerbestOlmali("run-after-abort");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] lifecycle.abort=OK");
        }
    }
}
