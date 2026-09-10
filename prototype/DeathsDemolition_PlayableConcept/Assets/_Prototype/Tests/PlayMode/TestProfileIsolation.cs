using System.IO;
using DD.Prototype.Profile;
using NUnit.Framework;
using UnityEngine;

// WP-000 · Aşama 3 (B2) — PlayMode testleri gerçek oyuncu profilini yazmasın.
//
// Kanıt (2026-09-10): FlowSmokeTest ölümü tetikleyince PrototypeUiFlow.OnRunEnded →
// ProfileService.ApplyRun → ProfileStore.Save gerçek persistentDataPath dosyasını
// yazıyordu; oyun çıkışında UiFlowRunner.OnApplicationQuit da yazıyor. Bu fixture,
// assembly'deki BÜTÜN PlayMode testlerinden önce profil dosyasını geçici önbelleğe
// yönlendirir. Üretim kodu değişmez; yalnız mevcut ProfileStore.SetPathOverride
// dikişi kullanılır.
//
// Namespace'siz [SetUpFixture] NUnit'te assembly kapsamındadır: DD.Prototype.Tests
// içindeki her fixture'dan önce bir kez çalışır.
//
// Override OneTimeTearDown'da BİLEREK geri alınmaz: Play Mode'dan çıkarken
// OnApplicationQuit → ProfileService.Save() teardown'dan SONRA koşar; override
// kaldırılırsa test profili gerçek dosyanın üstüne yazılır. Statik alanlar bir sonraki
// Play Mode girişinde ProfileStore.ResetStatics ile zaten sıfırlanır.
[SetUpFixture]
public sealed class TestProfileIsolation
{
    /// <summary>Testlerin yazdığı geçici profil dosyası. Kalıcı klasörün dışındadır.</summary>
    public static string TestProfilePath =>
        Path.Combine(Application.temporaryCachePath, "dd-test-profile.json");

    /// <summary>Gerçek oyuncu profili; testler bu dosyaya asla yazmamalı.</summary>
    public static string RealProfilePath =>
        Path.Combine(Application.persistentDataPath, ProfileStore.FileName);

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        ProfileStore.SetPathOverride(TestProfilePath);

        // Önceki koşudan kalan geçici profil temiz bir başlangıç için silinir; bu
        // dosya yalnız testlere aittir. ResetProfile önbelleği boşaltır ve override
        // yoluna taze bir profil yazar — gerçek dosyaya dokunmaz.
        ProfileService.ResetProfile();

        Assert.AreEqual(TestProfilePath, ProfileStore.Path,
            "Profil yolu override edilemedi; testler gerçek profili yazabilir.");
        Assert.IsTrue(File.Exists(TestProfilePath),
            "Geçici test profili oluşturulamadı: " + TestProfilePath);

        Debug.Log("[DD-TEST] profile.isolation path=" + TestProfilePath);
    }
}
