using System;
using DD.Prototype.Audio;
using DD.Prototype.Core.Config;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-107 (R4) · wall buy sesleri haritada: beş kimlik IDS.md değerlerinde (1000–1004), her bağlı olay kendi satırında, karışım
    /// planı satırla aynı bus / konumlulukta, satın alma duck yapmaz, istem 2D ve ≥ 1 s kapılı; raf seçimi (roketatar = ağır raf).
    /// Genel kapsam (satır tekilliği, rezerve olmayan satırın sesi, recipe / katalog karşılığı) AudioSliceEditModeTests'te
    /// AudioEventMap.Entries üzerinden otomatik koşar.
    /// </summary>
    [TestFixture]
    public sealed class Wp107WallBuyAudioMapTests
    {
        [Category("WP021")]
        [Test]
        public void WallBuy_HaritaSatirlari_KarisimPlaniylaAyniBus_RafSecimi()
        {
            Assert.AreEqual(1000, (int)AudioSoundId.WallBuyCoins, "IDS.md");
            Assert.AreEqual(1001, (int)AudioSoundId.WallBuyRack, "IDS.md");
            Assert.AreEqual(1002, (int)AudioSoundId.WallBuyDenied, "IDS.md");
            Assert.AreEqual(1003, (int)AudioSoundId.WallBuyHeavy, "IDS.md");
            Assert.AreEqual(1004, (int)AudioSoundId.WallBuyPrompt, "IDS.md");

            var satirlar = new (string olay, AudioSoundId ses)[]
            {
                ("WallBuyEvents.Resolved · Bought", AudioSoundId.WallBuyCoins),
                ("WallBuyEvents.Resolved · Bought → rack", AudioSoundId.WallBuyRack),
                ("WallBuyEvents.Resolved · Bought rl_1 → heavy rack", AudioSoundId.WallBuyHeavy),
                ("WallBuyEvents.Resolved · Refilled", AudioSoundId.WallBuyCoins),
                ("WallBuyEvents.Resolved · Denied", AudioSoundId.WallBuyDenied),
                ("WallBuyEvents.Resolved · Denied → UiDenied", AudioSoundId.UiDenied),
                ("WallBuyEvents.PromptChanged false→true", AudioSoundId.WallBuyPrompt)
            };

            foreach ((string olay, AudioSoundId ses) in satirlar)
            {
                Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Run, olay, out AudioEventBinding b), "Matris satırı yok: " + olay);
                Assert.IsFalse(b.Reserved, olay);
                Assert.AreEqual(ses, b.Sound, olay);
                Assert.IsFalse(string.IsNullOrEmpty(b.Note), olay + " satırın notu yok");
                AudioPlacement p = ses == AudioSoundId.UiDenied ? AudioMixPlan.Ui(ses) : AudioMixPlan.WallBuy(ses);
                Assert.AreEqual(ses, p.Sound, olay);
                Assert.AreEqual(b.Bus, p.Bus, olay + ": harita ile karışım planı aynı bus'ta olmalı");
                Assert.AreEqual(b.Spatial, p.Spatial > 0f, olay + ": konumluluk haritayla aynı");
                Assert.AreEqual(0f, p.DuckSeconds, olay + ": satın alma duck yapmaz");
                if (ses != AudioSoundId.UiDenied)
                {
                    Assert.AreEqual(1, p.MaxInstances, olay + ": bir basış bir ses");
                }
            }

            AudioPlacement istem = AudioMixPlan.WallBuy(AudioSoundId.WallBuyPrompt);
            Assert.AreEqual(AudioBus.Interface, istem.Bus);
            Assert.AreEqual(0f, istem.Spatial, "istem 2D");
            Assert.GreaterOrEqual(istem.MinRetrigger, 1f, "istem yarıçap titremesine karşı ≥ 1 s kapılı");
            Assert.Greater(AudioMixPlan.WallBuy(AudioSoundId.WallBuyRack).Delay, 0f, "raf sikkenin arkasından");
            Assert.AreEqual(0f, AudioMixPlan.WallBuy(AudioSoundId.WallBuyCoins).Delay, "sikke önde");

            Assert.AreEqual(AudioSoundId.WallBuyHeavy, AudioEventRouter.WallBuyRackFor(ClassicWeaponId.Rl1));
            foreach (ClassicWeaponId w in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                if (w != ClassicWeaponId.Rl1)
                {
                    Assert.AreEqual(AudioSoundId.WallBuyRack, AudioEventRouter.WallBuyRackFor(w), w + " tahta raftan alınır");
                }
            }
        }
    }
}
