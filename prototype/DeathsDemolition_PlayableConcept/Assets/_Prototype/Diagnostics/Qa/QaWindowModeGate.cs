#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Qa
{
    /// <summary>WP-061 opt-in C2-C4 evidence driver; inactive unless -ddQaWindowModes is present.</summary>
    public static class QaWindowModeGate
    {
        private const string Flag = "-ddQaWindowModes";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string output = QaArgs.Arg(Flag);
            if (string.IsNullOrEmpty(output)) return;
            var carrier = new GameObject("QaWindowModeGate");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(output);
        }

        [Serializable]
        private sealed class Frame
        {
            public string id;
            public string scene;
            public string mode;
            public int screenWidth;
            public int screenHeight;
            public int frameWidth;
            public int frameHeight;
            public int shaderMagentaPixels;
            public string image;
            public bool pass;
        }

        [Serializable]
        private sealed class Report
        {
            public string schema = "dd-window-modes-v1";
            public bool pass;
            public string note;
            public List<Frame> frames = new List<Frame>();
        }

        private sealed class Driver : MonoBehaviour
        {
            private string _output;
            private readonly Report _report = new Report();
            internal void Configure(string output) { _output = output; }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_output);
                yield return WaitScene(PrototypeScenes.MainMenu, 30f);
                yield return SetMode(1280, 720, FullScreenMode.Windowed);
                yield return Capture("c2-mainmenu", PrototypeScenes.MainMenu, FullScreenMode.Windowed, 1280, 720);

                PrototypeUiFlow.GoToSelection();
                yield return WaitScene(PrototypeScenes.Selection, 30f);
                PrototypeUiFlow.StartRun();
                yield return WaitScene(PrototypeScenes.Run, 60f);
                yield return WaitRunLive(30f);
                yield return Capture("c2-run", PrototypeScenes.Run, FullScreenMode.Windowed, 1280, 720);

                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "qa-window-modes");
                }
                yield return WaitScene(PrototypeScenes.Results, 45f);
                yield return Capture("c2-results", PrototypeScenes.Results, FullScreenMode.Windowed, 1280, 720);

                yield return SetMode(1024, 640, FullScreenMode.Windowed);
                yield return Capture("c3-results-small", PrototypeScenes.Results, FullScreenMode.Windowed, 1024, 640);
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitScene(PrototypeScenes.MainMenu, 30f);
                yield return Capture("c3-mainmenu-small", PrototypeScenes.MainMenu, FullScreenMode.Windowed, 1024, 640);
                PrototypeUiFlow.GoToSelection();
                yield return WaitScene(PrototypeScenes.Selection, 30f);
                PrototypeUiFlow.StartRun();
                yield return WaitScene(PrototypeScenes.Run, 60f);
                yield return WaitRunLive(30f);
                yield return Capture("c3-run-small", PrototypeScenes.Run, FullScreenMode.Windowed, 1024, 640);

                Resolution native = Screen.currentResolution;
                yield return SetMode(native.width, native.height, FullScreenMode.FullScreenWindow);
                yield return Capture("c4-fullscreen", PrototypeScenes.Run, FullScreenMode.FullScreenWindow, 0, 0);
                yield return SetMode(1280, 720, FullScreenMode.Windowed);
                yield return Capture("c4-windowed-after-fullscreen", PrototypeScenes.Run, FullScreenMode.Windowed, 1280, 720);

                _report.pass = _report.frames.Count == 8;
                for (int i = 0; i < _report.frames.Count; i++) _report.pass &= _report.frames[i].pass;
                _report.note = _report.pass ? "C2-C4 captures and in-process fullscreen round-trip passed" : "one or more C2-C4 frames failed";
                File.WriteAllText(Path.Combine(_output, "qa-report.json"), JsonUtility.ToJson(_report, true));
                Debug.Log("[QaWindowModeGate] " + (_report.pass ? "PASS " : "FAIL ") + _report.note);
                yield return null;
                Application.Quit(_report.pass ? 0 : 3);
            }

            private static IEnumerator SetMode(int width, int height, FullScreenMode mode)
            {
                // macOS can ignore a window size request made while the native window is still in
                // its fullscreen space. Leave that space first, then resize the restored window.
                if (mode == FullScreenMode.Windowed && Screen.fullScreenMode != FullScreenMode.Windowed)
                {
                    Screen.fullScreen = false;
                    Screen.fullScreenMode = FullScreenMode.Windowed;
                    float leaveDeadline = Time.realtimeSinceStartup + 12f;
                    while (Screen.fullScreenMode != FullScreenMode.Windowed && Time.realtimeSinceStartup < leaveDeadline)
                    {
                        yield return null;
                    }
                }

                Screen.SetResolution(width, height, mode);
                float deadline = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    bool dimensionsMatch = mode != FullScreenMode.Windowed || (Screen.width == width && Screen.height == height);
                    if (Screen.fullScreenMode == mode && dimensionsMatch) break;
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1f);
            }

            private IEnumerator Capture(string id, string expectedScene, FullScreenMode expectedMode, int expectedWidth, int expectedHeight)
            {
                yield return new WaitForEndOfFrame();
                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
                string path = Path.Combine(_output, id + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Color32[] pixels = texture.GetPixels32();
                int magenta = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    if (p.r >= 250 && p.g <= 5 && p.b >= 250) magenta++;
                }
                var frame = new Frame
                {
                    id = id,
                    scene = SceneManager.GetActiveScene().name,
                    mode = Screen.fullScreenMode.ToString(),
                    screenWidth = Screen.width,
                    screenHeight = Screen.height,
                    frameWidth = texture.width,
                    frameHeight = texture.height,
                    shaderMagentaPixels = magenta,
                    image = path,
                    pass = texture.width > 0 && texture.height > 0 && magenta == 0
                        && SceneManager.GetActiveScene().name == expectedScene
                        && Screen.fullScreenMode == expectedMode
                        && (expectedWidth <= 0 || Screen.width == expectedWidth)
                        && (expectedHeight <= 0 || Screen.height == expectedHeight)
                };
                _report.frames.Add(frame);
                Debug.Log("[QaWindowModeGate] " + id + " " + frame.mode + " screen=" + frame.screenWidth + "x" + frame.screenHeight + " frame=" + frame.frameWidth + "x" + frame.frameHeight + " magenta=" + magenta);
                UnityEngine.Object.Destroy(texture);
            }

            private static IEnumerator WaitScene(string scene, float timeout)
            {
                float start = Time.realtimeSinceStartup;
                while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup - start < timeout) yield return null;
                yield return null;
            }

            private static IEnumerator WaitRunLive(float timeout)
            {
                float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < timeout)
                {
                    if (RunDirector.Instance != null && RunDirector.Instance.IsRunLive && PlayerRegistry.Primary != null) yield break;
                    yield return null;
                }
            }
        }
    }
}
#endif
