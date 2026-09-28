using System.IO;
using DD.Prototype.Profile;
using NUnit.Framework;
using UnityEngine;

// WP-076 (28.09) · the Qa PlayMode assembly gets the same profile isolation as DD.Prototype.Tests.PlayMode (TestProfileIsolation):
// its bot tests end runs (a death; since WP-076 the bot closes the run after its last round), and ProfileRunCommitter writes the
// profile on every RunEnded. Namespace-less [SetUpFixture] = assembly scope. The override is deliberately NOT removed in teardown
// (OnApplicationQuit saves after it — see TestProfileIsolation).
[SetUpFixture]
public sealed class QaTestProfileIsolation
{
    public static string TestProfilePath =>
        Path.Combine(Application.temporaryCachePath, "dd-qa-test-profile-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".json");

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        ProfileStore.SetPathOverride(TestProfilePath);
        ProfileService.ResetProfile();
        Assert.AreEqual(TestProfilePath, ProfileStore.Path, "Profil yolu override edilemedi; Qa testleri gerçek profili yazabilir.");
        DD.Prototype.Diagnostics.Telemetry.RunTelemetry.ParseArgs(new[] { "-ddTelemetryDir",
            Path.Combine(Application.temporaryCachePath, "dd-qa-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "telemetry") });
        Debug.Log("[DD-TEST] qa.profile.isolation path=" + TestProfilePath);
    }
}
