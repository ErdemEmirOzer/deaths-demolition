using System.IO;
using System.Security.Cryptography;
using DD.Prototype.Profile;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-000 · B2 nöbetçisi — PlayMode koşusu gerçek profil dosyasını değiştirmez.
    ///
    /// Gerçek dosyanın SHA-256'sı (yoksa "yok" durumu) Save öncesi ve sonrası
    /// karşılaştırılır. Test profili ise gerçekten yazılmış olmalı; aksi halde
    /// yönlendirme sessizce kopmuş demektir.
    /// </summary>
    [TestFixture]
    public sealed class ProfileIsolationTests
    {
        [Test]
        [Category("Regression")]
        public void TestProfiliGeciciYoldaGercekProfilDegismez()
        {
            string real = TestProfileIsolation.RealProfilePath;
            string test = TestProfileIsolation.TestProfilePath;

            Assert.AreEqual(test, ProfileStore.Path, "Profil yolu geçici yola yönlenmiş olmalı.");
            Assert.AreNotEqual(real, ProfileStore.Path, "Profil yolu gerçek kalıcı yola işaret etmemeli.");

            string realBefore = Fingerprint(real);
            string testBefore = Fingerprint(test);

            ProfileService.Current.RunsPlayed += 1;
            Assert.IsTrue(ProfileService.Save(), "Test profili kaydedilemedi.");

            Assert.AreEqual(realBefore, Fingerprint(real),
                "GERÇEK profil dosyası PlayMode testi sırasında değişti: " + real);
            Assert.AreNotEqual(testBefore, Fingerprint(test),
                "Test profili yazılmadı; yönlendirme etkisiz: " + test);

            Debug.Log("[DD-TEST] profile.isolation real=" + realBefore.Substring(0, 12) + " unchanged");
        }

        private static string Fingerprint(string path)
        {
            if (!File.Exists(path))
            {
                return "absent";
            }

            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
