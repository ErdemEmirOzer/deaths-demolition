using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-009 · Profil transaction sınırı ve kalıcı exactly-once (ProfileService, izole dosya).
    ///
    /// Kilitlenen kurallar (§A, §B, §4.1, §4.4, §4.6, §8.2, §8.4): aynı completion identity aynı
    /// süreçte ve diskten yeniden yüklendikten sonra ikinci kez uygulanmaz; aynı verili farklı
    /// kimlikler iki ayrı koşudur; kimliksiz özet hiçbir şeyi değiştirmez; save başarısızlığında
    /// canlı profil = disk (ayrışma yok), Changed yayınlanmaz, aynı kimlikle retry tam bir kez
    /// uygular; kazanılmış entitlement config eşiği yükselse de ve reload sonrası açık kalır;
    /// bilinmeyen id içerik açmaz; unlock/achievement "new" yalnız ilk başarılı transaction'da.
    /// Gerçek profil dosyasına ASLA dokunulmaz (fixture yolu yönlendirir, hash karşılaştırır).
    /// </summary>
    [TestFixture]
    [Category("WP009")]
    public sealed class ProfileTransactionTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-editmode-wp009-tx", "prototype-profile.json");

        private static readonly string RealPath =
            Path.Combine(Application.persistentDataPath, ProfileStore.FileName);

        private string _realHashBefore;
        private int _changed;
        private PrototypeConfig _eskiConfig;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
            Directory.CreateDirectory(Path.GetDirectoryName(TestPath));
            ProfileStore.SetPathOverride(TestPath);
            OnbellegiBosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            OnbellegiBosalt();
            ProfileStore.SetPathOverride(null);
            TryDeleteDir(Path.GetDirectoryName(TestPath));
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path, "override kayboldu — gerçek profile yazılabilir");
            _eskiConfig = PrototypeConfig.Active;
            ProfileStore.SaveStageHook = null;
            ProfileService.ResetProfile();
            _changed = 0;
            ProfileService.Changed += Sayac;
        }

        [TearDown]
        public void TearDown()
        {
            ProfileService.Changed -= Sayac;
            ProfileStore.SaveStageHook = null;
            PrototypeConfig.SetActive(_eskiConfig);
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            if (ProfileStore.SaveLocked)
            {
                // Bir test kilitli durumda (future-version dosyası) düşmüş olsa bile sonraki test taze başlar.
                ProfileStore.SetPathOverride(TestPath);
                TryDelete(TestPath);
                OnbellegiBosalt();
            }
        }

        private void Sayac()
        {
            _changed++;
        }

        // ------------------------------------------------------------------ yardımcılar

        private static void OnbellegiBosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        /// <summary>"Uygulama yeniden açıldı": statikler temizlenir, profil diskten yeniden yüklenir.</summary>
        private static void YenidenYukle()
        {
            OnbellegiBosalt();
            ProfileService.EnsureLoaded();
        }

        private static string HashOf(string path)
        {
            if (!File.Exists(path))
            {
                return "MISSING";
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
            }
        }

        private static string Parmakizi(ProfileData p)
        {
            return JsonUtility.ToJson(p);
        }

        private static string DiskParmakizi()
        {
            ProfileData d;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out d));
            return Parmakizi(d);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }

        private static void TryDeleteDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }

        private static RunSummary Ozet(string id, int reached, int completed, int kills, int killXp, int roundXp)
        {
            return new RunSummary
            {
                CompletionId = id,
                ReachedRound = reached,
                CompletedRounds = completed,
                TotalKills = kills,
                StandardKills = kills,
                KillXp = killXp,
                CompletedRoundXp = roundXp,
                TotalXpEarned = killXp + roundXp,
                Weapon = ClassicWeaponId.AssaultRifle,
                BuildDescription = "P1",
                EndReason = RunEndReason.PlayerDeath
            };
        }

        /// <summary>WP-009H: kimlik yalnız yetkili authority'den (dev/Editor dikişi = üretim issue yolu).</summary>
        private static string Kimlik()
        {
            return CompletionAuthority.TestSeam.IssueForNextRun();
        }

        // ------------------------------------------------------------------ exactly-once

        [Test]
        public void AyniKimlik_AyniSurecte_IkinciKez_HicbirDeltaYok()
        {
            RunSummary o = Ozet(Kimlik(), 3, 2, 8, 8, 45);
            RunRewardReport r1 = ProfileService.ApplyRun(o);
            Assert.IsTrue(r1.Applied);
            Assert.AreEqual(ProfileCommitOutcome.Applied, r1.Outcome);
            string bellek = Parmakizi(ProfileService.Current);
            string disk = DiskParmakizi();
            Assert.AreEqual(bellek, disk, "başarılı transaction sonrası canlı profil = disk");
            Assert.AreEqual(1, _changed);

            RunRewardReport r2 = ProfileService.ApplyRun(o);
            Assert.AreSame(r1, r2, "aynı kimlik → orijinal makbuz");
            Assert.AreEqual(bellek, Parmakizi(ProfileService.Current), "XP/kill/round/RunsPlayed hiçbiri artmadı");
            Assert.AreEqual(disk, DiskParmakizi(), "disk de değişmedi");
            Assert.AreEqual(1, _changed, "duplicate Changed yayınlamaz");
        }

        [Test]
        public void AyniKimlik_YenidenYuklemeSonrasi_Replay_Reddedilir()
        {
            RunSummary o = Ozet(Kimlik(), 2, 1, 5, 5, 20);
            ProfileService.ApplyRun(o);
            string disk = DiskParmakizi();
            int runs = ProfileService.Current.RunsPlayed;

            YenidenYukle();
            int changedOnce = _changed;
            Assert.IsNull(ProfileService.LastReport, "yeniden açılışta bellekte makbuz yok");
            Assert.AreEqual(runs, ProfileService.Current.RunsPlayed, "diskten aynı profil geldi");
            Assert.AreEqual(1, ProfileService.Current.LastAppliedCompletionOrdinal, "ordinal diske yazılmış");

            RunRewardReport r = ProfileService.ApplyRun(o);
            Assert.AreEqual(ProfileCommitOutcome.Duplicate, r.Outcome);
            Assert.IsFalse(r.Applied);
            Assert.AreEqual(0, r.NewUnlocks.Count);
            Assert.AreEqual(0, r.NewAchievements.Count);
            Assert.AreEqual(r.LevelBefore, r.LevelAfter, "duplicate makbuzunda önce = sonra");
            Assert.AreEqual(runs, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(disk, DiskParmakizi(), "replay diske dokunmaz");
            Assert.AreEqual(changedOnce, _changed, "replay Changed yayınlamaz");
            Assert.AreSame(r, ProfileService.LastReport, "yeniden açılış sonrası bellekte makbuz yokken dürüst 'already recorded' makbuzu Results için saklanır");

            // Farklı bir koşunun makbuzu asla eski bir replay ile ezilmez.
            RunRewardReport yeni = ProfileService.ApplyRun(Ozet(Kimlik(), 1, 0, 1, 1, 0));
            Assert.IsTrue(yeni.Applied);
            RunRewardReport eskiReplay = ProfileService.ApplyRun(o);
            Assert.AreEqual(ProfileCommitOutcome.Duplicate, eskiReplay.Outcome);
            Assert.AreSame(yeni, ProfileService.LastReport, "LastReport son gerçek transaction'ın makbuzu kalır");
        }

        [Test]
        public void AyniVeri_FarkliKimlik_IkiAyriKosu()
        {
            // Tek aktif koşu: ikinci yetki ancak birincisi uygulandıktan sonra verilir (aynı içerik).
            RunSummary a = Ozet(Kimlik(), 2, 1, 5, 5, 20);
            Assert.IsTrue(ProfileService.ApplyRun(a).Applied);
            RunSummary b = a;
            b.CompletionId = Kimlik();
            Assert.AreNotEqual(a.CompletionId, b.CompletionId);
            Assert.IsTrue(ProfileService.ApplyRun(b).Applied);
            Assert.AreEqual(2, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(10, ProfileService.Current.TotalKills);
            Assert.AreEqual(50, ProfileService.Current.TotalXpEarned);
            Assert.AreEqual(2, _changed);
        }

        [Test]
        public void KimliksizOzet_HicbirSeyiDegistirmez()
        {
            string bellek = Parmakizi(ProfileService.Current);
            string disk = DiskParmakizi();
            RunSummary o = Ozet(null, 5, 4, 30, 30, 200);
            RunRewardReport r = ProfileService.ApplyRun(o);
            Assert.AreEqual(ProfileCommitOutcome.MissingIdentity, r.Outcome);
            Assert.IsFalse(r.Applied);
            Assert.IsTrue(r.HasRun);
            Assert.AreEqual(230, r.TotalXpEarned, "makbuz koşuyu anlatır ama profil değişmez");
            Assert.AreEqual(bellek, Parmakizi(ProfileService.Current));
            Assert.AreEqual(disk, DiskParmakizi());
            Assert.AreEqual(0, _changed);
            Assert.AreSame(r, ProfileService.LastReport, "dürüst makbuz Results için hazırdır");

            o.CompletionId = string.Empty;
            Assert.AreEqual(ProfileCommitOutcome.MissingIdentity, ProfileService.ApplyRun(o).Outcome);
        }

        [Test]
        public void EskiKimlikler_SuresizReddedilir_HalkaYok()
        {
            // WP-009'daki hâli ("KimlikHalkasi_EskiKimlikler_KapasiteKadarHatirlanir") yalnız 8 kimliğin
            // hatırlanmasını ve en eskinin ATILMASINI beklenen davranış sayıyordu — bu, exactly-once
            // sözleşmesiyle çelişen kusurun ta kendisiydi (WP-009H kırmızı kanıtı). Sözleşme: uygulanmış
            // her completion, aradan kaç koşu geçerse geçsin reddedilir.
            const int n = 20;
            var ozetler = new List<RunSummary>();
            var makbuzlar = new List<RunRewardReport>();
            for (int i = 0; i < n; i++)
            {
                RunSummary o = Ozet(Kimlik(), 1, 0, 1, 1, 0);
                RunRewardReport r = ProfileService.ApplyRun(o);
                Assert.IsTrue(r.Applied);
                ozetler.Add(o);
                makbuzlar.Add(r);
            }

            Assert.AreEqual(n, ProfileService.Current.LastAppliedCompletionOrdinal);
            int changedOnce = _changed;
            string disk = DiskParmakizi();
            for (int i = 0; i < n; i++)
            {
                RunRewardReport tekrar = ProfileService.ApplyRun(ozetler[i]);
                // En son kimlik: bellekteki orijinal makbuz geri döner; eskiler: Duplicate makbuzu.
                Assert.IsTrue(ReferenceEquals(tekrar, makbuzlar[i]) || tekrar.Outcome == ProfileCommitOutcome.Duplicate,
                    "her uygulanmış kimlik reddedilir: " + i + " → " + tekrar.Outcome);
                Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, ProfileService.LastVerdict);
            }

            Assert.AreEqual(changedOnce, _changed, "hiçbir replay Changed yayınlamaz");
            Assert.AreEqual(disk, DiskParmakizi(), "hiçbir replay diske dokunmaz");
            Assert.AreEqual(n, ProfileService.Current.RunsPlayed);
        }

        [Test]
        public void SaveBasarisiz_SonraYeniKosu_EskiTokenOlur_YeniUygulanir()
        {
            // K-4: başarısız save'den sonra yeni koşu aynı ordinal'i YENİ nonce ile alır; eski token
            // artık canlı yetki değildir (NotIssued → InvalidIdentity), yeni koşu uygulanır.
            RunSummary eski = Ozet(Kimlik(), 2, 1, 3, 3, 20);
            ProfileStore.SaveStageHook = s => { if (s == ProfileSaveStage.Replace) throw new IOException("injected"); };
            LogAssert.Expect(LogType.Warning, new Regex("injected"));
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, ProfileService.ApplyRun(eski).Outcome);
            ProfileStore.SaveStageHook = null;

            RunSummary yeni = Ozet(Kimlik(), 1, 0, 1, 1, 0);
            Assert.AreNotEqual(eski.CompletionId, yeni.CompletionId);
            string disk = DiskParmakizi();
            LogAssert.Expect(LogType.Warning, new Regex("not valid"));
            RunRewardReport eskiSonuc = ProfileService.ApplyRun(eski);
            Assert.AreEqual(ProfileCommitOutcome.InvalidIdentity, eskiSonuc.Outcome);
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, ProfileService.LastVerdict);
            Assert.AreEqual(disk, DiskParmakizi(), "ölü token diske dokunmaz");
            Assert.AreEqual(0, ProfileService.Current.RunsPlayed);

            Assert.IsTrue(ProfileService.ApplyRun(yeni).Applied, "yeni koşu uygulanır");
            Assert.AreEqual(1, ProfileService.Current.LastAppliedCompletionOrdinal);
            Assert.AreEqual(1, ProfileService.Current.RunsPlayed);
        }

        // ------------------------------------------------------------------ transaction / save failure

        [TestCase(ProfileSaveStage.Serialize)]
        [TestCase(ProfileSaveStage.BeforeCandidateWrite)]
        [TestCase(ProfileSaveStage.CandidateWriteMid)]
        [TestCase(ProfileSaveStage.Flush)]
        [TestCase(ProfileSaveStage.CandidateReadBack)]
        [TestCase(ProfileSaveStage.BackupCopy)]
        [TestCase(ProfileSaveStage.BeforeReplace)]
        [TestCase(ProfileSaveStage.Replace)]
        [TestCase(ProfileSaveStage.PostReplaceVerify)]
        public void SaveBasarisiz_CanliProfilDiskeEsit_RetryTekUygular(ProfileSaveStage hata)
        {
            // Bir koşu uygulanmış bir profil (RunsPlayed 1); ikinci koşu save'de düşer.
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 3, 3, 20)).Applied);
            string bellekOnce = Parmakizi(ProfileService.Current);
            string diskOnce = DiskParmakizi();
            int changedOnce = _changed;

            RunSummary o = Ozet(Kimlik(), 4, 3, 9, 9, 90);
            ProfileStore.SaveStageHook = s =>
            {
                if (s == hata)
                {
                    throw new IOException("injected fault at " + s);
                }
            };

            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            RunRewardReport r = ProfileService.ApplyRun(o);
            ProfileStore.SaveStageHook = null;

            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, r.Outcome, "aşama " + hata);
            Assert.IsTrue(r.SaveFailed);
            Assert.IsFalse(r.Applied);
            Assert.AreEqual(hata, ProfileStore.LastSaveResult.FailedStage);
            Assert.AreEqual(r.LevelBefore, r.LevelAfter, "uygulanmamış makbuzda seviye ilerlemez");
            Assert.AreEqual(0, r.NewUnlocks.Count);
            Assert.AreEqual(bellekOnce, Parmakizi(ProfileService.Current), "canlı profil ilerlemedi (§4.4)");
            Assert.AreEqual(diskOnce, DiskParmakizi(), "disk eski tam profil (ya eski ya yeni; yarım asla)");
            Assert.AreEqual(changedOnce, _changed, "başarısız save Changed yayınlamaz");
            Assert.IsFalse(File.Exists(ProfileStore.CandidatePath), "aday dosyası artık yok");
            Assert.AreEqual(1, ProfileService.Current.LastAppliedCompletionOrdinal, "ordinal ilerlemedi → retry mümkün");
            Assert.IsTrue(CompletionAuthority.HasLiveCapability, "başarısız save yetkiyi tüketmez");

            // Retry (aynı kimlik, hata yok): TAM BİR KEZ uygulanır; sonra duplicate.
            RunRewardReport r2 = ProfileService.ApplyRun(o);
            Assert.IsTrue(r2.Applied, "retry uygular");
            Assert.AreEqual(2, ProfileService.Current.RunsPlayed, "duplicate ilerleme yok");
            Assert.AreEqual(2, ProfileService.Current.LastAppliedCompletionOrdinal, "stats ve ordinal aynı transaction'da");
            Assert.AreEqual(changedOnce + 1, _changed);
            Assert.AreEqual(Parmakizi(ProfileService.Current), DiskParmakizi(), "canlı = disk");
            Assert.AreSame(r2, ProfileService.ApplyRun(o), "üçüncü çağrı duplicate");
            Assert.AreEqual(2, ProfileService.Current.RunsPlayed);
        }

        [Test]
        public void Changed_TransactionBasinaTamBirKez_UnlockNewTekKez()
        {
            // L1→L2 (100 XP): AutoSmg + Explosion açılır, First Blood kazanılır (PrototypeOnly değerler).
            RunSummary o = Ozet(Kimlik(), 2, 1, 5, 5, 95);
            RunRewardReport r1 = ProfileService.ApplyRun(o);
            Assert.AreEqual(1, _changed);
            Assert.Greater(r1.NewUnlocks.Count, 0);
            Assert.Greater(r1.NewAchievements.Count, 0);

            // Sonraki farklı koşu: aynı unlock/achievement tekrar "new" olarak gelmez.
            RunRewardReport r2 = ProfileService.ApplyRun(Ozet(Kimlik(), 1, 0, 1, 1, 0));
            Assert.AreEqual(2, _changed);
            Assert.AreEqual(0, r2.NewUnlocks.Count, "aynı unlock yalnız onu ilk üreten transaction'da 'new'");
            Assert.AreEqual(0, r2.NewAchievements.Count);
        }

        [Test]
        public void Makbuz_GetOnly_ListelerSaltOkunur_KimlikProfildeKayitli()
        {
            RunRewardReport r = ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 5, 5, 20));
            foreach (PropertyInfo p in typeof(RunRewardReport).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(p.CanWrite, "makbuz üyesi yazılabilir olmamalı: " + p.Name);
            }

            Assert.AreEqual(0, typeof(RunRewardReport).GetFields(BindingFlags.Public | BindingFlags.Instance).Length, "public alan yok");
            Assert.AreEqual(0, typeof(RunRewardReport).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length,
                "public ctor yok (yalnız Draft.Seal(); Draft testlerin makbuz kurabilmesi için public'tir — mühürlendikten sonra değişmez)");
            Assert.IsInstanceOf<IReadOnlyList<ProfileUnlockEntry>>(r.NewUnlocks);
            Assert.IsFalse(r.NewUnlocks is List<ProfileUnlockEntry>, "liste dışarıya List olarak sızmaz");
            CompletionToken parsed;
            Assert.AreEqual(CompletionTokenVerdict.Valid, CompletionAuthority.TryParse(r.CompletionId, out parsed));
            Assert.AreEqual(parsed.Ordinal, ProfileService.Current.LastAppliedCompletionOrdinal, "makbuzun ordinal'i profilin yüksek su işaretidir");
            Assert.IsFalse(CompletionAuthority.HasLiveCapability, "başarılı save yetkiyi tüketir");
        }

        // ------------------------------------------------------------------ entitlement

        [Test]
        public void Entitlement_ConfigEsigiYukselse_ReloadSonrasiDaAcikKalir()
        {
            Assert.IsFalse(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg));
            int hedef = Math.Max(ProfileService.UnlockLevelFor(ClassicWeaponId.AutoSmg), ProfileService.UnlockLevelFor(UpgradeBranch.Explosion));
            int gerekliXp = 0; for (int l = 1; l < hedef; l++) gerekliXp += ProfileService.LevelRequirement(l);
            RunRewardReport r = ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 0, 0, gerekliXp));
            Assert.AreEqual(hedef, r.LevelAfter);
            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg), "unlock otomatik; claim yok");
            Assert.IsTrue(ProfileService.IsBranchUnlocked(UpgradeBranch.Explosion));

            PrototypeConfig orijinal = PrototypeConfig.Active;
            Assert.IsNotNull(orijinal);
            PrototypeConfig klon = UnityEngine.Object.Instantiate(orijinal);
            try
            {
                klon.Weapons.Find(ClassicWeaponId.AutoSmg).UnlockProfileLevel = 40;
                for (int i = 0; i < klon.Economy.Branches.Count; i++)
                {
                    if (klon.Economy.Branches[i].Branch == UpgradeBranch.Explosion)
                    {
                        klon.Economy.Branches[i].UnlockProfileLevel = 40;
                    }
                }

                PrototypeConfig.SetActive(klon);
                Assert.AreEqual(40, ProfileService.UnlockLevelFor(ClassicWeaponId.AutoSmg), "merdiven yükseldi");
                Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg), "kazanılmış entitlement geri alınmaz");
                Assert.IsTrue(ProfileService.IsBranchUnlocked(UpgradeBranch.Explosion));

                YenidenYukle();
                Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg), "reload sonrası da açık");
                Assert.IsTrue(ProfileService.IsBranchUnlocked(UpgradeBranch.Explosion));

                // Taze profil yükselmiş eşikte SMG'yi kazanmaz.
                ProfileService.ResetProfile();
                Assert.IsFalse(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg));
                Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AssaultRifle));
            }
            finally
            {
                PrototypeConfig.SetActive(orijinal);
                UnityEngine.Object.DestroyImmediate(klon);
            }
        }

        [Test]
        public void BilinmeyenId_DisktenGelir_IcerikAcmaz_KaybolmazDa()
        {
            ProfileService.ApplyRun(Ozet(Kimlik(), 1, 0, 1, 1, 0));
            ProfileData disk;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out disk));
            disk.UnlockedWeapons.Add("Bazooka");
            disk.UnlockedBranches.Add("Laser");
            disk.Achievements.Add("ghost");
            Assert.IsTrue(ProfileStore.Save(disk).Success);

            YenidenYukle();
            Assert.IsFalse(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg));
            Assert.IsFalse(ProfileService.IsWeaponUnlocked(ClassicWeaponId.CombatShotgun));
            Assert.IsFalse(ProfileService.IsBranchUnlocked(UpgradeBranch.Chain));
            Assert.IsTrue(ProfileService.Current.UnlockedWeapons.Contains("Bazooka"), "bilinmeyen id korunur");

            // Bir transaction daha: bilinmeyen id'ler dosyada kalır (kayıp yok), bilinenler eklenir.
            ProfileService.ApplyRun(Ozet(Kimlik(), 1, 0, 1, 1, 0));
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out disk));
            Assert.IsTrue(disk.UnlockedWeapons.Contains("Bazooka"));
            Assert.IsTrue(disk.UnlockedBranches.Contains("Laser"));
            Assert.IsTrue(disk.Achievements.Contains("ghost"));
            Assert.IsTrue(disk.UnlockedWeapons.Contains("AssaultRifle"));
        }

        [Test]
        public void KilitliyKen_SaveReddedilir_CanliProfilDegismez()
        {
            // Future-version dosyası: yükleme salt-okunur moda geçer; ApplyRun SaveFailed döner, dosya dokunulmaz.
            File.WriteAllText(TestPath, "{\"SaveVersion\": 99, \"ProfileLevel\": 7}");
            string hashOnce = HashOf(TestPath);
            LogAssert.Expect(LogType.Warning, new Regex("newer version"));
            YenidenYukle();
            Assert.IsTrue(ProfileService.SaveLocked);
            Assert.AreEqual(1, ProfileService.Current.ProfileLevel, "bellekte taze profil");
            StringAssert.Contains("newer version", ProfileService.PersistenceNotice);

            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            RunRewardReport r = ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 5, 5, 20));
            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, r.Outcome);
            Assert.AreEqual(0, ProfileService.Current.RunsPlayed, "canlı profil ilerlemedi");
            Assert.AreEqual(hashOnce, HashOf(TestPath), "future dosya bayt bayt aynı (downgrade/overwrite yok)");
            Assert.AreEqual(0, _changed);
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            Assert.IsFalse(ProfileService.Save());
            Assert.AreEqual(hashOnce, HashOf(TestPath));

            // ResetProfile (F1 "Reset local profile") kilitliyken reddedilir: yeni build'in dosyası silinmez.
            LogAssert.Expect(LogType.Warning, new Regex("Reset refused"));
            ProfileService.ResetProfile();
            Assert.AreEqual(hashOnce, HashOf(TestPath), "reset future dosyayı silmedi");
            Assert.AreEqual(0, _changed);
            // Temizlik TearDown'da (kilit + dosya).
        }

        [Test]
        public void AcilmisDal_VarsayilanYuklemleTeklifeGirer_EsikYukselseDe()
        {
            // WP-008 teklif kapısı varsayılan yüklemi ProfileService.IsBranchUnlocked → kazanılmış liste.
            Assert.IsFalse(ProfileService.IsBranchUnlocked(UpgradeBranch.Explosion), "L1: Explosion listede yok");
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 0, 0, 100)).Applied);
            Assert.IsTrue(ProfileService.IsBranchUnlocked(UpgradeBranch.Explosion), "L2: Explosion kazanıldı");

            PrototypeConfig orijinal = PrototypeConfig.Active;
            PrototypeConfig klon = UnityEngine.Object.Instantiate(orijinal);
            try
            {
                for (int i = 0; i < klon.Economy.Branches.Count; i++)
                {
                    if (klon.Economy.Branches[i].Branch == UpgradeBranch.Explosion)
                    {
                        klon.Economy.Branches[i].UnlockProfileLevel = 40; // eşik sonradan yükseldi
                    }
                }

                PrototypeConfig.SetActive(klon);
                var wallet = new DD.Prototype.Economy.GoldWallet();
                wallet.InitializeForRun(7);
                var shop = new RoundUpgradeShop { Random = new SeededUpgradeOfferRandom(3) }; // varsayılan yüklem (enjekte yok)
                bool teklifEdildi = false;
                for (int round = 1; round <= 40 && !teklifEdildi; round++)
                {
                    shop.Open(round, klon, wallet, new DD.Prototype.Weapons.WeaponModifierState(), 7);
                    Assert.AreNotEqual(UpgradeEligibility.Locked, shop.Eligibility(UpgradeBranch.Explosion), "kazanılmış dal kilitli sınıflandırılmaz");
                    Assert.AreEqual(UpgradeEligibility.Locked, shop.Eligibility(UpgradeBranch.Chain), "kazanılmamış dal (L3) kilitli");
                    for (int i = 0; i < shop.Cards.Count; i++)
                    {
                        teklifEdildi |= shop.Cards[i].Branch == UpgradeBranch.Explosion;
                        Assert.AreNotEqual(UpgradeBranch.Chain, shop.Cards[i].Branch, "kilitli dal teklife girmez");
                    }

                    shop.Close();
                }

                Assert.IsTrue(teklifEdildi, "açılmış dal (eşik 40'a yükselmiş olsa da) teklif sisteminde kullanılabilir");
            }
            finally
            {
                PrototypeConfig.SetActive(orijinal);
                UnityEngine.Object.DestroyImmediate(klon);
            }
        }

        [Test]
        public void SilahSecimi_StartRun_AuthoritativeProfileGoreDuzeltir()
        {
            // Kilitli silahla koşu başlatılamaz: StartRun seçimi ilk açık silaha çevirir (sahne yüklemesi
            // EditMode'da yalnız log'lanır; silah kararı yükleme öncesi verilir).
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.CombatShotgun;
            Assert.IsFalse(ProfileService.IsWeaponUnlocked(ClassicWeaponId.CombatShotgun));
            PrototypeUiFlow.StartRun();
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, PrototypeUiFlow.SelectedWeapon, "L1'de kilitli seçim ilk açık silaha döner");

            // Kazanılmış SMG (açılış seviyesi = liste/DEC-085 Ö-3), eşik sonradan 40'a çıkmış: seçim korunur (liste otoritedir).
            int smgSeviye = ProfileService.UnlockLevelFor(ClassicWeaponId.AutoSmg);
            int smgXp = 0; for (int l = 1; l < smgSeviye; l++) smgXp += ProfileService.LevelRequirement(l);
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 0, 0, smgXp)).Applied);
            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg), "SMG açılış seviyesine ulaşıldı");
            PrototypeConfig orijinal = PrototypeConfig.Active;
            PrototypeConfig klon = UnityEngine.Object.Instantiate(orijinal);
            try
            {
                klon.Weapons.Find(ClassicWeaponId.AutoSmg).UnlockProfileLevel = 40;
                PrototypeConfig.SetActive(klon);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AutoSmg;
                PrototypeUiFlow.StartRun();
                Assert.AreEqual(ClassicWeaponId.AutoSmg, PrototypeUiFlow.SelectedWeapon, "kazanılmış silah eşik yükselse de seçili kalır");
            }
            finally
            {
                PrototypeConfig.SetActive(orijinal);
                UnityEngine.Object.DestroyImmediate(klon);
            }
        }

        [Test]
        public void ProfilSeviyesi_CombatSistemleriTarafindanOkunmaz()
        {
            // Güç eşitliğinin statik yarısı: Player/Weapons/Damage/Enemies/Economy/Spawn altındaki hiçbir
            // üretim dosyası profil servisini ya da profil verisini okumaz.
            string kok = Path.Combine(Application.dataPath, "_Prototype");
            string[] klasorler = { "Player", "Weapons", "Damage", "Enemies", "Economy", "Spawn" };
            var ihlaller = new List<string>();
            for (int i = 0; i < klasorler.Length; i++)
            {
                string dir = Path.Combine(kok, klasorler[i]);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string kaynak = File.ReadAllText(f);
                    string goreli = f.Substring(kok.Length).Replace('\\', '/');

                    // WP-034/035 istisnası: silah roster KAYNAĞI seçim katmanıdır (UI'nin okuduğu
                    // entitlement kapısı); yalnız IsWeaponUnlocked okur, seviye/istatistik/veri okumaz.
                    if (goreli.EndsWith("/Weapons/WeaponRosterSource.cs", StringComparison.Ordinal))
                    {
                        Assert.IsFalse(kaynak.Contains("ProfileData") || kaynak.Contains("ProfileLevel") || kaynak.Contains("XpIntoLevel") || kaynak.Contains("ProfileService.Current"),
                            "roster kaynağı yalnız entitlement kapısını okur: " + goreli);
                        StringAssert.Contains("ProfileService.IsWeaponUnlocked", kaynak);
                        continue;
                    }

                    if (kaynak.Contains("ProfileService") || kaynak.Contains("ProfileData") || kaynak.Contains("ProfileLevel"))
                    {
                        ihlaller.Add(goreli);
                    }
                }
            }

            Assert.AreEqual(0, ihlaller.Count, "combat/ekonomi sistemleri profili okumamalı: " + string.Join(", ", ihlaller));
        }
    }
}
