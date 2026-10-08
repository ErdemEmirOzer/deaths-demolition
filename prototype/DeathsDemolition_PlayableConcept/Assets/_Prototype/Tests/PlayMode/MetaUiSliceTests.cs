using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-005 · Meta UI dilimi — gerçek sahne zinciri üzerinde sunum ve navigation kanıtı.
    ///
    /// Buton yolu (OnSubmit) ile Main Menu → Selection → Run → Results → Main Menu;
    /// üç skin; HUD presenter metinleri ve gold olayı; Results sunumu ↔ RunSummary (run
    /// reset sonrası); ana menü pane'leri ve empty state; pause × upgrade kapısı ve odak
    /// iadesi; ölüm sırasında pause reddi; abort → Results/profil yok; tüm metin İngilizce;
    /// ikinci koşuda tekil presenter; HUD/Upgrading idle allocation gözlemi.
    ///
    /// Profil TestProfileIsolation ile izole dosyadadır (gerçek profil yazılmaz).
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class MetaUiSliceTests
    {
        private const float KurulumZamanAsimi = 25f;

        private readonly List<string> _hatalar = new List<string>();
        private float _eskiTimeScale;
        private PrototypeConfig _eskiConfig;

        [SetUp]
        public void SetUp()
        {
            RoundUpgradeShop.NodeCardsTestDefault = false;   // WP-077 K10: these slices test the classic card row
            _hatalar.Clear();
            _eskiTimeScale = Time.timeScale;
            _eskiConfig = PrototypeConfig.Active;
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            Application.logMessageReceived += HataTopla;
        }

        [TearDown]
        public void TearDown()
        {
            RoundUpgradeShop.NodeCardsTestDefault = null;
            Application.logMessageReceived -= HataTopla;
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
            LogAssert.ignoreFailingMessages = false;
            PrototypeConfig.SetActive(_eskiConfig);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;

            if (EnemySpawnDirector.Instance != null)
            {
                SpawnAyarla(true);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            Assert.AreEqual(0, _hatalar.Count, "Test boyunca UI/koşu hatası loglandı:\n" + string.Join("\n", _hatalar));
        }

        private void HataTopla(string mesaj, string iz, LogType tur)
        {
            if (tur != LogType.Error && tur != LogType.Exception && tur != LogType.Assert)
            {
                return;
            }

            if (mesaj.Contains("[UI]") || mesaj.Contains("[Run]") || mesaj.Contains("[Profile]") ||
                mesaj.Contains("NullReferenceException") || mesaj.Contains("MissingReferenceException") ||
                mesaj.Contains("InvalidOperationException") || mesaj.Contains("ArgumentException"))
            {
                _hatalar.Add(tur + ": " + mesaj);
            }
        }

        // ================================================================== yardımcılar

        private static void SpawnAyarla(bool acik)
        {
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "EnemySpawnDirector._spawningEnabled alanı bulunamadı.");
            f.SetValue(EnemySpawnDirector.Instance, acik);
        }

        private static UiButton Buton<TScreen>(string ad) where TScreen : UiScreen
        {
            TScreen ekran = UnityEngine.Object.FindObjectOfType<TScreen>();
            Assert.IsNotNull(ekran, typeof(TScreen).Name + " sahnede yok.");
            UiButton[] butonlar = ekran.GetComponentsInChildren<UiButton>(true);
            for (int i = 0; i < butonlar.Length; i++)
            {
                if (butonlar[i].gameObject.name == ad)
                {
                    return butonlar[i];
                }
            }

            Assert.Fail(typeof(TScreen).Name + " içinde '" + ad + "' butonu yok. Mevcut: " + ButonAdlari(butonlar));
            return null;
        }

        private static string ButonAdlari(UiButton[] butonlar)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < butonlar.Length; i++)
            {
                sb.Append(butonlar[i].gameObject.name).Append(' ');
            }

            return sb.ToString();
        }

        private static bool MetinVar<TScreen>(string parca, out string bulunan) where TScreen : UiScreen
        {
            bulunan = null;
            TScreen ekran = UnityEngine.Object.FindObjectOfType<TScreen>();
            if (ekran == null)
            {
                return false;
            }

            TMP_Text[] metinler = ekran.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < metinler.Length; i++)
            {
                if (metinler[i].text != null && metinler[i].text.Contains(parca))
                {
                    bulunan = metinler[i].text;
                    return true;
                }
            }

            return false;
        }

        private static TMP_Text HudMetni(RunHudScreen hud, string blok, string ad)
        {
            TMP_Text[] metinler = hud.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < metinler.Length; i++)
            {
                Transform t = metinler[i].transform;
                if (t.name != ad)
                {
                    continue;
                }

                for (Transform p = t.parent; p != null; p = p.parent)
                {
                    if (p.name == blok)
                    {
                        return metinler[i];
                    }
                }
            }

            Assert.Fail("HUD metni bulunamadı: " + blok + "/" + ad);
            return null;
        }

        private static IEnumerator KosuyaGir()
        {
            yield return BootTanMainMenuye();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
        }

        /// <summary>Kontrollü saha: director spawn'ı kapalı, sahne temiz, 60 Hz sabit kare.</summary>
        private static IEnumerator KontrolluSaha()
        {
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector yok.");
            yield return Bekle(() => GoldService.Instance != null, KurulumZamanAsimi, "GoldService yok.");
            SpawnAyarla(false);
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            GoldService.Instance.ClearOrbs(false);
            yield return null;
            Assert.AreEqual(0, ZombieEnemy.Live.Count, "Kontrollü kurulum: sahnede zombi kalmamalı.");
            Time.captureDeltaTime = 1f / 60f;
        }

        private static ZombieEnemy Dogur(Vector3 konum)
        {
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, RunDirector.Instance.Round, konum, Quaternion.identity);
            Assert.IsNotNull(z, "Test zombisi doğmadı.");
            return z;
        }

        private static IEnumerator OldurVeTopla(int adet)
        {
            Vector3 merkez = PlayerRegistry.Primary.transform.position;
            for (int i = 0; i < adet; i++)
            {
                Dogur(merkez + new Vector3(14f + i * 1.2f, 0f, 3f)).Kill();
                yield return null;
            }

            yield return null;
        }

        private static bool DebugButonunaBas(string etiketParcasi)
        {
            IReadOnlyList<DebugPanelEntry> girdiler = DebugPanelRegistry.Entries;
            for (int i = 0; i < girdiler.Count; i++)
            {
                if (girdiler[i] is DebugPanelButton buton && buton.Label != null &&
                    buton.Label.Contains(etiketParcasi) && buton.OnPressed != null)
                {
                    buton.OnPressed.Invoke();
                    return true;
                }
            }

            return false;
        }

        private static IEnumerator RoundTamamla()
        {
            RunDirector run = RunDirector.Instance;
            Assert.IsTrue(DebugButonunaBas("Complete this round"), "F1 'Complete this round' butonu yok.");
            yield return Bekle(() => run.Phase == RunPhase.Upgrading, FazZamanAsimi, "Upgrade ekranı açılmadı.");
            yield return null;
            yield return null;
        }

        private static IEnumerator Oldur()
        {
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = false;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp005-test");
            yield return Bekle(() => RunDirector.Instance == null || RunDirector.Instance.Phase == RunPhase.Dead,
                FazZamanAsimi, "Dead fazı gelmedi.");
        }

        /// <summary>
        /// Pozitif kontrol: kullanılan yığın sayacı (GC.GetTotalMemory) gerçekten sayıyor mu?
        /// Unity Mono'da GC.GetAllocatedBytesForCurrentThread HER ZAMAN 0 döner ve
        /// ProfilerRecorder "GC Allocated In Frame" batchmode Editor'de 64 KB'lik bilinçli
        /// ayırmayı 0 okudu (iter4–iter6, probe-alloc kanıtı); bu yüzden yığın deltası kullanılır.
        /// </summary>
        /// <summary>
        /// WP-022K diagnostic (runs right before HudMetinleri_SnapshotVeGoldOlayi in fixture
        /// order, so it sees the same leaked state): the same prelude (run, controlled arena,
        /// gold event, low health, invulnerable), then ProfilerRecorder frame counts / bytes
        /// with each HUD-side component toggled alone, plus a heap delta over 200 frames.
        /// Logs only - the assertion lives in the real test; this one names the source.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudMetinleri_AyirmaTanisi_BilesenBazinda()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            yield return null;
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud);
            PlayerRig rig = PlayerRegistry.Primary;
            GoldService.Instance.Wallet.Credit(7, rig.transform.position);
            rig.Health.ApplyDamage(rig.Health.Max * 0.75f, rig.transform.position + Vector3.forward, "wp022k-diag");
            yield return null;
            rig.Health.DiagnosisInvulnerable = true;

            int before = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            try
            {
                using (var count = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count"))
                using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    var sb = new System.Text.StringBuilder("[DD-TEST] wp022k.hudDiag");
                    for (int i = 0; i < 20; i++) yield return null;
                    long c = 0, b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" sum60 all=").Append(c).Append("/").Append(b).Append("B");

                    hud.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" hudOff=").Append(c).Append("/").Append(b).Append("B");
                    hud.enabled = true;

                    UiPulse[] pulses = hud.GetComponentsInChildren<UiPulse>(true);
                    foreach (UiPulse p in pulses) p.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" pulsesOff(").Append(pulses.Length).Append(")=").Append(c).Append("/").Append(b).Append("B");
                    foreach (UiPulse p in pulses) p.enabled = true;

                    var hit = UnityEngine.Object.FindObjectOfType<UiHitDirectionLayer>();
                    if (hit != null) hit.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" hitOff=").Append(c).Append("/").Append(b).Append("B");
                    if (hit != null) hit.enabled = true;

                    if (hud.EnemyStatus != null) hud.EnemyStatus.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" statusOff=").Append(c).Append("/").Append(b).Append("B");
                    if (hud.EnemyStatus != null) hud.EnemyStatus.enabled = true;

                    var driver = UnityEngine.Object.FindObjectOfType<UiPromptDriver>();
                    if (driver != null) driver.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" promptDriverOff=").Append(c).Append("/").Append(b).Append("B");
                    if (driver != null) driver.enabled = true;

                    // 600-frame timeline: total, frames with > 1 KB, and what the health
                    // state text / regen looked like on the heaviest frames.
                    long total = 0; int heavy = 0; long top = 0; string topNote = string.Empty; int changes = 0; string lastState = null;
                    TMPro.TMP_Text stateText = HudMetni(hud, "Health", "State");
                    for (int i = 0; i < 600; i++)
                    {
                        yield return null;
                        long v = bytes.LastValue; total += v;
                        if (v > 1024) heavy++;
                        string st = stateText != null ? stateText.text : string.Empty;
                        if (!ReferenceEquals(st, lastState)) { changes++; lastState = st; }
                        if (v > top) { top = v; topNote = "frame" + i + ":" + v + "B state='" + st + "' regen=" + rig.Health.Snapshot.IsRegenerating; }
                    }

                    sb.Append(" timeline600 totalB=").Append(total).Append(" heavyFrames=").Append(heavy).Append(" stateChanges=").Append(changes).Append(" top=").Append(topNote);
                    sb.Append(" special=").Append(WeaponEvents.Special.Phase).Append(" reduced=").Append(UiSettings.ReducedMotion).Append(" palette=").Append(UiSettings.ColorPalette).Append(" textScale=").Append(UiSettings.TextScale);
                    Debug.Log(sb.ToString());
                }
            }
            finally
            {
                Application.targetFrameRate = before;
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        private static IEnumerator SayacPozitifKontrol()
        {
            long once = GC.GetTotalMemory(false);
            var yem = new object[4000];
            for (int i = 0; i < yem.Length; i++) yem[i] = new byte[64];
            long fark = GC.GetTotalMemory(false) - once;
            Debug.Log("[DD-TEST] wp005.perf positiveControl allocB=" + fark + " keep=" + yem.Length);
            Assert.Greater(fark, 0, "yığın sayacı bilerek ayıran döngüde yükselmedi — ölçüm geçersiz");
            yield return null;
        }

        /// <summary>
        /// N kare boyunca kullanılan yığının büyümesini ölçer (test koşucusu dahil). Pencerede
        /// gen0 GC koşarsa sonuç güvenilmezdir; bir kez daha denenir, yine koşarsa −1 döner.
        /// Yığın 64 KB parçalar hâlinde büyüdüğünden pencere uzun tutulur (600 kare).
        /// </summary>
        /// <summary>
        /// WP-022K · arbiter method (WP-044 REPORT §ölçüm, WP-052): the window's cost is the
        /// ProfilerRecorder "GC Allocated In Frame" SUM when the recorder is live (a 64-object
        /// probe frame must read > 0), else the managed-heap delta of the old method. The heap
        /// delta grows in 64 KB steps and drifts with what earlier fixtures left on the heap
        /// (batch-order FAILs of WP-022C/F/J with the recorder reading a clean HUD), so it is
        /// the fallback, logged next to the recorder number when both exist.
        /// </summary>
        private static IEnumerator AyirmaOlc(int kare, Action<long> sonuc)
        {
            int frameRateBefore = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            try
            {
                using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    for (int i = 0; i < 10; i++) yield return null; // ısınma

                    // Recorder probe: one frame with 64 small objects must register.
                    var yem = new object[64];
                    for (int i = 0; i < yem.Length; i++) yem[i] = new byte[32];
                    yield return null;
                    bool recorderLive = bytes.Valid && bytes.LastValue > 0;
                    GC.KeepAlive(yem);
                    yield return null;

                    for (int deneme = 0; deneme < 2; deneme++)
                    {
                        long c0 = GC.CollectionCount(0);
                        long once = GC.GetTotalMemory(false);
                        long recorded = 0, top = 0; int topFrame = -1, heavy = 0; var buckets = new System.Text.StringBuilder();
                        for (int i = 0; i < kare; i++)
                        {
                            yield return null;
                            if (recorderLive)
                            {
                                long v = bytes.LastValue; recorded += v;
                                if (v > top) { top = v; topFrame = i; }
                                if (v > 512) heavy++;
                                if (i % 100 == 99) buckets.Append(recorded).Append(' ');
                            }
                        }

                        long fark = GC.GetTotalMemory(false) - once;
                        if (recorderLive)
                        {
                            Debug.Log("[DD-TEST] alloc window frames=" + kare + " recorderB=" + recorded + " heapDeltaB=" + fark + (GC.CollectionCount(0) != c0 ? " (gc ran)" : "")
                                      + " topFrame=" + topFrame + ":" + top + "B heavyFrames(>512B)=" + heavy + " cumulative@100=" + buckets);
                            sonuc(recorded);
                            yield break;
                        }

                        if (GC.CollectionCount(0) == c0)
                        {
                            sonuc(fark);
                            yield break;
                        }

                        Debug.Log("[DD-TEST] perf window gc-interrupted, retrying");
                    }

                    sonuc(-1);
                }
            }
            finally
            {
                Application.targetFrameRate = frameRateBefore;
            }
        }

        private static IEnumerator AkisiYatistir()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f && SceneManager.GetActiveScene().name != PrototypeScenes.Results)
            {
                yield return null;
            }

            yield return null;
        }

        // ================================================================== 1 · buton yolu

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ButonYolu_PlaySelectionStartRunResultsMainMenu()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;

            Assert.AreEqual(UiContext.MainMenu, UiNavigation.Base, "açılış tabanı ana menü");
            Assert.AreEqual(UiContext.None, UiNavigation.Layer, "WP-081 v4 (G5): ana menü bütün pane'ler kapalı açılır");
            Assert.IsFalse(UiNavigation.GameplayInputAllowed, "menü arkasında oyun girdisi kapalı");

            // Play → Selection (seçim ekranı ana menüden tek yol; her koşu öncesi açılır).
            Buton<MainMenuScreen>("Play").OnSubmit(null);
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            Assert.AreEqual(UiContext.Selection, UiNavigation.Base);
            Assert.IsFalse(UiNavigation.GameplayInputAllowed, "seçim ekranında oyun girdisi kapalı");

            // Kilitli silah satırı ölü kontrol: OnSubmit seçimi değiştirmez.
            ClassicWeaponId once = PrototypeUiFlow.SelectedWeapon;
            UiButton kilitli = null;
            foreach (UiButton b in UnityEngine.Object.FindObjectOfType<SelectionScreen>().GetComponentsInChildren<UiButton>(true))
            {
                if (b.gameObject.name.StartsWith("Weapon.") && !b.IsInteractable())
                {
                    kilitli = b;
                    break;
                }
            }

            if (kilitli != null)
            {
                kilitli.OnSubmit(null);
                Assert.AreEqual(once, PrototypeUiFlow.SelectedWeapon, "kilitli silah seçilemez");
            }

            Buton<SelectionScreen>("Start").OnSubmit(null);
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            Assert.AreEqual(UiContext.GameplayHud, UiNavigation.Base);
            Assert.IsTrue(UiNavigation.GameplayInputAllowed, "HUD tabanında oyun girdisi açık");
            Assert.AreEqual(1, AktifSayi<RunHudScreen>());
            Assert.AreEqual(1, AktifSayi<UpgradeScreen>());
            Assert.AreEqual(1, AktifSayi<PauseScreen>());

            yield return Oldur();
            yield return SahneyiBekle(PrototypeScenes.Results);
            yield return null;
            Assert.AreEqual(UiContext.RunResults, UiNavigation.Base);
            Assert.AreEqual(UiContext.None, UiNavigation.Layer);
            Assert.IsFalse(UiNavigation.MayLayer(UiContext.Paused), "Results üstünde pause imkânsız");
            Assert.AreEqual(0, AktifSayi<PauseScreen>(), "Results sahnesinde PauseScreen yok");
            Assert.AreEqual(0, AktifSayi<UpgradeScreen>());

            ResultsScreen sonuc = UnityEngine.Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(sonuc);
            Assert.IsTrue(sonuc.Model.HasRun);
            Assert.AreEqual("MAIN MENU", Buton<ResultsScreen>("MainMenu").LabelText.text);

            Buton<ResultsScreen>("MainMenu").OnSubmit(null);
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            Assert.AreEqual(UiContext.MainMenu, UiNavigation.Base);
            Assert.AreEqual(UiContext.None, UiNavigation.Layer, "dönüşte de pane'ler kapalı");
            Assert.AreEqual(1, AktifSayi<MainMenuScreen>());
            Debug.Log("[DD-TEST] wp005.buttonPath=OK");
        }

        // ================================================================== 2 · skinler

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator UcSkin_GorunurSecilebilir_StatDegismez()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;

            var skinler = new List<UiButton>();
            foreach (UiButton b in UnityEngine.Object.FindObjectOfType<SelectionScreen>().GetComponentsInChildren<UiButton>(true))
            {
                if (b.gameObject.name.StartsWith("Skin."))
                {
                    skinler.Add(b);
                }
            }

            Assert.AreEqual(3, skinler.Count, "üç skin kartı");
            for (int i = 0; i < skinler.Count; i++)
            {
                Assert.IsTrue(skinler[i].IsInteractable(), skinler[i].gameObject.name + " seçilebilir olmalı (baştan açık)");
                skinler[i].OnSubmit(null);
                Assert.AreEqual((PlayerSkin)i, PrototypeUiFlow.SelectedSkin, "kart " + i + " seçimi");
                int secili = 0;
                for (int j = 0; j < skinler.Count; j++)
                {
                    if (skinler[j].IsSelectedChoice) secili++;
                }

                Assert.AreEqual(1, secili, "tam bir skin seçili");
            }

            PrototypeUiFlow.SelectedSkin = PlayerSkin.IndigoBlack;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            PlayerRig rig = PlayerRegistry.Primary;
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.AreEqual(PlayerSkin.IndigoBlack, rig.Skin, "seçilen skin uygulandı");

            float can = rig.Health.Max;
            float atesAraligi = silah.Classic.ShotInterval;
            int sarjor = silah.Classic.MagazineSize;
            float dolum = silah.Classic.EffectiveReloadTime;
            int gold = GoldService.Instance.Wallet.Balance;
            int tier = silah.Modifiers.TotalTiers;

            for (int i = 0; i < PlayerSkins.Count; i++)
            {
                rig.ApplySkin((PlayerSkin)i);
                yield return null;
                Assert.AreEqual((PlayerSkin)i, rig.Skin);
                Assert.AreEqual(can, rig.Health.Max, "skin can değiştirmez");
                Assert.AreEqual(atesAraligi, silah.Classic.ShotInterval, 0.0001f, "skin atış hızı değiştirmez");
                Assert.AreEqual(sarjor, silah.Classic.MagazineSize, "skin şarjör değiştirmez");
                Assert.AreEqual(dolum, silah.Classic.EffectiveReloadTime, 0.0001f, "skin dolum değiştirmez");
                Assert.AreEqual(gold, GoldService.Instance.Wallet.Balance, "skin gold değiştirmez");
                Assert.AreEqual(tier, silah.Modifiers.TotalTiers, "skin upgrade değiştirmez");
            }

            Assert.AreEqual(PrototypeConfig.Active.Player.MaxHealth, rig.Health.Max, 0.001f, "can config'den");
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.skins=OK");
        }

        // ================================================================== 3 · HUD

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator HudMetinleri_SnapshotVeGoldOlayi()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            yield return null;
            yield return null;

            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud);
            RunHudViewModel vm = hud.Model;
            PlayerRig rig = PlayerRegistry.Primary;
            WeaponController silah = WeaponRegistry.ActiveController;

            Assert.AreEqual("1", vm.RoundText, "HUD Round 1");
            // WP-094 HUD v4 (R3): the round is drawn as a Roman numeral (the presenter keeps the Arabic text).
            Assert.AreEqual("I", HudMetni(hud, "Round", "RoundValue").text);

            int max = Mathf.RoundToInt(rig.Health.Max);
            Assert.AreEqual(max + " / " + max, vm.HealthText, "sağlık snapshot'ı");
            // WP-068 8-R: the ink HUD sets the value alone (NumericL) and "/ max" beside it (calm only) - the same numbers.
            Assert.AreEqual(vm.HealthCurrent.ToString(), HudMetni(hud, "Health", "Value").text);
            Assert.AreEqual("/ " + vm.HealthMax, HudMetni(hud, "Health", "Max").text);

            Assert.AreEqual(silah.Classic.Ammo + " / " + silah.Classic.MagazineSize, vm.AmmoText, "şarjör snapshot'ı");
            // WP-068 E11: "30" NumericXL over "/ 30" (Fraction) - the same numbers as the snapshot.
            Assert.AreEqual(vm.Ammo.ToString(), HudMetni(hud, "Ammo", "Value").text);
            Assert.AreEqual("/ " + vm.Magazine, HudMetni(hud, "Ammo", "Max").text);
            Assert.AreEqual(silah.Classic.DisplayName.ToUpperInvariant(), vm.WeaponName);
            Assert.AreEqual("no upgrades", vm.BuildText);

            Assert.AreEqual("0", vm.GoldText);
            TMP_Text goldMetni = HudMetni(hud, "Gold", "Value");
            Assert.AreEqual("0", goldMetni.text);

            // Gold: cüzdan kredisi aynı karede olayla HUD'a düşer (RunEvents.GoldChanged).
            int olay = 0;
            Action<int> dinle = b => olay++;
            RunEvents.GoldChanged += dinle;
            Assert.IsTrue(GoldService.Instance.Wallet.Credit(7, rig.transform.position));
            RunEvents.GoldChanged -= dinle;
            Assert.AreEqual(1, olay, "GoldChanged bir kez");
            Assert.AreEqual("7", vm.GoldText, "presenter aynı karede güncellendi (olay)");
            // WP-068 E12: the counter takes the new balance on the same frame and rolls to it (Currency, ≤ 600 ms).
            UiCounter goldSayaci = goldMetni.GetComponentInParent<UiCounter>();
            Assert.IsNotNull(goldSayaci, "gold bir UiCounter");
            Assert.AreEqual(7, goldSayaci.Target, "sayaç aynı karede hedefi aldı");
            float rulo = Time.realtimeSinceStartup + 1.5f;
            while (goldMetni.text != "7" && Time.realtimeSinceStartup < rulo)
            {
                yield return null;
            }

            Assert.AreEqual("7", goldMetni.text, "sayaç 7'ye yuvarlandı");

            // Hasar → sağlık metni ve düşük eşik.
            rig.Health.ApplyDamage(rig.Health.Max * 0.75f, rig.transform.position + Vector3.forward, "wp005-hud");
            yield return null;
            Assert.IsTrue(vm.HealthLow, "%25 can düşük eşiğin altında");
            StringAssert.StartsWith(Mathf.CeilToInt(rig.Health.Current) + " /", vm.HealthText);
            Assert.AreEqual(vm.HealthCurrent.ToString(), HudMetni(hud, "Health", "Value").text);

            // Sessiz kare allocation ölçümü: yönetilen yığın sayacı (GC.GetAllocatedBytesForCurrentThread)
            // 120 kare HUD açık, 120 kare HUD kapalı (Update durur); fark = HUD'ın kare başı maliyeti.
            // Test koşucusunun kendi kare maliyeti her iki pencerede de aynıdır.
            rig.Health.DiagnosisInvulnerable = true;

            // WP-022K: the window must be QUIET. Health regenerates from 25 % and crosses the
            // low-health threshold a few frames in; the HUD raises HudLowHealthLeft once and
            // its downstream consumers (audio cue, VFX) do a one-time ~200 KB set-up on that
            // frame - a batch-only FAIL that looked like 350 B/frame but was one frame
            // (recorder timeline: topFrame 41 = 197 KB, the other 599 frames ≈ 40 B). Wait
            // for the crossing before measuring; the per-frame contract is what this test is for.
            // WP-094 (08.10, feel contract §4.7): the HUD's LOW follows FeelEvents' band - in at ≤ 30 %, out at ≥ 38 % - so the
            // crossing that raises HudLowHealthLeft is the band's exit, a little after the presenter's 30 % flag clears.
            float t0 = Time.realtimeSinceStartup;
            while ((vm.HealthLow || DD.Prototype.Feedback.FeelEvents.InLowHealth) && Time.realtimeSinceStartup - t0 < 15f)
            {
                yield return null;
            }

            Assert.IsFalse(vm.HealthLow, "öncül: can eşiğin üstüne çıkmalı (regen)");
            Assert.IsFalse(DD.Prototype.Feedback.FeelEvents.InLowHealth, "öncül: FeelEvents düşük can bandından çıkmalı (≥ %38)");
            for (int i = 0; i < 30; i++) yield return null;

            yield return SayacPozitifKontrol();
            long hudAcik = 0;
            yield return AyirmaOlc(600, v => hudAcik = v);
            hud.enabled = false;
            long hudKapali = 0;
            yield return AyirmaOlc(600, v => hudKapali = v);
            hud.enabled = true;
            long hudFarki = hudAcik - hudKapali;
            Debug.Log("[DD-TEST] wp005.perf hudIdle600 heapB=" + hudAcik + " hudOff600 heapB=" + hudKapali +
                      " hudDelta600=" + hudFarki + " (B/frame≈" + (hudFarki / 600f).ToString("0.0") + ")");
            if (hudAcik >= 0 && hudKapali >= 0)
            {
                // 600 karede ≥128 KB = kare başı ≥~220 B: eski HUD'ın (≈300–500 B/kare) seviyesi.
                Assert.LessOrEqual(hudFarki, 128 * 1024,
                    "HUD 600 sessiz karede yığını 128 KB'den fazla büyüttü (kare başı string biçimleme geri geldi?)");
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.hud=OK");
        }

        // ================================================================== 4 · profil seviyesi güç değil

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ProfilSeviyesi_KosuBaslangicinaSizmaz()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();

            // İzole profili en az L4'e çıkar. Eğri profil yapılandırmasının (WP-077 B2a onu taşıdı; 600 XP artık L3):
            // L4'e varana dek 600 XP'lik koşular uygulanır - testin sorusu seviyenin koşuya sızmaması, eğri değil.
            for (int i = 0; i < 10 && ProfileService.Current.ProfileLevel < 4; i++)
            {
                ProfileService.ApplyRun(new RunSummary
                {
                    CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                    ReachedRound = 9, CompletedRounds = 8, CompletedRoundXp = 600, TotalXpEarned = 600,
                    EndReason = RunEndReason.PlayerDeath, BuildDescription = "wp005-level-setup"
                });
            }

            Assert.GreaterOrEqual(ProfileService.Current.ProfileLevel, 4);
            yield return null;

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            PlayerRig rig = PlayerRegistry.Primary;
            WeaponController silah = WeaponRegistry.ActiveController;
            PrototypeConfig config = PrototypeConfig.Active;
            ClassicWeaponSettings ayar = config.Weapons.Find(silah.Classic.Id);

            Assert.AreEqual(config.Player.MaxHealth, rig.Health.Max, 0.001f, "profil seviyesi can vermez");
            Assert.AreEqual(rig.Health.Max, rig.Health.Current, 0.001f, "tam canla başlar");
            Assert.AreEqual(0, GoldService.Instance.Wallet.Balance, "profil seviyesi başlangıç gold'u vermez");
            Assert.AreEqual(0, silah.Modifiers.TotalTiers, "profil seviyesi upgrade vermez");
            Assert.AreEqual(1f / ayar.FireRate, silah.Classic.ShotInterval, 0.0001f, "atış hızı config'den");
            Assert.AreEqual(ayar.MagazineSize, silah.Classic.MagazineSize, "şarjör config'den");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.levelIsolation=OK level=" + ProfileService.Current.ProfileLevel);
        }

        // ================================================================== 5 · Results ↔ özet

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Results_SunumOzetleEslesir_UpgradeBirKez_HudSonDegeriKorur()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            RunDirector run = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = true;
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            UpgradeScreen upgrade = UnityEngine.Object.FindObjectOfType<UpgradeScreen>();
            Assert.IsNotNull(upgrade);

            int upgradingSayisi = 0;
            Action<RunPhase> faz = p => { if (p == RunPhase.Upgrading) upgradingSayisi++; };
            RunEvents.PhaseChanged += faz;

            RunSummary ozet = default;
            int bittiSayisi = 0;
            Action<RunSummary> bitti = s => { ozet = s; bittiSayisi++; };
            RunEvents.RunEnded += bitti;

            try
            {
                // Round 1: 5 kill, sonra F1 ile tamamla → Upgrade bir kez, geçerli bağlamla.
                yield return OldurVeTopla(5);
                Assert.AreEqual(5, run.Stats.TotalKills);
                yield return RoundTamamla();

                Assert.AreEqual(1, upgradingSayisi, "Upgrading fazı bir kez");
                Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Layer, "navigation: shop katmanı açık");
                Assert.IsTrue(run.LastRoundEnd.IsValid);
                Assert.AreEqual(run.RunId, run.LastRoundEnd.RunId);
                Assert.AreEqual(1, run.LastRoundEnd.Round);
                Assert.AreEqual(GoldService.Instance.Wallet.Balance, run.Shop.Gold, "ekran bakiyesi = cüzdan");
                Assert.AreEqual(run.LastRoundEnd.BalanceAtOpen, run.Shop.Gold, "auto-collect sonrası bakiye");
                Assert.IsTrue(MetinVar<UpgradeScreen>(run.Shop.Gold.ToString(), out _), "gold bakiyesi ekranda yazılı");
                int goldR1 = run.Shop.Gold;

                // Kartlarda ikon yuvası: WP-022'den itibaren her görünür kart AP-012 dal ikonunu
                // çizer (Upgrade.<Branch>); monogram yalnız sanat yokken görünür.
                int ikon = 0, monogram = 0;
                foreach (Image img in upgrade.GetComponentsInChildren<Image>(false))
                {
                    if (img.gameObject.name == "Icon" && img.sprite != null && img.sprite.name.StartsWith("Upgrade.")) ikon++;
                }

                foreach (TMP_Text t in upgrade.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (t.gameObject.name == "Monogram" && !string.IsNullOrEmpty(t.text)) monogram++;
                }

                Assert.AreEqual(run.Shop.Cards.Count, ikon, "her kartta bir AP-012 dal ikonu");
                Assert.AreEqual(0, monogram, "sanat varken monogram fallback görünmez");

                // Devam → round 2: HUD yenilenir, gold korunur.
                run.CloseUpgradeScreen();
                yield return null; // ekran fazı bir sonraki Update'te okur
                Assert.AreEqual(UiContext.None, UiNavigation.Layer, "shop kapandı");
                yield return Bekle(() => run.Phase == RunPhase.Fighting && run.Round == 2, FazZamanAsimi, "Round 2 başlamadı.");
                yield return null;
                Assert.AreEqual("2", hud.Model.RoundText, "HUD round 2");
                Assert.AreEqual(goldR1.ToString(), hud.Model.GoldText, "gold roundlar arası korunur");

                // Round 2'de 2 kill, sonra ölüm. Upgrade açılmaz; Results bir kez.
                yield return OldurVeTopla(2);
                Assert.AreEqual(7, run.Stats.TotalKills);
                int goldOnce = GoldService.Instance.Wallet.Balance;
                string hudGoldOnce = hud.Model.GoldText;

                yield return Oldur();
                yield return null;
                Assert.AreEqual(1, bittiSayisi, "RunEnded bir kez");
                Assert.AreEqual(1, upgradingSayisi, "ölümde Upgrade açılmadı");
                Assert.AreEqual(0, GoldService.Instance.Wallet.Balance, "cüzdan run bitince sıfırlandı");
                Assert.AreEqual(hudGoldOnce, hud.Model.GoldText, "HUD ölüm beat'inde son canlı gold'u korur (0'a düşmez)");
                Assert.AreEqual(goldOnce, ozet.GoldEarned - ozet.GoldSpent, "özet: kazanılan − harcanan = ölüm anındaki bakiye");
                Assert.IsFalse(PauseScreen.CanOpen(), "Dead fazında pause açılamaz");

                yield return SahneyiBekle(PrototypeScenes.Results);
                yield return null;
            }
            finally
            {
                RunEvents.PhaseChanged -= faz;
                RunEvents.RunEnded -= bitti;
            }

            // Results: immutable rapor, run reset sonrası.
            Assert.IsNull(RunDirector.Instance, "run sahnesi gitti; director yok");
            ResultsScreen sonuc = UnityEngine.Object.FindObjectOfType<ResultsScreen>();
            ResultsViewModel vm = sonuc.Model;
            RunRewardReport rapor = ProfileService.LastReport;
            Assert.IsNotNull(rapor);

            Assert.AreEqual(2, ozet.ReachedRound);
            Assert.AreEqual(1, ozet.CompletedRounds);
            Assert.AreEqual(7, ozet.TotalKills);
            Assert.AreEqual(7, ozet.StandardKills);
            Assert.AreEqual(ozet.TotalKills, rapor.Summary.TotalKills, "rapor özeti = yayınlanan özet");

            Assert.AreEqual("2", vm.ReachedRound);
            Assert.AreEqual("DEFEATED IN ROUND 2", vm.Eyebrow);
            Assert.AreEqual("1 round completed", vm.RoundsCompletedCaption);
            Assert.AreEqual("7", vm.Combat[0].Value, "Total Kills");
            Assert.AreEqual("7", vm.Combat[1].Value, "Standard");
            Assert.AreEqual(2, vm.Combat.Count, "olmayan varyant satırı yok");
            Assert.AreEqual("+" + ozet.CompletedRoundXp, vm.XpBreakdown[0].Value);
            Assert.AreEqual("+" + ozet.KillXp, vm.XpBreakdown[1].Value);
            Assert.AreEqual("+" + (ozet.CompletedRoundXp + ozet.KillXp), vm.TotalXp);
            Assert.AreEqual(ozet.GoldEarned.ToString(), vm.Economy[0].Value, "Gold Collected = özet");
            Assert.Greater(ozet.GoldEarned, 0, "kill'ler gold üretti");

            int killXp = PrototypeConfig.Active.KillXp(EnemyTier.Standard) * 7;
            int roundXp = PrototypeConfig.Active.RoundCompletionXp(1);
            Assert.AreEqual(killXp, ozet.KillXp, "kill XP = 7 × Standard kill XP (yarım round dahil)");
            Assert.AreEqual(roundXp, ozet.CompletedRoundXp, "yalnız tamamlanan round 1 XP verir");

            // Ekrandaki Text'ler modeli yansıtıyor.
            Assert.IsTrue(MetinVar<ResultsScreen>("DEFEATED IN ROUND 2", out _));
            Assert.IsTrue(MetinVar<ResultsScreen>("MAIN MENU", out _));
            Assert.IsFalse(MetinVar<ResultsScreen>("CLAIM", out _), "Claim eylemi yok");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.results=OK reached=" + ozet.ReachedRound + " kills=" + ozet.TotalKills +
                      " roundXp=" + ozet.CompletedRoundXp + " killXp=" + ozet.KillXp + " gold=" + ozet.GoldEarned);
        }

        // ================================================================== 6 · ana menü pane'leri

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AnaMenuPaneleri_YeniDegerlerVeEmptyState()
        {
            LogAssert.ignoreFailingMessages = true;
            ProfileService.ResetProfile();
            yield return BootTanMainMenuye();
            yield return null;

            MainMenuScreen menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
            int changedOnce = AboneSayisi(typeof(ProfileService), "Changed");

            Buton<MainMenuScreen>("Tab.Statistics").OnSubmit(null);
            yield return null;
            Assert.AreEqual(UiContext.Statistics, UiNavigation.Layer);
            Assert.AreEqual(UiContext.Statistics, menu.Pane);
            Assert.IsTrue(MetinVar<MainMenuScreen>("No runs recorded yet", out _), "taze profil: statistics empty notice");
            Assert.IsTrue(MetinVar<MainMenuScreen>("Runs Played", out _));

            Buton<MainMenuScreen>("Tab.Achievements").OnSubmit(null);
            yield return null;
            Assert.AreEqual(UiContext.Achievements, UiNavigation.Layer);
            int tanim = ProfileService.AllAchievements.Count;
            Assert.IsTrue(MetinVar<MainMenuScreen>("0 of " + tanim + " earned", out _), "taze profil: hiç başarım yok");
            Assert.IsFalse(MetinVar<MainMenuScreen>("CLAIM", out _));

            // Aynı sekmeye ikinci tıklama idempotent: pane yeniden inşa edilmez.
            int cocukOnce = SayPaneCocuk(menu);
            Buton<MainMenuScreen>("Tab.Achievements").OnSubmit(null);
            Assert.AreEqual(cocukOnce, SayPaneCocuk(menu), "aynı pane yeniden inşa edilmedi");
            Assert.AreEqual(UiContext.Achievements, UiNavigation.Layer);

            // Achievement tanımı olmayan config → empty state, hata yok.
            PrototypeConfig klon = UnityEngine.Object.Instantiate(PrototypeConfig.Active);
            klon.Progression.Achievements.Clear();
            PrototypeConfig.SetActive(klon);
            Buton<MainMenuScreen>("Tab.Profile").OnSubmit(null);
            Buton<MainMenuScreen>("Tab.Achievements").OnSubmit(null);
            yield return null;
            if (ProfileService.AllAchievements.Count == 0)
            {
                Assert.IsTrue(MetinVar<MainMenuScreen>(AchievementsViewModel.EmptyTitle, out _), "tanım yok → empty state");
            }
            else
            {
                // WP-023: definitions come from AchievementCatalog, not PrototypeConfig, so an
                // empty config no longer empties the pane; the pane must still draw every
                // catalog entry without the empty state.
                Assert.IsFalse(MetinVar<MainMenuScreen>(AchievementsViewModel.EmptyTitle, out _), "katalog tanımlıyken empty state yok");
                Assert.IsTrue(MetinVar<MainMenuScreen>("0 of " + ProfileService.AllAchievements.Count + " earned", out _));
            }
            PrototypeConfig.SetActive(_eskiConfig);
            UnityEngine.Object.Destroy(klon);

            Buton<MainMenuScreen>("Tab.Profile").OnSubmit(null);
            yield return null;
            Assert.AreEqual(UiContext.Profile, UiNavigation.Layer);
            Assert.IsTrue(MetinVar<MainMenuScreen>("PROFILE LEVEL", out _));
            Assert.IsTrue(MetinVar<MainMenuScreen>("LEVEL 1", out _));
            Assert.IsTrue(MetinVar<MainMenuScreen>("never grants starting damage", out _), "seviye güç değil");

            // Bir koşu: ≥1 kill, ölüm → Results → Main Menu; pane'ler yeni değerleri gösterir.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return KontrolluSaha();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            yield return OldurVeTopla(3);
            Assert.AreEqual(3, RunDirector.Instance.Stats.TotalKills);
            yield return Oldur();
            yield return SahneyiBekle(PrototypeScenes.Results);
            Assert.AreEqual(changedOnce - 1, AboneSayisi(typeof(ProfileService), "Changed"), "menü aboneliği sahneyle gitti");
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            Assert.AreEqual(changedOnce, AboneSayisi(typeof(ProfileService), "Changed"), "menü aboneliği geri geldi (birikme yok)");
            menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu, "yeni MainMenuScreen");

            ProfileData profil = ProfileService.Current;
            Assert.AreEqual(1, profil.RunsPlayed);
            Assert.AreEqual(3, profil.TotalKills);
            Assert.IsFalse(menu.PaneOpen, "WP-081 v4: dönüşte pane'ler kapalı");
            Buton<MainMenuScreen>("Tab.Profile").OnSubmit(null);
            yield return null;
            Assert.IsTrue(MetinVar<MainMenuScreen>("1 of " + tanim + " achievements earned", out _) ||
                          MetinVar<MainMenuScreen>("achievements earned", out _), "profil pane başarım özeti");

            Buton<MainMenuScreen>("Tab.Statistics").OnSubmit(null);
            yield return null;
            Assert.IsFalse(MetinVar<MainMenuScreen>("No runs recorded yet", out _), "koşu sonrası empty notice yok");
            Assert.IsTrue(SatirDegeri(menu, "Runs Played", "1"), "Runs Played 1");
            Assert.IsTrue(SatirDegeri(menu, "Total Kills", "3"), "Total Kills 3");
            Assert.IsTrue(SatirDegeri(menu, "Highest Round", "1"), "Highest Round 1");

            Buton<MainMenuScreen>("Tab.Achievements").OnSubmit(null);
            yield return null;
            Assert.IsTrue(MetinVar<MainMenuScreen>("1 of " + tanim + " earned", out _), "First Blood otomatik açıldı");
            // WP-022D: a freshly earned entry reads NEW on the first visit (WP-035 "new" flag), EARNED afterwards.
            Assert.IsTrue(MetinVar<MainMenuScreen>("EARNED", out _) || MetinVar<MainMenuScreen>("NEW", out _), "kazanılmış çip");
            Debug.Log("[DD-TEST] wp005.panes=OK");
        }

        private static int SayPaneCocuk(MainMenuScreen menu)
        {
            Transform[] hepsi = menu.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < hepsi.Length; i++)
            {
                if (hepsi[i].name == "PaneCard")
                {
                    // WP-098B: the kit carrier PaneCard/{Content (+ C2 wash, edge trace)[, PaneLeaving while a pane cross-fades]} - no Fill / Frame
                    return hepsi[i].GetComponentsInChildren<Transform>(true).Length;
                }
            }

            return -1;
        }

        private static Transform PaneParcasi(MainMenuScreen menu, string ad)
        {
            foreach (Transform t in menu.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == ad)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>
        /// WP-098B · pane taşıyıcısı ömrü (fırça kiti, KILAVUZ §3): kapalı gelişte taşıyıcı ve perde çizilmez; açılınca
        /// çerçevesiz "PaneCard" (yıkama, çerçeve yok), perde kitin Pane α'sında ve metin zemini ≥ 0,945; her pane'de iz
        /// bütçesi (başlık 1, ayırıcı ≤ 2, C8 ≤ 2, kenar ≤ 1, toplam ≤ 6); pane değişince eski içerik solarak gider
        /// (çapraz geçiş) ve sonra düşer; kapatınca PaneOpen hemen false, solma bitince taşıyıcı ve perde kapanır, içerik
        /// temizlenir. Süreler ≤ 0,35 s.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AnaMenuPane_PerdeYikamaCaprazGecisKapanis()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;
            MainMenuScreen menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);
            float bekle = MainMenuScreen.PaneLook.MaxSeconds + 0.15f;

            UiBrushPane tasiyici = menu.PaneCarrier;
            Assert.IsNotNull(tasiyici, "pane taşıyıcısı kitten (UiBrushKit.Pane)");
            Transform kart = PaneParcasi(menu, MainMenuScreen.PaneLook.CardName);
            Assert.IsNotNull(kart, "PaneCard adı korunur");
            Assert.AreSame(tasiyici.Root, kart, "PaneCard = kitin pane kökü");
            Image perde = tasiyici.Scrim;
            Assert.IsNotNull(perde, "C1 perde var");
            Assert.IsFalse(menu.PaneOpen, "G5: pane'ler kapalı gelir");
            Assert.IsFalse(tasiyici.IsOpen);
            Assert.IsTrue(!perde.gameObject.activeInHierarchy || perde.color.a * perde.canvasRenderer.GetAlpha() < 1e-3f, "kapalı: perde çizilmez");

            menu.ShowPane(UiContext.Profile);
            yield return null;
            Assert.IsTrue(menu.PaneOpen);
            Assert.IsTrue(kart.gameObject.activeInHierarchy && perde.gameObject.activeInHierarchy);
            Assert.IsNotNull(tasiyici.Wash, "C2 yıkama");
            Assert.AreEqual(UiBrushScrim.Pane, tasiyici.ScrimLevel, "menü pane'i: Pane perdesi");
            Assert.IsNull(kart.GetComponent<Image>(), "çerçevesiz kart");
            Assert.IsNull(kart.Find("Frame"), "A4 çerçevesi yok");
            yield return new WaitForSecondsRealtime(bekle);
            Assert.AreEqual(UiBrushTokens.ScrimPane, perde.color.a * perde.canvasRenderer.GetAlpha(), 0.01f, "perde kitin Pane α'sında (doğrusal 0,72)");
            Assert.GreaterOrEqual(tasiyici.TextGroundAlpha, UiBrushTokens.TextGroundMin, "metin zemini perde ⊕ yıkama ≥ 0,945");
            CanvasGroup icerik = tasiyici.Content.GetComponent<CanvasGroup>();
            Assert.IsTrue(icerik == null || Mathf.Approximately(icerik.alpha, 1f), "içerik 0,35 s içinde yerinde");

            menu.ShowPane(UiContext.Statistics);
            yield return null;
            if (!UiMotion.ReducedMotion)
            {
                Transform giden = PaneParcasi(menu, MainMenuScreen.PaneLook.LeavingName);
                Assert.IsNotNull(giden, "çapraz geçiş: eski pane solarak gider");
                Assert.IsFalse(giden.GetComponent<CanvasGroup>().interactable, "giden içerik etkileşimsiz");
            }

            Assert.IsTrue(MetinVar<MainMenuScreen>("Runs Played", out _), "yeni pane hemen kurulu");
            yield return new WaitForSecondsRealtime(bekle);
            yield return null;
            Assert.IsNull(PaneParcasi(menu, MainMenuScreen.PaneLook.LeavingName), "solma bitince eski içerik düştü");

            // KILAVUZ §1: per window ≤ 6 traces - title 1, dividers ≤ 2, C8 ≤ 2, edge ≤ 1, wash ≤ 2.
            var butce = new List<string>();
            foreach (UiContext pane in new[] { UiContext.Profile, UiContext.Statistics, UiContext.Achievements, UiContext.Settings, UiContext.Controls, UiContext.Accessibility })
            {
                menu.ShowPane(pane);
                yield return new WaitForSecondsRealtime(bekle);
                yield return null;
                int baslik = 0, ayirici = 0, secim = 0, kenar = 0;
                foreach (UiBrushMark iz in kart.GetComponentsInChildren<UiBrushMark>(false))
                {
                    switch (iz.Kind)
                    {
                        case UiBrushMarkKind.TitleStroke: baslik++; break;
                        case UiBrushMarkKind.Divider: ayirici++; break;
                        case UiBrushMarkKind.Selection: secim++; break;
                        default: kenar++; break;
                    }
                }

                int yikama = tasiyici.Wash != null ? 1 : 0;
                int toplam = baslik + ayirici + secim + kenar + yikama;
                Debug.Log("[DD-TEST] wp098b.paneTraces " + pane + " title=" + baslik + " divider=" + ayirici + " c8=" + secim + " edge=" + kenar + " wash=" + yikama + " total=" + toplam);
                if (baslik != UiBrushTokens.MaxTitle) butce.Add(pane + ": başlık fırçası " + baslik);
                if (ayirici > UiBrushTokens.MaxDivider) butce.Add(pane + ": ayırıcı " + ayirici);
                if (secim > UiBrushTokens.MaxSelection) butce.Add(pane + ": C8 " + secim);
                if (kenar > UiBrushTokens.MaxEdge) butce.Add(pane + ": kenar " + kenar);
                if (toplam > UiBrushTokens.MaxTraces) butce.Add(pane + ": toplam " + toplam);
                if (pane == UiContext.Settings && secim != 1) butce.Add("Settings: sekme şeridinde tek C8 beklenir, " + secim);
                if (!menu.PaneOpen || menu.Pane != pane) butce.Add(pane + ": açılmadı");

                // WP-098B 2a: the content rows are the kit's - no legacy uGUI slider or Outline in any pane (envanter #7).
                if (kart.GetComponentsInChildren<Slider>(true).Length > 0) butce.Add(pane + ": eski uGUI Slider");
                if (kart.GetComponentsInChildren<Outline>(true).Length > 0) butce.Add(pane + ": Outline");
                if (pane == UiContext.Settings)
                {
                    Transform olcek = PaneParcasi(menu, "Setting.UI SCALE"), veri = PaneParcasi(menu, "Telemetry"), ekran = PaneParcasi(menu, "Display");
                    if (olcek == null || olcek.GetComponent<UiSlider>() == null) butce.Add("Settings: Setting.UI SCALE kitin C14'ü değil");
                    if (veri == null || veri.GetComponent<UiSwitch>() == null) butce.Add("Settings: Telemetry kitin C15'i değil");
                    if (ekran == null || ekran.GetComponent<UiStepper>() == null) butce.Add("Settings: Display kitin C16'sı değil");
                }

                if (pane == UiContext.Achievements)
                {
                    // C18 rows: a focus stop per achievement on the page, its status a word (EARNED / NEW / IN PROGRESS), no chip pulse.
                    int satir = 0;
                    foreach (UiButton b in kart.GetComponentsInChildren<UiButton>(false))
                    {
                        if (b.name.StartsWith("Achievement.")) satir++;
                    }

                    if (satir == 0) butce.Add("Achievements: satırlar kitin ListRow'u değil (UiButton yok)");
                    if (kart.GetComponentsInChildren<UiPulse>(true).Length > 0) butce.Add(pane + ": NEW çipi hâlâ nabızlı");
                }
            }

            Assert.IsEmpty(butce, "WP-098B iz bütçesi:\n" + string.Join("\n", butce));

            menu.ShowPane(UiContext.MainMenu);
            Assert.IsFalse(menu.PaneOpen, "kapatınca PaneOpen hemen false (solma 'açık' değil)");
            Assert.AreEqual(UiContext.MainMenu, menu.Pane);
            yield return new WaitForSecondsRealtime(bekle);
            yield return null;
            Assert.IsFalse(kart.gameObject.activeSelf, "solma bitince taşıyıcı kapandı (kitin cue'su)");
            Assert.IsFalse(perde.gameObject.activeSelf, "solma bitince perde kapandı");
            Assert.AreEqual(0, tasiyici.Content.childCount, "içerik temizlendi");
            Debug.Log("[DD-TEST] wp098b.paneLifecycle=OK reducedMotion=" + UiMotion.ReducedMotion);
        }

        /// <summary>Bir StatRow'da anahtar/değer çifti var mı (Row/Key, Row/Value).</summary>
        private static bool SatirDegeri(MainMenuScreen menu, string anahtar, string deger)
        {
            TMP_Text[] metinler = menu.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < metinler.Length; i++)
            {
                if (metinler[i].gameObject.name != "Key" || metinler[i].text != anahtar)
                {
                    continue;
                }

                Transform satir = metinler[i].transform.parent;
                TMP_Text valueText = null;
                for (int c = 0; c < satir.childCount; c++)
                {
                    if (satir.GetChild(c).name == "Value")
                    {
                        valueText = satir.GetChild(c).GetComponent<TMP_Text>();
                    }
                }

                if (valueText != null && valueText.text == deger)
                {
                    return true;
                }
            }

            return false;
        }

        // ================================================================== 7 · pause × upgrade

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PauseUpgradeUstunde_KapiVeOdakIadesi_IdleAllocation()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            RunDirector run = RunDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            PauseScreen pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();
            UpgradeScreen upgrade = UnityEngine.Object.FindObjectOfType<UpgradeScreen>();

            yield return OldurVeTopla(3);
            yield return RoundTamamla();
            Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Layer);

            CanvasGroup grup = null;
            Canvas upgradeCanvas = null;
            foreach (Canvas c in upgrade.GetComponentsInChildren<Canvas>(true))
            {
                CanvasGroup g = c.GetComponent<CanvasGroup>();
                if (g != null) { grup = g; upgradeCanvas = c; break; }
            }

            Assert.IsNotNull(grup, "Upgrade paneli CanvasGroup");

            // Fade bitsin, odak bir karta/CONTINUE'ya otursun.
            yield return Bekle(() => grup.alpha >= 0.999f, 5f, "upgrade fade bitmedi");
            yield return null;
            GameObject odakOnce = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(odakOnce, "upgrade ekranı açılınca odak var");
            Assert.IsTrue(odakOnce.transform.IsChildOf(upgradeCanvas.transform), "odak upgrade ekranında");

            // Upgrading idle allocation ölçümü (GateForPause artık sahne aramıyor): 120 kare
            // upgrade ekranı açık / 120 kare ekran bileşeni kapalı (Update durur); fark = ekran maliyeti.
            yield return SayacPozitifKontrol();
            long upgradeAcik = 0;
            yield return AyirmaOlc(600, v => upgradeAcik = v);
            upgrade.enabled = false;
            long upgradeKapali = 0;
            yield return AyirmaOlc(600, v => upgradeKapali = v);
            // Bileşen kapanınca OnDisable katmanı bırakır; yeniden açılınca panel fazdan yeniden
            // açılır (fade + odak). Devam etmeden önce eski duruma dönmesini bekle.
            upgrade.enabled = true;
            yield return null;
            yield return Bekle(() => UiNavigation.Layer == UiContext.RoundUpgrade && grup.alpha >= 0.999f, 5f,
                "upgrade ekranı yeniden açılmadı");
            yield return null;
            long upgradeFarki = upgradeAcik - upgradeKapali;
            Debug.Log("[DD-TEST] wp005.perf upgradingIdle600 heapB=" + upgradeAcik + " screenOff600 heapB=" + upgradeKapali +
                      " delta600=" + upgradeFarki + " (B/frame≈" + (upgradeFarki / 600f).ToString("0.0") + ")");
            if (upgradeAcik >= 0 && upgradeKapali >= 0)
            {
                Assert.LessOrEqual(upgradeFarki, 128 * 1024,
                    "Upgrade ekranı 600 idle karede yığını 128 KB'den fazla büyüttü (sahne araması/biçimleme geri geldi?)");
            }

            // Pause shop üstünde: shop etkileşimsiz, dünya donuk kalır, navigation Paused.
            pause.SetPaused(true);
            Assert.IsTrue(pause.IsPaused);
            Assert.IsTrue(UiNavigation.IsPaused);
            Assert.AreEqual(UiContext.Paused, UiNavigation.Current);
            Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Layer, "shop açık kalır");
            yield return null;
            Assert.IsFalse(grup.interactable, "pause açıkken shop etkileşimsiz");
            Assert.IsFalse(grup.blocksRaycasts);
            Assert.AreEqual(2, RunFlowClock.HoldCount, "RoundEndHold + PauseHold");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);

            // Resume: shop yeniden etkileşimli, odak shop'a döner, dünya HÂLÂ donuk (RoundEndHold).
            pause.SetPaused(false);
            yield return null;
            yield return null;
            Assert.IsFalse(UiNavigation.IsPaused);
            Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Current);
            Assert.IsTrue(grup.interactable, "resume sonrası shop etkileşimli");
            GameObject odakSonra = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(odakSonra, "resume sonrası odak var");
            Assert.IsTrue(odakSonra.transform.IsChildOf(upgradeCanvas.transform), "odak shop'a döndü");
            Assert.AreEqual(1, RunFlowClock.HoldCount, "yalnız RoundEndHold kaldı; shop altında dünya donuk");
            Assert.AreEqual(RunPhase.Upgrading, run.Phase);

            run.CloseUpgradeScreen();
            yield return Bekle(() => run.Phase == RunPhase.Fighting, FazZamanAsimi, "sonraki round başlamadı");
            yield return SimulasyonSerbestOlmali("upgrade-kapandi");

            // Fighting'de pause: odak iadesi (odak yoktu → yine yok, hata yok).
            pause.SetPaused(true);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            pause.SetPaused(false);
            yield return null;
            yield return SimulasyonSerbestOlmali("pause-kapandi");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.pauseUpgrade=OK");
        }

        /// <summary>
        /// WP-098B · raund sonu fırça penceresi (KILAVUZ §3 "Raund sonu / yükseltme", G6): taşıyıcı kitin UiBrushPane'i -
        /// Wallpaper perdesi kitin doğrusal α'sında, ağaç + detay sütununda iki C2 yıkama, metin zemini ≥ 0,945; başlık kitin
        /// C5'i (Menu.Upgrade, tek başlık fırçası), v2 süsü (yaprak, örtü, ayak çizgisi) yok; Continue / Undo / Reroll adları
        /// korunur; REROLL kitin C11'i ve isabeti ≥ 48; pad sırası düğümler → AL / İĞNELE → REROLL → DEVAM; kapanış kitin:
        /// girdi anında kapanır, solma bitince pane ve tuval kapanır (≤ 0,35 s).
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Yukseltme_FircaPencere_DuvarKagidiPerdesi_IkiYikama_RerollIsabet_PadSirasi()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            RunDirector run = RunDirector.Instance;
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            UpgradeScreen upgrade = UnityEngine.Object.FindObjectOfType<UpgradeScreen>();
            Assert.IsNotNull(upgrade);
            run.Shop.NodeCardsOverride = true;   // G6 is the tree's order (this fixture's default is the classic card row)
            GoldService.Instance.Wallet.DebugSet(5000);

            yield return RoundTamamla();
            Assert.AreEqual(UiContext.RoundUpgrade, UiNavigation.Layer);
            yield return new WaitForSecondsRealtime(0.4f);   // the kit's open: C1 140 ms, washes / title brush 280 ms, content 60 + 220 ms
            yield return null;

            UiBrushPane pane = upgrade.Pane;
            Assert.IsNotNull(pane, "taşıyıcı kitten (UiBrushKit.Pane)");
            Assert.IsTrue(pane.IsOpen, "pencere açık");
            Assert.AreEqual(UiBrushScrim.Wallpaper, pane.ScrimLevel, "KILAVUZ §3: Wallpaper perdesi");
            Assert.AreEqual(UiBrushTokens.ScrimWallpaper, pane.Scrim.color.a * pane.Scrim.canvasRenderer.GetAlpha(), 0.01f,
                "perde kitin doğrusal 0,71'inde (elle α yok)");
            // C2 yıkamaları pane kökünün doğrudan çocuklarıdır (UiBrushPane.AddWash: "Wash", "Wash.1", Content'in önünde).
            // Her UiButton da kendi hover katmanını "Wash" adıyla taşır (UiButton; düğümler, AL, İĞNELE, REROLL, DEVAM...):
            // o bir C2 değil, bu yüzden sayım alt ağaçta değil kökün kendi çocuklarında yapılır.
            int yikama = 0;
            foreach (Transform cocuk in pane.Root)
            {
                if (cocuk.gameObject.activeSelf && cocuk.name.StartsWith("Wash", StringComparison.Ordinal) && cocuk.GetComponent<Image>() != null)
                {
                    yikama++;
                }
            }

            Assert.AreEqual(UiBrushTokens.MaxWash, yikama, "ağaç + detay sütunu: iki C2 yıkama");
            Assert.IsNotNull(pane.Wash, "ilk C2 pane'in yıkaması (TextGroundAlpha onu okur)");
            Assert.AreSame(pane.Root, pane.Wash.transform.parent, "C2 pane kökünde");
            Assert.GreaterOrEqual(pane.TextGroundAlpha, UiBrushTokens.TextGroundMin, "metin zemini perde ⊕ yıkama ≥ 0,945");
            Assert.IsTrue(pane.Content.GetComponent<CanvasGroup>().interactable, "girdi açık");

            int baslik = 0, diger = 0;
            foreach (UiBrushMark iz in upgrade.GetComponentsInChildren<UiBrushMark>(false))
            {
                if (iz.Kind == UiBrushMarkKind.TitleStroke && iz.transform.IsChildOf(pane.Root)) baslik++;
                else if (iz.Kind == UiBrushMarkKind.TitleStroke) diger++;
            }

            Assert.AreEqual(1, baslik, "tek başlık fırçası (Menu.Upgrade başlığı)");
            Assert.AreEqual(0, diger);
            foreach (Transform t in upgrade.GetComponentsInChildren<Transform>(true))
            {
                Assert.IsFalse(t.name == "Petal" || t.name == "SkyVeil" || t.name == "GroundVeil" || t.name == "FootRule" || t.name == "HeaderOrnament",
                    "v2 süsü kalktı: " + t.name);
            }

            Assert.IsTrue(MetinVar<UpgradeScreen>(run.Shop.Gold.ToString(), out _), "gold bakiyesi ekranda yazılı");

            UiButton reroll = Buton<UpgradeScreen>("Reroll");
            UiButton devam = Buton<UpgradeScreen>("Continue");
            Assert.IsFalse(reroll.DrawsFocusRing, "REROLL kitin C11'i (odak = köşebent)");
            Assert.GreaterOrEqual(((RectTransform)reroll.transform).rect.height, UiTokens.TargetPreferred, "G6: REROLL isabeti ≥ 48");

            // G6: düğümler → AL / İĞNELE → REROLL → DEVAM.
            UiRunTreePanel agac = upgrade.GetComponentInChildren<UiRunTreePanel>(false);
            Assert.IsNotNull(agac, "ağaç modu");
            UiButton undo = Buton<UpgradeScreen>("Undo");
            Selectable altBant = undo.gameObject.activeInHierarchy ? (Selectable)undo : devam;
            Selectable cikis = reroll.IsInteractable() ? (Selectable)reroll : altBant;
            Selectable sonDetay = Canli(agac.ReforgeButton) ?? Canli(agac.PinButton) ?? Canli(agac.BuyButton);
            if (sonDetay != null)
            {
                Assert.AreSame(cikis, sonDetay.navigation.selectOnDown, "detayın son düğmesinden aşağı = " + cikis.name);
            }

            if (reroll.IsInteractable())
            {
                Selectable detay = Canli(agac.BuyButton) ?? Canli(agac.PinButton) ?? agac.FocusTarget();
                Assert.AreSame(detay, reroll.navigation.selectOnUp, "REROLL'den yukarı = detay (AL, yoksa İĞNELE, yoksa düğüm)");
            }

            if (reroll.IsInteractable())
            {
                Assert.AreSame(altBant, reroll.navigation.selectOnRight, "REROLL → UNDO / DEVAM");
            }

            Assert.IsTrue(devam.IsInteractable());

            // Kapanış: girdi anında kapanır, kitin solması ve cue'su pane'i, ardından tuvali kapatır.
            Canvas tuval = null;
            foreach (Canvas c in upgrade.GetComponentsInChildren<Canvas>(true))
            {
                if (c.GetComponent<CanvasGroup>() != null) { tuval = c; break; }
            }

            Assert.IsNotNull(tuval);
            run.CloseUpgradeScreen();
            yield return null;
            Assert.AreEqual(UiContext.None, UiNavigation.Layer, "shop kapandı");
            Assert.IsFalse(pane.IsOpen, "kapanışta girdi anında kapanır");
            yield return new WaitForSecondsRealtime(0.35f);
            yield return null;
            Assert.IsFalse(pane.Root.gameObject.activeSelf, "solma bitince pane kapandı (kitin cue'su)");
            Assert.IsFalse(tuval.gameObject.activeSelf, "pane kapanınca tuval de kapandı");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp098b.upgradeWindow=OK reroll=" + reroll.IsInteractable());
        }

        private static Selectable Canli(UiButton button)
        {
            return button != null && button.gameObject.activeInHierarchy && button.IsInteractable() ? button : null;
        }

        // ================================================================== 8 · ölüm ve pause

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PauseAcikkenOlum_ResultsGuvenli_DeadFazindaPauseReddedilir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            PauseScreen pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();

            pause.SetPaused(true);
            Assert.IsTrue(UiNavigation.IsPaused);
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            yield return Oldur();
            Assert.IsFalse(PauseScreen.CanOpen(), "Dead fazında pause açılamaz");
            yield return SahneyiBekle(PrototypeScenes.Results);
            float gecen = Time.realtimeSinceStartup - t0;
            Assert.Less(gecen, 4f, "pause altında ölüm: Results 1.6 s hand-off ile yüklenir (unscaled)");
            yield return SimulasyonSerbestOlmali("results-pause-altinda-olum");
            Assert.IsFalse(UiNavigation.IsPaused, "Results'ta pause overlay'i yok");
            Assert.IsFalse(UiNavigation.MayLayer(UiContext.Paused));
            Assert.AreEqual(0, AktifSayi<PauseScreen>());

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);

            // İkinci koşu: Dead fazında pause isteği reddedilir.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return KontrolluSaha();
            pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();
            yield return Oldur();
            pause.SetPaused(true);
            Assert.IsFalse(pause.IsPaused, "Dead fazında SetPaused(true) reddedilir");
            Assert.IsFalse(UiNavigation.IsPaused);
            Assert.IsFalse(RunFlowClock.HasHold(RunFlowClock.PauseHold));
            yield return SahneyiBekle(PrototypeScenes.Results);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.deathPause=OK");
        }

        // ================================================================== 9 · abort

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Abort_ResultsAcilmaz_ProfilYazilmaz_GecikmisResultsSizmaz()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            yield return OldurVeTopla(2);

            int runsOnce = ProfileService.Current.RunsPlayed;
            int killsOnce = ProfileService.Current.TotalKills;
            string diskOnce = System.IO.File.ReadAllText(TestProfileIsolation.TestProfilePath);
            int changed = 0;
            Action sayac = () => changed++;
            ProfileService.Changed += sayac;

            PauseScreen pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();
            pause.SetPaused(true);
            yield return null;

            // Pause → END RUN: B5 açık karar — mevcut davranış korunur (Results yok, profil yazılmaz).
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return SimulasyonSerbestOlmali("abort-menu");
            Assert.AreEqual(UiContext.MainMenu, UiNavigation.Base);
            Assert.IsFalse(UiNavigation.IsPaused, "pause overlay sahneyle gitti");

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2.5f)
            {
                Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "abort sonrası gecikmiş Results yüklenmemeli");
                yield return null;
            }

            ProfileService.Changed -= sayac;
            Assert.AreEqual(0, AktifSayi<ResultsScreen>());
            Assert.IsNull(ProfileService.LastReport, "abort raporu yok");
            Assert.AreEqual(0, changed, "abort profil değişikliği yayınlamaz (B5 açık)");
            Assert.AreEqual(runsOnce, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(killsOnce, ProfileService.Current.TotalKills);
            Assert.AreEqual(diskOnce, System.IO.File.ReadAllText(TestProfileIsolation.TestProfilePath), "abort diske yazmaz");

            // Hemen yeni koşu: eski koşunun durumu taşınmaz.
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.AreEqual("1", hud.Model.RoundText);
            Assert.AreEqual("0", hud.Model.GoldText, "yeni koşu eski gold'u göstermez");
            Assert.AreEqual("no upgrades", hud.Model.BuildText);
            Assert.AreEqual(UiContext.GameplayHud, UiNavigation.Current);
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.abort=OK");
        }

        // ================================================================== 10 · İngilizce

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TumKullaniciMetinleriIngilizce()
        {
            LogAssert.ignoreFailingMessages = true;
            var regex = new Regex("[çğıöşüÇĞİÖŞÜ]");
            var ihlaller = new List<string>();
            int toplam = 0;

            void Tara(string sahne)
            {
                TMP_Text[] metinler = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
                int n = 0;
                for (int i = 0; i < metinler.Length; i++)
                {
                    string m = metinler[i].text;
                    if (string.IsNullOrEmpty(m)) continue;
                    n++;
                    if (regex.IsMatch(m)) ihlaller.Add(sahne + ": " + m);
                }

                toplam += n;
                Debug.Log("[DD-TEST] text.scan scene=" + sahne + " labels=" + n);
            }

            yield return BootTanMainMenuye();
            yield return null;
            MainMenuScreen menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
            Tara("MainMenu/Profile");
            menu.ShowPane(UiContext.Statistics); Tara("MainMenu/Statistics");
            menu.ShowPane(UiContext.Achievements); Tara("MainMenu/Achievements");

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            Tara("Selection");

            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return KontrolluSaha();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            Tara("Run/HUD");
            yield return RoundTamamla();
            Tara("Run/Upgrade");
            PauseScreen pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();
            pause.SetPaused(true);
            yield return null;
            Tara("Run/Pause");
            pause.SetPaused(false);
            RunDirector.Instance.CloseUpgradeScreen();
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, FazZamanAsimi, "round 2 başlamadı");
            yield return Oldur();
            yield return SahneyiBekle(PrototypeScenes.Results);
            yield return null;
            Tara("Results");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);

            Assert.Greater(toplam, 150, "tarama boş");
            Assert.IsEmpty(ihlaller, "Türkçe karakterli kullanıcı metni:\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp005.english=OK labels=" + toplam);
        }

        // ================================================================== 11 · ikinci koşu tekillik

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator IkinciKosu_TekilPresenter_NavigationTemiz()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            Dictionary<string, int> run1 = AboneAnlikGoruntu();
            yield return Oldur();
            yield return SahneyiBekle(PrototypeScenes.Results);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            Dictionary<string, int> run2 = AboneAnlikGoruntu();

            AboneleriKarsilastir("run2 = run1 (WP-005 presenter'ları birikmedi)", run1, run2);
            Assert.AreEqual(1, AktifSayi<RunHudScreen>());
            Assert.AreEqual(1, AktifSayi<UpgradeScreen>());
            Assert.AreEqual(1, AktifSayi<PauseScreen>());
            Assert.AreEqual(1, AktifSayi<SelectedSkinApplier>());
            Assert.AreEqual(UiContext.GameplayHud, UiNavigation.Current, "yeni koşu: katman/overlay yok");
            Assert.IsFalse(UiNavigation.IsPaused);

            RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
            Assert.AreEqual("1", hud.Model.RoundText);
            Assert.AreEqual("0", hud.Model.GoldText);

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp005.secondRun=OK");
        }
    }
}
