// H12 (C-00 30.09): capture agents exist only in development builds and the Editor.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using DD.Prototype.Core.Boot;
using DD.Prototype.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU §8, the acceptance 30 s menu MP4) · <c>-ddUiCapture menuv5demo -ddUiCaptureOut &lt;dir&gt;</c>
    /// [<c>-ddUiCaptureFlip 0|1</c>] [<c>-ddUiReducedMotion 1</c> - §8's side-by-side RM take]: the §8 demo script driven through the same UI entry points a player
    /// uses - pointer enter / down / up / click and Submit through the EventSystem's handlers, focus travel as axis events,
    /// Cancel through UiInput's simulated press - while the frames are recorded in real time (the game is not slowed: the
    /// motion is the production motion at its production clock). The seam adds only what §8 says it may: a notification pass
    /// on ACHIEVEMENTS and a brush confirm window (S-04's kit window, its open / focus trap / close). Unlock and the background
    /// cut's timing are not driven (no unlock control exists yet; the 60 s loop runs on its own clock - the log marks a cut
    /// that lands in the window).
    ///
    /// From the first menu arrival of the session (the boot seal hands off; t = 0 at the curtain's reveal): 0–1.2 the arrival
    /// (M1–M5); 1.2–5 the look drifts right (M16, M17; M18 on PLAY at ≈ 3.6); 5–9 the pointer down the rail (M7); 9–12.5 keyboard
    /// travel at 10 steps / s and the ACHIEVEMENTS notification; 12.5–15 a PROFILE click (M9 → M10 → M11 + M13, the render-free
    /// freeze at +280 ms); 15–17.5 pad Submit on SETTINGS (M12), a slider step and a switch; 17.5–19.5 Cancel (M12 close);
    /// 19.5–22.5 the confirm window (§4); 22.5–26 the PLAY click (M10 vermilion → M14 brush cover); 26–30 Selection opens.
    ///
    /// OUT: &lt;dir&gt;/frames/f-NNNNNN.jpg (q 90, ≤ 1920 wide), frames.tsv ("idx\tt", t = real seconds from the recording's start,
    /// the frames2mp4 / tdmux input), script.tsv (each scripted step's t from the reveal), summary.json (revealAt: the reveal's t in
    /// frames.tsv). The input modules are off for the recording (the OS cursor
    /// cannot steer it); captures run inside the process-local UI data root (UiCaptureAgent) and quit at the end.
    /// </summary>
    public sealed class UiMenuDemoAgent : MonoBehaviour
    {
        public const string Mode = "menuv5demo";
        public const float Seconds = 30f;
        public const int Fps = 30;
        private const int MaxInFlight = 6;
        private const int JpegQuality = 90;
        private const int MaxWidth = 1920;

        private string _out;
        private string _framesDir;
        private int _flip = -1;
        private StreamWriter _tsv;
        private StreamWriter _script;
        private TdFrameSchedule _schedule;
        private RenderTexture _full;
        private RenderTexture _scaled;
        private int _outW, _outH;
        private bool _doFlip;
        private double _t0 = double.NaN;
        private double _recordFrom;
        private int _inFlight;
        private int _written;
        private int _listed;
        private int _failed;
        private bool _recording;
        private readonly Stack<byte[]> _pool = new Stack<byte[]>();
        private readonly object _poolLock = new object();

        public void Configure(string output, string flip)
        {
            _out = string.IsNullOrEmpty(output) ? Path.Combine(Application.persistentDataPath, "menuv5demo") : output;
            if (Path.HasExtension(_out))
            {
                _out = Path.GetDirectoryName(_out) ?? Application.persistentDataPath;
            }

            _flip = flip == "1" ? 1 : flip == "0" ? 0 : -1;
        }

        private IEnumerator Start()
        {
            _framesDir = Path.Combine(_out, "frames");
            Directory.CreateDirectory(_framesDir);
            _tsv = new StreamWriter(Path.Combine(_out, "frames.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            _script = new StreamWriter(Path.Combine(_out, "script.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            _script.WriteLine("# t_seconds\tstep");

            // The session's first menu: wait for the scene, start recording at once (the brush wipe is the arrival's first beat).
            float deadline = Time.realtimeSinceStartup + 60f;
            while (SceneManager.GetActiveScene().name != PrototypeScenes.MainMenu && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            MainMenuScreen menu = null;
            while (menu == null && Time.realtimeSinceStartup < deadline)
            {
                menu = FindObjectOfType<MainMenuScreen>();
                yield return null;
            }

            if (menu == null)
            {
                Finish("no main menu");
                yield break;
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Finish("AsyncGPUReadback is not supported on " + SystemInfo.graphicsDeviceType);
                yield break;
            }

            foreach (BaseInputModule module in FindObjectsOfType<BaseInputModule>())
            {
                module.enabled = false;   // the script steers, not the OS cursor
            }

            yield return new WaitForEndOfFrame();
            PrepareTargets();
            _schedule = new TdFrameSchedule(Fps);
            _recordFrom = Time.realtimeSinceStartupAsDouble;
            _recording = true;
            StartCoroutine(Record());

            while (UiCurtain.IsCovering && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _t0 = Time.realtimeSinceStartupAsDouble;
            Step("reveal (M1 brush open; M2-M5 arrival)");
            yield return Script(menu);

            while (T < Seconds)
            {
                yield return null;
            }

            _recording = false;
            float drain = Time.realtimeSinceStartup + 5f;
            while (Volatile.Read(ref _inFlight) > 0 && Time.realtimeSinceStartup < drain)
            {
                yield return null;
            }

            Finish(null);
        }

        private double T
        {
            get { return double.IsNaN(_t0) ? 0d : Time.realtimeSinceStartupAsDouble - _t0; }
        }

        private IEnumerator Until(double t)
        {
            while (T < t)
            {
                yield return null;
            }
        }

        private void Step(string what)
        {
            _script.WriteLine(T.ToString("F3", CultureInfo.InvariantCulture) + "\t" + what);
            Debug.Log("[MenuV5Demo] t=" + T.ToString("F2", CultureInfo.InvariantCulture) + " " + what);
        }

        // ================================================================== the §8 script

        private IEnumerator Script(MainMenuScreen menu)
        {
            yield return Until(1.2);
            Step("look drifts right (M16 parallax, M17 motes; M18 idle on PLAY ~3.6)");
            while (T < 5.0)
            {
                float u = Mathf.Clamp01((float)(T - 1.2) / 3.8f);
                UiBackdrop.LookOverride = new Vector2(Mathf.Lerp(0f, 0.7f, u * u * (3f - 2f * u)), 0.1f);
                yield return null;
            }

            string[] hover = { "Tab.Profile", "Tab.Statistics", "Tab.Achievements", "Tab.Settings" };
            GameObject previous = null;
            for (int i = 0; i < hover.Length && menu != null; i++)
            {
                yield return Until(5.0 + i);
                GameObject item = Item(menu, hover[i]);
                PointerExit(previous);
                PointerEnter(item);
                previous = item;
                Step("pointer over " + hover[i] + " (M7 lift, glow, ink wave, word)");
            }

            yield return Until(9.0);
            PointerExit(previous);
            string[] rail = { "Play", "Tab.Profile", "Tab.Statistics", "Tab.Achievements", "Tab.Settings", "Tab.Controls", "Tab.Accessibility", "Quit" };
            int at = 4, dir = 1;
            bool notified = false;
            Step("keyboard travel, 10 steps / s");
            while (T < 12.5 && menu != null)
            {
                at += dir;
                if (at <= 0 || at >= rail.Length - 1)
                {
                    dir = -dir;
                }

                FocusByAxis(Item(menu, rail[at]), dir > 0 ? MoveDirection.Down : MoveDirection.Up);
                if (!notified && T >= 10.0)
                {
                    notified = true;
                    UiIconButton achievements = Icon(menu, "Tab.Achievements");
                    if (achievements != null)
                    {
                        achievements.PlayNotify();
                        Step("seam: ACHIEVEMENTS notification pass (600 ms)");
                    }
                }

                double next = T + 0.1;
                while (T < next)
                {
                    yield return null;
                }
            }

            yield return Until(12.6);
            GameObject profile = Item(menu, "Tab.Profile");
            PointerEnter(profile);
            yield return Until(12.7);
            PointerDown(profile);
            Step("PROFILE pointer down (M9)");
            yield return Until(12.8);
            PointerUpClick(profile);
            Step("PROFILE click (M10 splash, M11 + M13; the world freezes at +280 ms)");

            yield return Until(15.2);
            FocusByAxis(Item(menu, "Tab.Settings"), MoveDirection.Down);
            yield return Until(15.5);
            Submit(Item(menu, "Tab.Settings"));
            Step("pad Submit SETTINGS (M9 pulse, M12 change)");
            yield return Until(16.3);
            Transform slider = FindDeep(menu.transform, "Setting.MASTER VOLUME");
            if (slider != null)
            {
                FocusByAxis(slider.gameObject, MoveDirection.Down);
                yield return null;
                ExecuteEvents.Execute(slider.gameObject, Axis(MoveDirection.Right), ExecuteEvents.moveHandler);
                Step("slider step (MASTER VOLUME)");
            }

            yield return Until(16.9);
            Transform toggle = FindDeep(menu.transform, "Hints");
            UiButton toggleButton = toggle != null ? toggle.GetComponentInChildren<UiButton>() : null;
            if (toggleButton != null)
            {
                FocusByAxis(toggleButton.gameObject, MoveDirection.Down);
                yield return null;
                Submit(toggleButton.gameObject);
                yield return Until(17.2);
                Submit(toggleButton.gameObject);   // and back: the demo leaves the setting as it found it
                Step("switch (HINTS) on and back");
            }

            yield return Until(17.6);
            UiInput.SimulateCancelNextFrame();
            Step("Cancel (M12 close; focus back on SETTINGS; the motes return)");

            yield return Until(19.6);
            RectTransform root = menu.GetComponentInChildren<Canvas>() != null ? (RectTransform)menu.GetComponentInChildren<Canvas>().transform : null;
            UiModal confirm = null;
            if (root != null)
            {
                confirm = UiBrushKit.Confirm("DemoConfirm", root, new UiBrushConfirm
                {
                    TitleKey = "menu.quit_confirm.title", TitleFallback = "LEAVE THE GAME?",
                    BodyKey = "menu.quit_confirm.body", BodyFallback = "Your profile is saved.",
                    ConfirmKey = "menu.quit", ConfirmFallback = "QUIT", CancelKey = "common.cancel", CancelFallback = "STAY"
                }, null, null);
                confirm.Open();
                Step("seam: brush confirm window opens (§4: scrim Fast, card, focus on the safe action)");
                yield return Until(20.6);
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                {
                    ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, Axis(MoveDirection.Right), ExecuteEvents.moveHandler);
                    yield return Until(21.0);
                    ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, Axis(MoveDirection.Left), ExecuteEvents.moveHandler);
                    Step("focus inside the window (the trap)");
                }

                yield return Until(21.8);
                confirm.HandleCancel();
                Step("window closes on Cancel (§4)");
            }

            yield return Until(22.8);
            GameObject play = Item(menu, "Play");
            PointerEnter(play);
            yield return Until(23.0);
            PointerDown(play);
            yield return Until(23.1);
            PointerUpClick(play);
            Step("PLAY click (M10 vermilion splash, M14 brush cover; ScreenExiting / CurtainClosed)");
            UiBackdrop.LookOverride = null;
            yield return Until(26.0);
            Step("Selection (brush wipe open, staggered entry)");
        }

        // ================================================================== input through the EventSystem's own handlers

        private static GameObject Item(MainMenuScreen menu, string name)
        {
            if (menu == null) return null;
            foreach (UiIconButton icon in menu.Rail)
            {
                if (icon != null && icon.gameObject.name == name) return icon.gameObject;
            }

            return null;
        }

        private static UiIconButton Icon(MainMenuScreen menu, string name)
        {
            GameObject go = Item(menu, name);
            return go != null ? go.GetComponent<UiIconButton>() : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(false))
            {
                if (t.name == name) return t;
            }

            return null;
        }

        private static PointerEventData Pointer(GameObject go)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            if (go != null)
            {
                var corners = new Vector3[4];
                ((RectTransform)go.transform).GetWorldCorners(corners);
                Canvas canvas = go.GetComponentInParent<Canvas>();
                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                data.position = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[2]) * 0.5f);
                data.pointerPress = go;
                data.pointerEnter = go;
            }

            return data;
        }

        private static AxisEventData Axis(MoveDirection direction)
        {
            return new AxisEventData(EventSystem.current) { moveDir = direction, moveVector = direction == MoveDirection.Down ? Vector2.down : direction == MoveDirection.Up ? Vector2.up : direction == MoveDirection.Left ? Vector2.left : Vector2.right };
        }

        private static void PointerEnter(GameObject go)
        {
            if (go != null) ExecuteEvents.Execute(go, Pointer(go), ExecuteEvents.pointerEnterHandler);
        }

        private static void PointerExit(GameObject go)
        {
            if (go != null) ExecuteEvents.Execute(go, Pointer(go), ExecuteEvents.pointerExitHandler);
        }

        private static void PointerDown(GameObject go)
        {
            if (go != null) ExecuteEvents.Execute(go, Pointer(go), ExecuteEvents.pointerDownHandler);
        }

        private static void PointerUpClick(GameObject go)
        {
            if (go == null) return;
            PointerEventData data = Pointer(go);
            ExecuteEvents.Execute(go, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, data, ExecuteEvents.pointerClickHandler);
        }

        private static void Submit(GameObject go)
        {
            if (go != null) ExecuteEvents.Execute(go, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        /// <summary>Keyboard / pad focus travel: the selection moves with an axis event (the Move cue, the icon's lift).</summary>
        private static void FocusByAxis(GameObject go, MoveDirection direction)
        {
            if (go != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(go, Axis(direction));
            }
        }

        // ================================================================== real-time recording (the TD agent's pipeline, compact)

        private void PrepareTargets()
        {
            Texture2D reference = ScreenCapture.CaptureScreenshotAsTexture();
            int backW = reference.width, backH = reference.height;
            Destroy(reference);
            _outW = Mathf.Min(backW, MaxWidth) & ~1;
            _outH = Mathf.Max(2, Mathf.RoundToInt(backH * (_outW / (float)backW))) & ~1;
            _full = new RenderTexture(backW, backH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "MenuDemo.Full" };
            _full.Create();
            if (_outW != backW || _outH != backH)
            {
                _scaled = new RenderTexture(_outW, _outH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "MenuDemo.Out", filterMode = FilterMode.Bilinear };
                _scaled.Create();
            }

            _doFlip = _flip >= 0 ? _flip == 1 : SystemInfo.graphicsUVStartsAtTop;
        }

        private IEnumerator Record()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (_recording)
            {
                yield return endOfFrame;
                double t = Time.realtimeSinceStartupAsDouble - _recordFrom;
                if (_schedule.TryTake(t, Volatile.Read(ref _inFlight) >= MaxInFlight, out long _))
                {
                    Grab(Time.realtimeSinceStartupAsDouble);
                }
            }
        }

        private void Grab(double at)
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            RenderTexture src = _full;
            if (_scaled != null)
            {
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(_full, _scaled);
                RenderTexture.active = previous;
                src = _scaled;
            }

            Interlocked.Increment(ref _inFlight);
            AsyncGPUReadback.Request(src, 0, TextureFormat.RGBA32, req => OnReadback(req, at));
        }

        private void OnReadback(AsyncGPUReadbackRequest req, double at)
        {
            if (req.hasError || _tsv == null)
            {
                Interlocked.Decrement(ref _inFlight);
                return;
            }

            var data = req.GetData<byte>();
            byte[] buffer = Rent(data.Length);
            data.CopyTo(buffer);
            int idx = _listed++;
            double t = at - _recordFrom;
            _tsv.WriteLine(idx.ToString(CultureInfo.InvariantCulture) + "\t" + t.ToString("F6", CultureInfo.InvariantCulture));
            string path = Path.Combine(_framesDir, "f-" + idx.ToString("000000", CultureInfo.InvariantCulture) + ".jpg");
            int w = req.width, h = req.height;
            bool flip = _doFlip;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (flip) TdOrientation.FlipRows(buffer, w, h, new byte[w * 4]);
                    File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(buffer, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, JpegQuality));
                    Interlocked.Increment(ref _written);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _failed);
                }
                finally
                {
                    Return(buffer);
                    Interlocked.Decrement(ref _inFlight);
                }
            });
        }

        private byte[] Rent(int length)
        {
            lock (_poolLock)
            {
                while (_pool.Count > 0)
                {
                    byte[] b = _pool.Pop();
                    if (b.Length == length) return b;
                }
            }

            return new byte[length];
        }

        private void Return(byte[] buffer)
        {
            lock (_poolLock)
            {
                if (_pool.Count < MaxInFlight) _pool.Push(buffer);
            }
        }

        private void Finish(string error)
        {
            _recording = false;
            UiBackdrop.LookOverride = null;
            string summary = "{\n  \"wp\": \"WP-098A\",\n  \"mode\": \"" + Mode + "\",\n  \"seconds\": " + Seconds.ToString(CultureInfo.InvariantCulture)
                             + ",\n  \"fps\": " + Fps + ",\n  \"out\": \"" + _outW + "x" + _outH + "\",\n  \"flip\": " + (_doFlip ? "true" : "false")
                             + ",\n  \"revealAt\": " + (double.IsNaN(_t0) ? "null" : (_t0 - _recordFrom).ToString("F3", CultureInfo.InvariantCulture))
                             + ",\n  \"framesListed\": " + _listed + ",\n  \"framesWritten\": " + Volatile.Read(ref _written)
                             + ",\n  \"encodeFailed\": " + Volatile.Read(ref _failed) + ",\n  \"skippedSlots\": " + (_schedule != null ? _schedule.Skipped : 0)
                             + ",\n  \"reducedMotion\": " + (UiMotion.ReducedMotion ? "true" : "false")
                             + ",\n  \"error\": " + (error == null ? "null" : "\"" + error.Replace("\"", "'") + "\"") + "\n}\n";
            try
            {
                _tsv?.Flush();
                _tsv?.Dispose();
                _script?.Flush();
                _script?.Dispose();
                _tsv = null;
                _script = null;
                File.WriteAllText(Path.Combine(_out, "summary.json"), summary);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MenuV5Demo] summary not written: " + e.Message);
            }

            if (_full != null) { _full.Release(); Destroy(_full); }
            if (_scaled != null) { _scaled.Release(); Destroy(_scaled); }
            Debug.Log("[MenuV5Demo] done " + (error ?? "ok") + " · " + summary.Replace("\n", " "));
            Application.Quit(error == null ? 0 : 3);
        }
    }
}
#endif
