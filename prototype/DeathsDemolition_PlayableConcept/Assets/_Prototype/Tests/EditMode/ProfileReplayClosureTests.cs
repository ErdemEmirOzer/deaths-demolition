using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-009H · Kalıcı exactly-once kapanışı — kırmızı karakterizasyon + sözleşme testleri.
    ///
    /// Kusur (WP-009 main 45506612): anti-replay durumu son 8 completion id'lik bir halkaydı;
    /// dokuz ve daha fazla başarılı koşudan sonra ilk koşunun özeti yeniden sunulunca
    /// XP/kill/RunsPlayed/round ikinci kez uygulanıyordu. Bu fixture düzeltme ÖNCESİ kırmızı
    /// (evidence/red), düzeltme SONRASI yeşil olmalıdır; kusuru saklayacak şekilde yumuşatılmaz.
    ///
    /// Her serinin ardından ilk/orta/son completion replay'i hiçbir profil değerini, RAM/disk
    /// hash'ini, Changed sayısını ve save çağrı sayısını değiştirmemelidir; aynı deney statik
    /// reset + diskten yeniden yükleme sonrasında da tekrarlanır.
    /// </summary>
    [TestFixture]
    [Category("WP009H")]
    public sealed class ProfileReplayClosureTests
    {
        private static readonly string TestDir =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp009h-replay");

        private static readonly string TestPath = Path.Combine(TestDir, "prototype-profile.json");

        private static readonly string RealPath =
            Path.Combine(Application.persistentDataPath, ProfileStore.FileName);

        private string _realHashBefore;
        private int _changed;
        private int _saves;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOfFile(RealPath);
            Directory.CreateDirectory(TestDir);
            ProfileStore.SetPathOverride(TestPath);
            Bosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Bosalt();
            ProfileStore.SaveStageHook = null;
            ProfileStore.SetPathOverride(null);
            try { Directory.Delete(TestDir, true); } catch (Exception) { }
            Assert.AreEqual(_realHashBefore, HashOfFile(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path, "override kayboldu");
            ProfileService.ResetProfile();
            Assert.IsFalse(ProfileStore.SaveLocked, "fixture salt-okunur olmamalı (pozitif kontrol)");
            Assert.IsTrue(ProfileService.Save(), "pozitif kontrol: save çalışıyor");
            _changed = 0;
            _saves = 0;
            ProfileService.Changed += Sayac;
            ProfileStore.SaveStageHook = s => { if (s == ProfileSaveStage.Serialize) _saves++; };
        }

        [TearDown]
        public void TearDown()
        {
            ProfileService.Changed -= Sayac;
            ProfileStore.SaveStageHook = null;
        }

        private void Sayac() => _changed++;

        // ------------------------------------------------------------------ yardımcılar

        private static void Bosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        /// <summary>"Uygulama yeniden açıldı": statikler temizlenir, profil diskten yeniden yüklenir.</summary>
        private static void YenidenYukle()
        {
            Bosalt();
            ProfileService.EnsureLoaded();
        }

        private static string HashOfFile(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").Substring(0, 16);
        }

        private static string RamHash()
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(ProfileService.Current)))).Replace("-", "").Substring(0, 16);
        }

        private struct Sayaclar
        {
            public int Runs, Xp, Into, Level, Kills, Standard, Elite, Rounds, Highest, Armor, Weapons, Branches, Ach;

            public static Sayaclar Al()
            {
                ProfileData p = ProfileService.Current;
                return new Sayaclar
                {
                    Runs = p.RunsPlayed, Xp = p.TotalXpEarned, Into = p.XpIntoLevel, Level = p.ProfileLevel, Kills = p.TotalKills,
                    Standard = p.StandardKills, Elite = p.EliteKills, Rounds = p.RoundsCompleted, Highest = p.HighestRound,
                    Armor = p.ArmorPiecesBroken, Weapons = p.UnlockedWeapons.Count, Branches = p.UnlockedBranches.Count, Ach = p.Achievements.Count
                };
            }

            public override string ToString()
            {
                return "runs=" + Runs + " xp=" + Xp + " into=" + Into + " level=" + Level + " kills=" + Kills + "/" + Standard + "/" + Elite +
                       " rounds=" + Rounds + " highest=" + Highest + " armor=" + Armor + " unlocks=" + Weapons + "+" + Branches + " ach=" + Ach;
            }
        }

        /// <summary>
        /// Kimlik kaynağı. KIRMIZI koşu (WP-009 main, evidence/red): RunDirector.StartRun'ın yaptığı gibi
        /// rastgele GUID (`Guid.NewGuid().ToString("N")`). YEŞİL (WP-009H): yetkili identity authority'nin
        /// dev/Editor dikişi = üretimdeki issue yolu (lineage + sonraki ordinal + nonce). Assert'ler aynıdır.
        /// </summary>
        private static string Kimlik()
        {
            return CompletionAuthority.TestSeam.IssueForNextRun();
        }

        private static RunSummary Ozet(int i)
        {
            return new RunSummary
            {
                CompletionId = Kimlik(),
                ReachedRound = 1 + (i % 3),
                CompletedRounds = i % 3,
                TotalKills = 2 + (i % 2),
                StandardKills = 2,
                EliteKills = i % 2,
                KillXp = 2 + 4 * (i % 2),
                CompletedRoundXp = 20 * (i % 3),
                TotalXpEarned = 2 + 4 * (i % 2) + 20 * (i % 3),
                ArmorPiecesBroken = i % 2,
                Weapon = ClassicWeaponId.AssaultRifle,
                BuildDescription = "P1",
                EndReason = RunEndReason.PlayerDeath
            };
        }

        private void ReplayHicbirSeyiDegistirmez(RunSummary eski, string etiket)
        {
            Sayaclar once = Sayaclar.Al();
            string ramOnce = RamHash();
            string diskOnce = HashOfFile(TestPath);
            int changedOnce = _changed;
            int savesOnce = _saves;
            RunRewardReport makbuzOnce = ProfileService.LastReport;

            RunRewardReport r = ProfileService.ApplyRun(eski);

            Sayaclar sonra = Sayaclar.Al();
            Debug.Log("[DD-TEST] wp009h.replay " + etiket + " id=" + eski.CompletionId + " outcome=" + r.Outcome +
                      " before[" + once + "] after[" + sonra + "] ram " + ramOnce + "->" + RamHash() + " disk " + diskOnce + "->" + HashOfFile(TestPath) +
                      " changed " + changedOnce + "->" + _changed + " saves " + savesOnce + "->" + _saves);

            // En son uygulanan koşunun replay'i bellekteki ORİJİNAL makbuzu geri verir (Results yeniden açılışı);
            // başka her replay Duplicate makbuzudur. İkisi de "uygulandı" değildir — aşağıdaki sayaç/hash
            // assert'leri bunun kanıtıdır.
            bool yenidenUygulandi = r.Applied && !ReferenceEquals(r, makbuzOnce);
            Assert.IsFalse(yenidenUygulandi, etiket + ": daha önce uygulanmış completion ikinci kez UYGULANDI (kusur) → " + r.Outcome);
            Assert.IsTrue(r.Outcome == ProfileCommitOutcome.Duplicate || ReferenceEquals(r, makbuzOnce), etiket + ": " + r.Outcome);
            Assert.AreEqual(once.ToString(), sonra.ToString(), etiket + ": profil sayaçları değişti");
            Assert.AreEqual(ramOnce, RamHash(), etiket + ": RAM hash değişti");
            Assert.AreEqual(diskOnce, HashOfFile(TestPath), etiket + ": disk hash değişti");
            Assert.AreEqual(changedOnce, _changed, etiket + ": Changed yayınlandı");
            Assert.AreEqual(savesOnce, _saves, etiket + ": save çağrıldı");
        }

        private List<RunSummary> Seri(int n)
        {
            var seri = new List<RunSummary>(n);
            for (int i = 0; i < n; i++)
            {
                RunSummary o = Ozet(i);
                RunRewardReport r = ProfileService.ApplyRun(o);
                Assert.IsTrue(r.Applied, "koşu " + i + " uygulanmalı (" + r.Outcome + ")");
                seri.Add(o);
            }

            Assert.AreEqual(n, ProfileService.Current.RunsPlayed);
            Assert.AreEqual(n, _changed);
            return seri;
        }

        // ------------------------------------------------------------------ A. kırmızı kusur testi

        [Test]
        public void A_DokuzKosuSonraIlkKosuReplay_AyniSurecte_UygulanmamalI()
        {
            string diskBaslangic = HashOfFile(TestPath);
            List<RunSummary> seri = Seri(9);
            Debug.Log("[DD-TEST] wp009h.A ids=" + string.Join(",", seri.ConvertAll(s => s.CompletionId)) +
                      " diskStart=" + diskBaslangic + " diskAfter9=" + HashOfFile(TestPath) + " " + Sayaclar.Al());
            ReplayHicbirSeyiDegistirmez(seri[0], "A.ilk(9 sonrası, aynı süreç)");
        }

        [Test]
        public void A_DokuzKosuSonraIlkKosuReplay_YenidenYuklemeSonrasi_UygulanmamalI()
        {
            List<RunSummary> seri = Seri(9);
            YenidenYukle();
            Assert.AreEqual(9, ProfileService.Current.RunsPlayed, "diskten aynı profil");
            ReplayHicbirSeyiDegistirmez(seri[0], "A.ilk(9 sonrası, reload)");
        }

        // ------------------------------------------------------------------ B. kapasite ötesi seriler

        [TestCase(9)]
        [TestCase(19)]
        [TestCase(64)]
        public void B_UzunSeri_IlkOrtaSonReplay_Reddedilir(int n)
        {
            List<RunSummary> seri = Seri(n);
            long boyut9 = new FileInfo(TestPath).Length;
            ReplayHicbirSeyiDegistirmez(seri[0], "B" + n + ".ilk");
            ReplayHicbirSeyiDegistirmez(seri[n / 2], "B" + n + ".orta");
            ReplayHicbirSeyiDegistirmez(seri[n - 1], "B" + n + ".son");
            Assert.AreEqual(n, ProfileService.Current.RunsPlayed);
            Debug.Log("[DD-TEST] wp009h.B" + n + " fileBytes=" + boyut9);
        }

        [Test]
        public void B_ProfilDosyasi_KosuSayisiylaBuyumez()
        {
            // WP-023: başarım/içerik listeleri KAZANILAN içerikle büyür (katalog boyutuyla sınırlı), koşu
            // sayısıyla değil; anti-replay durumunun O(1) olduğunu ölçmek için kataloglar boş verilir.
            DD.Prototype.Achievements.AchievementCatalog.TestSeam.Override(new List<DD.Prototype.Achievements.AchievementDefinition>());
            DD.Prototype.Achievements.UnlockCatalog.TestSeam.Override(new List<DD.Prototype.Achievements.UnlockDefinition>());
            try
            {
                Seri(8);
                long b8 = new FileInfo(TestPath).Length;
                for (int i = 8; i < 40; i++)
                {
                    Assert.IsTrue(ProfileService.ApplyRun(Ozet(i)).Applied);
                }

                long b40 = new FileInfo(TestPath).Length;
                Debug.Log("[DD-TEST] wp009h.size 8runs=" + b8 + "B 40runs=" + b40 + "B");
                Assert.LessOrEqual(b40, b8 + 64, "anti-replay durumu koşu sayısıyla büyümemeli (O(1)); yalnız sayısal alan genişliği kadar fark olabilir");
            }
            finally
            {
                DD.Prototype.Achievements.AchievementCatalog.TestSeam.Override(null);
                DD.Prototype.Achievements.UnlockCatalog.TestSeam.Override(null);
            }
        }

        // ------------------------------------------------------------------ C. reload / relaunch

        [Test]
        public void C_UzunSeriSonrasiReload_IlkOrtaSon_Reddedilir()
        {
            List<RunSummary> seri = Seri(32);
            YenidenYukle();
            Assert.AreEqual(32, ProfileService.Current.RunsPlayed);
            ReplayHicbirSeyiDegistirmez(seri[0], "C.ilk(reload)");
            ReplayHicbirSeyiDegistirmez(seri[16], "C.orta(reload)");
            ReplayHicbirSeyiDegistirmez(seri[31], "C.son(reload)");
            YenidenYukle();
            ReplayHicbirSeyiDegistirmez(seri[0], "C.ilk(2. reload)");
        }

        // ------------------------------------------------------------------ D. aynı içerikli iki ayrı koşu

        [Test]
        public void D_AyniIcerikliIkiAyriKosu_IkisiDeBirerKez()
        {
            // Tek aktif koşu modeli: iki koşu sırayla üretilir ve uygulanır; içerik birebir aynı.
            RunSummary k1 = Ozet(4);
            Assert.IsTrue(ProfileService.ApplyRun(k1).Applied);
            RunSummary k2 = Ozet(4);
            Assert.IsTrue(ProfileService.ApplyRun(k2).Applied);
            Assert.AreEqual(2, ProfileService.Current.RunsPlayed, "aynı içerik duplicate nedeni değildir");
            Assert.AreEqual(2, _changed);
            ReplayHicbirSeyiDegistirmez(k1, "D.k1");
            ReplayHicbirSeyiDegistirmez(k2, "D.k2");
            Assert.AreNotEqual(k1.CompletionId, k2.CompletionId);
        }

        // ------------------------------------------------------------------ I. v2 halka → v3 migrasyon + legacy replay

        private static string V2Belge(int runsPlayed, string[] ring)
        {
            string ids = string.Join(",", Array.ConvertAll(ring, x => "\"" + x + "\""));
            return "{\"SaveVersion\":2,\"ProfileLevel\":2,\"XpIntoLevel\":40,\"TotalXpEarned\":140,\"HighestRound\":3,\"RunsPlayed\":" + runsPlayed +
                   ",\"RoundsCompleted\":5,\"TotalKills\":31,\"StandardKills\":31,\"SpecialKills\":0,\"EliteKills\":0,\"UltraKills\":0,\"UnattributedKills\":0," +
                   "\"ArmorPiecesBroken\":2,\"BestRunXp\":80,\"Achievements\":[\"first_blood\"],\"UnlockedWeapons\":[\"AssaultRifle\",\"AutoSmg\"]," +
                   "\"UnlockedBranches\":[\"Piercing\"],\"AppliedCompletionIds\":[" + ids + "],\"LastSavedUtc\":\"2026-09-14T00:00:00.0000000Z\"}";
        }

        private void LegacyReplayReddedilir(string guid, string etiket)
        {
            Sayaclar once = Sayaclar.Al();
            string ramOnce = RamHash();
            string diskOnce = HashOfFile(TestPath);
            int changedOnce = _changed;
            int savesOnce = _saves;
            RunSummary o = Ozet(3);
            o.CompletionId = guid; // v2 ham GUID (halkada olan ya da atılmış)
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not valid"));
            RunRewardReport r = ProfileService.ApplyRun(o);
            Assert.AreEqual(ProfileCommitOutcome.InvalidIdentity, r.Outcome, etiket);
            Assert.AreEqual(CompletionTokenVerdict.Malformed, ProfileService.LastVerdict, etiket + ": legacy GUID biçim düzeyinde fail-closed");
            Assert.AreEqual(once.ToString(), Sayaclar.Al().ToString(), etiket);
            Assert.AreEqual(ramOnce, RamHash(), etiket);
            Assert.AreEqual(diskOnce, HashOfFile(TestPath), etiket);
            Assert.AreEqual(changedOnce, _changed, etiket);
            Assert.AreEqual(savesOnce, _saves, etiket);
        }

        [TestCase(0, TestName = "I_V2_BosHalka")]
        [TestCase(8, TestName = "I_V2_SekizId")]
        [TestCase(11, TestName = "I_V2_OnBirKosu_SonSekizId")]
        public void I_V2Fixture_V3eKayipsizGocer_LegacyReplayReddedilir(int kosu)
        {
            var tumIdler = new string[kosu];
            for (int i = 0; i < kosu; i++) tumIdler[i] = Guid.NewGuid().ToString("N");
            string[] halka = kosu <= 8 ? tumIdler : new ArraySegment<string>(tumIdler, kosu - 8, 8).ToArray();
            File.WriteAllText(TestPath, V2Belge(kosu, halka));
            YenidenYukle();
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(ProfileData.CurrentVersion, p.SaveVersion);
            Assert.IsTrue(ProfileData.IsLineageId(p.ProfileLineageId));
            Assert.AreEqual(0, p.LastAppliedCompletionOrdinal, "halka ordinal'e çevrilmez, tahmin yok");
            Assert.AreEqual(kosu, p.RunsPlayed, "progression korunur");
            Assert.AreEqual(2, p.ProfileLevel);
            Assert.AreEqual(140, p.TotalXpEarned);
            Assert.AreEqual(31, p.TotalKills);
            CollectionAssert.AreEqual(new[] { "ar_1", "smg_1", "sg_1", "ar_2", "smg_2", "sg_2", "lmg_1", "lmg_2", "sr_1" }, p.UnlockedWeapons,
                "WP-077 v5: M3 silah adları WP-069 anahtarlarına göçer (ar_1, smg_1 dosyadan, sıra korunur); WP-107: yükleme rl_1 dışındaki silahları da verir");
            CollectionAssert.AreEqual(new[] { "first_blood" }, p.Achievements);

            // Diske v3 yaz, halka gitmiş olsun; ikinci yükleme idempotent.
            Assert.IsTrue(ProfileService.Save());
            string json = File.ReadAllText(TestPath);
            StringAssert.DoesNotContain("AppliedCompletionIds", json);
            string soy = p.ProfileLineageId;
            YenidenYukle();
            Assert.AreEqual(soy, ProfileService.Current.ProfileLineageId, "lineage tek seferlik; ikinci yükleme değiştirmez");
            _changed = 0; _saves = 0;

            // Legacy replay: halkada olan ilk/orta/son ve (11 koşuda) halkadan atılmış en eski GUID.
            if (kosu > 0)
            {
                LegacyReplayReddedilir(halka[0], "I.halka.ilk");
                LegacyReplayReddedilir(halka[halka.Length / 2], "I.halka.orta");
                LegacyReplayReddedilir(halka[halka.Length - 1], "I.halka.son");
            }

            if (kosu > 8)
            {
                LegacyReplayReddedilir(tumIdler[0], "I.atilmis.ilk");
            }

            // Yeni soyda gerçek koşu uygulanır ve replay'i reddedilir.
            RunSummary yeni = Ozet(5);
            Assert.IsTrue(ProfileService.ApplyRun(yeni).Applied);
            Assert.AreEqual(1, ProfileService.Current.LastAppliedCompletionOrdinal);
            Assert.AreEqual(kosu + 1, ProfileService.Current.RunsPlayed);
            ReplayHicbirSeyiDegistirmez(yeni, "I.yeni");
        }

        [Test]
        public void I_V1_GercekBenzeri_V3eGocer_ReplayYok()
        {
            File.WriteAllText(TestPath, "{\"SaveVersion\":1,\"ProfileLevel\":1,\"XpIntoLevel\":37,\"TotalXpEarned\":37,\"HighestRound\":2,\"RunsPlayed\":12,\"RoundsCompleted\":1,\"TotalKills\":17,\"StandardKills\":17,\"SpecialKills\":0,\"EliteKills\":0,\"UltraKills\":0,\"ArmorPiecesBroken\":3,\"BestRunXp\":32,\"Achievements\":[\"first_blood\",\"armor_breaker\"],\"UnlockedWeapons\":[\"AssaultRifle\"],\"UnlockedBranches\":[\"Piercing\",\"Ricochet\"],\"LastSavedUtc\":\"2026-09-12T15:03:27.0000000Z\"}");
            YenidenYukle();
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(ProfileData.CurrentVersion, p.SaveVersion);
            Assert.AreEqual(12, p.RunsPlayed);
            Assert.AreEqual(37, p.TotalXpEarned);
            Assert.AreEqual(17, p.TotalKills);
            Assert.AreEqual(0, p.LastAppliedCompletionOrdinal);
            Assert.IsTrue(ProfileData.IsLineageId(p.ProfileLineageId));
            _changed = 0; _saves = 0;
            LegacyReplayReddedilir(Guid.NewGuid().ToString("N"), "I.v1.rastgeleGuid");
            RunSummary yeni = Ozet(1);
            Assert.IsTrue(ProfileService.ApplyRun(yeni).Applied);
            Assert.AreEqual(13, ProfileService.Current.RunsPlayed);
            ReplayHicbirSeyiDegistirmez(yeni, "I.v1.yeni");
        }
    }
}
