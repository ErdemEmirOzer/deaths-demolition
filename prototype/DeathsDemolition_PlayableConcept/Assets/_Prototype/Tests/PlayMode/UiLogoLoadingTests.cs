using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10) · the loading logo in the real flow (FastTransitions off, real scene loads):
    /// intro → loop → outro in that order on a long load with no extra hold; on a short load the logo holds a ready load
    /// ≤ 0.5 s (K3); the outro runs over the parting doors without moving <see cref="UiCurtain.Revealed"/>; reduced motion
    /// shows the static logo and holds nothing (K6); the Results / ring-only / ink screens never show it (K4); the error
    /// modal takes it away and gives it back (S3-18); the ring's own seal hides while it shows (K1); the loop allocates
    /// nothing per frame. The longest frame during the intro is recorded (C-00 08.10: the 0-6.9 s low-fps load) - a
    /// record, not a gate: the Editor runner is not the target hardware.
    /// </summary>
    [Category("AP035")]
    public sealed class UiLogoLoadingTests
    {
        private string _settingsPath;
        private string _timingPath;
        private bool _fast;

        [SetUp]
        public void SetUp()
        {
            _fast = PrototypeUiFlow.FastTransitions;
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-ap035-logo-ui-" + System.Guid.NewGuid().ToString("N") + ".json");
            _timingPath = Path.Combine(Path.GetTempPath(), "dd-ap035-logo-timing-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiLoadTiming.SetPathOverride(_timingPath);
        }

        [TearDown]
        public void TearDown()
        {
            PrototypeUiFlow.FastTransitions = _fast;
            PrototypeUiFlow.ArtificialLoadSeconds = 0f;
            RunFlowClock.ClearHolds();
            UiSettings.SetPathOverride(null);
            UiLoadTiming.SetPathOverride(null);
            foreach (string path in new[] { _settingsPath, _timingPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>What one real load showed (times on UiFxGlobals.Now - the logo's own clock).</summary>
        private sealed class Watch
        {
            public readonly List<UiLogoPhase> Phases = new List<UiLogoPhase>();
            public float GroupAt = -1f, EnteringAt = -1f, RevealedAt = -1f, ReadyAt = -1f;
            public UiLogoPhase PhaseAtReveal = UiLogoPhase.Hidden;
            public float MaxIntroFrame;
            public int IntroFrames;
            public bool RingSealWithLogo;
            public bool LayoutLowered = true;
        }

        private static IEnumerator WatchLoad(Watch w)
        {
            UiCurtain curtain = UiCurtain.Instance;
            System.Action onRevealed = () =>
            {
                w.RevealedAt = UiFxGlobals.Now;
                w.PhaseAtReveal = curtain.LogoPhase;
            };
            UiCurtain.Revealed += onRevealed;
            try
            {
                float deadline = Time.realtimeSinceStartup + 60f;
                while ((PrototypeUiFlow.IsLoading || UiCurtain.IsCovering || curtain.LogoVisible) && Time.realtimeSinceStartup < deadline)
                {
                    UiLogoPhase phase = curtain.LogoPhase;
                    if (w.Phases.Count == 0 || w.Phases[w.Phases.Count - 1] != phase)
                    {
                        w.Phases.Add(phase);
                    }

                    if (curtain.GroupVisible && w.GroupAt < 0f)
                    {
                        w.GroupAt = UiFxGlobals.Now;
                    }

                    if (curtain.GroupVisible && curtain.EyebrowText == "ENTERING" && w.EnteringAt < 0f)
                    {
                        w.EnteringAt = UiFxGlobals.Now;
                        w.ReadyAt = curtain.Logo.Timeline.ReadyAt;
                    }

                    if (phase == UiLogoPhase.Intro)
                    {
                        if (w.IntroFrames > 0)
                        {
                            w.MaxIntroFrame = Mathf.Max(w.MaxIntroFrame, Time.unscaledDeltaTime);
                        }

                        w.IntroFrames++;
                    }

                    if (curtain.LogoVisible && curtain.GroupVisible)
                    {
                        w.RingSealWithLogo |= curtain.RingSealVisible;
                    }

                    yield return null;
                }
            }
            finally
            {
                UiCurtain.Revealed -= onRevealed;
            }
        }

        private static IEnumerator MenuThenRealFlow()
        {
            PrototypeUiFlow.FastTransitions = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.FastTransitions = false;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator UzunYukleme_IntroDonguCikisSirasi_EkBeklemeYok_RevealedGecikmez()
        {
            yield return MenuThenRealFlow();
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f);   // SCREEN depth
            PrototypeUiFlow.ArtificialLoadSeconds = 4f;                // longer than the intro (2.10 s): the loop shows
            var w = new Watch();
            PrototypeUiFlow.GoToSelection();
            yield return WatchLoad(w);

            string seq = string.Join(" > ", w.Phases);
            Debug.Log("[DD-TEST] ap035.logo.long phases=" + seq + " ready=" + w.ReadyAt.ToString("F3") + " entering=" + w.EnteringAt.ToString("F3")
                      + " revealed=" + w.RevealedAt.ToString("F3") + " phaseAtReveal=" + w.PhaseAtReveal
                      + " maxIntroFrameMs=" + (w.MaxIntroFrame * 1000f).ToString("F1") + " introFrames=" + w.IntroFrames);
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
            int intro = w.Phases.IndexOf(UiLogoPhase.Intro), loop = w.Phases.IndexOf(UiLogoPhase.Loop), outro = w.Phases.IndexOf(UiLogoPhase.Outro);
            Assert.GreaterOrEqual(intro, 0, "intro: " + seq);
            Assert.Greater(loop, intro, "loop after the intro: " + seq);
            Assert.Greater(outro, loop, "outro after the loop: " + seq);
            Assert.AreEqual(UiLogoPhase.Outro, w.Phases[w.Phases.Count - 1], "the outro is the last phase seen while it shows: " + seq);
            Assert.AreEqual(UiLogoPhase.Hidden, UiCurtain.Instance.LogoPhase, "gone at the end");
            Assert.Greater(w.ReadyAt, 0f, "the flow told the logo the load was ready");
            Assert.LessOrEqual(w.EnteringAt - w.ReadyAt, 0.1f, "a load longer than the intro is never held (loop: may leave)");
            Assert.AreEqual(UiLogoPhase.Outro, w.PhaseAtReveal, "Revealed fires while the outro still runs: the outro never delays it");
            Assert.IsFalse(w.RingSealWithLogo, "K1: the ring's small seal hides while the logo shows");
            Assert.IsFalse(UiCurtain.Instance.LogoVisible);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KisaYukleme_EkBekleme_EnFazlaYarimSaniye()
        {
            yield return MenuThenRealFlow();
            UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f);   // SCREEN depth
            PrototypeUiFlow.ArtificialLoadSeconds = 0.6f;             // loaded before the 1.2 s minimum ends (the capture's "fast load")
            var w = new Watch();
            PrototypeUiFlow.GoToSelection();
            yield return WatchLoad(w);

            float extra = w.EnteringAt - w.ReadyAt;
            float specAtLeave = (w.EnteringAt - (w.GroupAt)) * UiLogoTimeline.IntroSpeed;
            Debug.Log("[DD-TEST] ap035.logo.short phases=" + string.Join(" > ", w.Phases) + " group=" + w.GroupAt.ToString("F3") + " ready=" + w.ReadyAt.ToString("F3")
                      + " entering=" + w.EnteringAt.ToString("F3") + " extraHold=" + extra.ToString("F3") + " specAtLeave~" + specAtLeave.ToString("F3")
                      + " maxIntroFrameMs=" + (w.MaxIntroFrame * 1000f).ToString("F1") + " introFrames=" + w.IntroFrames);
            Assert.Greater(w.ReadyAt, 0f, "ready told");
            Assert.GreaterOrEqual(w.ReadyAt - w.GroupAt, PrototypeUiFlow.MinimumDisplaySeconds - 0.05f, "ready after the minimum display");
            Assert.LessOrEqual(extra, UiLogoTimeline.MaxExtraHoldSeconds + 0.1f, "K3: the logo adds at most 0.5 s (+ a frame)");
            Assert.AreEqual(UiLogoPhase.Outro, w.PhaseAtReveal, "the outro runs over the reveal");
            Assert.AreEqual(PrototypeScenes.Selection, SceneManager.GetActiveScene().name);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AzHareket_StatikLogo_BeklemeYok_AsgariGosterimKorunur()
        {
            yield return MenuThenRealFlow();
            bool reduced = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = true;
            try
            {
                UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 5f);
                var w = new Watch();
                float t0 = UiFxGlobals.Now;
                PrototypeUiFlow.GoToSelection();
                Assert.AreEqual(UiLogoPhase.Static, UiCurtain.Instance.LogoPhase, "RM: the static logo at t0 with the group");
                yield return WatchLoad(w);
                Debug.Log("[DD-TEST] ap035.logo.rm phases=" + string.Join(" > ", w.Phases) + " ready=" + w.ReadyAt.ToString("F3") + " entering=" + w.EnteringAt.ToString("F3"));
                CollectionAssert.DoesNotContain(w.Phases, UiLogoPhase.Intro, "RM: no intro");
                CollectionAssert.DoesNotContain(w.Phases, UiLogoPhase.Outro, "RM: no outro (a short fade)");
                Assert.LessOrEqual(w.EnteringAt - w.ReadyAt, 0.1f, "RM: no hold");
                Assert.GreaterOrEqual(w.EnteringAt - t0, PrototypeUiFlow.MinimumDisplaySeconds - 0.05f, "RM: the minimum display stays");
            }
            finally
            {
                UiMotion.ReducedMotion = reduced;
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator SonuclarHalkaYalnizMurekkep_LogoYok_HataModaliAltindaCekilir()
        {
            yield return MenuThenRealFlow();
            foreach (string from in new[] { PrototypeScenes.MainMenu, PrototypeScenes.Selection, PrototypeScenes.Run })
            {
                Assert.IsFalse(PrototypeUiFlow.ShowsLogo(from, PrototypeScenes.Results), "K4: never on the way to the Results (from " + from + ")");
            }

            Assert.IsTrue(PrototypeUiFlow.ShowsLogo(PrototypeScenes.Results, PrototypeScenes.MainMenu));
            Assert.IsTrue(PrototypeUiFlow.ShowsLogo(PrototypeScenes.Selection, PrototypeScenes.Run));
            UiCurtain curtain = UiCurtain.Instance;
            curtain.Close(true);
            curtain.ShowInkGroup(UiCurtain.DestinationFor(PrototypeScenes.Results), UiCurtain.ResultsKeyArt);
            yield return null;
            Assert.IsFalse(curtain.LogoVisible, "the Results' key-art screen (ink) shows no logo");
            curtain.ShowRingOnly();
            curtain.PlayLoadingLogo();
            yield return null;
            Assert.IsFalse(curtain.LogoVisible, "the ring-only depth carries no logo");

            curtain.ShowGroup("ROUND 1", "ASSAULT RIFLE", null, true, UiCurtain.UpcomingKeyArt);
            curtain.PlayLoadingLogo();
            yield return null;
            Assert.IsTrue(curtain.LogoVisible, "the SCREEN depth shows it");
            Assert.IsFalse(curtain.RingSealVisible, "K1: one emblem per screen");
            curtain.ShowError(PrototypeScenes.Run, false, "RETURN TO MAIN MENU", () => { });
            yield return null;
            Assert.IsFalse(curtain.LogoVisible, "S3-18: the logo steps out under the error modal");
            curtain.HideError();
            yield return null;
            Assert.IsTrue(curtain.LogoVisible, "and comes back when it closes");
            curtain.Open(true);
            yield return null;
            Assert.IsFalse(curtain.LogoVisible, "an instant open takes it away");
            Assert.IsTrue(curtain.RingSealVisible || !curtain.GroupVisible, "the ring's seal is back for the next screen");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Dongu_KareBasiSifirAyirma()
        {
            yield return MenuThenRealFlow();
            UiCurtain curtain = UiCurtain.Instance;
            int frameRateWas = Application.targetFrameRate;
            Application.targetFrameRate = -1;   // the Editor runner leaves ProfilerRecorder.LastValue at 0 at 60 (test.sh note)
            try
            {
                curtain.Close(true);
                curtain.ShowGroup("ROUND 1", string.Empty, null, false, UiCurtain.UpcomingKeyArt);
                for (int i = 0; i < 5; i++) yield return null;
                UnityEngine.Profiling.Profiler.enabled = true;
                using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    yield return null;
                    yield return null;
                    var bait = new object[300];
                    for (int i = 0; i < bait.Length; i++) bait[i] = new byte[80];
                    long control = 0;
                    for (int i = 0; i < 3; i++) { yield return null; control = System.Math.Max(control, bytes.LastValue); }
                    System.GC.KeepAlive(bait);

                    var plain = new List<long>(40);
                    for (int i = 0; i < 40; i++) { yield return null; plain.Add(bytes.LastValue); }

                    curtain.PlayLoadingLogo();
                    float until = UiFxGlobals.Now + UiLogoTimeline.IntroSeconds / UiLogoTimeline.IntroSpeed + 0.2f;
                    yield return Bekle(() => UiFxGlobals.Now >= until, 10f, "the loop");
                    Assert.AreEqual(UiLogoPhase.Loop, curtain.LogoPhase);
                    for (int i = 0; i < 5; i++) yield return null;
                    var loop = new List<long>(40);
                    for (int i = 0; i < 40; i++) { yield return null; loop.Add(bytes.LastValue); }
                    long plainMin = Min(plain, out long plainWorst), loopMin = Min(loop, out long loopWorst);
                    Debug.Log("[DD-TEST] ap035.logo.alloc control=" + control + " plain min/worst=" + plainMin + "/" + plainWorst + " loop min/worst=" + loopMin + "/" + loopWorst);
                    Assert.Greater(control, 300 * 80, "positive control: the recorder sees allocations (" + control + ")");
                    Assert.LessOrEqual(loopMin, plainMin, "the logo's loop allocates nothing per frame (quietest frames)");
                }
            }
            finally
            {
                Application.targetFrameRate = frameRateWas;
                curtain.Open(true);
            }
        }

        private static long Min(List<long> values, out long worst)
        {
            long min = long.MaxValue;
            worst = 0;
            foreach (long v in values) { if (v < min) min = v; if (v > worst) worst = v; }
            return min;
        }
    }
}
