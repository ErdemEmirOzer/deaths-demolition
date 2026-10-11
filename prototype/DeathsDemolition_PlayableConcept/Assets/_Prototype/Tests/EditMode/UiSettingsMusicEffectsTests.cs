using System.IO;
using DD.Prototype.Audio;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// R4 WP-105 (§C4, S-08 contract db529f70): the MUSIC / EFFECTS sliders reach the audio module the way UI SOUNDS does -
    /// UiSettings writes PrototypeAudio.MusicVolume / EffectsVolume in the setter and on load (the director ramps them onto the
    /// buses; AudioSliceTests.Ayarlar_MuzikEfekt_* measures that half). Runs on a private settings file.
    /// </summary>
    public sealed class UiSettingsMusicEffectsTests
    {
        private string _path;
        private int _level;

        [SetUp]
        public void SetUp()
        {
            _level = QualitySettings.GetQualityLevel();   // the load applies the saved quality: put the editor's back
            _path = Path.Combine(Path.GetTempPath(), "dd-wp105-volumes-" + System.Guid.NewGuid().ToString("N") + ".json");
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

            PrototypeAudio.MusicVolume = 1f;
            PrototypeAudio.EffectsVolume = 1f;
            QualitySettings.SetQualityLevel(_level, false);
        }

        [Test]
        public void MuzikEfekt_Ayarlayici_VeYukleme_PrototypeAudioyaYazar()
        {
            UiSettings.MusicVolume = 0.4f;
            UiSettings.EffectsVolume = 0.3f;
            Assert.AreEqual(0.4f, PrototypeAudio.MusicVolume, 1e-4f, "MUSIC setter writes the audio module");
            Assert.AreEqual(0.3f, PrototypeAudio.EffectsVolume, 1e-4f, "EFFECTS setter writes the audio module");

            PrototypeAudio.MusicVolume = 1f;
            PrototypeAudio.EffectsVolume = 1f;
            UiSettings.SetPathOverride(_path);   // forget the loaded state: the saved file is read again, as at launch
            Assert.AreEqual(0.4f, UiSettings.MusicVolume, 1e-4f, "the saved MUSIC value");
            Assert.AreEqual(0.4f, PrototypeAudio.MusicVolume, 1e-4f, "load writes the saved MUSIC to the audio module");
            Assert.AreEqual(0.3f, PrototypeAudio.EffectsVolume, 1e-4f, "load writes the saved EFFECTS to the audio module");
        }
    }
}
