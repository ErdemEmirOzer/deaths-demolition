using System.Collections;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-022M: applies the Settings DISPLAY choice (fullscreen / windowed) in the Player.
    ///
    /// A mode change on macOS is asynchronous: the window changes after the request and
    /// <see cref="Screen.fullScreenMode"/> reports the request before the window follows.
    /// So the switch is one coroutine that never overlaps itself, makes a mode-only request
    /// (never a size: Screen/Display sizes are backing pixels on a Retina Mac, windows are
    /// sized in points), escalates through the other Screen APIs if a step is not honoured,
    /// and sets the windowed size only once the window is windowed. Each step is logged
    /// ("[UiDisplay] ...") as the proof trail; <c>-ddUiCapture display</c> drives it through
    /// the Settings row. Note: with the macOS session locked the Player performs no
    /// in-session window transition at all (WP-022M probe, 23.09) - prove it unlocked.
    /// </summary>
    public sealed class UiDisplayMode : MonoBehaviour
    {
        /// <summary>Seconds each step waits for the OS before the next strategy is tried.</summary>
        public const float StepTimeout = 4f;

        private const int FallbackWidth = 1280;
        private const int FallbackHeight = 720;

        private static UiDisplayMode s_runner;
        private static bool? s_pending;
        private static int s_width, s_height;
        private static bool s_busy;

        /// <summary>The mode being switched to, or null when no switch is in flight.</summary>
        public static bool? Pending => s_pending;

        /// <summary>Last completed switch: true when the requested mode was reached. Test / capture readout.</summary>
        public static bool LastSucceeded { get; private set; } = true;

        /// <summary>Human-readable name of the step that reached the mode (capture report).</summary>
        public static string LastStep { get; private set; } = string.Empty;

        public static void Request(bool fullscreen, int windowWidth, int windowHeight)
        {
            s_pending = fullscreen;
            s_width = windowWidth > 0 ? windowWidth : FallbackWidth;
            s_height = windowHeight > 0 ? windowHeight : FallbackHeight;
            if (!Application.isPlaying)
            {
                return;
            }

            if (s_runner == null)
            {
                var go = new GameObject("UiDisplayMode") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                s_runner = go.AddComponent<UiDisplayMode>();
            }

            if (!s_busy)
            {
                s_runner.StartCoroutine(s_runner.Run());
            }
        }

        private IEnumerator Run()
        {
            s_busy = true;
            // Loop: a request that lands while a switch is running is served after it.
            while (s_pending.HasValue)
            {
                bool full = s_pending.Value;
                int w = s_width, h = s_height;
                float t0 = Time.realtimeSinceStartup;
                bool ok;
                if (full)
                {
                    yield return ToFullscreen();
                }
                else
                {
                    yield return ToWindowed(w, h);
                }

                ok = full ? Screen.fullScreenMode != FullScreenMode.Windowed : Screen.fullScreenMode == FullScreenMode.Windowed;
                LastSucceeded = ok;
                Debug.Log(string.Format("[UiDisplay] {0} {1} via {2} in {3:0.0}s · mode {4} · {5}x{6}",
                    full ? "fullscreen" : "windowed", ok ? "reached" : "NOT reached", LastStep,
                    Time.realtimeSinceStartup - t0, Screen.fullScreenMode, Screen.width, Screen.height));

                if (s_pending.HasValue && s_pending.Value == full)
                {
                    s_pending = null;
                }
            }

            s_busy = false;
        }

        private static IEnumerator ToFullscreen()
        {
            // Mode only, never a size (pixels vs points, see the class note).
            LastStep = "fullScreenMode = FullScreenWindow";
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            yield return WaitMode(false);
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                LastStep = "fullScreen = true";
                Screen.fullScreen = true;
                yield return WaitMode(false);
            }
        }

        private static IEnumerator ToWindowed(int width, int height)
        {
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                // 1. Leave the fullscreen space with a mode-only request (no size in the same call).
                LastStep = "fullScreenMode = Windowed";
                Screen.fullScreenMode = FullScreenMode.Windowed;
                yield return WaitMode(true);

                // 2. Ignored: the legacy flag.
                if (Screen.fullScreenMode != FullScreenMode.Windowed)
                {
                    LastStep = "fullScreen = false";
                    Screen.fullScreen = false;
                    yield return WaitMode(true);
                }

                // 3. Still ignored: mode and size in one call.
                if (Screen.fullScreenMode != FullScreenMode.Windowed)
                {
                    LastStep = "SetResolution(w, h, Windowed)";
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
                    yield return WaitMode(true);
                }

                // Give the window a frame after the space animation before resizing it.
                yield return new WaitForSecondsRealtime(0.5f);
            }
            else
            {
                LastStep = "already windowed";
            }

            if (Screen.fullScreenMode == FullScreenMode.Windowed && (Screen.width != width || Screen.height != height))
            {
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                float deadline = Time.realtimeSinceStartup + StepTimeout;
                while (Time.realtimeSinceStartup < deadline && (Screen.width != width || Screen.height != height))
                {
                    yield return null;
                }
            }
        }

        private static IEnumerator WaitMode(bool windowed)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeout;
            while (Time.realtimeSinceStartup < deadline
                   && (Screen.fullScreenMode == FullScreenMode.Windowed) != windowed)
            {
                yield return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_runner = null;
            s_pending = null;
            s_busy = false;
            LastSucceeded = true;
            LastStep = string.Empty;
        }
    }
}
