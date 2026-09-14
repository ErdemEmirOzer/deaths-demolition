using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Profile.CompletionAuthority;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-009H · Completion identity authority: token biçimi, doğrulama sırası, yetki yaşam
    /// döngüsü, 10.000'lik saf seri (disk yok), taşma sınırı, reset/lineage, geçersiz token matrisi (§11.E).
    /// </summary>
    [TestFixture]
    [Category("WP009H")]
    public sealed class CompletionAuthorityTests
    {
        private const string Soy = "0123456789abcdef0123456789abcdef";
        private const string Yabanci = "fedcba9876543210fedcba9876543210";

        private static readonly string TestDir = Path.Combine(Application.temporaryCachePath, "dd-editmode-wp009h-authority");
        private static readonly string TestPath = Path.Combine(TestDir, "prototype-profile.json");
        private static readonly string RealPath = Path.Combine(Application.persistentDataPath, ProfileStore.FileName);
        private string _realHashBefore;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = Hash(RealPath);
            Directory.CreateDirectory(TestDir);
            ProfileStore.SetPathOverride(TestPath);
            Bosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Bosalt();
            ProfileStore.SetPathOverride(null);
            try { Directory.Delete(TestDir, true); } catch (Exception) { }
            Assert.AreEqual(_realHashBefore, Hash(RealPath), "gerçek profil değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path);
            ProfileService.ResetProfile();
            Assert.IsFalse(ProfileStore.SaveLocked, "fixture salt-okunur olmamalı (pozitif kontrol)");
            Assert.IsTrue(ProfileService.Save(), "pozitif kontrol: save çalışıyor");
            CompletionAuthority.InvalidateLive();
        }

        private static void Bosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        private static string Hash(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static ProfileData Profil(long ordinal, string soy = Soy)
        {
            var p = new ProfileData { ProfileLineageId = soy, LastAppliedCompletionOrdinal = ordinal };
            p.Sanitize();
            return p;
        }

        private static CompletionTokenVerdict Dogrula(string token, ProfileData p)
        {
            CompletionToken t;
            return CompletionAuthority.Validate(token, p, out t);
        }

        // ------------------------------------------------------------------ biçim

        [Test]
        public void Token_Bicimi_VeParse()
        {
            string t = CompletionAuthority.IssueForNewRun();
            Assert.IsNotNull(t);
            StringAssert.StartsWith("ddc1:", t);
            CompletionToken parsed;
            Assert.AreEqual(CompletionTokenVerdict.Valid, CompletionAuthority.TryParse(t, out parsed));
            Assert.AreEqual(ProfileService.Current.ProfileLineageId, parsed.Lineage);
            Assert.AreEqual(1, parsed.Ordinal);
            Assert.AreEqual(32, parsed.Nonce.Length);
            Assert.IsTrue(CompletionAuthority.HasLiveCapability);
            Assert.AreEqual(1, CompletionAuthority.LiveOrdinal);
        }

        [TestCase(null, CompletionTokenVerdict.Missing)]
        [TestCase("", CompletionTokenVerdict.Missing)]
        [TestCase("not a token", CompletionTokenVerdict.Malformed)]
        [TestCase("a86ac76958af4dfc9188be19544ba567", CompletionTokenVerdict.Malformed, TestName = "LegacyV2RawGuid")]
        [TestCase("ddc1:" + Soy + ":1", CompletionTokenVerdict.Malformed, TestName = "EksikBolum")]
        [TestCase("ddc1:" + Soy + ":1:abc", CompletionTokenVerdict.Malformed, TestName = "KisaNonce")]
        [TestCase("ddc1:" + Soy + ":1:0123456789ABCDEF0123456789ABCDEF", CompletionTokenVerdict.Malformed, TestName = "BuyukHarfNonce")]
        [TestCase("ddc1:ZZZ:1:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.Malformed, TestName = "BozukLineage")]
        [TestCase("ddc1:" + Soy + ":0:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.InvalidOrdinal, TestName = "OrdinalSifir")]
        [TestCase("ddc1:" + Soy + ":-3:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.InvalidOrdinal, TestName = "OrdinalNegatif")]
        [TestCase("ddc1:" + Soy + ":007:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.InvalidOrdinal, TestName = "OrdinalBasSifir")]
        [TestCase("ddc1:" + Soy + ": 7:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.InvalidOrdinal, TestName = "OrdinalBosluk")]
        [TestCase("ddc1:" + Soy + ":99999999999999999999:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.InvalidOrdinal, TestName = "OrdinalTasma")]
        [TestCase("ddc2:" + Soy + ":1:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.UnsupportedVersion, TestName = "FutureSchema")]
        [TestCase("ddc0:" + Soy + ":1:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.UnsupportedVersion, TestName = "EskiSchema")]
        [TestCase("xxxx:" + Soy + ":1:0123456789abcdef0123456789abcdef", CompletionTokenVerdict.Malformed, TestName = "BilinmeyenTag")]
        public void GecersizBicimler_FailClosed(string token, CompletionTokenVerdict beklenen)
        {
            ProfileData p = Profil(0);
            Assert.AreEqual(beklenen, Dogrula(token, p));
            CompletionToken t;
            Assert.AreEqual(beklenen, CompletionAuthority.TryParse(token, out t));
        }

        // ------------------------------------------------------------------ doğrulama sırası

        [Test]
        public void DogrulamaSirasi_Lineage_Mark_Sira_Yetki()
        {
            string nonce = TestSeam.NewNonce();
            ProfileData p = Profil(5);
            Assert.AreEqual(CompletionTokenVerdict.ForeignLineage, Dogrula(TestSeam.Forge(Yabanci, 6, nonce), p), "yabancı soy (Last+1 olsa da)");
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(TestSeam.Forge(Soy, 5, nonce), p), "mark'ta");
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(TestSeam.Forge(Soy, 1, nonce), p), "mark gerisinde");
            Assert.AreEqual(CompletionTokenVerdict.OrdinalGap, Dogrula(TestSeam.Forge(Soy, 7, nonce), p), "boşluk");
            Assert.AreEqual(CompletionTokenVerdict.OrdinalGap, Dogrula(TestSeam.Forge(Soy, long.MaxValue, nonce), p), "long.MaxValue boşluk");
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, Dogrula(TestSeam.Forge(Soy, 6, nonce), p), "doğru sıra ama bu süreç vermedi (random/unissued)");

            // Canlı yetki: aynı ordinal farklı nonce reddedilir; canlı olan geçer.
            string canli = TestSeam.Forge(Soy, 6, TestSeam.NewNonce());
            TestSeam.ArmLive(canli);
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, Dogrula(TestSeam.Forge(Soy, 6, nonce), p), "aynı ordinal farklı nonce");
            Assert.AreEqual(CompletionTokenVerdict.Valid, Dogrula(canli, p));

            // Tüketim: yalnız canlı token; başka token no-op.
            CompletionAuthority.Consume(TestSeam.Forge(Soy, 6, nonce));
            Assert.IsTrue(CompletionAuthority.HasLiveCapability, "başka token tüketmez");
            CompletionAuthority.Consume(canli);
            Assert.IsFalse(CompletionAuthority.HasLiveCapability);
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, Dogrula(canli, p), "tüketilmiş yetki (mark ilerlemeden önce) artık canlı değil");
        }

        [Test]
        public void YeniIssue_OncekiYetkiyiGecersizKilar_AbortVeResetGibi()
        {
            string a = CompletionAuthority.IssueForNewRun();
            string b = CompletionAuthority.IssueForNewRun();
            Assert.AreNotEqual(a, b, "aynı ordinal, yeni nonce");
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, Dogrula(a, p), "eski (stale) yetki");
            Assert.AreEqual(CompletionTokenVerdict.Valid, Dogrula(b, p));

            CompletionAuthority.InvalidateLive(); // abort
            Assert.AreEqual(CompletionTokenVerdict.NotIssued, Dogrula(b, p), "abort edilen koşunun yetkisi");
            Assert.AreEqual(0, p.LastAppliedCompletionOrdinal, "abort ordinal'i değiştirmez");
        }

        [Test]
        public void Reset_YeniLineage_EskiTokenYabanci()
        {
            string eskiSoy = ProfileService.Current.ProfileLineageId;
            string t = CompletionAuthority.IssueForNewRun();
            ProfileService.ResetProfile();
            Assert.AreNotEqual(eskiSoy, ProfileService.Current.ProfileLineageId, "reset yeni lineage üretir");
            Assert.AreEqual(0, ProfileService.Current.LastAppliedCompletionOrdinal);
            Assert.IsFalse(CompletionAuthority.HasLiveCapability, "reset canlı yetkiyi geçersiz kılar");
            Assert.AreEqual(CompletionTokenVerdict.ForeignLineage, Dogrula(t, ProfileService.Current), "reset öncesi token yabancı soy");

            // Reset'ten sonra eski token ApplyRun'da mutasyonsuz reddedilir.
            LogAssert.Expect(LogType.Warning, new Regex("not valid"));
            RunRewardReport r = ProfileService.ApplyRun(new RunSummary { CompletionId = t, ReachedRound = 1, KillXp = 5, TotalKills = 5, StandardKills = 5 });
            Assert.AreEqual(ProfileCommitOutcome.InvalidIdentity, r.Outcome);
            Assert.AreEqual(0, ProfileService.Current.RunsPlayed);
        }

        // ------------------------------------------------------------------ taşma

        [Test]
        public void Ordinal_TasmaSiniri_EskiyiGecerliKilmaz()
        {
            ProfileData p = ProfileService.Current;
            p.LastAppliedCompletionOrdinal = long.MaxValue - 1;
            Assert.IsTrue(ProfileService.Save(), "sınıra yakın mark yazılabilir");
            string son = CompletionAuthority.IssueForNewRun();
            Assert.IsNotNull(son, "MaxValue-1 → bir koşu daha verilir (MaxValue)");
            CompletionToken t;
            Assert.AreEqual(CompletionTokenVerdict.Valid, CompletionAuthority.TryParse(son, out t));
            Assert.AreEqual(long.MaxValue, t.Ordinal);
            Assert.IsTrue(ProfileService.ApplyRun(new RunSummary { CompletionId = son, ReachedRound = 1 }).Applied);
            Assert.AreEqual(long.MaxValue, ProfileService.Current.LastAppliedCompletionOrdinal);

            LogAssert.Expect(LogType.Warning, new Regex("exhausted"));
            Assert.IsNull(CompletionAuthority.IssueForNewRun(), "mark MaxValue → yeni kimlik verilmez; wraparound yok");
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(son, ProfileService.Current), "son token bile uygulanmış sayılır");
            string eski = TestSeam.Forge(ProfileService.Current.ProfileLineageId, 1, TestSeam.NewNonce());
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(eski, ProfileService.Current), "hiçbir eski ordinal yeniden geçerli olmaz");
            LogAssert.Expect(LogType.Warning, new Regex("no completion identity"));
            Assert.AreEqual(ProfileCommitOutcome.MissingIdentity, ProfileService.ApplyRun(new RunSummary { CompletionId = CompletionAuthority.IssueForNewRun(), ReachedRound = 1 }).Outcome);
        }

        // ------------------------------------------------------------------ saf uzun seri

        [Test]
        public void SafSeri_10000Completion_IlkOrtaSonReddedilir_DurumSabitBoyut()
        {
            // Disk yok: authority + bellek içi profil; 10.000 issue → validate → mark ilerlet → consume.
            const int n = 10000;
            ProfileData p = Profil(0);
            string ilk = null, orta = null, sonToken = null;
            for (int i = 1; i <= n; i++)
            {
                string nonce = TestSeam.NewNonce();
                string t = TestSeam.Forge(Soy, i, nonce);
                TestSeam.ArmLive(t);
                Assert.AreEqual(CompletionTokenVerdict.Valid, Dogrula(t, p), "issue " + i);
                p.LastAppliedCompletionOrdinal = i; // transaction'ın yaptığı
                CompletionAuthority.Consume(t);
                if (i == 1) ilk = t;
                if (i == n / 2) orta = t;
                if (i == n) sonToken = t;
            }

            Assert.AreEqual(n, p.LastAppliedCompletionOrdinal);
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(ilk, p));
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(orta, p));
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, Dogrula(sonToken, p));
            Assert.IsTrue(p.IsValid());
            string json = JsonUtility.ToJson(p);
            StringAssert.DoesNotContain("AppliedCompletionIds", json);
            Assert.Less(json.Length, 900, "anti-replay durumu iki sabit alan; 10.000 koşu sonrası belge büyümez: " + json.Length + " bayt");
        }

        [Test]
        public void ReleaseDll_TestSeamVeIssuer_YalnizEditorDevBuild()
        {
            // Kaynak kilidi: seam'ler #if UNITY_EDITOR || DEVELOPMENT_BUILD altında; final kapı release dll'i ayrıca tarar.
            string kaynak = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype", "Profile", "CompletionAuthority.cs"));
            int seam = kaynak.IndexOf("public static class TestSeam", StringComparison.Ordinal);
            int guard = kaynak.LastIndexOf("#if UNITY_EDITOR || DEVELOPMENT_BUILD", seam, StringComparison.Ordinal);
            Assert.Greater(seam, 0);
            Assert.Greater(guard, 0, "TestSeam #if altında olmalı");
            string store = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype", "Profile", "ProfileStore.cs"));
            StringAssert.DoesNotContain("continuing without it", store, "fail-open kilit yolu kaldırıldı");
        }
    }
}
