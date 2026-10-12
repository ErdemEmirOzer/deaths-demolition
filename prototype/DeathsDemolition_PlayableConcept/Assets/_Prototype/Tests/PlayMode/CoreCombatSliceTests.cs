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
using DD.Prototype.Difficulty;
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

            // C-00 05.10 (WP-087): game frames, a fixed 1/60 step — the windows are game time (1.2 s = 72 frames, the control
            // window from 0.25 s = 57). On real frames the count followed the machine: b6mh (load average ~100–170) saw 18
            // frames in 1.2 s, 9 in the control window, and failed "Kontrol penceresi çok kısa" while the same test alone saw
            // 314 / 300 (b6mi). The old reason for real frames (iter5: a fixed step stalled the unscaled clock the hit-stop
            // ended on, the sim stuck at 6 %) is gone: REQ-127's hit-stop is a local VFX envelope and never owns timeScale.
            // WP-095 K1 (C-00 08.10): top-down a global stop (armour break, heavy death, …) now does slow the clock for ≤ 4 real
            // frames, through FeelTimeScale — but it ages on Time.unscaledDeltaTime, which a fixed capture step leaves real, so
            // it ends in real time and cannot stall the sim; the loop below is game time (Time.time), a stop only adds frames.
            float captureDt0 = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;

            Vector3 merkez = _rig.transform.position;
            ZombieEnemy hedef = Dogur(merkez + new Vector3(0f, 0f, -6f), merkez);
            Assert.IsNotNull(hedef);
            Collider kafa = hedef.HitboxFor(DD.Prototype.Damage.BodyRegion.Head);
            Collider govde = hedef.HitboxFor(DD.Prototype.Damage.BodyRegion.UpperBody);
            Assert.IsNotNull(kafa);
            Assert.IsNotNull(govde);

            var vuruslar = new List<EnemyDamageInfo>(32);
            var beklenenBolge = new List<DD.Prototype.Damage.BodyRegion>(32);
            var geo = new List<string>(32);
            int olum = 0;
            // C-00 01.10 (as GameFeel, option 1): the expected region from the real colliders at the hit — the running target's
            // flinch can dip its head onto the flat line, so a head hit is physical when the line enters the head collider first.
            Action<EnemyDamageInfo> dinle = i =>
            {
                vuruslar.Add(i);
                var ray = new Ray(i.Point - i.Direction.normalized * 3f, i.Direction.normalized);
                bool headOn = kafa.Raycast(ray, out RaycastHit hh, 10f);
                bool upOn = govde.Raycast(ray, out RaycastHit uh, 10f);
                beklenenBolge.Add(headOn && (!upOn || hh.distance <= uh.distance) ? DD.Prototype.Damage.BodyRegion.Head : DD.Prototype.Damage.BodyRegion.UpperBody);
                geo.Add("line y " + i.Point.y.ToString("F3") + " head y " + kafa.bounds.min.y.ToString("F3") + "–" + kafa.bounds.max.y.ToString("F3") +
                        (headOn ? " (enter " + hh.distance.ToString("F2") + ")" : " (off)") + " upper " + (upOn ? "enter " + uh.distance.ToString("F2") : "off") +
                        " hit " + i.Result.Region);
            };
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
                float t0 = Time.time;
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
                    gecen = Time.time - t0;
                    k++;

                    // Hızlanma tamamlandıktan (0.138 s) sonra ölç.
                    if (gecen > 0.25f)
                    {
                        kontrolKare++;
                        Vector3 v = _rig.Motor.PlanarVelocity;
                        Vector3 bakis = _rig.Aim.AimDirection;
                        enBuyukHizX = Mathf.Max(enBuyukHizX, v.z);
                        // Running +z while facing −z is running straight backward — × BackwardSpeedMultiplier (0.85, user 06.10; 0.65 before).
                        if (v.z > 0.9f * OyuncuHizi * DD.Prototype.Core.Config.PlayerConfigSection.BackwardSpeedMultiplier && bakis.z < -0.9f)
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

                // C-00 30.09 B: top-down shots are planar — no vertical scatter onto the head (the 0af8269a red, isabet=2 olum=1,
                // was a random ×2 head hit from the old scatter). C-00 01.10 (as GameFeel): a head hit is allowed only where the
                // line meets the head collider (asserted per hit below), so every hit up to the kill must be a reverse-direction
                // one — three body hits, or fewer with a physical head hit; a target still alive needs three.
                int gerekenIsabet = olum > 0 ? vuruslar.Count : 3;

                rapor.AppendLine("  kare=" + k + " kontrol kare=" + kontrolKare + " uyumlu=" + uyumluKare +
                                 " en buyuk vz=" + enBuyukHizX.ToString("F2") +
                                 " mermi " + mermiOnce + "->" + mermiSonra +
                                 " isabet=" + vuruslar.Count + " ters yonlu=" + tersYonluIsabet + " kafa=" + kafaIsabeti + " olum=" + olum +
                                 " hitstop=" + HitStopService.Count + " timeScale=" + Time.timeScale.ToString("F2"));
                rapor.AppendLine("  geo: " + string.Join(" | ", geo));
                Debug.Log(rapor.ToString());

                Assert.Greater(kontrolKare, 10, "Kontrol penceresi çok kısa; ölçüm boş.\n" + rapor);
                Assert.Greater(mermiOnce - mermiSonra, 5,
                    "REQ-039: 1.2 sn basılı ateş yalnız " + (mermiOnce - mermiSonra) + " mermi harcadı.\n" + rapor);
                Assert.GreaterOrEqual(uyumluKare, kontrolKare * 0.9f,
                    "REQ-037: karakter +z'ye koşarken −z'ye bakmayı sürdüremedi (uyumlu " + uyumluKare + "/" + kontrolKare + ").\n" + rapor);
                for (int i = 0; i < vuruslar.Count; i++)
                {
                    Assert.AreEqual(beklenenBolge[i], vuruslar[i].Result.Region, "Tepeden isabet " + i + " bölgesi geometriyle uyuşmuyor (C-00 01.10): " + geo[i] + ".\n" + rapor);
                }

                Assert.GreaterOrEqual(tersYonluIsabet, gerekenIsabet,
                    "REQ-037: hareket yönünün tersine ateş hedefe isabet etmedi (" + tersYonluIsabet + "/" + gerekenIsabet + " isabet).\n" + rapor);
            }
            finally
            {
                DamageEvents.EnemyDamaged -= dinle;
                DamageEvents.EnemyDied -= olumDinle;
                Time.captureDeltaTime = captureDt0;
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
        /// WP-102 (R4, C-00 11.10): the difficulty roles on a live Run scene. Normal: sixteen bodies, every one a plain chaser or a
        /// WP-054 flanker (no role roll). Demolition (the run's config copy active): the non-flanking bodies split into interceptors
        /// and cutters; with the player running north for 2 s, a cutter takes a point ahead on the player's escape path (its
        /// offset from the player points along the run) and closes on it — the "yolunu kesen" behaviour the user asked for.
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator Zorluk_NormalRolYok_DemolitionKesiciKacisYolununOnunuTutar()
        {
            yield return RunSahnesiniHazirla();
            Vector3 merkez = _rig.transform.position;
            var normal = new List<ZombieEnemy>();
            for (int i = 0; i < 16; i++) normal.Add(HalkaDogur(merkez, i, 16));
            yield return null;
            foreach (ZombieEnemy z in normal) Assert.AreEqual(ZombieAiRole.Chase, z.AiRole, "Normal'de rol atandı: " + z.name);
            EnemyFactory.ClearAll();
            yield return null;

            DifficultyService.Selected = DifficultyMode.Demolition;
            DifficultyService.BeginRun();
            try
            {
                Assert.IsNotNull(DifficultyService.Current, "Demolition ayarı etkin değil");
                var zler = new List<ZombieEnemy>();
                EnemyFactory.ResetSeedForTests(1u);   // the role rolls are deterministic (gt 12.10)
                for (int i = 0; i < 16; i++) zler.Add(HalkaDogur(merkez, i, 16));
                yield return null;
                int kesici = 0, onleyici = 0;
                foreach (ZombieEnemy z in zler)
                {
                    if (z.AiRole == ZombieAiRole.Cut) kesici++;
                    else if (z.AiRole == ZombieAiRole.Intercept) onleyici++;
                }

                Assert.Greater(kesici, 0, "Demolition'da kesici yok");
                Assert.Greater(onleyici, 0, "Demolition'da önleyici yok");

                // the cut itself: six bodies in an arc ahead of the player (±40°, 8–11 m), all cutters; the player runs up the screen.
                // A cutter's accepted point lies ahead on the run (its offset from the player along the player's own velocity) and
                // the cutter closes on it.
                EnemyFactory.ClearAll();
                yield return null;
                var onde = new List<ZombieEnemy>();
                for (int i = 0; i < 6; i++)
                {
                    float a = (-40f + i * 16f) * Mathf.Deg2Rad;
                    float r = 8f + (i % 2) * 3f;
                    ZombieEnemy z = Dogur(NavPathFollowerClamp(merkez + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r)), merkez);
                    Assert.IsNotNull(z, "öndeki zombi doğmadı (" + i + ")");
                    z.SetAiRoleForTests(ZombieAiRole.Cut);
                    onde.Add(z);
                }

                _girdi.Yon = new Vector2(0f, 1f);
                bool ileriKesme = false, yaklasti = false;
                var ilkMesafe = new Dictionary<ZombieEnemy, float>();
                // 2.5 s: five pursuit decisions (0.5 s timer) while the player closes on the arc - the first one or two fall on a
                // player still accelerating (or standing: the escape is then the ring's open side, behind), so 1.5 s was a coin toss
                // on frame timing (S-04 b23 window: aheadCut False at 6.1 m moved; main 21d0fc46 True at 7.2 m)
                for (int k = 0; k < 150; k++)
                {
                    yield return null;
                    Vector3 p = Yatay(_rig.transform.position);
                    Vector3 v = Yatay(_rig.Motor.PlanarVelocity);
                    if (v.sqrMagnitude < 1f) continue;
                    Vector3 yon = v.normalized;
                    foreach (ZombieEnemy z in onde)
                    {
                        if (z == null || !z.IsAlive || !z.CutActive) continue;
                        Vector3 o = Yatay(z.CutOffset);
                        if (o.sqrMagnitude > 1f && Vector3.Dot(o.normalized, yon) > 0.8f) ileriKesme = true;
                        float d = Vector3.Distance(Yatay(z.transform.position), p + o);
                        if (!ilkMesafe.TryGetValue(z, out float d0)) ilkMesafe[z] = d;
                        else if (d < d0 - 1f) yaklasti = true;
                    }
                }

                _girdi.Yon = Vector2.zero;
                Debug.Log("[DD-TEST] wp102.roles cut=" + kesici + " intercept=" + onleyici + " aheadCut=" + ileriKesme + " closing=" + yaklasti
                          + " cutters=" + ilkMesafe.Count + " playerMoved=" + Vector3.Distance(Yatay(_rig.transform.position), Yatay(merkez)).ToString("F1"));
                Assert.IsTrue(ileriKesme, "hiçbir kesici koşunun önüne nokta almadı");
                Assert.IsTrue(yaklasti, "kesici kesme noktasına yaklaşmadı");
            }
            finally
            {
                _girdi.Yon = Vector2.zero;
                DifficultyService.EndRun();
                DifficultyService.Selected = DifficultyMode.Normal;
            }
        }

        /// <summary>
        /// WP-102 evidence (Explicit, DD_WP102_CAPTURE_DIR): the same eighteen-body crowd (factory seed fixed) in Normal and in
        /// Demolition, the player running up the screen for 4 s. Writes &lt;dir&gt;/&lt;mode&gt;/frames/f-NNNNNN.jpg + frames.tsv (30 fps game time,
        /// tools/td-capture/tdmux) and metrics.tsv: per frame the bodies inside the run corridor ahead (±2 m, 0.5–8 m) and the
        /// player's progress. Demolition's accepted cut points are red discs on the ground. Measures; asserts only that it recorded.
        /// </summary>
        [UnityTest]
        [Explicit("WP-102 kanıt çekimi")]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator Zorluk_KesmeKaniti_NormalVeDemolition_TdKareleri([Values(DifficultyMode.Normal, DifficultyMode.Demolition)] DifficultyMode mod)
        {
            string kok = Environment.GetEnvironmentVariable("DD_WP102_CAPTURE_DIR");
            if (string.IsNullOrEmpty(kok)) kok = System.IO.Path.Combine(Application.temporaryCachePath, "wp102-capture");
            string dir = System.IO.Path.Combine(kok, DifficultyService.Id(mod));
            string kareler = System.IO.Path.Combine(dir, "frames");
            System.IO.Directory.CreateDirectory(kareler);
            yield return RunSahnesiniHazirla();
            _can.DiagnosisInvulnerable = true;   // a capture: the run must last the 4 s
            if (mod != DifficultyMode.Normal) { DifficultyService.Selected = mod; DifficultyService.BeginRun(); }
            var isaretler = new List<GameObject>();
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                EnemyFactory.ResetSeedForTests(1020u);
                Vector3 merkez = _rig.transform.position;
                var zler = new List<ZombieEnemy>();
                for (int i = 0; i < 18; i++)
                {
                    float a = (i * 20f + 10f) * Mathf.Deg2Rad;
                    float r = 10f + (i % 3) * 2f;
                    ZombieEnemy z = Dogur(NavPathFollowerClamp(merkez + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r)), merkez);
                    if (z != null) zler.Add(z);
                }

                Material kirmizi = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                kirmizi.SetColor("_BaseColor", new Color(0.95f, 0.1f, 0.08f, 1f));
                for (int i = 0; i < 18; i++)
                {
                    GameObject disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    UnityEngine.Object.Destroy(disk.GetComponent<Collider>());
                    disk.transform.localScale = new Vector3(0.7f, 0.02f, 0.7f);
                    disk.GetComponent<Renderer>().sharedMaterial = kirmizi;
                    disk.SetActive(false);
                    isaretler.Add(disk);
                }

                Camera cam = PrototypeCameraRig.Active.Camera;
                rt = new RenderTexture(1280, 720, 24);
                tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                var tsv = new StringBuilder();
                var metrik = new StringBuilder("frame\tt\tprogress\tcorridor\tcutters\n");
                int yazilan = 0, enCokKoridor = 0;
                _girdi.Yon = new Vector2(0f, 1f);
                for (int k = 0; k < 240; k++)
                {
                    yield return null;
                    if ((k & 1) != 0) continue;
                    Vector3 p = Yatay(_rig.transform.position);
                    Vector3 v = Yatay(_rig.Motor.PlanarVelocity);
                    Vector3 ileri = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
                    int koridor = 0, kesen = 0, disk = 0;
                    foreach (ZombieEnemy z in zler)
                    {
                        if (z == null || !z.IsAlive) continue;
                        Vector3 d = Yatay(z.transform.position) - p;
                        float onde = Vector3.Dot(d, ileri);
                        float yan = Vector3.Cross(ileri, d).y;
                        if (onde > 0.5f && onde < 8f && Mathf.Abs(yan) < 2f) koridor++;
                        if (z.CutActive)
                        {
                            kesen++;
                            if (disk < isaretler.Count)
                            {
                                Vector3 c = p + Yatay(z.CutOffset);
                                c.y = _rig.transform.position.y + 0.03f;
                                isaretler[disk].transform.position = c;
                                isaretler[disk].SetActive(true);
                                disk++;
                            }
                        }
                    }

                    for (int i = disk; i < isaretler.Count; i++) isaretler[i].SetActive(false);
                    enCokKoridor = Mathf.Max(enCokKoridor, koridor);
                    RenderTexture eski = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = eski;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = null;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(kareler, "f-" + yazilan.ToString("000000") + ".jpg"), tex.EncodeToJPG(90));
                    float t = yazilan / 30f;
                    tsv.Append(yazilan).Append('\t').Append(t.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    metrik.Append(yazilan).Append('\t').Append(t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                          .Append(Vector3.Distance(p, Yatay(merkez)).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                          .Append(koridor).Append('\t').Append(kesen).Append('\n');
                    yazilan++;
                }

                _girdi.Yon = Vector2.zero;
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "frames.tsv"), tsv.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "metrics.tsv"), metrik.ToString());
                Debug.Log("[DD-TEST] wp102.capture mode=" + DifficultyService.Id(mod) + " frames=" + yazilan + " maxCorridor=" + enCokKoridor
                          + " progress=" + Vector3.Distance(Yatay(_rig.transform.position), Yatay(merkez)).ToString("F1") + " -> " + dir);
                Assert.AreEqual(120, yazilan, "kareler kayıtta değil");
            }
            finally
            {
                _girdi.Yon = Vector2.zero;
                foreach (GameObject g in isaretler) if (g != null) UnityEngine.Object.Destroy(g);
                if (rt != null) UnityEngine.Object.Destroy(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                if (mod != DifficultyMode.Normal) DifficultyService.EndRun();
                DifficultyService.Selected = DifficultyMode.Normal;
                if (_can != null) _can.DiagnosisInvulnerable = false;
            }
        }

        /// <summary>
        /// WP-102 evidence (Explicit, DD_WP102_CAPTURE_DIR/heavy): Demolition's big tiers — three Elite (running) and three Ultra (fast
        /// heavy walk) spawn 14 m ahead and come at the standing player; TD frames + frames.tsv (tdmux) and per body its gait clip,
        /// ground speed, gait rate, honest flag and body scale (heavy.tsv) for the art review. Asserts it recorded and that ≥ 90 % of
        /// the walking samples play an honest cadence (gt MUST-1 12.10: no foot slide on the big bodies).
        /// </summary>
        [UnityTest]
        [Explicit("WP-102 kanıt çekimi")]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator Zorluk_AgirKademeler_DurustKadans_Demolition_TdKareleri()
        {
            string kok = Environment.GetEnvironmentVariable("DD_WP102_CAPTURE_DIR");
            if (string.IsNullOrEmpty(kok)) kok = System.IO.Path.Combine(Application.temporaryCachePath, "wp102-capture");
            string dir = System.IO.Path.Combine(kok, "heavy");
            string kareler = System.IO.Path.Combine(dir, "frames");
            System.IO.Directory.CreateDirectory(kareler);
            yield return RunSahnesiniHazirla();
            _can.DiagnosisInvulnerable = true;
            DifficultyService.Selected = DifficultyMode.Demolition;
            DifficultyService.BeginRun();
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                EnemyFactory.ResetSeedForTests(1021u);
                Vector3 merkez = _rig.transform.position;
                var zler = new List<ZombieEnemy>();
                for (int i = 0; i < 6; i++)
                {
                    float a = (-30f + i * 12f) * Mathf.Deg2Rad;
                    Vector3 p = NavPathFollowerClamp(merkez + new Vector3(Mathf.Sin(a) * 14f, 0f, Mathf.Cos(a) * 14f));
                    Vector3 d = merkez - p; d.y = 0f;
                    ZombieEnemy z = EnemyFactory.Spawn(i % 2 == 0 ? EnemyTier.Elite : EnemyTier.Ultra, 3, p, Quaternion.LookRotation(d.normalized, Vector3.up));
                    if (z != null) zler.Add(z);
                }

                Camera cam = PrototypeCameraRig.Active.Camera;
                rt = new RenderTexture(1280, 720, 24);
                tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                var tsv = new StringBuilder();
                var agir = new StringBuilder("frame\tbody\ttier\tgait\tclip\tspeed\trate\thonest\tbody_scale\n");
                int yuruyus = 0, durust = 0;
                var onceki = new Dictionary<ZombieEnemy, Vector3>();
                int yazilan = 0;
                for (int k = 0; k < 240; k++)
                {
                    yield return null;
                    if ((k & 1) != 0) continue;
                    foreach (ZombieEnemy z in zler)
                    {
                        if (z == null || !z.IsAlive || z.Skinned == null) continue;
                        Vector3 q = Yatay(z.transform.position);
                        float v = onceki.TryGetValue(z, out Vector3 q0) ? Vector3.Distance(q, q0) / (2f * Adim) : 0f;
                        onceki[z] = q;
                        int g = z.Skinned.Gait;
                        agir.Append(yazilan).Append('\t').Append(z.name).Append('\t').Append(z.Tier).Append('\t').Append(g).Append('\t')
                            .Append(g >= 0 && g < DD.Prototype.Characters.CharacterAnimatorContract.ZombieGaitClips.Length ? DD.Prototype.Characters.CharacterAnimatorContract.ZombieGaitClips[g] : "?").Append('\t')
                            .Append(v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                            .Append(z.Skinned.GaitRate.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                            .Append(z.Skinned.GaitRateHonest ? 1 : 0).Append('\t')
                            .Append(z.Skinned.GaitBodyScale.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                        if (z.Skinned.GaitOn && v > 1.5f)
                        {
                            yuruyus++;
                            if (z.Skinned.GaitRateHonest) durust++;
                        }
                    }

                    RenderTexture eski = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = eski;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = null;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(kareler, "f-" + yazilan.ToString("000000") + ".jpg"), tex.EncodeToJPG(90));
                    tsv.Append(yazilan).Append('\t').Append((yazilan / 30f).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    yazilan++;
                }

                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "frames.tsv"), tsv.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "heavy.tsv"), agir.ToString());
                Debug.Log("[DD-TEST] wp102.heavy frames=" + yazilan + " bodies=" + zler.Count + " honest=" + durust + "/" + yuruyus + " -> " + dir);
                Assert.AreEqual(120, yazilan, "kareler kayıtta değil");
                Assert.AreEqual(6, zler.Count, "ağır gövdeler doğmadı");
                Assert.Greater(yuruyus, 100, "yürüyen ağır gövde örneği yok");
                Assert.GreaterOrEqual(durust, 0.9f * yuruyus, "ağır kademelerde kadans dürüst değil (ayak kayar): " + durust + "/" + yuruyus);
            }
            finally
            {
                if (rt != null) UnityEngine.Object.Destroy(rt);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                DifficultyService.EndRun();
                DifficultyService.Selected = DifficultyMode.Normal;
                if (_can != null) _can.DiagnosisInvulnerable = false;
            }
        }

        /// <summary>
        /// WP-102 measurement (Explicit, Diagnostics): does Demolition close the escape across layouts, not on one lucky seed? The
        /// capture's eighteen-Standard ring (10–14 m) with the factory seed and the ring phase varied per seed, the player running up
        /// the screen for 4 s, in four cells: Normal; Demolition; Demolition without cutters (CutShare 0 — the gt 12.10 control
        /// cell); Demolition with the design's arrival rule (a cutter may arrive up to 0.3 s after the player, margin −0.3). The
        /// numbers sit on a code-default DifficultyProfile instance (the shipped asset is never written). Logs per cell the player's
        /// progress (mean, median, blocked share = under 15 m) and the run-corridor bodies; asserts only that every run ran.
        /// </summary>
        [UnityTest]
        [Explicit("WP-102 kesme ölçümü")]
        [Category("Diagnostics")]
        [Timeout(900000)]
        public IEnumerator Zorluk_KesmeEtkisi_TohumTaramasi()
        {
            const int Tohum = 8;
            yield return RunSahnesiniHazirla();
            _can.DiagnosisInvulnerable = true;   // a measurement: every run must last the 4 s
            if (EnemySpawnDirector.Instance != null) EnemySpawnDirector.Instance.StopRound();   // only the ring: the director adds no bodies
            Vector3 merkez = _rig.transform.position;
            Quaternion bakis = _rig.transform.rotation;
            string[] hucreler = { "normal", "demolition", "demolition-cut0", "demolition-margin-0.3" };
            var profiller = new List<DifficultyProfile>();
            var rapor = new StringBuilder("[DD-TEST] wp102.cutsweep seeds=" + Tohum + " run=4s ring=18 Standard 10-14 m\n");
            int kosu = 0;
            try
            {
                for (int h = 0; h < hucreler.Length; h++)
                {
                    DifficultyProfile profil = ScriptableObject.CreateInstance<DifficultyProfile>();
                    profiller.Add(profil);
                    DifficultyModeSettings d = profil.For(DifficultyMode.Demolition);
                    if (h == 2) d.CutShare = 0f;
                    if (h == 3) d.CutArrivalMarginSeconds = -0.3f;
                    DifficultyProfile.SetActiveForTests(profil);
                    DifficultyService.Selected = h == 0 ? DifficultyMode.Normal : DifficultyMode.Demolition;
                    DifficultyService.BeginRun();
                    var ilerleme = new List<float>();
                    float koridorOrt = 0f; int kesenOrnek = 0, ornek = 0;
                    for (int s = 0; s < Tohum; s++)
                    {
                        _girdi.Yon = Vector2.zero;
                        if (EnemySpawnDirector.Instance != null) { EnemySpawnDirector.Instance.StopRound(); EnemySpawnDirector.Instance.ClearAllEnemies(); }
                        EnemyFactory.ClearAll();
                        _rig.Teleport(merkez, bakis);
                        yield return null;
                        yield return null;
                        EnemyFactory.ResetSeedForTests(1020u + (uint)s * 7u);
                        var zler = new List<ZombieEnemy>();
                        for (int i = 0; i < 18; i++)
                        {
                            float a = (i * 20f + 10f + s * 7.5f) * Mathf.Deg2Rad;
                            float r = 10f + (i % 3) * 2f;
                            ZombieEnemy z = Dogur(NavPathFollowerClamp(merkez + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r)), merkez);
                            if (z != null) zler.Add(z);
                        }

                        yield return null;
                        _girdi.Yon = new Vector2(0f, 1f);
                        for (int k = 0; k < 240; k++)
                        {
                            yield return null;
                            if ((k % 10) != 9) continue;
                            Vector3 p = Yatay(_rig.transform.position);
                            Vector3 v = Yatay(_rig.Motor.PlanarVelocity);
                            Vector3 ileri = v.sqrMagnitude > 1f ? v.normalized : Vector3.forward;
                            int koridor = 0;
                            foreach (ZombieEnemy z in zler)
                            {
                                if (z == null || !z.IsAlive) continue;
                                Vector3 dz = Yatay(z.transform.position) - p;
                                float onde = Vector3.Dot(dz, ileri);
                                if (onde > 0.5f && onde < 8f && Mathf.Abs(Vector3.Cross(ileri, dz).y) < 2f) koridor++;
                                if (z.CutActive) kesenOrnek++;
                            }

                            koridorOrt += koridor;
                            ornek++;
                        }

                        _girdi.Yon = Vector2.zero;
                        ilerleme.Add(Vector3.Distance(Yatay(_rig.transform.position), Yatay(merkez)));
                        kosu++;
                    }

                    DifficultyService.EndRun();
                    ilerleme.Sort();
                    float toplam = 0f; int kapali = 0;
                    var satir = new StringBuilder();
                    foreach (float x in ilerleme)
                    {
                        toplam += x;
                        if (x < 15f) kapali++;
                        satir.Append(x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                    }

                    float medyan = 0.5f * (ilerleme[(Tohum - 1) / 2] + ilerleme[Tohum / 2]);
                    rapor.Append("  ").Append(hucreler[h]).Append(" progress mean=").Append((toplam / Tohum).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                         .Append(" median=").Append(medyan.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                         .Append(" blocked(<15m)=").Append(kapali).Append('/').Append(Tohum)
                         .Append(" corridor=").Append((ornek > 0 ? koridorOrt / ornek : 0f).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                         .Append(" cutSamples=").Append(kesenOrnek)
                         .Append(" | ").Append(satir).Append('\n');
                }

                Debug.Log(rapor.ToString());
                Assert.AreEqual(hucreler.Length * Tohum, kosu, "koşuların hepsi bitmedi");
            }
            finally
            {
                _girdi.Yon = Vector2.zero;
                DifficultyService.EndRun();
                DifficultyService.Selected = DifficultyMode.Normal;
                DifficultyProfile.SetActiveForTests(null);
                foreach (DifficultyProfile pr in profiller) if (pr != null) UnityEngine.Object.Destroy(pr);
                if (_can != null) _can.DiagnosisInvulnerable = false;
            }
        }

        private static ZombieEnemy HalkaDogur(Vector3 merkez, int i, int n)
        {
            float a = i * Mathf.PI * 2f / n;
            float r = 9f + (i % 3) * 1.5f;
            Vector3 p = merkez + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
            ZombieEnemy z = Dogur(NavPathFollowerClamp(p), merkez);
            Assert.IsNotNull(z, "zombi doğmadı (" + i + ")");
            return z;
        }

        private static Vector3 NavPathFollowerClamp(Vector3 p) => DD.Prototype.Navigation.NavPathFollower.ClampToNavMesh(p, 3f);

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
                // C-00 01.10 (option 1): the expected region from the real colliders NOW — the flat line enters the head collider
                // first → Head, else UpperBody (the capsule fallback counts as UpperBody too). The target's own flinch can dip the
                // head onto the line (main-ci 55c6e37f / b5g: head centre 1.80 → 1.37 over three hits), so "head 0" was not physical.
                // Collider.Raycast, not bounds (main-ci 5cf17f9e): the head hitbox is a sphere and its box is wider off the centre —
                // a line 0.093 m under the centre and 0.13 m to the side entered the box but passed the sphere (0.161 m > r 0.154),
                // went through the capsule alone and was, rightly, UpperBody.
                Bounds hb = kafa.bounds, ub = govde.bounds;
                var ray = new Ray(i.Point - i.Direction.normalized * 3f, i.Direction.normalized);
                bool headOn = kafa.Raycast(ray, out RaycastHit hh, 10f);
                bool upOn = govde.Raycast(ray, out RaycastHit uh, 10f);
                float hd = headOn ? hh.distance : 0f, ud = upOn ? uh.distance : 0f;
                float headGap = Vector3.Cross(hb.center - ray.origin, ray.direction).magnitude - hb.extents.y;   // line to the sphere's surface
                beklenenBolge.Add(headOn && (!upOn || hd <= ud) ? DD.Prototype.Damage.BodyRegion.Head : DD.Prototype.Damage.BodyRegion.UpperBody);
                geo.Add("line y " + i.Point.y.ToString("F3") + " head y " + hb.min.y.ToString("F3") + "–" + hb.max.y.ToString("F3") +
                        (headOn ? " (enter " + hd.ToString("F2") + ")" : " (missed by " + (headGap * 1000f).ToString("F0") + " mm" + (hb.IntersectRay(ray) ? ", box entered)" : ")")) +
                        " upper y " + ub.min.y.ToString("F3") + "–" + ub.max.y.ToString("F3") +
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
            int globalStopOnce = FeelTimeScale.Stops;   // WP-095 K1: the top-down global stop is counted by the clock arbiter
            int lastZombieOnce = FeelEvents.Count(FeelEventKind.LastZombie);
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
                // enters the head collider first, else UpperBody (12 HP; a bare head ×2 = 24), dressed FleshHit until the killing one
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
                // WP-095 K1 (C-00 08.10): REQ-127 changed — top-down may stop the world now, but never for an AR flesh hit or an
                // ordinary kill (his sözleşmesi §4.3; a critical kill only with a single-shot class weapon, ar_1 is not one). The
                // round's last zombie would (§4.11), so a raised LastZombie (not expected in this harness: the round is stopped)
                // skips the check.
                if (FeelEvents.Count(FeelEventKind.LastZombie) == lastZombieOnce)
                {
                    Assert.AreEqual(globalStopOnce, FeelTimeScale.Stops, "K1: AR ile flesh hit/sıradan ölüm küresel durma istedi.");
                }
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
                TopDownFeelCamera tdKanal = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.TopDownFeel : null;
                int tdDunyaOnce = tdKanal != null ? tdKanal.WorldEvents : 0;
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
                    if (CameraModeService.Current == CameraMode.TopDown)
                    {
                        // WP-095 (R3, his sözleşmesi §2): tepeden hasar kamerayı K2 / K3 ile oynatır (TopDownFeelCamera ←
                        // FeelEvents.PlayerDamaged); yönlendiricinin Bump'ı artık köprüye gitmez (ikinci sarsıntı olurdu).
                        // DEC-031 korunur: hasar kamerayı oynatır — ölçüm yeri değişti.
                        Assert.IsNotNull(tdKanal, "Tepeden kamera kanalları yok.");
                        Assert.AreEqual(0, shakeSayisi, "Tepeden hasar Bump'ı köprüye hâlâ gidiyor (çift sarsıntı).");
                        Assert.Greater(tdKanal.WorldEvents, tdDunyaOnce, "Hasar tepeden dünya travması (K2) üretmedi (DEC-031).");
                    }
                    else
                    {
                        Assert.Greater(shakeSayisi, 0, "Hasar kamera Bump sarsıntısı üretmedi (DEC-031).");
                        Assert.Greater(bumpAlindi, 0, "Bump kameraya ulaştı ama CameraShakeService almadı (alıcı yok?).");
                    }
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

        // ================================================================ WP-095 · FeelEvents tek kaynak

        /// <summary>
        /// WP-095 (R3, C-00 07.10): FeelEvents gerçek oyun döngüsünde tek kaynak. Basılı tetikle çıplak bir zombi öldürülür:
        /// her tetik çekişi tek ShotFired (mermi 0'ın Spawned sayısına eşit, sıfırdan farklı ve tekil grup), her hasar
        /// uygulaması tek EnemyHit (DamageEvents.EnemyDamaged sayısına eşit) ve kendi atışının grubunu taşır, ölüm tek
        /// EnemyKilled (grup + klasik silah kimliği). Sonra elle reload: tek ReloadStarted, tek ReloadEnded (tamamlandı).
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator FeelEvents_GercekAtisVurusOlumReload_TekKaynakVeAtisGrubu()
        {
            yield return RunSahnesiniHazirla();
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current);

            Vector3 merkez = _rig.transform.position;
            ZombieEnemy hedef = Dogur(merkez + new Vector3(0f, 0f, 7f), merkez);
            Assert.IsNotNull(hedef);
            hedef.FitArmorComposition(false, false, false);
            Collider govde = hedef.HitboxFor(DD.Prototype.Damage.BodyRegion.UpperBody);
            Assert.IsNotNull(govde);

            var atis = new List<FeelShot>(32);
            var vurus = new List<FeelHit>(32);
            var olum = new List<FeelKill>(2);
            var reloadBas = new List<FeelReload>(2);
            var reloadSon = new List<FeelReload>(2);
            int mermi0 = 0, hasarOlayi = 0, olumOlayi = 0;
            Action<FeelShot> aDinle = e => atis.Add(e);
            Action<FeelHit> vDinle = e => vurus.Add(e);
            Action<FeelKill> oDinle = e => olum.Add(e);
            Action<FeelReload> rbDinle = e => reloadBas.Add(e);
            Action<FeelReload> rsDinle = e => reloadSon.Add(e);
            Action<ProjectileSpawnInfo> sDinle = i => { if (i.PelletIndex == 0) mermi0++; };
            Action<EnemyDamageInfo> hDinle = _ => hasarOlayi++;
            Action<EnemyDeathInfo> dDinle = _ => olumOlayi++;
            FeelEvents.ShotFired += aDinle;
            FeelEvents.EnemyHit += vDinle;
            FeelEvents.EnemyKilled += oDinle;
            FeelEvents.ReloadStarted += rbDinle;
            FeelEvents.ReloadEnded += rsDinle;
            ProjectileEvents.Spawned += sDinle;
            DamageEvents.EnemyDamaged += hDinle;
            DamageEvents.EnemyDied += dDinle;
            try
            {
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
                for (int i = 0; i < 30; i++) yield return null;   // uçuşta kalan mermiler insin (0,5 s)

                ClassicWeaponRuntime silah = WeaponRegistry.ActiveController.Classic;
                Assert.Less(silah.Ammo, silah.MagazineSize, "Reload ölçümü için şarjör dolu olmamalı.");
                _girdi.DoldurBasti = true;
                yield return null;
                _girdi.DoldurBasti = false;
                float r0 = Time.realtimeSinceStartup;
                while ((silah.IsReloading || reloadSon.Count == 0) && Time.realtimeSinceStartup - r0 < 10f) yield return null;

                var gruplar = new HashSet<int>();
                foreach (FeelShot s in atis) gruplar.Add(s.GroupId);
                Debug.Log("[DD-TEST] wp095.feelevents atis=" + atis.Count + " mermi0=" + mermi0 + " grup=" + gruplar.Count +
                          " vurus=" + vurus.Count + " hasarOlayi=" + hasarOlayi + " olum=" + olum.Count + " olumOlayi=" + olumOlayi +
                          " olumGrup=" + (olum.Count > 0 ? olum[0].GroupId : -1) + " reload=" + reloadBas.Count + "/" + reloadSon.Count);

                Assert.GreaterOrEqual(atis.Count, 3, "Ölçüm boş: üç vuruşluk zombiye en az üç atış.");
                Assert.AreEqual(mermi0, atis.Count, "Tetik çekişi başına tek ShotFired (mermi 0'ın Spawned sayısı).");
                Assert.IsFalse(gruplar.Contains(0), "Her atışın sunum grubu sıfırdan farklı olmalı.");
                Assert.AreEqual(atis.Count, gruplar.Count, "Her tetik çekişinin kendi grubu.");
                foreach (FeelShot s in atis)
                {
                    Assert.IsFalse(s.Special);
                    Assert.AreEqual(ClassicWeaponId.AssaultRifle, s.Classic);
                    Assert.AreEqual(1, s.Pellets);
                    // WP-104 B4-2 (S-07 / S-06 B12): the shot carries where the flash left the weapon and its forward
                    Assert.Greater(s.MuzzleForward.sqrMagnitude, 0.5f, "atış olayı namlu yönünü taşımalı");
                    Assert.Less(Vector3.Distance(s.MuzzlePosition, _rig.transform.position), 2.5f, "atış olayının namlu noktası oyuncunun elinde olmalı");
                }

                Assert.Greater(vurus.Count, 0);
                Assert.AreEqual(hasarOlayi, vurus.Count, "Hasar uygulaması başına tek EnemyHit.");
                foreach (FeelHit h in vurus)
                {
                    Assert.IsTrue(gruplar.Contains(h.GroupId), "Vuruş kendi atışının grubunu taşımalı (grup " + h.GroupId + ").");
                    Assert.IsTrue(h.FromClassic);
                    Assert.AreEqual(ClassicWeaponId.AssaultRifle, h.Classic);
                }

                Assert.AreEqual(1, olumOlayi);
                Assert.AreEqual(1, olum.Count, "Ölüm başına tek EnemyKilled.");
                Assert.IsTrue(gruplar.Contains(olum[0].GroupId), "Öldürme, öldüren atışın grubunu taşımalı.");
                Assert.AreEqual(vurus[vurus.Count - 1].GroupId, olum[0].GroupId);
                Assert.IsTrue(olum[0].FromClassic);
                Assert.AreEqual(0, FeelEvents.CurrentHitGroup, "Grup kapsamı hasar uygulamasından sonra kapanmalı.");

                Assert.AreEqual(1, reloadBas.Count, "Tek ReloadStarted.");
                Assert.AreEqual(1, reloadSon.Count, "Tek ReloadEnded.");
                Assert.IsFalse(reloadBas[0].Empty, "Taktik reload (şarjör boş değildi).");
                Assert.IsTrue(reloadSon[0].Completed, "Reload tamamlandı.");
                Assert.Greater(reloadBas[0].DurationSeconds, 0f);
            }
            finally
            {
                _girdi.AtesBasili = false;
                FeelEvents.ShotFired -= aDinle;
                FeelEvents.EnemyHit -= vDinle;
                FeelEvents.EnemyKilled -= oDinle;
                FeelEvents.ReloadStarted -= rbDinle;
                FeelEvents.ReloadEnded -= rsDinle;
                ProjectileEvents.Spawned -= sDinle;
                DamageEvents.EnemyDamaged -= hDinle;
                DamageEvents.EnemyDied -= dDinle;
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

        // ================================================================ WP-095 · tepeden kamera K1–K5

        /// <summary>
        /// WP-095 (R3, his sözleşmesi §2 · K/wp-095/F2-KAMERA-GOREV.md): tepeden ar_1 ile 2 s basılı ateş.
        /// (a) sarsıntı düğümü oynar ve K1 + K2 + K3 toplamı ≤ aggregateCapMeters (0,22 m); K5 bu testte yok — raund başı
        /// nefesi bitmeden ateş başlamaz; (b) eski tepeden ateş sarsıntısı (CameraShakeService) çalışmaz: çift sarsıntı yok;
        /// (c) imlecin dünya noktası kararlı pozdan kurulur: rig'in nişanı, yalnız kararlı pozdan hesaplanan noktadan &lt; 1 cm
        /// sapar, telafisiz ham kamera ışını oynar (pozitif kontrol); (d) tetik bırakılınca 1 s (60 oyun karesi) içinde düğüm ≈ 0
        /// (K1 1,6/s ile söner, K3 yayı döner).
        /// </summary>
        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator Wp095_TepedenAr1Ates_SarsintiKenetli_NisanKararli_BirakincaSifir()
        {
            yield return RunSahnesiniHazirla();
            Assert.AreEqual(CameraMode.TopDown, CameraModeService.Current, "Test tepeden kamerada koşar.");
            ClassicWeaponId onceki = WeaponRegistry.SelectedWeapon;
            try
            {
                Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.AssaultRifle), "ar_1 kuşanılmadı");
                PrototypeCameraRig kam = PrototypeCameraRig.Active;
                TopDownFeelCamera kanal = kam.TopDownFeel;
                Transform dugum = kam.Camera.transform;
                WeaponController silah = WeaponRegistry.ActiveController;
                yield return Bekle(() => silah.Classic.CanFire, 5f, "Silah ateşe hazır değil.");
                // Raund başı K5 nefesi (+%4 → 0, 0,8 s ölçeksiz) ve kurulumdan kalan her şey sönsün: bu test yalnız K1–K3'ü ölçer.
                yield return Bekle(() => Mathf.Abs(kanal.ZoomFraction) < 1e-6f && dugum.localPosition.magnitude < 1e-4f, 8f,
                    "Ateşten önce kamera dinlenmeye dönmedi (K5 / düğüm).");

                float duzlemY = _rig.transform.position.y + NisanDuzlemi;
                Vector2 ekran = new Vector2(Screen.width * 0.5f, Screen.height * 0.8f);   // oyuncunun önü
                _girdi.Imlec = ekran;
                int atisOnce = kanal.Shots;
                float enBuyukOfset = 0f, enBuyukToplam = 0f, enBuyukNisanSapma = 0f, enBuyukHamSapma = 0f;
                _girdi.AtesBasili = true;
                for (int i = 0; i < 120; i++)
                {
                    yield return null;
                    enBuyukOfset = Mathf.Max(enBuyukOfset, dugum.localPosition.magnitude);
                    enBuyukToplam = Mathf.Max(enBuyukToplam, kanal.Last.TotalMeters);
                    Assert.AreEqual(0, CameraShakeService.ActiveCount, "Eski tepeden ateş sarsıntısı hâlâ çalışıyor (çift sarsıntı).");

                    Vector3 nisan;
                    Assert.IsTrue(kam.ScreenPointToAimPlane(ekran, duzlemY, out nisan), "Nişan projeksiyonu başarısız.");
                    Vector3 kararli = KararliPozdanDuzlemNoktasi(kam, ekran, duzlemY);
                    enBuyukNisanSapma = Mathf.Max(enBuyukNisanSapma, Vector3.Distance(nisan, kararli));
                    Ray ham = kam.Camera.ScreenPointToRay(ekran);
                    if (Mathf.Abs(ham.direction.y) > 1e-5f)
                    {
                        Vector3 hamNokta = ham.origin + ham.direction * ((duzlemY - ham.origin.y) / ham.direction.y);
                        enBuyukHamSapma = Mathf.Max(enBuyukHamSapma, Vector3.Distance(hamNokta, kararli));
                    }
                }

                _girdi.AtesBasili = false;
                int atis = kanal.Shots - atisOnce;
                for (int i = 0; i < 60; i++)
                {
                    yield return null;
                }

                float kalan = dugum.localPosition.magnitude;
                float kalanRoll = Quaternion.Angle(dugum.localRotation, Quaternion.identity);
                float tavan = FeelProfileTD.Active.aggregateCapMeters;
                Debug.Log("[DD-TEST] wp095.kamera ar_1 2s atis=" + atis + " dugum max=" + enBuyukOfset.ToString("F4") + " m toplam max=" +
                          enBuyukToplam.ToString("F4") + " m (tavan " + tavan + ") nisan sapma=" + (enBuyukNisanSapma * 1000f).ToString("F2") +
                          " mm ham=" + (enBuyukHamSapma * 1000f).ToString("F1") + " mm birakinca 1s=" + kalan.ToString("F5") + " m");

                Assert.GreaterOrEqual(atis, 10, "Ölçüm boş: 2 s'de tepeden kanala yeterli atış ulaşmadı (FeelEvents.ShotFired → K1).");
                Assert.Greater(enBuyukOfset, 0.005f, "Tepeden ateş sarsıntı düğümünü oynatmadı (K1 / K3).");
                Assert.LessOrEqual(enBuyukOfset, tavan + 1e-4f, "K1 + K2 + K3 düğüm ofseti 0,22 m tavanını aştı: " + enBuyukOfset);
                Assert.LessOrEqual(enBuyukToplam, tavan + 1e-4f, "Kenet sonrası toplam tavanı aştı: " + enBuyukToplam);
                Assert.Greater(enBuyukHamSapma, 0.002f, "Ham kamera ışını bile oynamadı; telafi ölçülemiyor (ölçüm boş).");
                Assert.Less(enBuyukNisanSapma, 0.01f,
                    "REQ-126: sarsıntı imlecin dünya noktasını " + (enBuyukNisanSapma * 1000f).ToString("F2") + " mm oynattı.");
                Assert.Less(kalan, 1e-3f, "Tetik bırakılınca 1 s içinde düğüm sıfıra dönmedi: " + dugum.localPosition);
                Assert.Less(kalanRoll, 0.01f, "Bırakınca roll kaldı: " + kalanRoll);
            }
            finally
            {
                _girdi.AtesBasili = false;
                WeaponRegistry.SelectedWeapon = onceki;   // T1: the process-wide selection goes back
            }
        }

        // ======================================================================== yardımcı

        /// <summary>WP-095: the aim-plane point under <paramref name="ekran"/> from the rig's STABLE pose only (pinhole; the shake node is ignored).</summary>
        private static Vector3 KararliPozdanDuzlemNoktasi(PrototypeCameraRig kam, Vector2 ekran, float duzlemY)
        {
            Camera c = kam.Camera;
            Rect r = c.pixelRect;
            float tan = Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float x = ((ekran.x - r.x) / r.width * 2f - 1f) * tan * c.aspect;
            float y = ((ekran.y - r.y) / r.height * 2f - 1f) * tan;
            Vector3 yon = kam.StableRotation * new Vector3(x, y, 1f).normalized;
            Vector3 koken = kam.StablePosition;
            return koken + yon * ((duzlemY - koken.y) / yon.y);
        }

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
