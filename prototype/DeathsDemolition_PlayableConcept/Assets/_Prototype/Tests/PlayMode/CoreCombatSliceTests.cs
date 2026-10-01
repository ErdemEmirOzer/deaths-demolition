using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// Savaş dilimi testlerinin sürülebilir girdisi: hareket, imleç, ateş.
    /// Registry'ye takılır; motor, nişan ve silah aynı kaynağı okur.
    /// </summary>
    internal sealed class SavasTestGirdisi : IInputSource, IFocusInputSource
    {
        public Vector2 Yon;
        public bool OdakBasili;   // WP-069 FP feel: Focus / ADS held
        public bool FocusHeld => OdakBasili;
        public Vector2 Imlec = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public bool AtesBasili;
        public bool AtesBasti;
        public bool DoldurBasti;

        public bool IsEnabled => true;
        public Vector2 Move => Yon;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => Imlec;
        public Vector2 AimStick => Vector2.zero;
        public bool FireHeld => AtesBasili;
        public bool FirePressed => AtesBasti;
        public bool FireReleased => false;
        public bool ReloadPressed => DoldurBasti;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;
#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// WP-002 · M1 SAVAŞ ÇEKİRDEĞİ nöbetçisi — Run sahnesinde, gerçek zincirle.
    /// REQ-036/037/038/039 · REQ-043/044/045/046/048 · REQ-121/125/127 · DEC-029/030/031/032/034.
    ///
    /// Hiçbir test ApplyDamage/Kill çağırmaz (ölüm testi hariç, orada da can zombilerle
    /// düşürülür). Ölçümler zincirin gerçek uçlarından alınır: hasar yalnız zombinin
    /// aktif penceresinden, isabet yalnız silahın hitscan'inden gelir.
    ///
    /// SAYILAR PROTOTYPE-ONLY — ÜRÜN KARARI DEĞİL
    /// -----------------------------------------
    /// Standard: menzil 1.7, telegraf 0.55, aktif 0.12, toparlanma 0.45, bekleme 1.5, hız 2.7,
    /// hasar 10, gövde 0.45, can 34 (R1). AR: hasar 11, 8.5 atış/s, şarjör 30. Oyuncu: hız 6.2,
    /// ivme 45, can 100. Hepsi test sabiti; eşikler formülden türetildi.
    /// </summary>
    [TestFixture]
    public sealed class CoreCombatSliceTests
    {
        // WP-076: this suite checks the WP-002/003 damage hand-off against the DEC-021 per-tier formula (every swing = the
        // tier's own attack). The five-style variety has its own suites (AttackSelectorTests / AttackVarietySliceTests), so
        // the selector is pinned to the signature style here — the same switch as `-ddAiV3 0`.
        [SetUp] public void Wp076ImzaStil() => AttackSelector.Enabled = false;
        [TearDown] public void Wp076ImzaStilBitir() => AttackSelector.Enabled = true;

        private const float Telegraf = 0.55f;
        private const float AktifPencere = 0.12f;
        private const float Toparlanma = 0.45f;
        private const float Bekleme = 1.5f;
        private const float SaldiriDongusu = Telegraf + AktifPencere + Toparlanma + Bekleme;
        private const float SaldiriMenzili = 1.7f;
        private const float ZombiHizi = 2.7f;
        private const float ZombiCani = 34f;
        private const float TekVurusHasari = 10f;
        private const float OyuncuHizi = 6.2f;
        private const float OyuncuIvmesi = 45f;
        private const float BaslangicCani = 100f;
        private const float TufekHasari = 12f;   // WP-069 B2 Hinawa (ar_1)
        private const float TufekAtisHizi = 10f;   // 600 RPM
        private const float NisanDuzlemi = 1.15f;
        private const float Adim = 1f / 60f;
        private const float KurulumZamanAsimi = 40f;

        private PlayerRig _rig;
        private PlayerHealth _can;
        private SavasTestGirdisi _girdi;
        private IInputSource _oncekiGirdi;
        private bool _olumTesti;

        // ======================================================================== kurulum

        private IEnumerator RunSahnesiniHazirla()
        {
            LogAssert.ignoreFailingMessages = true;
            _oncekiGirdi = InputSourceRegistry.Current;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, KurulumZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => PrototypeCameraRig.Active != null, KurulumZamanAsimi, "Kamera rig'i yok.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");

            _rig = PlayerRegistry.Primary;
            _can = _rig.Health;
            Assert.IsNotNull(_can);
            Assert.IsFalse(_can.DiagnosisInvulnerable, "Oyuncu tanı dokunulmazlığıyla açıldı.");
            Assert.AreEqual(BaslangicCani, _can.Max, 0.01f, "Maksimum can test sabitinden farklı.");
            Assert.AreEqual(PrototypeLayers.Player, _rig.gameObject.layer, "Oyuncu kökü Player katmanında değil.");

            if (EnemySpawnDirector.Instance != null)
            {
                EnemySpawnDirector.Instance.StopRound();
                EnemySpawnDirector.Instance.ClearAllEnemies();
            }

            EnemyFactory.ClearAll();
            yield return null;

            _girdi = new SavasTestGirdisi();
            InputSourceRegistry.SetCurrent(_girdi);
            _rig.Motor.SetInputSource(null);
            _rig.Aim.SetInputSource(null);

            Time.captureDeltaTime = Adim;
            yield return null;

            Assert.AreEqual(OyuncuHizi, _rig.Motor.MaxSpeed, 0.01f, "Oyuncu hızı test sabitinden farklı.");
            Assert.AreEqual(0, CanliZombi(), "Kurulum sonrası sahnede canlı zombi kaldı.");
            SabitTazeligiDogrula();
        }

        /// <summary>
        /// Sabit tazelik nöbetçisi: test sabitleri config'ten sapınca test kırılır ve mesaj
        /// "türetmeyi yenile" der. Sayıyı kilitlemez; kilitlediği şey türetmenin güncel kalmasıdır.
        /// </summary>
        private static void SabitTazeligiDogrula()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config);
            EnemyTierSettings std = config.Enemies.Find(EnemyTier.Standard);
            Assert.IsNotNull(std);
            const string not = " — test sabiti config'ten saptı; türetmeyi yenile (PrototypeOnly, ürün kararı değil).";
            Assert.AreEqual(Telegraf, std.TelegraphTime, 0.0001f, "TelegraphTime" + not);
            Assert.AreEqual(AktifPencere, std.AttackActiveTime, 0.0001f, "AttackActiveTime" + not);
            Assert.AreEqual(Toparlanma, std.AttackRecoveryTime, 0.0001f, "AttackRecoveryTime" + not);
            Assert.AreEqual(Bekleme, std.AttackCooldown, 0.0001f, "AttackCooldown" + not);
            Assert.AreEqual(SaldiriMenzili, std.AttackRange, 0.0001f, "AttackRange" + not);
            Assert.AreEqual(ZombiHizi, std.MoveSpeed, 0.0001f, "MoveSpeed" + not);
            Assert.AreEqual(TekVurusHasari, config.EnemyAttackDamage(EnemyTier.Standard, 1), 0.0001f, "EnemyAttackDamage(R1)" + not);
            Assert.AreEqual(ZombiCani, config.EnemyHealth(EnemyTier.Standard, 1), 0.0001f, "EnemyHealth(R1)" + not);
            Assert.AreEqual(OyuncuHizi, config.Player.MoveSpeed, 0.0001f, "Player.MoveSpeed" + not);
            Assert.AreEqual(OyuncuIvmesi, config.Player.Acceleration, 0.0001f, "Player.Acceleration" + not);
            Assert.AreEqual(BaslangicCani, config.Player.MaxHealth, 0.0001f, "Player.MaxHealth" + not);
            Assert.AreEqual(NisanDuzlemi, config.Player.AimPlaneHeight, 0.0001f, "Player.AimPlaneHeight" + not);
            ClassicWeaponSettings ar = config.Weapons.Find(ClassicWeaponId.AssaultRifle);
            Assert.IsNotNull(ar);
            Assert.AreEqual(TufekHasari, ar.Damage, 0.0001f, "AssaultRifle.Damage" + not);
            Assert.AreEqual(TufekAtisHizi, ar.FireRate, 0.0001f, "AssaultRifle.FireRate" + not);
        }

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            Time.captureDeltaTime = 0f;

            if (_girdi != null)
            {
                _girdi.Yon = Vector2.zero;
                _girdi.AtesBasili = false;
                _girdi.AtesBasti = false;
                _girdi.DoldurBasti = false;
            }

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            InputSourceRegistry.SetCurrent(_oncekiGirdi);
            EnemyFactory.ClearAll();

            if (_olumTesti)
            {
                // Ölüm Results'a geçişi başlatır; akışı kapatarak sonraki testi temiz bırak.
                _olumTesti = false;
                PrototypeUiFlow.GoToMainMenu();
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup - t0 < 20f)
                {
                    yield return null;
                }
            }

            _rig = null;
            _can = null;
            _girdi = null;
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        // ================================================================ REQ-036/037/039

        /// <summary>
        /// Oyuncu +z'ye (kamera ileri, kuzey) koşarken imleç −z'de: karakter −z'ye bakar, hız
        /// +z'dedir ve basılı tutulan ateş −z'deki zombiye isabet eder. Üç ölçüm aynı karelerde
        /// alınır. Hitscan yönü DamageEvents.EnemyDamaged.Direction'dan okunur. (Kuzey rotası
        /// seçildi: doğuda +x 3.9 m'de su havuzunun direkleri var, WorldStructures (6,−5).)
        /// </summary>
        /// <summary>
        /// R1-B W6 (C-00 30.09): a run started again in the same scene (the debug restart) begins with the weapon of a new
        /// run — a full magazine and the round-1 reserve — not the previous run's leftovers.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AyniSahnedeYeniKosu_SilahSarjorVeYedegiSifirdanBaslar()
        {
            yield return RunSahnesiniHazirla();
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsNotNull(silah);
            int sarjor = silah.Classic.MagazineSize, yedek0 = silah.Classic.ReserveAmmo;
            _girdi.AtesBasili = true;
            float t0 = Time.realtimeSinceStartup;
            while (silah.Classic.Ammo > sarjor / 2 && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            _girdi.AtesBasili = false;
            _girdi.DoldurBasti = true;
            yield return null;
            _girdi.DoldurBasti = false;
            yield return Bekle(() => !silah.Classic.IsReloading, 8f, "reload bitmedi");
            Assert.Less(silah.Classic.ReserveAmmo, yedek0, "ölçüm boş: yedek harcanmadı");

            RunDirector.Instance.StartRun();
            yield return null;
            WeaponController yeni = WeaponRegistry.ActiveController;
            Assert.AreEqual(yeni.Classic.MagazineSize, yeni.Classic.Ammo, "yeni koşu dolu şarjörle başlar");
            Assert.AreEqual(yedek0, yeni.Classic.ReserveAmmo, "yeni koşu 1. tur yedeğiyle başlar, önceki koşunun artığıyla değil");
        }

        /// <summary>
        /// R1-B W6 × RT7 (C-00 30.09, found moving the batch onto main 1542284d): the same-scene restart re-equips the weapon
        /// from THIS run's tree. smg_1's fork X (×1.5 magazine) owned in run 1 must not reach run 2's first magazine — the
        /// re-equip used to run before BeginRunTree and read the previous run's still-built tree.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AyniSahnedeYeniKosu_OncekiKosununSarjorDugumunuTasimaz()
        {
            yield return RunSahnesiniHazirla();
            ClassicWeaponId onceki = WeaponRegistry.SelectedWeapon;
            try
            {
                Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.AutoSmg), "smg_1 kuşanılmadı");
                RunDirector.Instance.StartRun();   // this run's tree is built for smg_1
                yield return null;
                WeaponController silah = WeaponRegistry.ActiveController;
                int taban = silah.Classic.MagazineSize;
                RunTreeState agac = RunDirector.Instance.Shop.Tree;
                Assert.IsTrue(agac.MarkOwned(agac.Graph.IndexOf("fork_smg_1_x")), "fork_smg_1_x sahiplenilemedi");
                int buyuk = silah.Classic.MagazineSize;
                Assert.AreEqual(Mathf.FloorToInt(taban * 1.5f), buyuk, "ölçüm boş: çatal şarjörü büyütmedi");
                Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.AutoSmg));   // re-equip fills the fork's magazine
                Assert.AreEqual(buyuk, silah.Classic.Ammo, "ölçüm boş: büyük şarjör dolmadı");

                RunDirector.Instance.StartRun();
                yield return null;
                WeaponController yeni = WeaponRegistry.ActiveController;
                Assert.AreEqual(taban, yeni.Classic.MagazineSize, "yeni koşunun ağacında çatal yok");
                Assert.AreEqual(taban, yeni.Classic.Ammo, "yeni koşu önceki koşunun şarjör düğümünü taşımaz");
            }
            finally
            {
                WeaponRegistry.SelectedWeapon = onceki;   // T1: the process-wide selection goes back
            }
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator HareketVeNisanBagimsiz_KosarkenTersYoneAtesEder()
        {
            yield return RunSahnesiniHazirla();

            // Zırhlı bir Standard'a isabet zırh kırılması → hit-stop üretebilir; hit-stop ölçeksiz
            // saatle biter ve sabit yakalama adımı ölçeksiz zamanı ilerletmez (iter5: sim %6'da
            // takılı kaldı, 1.2 s'de 5 atış). Bu test gerçek karelerle koşar; pencereler zamanla ölçülür.
            Time.captureDeltaTime = 0f;

            Vector3 merkez = _rig.transform.position;
            ZombieEnemy hedef = Dogur(merkez + new Vector3(0f, 0f, -6f), merkez);
            Assert.IsNotNull(hedef);

            var vuruslar = new List<EnemyDamageInfo>(32);
            int olum = 0;
            Action<EnemyDamageInfo> dinle = i => vuruslar.Add(i);
            Action<EnemyDeathInfo> olumDinle = _ => olum++;
            DamageEvents.EnemyDamaged += dinle;
            DamageEvents.EnemyDied += olumDinle;

            int kontrolKare = 0;
            int uyumluKare = 0;
            int uyumsuzOrnek = 0;
            float enBuyukHizX = 0f;
            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] savas.bagimsizlik (+z kos, -z nisan, ates basili)");

            try
            {
                int mermiOnce = WeaponRegistry.ActiveController.Classic.Ammo;
                float t0 = Time.realtimeSinceStartup;
                float gecen = 0f;
                int k = 0;

                while (gecen < 1.2f)
                {
                    // Nişan: her karede zombinin göğsüne (nişan düzlemi yüksekliği).
                    Vector3 nisanNoktasi = hedef != null && hedef.IsAlive
                        ? hedef.transform.position + Vector3.up * NisanDuzlemi
                        : merkez + new Vector3(0f, NisanDuzlemi, -6f);
                    _girdi.Imlec = EkranNoktasi(nisanNoktasi);
                    _girdi.Yon = new Vector2(0f, 1f);
                    _girdi.AtesBasili = true;

                    yield return null;
                    gecen = Time.realtimeSinceStartup - t0;
                    k++;

                    // Hızlanma tamamlandıktan (0.138 s) sonra ölç.
                    if (gecen > 0.25f)
                    {
                        kontrolKare++;
                        Vector3 v = _rig.Motor.PlanarVelocity;
                        Vector3 bakis = _rig.Aim.AimDirection;
                        enBuyukHizX = Mathf.Max(enBuyukHizX, v.z);
                        if (v.z > 0.9f * OyuncuHizi && bakis.z < -0.9f)
                        {
                            uyumluKare++;
                        }
                        else if (uyumsuzOrnek < 6)
                        {
                            uyumsuzOrnek++;
                            rapor.AppendLine("    uyumsuz kare " + k + ": v=" + v.ToString("F2") + " bakis=" + bakis.ToString("F2") +
                                             " pos=" + _rig.transform.position.ToString("F1") + " hasAim=" + _rig.Aim.HasAimPoint +
                                             " aimPt=" + _rig.Aim.AimWorldPoint.ToString("F1") + " imlec=" + _girdi.Imlec.ToString("F0") +
                                             " ekran=" + Screen.width + "x" + Screen.height);
                        }
                    }
                }

                _girdi.AtesBasili = false;
                _girdi.Yon = Vector2.zero;
                int mermiSonra = WeaponRegistry.ActiveController.Classic.Ammo;

                int tersYonluIsabet = 0, kafaIsabeti = 0;
                for (int i = 0; i < vuruslar.Count; i++)
                {
                    Vector3 d = vuruslar[i].Direction;
                    if (d.z < -0.9f)
                    {
                        tersYonluIsabet++;
                    }

                    if (vuruslar[i].Result.Region == DD.Prototype.Damage.BodyRegion.Head) kafaIsabeti++;
                }

                // C-00 30.09 B: top-down shots are planar — no vertical scatter onto the head — so the running Standard
                // takes body hits only and 34 HP needs three of them (the 0af8269a red, isabet=2 olum=1, was a random ×2
                // head hit from the old scatter).
                const int gerekenIsabet = 3;

                rapor.AppendLine("  kare=" + k + " kontrol kare=" + kontrolKare + " uyumlu=" + uyumluKare +
                                 " en buyuk vz=" + enBuyukHizX.ToString("F2") +
                                 " mermi " + mermiOnce + "->" + mermiSonra +
                                 " isabet=" + vuruslar.Count + " ters yonlu=" + tersYonluIsabet + " kafa=" + kafaIsabeti + " olum=" + olum +
                                 " hitstop=" + HitStopService.Count + " timeScale=" + Time.timeScale.ToString("F2"));
                Debug.Log(rapor.ToString());

                Assert.Greater(kontrolKare, 10, "Kontrol penceresi çok kısa; ölçüm boş.\n" + rapor);
                Assert.Greater(mermiOnce - mermiSonra, 5,
                    "REQ-039: 1.2 sn basılı ateş yalnız " + (mermiOnce - mermiSonra) + " mermi harcadı.\n" + rapor);
                Assert.GreaterOrEqual(uyumluKare, kontrolKare * 0.9f,
                    "REQ-037: karakter +z'ye koşarken −z'ye bakmayı sürdüremedi (uyumlu " + uyumluKare + "/" + kontrolKare + ").\n" + rapor);
                Assert.AreEqual(0, kafaIsabeti, "Tepeden kafa isabeti (C-00 B: 0 beklenir).\n" + rapor);
                Assert.GreaterOrEqual(tersYonluIsabet, gerekenIsabet,
                    "REQ-037: hareket yönünün tersine ateş hedefe isabet etmedi (" + tersYonluIsabet + "/" + gerekenIsabet + " isabet).\n" + rapor);
            }
            finally
            {
                DamageEvents.EnemyDamaged -= dinle;
                DamageEvents.EnemyDied -= olumDinle;
            }
        }

        /// <summary>
        /// Basılı tutma: 1 sn'de ≥ 6 atış (8.5/s formül, ilk atış anında). Tek kare basış:
        /// TAM 1 atış — bir basışın bir atışa karşılık gelmesi hold-to-fire'ın öbür yüzüdür.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator HoldToFire_BasiliSurekliAtes_TekBasisTekAtis()
        {
            yield return RunSahnesiniHazirla();

            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsFalse(silah.Classic.InfiniteAmmo);
            yield return Bekle(() => silah.Classic.CanFire, 5f, "Silah ateşe hazır değil.");

            int once = silah.Classic.Ammo;
            _girdi.AtesBasili = true;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            _girdi.AtesBasili = false;
            int basiliSonra = silah.Classic.Ammo;
            int basiliAtis = once - basiliSonra;

            // Bırak, soğusun (2 aralık), sonra tek kare basış.
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            int tapOnce = silah.Classic.Ammo;
            _girdi.AtesBasti = true;
            _girdi.AtesBasili = true;
            yield return null;
            _girdi.AtesBasti = false;
            _girdi.AtesBasili = false;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            int tapAtis = tapOnce - silah.Classic.Ammo;

            Debug.Log("[DD-TEST] savas.holdtofire basili 1s atis=" + basiliAtis + " tek kare basis atis=" + tapAtis);

            int beklenen = Mathf.FloorToInt(TufekAtisHizi) + 1;   // 11: t=0 dahil
            Assert.GreaterOrEqual(basiliAtis, 6, "REQ-039: basılı tutma sürekli ateş üretmedi (" + basiliAtis + ").");
            Assert.LessOrEqual(basiliAtis, beklenen + 1, "Basılı ateş formülden hızlı: " + basiliAtis + " > " + (beklenen + 1));
            Assert.AreEqual(1, tapAtis, "Tek kare basış " + tapAtis + " atış üretti; bir basış bir atış olmalı.");
        }

        /// <summary>
        /// Donmuş simülasyonda (pause tutamağı) reload tuşu şarjör değiştirmeye başlamaz ve
        /// boş tetik dry-fire cue'su üretmez; tutamak kalkınca aynı basış reload başlatır
        /// (girdi yolunun canlı olduğu kanıtı). PauseFireTests'in ateş kapısını tamamlar.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator DonmusSimulasyonda_ReloadVeDryFireYok_CozulunceReloadVar()
        {
            yield return RunSahnesiniHazirla();

            WeaponController silah = WeaponRegistry.ActiveController;
            yield return Bekle(() => silah.Classic.CanFire, 5f, "Silah hazır değil.");

            // Şarjörü kısmen boşalt ki reload anlamlı olsun.
            _girdi.AtesBasili = true;
            for (int i = 0; i < 20; i++) yield return null;
            _girdi.AtesBasili = false;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.Less(silah.Classic.Ammo, silah.Classic.MagazineSize, "Şarjör boşalmadı; ölçüm boş.");
            Assert.IsFalse(silah.Classic.IsReloading);

            int reloadBasladi = 0;
            int dryFire = 0;
            Action<float> rDinle = _ => reloadBasladi++;
            Action dDinle = () => dryFire++;
            WeaponEvents.ReloadStarted += rDinle;
            WeaponEvents.DryFire += dDinle;

            try
            {
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                Assert.IsTrue(RunFlowClock.IsFrozen);

                _girdi.DoldurBasti = true;
                _girdi.AtesBasti = true;
                _girdi.AtesBasili = true;
                for (int i = 0; i < 6; i++) yield return null;
                _girdi.DoldurBasti = false;
                _girdi.AtesBasti = false;
                _girdi.AtesBasili = false;

                Assert.AreEqual(0, reloadBasladi, "Donmuş simülasyonda reload başladı.");
                Assert.IsFalse(silah.Classic.IsReloading, "Donmuş simülasyonda silah reload durumuna girdi.");
                Assert.AreEqual(0, dryFire, "Donmuş simülasyonda dry-fire olayı yayınlandı.");

                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                Assert.IsFalse(RunFlowClock.IsFrozen);

                _girdi.DoldurBasti = true;
                yield return null;
                _girdi.DoldurBasti = false;
                yield return null;

                Assert.AreEqual(1, reloadBasladi, "Çözülünce reload başlamadı; girdi yolu ölü (test geçersiz).");
                Assert.IsTrue(silah.Classic.IsReloading);
            }
            finally
            {
                WeaponEvents.ReloadStarted -= rDinle;
                WeaponEvents.DryFire -= dDinle;
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            }
        }

        // ================================================================ REQ-038

        /// <summary>
        /// Ekranın dört köşesi ve merkezi nişan düzlemine sonlu, plane-yüksekliğinde bir
        /// noktaya düşer; PlayerAim her köşede HasAimPoint verir ve normalize yön üretir.
        /// İmleç karakterin üstündeyken yön sabit kalır (spin yok).
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator EkranKoselerindeNisan_KararliVeSonlu()
        {
            yield return RunSahnesiniHazirla();

            PrototypeCameraRig kamera = PrototypeCameraRig.Active;
            float duzlemY = _rig.Aim.AimPlaneY;
            Vector2[] noktalar =
            {
                new Vector2(0f, 0f), new Vector2(Screen.width - 1f, 0f),
                new Vector2(0f, Screen.height - 1f), new Vector2(Screen.width - 1f, Screen.height - 1f),
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            };

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] nisan.koseler ekran=" + Screen.width + "x" + Screen.height);

            for (int i = 0; i < noktalar.Length; i++)
            {
                Vector3 dunya;
                bool ok = kamera.ScreenPointToAimPlane(noktalar[i], duzlemY, out dunya);
                rapor.AppendLine("  " + noktalar[i] + " -> " + ok + " " + dunya.ToString("F2"));
                Assert.IsTrue(ok, "Köşe " + noktalar[i] + " nişan düzlemine düşmedi.");
                Assert.IsFalse(float.IsNaN(dunya.x) || float.IsInfinity(dunya.x) || float.IsNaN(dunya.z), "Sonsuz/NaN nişan noktası.");
                Assert.AreEqual(duzlemY, dunya.y, 0.001f, "Nişan noktası düzlem yüksekliğinde değil.");

                _girdi.Imlec = noktalar[i];
                yield return null;
                yield return null;
                Assert.IsTrue(_rig.Aim.HasAimPoint, "PlayerAim köşede nişan noktası üretmedi: " + noktalar[i]);
                Assert.AreEqual(1f, _rig.Aim.AimDirection.magnitude, 0.001f, "Nişan yönü normalize değil.");
                Assert.AreEqual(0f, _rig.Aim.AimDirection.y, 0.001f, "Nişan yönü yatay değil.");
            }

            // İmleç tam karakterin üstünde: yön değişmez VE silah bakış yönüne ateş eder
            // (namlu kökten ~0.9 m önde olduğu için nişan noktası namlunun arkasına düşer;
            // WP-002 öncesi silah bu durumda namludan köke, yani ~145° geriye ateş ediyordu).
            _girdi.Imlec = new Vector2(Screen.width - 1f, Screen.height - 1f);
            // Dönüş tamamlansın: 720°/s ile en fazla 180° = 0.25 s; 30 kare (0.5 s) bol pay.
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Vector3 onceki = _rig.Aim.AimDirection;
            Assert.Less(Vector3.Angle(_rig.transform.forward, onceki), 1f, "Karakter köşedeki imlece dönmedi (REQ-038).");
            var binding = _rig.GetComponent<PlayerWeaponBinding>();
            Assert.IsNotNull(binding);
            // Kamera kadraj kayması geri çekilirken ekran noktası dünyada kayar; "imleç
            // karakterin üstünde" senaryosu için ekran noktası her karede yeniden hesaplanır.
            _girdi.Imlec = EkranNoktasi(_rig.transform.position + Vector3.up * NisanDuzlemi);
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                _girdi.Imlec = EkranNoktasi(_rig.transform.position + Vector3.up * NisanDuzlemi);
                if (i < 2)
                {
                    continue;   // kadraj yerleşene kadar ilk iki kare ölçülmez
                }
                Assert.Less(Vector3.Angle(onceki, _rig.Aim.AimDirection), 0.5f,
                    "İmleç karakterin üstündeyken nişan yönü döndü (spin).");
                Vector3 atis = binding.AimDirection;
                atis.y = 0f;
                Assert.Greater(Vector3.Dot(atis.normalized, _rig.Aim.AimDirection), 0.9f,
                    "İmleç karakterin üstündeyken silah bakış yönünün dışına ateş ediyor (açı " +
                    Vector3.Angle(atis, _rig.Aim.AimDirection).ToString("F0") + "°). root=" +
                    _rig.transform.position.ToString("F2") + " muzzle=" + binding.MuzzlePosition.ToString("F2") +
                    " aimPt=" + _rig.Aim.AimWorldPoint.ToString("F2") + " hasAim=" + _rig.Aim.HasAimPoint +
                    " bakis=" + _rig.Aim.AimDirection.ToString("F2") + " forward=" + _rig.transform.forward.ToString("F2") +
                    " imlec=" + _girdi.Imlec.ToString("F0"));
            }

            Debug.Log(rapor.ToString());
        }

        // ================================================================ REQ-043/044

        /// <summary>
        /// Tek zombi menzilde, oyuncu hareketsiz: ilk saldırı döngüsünde TAM 1 hasar
        /// (bir swing bir hasar); sonraki döngüde oyuncu telegrafı görünce kaçar → 0 hasar.
        /// Formül: telegraf 0.55 s'de oyuncu ~2.98 m açılır (0.5·45·0.138² + 6.2·0.412),
        /// zombi 2.7 m/s ile kovalasa da açık ≥ 1.6 + 0.55·(6.2−2.7) − 0.43 ≈ 3.1 m &gt;
        /// hit yarıçapı 0.765 + oyuncu 0.45 + soket 0.55 = 1.77 m.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator TekSwingTekHasar_TelegrafSirasindaKacanHasarAlmaz()
        {
            yield return RunSahnesiniHazirla();

            Vector3 merkez = _rig.transform.position;
            Vector3 zombiKonum = merkez + new Vector3(0f, 0f, 1.6f);
            ZombieEnemy zombi = Dogur(zombiKonum, merkez);
            Assert.IsNotNull(zombi);

            // Nişan zombiye ama ateş yok: kontrol kesintisi ölçümü için silah boşta.
            var vuruslar = new List<PlayerDamageInfo>(8);
            var vurusFazlari = new List<ZombieAttackPhase>(8);
            Action<PlayerDamageInfo> dinle = i =>
            {
                vuruslar.Add(i);
                vurusFazlari.Add(zombi != null ? zombi.AttackPhase : ZombieAttackPhase.None);
            };
            _can.Damaged += dinle;

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] savas.swing (1 zombi 1.6 m, once dur sonra kac)");

            try
            {
                // ---- 1 · hareketsiz, bir tam döngü (2.62 s): tam 1 hasar, Active fazında.
                int kare = Mathf.RoundToInt((SaldiriDongusu - 0.1f) / Adim);
                for (int k = 0; k < kare; k++)
                {
                    yield return null;
                }

                rapor.AppendLine("  dongu-1: hasar=" + vuruslar.Count + " can=" + _can.Current.ToString("F1"));
                Assert.AreEqual(1, vuruslar.Count,
                    "Tek zombi bir saldırı döngüsünde " + vuruslar.Count + " hasar uyguladı; tam 1 beklenir.\n" + rapor);
                Assert.AreEqual(ZombieAttackPhase.Active, vurusFazlari[0], "Hasar Active fazı dışında geldi (REQ-043).");
                Assert.AreEqual(TekVurusHasari, vuruslar[0].AppliedAmount, 0.01f);

                // ---- 2 · bir sonraki Windup'ı bekle, görür görmez −z'ye kaç.
                yield return Bekle(() => zombi.AttackPhase == ZombieAttackPhase.Windup, 5f, "İkinci telegraf başlamadı.");
                int hasarKacisOnce = vuruslar.Count;
                _girdi.Yon = new Vector2(0f, -1f);

                float sure = 0f;
                float enKucukSoketMesafe = float.MaxValue;
                while (sure < Telegraf + AktifPencere + Toparlanma)
                {
                    yield return null;
                    sure += Adim;
                    if (zombi.AttackPhase == ZombieAttackPhase.Active)
                    {
                        // Aktif penceredeki gerçek açıklık: soket → oyuncu.
                        float m = Vector3.Distance(zombi.transform.position + zombi.transform.forward * 0.55f, _rig.transform.position);
                        enKucukSoketMesafe = Mathf.Min(enKucukSoketMesafe, m);
                    }
                }

                _girdi.Yon = Vector2.zero;
                rapor.AppendLine("  kacis: hasar=" + (vuruslar.Count - hasarKacisOnce) +
                                 " aktif penceredeki en kucuk soket-oyuncu mesafesi=" +
                                 (enKucukSoketMesafe < float.MaxValue ? enKucukSoketMesafe.ToString("F2") : "olculmedi"));
                Debug.Log(rapor.ToString());

                Assert.AreEqual(hasarKacisOnce, vuruslar.Count,
                    "Telegraf sırasında kaçan oyuncu yine hasar aldı; saldırı görülüp kaçılabilir olmalı (DEC-029).\n" + rapor);
                Assert.Less(enKucukSoketMesafe, float.MaxValue, "Aktif pencere gözlenmedi; ölçüm boş.\n" + rapor);
                Assert.Greater(enKucukSoketMesafe, 1.2f,
                    "Kaçış açıklığı formülden az: " + enKucukSoketMesafe.ToString("F2") + " m.\n" + rapor);
            }
            finally
            {
                _can.Damaged -= dinle;
            }
        }

        /// <summary>
        /// İki zombi simetrik konumda, aynı karede doğar ve aynı karede saldırır: iki hasar
        /// olayı ikisi de TAM 10, aralarında en fazla 3 kare; can 100 → 80 (REQ-044).
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator IkiZombiEszamanliSaldiri_IkiTamHasarUstUste()
        {
            yield return RunSahnesiniHazirla();

            Vector3 merkez = _rig.transform.position;
            Assert.IsNotNull(Dogur(merkez + new Vector3(1.55f, 0f, 0f), merkez));
            Assert.IsNotNull(Dogur(merkez + new Vector3(-1.55f, 0f, 0f), merkez));

            var vuruslar = new List<PlayerDamageInfo>(8);
            var kareler = new List<int>(8);
            Action<PlayerDamageInfo> dinle = i => { vuruslar.Add(i); kareler.Add(Time.frameCount); };
            _can.Damaged += dinle;

            try
            {
                int kare = Mathf.RoundToInt(1.5f / Adim);
                for (int k = 0; k < kare; k++)
                {
                    yield return null;
                }

                var rapor = new StringBuilder();
                rapor.AppendLine("[DD-TEST] savas.eszamanli (2 zombi ±1.55 m)");
                for (int i = 0; i < vuruslar.Count; i++)
                {
                    rapor.AppendLine("  vurus[" + i + "] kare=" + kareler[i] + " uygulanan=" + vuruslar[i].AppliedAmount.ToString("F1") +
                                     " yon.x=" + vuruslar[i].HitDirection.x.ToString("F2"));
                }

                Debug.Log(rapor.ToString());

                Assert.AreEqual(2, vuruslar.Count, "İki zombinin ilk saldırısından " + vuruslar.Count + " hasar geldi; 2 beklenir.\n" + rapor);
                Assert.AreEqual(TekVurusHasari, vuruslar[0].AppliedAmount, 0.01f);
                Assert.AreEqual(TekVurusHasari, vuruslar[1].AppliedAmount, 0.01f, "İkinci eşzamanlı hasar tam uygulanmadı (REQ-044).");
                Assert.LessOrEqual(Mathf.Abs(kareler[1] - kareler[0]), 3, "İki saldırı eşzamanlı değil: " + kareler[0] + " / " + kareler[1]);
                Assert.Less(vuruslar[0].HitDirection.x * vuruslar[1].HitDirection.x, 0f, "İki hasar aynı zombiden geldi (yönler aynı taraf).");
                Assert.AreEqual(BaslangicCani - 2f * TekVurusHasari, _can.Current, 0.01f, "Can 100 → 80 olmalı.\n" + rapor);
            }
            finally
            {
                _can.Damaged -= dinle;
            }
        }

        // ================================================================ REQ-046

        /// <summary>
        /// Beş canlı zombi duvarı önde: 1.5 sn tam güç ileri → yer değiştirme küçük (engel).
        /// Duvar öldürülür (KillAll → detectCollisions kapanır): aynı itme → oyuncu geçer.
        /// Formül: 1.5 s'de serbest yol ≈ 6.2·1.5 − 0.43 = 8.9 m; eşik engelli &lt; 1.0 m, serbest &gt; 5 m.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator CanliZombiDuvari_OyuncuyuDurdurur_OluZombiGecilir()
        {
            yield return RunSahnesiniHazirla();

            Vector3 merkez = _rig.transform.position;
            for (int i = -2; i <= 2; i++)
            {
                Assert.IsNotNull(Dogur(merkez + new Vector3(i * 0.95f, 0f, 1.4f), merkez), "Duvar zombisi doğmadı.");
            }

            yield return null;

            Vector3 basla = _rig.transform.position;
            _girdi.Yon = new Vector2(0f, 1f);
            for (int k = 0; k < 90; k++)
            {
                yield return null;
            }

            _girdi.Yon = Vector2.zero;
            float engelli = Vector3.Distance(Yatay(_rig.transform.position), Yatay(basla));

            int olduruldu = EnemyFactory.KillAll();
            yield return null;

            Vector3 basla2 = _rig.transform.position;
            _girdi.Yon = new Vector2(0f, 1f);
            for (int k = 0; k < 90; k++)
            {
                yield return null;
            }

            _girdi.Yon = Vector2.zero;
            float serbest = Vector3.Distance(Yatay(_rig.transform.position), Yatay(basla2));

            Debug.Log("[DD-TEST] savas.collision engelli=" + engelli.ToString("F2") + " m  olu duvar sonrasi=" + serbest.ToString("F2") +
                      " m  olduruldu=" + olduruldu + "  can=" + _can.Current.ToString("F1"));

            Assert.AreEqual(5, olduruldu);
            Assert.Less(engelli, 1.0f, "REQ-046: oyuncu canlı zombi duvarından " + engelli.ToString("F2") + " m geçti.");
            Assert.Greater(serbest, 5f, "Ölü zombiler oyuncuyu hâlâ engelliyor: " + serbest.ToString("F2") + " m.");
        }

        // ================================================================ REQ-048 / DEC-034

        /// <summary>
        /// Sürü oyuncuyu doğal yolla öldürür: Died 1, AnyPlayerDied 1, RunEnded 1, faz Dead;
        /// sonrasında zombi saldırıları sürse de (PlayerAttacked &gt; 0) can 0'da kalır, Damaged
        /// gelmez, kontrol kapalı, silah ateş edemez, regen yok.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator OyuncuOlumu_TekEndRun_SonrasiHasarKontrolAtesRegenYok()
        {
            yield return RunSahnesiniHazirla();
            _olumTesti = true;

            Vector3 merkez = _rig.transform.position;
            for (int i = 0; i < 8; i++)
            {
                float aci = i * Mathf.PI * 2f / 8f;
                Assert.IsNotNull(Dogur(merkez + new Vector3(Mathf.Cos(aci), 0f, Mathf.Sin(aci)) * 1.6f, merkez));
            }

            int died = 0, anyDied = 0, runEnded = 0, damagedSonrasi = 0, attackedSonrasi = 0;
            bool olduMu = false;
            Action<PlayerDeathInfo> diedDinle = _ => died++;
            Action<PlayerDeathInfo> anyDinle = _ => anyDied++;
            Action<RunSummary> runDinle = _ => runEnded++;
            Action<PlayerDamageInfo> dmgDinle = _ => { if (olduMu) damagedSonrasi++; };
            Action<EnemyAttackLandedInfo> atkDinle = _ => { if (olduMu) attackedSonrasi++; };
            _can.Died += diedDinle;
            PlayerHealth.AnyPlayerDied += anyDinle;
            RunEvents.RunEnded += runDinle;
            _can.Damaged += dmgDinle;
            DamageEvents.PlayerAttacked += atkDinle;

            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (!_can.IsDead && Time.realtimeSinceStartup - t0 < 60f)
                {
                    yield return null;
                }

                Assert.IsTrue(_can.IsDead, "8 zombi 60 sn'de oyuncuyu öldüremedi.");
                olduMu = true;
                Assert.AreEqual(0f, _can.Current, 0.0001f);
                Assert.AreEqual(1, died, "Died " + died + " kez.");
                Assert.AreEqual(1, anyDied, "AnyPlayerDied " + anyDied + " kez.");
                Assert.AreEqual(1, runEnded, "RunEnded " + runEnded + " kez (tek end-run).");
                Assert.AreEqual(RunPhase.Dead, RunDirector.Instance.Phase);
                Assert.IsFalse(_rig.Motor.ControlEnabled, "Ölümden sonra hareket kontrolü açık.");
                Assert.IsFalse(_rig.Aim.ControlEnabled, "Ölümden sonra nişan kontrolü açık.");

                var binding = _rig.GetComponent<PlayerWeaponBinding>();
                Assert.IsNotNull(binding);
                Assert.IsFalse(binding.CanFire, "Ölümden sonra silah ateş edebilir.");

                // 0.8 s sürü saldırmayı sürdürür (Results'a geçiş 1.6 s'de).
                _girdi.AtesBasili = true;
                _girdi.Yon = new Vector2(1f, 0f);
                int mermi = WeaponRegistry.ActiveController != null ? WeaponRegistry.ActiveController.Classic.Ammo : -1;
                Vector3 konum = _rig.transform.position;
                for (int k = 0; k < 48; k++)
                {
                    yield return null;
                }

                _girdi.AtesBasili = false;
                _girdi.Yon = Vector2.zero;

                Debug.Log("[DD-TEST] savas.olum died=" + died + " anyDied=" + anyDied + " runEnded=" + runEnded +
                          " sonrasi attacked=" + attackedSonrasi + " damaged=" + damagedSonrasi + " can=" + _can.Current);

                Assert.AreEqual(0, damagedSonrasi, "Ölümden sonra Damaged olayı geldi.");
                Assert.AreEqual(0, attackedSonrasi,
                    "Ölümden sonra " + attackedSonrasi + " PlayerAttacked olayı yayınlandı; ölü oyuncuya 'hasar uygulandı' sinyali verilmemeli.");
                Assert.AreEqual(0f, _can.Current, 0.0001f, "Ölümden sonra can değişti (regen/hasar).");
                Assert.AreEqual(1, died, "Ölümden sonra ikinci Died.");
                Assert.AreEqual(1, runEnded, "Ölümden sonra ikinci RunEnded.");
                if (WeaponRegistry.ActiveController != null && mermi >= 0)
                {
                    Assert.AreEqual(mermi, WeaponRegistry.ActiveController.Classic.Ammo, "Ölü oyuncu ateş etti.");
                }

                Assert.Less(Vector3.Distance(Yatay(_rig.transform.position), Yatay(konum)), 0.2f, "Ölü oyuncu hareket etti.");
            }
            finally
            {
                _can.Died -= diedDinle;
                PlayerHealth.AnyPlayerDied -= anyDinle;
                RunEvents.RunEnded -= runDinle;
                _can.Damaged -= dmgDinle;
                DamageEvents.PlayerAttacked -= atkDinle;
            }
        }

        // ================================================================ REQ-121/125/127

        /// <summary>
        /// Game-feel olay sözleşmesi: bir zombi AR ile öldürülür → EnemyDamaged (flesh,
        /// Killed=false) ≥ 3, EnemyHitDressed FleshHit tier'ları ve tam 1 Death tier'ı,
        /// EnemyDied tam 1 (Standard, hit-stop önerisi 0), HitStopService.Count değişmez
        /// (Medium silah), ses cue'ları ClassicWeaponFire/FleshHit/EnemyDeath ayrı ayrı gelir;
        /// ardından oyuncu bir zombiden hasar alır → AnyDamaged 1, HitDirection normalize,
        /// kamera Bump sarsıntısı aktif ve hasar flaşı &gt; 0 (görsel/işitsel/kamera geri bildirimi).
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator GameFeelOlaylari_FleshHitOlumVeOyuncuHasari_AyriSinyaller()
        {
            yield return RunSahnesiniHazirla();

            // Ses bastırma penceresi ve hit-stop ölçeksiz saatle çalışır; sabit yakalama adımı
            // (captureDeltaTime) ölçeksiz zamanı ilerletmez → bu test gerçek karelerle koşar.
            Time.captureDeltaTime = 0f;

            Vector3 merkez = _rig.transform.position;
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);
            ZombieEnemy hedef = Dogur(merkez + new Vector3(0f, 0f, 7f), merkez);
            Assert.IsNotNull(hedef);
            // WP-018: a rolled helmet turned "flesh" hits into armour hits; this test is about the flesh-hit / death /
            // player-damage signals, not the roll — the target is bare.
            hedef.FitArmorComposition(false, false, false);
            Collider govde = hedef.HitboxFor(DD.Prototype.Damage.BodyRegion.UpperBody);
            Collider kafa = hedef.HitboxFor(DD.Prototype.Damage.BodyRegion.Head);
            Assert.IsNotNull(govde);
            Assert.IsNotNull(kafa);

            var hasar = new List<EnemyDamageInfo>(16);
            var geo = new List<string>(16);   // per hit: line y, head / upper box min–max, entry distances, expected region
            var beklenenBolge = new List<DD.Prototype.Damage.BodyRegion>(16);
            var olum = new List<EnemyDeathInfo>(4);
            var tierler = new List<HitFeedbackTier>(16);
            var cueler = new List<CombatAudioEvent>(64);
            Action<EnemyDamageInfo> hDinle = i =>
            {
                hasar.Add(i);
                // C-00 01.10 (option 1): the expected region from the real colliders NOW — the flat line enters the head box first
                // → Head, else UpperBody (the capsule fallback counts as UpperBody too). The target's own flinch can dip the head
                // onto the line (main-ci 55c6e37f / b5g: head centre 1.80 → 1.37 over three hits), so "head 0" was not physical.
                Bounds hb = kafa.bounds, ub = govde.bounds;
                var ray = new Ray(i.Point - i.Direction.normalized * 3f, i.Direction.normalized);
                bool headOn = hb.IntersectRay(ray, out float hd);
                bool upOn = ub.IntersectRay(ray, out float ud);
                beklenenBolge.Add(headOn && (!upOn || hd <= ud) ? DD.Prototype.Damage.BodyRegion.Head : DD.Prototype.Damage.BodyRegion.UpperBody);
                geo.Add("line y " + i.Point.y.ToString("F3") + " head y " + hb.min.y.ToString("F3") + "–" + hb.max.y.ToString("F3") +
                        (headOn ? " (enter " + hd.ToString("F2") + ")" : " (off line)") + " upper y " + ub.min.y.ToString("F3") + "–" + ub.max.y.ToString("F3") +
                        (upOn ? " (enter " + ud.ToString("F2") + ")" : " (off line)") + " hit " + i.Result.Region);
                Debug.Log("[DD-TEST] gamefeel.impact region=" + i.Result.Region + " hp=" + i.Result.HealthDamage +
                          " point=" + i.Point.ToString("F3") + " direction=" + i.Direction.ToString("F3") +
                          " rising=" + hedef.IsRising + " head=" + kafa.bounds + " upper=" + govde.bounds);
            };
            Action<ProjectileSpawnInfo> atisDinle = i => Debug.Log("[DD-TEST] gamefeel.shot id=" + i.ProjectileId +
                " origin=" + i.Position.ToString("F3") + " direction=" + i.Direction.ToString("F3"));
            Action<EnemyDeathInfo> oDinle = i => olum.Add(i);
            Action<EnemyHitFeedbackInfo, HitFeedbackTier> tDinle = (i, t) => tierler.Add(t);
            var bastirilan = new List<CombatAudioEvent>(64);
            Action<CombatAudioCue> cDinle = c => cueler.Add(c.Event);
            Action<CombatAudioCue> bDinle = c => bastirilan.Add(c.Event);
            ProjectileEvents.Spawned += atisDinle;
            DamageEvents.EnemyDamaged += hDinle;
            DamageEvents.EnemyDied += oDinle;
            HitFeedbackDirector.EnemyHitDressed += tDinle;
            AudioEvents.Cue += cDinle;
            AudioEvents.CueSuppressed += bDinle;
            int hitStopOnce = HitStopService.Count;
            float unscaledOnce = Time.unscaledTime;

            try
            {
                // C-00 30.09 decision B: top-down shots carry no vertical scatter — the damage ray runs flat at root +
                // ShotRayHeight (1.39 m), below an upright Standard's head sphere — so a held trigger on the torso lands
                // body hits only, and 34 HP / 12 is exactly three of them (the 0af8269a red was a random ×2 head hit
                // from the vertical scatter). The head bounds ride on every impact log line (pose-driven head dips are
                // measured separately).
                float t0 = Time.realtimeSinceStartup;
                while (hedef.IsAlive && Time.realtimeSinceStartup - t0 < 20f)
                {
                    Vector3 nisan = govde.bounds.center;
                    nisan.y = _rig.transform.position.y + NisanDuzlemi;
                    _girdi.Imlec = EkranNoktasi(nisan);
                    _girdi.AtesBasili = true;
                    yield return null;
                }

                _girdi.AtesBasili = false;
                Assert.IsFalse(hedef.IsAlive, "Zombi 20 sn'de öldürülemedi.");
                yield return null;

                // Ceset penceresi: 0.4 s daha cesede ateş — yeni giydirme, hasar olayı veya flesh cue YOK.
                int dressedOlumde = tierler.Count;
                int hasarOlumde = hasar.Count;
                int fleshCueOlumde = 0;
                for (int i = 0; i < cueler.Count; i++) if (cueler[i] == CombatAudioEvent.FleshHit) fleshCueOlumde++;
                Vector3 cesetNoktasi = hedef.transform.position + Vector3.up * NisanDuzlemi;
                int cesetMermiOnce = WeaponRegistry.ActiveController.Classic.Ammo;
                float cesetT0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - cesetT0 < 0.45f)   // ceset penceresi (~1.6 s) içinde, ≥ 3 atış
                {
                    _girdi.Imlec = EkranNoktasi(cesetNoktasi);
                    _girdi.AtesBasili = true;
                    yield return null;
                }

                _girdi.AtesBasili = false;
                int cesetMermiSonra = WeaponRegistry.ActiveController.Classic.Ammo;
                int fleshCueSonra = 0;
                for (int i = 0; i < cueler.Count; i++) if (cueler[i] == CombatAudioEvent.FleshHit) fleshCueSonra++;
                Assert.Less(cesetMermiSonra, cesetMermiOnce, "Ceset ölçümünde silah ateş etmedi; ölçüm boş.");
                Assert.AreEqual(dressedOlumde, tierler.Count, "Cesede isabet 'vuruş' olarak giydirildi (ölümden sonra etki kalmamalı).");
                Assert.AreEqual(hasarOlumde, hasar.Count, "Cesede isabet EnemyDamaged yayınladı.");
                Assert.AreEqual(fleshCueOlumde, fleshCueSonra, "Cesede isabet FleshHit sesi çaldı.");

                int flesh = 0, olumTier = 0, digerTier = 0;
                for (int i = 0; i < tierler.Count; i++)
                {
                    if (tierler[i] == HitFeedbackTier.FleshHit) flesh++;
                    else if (tierler[i] == HitFeedbackTier.Death) olumTier++;
                    else digerTier++;
                }

                int ates = 0, fleshCue = 0, olumCue = 0;
                for (int i = 0; i < cueler.Count; i++)
                {
                    if (cueler[i] == CombatAudioEvent.ClassicWeaponFire) ates++;
                    else if (cueler[i] == CombatAudioEvent.FleshHit) fleshCue++;
                    else if (cueler[i] == CombatAudioEvent.EnemyDeath) olumCue++;
                }

                var rapor = new StringBuilder();
                int atesBastirilan = 0;
                for (int i = 0; i < bastirilan.Count; i++) if (bastirilan[i] == CombatAudioEvent.ClassicWeaponFire) atesBastirilan++;
                rapor.AppendLine("[DD-TEST] gamefeel.olaylar hasar=" + hasar.Count + " olum=" + olum.Count +
                                 " tier flesh/death/diger=" + flesh + "/" + olumTier + "/" + digerTier +
                                 " cue ates/flesh/olum=" + ates + "/" + fleshCue + "/" + olumCue +
                                 " ates-bastirilan=" + atesBastirilan + " unscaled+=" + (Time.unscaledTime - unscaledOnce).ToString("F2") +
                                 " hitstop " + hitStopOnce + "->" + HitStopService.Count);
                int criticalCue = 0, fleshBastirilan = 0, criticalBastirilan = 0;
                foreach (var cue in cueler) if (cue == CombatAudioEvent.CriticalHit) criticalCue++;
                foreach (var cue in bastirilan)
                {
                    if (cue == CombatAudioEvent.FleshHit) fleshBastirilan++;
                    if (cue == CombatAudioEvent.CriticalHit) criticalBastirilan++;
                }
                rapor.AppendLine("critical-cue=" + criticalCue + " suppressed-flesh/critical=" +
                                 fleshBastirilan + "/" + criticalBastirilan);
                Debug.Log(rapor.ToString());
                for (int i = 0; i < hasar.Count; i++) Debug.Log("[DD-TEST] gamefeel.hit " + i + " region=" + hasar[i].Result.Region + " hp=" + hasar[i].Result.HealthDamage + " killed=" + hasar[i].Result.Killed + " armored=" + hasar[i].Result.WasArmored + " tier=" + (i < tierler.Count ? tierler[i].ToString() : "-"));

                // C-00 30.09 B + 01.10 (option 1): a top-down hit lands where the flat line meets the body NOW — Head when the line
                // enters the head box first, else UpperBody (12 HP; a bare head ×2 = 24), dressed FleshHit until the killing one
                // (Death); the hit count is the first n whose damage reaches 34 HP (three body hits, or fewer with a head hit).
                int beklenenIsabet = 0;
                float toplam = 0f;
                for (int i = 0; i < hasar.Count && toplam < ZombiCani; i++)
                {
                    toplam += TufekHasari * (beklenenBolge[i] == DD.Prototype.Damage.BodyRegion.Head ? 2f : 1f);
                    beklenenIsabet++;
                }
                Assert.AreEqual(hasar.Count, tierler.Count, "Her EnemyDamaged tek bir dressed tier üretmeli.\n" + rapor);
                int kafaIsabeti = 0, killedSayisi = 0;
                for (int i = 0; i < hasar.Count; i++)
                {
                    DamageResult r = hasar[i].Result;
                    if (r.Region == DD.Prototype.Damage.BodyRegion.Head) kafaIsabeti++;
                    Assert.AreEqual(beklenenBolge[i], r.Region, "Tepeden isabet " + i + " bölgesi geometriyle uyuşmuyor (C-00 01.10): " + geo[i] + ".\n" + rapor);
                    Assert.IsFalse(r.WasArmored, "Çıplak hedefte zırh isabeti (" + i + ").\n" + rapor);
                    float bolgeHasari = TufekHasari * (r.Region == DD.Prototype.Damage.BodyRegion.Head ? 2f : 1f);
                    Assert.AreEqual(bolgeHasari, r.HealthDamage, 0.001f, "Bölge hasarı formülden farklı (" + i + ", " + r.Region + ").\n" + rapor);
                    Assert.AreEqual(r.Killed ? HitFeedbackTier.Death : HitFeedbackTier.FleshHit, tierler[i], "Gövde hasarı ile dressed tier eşleşmiyor (" + i + ").\n" + rapor);
                    Assert.AreEqual(EnemyTier.Standard, hasar[i].Tier);
                    if (r.Killed) killedSayisi++;
                }

                Debug.Log("[DD-TEST] gamefeel.geo kafa=" + kafaIsabeti + " | " + string.Join(" | ", geo));
                Assert.AreEqual(beklenenIsabet, hasar.Count, "34 HP = bölgelere göre " + beklenenIsabet + " isabet.\n" + rapor);
                Assert.IsTrue(hasar[hasar.Count - 1].Result.Killed, "Öldüren isabet son hasar olayı değil.\n" + rapor);
                Assert.AreEqual(1, killedSayisi, "Killed=true olan EnemyDamaged sayısı 1 olmalı.");
                Assert.AreEqual(1, olum.Count, "EnemyDied " + olum.Count + " kez (tam 1).\n" + rapor);
                Assert.AreEqual(1, olumTier, "Death tier'ı " + olumTier + " kez dressed (tam 1).\n" + rapor);
                Assert.AreEqual(beklenenIsabet - 1, flesh, "FleshHit tier'ı ayrı sinyal üretmedi.\n" + rapor);
                Assert.AreEqual(EnemyTier.Standard, olum[0].Tier);
                Assert.IsFalse(olum[0].WasHeavyDeath);
                Assert.AreEqual(0f, olum[0].SuggestedHitStop, 0.0001f, "Standard ölümü hit-stop önerdi (REQ-127).");
                Assert.AreEqual(hitStopOnce, HitStopService.Count, "REQ-127: AR ile flesh hit/ölüm hit-stop üretti.");
                Assert.GreaterOrEqual(ates, beklenenIsabet, "Ateş sesi cue'su eksik.");
                Assert.GreaterOrEqual(fleshCue, 1, "FleshHit ses cue'su yok.");
                Assert.AreEqual(1, olumCue, "EnemyDeath ses cue'su " + olumCue + " kez (tam 1).");

                // ---- oyuncu hasarı geri bildirimi
                var oyuncuHasar = new List<PlayerDamageInfo>(4);
                float flash = 0f;
                int bumpIstek = 0, bumpAlindi = 0, bumpHasarda = -1;
                var feedback = _rig.GetComponent<PlayerDamageFeedback>();
                Action<PlayerDamageInfo> pDinle = i =>
                {
                    oyuncuHasar.Add(i);
                    if (bumpHasarda < 0) bumpHasarda = bumpIstek;
                    flash = feedback != null ? feedback.FlashStrength01 : -1f;
                };
                // The Bump is counted when it reaches the camera (the bridge's public handlers, forwarded untouched) and the
                // service took it — not by sampling ActiveCount a frame later: the player-damage Bump lasts 0.15 s, so one
                // long CI frame could end it before the sample (2b300f06 red; S-07 25.09).
                Action<float, float, ShakeProfile> eskiShake = CameraShakeBridge.Handler;
                Action<float, float, ShakeProfile, Vector3> eskiYonlu = CameraShakeBridge.DirectionalHandler;
                CameraShakeBridge.Handler = (a, d, prof) =>
                {
                    eskiShake?.Invoke(a, d, prof);
                    if (CameraShakeBridge.ToShakeKind(prof) != ShakeKind.Bump) return;
                    bumpIstek++;
                    if (CameraShakeService.ActiveCount > 0) bumpAlindi++;
                };
                CameraShakeBridge.DirectionalHandler = (a, d, prof, dir) =>
                {
                    eskiYonlu?.Invoke(a, d, prof, dir);
                    if (CameraShakeBridge.ToShakeKind(prof) != ShakeKind.Bump) return;
                    bumpIstek++;
                    if (CameraShakeService.ActiveCount > 0) bumpAlindi++;
                };
                PlayerHealth.AnyDamaged += pDinle;
                try
                {
                    Assert.IsNotNull(Dogur(merkez + new Vector3(0f, 0f, 1.6f), merkez));
                    yield return Bekle(() => oyuncuHasar.Count > 0, 6f, "Zombi oyuncuya vuramadı.");
                    // WP-062 presentation arbitration flushes once in LateUpdate, after the gameplay damage callback that
                    // deliberately precedes presentation: the Bump request lands at that same-frame router boundary.
                    yield return null;
                    int shakeSayisi = bumpIstek - bumpHasarda;

                    Assert.AreEqual(1, oyuncuHasar.Count);
                    Assert.AreEqual(1f, oyuncuHasar[0].HitDirection.magnitude, 0.01f, "HitDirection normalize değil.");
                    Assert.AreEqual(TekVurusHasari, oyuncuHasar[0].AppliedAmount, 0.01f);
                    StringAssert.StartsWith("Zombie.", oyuncuHasar[0].SourceId);
                    Assert.Greater(flash, 0f, "Hasar flaşı (görsel geri bildirim) yok.");
                    Assert.Greater(shakeSayisi, 0, "Hasar kamera Bump sarsıntısı üretmedi (DEC-031).");
                    Assert.Greater(bumpAlindi, 0, "Bump kameraya ulaştı ama CameraShakeService almadı (alıcı yok?).");
                }
                finally
                {
                    PlayerHealth.AnyDamaged -= pDinle;
                    CameraShakeBridge.Handler = eskiShake;
                    CameraShakeBridge.DirectionalHandler = eskiYonlu;
                }
            }
            finally
            {
                ProjectileEvents.Spawned -= atisDinle;
                DamageEvents.EnemyDamaged -= hDinle;
                DamageEvents.EnemyDied -= oDinle;
                HitFeedbackDirector.EnemyHitDressed -= tDinle;
                AudioEvents.Cue -= cDinle;
                AudioEvents.CueSuppressed -= bDinle;
            }
        }

        // ================================================================ Aşama 8.11 · performans gözlemi

        /// <summary>
        /// GÖZLEM, KAPI DEĞİL (onaylı performans hedefi yok — sayılar ürün kabul sayısına
        /// dönüştürülmez). 30 Standard zombi, oyuncu tanı dokunulmazlığında, 10 sn sürekli
        /// AR ateşi, gerçek kare süresi (captureDeltaTime 0). Raporlanan: kare p50/p95/max ms,
        /// GC toplama sayısı, mono heap farkı, kare başına GC ayırma (ProfilerRecorder),
        /// crowd çözücü ms, aktif zombi/CharacterController/Collider sayıları, havuz doluluğu.
        /// Tek assert: ölçüm gerçekten yapıldı (kare &gt; 0, zombi ≥ 1, değerler sonlu).
        /// Editor batchmode ölçümü Player'ı temsil etmez (JIT/Editor kancaları, küçük Game View).
        /// </summary>
        [UnityTest]
        [Category("Perf")]
        [Timeout(180000)]
        public IEnumerator PerformansGozlemi_30Zombi_10snSurekliAtes()
        {
            yield return RunSahnesiniHazirla();
            Time.captureDeltaTime = 0f;
            _can.DiagnosisInvulnerable = true;   // tanı; ölçüm ölümle kesilmesin

            Vector3 merkez = _rig.transform.position;
            int dogan = 0;
            for (int i = 0; i < 30; i++)
            {
                float aci = i * Mathf.PI * 2f / 30f;
                float r = 4f + (i % 3) * 1.5f;
                if (Dogur(merkez + new Vector3(Mathf.Cos(aci), 0f, Mathf.Sin(aci)) * r, merkez) != null) dogan++;
            }

            var kareMs = new List<float>(1200);
            int gc0 = GC.CollectionCount(0);
            long mono0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var gcAlloc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            bool gcAllocVar = gcAlloc.Valid;
            long gcAllocToplam = 0;
            int gcAllocOrnek = 0;

            float baslangic = Time.realtimeSinceStartup;
            float oncekiKare = baslangic;
            try
            {
                while (Time.realtimeSinceStartup - baslangic < 10f)
                {
                    // Nişan: en yakın canlı zombi.
                    ZombieEnemy hedef = null;
                    float enYakin = float.MaxValue;
                    IReadOnlyList<ZombieEnemy> canli = ZombieEnemy.Live;
                    for (int i = 0; i < canli.Count; i++)
                    {
                        if (canli[i] == null || !canli[i].IsAlive) continue;
                        float d = (canli[i].transform.position - _rig.transform.position).sqrMagnitude;
                        if (d < enYakin) { enYakin = d; hedef = canli[i]; }
                    }

                    if (hedef != null)
                    {
                        _girdi.Imlec = EkranNoktasi(hedef.transform.position + Vector3.up * NisanDuzlemi);
                        _girdi.AtesBasili = true;
                    }
                    else
                    {
                        _girdi.AtesBasili = false;
                    }

                    yield return null;
                    float simdi = Time.realtimeSinceStartup;
                    kareMs.Add((simdi - oncekiKare) * 1000f);
                    oncekiKare = simdi;
                    if (gcAlloc.Valid)
                    {
                        gcAllocToplam += gcAlloc.LastValue;
                        gcAllocOrnek++;
                    }
                }
            }
            finally
            {
                _girdi.AtesBasili = false;
                gcAlloc.Dispose();
                _can.DiagnosisInvulnerable = false;
            }

            int gc1 = GC.CollectionCount(0);
            long mono1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            kareMs.Sort();
            float p50 = kareMs[kareMs.Count / 2];
            float p95 = kareMs[Mathf.Min(kareMs.Count - 1, Mathf.RoundToInt(kareMs.Count * 0.95f))];
            float max = kareMs[kareMs.Count - 1];

            var havuz = new StringBuilder();
            IReadOnlyList<DD.Prototype.Core.Pooling.IPrototypePool> havuzlar = DD.Prototype.Core.Pooling.PoolService.Pools;
            for (int i = 0; i < havuzlar.Count; i++)
            {
                havuz.Append(havuzlar[i].Name).Append('=').Append(havuzlar[i].CountSpawned).Append('/')
                     .Append(havuzlar[i].CountSpawned + havuzlar[i].CountAvailable).Append(' ');
            }

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-PERF] gozlem (kapi degil) 30 Standard zombi (dogan " + dogan + "), 10 s surekli AR atesi, Editor batchmode");
            rapor.AppendLine("  kare: n=" + kareMs.Count + " p50=" + p50.ToString("F2") + " ms p95=" + p95.ToString("F2") + " ms max=" + max.ToString("F2") + " ms");
            rapor.AppendLine("  GC: collections(gen0)=" + (gc1 - gc0) + " mono heap delta=" + ((mono1 - mono0) / 1024) + " KB" +
                             " gc-alloc/frame avg=" + (gcAllocOrnek > 0 ? (gcAllocToplam / gcAllocOrnek) : -1) + " B (" + (gcAllocVar ? "recorder" : "recorder yok") + ")");
            rapor.AppendLine("  crowd: members=" + DD.Prototype.Navigation.CrowdService.MemberCount +
                             " solve last/avg/peak=" + DD.Prototype.Navigation.CrowdService.Solver.LastSolveMilliseconds.ToString("F3") + "/" +
                             DD.Prototype.Navigation.CrowdService.Solver.AverageSolveMilliseconds.ToString("F3") + "/" +
                             DD.Prototype.Navigation.CrowdService.Solver.PeakSolveMilliseconds.ToString("F3") + " ms pairTests=" +
                             DD.Prototype.Navigation.CrowdService.Solver.LastPairTests);
            rapor.AppendLine("  sahne: canli zombi=" + CanliZombi() + " CharacterController=" + UnityEngine.Object.FindObjectsOfType<CharacterController>().Length +
                             " Collider=" + UnityEngine.Object.FindObjectsOfType<Collider>().Length +
                             " hitstop count/refused=" + HitStopService.Count + "/" + HitStopService.Refused +
                             " stagger throttled=" + StaggerGovernor.ThrottledCount +
                             " audio delivered/suppressed=" + AudioEvents.DeliveredCount + "/" + AudioEvents.SuppressedCount);
            rapor.AppendLine("  havuzlar: " + havuz);
            Debug.Log(rapor.ToString());

            Assert.Greater(kareMs.Count, 60, "Ölçüm penceresinde 60'tan az kare; ölçüm boş.");
            Assert.GreaterOrEqual(dogan, 20, "Gözlem için yeterli zombi doğmadı.");
            Assert.IsFalse(float.IsNaN(p50) || float.IsInfinity(p50));
        }

        // ======================================================================== yardımcı

        private static ZombieEnemy Dogur(Vector3 konum, Vector3 bakisHedefi)
        {
            Vector3 d = bakisHedefi - konum;
            d.y = 0f;
            Quaternion bakis = d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
            return EnemyFactory.Spawn(EnemyTier.Standard, 1, konum, bakis);
        }

        private static Vector2 EkranNoktasi(Vector3 dunya)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Camera cam = rig != null ? rig.Camera : Camera.main;
            Vector3 p = cam.WorldToScreenPoint(dunya);
            return new Vector2(p.x, p.y);
        }

        private static Vector3 Yatay(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static int CanliZombi()
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

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
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
    }
}
