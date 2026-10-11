using DD.Prototype.UI;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-105 (§C5, KOD SHOULD-9; C-00 11.10 decision 5) · when a lost window focus pauses the run. On for a player's own
    /// launch and a guarded playtest; off in batch mode, under the test runner and with an automation flag - the explicit
    /// -ddNoFocusPause, the UI capture agents (-ddUiCapture…), the QA bot (QaRunBot's -ddQaBot and every -ddQa… run), the
    /// TopDown capture, the pilot / perf / probe runs - which play unfocused by design.
    /// </summary>
    [TestFixture]
    [Category("WP105")]
    public sealed class UiPauseFocusRuleTests
    {
        [Test]
        public void Duraklat_OdakKaybiKurali_OyuncuAcilisindaAcik_OtomasyondaKapali()
        {
            Assert.IsTrue(PauseScreen.FocusPauseAllowed(false, null), "argüman yok: açık");
            Assert.IsTrue(PauseScreen.FocusPauseAllowed(false, new string[0]));
            Assert.IsTrue(PauseScreen.FocusPauseAllowed(false, new[] { "/Applications/DD.app/Contents/MacOS/DD" }), "oyuncunun açılışı");
            Assert.IsTrue(PauseScreen.FocusPauseAllowed(false, new[] { "DD", "-ddTelemetryDir", "/tmp/t", "-ddProfilePath", "/tmp/p", "-ddUiDataRoot", "/tmp/u" }),
                "korumalı oyun testi (player-run.sh) özelliği görür");
            Assert.IsTrue(PauseScreen.FocusPauseAllowed(false, new[] { "DD", "-screen-width", "1920" }), "Unity'nin kendi bayrakları");

            Assert.IsFalse(PauseScreen.FocusPauseAllowed(true, null), "batchmode: kapalı");
            Assert.IsFalse(PauseScreen.FocusPauseAllowed(false, new[] { "Unity", "-runTests" }), "test koşucusu");
            foreach (string flag in new[]
            {
                "-ddNoFocusPause", "-ddUiCapture", "-ddUiCaptureRounds", "-ddUiFxCapture", "-ddUiKitPage", "-ddUiPerf", "-ddCapture",
                "-ddQaBot", "-ddQaBotRounds", "-ddQaReadability", "-ddTdCapture", "-ddPilotPerfProbe", "-ddProbe", "-ddArmorProbe",
                "-ddRoundProbe", "-ddVfxImpactCapture", "-ddWeaponShowcase", "-ddFeelCost", "-ddCinePerfSeconds"
            })
            {
                Assert.IsFalse(PauseScreen.FocusPauseAllowed(false, new[] { "DD", flag, "x" }), flag + ": otomasyon odaksız koşar, kapalı");
            }

        }
    }
}
