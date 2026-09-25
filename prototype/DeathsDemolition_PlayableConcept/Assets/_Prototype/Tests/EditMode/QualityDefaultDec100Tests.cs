using System.IO;
using DD.Prototype.Core;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// DEC-100 (user, 25.09): with no saved settings the game opens on Cinematic; a saved choice stays as it is (no
    /// migration); Settings labels Cinematic "30 FPS" and High "60 FPS". Runs on a private settings file and puts the
    /// editor's quality level back.
    /// </summary>
    public sealed class QualityDefaultDec100Tests
    {
        private string _path;
        private int _level;

        [SetUp]
        public void SetUp()
        {
            _level = QualitySettings.GetQualityLevel();
            _path = Path.Combine(Path.GetTempPath(), "dd-dec100-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_path);
        }

        [TearDown]
        public void TearDown()
        {
            UiSettings.SetPathOverride(null);
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            QualitySettings.SetQualityLevel(_level, false);
        }

        [Test]
        public void IlkAcilis_Sinematik_KayitliSecimKorunur_Etiketler()
        {
            int levels = QualitySettings.names.Length;
            int cinematic = QualityTiers.LevelOfTier(QualityTier.Sinematik, levels);
            Assert.IsFalse(File.Exists(_path), "no saved settings");
            Assert.AreEqual(cinematic, UiSettings.Quality, "first launch opens on Cinematic");

            int high = QualityTiers.LevelOfTier(QualityTier.Yuksek, levels);
            UiSettings.Quality = high;
            UiSettings.SetPathOverride(_path);   // forget the loaded state: the saved file is read again
            Assert.AreEqual(high, UiSettings.Quality, "a saved choice is kept (no migration to Cinematic)");

            StringAssert.EndsWith("30 FPS", UiSettings.QualityLabel(cinematic));
            StringAssert.EndsWith("60 FPS", UiSettings.QualityLabel(high));
            StringAssert.DoesNotContain("FPS", UiSettings.QualityLabel(QualityTiers.LevelOfTier(QualityTier.Performans, levels)));
        }
    }
}
