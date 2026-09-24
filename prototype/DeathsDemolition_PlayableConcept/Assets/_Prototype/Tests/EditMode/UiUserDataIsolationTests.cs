using System;
using System.IO;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 user-data rule: the game writes ui-settings.json only when the player changes
    /// a setting. Measured load times go to load-timing.json and the HINTs already shown go to
    /// ui-hints.json. The load-time and hint keys older files carry are never read, and they
    /// drop out on the player's next save. Tests write only to the process-local folder
    /// (TestUiDataIsolation), and the player's real files are left untouched.
    /// </summary>
    [TestFixture]
    [Category("WP068")]
    public sealed class UiUserDataIsolationTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(UiDataPaths.ProcessLocalRoot, "case-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.SetPathOverride(null);
            UiLoadTiming.SetPathOverride(null);
            UiHintState.SetPathOverride(null);
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        [Test]
        public void Yollar_SurecYerelKlasorde_GercekKlasordeDegil()
        {
            Assert.IsTrue(UiDataPaths.IsProcessLocal, "test koşusu süreç-yerel kökte");
            foreach (string path in new[] { UiSettings.FilePath, UiLoadTiming.FilePath, UiHintState.FilePath })
            {
                StringAssert.StartsWith(UiDataPaths.ProcessLocalRoot, path);
                Assert.IsFalse(path.StartsWith(Application.persistentDataPath, StringComparison.Ordinal), "gerçek klasör: " + path);
            }
        }

        [Test]
        public void TestKosucusu_VarsayilanKok_SurecYerel_FixtureOlmasaBile()
        {
            Assume.That(UiDataPaths.RunsUnderTestRunner, "yalnız -runTests ile başlatılan (batch) koşuda anlamlı");
            try
            {
                UiDataPaths.SetRootOverride(null);   // what an assembly without a fixture sees
                Assert.AreEqual(UiDataPaths.ProcessLocalRoot, UiDataPaths.Root, "fixture'sız derlemede de kök süreç-yerel");
                StringAssert.StartsWith(UiDataPaths.ProcessLocalRoot, UiSettings.FilePath);
                Assert.IsTrue(UiDataPaths.IsProcessLocal);
            }
            finally
            {
                UiDataPaths.UseProcessLocalRoot();
            }
        }

        [Test]
        public void HerYazar_GercekDosyalaraDokunmaz()
        {
            Assert.IsTrue(UiDataPaths.IsProcessLocal, "yalıtım yok: yazmadan önce dur");
            string before = TestUiDataIsolation.RealFiles();
            float scale = UiSettings.UiScale;
            bool reducedMotion = UiSettings.ReducedMotion;
            bool hints = UiSettings.Hints;
            float textScale = UiSettings.TextScale;
            bool flashes = UiSettings.ReduceFlashes;
            int seen = UiHintState.Seen;
            try
            {
                UiSettings.UiScale = scale > 1f ? 1f : 1.1f;
                UiSettings.ReducedMotion = !reducedMotion;
                UiSettings.Hints = !hints;
                UiSettings.TextScale = textScale > 1f ? 1f : 1.25f;
                UiSettings.ReduceFlashes = !flashes;
                UiLoadTiming.RecordLoad("Selection", 3f);
                UiHintState.Seen = seen ^ 1;
            }
            finally
            {
                UiSettings.UiScale = scale;
                UiSettings.ReducedMotion = reducedMotion;
                UiSettings.Hints = hints;
                UiSettings.TextScale = textScale;
                UiSettings.ReduceFlashes = flashes;
                UiHintState.Seen = seen;
            }

            Assert.IsTrue(File.Exists(UiSettings.FilePath), "ayarlar süreç-yerel dosyaya yazıldı");
            Assert.IsTrue(File.Exists(UiLoadTiming.FilePath), "yükleme süresi süreç-yerel dosyaya yazıldı");
            Assert.IsTrue(File.Exists(UiHintState.FilePath), "HINT maskesi süreç-yerel dosyaya yazıldı");
            Assert.AreEqual(before, TestUiDataIsolation.RealFiles(), "oyuncunun gerçek UI dosyaları değişti");
        }

        [Test]
        public void EskiAyarDosyasi_YuklemeVeHintAnahtarlariOkunmaz_OyuncuKaydindaDuser()
        {
            string settings = Path.Combine(_dir, UiSettings.FileName);
            File.WriteAllText(settings, "{ \"Version\": 1, \"UiScale\": 1.1, \"LoadScenes\": [\"Selection\"], \"LoadSeconds\": [5.0], \"Hints\": false, \"HintsSeen\": 7 }");
            string legacy = File.ReadAllText(settings);
            UiSettings.SetPathOverride(settings);
            UiLoadTiming.SetPathOverride(Path.Combine(_dir, UiLoadTiming.FileName));
            UiHintState.SetPathOverride(Path.Combine(_dir, UiHintState.FileName));

            Assert.AreEqual(1.1f, UiSettings.UiScale, 1e-4f, "oyuncu ayarları okunur");
            Assert.IsFalse(UiSettings.Hints);
            Assert.AreEqual(-1f, UiLoadTiming.LastLoadSeconds("Selection"), "eski yükleme anahtarı okunmaz: ölçülmemiş, varsayılan eşik");
            Assert.AreEqual(0, UiHintState.Seen, "eski HintsSeen okunmaz");

            UiLoadTiming.RecordLoad("Selection", 0.4f);
            UiHintState.Seen = 3;
            Assert.AreEqual(legacy, File.ReadAllText(settings), "oyunun kendi yazdıkları ui-settings.json'a dokunmaz");

            UiSettings.UiScale = 1f; // the player changes a setting
            string saved = File.ReadAllText(settings);
            StringAssert.DoesNotContain("LoadScenes", saved, "eski anahtar oyuncu kaydında düşer");
            StringAssert.DoesNotContain("LoadSeconds", saved);
            StringAssert.DoesNotContain("HintsSeen", saved);
            StringAssert.Contains("\"Hints\": false", saved, "oyuncunun ayarı korunur");

            UiLoadTiming.SetPathOverride(Path.Combine(_dir, UiLoadTiming.FileName));
            UiHintState.SetPathOverride(Path.Combine(_dir, UiHintState.FileName));
            Assert.AreEqual(0.4f, UiLoadTiming.LastLoadSeconds("Selection"), 1e-4f, "load-timing.json geri okunur");
            Assert.AreEqual(3, UiHintState.Seen, "ui-hints.json geri okunur");
        }

        [Test]
        public void YuklemeZamanlamasi_BozukVeyaUyumsuzDosya_OlculmemisSayilir()
        {
            string timing = Path.Combine(_dir, UiLoadTiming.FileName);
            File.WriteAllText(timing, "{ \"Version\": 1, \"Scenes\": [\"Run\", \"Selection\"], \"Seconds\": [2.0] }");
            UiLoadTiming.SetPathOverride(timing);
            Assert.AreEqual(-1f, UiLoadTiming.LastLoadSeconds("Run"), "dizi boyları uyuşmuyor: tablo yok sayılır");

            File.WriteAllText(timing, "not json");
            UiLoadTiming.SetPathOverride(timing);
            Assert.AreEqual(-1f, UiLoadTiming.LastLoadSeconds("Run"), "okunamayan dosya: varsayılan eşik");
            UiLoadTiming.RecordLoad("Run", 1.5f);
            UiLoadTiming.SetPathOverride(timing);
            Assert.AreEqual(1.5f, UiLoadTiming.LastLoadSeconds("Run"), 1e-4f, "ilk ölçüm dosyayı yeniden kurar");
        }
    }
}
