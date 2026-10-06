using System;
using System.IO;
using DD.Prototype.Audio;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // WP-051C: Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode.Audio
{
    /// <summary>
    /// WP-085 G4 (B-03 GC incelemesi): silah katman / kovan tabloları tip başlatıcısında kurulur (160 ad birleştirmesi +
    /// Enum.TryParse); ses yönetmeni Awake'te ısıtır, ilk atış ve ilk kovan karesi kurmaz.
    /// Sınır: EditMode'da tip başlatıcıları aynı etki alanında önceki testlerce çalışmış olabilir — bu dosya ilk çağrının
    /// ayırmasının oyunda taşındığını ölçmez; Warm'ın tabloları okuduğunu, ısıtma sonrası aramanın 0 B olduğunu ve kurulum
    /// yolunun Warm'ı çağırdığını (kaynak kilidi) kanıtlar.
    /// </summary>
    [TestFixture]
    [Category("WP085")]
    public sealed class AudioWarmTests
    {
        [Test]
        public void SilahSesTablolari_Isitma_TeslimEdilenKimliklerleTutar_TekrariAyirmaz()
        {
            // Bağımsız sayım, tabloyla aynı ad kuralıyla (LayerName; "Casing" / "Link" + Ar1 + yüzey).
            int beklenenKatman = 0, beklenenKovan = 0;
            foreach (WeaponVoiceSet set in WeaponAudioIdentity.All)
            {
                foreach (WeaponLayer l in Enum.GetValues(typeof(WeaponLayer)))
                {
                    if (Enum.TryParse(WeaponAudioIdentity.LayerName(set.AudioId, l), false, out AudioSoundId _)) beklenenKatman++;
                }

                string pascal = set.AudioId[0] + set.AudioId.Substring(1).ToLowerInvariant();
                foreach (string yuzey in WeaponFoleyV3.V4Surfaces)
                {
                    if (Enum.TryParse("Casing" + pascal + yuzey, false, out AudioSoundId _)) beklenenKovan++;
                    if (Enum.TryParse("Link" + pascal + yuzey, false, out AudioSoundId _)) beklenenKovan++;
                }
            }

            Assert.Greater(beklenenKatman, 0, "AP-027 katman kimlikleri yok — test boş kalır");
            Assert.Greater(beklenenKovan, 0, "AP-027 kovan kimlikleri yok — test boş kalır");
            Assert.AreEqual(beklenenKatman, WeaponAudioIdentity.Warm(), "Warm katman tablosunu okumalı (teslim edilen katman sayısı)");
            Assert.AreEqual(beklenenKovan, WeaponFoleyV3.Warm(), "Warm kovan / şerit halkası tablolarını okumalı");

            // İdempotan: tablo bir kez kurulur; yeniden çağrı (ör. ikinci yönetmen, alan yeniden yüklemesiz Play) ayırmaz.
            Assert.That(() =>
            {
                WeaponAudioIdentity.Warm();
                WeaponFoleyV3.Warm();
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ikinci Warm ayırdı (tablo yeniden mi kuruluyor?)");
        }

        [Test]
        public void SilahSesTablolari_IsitmaSonrasi_KatmanVeKovanAramasi_Ayirmaz()
        {
            WeaponAudioIdentity.Warm();
            WeaponFoleyV3.Warm();
            var silahlar = new string[WeaponAudioIdentity.All.Length];
            for (int i = 0; i < silahlar.Length; i++) silahlar[i] = WeaponAudioIdentity.All[i].AudioId;
            SurfaceKind[] yuzeyler = { SurfaceKind.Stone, SurfaceKind.Default, SurfaceKind.Wood, SurfaceKind.Dirt, SurfaceKind.Water, SurfaceKind.Foliage };
            int katmanCount = Enum.GetValues(typeof(WeaponLayer)).Length;
            int bulunan = 0;

            // Pozitif kontrol, ısıtmanın gerekçesiyle: tablo kurulumunun işi (ad birleştirme + Enum.TryParse) ayırır ve kayıtçı bunu görür.
            bool ad = false;
            Assert.That(() => { ad = Enum.TryParse(WeaponAudioIdentity.LayerName("AR1", WeaponLayer.TailOpen), false, out AudioSoundId _); },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ad kurulumunun ayırmasını görmeli");
            Assert.IsTrue(ad, "WeaponAr1TailOpen AudioSoundId'de olmalı");

            // Ayrı ısınma turu yok: arama yöntemlerinin ilk çağrısı (Mono JIT derlemesi) Warm'ın içinde yapıldı.
            Assert.That(() =>
            {
                for (int n = 0; n < 200; n++)
                {
                    for (int i = 0; i < silahlar.Length; i++)
                    {
                        for (int l = 0; l < katmanCount; l++)
                        {
                            if (WeaponAudioIdentity.Layer(silahlar[i], (WeaponLayer)l) != AudioSoundId.None) bulunan++;
                        }

                        for (int s = 0; s < yuzeyler.Length; s++)
                        {
                            if (WeaponFoleyV3.CasingV4(silahlar[i], yuzeyler[s], (s & 1) == 0) != AudioSoundId.None) bulunan++;
                            if (WeaponFoleyV3.CasingV4(silahlar[i], yuzeyler[s], false, true) != AudioSoundId.None) bulunan++;
                        }
                    }
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "ısıtma sonrası katman / kovan araması ayırdı (bu iş parçacığında GC.Alloc örneği; hedef 0 B)");
            Assert.Greater(bulunan, 0, "arama teslim edilen kimliklere ulaşmalı (boş tabloda 0 B anlamsız)");
            Debug.Log("[DD-TEST] wp085.g4 warm layers=" + WeaponAudioIdentity.Warm() + " casings=" + WeaponFoleyV3.Warm() + " lookupHits=" + bulunan + " threadAlloc=none");
        }

        [Test]
        public void SesYonetmeni_Awake_SilahSesTablolariniIsitir()
        {
            // Kaynak kilidi: ısıtma kurulum yolunda (PrototypeAudioDirector.Awake, açılışta bir kez). Buradan düşerse tablolar yine
            // ilk atışın / ilk kovanın karesinde kurulur (G4 geri gelir) — hiçbir davranış testi bunu kırmızıya çevirmez.
            string kaynak = File.ReadAllText(Path.Combine(Application.dataPath, "_Prototype", "Audio", "Runtime", "PrototypeAudioDirector.cs"));
            int awake = kaynak.IndexOf("private void Awake()", StringComparison.Ordinal);
            Assert.Greater(awake, 0, "PrototypeAudioDirector.Awake bulunamadı");
            int son = kaynak.IndexOf("private IEnumerator Start()", awake, StringComparison.Ordinal);
            Assert.Greater(son, awake, "Awake gövdesinin sonu (Start) bulunamadı");
            string govde = kaynak.Substring(awake, son - awake);
            StringAssert.Contains("WeaponAudioIdentity.Warm();", govde, "Awake katman tablosunu ısıtmıyor");
            StringAssert.Contains("WeaponFoleyV3.Warm();", govde, "Awake kovan tablolarını ısıtmıyor");
        }
    }
}
