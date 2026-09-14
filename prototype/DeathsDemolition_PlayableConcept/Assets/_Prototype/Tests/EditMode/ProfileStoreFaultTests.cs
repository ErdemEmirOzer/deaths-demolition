using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Profile;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-009 · Crash-safe save zinciri, recovery, migration ve yazma sahipliği (ProfileStore, izole klasör).
    ///
    /// İki tür fault injection: (1) her aşamada HATA (hook fırlatır) → store toparlar, sonuç dürüst;
    /// (2) her aşamada ÇÖKME (hook klasörün anlık görüntüsünü alır; temizlik koşmaz) → o anlık
    /// görüntüden yükleme ya ESKİ ya YENİ eksiksiz profili verir. Ek olarak el ile kurulmuş
    /// disk durumları (orphan temp, eksik primary, bozuk primary + bak, doğrulanmamış daha yeni
    /// temp), teşhis (empty/truncated/malformed/future/no-version), v1→v2 kayıpsız migration,
    /// idempotent migration, çoklu load/save döngüsü ve pid tabanlı sahiplik kilidi.
    /// </summary>
    [TestFixture]
    [Category("WP009")]
    public sealed class ProfileStoreFaultTests
    {
        private static readonly string Dir =
            Path.Combine(Application.temporaryCachePath, "dd-editmode-wp009-store");

        private static readonly string Primary = Path.Combine(Dir, ProfileStore.FileName);
        private static string Candidate => Primary + ProfileStore.CandidateSuffix;
        private static string Backup => Primary + ProfileStore.BackupSuffix;
        private static string Lock => Primary + ProfileStore.LockSuffix;

        private static readonly string RealPath =
            Path.Combine(Application.persistentDataPath, ProfileStore.FileName);

        private string _realHashBefore;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            ProfileStore.SetPathOverride(null);
            ProfileStore.SaveStageHook = null;
            ProfileStore.ProcessLivenessProbe = null;
            TryDeleteDir(Dir);
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            ProfileStore.SaveStageHook = null;
            ProfileStore.ProcessLivenessProbe = null;
            ProfileStore.SetPathOverride(null);
            TryDeleteDir(Dir);
            Directory.CreateDirectory(Dir);
            ProfileStore.SetPathOverride(Primary);
            Assert.AreEqual(Primary, ProfileStore.Path);
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.SaveStageHook = null;
            ProfileStore.ProcessLivenessProbe = null;
            ProfileStore.SetPathOverride(null);
        }

        // ------------------------------------------------------------------ yardımcılar

        private static ProfileData Profil(int runs, int level = 1)
        {
            var p = new ProfileData { RunsPlayed = runs, ProfileLevel = level, TotalKills = runs * 3, StandardKills = runs * 3, TotalXpEarned = runs * 10 };
            p.UnlockedWeapons.Add("AssaultRifle");
            p.Achievements.Add("syn_" + runs);
            p.AppliedCompletionIds.Add("id-" + runs);
            p.Sanitize();
            return p;
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

        private static string Json(ProfileData p)
        {
            return JsonUtility.ToJson(p, true);
        }

        private static int CorruptCount()
        {
            return Directory.GetFiles(Dir, ProfileStore.FileName + ProfileStore.CorruptSuffix + "*").Length;
        }

        private static void Snapshot(string target)
        {
            TryDeleteDir(target);
            Directory.CreateDirectory(target);
            foreach (string f in Directory.GetFiles(Dir))
            {
                // Bir yazma akışı açıkken de kopyalanabilmeli (çökme anı): paylaşımlı okuma.
                using (var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var dst = new FileStream(Path.Combine(target, Path.GetFileName(f)), FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    src.CopyTo(dst);
                }
            }
        }

        // ------------------------------------------------------------------ zincir

        [Test]
        public void BasariliSave_PrimaryYeni_BakOncekiPrimary_AdayKalmaz()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(1)).Success);
            string ilk = File.ReadAllText(Primary);
            Assert.IsFalse(File.Exists(Backup), "ilk save'de yedeklenecek primary yoktu");
            Assert.IsFalse(File.Exists(Candidate));

            Assert.IsTrue(ProfileStore.Save(Profil(2)).Success);
            Assert.IsTrue(File.Exists(Backup));
            Assert.AreEqual(ilk, File.ReadAllText(Backup), ".bak = doğrulanmış önceki primary (bayt bayt)");
            Assert.IsFalse(File.Exists(Candidate), "aday yeniden adlandırıldı, kopyası kalmadı");

            ProfileData yuklenen = ProfileStore.Load();
            Assert.AreEqual(2, yuklenen.RunsPlayed);
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(ProfileLoadRecovery.None, ProfileStore.LastLoadRecovery);
        }

        [TestCase(ProfileSaveStage.Serialize)]
        [TestCase(ProfileSaveStage.BeforeCandidateWrite)]
        [TestCase(ProfileSaveStage.CandidateWriteMid)]
        [TestCase(ProfileSaveStage.Flush)]
        [TestCase(ProfileSaveStage.CandidateReadBack)]
        [TestCase(ProfileSaveStage.BackupCopy)]
        [TestCase(ProfileSaveStage.BeforeReplace)]
        [TestCase(ProfileSaveStage.Replace)]
        [TestCase(ProfileSaveStage.PostReplaceVerify)]
        public void HataEnjeksiyonu_HerAsama_SonucDurust_DiskEskiVeyaYeniTam(ProfileSaveStage hata)
        {
            Assert.IsTrue(ProfileStore.Save(Profil(5)).Success);
            string eski = File.ReadAllText(Primary);

            ProfileStore.SaveStageHook = s =>
            {
                if (s == hata)
                {
                    throw new IOException("injected " + s);
                }
            };
            LogAssert.Expect(LogType.Warning, new Regex("injected " + hata));
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            ProfileSaveResult r = ProfileStore.Save(Profil(6));
            ProfileStore.SaveStageHook = null;

            Assert.IsFalse(r.Success);
            Assert.AreEqual(hata, r.FailedStage);
            Assert.AreEqual("The profile file could not be written.", r.Message, "oyuncuya giden mesaj sabit; exception ayrıntısı yalnız log'da");
            Assert.IsFalse(File.Exists(Candidate), "aday temizlendi: " + hata);
            Assert.AreEqual(eski, File.ReadAllText(Primary), "primary eski tam profil: " + hata);

            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(5, y.RunsPlayed);
            Assert.IsTrue(y.IsValid());
        }

        [Test]
        public void CokmeEnjeksiyonu_HerAsamadanSonraYukleme_EskiVeyaYeniTamProfil()
        {
            // Çökme: hook, o aşamadaki klasörü kopyalar; save normal biter. Her kopya "o anda süreç
            // öldü" demektir; oradan yükleme ya 5 ya 6 verir, asla yarım/boş/sıfır.
            Assert.IsTrue(ProfileStore.Save(Profil(5)).Success);
            var asamalar = new List<ProfileSaveStage>();
            ProfileStore.SaveStageHook = s =>
            {
                asamalar.Add(s);
                Snapshot(Path.Combine(Dir, "..", "dd-editmode-wp009-crash-" + s));
            };
            Assert.IsTrue(ProfileStore.Save(Profil(6)).Success);
            ProfileStore.SaveStageHook = null;
            CollectionAssert.AreEqual(new[]
            {
                ProfileSaveStage.Serialize, ProfileSaveStage.BeforeCandidateWrite, ProfileSaveStage.CandidateWriteMid,
                ProfileSaveStage.Flush, ProfileSaveStage.CandidateReadBack, ProfileSaveStage.BackupCopy,
                ProfileSaveStage.BeforeReplace, ProfileSaveStage.Replace, ProfileSaveStage.PostReplaceVerify
            }, asamalar, "aşama sırası sözleşmesi");

            var sonuc = new List<string>();
            for (int i = 0; i < asamalar.Count; i++)
            {
                string kopya = Path.Combine(Dir, "..", "dd-editmode-wp009-crash-" + asamalar[i]);
                ProfileStore.SetPathOverride(Path.Combine(kopya, ProfileStore.FileName));
                ProfileData y = ProfileStore.Load();
                Assert.IsTrue(y.IsValid(), asamalar[i].ToString());
                Assert.IsTrue(y.RunsPlayed == 5 || y.RunsPlayed == 6, asamalar[i] + " → " + y.RunsPlayed);
                Assert.IsFalse(File.Exists(Path.Combine(kopya, ProfileStore.FileName + ProfileStore.CandidateSuffix)), "orphan aday yükleme sonrası kalmaz: " + asamalar[i]);
                sonuc.Add(asamalar[i] + "=" + y.RunsPlayed + "/" + ProfileStore.LastLoadRecovery);
                ProfileStore.SetPathOverride(null);
                TryDeleteDir(kopya);
            }

            // Anlık görüntü replace'ten ÖNCE alındığından (hook rename'den önce), Replace dahil her
            // kopya eski profili taşır; PostReplaceVerify kopyası yeni profili. Rename'in kendisi
            // (tek rename(2)) süreç içinden gözlenemez — bu bir runtime varsayımıdır; iki-rename
            // penceresi ve yalnız-aday durumları aşağıda el ile kurulur.
            Debug.Log("[DD-TEST] wp009.crashMatrix " + string.Join(" ", sonuc));
            StringAssert.Contains("PostReplaceVerify=6", string.Join(" ", sonuc));
            StringAssert.Contains("Replace=5", string.Join(" ", sonuc));
            ProfileStore.SetPathOverride(Primary);
        }

        [Test]
        public void CokmeEnjeksiyonu_CandidateWriteMid_GercektenYarimDosya()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(5)).Success);
            long adayBoyut = -1;
            ProfileStore.SaveStageHook = st =>
            {
                if (st == ProfileSaveStage.CandidateWriteMid)
                {
                    adayBoyut = new FileInfo(Candidate).Length;
                }
            };
            Assert.IsTrue(ProfileStore.Save(Profil(6)).Success);
            ProfileStore.SaveStageHook = null;
            long tam = new FileInfo(Primary).Length;
            Assert.Greater(adayBoyut, 0, "yarım aday diskte gerçekten bayt taşır (tampon boşaltıldı)");
            Assert.Less(adayBoyut, tam, "aday yarım: tam boyutun altında");
        }

        [Test]
        public void GecerliPrimary_BozukTemp_PrimaryKullanilir_TempSilinir()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(3)).Success);
            File.WriteAllText(Candidate, Json(Profil(4)).Substring(0, 50)); // truncated temp
            Assert.AreEqual(3, ProfileStore.Load().RunsPlayed);
            Assert.IsFalse(File.Exists(Candidate));
            File.WriteAllText(Candidate, "garbage"); // malformed temp
            Assert.AreEqual(3, ProfileStore.Load().RunsPlayed);
            Assert.IsFalse(File.Exists(Candidate));
        }

        [Test]
        public void EskiPrimary_BozukBak_TamAday_CopyDurableIcindeOlum()
        {
            // Ölüm .bak kopyası sırasında: primary eski tam, .bak yarım, .writing yeni tam (doğrulanmış).
            Assert.IsTrue(ProfileStore.Save(Profil(5)).Success);
            File.WriteAllText(Backup, Json(Profil(5)).Substring(0, 30));
            File.WriteAllText(Candidate, Json(Profil(6)));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(5, y.RunsPlayed, "geçerli primary kazanır; aday doğrulanmamış sayılır");
            Assert.IsFalse(File.Exists(Candidate));
            Assert.IsTrue(ProfileStore.Save(Profil(7)).Success, "sonraki save yarım .bak'ı yeniden yazar");
            ProfileData bak;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(Backup, out bak));
            Assert.AreEqual(5, bak.RunsPlayed);
        }

        [Test]
        public void IlkSave_YalnizAday_PrimaryVeBakYok_IleriSarilir()
        {
            // İlk kaydın ölümü: aday tam, primary yok, .bak yok → aday ileri sarılır (tek tam profil o).
            File.WriteAllText(Candidate, Json(Profil(1)));
            LogAssert.Expect(LogType.Warning, new Regex("interrupted"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(1, y.RunsPlayed);
            Assert.AreEqual(ProfileLoadRecovery.RolledForwardCandidate, ProfileStore.LastLoadRecovery);
            Assert.IsTrue(File.Exists(Primary));
            Assert.IsFalse(File.Exists(Candidate));
        }

        [Test]
        public void EksikPrimary_FutureVersionYanDosya_SaltOkunurKilit()
        {
            File.WriteAllText(Backup, "{\"SaveVersion\": 99, \"RunsPlayed\": 3}");
            LogAssert.Expect(LogType.Warning, new Regex("newer version"));
            ProfileData y = ProfileStore.Load();
            Assert.IsTrue(ProfileStore.SaveLocked, "yeni build'in yan dosyası da kilitler");
            Assert.AreEqual(0, y.RunsPlayed);
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            Assert.IsFalse(ProfileStore.Save(Profil(1)).Success);
            Assert.AreEqual("{\"SaveVersion\": 99, \"RunsPlayed\": 3}", File.ReadAllText(Backup), "yan dosya dokunulmadı");
        }

        [Test]
        public void SaltOkunurOturum_YuklemeHicbirDosyayaDokunmaz()
        {
            // Başka canlı örnek kilidi tutuyor: primary bozuk, .writing ve .bak var → hiçbiri taşınmaz/silinmez.
            File.WriteAllText(Lock, "pid=424242\nname=Deaths Demolition - Playable Concept\n");
            ProfileStore.ProcessLivenessProbe = (pid, name) => true;
            File.WriteAllText(Primary, "{{{ not json");
            File.WriteAllText(Candidate, Json(Profil(9)));
            File.WriteAllText(Backup, Json(Profil(4)));
            LogAssert.Expect(LogType.Warning, new Regex("Another copy"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(4, y.RunsPlayed, "salt-okunur: doğrulanmış yedek bellekte kullanılır");
            Assert.AreEqual("{{{ not json", File.ReadAllText(Primary), "bozuk primary yana alınmadı");
            Assert.IsTrue(File.Exists(Candidate), "diğer örneğin adayı silinmedi");
            Assert.AreEqual(0, CorruptCount());
            Assert.IsTrue(ProfileStore.SaveLocked);
        }

        // ------------------------------------------------------------------ el ile kurulmuş disk durumları

        [Test]
        public void OrphanTemp_GecerliPrimary_PrimaryKullanilir_TempSilinir()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(3)).Success);
            File.WriteAllText(Candidate, Json(Profil(9)));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(3, y.RunsPlayed, "doğrulanmamış aday geçerli primary'yi ezemez");
            Assert.IsFalse(File.Exists(Candidate), "orphan temp reconcile edildi");
            Assert.AreEqual(ProfileLoadRecovery.None, ProfileStore.LastLoadRecovery);
        }

        [Test]
        public void GecerliPrimary_DahaYeniAmaDogrulanmamisTemp_PrimaryKazanir()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(3)).Success);
            ProfileData yeni = Profil(4);
            yeni.LastSavedUtc = DateTime.UtcNow.AddHours(1).ToString("o");
            File.WriteAllText(Candidate, Json(yeni));
            Assert.AreEqual(3, ProfileStore.Load().RunsPlayed);
            Assert.IsFalse(File.Exists(Candidate));
        }

        [Test]
        public void EksikPrimary_GecerliTempVeBak_KesintiliReplaceIleriSarilir()
        {
            // İki-rename penceresi: primary yok, .bak eski tam, .writing yeni tam.
            File.WriteAllText(Backup, Json(Profil(5)));
            File.WriteAllText(Candidate, Json(Profil(6)));
            LogAssert.Expect(LogType.Warning, new Regex("interrupted"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(6, y.RunsPlayed, "doğrulanmış aday ileri sarılır (yeni tam profil)");
            Assert.AreEqual(ProfileLoadRecovery.RolledForwardCandidate, ProfileStore.LastLoadRecovery);
            Assert.IsTrue(File.Exists(Primary));
            Assert.IsFalse(File.Exists(Candidate));
            Assert.AreEqual(6, ProfileStore.Load().RunsPlayed, "ikinci yükleme normal");
        }

        [Test]
        public void EksikPrimary_BozukTemp_GecerliBak_YedekGeriYuklenir()
        {
            File.WriteAllText(Backup, Json(Profil(5)));
            File.WriteAllText(Candidate, Json(Profil(6)).Substring(0, 40));
            LogAssert.Expect(LogType.Warning, new Regex("backup was restored"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(5, y.RunsPlayed, "eski tam profil");
            Assert.AreEqual(ProfileLoadRecovery.RestoredBackup, ProfileStore.LastLoadRecovery);
            Assert.IsTrue(File.Exists(Primary), "yedek primary olarak yazıldı");
            Assert.IsFalse(File.Exists(Candidate));
            Assert.IsTrue(File.Exists(Backup), "yedek korunur");
        }

        [Test]
        public void EksikPrimary_HicbirSeyYok_TazeProfil_HataDegil()
        {
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(0, y.RunsPlayed);
            Assert.AreEqual(1, y.ProfileLevel);
            Assert.IsFalse(ProfileStore.LastLoadFellBack, "ilk açılış hata değildir");
            Assert.AreEqual(ProfileLoadStatus.Missing, ProfileStore.LastLoadStatus);
        }

        [Test]
        public void BozukPrimary_GecerliBak_YedekGeriYuklenir_BozukKorunur()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(5)).Success);
            Assert.IsTrue(ProfileStore.Save(Profil(6)).Success); // bak = 5
            File.WriteAllText(Primary, "{ \"SaveVersion\": 2, \"RunsPlayed\": 7, \"Profi"); // truncated
            File.WriteAllText(Candidate, Json(Profil(8)));
            LogAssert.Expect(LogType.Warning, new Regex("cut off"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(5, y.RunsPlayed, "yalnız doğrulanmış last-known-good yedekten (aday değil)");
            Assert.AreEqual(ProfileLoadRecovery.RestoredBackup, ProfileStore.LastLoadRecovery);
            Assert.IsTrue(ProfileStore.LastLoadFellBack);
            Assert.AreEqual(ProfileLoadStatus.Truncated, ProfileStore.LastLoadStatus);
            Assert.AreEqual(1, CorruptCount(), "bozuk bayt'lar .corrupt olarak korunur");
            Assert.IsFalse(File.Exists(Candidate), "doğrulanmamış aday silinir");
            Assert.AreEqual(5, ProfileStore.Load().RunsPlayed);
        }

        [Test]
        public void BozukPrimary_YedekYok_TazeProfil_BozukKorunur()
        {
            File.WriteAllText(Primary, "{{{ not json");
            LogAssert.Expect(LogType.Warning, new Regex("not valid JSON"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(1, y.ProfileLevel);
            Assert.AreEqual(ProfileLoadRecovery.FreshAfterCorruption, ProfileStore.LastLoadRecovery);
            Assert.IsTrue(ProfileStore.LastLoadFellBack);
            Assert.AreEqual(1, CorruptCount());
            Assert.IsFalse(File.Exists(Primary), "taze profil yükleme anında yazılmaz; ilk save yazar");
            Assert.IsTrue(ProfileStore.Save(y).Success);
            Assert.AreEqual(1, CorruptCount(), "save bozuk kopyayı silmez");
        }

        [Test]
        public void BozukPrimary_BozukBak_TazeProfil_YedekEzilmez()
        {
            File.WriteAllText(Primary, string.Empty);
            File.WriteAllText(Backup, "garbage");
            LogAssert.Expect(LogType.Warning, new Regex("empty"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(0, y.RunsPlayed);
            Assert.AreEqual(ProfileLoadStatus.Empty, ProfileStore.LastLoadStatus);
            Assert.AreEqual("garbage", File.ReadAllText(Backup), "geçersiz yedek dokunulmadan bırakılır");
        }

        // ------------------------------------------------------------------ teşhis

        [Test]
        public void Teshis_EmptyTruncatedMalformedFutureNoVersion_Ayrilir()
        {
            ProfileData d;
            File.WriteAllText(Primary, string.Empty);
            Assert.AreEqual(ProfileLoadStatus.Empty, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "   \n\t ");
            Assert.AreEqual(ProfileLoadStatus.Empty, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, Json(Profil(2)).Substring(0, 60));
            Assert.AreEqual(ProfileLoadStatus.Truncated, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{ this is not json }");
            Assert.AreEqual(ProfileLoadStatus.Malformed, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{\"SaveVersion\": 99}");
            Assert.AreEqual(ProfileLoadStatus.FutureVersion, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{\"SaveVersion\": 0, \"ProfileLevel\": 4}");
            Assert.AreEqual(ProfileLoadStatus.NoVersion, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{\"SaveVersion\": -3}");
            Assert.AreEqual(ProfileLoadStatus.NoVersion, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{}");
            Assert.AreEqual(ProfileLoadStatus.NoVersion, ProfileStore.ReadFile(Primary, out d), "anahtarsız/yabancı JSON geçerli profil sayılmaz");
            File.WriteAllText(Primary, "{\"foo\": 1, \"ProfileLevel\": 5}");
            Assert.AreEqual(ProfileLoadStatus.NoVersion, ProfileStore.ReadFile(Primary, out d));
            File.Delete(Primary);
            Assert.AreEqual(ProfileLoadStatus.Missing, ProfileStore.ReadFile(Primary, out d));
            Assert.IsNull(d);
        }

        [Test]
        public void FutureVersion_SaltOkunur_DosyaDokunulmaz_SaveReddedilir()
        {
            File.WriteAllText(Primary, "{\"SaveVersion\": 99, \"ProfileLevel\": 7, \"RunsPlayed\": 3}");
            string hash = HashOf(Primary);
            LogAssert.Expect(LogType.Warning, new Regex("newer version"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(1, y.ProfileLevel, "bellekte taze profil; dosya downgrade edilmez");
            Assert.IsTrue(ProfileStore.SaveLocked);
            Assert.AreEqual(ProfileLoadRecovery.ReadOnlyFutureVersion, ProfileStore.LastLoadRecovery);
            Assert.AreEqual(0, CorruptCount(), "future dosya .corrupt olarak taşınmaz");

            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            ProfileSaveResult r = ProfileStore.Save(Profil(1));
            Assert.IsFalse(r.Success);
            Assert.AreEqual(ProfileSaveStage.Refused, r.FailedStage);
            Assert.AreEqual(hash, HashOf(Primary), "future dosya bayt bayt aynı");
            Assert.IsFalse(File.Exists(Backup));
            Assert.IsFalse(File.Exists(Candidate));
        }

        [Test]
        public void EksikAlanlar_VarsayilanGuvenli_V2yeTasinir()
        {
            File.WriteAllText(Primary, "{\"SaveVersion\": 1, \"ProfileLevel\": 3}");
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(3, y.ProfileLevel);
            Assert.AreEqual(0, y.RunsPlayed);
            Assert.IsNotNull(y.Achievements);
            Assert.IsNotNull(y.AppliedCompletionIds);
            Assert.AreEqual(0, y.AppliedCompletionIds.Count);
            Assert.AreEqual(0, y.UnattributedKills);
            Assert.AreEqual(ProfileData.CurrentVersion, y.SaveVersion);
            Assert.AreEqual(string.Empty, y.LastSavedUtc);
        }

        [Test]
        public void NegatifVeAralikDisi_Sanitize_KopyaIdTemizlenir_KillInvariantiKorunur()
        {
            File.WriteAllText(Primary,
                "{\"SaveVersion\": 2, \"ProfileLevel\": -5, \"XpIntoLevel\": -1, \"RunsPlayed\": -9, \"TotalKills\": 20, " +
                "\"StandardKills\": 4, \"EliteKills\": -2, \"Achievements\": [\"a\", \"a\", \"\", null, \"b\"], " +
                "\"UnlockedWeapons\": [\"AssaultRifle\", \"AssaultRifle\"], \"AppliedCompletionIds\": [\"x\", \"x\"], \"LastSavedUtc\": null}");
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(1, y.ProfileLevel);
            Assert.AreEqual(0, y.XpIntoLevel);
            Assert.AreEqual(0, y.RunsPlayed);
            Assert.AreEqual(0, y.EliteKills);
            Assert.AreEqual(4, y.StandardKills);
            Assert.AreEqual(16, y.UnattributedKills, "20 − 4: eksik kill'ler uydurulmaz, atanmamış kovaya");
            Assert.AreEqual(20, y.TotalKills);
            CollectionAssert.AreEqual(new[] { "a", "b" }, y.Achievements);
            CollectionAssert.AreEqual(new[] { "AssaultRifle" }, y.UnlockedWeapons);
            CollectionAssert.AreEqual(new[] { "x" }, y.AppliedCompletionIds);
            Assert.AreEqual(string.Empty, y.LastSavedUtc);
            Assert.IsTrue(y.IsValid());
        }

        // ------------------------------------------------------------------ migration

        private const string V1Document =
            "{\n    \"SaveVersion\": 1,\n    \"ProfileLevel\": 3,\n    \"XpIntoLevel\": 40,\n    \"TotalXpEarned\": 315,\n" +
            "    \"HighestRound\": 6,\n    \"RunsPlayed\": 12,\n    \"RoundsCompleted\": 9,\n    \"TotalKills\": 40,\n" +
            "    \"StandardKills\": 31,\n    \"SpecialKills\": 3,\n    \"EliteKills\": 1,\n    \"UltraKills\": 0,\n" +
            "    \"ArmorPiecesBroken\": 5,\n    \"BestRunXp\": 120,\n    \"Achievements\": [\"first_blood\", \"armor_breaker\"],\n" +
            "    \"UnlockedWeapons\": [\"AssaultRifle\", \"AutoSmg\", \"CombatShotgun\"],\n" +
            "    \"UnlockedBranches\": [\"Piercing\", \"Explosion\"],\n    \"LastSavedUtc\": \"2026-09-12T15:03:27.0000000Z\"\n}";

        [Test]
        public void V1_V2_KayipsizMigration_AlanAlan()
        {
            File.WriteAllText(Primary, V1Document);
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(2, y.SaveVersion);
            Assert.AreEqual(3, y.ProfileLevel);
            Assert.AreEqual(40, y.XpIntoLevel);
            Assert.AreEqual(315, y.TotalXpEarned);
            Assert.AreEqual(6, y.HighestRound);
            Assert.AreEqual(12, y.RunsPlayed);
            Assert.AreEqual(9, y.RoundsCompleted);
            Assert.AreEqual(40, y.TotalKills, "legacy toplam korunur");
            Assert.AreEqual(31, y.StandardKills);
            Assert.AreEqual(3, y.SpecialKills);
            Assert.AreEqual(1, y.EliteKills);
            Assert.AreEqual(0, y.UltraKills);
            Assert.AreEqual(5, y.UnattributedKills, "40 − 35: kayıpsız, tier'lara dağıtılmadı");
            Assert.AreEqual(5, y.ArmorPiecesBroken);
            Assert.AreEqual(120, y.BestRunXp);
            CollectionAssert.AreEqual(new[] { "first_blood", "armor_breaker" }, y.Achievements);
            CollectionAssert.AreEqual(new[] { "AssaultRifle", "AutoSmg", "CombatShotgun" }, y.UnlockedWeapons);
            CollectionAssert.AreEqual(new[] { "Piercing", "Explosion" }, y.UnlockedBranches);
            Assert.AreEqual(0, y.AppliedCompletionIds.Count, "v1 koşuları replay edilemez; kimlik listesi boş başlar");
            Assert.AreEqual("2026-09-12T15:03:27.0000000Z", y.LastSavedUtc);
            Assert.IsTrue(y.IsValid());

            // Gidiş-dönüş: yaz, yeniden yükle, alan alan aynı (LastSavedUtc hariç).
            Assert.IsTrue(ProfileStore.Save(y).Success);
            ProfileData z = ProfileStore.Load();
            z.LastSavedUtc = y.LastSavedUtc = string.Empty;
            Assert.AreEqual(Json(y), Json(z));
        }

        [Test]
        public void Migration_Idempotent_VeMevcutSurumeDokunmaz()
        {
            ProfileData a;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileMigration.Parse(V1Document, out a));
            string bir = Json(a);
            ProfileMigration.Migrate(a);
            ProfileMigration.Migrate(a);
            Assert.AreEqual(bir, Json(a), "aynı belgede tekrar migration hiçbir şeyi değiştirmez");

            ProfileData v2 = Profil(4);
            v2.UnattributedKills = 2;
            v2.TotalKills += 2;
            v2.Sanitize();
            string once = Json(v2);
            ProfileMigration.Migrate(v2);
            Assert.AreEqual(once, Json(v2), "v2 belgesinde UnattributedKills sıfırlanmaz");
        }

        [Test]
        public void CokluLoadSaveDongusu_VeriKaybetmez()
        {
            File.WriteAllText(Primary, V1Document);
            string referans = null;
            for (int i = 0; i < 5; i++)
            {
                ProfileData y = ProfileStore.Load();
                Assert.IsTrue(ProfileStore.Save(y).Success, "döngü " + i);
                y.LastSavedUtc = string.Empty;
                string simdi = Json(y);
                if (referans == null)
                {
                    referans = simdi;
                }

                Assert.AreEqual(referans, simdi, "döngü " + i + " veri değiştirdi");
            }

            Assert.AreEqual(0, CorruptCount());
            Assert.IsFalse(File.Exists(Candidate));
        }

        // ------------------------------------------------------------------ sahiplik

        [Test]
        public void Sahiplik_CanliBaskaOrnek_SaltOkunur_SaveReddedilir()
        {
            File.WriteAllText(Lock, "pid=424242\nname=Deaths Demolition - Playable Concept\n");
            ProfileStore.ProcessLivenessProbe = (pid, name) => pid == 424242 && name.StartsWith("Deaths");
            File.WriteAllText(Primary, Json(Profil(2)));
            LogAssert.Expect(LogType.Warning, new Regex("Another copy"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(2, y.RunsPlayed, "profil yine okunur");
            Assert.IsTrue(ProfileStore.SaveLocked);
            Assert.IsFalse(ProfileStore.OwnsProfile);
            StringAssert.Contains("Another copy", ProfileStore.SaveLockReason);
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            Assert.IsFalse(ProfileStore.Save(Profil(3)).Success);
            LogAssert.Expect(LogType.Warning, new Regex("Another copy"));
            Assert.AreEqual(2, ProfileStore.Load().RunsPlayed, "başka örneğin dosyası ezilmedi");
            Assert.AreEqual("pid=424242\nname=Deaths Demolition - Playable Concept\n", File.ReadAllText(Lock), "kilit ele geçirilmedi");
        }

        [Test]
        public void Sahiplik_OluSurecKilidi_DevralinirVeCikistaBirakilir()
        {
            File.WriteAllText(Lock, "pid=424242\nname=Deaths Demolition - Playable Concept\n");
            ProfileStore.ProcessLivenessProbe = (pid, name) => false;
            ProfileStore.Load();
            Assert.IsFalse(ProfileStore.SaveLocked);
            Assert.IsTrue(ProfileStore.OwnsProfile);
            int pid1;
            string name1;
            Assert.IsTrue(ProfileStore.ParseLock(File.ReadAllText(Lock), out pid1, out name1));
            Assert.AreEqual(System.Diagnostics.Process.GetCurrentProcess().Id, pid1, "kilit bu sürecin pid'ini taşır");
            Assert.IsTrue(ProfileStore.Save(Profil(1)).Success);

            ProfileStore.ReleaseOwnership();
            Assert.IsFalse(File.Exists(Lock), "çıkışta kilit bırakılır");
            Assert.IsFalse(ProfileStore.OwnsProfile);
        }

        [Test]
        public void Sahiplik_AyniPid_HerZamanSahip_ProbeCagrilmaz()
        {
            int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            File.WriteAllText(Lock, "pid=" + me + "\nname=whatever\n");
            bool probeCagrildi = false;
            ProfileStore.ProcessLivenessProbe = (pid, name) => { probeCagrildi = true; return true; };
            ProfileStore.Load();
            Assert.IsFalse(probeCagrildi, "aynı pid = biziz; canlılık sorgulanmaz");
            Assert.IsTrue(ProfileStore.OwnsProfile);
            Assert.IsFalse(ProfileStore.SaveLocked);
        }

        [Test]
        public void ParseLock_BozukIcerik()
        {
            int pid;
            string name;
            Assert.IsFalse(ProfileStore.ParseLock(null, out pid, out name));
            Assert.IsFalse(ProfileStore.ParseLock("nonsense", out pid, out name));
            Assert.IsFalse(ProfileStore.ParseLock("pid=abc\n", out pid, out name));
            Assert.IsTrue(ProfileStore.ParseLock("name=X\npid=17\n", out pid, out name));
            Assert.AreEqual(17, pid);
            Assert.AreEqual("X", name);
        }

        [Test]
        public void Delete_YanDosyalariDaKaldirir_BozukKopyayiKorur()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(1)).Success);
            Assert.IsTrue(ProfileStore.Save(Profil(2)).Success);
            File.WriteAllText(Candidate, "x");
            File.WriteAllText(Primary + ProfileStore.CorruptSuffix + ".1.json", "old");
            ProfileStore.Delete();
            Assert.IsFalse(File.Exists(Primary));
            Assert.IsFalse(File.Exists(Backup));
            Assert.IsFalse(File.Exists(Candidate));
            Assert.AreEqual(1, CorruptCount(), "kanıt dosyaları debug reset'te bile silinmez");
        }
    }
}
