using System;
using DD.Prototype.Audio;
using DD.Prototype.Audio.EditorTools;
using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEditor;

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-101 (R4, kullanıcı notu 1 "ultra gerçekçi mermi") · AP-011 v025: dört v3 darbe setine diskteki lisanslı Sonniss'ten tek
    /// olaylı gerçek kayıt varyasyonu (v018'in üç klibi yerinde, önde; ses kapısı kalanları reddetti), sr_1 kendi keskin isabet katmanını alır.
    /// </summary>
    [TestFixture]
    public sealed class Wp101ImpactAudioTests
    {
        // AP-011 v025 after its single sound gate (K/wp-097/evidence/v025-gate-1210): four sets gained one real take each, cut to one
        // event and levelled on the first 100 ms; every other set keeps v018's three cards (the disk holds no bullet impact take).
        private static readonly (AudioSoundId Sound, int Clips)[] Setler =
        {
            (AudioSoundId.ImpactV3DefaultHeavy, 4), (AudioSoundId.ImpactV3DirtLight, 4), (AudioSoundId.ImpactV3DirtHeavy, 4),
            (AudioSoundId.ImpactV3WoodHeavy, 4),
            // AP-011 v026 (Adobe Firefly bullet impacts, user approval 12.10, gate K/wp-097/evidence/v026-gate-1210): one event each.
            (AudioSoundId.ImpactV3StoneLight, 5), (AudioSoundId.ImpactV3StoneHeavy, 5), (AudioSoundId.ImpactV3MetalLight, 4),
            (AudioSoundId.ImpactV3StoneMedium, 3), (AudioSoundId.ImpactV3MetalHeavy, 3), (AudioSoundId.ImpactV3WaterMedium, 3),
            (AudioSoundId.ImpactV3WoodLight, 3), (AudioSoundId.ImpactV3DefaultLight, 3), (AudioSoundId.ImpactV3DirtMedium, 3),
            (AudioSoundId.ImpactV3FoliageMedium, 3), (AudioSoundId.ImpactV3PaperMedium, 3),
            (AudioSoundId.ImpactSniperCrack, 3),
        };

        [Category("WP021")]
        [Test]
        public void KeskinNisanciKatmani_YalnizSr1_ImpactBusKonumlu_HaritadaBagli()
        {
            Assert.AreEqual(1064, (int)AudioSoundId.ImpactSniperCrack);
            foreach (ClassicWeaponId w in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                Assert.AreEqual(w == ClassicWeaponId.Sr1, AudioMixPlan.SniperImpactLayer(w), w + ": katman yalnız keskin nişancıda");
            }

            AudioPlacement p = AudioMixPlan.SniperImpact();
            Assert.AreEqual(AudioSoundId.ImpactSniperCrack, p.Sound);
            Assert.AreEqual(AudioBus.Impact, p.Bus, "isabetle aynı bus (silah atışı duck'ı aynı)");
            Assert.Greater(p.Spatial, 0.5f, "isabet noktasında konumlu");
            Assert.AreEqual(0f, p.DuckSeconds, "katman kimseyi kısmaz");
            Assert.Less(AudioMixPlan.SniperLayerBodyDb, AudioMixPlan.SniperLayerWorldDb, "bedende dünyadakinden kısık");

            Assert.IsTrue(AudioEventMap.TryFind(AudioEventSource.Combat, "ProjectileEvents.Impact (sr_1) → sniper layer", out AudioEventBinding b),
                "harita satırı yok");
            Assert.IsFalse(b.Reserved);
            Assert.AreEqual(AudioSoundId.ImpactSniperCrack, b.Sound);
            Assert.AreEqual(AudioBus.Impact, b.Bus);
        }

        [Category("WP021")]
        [Test]
        public void DarbeSetleri_GercekKayitVaryasyonlari_KatalogdaKlipSayisi()
        {
            var katalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(AudioCatalogBuilder.CatalogPath);
            Assert.IsNotNull(katalog, "katalog yüklenmedi");
            foreach ((AudioSoundId ses, int klip) in Setler)
            {
                Assert.IsTrue(katalog.TryGet(ses, out AudioCatalog.Entry e), ses + " katalogda değil");
                Assert.AreEqual(klip, e.Clips.Length, ses + ": v018 üç klip + v025 gerçek kayıt varyasyonları");
                for (int i = 0; i < e.Clips.Length; i++) Assert.IsNotNull(e.Clips[i], ses + " klip " + i);
                if (ses != AudioSoundId.ImpactSniperCrack)
                {
                    StringAssert.EndsWith("_01", e.Clips[0].name, ses + ": v018 klipleri önde kalır (lufs damgası 1. dosyanın)");
                }
            }
        }
    }
}
