using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(DD.Prototype.Tests.PlayMode.TestClockLeakGuard))]

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-062J · The PlayMode run enters play mode once, so process-long clock state crosses test boundaries.
    /// Measured in the full run (wp-019r2/evidence/wp062j-full*): death beats (<see cref="DeathSequence"/>, WP-059)
    /// left <c>Time.timeScale</c> at 0.5 / 0.87 after player-death tests, hit-stop tokens (<see cref="HitStopService"/>,
    /// WP-062) outlived their scene, and AllocationProbe / QaRunBot left <c>Application.targetFrameRate</c> uncapped
    /// (the next AudioSlice test then ran 10 365 frames in 8.8 s). Fixtures that save/restore "the old time scale"
    /// re-install a leaked value, so the reset lives here, after every test: death beat, hit-stop token and feel
    /// router queue released; time scale back to 1 when no <see cref="RunFlowClock"/> hold owns it;
    /// an uncapped frame rate back to the cap the test started with, capture delta back to 0. Each release is logged (<c>[DD-CLOCK]</c>).
    /// Holds themselves are left alone — a fixture may legitimately keep one across its tests.
    /// </summary>
    public sealed class TestClockLeakGuard : ITestRunCallback
    {
        private static int s_fpsAtStart = int.MinValue;

        public void RunStarted(ITest testsToRun) { }
        public void RunFinished(ITestResult testResults) { }

        public void TestStarted(ITest test)
        {
            if (!Application.isPlaying || test.IsSuite) return;
            s_fpsAtStart = Application.targetFrameRate;
        }

        public void TestFinished(ITestResult result)
        {
            if (!Application.isPlaying || result.Test.IsSuite) return;
            string name = result.Test.Name;

            // Only an UNCAPPING is a leak (a ProfilerRecorder fixture sets -1 and misses its restore); the bootstrapper
            // capping to the config's 60 on boot is the game's own behaviour and stays.
            if (s_fpsAtStart > 0 && Application.targetFrameRate <= 0)
            {
                Debug.Log("[DD-CLOCK] end-leak " + name + " targetFrameRate " + Application.targetFrameRate + " -> " + s_fpsAtStart);
                Application.targetFrameRate = s_fpsAtStart;
            }

            if (Time.captureDeltaTime != 0f)
            {
                Debug.Log("[DD-CLOCK] end-leak " + name + " captureDeltaTime " + Time.captureDeltaTime + " -> 0");
                Time.captureDeltaTime = 0f;
            }

            if (Mathf.Abs(Time.timeScale - 1f) <= 0.0001f && !DeathSequence.IsActive && !HitStopService.IsActive) return;

            string before = Describe();
            DeathSequence.Cancel();
            FeelEventRouter.Clear();   // cancels the hit-stop token and the pending camera requests
            if (!RunFlowClock.IsFrozen && Mathf.Abs(Time.timeScale - 1f) > 0.0001f) Time.timeScale = 1f;
            Debug.Log("[DD-CLOCK] end-leak " + name + " " + before + " -> " + Describe());
        }

        private static string Describe()
        {
            return "timeScale=" + Time.timeScale.ToString("F3") + " death=" + DeathSequence.IsActive + " hitStop=" + HitStopService.IsActive +
                   " holds=" + RunFlowClock.DescribeHolds();
        }
    }
}
