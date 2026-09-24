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
    // WP-023: the path carries the Editor's process id. temporaryCachePath is shared by
    // every worktree of this product, and since WP-009H the profile store holds a
    // fail-closed fcntl lease on its file: two Play Mode sessions on one machine would
    // otherwise fight over one file and the loser would run read-only ("Another copy…"),
    // failing every profile assertion for an environmental reason.
    public static string TestProfilePath =>
        Path.Combine(Application.temporaryCachePath,
            "dd-test-profile-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json");

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

        // WP-079 (main CI, 24.09): Play Mode runs end runs, and RunTelemetry wrote one JSON per run
        // into the REAL player folder (persistentDataPath/telemetry — 23 files in one CI pass).
        // Point it at a per-process scratch folder for the whole assembly, like the profile.
        DD.Prototype.Diagnostics.Telemetry.RunTelemetry.ParseArgs(new[] { "-ddTelemetryDir", TestTelemetryDirectory });
        Assert.AreEqual(TestTelemetryDirectory, DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Directory,
            "Telemetri klasörü yönlendirilemedi; testler gerçek Player klasörüne yazabilir.");
    }

    /// <summary>WP-079: per-process scratch folder for run telemetry written by Play Mode tests.</summary>
    public static string TestTelemetryDirectory =>
        Path.Combine(Application.temporaryCachePath,
            "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "telemetry");
}
