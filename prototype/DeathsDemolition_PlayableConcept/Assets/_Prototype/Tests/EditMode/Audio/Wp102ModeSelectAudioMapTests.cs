using DD.Prototype.Audio;
using DD.Prototype.Difficulty;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-102 (R4) · mod seçimi sesleri haritada: iki kimlik IDS.md değerlerinde (1010 / 1011), Hard / Demolition satırı bağlı ve
    /// Interface bus'ta, Normal satırı rezerve (yeni ses yok, gerekçeli), router'ın mod → ses tablosu haritayla aynı, menü
    /// stinger'ları duck yapmaz. Genel kapsam AudioSliceEditModeTests'te AudioEventMap.Entries üzerinden otomatik koşar.
    /// </summary>
    [TestFixture]
    public sealed class Wp102ModeSelectAudioMapTests
    {
        [Category("WP021")]
        [Test]
        public void ModSecimi_HaritaSatirlari_RouterTablosuylaAyni_NormalRezerve()
        {
            Assert.AreEqual(1010, (int)AudioSoundId.ModeSelectHard, "IDS.md");
            Assert.AreEqual(1011, (int)AudioSoundId.ModeSelectDemolition, "IDS.md");

            foreach (DifficultyMode mode in new[] { DifficultyMode.Hard, DifficultyMode.Demolition })
            {
                string olay = "DifficultyService.SelectedChanged → " + mode;
                Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Run, olay, out AudioEventBinding b), "Matris satırı yok: " + olay);
                Assert.IsFalse(b.Reserved, olay);
                Assert.AreEqual(AudioEventRouter.ModeSelectSoundFor(mode), b.Sound, olay + ": router tablosu haritayla aynı");
                Assert.AreEqual(AudioBus.Interface, b.Bus, olay);
                AudioPlacement p = AudioMixPlan.UiEvent(b.Sound);
                Assert.AreEqual(b.Sound, p.Sound);
                Assert.AreEqual(AudioBus.Interface, p.Bus);
                Assert.AreEqual(0f, p.Spatial, olay + ": 2D");
                Assert.AreEqual(0f, p.DuckSeconds, olay + ": menü stinger'ı duck yapmaz");
            }

            Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Run, "DifficultyService.SelectedChanged → Normal", out AudioEventBinding normal));
            Assert.IsTrue(normal.Reserved, "Normal yeni ses çalmaz (UiConfirm mevcut yoldan)");
            Assert.IsFalse(string.IsNullOrEmpty(normal.Note), "rezerve satırın gerekçesi yok");
            Assert.AreEqual(AudioSoundId.None, AudioEventRouter.ModeSelectSoundFor(DifficultyMode.Normal));
            Assert.GreaterOrEqual(AudioEventRouter.ModeVoiceMaxSeconds, 4.9f, "mode_demolition 4,8 s: UI sesi kesmeden bitirir");
        }
    }
}
