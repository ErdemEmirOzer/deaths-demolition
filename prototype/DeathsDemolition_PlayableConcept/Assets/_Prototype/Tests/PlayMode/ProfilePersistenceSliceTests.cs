using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-009 · Profil kalıcılığı dilimi (gerçek akış, izole test profili).
    ///
    ///   P1 duplicate RunEnded + Results yeniden açma + scene rebind → tek commit (§8.2)
    ///   P2 abort: RAM/disk hash, XP, stat, unlock, achievement, Results durumu → sıfır delta (§7, §8.6)
    ///   P3 güç eşitliği: L1 ile yüksek seviye aynı başlangıç health/damage/gold (§2.3, §8.5)
    ///   P4 iki ardışık koşu + "yeniden açılış": session RunId kimlik değildir (§A, §8.2)
    ///   P5 gerçek akışta save başarısızlığı: Results dürüst, canlı profil = disk (§B, §12)
    ///
    /// Profil yolu TestProfileIsolation ile geçici önbelleğe yönlendirilmiştir; gerçek profil
    /// dosyasına dokunulmaz (ProfileIsolationTests nöbetçisi ayrıca ölçer).
    /// </summary>
    [TestFixture]
    [Category("WP009")]
    public sealed class ProfilePersistenceSliceTests
    {
        private const float KurulumZamanAsimi = 25f;

        private float _eskiTimeScale;
        private int _changed;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            _changed = 0;
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            ProfileStore.SaveStageHook = null;
            ProfileService.Changed += Sayac;
        }

        [TearDown]
        public void TearDown()
        {
            ProfileService.Changed -= Sayac;
            ProfileStore.SaveStageHook = null;
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
            LogAssert.ignoreFailingMessages = false;
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
        }

        private void Sayac()
        {
            _changed++;
        }

        // ------------------------------------------------------------------ yardımcılar

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

        private static IEnumerator YeniKosu()
        {
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
        }

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

        private static void SpawnAyarla(bool acik)
        {
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "EnemySpawnDirector._spawningEnabled alanı bulunamadı.");
            f.SetValue(EnemySpawnDirector.Instance, acik);
        }

        private static IEnumerator Oldur(int adet)
        {
            Vector3 merkez = PlayerRegistry.Primary.transform.position;
            for (int i = 0; i < adet; i++)
            {
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, RunDirector.Instance.Round, merkez + new Vector3(14f + i * 1.2f, 0f, 3f), Quaternion.identity);
                Assert.IsNotNull(z, "Test zombisi doğmadı.");
                z.Kill();
                yield return null;
            }

            yield return null;
        }

        /// <summary>Bir zırhsız Standard doğurur, silah runtime'ının ürettiği gerçek atışı ona ateşler, HP farkını verir.</summary>
        private static IEnumerator AtisHasari(Action<float> sonuc)
        {
            Vector3 merkez = PlayerRegistry.Primary.transform.position;
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, RunDirector.Instance.Round, merkez + new Vector3(0f, 0f, 6f), Quaternion.identity);
            Assert.IsNotNull(z, "hedef zombi doğmadı");
            z.FitArmorComposition(false, false, false);
            // WP-063J: sıra bağımlılığı. DEC-081'den beri kameranın görmediği spawn yerden KALKAR (hitbox'lar
            // yatık), ve kameranın ne gördüğü önceki testlerin bıraktığı duruma bağlı; üstelik
            // autoSyncTransforms kapalı, havuzdan gelen gövdenin collider'ı ilk karelerde eski yerinde olabilir.
            // Tam süitte ikinci koşunun atışı böylece hiçbir şeye değmiyordu (hpDelta 0). Kalkışı bitir, poza iki
            // kare ver, fiziği eşitle ve gövdenin kendi üst-gövde hitbox'ına nişan al — iki koşu aynı hedefi vurur.
            z.CompleteRise();
            for (int i = 0; i < 180 && z.IsRising; i++) yield return null;   // zamanlayıcı + AP-009 kalkış klibi
            Assert.IsFalse(z.IsRising, "hedef kalkışı bitirmedi");
            for (int i = 0; i < 30; i++) yield return null;                   // kalkış → ayakta poz karışımı (~0,5 s @ 1/60)
            Physics.SyncTransforms();
            float once = z.Health;
            Vector3 hedef = z.HitboxFor(DD.Prototype.Damage.BodyRegion.UpperBody).bounds.center;
            Vector3 kaynak = hedef + Vector3.back * 2.5f;
            ShotRequest atis = WeaponRegistry.ActiveController.Classic.BuildShot(kaynak, (hedef - kaynak).normalized);
            atis.Direction = (hedef - kaynak).normalized; // spread'siz, deterministik
            atis.DrawTracer = false;
            HitscanEngine.Fire(in atis);
            yield return null;
            float sonra = z.Health;
            z.Remove();
            yield return null;
            sonuc(once - sonra);
        }

        private static IEnumerator OyuncuyuOldurVeResultsBekle()
        {
            RunDirector director = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = false;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp009-death");
            yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2f)
            {
                yield return null;
            }
        }

        private static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes));
            }
        }

        private static string RamHash()
        {
            return Sha(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(ProfileService.Current)));
        }

        private static string DiskHash()
        {
            return File.Exists(ProfileStore.Path) ? Sha(File.ReadAllBytes(ProfileStore.Path)) : "MISSING";
        }

        /// <summary>
        /// WP-063J: diskteki profilin koşu/commit içeriği — WP-035 "görüldü" sayaçları (AchievementsSeen /
        /// ContentSeen) ve o yazımın LastSavedUtc damgası hariç. Ana menünün Profil/Başarımlar paneli yeni açılan içeriği görüldü sayıp bunu
        /// diske yazar (meşru kozmetik yazım, commit değil). Taze profilde ilk koşu yeni içerik açtığı için P1
        /// izole koşuda bu yazımı görür, tam süitte (içerik önceden görülmüş) görmez → sıra bağımlılığı.
        /// </summary>
        private static string DiskCommitHash()
        {
            return Sha(System.Text.Encoding.UTF8.GetBytes(DiskCommitJson()));
        }

        private static string DiskCommitJson()
        {
            ProfileData d = Disk();
            d.AchievementsSeen = 0;
            d.ContentSeen = 0;
            d.LastSavedUtc = string.Empty;   // o yazımın zaman damgası
            return JsonUtility.ToJson(d, true);
        }

        private static string JsonFarki(string once, string sonra)
        {
            string[] a = once.Split('\n'), b = sonra.Split('\n');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].Trim() : "∅", y = i < b.Length ? b[i].Trim() : "∅";
                if (x != y) sb.Append(" [").Append(x).Append(" → ").Append(y).Append(']');
            }
            return sb.ToString();
        }

        private static ProfileData Disk()
        {
            ProfileData d;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(ProfileStore.Path, out d), "disk profili okunmalı");
            return d;
        }

        // ================================================================== P1

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator P1_DuplicateRunEnded_ResultsYenidenAcma_SceneRebind_TekCommit()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            yield return Oldur(3);

            int runsOnce = ProfileService.Current.RunsPlayed;
            int killsOnce = ProfileService.Current.TotalKills;
            int xpOnce = ProfileService.Current.TotalXpEarned;
            int eventsOnce = ProfileRunCommitter.EventsSeen;
            int commitsOnce = ProfileRunCommitter.Commits;
            _changed = 0;

            yield return OyuncuyuOldurVeResultsBekle();

            RunRewardReport makbuz = ProfileService.LastReport;
            Assert.IsNotNull(makbuz);
            Assert.IsTrue(makbuz.Applied, "gerçek ölüm → transaction uygulandı");
            Assert.IsNotEmpty(makbuz.CompletionId, "koşu kalıcı kimlik taşır");
            Assert.AreEqual(RunEvents.LastRunSummary.CompletionId, makbuz.CompletionId);
            Assert.AreEqual(1, _changed, "Changed tam bir kez");
            Assert.AreEqual(eventsOnce + 1, ProfileRunCommitter.EventsSeen);
            Assert.AreEqual(commitsOnce + 1, ProfileRunCommitter.Commits);
            Assert.AreEqual(runsOnce + 1, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(killsOnce + 3, ProfileService.Current.TotalKills);
            Assert.AreEqual(xpOnce + makbuz.TotalXpEarned, ProfileService.Current.TotalXpEarned);
            Assert.AreEqual(CompletionTokenVerdict.Valid, ProfileService.LastVerdict);
            Assert.Greater(ProfileService.Current.LastAppliedCompletionOrdinal, 0, "ordinal ilerledi");
            Assert.AreEqual(runsOnce + 1, Disk().RunsPlayed, "disk +1");
            string diskHash = DiskHash();
            string diskCommitHash = DiskCommitHash();
            string diskCommitJson = DiskCommitJson();

            // Duplicate RunEnded (aynı özet, aynı kimlik): tek commit.
            RunEvents.PublishRunEnded(RunEvents.LastRunSummary);
            yield return null;
            Assert.AreEqual(eventsOnce + 2, ProfileRunCommitter.EventsSeen, "olay committer'a ulaştı");
            Assert.AreEqual(commitsOnce + 1, ProfileRunCommitter.Commits, "ikinci commit YOK");
            Assert.AreEqual(1, _changed, "duplicate Changed yayınlamaz");
            Assert.AreEqual(runsOnce + 1, ProfileService.Current.RunsPlayed);
            Assert.AreSame(makbuz, ProfileService.LastReport, "orijinal makbuz korunur");
            Assert.AreEqual(diskHash, DiskHash(), "disk değişmedi");

            // Results yeniden açılır: makbuz okunur, commit yok.
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            PrototypeUiFlow.GoToResults();
            yield return SahneyiBekle(PrototypeScenes.Results);
            yield return null;
            ResultsScreen ekran = UnityEngine.Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(ekran);
            Assert.IsTrue(ekran.Model.HasRun);
            Assert.IsTrue(ekran.Model.Applied, "yeniden açılan Results aynı uygulanmış makbuzu gösterir");
            StringAssert.Contains("already saved", ekran.Model.FooterNote);
            Assert.AreEqual(1, _changed);
            Assert.AreEqual(commitsOnce + 1, ProfileRunCommitter.Commits);

            // Scene rebind + geç gelen duplicate: yine commit yok.
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            RunEvents.PublishRunEnded(RunEvents.LastRunSummary);
            yield return null;
            Assert.AreEqual(commitsOnce + 1, ProfileRunCommitter.Commits);
            Assert.AreEqual(1, _changed);
            Assert.AreEqual(runsOnce + 1, Disk().RunsPlayed);
            // Ana menü ziyaretleri "görüldü" sayaçlarını yazabilir (WP-035); koşu/commit içeriği birebir aynı kalmalı.
            Assert.AreEqual(diskCommitHash, DiskCommitHash(), "geç duplicate diskteki koşu içeriğini değiştirmedi: " + JsonFarki(diskCommitJson, DiskCommitJson()));
            Debug.Log("[DD-TEST] wp009.P1=OK id=" + makbuz.CompletionId + " events=" + (ProfileRunCommitter.EventsSeen - eventsOnce) + " commits=" + (ProfileRunCommitter.Commits - commitsOnce));
        }

        // ================================================================== P2

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator P2_Abort_SifirProfilDeltasi_RamVeDiskHash()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            yield return Oldur(2);

            ProfileData once = ProfileService.Current.Clone();
            Assert.IsTrue(File.Exists(ProfileStore.Path), "izole profil dosyası diskte olmalı (hash karşılaştırması boş kalmasın)");
            string ramOnce = RamHash();
            string diskOnce = DiskHash();
            int eventsOnce = ProfileRunCommitter.EventsSeen;
            _changed = 0;
            Assert.IsNull(ProfileService.LastReport, "koşu içinde makbuz yok");

            PauseScreen pause = UnityEngine.Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause);
            pause.SetPaused(true);
            yield return null;
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2.5f)
            {
                Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "abort sonrası Results yüklenmemeli");
                yield return null;
            }

            Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<ResultsScreen>().Length, "Results açılmaz");
            Assert.IsNull(ProfileService.LastReport, "LastReport oluşmaz");
            Assert.AreEqual(0, _changed, "Profile Changed yayınlanmaz");
            Assert.AreEqual(eventsOnce, ProfileRunCommitter.EventsSeen, "RunEnded committer'a ulaşmaz (ApplyRun çağrılmaz)");
            Assert.AreEqual(ramOnce, RamHash(), "RAM profil hash'i aynı");
            Assert.AreEqual(diskOnce, DiskHash(), "disk profil hash'i aynı");
            ProfileData sonra = ProfileService.Current;
            Assert.AreEqual(once.RunsPlayed, sonra.RunsPlayed, "Runs Played artmaz");
            Assert.AreEqual(once.TotalXpEarned, sonra.TotalXpEarned);
            Assert.AreEqual(once.XpIntoLevel, sonra.XpIntoLevel);
            Assert.AreEqual(once.ProfileLevel, sonra.ProfileLevel);
            Assert.AreEqual(once.TotalKills, sonra.TotalKills);
            Assert.AreEqual(once.StandardKills, sonra.StandardKills);
            Assert.AreEqual(once.UnlockedWeapons.Count, sonra.UnlockedWeapons.Count);
            Assert.AreEqual(once.UnlockedBranches.Count, sonra.UnlockedBranches.Count);
            Assert.AreEqual(once.Achievements.Count, sonra.Achievements.Count);
            Assert.AreEqual(once.LastAppliedCompletionOrdinal, sonra.LastAppliedCompletionOrdinal, "abort ordinal'i ilerletmez");
            Assert.AreEqual(once.ProfileLineageId, sonra.ProfileLineageId);
            Assert.IsFalse(CompletionAuthority.HasLiveCapability, "abort canlı yetkiyi geçersiz kılar");
            Debug.Log("[DD-TEST] wp009.P2=OK abort delta=0 ram=" + ramOnce.Substring(0, 12) + " disk=" + diskOnce.Substring(0, 12));
        }

        // ================================================================== P3

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator P3_GucEsitligi_L1_ve_YuksekSeviye_AyniBaslangic()
        {
            LogAssert.ignoreFailingMessages = true;
            ProfileService.ResetProfile();
            yield return KosuyaGir();
            Assert.AreEqual(1, ProfileService.Current.ProfileLevel, "taze profil L1");
            float healthMax1 = PlayerRegistry.Primary.Health.Max;
            float health1 = PlayerRegistry.Primary.Health.Current;
            ClassicWeaponSettings silah1 = PrototypeConfig.Active.Weapons.Find(WeaponRegistry.SelectedWeapon);
            float damage1 = WeaponRegistry.ActiveController.Classic.BuildShot(Vector3.zero, Vector3.forward).Damage;
            float fireRate1 = WeaponRegistry.ActiveController.Classic.EffectiveFireRate;
            int tiers1 = WeaponRegistry.ActiveController.Modifiers.TotalTiers;
            int gold1 = GoldService.Instance.Wallet.Balance;
            yield return KontrolluSaha();
            float hpDelta1 = 0f;
            yield return AtisHasari(v => hpDelta1 = v);

            // Koşudan profil yazmadan çık (abort), profili yüksek seviyeye çıkar (izole dosya).
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            RunRewardReport r = ProfileService.ApplyRun(new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(), ReachedRound = 12, CompletedRounds = 11,
                CompletedRoundXp = 3000, KillXp = 400, TotalKills = 200, StandardKills = 200, EndReason = RunEndReason.PlayerDeath
            });
            Assert.IsTrue(r.Applied);
            Assert.GreaterOrEqual(ProfileService.Current.ProfileLevel, 4, "yüksek seviye kurulumu");

            yield return YeniKosu();
            Assert.AreEqual(WeaponRegistry.SelectedWeapon, silah1.Id, "aynı silah");
            Assert.AreEqual(healthMax1, PlayerRegistry.Primary.Health.Max, 0.0001f, "starting health (max) eşit");
            Assert.AreEqual(health1, PlayerRegistry.Primary.Health.Current, 0.0001f, "starting health eşit");
            Assert.AreEqual(damage1, WeaponRegistry.ActiveController.Classic.BuildShot(Vector3.zero, Vector3.forward).Damage, 0.0001f,
                "starting damage eşit (silah runtime'ının ürettiği atış hasarı)");
            Assert.AreEqual(fireRate1, WeaponRegistry.ActiveController.Classic.EffectiveFireRate, 0.0001f, "fire rate eşit");
            Assert.AreEqual(tiers1, WeaponRegistry.ActiveController.Modifiers.TotalTiers, "build stok (0 tier)");
            Assert.AreEqual(0, tiers1);
            Assert.AreEqual(gold1, GoldService.Instance.Wallet.Balance, "starting gold eşit");
            Assert.AreEqual(0, gold1);
            yield return KontrolluSaha();
            float hpDelta2 = 0f;
            yield return AtisHasari(v => hpDelta2 = v);
            Assert.Greater(hpDelta1, 0f, "L1 atışı hedefe hasar verdi (ölçüm boş değil)");
            Assert.AreEqual(hpDelta1, hpDelta2, 0.0001f, "aynı gerçek atış aynı düşmana aynı hasarı verir (L1 = yüksek seviye)");
            Debug.Log("[DD-TEST] wp009.P3=OK level=" + ProfileService.Current.ProfileLevel + " health=" + healthMax1 + " shotDamage=" + damage1 + " hpDelta=" + hpDelta1 + "/" + hpDelta2 + " gold=" + gold1);

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== P4

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator P4_IkiArdisikKosu_FarkliKimlik_SessionRunIdKimlikDegildir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            int runsOnce = ProfileService.Current.RunsPlayed;
            long ordOnce = ProfileService.Current.LastAppliedCompletionOrdinal;
            int runId1 = RunDirector.Instance.RunId;
            string kimlik1 = RunDirector.Instance.CompletionId;
            Assert.IsNotEmpty(kimlik1);
            yield return OyuncuyuOldurVeResultsBekle();
            Assert.AreEqual(kimlik1, ProfileService.LastReport.CompletionId);
            Assert.IsTrue(ProfileService.LastReport.Applied);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return YeniKosu();
            int runId2 = RunDirector.Instance.RunId;
            string kimlik2 = RunDirector.Instance.CompletionId;
            Assert.AreNotEqual(runId1, runId2, "oturum içi RunId artar");
            Assert.AreNotEqual(kimlik1, kimlik2, "kimlik her koşuda farklı");
            yield return OyuncuyuOldurVeResultsBekle();
            Assert.IsTrue(ProfileService.LastReport.Applied);
            Assert.AreEqual(runsOnce + 2, ProfileService.Current.RunsPlayed, "iki ayrı koşu");

            // "Uygulama yeniden açıldı": statikler sıfırlanır, disk kalır. Sahne yeniden yüklemesi
            // domain'i sıfırlamaz; RunDirector'ın oturum serisi (SubsystemRegistration'da sıfırlanan
            // s_runSerial) burada yeniden açılıştaki gibi elle sıfırlanır — gerçek süreç sınırı
            // ProfilePersistenceProbe faz B'de ölçülür.
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            yield return BootTanMainMenuye();
            FieldInfo seri = typeof(RunDirector).GetField("s_runSerial", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(seri, "RunDirector.s_runSerial bulunamadı");
            seri.SetValue(null, runId1 - 1);
            Assert.IsNull(ProfileService.LastReport, "bellek boşaltıldı; profil diskten yeniden yüklenecek");
            Assert.AreEqual(runsOnce + 2, ProfileService.Current.RunsPlayed, "diskten geri geldi");
            Assert.AreEqual(ordOnce + 2, ProfileService.Current.LastAppliedCompletionOrdinal, "iki koşu → ordinal +2");
            yield return YeniKosu();
            int runId3 = RunDirector.Instance.RunId;
            string kimlik3 = RunDirector.Instance.CompletionId;
            Assert.AreEqual(runId1, runId3, "session RunId yeniden 1'den başlar → tek başına kimlik OLAMAZ");
            Assert.AreNotEqual(kimlik1, kimlik3, "kalıcı kimlik ise yine farklı");
            yield return OyuncuyuOldurVeResultsBekle();
            Assert.IsTrue(ProfileService.LastReport.Applied, "aynı RunId'li üçüncü koşu ayrı koşu olarak uygulanır");
            Assert.AreEqual(runsOnce + 3, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(runsOnce + 3, Disk().RunsPlayed);
            Debug.Log("[DD-TEST] wp009.P4=OK runIds=" + runId1 + "," + runId2 + "," + runId3 + " ids distinct=" + (kimlik1 != kimlik2 && kimlik2 != kimlik3));

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== P5

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator P5_GercekAkistaSaveBasarisiz_ResultsDurust_CanliDiskeEsit()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            yield return Oldur(1);

            int runsOnce = ProfileService.Current.RunsPlayed;
            string ramOnce = RamHash();
            string diskOnce = DiskHash();
            _changed = 0;
            ProfileStore.SaveStageHook = s =>
            {
                if (s == ProfileSaveStage.Replace)
                {
                    throw new IOException("wp009 injected replace fault");
                }
            };

            yield return OyuncuyuOldurVeResultsBekle();
            ProfileStore.SaveStageHook = null;

            RunRewardReport makbuz = ProfileService.LastReport;
            Assert.IsNotNull(makbuz);
            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, makbuz.Outcome);
            Assert.IsFalse(makbuz.Applied);
            Assert.AreEqual(makbuz.LevelBefore, makbuz.LevelAfter);
            Assert.AreEqual(0, makbuz.NewUnlocks.Count);
            Assert.AreEqual(0, _changed, "başarısız save Changed yayınlamaz");
            Assert.AreEqual(runsOnce, ProfileService.Current.RunsPlayed, "canlı profil ilerlemedi");
            Assert.AreEqual(ramOnce, RamHash(), "RAM = önceki");
            Assert.AreEqual(diskOnce, DiskHash(), "disk = önceki (ayrışma yok)");

            ResultsScreen ekran = UnityEngine.Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(ekran);
            Assert.IsTrue(ekran.Model.HasRun);
            Assert.IsFalse(ekran.Model.Applied, "Results kaydedildi demez");
            Assert.IsTrue(ekran.Model.SaveFailed);
            StringAssert.Contains("could not be written", ekran.Model.FooterNote);
            StringAssert.Contains("unchanged", ekran.Model.FooterNote);
            Assert.IsFalse(ekran.Model.Celebrate);

            // Güvenli retry (aynı kimlik, hata yok): tam bir kez uygulanır.
            RunRewardReport tekrar = ProfileService.ApplyRun(makbuz.Summary);
            Assert.IsTrue(tekrar.Applied, "retry uygular");
            Assert.AreEqual(runsOnce + 1, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(runsOnce + 1, Disk().RunsPlayed);
            Assert.AreEqual(1, _changed);
            Assert.AreSame(tekrar, ProfileService.ApplyRun(makbuz.Summary), "ikinci retry duplicate");
            Assert.AreEqual(runsOnce + 1, ProfileService.Current.RunsPlayed);
            Debug.Log("[DD-TEST] wp009.P5=OK outcome=" + makbuz.Outcome + " retryApplied=" + tekrar.Applied);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        // ================================================================== P6 (WP-009H)

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator P6_DokuzGercekKosu_IlkReplayReddi_AbortTokenOlur_ResetLineage()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            int runsOnce = ProfileService.Current.RunsPlayed;
            long ordOnce = ProfileService.Current.LastAppliedCompletionOrdinal;
            string soy = ProfileService.Current.ProfileLineageId;
            RunSummary ilk = default;
            for (int k = 0; k < 9; k++)
            {
                if (k > 0) yield return YeniKosu();
                string token = RunDirector.Instance.CompletionId;
                Assert.IsNotNull(token);
                yield return OyuncuyuOldurVeResultsBekle();
                Assert.IsTrue(ProfileService.LastReport.Applied, "koşu " + k);
                Assert.AreEqual(token, ProfileService.LastReport.CompletionId);
                if (k == 0) ilk = RunEvents.LastRunSummary;
                PrototypeUiFlow.GoToMainMenu();
                yield return SahneyiBekle(PrototypeScenes.MainMenu);
            }

            Assert.AreEqual(runsOnce + 9, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(ordOnce + 9, ProfileService.Current.LastAppliedCompletionOrdinal);

            // İlk koşu (9 koşu önce) aynı süreçte, sonra statikler boşaltılıp diskten yükleme sonrası: ret.
            string ram = RamHash(); string disk = DiskHash(); _changed = 0;
            RunRewardReport r = ProfileRunCommitter.Commit(ilk);
            Assert.IsFalse(r.Applied);
            Assert.AreEqual(ProfileCommitOutcome.Duplicate, r.Outcome);
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, ProfileService.LastVerdict);
            Assert.AreEqual(ram, RamHash()); Assert.AreEqual(disk, DiskHash()); Assert.AreEqual(0, _changed);
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            Assert.AreEqual(runsOnce + 9, ProfileService.Current.RunsPlayed, "diskten");
            r = ProfileRunCommitter.Commit(ilk);
            Assert.AreEqual(ProfileCommitOutcome.Duplicate, r.Outcome, "reload sonrası da ret");
            Assert.AreEqual(disk, DiskHash()); Assert.AreEqual(0, _changed);

            // Abort: canlı yetki ölür; token sonradan uygulanamaz; profil delta 0.
            yield return YeniKosu();
            string abortToken = RunDirector.Instance.CompletionId;
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Assert.IsFalse(CompletionAuthority.HasLiveCapability);
            RunSummary abortSum = ilk; abortSum.CompletionId = abortToken;
            r = ProfileRunCommitter.Commit(abortSum);
            Assert.AreEqual(ProfileCommitOutcome.InvalidIdentity, r.Outcome);
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, ProfileService.LastVerdict);
            Assert.AreEqual(disk, DiskHash()); Assert.AreEqual(0, _changed);

            // Reset: yeni lineage; reset öncesi token yabancı.
            yield return YeniKosu();
            string preReset = RunDirector.Instance.CompletionId;
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            ProfileService.ResetProfile();
            Assert.AreNotEqual(soy, ProfileService.Current.ProfileLineageId);
            Assert.AreEqual(0, ProfileService.Current.LastAppliedCompletionOrdinal);
            Assert.AreEqual(0, ProfileService.Current.RunsPlayed);
            RunSummary preSum = ilk; preSum.CompletionId = preReset;
            r = ProfileRunCommitter.Commit(preSum);
            Assert.AreEqual(ProfileCommitOutcome.InvalidIdentity, r.Outcome);
            Assert.AreEqual(CompletionTokenVerdict.ForeignLineage, ProfileService.LastVerdict);
            Assert.AreEqual(0, ProfileService.Current.RunsPlayed);

            // Sonraki koşu normal başlar ve uygulanır (ordinal 1).
            yield return YeniKosu();
            yield return OyuncuyuOldurVeResultsBekle();
            Assert.IsTrue(ProfileService.LastReport.Applied);
            Assert.AreEqual(1, ProfileService.Current.LastAppliedCompletionOrdinal);
            Debug.Log("[DD-TEST] wp009h.P6=OK nineRuns replayRefused(sameProcess+reload) abortTokenNotIssued resetForeignLineage");
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }
    }
}
