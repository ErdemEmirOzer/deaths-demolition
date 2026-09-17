using System;
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Player-side screen captures for the UI quality gate (WP-022C, directive §14).
    ///
    ///   DeathsDemolition.app/Contents/MacOS/... -ddUiCapture selection -ddUiCaptureOut /tmp/sel.png -ddSize 1920x1080
    ///
    /// Screens: <c>mainmenu</c>, <c>settings</c> (main menu with the Settings pane),
    /// <c>selection</c>, <c>pause</c> (a run, paused), <c>results</c> (a run ended by
    /// lethal diagnostic damage, so the real Results screen with a real receipt - on the
    /// Player's own profile: run the user-data restore afterwards, as every smoke does).
    ///
    /// Entirely opt-in: without <c>-ddUiCapture</c> nothing is created. The agent drives
    /// the same public flow entry points a player would (GoToSelection, StartRun,
    /// PauseScreen.SetPaused, ApplyDamage), waits for the scene, then captures the back
    /// buffer at its own size (the WP-011 CaptureAgent lesson: CaptureScreenshotAsTexture,
    /// not the path overload) and quits.
    /// </summary>
    public static class UiCaptureAgent
    {
        private const string ScreenFlag = "-ddUiCapture";
        private const string OutFlag = "-ddUiCaptureOut";
        private const string SizeFlag = "-ddSize";
        private const string DelayFlag = "-ddUiCaptureDelay";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string screen = Arg(ScreenFlag);
            if (string.IsNullOrEmpty(screen))
            {
                return;
            }

            var carrier = new GameObject("UiCaptureAgent");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(screen.ToLowerInvariant(), Arg(OutFlag), Arg(SizeFlag), Arg(DelayFlag));
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private sealed class Driver : MonoBehaviour
        {
            private string _screen;
            private string _out;
            private string _size;
            private float _delay = 2f;

            internal void Configure(string screen, string output, string size, string delay)
            {
                _screen = screen;
                _out = string.IsNullOrEmpty(output) ? Path.Combine(Application.persistentDataPath, "ui-capture-" + screen + ".png") : output;
                _size = size;
                float d;
                if (!string.IsNullOrEmpty(delay) && float.TryParse(delay, out d))
                {
                    _delay = d;
                }
            }

            private IEnumerator Start()
            {
                // Size: pass -screen-fullscreen 1 -screen-width W -screen-height H on the
                // command line; the fullscreen back buffer is exactly WxH on the Retina
                // desktop, while windowed mode is clamped by PrototypeWindowFit (WP-025).
                // -ddSize is accepted and logged for parity with the WP-011 capture agent.
                yield return WaitScene(PrototypeScenes.MainMenu, 20f);
                yield return new WaitForSecondsRealtime(1f);

                switch (_screen)
                {
                    case "settings":
                        {
                            MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                            if (menu != null)
                            {
                                menu.ShowPane(UiContext.Settings);
                            }

                            break;
                        }

                    case "selection":
                        PrototypeUiFlow.GoToSelection();
                        yield return WaitScene(PrototypeScenes.Selection, 20f);
                        break;

                    case "pause":
                    case "results":
                        PrototypeUiFlow.GoToSelection();
                        yield return WaitScene(PrototypeScenes.Selection, 20f);
                        yield return new WaitForSecondsRealtime(0.5f);
                        PrototypeUiFlow.StartRun();
                        yield return WaitScene(PrototypeScenes.Run, 30f);
                        yield return WaitRunLive(20f);
                        yield return new WaitForSecondsRealtime(2f);
                        if (_screen == "pause")
                        {
                            PauseScreen pause = FindObjectOfType<PauseScreen>();
                            if (pause != null)
                            {
                                pause.SetPaused(true);
                            }
                        }
                        else
                        {
                            PlayerRig rig = PlayerRegistry.Primary;
                            if (rig != null && rig.Health != null)
                            {
                                rig.Health.DiagnosisInvulnerable = false;
                                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ui-capture");
                            }

                            yield return WaitScene(PrototypeScenes.Results, 30f);
                        }

                        break;
                }

                yield return new WaitForSecondsRealtime(_delay);
                yield return new WaitForEndOfFrame();

                string folder = Path.GetDirectoryName(_out);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(_out, frame.EncodeToPNG());
                Debug.Log(string.Format("[UiCapture] {0} to {1} · screen {2}x{3} · framebuffer {4}x{5} · scene {6} · requested {7}",
                    _screen, _out, Screen.width, Screen.height, frame.width, frame.height, SceneManager.GetActiveScene().name, _size ?? "window"));
                Destroy(frame);
                yield return null;
                Debug.Log("[UiCapture] done");
                Application.Quit();
            }

            private static IEnumerator WaitScene(string name, float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup - t0 < timeout)
                {
                    yield return null;
                }

                yield return null;
            }

            private static IEnumerator WaitRunLive(float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < timeout)
                {
                    RunDirector director = RunDirector.Instance;
                    if (director != null && director.IsRunLive && PlayerRegistry.Primary != null)
                    {
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
