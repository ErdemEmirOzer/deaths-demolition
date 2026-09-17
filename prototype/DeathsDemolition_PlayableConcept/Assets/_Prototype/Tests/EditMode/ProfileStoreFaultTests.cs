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
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp009-store");

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
            ProfileStore.LockAcquisitionFault = null;
            TryDeleteDir(Dir);
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            ProfileStore.SaveStageHook = null;
            ProfileStore.LockAcquisitionFault = null;
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
            ProfileStore.LockAcquisitionFault = null;
            ProfileStore.SetPathOverride(null);
        }

        // ------------------------------------------------------------------ yardımcılar

        private const string Soy = "0123456789abcdef0123456789abcdef";

        private static ProfileData Profil(int runs, int level = 1)
        {
            var p = new ProfileData { RunsPlayed = runs, ProfileLevel = level, TotalKills = runs * 3, StandardKills = runs * 3, TotalXpEarned = runs * 10 };
            p.UnlockedWeapons.Add("AssaultRifle");
            p.Achievements.Add("syn_" + runs);
            p.ProfileLineageId = Soy;
            p.LastAppliedCompletionOrdinal = runs;
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
            // WP-009H: başka canlı süreç lease'i tutuyor (gerçek fcntl süreci): primary bozuk, .writing ve .bak
            // var → salt-okunur yükleme hiçbirini taşımaz/silmez; doğrulanmış yedek yalnız bellekte kullanılır.
            if (!File.Exists("/usr/bin/python3")) Assert.Ignore("python3 yok; çapraz süreç kanıtı probe L1/L2");
            File.WriteAllText(Primary, "{{{ not json");
            File.WriteAllText(Candidate, Json(Profil(9)));
            File.WriteAllText(Backup, Json(Profil(4)));
            ProfileStore.SetPathOverride(null); // henüz lease yok
            string hata;
            System.Diagnostics.Process diger = KilitTutanSurec(Lock, 20, out hata);
            Assert.IsNotNull(diger, "yardımcı süreç kilidi alamadı: " + hata);
            try
            {
                ProfileStore.SetPathOverride(Primary);
                LogAssert.Expect(LogType.Warning, new Regex("Another copy"));
                ProfileData y = ProfileStore.Load();
                Assert.AreEqual(4, y.RunsPlayed, "salt-okunur: doğrulanmış yedek bellekte kullanılır");
                Assert.AreEqual("{{{ not json", File.ReadAllText(Primary), "bozuk primary yana alınmadı");
                Assert.IsTrue(File.Exists(Candidate), "diğer örneğin adayı silinmedi");
                Assert.AreEqual(0, CorruptCount());
                Assert.IsTrue(ProfileStore.SaveLocked);
                Assert.IsTrue(File.Exists(Lock), "başkasının kilit dosyası silinmez");
            }
            finally
            {
                Bitir(diger);
            }
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
            Assert.IsTrue(ProfileData.IsLineageId(y.ProfileLineageId), "taze profil lineage taşır (v3)");
            Assert.AreEqual(0, y.LastAppliedCompletionOrdinal);
            Assert.IsFalse(ProfileStore.LastLoadFellBack, "ilk açılış hata değildir");
            Assert.AreEqual(ProfileLoadStatus.Missing, ProfileStore.LastLoadStatus);
        }

        [Test]
        public void BozukPrimary_GecerliBak_YedekGeriYuklenir_BozukKorunur()
        {
            Assert.IsTrue(ProfileStore.Save(Profil(5)).Success);
            Assert.IsTrue(ProfileStore.Save(Profil(6)).Success); // bak = 5
            File.WriteAllText(Primary, "{ \"SaveVersion\": 3, \"RunsPlayed\": 7, \"Profi"); // truncated
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
            File.WriteAllText(Primary, "{\"SaveVersion\": 3, \"ProfileLevel\": 2}");
            Assert.AreEqual(ProfileLoadStatus.InvalidLineage, ProfileStore.ReadFile(Primary, out d), "v3 belgesinde lineage yok → geçersiz (sessiz onarım yok)");
            File.WriteAllText(Primary, "{\"SaveVersion\": 3, \"ProfileLineageId\": \"ZZZ\"}");
            Assert.AreEqual(ProfileLoadStatus.InvalidLineage, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{\"SaveVersion\": 3, \"ProfileLineageId\": \"" + Soy + "\", \"LastAppliedCompletionOrdinal\": -4}");
            Assert.AreEqual(ProfileLoadStatus.Malformed, ProfileStore.ReadFile(Primary, out d), "negatif ordinal onarılmaz; geçersiz belge");
            File.WriteAllText(Primary, "{\"SaveVersion\": 5, \"ProfileLineageId\": \"" + Soy + "\"}");
            Assert.AreEqual(ProfileLoadStatus.FutureVersion, ProfileStore.ReadFile(Primary, out d));
            File.WriteAllText(Primary, "{\"SaveVersion\": 5}");
            Assert.AreEqual(ProfileLoadStatus.FutureVersion, ProfileStore.ReadFile(Primary, out d), "v5 lineage'sız da future: dokunulmaz (WP-023: v4 artık güncel)");
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
        public void EksikAlanlar_VarsayilanGuvenli_V3eTasinir()
        {
            File.WriteAllText(Primary, "{\"SaveVersion\": 1, \"ProfileLevel\": 3}");
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(3, y.ProfileLevel);
            Assert.AreEqual(0, y.RunsPlayed);
            Assert.IsNotNull(y.Achievements);
            Assert.IsTrue(ProfileData.IsLineageId(y.ProfileLineageId), "v1 → v3: lineage üretildi");
            Assert.AreEqual(0, y.LastAppliedCompletionOrdinal, "yeni soyda uygulanmış completion yok");
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
            Assert.IsTrue(ProfileData.IsLineageId(y.ProfileLineageId));
            Assert.AreEqual(0, y.LastAppliedCompletionOrdinal);
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
        public void V1_V3_KayipsizMigration_AlanAlan()
        {
            File.WriteAllText(Primary, V1Document);
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(ProfileData.CurrentVersion, y.SaveVersion);
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
            Assert.IsTrue(ProfileData.IsLineageId(y.ProfileLineageId), "v1 → v3: tek seferlik lineage");
            Assert.AreEqual(0, y.LastAppliedCompletionOrdinal, "v1 koşuları ordinal'e çevrilmez; 0'dan başlar");
            Assert.AreEqual(ProfileData.CurrentVersion, y.SaveVersion);
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

            ProfileData v3 = Profil(4);
            v3.UnattributedKills = 2;
            v3.TotalKills += 2;
            v3.Sanitize();
            string once = Json(v3);
            ProfileMigration.Migrate(v3);
            Assert.AreEqual(once, Json(v3), "v3 belgesinde UnattributedKills, lineage ve ordinal değişmez");
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

        // ------------------------------------------------------------------ sahiplik (fcntl lease)

        /// <summary>
        /// Gerçek ikinci süreç: python3 aynı kilit dosyasına fcntl(F_SETLK, LOCK_EX|LOCK_NB) alır ve
        /// verilen süre tutar (Mono'nun FileStream.Lock'u ile aynı kilit ailesi). python3 yoksa test
        /// atlanır (Assert.Ignore) — bu makinede çapraz süreç kanıtı ayrıca ProfilePersistenceProbe
        /// E/F fazlarıyla (iki gerçek Player) alınır.
        /// </summary>
        private static System.Diagnostics.Process KilitTutanSurec(string lockPath, double saniye, out string hata)
        {
            hata = null;
            try
            {
                string script = "import fcntl,sys,time\n" +
                                "f=open(sys.argv[1],'a+')\n" +
                                "try:\n    fcntl.lockf(f, fcntl.LOCK_EX|fcntl.LOCK_NB)\nexcept OSError as e:\n    print('LOCKFAIL',e); sys.exit(3)\n" +
                                "print('LOCKED',flush=True)\n" +
                                "time.sleep(float(sys.argv[2]))\n";
                string scriptPath = Path.Combine(Dir, "lock-helper.py");
                File.WriteAllText(scriptPath, script);
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/python3", "\"" + scriptPath + "\" \"" + lockPath + "\" " + saniye.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                string line = proc.StandardOutput.ReadLine();
                if (line == null || !line.StartsWith("LOCKED"))
                {
                    hata = "helper: " + line + " " + proc.StandardError.ReadToEnd();
                    try { proc.Kill(); } catch (Exception) { }
                    return null;
                }

                return proc;
            }
            catch (Exception e)
            {
                hata = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private static void Bitir(System.Diagnostics.Process proc)
        {
            if (proc == null) return;
            try { if (!proc.HasExited) proc.Kill(); proc.WaitForExit(5000); } catch (Exception) { }
            proc.Dispose();
        }

        [Test]
        public void Sahiplik_BaskaSurecKilidiTutuyor_SaltOkunur_SaveResetReddedilir()
        {
            if (!File.Exists("/usr/bin/python3")) Assert.Ignore("python3 yok; çapraz süreç kanıtı probe E/F");
            File.WriteAllText(Primary, Json(Profil(2)));
            ProfileStore.SetPathOverride(null); // henüz lease yok
            string hata;
            System.Diagnostics.Process diger = KilitTutanSurec(Lock, 20, out hata);
            Assert.IsNotNull(diger, "yardımcı süreç kilidi alamadı: " + hata);
            try
            {
                ProfileStore.SetPathOverride(Primary);
                LogAssert.Expect(LogType.Warning, new Regex("Another copy"));
                ProfileData y = ProfileStore.Load();
                Assert.AreEqual(2, y.RunsPlayed, "profil yine okunur");
                Assert.IsTrue(ProfileStore.SaveLocked, "başka canlı süreç lease'i tutuyor → salt-okunur");
                Assert.IsFalse(ProfileStore.OwnsProfile);
                StringAssert.Contains("Another copy", ProfileStore.SaveLockReason);
                LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
                Assert.IsFalse(ProfileStore.Save(Profil(3)).Success, "kaybeden save yapamaz");
                LogAssert.Expect(LogType.Warning, new Regex("Reset refused"));
                Assert.IsNull(ProfileStore.Reset(), "kaybeden reset yapamaz");
                Assert.AreEqual(2, ProfileStore.Load().RunsPlayed, "dosya ezilmedi");
                Assert.IsTrue(File.Exists(Lock), "başkasının kilit dosyası silinmez");
            }
            finally
            {
                Bitir(diger);
            }

            // Sahip çıkınca (crash/exit → çekirdek bırakır) sonraki yükleme lease'i alır.
            ProfileStore.SetPathOverride(null);
            ProfileStore.SetPathOverride(Primary);
            Assert.AreEqual(2, ProfileStore.Load().RunsPlayed);
            Assert.IsFalse(ProfileStore.SaveLocked, "ölü sahibin kilidi devralınır");
            Assert.IsTrue(ProfileStore.OwnsProfile);
            Assert.IsTrue(ProfileStore.Save(Profil(3)).Success);
        }

        [Test]
        public void Sahiplik_BizTutarken_BaskaSurecAlamaz_CikistaBirakilir()
        {
            if (!File.Exists("/usr/bin/python3")) Assert.Ignore("python3 yok; çapraz süreç kanıtı probe E/F");
            ProfileStore.Load();
            Assert.IsTrue(ProfileStore.OwnsProfile);
            string hata;
            System.Diagnostics.Process diger = KilitTutanSurec(Lock, 5, out hata);
            Assert.IsNull(diger, "biz tutarken başka süreç kilidi alamamalı (LOCKFAIL beklenir): " + hata);
            Assert.IsTrue(hata != null && hata.Contains("LOCKFAIL"), "yardımcı LOCKFAIL demeli: " + hata);
            Bitir(diger);

            // Aynı süreçte ikinci Load aynı yol → hâlâ sahip (tek açılış; ikinci açılış yok).
            ProfileStore.Load();
            Assert.IsTrue(ProfileStore.OwnsProfile);
            Assert.IsFalse(ProfileStore.SaveLocked);

            // Release (çıkış / SetPathOverride) → başka süreç alabilir; kilit dosyası silinmez.
            ProfileStore.ReleaseOwnership();
            Assert.IsFalse(ProfileStore.OwnsProfile);
            Assert.IsTrue(File.Exists(Lock), "release kilit dosyasını silmez");
            diger = KilitTutanSurec(Lock, 5, out hata);
            Assert.IsNotNull(diger, "release sonrası başka süreç kilidi alabilmeli: " + hata);
            Bitir(diger);
        }

        [Test]
        public void Sahiplik_KilitAlinamazsa_FailClosed_SaltOkunur()
        {
            ProfileStore.LockAcquisitionFault = () => { throw new UnauthorizedAccessException("injected lock fault"); };
            File.WriteAllText(Primary, Json(Profil(2)));
            LogAssert.Expect(LogType.Warning, new Regex("could not be locked"));
            ProfileData y = ProfileStore.Load();
            Assert.AreEqual(2, y.RunsPlayed, "profil okunur");
            Assert.IsTrue(ProfileStore.SaveLocked, "kilit alınamadı → salt-okunur (continuing-without-lock yolu yok)");
            Assert.IsFalse(ProfileStore.OwnsProfile);
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            Assert.IsFalse(ProfileStore.Save(Profil(3)).Success);
            Assert.AreEqual(ProfileSaveStage.Refused, ProfileStore.LastSaveResult.FailedStage);
            LogAssert.Expect(LogType.Warning, new Regex("Reset refused"));
            Assert.IsNull(ProfileStore.Reset());
            ProfileStore.LockAcquisitionFault = null;

            // Aynı süreçte sonraki edinme (path değişimi) başarılı: kalıcı bir kilitlenme değildir.
            ProfileStore.SetPathOverride(null);
            ProfileStore.SetPathOverride(Primary);
            ProfileStore.Load();
            Assert.IsFalse(ProfileStore.SaveLocked);
            Assert.IsTrue(ProfileStore.OwnsProfile);
        }

        [Test]
        public void Sahiplik_KilitDosyasiMetniYetkiDegildir_DeleteVeResetDokunmaz()
        {
            ProfileStore.Load();
            Assert.IsTrue(ProfileStore.OwnsProfile);
            Assert.IsTrue(ProfileStore.Save(Profil(1)).Success);
            Assert.IsTrue(File.Exists(Lock));
            ProfileStore.Delete();
            Assert.IsTrue(File.Exists(Lock), "Delete kilit dosyasına dokunmaz");
            Assert.IsNotNull(ProfileStore.Reset());
            Assert.IsTrue(File.Exists(Lock), "Reset kilit dosyasına dokunmaz");
            Assert.IsTrue(ProfileStore.OwnsProfile, "lease korunur");
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
